using FluentValidation;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MovieDB.Common;
using MovieDB.Infrastructure.Auth;
using MovieDB.Infrastructure.Entities;

namespace MovieDB.Auth;

public static class AuthEndpoints
{
    public const string LoginPath = "/login";
    public const string LogoutPath = "/logout";
    public const string AccessDeniedPath = "/accessdenied";
    public const string ChangePasswordPath = "/account/wachtwoord";

    /// <summary>Verified for unknown users so response time does not reveal whether a user name exists.</summary>
    private static readonly User DummyUser = new();

    private static readonly string DummyPasswordHash = new PasswordHasher<User>().HashPassword(
        DummyUser,
        Guid.NewGuid().ToString()
    );

    private sealed record EndpointServices(
        AuthProtection Protection,
        AuthThrottleStore Throttle,
        SignInManager<User> SignInManager,
        ILoggerFactory LoggerFactory
    );

    extension(IServiceCollection services)
    {
        public IServiceCollection AddAuthServices(IConfiguration configuration)
        {
            services
                .AddOptions<AuthOptions>()
                .Bind(configuration.GetSection(AuthOptions.Section))
                .ValidateDataAnnotations()
                .ValidateOnStart();
            services.AddValidatorsFromAssemblyContaining<LoginFormValidator>(
                ServiceLifetime.Singleton,
                includeInternalTypes: true
            );
            services
                .AddOptions<AuthProtectionOptions>()
                .Bind(configuration.GetSection(AuthProtectionOptions.Section))
                .ValidateDataAnnotations()
                .Validate(
                    limits => 3L * limits.GlobalAttempts * 60 <= 500L * limits.GlobalWindowSeconds,
                    "The global rate must allow at most 500 new reputation keys per minute so indexed cleanup can keep up."
                )
                .ValidateOnStart();
            services.AddSingleton<AuthThrottleStore>();
            services.AddSingleton<AuthProtection>();
            services.AddHostedService<AuthProtectionCleanup>();
            return services;
        }
    }

    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapAuthEndpoints()
        {
            endpoints.MapPost(LoginPath, LoginAsync).AllowAnonymous();
            endpoints.MapPost(LogoutPath, LogoutAsync).AllowAnonymous();
            endpoints.MapPost(ChangePasswordPath, ChangePasswordAsync).RequireAuthorization();
            return endpoints;
        }
    }

    public static string LoginFailedUrl(string? returnUrl, string error = "1") =>
        LocalUrl.IsLocal(returnUrl)
            ? $"{LoginPath}?error={error}&returnUrl={Uri.EscapeDataString(returnUrl!)}"
            : $"{LoginPath}?error={error}";

    private static async Task<IResult> LoginAsync(
        [FromForm] LoginForm form,
        HttpContext context,
        IAntiforgery antiforgery,
        [AsParameters] EndpointServices services,
        IValidator<LoginForm> validator,
        IPasswordHasher<User> passwordHasher,
        CancellationToken cancellationToken
    )
    {
        // Reject cross-site credential posts before any credential check or sign-in.
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return Results.BadRequest();
        }

        var protection = services.Protection;
        var signInManager = services.SignInManager;
        var logger = services.LoggerFactory.CreateLogger("MovieDB.Auth.Login");
        using var lease = protection.TryEnter();
        if (!lease.IsAcquired)
        {
            return protection.Reject(context, 1);
        }

        var (reservation, rejection) = await ReserveAttemptAsync(
            context,
            services,
            signInManager.UserManager.NormalizeName(form.Username is { Length: <= 255 } ? form.Username : ""),
            logger,
            cancellationToken
        );
        if (rejection is not null)
        {
            return rejection;
        }
        if (reservation is null)
        {
            return protection.Reject(context, 60, unavailable: true);
        }

        if (!(await validator.ValidateAsync(form, cancellationToken)).IsValid)
        {
            var completionFailure = await CompleteAttemptAsync(
                reservation,
                AuthAttemptOutcome.InvalidInput,
                context,
                services,
                logger
            );
            return completionFailure ?? Results.Redirect(LoginFailedUrl(form.ReturnUrl));
        }

        return await AuthenticateLoginAsync(form, context, services, passwordHasher, reservation, logger);
    }

    private static async Task<IResult> AuthenticateLoginAsync(
        LoginForm form,
        HttpContext context,
        EndpointServices services,
        IPasswordHasher<User> passwordHasher,
        AuthReservation reservation,
        ILogger logger
    )
    {
        var signInManager = services.SignInManager;
        var user = await signInManager.UserManager.FindByNameAsync(form.Username!);
        if (user is null)
        {
            passwordHasher.VerifyHashedPassword(DummyUser, DummyPasswordHash, form.Password!);
            return await FailedLoginAsync(form, reservation, context, services, logger);
        }

        var result = await signInManager.CheckPasswordSignInAsync(user, form.Password!, lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            return await FailedLoginAsync(form, reservation, context, services, logger);
        }

        return await CompleteLoginAsync(user, form, reservation, context, services, logger);
    }

    private static async Task<IResult> CompleteLoginAsync(
        User user,
        LoginForm form,
        AuthReservation reservation,
        HttpContext context,
        EndpointServices services,
        ILogger logger
    )
    {
        var completionFailure = await CompleteAttemptAsync(
            reservation,
            AuthAttemptOutcome.Succeeded,
            context,
            services,
            logger
        );
        if (completionFailure is not null)
        {
            return completionFailure;
        }

        await services.SignInManager.SignInAsync(user, isPersistent: form.RememberMe);
        return Results.Redirect(LocalUrl.OrDefault(form.ReturnUrl));
    }

    private static async Task<IResult> FailedLoginAsync(
        LoginForm form,
        AuthReservation reservation,
        HttpContext context,
        EndpointServices services,
        ILogger logger
    )
    {
        var completionFailure = await CompleteAttemptAsync(
            reservation,
            AuthAttemptOutcome.Failed,
            context,
            services,
            logger
        );
        if (completionFailure is not null)
        {
            return completionFailure;
        }

        logger.LogInformation("Failed login attempt.");
        return Results.Redirect(LoginFailedUrl(form.ReturnUrl));
    }

    private static async Task<(AuthReservation? Reservation, IResult? Rejection)> ReserveAttemptAsync(
        HttpContext context,
        EndpointServices services,
        string normalizedUsername,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var protection = services.Protection;
        var admission = await services.Throttle.ReserveAsync(
            normalizedUsername,
            ClientAddress(context),
            protection.Device(context),
            cancellationToken
        );
        if (!admission.IsSuccess)
        {
            logger.LogWarning(
                "Authentication protection could not reserve an attempt: {Reason}",
                admission.Error.Message
            );
            return (null, protection.Reject(context, 60, unavailable: true));
        }

        if (admission.Value.Reservation is { } reservation)
        {
            return (reservation, null);
        }

        return (null, protection.Reject(context, admission.Value.RetryAfterSeconds));
    }

    private static async Task<IResult?> CompleteAttemptAsync(
        AuthReservation reservation,
        AuthAttemptOutcome outcome,
        HttpContext context,
        EndpointServices services,
        ILogger logger
    )
    {
        var completed = await services.Throttle.CompleteAsync(reservation, outcome, CancellationToken.None);
        if (completed.IsSuccess)
        {
            return null;
        }

        logger.LogWarning("Authentication protection completion failed: {Reason}", completed.Error.Message);
        return services.Protection.Reject(context, 60, unavailable: true);
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        SignInManager<User> signInManager
    )
    {
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return Results.BadRequest();
        }

        await signInManager.SignOutAsync();
        return Results.Redirect(LoginPath);
    }

    private static async Task<IResult> ChangePasswordAsync(
        [FromForm] ChangePasswordModel form,
        HttpContext context,
        IAntiforgery antiforgery,
        [AsParameters] EndpointServices services,
        IValidator<ChangePasswordModel> validator,
        CancellationToken cancellationToken
    )
    {
        if (!await antiforgery.IsRequestValidAsync(context))
        {
            return Results.BadRequest();
        }

        var protection = services.Protection;
        var logger = services.LoggerFactory.CreateLogger("MovieDB.Auth.ChangePassword");
        var userManager = services.SignInManager.UserManager;
        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
        {
            return Results.Redirect(LoginPath);
        }

        using var lease = protection.TryEnter();
        if (!lease.IsAcquired)
        {
            return protection.Reject(context, 1);
        }

        var (reservation, rejection) = await ReserveAttemptAsync(
            context,
            services,
            userManager.NormalizeName(user.UserName),
            logger,
            cancellationToken
        );
        if (rejection is not null)
        {
            return rejection;
        }
        if (reservation is null)
        {
            return protection.Reject(context, 60, unavailable: true);
        }

        if (!(await validator.ValidateAsync(form, cancellationToken)).IsValid)
        {
            var completionFailure = await CompleteAttemptAsync(
                reservation,
                AuthAttemptOutcome.InvalidInput,
                context,
                services,
                logger
            );
            return completionFailure ?? Results.Redirect(ChangePasswordUrl(form, "invalid"));
        }

        return await ProcessPasswordChangeAsync(form, user, context, services, logger, reservation);
    }

    private static async Task<IResult> ProcessPasswordChangeAsync(
        ChangePasswordModel form,
        User user,
        HttpContext context,
        EndpointServices services,
        ILogger logger,
        AuthReservation reservation
    )
    {
        var userManager = services.SignInManager.UserManager;
        var result = await userManager.ChangePasswordAsync(user, form.CurrentPassword!, form.NewPassword!);
        var outcome = GetPasswordChangeOutcome(result);
        var completionFailure = await CompleteAttemptAsync(reservation, outcome, context, services, logger);
        if (completionFailure is not null)
        {
            return completionFailure;
        }

        if (!result.Succeeded)
        {
            return Results.Redirect(ChangePasswordUrl(form, IsPasswordMismatch(result) ? "current" : "failed"));
        }

        await services.SignInManager.RefreshSignInAsync(user);
        return Results.Redirect(LocalUrl.OrDefault(form.ReturnUrl));
    }

    private static AuthAttemptOutcome GetPasswordChangeOutcome(IdentityResult result)
    {
        var passwordChangeOutcome = IsPasswordMismatch(result)
            ? AuthAttemptOutcome.Failed
            : AuthAttemptOutcome.InvalidInput;
        return result.Succeeded ? AuthAttemptOutcome.Succeeded : passwordChangeOutcome;
    }

    private static bool IsPasswordMismatch(IdentityResult result) =>
        result.Errors.Any(identityError => identityError.Code == "PasswordMismatch");

    private static string ChangePasswordUrl(ChangePasswordModel form, string? error)
    {
        var url = $"{ChangePasswordPath}?returnUrl={Uri.EscapeDataString(LocalUrl.OrDefault(form.ReturnUrl))}";
        return error is null ? url : $"{url}&error={Uri.EscapeDataString(error)}";
    }

    private static string ClientAddress(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        var mapToIPv4 = address?.IsIPv4MappedToIPv6 ?? false ? address.MapToIPv4() : address;
        return address is null ? "unknown" : mapToIPv4?.ToString() ?? string.Empty;
    }
}
