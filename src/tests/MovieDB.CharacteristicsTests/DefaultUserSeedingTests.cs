using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieDB.Infrastructure;
using MovieDB.Infrastructure.Auth;
using MovieDB.Infrastructure.Entities;
using Shouldly;

namespace MovieDB.CharacteristicsTests;

[Collection(MariaDbCollection.Name)]
public sealed class DefaultUserSeedingTests
{
    private readonly MariaDbFixture _fixture;

    public DefaultUserSeedingTests(MariaDbFixture fixture)
    {
        this._fixture = fixture;
    }

    /// <summary>Binds the default password from the environment, like the host does, unless a configuration is given.</summary>
    /// <param name="connectionString">The connection string of the test database.</param>
    /// <param name="configuration">An explicit configuration, or null to read the environment.</param>
    private static ServiceProvider BuildServices(string connectionString, IConfiguration? configuration = null)
    {
        configuration ??= new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddMovieDbDatabase(connectionString);
        services.AddOptions<DefaultUserOptions>().Bind(configuration.GetSection(DefaultUserOptions.Section));
        services.AddMovieDbIdentity();
        return services.BuildServiceProvider();
    }

    private static DatabaseMigrator CreateMigrator(ServiceProvider services) =>
        new(
            services.GetRequiredService<IDbContextFactory<MovieDbContext>>(),
            NullLogger<DatabaseMigrator>.Instance,
            services.GetRequiredService<DefaultUserSeeder>()
        );

    [Fact]
    public async Task An_empty_database_gets_the_default_user_exactly_once()
    {
        var connectionString = await _fixture.CreateDatabaseAsync();
        await using var services = BuildServices(connectionString);
        var migrator = CreateMigrator(services);

        await migrator.MigrateAndSeedAsync(TestContext.Current.CancellationToken);
        await migrator.MigrateAndSeedAsync(TestContext.Current.CancellationToken);

        (await MariaDbFixture.ScalarAsync<long>(connectionString, "SELECT COUNT(*) FROM users")).ShouldBe(1);
        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var admin = await users.FindByNameAsync("ADMIN");
        admin.ShouldNotBeNull();
        admin.UserName.ShouldBe("admin");
        (await users.CheckPasswordAsync(admin, MariaDbFixture.DefaultPassword)).ShouldBeTrue();
        admin.PasswordHash.ShouldNotBe(MariaDbFixture.DefaultPassword);
    }

    [Fact]
    public async Task The_default_user_gets_the_configured_password()
    {
        var connectionString = await _fixture.CreateDatabaseAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:DefaultPassword"] = "another secret" })
            .Build();
        await using var services = BuildServices(connectionString, configuration);

        await CreateMigrator(services).MigrateAndSeedAsync(TestContext.Current.CancellationToken);

        await using var scope = services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var admin = await users.FindByNameAsync("admin");
        admin.ShouldNotBeNull();
        (await users.CheckPasswordAsync(admin, "another secret")).ShouldBeTrue();
        (await users.CheckPasswordAsync(admin, MariaDbFixture.DefaultPassword)).ShouldBeFalse();
    }

    [Fact]
    public async Task An_empty_database_without_a_default_password_fails_startup_and_creates_no_user()
    {
        var connectionString = await _fixture.CreateDatabaseAsync();
        await using var services = BuildServices(connectionString, new ConfigurationBuilder().Build());

        var exception = await Should.ThrowAsync<InvalidOperationException>(() =>
            CreateMigrator(services).MigrateAndSeedAsync(TestContext.Current.CancellationToken)
        );

        exception.Message.ShouldContain("Auth__DefaultPassword");
        (await MariaDbFixture.ScalarAsync<long>(connectionString, "SELECT COUNT(*) FROM users")).ShouldBe(0);
    }

    [Fact]
    public async Task A_database_with_users_does_not_need_a_default_password()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using var services = BuildServices(connectionString, new ConfigurationBuilder().Build());
        await using (var scope = services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            (await users.CreateAsync(new User { UserName = "owner" }, "another password")).Succeeded.ShouldBeTrue();
        }

        await CreateMigrator(services).MigrateAndSeedAsync(TestContext.Current.CancellationToken);

        (await MariaDbFixture.ScalarAsync<long>(connectionString, "SELECT COUNT(*) FROM users")).ShouldBe(1);
    }

    [Fact]
    public async Task Existing_users_are_left_untouched()
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        await using var services = BuildServices(connectionString);
        await using (var scope = services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            (await users.CreateAsync(new User { UserName = "owner" }, "another password")).Succeeded.ShouldBeTrue();
        }

        await CreateMigrator(services).MigrateAndSeedAsync(TestContext.Current.CancellationToken);

        (await MariaDbFixture.ScalarAsync<long>(connectionString, "SELECT COUNT(*) FROM users")).ShouldBe(1);
        (
            await MariaDbFixture.ScalarAsync<long>(
                connectionString,
                "SELECT COUNT(*) FROM users WHERE user_name = 'admin'"
            )
        ).ShouldBe(0);
    }

    [Fact]
    public async Task Concurrent_startups_create_one_user()
    {
        var connectionString = await _fixture.CreateDatabaseAsync();
        await using var services = BuildServices(connectionString);

        await Task.WhenAll(
            Enumerable
                .Range(0, 4)
                .Select(_ => CreateMigrator(services).MigrateAndSeedAsync(TestContext.Current.CancellationToken))
        );

        (await MariaDbFixture.ScalarAsync<long>(connectionString, "SELECT COUNT(*) FROM users")).ShouldBe(1);
    }

    [Fact]
    public async Task Migration_only_does_not_seed()
    {
        var connectionString = await _fixture.CreateDatabaseAsync();
        await using var services = BuildServices(connectionString);

        await CreateMigrator(services).MigrateAsync(TestContext.Current.CancellationToken);

        (await MariaDbFixture.ScalarAsync<long>(connectionString, "SELECT COUNT(*) FROM users")).ShouldBe(0);
    }
}
