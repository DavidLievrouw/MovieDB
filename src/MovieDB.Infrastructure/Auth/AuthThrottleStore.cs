using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MovieDB.Common;
using MySqlConnector;

namespace MovieDB.Infrastructure.Auth;

public sealed record AuthAdmission(int RetryAfterSeconds, AuthReservation? Reservation = null);

public sealed record AuthReservation(string[] Keys, DateTime AccountWindow);

public enum AuthAttemptOutcome
{
    Failed = 0,
    Succeeded = 1,
    InvalidInput = 2,
}

/// <summary>
/// Database-backed login throttling per account, IP address, device and globally. Every decision is made under a
/// row lock on the singleton <c>global</c> row, so it holds across instances. It fails closed: when the database
/// operation fails, the attempt is denied.
/// </summary>
public sealed class AuthThrottleStore
{
    private const string GlobalKey = "global";

    private readonly IDbContextFactory<MovieDbContext> _factory;
    private readonly TimeProvider _clock;
    private readonly ILogger<AuthThrottleStore> _logger;
    private readonly AuthProtectionOptions _limits;

    public AuthThrottleStore(
        IDbContextFactory<MovieDbContext> factory,
        IOptions<AuthProtectionOptions> options,
        TimeProvider clock,
        ILogger<AuthThrottleStore> logger
    )
    {
        _factory = factory;
        _clock = clock;
        _logger = logger;
        _limits = options.Value;
    }

    public static string Key(string kind, string value) =>
        $"{kind}:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))}";

    public async Task<Result<AuthAdmission>> ReserveAsync(
        string normalizedUsername,
        string address,
        string device,
        CancellationToken cancellationToken
    ) =>
        await RunAsync<AuthAdmission>(
            (db, ct) => ReserveInTransactionAsync(db, normalizedUsername, address, device, ct),
            cancellationToken
        );

    private async Task<Result<AuthAdmission>> ReserveInTransactionAsync(
        MovieDbContext db,
        string normalizedUsername,
        string address,
        string device,
        CancellationToken ct
    )
    {
        var global = await LockGlobalAsync(db, ct);
        var now = UtcNow();
        ResetWindow(global, now, _limits.GlobalWindowSeconds);
        if (global.Attempts >= _limits.GlobalAttempts)
        {
            return Result<AuthAdmission>.Ok(
                new AuthAdmission(SecondsUntil(global.WindowStart.AddSeconds(_limits.GlobalWindowSeconds), now))
            );
        }

        // Charge the global budget even when an account/source is already blocked. This bounds DB work
        // and key creation during floods; only the singleton row is read once that budget is exhausted.
        global.Attempts++;
        await CleanupExpiredAsync(db, global, now, ct);
        var accountKey = await GetAccountKeyAsync(db, normalizedUsername, ct);
        string[] keys = [accountKey, Key("ip", address), Key("device", device)];
        var stored = await db.AuthThrottleStates.Where(row => Enumerable.Contains(keys, row.Key)).ToArrayAsync(ct);
        var states = CreateStates(keys, stored, now);
        var retry = GetRetryAfter(states, now);
        if (retry == 0)
        {
            RecordReservation(db, states, accountKey, now);
        }

        await db.SaveChangesAsync(ct);
        return Result<AuthAdmission>.Ok(
            new AuthAdmission(retry, retry == 0 ? new AuthReservation(keys, states[0].WindowStart) : null)
        );
    }

    private static async Task<string> GetAccountKeyAsync(
        MovieDbContext db,
        string normalizedUsername,
        CancellationToken ct
    )
    {
        // The collation also equates accents and trailing spaces. Key existing accounts by their immutable ID,
        // not merely by an uppercased spelling of the submitted user name.
        var userIds = await db
            .Users.AsNoTracking()
            .Where(user => user.NormalizedUserName == normalizedUsername)
            .Select(user => user.Id)
            .Take(1)
            .ToArrayAsync(ct);
        return userIds.Length == 0
            ? Key("account", $"name:{normalizedUsername}")
            : Key("account", $"id:{userIds[0].Value}");
    }

    private AuthThrottleState[] CreateStates(string[] keys, AuthThrottleState[] stored, DateTime now)
    {
        var states = new AuthThrottleState[keys.Length];
        for (var index = 0; index < keys.Length; index++)
        {
            var state = Array.Find(stored, row => row.Key == keys[index]);
            states[index] = PrepareState(state, keys[index], index, now);
        }

        return states;
    }

