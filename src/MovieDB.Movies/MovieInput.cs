using FluentValidation;
using MovieDB.Common;
using MovieDB.Infrastructure;

namespace MovieDB.Movies;

/// <summary>The Dutch messages of the edit page and the save command.</summary>
internal static class MovieMessages
{
    /// <summary>Shown when another movie has the same title, year and directors.</summary>
    public const string Duplicate = "Deze film bestaat al (Jaar en Regisseurs zijn gelijk).";

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

    /// <summary>The longest director name.</summary>
    public const int DirectorMaxLength = 150;

    /// <summary>The largest number of directors of a movie.</summary>
    public const int MaxDirectors = 5;

    /// <summary>The longest genre or medium name.</summary>
    public const int LookupMaxLength = MovieGenres.MaxNameLength;

    /// <summary>The largest number of genres of a movie.</summary>
    public const int MaxGenres = MovieGenres.MaxPerMovie;

    /// <summary>The longest review.</summary>
    public const int ReviewMaxLength = 20_000;

    /// <summary>The earliest accepted release year.</summary>
    public const int MinYear = 1888;

    /// <summary>The number of years after the current year that are still accepted as a release year.</summary>
    public const int FutureYears = 5;

    /// <summary>The shortest accepted runtime in minutes.</summary>
    public const int MinRuntimeMinutes = 1;

    /// <summary>The longest accepted runtime in minutes.</summary>
    public const int MaxRuntimeMinutes = 999;

    /// <summary>The title.</summary>
    public string? Title { get; set; }

    /// <summary>The genre names, in the order they were added.</summary>
    public List<string> Genres { get; set; } = [];

    /// <summary>The runtime in minutes.</summary>
    public int? RuntimeMinutes { get; set; }

    /// <summary>The release year.</summary>
    public int? ReleaseYear { get; set; }

    /// <summary>The director names, in the order they were added.</summary>
    public List<string> Directors { get; set; } = [];

    /// <summary>The viewing date.</summary>
    public DateOnly? WatchedOn { get; set; }

    /// <summary>The medium name, if any.</summary>
    public string? Medium { get; set; }

    /// <summary>The written review, if any.</summary>
    public string? Review { get; set; }

    /// <summary>The overall score (1-10), required.</summary>
    public int? OverallScore { get; set; }

    /// <summary>The story score (1-10), required.</summary>
    public int? StoryScore { get; set; }

    /// <summary>The picture score (1-10), required.</summary>
    public int? PictureScore { get; set; }

    /// <summary>The music score (1-10), required.</summary>
    public int? MusicScore { get; set; }

    /// <summary>The sound score (1-10), required.</summary>
    public int? SoundScore { get; set; }

    /// <summary>Creates a copy.</summary>
    /// <returns>An independent copy with the same values.</returns>
    public MovieInput Clone()
    {
        var copy = (MovieInput)MemberwiseClone();
        copy.Genres = [.. Genres];
        copy.Directors = [.. Directors];
        return copy;
    }

    /// <summary>
    /// Creates a copy with trimmed text; empty genres and directors are dropped, and an empty medium or review becomes
    /// <c>null</c>.
    /// </summary>
    /// <returns>The normalized copy.</returns>
    public MovieInput Normalized()
    {
        var copy = Clone();
        copy.Title = Trim(Title);
        copy.Genres = [.. Genres.Select(g => g?.Trim() ?? "").Where(g => g.Length > 0)];
        copy.Directors = [.. Directors.Select(d => d?.Trim() ?? "").Where(d => d.Length > 0)];
        copy.Medium = NullIfEmpty(Medium);
        copy.Review = NullIfEmpty(Review);
        return copy;
    }

    /// <summary>
    /// Tells whether all values equal those of another input. The genres are compared as an unordered set; the
    /// directors as an ordered sequence, because their order is shown.
    /// </summary>
    /// <param name="other">The input to compare with.</param>
    /// <returns><c>true</c> when every value is the same.</returns>
    public bool SameAs(MovieInput other) =>
        Title == other.Title
        && Genres.Count == other.Genres.Count
        && Genres.ToHashSet(StringComparer.Ordinal).SetEquals(other.Genres)
        && RuntimeMinutes == other.RuntimeMinutes
        && ReleaseYear == other.ReleaseYear
        && Directors.SequenceEqual(other.Directors, StringComparer.Ordinal)
        && WatchedOn == other.WatchedOn
        && Medium == other.Medium
        && Review == other.Review
        && OverallScore == other.OverallScore
        && StoryScore == other.StoryScore
        && PictureScore == other.PictureScore
        && MusicScore == other.MusicScore
        && SoundScore == other.SoundScore;

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
        RuleFor(x => x.Genres)
            .Must(genres => genres.Count >= 1)
            .WithMessage("Kies minstens één genre.")
            .Must(genres => genres.Count <= MovieInput.MaxGenres)
            .WithMessage($"Kies maximaal {MovieInput.MaxGenres} genres.")
            .Must(genres => genres.All(g => g.Length <= MovieInput.LookupMaxLength))
            .WithMessage($"Genre mag maximaal {MovieInput.LookupMaxLength} tekens bevatten.")
            .Must(genres => genres.Distinct(StringComparer.OrdinalIgnoreCase).Count() == genres.Count)
            .WithMessage("Een genre komt maar één keer voor.");
        RuleFor(x => x.Directors)
            .Must(directors => directors.Count >= 1)
            .WithMessage("Geef minstens één regisseur op.")
            .Must(directors => directors.Count <= MovieInput.MaxDirectors)
            .WithMessage($"Maximaal {MovieInput.MaxDirectors} regisseurs.")
            .Must(directors => directors.All(d => d.Length <= MovieInput.DirectorMaxLength))
            .WithMessage($"Regisseur mag maximaal {MovieInput.DirectorMaxLength} tekens bevatten.")
            .Must(directors => directors.All(d => !d.Contains(',', StringComparison.Ordinal)))
            .WithMessage("Een regisseur mag geen komma bevatten.")
            .Must(directors => directors.Distinct(StringComparer.OrdinalIgnoreCase).Count() == directors.Count)
            .WithMessage("Een regisseur komt maar één keer voor.");
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
            .InclusiveBetween(MovieInput.MinRuntimeMinutes, MovieInput.MaxRuntimeMinutes)
            .WithMessage(
                $"Duur moet tussen {MovieInput.MinRuntimeMinutes} en {MovieInput.MaxRuntimeMinutes} minuten liggen."
            );
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

        RuleScore(x => x.OverallScore, "Totaal");
        RuleScore(x => x.StoryScore, "Film");
        RuleScore(x => x.PictureScore, "Beeld");
        RuleScore(x => x.MusicScore, "Muziek");
        RuleScore(x => x.SoundScore, "Geluid");
    }

    private void RuleScore(System.Linq.Expressions.Expression<Func<MovieInput, int?>> score, string label) =>
        RuleFor(score)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage($"{label} is verplicht.")
            .InclusiveBetween(1, 10)
            .WithMessage("Kies een score van 1 tot 10.");
}
