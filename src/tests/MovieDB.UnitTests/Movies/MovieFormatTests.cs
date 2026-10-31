using MovieDB.Movies;
using Shouldly;

namespace MovieDB.UnitTests.Movies;

public class MovieFormatTests
{
    [Fact]
    public void Dates_use_weekday_month_ordinal_day_and_year() =>
        Formats.Date(new DateOnly(2022, 6, 15)).ShouldBe("We, June 15th, 2022");

    [Theory]
    [InlineData(2026, 3, 7, "Sa, March 7th, 2026")]
    [InlineData(2026, 3, 1, "Su, March 1st, 2026")]
    [InlineData(2026, 3, 2, "Mo, March 2nd, 2026")]
    [InlineData(2026, 3, 3, "Tu, March 3rd, 2026")]
    [InlineData(2026, 3, 11, "We, March 11th, 2026")]
    [InlineData(2026, 3, 12, "Th, March 12th, 2026")]
    [InlineData(2026, 3, 13, "Fr, March 13th, 2026")]
    [InlineData(2026, 3, 21, "Sa, March 21st, 2026")]
    [InlineData(2026, 3, 22, "Su, March 22nd, 2026")]
    [InlineData(2026, 3, 23, "Mo, March 23rd, 2026")]
    public void Ordinal_suffixes_follow_english_rules(int year, int month, int day, string expected) =>
        Formats.Date(new DateOnly(year, month, day)).ShouldBe(expected);

    [Fact]
    public void Counts_use_a_dot_as_thousands_separator() => Formats.Count(1203).ShouldBe("1.203");

    [Fact]
    public void Runtimes_are_shown_in_minutes() => Formats.Runtime(123).ShouldBe("123 min");

    [Fact]
    public void Average_scores_use_a_decimal_comma() => Formats.AverageScore(7.26).ShouldBe("7,3");

    [Fact]
    public void Total_runtimes_are_shown_in_whole_hours() => Formats.Hours(128_700).ShouldBe("2.145 uur");

    [Theory]
    [InlineData("abc", "%abc%")]
    [InlineData("100%", "%100\\%%")]
    [InlineData("a_b", "%a\\_b%")]
    [InlineData("a\\b", "%a\\\\b%")]
    [InlineData("\\%_", "%\\\\\\%\\_%")]
    public void Like_patterns_escape_wildcards(string text, string expected) =>
        MovieQueries.ContainsPattern(text).ShouldBe(expected);
}
