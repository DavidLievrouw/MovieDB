using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MovieDB.AccessImport;
using MovieDB.Infrastructure;
using MySqlConnector;
using Shouldly;

namespace MovieDB.CharacteristicsTests.Import;

[Collection(MariaDbCollection.Name)]
public sealed class ImportLoaderTests
{
    private readonly MariaDbFixture _fixture;

    public ImportLoaderTests(MariaDbFixture fixture)
    {
        _fixture = fixture;
    }

    private static SourceFilm Film(
        int id,
        string title,
        int year = 1999,
        string director = "Jane Doe",
        string genre = "Drama",
        string? medium = "DVD",
        string? review = "Synthetic review 😀"
    ) =>
        new(
            id,
            title,
            genre,
            " 90",
            year.ToString(System.Globalization.CultureInfo.InvariantCulture),
            director,
            new DateTime(2020, 5, 17, 0, 0, 0, DateTimeKind.Unspecified),
            7,
            6,
            5,
            4,
            3,
            "n/a",
            medium,
            review
        );

    private static ImportPlan SamplePlan() =>
        ImportPlanner.Plan([
            Film(10, "Léon"),
            Film(250, "Amélie", 2001, "Jean-Pierre Jeunet", "Komedie", null, null),
            Film(11, "LEON", 1999),
            Film(300, "Ongekend", 2005, "X", "Nieuw genre", "Laserdisc"),
        ]);

    private static async Task<ImportLoader> LoaderAsync(string connectionString) =>
        await Task.FromResult(new ImportLoader(MariaDbFixture.CreateFactory(connectionString), TimeProvider.System));

    [Fact]
    public async Task Load_preserves_ids_and_seeds_suggestions_and_verifies()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        var plan = SamplePlan();

        var result = await (await LoaderAsync(connectionString)).LoadAsync(
            plan,
            reset: false,
            TestContext.Current.CancellationToken
        );

        result.Problems.ShouldBeEmpty();
        result.Database.ShouldBe(plan.Stats);
        (await MariaDbFixture.ScalarAsync<int>(connectionString, "SELECT COUNT(*) FROM movies")).ShouldBe(
            plan.Movies.Count
        );
        (await MariaDbFixture.ScalarAsync<int>(connectionString, "SELECT MAX(id) FROM movies")).ShouldBe(300);
        (
            await MariaDbFixture.ScalarAsync<int>(connectionString, "SELECT COUNT(*) FROM movies WHERE id = 250")
        ).ShouldBe(1);
        (await MariaDbFixture.ScalarAsync<int>(connectionString, "SELECT COUNT(*) FROM genres")).ShouldBe(
            ImportPlanner.KnownGenres.Count + 1
        );
        (await MariaDbFixture.ScalarAsync<int>(connectionString, "SELECT COUNT(*) FROM media")).ShouldBe(
            ImportPlanner.KnownMedia.Count + 1
        );
    }

    [Fact]
    public async Task Load_leaves_the_auto_increment_counter_past_the_highest_id()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await (await LoaderAsync(connectionString)).LoadAsync(
            SamplePlan(),
            reset: false,
            TestContext.Current.CancellationToken
        );

        await MariaDbFixture.ExecuteAsync(
            connectionString,
            "INSERT INTO movies (title, release_year, director, genre_id, runtime_minutes, watched_on, created_at, updated_at, version) "
                + "VALUES ('Next', 2024, 'Y', (SELECT MIN(id) FROM genres), 100, '2024-01-01', NOW(6), NOW(6), 1)"
        );

        (
            await MariaDbFixture.ScalarAsync<int>(connectionString, "SELECT id FROM movies WHERE title = 'Next'")
        ).ShouldBeGreaterThan(300);
    }

    [Fact]
    public async Task Load_refuses_a_non_empty_database_without_reset()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        var loader = await LoaderAsync(connectionString);
        await loader.LoadAsync(SamplePlan(), reset: false, TestContext.Current.CancellationToken);

        await Should.ThrowAsync<ImportException>(() =>
            loader.LoadAsync(SamplePlan(), reset: false, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Reset_replaces_domain_data_but_keeps_users()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await MariaDbFixture.ExecuteAsync(
            connectionString,
            "INSERT INTO users (id, user_name, normalized_user_name, password_hash, security_stamp, concurrency_stamp, created_at, updated_at) "
                + "VALUES ('u1', 'admin', 'ADMIN', 'x', 's', 'c', NOW(6), NOW(6))"
        );
        var loader = await LoaderAsync(connectionString);
        await loader.LoadAsync(SamplePlan(), reset: false, TestContext.Current.CancellationToken);

        var second = ImportPlanner.Plan([Film(5, "Only")]);
        var result = await loader.LoadAsync(second, reset: true, TestContext.Current.CancellationToken);

        result.Problems.ShouldBeEmpty();
        (await MariaDbFixture.ScalarAsync<int>(connectionString, "SELECT COUNT(*) FROM movies")).ShouldBe(1);
        (await MariaDbFixture.ScalarAsync<int>(connectionString, "SELECT COUNT(*) FROM users")).ShouldBe(1);
    }
}

