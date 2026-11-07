using MovieDB.Common;
using MovieDB.Movies.Lookup;
using Shouldly;

namespace MovieDB.UnitTests.Movies.Lookup;

public sealed class LookupInputParserTests
{
    private const int CurrentYear = 2026;

    [Theory]
    [InlineData("The Matrix", "The Matrix", null)]
    [InlineData("  The Matrix  ", "The Matrix", null)]
    [InlineData("The Matrix (1999)", "The Matrix", 1999)]
    [InlineData("The Matrix ( 1999 )", "The Matrix", 1999)]
    [InlineData("The Matrix 1999", "The Matrix", 1999)]
    [InlineData("Blade Runner 2049", "Blade Runner 2049", null)]
    [InlineData("1999", "1999", null)]
    [InlineData("(1999)", "(1999)", null)]
    [InlineData("Alien 1700", "Alien 1700", null)]
    [InlineData("Alien 2032", "Alien 2032", null)]
    [InlineData("https://www.imdb.com/title/tt0133093/", "https://www.imdb.com/title/tt0133093/", null)]
    [InlineData("tt0133093", "tt0133093", null)]
    public void Parses_title_and_optional_year(string text, string title, int? year)
    {
        var result = LookupInputParser.Parse(text, CurrentYear);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Title.ShouldBe(title);
        result.Value.Year.ShouldBe(year);
    }

    [Fact]
    public void A_year_up_to_the_current_year_is_split_off_even_for_sequels()
    {
        var result = LookupInputParser.Parse("Blade Runner 2049", 2045);

        result.Value.Title.ShouldBe("Blade Runner");
        result.Value.Year.ShouldBe(2049);
    }

    [Fact]
    public void Year_range_is_inclusive_up_to_five_years_ahead()
    {
        LookupInputParser.Parse("Dune 1888", CurrentYear).Value.Year.ShouldBe(1888);
        LookupInputParser.Parse("Dune 1887", CurrentYear).Value.Year.ShouldBeNull();
        LookupInputParser.Parse("Dune 2031", CurrentYear).Value.Year.ShouldBe(2031);
        LookupInputParser.Parse("Dune 2032", CurrentYear).Value.Year.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    [InlineData(" a ")]
    public void Too_short_text_is_a_validation_error(string? text)
    {
        var result = LookupInputParser.Parse(text, CurrentYear);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
    }

    [Fact]
    public void Text_length_boundaries()
    {
        LookupInputParser.Parse(new string('x', 200), CurrentYear).IsSuccess.ShouldBeTrue();
        LookupInputParser.Parse(new string('x', 201), CurrentYear).Error.Kind.ShouldBe(ErrorKind.Validation);
        LookupInputParser.Parse("ab", CurrentYear).IsSuccess.ShouldBeTrue();
    }
}
