using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MovieDB.Infrastructure;
using MySqlConnector;

namespace MovieDB.AccessImport;

/// <summary>
/// Command line entry point. The connection string is only ever read from the <c>MOVIEDB_CONNECTION_STRING</c>
/// environment variable, so it never shows up in a process listing or a log.
/// </summary>
internal static class ImportTool
{
    public const string ConnectionStringVariable = "MOVIEDB_CONNECTION_STRING";

    private const string Usage = """
        Usage:
          import --source <file.mdb> [--reset] [--dry-run]
          migrate
          fingerprint
          wrap-dump --body <file> --output <file> --source <name>
        The database connection string is read from the MOVIEDB_CONNECTION_STRING environment variable.
        """;

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                await Console.Error.WriteLineAsync(Usage);
                return 2;
            }

            var options = Options.Parse(args.Skip(1).ToList());
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            return args[0] switch
            {
                "import" => await ImportAsync(options, TimeProvider.System, Console.Out, cts.Token),
                "migrate" => await MigrateAsync(cts.Token),
                "fingerprint" => await FingerprintAsync(cts.Token),
                "wrap-dump" => await WrapDumpAsync(options, TimeProvider.System, cts.Token),
                _ => Fail(Usage),
            };
        }
        catch (ImportException ex)
        {
            await Console.Error.WriteLineAsync($"ERROR: {ex.Message}");
            return 1;
        }
        catch (OperationCanceledException)
        {
            await Console.Error.WriteLineAsync("Cancelled.");
            return 130;
        }
        catch (Exception ex) when (ex is MySqlException or DbUpdateException or InvalidOperationException)
        {
            // Type and message only: database messages can echo values from the rows.
            await Console.Error.WriteLineAsync($"ERROR: {ex.GetType().Name} while talking to the database.");
            return 1;
        }
    }

    internal static async Task<int> ImportAsync(
        Options options,
        TimeProvider clock,
        TextWriter output,
        CancellationToken cancellationToken,
        IFilmSource? source = null,
        string? connectionString = null
    )
    {
        var sourcePath = options.Required("--source");
        source ??= new OdbcFilmSource(sourcePath);
        var dryRun = options.Flag("--dry-run");

        var rows = source.ReadFilms();
        var plan = ImportPlanner.Plan(rows);
        if (plan.HasErrors)
        {
            await output.WriteLineAsync($"Import aborted: {plan.Errors.Count} invalid value(s) in the source data:");
            foreach (var error in plan.Errors)
            {
                await output.WriteLineAsync($"  {error}");
            }

            return 1;
        }

        DataStats? database = null;
        var problems = new List<string>();
        if (!dryRun)
        {
            await using var provider = BuildServices(connectionString ?? RequireConnectionString());
            var factory = provider.GetRequiredService<IDbContextFactory<MovieDbContext>>();
            var migrator = new DatabaseMigrator(factory, NullLogger<DatabaseMigrator>.Instance);
            await migrator.MigrateAsync(cancellationToken);

            var result = await new ImportLoader(factory, clock).LoadAsync(
                plan,
                options.Flag("--reset"),
                cancellationToken
            );
            database = result.Database;
            problems.AddRange(result.Problems);
        }

        foreach (var line in ImportReport.Render(plan, database, dryRun))
        {
            await output.WriteLineAsync(line);
        }

        foreach (var problem in problems)
        {
            await output.WriteLineAsync($"PROBLEM: {problem}");
        }

        return problems.Count == 0 ? 0 : 1;
    }

    private static async Task<int> MigrateAsync(CancellationToken cancellationToken)
    {
        await using var provider = BuildServices(RequireConnectionString());
        var factory = provider.GetRequiredService<IDbContextFactory<MovieDbContext>>();
        await new DatabaseMigrator(factory, NullLogger<DatabaseMigrator>.Instance).MigrateAsync(cancellationToken);
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).Last();
        Console.WriteLine($"Database is at migration {applied}.");
        return 0;
    }

    private static async Task<int> FingerprintAsync(CancellationToken cancellationToken)
    {
        await using var provider = BuildServices(RequireConnectionString());
        var factory = provider.GetRequiredService<IDbContextFactory<MovieDbContext>>();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        await context.Database.OpenConnectionAsync(cancellationToken);
        foreach (var table in await Fingerprint.ComputeAsync(context.Database.GetDbConnection(), cancellationToken))
        {
            Console.WriteLine(table);
        }

        return 0;
    }

    internal static async Task<int> WrapDumpAsync(
        Options options,
        TimeProvider clock,
        CancellationToken cancellationToken,
        string? connectionString = null
    )
    {
        var bodyPath = options.Required("--body");
        var outputPath = options.Required("--output");
        var sourceName = options.Required("--source");

        await using var provider = BuildServices(connectionString ?? RequireConnectionString());
        var factory = provider.GetRequiredService<IDbContextFactory<MovieDbContext>>();
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        var expected = context.Database.GetMigrations().Last();
        var applied = (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).LastOrDefault();
        if (applied != expected)
        {
            throw new ImportException("The database is not at the latest migration; run 'migrate' first.");
        }

        await context.Database.OpenConnectionAsync(cancellationToken);
        var stats = await ImportLoader.ReadStatsAsync(context.Database.GetDbConnection(), cancellationToken);

        var body = await File.ReadAllTextAsync(bodyPath, new UTF8Encoding(false), cancellationToken);
        if (!body.Contains("INSERT INTO `movies`", StringComparison.Ordinal))
        {
            throw new ImportException("The dump body does not contain any movies.");
        }

        var script = DumpComposer.Compose(body, Path.GetFileName(sourceName), clock.GetUtcNow(), expected, stats);
        await File.WriteAllTextAsync(outputPath, script, new UTF8Encoding(false), cancellationToken);
        Console.WriteLine(
            string.Create(CultureInfo.InvariantCulture, $"Wrote guarded dump with {stats.Movies} movies.")
        );
        return 0;
    }

    internal static ServiceProvider BuildServices(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString) { CharacterSet = "utf8mb4" };
        return new ServiceCollection().AddMovieDbDatabase(builder.ConnectionString).BuildServiceProvider();
    }

    private static string RequireConnectionString() =>
        Environment.GetEnvironmentVariable(ConnectionStringVariable) is { Length: > 0 } value
            ? value
            : throw new ImportException($"Set the {ConnectionStringVariable} environment variable.");

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 2;
    }

    internal sealed class Options
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.Ordinal);

        public static Options Parse(IReadOnlyList<string> args)
        {
            var options = new Options();
            string? pending = null;
            foreach (var arg in args)
            {
                if (arg.StartsWith("--", StringComparison.Ordinal))
                {
                    if (pending is not null)
                    {
                        options._values[pending] = null;
                    }

                    pending = arg;
                }
                else if (pending is not null)
                {
                    options._values[pending] = arg;
                    pending = null;
                }
                else
                {
                    throw new ImportException($"Unexpected argument '{arg}'.");
                }
            }

            if (pending is not null)
            {
                options._values[pending] = null;
            }

            return options;
        }

        public bool Flag(string name) => _values.ContainsKey(name);

        public string Required(string name) =>
            _values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : throw new ImportException($"Missing required option {name}.");
    }
}
