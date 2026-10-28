using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MovieDB.Infrastructure;
using Shouldly;

namespace MovieDB.CharacteristicsTests;

[Collection(MariaDbCollection.Name)]
public sealed class MigrationTests
{
    private readonly MariaDbFixture _fixture;

    public MigrationTests(MariaDbFixture fixture)
    {
        this._fixture = fixture;
    }

    private static readonly string[] ExpectedTables =
    [
        "__EFMigrationsHistory",
        "auth_throttle_states",
        "data_protection_keys",
        "genres",
        "media",
        "movies",
        "users",
    ];

    private static readonly int MigrationCount = typeof(MovieDbContext)
        .Assembly.GetTypes()
        .Count(type => type.IsDefined(typeof(MigrationAttribute), inherit: false));

    private static async Task<long> AppliedMigrationsAsync(string connectionString) =>
        await CountAsync(connectionString, "SELECT COUNT(*) FROM `__EFMigrationsHistory`");

    private static Task<long> CountAsync(string connectionString, string sql) =>
        MariaDbFixture.ScalarAsync<long>(connectionString, sql);

    private static async Task<List<string>> TablesAsync(string connectionString)
    {
        await using var connection = new MySqlConnector.MySqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE() ORDER BY table_name";
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken))
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    [Fact]
    public async Task Fresh_database_gets_all_tables_in_snake_case()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();

        (await TablesAsync(connectionString)).ShouldBe(ExpectedTables, ignoreOrder: true);
        (await AppliedMigrationsAsync(connectionString)).ShouldBe(MigrationCount);
    }

    [Fact]
    public async Task Model_matches_the_migrated_schema()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using var context = MariaDbFixture.CreateFactory(connectionString).CreateDbContext();

        context.Database.HasPendingModelChanges().ShouldBeFalse();
        (await context.Genres.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        (await context.Movies.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task Running_the_migrator_again_is_a_no_op()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        var migrator = MariaDbFixture.CreateMigrator(connectionString);

        await migrator.MigrateAsync(TestContext.Current.CancellationToken);
        await migrator.MigrateAsync(TestContext.Current.CancellationToken);

        (await AppliedMigrationsAsync(connectionString)).ShouldBe(MigrationCount);
    }

    [Fact]
    public async Task Concurrent_migrators_serialise_on_the_named_lock()
    {
        var connectionString = await _fixture.CreateDatabaseAsync();

        await Task.WhenAll(
            Enumerable
                .Range(0, 5)
                .Select(_ =>
                    Task.Run(
                        () =>
                            MariaDbFixture
                                .CreateMigrator(connectionString)
                                .MigrateAsync(TestContext.Current.CancellationToken),
                        TestContext.Current.CancellationToken
                    )
                )
        );

        (await TablesAsync(connectionString)).ShouldBe(ExpectedTables, ignoreOrder: true);
        (await AppliedMigrationsAsync(connectionString)).ShouldBe(MigrationCount);
    }

    [Fact]
    public async Task A_partially_applied_migration_can_be_retried()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await MariaDbFixture.ExecuteAsync(connectionString, "DROP TABLE `movies`");
        await MariaDbFixture.ExecuteAsync(connectionString, "DROP TABLE `users`");
        await MariaDbFixture.ExecuteAsync(connectionString, "DELETE FROM `__EFMigrationsHistory`");

        await MariaDbFixture.CreateMigrator(connectionString).MigrateAsync(TestContext.Current.CancellationToken);

        (await TablesAsync(connectionString)).ShouldBe(ExpectedTables, ignoreOrder: true);
        (await AppliedMigrationsAsync(connectionString)).ShouldBe(MigrationCount);
        (
            await CountAsync(connectionString, "SELECT COUNT(*) FROM `auth_throttle_states` WHERE `key` = 'global'")
        ).ShouldBe(1);
    }

    [Fact]
    public async Task Down_removes_the_domain_and_auth_tables_and_up_can_be_applied_again()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using (var context = MariaDbFixture.CreateFactory(connectionString).CreateDbContext())
        {
            await context.GetService<IMigrator>().MigrateAsync("0", TestContext.Current.CancellationToken);
        }

        (await TablesAsync(connectionString)).ShouldBe(["__EFMigrationsHistory"]);

        await MariaDbFixture.CreateMigrator(connectionString).MigrateAsync(TestContext.Current.CancellationToken);

        (await TablesAsync(connectionString)).ShouldBe(ExpectedTables, ignoreOrder: true);
    }

    [Fact]
    public async Task Text_columns_use_the_uca1400_accent_and_case_insensitive_collation()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();

        var collations = await CountAsync(
            connectionString,
            """
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name IN ('movies', 'genres', 'media', 'users')
              AND data_type IN ('varchar', 'mediumtext')
              AND collation_name <> 'utf8mb4_uca1400_ai_ci'
            """
        );

        collations.ShouldBe(0);
    }
}
