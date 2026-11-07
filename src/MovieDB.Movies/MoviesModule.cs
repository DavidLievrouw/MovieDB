using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MovieDB.Common;
using MovieDB.Movies.Lookup;

namespace MovieDB.Movies;

public static class MoviesModule
{
    public static System.Reflection.Assembly Assembly => typeof(MoviesModule).Assembly;

    public static IServiceCollection AddMoviesServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<GenreCatalog>();
        services.AddScoped<MovieQueries>();
        services.AddScoped<MovieCommands>();
        services.AddSingleton<IValidator<MovieInput>>(sp => new MovieInputValidator(
            sp.GetRequiredService<UserClock>()
        ));

        services.AddOptions<TmdbOptions>().Bind(configuration.GetSection(TmdbOptions.Section)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TmdbOptions>, TmdbOptionsValidator>());
        services
            .AddHttpClient<IMovieLookup, TmdbMovieLookup>(
                (sp, client) => TmdbMovieLookup.Configure(client, sp.GetRequiredService<IOptions<TmdbOptions>>().Value)
            )
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        return services;
    }
}
