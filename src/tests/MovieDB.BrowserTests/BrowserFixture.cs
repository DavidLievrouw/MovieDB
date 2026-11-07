using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;
using MovieDB.Common;
using MovieDB.Infrastructure;
using MovieDB.Infrastructure.Entities;
using MovieDB.Movies;
using MovieDB.Movies.Lookup;
using Testcontainers.MariaDb;

namespace MovieDB.BrowserTests;

/// <summary>
/// One MariaDB container, one real HTTP host (Kestrel, because the Blazor circuit needs a WebSocket) and one
/// Chromium instance per test run. All data is synthetic. Tests never rely on the total number of movies: they
/// create their own movies under a unique name (<see cref="Unique"/>) and search for it.
/// </summary>
public sealed class BrowserFixture : IAsyncLifetime
{
    /// <summary>The seeded default user.</summary>
    public const string AdminUser = "admin";

    /// <summary>The password of the seeded default user; the host reads it from the environment, like in production.</summary>
    public const string AdminPassword = "demodemo";

    private static readonly string[] EnvironmentVariables =
    [
        "ConnectionStrings__MovieDb",
        "Auth__DefaultPassword",
        "Auth__SecureCookies",
        "App__TimeZone",
        "Auth__Protection__IpAttempts",
        "Auth__Protection__DeviceAttempts",
        "Auth__Protection__MaximumCooldownSeconds",
    ];

    private readonly MariaDbContainer _container = new MariaDbBuilder("mariadb:12.3.3").Build();
    private WebApplicationFactory<Program>? _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private string _storageState = "";

    /// <summary>The address the host listens on, without a trailing slash.</summary>
    public string BaseUrl { get; private set; } = "";

    /// <summary>The scripted online movie database; call <see cref="FakeMovieLookup.Reset"/> at the start of a test.</summary>
    internal FakeMovieLookup Lookup { get; } = new();

    /// <summary>The services of the running host, for seeding and for reading back saved data.</summary>
    public IServiceProvider Services => _app!.Services;

    /// <summary>A name that is unique within this run, so that a test can find its own movies by searching.</summary>
    /// <param name="prefix">Readable part of the name.</param>
    public static string Unique(string prefix) => $"{prefix} {Guid.NewGuid().ToString("N")[..10]}";

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();

