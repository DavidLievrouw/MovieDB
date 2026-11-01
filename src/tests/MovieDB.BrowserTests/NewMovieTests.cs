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
    public async Task Saving_an_empty_form_shows_the_validation_messages_and_stores_nothing(int width, int height)
    {
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenNewAsync();

        await session.SaveButton.ClickAsync();

        await Expect(session.Page.GetByText("Controleer de gemarkeerde velden.")).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("Filmtitel is verplicht.")).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("Kies minstens één genre.")).ToBeVisibleAsync();
        await Expect(session.Page.GetByText("Regisseur is verplicht.")).ToBeVisibleAsync();
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
        await session.FillAsync("Regisseur", "Synthetische Regisseur");
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
        await session.FillAsync("Regisseur", "Synthetische Regisseur");
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
        await session.FillAsync("Regisseur", "Synthetische Regisseur");
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
        await Expect(session.Field("Regisseur")).ToHaveValueAsync("");
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
        await session.FillAsync("Regisseur", "synthetische regisseur");
        await session.SetAllScoresAsync(7);
        await session.SaveButton.ClickAsync();

        await Expect(session.Page.GetByText("Deze film bestaat al (Jaar en Regisseur zijn gelijk)."))
            .ToBeVisibleAsync();
        var link = session.Page.GetByRole(AriaRole.Link, new() { Name = "Bekijk bestaande film" });
        await Expect(link).ToHaveAttributeAsync("href", new Regex($"^/films/{existing}"));
        session.Page.Url.ShouldContain("/films/nieuw");
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
        await session.FillAsync("Regisseur", "Tweede Regisseur");
        await session.SetAllScoresAsync(7);
        var dialog = session.Page.Locator(".mud-dialog");

        // Back: the form stays as it was and nothing is stored.
        await session.SaveButton.ClickAsync();
        await Expect(dialog.GetByText("Bestaat deze film al?")).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Terug naar formulier" }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        session.Page.Url.ShouldContain("/films/nieuw");
        await Expect(session.Field("Regisseur")).ToHaveValueAsync("Tweede Regisseur");

        // Save anyway: the second movie is created.
        await session.SaveButton.ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Toch opslaan" }).ClickAsync();
        await Expect(session.Page).ToHaveURLAsync(new Regex(@"/films/\d+"));
        await Expect(session.Page.Locator("h1.films-title")).ToHaveTextAsync(title);
    }
}
