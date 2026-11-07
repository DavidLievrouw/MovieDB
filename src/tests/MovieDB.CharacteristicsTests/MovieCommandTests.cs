using Microsoft.EntityFrameworkCore;
using MovieDB.Common;
using MovieDB.Infrastructure;
using MovieDB.Movies;
using Shouldly;

namespace MovieDB.CharacteristicsTests;

[Collection(MariaDbCollection.Name)]
public sealed class MovieCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly MariaDbFixture _fixture;

    public MovieCommandTests(MariaDbFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record Setup(
        MovieCommands Commands,
        MovieQueries Queries,
        IDbContextFactory<MovieDbContext> Factory,
        string ConnectionString
    );

    private async Task<Setup> CreateAsync()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        var factory = MariaDbFixture.CreateFactory(connectionString);
        var time = new FixedTimeProvider();
        var clock = new UserClock(time, UserClock.ResolveZone("Europe/Brussels"));
        return new Setup(
            new MovieCommands(factory, new MovieInputValidator(clock), time),
            new MovieQueries(factory),
            factory,
            connectionString
        );
    }

    private static MovieInput Input(
        string title = "Heat",
        int year = 1995,
        string director = "Michael Mann", // several directors are separated with |
        string[]? genres = null,
        string? medium = "Bioscoop"
    ) =>
        new()
        {
            Title = title,
            ReleaseYear = year,
            Directors = [.. director.Split('|')],
            Genres = [.. genres ?? ["Thriller"]],
            Medium = medium,
            RuntimeMinutes = 170,
            WatchedOn = new DateOnly(2026, 3, 1),
            OverallScore = 8,
            StoryScore = 7,
            PictureScore = 6,
            MusicScore = 5,
            SoundScore = 4,
            Review = "synthetic",
        };

    private static async Task<MovieId> CreateMovieAsync(Setup setup, MovieInput input)
    {
        var result = await setup.Commands.SaveAsync(null, 1, input, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        return result.Value.Id;
    }

    [Fact]
    public async Task A_new_movie_is_created_with_version_1_and_its_lookup_values()
    {
        var setup = await CreateAsync();

        var result = await setup.Commands.SaveAsync(
            null,
            1,
            Input(title: "  Heat  "),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe(1);
        var edit = await setup.Queries.FindForEditAsync(result.Value.Id, TestContext.Current.CancellationToken);
        edit.ShouldNotBeNull();
        edit.Input.Title.ShouldBe("Heat");
        edit.Input.Genres.ShouldBe(["Thriller"]);
        edit.Input.Medium.ShouldBe("Bioscoop");
        edit.Input.OverallScore.ShouldBe(8);
        edit.Input.StoryScore.ShouldBe(7);
        edit.CreatedAt.ShouldBe(Now.UtcDateTime);
        edit.UpdatedAt.ShouldBe(Now.UtcDateTime);
    }

    [Fact]
    public async Task Unicode_text_round_trips_unchanged()
    {
        var setup = await CreateAsync();
        var input = Input(title: "Amélie 🎬 東京物語", director: "Jean-Pierre Jeunet");
        input.Review = "Zoë’s «favoriet» ✓ 日本語";

        var id = await CreateMovieAsync(setup, input);

        var edit = await setup.Queries.FindForEditAsync(id, TestContext.Current.CancellationToken);
        edit!.Input.Title.ShouldBe(input.Title);
        edit.Input.Review.ShouldBe(input.Review);
    }

    [Fact]
    public async Task An_empty_medium_and_review_are_stored_as_null()
    {
        var setup = await CreateAsync();
        var input = Input(medium: "  ");
        input.Review = "   ";

        var id = await CreateMovieAsync(setup, input);

        var edit = await setup.Queries.FindForEditAsync(id, TestContext.Current.CancellationToken);
        edit!.Input.Medium.ShouldBeNull();
        edit.Input.Review.ShouldBeNull();
        (await MariaDbFixture.ScalarAsync<long>(setup.ConnectionString, "SELECT COUNT(*) FROM media")).ShouldBe(0);
    }

    [Fact]
    public async Task Updating_changes_the_values_and_increments_the_version()
    {
        var setup = await CreateAsync();
        var id = await CreateMovieAsync(setup, Input());
        var changed = Input(title: "Heat (director's cut)", genres: ["Misdaad"], medium: null);
        changed.OverallScore = 9;

        var result = await setup.Commands.SaveAsync(id, 1, changed, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe(2);
        var edit = await setup.Queries.FindForEditAsync(id, TestContext.Current.CancellationToken);
        edit!.Version.ShouldBe(2);
        edit.Input.Title.ShouldBe("Heat (director's cut)");
        edit.Input.Genres.ShouldBe(["Misdaad"]);
        edit.Input.Medium.ShouldBeNull();
        edit.Input.OverallScore.ShouldBe(9);
    }

    [Fact]
    public async Task A_save_with_an_old_version_is_a_conflict_and_changes_nothing()
    {
        var setup = await CreateAsync();
        var id = await CreateMovieAsync(setup, Input());
        (
            await setup.Commands.SaveAsync(id, 1, Input(title: "First edit"), TestContext.Current.CancellationToken)
        ).IsSuccess.ShouldBeTrue();

        var stale = await setup.Commands.SaveAsync(
            id,
            1,
            Input(title: "Stale edit"),
            TestContext.Current.CancellationToken
        );

        stale.IsSuccess.ShouldBeFalse();
        stale.Error.Kind.ShouldBe(ErrorKind.Conflict);
        stale.Error.Message.ShouldBe(MovieMessages.Changed);
        var edit = await setup.Queries.FindForEditAsync(id, TestContext.Current.CancellationToken);
        edit!.Input.Title.ShouldBe("First edit");
        edit.Version.ShouldBe(2);

        var retried = await setup.Commands.SaveAsync(
            id,
            edit.Version,
            Input(title: "Stale edit"),
            TestContext.Current.CancellationToken
        );
        retried.IsSuccess.ShouldBeTrue();
        retried.Value.Version.ShouldBe(3);
    }

    [Fact]
    public async Task Updating_a_missing_movie_is_not_found()
    {
        var setup = await CreateAsync();

        var result = await setup.Commands.SaveAsync(
            MovieId.From(999),
            1,
            Input(),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeFalse();
        result.Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Theory]
    [InlineData("HEAT", "michael mann")]
    [InlineData("Héat", "Michael Mann")]
    [InlineData("Heat", "Michaël Mann")]
    public async Task A_movie_with_the_same_title_year_and_director_is_a_duplicate_regardless_of_case_and_accents(
        string title,
        string director
    )
    {
        var setup = await CreateAsync();
        await CreateMovieAsync(setup, Input());

        var result = await setup.Commands.SaveAsync(
            null,
            1,
            Input(title: title, director: director),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeFalse();
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Fields!["Title"].ShouldBe([MovieMessages.Duplicate]);
        (await MariaDbFixture.ScalarAsync<long>(setup.ConnectionString, "SELECT COUNT(*) FROM movies")).ShouldBe(1);
    }

    [Fact]
    public async Task The_same_title_with_another_year_or_director_is_another_movie()
    {
        var setup = await CreateAsync();
        await CreateMovieAsync(setup, Input());

        await CreateMovieAsync(setup, Input(year: 1996));
        await CreateMovieAsync(setup, Input(director: "Someone Else"));

        (await MariaDbFixture.ScalarAsync<long>(setup.ConnectionString, "SELECT COUNT(*) FROM movies")).ShouldBe(3);
    }

    [Fact]
    public async Task A_movie_is_not_a_duplicate_of_itself()
    {
        var setup = await CreateAsync();
        var id = await CreateMovieAsync(setup, Input());

        var result = await setup.Commands.SaveAsync(id, 1, Input(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        (
            await setup.Queries.FindDuplicateAsync(
                "heat",
                1995,
                ["MICHAEL MANN"],
                null,
                TestContext.Current.CancellationToken
            )
        ).ShouldBe(id);
        (
            await setup.Queries.FindDuplicateAsync(
                "heat",
                1995,
                ["MICHAEL MANN"],
                id,
                TestContext.Current.CancellationToken
            )
        ).ShouldBeNull();
    }

    [Fact]
    public async Task Invalid_input_is_rejected_per_field_before_anything_is_written()
    {
        var setup = await CreateAsync();
        var input = Input(medium: "Bioscoop");
        input.Title = " ";
        input.ReleaseYear = 1700;
        input.OverallScore = 11;

        var result = await setup.Commands.SaveAsync(null, 1, input, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Fields!.Keys.ShouldBe(["Title", "ReleaseYear", "OverallScore"], ignoreOrder: true);
        (await MariaDbFixture.ScalarAsync<long>(setup.ConnectionString, "SELECT COUNT(*) FROM movie_genres")).ShouldBe(
            0
        );
        (await MariaDbFixture.ScalarAsync<long>(setup.ConnectionString, "SELECT COUNT(*) FROM media")).ShouldBe(0);
    }

    [Fact]
    public async Task A_movie_without_scores_is_rejected_and_nothing_is_written()
    {
        var setup = await CreateAsync();
        var input = Input();
        input.OverallScore = null;
        input.StoryScore = null;
        input.PictureScore = null;
        input.MusicScore = null;
        input.SoundScore = null;

        var result = await setup.Commands.SaveAsync(null, 1, input, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Fields!.Keys.ShouldBe(
            ["OverallScore", "StoryScore", "PictureScore", "MusicScore", "SoundScore"],
            ignoreOrder: true
        );
        (await MariaDbFixture.ScalarAsync<long>(setup.ConnectionString, "SELECT COUNT(*) FROM movies")).ShouldBe(0);
    }

    [Fact]
    public async Task Typed_media_match_existing_rows_by_collation_instead_of_creating_new_ones()
    {
        var setup = await CreateAsync();
        await CreateMovieAsync(setup, Input(medium: "Bioscoop"));

        await CreateMovieAsync(setup, Input(title: "Other", medium: "bioscoop"));

        (await MariaDbFixture.ScalarAsync<long>(setup.ConnectionString, "SELECT COUNT(*) FROM media")).ShouldBe(1);
    }

    [Fact]
    public async Task A_movie_is_saved_with_several_genres_matched_by_collation_and_alphabetically_read_back()
    {
        var setup = await CreateAsync();

        var id = await CreateMovieAsync(setup, Input(genres: ["thriller", "ACTIE", "Misdaad"]));

        var edit = await setup.Queries.FindForEditAsync(id, TestContext.Current.CancellationToken);
        edit!.Input.Genres.ShouldBe(["Actie", "Misdaad", "Thriller"]);
        (await MariaDbFixture.ScalarAsync<long>(setup.ConnectionString, "SELECT COUNT(*) FROM movie_genres")).ShouldBe(
            3
        );
        (await MariaDbFixture.ScalarAsync<long>(setup.ConnectionString, "SELECT COUNT(*) FROM genres")).ShouldBe(
            MovieGenres.All.Count
        );
    }

    [Fact]
    public async Task An_unknown_genre_is_rejected_and_nothing_is_written()
    {
        var setup = await CreateAsync();

        var result = await setup.Commands.SaveAsync(
            null,
            1,
            Input(genres: ["Thriller", "Nieuw genre"]),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeFalse();
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.Fields!.Keys.ShouldBe(["Genres"]);
        (await MariaDbFixture.ScalarAsync<long>(setup.ConnectionString, "SELECT COUNT(*) FROM movies")).ShouldBe(0);
        (await MariaDbFixture.ScalarAsync<long>(setup.ConnectionString, "SELECT COUNT(*) FROM movie_genres")).ShouldBe(
            0
        );
        (await MariaDbFixture.ScalarAsync<long>(setup.ConnectionString, "SELECT COUNT(*) FROM genres")).ShouldBe(
            MovieGenres.All.Count
        );
    }

    [Theory]
    [InlineData("Actie", "actie")]
    [InlineData("Sci-fi", "SCI-FI")]
    public async Task The_same_genre_twice_is_rejected(string first, string second)
    {
        var setup = await CreateAsync();

        var result = await setup.Commands.SaveAsync(
            null,
            1,
            Input(genres: [first, second]),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeFalse();
        result.Error.Fields!.Keys.ShouldBe(["Genres"]);
    }

    [Fact]
    public async Task More_than_four_or_zero_genres_are_rejected()
    {
        var setup = await CreateAsync();

        var tooMany = await setup.Commands.SaveAsync(
            null,
            1,
            Input(genres: ["Actie", "Drama", "Horror", "Komedie", "Western"]),
            TestContext.Current.CancellationToken
        );
        var none = await setup.Commands.SaveAsync(null, 1, Input(genres: []), TestContext.Current.CancellationToken);

        tooMany.Error.Fields!.Keys.ShouldBe(["Genres"]);
        none.Error.Fields!.Keys.ShouldBe(["Genres"]);
        (await MariaDbFixture.ScalarAsync<long>(setup.ConnectionString, "SELECT COUNT(*) FROM movies")).ShouldBe(0);
    }

    [Fact]
    public async Task Updating_replaces_the_genres_and_a_genre_only_change_bumps_the_version()
    {
        var setup = await CreateAsync();
        var id = await CreateMovieAsync(setup, Input(genres: ["Actie", "Drama"]));

        var result = await setup.Commands.SaveAsync(
            id,
            1,
            Input(genres: ["Drama", "Komedie", "Western"]),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe(2);
        var edit = await setup.Queries.FindForEditAsync(id, TestContext.Current.CancellationToken);
        edit!.Input.Genres.ShouldBe(["Drama", "Komedie", "Western"]);
        (await MariaDbFixture.ScalarAsync<long>(setup.ConnectionString, "SELECT COUNT(*) FROM movie_genres")).ShouldBe(
            3
        );
    }

    [Fact]
    public async Task A_stale_genre_change_is_a_conflict_and_keeps_the_old_genres()
    {
        var setup = await CreateAsync();
        var id = await CreateMovieAsync(setup, Input(genres: ["Actie"]));
        (
            await setup.Commands.SaveAsync(id, 1, Input(genres: ["Drama"]), TestContext.Current.CancellationToken)
        ).IsSuccess.ShouldBeTrue();

        var stale = await setup.Commands.SaveAsync(
            id,
            1,
            Input(genres: ["Horror"]),
            TestContext.Current.CancellationToken
        );

        stale.Error.Kind.ShouldBe(ErrorKind.Conflict);
        var edit = await setup.Queries.FindForEditAsync(id, TestContext.Current.CancellationToken);
        edit!.Input.Genres.ShouldBe(["Drama"]);
    }

    [Fact]
    public async Task Concurrent_saves_with_the_same_new_medium_create_it_once()
    {
        var setup = await CreateAsync();

        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 6)
                .Select(i =>
                    setup.Commands.SaveAsync(
                        null,
                        1,
                        Input(title: $"Movie {i}", medium: "Nieuw medium"),
                        TestContext.Current.CancellationToken
                    )
                )
        );

        results.ShouldAllBe(r => r.IsSuccess);
        (await MariaDbFixture.ScalarAsync<long>(setup.ConnectionString, "SELECT COUNT(*) FROM movies")).ShouldBe(6);
        (await MariaDbFixture.ScalarAsync<long>(setup.ConnectionString, "SELECT COUNT(*) FROM media")).ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_saves_of_the_same_movie_create_it_only_once()
    {
        var setup = await CreateAsync();

        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 4)
                .Select(_ => setup.Commands.SaveAsync(null, 1, Input(), TestContext.Current.CancellationToken))
        );

        results.Count(r => r.IsSuccess).ShouldBe(1);
        results.Where(r => !r.IsSuccess).ShouldAllBe(r => r.Error.Kind == ErrorKind.Validation);
        (await MariaDbFixture.ScalarAsync<long>(setup.ConnectionString, "SELECT COUNT(*) FROM movies")).ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_updates_of_the_same_version_succeed_only_once()
    {
        var setup = await CreateAsync();
        var id = await CreateMovieAsync(setup, Input());

        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 4)
                .Select(i =>
                    setup.Commands.SaveAsync(id, 1, Input(title: $"Edit {i}"), TestContext.Current.CancellationToken)
                )
        );

        results.Count(r => r.IsSuccess).ShouldBe(1);
        results.Where(r => !r.IsSuccess).ShouldAllBe(r => r.Error.Kind == ErrorKind.Conflict);
        (await MariaDbFixture.ScalarAsync<int>(setup.ConnectionString, "SELECT version FROM movies")).ShouldBe(2);
    }

    [Fact]
    public async Task Media_suggestions_contain_the_text_alphabetically_and_treat_wildcards_literally()
    {
        var setup = await CreateAsync();
        await CreateMovieAsync(setup, Input(title: "A", medium: "Bioscoop"));
        await CreateMovieAsync(setup, Input(title: "B", medium: "Bioscoop"));
        await CreateMovieAsync(setup, Input(title: "C", medium: "Netflix"));
        await CreateMovieAsync(setup, Input(title: "D", medium: "dvd"));
        await CreateMovieAsync(setup, Input(title: "E", medium: "100%_Disc"));

        var media = await setup.Queries.SuggestMediaAsync("o", 10, TestContext.Current.CancellationToken);
        var allMedia = await setup.Queries.SuggestMediaAsync("", 10, TestContext.Current.CancellationToken);
        var percent = await setup.Queries.SuggestMediaAsync("%", 10, TestContext.Current.CancellationToken);
        var underscore = await setup.Queries.SuggestMediaAsync("_", 10, TestContext.Current.CancellationToken);
        var limited = await setup.Queries.SuggestMediaAsync("", 2, TestContext.Current.CancellationToken);

        media.ShouldBe(["Bioscoop"]);
        allMedia.ShouldBe(["100%_Disc", "Bioscoop", "dvd", "Netflix"]);
        percent.ShouldBe(["100%_Disc"]);
        underscore.ShouldBe(["100%_Disc"]);
        limited.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Movies_with_the_same_title_and_year_but_another_director_are_found()
    {
        var setup = await CreateAsync();
        var first = await CreateMovieAsync(setup, Input(title: "Crash", year: 1996, director: "David Cronenberg"));
        var second = await CreateMovieAsync(setup, Input(title: "Crash", year: 1996, director: "Someone Else"));
        await CreateMovieAsync(setup, Input(title: "Crash", year: 2004, director: "Paul Haggis"));
        await CreateMovieAsync(setup, Input(title: "Other", year: 1996, director: "Nobody"));
        var ct = TestContext.Current.CancellationToken;

        var found = await setup.Queries.FindSameTitleAndYearAsync("CRASH", 1996, ["Paul Haggis"], null, 5, ct);
        var sameDirector = await setup.Queries.FindSameTitleAndYearAsync(
            "crash",
            1996,
            ["david cronenberg"],
            null,
            5,
            ct
        );
        var excludingOwn = await setup.Queries.FindSameTitleAndYearAsync("Crash", 1996, ["Paul Haggis"], second, 5, ct);
        var limited = await setup.Queries.FindSameTitleAndYearAsync("Crash", 1996, ["Paul Haggis"], null, 1, ct);
        var none = await setup.Queries.FindSameTitleAndYearAsync("Unknown", 1996, ["Paul Haggis"], null, 5, ct);

        found.Select(m => m.Id).ShouldBe([first, second]);
        sameDirector.Select(m => m.Id).ShouldBe([second]);
        excludingOwn.Select(m => m.Id).ShouldBe([first]);
        limited.Count.ShouldBe(1);
        none.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_director_is_saved_with_the_spelling_that_other_movies_already_use()
    {
        var setup = await CreateAsync();
        await CreateMovieAsync(setup, Input(title: "A", director: "Jean-Pierre Jeunet"));
        await CreateMovieAsync(setup, Input(title: "B", director: "Jean-Pierre Jeunet"));
        await CreateMovieAsync(setup, Input(title: "C", director: "Jean-Pierre JEUNET"));

        var other = await CreateMovieAsync(setup, Input(title: "D", director: "jean-pierre jeunet"));
        var accented = await CreateMovieAsync(setup, Input(title: "E", director: "Jéan-Pierre Jeunet"));
        var different = await CreateMovieAsync(setup, Input(title: "F", director: "Pierre Morel"));

        (await DirectorOfAsync(setup, other)).ShouldBe("Jean-Pierre Jeunet");
        (await DirectorOfAsync(setup, accented)).ShouldBe("Jean-Pierre Jeunet");
        (await DirectorOfAsync(setup, different)).ShouldBe("Pierre Morel");
    }

    [Fact]
    public async Task The_spelling_of_a_director_can_be_corrected_when_no_other_movie_uses_it()
    {
        var setup = await CreateAsync();
        var id = await CreateMovieAsync(setup, Input(title: "A", director: "jean-pierre jeunet"));

        var result = await setup.Commands.SaveAsync(
            id,
            1,
            Input(title: "A", director: "Jean-Pierre Jeunet"),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeTrue();
        (await DirectorOfAsync(setup, id)).ShouldBe("Jean-Pierre Jeunet");
    }

    [Fact]
    public async Task An_update_takes_over_the_spelling_of_the_other_movies()
    {
        var setup = await CreateAsync();
        await CreateMovieAsync(setup, Input(title: "A", director: "Jean-Pierre Jeunet"));
        var id = await CreateMovieAsync(setup, Input(title: "B", director: "Someone Else"));

        var result = await setup.Commands.SaveAsync(
            id,
            1,
            Input(title: "B", director: "JEAN-PIERRE JEUNET"),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeTrue();
        (await DirectorOfAsync(setup, id)).ShouldBe("Jean-Pierre Jeunet");
    }

    private static async Task<string[]> DirectorsOfAsync(Setup setup, MovieId id) =>
        [.. (await setup.Queries.FindForEditAsync(id, TestContext.Current.CancellationToken))!.Input.Directors];

    private static async Task<string> DirectorOfAsync(Setup setup, MovieId id) =>
        (await DirectorsOfAsync(setup, id)).Single();

    private static Task<long> CountAsync(Setup setup, string table) =>
        MariaDbFixture.ScalarAsync<long>(setup.ConnectionString, $"SELECT COUNT(*) FROM {table}");

    [Fact]
    public async Task Director_suggestions_are_distinct_by_collation_alphabetical_and_treat_wildcards_literally()
    {
        var setup = await CreateAsync();
        await CreateMovieAsync(setup, Input(title: "A", director: "Jean-Pierre Jeunet"));
        await CreateMovieAsync(setup, Input(title: "B", director: "jean-pierre jeunet"));
        await CreateMovieAsync(setup, Input(title: "C", director: "Pierre Morel"));
        await CreateMovieAsync(setup, Input(title: "D", director: "Joe 100%_Smith"));

        var all = await setup.Queries.SuggestDirectorsAsync("", 10, TestContext.Current.CancellationToken);
        var pierre = await setup.Queries.SuggestDirectorsAsync("PIERRE", 10, TestContext.Current.CancellationToken);
        var percent = await setup.Queries.SuggestDirectorsAsync("%", 10, TestContext.Current.CancellationToken);
        var underscore = await setup.Queries.SuggestDirectorsAsync("_", 10, TestContext.Current.CancellationToken);
        var limited = await setup.Queries.SuggestDirectorsAsync("", 2, TestContext.Current.CancellationToken);

        all.ShouldBe(["Jean-Pierre Jeunet", "Joe 100%_Smith", "Pierre Morel"]);
        pierre.ShouldBe(["Jean-Pierre Jeunet", "Pierre Morel"]);
        percent.ShouldBe(["Joe 100%_Smith"]);
        underscore.ShouldBe(["Joe 100%_Smith"]);
        limited.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_movie_with_several_directors_keeps_their_order_and_a_reordering_is_a_change()
    {
        var setup = await CreateAsync();
        var id = await CreateMovieAsync(setup, Input(title: "Fargo", director: "Joel Coen|Ethan Coen"));
        (await DirectorsOfAsync(setup, id)).ShouldBe(["Joel Coen", "Ethan Coen"]);

        var result = await setup.Commands.SaveAsync(
            id,
            1,
            Input(title: "Fargo", director: "Ethan Coen|Joel Coen"),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeTrue();
        result.Value.Version.ShouldBe(2);
        (await DirectorsOfAsync(setup, id)).ShouldBe(["Ethan Coen", "Joel Coen"]);
        (await CountAsync(setup, "directors")).ShouldBe(2);
    }

    [Fact]
    public async Task The_same_directors_in_another_order_are_a_duplicate()
    {
        var setup = await CreateAsync();
        await CreateMovieAsync(setup, Input(title: "Fargo", director: "Joel Coen|Ethan Coen"));

        var result = await setup.Commands.SaveAsync(
            null,
            1,
            Input(title: "fargo", director: "ethan coen|JOEL COEN"),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeFalse();
        result.Error.Fields!["Title"].ShouldBe([MovieMessages.Duplicate]);
        (await CountAsync(setup, "movies")).ShouldBe(1);
        (await CountAsync(setup, "directors")).ShouldBe(2);
    }

    [Fact]
    public async Task A_subset_or_superset_of_the_directors_is_another_movie()
    {
        var setup = await CreateAsync();
        await CreateMovieAsync(setup, Input(title: "Fargo", director: "Joel Coen|Ethan Coen"));

        await CreateMovieAsync(setup, Input(title: "Fargo", director: "Joel Coen"));
        await CreateMovieAsync(setup, Input(title: "Fargo", director: "Joel Coen|Ethan Coen|Someone Else"));

        (await CountAsync(setup, "movies")).ShouldBe(3);
    }

    [Fact]
    public async Task A_director_shared_by_movies_is_stored_once_and_orphans_are_removed_when_unlinked()
    {
        var setup = await CreateAsync();
        var first = await CreateMovieAsync(setup, Input(title: "A", director: "Shared|Only A"));
        await CreateMovieAsync(setup, Input(title: "B", director: "SHARED"));
        (await CountAsync(setup, "directors")).ShouldBe(2);
        (await CountAsync(setup, "movie_directors")).ShouldBe(3);

        var result = await setup.Commands.SaveAsync(
            first,
            1,
            Input(title: "A", director: "Shared"),
            TestContext.Current.CancellationToken
        );

        result.IsSuccess.ShouldBeTrue();
        (await CountAsync(setup, "directors")).ShouldBe(1);
        (await CountAsync(setup, "movie_directors")).ShouldBe(2);
    }

    [Fact]
    public async Task A_list_only_change_of_the_directors_bumps_the_version_and_a_stale_version_conflicts()
    {
        var setup = await CreateAsync();
        var id = await CreateMovieAsync(setup, Input(director: "A|B"));
        await setup.Commands.SaveAsync(id, 1, Input(director: "B|A"), TestContext.Current.CancellationToken);

        var stale = await setup.Commands.SaveAsync(
            id,
            1,
            Input(director: "A|B"),
            TestContext.Current.CancellationToken
        );

        stale.IsSuccess.ShouldBeFalse();
        stale.Error.Kind.ShouldBe(ErrorKind.Conflict);
    }

    [Fact]
    public async Task Concurrent_saves_with_the_same_new_director_create_it_once()
    {
        var setup = await CreateAsync();

        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 6)
                .Select(i =>
                    setup.Commands.SaveAsync(
                        null,
                        1,
                        Input(title: $"Movie {i}", director: "Race Director"),
                        TestContext.Current.CancellationToken
                    )
                )
        );

        results.ShouldAllBe(r => r.IsSuccess);
        (await CountAsync(setup, "movies")).ShouldBe(6);
        (await CountAsync(setup, "directors")).ShouldBe(1);
        (await CountAsync(setup, "movie_directors")).ShouldBe(6);
    }

    [Fact]
    public async Task Unlinking_a_director_while_other_saves_link_it_never_leaves_a_dangling_link()
    {
        var setup = await CreateAsync();
        var id = await CreateMovieAsync(setup, Input(title: "Holder", director: "Race Director"));

        var saves = Enumerable
            .Range(0, 5)
            .Select(i =>
                setup.Commands.SaveAsync(
                    null,
                    1,
                    Input(title: $"Linker {i}", director: "Race Director"),
                    TestContext.Current.CancellationToken
                )
            )
            .Append(
                setup.Commands.SaveAsync(
                    id,
                    1,
                    Input(title: "Holder", director: "Other Director"),
                    TestContext.Current.CancellationToken
                )
            );
        var results = await Task.WhenAll(saves);

        results.ShouldAllBe(r => r.IsSuccess);
        (await CountAsync(setup, "movies")).ShouldBe(6);
        (await CountAsync(setup, "movie_directors")).ShouldBe(6);
        (
            await MariaDbFixture.ScalarAsync<long>(
                setup.ConnectionString,
                "SELECT COUNT(*) FROM directors d WHERE NOT EXISTS (SELECT 1 FROM movie_directors md WHERE md.director_id = d.id)"
            )
        ).ShouldBe(0);
    }

    [Fact]
    public async Task The_directors_key_is_the_sorted_ids_whatever_the_order_entered()
    {
        var setup = await CreateAsync();
        var random = new Random(42);
        var names = Enumerable.Range(1, 8).Select(i => $"Director {i}").ToList();
        await CreateMovieAsync(setup, Input(title: "Seed 1", director: string.Join('|', names.Take(4))));
        await CreateMovieAsync(setup, Input(title: "Seed 2", director: string.Join('|', names.Skip(4))));

        for (var round = 0; round < 5; round++)
        {
            var count = random.Next(1, 6);
            var subset = names.OrderBy(_ => random.Next()).Take(count).ToList();
            var title = $"Film {round}";
            var id = await CreateMovieAsync(setup, Input(title: title, director: string.Join('|', subset)));

            await using var db = await setup.Factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
            var movie = await db
                .Movies.AsNoTracking()
                .Include(m => m.DirectorLinks)
                .SingleAsync(m => m.Id == id, TestContext.Current.CancellationToken);
            movie.DirectorsKey.ShouldBe(DirectorsKey.Of(movie.DirectorLinks.Select(l => l.DirectorId)));
            movie.DirectorLinks.Select(l => l.Position).Order().ShouldBe(Enumerable.Range(1, count));
            movie.DirectorsKey.Split(',').Length.ShouldBe(count);
        }
    }
}
