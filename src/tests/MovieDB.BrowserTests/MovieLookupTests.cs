using System.Text.RegularExpressions;
using Microsoft.Playwright;
using MovieDB.Common;
using MovieDB.Movies.Lookup;
using Shouldly;
using static Microsoft.Playwright.Assertions;

namespace MovieDB.BrowserTests;

[Collection(BrowserCollection.Name)]
public sealed class MovieLookupTests
{
    private readonly BrowserFixture _fixture;

    public MovieLookupTests(BrowserFixture fixture)
    {
        _fixture = fixture;
        _fixture.Lookup.Reset();
    }

    private static LookupHit Hit(string title, int id = 949, int? year = 1995, string? original = null) =>
        new(
            id,
            title,
            original ?? title,
            year,
            "Een synthetische samenvatting.",
            new Uri($"https://www.themoviedb.org/movie/{id}")
        );

    private static LookupDetails Details(
        string title,
        IReadOnlyList<string> directors,
        int? runtime = 120,
        int? year = 1995,
        int id = 949
    ) =>
        new(
            title,
            year,
            runtime,
            ["Drama", "Thriller"],
            directors,
            new Uri($"https://www.themoviedb.org/movie/{id}"),
            new Uri("https://www.imdb.com/title/tt0113277/")
        );

    private void Script(LookupHit hit, LookupDetails details)
    {
        _fixture.Lookup.Search = _ => Result<IReadOnlyList<LookupHit>>.Ok([hit]);
        _fixture.Lookup.Details = _ => Result<LookupDetails>.Ok(details);
    }

    private static async Task SearchAsync(BrowserSession session, string text)
    {
        await session.Page.GetByTestId("lookup-open").ClickAsync();
        var dialog = session.Page.Locator(".mud-dialog");
        await Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByTestId("lookup-search").FillAsync(text);
    }