        // Environment variables win over user-secrets, so the host can never reach the developer's own database.
        Environment.SetEnvironmentVariable(
            "ConnectionStrings__MovieDb",
            _container.GetConnectionString() + ";CharSet=utf8mb4"
        );
        Environment.SetEnvironmentVariable("Auth__DefaultPassword", AdminPassword);
        Environment.SetEnvironmentVariable("Auth__SecureCookies", "false");
        Environment.SetEnvironmentVariable("App__TimeZone", "Europe/Brussels");
        // All browsers share one source address, so the per-source budgets and cooldowns must not starve later tests.
        Environment.SetEnvironmentVariable("Auth__Protection__IpAttempts", "1000");
        Environment.SetEnvironmentVariable("Auth__Protection__DeviceAttempts", "1000");
        Environment.SetEnvironmentVariable("Auth__Protection__MaximumCooldownSeconds", "1");

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMovieLookup>();
                services.AddSingleton<IMovieLookup>(Lookup);
            })
        );
        factory.UseKestrel(0);
        factory.StartServer();
        _app = factory;
        BaseUrl = factory.ClientOptions.BaseAddress.ToString().TrimEnd('/');

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });

        await using var context = await _browser.NewContextAsync(new BrowserNewContextOptions { Locale = "nl-BE" });
        var page = await context.NewPageAsync();
        await LoginAsync(page, AdminUser, AdminPassword);
        _storageState = await context.StorageStateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
        {
            await _browser.DisposeAsync();
        }

        _playwright?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        await _container.DisposeAsync();
        foreach (var name in EnvironmentVariables)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    /// <summary>Opens a browser context that is already logged in as the default user.</summary>
    /// <param name="width">The viewport width in pixels.</param>
    /// <param name="height">The viewport height in pixels.</param>
    public Task<BrowserSession> OpenAsync(int width, int height) => OpenCoreAsync(width, height, _storageState);

    /// <summary>Opens a browser context without any cookies (not logged in).</summary>
    /// <param name="width">The viewport width in pixels.</param>
    /// <param name="height">The viewport height in pixels.</param>
    public Task<BrowserSession> OpenAnonymousAsync(int width, int height) => OpenCoreAsync(width, height, null);

    private async Task<BrowserSession> OpenCoreAsync(int width, int height, string? storageState)
    {
        var context = await _browser!.NewContextAsync(
            new BrowserNewContextOptions
            {
                Locale = "nl-BE",
                TimezoneId = "Europe/Brussels",
                ViewportSize = new ViewportSize { Width = width, Height = height },
                StorageState = storageState,
                BaseURL = BaseUrl,
            }
        );
        context.SetDefaultTimeout(15_000);
        return await BrowserSession.CreateAsync(context, BaseUrl);
    }

    /// <summary>Logs in through the login form, retrying while the brief cooldown after a failed login is active.</summary>
    /// <param name="page">The page to use.</param>
    /// <param name="userName">The user name.</param>
    /// <param name="password">The password.</param>
    /// <exception cref="InvalidOperationException">The login did not succeed.</exception>
    public async Task LoginAsync(IPage page, string userName, string password)
    {
        const int maxAttempts = 5;
        var lastMessage = "";
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await page.GotoAsync(BaseUrl + "/login");
            await page.Locator("#username").FillAsync(userName);
            await page.Locator("#password").FillAsync(password);
            await page.GetByRole(AriaRole.Button, new() { Name = "Aanmelden" }).ClickAsync();
            var outcome = await Task.WhenAny(
                page.WaitForURLAsync(url => !url.Contains("/login", StringComparison.Ordinal)),
                page.Locator(".auth-error").WaitForAsync()
            );
            await outcome;
            if (!page.Url.Contains("/login", StringComparison.Ordinal))
            {
                return;
            }

            lastMessage = await page.Locator(".auth-error").InnerTextAsync();
            if (!lastMessage.Contains("tijdelijk beperkt", StringComparison.Ordinal))
            {
                break;
            }

            await WaitOutLoginCooldownAsync();
        }

        throw new InvalidOperationException($"Login failed on {page.Url}: {lastMessage}");
    }

    /// <summary>
    /// Waits until the one-second cooldown that follows a failed login (per source address) has passed, so that a
    /// later test is not refused.
    /// </summary>
    public static Task WaitOutLoginCooldownAsync() => Task.Delay(TimeSpan.FromMilliseconds(1500));

    /// <summary>Creates a movie directly in the database through the application's own command.</summary>
    /// <param name="title">The title.</param>
    /// <param name="year">The release year.</param>
    /// <param name="director">The directors, separated with <c>|</c> when there are several.</param>
    /// <param name="genres">The genre names; <c>Drama</c> when omitted.</param>
    /// <param name="medium">The medium, if any.</param>
    /// <param name="overallScore">The overall score, if any.</param>
    /// <param name="watchedOn">The viewing date.</param>
    /// <exception cref="InvalidOperationException">The movie could not be saved.</exception>
    public async Task<int> SeedMovieAsync(
        string title,
        int year = 1999,
        string director = "Synthetische Regisseur",
        string[]? genres = null,
        string? medium = "TV",
        int overallScore = 7,
        DateOnly? watchedOn = null
    )
    {
        await using var scope = Services.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<MovieCommands>();
        var result = await commands.SaveAsync(
            null,
            1,
            new MovieInput
            {
                Title = title,
                ReleaseYear = year,
                Directors = [.. director.Split('|')],
                Genres = [.. genres ?? ["Drama"]],
                Medium = medium,
                RuntimeMinutes = 100,
                WatchedOn = watchedOn ?? new DateOnly(2024, 5, 17),
                OverallScore = overallScore,
                StoryScore = 6,
                PictureScore = 5,
                MusicScore = 4,
                SoundScore = 3,
                Review = "Synthetische recensie.",
            },
            TestContext.Current.CancellationToken
        );
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Seeding '{title}' failed: {result.Error.Message}");
        }

        return result.Value.Id.Value;
    }

    /// <summary>Clears the story score of a saved movie, like a film imported without it.</summary>
    /// <param name="id">The movie id.</param>
    public async Task ClearStoryScoreAsync(int id)
    {
        await using var scope = Services.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MovieDbContext>>();
        await using var context = await factory.CreateDbContextAsync(TestContext.Current.CancellationToken);
        await context
            .Movies.Where(m => m.Id == MovieId.From(id))
            .ExecuteUpdateAsync(
                s => s.SetProperty(m => m.StoryScore, (int?)null),
                TestContext.Current.CancellationToken
            );
    }

    /// <summary>Creates a user that can log in; used by the tests that change a password.</summary>
    /// <param name="userName">The user name.</param>
    /// <param name="password">The password.</param>
    /// <exception cref="InvalidOperationException">The user could not be created.</exception>
    public async Task CreateUserAsync(string userName, string password)
    {
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var result = await users.CreateAsync(new User { UserName = userName }, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Creating the user failed: " + string.Join(", ", result.Errors.Select(e => e.Code))
            );
        }
    }

    /// <summary>Reads back a saved movie, so that a test can verify what reached the database.</summary>
    /// <param name="id">The movie id.</param>
    public async Task<SavedMovieValues?> FindMovieAsync(int id)
    {
        await using var scope = Services.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<MovieQueries>();
        var edit = await queries.FindForEditAsync(MovieId.From(id), TestContext.Current.CancellationToken);
        return edit is null
            ? null
            : new SavedMovieValues(
                edit.Input.Title!,
                edit.Input.ReleaseYear!.Value,
                edit.Input.Directors,
                edit.Input.Genres,
                edit.Input.Medium,
                edit.Input.OverallScore,
                edit.Input.StoryScore,
                edit.Input.Review,
                edit.Version
            );
    }
}

/// <summary>The values of a saved movie that the tests verify.</summary>
/// <param name="Title">The title.</param>
/// <param name="ReleaseYear">The release year.</param>
/// <param name="Directors">The director names, in order.</param>
/// <param name="Genres">The genre names, alphabetically.</param>
/// <param name="Medium">The medium name, if any.</param>
/// <param name="OverallScore">The overall score, if any.</param>
/// <param name="StoryScore">The story score, if any.</param>
/// <param name="Review">The review, if any.</param>
/// <param name="Version">The concurrency version.</param>
public sealed record SavedMovieValues(
    string Title,
    int ReleaseYear,
    IReadOnlyList<string> Directors,
    IReadOnlyList<string> Genres,
    string? Medium,
    int? OverallScore,
    int? StoryScore,
    string? Review,
    int Version
);

[CollectionDefinition(Name)]
public sealed class BrowserCollection : ICollectionFixture<BrowserFixture>
{
    public const string Name = "Browser";
}
