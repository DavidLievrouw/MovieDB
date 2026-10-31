using MovieDB.Common;
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
            Genre = "Thriller",
            Director = "Michael Mann",
            ReleaseYear = 1995,
            RuntimeMinutes = 170,
            WatchedOn = new DateOnly(2026, 3, 11),
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
                    ["Genre"] = "Genre is verplicht.",
                    ["Director"] = "Regisseur is verplicht.",
                    ["RuntimeMinutes"] = "Duur is verplicht.",
                    ["ReleaseYear"] = "Jaar is verplicht.",
                    ["WatchedOn"] = "Datum is verplicht.",
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
    [InlineData(null, true)]
    [InlineData(1, true)]
    [InlineData(10, true)]
    [InlineData(0, false)]
    [InlineData(11, false)]
    [InlineData(-1, false)]
    public void Scores_are_empty_or_between_1_and_10(int? score, bool valid)
    {
        var input = Valid();
        input.OverallScore = score;
        input.StoryScore = score;
        input.PictureScore = score;
        input.MusicScore = score;
        input.SoundScore = score;
        input.ExtrasScore = score;

        Validator.Validate(input).Errors.Count.ShouldBe(valid ? 0 : 6);
    }

    [Fact]
    public void Text_lengths_are_limited()
    {
        var input = Valid();
        input.Title = new string('a', MovieInput.TitleMaxLength + 1);
        input.Director = new string('a', MovieInput.DirectorMaxLength + 1);
        input.Genre = new string('a', MovieInput.LookupMaxLength + 1);
        input.Medium = new string('a', MovieInput.LookupMaxLength + 1);
        input.Review = new string('a', MovieInput.ReviewMaxLength + 1);

        Validator
            .Validate(input)
            .Errors.Select(e => e.PropertyName)
            .ShouldBe(["Title", "Genre", "Director", "Medium", "Review"], ignoreOrder: true);
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
        copy.ExtrasScore = 3;
        copy.SameAs(input).ShouldBeFalse();
    }
}
