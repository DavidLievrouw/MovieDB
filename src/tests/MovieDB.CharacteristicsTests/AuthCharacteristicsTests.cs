using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MovieDB.Auth;
using MovieDB.Infrastructure;
using MovieDB.Infrastructure.Entities;
using Shouldly;

namespace MovieDB.CharacteristicsTests;

[Collection(MariaDbCollection.Name)]
public sealed partial class AuthCharacteristicsTests
{
    private const string Password = "S3cret pässword!";

    private readonly MariaDbFixture _fixture;

    public AuthCharacteristicsTests(MariaDbFixture fixture)
    {
        this._fixture = fixture;
    }

    private sealed class AuthApp : WebApplicationFactory<Program>
    {
        private readonly string _connectionString;
        private readonly int _loginLimit;
        private readonly bool _secureCookies;
        private readonly string _environment;
        private readonly bool _configureKeyRingCertificate;
        private TestDataProtectionCertificate? _certificate;

        public AuthApp(
            string connectionString,
            int loginLimit,
            bool secureCookies,
            string environment = "Development",
            bool configureKeyRingCertificate = true
        )
        {
            _connectionString = connectionString;
            _loginLimit = loginLimit;
            _secureCookies = secureCookies;
            _environment = environment;
            _configureKeyRingCertificate = configureKeyRingCertificate;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _certificate?.Dispose();
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(_environment);
            if (_environment != "Development" && _configureKeyRingCertificate)
            {
                _certificate = TestDataProtectionCertificate.Create();
                _certificate.Configure(builder);
            }

            builder
                .UseSetting("ConnectionStrings:MovieDb", _connectionString)
                .UseSetting("Auth:SecureCookies", _secureCookies.ToString())
                .UseSetting("Auth:Protection:IpAttempts", _loginLimit.ToString());
        }

        public HttpClient NewClient() =>
            CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
    }

    private static string LocationPathOf(HttpResponseMessage response) =>
        response.Headers.Location!.IsAbsoluteUri
            ? response.Headers.Location.PathAndQuery
            : response.Headers.Location.OriginalString;

    private async Task<AuthApp> StartAsync(int loginLimit = 1000, bool secureCookies = false)
    {
        var app = new AuthApp(await _fixture.CreateDatabaseAsync(), loginLimit, secureCookies);
        // Creating the host runs the migrator, which seeds admin with the default password from the environment.
        app.NewClient().Dispose();
        return app;
    }

