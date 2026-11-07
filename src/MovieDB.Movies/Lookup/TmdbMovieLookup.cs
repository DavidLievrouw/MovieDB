using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MovieDB.Common;

namespace MovieDB.Movies.Lookup;

/// <summary>
/// The TMDB implementation of <see cref="IMovieLookup"/>. Typed <see cref="HttpClient"/> with a short timeout and no
/// retries. Links are built here, never taken from the response. Never logs the token, headers, response bodies, film
/// data or the typed text.
/// </summary>
internal sealed partial class TmdbMovieLookup : IMovieLookup
{
    private const int MaxHits = 10;
    private const int MaxOverviewLength = 200;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http;
    private readonly TmdbOptions _options;
    private readonly ILogger<TmdbMovieLookup> _logger;

    /// <summary>Creates the lookup.</summary>
    /// <param name="http">The typed client; configured by <see cref="Configure"/>.</param>
    /// <param name="options">The TMDB settings.</param>
    /// <param name="logger">The logger.</param>
    public TmdbMovieLookup(HttpClient http, IOptions<TmdbOptions> options, ILogger<TmdbMovieLookup> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsConfigured => _options.Token is not null;

    [GeneratedRegex(@"^(?<year>\d{4})(-\d{2}(-\d{2})?)?$", RegexOptions.CultureInvariant)]
    private static partial Regex ReleaseDatePattern();

    [GeneratedRegex(@"^tt\d{7,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex ImdbIdPattern();

    /// <summary>Sets the base address, headers and timeout of the typed client.</summary>
    /// <param name="client">The client to configure.</param>
    /// <param name="options">The TMDB settings.</param>
    public static void Configure(HttpClient client, TmdbOptions options)
    {
        client.BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute);
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MovieDB", GetVersion()));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<LookupHit>>> SearchAsync(
        LookupQuery query,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(query);
        if (_options.Token is null)
        {
            return Error.NotConfigured("TMDB token is not configured.");
        }

        var url =
            $"search/movie?query={Uri.EscapeDataString(query.Title)}&language={Uri.EscapeDataString(_options.Language)}&include_adult=false&page=1";
        if (query.Year is { } year)
        {
            url += $"&year={year}";
        }

        var response = await GetAsync<SearchResponse>(url, "search", cancellationToken, notFoundIsNotFound: false);
        if (!response.IsSuccess)
        {
            return response.Error;
        }

        var results = response.Value.Results ?? [];
        var hits = new List<LookupHit>();
        foreach (var item in results.Take(MaxHits))
        {
            if (item is null || item.Id is not > 0)
            {
                _logger.LogError("TMDB search returned a result without a valid id.");
                return Error.InvalidResponse("A search result has no valid id.");
            }

            var title = item.Title?.Trim();
            var original = string.IsNullOrWhiteSpace(item.OriginalTitle) ? null : item.OriginalTitle.Trim();
            if (string.IsNullOrEmpty(title))
            {
                title = original;
            }

            if (string.IsNullOrEmpty(title))
            {
                _logger.LogError("TMDB search returned a result without a title.");
                return Error.InvalidResponse("A search result has no title.");
            }

            hits.Add(
                new LookupHit(
                    item.Id.Value,
                    title,
                    original,
                    ParseYear(item.ReleaseDate),
                    CutOverview(item.Overview),
                    SourceUrl(item.Id.Value)
                )
            );
        }

        return Result<IReadOnlyList<LookupHit>>.Ok(hits);
    }

    /// <inheritdoc />
    public async Task<Result<LookupDetails>> GetDetailsAsync(int id, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        if (_options.Token is null)
        {
            return Error.NotConfigured("TMDB token is not configured.");
        }

        var url = $"movie/{id}?language={Uri.EscapeDataString(_options.Language)}&append_to_response=credits";
        var response = await GetAsync<DetailsResponse>(url, "details", cancellationToken, notFoundIsNotFound: true);
        if (!response.IsSuccess)
        {
            return response.Error;
        }

        var d = response.Value;
        if (d.Id is not > 0)
        {
            _logger.LogError("TMDB details response has no valid id.");
            return Error.InvalidResponse("The details have no valid id.");
        }

        var title = (string.IsNullOrWhiteSpace(d.OriginalTitle) ? d.Title : d.OriginalTitle)?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            _logger.LogError("TMDB details response has no title.");
            return Error.InvalidResponse("The details have no title.");
        }

        return Result<LookupDetails>.Ok(
            new LookupDetails(
                title,
                ParseYear(d.ReleaseDate),
                d.Runtime is > 0 ? d.Runtime : null,
                MapGenres(d.Genres),
                MapDirectors(d.Credits?.Crew),
                SourceUrl(d.Id.Value),
                ImdbUrl(d.ImdbId)
            )
        );
    }

    private static List<string> MapGenres(List<GenreItem?>? items)
    {
        var genres = new List<string>();
        foreach (var item in items ?? [])
        {
            if (item?.Id is { } genreId && TmdbGenreMap.TryGetName(genreId, out var name) && !genres.Contains(name))
            {
                genres.Add(name);
            }
        }

        return genres;
    }

    private static List<string> MapDirectors(List<CrewItem?>? crew)
    {
        var directors = new List<string>();
        var seenPeople = new HashSet<int>();
        foreach (var member in crew ?? [])
        {
            if (member is null || !string.Equals(member.Job, "Director", StringComparison.Ordinal))
            {
                continue;
            }

            var name = member.Name?.Trim();
            var isDuplicate = member.Id is { } personId && !seenPeople.Add(personId);
            if (!string.IsNullOrEmpty(name) && !isDuplicate)
            {
                directors.Add(name);
            }
        }

        return directors;
    }

    private static Uri? ImdbUrl(string? imdbId) =>
        imdbId is not null && ImdbIdPattern().IsMatch(imdbId) ? new Uri($"https://www.imdb.com/title/{imdbId}/") : null;

    private async Task<Result<T>> GetAsync<T>(
        string url,
        string operation,
        CancellationToken cancellationToken,
        bool notFoundIsNotFound
    )
        where T : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.Token);
        try
        {
            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            switch (response.StatusCode)
            {
                case HttpStatusCode.OK:
                    break;
                case HttpStatusCode.NotFound when notFoundIsNotFound:
                    return Error.NotFound("TMDB does not know this movie.");
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    _logger.LogError(
                        "TMDB {Operation} rejected the credentials (HTTP {Status}).",
                        operation,
                        (int)response.StatusCode
                    );
                    return Error.Misconfigured("TMDB rejected the credentials.");
                case HttpStatusCode.TooManyRequests:
                    _logger.LogWarning("TMDB {Operation} was rate limited.", operation);
                    return Error.RateLimited("TMDB rate limit reached.");
                default:
                    _logger.LogWarning(
                        "TMDB {Operation} failed with HTTP {Status}.",
                        operation,
                        (int)response.StatusCode
                    );
                    return Error.Unavailable("TMDB answered with an error status.");
            }

            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var body = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
                if (body is null)
                {
                    _logger.LogError("TMDB {Operation} returned an empty body.", operation);
                    return Error.InvalidResponse("TMDB returned an empty body.");
                }

                return Result<T>.Ok(body);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "TMDB {Operation} returned data that is not valid JSON.", operation);
                return Error.InvalidResponse("TMDB returned invalid JSON.");
            }
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "TMDB {Operation} timed out.", operation);
            return Error.Unavailable("TMDB timed out.");
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "TMDB {Operation} failed: {ExceptionType}.", operation, ex.GetType().Name);
            return Error.Unavailable("TMDB could not be reached.");
        }
    }

    private static int? ParseYear(string? releaseDate)
    {
        if (releaseDate is null)
        {
            return null;
        }

        var match = ReleaseDatePattern().Match(releaseDate);
        return match.Success
            ? int.Parse(match.Groups["year"].Value, System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    private static string? CutOverview(string? overview)
    {
        var text = overview?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return text.Length <= MaxOverviewLength ? text : text[..MaxOverviewLength].TrimEnd() + "…";
    }

    private static Uri SourceUrl(int id) => new($"https://www.themoviedb.org/movie/{id}");

    private static string GetVersion()
    {
        var version = typeof(TmdbMovieLookup)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var plus = version?.IndexOf('+') ?? -1;
        if (plus >= 0)
        {
            version = version![..plus];
        }

        return string.IsNullOrWhiteSpace(version) ? "0.0.0" : version;
    }

    private sealed class SearchResponse
    {
        [JsonPropertyName("results")]
        public List<SearchItem?>? Results { get; set; }
    }

    private sealed class SearchItem
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("original_title")]
        public string? OriginalTitle { get; set; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("overview")]
        public string? Overview { get; set; }
    }

    private sealed class DetailsResponse
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("original_title")]
        public string? OriginalTitle { get; set; }

        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }

        [JsonPropertyName("runtime")]
        public int? Runtime { get; set; }

        [JsonPropertyName("imdb_id")]
        public string? ImdbId { get; set; }

        [JsonPropertyName("genres")]
        public List<GenreItem?>? Genres { get; set; }

        [JsonPropertyName("credits")]
        public CreditsItem? Credits { get; set; }
    }

    private sealed class GenreItem
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }
    }

    private sealed class CreditsItem
    {
        [JsonPropertyName("crew")]
        public List<CrewItem?>? Crew { get; set; }
    }

    private sealed class CrewItem
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("job")]
        public string? Job { get; set; }
    }
}
