using Microsoft.EntityFrameworkCore;
using MovieDB.Common;
using MovieDB.Infrastructure;
using MovieDB.Infrastructure.Entities;
using MySqlConnector;
using Shouldly;

namespace MovieDB.CharacteristicsTests;

[Collection(MariaDbCollection.Name)]
public sealed class MovieConstraintTests(MariaDbFixture fixture)
{
    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private static MovieDbContext NewContext(string connectionString) =>
        MariaDbFixture.CreateFactory(connectionString).CreateDbContext();

    private static async Task<Genre> AddGenreAsync(MovieDbContext context, string name = "Drama")
    {
        var genre = new Genre { Name = name, CreatedAt = Now };
        context.Genres.Add(genre);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return genre;
    }

    private static Movie NewMovie(
        Genre genre,
        string title = "Léon",
        int year = 1994,
        string director = "Luc Besson"
    ) =>
        new()
        {
            Title = title,
            ReleaseYear = year,
            Director = director,
            GenreId = genre.Id,
            RuntimeMinutes = 110,
            WatchedOn = new DateOnly(2026, 1, 15),
            CreatedAt = Now,
            UpdatedAt = Now,
        };

    private static async Task<int?> SaveFailureNumberAsync(MovieDbContext context)
    {
        var exception = await Should.ThrowAsync<DbUpdateException>(() =>
            context.SaveChangesAsync(TestContext.Current.CancellationToken)
        );
        return (exception.InnerException as MySqlException)?.Number;
    }

    [Fact]
    public async Task A_movie_round_trips_with_unicode_and_optional_values_empty()
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        await using (var context = NewContext(connectionString))
        {
            var genre = await AddGenreAsync(context);
            var movie = NewMovie(genre, title: "Amélie — 少年", director: "Jean-Pierre Jeunet");
            movie.Review = "Très bien ✓";
            context.Movies.Add(movie);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = NewContext(connectionString);
        var stored = await read.Movies.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        stored.Title.ShouldBe("Amélie — 少年");
        stored.Review.ShouldBe("Très bien ✓");
        stored.WatchedOn.ShouldBe(new DateOnly(2026, 1, 15));
        stored.Version.ShouldBe(1);
        stored.MediumId.ShouldBeNull();
        stored.OverallScore.ShouldBeNull();
        stored.ExtrasScore.ShouldBeNull();
    }

    [Fact]
    public async Task Title_year_and_director_are_unique_ignoring_case_and_accents()
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        await using var context = NewContext(connectionString);
        var genre = await AddGenreAsync(context);
        context.Movies.Add(NewMovie(genre, "Léon", 1994, "Luc Besson"));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Movies.Add(NewMovie(genre, "LEON", 1994, "luc besson"));