    private AuthThrottleState PrepareState(AuthThrottleState? state, string key, int index, DateTime now)
    {
        if (state is null)
        {
            state = new AuthThrottleState { Key = key, WindowStart = now };
        }
        else if (state.ExpiresAt <= now)
        {
            ResetExpiredState(state, now);
        }

        if (state.LastFailure.AddSeconds(_limits.AccountWindowSeconds) <= now)
        {
            state.Failures = 0;
        }

        ResetWindow(state, now, index == 0 ? _limits.AccountWindowSeconds : _limits.SourceWindowSeconds);
        return state;
    }

    private static void ResetExpiredState(AuthThrottleState state, DateTime now)
    {
        state.Attempts = 0;
        state.Failures = 0;
        state.AccountChanges = 0;
        state.LastAccount = null;
        state.LastSuccess = null;
        state.BlockedUntil = DateTime.UnixEpoch;
        state.WindowStart = now;
    }

    private int GetRetryAfter(AuthThrottleState[] states, DateTime now)
    {
        var retry = 0;
        for (var index = 0; index < states.Length; index++)
        {
            var state = states[index];
            var window = index == 0 ? _limits.AccountWindowSeconds : _limits.SourceWindowSeconds;
            if (state.Attempts >= GetPermits(index))
            {
                retry = Math.Max(retry, SecondsUntil(state.WindowStart.AddSeconds(window), now));
            }

            retry = Math.Max(retry, SecondsUntil(state.BlockedUntil, now));
        }

        return retry;
    }

    private int GetPermits(int index) =>
        index switch
        {
            0 => _limits.AccountFailures,
            1 => _limits.IpAttempts,
            _ => _limits.DeviceAttempts,
        };

    private void RecordReservation(MovieDbContext db, AuthThrottleState[] states, string accountKey, DateTime now)
    {
        foreach (var state in states)
        {
            if (db.Entry(state).State == EntityState.Detached)
            {
                db.AuthThrottleStates.Add(state);
            }

            state.Attempts++;
            state.ExpiresAt = now.AddHours(_limits.ReputationHours);
        }

        for (var index = 1; index < states.Length; index++)
        {
            RecordSourceAccount(states[index], accountKey);
        }
    }

    private static void RecordSourceAccount(AuthThrottleState state, string accountKey)
    {
        if (state.LastAccount is not null && state.LastAccount != accountKey)
        {
            state.AccountChanges = Math.Min(100, state.AccountChanges + 1);
        }

        state.LastAccount = accountKey;
    }

    public async Task<Result<Unit>> CompleteAsync(
        AuthReservation reservation,
        AuthAttemptOutcome outcome,
        CancellationToken cancellationToken
    ) => await RunAsync((db, ct) => CompleteInTransactionAsync(db, reservation, outcome, ct), cancellationToken);

