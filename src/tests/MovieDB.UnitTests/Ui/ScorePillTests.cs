using Bunit;
using MovieDB.Ui;
using Shouldly;

namespace MovieDB.UnitTests.Ui;

public class ScorePillTests : BunitContext
{
    [Theory]
    [InlineData(1, "score-pill-error")]
    [InlineData(4, "score-pill-error")]
    [InlineData(5, "score-pill-warning")]
    [InlineData(6, "score-pill-warning")]
    [InlineData(7, "score-pill-primary")]
    [InlineData(8, "score-pill-primary")]
    [InlineData(9, "score-pill-success")]
    [InlineData(10, "score-pill-success")]
    public void A_score_is_shown_in_its_band(int score, string expectedClass)
    {
        var cut = Render<ScorePill>(parameters => parameters.Add(p => p.Value, score));

        var pill = cut.Find("span.score-pill");
        pill.ClassList.ShouldContain(expectedClass);
        pill.TextContent.ShouldBe(score.ToString());
    }

    [Fact]
    public void An_empty_score_shows_a_dash()
    {
        var cut = Render<ScorePill>();

        var pill = cut.Find("span.score-pill");
        pill.ClassList.ShouldContain("score-pill-empty");
        pill.TextContent.ShouldBe("–");
        pill.GetAttribute("aria-label").ShouldBe("geen score");
    }

    [Fact]
    public void The_label_is_part_of_the_accessible_name()
    {
        var cut = Render<ScorePill>(parameters => parameters.Add(p => p.Value, 8).Add(p => p.Label, "Totaal"));

        cut.Find("span.score-pill").GetAttribute("aria-label").ShouldBe("Totaal: 8 op 10");
    }
}
