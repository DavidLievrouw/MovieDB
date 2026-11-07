using System.Text.RegularExpressions;
using MovieDB.Common;

namespace MovieDB.Movies.Lookup;

/// <summary>
/// Applies provider details to the new-movie form. Pure: no I/O. Touches only title, genres, runtime, release year and
/// directors; a value that cannot be filled leaves its field empty and yields a Dutch notice.
/// </summary>
internal static partial class LookupFill
{
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    /// <summary>Tells whether the owner has typed anything besides the title, so a fill would overwrite work.</summary>
    /// <param name="input">The form values.</param>
    /// <returns><c>true</c> when genres, runtime, year or directors are set; the title alone does not count.</returns>
    public static bool HasInputBesidesTitle(MovieInput input) =>
        input.Genres.Count > 0
        || input.RuntimeMinutes is not null
        || input.ReleaseYear is not null
        || input.Directors.Count > 0;

    /// <summary>Overwrites the five lookup fields of <paramref name="target"/>.</summary>
    /// <param name="target">The form values to fill.</param>
    /// <param name="details">The provider details.</param>
    /// <param name="clock">The owner's clock, for the accepted year range.</param>
    /// <returns>The Dutch notices, in form-field order (genre, duur, jaar, regisseur); empty when all was filled.</returns>
    public static IReadOnlyList<string> Apply(MovieInput target, LookupDetails details, UserClock clock)
    {
        var notices = new List<string>();

        var title = (details.Title ?? "").Trim();
        target.Title = title.Length > MovieInput.TitleMaxLength ? title[..MovieInput.TitleMaxLength].TrimEnd() : title;

        target.Genres =
        [
            .. details
                .Genres.Select(g => g.Trim())
                .Where(g => g.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MovieInput.MaxGenres),
        ];
        if (target.Genres.Count == 0)
        {
            notices.Add(LookupMessages.GenreMissing);
        }

        if (details.RuntimeMinutes is >= MovieInput.MinRuntimeMinutes and <= MovieInput.MaxRuntimeMinutes)
        {
            target.RuntimeMinutes = details.RuntimeMinutes;
        }
        else
        {
            target.RuntimeMinutes = null;
            notices.Add(LookupMessages.RuntimeMissing);
        }

        if (
            details.Year is { } year
            && year >= MovieInput.MinYear
            && year <= clock.CurrentYear + MovieInput.FutureYears
        )
        {
            target.ReleaseYear = year;
        }
        else
        {
            target.ReleaseYear = null;
            notices.Add(LookupMessages.YearMissing);
        }

        var directors = details
            .Directors.Select(CleanDirector)
            .Where(d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        target.Directors = [.. directors.Take(MovieInput.MaxDirectors)];
        if (directors.Count == 0)
        {
            notices.Add(LookupMessages.DirectorMissing);
        }
        else if (directors.Count > MovieInput.MaxDirectors)
        {
            notices.Add(LookupMessages.TooManyDirectors);
        }

        return notices;
    }

    private static string CleanDirector(string name)
    {
        var cleaned = Whitespace().Replace((name ?? "").Replace(',', ' '), " ").Trim();
        return cleaned.Length > MovieInput.DirectorMaxLength
            ? cleaned[..MovieInput.DirectorMaxLength].TrimEnd()
            : cleaned;
    }
}
