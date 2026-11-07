using System.Globalization;
using System.Text.RegularExpressions;
using MovieDB.Common;

namespace MovieDB.Movies.Lookup;

/// <summary>Turns the text the owner typed into a <see cref="LookupQuery"/>. Pure: no I/O.</summary>
internal static partial class LookupInputParser
{
    /// <summary>The shortest searchable text.</summary>
    public const int MinLength = 2;

    /// <summary>The longest searchable text.</summary>
    public const int MaxLength = 200;

    [GeneratedRegex(@"^(?<title>.*?)(?:\s+(?<year>\d{4})|\s*\(\s*(?<year>\d{4})\s*\))$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingYear();

    /// <summary>
    /// Parses the typed text. A trailing <c>(yyyy)</c> or <c> yyyy</c> becomes the year when it lies within the accepted
    /// release years and a title remains. Links and IMDb ids are not interpreted: they are searched as plain text.
    /// </summary>
    /// <param name="text">The typed text.</param>
    /// <param name="currentYear">The current year, from the owner's clock (never from the browser).</param>
    /// <returns>The query, or a validation error with a Dutch message.</returns>
    public static Result<LookupQuery> Parse(string? text, int currentYear)
    {
        var trimmed = text?.Trim() ?? "";
        if (trimmed.Length < MinLength)
        {
            return Error.Validation("Title", LookupMessages.TooShort);
        }

        if (trimmed.Length > MaxLength)
        {
            return Error.Validation("Title", LookupMessages.TooLong);
        }

        var match = TrailingYear().Match(trimmed);
        if (match.Success)
        {
            var title = match.Groups["title"].Value.Trim();
            var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
            if (title.Length > 0 && year >= MovieInput.MinYear && year <= currentYear + MovieInput.FutureYears)
            {
                return Result<LookupQuery>.Ok(new LookupQuery(title, year));
            }
        }

        return Result<LookupQuery>.Ok(new LookupQuery(trimmed));
    }
}