[Collection(MariaDbCollection.Name)]
public sealed class DumpRoundTripTests
{
    private readonly MariaDbFixture _fixture;

    public DumpRoundTripTests(MariaDbFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<IReadOnlyList<TableFingerprint>> FingerprintAsync(string connectionString)
    {
        await using var provider = ImportTool.BuildServices(connectionString);
        var factory = provider.GetRequiredService<IDbContextFactory<MovieDbContext>>();
        await using var context = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);
        return await Fingerprint.ComputeAsync(
            context.Database.GetDbConnection(),
            TestContext.Current.CancellationToken
        );
    }

    private static async Task<string> SeedAsync(MariaDbFixture fixture)
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        var plan = ImportPlanner.Plan([
            Film(10, "Léon", "Luc Besson", "Actie", "DVD", "Eerste regel\nTweede regel — 😀 'quote' \"dq\" \\ back"),
            Film(250, "Amélie", "Jean-Pierre Jeunet", "Komedie", null, null),
            Film(300, "Ongekend", "X", "Nieuw genre", "Laserdisc", "Ça va"),
        ]);
        await new ImportLoader(MariaDbFixture.CreateFactory(connectionString), TimeProvider.System).LoadAsync(
            plan,
            reset: false,
            TestContext.Current.CancellationToken
        );
        return connectionString;
    }

    private static SourceFilm Film(
        int id,
        string title,
        string director,
        string genre,
        string? medium,
        string? review
    ) =>
        new(
            id,
            title,
            genre,
            "100",
            "2001",
            director,
            new DateTime(2022, 2, 3, 0, 0, 0, DateTimeKind.Unspecified),
            8,
            7,
            null,
            6,
            5,
            "9",
            medium,
            review
        );

    private static async Task<string> WrapAsync(string connectionString, string body)
    {
        var bodyPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".body.sql");
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".sql");
        try
        {
            await File.WriteAllTextAsync(
                bodyPath,
                body,
                new System.Text.UTF8Encoding(false),
                TestContext.Current.CancellationToken
            );
            var exit = await ImportTool.WrapDumpAsync(
                ImportTool.Options.Parse(["--body", bodyPath, "--output", outputPath, "--source", "x.mdb"]),
                TimeProvider.System,
                TestContext.Current.CancellationToken,
                connectionString
            );
            exit.ShouldBe(0);
            return await File.ReadAllTextAsync(outputPath, TestContext.Current.CancellationToken);
        }
        finally
        {
            File.Delete(bodyPath);
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task Dump_restores_identical_data_into_an_empty_schema_and_is_then_refused()
    {
        var source = await SeedAsync(_fixture);
        var script = await WrapAsync(source, await _fixture.DumpDataAsync(source));
        var target = await _fixture.CreateMigratedDatabaseAsync();

        var load = await _fixture.RunScriptAsync(target, script);
        load.ExitCode.ShouldBe(0, load.Stderr);

        (await FingerprintAsync(target)).ShouldBe(await FingerprintAsync(source));

        var again = await _fixture.RunScriptAsync(target, script);
        again.ExitCode.ShouldNotBe(0);
        again.Stderr.ShouldContain("Refusing to load");
        (await FingerprintAsync(target)).ShouldBe(await FingerprintAsync(source));
    }

    [Fact]
    public async Task Dump_is_refused_when_the_schema_is_not_at_the_expected_migration()
    {
        var source = await SeedAsync(_fixture);
        var script = await WrapAsync(source, await _fixture.DumpDataAsync(source));
        var target = await _fixture.CreateMigratedDatabaseAsync();
        await MariaDbFixture.ExecuteAsync(
            target,
            "INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`) VALUES ('99999999999999_Future', '10.0.0')"
        );

        var load = await _fixture.RunScriptAsync(target, script);

        load.ExitCode.ShouldNotBe(0);
        load.Stderr.ShouldContain("Refusing to load");
        (await MariaDbFixture.ScalarAsync<int>(target, "SELECT COUNT(*) FROM movies")).ShouldBe(0);
    }

    [Fact]
    public async Task A_failing_dump_statement_rolls_the_whole_load_back_or_aborts_before_changes()
    {
        var source = await SeedAsync(_fixture);
        var script = await WrapAsync(source, await _fixture.DumpDataAsync(source));
        var broken = script.Replace(
            "COMMIT;",
            "INSERT INTO `movies` (`id`) VALUES (1);\nCOMMIT;",
            StringComparison.Ordinal
        );
        var target = await _fixture.CreateMigratedDatabaseAsync();

        var load = await _fixture.RunScriptAsync(target, broken);

        load.ExitCode.ShouldNotBe(0);
        (await MariaDbFixture.ScalarAsync<int>(target, "SELECT COUNT(*) FROM movies")).ShouldBe(0);
    }
}
