using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MovieDB.Infrastructure.Auth;

namespace MovieDB.Auth;

internal sealed class AuthProtection : IDisposable
{
    internal const string DeviceCookie = "moviedb.device";
    private readonly ConcurrencyLimiter _checks;
    private readonly ITimeLimitedDataProtector _protector;
    private readonly AuthOptions _auth;
    private readonly AuthProtectionOptions _limits;
    private readonly ILogger<AuthProtection> _logger;

    public AuthProtection(
        IDataProtectionProvider protection,
        IOptions<AuthOptions> authOptions,
        IOptions<AuthProtectionOptions> options,
        ILogger<AuthProtection> log
    )
    {
        _limits = options.Value;
        _auth = authOptions.Value;
        _logger = log;
        _protector = protection.CreateProtector("MovieDB.Auth.Device.v1").ToTimeLimitedDataProtector();
        _checks = new ConcurrencyLimiter(
            new ConcurrencyLimiterOptions { PermitLimit = _limits.ConcurrentPasswordChecks, QueueLimit = 0 }
        );
    }

    public RateLimitLease TryEnter() => _checks.AttemptAcquire();

    public string Device(HttpContext context)
    {
        if (context.Request.Cookies.TryGetValue(DeviceCookie, out var cookie))
        {
            try
            {
                if (cookie.Length <= 1024)
                {
                    var device = _protector.Unprotect(cookie);
                    if (device.Length == 32 && Guid.TryParseExact(device, "N", out _))
                    {
                        return device;
                    }
                }
                _logger.LogDebug("Invalid authentication device cookie was replaced.");
            }
            catch (CryptographicException)
            {
                _logger.LogDebug("Expired or unrecognized authentication device cookie was replaced.");
            }
        }

        var id = Guid.NewGuid().ToString("N");
        context.Response.Cookies.Append(
            DeviceCookie,
            _protector.Protect(id, TimeSpan.FromDays(_limits.DeviceCookieDays)),
            new CookieOptions
            {
                HttpOnly = true,
                Secure = _auth.SecureCookies || context.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/",
                MaxAge = TimeSpan.FromDays(_limits.DeviceCookieDays),
                IsEssential = true,
            }
        );
        return id;
    }

    public IResult Reject(HttpContext context, int retrySeconds, bool unavailable = false)
    {
        _logger.LogInformation(
            "Authentication request rejected by {ProtectionDecision}.",
            unavailable ? "unavailable protection state" : "rate or reputation policy"
        );
        var retry = Math.Max(1, retrySeconds);
        context.Response.Headers.RetryAfter = retry.ToString(System.Globalization.CultureInfo.InvariantCulture);
        context.Response.Headers.CacheControl = "no-store";
        var back =
            context.Request.Path == AuthEndpoints.ChangePasswordPath
                ? AuthEndpoints.ChangePasswordPath
                : AuthEndpoints.LoginPath;
        var theme = context.Request.Cookies["films-theme"] == "light" ? "light" : "dark";
        return Results.Content(
            $"""
            <!doctype html><html lang="nl-BE" data-theme="{theme}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><link rel="stylesheet" href="/_content/MovieDB.Auth/auth.css"><title>Probeer het later opnieuw – Films</title></head>
            <body class="auth-shell" style="margin:0"><main class="auth-card"><h1>Probeer het later opnieuw</h1><p class="auth-error" role="alert">Aanmelden is tijdelijk beperkt. Wacht {retry} seconden voor je het opnieuw probeert.</p><a href="{back}">Terug naar het formulier</a></main></body></html>
            """,
            "text/html",
            statusCode: unavailable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status429TooManyRequests
        );
    }

    public void Dispose() => _checks.Dispose();
}
