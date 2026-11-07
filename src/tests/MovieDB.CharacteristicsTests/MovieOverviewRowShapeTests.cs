using Microsoft.EntityFrameworkCore;
using MovieDB.Common;
using MovieDB.Infrastructure;
using MovieDB.Infrastructure.Entities;
using MovieDB.Movies;
using Shouldly;

namespace MovieDB.CharacteristicsTests;

/// <summary>The overview loads the names of the page rows with the page query; this checks how they come back.</summary>
[Collection(MariaDbCollection.Name)]
public sealed class MovieOverviewRowShapeTests
{
    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);
    private readonly MariaDbFixture _fixture;

    public MovieOverviewRowShapeTests(MariaDbFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(MovieQueries Queries, IDbContextFactory<MovieDbContext> Factory)> CreateAsync()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        var factory = MariaDbFixture.CreateFactory(connectionString);
        return (new MovieQueries(factory), factory);
    }

    private static Movie NewMovie(string title, int year, IReadOnlyList<Director> directors, int[] genreIds) =>
        new()
        {
            Title = title,
            ReleaseYear = year,
            DirectorsKey = DirectorsKey.Of(directors.Select(d => d.Id)),
            DirectorLinks = [.. directors.Select((d, i) => new MovieDirector { DirectorId = d.Id, Position = i + 1 })],
            GenreLinks = [.. genreIds.Select(g => new MovieGenre { GenreId = GenreId.From(g) })],
            RuntimeMinutes = 100,
            WatchedOn = new DateOnly(2025, 3, 1),
            CreatedAt = Now,
            UpdatedAt = Now,
        };

    [Fact]
    public async Task Directors_come_in_the_entered_order_and_genres_alphabetically()
    {
        var ct = TestContext.Current.CancellationToken;
        var (queries, factory) = await CreateAsync();
        string[] expectedGenres;
        await using (var db = factory.CreateDbContext())
        {
            var zed = new Director { Name = "Zed", CreatedAt = Now };
            var anna = new Director { Name = "Anna", CreatedAt = Now };
            var commaName = new Director { Name = "Smith, John", CreatedAt = Now };
            db.Directors.AddRange(zed, anna, commaName);
            await db.SaveChangesAsync(ct);

            var genres = await db.Genres.OrderByDescending(g => g.Id).Take(3).ToListAsync(ct);
            expectedGenres = [.. genres.Select(g => g.Name).Order(StringComparer.OrdinalIgnoreCase)];
            db.Movies.Add(NewMovie("Ordered", 2001, [zed, commaName, anna], [.. genres.Select(g => g.Id.Value)]));
            await db.SaveChangesAsync(ct);
        }

        var page = await queries.ListOverviewAsync(OverviewState.Default, ct);

        var row = page.Rows.ShouldHaveSingleItem();
        row.Directors.ShouldBe(["Zed", "Smith, John", "Anna"]);
        row.Genres.ShouldBe(expectedGenres);
    }

    [Fact]
    public async Task A_movie_without_directors_genres_or_medium_has_empty_lists_and_no_medium()
    {
        var ct = TestContext.Current.CancellationToken;
        var (queries, factory) = await CreateAsync();
        await using (var db = factory.CreateDbContext())
        {
            db.Movies.Add(NewMovie("Bare", 1999, [], []));
            await db.SaveChangesAsync(ct);
        }

        var page = await queries.ListOverviewAsync(OverviewState.Default, ct);

        var row = page.Rows.ShouldHaveSingleItem();
        row.Directors.ShouldBeEmpty();
        row.Genres.ShouldBeEmpty();
        row.Medium.ShouldBeNull();
        row.OverallScore.ShouldBeNull();
        page.TotalMatching.ShouldBe(1);
        page.TotalMovies.ShouldBe(1);
    }

    [Fact]
    public async Task A_page_beyond_the_last_is_clamped_and_an_empty_result_has_one_empty_page()
    {
        var ct = TestContext.Current.CancellationToken;
        var (queries, factory) = await CreateAsync();
        await using (var db = factory.CreateDbContext())
        {
            for (var i = 0; i < 12; i++)
            {
                var director = new Director { Name = "Director " + i, CreatedAt = Now };
                db.Directors.Add(director);
                await db.SaveChangesAsync(ct);
                db.Movies.Add(NewMovie("Movie " + i, 2000 + i, [director], []));
            }

            await db.SaveChangesAsync(ct);
        }

        var beyond = await queries.ListOverviewAsync(
            new OverviewState("", [new SortKey(OverviewSortField.Title, false)], 99, 10),
            ct
        );
        var none = await queries.ListOverviewAsync(
            new OverviewState("nothing like this", [new SortKey(OverviewSortField.Title, false)], 5, 10),
            ct
        );

        beyond.Page.ShouldBe(2);
        beyond.PageCount.ShouldBe(2);
        beyond.Rows.Count.ShouldBe(2);
        none.Page.ShouldBe(1);
        none.PageCount.ShouldBe(1);
        none.Rows.ShouldBeEmpty();
        none.TotalMatching.ShouldBe(0);
        none.TotalMovies.ShouldBe(12);
    }
}
