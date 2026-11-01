using Microsoft.Playwright;
using Shouldly;

namespace MovieDB.BrowserTests;

[Collection(BrowserCollection.Name)]
public sealed class LoginTests
{
    private readonly BrowserFixture _fixture;

    public LoginTests(BrowserFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task An_anonymous_visitor_is_sent_to_the_login_page(int width, int height)
    {
        await using var session = await _fixture.OpenAnonymousAsync(width, height);

        await session.GotoAsync("/films");

        await Assertions.Expect(session.Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login"));
        await Assertions.Expect(session.Page.Locator("#username")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Logging_in_leads_to_the_overview(int width, int height)
    {
        await using var session = await _fixture.OpenAnonymousAsync(width, height);
        await session.GotoAsync("/films");

        await session.Page.Locator("#username").FillAsync(BrowserFixture.AdminUser);
        await session.Page.Locator("#password").FillAsync(BrowserFixture.AdminPassword);
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Aanmelden" }).ClickAsync();

        await Assertions.Expect(session.Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/films$"));
        await session.WaitForOverviewAsync();
        await Assertions.Expect(session.Page.Locator("h1.films-title")).ToHaveTextAsync("Films");
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task A_wrong_password_shows_a_generic_error(int width, int height)
    {
        await using var session = await _fixture.OpenAnonymousAsync(width, height);
        await session.GotoAsync("/login");

        await session.Page.Locator("#username").FillAsync(BrowserFixture.AdminUser);
        await session.Page.Locator("#password").FillAsync("not-the-password");
        await session.Page.GetByRole(AriaRole.Button, new() { Name = "Aanmelden" }).ClickAsync();

        await Assertions.Expect(session.Page.Locator(".auth-error")).ToHaveTextAsync("Aanmelden mislukt.");
        session.Page.Url.ShouldContain("/login");
        await BrowserFixture.WaitOutLoginCooldownAsync();
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Logging_out_ends_the_session(int width, int height)
    {
        await using var session = await _fixture.OpenAsync(width, height);
        await session.OpenOverviewAsync();

        await (await session.UserMenuItemAsync("Afmelden")).ClickAsync();
        await Assertions.Expect(session.Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login"));

        await session.GotoAsync("/films");
        await Assertions.Expect(session.Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/login"));
    }
}
