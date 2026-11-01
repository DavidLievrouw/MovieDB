using Microsoft.EntityFrameworkCore;
using MovieDB.Common;
using MovieDB.Infrastructure;
using MovieDB.Infrastructure.Entities;
using MySqlConnector;
using Shouldly;

namespace MovieDB.CharacteristicsTests;

[Collection(MariaDbCollection.Name)]
public sealed class MovieConstraintTests
{
    private readonly MariaDbFixture _fixture;

    public MovieConstraintTests(MariaDbFixture fixture)
    {
        this._fixture = fixture;
    }

    private static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    private static MovieDbContext NewContext(string connectionString) =>
        MariaDbFixture.CreateFactory(connectionString).CreateDbContext();

    private static Task<Genre> AddGenreAsync(MovieDbContext context, string name = "Drama") =>
        context.Genres.SingleAsync(g => g.Name == name, TestContext.Current.CancellationToken);

    private static async Task<Movie> NewMovieAsync(
        MovieDbContext context,
        Genre genre,
        string title = "Léon",
        int year = 1994,
        IReadOnlyList<string>? directors = null
    )
    {
        var movie = new Movie
        {
            Title = title,
            ReleaseYear = year,
            GenreLinks = [new MovieGenre { GenreId = genre.Id }],
            RuntimeMinutes = 110,
            WatchedOn = new DateOnly(2026, 1, 15),
            CreatedAt = Now,
            UpdatedAt = Now,
        };
        await DirectorTestData.AssignAsync(context, movie, directors ?? ["Luc Besson"]);
        return movie;
    }

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
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using (var context = NewContext(connectionString))
        {
            var genre = await AddGenreAsync(context);
            var movie = await NewMovieAsync(context, genre, title: "Amélie — 少年", directors: ["Jean-Pierre Jeunet"]);
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
    }

    [Fact]
    public async Task Title_year_and_director_set_are_unique_ignoring_case_and_accents()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using var context = NewContext(connectionString);
        var genre = await AddGenreAsync(context);
        context.Movies.Add(await NewMovieAsync(context, genre, "Léon", 1994, ["Luc Besson"]));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Movies.Add(await NewMovieAsync(context, genre, "LEON", 1994, ["luc besson"]));

