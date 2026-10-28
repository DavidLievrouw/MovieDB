using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MovieDB.Infrastructure.Entities;

namespace MovieDB.Infrastructure.Auth;

/// <summary>
/// Creates the default <c>admin</c> user when the <c>users</c> table is empty, exactly once per database. The password
/// comes from <see cref="DefaultUserOptions.DefaultPassword"/>; startup fails when it is needed but not configured.
/// The caller holds the migration lock, so concurrent instances cannot both seed; the unique index on the normalized
/// user name is the backstop.
/// </summary>
public sealed class DefaultUserSeeder
{
    public const string DefaultUserName = "admin";

    private readonly IDbContextFactory<MovieDbContext> _factory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<DefaultUserOptions> _options;
    private readonly ILogger<DefaultUserSeeder> _logger;

    public DefaultUserSeeder(
        IDbContextFactory<MovieDbContext> factory,
        IServiceScopeFactory scopeFactory,
        IOptions<DefaultUserOptions> options,
        ILogger<DefaultUserSeeder> logger
    )
    {
        _factory = factory;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var defaultPassword = _options.Value.DefaultPassword;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();

        await using (var context = await _factory.CreateDbContextAsync(cancellationToken))
        {
            if (!await context.Users.AnyAsync(cancellationToken))
            {
                if (string.IsNullOrEmpty(defaultPassword))
                {
                    throw new InvalidOperationException(
                        $"The users table is empty and no default password is configured. Set {DefaultUserOptions.Section}:{nameof(DefaultUserOptions.DefaultPassword)} "
                            + "(environment variable Auth__DefaultPassword) to create the default user."
                    );
                }

                var result = await users.CreateAsync(new User { UserName = DefaultUserName }, defaultPassword);
                if (!result.Succeeded)
                {
                    throw new InvalidOperationException(
                        "Could not create the default user: " + string.Join("; ", result.Errors.Select(e => e.Code))
                    );
                }

                _logger.LogInformation("Created the default user '{UserName}'.", DefaultUserName);
            }
        }

        var admin = await users.FindByNameAsync(DefaultUserName);
        if (
            admin is not null
            && !string.IsNullOrEmpty(defaultPassword)
            && users.PasswordHasher.VerifyHashedPassword(admin, admin.PasswordHash, defaultPassword)
                != PasswordVerificationResult.Failed
        )
        {
            _logger.LogWarning("Default admin password is still in use. Change it at /account/wachtwoord.");
        }
    }
}
