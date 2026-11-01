using Microsoft.Playwright;

namespace MovieDB.BrowserTests;

/// <summary>A browser context with one page, and the helpers that every flow needs.</summary>
public sealed class BrowserSession : IAsyncDisposable
{
    private readonly IBrowserContext _context;
    private readonly string _baseUrl;

    private BrowserSession(IBrowserContext context, IPage page, string baseUrl)
    {
        _context = context;
        Page = page;
        _baseUrl = baseUrl;
    }

    /// <summary>The page of the session.</summary>
    public IPage Page { get; }

    /// <summary>Creates a session in a new page of the given context; it owns the context from here on.</summary>
    /// <param name="context">The browser context.</param>
    /// <param name="baseUrl">The address of the application.</param>
    public static async Task<BrowserSession> CreateAsync(IBrowserContext context, string baseUrl) =>
        new(context, await context.NewPageAsync(), baseUrl);

    public async ValueTask DisposeAsync() => await _context.DisposeAsync();

    /// <summary>Opens a path of the application.</summary>
    /// <param name="path">The path and query, starting with a slash.</param>
    public async Task GotoAsync(string path) => await Page.GotoAsync(_baseUrl + path);

    /// <summary>Opens the overview and waits until the circuit has loaded the data.</summary>
    /// <param name="query">The optional query string, including the leading question mark.</param>
    public async Task OpenOverviewAsync(string query = "")
    {
        await GotoAsync("/films" + query);
        await WaitForOverviewAsync();
    }

    /// <summary>Waits until the overview shows data (the data is loaded by the circuit, not while prerendering).</summary>
    public async Task WaitForOverviewAsync() =>
        await Assertions.Expect(Page.Locator(".films-total-value")).Not.ToHaveTextAsync("–");

    /// <summary>Opens the edit page of a movie and waits until its form is loaded.</summary>
    /// <param name="id">The movie id.</param>
    /// <param name="query">The optional query string, including the leading question mark.</param>
    public async Task OpenEditAsync(int id, string query = "")
    {
        await GotoAsync($"/films/{id}{query}");
        await WaitForFormAsync();
    }

    /// <summary>Opens the page for a new movie and waits until its form is shown.</summary>
    public async Task OpenNewAsync()
    {
        await GotoAsync("/films/nieuw");
        await WaitForFormAsync();
    }

    /// <summary>Waits until the circuit has loaded the form (the prerendered page only shows a skeleton).</summary>
    public async Task WaitForFormAsync() => await Assertions.Expect(Page.Locator(".films-savebar")).ToBeVisibleAsync();

    /// <summary>A field of the edit form, found by its label.</summary>
    /// <param name="label">The label of the field.</param>
    public ILocator Field(string label) => Page.GetByLabel(label, new() { Exact = true });

    /// <summary>The selected chips of a chip multi-select field.</summary>
    /// <param name="testId">The <c>data-testid</c> of the field.</param>
    public ILocator Chips(string testId) => Page.Locator($"[data-testid={testId}] .chip-multi-select-chip");

    /// <summary>The text input of a chip multi-select field.</summary>
    /// <param name="testId">The <c>data-testid</c> of the field.</param>
    public ILocator ChipInput(string testId) => Page.Locator($"[data-testid={testId}] input").First;

    /// <summary>The outlined box that holds the chips and the input of a chip multi-select field.</summary>
    /// <param name="testId">The <c>data-testid</c> of the field.</param>
    public ILocator ChipBox(string testId) => Page.Locator($"[data-testid={testId}] .chip-multi-select-box");

    /// <summary>
    /// The vertical centre of an element's content box (without padding), which is where its text or chip is drawn.
    /// </summary>
    /// <param name="element">The element to measure.</param>
    public static async Task<double> ContentCenterYAsync(ILocator element) =>
        await element.EvaluateAsync<double>(
            """
            el => {
                const r = el.getBoundingClientRect();
                const s = getComputedStyle(el);
                const top = r.y + parseFloat(s.paddingTop);
                const bottom = r.y + r.height - parseFloat(s.paddingBottom);
                return (top + bottom) / 2;
            }
            """
        );

