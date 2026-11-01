using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using MovieDB.Ui;
using MudBlazor;
using MudBlazor.Services;
using Shouldly;

namespace MovieDB.UnitTests.Ui;

public class ChipMultiSelectTests : BunitContext
{
    private static readonly string[] Genres = ["Actie", "Drama", "Thriller"];

    public ChipMultiSelectTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Render<MudPopoverProvider>();
    }

    private IRenderedComponent<ChipMultiSelect> RenderSelect(
        IReadOnlyList<string> values,
        Action<IReadOnlyList<string>>? changed = null,
        bool allowNew = false,
        int? maxSelected = null,
        Func<string, bool>? isKnown = null,
        bool required = false
    ) =>
        Render<ChipMultiSelect>(parameters =>
        {
            parameters
                .Add(p => p.Values, values)
                .Add(p => p.Required, required)
                .Add(p => p.Label, "Genre")
                .Add(p => p.AllowNew, allowNew)
                .Add(p => p.MaxSelected, maxSelected)
                .Add(p => p.IsKnown, isKnown)
                .Add(
                    p => p.Suggestions,
                    (text, _) =>
                        Task.FromResult<IReadOnlyList<string>>([
                            .. Genres.Where(g => g.Contains(text, StringComparison.OrdinalIgnoreCase)),
                        ])
                );
            if (changed is not null)
            {
                parameters.Add(
                    p => p.ValuesChanged,
                    EventCallback.Factory.Create<IReadOnlyList<string>>(this, changed)
                );
            }
        });

    [Fact]
    public void Every_selected_value_is_shown_as_a_chip()
    {
        var cut = RenderSelect(["Actie", "Drama"]);

        cut.FindAll(".chip-multi-select-chip").Select(c => c.TextContent.Trim()).ShouldBe(["Actie", "Drama"]);
    }

    [Fact]
    public void The_chips_and_the_input_are_inside_the_outlined_box()
    {
        var cut = RenderSelect(["Actie", "Drama"]);

        var box = cut.Find(".chip-multi-select-box");
        box.QuerySelectorAll(".chip-multi-select-chip").Length.ShouldBe(2);
        box.QuerySelector("input").ShouldNotBeNull();
    }

    [Fact]
    public void Backspace_in_the_empty_input_does_not_remove_a_chip()
    {
        IReadOnlyList<string>? raised = null;
        var cut = RenderSelect(["Actie", "Drama"], v => raised = v);

        cut.Find("input").KeyDown(new Microsoft.AspNetCore.Components.Web.KeyboardEventArgs { Key = "Backspace" });

        raised.ShouldBeNull();
        cut.FindAll(".chip-multi-select-chip").Count.ShouldBe(2);
    }

    [Fact]
    public void No_chips_are_shown_without_values()
    {
        RenderSelect([]).FindAll(".chip-multi-select-chip").ShouldBeEmpty();
    }

    [Fact]
    public void Closing_a_chip_raises_the_remaining_values()
    {
        IReadOnlyList<string>? raised = null;
        var cut = RenderSelect(["Actie", "Drama", "Thriller"], v => raised = v);

        cut.FindAll(".chip-multi-select-chip .mud-chip-close-button")[1].Click();

        raised.ShouldBe(["Actie", "Thriller"]);
    }

    [Fact]
    public void A_value_that_is_not_known_gets_the_new_marker()
    {
        var cut = RenderSelect(["Actie", "Zzz"], isKnown: v => Genres.Contains(v));

        var chips = cut.FindAll(".chip-multi-select-chip");
        chips[0].QuerySelector(".chip-multi-select-new").ShouldBeNull();
        chips[1].QuerySelector(".chip-multi-select-new")!.TextContent.ShouldBe("nieuw");
    }

    [Fact]
    public void No_marker_is_shown_without_an_IsKnown_check()
    {
        RenderSelect(["Zzz"]).FindAll(".chip-multi-select-new").ShouldBeEmpty();
    }

    [Fact]
    public void The_input_is_disabled_when_the_maximum_is_reached()
    {
        var full = RenderSelect(["Actie", "Drama"], maxSelected: 2);
        var room = RenderSelect(["Actie"], maxSelected: 2);

        full.Find("input").HasAttribute("disabled").ShouldBeTrue();
        room.Find("input").HasAttribute("disabled").ShouldBeFalse();
    }

    [Fact]
    public void Typing_a_comma_separated_list_adds_each_value_when_new_values_are_allowed()
    {
        IReadOnlyList<string>? raised = null;
        var cut = RenderSelect(["Actie"], v => raised = v, allowNew: true);

        cut.Find("input").Input("Drama, Zzz, actie");

        cut.WaitForAssertion(() => raised.ShouldBe(["Actie", "Drama", "Zzz"]));
    }

    [Fact]
    public void A_comma_adds_nothing_when_only_suggestions_are_allowed()
    {
        IReadOnlyList<string>? raised = null;
        var cut = RenderSelect([], v => raised = v, allowNew: false);

        cut.Find("input").Input("Drama, Zzz");

        Should.Throw<Exception>(() =>
            cut.WaitForAssertion(() => raised.ShouldNotBeNull(), TimeSpan.FromMilliseconds(600))
        );
    }

    [Fact]
    public void Leaving_a_required_field_that_has_values_does_not_show_an_error()
    {
        var cut = RenderSelect(["Actie"], required: true);

        cut.Find("input").Focus();
        cut.Find("input").Blur();

        cut.FindAll(".mud-input-error").ShouldBeEmpty();
    }

    [Fact]
    public void A_required_field_marks_its_label_and_shows_an_error_only_when_the_parent_says_so()
    {
        var cut = RenderSelect([], required: true);

        cut.Find("input").Focus();
        cut.Find("input").Blur();
        cut.FindAll(".mud-input-error").ShouldBeEmpty();
        cut.Find("legend").TextContent.ShouldContain("*");
        cut.FindAll(".chip-multi-select-invalid").ShouldBeEmpty();

        cut.Render(parameters => parameters.Add(p => p.Error, true).Add(p => p.ErrorText, "Kies minstens één genre."));

        cut.FindAll(".mud-input-error").ShouldNotBeEmpty();
        cut.FindAll(".chip-multi-select-invalid").ShouldNotBeEmpty();
        cut.Markup.ShouldContain("Kies minstens één genre.");
    }

    [Fact]
    public void The_values_are_added_up_to_the_maximum()
    {
        IReadOnlyList<string>? raised = null;
        var cut = RenderSelect(["Actie"], v => raised = v, allowNew: true, maxSelected: 2);

        cut.Find("input").Input("Drama, Thriller");

        cut.WaitForAssertion(() => raised.ShouldBe(["Actie", "Drama"]));
    }

    private IRenderedComponent<ChipMultiSelect> RenderWithBlur(IReadOnlyList<string> values, Action onBlur) =>
        Render<ChipMultiSelect>(parameters =>
            parameters
                .Add(p => p.Values, values)
                .Add(p => p.Label, "Genre")
                .Add(p => p.Placeholder, "Kies een genre")
                .Add(p => p.OnBlur, EventCallback.Factory.Create<FocusEventArgs>(this, onBlur))
                .Add(p => p.Suggestions, (_, _) => Task.FromResult<IReadOnlyList<string>>([.. Genres]))
        );

    [Fact]
    public void The_placeholder_is_shown_only_while_nothing_is_selected()
    {
        RenderWithBlur([], () => { }).Find("input").GetAttribute("placeholder").ShouldBe("Kies een genre");
        RenderWithBlur(["Actie"], () => { }).Find("input").GetAttribute("placeholder").ShouldBeNullOrEmpty();
    }

    [Fact]
    public void Leaving_the_input_raises_the_blur_after_a_short_delay()
    {
        var blurred = 0;
        var cut = RenderWithBlur([], () => Interlocked.Increment(ref blurred));

        cut.Find("input").Blur();

        blurred.ShouldBe(0);
        cut.WaitForAssertion(() => blurred.ShouldBe(1), TimeSpan.FromSeconds(2));
    }
}
