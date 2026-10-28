using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microting.EntityFrameworkCore.MySql.Infrastructure;
using MySqlConnector;

namespace MovieDB.Infrastructure;

public static class DatabaseConfiguration
{
    /// <summary>Production is MariaDB 12.3.x. Fixed so startup never needs a connection to detect the version.</summary>
    public static readonly ServerVersion Version = ServerVersion.Create(12, 3, 3, ServerType.MariaDb);

    public static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        var connection = new MySqlConnectionStringBuilder(connectionString) { CharacterSet = "utf8mb4" };
        options.UseMySql(connection.ConnectionString, Version, o => o.MigrationsHistoryTable("__EFMigrationsHistory"));
    }

    /// <summary>Fails when the connection string does not explicitly select utf8mb4 (CharSet=utf8mb4).</summary>
    public static string RequireUtf8mb4(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString);
        return string.Equals(builder.CharacterSet, "utf8mb4", StringComparison.OrdinalIgnoreCase)
            ? connectionString
            : throw new InvalidOperationException("The MovieDb connection string must contain CharSet=utf8mb4.");
    }

    public static IServiceCollection AddMovieDbDatabase(this IServiceCollection services, string connectionString)
    {
        RequireUtf8mb4(connectionString);
        return services
            .AddPooledDbContextFactory<MovieDbContext>(o => Configure(o, connectionString))
            .AddScoped<MovieDbContext>(sp =>
                sp.GetRequiredService<IDbContextFactory<MovieDbContext>>().CreateDbContext()
            );
    }
}
