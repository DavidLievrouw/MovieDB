using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using static Microsoft.Playwright.Assertions;

namespace MovieDB.BrowserTests;

[Collection(BrowserCollection.Name)]
public sealed class MovieEditTests
{
    private readonly BrowserFixture _fixture;

    public MovieEditTests(BrowserFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Editing_a_movie_and_saving_stores_the_changes(int width, int height)
    {
        var title = BrowserFixture.Unique("Bewerkfilm");
        var id = await _fixture.SeedMovieAsync(title, overallScore: 6);
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenEditAsync(id);
        await Expect(session.Field("Filmtitel")).ToHaveValueAsync(title);
        await Expect(session.Page.Locator(".films-savebar-state")).ToHaveTextAsync("");

        await session.FillAsync("Opmerkingen", "Een aangepaste, synthetische recensie.");
        await session.SetScoreAsync("Totaal (geen gemiddelde)", 9);
        await session.SetScoreAsync("Film", 8);

        await Expect(session.Page.Locator(".films-savebar-state")).ToHaveTextAsync("Niet-opgeslagen wijzigingen");
        await session.SaveButton.ClickAsync();

        await Expect(session.Page.GetByText("Film opgeslagen.")).ToBeVisibleAsync();
        await Expect(session.Page.Locator(".films-savebar-state")).ToHaveTextAsync("");
        var saved = await _fixture.FindMovieAsync(id);
        saved.ShouldNotBeNull();
        saved.Review.ShouldBe("Een aangepaste, synthetische recensie.");
        saved.OverallScore.ShouldBe(9);
        saved.StoryScore.ShouldBe(8);
        saved.Version.ShouldBe(2);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task A_movie_with_a_missing_score_must_be_completed_before_it_can_be_saved(int width, int height)
    {
        var title = BrowserFixture.Unique("Oudefilm");
        var id = await _fixture.SeedMovieAsync(title);
        await _fixture.ClearStoryScoreAsync(id);
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenEditAsync(id);
        await session.FillAsync("Opmerkingen", "Aangevuld.");

        await session.SaveButton.ClickAsync();

        await Expect(session.Page.GetByText("Film is verplicht.")).ToBeVisibleAsync();
        (await _fixture.FindMovieAsync(id))!.Version.ShouldBe(1);

        await session.SetScoreAsync("Film", 8);
        await session.SaveButton.ClickAsync();

        await Expect(session.Page.GetByText("Film opgeslagen.")).ToBeVisibleAsync();
        (await _fixture.FindMovieAsync(id))!.StoryScore.ShouldBe(8);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Cancelling_restores_the_saved_values(int width, int height)
    {
        var title = BrowserFixture.Unique("Annuleerfilm");
        var id = await _fixture.SeedMovieAsync(title);
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenEditAsync(id);

        await session.FillAsync("Filmtitel", title + " gewijzigd");
        await session.CancelButton.ClickAsync();

        await Expect(session.Field("Filmtitel")).ToHaveValueAsync(title);
        await Expect(session.Page.Locator(".films-savebar-state")).ToHaveTextAsync("");
        (await _fixture.FindMovieAsync(id))!.Title.ShouldBe(title);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task A_movie_that_was_changed_elsewhere_gives_a_conflict_that_can_be_reloaded(int width, int height)
    {
        var title = BrowserFixture.Unique("Conflictfilm");
        var id = await _fixture.SeedMovieAsync(title);
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenEditAsync(id);
        await using var other = await _fixture.OpenAsync(width, height);
        await other.OpenEditAsync(id);
        await other.FillAsync("Opmerkingen", "Wijziging van de andere sessie.");
        await other.SaveButton.ClickAsync();
        await Expect(other.Page.GetByText("Film opgeslagen.")).ToBeVisibleAsync();

        await session.FillAsync("Opmerkingen", "Mijn eigen wijziging.");
        await session.SaveButton.ClickAsync();

        await Expect(session.Page.GetByText("Deze film is intussen gewijzigd.")).ToBeVisibleAsync();
        (await _fixture.FindMovieAsync(id))!.Review.ShouldBe("Wijziging van de andere sessie.");

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Herladen" }).ClickAsync();

        await Expect(session.Page.GetByText("Deze film is intussen gewijzigd.")).ToBeHiddenAsync();
        await Expect(session.Field("Opmerkingen")).ToHaveValueAsync("Wijziging van de andere sessie.");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task An_unknown_movie_shows_a_not_found_message(int width, int height)
    {
        await using var session = await _fixture.OpenAsync(width, height);

        await session.GotoAsync("/films/2000000000");

        await Expect(session.Page.GetByText("Film niet gevonden.")).ToBeVisibleAsync();
        await Expect(session.Page.GetByRole(AriaRole.Link, new() { Name = "Naar overzicht" })).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task The_record_navigation_walks_through_the_search_results(int width, int height)
    {
        var prefix = BrowserFixture.Unique("Navfilm");
        // The default order is the viewing date, newest first.
        var newest = await _fixture.SeedMovieAsync(prefix + " A", watchedOn: new DateOnly(2024, 3, 3));
        var middle = await _fixture.SeedMovieAsync(prefix + " B", watchedOn: new DateOnly(2024, 2, 2));
        var oldest = await _fixture.SeedMovieAsync(prefix + " C", watchedOn: new DateOnly(2024, 1, 1));
        await using var session = await _fixture.OpenAsync(width, height);

        await session.OpenEditAsync(newest, "?q=" + Uri.EscapeDataString(prefix));

        var position = session.Page.Locator(".films-position");
        await Expect(position).ToHaveTextAsync("1 van 3");
        await Expect(session.Page.GetByLabel("Eerste film")).ToBeDisabledAsync();
        await Expect(session.Page.GetByLabel("Vorige film")).ToBeDisabledAsync();

        await session.Page.GetByLabel("Volgende film").ClickAsync();
        await Expect(session.Page).ToHaveURLAsync(new Regex($"/films/{middle}"));
        await Expect(session.Page.Locator("h1.films-title")).ToHaveTextAsync(prefix + " B");
        await Expect(position).ToHaveTextAsync("2 van 3");

        await session.Page.GetByLabel("Laatste film").ClickAsync();
        await Expect(session.Page).ToHaveURLAsync(new Regex($"/films/{oldest}"));
        await Expect(position).ToHaveTextAsync("3 van 3");
        await Expect(session.Page.GetByLabel("Volgende film")).ToBeDisabledAsync();

        await session.Page.GetByLabel("Vorige film").ClickAsync();
        await Expect(session.Page.Locator("h1.films-title")).ToHaveTextAsync(prefix + " B");

        await session.Page.GetByLabel("Eerste film").ClickAsync();
        await Expect(session.Page).ToHaveURLAsync(new Regex($"/films/{newest}"));
        await Expect(position).ToHaveTextAsync("1 van 3");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Leaving_with_unsaved_changes_asks_what_to_do(int width, int height)
    {
        var title = BrowserFixture.Unique("Dialoogfilm");
        var id = await _fixture.SeedMovieAsync(title);
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenEditAsync(id);
        await session.FillAsync("Opmerkingen", "Nog niet opgeslagen.");
        var dialog = session.Page.Locator(".mud-dialog");

        // Stay: the page and the edit are kept.
        await session.Page.Locator("a.films-back").ClickAsync();
        await Expect(dialog.GetByText("Niet-opgeslagen wijzigingen")).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Blijven" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(session.Field("Opmerkingen")).ToHaveValueAsync("Nog niet opgeslagen.");
        session.Page.Url.ShouldContain($"/films/{id}");

        // Save: stored, then leaves.
        await session.Page.Locator("a.films-back").ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Opslaan", Exact = true }).ClickAsync();
        await Expect(session.Page).ToHaveURLAsync(new Regex("/films(\\?.*)?$"));
        (await _fixture.FindMovieAsync(id))!.Review.ShouldBe("Nog niet opgeslagen.");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Discarding_unsaved_changes_leaves_without_saving(int width, int height)
    {
        var title = BrowserFixture.Unique("Weggooifilm");
        var id = await _fixture.SeedMovieAsync(title);
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenEditAsync(id);
        await session.FillAsync("Opmerkingen", "Dit wordt weggegooid.");

        await session.Page.Locator("a.films-back").ClickAsync();
        await session
            .Page.Locator(".mud-dialog")
            .GetByRole(AriaRole.Button, new() { Name = "Niet opslaan" })
            .ClickAsync();

        await Expect(session.Page).ToHaveURLAsync(new Regex("/films(\\?.*)?$"));
        (await _fixture.FindMovieAsync(id))!.Review.ShouldBe("Synthetische recensie.");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Saving_with_the_keyboard_shortcut_works(int width, int height)
    {
        var title = BrowserFixture.Unique("Sneltoetsfilm");
        var id = await _fixture.SeedMovieAsync(title);
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenEditAsync(id);
        await session.Field("Opmerkingen").FillAsync("Via sneltoets.");

        await session.Page.Keyboard.PressAsync("Control+s");

        await Expect(session.Page.GetByText("Film opgeslagen.")).ToBeVisibleAsync();
        (await _fixture.FindMovieAsync(id))!.Review.ShouldBe("Via sneltoets.");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Leaving_the_genre_field_of_a_movie_that_has_genres_shows_no_error(int width, int height)
    {
        var id = await _fixture.SeedMovieAsync(BrowserFixture.Unique("Genrefout"), genres: ["Drama", "Actie"]);
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenEditAsync(id);

        await session.ChipInput("genre-field").ClickAsync();
        await session.Field("Filmtitel").ClickAsync();

        await Expect(session.Page.Locator("[data-testid=genre-field] .mud-input-error")).ToHaveCountAsync(0);
        await Expect(session.Page.GetByText("Kies minstens één genre.")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task The_genres_of_a_movie_can_be_changed_and_are_limited_to_four(int width, int height)
    {
        var title = BrowserFixture.Unique("Genrefilm");
        var id = await _fixture.SeedMovieAsync(title, genres: ["Drama", "Actie"]);
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenEditAsync(id);
        await Expect(session.Chips("genre-field")).ToHaveTextAsync(["Actie", "Drama"]);

        await session
            .Chips("genre-field")
            .Filter(new() { HasText = "Actie" })
            .Locator(".mud-chip-close-button")
            .ClickAsync();
        await session.AddChipAsync("genre-field", "Komedie");
        await session.AddChipAsync("genre-field", "Thriller");
        await session.AddChipAsync("genre-field", "Western");

        await Expect(session.Chips("genre-field")).ToHaveCountAsync(4);
        await Expect(session.ChipInput("genre-field")).ToBeDisabledAsync();
        await session.SaveButton.ClickAsync();

        await Expect(session.Page.GetByText("Film opgeslagen.")).ToBeVisibleAsync();
        var saved = await _fixture.FindMovieAsync(id);
        saved.ShouldNotBeNull();
        saved.Genres.ShouldBe(["Drama", "Komedie", "Thriller", "Western"]);
        saved.Version.ShouldBe(2);
    }

    [Fact]
    public async Task On_a_phone_the_scores_are_toggle_buttons_that_fit_the_screen_and_the_save_bar_ends_the_page()
    {
        var id = await _fixture.SeedMovieAsync(BrowserFixture.Unique("Telefoonfilm"), overallScore: 6);
        await using var session = await _fixture.OpenPhoneAsync();
        await session.OpenEditAsync(id);

        var group = session.ScoreGroup("Totaal (geen gemiddelde)");
        await group.ScrollIntoViewIfNeededAsync();
        await Expect(group.Locator("div.mud-select")).ToHaveCountAsync(0);
        var items = group.Locator(".score-input-toggles .mud-toggle-item");
        await Expect(items).ToHaveCountAsync(10);
        var first = (await items.First.BoundingBoxAsync())!;
        var last = (await items.Last.BoundingBoxAsync())!;
        first.X.ShouldBeGreaterThanOrEqualTo(0);
        (last.X + last.Width).ShouldBeLessThanOrEqualTo(412);
        first.Height.ShouldBeGreaterThanOrEqualTo(44);
        await items.Nth(8).TapAsync();
        await Expect(items.Nth(8)).ToHaveClassAsync(new Regex("mud-toggle-item-selected"));
        await Expect(session.Page.Locator(".films-savebar-state")).ToHaveTextAsync("Niet-opgeslagen wijzigingen");

        await session.Page.EvaluateAsync("window.scrollTo(0, document.documentElement.scrollHeight)");
        var bar = (await session.Page.Locator(".films-savebar").BoundingBoxAsync())!;
        var viewportHeight = await session.Page.EvaluateAsync<double>("window.innerHeight");
        (bar.Y + bar.Height).ShouldBe((float)viewportHeight, 1f);
        (
            await session.Page.EvaluateAsync<double>("document.documentElement.scrollHeight - scrollY - innerHeight")
        ).ShouldBeLessThanOrEqualTo(1);
    }
}
