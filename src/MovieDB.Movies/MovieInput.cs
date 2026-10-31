using FluentValidation;
using MovieDB.Common;

namespace MovieDB.Movies;

/// <summary>The Dutch messages of the edit page and the save command.</summary>
internal static class MovieMessages
{
    /// <summary>Shown when another movie has the same title, year and director.</summary>
    public const string Duplicate = "Deze film bestaat al (Jaar en Regisseur zijn gelijk).";

    /// <summary>Shown when the movie was changed by someone else since it was loaded.</summary>
    public const string Changed = "Deze film is intussen gewijzigd.";

    /// <summary>Shown when the movie no longer exists.</summary>
    public const string NotFound = "Film niet gevonden.";
}

/// <summary>The editable values of a movie, as typed in the form.</summary>
internal sealed class MovieInput
{
    /// <summary>The longest title.</summary>
    public const int TitleMaxLength = 200;

    /// <summary>The longest director.</summary>
    public const int DirectorMaxLength = 150;

    /// <summary>The longest genre or medium name.</summary>
    public const int LookupMaxLength = 50;

    /// <summary>The longest review.</summary>
    public const int ReviewMaxLength = 20_000;

    /// <summary>The earliest accepted release year.</summary>
    public const int MinYear = 1888;

    /// <summary>The number of years after the current year that are still accepted as a release year.</summary>
    public const int FutureYears = 5;

    /// <summary>The title.</summary>
    public string? Title { get; set; }

    /// <summary>The genre name.</summary>
    public string? Genre { get; set; }

    /// <summary>The runtime in minutes.</summary>
    public int? RuntimeMinutes { get; set; }

    /// <summary>The release year.</summary>
    public int? ReleaseYear { get; set; }

    /// <summary>The director.</summary>
    public string? Director { get; set; }

    /// <summary>The viewing date.</summary>
    public DateOnly? WatchedOn { get; set; }

    /// <summary>The medium name, if any.</summary>
    public string? Medium { get; set; }

    /// <summary>The written review, if any.</summary>
    public string? Review { get; set; }

    /// <summary>The overall score, if any.</summary>
    public int? OverallScore { get; set; }

    /// <summary>The story score, if any.</summary>
    public int? StoryScore { get; set; }

    /// <summary>The picture score, if any.</summary>
    public int? PictureScore { get; set; }

    /// <summary>The music score, if any.</summary>
    public int? MusicScore { get; set; }

    /// <summary>The sound score, if any.</summary>
    public int? SoundScore { get; set; }

    /// <summary>The extras score, if any.</summary>
    public int? ExtrasScore { get; set; }

    /// <summary>Creates a copy.</summary>
    /// <returns>An independent copy with the same values.</returns>
    public MovieInput Clone() => (MovieInput)MemberwiseClone();

    /// <summary>Creates a copy with trimmed text; an empty medium or review becomes <c>null</c>.</summary>
    /// <returns>The normalized copy.</returns>
    public MovieInput Normalized()
    {
        var copy = Clone();
        copy.Title = Trim(Title);
        copy.Genre = Trim(Genre);
        copy.Director = Trim(Director);
        copy.Medium = NullIfEmpty(Medium);
        copy.Review = NullIfEmpty(Review);
        return copy;
    }

    /// <summary>Tells whether all values equal those of another input.</summary>
    /// <param name="other">The input to compare with.</param>
    /// <returns><c>true</c> when every value is the same.</returns>
    public bool SameAs(MovieInput other) =>
        Title == other.Title
        && Genre == other.Genre
        && RuntimeMinutes == other.RuntimeMinutes
        && ReleaseYear == other.ReleaseYear
        && Director == other.Director
        && WatchedOn == other.WatchedOn
        && Medium == other.Medium
        && Review == other.Review
        && OverallScore == other.OverallScore
        && StoryScore == other.StoryScore
        && PictureScore == other.PictureScore
        && MusicScore == other.MusicScore
        && SoundScore == other.SoundScore
        && ExtrasScore == other.ExtrasScore;