    private async Task<Result<Unit>> CompleteInTransactionAsync(
        MovieDbContext db,
        AuthReservation reservation,
        AuthAttemptOutcome outcome,
        CancellationToken ct
    )
    {
        await LockGlobalAsync(db, ct);
        var now = UtcNow();
        var states = await db
            .AuthThrottleStates.Where(row => Enumerable.Contains(reservation.Keys, row.Key))
            .ToArrayAsync(ct);
        foreach (var state in states)
        {
            CompleteState(state, state.Key == reservation.Keys[0], reservation, outcome, now);
        }

        if (states.Length != reservation.Keys.Length)
        {
            _logger.LogWarning("Authentication protection reservation was removed before completion.");
            return Error.Conflict("Authentication protection state changed. Retry later.");
        }

        await db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    private void CompleteState(
        AuthThrottleState state,
        bool account,
        AuthReservation reservation,
        AuthAttemptOutcome outcome,
        DateTime now
    )
    {
        switch (outcome)
        {
            case AuthAttemptOutcome.Succeeded:
                RecordSuccess(state, account, reservation, now);
                break;
            case AuthAttemptOutcome.Failed:
                RecordFailure(state, account, now);
                break;
            case AuthAttemptOutcome.InvalidInput:
                break;
        }

        state.ExpiresAt = now.AddHours(_limits.ReputationHours);
    }

    private static void RecordSuccess(AuthThrottleState state, bool account, AuthReservation reservation, DateTime now)
    {
        state.LastSuccess = now;
        // Successful logins consume source/global permits, not the failed-account budget.
        // Do not wipe other concurrent failures or cooldowns on a successful request.
        if (account && state.WindowStart == reservation.AccountWindow)
        {
            state.Attempts = Math.Max(0, state.Attempts - 1);
        }
    }

    private void RecordFailure(AuthThrottleState state, bool account, DateTime now)
    {
        if (state.LastFailure.AddSeconds(_limits.AccountWindowSeconds) <= now)
        {
            state.Failures = 0;
        }

        state.LastFailure = now;
        state.Failures = Math.Min(100, state.Failures + 1);
        var exponent = GetCooldownExponent(state, account, now);
        if (exponent >= 0)
        {
            var seconds = Math.Min(_limits.MaximumCooldownSeconds, 1 << Math.Min(16, exponent));
            state.BlockedUntil = now.AddSeconds(seconds);
        }
    }

    private int GetCooldownExponent(AuthThrottleState state, bool account, DateTime now)
    {
        var suspiciousSource = !account && state.AccountChanges >= 4 && state.Failures >= 3;
        var unprovenSource =
            !account && (state.LastSuccess is null || state.LastSuccess.Value.AddHours(_limits.ReputationHours) <= now);
        return state.Failures - (account || unprovenSource ? 3 : 4) + (suspiciousSource ? 2 : 0);
    }

    public Task<Result<Unit>> CleanupAsync(CancellationToken cancellationToken) =>
        RunAsync(
            async (db, ct) =>
            {
                var global = await LockGlobalAsync(db, ct);
                await CleanupExpiredAsync(db, global, UtcNow(), ct);
                await db.SaveChangesAsync(ct);
                return Result.Ok();
            },
            cancellationToken
        );

    private static async Task CleanupExpiredAsync(
        MovieDbContext db,
        AuthThrottleState global,
        DateTime now,
        CancellationToken ct
    )
    {
        if (global.ExpiresAt > now)
        {
            return;
        }

        var expiredKeys = await db
            .AuthThrottleStates.Where(row => row.Key != GlobalKey && row.ExpiresAt <= now)
            .OrderBy(row => row.ExpiresAt)
            .Select(row => row.Key)
            .Take(500)
            .ToArrayAsync(ct);
        if (expiredKeys.Length != 0)
        {
            await db.AuthThrottleStates.Where(row => Enumerable.Contains(expiredKeys, row.Key)).ExecuteDeleteAsync(ct);
        }

        global.ExpiresAt = now.AddMinutes(1);
    }

    private async Task<Result<T>> RunAsync<T>(
        Func<MovieDbContext, CancellationToken, Task<Result<T>>> work,
        CancellationToken ct
    )
    {
        try
        {
            return await _factory.InTransactionAsync(work, cancellationToken: ct);
        }
        catch (Exception exception) when (exception is MySqlException or DbUpdateException)
        {
            _logger.LogError(exception, "Authentication protection database operation failed; authentication denied.");
            return Error.Conflict("Authentication protection is unavailable.");
        }
    }

    private static async Task<AuthThrottleState> LockGlobalAsync(MovieDbContext db, CancellationToken ct)
    {
        var rows = await db
            .AuthThrottleStates.FromSql($"SELECT * FROM `auth_throttle_states` WHERE `key` = 'global' FOR UPDATE")
            .ToListAsync(ct);
        return rows.Single();
    }

    private static void ResetWindow(AuthThrottleState state, DateTime now, int seconds)
    {
        if (state.WindowStart.AddSeconds(seconds) <= now)
        {
            state.WindowStart = now;
            state.Attempts = 0;
        }
    }

    private static int SecondsUntil(DateTime until, DateTime now) =>
        until <= now ? 0 : Math.Max(1, (int)Math.Ceiling((until - now).TotalSeconds));

    private DateTime UtcNow()
    {
        var ticks = _clock.GetUtcNow().UtcTicks;
        // MariaDB datetime(6) persists microseconds, not .NET's 100 ns ticks.
        return new DateTime(ticks - (ticks % 10), DateTimeKind.Utc);
    }
}
