using Bunit;
using MovieDB.Movies;
using Shouldly;

namespace MovieDB.UnitTests.Movies;

public sealed class TmdbAttributionTests : BunitContext
{
    [Fact]
    public void Shows_the_exact_notice_in_English_with_the_logo()
    {
        var cut = Render<TmdbAttribution>();

        var text = cut.Find("p.tmdb-attribution-text");
        text.TextContent.Trim()
            .ShouldBe(
                "This application uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB."
            );
        text.GetAttribute("lang").ShouldBe("en");
        var logo = cut.Find("img.tmdb-attribution-logo");
        logo.GetAttribute("src").ShouldBe("/_content/MovieDB.Movies/images/tmdb-logo.svg");
        logo.GetAttribute("alt").ShouldBe("TMDB");
    }
}
