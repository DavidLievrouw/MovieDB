namespace MovieDB.Movies;

/// <summary>Display formats of the Dutch (nl-BE) UI.</summary>
internal static class Formats
{
    private static readonly System.Globalization.CultureInfo Dutch = System.Globalization.CultureInfo.GetCultureInfo(
        "nl-BE"
    );

    /// <summary>Formats a date as <c>Sa, June 15th, 2022</c> (English names, by explicit request).</summary>
    /// <param name="date">The date.</param>
    /// <returns>The text.</returns>
    public static string Date(DateOnly date)
    {
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var day = date.DayOfWeek.ToString()[..2];
        return $"{day}, {date.ToString("MMMM", invariant)} {date.Day}{OrdinalSuffix(date.Day)}, {date.Year}";
    }

    private static string OrdinalSuffix(int day) =>
        day is >= 11 and <= 13
            ? "th"
            : (day % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th",
            };

    /// <summary>Formats a count with the nl-BE thousands separator (<c>1.203</c>).</summary>
    /// <param name="count">The count.</param>
    /// <returns>The text.</returns>
    public static string Count(int count) => count.ToString("N0", Dutch);

    /// <summary>Formats an average score with one decimal and a decimal comma (<c>7,2</c>).</summary>
    /// <param name="score">The average score.</param>
    /// <returns>The text.</returns>
    public static string AverageScore(double score) => score.ToString("0.0", Dutch);

    /// <summary>Formats a total runtime in whole hours (<c>2.145 uur</c>).</summary>
    /// <param name="minutes">The runtime in minutes.</param>
    /// <returns>The text.</returns>
    public static string Hours(int minutes) => $"{Count(minutes / 60)} uur";

    /// <summary>Formats a runtime as <c>123 min</c>.</summary>
    /// <param name="minutes">The runtime in minutes.</param>
    /// <returns>The text.</returns>
    public static string Runtime(int minutes) =>
        $"{minutes.ToString(System.Globalization.CultureInfo.InvariantCulture)} min";
}
