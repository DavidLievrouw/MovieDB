using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MovieDB.Common;
using MovieDB.Movies;
using MovieDB.Movies.Lookup;
using MudBlazor;
using MudBlazor.Services;
using Shouldly;

namespace MovieDB.UnitTests.Movies;

public sealed class MovieLookupDialogTests : BunitContext
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeLookup : IMovieLookup
    {
        public bool IsConfigured => true;

        public List<LookupQuery> Queries { get; } = [];

        public Func<LookupQuery, CancellationToken, Task<Result<IReadOnlyList<LookupHit>>>> Search { get; set; } =
            (_, _) => Task.FromResult(Result<IReadOnlyList<LookupHit>>.Ok([]));

        public Func<int, CancellationToken, Task<Result<LookupDetails>>> Details { get; set; } =
            (_, _) => Task.FromResult(Result<LookupDetails>.Ok(DefaultDetails));

        public Task<Result<IReadOnlyList<LookupHit>>> SearchAsync(
            LookupQuery query,
            CancellationToken cancellationToken
        )
        {
            Queries.Add(query);
            return Search(query, cancellationToken);
        }

        public Task<Result<LookupDetails>> GetDetailsAsync(int id, CancellationToken cancellationToken) =>
            Details(id, cancellationToken);
    }

    private static readonly LookupDetails DefaultDetails = new(
        "Heat",
        1995,
        170,
        ["Thriller"],
        ["Michael Mann"],
        new Uri("https://www.themoviedb.org/movie/949"),
        null
    );

    private static readonly LookupHit HeatHit = new(
        949,
        "Heat",
        "Heat",
        1995,
        "A group of professional bank robbers.",
        new Uri("https://www.themoviedb.org/movie/949"),
        new Uri("https://image.tmdb.org/t/p/w92/heat.jpg")
    );

    private static readonly LookupHit DutchHit = new(
        1,
        "The Lives of Others",
        "Das Leben der Anderen",
        2006,
        null,
        new Uri("https://www.themoviedb.org/movie/582")
    );

    private readonly FakeLookup _lookup = new();

    public MovieLookupDialogTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
        Services.AddSingleton<IMovieLookup>(_lookup);
        Services.AddSingleton(new UserClock(new FixedTimeProvider(), UserClock.ResolveZone("Europe/Brussels")));
        Render<MudPopoverProvider>();
    }

    private async Task<(IRenderedComponent<MudDialogProvider> Provider, IDialogReference Dialog)> OpenAsync(
        string? initialText = null
    )
    {
        var provider = Render<MudDialogProvider>();
        var service = Services.GetRequiredService<IDialogService>();
        var parameters = new DialogParameters<MovieLookupDialog> { { x => x.InitialText, initialText } };
        var dialog = await service.ShowAsync<MovieLookupDialog>(null, parameters);
        return (provider, dialog);
    }

    private static void Type(IRenderedComponent<MudDialogProvider> provider, string text) =>
        provider.Find("input").Input(text);

    [Fact]
    public async Task Searches_the_initial_text_at_once_and_lists_the_hits_with_a_checkable_link()
    {
        _lookup.Search = (_, _) => Task.FromResult(Result<IReadOnlyList<LookupHit>>.Ok([HeatHit, DutchHit]));

        var (provider, _) = await OpenAsync("Heat 1995");

        await provider.WaitForAssertionAsync(
            () => provider.FindAll("[data-testid=lookup-hit]").Count.ShouldBe(2),
            Timeout
        );
        _lookup.Queries.ShouldHaveSingleItem().ShouldBe(new LookupQuery("Heat", 1995));
        var link = provider.FindAll("a.lookup-hit-link")[0];
        link.GetAttribute("href").ShouldBe("https://www.themoviedb.org/movie/949");
        link.GetAttribute("target").ShouldBe("_blank");
        link.GetAttribute("rel").ShouldBe("noopener noreferrer");
        provider.Markup.ShouldContain("Das Leben der Anderen (2006)");
        provider.Markup.ShouldContain("The Lives of Others");
        provider.Markup.ShouldContain("Controleer via de link of dit de juiste film is.");
    }

    [Fact]
    public async Task Shows_the_poster_next_to_a_hit_and_a_placeholder_when_there_is_none()
    {
        _lookup.Search = (_, _) => Task.FromResult(Result<IReadOnlyList<LookupHit>>.Ok([HeatHit, DutchHit]));

        var (provider, _) = await OpenAsync("Heat");

        await provider.WaitForAssertionAsync(
            () => provider.FindAll("[data-testid=lookup-hit]").Count.ShouldBe(2),
            Timeout
        );
        var poster = provider.FindAll("[data-testid=lookup-hit-poster]").ShouldHaveSingleItem();
        poster.GetAttribute("src").ShouldBe("https://image.tmdb.org/t/p/w92/heat.jpg");
        poster.GetAttribute("alt").ShouldBe("");
        poster.GetAttribute("referrerpolicy").ShouldBe("no-referrer");
        poster.GetAttribute("loading").ShouldBe("lazy");
        provider.FindAll("[data-testid=lookup-hit-no-poster]").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Does_not_search_an_initial_text_that_is_too_short()
    {
        var (provider, _) = await OpenAsync("H");

        await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);
        _lookup.Queries.ShouldBeEmpty();
        provider.FindAll("[data-testid=lookup-hit]").ShouldBeEmpty();
    }

    [Fact]
    public async Task Typing_searches_after_the_debounce_and_only_for_the_last_text()
    {
        _lookup.Search = (_, _) => Task.FromResult(Result<IReadOnlyList<LookupHit>>.Ok([HeatHit]));
        var (provider, _) = await OpenAsync();

        Type(provider, "He");
        Type(provider, "Hea");
        Type(provider, "Heat");

        await provider.WaitForAssertionAsync(
            () => provider.FindAll("[data-testid=lookup-hit]").Count.ShouldBe(1),
            Timeout
        );
        _lookup.Queries.ShouldHaveSingleItem().Title.ShouldBe("Heat");
    }

    [Fact]
    public async Task Shows_the_no_results_message()
    {
        var (provider, _) = await OpenAsync("Nonexistent film");

        await provider.WaitForAssertionAsync(
            () =>
                provider
                    .Find("[data-testid=lookup-status]")
                    .TextContent.ShouldBe(LookupMessages.NoResults("Nonexistent film")),
            Timeout
        );
    }

    [Theory]
    [InlineData(ErrorKind.Unavailable, LookupMessages.Unavailable)]
    [InlineData(ErrorKind.RateLimited, LookupMessages.RateLimited)]
    [InlineData(ErrorKind.Misconfigured, LookupMessages.Misconfigured)]
    [InlineData(ErrorKind.InvalidResponse, LookupMessages.InvalidResponse)]
    public async Task Shows_a_Dutch_message_when_the_search_fails(ErrorKind kind, string expected)
    {
        _lookup.Search = (_, _) => Task.FromResult(Result<IReadOnlyList<LookupHit>>.Fail(new Error(kind, "technical")));

        var (provider, _) = await OpenAsync("Heat");

        await provider.WaitForAssertionAsync(
            () => provider.Find("[data-testid=lookup-status]").TextContent.ShouldBe(expected),
            Timeout
        );
        provider.Markup.ShouldNotContain("technical");
    }

    [Fact]
    public async Task Shows_a_Dutch_message_when_the_lookup_throws()
    {
        _lookup.Search = (_, _) => throw new InvalidOperationException("boom");

        var (provider, _) = await OpenAsync("Heat");

        await provider.WaitForAssertionAsync(
            () => provider.Find("[data-testid=lookup-status]").TextContent.ShouldBe(LookupMessages.Unavailable),
            Timeout
        );
        provider.Markup.ShouldNotContain("boom");
    }

    [Fact]
    public async Task Choosing_a_hit_closes_the_dialog_with_the_details()
    {
        _lookup.Search = (_, _) => Task.FromResult(Result<IReadOnlyList<LookupHit>>.Ok([HeatHit]));
        var (provider, dialog) = await OpenAsync("Heat");
        await provider.WaitForAssertionAsync(
            () => provider.FindAll("[data-testid=lookup-hit]").Count.ShouldBe(1),
            Timeout
        );

        await provider.Find("button[aria-label='Kies Heat']").ClickAsync();

        var result = await dialog.Result;
        result.ShouldNotBeNull();
        result.Canceled.ShouldBeFalse();
        result.Data.ShouldBe(DefaultDetails);
    }

    [Fact]
    public async Task Clicking_the_hit_text_chooses_it()
    {
        _lookup.Search = (_, _) => Task.FromResult(Result<IReadOnlyList<LookupHit>>.Ok([HeatHit]));
        var requested = new List<int>();
        _lookup.Details = (id, _) =>
        {
            requested.Add(id);
            return Task.FromResult(Result<LookupDetails>.Ok(DefaultDetails));
        };
        var (provider, _) = await OpenAsync("Heat");
        await provider.WaitForAssertionAsync(
            () => provider.FindAll("[data-testid=lookup-hit]").Count.ShouldBe(1),
            Timeout
        );

        await provider.Find(".lookup-hit-main").ClickAsync();
        requested.ShouldBe([949]);
    }

    [Fact]
    public async Task A_failed_details_call_keeps_the_list_and_offers_a_retry()
    {
        _lookup.Search = (_, _) => Task.FromResult(Result<IReadOnlyList<LookupHit>>.Ok([HeatHit]));
        var calls = 0;
        _lookup.Details = (_, _) =>
            Task.FromResult(
                ++calls == 1
                    ? Result<LookupDetails>.Fail(new Error(ErrorKind.Unavailable, "technical"))
                    : Result<LookupDetails>.Ok(DefaultDetails)
            );
        var (provider, dialog) = await OpenAsync("Heat");
        await provider.WaitForAssertionAsync(
            () => provider.FindAll("[data-testid=lookup-hit]").Count.ShouldBe(1),
            Timeout
        );

        await provider.Find("button[aria-label='Kies Heat']").ClickAsync();

        await provider.WaitForAssertionAsync(
            () =>
            {
                var error = provider.Find("[data-testid=lookup-details-error]").TextContent;
                error.ShouldContain(LookupMessages.Unavailable);
                error.ShouldContain(LookupMessages.DetailsFailed);
                error.ShouldContain("Opnieuw proberen");
            },
            Timeout
        );
        provider.FindAll("[data-testid=lookup-hit]").Count.ShouldBe(1);
        dialog.Result.IsCompleted.ShouldBeFalse();

        await provider.FindAll("button").Single(b => b.TextContent.Contains("Opnieuw proberen")).ClickAsync();

        var result = await dialog.Result;
        result!.Data.ShouldBe(DefaultDetails);
        calls.ShouldBe(2);
    }

    [Fact]
    public async Task A_details_call_for_a_missing_film_offers_no_retry()
    {
        _lookup.Search = (_, _) => Task.FromResult(Result<IReadOnlyList<LookupHit>>.Ok([HeatHit]));
        _lookup.Details = (_, _) =>
            Task.FromResult(Result<LookupDetails>.Fail(new Error(ErrorKind.NotFound, "technical")));
        var (provider, _) = await OpenAsync("Heat");
        await provider.WaitForAssertionAsync(
            () => provider.FindAll("[data-testid=lookup-hit]").Count.ShouldBe(1),
            Timeout
        );

        await provider.Find("button[aria-label='Kies Heat']").ClickAsync();

        await provider.WaitForAssertionAsync(
            () =>
                provider.Find("[data-testid=lookup-details-error]").TextContent.ShouldContain(LookupMessages.NotFound),
            Timeout
        );
        provider.Markup.ShouldNotContain("Opnieuw proberen");
        provider.FindAll("[data-testid=lookup-hit]").Count.ShouldBe(1);
    }

    [Fact]
    public async Task Cancelling_closes_the_dialog_without_a_result()
    {
        var (provider, dialog) = await OpenAsync();

        await provider.FindAll("button").Single(b => b.TextContent.Contains("Annuleren")).ClickAsync();

        var result = await dialog.Result;
        result!.Canceled.ShouldBeTrue();
    }

    [Fact]
    public async Task A_slow_older_search_does_not_replace_the_newer_results()
    {
        var first = new TaskCompletionSource<Result<IReadOnlyList<LookupHit>>>();
        _lookup.Search = (query, _) =>
            query.Title == "Slow" ? first.Task : Task.FromResult(Result<IReadOnlyList<LookupHit>>.Ok([HeatHit]));
        var (provider, _) = await OpenAsync("Slow");
        await provider.WaitForAssertionAsync(() => _lookup.Queries.Count.ShouldBe(1), Timeout);

        Type(provider, "Heat");
        await provider.WaitForAssertionAsync(
            () => provider.FindAll("[data-testid=lookup-hit]").Count.ShouldBe(1),
            Timeout
        );
        first.SetResult(Result<IReadOnlyList<LookupHit>>.Ok([DutchHit, DutchHit]));
        await Task.Delay(100, Xunit.TestContext.Current.CancellationToken);

        provider.FindAll("[data-testid=lookup-hit]").Count.ShouldBe(1);
        provider.Markup.ShouldNotContain("Das Leben der Anderen");
    }

    [Fact]
    public async Task Shows_the_attribution_in_the_footer()
    {
        var (provider, _) = await OpenAsync();

        provider.Find(".mud-dialog-actions [data-testid=tmdb-attribution]").ShouldNotBeNull();
    }
}
