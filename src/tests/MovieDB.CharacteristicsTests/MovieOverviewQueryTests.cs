using MovieDB.Common;
using MovieDB.Infrastructure;
using MovieDB.Infrastructure.Entities;
using MovieDB.Movies;
using Shouldly;

namespace MovieDB.CharacteristicsTests;

[Collection(MariaDbCollection.Name)]
public sealed class MovieOverviewQueryTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly MariaDbFixture _fixture;

    public MovieOverviewQueryTests(MariaDbFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record Seed(
        string Title,
        DateOnly WatchedOn,
        int? Score = null,
        string? Medium = null,
        int Year = 2000
    );

    private async Task<MovieQueries> SeedAsync(params Seed[] seeds)
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        var factory = MariaDbFixture.CreateFactory(connectionString);
        await using (var db = factory.CreateDbContext())
        {
            var genre = new Genre { Name = "Drama", CreatedAt = Now };
            db.Genres.Add(genre);
            var media = new Dictionary<string, Medium>();
            foreach (var name in seeds.Select(s => s.Medium).OfType<string>().Distinct())
            {
                media[name] = new Medium { Name = name, CreatedAt = Now };
                db.Media.Add(media[name]);
            }

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            foreach (var seed in seeds)
            {
                db.Movies.Add(
                    new Movie
                    {
                        Title = seed.Title,
                        ReleaseYear = seed.Year,
                        Director = "Director",
                        GenreId = genre.Id,
                        RuntimeMinutes = 100,
                        WatchedOn = seed.WatchedOn,
                        MediumId = seed.Medium is null ? null : media[seed.Medium].Id,
                        OverallScore = seed.Score,
                        CreatedAt = Now,
                        UpdatedAt = Now,
                    }
                );
            }

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        return new MovieQueries(factory);
    }

    private static DateOnly Day(int day) => new(2026, 1, day);

    private static OverviewState State(
        string search = "",
        OverviewSortField sort = OverviewSortField.WatchedOn,
        bool? descending = null,
        int page = 1,
        int size = 50
    ) => new(search, [new SortKey(sort, descending ?? OverviewState.DefaultDescending(sort))], page, size);

    private static OverviewState State(params SortKey[] sorts) => new("", sorts, 1, 50);

    [Fact]
    public async Task Multiple_sort_columns_apply_in_order_with_missing_values_last()
    {
        var queries = await SeedAsync(
            new Seed("b", Day(1), Score: 5),
            new Seed("a", Day(2), Score: 5),
            new Seed("c", Day(3), Score: 8),
            new Seed("d", Day(4)),
            new Seed("e", Day(5))
        );

        var scoreThenTitleDesc = await queries.ListOverviewAsync(
            State(new SortKey(OverviewSortField.OverallScore, false), new SortKey(OverviewSortField.Title, true)),
            TestContext.Current.CancellationToken
        );
        var scoreDescThenDate = await queries.ListOverviewAsync(
            State(new SortKey(OverviewSortField.OverallScore, true), new SortKey(OverviewSortField.WatchedOn, false)),
            TestContext.Current.CancellationToken
        );

        Titles(scoreThenTitleDesc).ShouldBe(["b", "a", "c", "e", "d"]);
        Titles(scoreDescThenDate).ShouldBe(["c", "b", "a", "d", "e"]);
    }

    private static string[] Titles(MovieOverviewPage page) => [.. page.Rows.Select(r => r.Title)];

    [Fact]
    public async Task The_default_order_is_newest_viewing_first_then_title()
    {
        var queries = await SeedAsync(
            new Seed("B", Day(2)),
            new Seed("A", Day(2)),
            new Seed("C", Day(5)),
            new Seed("D", Day(1))
        );

        var page = await queries.ListOverviewAsync(State(), TestContext.Current.CancellationToken);

        Titles(page).ShouldBe(["C", "A", "B", "D"]);
        page.TotalMovies.ShouldBe(4);
        page.TotalMatching.ShouldBe(4);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_scores_sort_last_in_both_directions(bool descending)
    {
        var queries = await SeedAsync(
            new Seed("none", Day(1)),
            new Seed("low", Day(2), Score: 3),
            new Seed("high", Day(3), Score: 9)
        );

        var page = await queries.ListOverviewAsync(
            State(sort: OverviewSortField.OverallScore, descending: descending),
            TestContext.Current.CancellationToken
        );

        Titles(page).ShouldBe(descending ? ["high", "low", "none"] : ["low", "high", "none"]);
    }

    [Fact]
    public async Task Missing_media_sort_last()
    {
        var queries = await SeedAsync(
            new Seed("none", Day(1)),
            new Seed("dvd", Day(2), Medium: "DVD"),
            new Seed("bluray", Day(3), Medium: "Blu-ray")
        );

        var ascending = await queries.ListOverviewAsync(
            State(sort: OverviewSortField.Medium),
            TestContext.Current.CancellationToken
        );
        var descending = await queries.ListOverviewAsync(
            State(sort: OverviewSortField.Medium, descending: true),
            TestContext.Current.CancellationToken
        );

        Titles(ascending).ShouldBe(["bluray", "dvd", "none"]);
        Titles(descending).ShouldBe(["dvd", "bluray", "none"]);
    }

    [Fact]
    public async Task Titles_sort_ignoring_case_and_accents()
    {
        var queries = await SeedAsync(new Seed("zèbre", Day(1)), new Seed("Éclair", Day(2)), new Seed("apple", Day(3)));

        var page = await queries.ListOverviewAsync(
            State(sort: OverviewSortField.Title),
            TestContext.Current.CancellationToken
        );

        Titles(page).ShouldBe(["apple", "Éclair", "zèbre"]);
    }

    [Fact]
    public async Task Rows_with_equal_sort_values_keep_a_stable_order_across_pages()
    {
        var queries = await SeedAsync([.. Enumerable.Range(1, 60).Select(i => new Seed($"Film {i}", Day(1)))]);

        var titles = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            titles.AddRange(
                Titles(
                    await queries.ListOverviewAsync(
                        State(sort: OverviewSortField.ReleaseYear, page: page, size: 25),
                        TestContext.Current.CancellationToken
                    )
                )
            );
        }

        titles.Count.ShouldBe(60);
        titles.Distinct().Count().ShouldBe(60);
    }

    [Fact]
    public async Task Search_matches_part_of_the_title_ignoring_case_and_accents()
    {
        var queries = await SeedAsync(
            new Seed("Léon", Day(1)),
            new Seed("The Lion King", Day(2)),
            new Seed("Heat", Day(3))
        );

        var page = await queries.ListOverviewAsync(State(search: "LEO"), TestContext.Current.CancellationToken);

        Titles(page).ShouldBe(["Léon"]);
        page.TotalMatching.ShouldBe(1);
        page.TotalMovies.ShouldBe(3);
    }

    [Theory]
    [InlineData("100%", "100% Wolf")]
    [InlineData("_", "A_B")]
    [InlineData("\\", "Back\\slash")]
    public async Task Wildcard_characters_in_the_search_match_literally(string search, string expected)
    {
        var queries = await SeedAsync(
            new Seed("100% Wolf", Day(1)),
            new Seed("A_B", Day(2)),
            new Seed("Back\\slash", Day(3)),
            new Seed("Plain", Day(4))
        );

        var page = await queries.ListOverviewAsync(State(search: search), TestContext.Current.CancellationToken);

        Titles(page).ShouldBe([expected]);
    }

    [Fact]
    public async Task A_page_beyond_the_last_is_clamped()
    {
        var queries = await SeedAsync([.. Enumerable.Range(1, 30).Select(i => new Seed($"Film {i:00}", Day(1)))]);

        var page = await queries.ListOverviewAsync(State(page: 9, size: 25), TestContext.Current.CancellationToken);

        page.PageCount.ShouldBe(2);
        page.Page.ShouldBe(2);
        page.Rows.Count.ShouldBe(5);
    }

    [Fact]
    public async Task An_empty_database_gives_one_empty_page()
    {
        var queries = await SeedAsync();

        var page = await queries.ListOverviewAsync(State(page: 4), TestContext.Current.CancellationToken);

        page.Rows.ShouldBeEmpty();
        page.Page.ShouldBe(1);
        page.PageCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_movie_is_found_by_id_with_its_genre_and_medium()
    {
        var queries = await SeedAsync(new Seed("Heat", Day(3), Score: 8, Medium: "DVD"));
        var row = (await queries.ListOverviewAsync(State(), TestContext.Current.CancellationToken)).Rows.Single();

        var found = await queries.FindAsync(row.Id, TestContext.Current.CancellationToken);

        found.ShouldNotBeNull();
        found.Title.ShouldBe("Heat");
        found.Genre.ShouldBe("Drama");
        found.Medium.ShouldBe("DVD");
        found.OverallScore.ShouldBe(8);
        (
            await queries.FindAsync(MovieId.From(row.Id.Value + 1000), TestContext.Current.CancellationToken)
        ).ShouldBeNull();
    }
}
