using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using MovieDB.Infrastructure;

namespace MovieDB.Web;

internal sealed class DatabaseHealthCheck(IDbContextFactory<MovieDbContext> factory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            _ = await db.Genres.AsNoTracking().AnyAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
            when (exception is DbException or InvalidOperationException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("MariaDB query failed.", exception);
        }
    }
}

internal sealed class PublicHostHealthCheck(IHttpClientFactory clients, IOptions<HealthOptions> options) : IHealthCheck
{
    public const string ClientName = "public-host-health";
    public const string LivenessResponse = "MovieDB";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            using var client = clients.CreateClient(ClientName);
            using var response = await client.GetAsync(
                new Uri(new Uri(options.Value.PublicUrl!), "/health/live"),
                cancellationToken
            );
            if (
                response.IsSuccessStatusCode
                && await response.Content.ReadAsStringAsync(cancellationToken) == LivenessResponse
            )
            {
                return HealthCheckResult.Healthy();
            }

            return HealthCheckResult.Unhealthy("Public HTTPS endpoint returned an unexpected response.");
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Public HTTPS endpoint is unreachable.", exception);
        }
    }
}

public sealed class HealthOptions
{
    public const string Section = "Health";

    /// <summary>Public HTTPS origin of the app. When empty (development, tests) the public-host check is not registered.</summary>
    public string? PublicUrl { get; set; }
}
