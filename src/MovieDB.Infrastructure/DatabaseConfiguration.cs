using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microting.EntityFrameworkCore.MySql.Infrastructure;
using MySqlConnector;

namespace MovieDB.Infrastructure;

public static class DatabaseConfiguration
{
    /// <summary>Production is MariaDB 12.3.x. Fixed so startup never needs a connection to detect the version.</summary>
    public static readonly ServerVersion Version = ServerVersion.Create(12, 3, 3, ServerType.MariaDb);

    /// <summary>
    /// The connection and context pool size for this single-user application. MySqlConnector defaults to 100
    /// connections and EF Core to 1024 pooled contexts.
    /// </summary>
    internal const int PoolSize = 8;

    public static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        var connection = new MySqlConnectionStringBuilder(connectionString) { CharacterSet = "utf8mb4" };
        if (!ContainsPoolSize(connectionString))
        {
            connection.MaximumPoolSize = PoolSize;
        }

        options.UseMySql(connection.ConnectionString, Version, o => o.MigrationsHistoryTable("__EFMigrationsHistory"));
    }

    /// <summary>Whether the connection string sets the maximum pool size itself (under any of its names).</summary>
    /// <param name="connectionString">The connection string to check.</param>
    internal static bool ContainsPoolSize(string connectionString)
    {
        var raw = new DbConnectionStringBuilder { ConnectionString = connectionString };
        return raw.ContainsKey("Maximum Pool Size")
            || raw.ContainsKey("MaximumPoolSize")
            || raw.ContainsKey("Max Pool Size")
            || raw.ContainsKey("MaxPoolSize");
    }

    /// <summary>Fails when the connection string does not explicitly select utf8mb4 (CharSet=utf8mb4).</summary>
    /// <param name="connectionString">The connection string to check.</param>
    /// <exception cref="InvalidOperationException">The connection string does not specify CharSet=utf8mb4.</exception>
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
            .AddPooledDbContextFactory<MovieDbContext>(o => Configure(o, connectionString), PoolSize)
            .AddScoped<MovieDbContext>(sp =>
                sp.GetRequiredService<IDbContextFactory<MovieDbContext>>().CreateDbContext()
            );
    }
}
