using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MovieDB.Common;
using MovieDB.Infrastructure;
using MovieDB.Movies;
using MySqlConnector;
using Testcontainers.MariaDb;

namespace MovieDB.Benchmarks;

/// <summary>
/// One throwaway MariaDB container for the whole benchmark run, with one migrated and seeded database per movie count.
/// The databases are created lazily and shared by all benchmarks that ask for the same size.
/// </summary>
internal static class BenchmarkEnvironment
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Dictionary<int, SeededDatabase> Databases = [];
    private static MariaDbContainer? _container;

    /// <summary>Starts the container; must be called once before <see cref="GetDatabase"/>.</summary>
    /// <exception cref="InvalidOperationException">The benchmark user could not be given database privileges.</exception>
    public static async Task StartAsync()
    {
        _container = new MariaDbBuilder("mariadb:12.3.3").WithCommand("--max-connections=1000").Build();
        await _container.StartAsync();

        // The default test user is limited to one database; the benchmarks create their own.
        var grant = await _container.ExecAsync([
            "mariadb",
            "--user=root",
            "--password=mariadb",
            "--execute=GRANT ALL PRIVILEGES ON *.* TO 'mariadb'@'%'; FLUSH PRIVILEGES;",
        ]);
        if (grant.ExitCode != 0)
        {
            throw new InvalidOperationException("Could not grant privileges to the benchmark user: " + grant.Stderr);
        }
    }

    /// <summary>Drops all databases and stops the container.</summary>
    public static async Task StopAsync()
    {
        foreach (var database in Databases.Values)
        {
            await database.DisposeAsync();
        }

        Databases.Clear();
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Returns the seeded database for a movie count, creating it on first use.</summary>
    /// <param name="movieCount">The number of movies to seed.</param>
    /// <exception cref="InvalidOperationException">The container has not been started.</exception>
    public static async Task<SeededDatabase> GetDatabase(int movieCount)
    {
        var container = _container ?? throw new InvalidOperationException("The container has not been started.");
        await Gate.WaitAsync();
        try
        {
            if (!Databases.TryGetValue(movieCount, out var database))
            {
                database = await CreateAsync(container, movieCount);
                Databases[movieCount] = database;
            }

            return database;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<SeededDatabase> CreateAsync(MariaDbContainer container, int movieCount)
    {
        var name = "moviedb_bench_" + movieCount;
        var adminConnectionString = container.GetConnectionString() + ";CharSet=utf8mb4";
        await ExecuteAsync(adminConnectionString, $"CREATE DATABASE `{name}`");

        var connectionString = new MySqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = name,
            CharacterSet = "utf8mb4",
        }.ConnectionString;

        var counter = new QueryCounter();
        var provider = new ServiceCollection()
            .AddPooledDbContextFactory<MovieDbContext>(options =>
            {
                DatabaseConfiguration.Configure(options, connectionString);
                options.AddInterceptors(counter);
            })
            .BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<MovieDbContext>>();

        await new DatabaseMigrator(factory, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(default);
        var sample = await SyntheticData.SeedAsync(factory, movieCount);
        await ExecuteAsync(
            connectionString,
            "ANALYZE TABLE movies, movie_genres, movie_directors, directors, genres, media"
        );

        var timeProvider = TimeProvider.System;
        var clock = new UserClock(timeProvider, UserClock.ResolveZone("Europe/Brussels"));
        return new SeededDatabase(
            adminConnectionString,
            connectionString,
            name,
            counter,
            new MovieQueries(factory),
            new MovieCommands(factory, new MovieInputValidator(clock), timeProvider),
            clock,
            sample,
            provider
        );
    }

    /// <summary>Runs a statement without a result.</summary>
    /// <param name="connectionString">The connection string to run it on.</param>
    /// <param name="sql">The statement.</param>
    public static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>A migrated and seeded benchmark database with the services that work on it.</summary>
internal sealed class SeededDatabase : IAsyncDisposable
{
    private readonly string _adminConnectionString;
    private readonly ServiceProvider _provider;

    public SeededDatabase(
        string adminConnectionString,
        string connectionString,
        string name,
        QueryCounter counter,
        MovieQueries queries,
        MovieCommands commands,
        UserClock clock,
        SampleData sample,
        ServiceProvider provider
    )
    {
        _adminConnectionString = adminConnectionString;
        ConnectionString = connectionString;
        Name = name;
        Counter = counter;
        Queries = queries;
        Commands = commands;
        Clock = clock;
        Sample = sample;
        _provider = provider;
    }

    public string ConnectionString { get; }

    public string Name { get; }

    public QueryCounter Counter { get; }

    public MovieQueries Queries { get; }

    public MovieCommands Commands { get; }

    public UserClock Clock { get; }

    public SampleData Sample { get; }

    /// <summary>The version of the movie that the update operation saves; it grows with every save.</summary>
    public int UpdateVersion { get; set; } = 1;

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await MySqlConnection.ClearAllPoolsAsync();
        await BenchmarkEnvironment.ExecuteAsync(_adminConnectionString, $"DROP DATABASE IF EXISTS `{Name}`");
    }
}
