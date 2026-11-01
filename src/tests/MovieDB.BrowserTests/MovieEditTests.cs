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
}
