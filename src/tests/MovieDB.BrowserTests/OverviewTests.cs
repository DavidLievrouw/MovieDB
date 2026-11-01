using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using static Microsoft.Playwright.Assertions;

namespace MovieDB.BrowserTests;

[Collection(BrowserCollection.Name)]
public sealed class OverviewTests
{
    private readonly BrowserFixture _fixture;

    public OverviewTests(BrowserFixture fixture)
    {
        _fixture = fixture;
    }

    private static ILocator ResultLinks(BrowserSession session) =>
        session.Page.Locator("a.films-link:visible, a.films-card:visible");

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task The_search_box_filters_by_title_after_a_debounce_and_updates_the_url(int width, int height)
    {
        var needle = BrowserFixture.Unique("Zoekfilm");
        await _fixture.SeedMovieAsync(needle + " Eerste");
        await _fixture.SeedMovieAsync(needle + " Tweede");
        await _fixture.SeedMovieAsync(BrowserFixture.Unique("Andere film"));
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenOverviewAsync();

        await session.Page.GetByLabel("Zoek op titel").FillAsync(needle);

        await Expect(session.Page).ToHaveURLAsync(new Regex("[?&]q="));
        await Expect(session.Page.Locator(".films-total-value")).ToHaveTextAsync("2");
        await Expect(ResultLinks(session)).ToHaveCountAsync(2);
        await Expect(ResultLinks(session).First).ToContainTextAsync(needle);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task A_search_without_matches_shows_the_empty_state_and_can_be_cleared(int width, int height)
    {
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenOverviewAsync("?q=" + Guid.NewGuid().ToString("N"));

        await Expect(session.Page.GetByText("Geen films gevonden voor deze zoekopdracht.")).ToBeVisibleAsync();

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Zoekopdracht wissen" }).ClickAsync();

        await Expect(session.Page.GetByText("Geen films gevonden voor deze zoekopdracht.")).ToBeHiddenAsync();
        await Expect(session.Page).Not.ToHaveURLAsync(new Regex("[?&]q="));
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task A_search_result_opens_the_movie_and_the_back_link_returns_to_the_same_search(
        int width,
        int height
    )
    {
        var title = BrowserFixture.Unique("Openfilm");
        await _fixture.SeedMovieAsync(title);
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenOverviewAsync("?q=" + Uri.EscapeDataString(title));

        await ResultLinks(session).First.ClickAsync();

        await Expect(session.Page).ToHaveURLAsync(new Regex(@"/films/\d+"));
        await Expect(session.Page.Locator("h1.films-title")).ToHaveTextAsync(title);
        await session.WaitForFormAsync();

        await session.Page.Locator("a.films-back").ClickAsync();

        await Expect(session.Page).ToHaveURLAsync(new Regex(@"/films\?q="));
        await session.WaitForOverviewAsync();
        await Expect(ResultLinks(session)).ToHaveCountAsync(1);
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task The_new_movie_button_opens_an_empty_form_with_todays_date(int width, int height)
    {
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenOverviewAsync();

        await session
            .Page.Locator("a:has-text('Nieuwe film'):visible, a[aria-label='Nieuwe film']:visible")
            .First.ClickAsync();

        await Expect(session.Page).ToHaveURLAsync(new Regex("/films/nieuw"));
        await session.WaitForFormAsync();
        await Expect(session.Field("Filmtitel")).ToHaveValueAsync("");
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");
        var today = TimeZoneInfo
            .ConvertTime(DateTimeOffset.UtcNow, zone)
            .ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
        await Expect(session.Field("Datum")).ToHaveValueAsync(today);
    }

    [Fact]
    public async Task The_search_box_and_the_genre_filter_are_aligned_on_desktop()
    {
        await _fixture.SeedMovieAsync(BrowserFixture.Unique("Uitlijning"), genres: ["Western"]);
        await using var session = await _fixture.OpenAsync(1440, 900);
        await session.OpenOverviewAsync();
        var search = session.Page.Locator(".films-search");
        var genre = session.Page.Locator("[data-testid=genre-filter] .mud-input-control").First;

        await AssertSameRowAsync(search, genre);
        await session.AddChipAsync("genre-filter", "Western");

        await AssertSameRowAsync(search, genre);
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
    public async Task The_genre_filter_keeps_the_films_that_have_all_chosen_genres_and_updates_the_url(
        int width,
        int height
    )
    {
        var prefix = BrowserFixture.Unique("Filterfilm");
        await _fixture.SeedMovieAsync(prefix + " Beide", genres: ["Western", "Horror"]);
        await _fixture.SeedMovieAsync(prefix + " Alleen western", genres: ["Western"]);
        await _fixture.SeedMovieAsync(prefix + " Alleen horror", genres: ["Horror"]);
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenOverviewAsync("?q=" + Uri.EscapeDataString(prefix));
        await Expect(ResultLinks(session)).ToHaveCountAsync(3);

        await session.AddChipAsync("genre-filter", "Western");
        await Expect(ResultLinks(session)).ToHaveCountAsync(2);
        await session.AddChipAsync("genre-filter", "Horror");

        await Expect(ResultLinks(session)).ToHaveCountAsync(1);
        await Expect(ResultLinks(session).First).ToContainTextAsync("Beide");
        await Expect(session.Page).ToHaveURLAsync(new Regex(@"[?&]genre=\d+(,|%2C)\d+"));

        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Genrefilter wissen" }).ClickAsync();

        await Expect(ResultLinks(session)).ToHaveCountAsync(3);
        await Expect(session.Page).Not.ToHaveURLAsync(new Regex("[?&]genre="));
    }
}
