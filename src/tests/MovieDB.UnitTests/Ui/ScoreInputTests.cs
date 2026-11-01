using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MovieDB.Ui;
using MudBlazor;
using MudBlazor.Services;
using Shouldly;

namespace MovieDB.UnitTests.Ui;

public class ScoreInputTests : BunitContext
{
    public ScoreInputTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Render<MudPopoverProvider>();
    }

    private IRenderedComponent<ScoreInput> RenderInput(
        int? value,
        Action<int?>? changed = null,
        bool allowEmpty = true
    ) =>
        Render<ScoreInput>(parameters =>
        {
            parameters.Add(p => p.Value, value).Add(p => p.Label, "Totaal").Add(p => p.AllowEmpty, allowEmpty);
            if (changed is not null)
            {
                parameters.Add(p => p.ValueChanged, EventCallback.Factory.Create<int?>(this, changed));
            }
        });

    [Fact]
    public void It_offers_an_empty_option_and_the_scores_one_to_ten()
    {
        var cut = RenderInput(null);

        var items = cut.FindAll(".score-input-toggles .mud-toggle-item");
        items.Count.ShouldBe(11);
        items[0].TextContent.Trim().ShouldBe("–");
        items[^1].TextContent.Trim().ShouldBe("10");
    }

    [Fact]
    public void Without_the_empty_option_only_the_scores_one_to_ten_are_offered()
    {
        var cut = RenderInput(null, allowEmpty: false);

        var items = cut.FindAll(".score-input-toggles .mud-toggle-item");
        items.Count.ShouldBe(10);
        items[0].TextContent.Trim().ShouldBe("1");
        items[^1].TextContent.Trim().ShouldBe("10");
    }

    [Fact]
    public void An_invalid_input_shows_its_message()
    {
        var cut = Render<ScoreInput>(p =>
            p.Add(x => x.Label, "Totaal").Add(x => x.Error, true).Add(x => x.ErrorText, "Totaal is verplicht.")
        );

        cut.Find(".score-input-error").TextContent.ShouldBe("Totaal is verplicht.");
        cut.Find("div.score-input").GetAttribute("aria-invalid").ShouldBe("true");
    }

    [Fact]
    public void The_group_is_named_after_the_score()
    {
        var cut = RenderInput(7);

        cut.Find("div.score-input").GetAttribute("aria-label").ShouldBe("Totaal");
        cut.Find(".score-input-label").TextContent.ShouldBe("Totaal");
    }

    [Fact]
    public void Picking_a_score_raises_the_change()
    {
        int? received = -1;
        var cut = RenderInput(null, v => received = v);

        cut.FindAll(".score-input-toggles .mud-toggle-item")[8].Click();

        received.ShouldBe(8);
    }

    [Fact]
    public void Picking_the_empty_option_raises_null()
    {
        int? received = -1;
        var cut = RenderInput(5, v => received = v);

        cut.FindAll(".score-input-toggles .mud-toggle-item")[0].Click();

        received.ShouldBeNull();
    }
}
