using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using MovieDB.Common;

namespace MovieDB.Movies;

public static class MoviesModule
{
    public static System.Reflection.Assembly Assembly => typeof(MoviesModule).Assembly;

    public static IServiceCollection AddMoviesServices(this IServiceCollection services)
    {
        services.AddScoped<MovieQueries>();
        services.AddScoped<MovieCommands>();
        services.AddSingleton<IValidator<MovieInput>>(sp => new MovieInputValidator(
            sp.GetRequiredService<UserClock>()
        ));
        return services;
    }
}
