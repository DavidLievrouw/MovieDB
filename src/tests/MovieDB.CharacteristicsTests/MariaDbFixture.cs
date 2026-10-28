using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MovieDB.Infrastructure;
using MySqlConnector;
using Testcontainers.MariaDb;

namespace MovieDB.CharacteristicsTests;

/// <summary>One MariaDB container per test run; every test gets its own empty database on it.</summary>
public sealed class MariaDbFixture : IAsyncLifetime
{
    /// <summary>Password of the default user in tests; the hosts read it from the environment, like in production.</summary>
    public const string DefaultPassword = "demodemo";

    public const string DefaultPasswordVariable = "Auth__DefaultPassword";

    private readonly MariaDbContainer _container = new MariaDbBuilder("mariadb:12.3.3").Build();

    public async ValueTask InitializeAsync()
    {
        Environment.SetEnvironmentVariable(DefaultPasswordVariable, DefaultPassword);
        await _container.StartAsync();

        // The default test user is limited to one database; tests create their own databases.
        var grant = await _container.ExecAsync([
            "mariadb",
            "--user=root",
            "--password=mariadb",
            "--execute=GRANT ALL PRIVILEGES ON *.* TO 'mariadb'@'%'; FLUSH PRIVILEGES;",
        ]);
        if (grant.ExitCode != 0)
        {
            throw new InvalidOperationException("Could not grant privileges to the test user: " + grant.Stderr);
        }
    }

    public async ValueTask DisposeAsync()
    {
        Environment.SetEnvironmentVariable(DefaultPasswordVariable, null);
        await _container.DisposeAsync();
    }

    /// <summary>Creates an empty database and returns a utf8mb4 connection string for it.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = "moviedb_" + Guid.NewGuid().ToString("N");
        await using (var admin = new MySqlConnection(_container.GetConnectionString() + ";CharSet=utf8mb4"))
        {
            await admin.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = admin.CreateCommand();
            command.CommandText = $"CREATE DATABASE `{name}`";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        return new MySqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = name,
            CharacterSet = "utf8mb4",
        }.ConnectionString;
    }

    /// <summary>Creates an empty database and applies all migrations to it.</summary>
    public async Task<string> CreateMigratedDatabaseAsync()
    {
        var connectionString = await CreateDatabaseAsync();
        await CreateMigrator(connectionString).MigrateAsync(TestContext.Current.CancellationToken);
        return connectionString;
    }

    public static DatabaseMigrator CreateMigrator(string connectionString) =>
        new(CreateFactory(connectionString), NullLogger<DatabaseMigrator>.Instance);

    public static IDbContextFactory<MovieDbContext> CreateFactory(string connectionString) =>
        new ServiceCollection()
            .AddMovieDbDatabase(connectionString)
            .BuildServiceProvider()
            .GetRequiredService<IDbContextFactory<MovieDbContext>>();

    public static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
        return (T)(
            Convert.ChangeType(result, typeof(T), System.Globalization.CultureInfo.InvariantCulture)
            ?? throw new InvalidOperationException("The query returned no value.")
        );
    }

    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}

[CollectionDefinition(Name)]
public sealed class MariaDbCollection : ICollectionFixture<MariaDbFixture>
{
    public const string Name = "MariaDB";
}
