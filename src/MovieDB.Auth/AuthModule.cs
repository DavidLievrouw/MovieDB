using Microsoft.Extensions.DependencyInjection;

namespace MovieDB.Auth;

public static class AuthModule
{
    public static System.Reflection.Assembly Assembly => typeof(AuthModule).Assembly;

    public static IServiceCollection AddAuthServices(this IServiceCollection services) => services;
}