    private static async Task CreateUserAsync(AuthApp app, string username)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var result = await users.CreateAsync(new User { UserName = username }, Password);
        result.Succeeded.ShouldBeTrue(string.Join(", ", result.Errors.Select(e => e.Code)));
    }

    [GeneratedRegex(
        @"<input[^>]*name=""__RequestVerificationToken""[^>]*value=""([^""]+)""|<input[^>]*value=""([^""]+)""[^>]*name=""__RequestVerificationToken"""
    )]
    private static partial Regex TokenRegex();

    private static async Task<string> TokenAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);
        var html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        var match = TokenRegex().Match(html);
        match.Success.ShouldBeTrue($"no antiforgery token on {path} ({(int)response.StatusCode})");
        return WebUtility.HtmlDecode(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
    }

    private static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string username,
        string password,
        string? returnUrl = null,
        string? token = null,
        bool rememberMe = false
    )
    {
        token ??= await TokenAsync(client, "/login");
        var form = new Dictionary<string, string>
        {
            ["Username"] = username,
            ["Password"] = password,
            ["ReturnUrl"] = returnUrl ?? "",
            ["__RequestVerificationToken"] = token,
        };
        if (rememberMe)
        {
            form["RememberMe"] = "true";
        }

        return await client.PostAsync("/login", new FormUrlEncodedContent(form), TestContext.Current.CancellationToken);
    }

    private static Task<HttpResponseMessage> PostChangeAsync(
        HttpClient client,
        string token,
        string current,
        string next,
        string confirm
    ) =>
        client.PostAsync(
            "/account/wachtwoord",
            new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = token,
                    ["CurrentPassword"] = current,
                    ["NewPassword"] = next,
                    ["ConfirmPassword"] = confirm,
                }
            ),
            TestContext.Current.CancellationToken
        );

    [Fact]
    public async Task Anonymous_requests_are_redirected_to_login_with_a_local_return_url()
    {
        await using var app = await StartAsync();

        var response = await app.NewClient().GetAsync("/", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        LocationPathOf(response).ShouldBe("/login?returnUrl=%2F");
    }

    [Fact]
    public async Task Login_access_denied_health_and_static_assets_are_public()
    {
        await using var app = await StartAsync();
        var client = app.NewClient();

        foreach (
            var path in new[] { "/login", "/accessdenied", "/health", "/health/live", "/_framework/blazor.web.js" }
        )
        {
            (await client.GetAsync(path, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(
                HttpStatusCode.OK,
                path
            );
        }

        (
            await client.GetAsync("/_content/MovieDB.Auth/auth.css", TestContext.Current.CancellationToken)
        ).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Login_page_is_dutch_with_the_dutch_culture_and_circuit_options()
    {
        await using var app = await StartAsync();
        var page = await app.NewClient().GetStringAsync("/login", TestContext.Current.CancellationToken);
        var options = app.Services.GetRequiredService<IOptions<CircuitOptions>>().Value;

        page.ShouldContain("<html lang=\"nl-BE\"");
        page.ShouldContain("Aanmelden – Films");
        page.ShouldContain("Database van films");
        page.ShouldContain("MovieDB.Web.");
        page.ShouldContain(".styles.css");
        options.DisconnectedCircuitRetentionPeriod.ShouldBe(TimeSpan.FromMinutes(5));
        options.DisconnectedCircuitMaxRetained.ShouldBe(20);
    }

    [Fact]
    public async Task Responses_include_security_headers_and_a_nonce_on_scripts()
    {
        await using var app = await StartAsync();
        var client = app.NewClient();

        var health = await client.GetAsync("/health/live", TestContext.Current.CancellationToken);
        var policy = health.Headers.GetValues("Content-Security-Policy").Single();
        policy.ShouldContain("default-src 'self'");
        policy.ShouldContain("frame-ancestors 'none'");
        policy.ShouldContain("form-action 'self'");
        policy.ShouldContain("style-src 'self' 'unsafe-inline'");
        policy.ShouldNotContain("unsafe-eval");
        health.Headers.GetValues("Permissions-Policy").ShouldContain("camera=(), microphone=(), geolocation=()");
        health.Headers.GetValues("Referrer-Policy").ShouldContain("strict-origin-when-cross-origin");
        health.Headers.GetValues("X-Content-Type-Options").ShouldContain("nosniff");
        health.Headers.GetValues("X-Frame-Options").ShouldContain("DENY");

        var login = await client.GetAsync("/login", TestContext.Current.CancellationToken);
        var page = WebUtility.HtmlDecode(await login.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var loginPolicy = login
            .Headers.GetValues("Content-Security-Policy")
            .Single(p => p.Contains("default-src 'self'", StringComparison.Ordinal));
        var nonce = NonceRegex().Match(loginPolicy).Groups[1].Value;
        nonce.ShouldNotBeEmpty();
        page.ShouldContain($"<script type=\"importmap\" nonce=\"{nonce}\">");
        page.ShouldContain($"<script nonce=\"{nonce}\" src=");
        page.ShouldNotContain("fonts.googleapis");
    }

    [GeneratedRegex("'nonce-([^']+)'")]
    private static partial Regex NonceRegex();

    [Fact]
    public async Task The_seeded_default_user_can_sign_in_and_gets_a_hardened_cookie()
    {
        await using var app = await StartAsync(secureCookies: true);
        var client = app.NewClient();
        client.BaseAddress = new Uri("https://localhost");

        var response = await LoginAsync(client, "admin", "demodemo");

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        LocationPathOf(response).ShouldBe("/");
        var cookie = response
            .Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith("moviedb.auth=", StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=lax", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldNotContain("expires=", Case.Insensitive);
        (await client.GetAsync("/films", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_home_page_redirects_to_the_overview_and_the_shell_serves_local_fonts()
    {
        await using var app = await StartAsync();
        var client = app.NewClient();
        await LoginAsync(client, "admin", "demodemo");

        var home = await client.GetAsync("/", TestContext.Current.CancellationToken);
        var overview = await client.GetAsync("/films", TestContext.Current.CancellationToken);
        var detail = await client.GetAsync("/films/999999", TestContext.Current.CancellationToken);
        var markup = await overview.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        home.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        LocationPathOf(home).ShouldBe("/films");
        overview.StatusCode.ShouldBe(HttpStatusCode.OK);
        detail.StatusCode.ShouldBe(HttpStatusCode.OK);
        markup.ShouldContain("Nieuwe film");
        markup.ShouldContain("rel=\"stylesheet\"");
        markup.ShouldNotContain("fonts.googleapis");
        overview
            .Headers.GetValues("Content-Security-Policy")
            .Single(p => p.Contains("default-src 'self'", StringComparison.Ordinal))
            .ShouldContain("font-src 'self'");
        foreach (
            var font in new[] { "inter-latin", "inter-latin-ext", "space-grotesk-latin", "space-grotesk-latin-ext" }
        )
        {
            var response = await client.GetAsync($"/fonts/{font}.woff2", TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK, font);
        }
    }

    [Fact]
    public async Task Remember_me_makes_the_cookie_persistent()
    {
        await using var app = await StartAsync();

        var response = await LoginAsync(app.NewClient(), "admin", "demodemo", rememberMe: true);

        response
            .Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith("moviedb.auth=", StringComparison.Ordinal))
            .ShouldContain("expires=", Case.Insensitive);
    }

    [Fact]
    public async Task Authenticated_pages_are_dutch_and_link_to_password_change_and_logout()
    {
        await using var app = await StartAsync();
        var client = app.NewClient();
        await LoginAsync(client, "admin", "demodemo");

        var markup = await client.GetStringAsync("/films", TestContext.Current.CancellationToken);

        markup.ShouldContain("href=\"/account/wachtwoord\"");
        markup.ShouldContain("action=\"/logout\"");
        markup.ShouldContain("Afmelden");
    }

    [Theory]
    [InlineData("admin", "wrong password")]
    [InlineData("nobody", "whatever")]
    [InlineData("", "")]
    public async Task Failed_logins_show_one_generic_message(string username, string password)
    {
        await using var app = await StartAsync();
        var client = app.NewClient();

        var response = await LoginAsync(client, username, password);

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        LocationPathOf(response).ShouldBe("/login?error=1");
        (await client.GetStringAsync("/login?error=1", TestContext.Current.CancellationToken)).ShouldContain(
            "Aanmelden mislukt."
        );
        (await client.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(
            HttpStatusCode.Redirect
        );
    }

    [Fact]
    public async Task User_names_are_matched_case_insensitively()
    {
        await using var app = await StartAsync();

        LocationPathOf(await LoginAsync(app.NewClient(), "ADMIN", "demodemo")).ShouldBe("/");
    }

    [Theory]
    [InlineData("/films/5?x=1", "/films/5?x=1")]
    [InlineData("//evil.example", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("https://evil.example/", "/")]
    public async Task Only_local_return_urls_are_followed(string returnUrl, string expected)
    {
        await using var app = await StartAsync();

        var response = await LoginAsync(app.NewClient(), "admin", "demodemo", returnUrl);

        LocationPathOf(response).ShouldBe(expected);
    }

    [Fact]
    public async Task Login_post_without_antiforgery_token_is_rejected_before_any_throttle_state_is_written()
    {
        await using var app = await StartAsync();
        var client = app.NewClient();
        (await client.GetAsync("/login", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await client.PostAsync(
            "/login",
            new FormUrlEncodedContent(
                new Dictionary<string, string> { ["Username"] = "admin", ["Password"] = "demodemo" }
            ),
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(
            HttpStatusCode.Redirect
        );
        await using var context = await app
            .Services.GetRequiredService<IDbContextFactory<MovieDB.Infrastructure.MovieDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        (
            await context.AuthThrottleStates.SingleAsync(
                state => state.Key == "global",
                TestContext.Current.CancellationToken
            )
        ).Attempts.ShouldBe(0);
    }

    [Fact]
    public async Task Login_attempts_are_rate_limited_per_client_with_a_dutch_retry_page()
    {
        await using var app = await StartAsync(loginLimit: 3);
        var client = app.NewClient();
        client.DefaultRequestHeaders.Add("Cookie", "films-theme=light");
        var token = await TokenAsync(client, "/login");

        for (var i = 0; i < 3; i++)
        {
            (await LoginAsync(client, "nobody", "x", token: token)).StatusCode.ShouldBe(HttpStatusCode.Redirect);
        }

        var blocked = await LoginAsync(client, "nobody", "x", token: token);
        blocked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        blocked.Headers.RetryAfter!.Delta!.Value.ShouldBeGreaterThan(TimeSpan.Zero);
        blocked.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var page = await blocked.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        page.ShouldContain("data-theme=\"light\"");
        page.ShouldContain("class=\"auth-card\"");
        page.ShouldContain("role=\"alert\"");
        (await client.GetAsync("/health", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(
            HttpStatusCode.OK
        );
    }

    [Fact]
    public async Task Untrusted_forwarded_for_headers_do_not_bypass_rate_limiting()
    {
        await using var app = await StartAsync(loginLimit: 2);
        var client = app.NewClient();
        var token = await TokenAsync(client, "/login");

        for (var index = 0; index < 2; index++)
        {
            using var request = LoginRequest(token, $"203.0.113.{index + 1}");
            (await client.SendAsync(request, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(
                HttpStatusCode.Redirect
            );
        }

        using var limited = LoginRequest(token, "203.0.113.200");
        (await client.SendAsync(limited, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(
            HttpStatusCode.TooManyRequests
        );
    }

    [Fact]
    public async Task Device_cookie_is_protected_and_a_forged_one_does_not_bypass_limits()
    {
        await using var app = await StartAsync(loginLimit: 1, secureCookies: true);
        var client = app.NewClient();
        client.BaseAddress = new Uri("https://localhost");
        var response = await LoginAsync(client, "nobody", "wrong");
        var cookie = response
            .Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("moviedb.device=", StringComparison.Ordinal));
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("samesite=lax", Case.Insensitive);

        var other = app.NewClient();
        other.BaseAddress = new Uri("https://localhost");
        other.DefaultRequestHeaders.Add("Cookie", "moviedb.device=forged");
        var blocked = await LoginAsync(other, "different", "wrong");

        blocked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Password_hash_concurrency_guard_rejects_before_reserving_database_state()
    {
        await using var app = await StartAsync();
        var client = app.NewClient();
        var token = await TokenAsync(client, "/login");
        var protection = app.Services.GetRequiredService<AuthProtection>();
        using var first = protection.TryEnter();
        using var second = protection.TryEnter();
        using var third = protection.TryEnter();
        using var fourth = protection.TryEnter();
        fourth.IsAcquired.ShouldBeTrue();

        var blocked = await LoginAsync(client, "nobody", "wrong", token: token);

        blocked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        await using var context = await app
            .Services.GetRequiredService<IDbContextFactory<MovieDB.Infrastructure.MovieDbContext>>()
            .CreateDbContextAsync(TestContext.Current.CancellationToken);
        (await context.AuthThrottleStates.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task Logout_is_post_only_with_antiforgery_and_ends_the_session()
    {
        await using var app = await StartAsync();
        var client = app.NewClient();
        await LoginAsync(client, "admin", "demodemo");
        var token = await TokenAsync(client, "/films");

        (await client.GetAsync("/logout", TestContext.Current.CancellationToken)).StatusCode.ShouldNotBe(
            HttpStatusCode.OK
        );
        (
            await client.PostAsync("/logout", new FormUrlEncodedContent([]), TestContext.Current.CancellationToken)
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await client.GetAsync("/films", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await client.PostAsync(
            "/logout",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }),
            TestContext.Current.CancellationToken
        );

        LocationPathOf(response).ShouldBe("/login");
        (await client.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(
            HttpStatusCode.Redirect
        );
    }

    [Fact]
    public async Task Change_password_requires_login_and_has_dutch_error_messages()
    {
        await using var app = await StartAsync();
        var client = app.NewClient();

        var anonymous = await client.GetAsync("/account/wachtwoord", TestContext.Current.CancellationToken);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        LocationPathOf(anonymous).ShouldStartWith("/login?returnUrl=");

        await LoginAsync(client, "admin", "demodemo");
        var token = await TokenAsync(client, "/account/wachtwoord");

        var wrong = await PostChangeAsync(
            client,
            token,
            "not the password",
            "brand new password",
            "brand new password"
        );
        wrong.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        (await client.GetStringAsync(LocationPathOf(wrong), TestContext.Current.CancellationToken)).ShouldContain(
            "Het huidige wachtwoord is onjuist."
        );

        var weak = await PostChangeAsync(client, token, "demodemo", "short", "short");
        (await client.GetStringAsync(LocationPathOf(weak), TestContext.Current.CancellationToken)).ShouldContain(
            "Controleer de eisen voor het wachtwoord"
        );

        var mismatch = await PostChangeAsync(client, token, "demodemo", "brand new password", "different one");
        LocationPathOf(mismatch).ShouldContain("error=invalid");

        var same = await PostChangeAsync(client, token, "demodemo", "demodemo", "demodemo");
        LocationPathOf(same).ShouldContain("error=invalid");
    }

    [Fact]
    public async Task Changing_the_password_keeps_the_session_and_replaces_the_credentials()
    {
        await using var app = await StartAsync();
        var client = app.NewClient();
        await LoginAsync(client, "admin", "demodemo");
        var token = await TokenAsync(client, "/account/wachtwoord");

        var ok = await PostChangeAsync(client, token, "demodemo", "brand new password", "brand new password");

        ok.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        LocationPathOf(ok).ShouldBe("/");
        (await client.GetAsync("/films", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);
        LocationPathOf(await LoginAsync(app.NewClient(), "admin", "demodemo")).ShouldBe("/login?error=1");
        LocationPathOf(await LoginAsync(app.NewClient(), "admin", "brand new password")).ShouldBe("/");
    }

    [Fact]
    public async Task Password_changes_use_the_same_account_failure_budget_as_login()
    {
        await using var app = await StartAsync();
        var client = app.NewClient();
        await LoginAsync(client, "admin", "demodemo");
        var token = await TokenAsync(client, "/account/wachtwoord");

        var blockedAt = 0;
        for (var attempt = 1; attempt <= 8 && blockedAt == 0; attempt++)
        {
            var response = await PostChangeAsync(client, token, "wrong", "new password value", "new password value");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                blockedAt = attempt;
            }
            else
            {
                response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
            }
        }

        blockedAt.ShouldBeInRange(2, 8);
        var blocked = await PostChangeAsync(client, token, "demodemo", "new password value", "new password value");
        blocked.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        var login = await LoginAsync(app.NewClient(), "admin", "demodemo");
        login.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task A_second_user_can_sign_in_independently()
    {
        await using var app = await StartAsync();
        await CreateUserAsync(app, "guest");

        LocationPathOf(await LoginAsync(app.NewClient(), "guest", Password)).ShouldBe("/");
    }

    [Fact]
    public async Task Antiforgery_cookie_is_secure_when_secure_cookies_are_enabled()
    {
        await using var app = await StartAsync(secureCookies: true);
        var client = app.NewClient();
        client.BaseAddress = new Uri("https://localhost");

        var response = await client.GetAsync("/login", TestContext.Current.CancellationToken);

        var cookie = response
            .Headers.GetValues("Set-Cookie")
            .Single(c => c.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal));
        cookie.ShouldContain("secure", Case.Insensitive);
        cookie.ShouldContain("httponly", Case.Insensitive);
    }

    [Fact]
    public async Task Unknown_pages_show_the_dutch_not_found_page()
    {
        await using var app = await StartAsync();
        var client = app.NewClient();
        await LoginAsync(client, "admin", "demodemo");

        var response = await client.GetAsync("/bestaat-niet", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain(
            "Pagina niet gevonden"
        );
    }

    [Fact]
    public async Task Production_data_protection_keys_are_encrypted_at_rest()
    {
        var connectionString = await _fixture.CreateDatabaseAsync();
        await using var app = new AuthApp(connectionString, 1000, true, "Production");
        var client = app.NewClient();
        client.BaseAddress = new Uri("https://mdb.dalion.eu");

        (await client.GetAsync("/login", TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.OK);

        (
            await MariaDbFixture.ScalarAsync<long>(
                connectionString,
                "SELECT COUNT(*) FROM data_protection_keys WHERE xml LIKE '%encryptedSecret%'"
            )
        ).ShouldBeGreaterThan(0);
        (
            await MariaDbFixture.ScalarAsync<long>(
                connectionString,
                "SELECT COUNT(*) FROM data_protection_keys WHERE xml NOT LIKE '%encryptedSecret%'"
            )
        ).ShouldBe(0);
    }

    [Fact]
    public async Task Production_requires_a_key_ring_certificate()
    {
        await using var app = new AuthApp(await _fixture.CreateDatabaseAsync(), 1000, true, "Production", false);

        var exception = Should.Throw<InvalidOperationException>(() => app.NewClient());

        exception.ToString().ShouldContain("DataProtection:CertificatePath must be configured");
    }

    [Fact]
    public async Task Data_protection_cookies_survive_another_application_instance()
    {
        var connectionString = await _fixture.CreateDatabaseAsync();
        await using var first = new AuthApp(connectionString, 1000, false);
        await using var second = new AuthApp(connectionString, 1000, false);
        var login = await LoginAsync(first.NewClient(), "admin", "demodemo");
        var authCookie = login
            .Headers.GetValues("Set-Cookie")
            .Single(cookie => cookie.StartsWith("moviedb.auth=", StringComparison.Ordinal))
            .Split(';', 2)[0];

        using var request = new HttpRequestMessage(HttpMethod.Get, "/films");
        request.Headers.Add("Cookie", authCookie);
        var response = await second.NewClient().SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private static HttpRequestMessage LoginRequest(string token, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/login")
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["Username"] = "nobody",
                    ["Password"] = "x",
                    ["ReturnUrl"] = "",
                    ["__RequestVerificationToken"] = token,
                }
            ),
        };
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        return request;
    }
}
