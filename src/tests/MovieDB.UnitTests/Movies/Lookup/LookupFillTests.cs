using MovieDB.Common;
using MovieDB.Infrastructure;
using MovieDB.Movies;
using MovieDB.Movies.Lookup;
using Shouldly;

namespace MovieDB.UnitTests.Movies.Lookup;

public sealed class LookupFillTests
{
    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    }

    private static readonly UserClock Clock = new(new FixedTimeProvider(), UserClock.ResolveZone("Europe/Brussels"));

    private static LookupDetails Details(
        string title = "Heat",
        int? year = 1995,
        int? runtime = 170,
        string[]? genres = null,
        string[]? directors = null
    ) =>
        new(
            title,
            year,
            runtime,
            genres ?? ["Thriller", "Misdaad"],
            directors ?? ["Michael Mann"],
            new Uri("https://www.themoviedb.org/movie/949"),
            null
        );

    [Fact]
    public void Fills_all_five_fields_without_notices()
    {
        var input = new MovieInput();

        var notices = LookupFill.Apply(input, Details(), Clock);

        notices.ShouldBeEmpty();
        input.Title.ShouldBe("Heat");
        input.Genres.ShouldBe(["Thriller", "Misdaad"]);
        input.RuntimeMinutes.ShouldBe(170);
        input.ReleaseYear.ShouldBe(1995);
        input.Directors.ShouldBe(["Michael Mann"]);
    }

    [Fact]
    public void Touches_only_the_five_lookup_fields()
    {
        var input = new MovieInput
        {
            Title = "Old",
            WatchedOn = new DateOnly(2026, 1, 2),
            Medium = "Bioscoop",
            Review = "Mooi",
            OverallScore = 8,
            StoryScore = 7,
            PictureScore = 6,
            MusicScore = 5,
            SoundScore = 4,
        };

        LookupFill.Apply(input, Details(), Clock);

        input.WatchedOn.ShouldBe(new DateOnly(2026, 1, 2));
        input.Medium.ShouldBe("Bioscoop");
        input.Review.ShouldBe("Mooi");
        (input.OverallScore, input.StoryScore, input.PictureScore, input.MusicScore, input.SoundScore).ShouldBe(
            (8, 7, 6, 5, 4)
        );
    }

    [Fact]
    public void Overwrites_existing_values()
    {
        var input = new MovieInput
        {
            Title = "Old",
            Genres = ["Western"],
            RuntimeMinutes = 5,
            ReleaseYear = 2000,
            Directors = ["Someone"],
        };

        LookupFill.Apply(input, Details(), Clock);

        input.Title.ShouldBe("Heat");
        input.Genres.ShouldBe(["Thriller", "Misdaad"]);
        input.RuntimeMinutes.ShouldBe(170);
        input.ReleaseYear.ShouldBe(1995);
        input.Directors.ShouldBe(["Michael Mann"]);
    }

    [Fact]
    public void Title_is_trimmed_and_cut_at_the_maximum()
    {
        var input = new MovieInput();

        LookupFill.Apply(input, Details(title: "  " + new string('x', 250) + "  "), Clock);

        input.Title!.Length.ShouldBe(MovieInput.TitleMaxLength);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1887)]
    [InlineData(2032)]
    public void Year_outside_the_accepted_range_is_left_empty_with_a_notice(int? year)
    {
        var input = new MovieInput { ReleaseYear = 1990 };

        var notices = LookupFill.Apply(input, Details(year: year), Clock);

        input.ReleaseYear.ShouldBeNull();
        notices.ShouldBe([LookupMessages.YearMissing]);
    }

    [Theory]
    [InlineData(1888)]
    [InlineData(2031)]
    public void Year_boundaries_are_accepted(int year)
    {
        var input = new MovieInput();

        LookupFill.Apply(input, Details(year: year), Clock).ShouldBeEmpty();

        input.ReleaseYear.ShouldBe(year);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1000)]
    public void Runtime_outside_the_accepted_range_is_left_empty_with_a_notice(int? runtime)
    {
        var input = new MovieInput { RuntimeMinutes = 90 };

        var notices = LookupFill.Apply(input, Details(runtime: runtime), Clock);

        input.RuntimeMinutes.ShouldBeNull();
        notices.ShouldBe([LookupMessages.RuntimeMissing]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(999)]
    public void Runtime_boundaries_are_accepted(int runtime)
    {
        var input = new MovieInput();

        LookupFill.Apply(input, Details(runtime: runtime), Clock).ShouldBeEmpty();

        input.RuntimeMinutes.ShouldBe(runtime);
    }

    [Fact]
    public void Genres_are_deduplicated_ignoring_case_and_capped_at_four()
    {
        var input = new MovieInput();

        var notices = LookupFill.Apply(
            input,
            Details(genres: ["Actie", "actie", "Drama", "Horror", "Western", "Thriller"]),
            Clock
        );

        input.Genres.ShouldBe(["Actie", "Drama", "Horror", "Western"]);
        notices.ShouldBeEmpty();
    }

    [Fact]
    public void No_genre_leaves_the_field_empty_with_a_notice()
    {
        var input = new MovieInput { Genres = ["Western"] };

        var notices = LookupFill.Apply(input, Details(genres: []), Clock);

        input.Genres.ShouldBeEmpty();
        notices.ShouldBe([LookupMessages.GenreMissing]);
    }

    [Fact]
    public void Directors_are_cleaned_deduplicated_and_empties_dropped()
    {
        var input = new MovieInput();

        LookupFill.Apply(
            input,
            Details(directors: ["  Michael   Mann ", "michael mann", "Mann, Michael", "", "   ", "Lana, Wachowski"]),
            Clock
        );

        input.Directors.ShouldBe(["Michael Mann", "Mann Michael", "Lana Wachowski"]);
    }

    [Fact]
    public void Director_names_are_cut_at_the_maximum()
    {
        var input = new MovieInput();

        LookupFill.Apply(input, Details(directors: [new string('x', 200)]), Clock);

        input.Directors.Single().Length.ShouldBe(MovieInput.DirectorMaxLength);
    }

    [Fact]
    public void More_than_five_directors_keeps_the_first_five_with_a_notice()
    {
        var input = new MovieInput();

        var notices = LookupFill.Apply(input, Details(directors: ["A1", "B2", "C3", "D4", "E5", "F6"]), Clock);

        input.Directors.ShouldBe(["A1", "B2", "C3", "D4", "E5"]);
        notices.ShouldBe([LookupMessages.TooManyDirectors]);
    }

    [Fact]
    public void Exactly_five_directors_gives_no_notice()
    {
        var input = new MovieInput();

        LookupFill.Apply(input, Details(directors: ["A1", "B2", "C3", "D4", "E5"]), Clock).ShouldBeEmpty();
    }

    [Fact]
    public void No_director_leaves_the_field_empty_with_a_notice()
    {
        var input = new MovieInput { Directors = ["Someone"] };

        var notices = LookupFill.Apply(input, Details(directors: []), Clock);

        input.Directors.ShouldBeEmpty();
        notices.ShouldBe([LookupMessages.DirectorMissing]);
    }

    [Fact]
    public void Notices_follow_the_form_field_order()
    {
        var input = new MovieInput();

        var notices = LookupFill.Apply(input, Details(year: null, runtime: null, genres: [], directors: []), Clock);

        notices.ShouldBe([
            LookupMessages.GenreMissing,
            LookupMessages.RuntimeMissing,
            LookupMessages.YearMissing,
            LookupMessages.DirectorMissing,
        ]);
    }

    [Fact]
    public void HasInputBesidesTitle_ignores_the_title_and_other_fields()
    {
        LookupFill
            .HasInputBesidesTitle(
                new MovieInput
                {
                    Title = "Heat",
                    Review = "x",
                    OverallScore = 5,
                }
            )
            .ShouldBeFalse();
        LookupFill.HasInputBesidesTitle(new MovieInput { Genres = ["Drama"] }).ShouldBeTrue();
        LookupFill.HasInputBesidesTitle(new MovieInput { RuntimeMinutes = 90 }).ShouldBeTrue();
        LookupFill.HasInputBesidesTitle(new MovieInput { ReleaseYear = 1990 }).ShouldBeTrue();
        LookupFill.HasInputBesidesTitle(new MovieInput { Directors = ["A"] }).ShouldBeTrue();
    }
}
