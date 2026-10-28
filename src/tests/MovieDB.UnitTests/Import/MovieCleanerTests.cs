using MovieDB.AccessImport;
using Shouldly;

namespace MovieDB.UnitTests.Import;

internal static class SyntheticFilms
{
    public static SourceFilm Film(
        int id,
        string title = "Synthetic Title",
        string genre = "Drama",
        string duration = " 88",
        string year = "1999",
        string director = "Jane Doe",
        DateTime? watchedOn = null,
        int? overall = 7,
        int? story = 6,
        int? picture = 5,
        int? music = 4,
        int? sound = 3,
        string? extras = "8",
        string? medium = "DVD",
        string? review = "Synthetic review."
    ) =>
        new(
            id,
            title,
            genre,
            duration,
            year,
            director,
            watchedOn ?? new DateTime(2020, 5, 17, 0, 0, 0, DateTimeKind.Unspecified),
            overall,
            story,
            picture,
            music,
            sound,
            extras,
            medium,
            review
        );
}

public sealed class MovieCleanerTests
{
    private static CleanMovie Cleaned(SourceFilm row)
    {
        var result = MovieCleaner.Clean(row);
        result.Errors.ShouldBeEmpty();
        return result.Movie.ShouldNotBeNull();
    }

    [Fact]
    public void Valid_row_is_mapped_with_the_original_id()
    {
        var movie = Cleaned(SyntheticFilms.Film(42));

        movie.Id.ShouldBe(42);
        movie.Title.ShouldBe("Synthetic Title");
        movie.RuntimeMinutes.ShouldBe(88);
        movie.ReleaseYear.ShouldBe(1999);
        movie.WatchedOn.ShouldBe(new DateOnly(2020, 5, 17));
        movie.OverallScore.ShouldBe(7);
        movie.ExtrasScore.ShouldBe(8);
        movie.Medium.ShouldBe("DVD");
    }

    [Theory]
    [InlineData("  Spaced   Out  ", "Spaced Out")]
    [InlineData("First\r\nSecond", "First")]
    [InlineData("\r\n\tFirst\tSecond", "First")]
    [InlineData("A\u00A0\u00A0B", "A B")]
    public void Titles_are_reduced_to_the_first_clean_line(string raw, string expected) =>
        Cleaned(SyntheticFilms.Film(1, title: raw)).Title.ShouldBe(expected);

    [Fact]
    public void Dropped_extra_parts_and_whitespace_cleanup_are_reported()
    {
        var multi = MovieCleaner.Clean(SyntheticFilms.Film(1, title: "A\r\nB", director: "X\tY"));
        multi.TitleExtraPartsDropped.ShouldBeTrue();
        multi.DirectorExtraPartsDropped.ShouldBeTrue();
        multi.WhitespaceCleaned.ShouldBeTrue();

        var padded = MovieCleaner.Clean(SyntheticFilms.Film(2, title: "A \r\n"));
        padded.TitleExtraPartsDropped.ShouldBeFalse();
        padded.WhitespaceCleaned.ShouldBeTrue();

        MovieCleaner.Clean(SyntheticFilms.Film(3)).WhitespaceCleaned.ShouldBeFalse();
    }

    [Theory]
    [InlineData("099", 99)]
    [InlineData(" 88", 88)]
    [InlineData("123", 123)]
    public void Runtime_text_is_parsed(string raw, int expected) =>
        Cleaned(SyntheticFilms.Film(1, duration: raw)).RuntimeMinutes.ShouldBe(expected);

    [Theory]
    [InlineData("0")]
    [InlineData("1000")]
    [InlineData("abc")]
    [InlineData("")]
    public void Invalid_runtime_is_an_error(string raw)
    {
        var result = MovieCleaner.Clean(SyntheticFilms.Film(5, duration: raw));
        result.Movie.ShouldBeNull();
        result.Errors.Single().Field.ShouldBe("Duration");
        result.Errors.Single().FilmId.ShouldBe(5);
    }

    [Theory]
    [InlineData("1887")]
    [InlineData("19x9")]
    [InlineData(null)]
    public void Invalid_year_is_an_error(string? raw) =>
        MovieCleaner.Clean(SyntheticFilms.Film(1, year: raw!)).Errors.ShouldContain(e => e.Field == "Year");

