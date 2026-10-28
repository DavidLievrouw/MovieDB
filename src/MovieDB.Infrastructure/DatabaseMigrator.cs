using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MovieDB.Infrastructure;

/// <summary>
/// Applies pending EF migrations. Serialised across instances with a MariaDB named lock on a dedicated connection;
/// every migration is itself self-guarding, so it is safe to run repeatedly. <see cref="MigrateAsync" /> is also used
/// by the Access import tool; the host runs it before it starts listening.
/// </summary>
public sealed class DatabaseMigrator(IDbContextFactory<MovieDbContext> factory, ILogger<DatabaseMigrator> logger)
    : IHostedLifecycleService
{
    public const string LockName = "moviedb-migrate";
    private const int LockTimeoutSeconds = 120;

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
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
                    logger.LogInformation("Database is up to date.");
                    return;
                }

                logger.LogInformation("Applying {Count} migration(s): {Migrations}", pending.Count, pending);
                await context.Database.MigrateAsync(cancellationToken);
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
    public Task StartingAsync(CancellationToken cancellationToken) => MigrateAsync(cancellationToken);

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