    private static string? Trim(string? text) => text?.Trim();

    private static string? NullIfEmpty(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>Validates the editable values of a movie. Expects normalized input (see <see cref="MovieInput.Normalized"/>).</summary>
internal sealed class MovieInputValidator : AbstractValidator<MovieInput>
{
    /// <summary>The earliest accepted viewing date.</summary>
    public static readonly DateOnly MinWatchedOn = new(1900, 1, 1);

    /// <summary>Creates the validator.</summary>
    /// <param name="clock">The owner's clock, which decides what "today" and "the current year" are.</param>
    public MovieInputValidator(UserClock clock)
    {
        RuleFor(x => x.Title)
            .NotEmpty()
            .WithMessage("Filmtitel is verplicht.")
            .MaximumLength(MovieInput.TitleMaxLength)
            .WithMessage($"Filmtitel mag maximaal {MovieInput.TitleMaxLength} tekens bevatten.");
        RuleFor(x => x.Genre)
            .NotEmpty()
            .WithMessage("Genre is verplicht.")
            .MaximumLength(MovieInput.LookupMaxLength)
            .WithMessage($"Genre mag maximaal {MovieInput.LookupMaxLength} tekens bevatten.");
        RuleFor(x => x.Director)
            .NotEmpty()
            .WithMessage("Regisseur is verplicht.")
            .MaximumLength(MovieInput.DirectorMaxLength)
            .WithMessage($"Regisseur mag maximaal {MovieInput.DirectorMaxLength} tekens bevatten.");
        RuleFor(x => x.Medium)
            .MaximumLength(MovieInput.LookupMaxLength)
            .WithMessage($"Gezien op/in mag maximaal {MovieInput.LookupMaxLength} tekens bevatten.");
        RuleFor(x => x.Review)
            .MaximumLength(MovieInput.ReviewMaxLength)
            .WithMessage($"Opmerkingen mogen maximaal {Formats.Count(MovieInput.ReviewMaxLength)} tekens bevatten.");

        RuleFor(x => x.RuntimeMinutes)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Duur is verplicht.")
            .InclusiveBetween(1, 999)
            .WithMessage("Duur moet tussen 1 en 999 minuten liggen.");
        RuleFor(x => x.ReleaseYear)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Jaar is verplicht.")
            .Must(year => year >= MovieInput.MinYear && year <= clock.CurrentYear + MovieInput.FutureYears)
            .WithMessage(_ =>
                $"Jaar moet tussen {MovieInput.MinYear} en {clock.CurrentYear + MovieInput.FutureYears} liggen."
            );
        RuleFor(x => x.WatchedOn)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Datum is verplicht.")
            .Must(date => date >= MinWatchedOn && date <= clock.Today.AddDays(1))
            .WithMessage(_ =>
                $"Datum moet tussen {MinWatchedOn:dd/MM/yyyy} en {clock.Today.AddDays(1):dd/MM/yyyy} liggen."
            );

        RuleFor(x => x.OverallScore).Must(BeAScore).WithMessage(ScoreMessage);
        RuleFor(x => x.StoryScore).Must(BeAScore).WithMessage(ScoreMessage);
        RuleFor(x => x.PictureScore).Must(BeAScore).WithMessage(ScoreMessage);
        RuleFor(x => x.MusicScore).Must(BeAScore).WithMessage(ScoreMessage);
        RuleFor(x => x.SoundScore).Must(BeAScore).WithMessage(ScoreMessage);
        RuleFor(x => x.ExtrasScore).Must(BeAScore).WithMessage(ScoreMessage);
    }

    private static string ScoreMessage => "Score moet tussen 1 en 10 liggen.";

    private static bool BeAScore(int? score) => score is null or >= 1 and <= 10;
}
