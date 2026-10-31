using MovieDB.Infrastructure;
using MovieDB.Infrastructure.Entities;
using MovieDB.Movies;
using Shouldly;

namespace MovieDB.CharacteristicsTests;

[Collection(MariaDbCollection.Name)]
public sealed class MovieStatisticsQueryTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 6, 15);

    private readonly MariaDbFixture _fixture;

    public MovieStatisticsQueryTests(MariaDbFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed record Seed(
        string Genre = "Drama",
        string Director = "Director",
        string? Medium = null,
        int Runtime = 100,
        int? Score = null,
        DateOnly? WatchedOn = null
    );

    private async Task<MovieQueries> SeedAsync(params Seed[] seeds)
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        var factory = MariaDbFixture.CreateFactory(connectionString);
        await using (var db = factory.CreateDbContext())
        {
            var genres = seeds
                .Select(s => s.Genre)
                .Distinct()
                .ToDictionary(name => name, name => new Genre { Name = name, CreatedAt = Now });
            var media = seeds
                .Select(s => s.Medium)
                .OfType<string>()
                .Distinct()
                .ToDictionary(name => name, name => new Medium { Name = name, CreatedAt = Now });
            db.Genres.AddRange(genres.Values);
            db.Media.AddRange(media.Values);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var number = 0;
            foreach (var seed in seeds)
            {
                number++;
                db.Movies.Add(
                    new Movie
                    {
                        Title = $"Film {number}",
                        ReleaseYear = 2000,
                        Director = seed.Director,
                        GenreId = genres[seed.Genre].Id,
                        RuntimeMinutes = seed.Runtime,
                        WatchedOn = seed.WatchedOn ?? new DateOnly(2020, 1, 1),
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

    private static Seed[] Many(int count, Seed seed) => [.. Enumerable.Repeat(seed, count)];

    [Fact]
    public async Task An_empty_database_has_empty_statistics()
    {
        var queries = await SeedAsync();

        var stats = await queries.GetStatisticsAsync(Today, TestContext.Current.CancellationToken);

        stats.Genres.ShouldBeEmpty();
        stats.Directors.ShouldBeEmpty();
        stats.Media.ShouldBeEmpty();
        stats.WatchedThisYear.ShouldBe(0);
        stats.TotalRuntimeMinutes.ShouldBe(0);
        stats.AverageScore.ShouldBeNull();
        stats.HighlyRated.ShouldBe(0);
    }

    [Fact]
    public async Task Genres_and_directors_are_counted_most_common_first_with_ties_by_name_and_limited_to_the_top()
    {
        var queries = await SeedAsync([
            .. Many(3, new Seed(Genre: "Komedie", Director: "Zed")),
            .. Many(2, new Seed(Genre: "Drama", Director: "Amy")),
            .. Many(2, new Seed(Genre: "Actie", Director: "Bob")),
            new Seed(Genre: "G4", Director: "D4"),
            new Seed(Genre: "G5", Director: "D5"),
            new Seed(Genre: "G6", Director: "D6"),
            new Seed(Genre: "G7", Director: "D7"),
        ]);

        var stats = await queries.GetStatisticsAsync(Today, TestContext.Current.CancellationToken);

        stats.Genres.Count.ShouldBe(MovieQueries.TopCount);
        stats
            .Genres.Take(3)
            .ShouldBe([new NamedCount("Komedie", 3), new NamedCount("Actie", 2), new NamedCount("Drama", 2)]);
        stats
            .Directors.Take(3)
            .ShouldBe([new NamedCount("Zed", 3), new NamedCount("Amy", 2), new NamedCount("Bob", 2)]);
    }

    [Fact]
    public async Task Directors_that_differ_only_in_case_or_accents_are_counted_together()
    {
        var queries = await SeedAsync(
            new Seed(Director: "Amélie"),
            new Seed(Director: "AMELIE"),
            new Seed(Director: "Other")
        );

        var stats = await queries.GetStatisticsAsync(Today, TestContext.Current.CancellationToken);

        stats.Directors[0].Count.ShouldBe(2);
        stats.Directors.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Media_are_counted_most_used_first_and_movies_without_medium_are_left_out()
    {
        var queries = await SeedAsync([
            .. Many(3, new Seed(Medium: "Streaming")),
            .. Many(2, new Seed(Medium: "Bioscoop")),
            new Seed(Medium: "DVD"),
            new Seed(),
        ]);

        var stats = await queries.GetStatisticsAsync(Today, TestContext.Current.CancellationToken);

        stats.Media.ShouldBe([new NamedCount("Streaming", 3), new NamedCount("Bioscoop", 2), new NamedCount("DVD", 1)]);
    }

    [Fact]
    public async Task Viewing_counts_use_the_current_month_year_and_previous_year()
    {
        var queries = await SeedAsync(
            new Seed(WatchedOn: new DateOnly(2026, 6, 1)),
            new Seed(WatchedOn: new DateOnly(2026, 6, 15)),
            new Seed(WatchedOn: new DateOnly(2026, 1, 1)),
            new Seed(WatchedOn: new DateOnly(2025, 12, 31)),
            new Seed(WatchedOn: new DateOnly(2025, 1, 1)),
            new Seed(WatchedOn: new DateOnly(2024, 12, 31))
        );

        var stats = await queries.GetStatisticsAsync(Today, TestContext.Current.CancellationToken);

        stats.WatchedThisMonth.ShouldBe(2);
        stats.WatchedThisYear.ShouldBe(3);
        stats.WatchedLastYear.ShouldBe(2);
    }

    [Fact]
    public async Task Runtime_is_summed_and_scores_are_averaged_ignoring_missing_scores()
    {
        var queries = await SeedAsync(
            new Seed(Runtime: 90, Score: 10),
            new Seed(Runtime: 120, Score: 9),
            new Seed(Runtime: 60, Score: 5),
            new Seed(Runtime: 30)
        );

        var stats = await queries.GetStatisticsAsync(Today, TestContext.Current.CancellationToken);

        stats.TotalRuntimeMinutes.ShouldBe(300);
        stats.AverageScore.ShouldNotBeNull().ShouldBe(8.0, 0.0001);
        stats.HighlyRated.ShouldBe(2);
    }

    [Fact]
    public async Task Runtime_sum_may_exceed_the_range_of_the_smallint_column()
    {
        var queries = await SeedAsync(Many(40, new Seed(Runtime: 999)));

        var stats = await queries.GetStatisticsAsync(Today, TestContext.Current.CancellationToken);

        stats.TotalRuntimeMinutes.ShouldBe(39_960);
    }
}
