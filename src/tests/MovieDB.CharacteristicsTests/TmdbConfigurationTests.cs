using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using MovieDB.Movies.Lookup;
using Shouldly;

namespace MovieDB.CharacteristicsTests;

[Collection(MariaDbCollection.Name)]
public sealed class TmdbConfigurationTests
{
    private readonly MariaDbFixture _fixture;

    public TmdbConfigurationTests(MariaDbFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    /// Environment variables beat user-secrets, so the host never uses the developer's own database or token. A blank
    /// value stands for "not set": on Windows, setting a variable to the empty string would remove it.
    /// </summary>
    private sealed class HostSettings : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = [];

        public HostSettings(string connectionString, string? token, string? baseUrl)
        {
            Set("ConnectionStrings__MovieDb", connectionString);
            Set("Tmdb__ApiReadAccessToken", token);
            Set("Tmdb__BaseUrl", baseUrl);
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

    [Fact]
    public async Task The_app_starts_without_a_token_and_online_search_is_not_configured()
    {
        using var settings = new HostSettings(await _fixture.CreateDatabaseAsync(), " ", null);
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.ShouldBeTrue();
        var lookup = factory.Services.GetRequiredService<IMovieLookup>();
        lookup.IsConfigured.ShouldBeFalse();
    }

    [Fact]
    public async Task A_configured_token_is_picked_up_without_being_validated_against_the_service()
    {
        using var settings = new HostSettings(await _fixture.CreateDatabaseAsync(), "synthetic-token", null);
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        factory.Services.GetRequiredService<IMovieLookup>().IsConfigured.ShouldBeTrue();
    }

    [Theory]
    [InlineData("http://api.themoviedb.org/3/")]
    [InlineData("https://api.themoviedb.org/3")]
    [InlineData("not a url")]
    public async Task An_invalid_base_url_stops_startup(string baseUrl)
    {
        using var settings = new HostSettings(await _fixture.CreateDatabaseAsync(), null, baseUrl);
        await using var factory = new WebApplicationFactory<Program>();

        Should.Throw<Exception>(() => factory.CreateClient()).ToString().ShouldContain("Tmdb");
    }
}
