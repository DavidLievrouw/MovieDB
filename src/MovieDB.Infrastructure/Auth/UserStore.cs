using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MovieDB.Common;
using MovieDB.Infrastructure.Entities;

namespace MovieDB.Infrastructure.Auth;

/// <summary>
/// Identity store over the <c>users</c> table. One context per operation. Updates are guarded by the concurrency
/// stamp, so two concurrent password changes cannot both win silently.
/// </summary>
internal sealed class UserStore : IUserPasswordStore<User>, IUserSecurityStampStore<User>
{
    private readonly IDbContextFactory<MovieDbContext> _factory;
    private readonly TimeProvider _timeProvider;

    public UserStore(IDbContextFactory<MovieDbContext> factory, TimeProvider timeProvider)
    {
        _factory = factory;
        _timeProvider = timeProvider;
    }

    public Task<string> GetUserIdAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(user.Id.Value);

    public Task<string?> GetUserNameAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.UserName);

    public Task SetUserNameAsync(User user, string? userName, CancellationToken cancellationToken)
    {
        user.UserName = userName ?? "";
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(User user, string? normalizedName, CancellationToken cancellationToken)
    {
        user.NormalizedUserName = normalizedName ?? "";
        return Task.CompletedTask;
    }

    public async Task<IdentityResult> CreateAsync(User user, CancellationToken cancellationToken)
    {
        if (!user.Id.IsInitialized())
        {
            user.Id = UserId.From(Guid.NewGuid().ToString());
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        user.ConcurrencyStamp = Guid.NewGuid().ToString("N");
        user.CreatedAt = now;
        user.UpdatedAt = now;

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        context.Users.Add(user);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DbOperations.Classify(ex) == DbFailure.Duplicate)
        {
            return IdentityResult.Failed(new IdentityErrorDescriber().DuplicateUserName(user.UserName));
        }

        return IdentityResult.Success;
    }

    public Task<IdentityResult> DeleteAsync(User user, CancellationToken cancellationToken) =>
        throw new NotSupportedException("Users cannot be deleted.");

    public async Task<IdentityResult> UpdateAsync(User user, CancellationToken cancellationToken)
    {
        var previousStamp = user.ConcurrencyStamp;
        var newStamp = Guid.NewGuid().ToString("N");
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        var rows = await context
            .Users.Where(u => u.Id == user.Id && u.ConcurrencyStamp == previousStamp)
            .ExecuteUpdateAsync(
                s =>
                    s.SetProperty(u => u.PasswordHash, user.PasswordHash)
                        .SetProperty(u => u.SecurityStamp, user.SecurityStamp)
                        .SetProperty(u => u.ConcurrencyStamp, newStamp)
                        .SetProperty(u => u.UpdatedAt, now),
                cancellationToken
            );

        if (rows == 0)
        {
            return IdentityResult.Failed(new IdentityErrorDescriber().ConcurrencyFailure());
        }

        user.ConcurrencyStamp = newStamp;
        user.UpdatedAt = now;
        return IdentityResult.Success;
    }

    public async Task<User?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        return await context
            .Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == UserId.From(userId), cancellationToken);
    }

    public async Task<User?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        return await context
            .Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.NormalizedUserName == normalizedUserName, cancellationToken);
    }

    public Task SetPasswordHashAsync(User user, string? passwordHash, CancellationToken cancellationToken)
    {
        user.PasswordHash = passwordHash ?? "";
        return Task.CompletedTask;
    }

    public Task<string?> GetPasswordHashAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.PasswordHash);

    public Task<bool> HasPasswordAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult(!string.IsNullOrEmpty(user.PasswordHash));

    public Task SetSecurityStampAsync(User user, string stamp, CancellationToken cancellationToken)
    {
        user.SecurityStamp = stamp;
        return Task.CompletedTask;
    }

    public Task<string?> GetSecurityStampAsync(User user, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(user.SecurityStamp);

    public void Dispose() { }
}
