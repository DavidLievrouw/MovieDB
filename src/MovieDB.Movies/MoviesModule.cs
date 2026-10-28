using Microsoft.Extensions.DependencyInjection;

namespace MovieDB.Movies;

public static class MoviesModule
{
    public static System.Reflection.Assembly Assembly => typeof(MoviesModule).Assembly;

    public static IServiceCollection AddMoviesServices(this IServiceCollection services) => services;
}