        (await SaveFailureNumberAsync(context)).ShouldBe(1062);
    }

    [Fact]
    public async Task The_same_director_set_in_another_order_has_the_same_key_and_is_a_duplicate()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using var context = NewContext(connectionString);
        var genre = await AddGenreAsync(context);
        var first = await NewMovieAsync(context, genre, "Fargo", 1996, ["Joel Coen", "Ethan Coen"]);
        context.Movies.Add(first);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var reversed = await NewMovieAsync(context, genre, "Fargo", 1996, ["Ethan Coen", "Joel Coen"]);
        reversed.DirectorsKey.ShouldBe(first.DirectorsKey);
        context.Movies.Add(reversed);

        (await SaveFailureNumberAsync(context)).ShouldBe(1062);
    }

    [Theory]
    [InlineData("Léon", 1995, "Luc Besson")]
    [InlineData("Léon", 1994, "Besson, Luc")]
    [InlineData("Léon", 1994, "Luc Besson|Someone Else")]
    [InlineData("Léon 2", 1994, "Luc Besson")]
    public async Task A_different_year_director_set_or_title_is_a_different_movie(
        string title,
        int year,
        string directors
    )
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using var context = NewContext(connectionString);
        var genre = await AddGenreAsync(context);
        context.Movies.Add(await NewMovieAsync(context, genre, "Léon", 1994, ["Luc Besson"]));
        context.Movies.Add(await NewMovieAsync(context, genre, title, year, [.. directors.Split('|')]));

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        (await context.Movies.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task Director_names_are_unique_ignoring_case_and_accents()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using var context = NewContext(connectionString);
        context.Directors.Add(new Director { Name = "Pedro Almodóvar", CreatedAt = Now });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Directors.Add(new Director { Name = "PEDRO ALMODOVAR", CreatedAt = Now });

        (await SaveFailureNumberAsync(context)).ShouldBe(1062);
    }

    [Fact]
    public async Task A_movie_keeps_its_directors_in_order_and_cannot_have_one_twice_or_two_at_one_position()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using (var context = NewContext(connectionString))
        {
            context.Movies.Add(
                await NewMovieAsync(context, await AddGenreAsync(context), directors: ["Zed", "Amy", "Bob"])
            );
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var read = NewContext(connectionString);
        var movie = await read
            .Movies.Include(m => m.DirectorLinks)
                .ThenInclude(l => l.Director)
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        movie
            .DirectorLinks.OrderBy(l => l.Position)
            .Select(l => l.Director.Name)
            .ToList()
            .ShouldBe(["Zed", "Amy", "Bob"]);

        var extra = new Director { Name = "Cat", CreatedAt = Now };
        read.Directors.Add(extra);
        await read.SaveChangesAsync(TestContext.Current.CancellationToken);
        var existing = movie.DirectorLinks[0];
        read.MovieDirectorLinks.Add(
            new MovieDirector
            {
                MovieId = movie.Id,
                DirectorId = existing.DirectorId,
                Position = 9,
            }
        );
        (await SaveFailureNumberAsync(read)).ShouldBe(1062);

        await using var samePosition = NewContext(connectionString);
        var cat = await samePosition.Directors.SingleAsync(d => d.Name == "Cat", TestContext.Current.CancellationToken);
        samePosition.MovieDirectorLinks.Add(
            new MovieDirector
            {
                MovieId = movie.Id,
                DirectorId = cat.Id,
                Position = 1,
            }
        );
        (await SaveFailureNumberAsync(samePosition)).ShouldBe(1062);
    }

    [Fact]
    public async Task A_director_that_is_linked_cannot_be_deleted_and_a_movie_needs_an_existing_director()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using (var context = NewContext(connectionString))
        {
            context.Movies.Add(await NewMovieAsync(context, await AddGenreAsync(context)));
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var delete = NewContext(connectionString);
        var failure = await Should.ThrowAsync<MySqlException>(() =>
            delete.Directors.ExecuteDeleteAsync(TestContext.Current.CancellationToken)
        );
        failure.Number.ShouldBe(1451);

        await using var missing = NewContext(connectionString);
        var movie = await NewMovieAsync(missing, await AddGenreAsync(missing), "Other");
        movie.DirectorLinks = [new MovieDirector { DirectorId = DirectorId.From(99999), Position = 1 }];
        missing.Movies.Add(movie);
        (await SaveFailureNumberAsync(missing)).ShouldBe(1452);
    }

    [Fact]
    public async Task Genre_and_medium_names_are_unique_ignoring_case_and_accents()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using var context = NewContext(connectionString);
        context.Media.Add(new Medium { Name = "Café", CreatedAt = Now });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.Genres.Add(new Genre { Name = "THRILLER", CreatedAt = Now });
        (await SaveFailureNumberAsync(context)).ShouldBe(1062);

        await using var second = NewContext(connectionString);
        second.Media.Add(new Medium { Name = "CAFE", CreatedAt = Now });
        (await SaveFailureNumberAsync(second)).ShouldBe(1062);
    }

    [Fact]
    public async Task The_genres_are_seeded_with_stable_ids()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using var context = NewContext(connectionString);

        var genres = await context
            .Genres.AsNoTracking()
            .OrderBy(g => g.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        genres.Select(g => (g.Id.Value, g.Name)).ShouldBe(MovieGenres.All);
    }

    [Fact]
    public async Task A_movie_can_have_several_genres_but_not_the_same_genre_twice()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using (var context = NewContext(connectionString))
        {
            var movie = await NewMovieAsync(context, await AddGenreAsync(context));
            movie.GenreLinks.Add(new MovieGenre { GenreId = (await AddGenreAsync(context, "Actie")).Id });
            context.Movies.Add(movie);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        (await MariaDbFixture.ScalarAsync<long>(connectionString, "SELECT COUNT(*) FROM movie_genres")).ShouldBe(2);
        await using var duplicate = NewContext(connectionString);
        var movieId = (await duplicate.Movies.SingleAsync(TestContext.Current.CancellationToken)).Id;
        duplicate.MovieGenreLinks.Add(
            new MovieGenre { MovieId = movieId, GenreId = (await AddGenreAsync(duplicate)).Id }
        );
        (await SaveFailureNumberAsync(duplicate)).ShouldBe(1062);
    }

    [Fact]
    public async Task A_movie_with_genres_cannot_be_deleted_without_its_links()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using var context = NewContext(connectionString);
        context.Movies.Add(await NewMovieAsync(context, await AddGenreAsync(context)));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var delete = NewContext(connectionString);
        var failure = await Should.ThrowAsync<MySqlException>(() =>
            delete.Movies.ExecuteDeleteAsync(TestContext.Current.CancellationToken)
        );
        failure.Number.ShouldBe(1451);
    }

    [Fact]
    public async Task A_genre_or_medium_that_is_in_use_cannot_be_deleted()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using var context = NewContext(connectionString);
        var genre = await AddGenreAsync(context);
        var medium = new Medium { Name = "Bioscoop", CreatedAt = Now };
        context.Media.Add(medium);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var movie = await NewMovieAsync(context, genre);
        movie.MediumId = medium.Id;
        context.Movies.Add(movie);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await using var deleteGenre = NewContext(connectionString);
        deleteGenre.Genres.Remove(await AddGenreAsync(deleteGenre));
        (await SaveFailureNumberAsync(deleteGenre)).ShouldBe(1451);

        await using var deleteMedium = NewContext(connectionString);
        deleteMedium.Media.Remove(await deleteMedium.Media.SingleAsync(TestContext.Current.CancellationToken));
        (await SaveFailureNumberAsync(deleteMedium)).ShouldBe(1451);
    }

    [Fact]
    public async Task A_movie_needs_an_existing_genre()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using var context = NewContext(connectionString);
        var movie = await NewMovieAsync(context, new Genre { Id = GenreId.From(9999) });
        context.Movies.Add(movie);

        (await SaveFailureNumberAsync(context)).ShouldBe(1452);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    public async Task Runtime_must_be_between_1_and_999_minutes(int runtime)
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using var context = NewContext(connectionString);
        var movie = await NewMovieAsync(context, await AddGenreAsync(context));
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
    public async Task Scores_must_be_between_1_and_10_or_empty(string score)
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        foreach (var invalid in new[] { 0, 11 })
        {
            await using var context = NewContext(connectionString);
            var movie = await NewMovieAsync(context, await AddGenreAsync(context));
            Set(movie, score, invalid);
            context.Movies.Add(movie);
            (await SaveFailureNumberAsync(context)).ShouldBe(4025, $"{score} = {invalid}");
        }

        await using var valid = NewContext(connectionString);
        var boundaries = await NewMovieAsync(valid, await AddGenreAsync(valid));
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
            default:
                throw new ArgumentOutOfRangeException(nameof(score));
        }
    }

    [Fact]
    public async Task The_movies_table_has_no_extras_column()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();

        (
            await MariaDbFixture.ScalarAsync<long>(
                connectionString,
                "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'movies' AND COLUMN_NAME LIKE '%extra%'"
            )
        ).ShouldBe(0);
    }

    [Fact]
    public async Task Version_is_a_concurrency_token()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using (var seed = NewContext(connectionString))
        {
            seed.Movies.Add(await NewMovieAsync(seed, await AddGenreAsync(seed)));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var first = NewContext(connectionString);
        await using var second = NewContext(connectionString);
        var movieOne = await first.Movies.SingleAsync(TestContext.Current.CancellationToken);
        var movieTwo = await second.Movies.SingleAsync(TestContext.Current.CancellationToken);

        movieOne.Review = "First";
        movieOne.Version++;
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);

        movieTwo.Review = "Second";
        movieTwo.Version++;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() =>
            second.SaveChangesAsync(TestContext.Current.CancellationToken)
        );

        await using var read = NewContext(connectionString);
        var stored = await read.Movies.AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        stored.Review.ShouldBe("First");
        stored.Version.ShouldBe(2);
    }

    [Fact]
    public async Task Users_are_unique_on_normalized_user_name()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
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
