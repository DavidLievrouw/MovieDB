using MovieDB.AccessImport;
using MySqlConnector;
using Shouldly;

namespace MovieDB.CharacteristicsTests.Import;

[Collection(MariaDbCollection.Name)]
public sealed class CollationParityTests
{
    private readonly MariaDbFixture _fixture;

    public CollationParityTests(MariaDbFixture fixture)
    {
        _fixture = fixture;
    }

    public static TheoryData<string, string> Pairs =>
        new()
        {
            { "Léon", "Leon" },
            { "LÉON", "leon" },
            { "Amélie", "Amelie" },
            { "Ça va", "Ca va" },
            { "Ångström", "Angstrom" },
            { "Mørk", "Mork" },
            { "Æon Flux", "Aeon Flux" },
            { "Cœur", "Coeur" },
            { "Straße", "Strasse" },
            { "Đorđe", "Dorde" },
            { "Łódź", "Lodz" },
            { "Title ", "Title" },
            { "Title", "Title  " },
            { "ñandú", "NANDU" },
            { "Noël", "Noel" },
            { "Zoë", "Zoe" },
            { "Léon", "Léo" },
            { "A B", "AB" },
            { "A-B", "AB" },
            { "A.B", "AB" },
            { "Léon", "Leon 2" },
            { "千と千尋", "千と千尋" },
            { "千と千尋", "千と千" },
            { "Ωmega", "omega" },
            { "I", "ı" },
            { "ǆ", "dž" },
            { "ﬁlm", "film" },
            { "x\u00A0y", "x y" },
        };

    [Theory]
    [MemberData(nameof(Pairs))]
    public async Task Collation_key_agrees_with_the_database(string left, string right)
    {
        var connectionString = await _fixture.CreateDatabaseAsync();
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT (@left COLLATE utf8mb4_uca1400_ai_ci) = @right";
        command.Parameters.AddWithValue("@left", left);
        command.Parameters.AddWithValue("@right", right);
        var databaseEqual = Convert.ToInt32(
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken),
            System.Globalization.CultureInfo.InvariantCulture
        );

        (CollationKey.Of(left) == CollationKey.Of(right)).ShouldBe(databaseEqual == 1);
    }
}
