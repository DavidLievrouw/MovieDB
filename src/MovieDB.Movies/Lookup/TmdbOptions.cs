using Microsoft.Extensions.Options;

namespace MovieDB.Movies.Lookup;

/// <summary>Settings of the optional TMDB lookup (section <c>Tmdb</c>).</summary>
internal sealed class TmdbOptions
{
    /// <summary>The configuration section name.</summary>
    public const string Section = "Tmdb";

    /// <summary>The shortest accepted request timeout in seconds.</summary>
    public const int MinTimeoutSeconds = 1;

    /// <summary>The longest accepted request timeout in seconds.</summary>
    public const int MaxTimeoutSeconds = 30;

    /// <summary>The TMDB "API Read Access Token". Empty or missing means the online search is not set up.</summary>
    public string? ApiReadAccessToken { get; set; }

    /// <summary>The language of the requests.</summary>
    public string Language { get; set; } = "en-US";

    /// <summary>The API base address: absolute, https and ending in a slash.</summary>
    public string BaseUrl { get; set; } = "https://api.themoviedb.org/3/";

    /// <summary>The request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Gets the token without surrounding whitespace, or <c>null</c> when none is configured.</summary>
    public string? Token => string.IsNullOrWhiteSpace(ApiReadAccessToken) ? null : ApiReadAccessToken.Trim();
}

/// <summary>Validates <see cref="TmdbOptions"/>; only what is set is checked, an empty token is valid.</summary>
internal sealed class TmdbOptionsValidator : IValidateOptions<TmdbOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TmdbOptions options)
    {
        var failures = new List<string>();
        if (options.Token is { } token && token.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
        {
            failures.Add("Tmdb:ApiReadAccessToken must not contain whitespace or control characters.");
        }

        if (string.IsNullOrWhiteSpace(options.Language))
        {
            failures.Add("Tmdb:Language must not be empty.");
        }

        if (
            !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl)
            || baseUrl.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(baseUrl.UserInfo)
            || !string.IsNullOrEmpty(baseUrl.Query)
            || !string.IsNullOrEmpty(baseUrl.Fragment)
            || !baseUrl.AbsolutePath.EndsWith('/')
        )
        {
            failures.Add(
                "Tmdb:BaseUrl must be an absolute HTTPS URL without credentials, query or fragment, ending in '/'."
            );
        }

        if (options.TimeoutSeconds is < TmdbOptions.MinTimeoutSeconds or > TmdbOptions.MaxTimeoutSeconds)
        {
            failures.Add(
                $"Tmdb:TimeoutSeconds must be between {TmdbOptions.MinTimeoutSeconds} and {TmdbOptions.MaxTimeoutSeconds}."
            );
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
