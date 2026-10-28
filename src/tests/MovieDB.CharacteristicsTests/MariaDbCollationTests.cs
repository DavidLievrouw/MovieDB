using MySqlConnector;
using Shouldly;
using Testcontainers.MariaDb;

namespace MovieDB.CharacteristicsTests;

/// <summary>Smoke test proving the Testcontainers MariaDB image and the collation the domain rules rely on.</summary>
public sealed class MariaDbCollationTests : IAsyncLifetime
{
    private readonly MariaDbContainer _db = new MariaDbBuilder("mariadb:12.3.3").Build();

    public async ValueTask InitializeAsync() => await _db.StartAsync();

    public async ValueTask DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Collation_treats_case_and_accents_as_equal()
    {
        await using var connection = new MySqlConnection(_db.GetConnectionString() + ";CharSet=utf8mb4");
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 'Amélie' = 'AMELIE' COLLATE utf8mb4_uca1400_ai_ci";

        var equal = Convert.ToInt32(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));

        equal.ShouldBe(1);
    }
}
