using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MovieDB.Infrastructure.Auth;

namespace MovieDB.Infrastructure;

/// <summary>
/// Applies pending EF migrations. Serialised across instances with a MariaDB named lock on a dedicated connection;
/// every migration is itself self-guarding, so it is safe to run repeatedly. <see cref="MigrateAsync" /> only migrates
/// (used by the Access import tool); <see cref="MigrateAndSeedAsync" /> is the startup path that also seeds the default
/// user under the same lock. The host runs it before it starts listening.
/// </summary>
public sealed class DatabaseMigrator : IHostedLifecycleService
{
    public const string LockName = "moviedb-migrate";
    private const int LockTimeoutSeconds = 120;

    private readonly IDbContextFactory<MovieDbContext> _factory;
    private readonly ILogger<DatabaseMigrator> _logger;
    private readonly DefaultUserSeeder? _seeder;

    public DatabaseMigrator(
        IDbContextFactory<MovieDbContext> factory,
        ILogger<DatabaseMigrator> logger,
        DefaultUserSeeder? seeder = null
    )
    {
        _factory = factory;
        _logger = logger;
        _seeder = seeder;
    }

    public Task MigrateAsync(CancellationToken cancellationToken) => RunAsync(seed: false, cancellationToken);

    public Task MigrateAndSeedAsync(CancellationToken cancellationToken) => RunAsync(seed: true, cancellationToken);

    private async Task RunAsync(bool seed, CancellationToken cancellationToken)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        try
        {
            if (!await TryLockAsync(connection, cancellationToken))
            {
                throw new InvalidOperationException("Could not acquire the migration lock in time.");
            }

            try
            {
                var pending = (await context.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
                if (pending.Count == 0)
                {
                    _logger.LogInformation("Database is up to date.");
                }
                else
                {
                    _logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Count, pending);
                    await context.Database.MigrateAsync(cancellationToken);
                }

                if (seed && _seeder is not null)
                {
                    await _seeder.SeedAsync(cancellationToken);
                }
            }
            finally
            {
                await ReleaseLockAsync(connection);
            }
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static async Task<bool> TryLockAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT GET_LOCK(@lockName, @timeoutSeconds)";
        var lockName = command.CreateParameter();
        lockName.ParameterName = "@lockName";
        lockName.Value = LockName;
        command.Parameters.Add(lockName);

        var timeoutSeconds = command.CreateParameter();
        timeoutSeconds.ParameterName = "@timeoutSeconds";
        timeoutSeconds.Value = LockTimeoutSeconds;
        command.Parameters.Add(timeoutSeconds);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is not null && Convert.ToInt32(result, CultureInfo.InvariantCulture) == 1;
    }

    private static async Task ReleaseLockAsync(DbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT RELEASE_LOCK('{LockName}')";
        await command.ExecuteScalarAsync(CancellationToken.None);
    }

    /// <summary>Runs before the host starts listening, so no request ever sees an unmigrated schema.</summary>
    /// <param name="cancellationToken">Cancels the migration.</param>
    public Task StartingAsync(CancellationToken cancellationToken) => MigrateAndSeedAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
