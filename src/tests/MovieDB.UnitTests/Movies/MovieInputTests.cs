using MovieDB.Common;
using MovieDB.Infrastructure;
using MovieDB.Movies;
using Shouldly;

namespace MovieDB.UnitTests.Movies;

public sealed class MovieInputTests
{
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>2026-03-10 23:30 UTC is already 2026-03-11 in Brussels.</summary>
    private static readonly UserClock Clock = new(
        new FixedTimeProvider(new DateTimeOffset(2026, 3, 10, 23, 30, 0, TimeSpan.Zero)),
        UserClock.ResolveZone("Europe/Brussels")
    );

    private static readonly MovieInputValidator Validator = new(Clock);

    private static MovieInput Valid() =>
        new()
        {
            Title = "Heat",
            Genres = ["Thriller"],
            Directors = ["Michael Mann"],
            ReleaseYear = 1995,
            RuntimeMinutes = 170,
            WatchedOn = new DateOnly(2026, 3, 11),
            OverallScore = 8,
            StoryScore = 7,
            PictureScore = 6,
            MusicScore = 5,
            SoundScore = 4,
        };

    private static string[] ErrorsFor(MovieInput input, string property) =>
        [.. Validator.Validate(input).Errors.Where(e => e.PropertyName == property).Select(e => e.ErrorMessage)];

    [Fact]
    public void A_complete_input_is_valid()
    {
        Validator.Validate(Valid()).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Required_values_have_Dutch_messages()
    {
        var result = Validator.Validate(new MovieInput());

        result
            .Errors.ToDictionary(e => e.PropertyName, e => e.ErrorMessage)
            .ShouldBe(
                new Dictionary<string, string>
                {
                    ["Title"] = "Filmtitel is verplicht.",
                    ["Genres"] = "Kies minstens één genre.",
                    ["Directors"] = "Geef minstens één regisseur op.",
                    ["RuntimeMinutes"] = "Duur is verplicht.",
                    ["ReleaseYear"] = "Jaar is verplicht.",
                    ["WatchedOn"] = "Datum is verplicht.",
                    ["OverallScore"] = "Totaal is verplicht.",
                    ["StoryScore"] = "Film is verplicht.",
                    ["PictureScore"] = "Beeld is verplicht.",
                    ["MusicScore"] = "Muziek is verplicht.",
                    ["SoundScore"] = "Geluid is verplicht.",
                },
                ignoreOrder: true
            );
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(999, true)]
    [InlineData(1000, false)]
    public void The_runtime_is_between_1_and_999(int minutes, bool valid)
    {
        var input = Valid();
        input.RuntimeMinutes = minutes;

        ErrorsFor(input, "RuntimeMinutes").Length.ShouldBe(valid ? 0 : 1);
    }

    [Theory]
    [InlineData(1887, false)]
    [InlineData(1888, true)]
    [InlineData(2031, true)]
    [InlineData(2032, false)]
    public void The_year_runs_from_1888_to_five_years_after_the_current_year(int year, bool valid)
    {
        var input = Valid();
        input.ReleaseYear = year;

        ErrorsFor(input, "ReleaseYear").Length.ShouldBe(valid ? 0 : 1);
    }

    [Fact]
    public void The_year_message_names_the_limits_in_the_owners_time_zone()
    {
        var input = Valid();
        input.ReleaseYear = 3000;

        ErrorsFor(input, "ReleaseYear").ShouldBe(["Jaar moet tussen 1888 en 2031 liggen."]);
    }

    [Theory]
    [InlineData("1899-12-31", false)]
    [InlineData("1900-01-01", true)]
    [InlineData("2026-03-12", true)]
    [InlineData("2026-03-13", false)]
    public void The_date_runs_from_1900_to_tomorrow_in_the_owners_time_zone(string date, bool valid)
    {
        var input = Valid();
        input.WatchedOn = DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);

        ErrorsFor(input, "WatchedOn").Length.ShouldBe(valid ? 0 : 1);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(10, true)]
    [InlineData(0, false)]
    [InlineData(11, false)]
    [InlineData(-1, false)]
    public void Scores_are_between_1_and_10(int score, bool valid)
    {
        var input = Valid();
        input.OverallScore = score;
        input.StoryScore = score;
        input.PictureScore = score;
        input.MusicScore = score;
        input.SoundScore = score;

        var errors = Validator.Validate(input).Errors;

        errors.Count.ShouldBe(valid ? 0 : 5);
        errors.ShouldAllBe(e => e.ErrorMessage == "Kies een score van 1 tot 10.");
    }

    [Theory]
    [InlineData("OverallScore", "Totaal is verplicht.")]
    [InlineData("StoryScore", "Film is verplicht.")]
    [InlineData("PictureScore", "Beeld is verplicht.")]
    [InlineData("MusicScore", "Muziek is verplicht.")]
    [InlineData("SoundScore", "Geluid is verplicht.")]
    public void Every_score_is_required(string property, string message)
    {
        var input = Valid();
        switch (property)
        {
            case "OverallScore":
                input.OverallScore = null;
                break;
            case "StoryScore":
                input.StoryScore = null;
                break;
            case "PictureScore":
                input.PictureScore = null;
                break;
            case "MusicScore":
                input.MusicScore = null;
                break;
            default:
                input.SoundScore = null;
                break;
        }

        ErrorsFor(input, property).ShouldBe([message]);
    }