    /// <summary>Adds a value to a chip multi-select field by picking it from the suggestions.</summary>
    /// <param name="testId">The <c>data-testid</c> of the field.</param>
    /// <param name="text">The value to type and pick.</param>
    public async Task AddChipAsync(string testId, string text)
    {
        var input = ChipInput(testId);
        await input.ClickAsync();
        await input.FillAsync(text);
        await Page.Locator(".mud-popover-open .mud-list-item").GetByText(text, new() { Exact = true }).ClickAsync();
        await Assertions.Expect(Chips(testId).GetByText(text, new() { Exact = true })).ToBeVisibleAsync();

        // The field gets the focus back to add the next value, which reopens the list over the rest of the form.
        await DismissPopoverAsync(4000);
    }

    /// <summary>A score input of the edit form.</summary>
    /// <param name="label">The label of the score.</param>
    public ILocator ScoreGroup(string label) => Page.Locator($"[role=group][aria-label='{label}']");

    /// <summary>Fills a text field of the edit form and leaves it, like a user does.</summary>
    /// <param name="label">The label of the field.</param>
    /// <param name="value">The value to type.</param>
    public async Task FillAsync(string label, string value)
    {
        var field = Field(label);
        await field.FillAsync(value);
        await DismissPopoverAsync(500);
        await field.BlurAsync();
    }

    /// <summary>Closes a suggestion list that opens after the debounce, also when the field was left in the meantime.</summary>
    /// <param name="timeoutMs">How long to wait for a list to show up.</param>
    private async Task DismissPopoverAsync(float timeoutMs)
    {
        var popover = Page.Locator(".mud-popover-open");
        try
        {
            await popover.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
        }
        catch (TimeoutException)
        {
            return;
        }

        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(popover).ToHaveCountAsync(0);
    }

    /// <summary>Picks a score, using the toggle buttons on a wide screen and the select on a narrow one.</summary>
    /// <param name="label">The label of the score.</param>
    /// <param name="score">The score, from 1 to 10.</param>
    public async Task SetScoreAsync(string label, int score)
    {
        var group = ScoreGroup(label);
        var toggles = group.Locator(".score-input-toggles");
        if (await toggles.IsVisibleAsync())
        {
            await toggles.Locator(".mud-toggle-item").Nth(score - 1).ClickAsync();
            return;
        }

        await group.Locator("div.mud-select").First.ClickAsync();
        await Page.Locator(".mud-popover-open .mud-list-item").Nth(score - 1).ClickAsync();
    }

    /// <summary>Picks the same score for all five scores.</summary>
    /// <param name="score">The score, from 1 to 10.</param>
    public async Task SetAllScoresAsync(int score)
    {
        foreach (var label in new[] { "Totaal (geen gemiddelde)", "Film", "Beeld", "Muziek", "Geluid" })
        {
            await SetScoreAsync(label, score);
        }
    }

    /// <summary>The save button of the edit form.</summary>
    public ILocator SaveButton => Page.Locator(".films-savebar").GetByRole(AriaRole.Button, new() { Name = "Opslaan" });

    /// <summary>The cancel button of the edit form.</summary>
    public ILocator CancelButton =>
        Page.Locator(".films-savebar").GetByRole(AriaRole.Button, new() { Name = "Annuleren" });

    /// <summary>Opens the user menu in the header and returns the menu item with the given name.</summary>
    /// <param name="name">The visible name of the item.</param>
    public async Task<ILocator> UserMenuItemAsync(string name)
    {
        await Page.GetByLabel("Gebruikersmenu").ClickAsync();
        return Page.Locator(".films-user-menu").GetByText(name, new() { Exact = true });
    }
}
