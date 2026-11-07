using MovieDB.Infrastructure.Entities;
using MovieDB.Movies;
using Shouldly;

namespace MovieDB.CharacteristicsTests;

[Collection(MariaDbCollection.Name)]
public sealed class KnownDirectorsQueryTests
{
    private static readonly DateTime Created = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly MariaDbFixture _fixture;

    public KnownDirectorsQueryTests(MariaDbFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<MovieQueries> SeedAsync(params string[] directors)
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        var factory = MariaDbFixture.CreateFactory(connectionString);
        await using (var db = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken))
        {
            foreach (var name in directors)
            {
                db.Directors.Add(new Director { Name = name, CreatedAt = Created });
            }

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return new MovieQueries(factory);
    }

    [Fact]
    public async Task Case_and_accent_variants_return_the_stored_spelling()
    {
        var queries = await SeedAsync("Michael Mann", "Luc Besson", "Chloé Zhao");

        var known = await queries.FindKnownDirectorsAsync(
            ["michael mann", "LUC BESSON", "Chloe Zhao"],
            TestContext.Current.CancellationToken
        );

        known.ShouldBe(["Michael Mann", "Luc Besson", "Chloé Zhao"]);
    }

    [Fact]
    public async Task Unknown_names_are_absent()
    {
        var queries = await SeedAsync("Michael Mann");

        var known = await queries.FindKnownDirectorsAsync(
            ["Michael Mann", "Nobody Atall"],
            TestContext.Current.CancellationToken
        );

        known.ShouldBe(["Michael Mann"]);
    }

    [Fact]
    public async Task Empty_blank_and_duplicate_input_is_handled()
    {
        var queries = await SeedAsync("Michael Mann");

        (await queries.FindKnownDirectorsAsync([], TestContext.Current.CancellationToken)).ShouldBeEmpty();
        (await queries.FindKnownDirectorsAsync(["  ", ""], TestContext.Current.CancellationToken)).ShouldBeEmpty();
        (
            await queries.FindKnownDirectorsAsync(
                [" Michael Mann ", "Michael Mann"],
                TestContext.Current.CancellationToken
            )
        ).ShouldBe(["Michael Mann"]);
    }

    [Fact]
    public async Task Wildcard_characters_are_not_patterns()
    {
        var queries = await SeedAsync("Michael Mann");

        (
            await queries.FindKnownDirectorsAsync(["%", "Michael _ann", "M%"], TestContext.Current.CancellationToken)
        ).ShouldBeEmpty();
    }
}
