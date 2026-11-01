using System.Text.RegularExpressions;
using Microsoft.Playwright;
using Shouldly;
using static Microsoft.Playwright.Assertions;

namespace MovieDB.BrowserTests;

[Collection(BrowserCollection.Name)]
public sealed class ChangePasswordTests
{
    private const string OldPassword = "oud-wachtwoord-1";
    private const string NewPassword = "nieuw-wachtwoord-2";

    private readonly BrowserFixture _fixture;

    public ChangePasswordTests(BrowserFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<IPage> SignInAsync(BrowserSession session, string userName)
    {
        await _fixture.LoginAsync(session.Page, userName, OldPassword);
        await session.GotoAsync("/account/wachtwoord");
        return session.Page;
    }

    private static async Task SubmitAsync(IPage page, string current, string next, string confirm)
    {
        await page.Locator("#current").FillAsync(current);
        await page.Locator("#new").FillAsync(next);
        await page.Locator("#confirm").FillAsync(confirm);
        await page.GetByRole(AriaRole.Button, new() { Name = "Wachtwoord wijzigen", Exact = true }).ClickAsync();
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task A_wrong_current_password_is_refused(int width, int height)
    {
        var user = BrowserFixture.Unique("pw-fout").Replace(' ', '-');
        await _fixture.CreateUserAsync(user, OldPassword);
        await using var session = await _fixture.OpenAnonymousAsync(width, height);
        var page = await SignInAsync(session, user);

        await SubmitAsync(page, "verkeerd-wachtwoord", NewPassword, NewPassword);

        await Expect(page.GetByText("Het huidige wachtwoord is onjuist.")).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task A_new_password_that_is_too_weak_or_mismatched_is_refused(int width, int height)
    {
        var user = BrowserFixture.Unique("pw-zwak").Replace(' ', '-');
        await _fixture.CreateUserAsync(user, OldPassword);
        await using var session = await _fixture.OpenAnonymousAsync(width, height);
        var page = await SignInAsync(session, user);

        await SubmitAsync(page, OldPassword, NewPassword, NewPassword + "x");

        await Expect(page.Locator(".auth-error")).ToBeVisibleAsync();
        await Expect(page).ToHaveURLAsync(new Regex("/account/wachtwoord"));
    }

    [Theory]
    [InlineData(390, 844)]
    [InlineData(1440, 900)]
    public async Task Changing_the_password_makes_the_new_one_the_only_one_that_works(int width, int height)
    {
        var user = BrowserFixture.Unique("pw-ok").Replace(' ', '-');
        await _fixture.CreateUserAsync(user, OldPassword);
        await using var session = await _fixture.OpenAnonymousAsync(width, height);
        var page = await SignInAsync(session, user);

        await SubmitAsync(page, OldPassword, NewPassword, NewPassword);

        await Expect(page).Not.ToHaveURLAsync(new Regex("/account/wachtwoord"));

        // The new password is tried first: a failed attempt starts a cooldown for the account.
        await using var newAttempt = await _fixture.OpenAnonymousAsync(width, height);
        await _fixture.LoginAsync(newAttempt.Page, user, NewPassword);
        newAttempt.Page.Url.ShouldNotContain("/login");

        await using var oldAttempt = await _fixture.OpenAnonymousAsync(width, height);
        await oldAttempt.GotoAsync("/login");
        await oldAttempt.Page.Locator("#username").FillAsync(user);
        await oldAttempt.Page.Locator("#password").FillAsync(OldPassword);
        await oldAttempt.Page.GetByRole(AriaRole.Button, new() { Name = "Aanmelden" }).ClickAsync();
        await Expect(oldAttempt.Page.GetByText("Aanmelden mislukt.")).ToBeVisibleAsync();
        await BrowserFixture.WaitOutLoginCooldownAsync();
    }
}
