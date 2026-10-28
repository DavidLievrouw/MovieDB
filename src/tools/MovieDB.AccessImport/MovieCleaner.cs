using System.Globalization;
using System.Text;

namespace MovieDB.AccessImport;

/// <summary>A cleaned, validated movie ready to load. <c>Id</c> is the preserved Access <c>FilmID</c>.</summary>
internal sealed record CleanMovie(
    int Id,
    string Title,
    int ReleaseYear,
    string Director,
    string Genre,
    int RuntimeMinutes,
    DateOnly WatchedOn,
    string? Medium,
    int? OverallScore,
    int? StoryScore,
    int? PictureScore,
    int? MusicScore,
    int? SoundScore,
    int? ExtrasScore,
    string? Review
);

/// <summary>One validation problem. Only the row id and the field are reported, never the content.</summary>
internal sealed record RowError(int FilmId, string Field, string Problem)
{
    public override string ToString() => $"FilmID {FilmId}: {Field} {Problem}";
}

/// <summary>Outcome of cleaning one row: either a movie or errors, plus which cleanups were applied.</summary>
internal sealed record CleanResult(
    int FilmId,
    CleanMovie? Movie,
    IReadOnlyList<RowError> Errors,
    bool TitleExtraPartsDropped,
    bool DirectorExtraPartsDropped,
    bool WhitespaceCleaned
);

internal static class MovieCleaner
{
    public const int MaxTitleLength = 200;
    public const int MaxDirectorLength = 150;
    public const int MaxLookupLength = 50;
    public const int MaxReviewLength = 20000;
    public const int MinYear = 1888;
    public const int MaxYear = 9999;

    private static readonly char[] LineBreaks = ['\r', '\n', '\t'];

    public static CleanResult Clean(SourceFilm row)
    {
        var errors = new List<RowError>();
        var whitespaceCleaned = false;

        var (title, titleExtra, titleChanged) = SingleLine(row.Title);
        var (director, directorExtra, directorChanged) = SingleLine(row.Director);
        whitespaceCleaned |= titleChanged || directorChanged;

        var (genre, _, genreChanged) = SingleLine(row.Genre);
        var (mediumValue, _, mediumChanged) = SingleLine(row.Medium);
        whitespaceCleaned |= genreChanged || mediumChanged;

        Require(errors, row.FilmId, "Title", title, MaxTitleLength);
        Require(errors, row.FilmId, "Director", director, MaxDirectorLength);
        Require(errors, row.FilmId, "Genre", genre, MaxLookupLength);
        string? medium = mediumValue.Length == 0 ? null : mediumValue;
        if (medium is not null && CharCount(medium) > MaxLookupLength)
        {
            errors.Add(new RowError(row.FilmId, "Medium", "is too long"));
        }

        var year = ParseInRange(row.Year, row.FilmId, "Year", MinYear, MaxYear, errors);
        var runtime = ParseInRange(row.Duration, row.FilmId, "Duration", 1, 999, errors);
        var watchedOn = ParseDate(row.WatchedOn, row.FilmId, errors);

        var overall = Score(row.OverallScore, row.FilmId, "OverallScore", errors);
        var story = Score(row.StoryScore, row.FilmId, "StoryScore", errors);
        var picture = Score(row.PictureScore, row.FilmId, "PictureScore", errors);
        var music = Score(row.MusicScore, row.FilmId, "MusicScore", errors);
        var sound = Score(row.SoundScore, row.FilmId, "SoundScore", errors);
        var extras = ExtrasScore(row.Extras, row.FilmId, errors);

        var review = CleanReview(row.Review);
        if (review is not null && CharCount(review) > MaxReviewLength)
        {
            errors.Add(new RowError(row.FilmId, "Review", "is too long"));
        }

        if (errors.Count > 0 || year is null || runtime is null || watchedOn is null)
        {
            return new CleanResult(row.FilmId, null, errors, titleExtra, directorExtra, whitespaceCleaned);
        }

        var movie = new CleanMovie(
            row.FilmId,
            title,
            year.Value,
            director,
            genre,
            runtime.Value,
            watchedOn.Value,
            medium,
            overall,
            story,
            picture,
            music,
            sound,
            extras,
            review
        );
        return new CleanResult(row.FilmId, movie, errors, titleExtra, directorExtra, whitespaceCleaned);
    }