    private static async Task PickAsync(BrowserSession session, string name)
    {
        var dialog = session.Page.GetByRole(AriaRole.Dialog, new() { Name = "Film online zoeken" });
        await dialog.GetByRole(AriaRole.Button, new() { Name = $"Kies {name}" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Searching_and_picking_a_film_fills_the_form_and_it_can_be_saved(int width, int height)
    {
        var title = BrowserFixture.Unique("Zoekfilm");
        var known = BrowserFixture.Unique("Bekende Regisseur");
        var fresh = BrowserFixture.Unique("Nieuwe Regisseur");
        await _fixture.SeedMovieAsync(BrowserFixture.Unique("Bestaand"), director: known);
        Script(Hit(title), Details(title, [known.ToUpperInvariant(), fresh]));
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();

        await SearchAsync(session, title);
        var dialog = session.Page.Locator(".mud-dialog");
        var link = dialog.GetByRole(AriaRole.Link, new() { Name = $"Bekijk {title} op TMDB" });
        await Expect(link).ToHaveAttributeAsync("href", "https://www.themoviedb.org/movie/949");
        await Expect(link).ToHaveAttributeAsync("target", "_blank");
        await Expect(link).ToHaveAttributeAsync("rel", "noopener noreferrer");
        await Expect(dialog.GetByTestId("tmdb-attribution")).ToBeVisibleAsync();
        await PickAsync(session, title);

        await Expect(session.Field("Filmtitel")).ToHaveValueAsync(title);
        await Expect(session.Field("Duur")).ToHaveValueAsync("120");
        await Expect(session.Field("Jaar")).ToHaveValueAsync("1995");
        await Expect(session.Chips("genre-field")).ToHaveCountAsync(2);
        await Expect(session.Chips("director-field")).ToHaveCountAsync(2);
        await Expect(session.Chips("director-field").First).ToContainTextAsync(known);
        await Expect(session.Page.Locator("[data-testid=director-field] .chip-multi-select-new")).ToHaveCountAsync(1);
        await Expect(session.Page.GetByTestId("lookup-success")).ToBeVisibleAsync();
        var source = session.Page.GetByTestId("lookup-source");
        await Expect(source).ToContainTextAsync(title);
        await Expect(source.GetByRole(AriaRole.Link, new() { Name = "IMDb ↗" }))
            .ToHaveAttributeAsync("href", "https://www.imdb.com/title/tt0113277/");
        await Expect(session.Page.Locator(".films-savebar-state")).ToHaveTextAsync("Niet-opgeslagen wijzigingen");
        await Expect(session.Page.GetByTestId("tmdb-attribution")).ToBeVisibleAsync();

        await session.SetAllScoresAsync(7);
        await session.SaveButton.ClickAsync();

        await Expect(session.Page).ToHaveURLAsync(new Regex(@"/films/\d+"));
        var id = int.Parse(new Regex(@"/films/(\d+)").Match(session.Page.Url).Groups[1].Value);
        var saved = await _fixture.FindMovieAsync(id);
        saved.ShouldNotBeNull();
        saved.Title.ShouldBe(title);
        saved.Directors.ShouldBe([known, fresh]);
        saved.Genres.ShouldBe(["Drama", "Thriller"]);
        saved.ReleaseYear.ShouldBe(1995);
        await Expect(session.Page.GetByTestId("lookup-open")).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task A_missing_runtime_is_reported_and_left_for_the_user(int width, int height)
    {
        var title = BrowserFixture.Unique("Zonderduur");
        Script(Hit(title), Details(title, ["Een Regisseur"], runtime: null));
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();

        await SearchAsync(session, title);
        await PickAsync(session, title);

        await Expect(session.Field("Duur")).ToHaveValueAsync("");
        await Expect(session.Page.GetByTestId("lookup-success")).ToContainTextAsync(LookupMessages.RuntimeMissing);
        await Expect(session.Field("Duur")).ToBeFocusedAsync();
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task A_film_that_exists_already_shows_the_duplicate_hint(int width, int height)
    {
        var title = BrowserFixture.Unique("Dubbelfilm");
        var director = BrowserFixture.Unique("Dubbele Regisseur");
        await _fixture.SeedMovieAsync(title, year: 1995, director: director);
        Script(Hit(title), Details(title, [director]));
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();

        await SearchAsync(session, title);
        await PickAsync(session, title);

        await Expect(session.Page.GetByText("Deze film bestaat al (Jaar en Regisseurs zijn gelijk)."))
            .ToBeVisibleAsync();
        await Expect(session.Page.GetByRole(AriaRole.Link, new() { Name = "Bekijk bestaande film" }))
            .ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Existing_input_is_only_overwritten_after_confirmation(int width, int height)
    {
        var title = BrowserFixture.Unique("Overschrijf");
        Script(Hit(title), Details(title, ["Een Regisseur"]));
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();
        await session.FillAsync("Duur", "88");

        await SearchAsync(session, title);
        await PickAsync(session, title);
        var dialog = session.Page.GetByRole(AriaRole.Dialog, new() { Name = "Bestaande gegevens overschrijven?" });
        await Expect(dialog).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Annuleren" }).ClickAsync();

        await Expect(session.Field("Duur")).ToHaveValueAsync("88");
        await Expect(session.Field("Filmtitel")).ToHaveValueAsync("");
        await Expect(session.Page.GetByTestId("lookup-source")).ToHaveCountAsync(0);

        await SearchAsync(session, title);
        await PickAsync(session, title);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Overschrijven" }).ClickAsync();

        await Expect(session.Field("Duur")).ToHaveValueAsync("120");
        await Expect(session.Field("Filmtitel")).ToHaveValueAsync(title);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task A_typed_title_alone_is_not_an_overwrite_and_is_the_initial_search(int width, int height)
    {
        var title = BrowserFixture.Unique("Titelalleen");
        Script(Hit(title), Details(title, ["Een Regisseur"]));
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();
        await session.FillAsync("Filmtitel", title);

        await session.Page.GetByTestId("lookup-open").ClickAsync();
        var dialog = session.Page.Locator(".mud-dialog");
        await Expect(dialog.GetByTestId("lookup-hit")).ToHaveCountAsync(1);
        await Expect(dialog.GetByTestId("lookup-search")).ToHaveValueAsync(title);
        await PickAsync(session, title);

        await Expect(session.Page.GetByText("Bestaande gegevens overschrijven?")).ToHaveCountAsync(0);
        await Expect(session.Field("Duur")).ToHaveValueAsync("120");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Failures_are_explained_and_a_failed_details_call_can_be_retried(int width, int height)
    {
        var title = BrowserFixture.Unique("Foutfilm");
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();
        var dialog = session.Page.Locator(".mud-dialog");
        var status = dialog.GetByTestId("lookup-status");

        await SearchAsync(session, title);
        await Expect(status).ToContainTextAsync("Geen films gevonden");

        _fixture.Lookup.Search = _ => new Error(ErrorKind.Unavailable, "down");
        await dialog.GetByTestId("lookup-search").FillAsync(title + " x");
        await Expect(status).ToHaveTextAsync(LookupMessages.Unavailable);

        var calls = 0;
        _fixture.Lookup.Search = _ => Result<IReadOnlyList<LookupHit>>.Ok([Hit(title)]);
        _fixture.Lookup.Details = _ =>
            ++calls == 1
                ? new Error(ErrorKind.Unavailable, "down")
                : Result<LookupDetails>.Ok(Details(title, ["Een Regisseur"]));
        await dialog.GetByTestId("lookup-search").FillAsync(title);
        await dialog.GetByRole(AriaRole.Button, new() { Name = $"Kies {title}" }).ClickAsync();
        await Expect(dialog.GetByTestId("lookup-details-error")).ToContainTextAsync(LookupMessages.DetailsFailed);
        await Expect(dialog.GetByTestId("lookup-hit")).ToHaveCountAsync(1);

        await dialog.GetByRole(AriaRole.Button, new() { Name = "Opnieuw proberen" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        await Expect(session.Field("Filmtitel")).ToHaveValueAsync(title);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Escape_closes_the_dialog_and_the_dialog_does_not_scroll_sideways(int width, int height)
    {
        var title = BrowserFixture.Unique("Toetsenbord");
        Script(Hit(title), Details(title, ["Een Regisseur"]));
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();

        await SearchAsync(session, title);
        var dialog = session.Page.Locator(".mud-dialog");
        await Expect(dialog.GetByTestId("lookup-hit")).ToHaveCountAsync(1);
        (
            await session.Page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth")
        ).ShouldBeFalse();
        (await dialog.EvaluateAsync<bool>("el => el.scrollWidth > el.clientWidth")).ShouldBeFalse();

        await session.Page.Keyboard.PressAsync("Escape");

        await Expect(dialog).ToBeHiddenAsync();
        await Expect(session.Field("Filmtitel")).ToHaveValueAsync("");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task The_page_has_no_sideways_scroll_and_the_attribution_clears_the_save_bar(int width, int height)
    {
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();

        (
            await session.Page.EvaluateAsync<bool>("document.documentElement.scrollWidth > window.innerWidth")
        ).ShouldBeFalse();
        var attribution = session.Page.GetByTestId("tmdb-attribution");
        await attribution.ScrollIntoViewIfNeededAsync();
        var box = (await attribution.BoundingBoxAsync())!;
        var bar = (await session.Page.Locator(".films-savebar").BoundingBoxAsync())!;
        (box.Y + box.Height).ShouldBeLessThanOrEqualTo(bar.Y);
    }

    [Fact]
    public async Task The_button_is_disabled_with_an_explanation_when_lookup_is_not_configured()
    {
        _fixture.Lookup.IsConfigured = false;
        await using var session = await _fixture.OpenAsync(1440, 900);
        await session.OpenNewAsync();

        var button = session.Page.GetByTestId("lookup-open");

        await Expect(button).ToBeDisabledAsync();
    }

    [Fact]
    public async Task The_button_and_the_attribution_are_absent_when_editing_an_existing_film()
    {
        var id = await _fixture.SeedMovieAsync(BrowserFixture.Unique("Bestaandefilm"));
        await using var session = await _fixture.OpenAsync(1440, 900);
        await session.OpenEditAsync(id);

        await Expect(session.Page.GetByTestId("lookup-open")).ToHaveCountAsync(0);
        await Expect(session.Page.GetByTestId("tmdb-attribution")).ToHaveCountAsync(0);
    }
}
