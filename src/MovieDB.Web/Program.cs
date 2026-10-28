using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using MovieDB.Auth;
using MovieDB.Common;
using MovieDB.Infrastructure;
using MovieDB.Infrastructure.Auth;
using MovieDB.Movies;
using MovieDB.Web;
using Serilog;
using Serilog.Events;

if (args is ["--health-check"])
{
    await using var logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
    var configuredEndpoint = Environment.GetEnvironmentVariable("Kestrel__Endpoints__Http__Url");
    var configuredPorts = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS");
    var parsedPort = configuredPorts is not null && int.TryParse(configuredPorts, out var port) ? port : 8080;
    var healthPort = configuredEndpoint is not null ? new Uri(configuredEndpoint).Port : parsedPort;
    var healthHost = Environment.GetEnvironmentVariable("HEALTHCHECK_HOST") ?? "mdb.dalion.eu";
    using var client = new HttpClient();
    client.Timeout = TimeSpan.FromSeconds(10);
    using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{healthPort}/health");
    request.Headers.Host = healthHost;

    try
    {
        using var response = await client.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            logger.Error("Health endpoint returned HTTP {StatusCode}.", (int)response.StatusCode);
        }
        return response.IsSuccessStatusCode ? 0 : 1;
    }
    catch (HttpRequestException exception)
    {
        logger.Error(exception, "Could not reach the local health endpoint on port {Port}.", healthPort);
        return 1;
    }
    catch (TaskCanceledException exception)
    {
        logger.Error(exception, "Local health endpoint timed out on port {Port}.", healthPort);
        return 1;
    }
}

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog(
    (_, loggerConfiguration) =>
        loggerConfiguration
            .MinimumLevel.Is(builder.Environment.IsDevelopment() ? LogEventLevel.Debug : LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Console()
);

builder
    .Services.AddOptions<AppOptions>()
    .Bind(builder.Configuration.GetSection(AppOptions.Section))
    .ValidateDataAnnotations()
    .Validate(
        options => UserClock.TryResolveZone(options.TimeZone, out _),
        "App:TimeZone must be a valid IANA time zone id."
    )
    .ValidateOnStart();
builder
    .Services.AddOptions<AuthProtectionOptions>()
    .Bind(builder.Configuration.GetSection(AuthProtectionOptions.Section))
    .ValidateDataAnnotations()
    .ValidateOnStart();

var connectionString =
    builder.Configuration.GetConnectionString("MovieDb")
    ?? throw new InvalidOperationException(
        $"ConnectionStrings:MovieDb is not configured (environment: {builder.Environment.EnvironmentName}). "
            + "User-secrets are only loaded in Development; use the 'MovieDB.Web: http' launch profile or set ASPNETCORE_ENVIRONMENT=Development."
    );

builder.Services.AddMovieDbDatabase(connectionString);
builder.Services.AddHostedService<DatabaseMigrator>();
var dataProtection = builder
    .Services.AddDataProtection()
    .SetApplicationName("MovieDB")
    .PersistKeysToDbContext<MovieDbContext>();
var keyRingCertificate = KeyRingProtection.LoadCertificate(
    builder.Configuration.GetSection(KeyRingProtectionOptions.Section).Get<KeyRingProtectionOptions>() ?? new(),
    builder.Environment.IsDevelopment()
);
if (keyRingCertificate is not null)
{
    dataProtection.ProtectKeysWithCertificate(keyRingCertificate);
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp => new UserClock(
    sp.GetRequiredService<TimeProvider>(),
    UserClock.ResolveZone(sp.GetRequiredService<IOptions<AppOptions>>().Value.TimeZone)
));

builder.Services.AddAuthServices().AddMoviesServices();

// Configure trusted proxies explicitly so clients cannot spoof X-Forwarded-For to bypass login rate limits.
var knownProxyAddresses = builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
var knownProxies = knownProxyAddresses
    .Select(proxyAddress =>
        IPAddress.TryParse(proxyAddress, out var address)
            ? address
            : throw new InvalidOperationException(
                $"ForwardedHeaders:KnownProxies contains an invalid IP address: {proxyAddress}"
            )
    )
    .ToArray();
builder.Services.PostConfigure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    foreach (var address in knownProxies)
    {
        o.KnownProxies.Add(address);
    }
});

var healthChecks = builder
    .Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("mariadb", timeout: TimeSpan.FromSeconds(5));
var healthOptions = builder
    .Services.AddOptions<HealthOptions>()
    .Bind(builder.Configuration.GetSection(HealthOptions.Section));
if (!string.IsNullOrWhiteSpace(builder.Configuration[$"{HealthOptions.Section}:{nameof(HealthOptions.PublicUrl)}"]))
{
    healthOptions
        .Validate(
            options =>
                Uri.TryCreate(options.PublicUrl, UriKind.Absolute, out var uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && string.IsNullOrEmpty(uri.UserInfo)
                && uri.AbsolutePath == "/"
                && string.IsNullOrEmpty(uri.Query)
                && string.IsNullOrEmpty(uri.Fragment),
            "Health:PublicUrl must be an absolute HTTPS origin without credentials, path, query, or fragment."
        )
        .ValidateOnStart();
    builder
        .Services.AddHttpClient(PublicHostHealthCheck.ClientName, client => client.Timeout = TimeSpan.FromSeconds(5))
        .ConfigurePrimaryHttpMessageHandler(() =>
            new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }
        );
    healthChecks.AddCheck<PublicHostHealthCheck>("public-host", timeout: TimeSpan.FromSeconds(5));
}

var app = builder.Build();

if (!app.Environment.IsDevelopment() && knownProxies.Length > 0)
{
    app.UseForwardedHeaders();
}
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseSerilogRequestLogging();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapGet("/health/live", () => Results.Text(PublicHostHealthCheck.LivenessResponse)).AllowAnonymous();

await app.RunAsync();
return 0;

public partial class Program;