    /// <summary>
    /// Takes the first non-empty line (split on CR, LF and tab), trims it and collapses runs of spaces. Reports whether
    /// further non-empty parts were dropped and whether anything other than nothing was changed.
    /// </summary>
    internal static (string Value, bool ExtraPartsDropped, bool Changed) SingleLine(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return ("", false, false);
        }

        var parts = raw.Split(LineBreaks, StringSplitOptions.None)
            .Select(p => CollapseSpaces(p.Trim()))
            .Where(p => p.Length > 0)
            .ToList();
        var value = parts.Count > 0 ? parts[0] : "";
        return (value, parts.Count > 1, !string.Equals(value, raw, StringComparison.Ordinal));
    }

    internal static string CollapseSpaces(string value)
    {
        var builder = new StringBuilder(value.Length);
        var previousSpace = false;
        foreach (var c in value)
        {
            var isSpace = c == ' ' || c == '\u00A0';
            if (isSpace && previousSpace)
            {
                continue;
            }

            builder.Append(isSpace ? ' ' : c);
            previousSpace = isSpace;
        }

        return builder.ToString();
    }

    internal static string? CleanReview(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var normalised = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        return normalised.Length == 0 ? null : normalised;
    }

    /// <summary>Length in code points, which is how MariaDB counts utf8mb4 characters.</summary>
    internal static int CharCount(string value) => value.EnumerateRunes().Count();

    private static void Require(List<RowError> errors, int filmId, string field, string value, int maxLength)
    {
        if (value.Length == 0)
        {
            errors.Add(new RowError(filmId, field, "is missing"));
        }
        else if (CharCount(value) > maxLength)
        {
            errors.Add(new RowError(filmId, field, "is too long"));
        }
    }

    private static int? ParseInt(string? raw, int filmId, string field, List<RowError> errors)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            errors.Add(new RowError(filmId, field, "is missing"));
            return null;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            errors.Add(new RowError(filmId, field, "is not a number"));
            return null;
        }

        return value;
    }

    private static int? ParseInRange(string? raw, int filmId, string field, int min, int max, List<RowError> errors)
    {
        var value = ParseInt(raw, filmId, field, errors);
        if (value is { } v && (v < min || v > max))
        {
            errors.Add(new RowError(filmId, field, "is out of range"));
        }

        return value;
    }

    private static DateOnly? ParseDate(DateTime? raw, int filmId, List<RowError> errors)
    {
        if (raw is { } date)
        {
            return DateOnly.FromDateTime(date);
        }

        errors.Add(new RowError(filmId, "Date", "is missing"));
        return null;
    }

    private static int? Score(int? raw, int filmId, string field, List<RowError> errors)
    {
        switch (raw)
        {
            case null:
            case 0:
                return null;
            case >= 1 and <= 10:
                return raw;
            default:
                errors.Add(new RowError(filmId, field, "is out of range"));
                return null;
        }
    }

    private static int? ExtrasScore(string? raw, int filmId, List<RowError> errors)
    {
        var text = raw?.Trim();
        if (string.IsNullOrEmpty(text) || string.Equals(text, "n/a", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            errors.Add(new RowError(filmId, "ExtrasScore", "is not a number"));
            return null;
        }

        if (value == 0)
        {
            return null;
        }

        if (value is < 1 or > 10)
        {
            errors.Add(new RowError(filmId, "ExtrasScore", "is out of range"));
            return null;
        }

        return value;
    }
}
