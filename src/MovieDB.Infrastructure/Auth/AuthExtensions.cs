using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using MovieDB.Infrastructure.Entities;

namespace MovieDB.Infrastructure.Auth;

public static class AuthExtensions
{
    public const int MinimumPasswordLength = 8;

    /// <summary>Identity core over the users table. The host adds cookies and the sign-in manager.</summary>
    public static IdentityBuilder AddMovieDbIdentity(this IServiceCollection services)
    {
        services.AddSingleton<DefaultUserSeeder>();
        return services
            .AddIdentityCore<User>(o =>
            {
                o.Password.RequiredLength = MinimumPasswordLength;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.User.AllowedUserNameCharacters = "";
            })
            .AddUserStore<UserStore>();
    }
}
