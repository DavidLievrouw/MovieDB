using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using static Microsoft.Playwright.Assertions;

namespace MovieDB.BrowserTests;

[Collection(BrowserCollection.Name)]
public sealed class NewMovieTests
{
    private readonly BrowserFixture _fixture;

    public NewMovieTests(BrowserFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Leaving_the_genre_field_is_only_an_error_when_no_genre_is_chosen(int width, int height)
    {
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();
        var genreError = session.Page.Locator("[data-testid=genre-field] .mud-input-error");

        await session.ChipInput("genre-field").ClickAsync();
        await session.Field("Filmtitel").ClickAsync();
        await Expect(session.Page.GetByText("Kies minstens één genre.")).ToBeVisibleAsync();

        await session.AddChipAsync("genre-field", "Drama");
        await Expect(session.Page.GetByText("Kies minstens één genre.")).ToHaveCountAsync(0);
        await session.ChipInput("genre-field").ClickAsync();
        await session.Field("Filmtitel").ClickAsync();

        await Expect(genreError).ToHaveCountAsync(0);
        await Expect(session.Page.GetByText("Kies minstens één genre.")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Picking_the_first_genre_with_a_slow_mouse_click_selects_it_without_an_error(int width, int height)
    {
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();
        var input = session.ChipInput("genre-field");
        await Expect(input).ToHaveAttributeAsync("placeholder", "Kies een genre*");

        await input.ClickAsync();
        var item = session.Page.Locator(".mud-popover-open .mud-list-item").First;
        await item.WaitForAsync();
        var name = (await item.InnerTextAsync()).Trim();
        var box = (await item.BoundingBoxAsync())!;

        // The input blurs on mouse down; the click only counts when nothing moved or changed before the mouse is released.
        await session.Page.Mouse.MoveAsync(box.X + 20, box.Y + (box.Height / 2));
        await session.Page.Mouse.DownAsync();
        await Task.Delay(600, TestContext.Current.CancellationToken);
        await session.Page.Mouse.UpAsync();

        await Expect(session.Chips("genre-field").GetByText(name, new() { Exact = true })).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("Kies minstens één genre.")).ToHaveCountAsync(0);
        await Expect(session.Page.GetByText("Controleer de gemarkeerde velden.")).ToHaveCountAsync(0);
        await Expect(input).Not.ToHaveAttributeAsync("placeholder", "Kies een genre*");
    }

    [Fact]
    public async Task The_genre_box_and_the_duration_field_share_a_row_and_are_aligned_on_desktop()
    {
        await using var session = await _fixture.OpenAsync(1440, 900);
        await session.OpenNewAsync();
        var genre = session.ChipBox("genre-field");
        var duration = session.Field("Duur").Locator("xpath=ancestor::div[contains(@class,'mud-input')][1]");

        await AssertSameRowAsync(genre, duration);
        await session.AddChipAsync("genre-field", "Drama");
        await AssertSameRowAsync(genre, duration);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task The_genre_label_sits_inside_the_empty_box_like_the_other_labels_and_floats_when_used(
        int width,
        int height
    )
    {
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();
        var label = session.Page.Locator("[data-testid=genre-field] .chip-multi-select-label");
        var titleControl = session.Page.Locator(".mud-input-control").Filter(new() { HasText = "Filmtitel" }).First;
        var titleBox = (await titleControl.Locator(".mud-input").First.BoundingBoxAsync())!;
        var title = await TextLeftAsync(titleControl.Locator("label"));
        var box = (await session.ChipBox("genre-field").BoundingBoxAsync())!;

        await Expect(label).ToBeVisibleAsync();
        await Expect(label).ToHaveTextAsync("Genre*");
        var at = (await label.BoundingBoxAsync())!;
        (await TextLeftAsync(label) - box.X).ShouldBe(title - titleBox.X, 1);
        (at.Y + (at.Height / 2)).ShouldBe(box.Y + (box.Height / 2), 1);

        await session.ChipInput("genre-field").ClickAsync();
        await Expect(label).ToBeVisibleAsync();
        await Expect(session.Page.Locator("[data-testid=genre-field] legend")).ToHaveTextAsync("Genre*");

        // The border opens a gap for the floated label, and the label animates there instead of jumping.
        var legend = session.Page.Locator("[data-testid=genre-field] legend");
        await Expect(legend).ToHaveCSSAsync("max-width", "100%");
        (await label.EvaluateAsync<string>("el => getComputedStyle(el).transitionDuration")).ShouldContain("0.2s");
        await Task.Delay(400, TestContext.Current.CancellationToken);
        var floated = (await label.BoundingBoxAsync())!;
        var outline = (
            await session.Page.Locator("[data-testid=genre-field] .chip-multi-select-outline").BoundingBoxAsync()
        )!;
        (floated.Y + (floated.Height / 2)).ShouldBe(outline.Y + 5, 2);
    }

    [Fact]
    public async Task The_genre_filter_on_the_overview_has_no_label_like_the_search_box()
    {
        await using var session = await _fixture.OpenAsync(1440, 900);
        await session.GotoAsync("/films");
        var filter = session.Page.Locator(".films-genre-filter");

        await Expect(filter.Locator(".chip-multi-select-input")).ToBeVisibleAsync();
        await Expect(filter.Locator("legend")).ToHaveCountAsync(0);
        await Expect(filter.Locator(".chip-multi-select-label")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task The_placeholder_and_the_chips_are_vertically_centered_in_the_box(int width, int height)
    {
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();
        var box = session.ChipBox("genre-field");

        await AssertCenteredAsync(box, session.ChipInput("genre-field"));
        await session.AddChipAsync("genre-field", "Drama");
        await AssertCenteredAsync(box, session.Chips("genre-field").First);
        await AssertCenteredAsync(box, session.ChipInput("genre-field"));
    }

    private static async Task<double> TextLeftAsync(ILocator element) =>
        await element.EvaluateAsync<double>(
            "el => { const r = document.createRange(); r.selectNodeContents(el); return r.getBoundingClientRect().left; }"
        );

    private static async Task AssertCenteredAsync(ILocator box, ILocator inner)
    {
        var outer = await box.BoundingBoxAsync();
        outer.ShouldNotBeNull();
        (await BrowserSession.ContentCenterYAsync(inner)).ShouldBe(outer.Y + (outer.Height / 2), 1);
    }

    private static async Task AssertSameRowAsync(ILocator first, ILocator second)
    {
        var a = await first.BoundingBoxAsync();
        var b = await second.BoundingBoxAsync();
        a.ShouldNotBeNull();
        b.ShouldNotBeNull();
        b.Y.ShouldBe(a.Y, 1);
        b.Height.ShouldBe(a.Height, 1);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task The_chips_are_inside_the_box_and_only_their_x_button_removes_them(int width, int height)
    {
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();
        await session.AddChipAsync("genre-field", "Drama");
        await session.AddChipAsync("genre-field", "Thriller");

        var box = await session.ChipBox("genre-field").BoundingBoxAsync();
        box.ShouldNotBeNull();
        foreach (
            var inner in new[]
            {
                session.Chips("genre-field").First,
                session.Chips("genre-field").Last,
                session.ChipInput("genre-field"),
            }
        )
        {
            var inside = await inner.BoundingBoxAsync();
            inside.ShouldNotBeNull();
            inside.X.ShouldBeGreaterThanOrEqualTo(box.X);
            (inside.X + inside.Width).ShouldBeLessThanOrEqualTo(box.X + box.Width + 1);
            inside.Y.ShouldBeGreaterThanOrEqualTo(box.Y);
            (inside.Y + inside.Height).ShouldBeLessThanOrEqualTo(box.Y + box.Height + 1);
        }

        // Clicking the box itself focuses the input; Backspace in an empty input keeps the chips.
        await session
            .ChipBox("genre-field")
            .ClickAsync(
                new()
                {
                    Position = new() { X = box.Width - 6, Y = box.Height - 6 },
                }
            );
        await Expect(session.ChipInput("genre-field")).ToBeFocusedAsync();
        await session.Page.Keyboard.PressAsync("Escape");
        await session.Page.Keyboard.PressAsync("Backspace");
        await Expect(session.Chips("genre-field")).ToHaveTextAsync(["Drama", "Thriller"]);

        await session.Chips("genre-field").First.Locator(".mud-chip-close-button").ClickAsync();
        await Expect(session.Chips("genre-field")).ToHaveTextAsync(["Thriller"]);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Saving_an_empty_form_shows_the_validation_messages_and_stores_nothing(int width, int height)
    {
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();

        await session.SaveButton.ClickAsync();

        await Expect(session.Page.GetByText("Controleer de gemarkeerde velden.")).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("Filmtitel is verplicht.")).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("Kies minstens één genre.")).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("Geef minstens één regisseur op.")).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("Duur is verplicht.")).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("Jaar is verplicht.")).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("Totaal is verplicht.")).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("Film is verplicht.")).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("Beeld is verplicht.")).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("Muziek is verplicht.")).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("Geluid is verplicht.")).ToBeVisibleAsync();
        session.Page.Url.ShouldContain("/films/nieuw");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Out_of_range_values_are_rejected(int width, int height)
    {
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();
        await session.FillAsync("Filmtitel", BrowserFixture.Unique("Bereikfilm"));
        await session.AddChipAsync("genre-field", "Drama");
        await session.TypeChipAsync("director-field", "Synthetische Regisseur");
        await session.FillAsync("Duur", "1000");
        await session.FillAsync("Jaar", "1800");

        await session.SaveButton.ClickAsync();

        await Expect(session.Page.GetByText("Duur moet tussen 1 en 999 minuten liggen.")).ToBeVisibleAsync();
        await Expect(session.Page.GetByText(new Regex("^Jaar moet tussen 1888 en \\d{4} liggen\\.$")))
            .ToBeVisibleAsync();
        session.Page.Url.ShouldContain("/films/nieuw");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task A_form_without_scores_is_rejected_and_picking_a_score_clears_its_message(int width, int height)
    {
        var title = BrowserFixture.Unique("Scorefilm");
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();
        await session.FillAsync("Filmtitel", title);
        await session.AddChipAsync("genre-field", "Drama");
        await session.FillAsync("Duur", "100");
        await session.FillAsync("Jaar", "2002");
        await session.TypeChipAsync("director-field", "Synthetische Regisseur");
        await session.SetScoreAsync("Film", 7);

        await session.SaveButton.ClickAsync();

        await Expect(session.Page.GetByText("Totaal is verplicht.")).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("Film is verplicht.")).ToHaveCountAsync(0);
        session.Page.Url.ShouldContain("/films/nieuw");

        await session.SetScoreAsync("Totaal (geen gemiddelde)", 9);

        await Expect(session.Page.GetByText("Totaal is verplicht.")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task A_complete_form_creates_the_movie_and_opens_it(int width, int height)
    {
        var title = BrowserFixture.Unique("Nieuwe testfilm");
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();

        await session.FillAsync("Filmtitel", title);
        await session.AddChipAsync("genre-field", "Thriller");
        await session.AddChipAsync("genre-field", "Detective");
        await session.FillAsync("Duur", "123");
        await session.FillAsync("Jaar", "2001");
        await session.TypeChipAsync("director-field", "Synthetische Regisseur");
        await session.FillAsync("Gezien op/in", "Bioscoop");
        await session.FillAsync("Opmerkingen", "Een nieuwe, synthetische recensie.");
        await session.SetScoreAsync("Totaal (geen gemiddelde)", 8);
        await session.SetScoreAsync("Film", 7);
        await session.SetScoreAsync("Beeld", 6);
        await session.SetScoreAsync("Muziek", 5);
        await session.SetScoreAsync("Geluid", 4);
        await session.SaveButton.ClickAsync();

        await Expect(session.Page).ToHaveURLAsync(new Regex(@"/films/\d+"));
        await Expect(session.Page.Locator("h1.films-title")).ToHaveTextAsync(title);
        var id = int.Parse(new Regex(@"/films/(\d+)").Match(session.Page.Url).Groups[1].Value);
        var saved = await _fixture.FindMovieAsync(id);
        saved.ShouldNotBeNull();
        saved.Title.ShouldBe(title);
        saved.ReleaseYear.ShouldBe(2001);
        saved.Genres.ShouldBe(["Detective", "Thriller"]);
        saved.Medium.ShouldBe("Bioscoop");
        saved.OverallScore.ShouldBe(8);
        saved.StoryScore.ShouldBe(7);
        saved.Version.ShouldBe(1);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Starting_a_new_movie_from_the_edit_page_shows_an_empty_form_without_errors(int width, int height)
    {
        var id = await _fixture.SeedMovieAsync(BrowserFixture.Unique("Vorige film"));
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenEditAsync(id);
        await session.ChipInput("genre-field").ClickAsync();

        await session
            .Page.Locator(".films-record-nav")
            .GetByRole(AriaRole.Link, new() { Name = "Nieuwe film" })
            .ClickAsync();

        await Expect(session.Page).ToHaveURLAsync(new Regex("/films/nieuw"));
        await Expect(session.Page.Locator("h1.films-title")).ToHaveTextAsync("Nieuwe film");
        await Expect(session.Field("Filmtitel")).ToHaveValueAsync("");
        await Expect(session.Chips("genre-field")).ToHaveCountAsync(0);
        await Expect(session.ChipInput("genre-field")).ToHaveValueAsync("");
        await Expect(session.Chips("director-field")).ToHaveCountAsync(0);
        await Expect(session.ChipInput("director-field")).ToHaveValueAsync("");
        await Expect(session.Page.Locator(".films-fields .mud-input-error")).ToHaveCountAsync(0);
        await Expect(session.Page.Locator(".films-fields [aria-invalid=true]")).ToHaveCountAsync(0);
        await Expect(session.Page.GetByText("Controleer de gemarkeerde velden.")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task A_duplicate_is_reported_with_a_link_to_the_existing_movie(int width, int height)
    {
        var title = BrowserFixture.Unique("Dubbelfilm");
        var existing = await _fixture.SeedMovieAsync(title, year: 2005, director: "Synthetische Regisseur");
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();

        await session.FillAsync("Filmtitel", title.ToUpperInvariant());
        await session.AddChipAsync("genre-field", "Drama");
        await session.FillAsync("Duur", "100");
        await session.FillAsync("Jaar", "2005");
        await session.TypeChipAsync("director-field", "synthetische regisseur");
        await session.SetAllScoresAsync(7);
        await session.SaveButton.ClickAsync();

        await Expect(session.Page.GetByText("Deze film bestaat al (Jaar en Regisseurs zijn gelijk)."))
            .ToBeVisibleAsync();
        var link = session.Page.GetByRole(AriaRole.Link, new() { Name = "Bekijk bestaande film" });
        await Expect(link).ToHaveAttributeAsync("href", new Regex($"^/films/{existing}"));
        session.Page.Url.ShouldContain("/films/nieuw");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task The_same_directors_in_another_order_are_a_duplicate(int width, int height)
    {
        var title = BrowserFixture.Unique("Omgekeerd");
        await _fixture.SeedMovieAsync(title, year: 2006, director: "Eerste Regisseur|Tweede Regisseur");
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();
        await session.FillAsync("Filmtitel", title);
        await session.AddChipAsync("genre-field", "Drama");
        await session.FillAsync("Duur", "100");
        await session.FillAsync("Jaar", "2006");
        await session.TypeChipAsync("director-field", "Tweede Regisseur");
        await session.TypeChipAsync("director-field", "Eerste Regisseur");
        await session.SetAllScoresAsync(7);

        await session.SaveButton.ClickAsync();

        await Expect(session.Page.GetByText("Deze film bestaat al (Jaar en Regisseurs zijn gelijk)."))
            .ToBeVisibleAsync();
        session.Page.Url.ShouldContain("/films/nieuw");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Typed_directors_are_committed_by_a_comma_by_Tab_and_by_leaving_the_field(int width, int height)
    {
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();
        var input = session.ChipInput("director-field");

        await input.ClickAsync();
        await input.PressSequentiallyAsync("Eerste Persoon,");
        await Expect(session.Chips("director-field")).ToHaveCountAsync(1);

        await input.PressSequentiallyAsync("Tweede Persoon");
        await session.Page.Keyboard.PressAsync("Tab");
        await Expect(session.Chips("director-field")).ToHaveCountAsync(2);

        await input.ClickAsync();
        await input.PressSequentiallyAsync("Derde Persoon");
        await session.Field("Filmtitel").ClickAsync();
        await Expect(session.Chips("director-field")).ToHaveCountAsync(3);
        await Expect(session.Chips("director-field").Nth(2)).ToContainTextAsync("Derde Persoon");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task A_movie_with_two_genres_and_two_directors_one_of_them_new_is_saved_in_order(int width, int height)
    {
        var title = BrowserFixture.Unique("Meerdere regisseurs");
        var known = BrowserFixture.Unique("Bekende Regisseur");
        var brandNew = BrowserFixture.Unique("Nieuwe Regisseur");
        await _fixture.SeedMovieAsync(BrowserFixture.Unique("Bekend werk"), director: known);
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();
        await session.FillAsync("Filmtitel", title);
        await session.AddChipAsync("genre-field", "Thriller");
        await session.AddChipAsync("genre-field", "Detective");
        await session.FillAsync("Duur", "110");
        await session.FillAsync("Jaar", "2011");
        await session.TypeChipAsync("director-field", brandNew);
        await session.TypeChipAsync("director-field", known);
        await session.SetAllScoresAsync(6);

        await session.SaveButton.ClickAsync();

        await Expect(session.Page).ToHaveURLAsync(new Regex(@"/films/\d+"));
        var id = int.Parse(new Regex(@"/films/(\d+)").Match(session.Page.Url).Groups[1].Value);
        var saved = await _fixture.FindMovieAsync(id);
        saved.ShouldNotBeNull();
        saved.Genres.ShouldBe(["Detective", "Thriller"]);
        saved.Directors.ShouldBe([brandNew, known]);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task The_same_title_and_year_with_another_director_asks_for_confirmation(int width, int height)
    {
        var title = BrowserFixture.Unique("Zelfde titel");
        await _fixture.SeedMovieAsync(title, year: 2010, director: "Eerste Regisseur");
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();
        await session.FillAsync("Filmtitel", title);
        await session.AddChipAsync("genre-field", "Drama");
        await session.FillAsync("Duur", "100");
        await session.FillAsync("Jaar", "2010");
        await session.TypeChipAsync("director-field", "Tweede Regisseur");
        await session.SetAllScoresAsync(7);
        var dialog = session.Page.Locator(".mud-dialog");

        // Back: the form stays as it was and nothing is stored.
        await session.SaveButton.ClickAsync();
        await Expect(dialog.GetByText("Bestaat deze film al?")).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Terug naar formulier" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        session.Page.Url.ShouldContain("/films/nieuw");
        await Expect(session.Chips("director-field")).ToHaveTextAsync(new Regex("^Tweede Regisseur"));

        // Save anyway: the second movie is created.
        await session.SaveButton.ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Toch opslaan" }).ClickAsync();
        await Expect(session.Page).ToHaveURLAsync(new Regex(@"/films/\d+"));
        await Expect(session.Page.Locator("h1.films-title")).ToHaveTextAsync(title);
    }
}