    [Fact]
    public void Text_lengths_are_limited()
    {
        var input = Valid();
        input.Title = new string('a', MovieInput.TitleMaxLength + 1);
        input.Directors = [new string('a', MovieInput.DirectorMaxLength + 1)];
        input.Genres = [new string('a', MovieInput.LookupMaxLength + 1)];
        input.Medium = new string('a', MovieInput.LookupMaxLength + 1);
        input.Review = new string('a', MovieInput.ReviewMaxLength + 1);

        Validator
            .Validate(input)
            .Errors.Select(e => e.PropertyName)
            .ShouldBe(["Title", "Genres", "Directors", "Medium", "Review"], ignoreOrder: true);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    public void A_movie_has_at_most_five_directors(int count, bool valid)
    {
        var input = Valid();
        input.Directors = [.. Enumerable.Range(1, count).Select(i => $"Director {i}")];

        ErrorsFor(input, "Directors").ShouldBe(valid ? [] : ["Maximaal 5 regisseurs."]);
    }

    [Fact]
    public void A_movie_needs_at_least_one_director()
    {
        var input = Valid();
        input.Directors = [];

        ErrorsFor(input, "Directors").ShouldBe(["Geef minstens één regisseur op."]);
    }

    [Fact]
    public void A_director_cannot_contain_a_comma_or_be_chosen_twice()
    {
        var input = Valid();
        input.Directors = ["Coen, Joel"];
        ErrorsFor(input, "Directors").ShouldBe(["Een regisseur mag geen komma bevatten."]);

        input.Directors = ["Joel Coen", "joel coen"];
        ErrorsFor(input, "Directors").ShouldBe(["Een regisseur komt maar één keer voor."]);
    }

    [Fact]
    public void Directors_are_compared_in_order_and_normalized()
    {
        var first = Valid();
        first.Directors = ["A", "B"];
        var reversed = Valid();
        reversed.Directors = ["B", "A"];
        var padded = Valid();
        padded.Directors = [" A ", "", "B"];

        first.SameAs(reversed).ShouldBeFalse();
        first.SameAs(padded.Normalized()).ShouldBeTrue();
    }

    [Fact]
    public void A_movie_needs_at_least_one_genre()
    {
        var input = Valid();
        input.Genres = [];

        ErrorsFor(input, "Genres").ShouldBe(["Kies minstens één genre."]);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void A_movie_has_at_most_four_genres(int count, bool valid)
    {
        var input = Valid();
        input.Genres = [.. MovieGenres.All.Take(count).Select(g => g.Name)];

        ErrorsFor(input, "Genres").ShouldBe(valid ? [] : ["Kies maximaal 4 genres."]);
    }

    [Fact]
    public void A_genre_cannot_be_chosen_twice()
    {
        var input = Valid();
        input.Genres = ["Thriller", "thriller"];

        ErrorsFor(input, "Genres").ShouldBe(["Een genre komt maar één keer voor."]);
    }

    [Fact]
    public void Normalizing_drops_empty_genres_and_trims_the_others()
    {
        var input = Valid();
        input.Genres = [" Actie ", "  ", "Drama"];

        input.Normalized().Genres.ShouldBe(["Actie", "Drama"]);
    }

    [Fact]
    public void The_genres_are_compared_as_an_unordered_set()
    {
        var first = Valid();
        first.Genres = ["Actie", "Drama"];
        var second = Valid();
        second.Genres = ["Drama", "Actie"];
        var third = Valid();
        third.Genres = ["Drama"];

        first.SameAs(second).ShouldBeTrue();
        first.SameAs(third).ShouldBeFalse();
    }

    [Fact]
    public void Text_at_the_maximum_length_is_valid()
    {
        var input = Valid();
        input.Title = new string('a', MovieInput.TitleMaxLength);
        input.Review = new string('a', MovieInput.ReviewMaxLength);

        Validator.Validate(input).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Normalizing_trims_text_and_turns_empty_optional_text_into_null()
    {
        var input = Valid();
        input.Title = "  Heat ";
        input.Medium = "   ";
        input.Review = "  text ";

        var normalized = input.Normalized();

        normalized.Title.ShouldBe("Heat");
        normalized.Medium.ShouldBeNull();
        normalized.Review.ShouldBe("text");
        input.Title.ShouldBe("  Heat ");
    }

    [Fact]
    public void SameAs_compares_every_value()
    {
        var input = Valid();
        input.OverallScore = 7;

        var copy = input.Clone();

        copy.SameAs(input).ShouldBeTrue();
        copy.SoundScore = 3;
        copy.SameAs(input).ShouldBeFalse();
    }
}
