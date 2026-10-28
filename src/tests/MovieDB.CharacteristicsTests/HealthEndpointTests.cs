using Microsoft.AspNetCore.Mvc.Testing;
using Shouldly;

namespace MovieDB.CharacteristicsTests;

[Collection(MariaDbCollection.Name)]
public sealed class HealthEndpointTests
{
    private readonly MariaDbFixture _fixture;

    public HealthEndpointTests(MariaDbFixture fixture)
    {
        this._fixture = fixture;
    }

    /// <summary>
    /// Environment variables beat user-secrets, so the host never touches the developer's own database. The tests of
    /// this collection run serially, so the process-wide variables cannot leak between them.
    /// </summary>
    private sealed class HostSettings : IDisposable
    {
        private readonly Dictionary<string, string?> _previous = [];

        public HostSettings(string connectionString, string? timeZone)
        {
            Set("ConnectionStrings__MovieDb", connectionString);
            Set("App__TimeZone", timeZone);
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
    public async Task Live_endpoint_answers_without_dependencies()
    {
        using var settings = new HostSettings(await _fixture.CreateDatabaseAsync(), null);
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.ShouldBeTrue();
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("MovieDB");
    }

    [Fact]
    public async Task Startup_migrates_an_empty_database_and_health_reports_healthy()
    {
        var connectionString = await _fixture.CreateDatabaseAsync();
        using var settings = new HostSettings(connectionString, null);
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health", TestContext.Current.CancellationToken);

        response.IsSuccessStatusCode.ShouldBeTrue();
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Healthy");
        (
            await MariaDbFixture.ScalarAsync<long>(
                connectionString,
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = 'movies'"
            )
        ).ShouldBe(1);
    }

    [Fact]
    public async Task Startup_on_an_empty_database_without_a_default_password_fails()
    {
        using var settings = new HostSettings(await _fixture.CreateDatabaseAsync(), null);
        Environment.SetEnvironmentVariable(MariaDbFixture.DefaultPasswordVariable, null);
        try
        {
            await using var factory = new WebApplicationFactory<Program>();

            var exception = Should.Throw<Exception>(() => factory.CreateClient());

            exception.ToString().ShouldContain(MariaDbFixture.DefaultPasswordVariable);
        }
        finally
        {
            Environment.SetEnvironmentVariable(MariaDbFixture.DefaultPasswordVariable, MariaDbFixture.DefaultPassword);
        }
    }

    [Fact]
    public async Task An_invalid_time_zone_stops_startup()
    {
        using var settings = new HostSettings(await _fixture.CreateDatabaseAsync(), "Not/AZone");
        await using var factory = new WebApplicationFactory<Program>();

        Should.Throw<Exception>(() => factory.CreateClient());
    }
}