    [Theory]
    [InlineData(0, null)]
    [InlineData(null, null)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    public void Scores_map_zero_and_null_to_empty(int? raw, int? expected) =>
        Cleaned(SyntheticFilms.Film(1, overall: raw, story: raw, picture: raw, music: raw, sound: raw))
            .OverallScore.ShouldBe(expected);

    [Theory]
    [InlineData(11)]
    [InlineData(-1)]
    public void Out_of_range_scores_are_errors(int raw) =>
        MovieCleaner
            .Clean(SyntheticFilms.Film(9, music: raw))
            .Errors.ShouldHaveSingleItem()
            .Field.ShouldBe("MusicScore");

    [Theory]
    [InlineData("n/a", null)]
    [InlineData("N/A", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("0", null)]
    [InlineData("7", 7)]
    [InlineData(" 10 ", 10)]
    public void Extras_text_is_parsed(string? raw, int? expected) =>
        Cleaned(SyntheticFilms.Film(1, extras: raw)).ExtrasScore.ShouldBe(expected);

    [Theory]
    [InlineData("11")]
    [InlineData("good")]
    public void Invalid_extras_is_an_error(string raw) =>
        MovieCleaner.Clean(SyntheticFilms.Film(1, extras: raw)).Errors.ShouldContain(e => e.Field == "ExtrasScore");

    [Fact]
    public void Missing_required_values_are_all_reported()
    {
        var row = SyntheticFilms.Film(7, title: " ", genre: "", director: "") with { WatchedOn = null };
        var fields = MovieCleaner.Clean(row).Errors.Select(e => e.Field).ToList();

        fields.ShouldBe(["Title", "Director", "Genre", "Date"], ignoreOrder: true);
    }

    [Fact]
    public void Too_long_values_are_errors_and_counted_in_code_points()
    {
        var emoji = string.Concat(Enumerable.Repeat("😀", 200));
        Cleaned(SyntheticFilms.Film(1, title: emoji)).Title.ShouldBe(emoji);

        MovieCleaner
            .Clean(SyntheticFilms.Film(1, title: new string('x', 201)))
            .Errors.ShouldContain(e => e.Field == "Title");
        MovieCleaner
            .Clean(SyntheticFilms.Film(1, director: new string('x', 151)))
            .Errors.ShouldContain(e => e.Field == "Director");
        MovieCleaner
            .Clean(SyntheticFilms.Film(1, medium: new string('x', 51)))
            .Errors.ShouldContain(e => e.Field == "Medium");
        MovieCleaner
            .Clean(SyntheticFilms.Film(1, review: new string('x', 20001)))
            .Errors.ShouldContain(e => e.Field == "Review");
    }

    [Fact]
    public void Reviews_are_normalised_and_empty_ones_become_null()
    {
        Cleaned(SyntheticFilms.Film(1, review: "  Line 1\r\nLine 2\rLine 3  "))
            .Review.ShouldBe("Line 1\nLine 2\nLine 3");
        Cleaned(SyntheticFilms.Film(1, review: " \r\n ")).Review.ShouldBeNull();
        Cleaned(SyntheticFilms.Film(1, review: null)).Review.ShouldBeNull();
    }

    [Fact]
    public void Empty_medium_becomes_null()
    {
        Cleaned(SyntheticFilms.Film(1, medium: "  ")).Medium.ShouldBeNull();
        Cleaned(SyntheticFilms.Film(1, medium: null)).Medium.ShouldBeNull();
    }

    [Fact]
    public void Unicode_is_preserved()
    {
        var movie = Cleaned(SyntheticFilms.Film(1, title: "Amélie — 千と千尋", review: "Ça va très bien 😀"));
        movie.Title.ShouldBe("Amélie — 千と千尋");
        movie.Review.ShouldBe("Ça va très bien 😀");
    }

    [Fact]
    public void Errors_do_not_contain_row_content()
    {
        var error = MovieCleaner
            .Clean(SyntheticFilms.Film(5, title: "SECRET-TITLE", duration: "SECRET"))
            .Errors.Single();
        error.ToString().ShouldNotContain("SECRET");
    }
}
