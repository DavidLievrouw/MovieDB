using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace MovieDB.CharacteristicsTests;

[Collection(MariaDbCollection.Name)]
public sealed class TmdbAttributionAssetTests
{
    private readonly MariaDbFixture _fixture;

    public TmdbAttributionAssetTests(MariaDbFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Environment variables beat user-secrets, so the host never uses the developer's database or token.</summary>
    private sealed class HostSettings : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = [];

        public HostSettings(string connectionString)
        {
            Set("ConnectionStrings__MovieDb", connectionString);
            Set("Tmdb__ApiReadAccessToken", "synthetic-token");
        }

        private void Set(string name, string? value)
        {
            _previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            foreach (var (name, value) in _previous)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }
    }

    private async Task<(WebApplicationFactory<Program> Factory, HttpClient Client, HostSettings Settings)> StartAsync()
    {
        var settings = new HostSettings(await _fixture.CreateDatabaseAsync());
        var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        return (factory, client, settings);
    }

    [Fact]
    public async Task The_logo_is_served_as_a_local_svg_and_the_csp_allows_it_without_external_sources()
    {
        var (factory, client, settings) = await StartAsync();
        using var _ = settings;
        await using var __ = factory;
        using var ___ = client;

        using var logo = await client.GetAsync(
            "/_content/MovieDB.Movies/images/tmdb-logo.svg",
            TestContext.Current.CancellationToken
        );
        using var login = await client.GetAsync("/login", TestContext.Current.CancellationToken);

        logo.StatusCode.ShouldBe(HttpStatusCode.OK);
        logo.Content.Headers.ContentType!.MediaType.ShouldBe("image/svg+xml");
        (await logo.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldStartWith("<svg");
        var policy = login
            .Headers.GetValues("Content-Security-Policy")
            .Single(p => p.Contains("default-src 'self'", StringComparison.Ordinal));
        policy.ShouldContain("img-src 'self' data:");
        policy.ShouldNotContain("themoviedb");
        policy.ShouldNotContain("tmdb");
    }

    [Fact]
    public async Task The_login_page_has_no_attribution_and_the_new_movie_page_requires_a_login()
    {
        var (factory, client, settings) = await StartAsync();
        using var _ = settings;
        await using var __ = factory;
        using var ___ = client;

        using var login = await client.GetAsync("/login", TestContext.Current.CancellationToken);
        using var newMovie = await client.GetAsync("/films/nieuw", TestContext.Current.CancellationToken);

        (await login.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain(
            "tmdb-attribution"
        );
        newMovie.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        newMovie.Headers.Location!.OriginalString.ShouldContain("/login");
    }
}