        (await SaveFailureNumberAsync(context)).ShouldBe(1062);
    }

    [Theory]
    [InlineData("Léon", 1995, "Luc Besson")]
    [InlineData("Léon", 1994, "Besson, Luc")]
    [InlineData("Léon 2", 1994, "Luc Besson")]
    public async Task A_different_year_director_or_title_is_a_different_movie(string title, int year, string director)
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        await using var context = NewContext(connectionString);
        var genre = await AddGenreAsync(context);
        context.Movies.Add(NewMovie(genre, "Léon", 1994, "Luc Besson"));
        context.Movies.Add(NewMovie(genre, title, year, director));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await context.Movies.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task Genre_and_medium_names_are_unique_ignoring_case_and_accents()
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        await using var context = NewContext(connectionString);
        context.Genres.Add(new Genre { Name = "Thriller", CreatedAt = Now });
        context.Media.Add(new Medium { Name = "Café", CreatedAt = Now });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Genres.Add(new Genre { Name = "THRILLER", CreatedAt = Now });
        (await SaveFailureNumberAsync(context)).ShouldBe(1062);

        await using var second = NewContext(connectionString);
        second.Media.Add(new Medium { Name = "CAFE", CreatedAt = Now });
        (await SaveFailureNumberAsync(second)).ShouldBe(1062);
    }

    [Fact]
    public async Task A_genre_or_medium_that_is_in_use_cannot_be_deleted()
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        await using var context = NewContext(connectionString);
        var genre = await AddGenreAsync(context);
        var medium = new Medium { Name = "Bioscoop", CreatedAt = Now };
        context.Media.Add(medium);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var movie = NewMovie(genre);
        movie.MediumId = medium.Id;
        context.Movies.Add(movie);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var deleteGenre = NewContext(connectionString);
        deleteGenre.Genres.Remove(await deleteGenre.Genres.SingleAsync(TestContext.Current.CancellationToken));
        (await SaveFailureNumberAsync(deleteGenre)).ShouldBe(1451);

        await using var deleteMedium = NewContext(connectionString);
        deleteMedium.Media.Remove(await deleteMedium.Media.SingleAsync(TestContext.Current.CancellationToken));
        (await SaveFailureNumberAsync(deleteMedium)).ShouldBe(1451);
    }

    [Fact]
    public async Task A_movie_needs_an_existing_genre()
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        await using var context = NewContext(connectionString);
        var movie = NewMovie(new Genre());
        movie.GenreId = GenreId.From(9999);
        context.Movies.Add(movie);

        (await SaveFailureNumberAsync(context)).ShouldBe(1452);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public async Task Runtime_must_be_between_1_and_999_minutes(int runtime)
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        await using var context = NewContext(connectionString);
        var movie = NewMovie(await AddGenreAsync(context));
        movie.RuntimeMinutes = runtime;
        context.Movies.Add(movie);

        (await SaveFailureNumberAsync(context)).ShouldBe(4025);
    }

    [Theory]
    [InlineData("overall")]
    [InlineData("story")]
    [InlineData("picture")]
    [InlineData("music")]
    [InlineData("sound")]
    [InlineData("extras")]
    public async Task Scores_must_be_between_1_and_10_or_empty(string score)
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        foreach (var invalid in new[] { 0, 11 })
        {
            await using var context = NewContext(connectionString);
            var movie = NewMovie(
                await context.Genres.FirstOrDefaultAsync(TestContext.Current.CancellationToken)
                    ?? await AddGenreAsync(context)
            );
            Set(movie, score, invalid);
            context.Movies.Add(movie);
            (await SaveFailureNumberAsync(context)).ShouldBe(4025, $"{score} = {invalid}");
        }

        await using var valid = NewContext(connectionString);
        var boundaries = NewMovie(await valid.Genres.SingleAsync(TestContext.Current.CancellationToken));
        Set(boundaries, score, 1);
        valid.Movies.Add(boundaries);
        await valid.SaveChangesAsync(TestContext.Current.CancellationToken);
        Set(boundaries, score, 10);
        await valid.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private static void Set(Movie movie, string score, int? value)
    {
        switch (score)
        {
            case "overall":
                movie.OverallScore = value;
                break;
            case "story":
                movie.StoryScore = value;
                break;
            case "picture":
                movie.PictureScore = value;
                break;
            case "music":
                movie.MusicScore = value;
                break;
            case "sound":
                movie.SoundScore = value;
                break;
            case "extras":
                movie.ExtrasScore = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(score));
        }
    }

    [Fact]
    public async Task Version_is_a_concurrency_token()
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        await using (var seed = NewContext(connectionString))
        {
            seed.Movies.Add(NewMovie(await AddGenreAsync(seed)));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var first = NewContext(connectionString);
        await using var second = NewContext(connectionString);
        var movieOne = await first.Movies.SingleAsync(TestContext.Current.CancellationToken);
        var movieTwo = await second.Movies.SingleAsync(TestContext.Current.CancellationToken);

        movieOne.Director = "First";
        movieOne.Version++;
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);

        movieTwo.Director = "Second";
        movieTwo.Version++;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() =>
            second.SaveChangesAsync(TestContext.Current.CancellationToken)
        );

        await using var read = NewContext(connectionString);
        var stored = await read.Movies.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        stored.Director.ShouldBe("First");
        stored.Version.ShouldBe(2);
    }

    [Fact]
    public async Task Users_are_unique_on_normalized_user_name()
    {
        var connectionString = await fixture.CreateMigratedDatabaseAsync();
        await using var context = NewContext(connectionString);
        context.Users.Add(NewUser("ADMIN"));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Users.Add(NewUser("ADMIN"));

        (await SaveFailureNumberAsync(context)).ShouldBe(1062);
    }

    private static User NewUser(string normalized) =>
        new()
        {
            Id = UserId.From(Guid.NewGuid().ToString()),
            UserName = normalized.ToLowerInvariant(),
            NormalizedUserName = normalized,
            PasswordHash = "hash",
            SecurityStamp = "stamp",
            ConcurrencyStamp = "concurrency",
            CreatedAt = Now,
            UpdatedAt = Now,
        };
}
