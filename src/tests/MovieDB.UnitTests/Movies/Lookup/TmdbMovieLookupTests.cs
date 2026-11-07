using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MovieDB.Common;
using MovieDB.Movies.Lookup;
using Shouldly;

namespace MovieDB.UnitTests.Movies.Lookup;

public sealed class TmdbMovieLookupTests
{
    private const string Token = "secret-test-token-123";

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((request, _) => Task.FromResult(respond(request))) { }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            return _respond(request, cancellationToken);
        }
    }

    private sealed class CapturingLogger : ILogger<TmdbMovieLookup>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception) + exception));
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (TmdbMovieLookup Lookup, StubHandler Handler, CapturingLogger Logger) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        string? token = Token
    )
    {
        var handler = new StubHandler(respond);
        return Create(handler, token);
    }

    private static (TmdbMovieLookup Lookup, StubHandler Handler, CapturingLogger Logger) Create(
        StubHandler handler,
        string? token = Token
    )
    {
        var options = new TmdbOptions { ApiReadAccessToken = token };
        var client = new HttpClient(handler);
        TmdbMovieLookup.Configure(client, options);
        var logger = new CapturingLogger();
        return (new TmdbMovieLookup(client, Options.Create(options), logger), handler, logger);
    }

    private const string SearchJson = """
        {"page":1,"results":[
          {"id":603,"title":"The Matrix","original_title":"The Matrix","release_date":"1999-03-30","overview":"A hacker learns the truth.","poster_path":"/f89JNg1sJyPr4aVnKYoXk1rQGb.jpg"},
          {"id":604,"title":"The Matrix Reloaded","original_title":"The Matrix Reloaded","release_date":"","overview":"","poster_path":null}
        ],"total_results":2}
        """;

    private const string DetailsJson = """
        {"id":603,"title":"The Matrix","original_title":"The Matrix","release_date":"1999-03-30","runtime":136,
         "imdb_id":"tt0133093",
         "genres":[{"id":28,"name":"Action"},{"id":878,"name":"Science Fiction"},{"id":10770,"name":"TV Movie"},{"id":28,"name":"Action"}],
         "credits":{"crew":[
           {"id":1,"name":" Lana Wachowski ","job":"Director"},
           {"id":2,"name":"Lilly Wachowski","job":"Director"},
           {"id":1,"name":"Lana Wachowski","job":"Director"},
           {"id":3,"name":"Joel Silver","job":"Producer"}
         ]}}
        """;

    [Fact]
    public async Task Search_sends_a_bearer_request_with_an_escaped_query_and_year()
    {
        var (lookup, handler, _) = Create(_ => Json(SearchJson));

        var result = await lookup.SearchAsync(new LookupQuery("Amélie & co?", 2001), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var request = handler.Requests.Single();
        request.Method.ShouldBe(HttpMethod.Get);
        request.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        request.Headers.Authorization.Parameter.ShouldBe(Token);
        var uri = request.RequestUri!.AbsoluteUri;
        uri.ShouldStartWith("https://api.themoviedb.org/3/search/movie?");
        uri.ShouldContain("query=Am%C3%A9lie%20%26%20co%3F");
        uri.ShouldContain("language=en-US");
        uri.ShouldContain("include_adult=false");
        uri.ShouldContain("page=1");
        uri.ShouldContain("year=2001");
        uri.ShouldNotContain("api_key");
        request.Headers.UserAgent.ToString().ShouldStartWith("MovieDB/");
        request.Headers.Accept.ToString().ShouldContain("application/json");
    }

    [Fact]
    public async Task Search_without_a_year_omits_the_year_parameter()
    {
        var (lookup, handler, _) = Create(_ => Json(SearchJson));

        await lookup.SearchAsync(new LookupQuery("Matrix"), CancellationToken.None);

        handler.Requests.Single().RequestUri!.Query.ShouldNotContain("year=");
    }

    [Fact]
    public async Task Search_maps_hits_and_builds_links_itself()
    {
        var (lookup, _, _) = Create(_ => Json(SearchJson));

        var result = await lookup.SearchAsync(new LookupQuery("matrix"), CancellationToken.None);

        var hits = result.Value;
        hits.Count.ShouldBe(2);
        hits[0].Id.ShouldBe(603);
        hits[0].Title.ShouldBe("The Matrix");
        hits[0].OriginalTitle.ShouldBe("The Matrix");
        hits[0].Year.ShouldBe(1999);
        hits[0].Overview.ShouldBe("A hacker learns the truth.");
        hits[0].SourceUrl.ToString().ShouldBe("https://www.themoviedb.org/movie/603");
        hits[1].Year.ShouldBeNull();
        hits[1].Overview.ShouldBeNull();
    }

    [Fact]
    public async Task Search_builds_the_poster_url_on_the_fixed_image_host()
    {
        var (lookup, _, _) = Create(_ => Json(SearchJson));

        var hits = (await lookup.SearchAsync(new LookupQuery("matrix"), CancellationToken.None)).Value;

        hits[0]
            .PosterUrl.ShouldNotBeNull()
            .AbsoluteUri.ShouldBe("https://image.tmdb.org/t/p/w92/f89JNg1sJyPr4aVnKYoXk1rQGb.jpg");
        hits[0].PosterUrl!.GetLeftPart(UriPartial.Authority).ShouldBe(TmdbMovieLookup.ImageOrigin);
        hits[1].PosterUrl.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("poster.jpg")]
    [InlineData("/")]
    [InlineData("/a/b.jpg")]
    [InlineData("/../secret.jpg")]
    [InlineData("/a.jpg?x=1")]
    [InlineData("//evil.example/a.jpg")]
    [InlineData("https://evil.example/a.jpg")]
    [InlineData("/a.svg")]
    [InlineData("/a b.jpg")]
    public async Task Search_ignores_a_poster_path_that_is_not_a_plain_image_file_name(string path)
    {
        var json = $$"""{"results":[{"id":1,"title":"T","original_title":"T","poster_path":"{{path}}"}]}""";
        var (lookup, _, _) = Create(_ => Json(json));

        var result = await lookup.SearchAsync(new LookupQuery("t"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldHaveSingleItem().PosterUrl.ShouldBeNull();
    }

    [Fact]
    public async Task Search_cuts_long_overviews_and_returns_at_most_ten_hits()
    {
        var items = string.Join(
            ",",
            Enumerable
                .Range(1, 15)
                .Select(i =>
                    $$"""{"id":{{i}},"title":"T{{i}}","original_title":"T{{i}}","release_date":"2000-01-01","overview":"{{new string('x', 500)}}"}"""
                )
        );
        var (lookup, _, _) = Create(_ => Json($$"""{"results":[{{items}}]}"""));

        var hits = (await lookup.SearchAsync(new LookupQuery("t"), CancellationToken.None)).Value;

        hits.Count.ShouldBe(10);
        hits[0].Overview!.Length.ShouldBeLessThanOrEqualTo(201);
    }

    [Fact]
    public async Task Search_without_results_is_a_success_with_an_empty_list()
    {
        var (lookup, _, _) = Create(_ => Json("""{"page":1,"results":[],"total_results":0}"""));

        var result = await lookup.SearchAsync(new LookupQuery("zzzz"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task Search_falls_back_to_the_original_title_when_the_title_is_empty()
    {
        var (lookup, _, _) = Create(_ => Json("""{"results":[{"id":5,"title":"","original_title":"Le Film"}]}"""));

        var hit = (await lookup.SearchAsync(new LookupQuery("film"), CancellationToken.None)).Value.Single();

        hit.Title.ShouldBe("Le Film");
    }

    [Fact]
    public async Task Search_with_an_item_without_a_valid_id_is_an_invalid_response()
    {
        var (lookup, _, _) = Create(_ => Json("""{"results":[{"title":"No id"}]}"""));

        var result = await lookup.SearchAsync(new LookupQuery("x1"), CancellationToken.None);

        result.Error.Kind.ShouldBe(ErrorKind.InvalidResponse);
    }

    [Fact]
    public async Task Details_requests_credits_and_maps_everything()
    {
        var (lookup, handler, _) = Create(_ => Json(DetailsJson));

        var result = await lookup.GetDetailsAsync(603, CancellationToken.None);

        var uri = handler.Requests.Single().RequestUri!.ToString();
        uri.ShouldStartWith("https://api.themoviedb.org/3/movie/603?");
        uri.ShouldContain("append_to_response=credits");
        uri.ShouldContain("language=en-US");

        var d = result.Value;
        d.Title.ShouldBe("The Matrix");
        d.Year.ShouldBe(1999);
        d.RuntimeMinutes.ShouldBe(136);
        d.Genres.ShouldBe(["Actie", "Sci-fi"]);
        d.Directors.ShouldBe(["Lana Wachowski", "Lilly Wachowski"]);
        d.SourceUrl.ToString().ShouldBe("https://www.themoviedb.org/movie/603");
        d.ImdbUrl!.ToString().ShouldBe("https://www.imdb.com/title/tt0133093/");
    }

    [Fact]
    public async Task Details_use_the_original_title_and_fall_back_to_the_title()
    {
        var (lookup, _, _) = Create(_ => Json("""{"id":1,"title":"Amelie","original_title":"Le Fabuleux Destin"}"""));
        (await lookup.GetDetailsAsync(1, CancellationToken.None)).Value.Title.ShouldBe("Le Fabuleux Destin");

        var (lookup2, _, _) = Create(_ => Json("""{"id":1,"title":"Amelie","original_title":""}"""));
        (await lookup2.GetDetailsAsync(1, CancellationToken.None)).Value.Title.ShouldBe("Amelie");
    }

    [Theory]
    [InlineData("tt0133093", true)]
    [InlineData("tt1234567890", true)]
    [InlineData("tt123456", false)]
    [InlineData("nm0133093", false)]
    [InlineData("tt0133093/../x", false)]
    [InlineData("", false)]
    public async Task Imdb_link_is_only_built_for_a_valid_imdb_id(string imdbId, bool expected)
    {
        var (lookup, _, _) = Create(_ => Json($$"""{"id":1,"title":"X","imdb_id":"{{imdbId}}"}"""));

        var d = (await lookup.GetDetailsAsync(1, CancellationToken.None)).Value;

        (d.ImdbUrl is not null).ShouldBe(expected);
    }

    [Fact]
    public async Task Missing_release_date_runtime_and_credits_are_tolerated()
    {
        var (lookup, _, _) = Create(_ => Json("""{"id":9,"title":"Bare","release_date":"","runtime":0}"""));

        var d = (await lookup.GetDetailsAsync(9, CancellationToken.None)).Value;

        d.Year.ShouldBeNull();
        d.RuntimeMinutes.ShouldBeNull();
        d.Genres.ShouldBeEmpty();
        d.Directors.ShouldBeEmpty();
        d.ImdbUrl.ShouldBeNull();
    }

    [Theory]
    [InlineData("2001")]
    [InlineData("2001-05")]
    [InlineData("2001-05-17")]
    public async Task Release_date_variants_give_the_year(string date)
    {
        var (lookup, _, _) = Create(_ => Json($$"""{"id":9,"title":"Bare","release_date":"{{date}}"}"""));

        (await lookup.GetDetailsAsync(9, CancellationToken.None)).Value.Year.ShouldBe(2001);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("20010517")]
    [InlineData("0000-00-00x")]
    public async Task Invalid_release_date_gives_no_year(string date)
    {
        var (lookup, _, _) = Create(_ => Json($$"""{"id":9,"title":"Bare","release_date":"{{date}}"}"""));

        (await lookup.GetDetailsAsync(9, CancellationToken.None)).Value.Year.ShouldBeNull();
    }

    [Fact]
    public async Task Details_with_many_directors_are_not_capped_by_the_client()
    {
        var crew = string.Join(
            ",",
            Enumerable.Range(1, 7).Select(i => $$"""{"id":{{i}},"name":"D{{i}}","job":"Director"}""")
        );
        var (lookup, _, _) = Create(_ => Json("""{"id":9,"title":"T","credits":{"crew":[""" + crew + "]}}"));

        (await lookup.GetDetailsAsync(9, CancellationToken.None)).Value.Directors.Count.ShouldBe(7);
    }

    [Fact]
    public async Task Details_without_a_valid_id_is_an_invalid_response()
    {
        var (lookup, _, _) = Create(_ => Json("""{"title":"No id"}"""));

        (await lookup.GetDetailsAsync(1, CancellationToken.None)).Error.Kind.ShouldBe(ErrorKind.InvalidResponse);
    }

    [Fact]
    public async Task Details_404_is_not_found()
    {
        var (lookup, _, _) = Create(_ => Json("""{"success":false}""", HttpStatusCode.NotFound));

        (await lookup.GetDetailsAsync(1, CancellationToken.None)).Error.Kind.ShouldBe(ErrorKind.NotFound);
    }

    [Fact]
    public async Task Search_404_is_unavailable_not_not_found()
    {
        var (lookup, _, _) = Create(_ => Json("{}", HttpStatusCode.NotFound));

        (await lookup.SearchAsync(new LookupQuery("xx"), CancellationToken.None)).Error.Kind.ShouldBe(
            ErrorKind.Unavailable
        );
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Rejected_credentials_are_misconfigured_and_logged_as_an_error(HttpStatusCode status)
    {
        var (lookup, _, logger) = Create(_ => Json("{}", status));

        var result = await lookup.SearchAsync(new LookupQuery("xx"), CancellationToken.None);

        result.Error.Kind.ShouldBe(ErrorKind.Misconfigured);
        logger.Entries.ShouldContain(e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Too_many_requests_is_rate_limited()
    {
        var (lookup, _, _) = Create(_ => Json("{}", HttpStatusCode.TooManyRequests));

        (await lookup.SearchAsync(new LookupQuery("xx"), CancellationToken.None)).Error.Kind.ShouldBe(
            ErrorKind.RateLimited
        );
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task Other_failure_statuses_are_unavailable(HttpStatusCode status)
    {
        var (lookup, _, _) = Create(_ => Json("{}", status));

        (await lookup.GetDetailsAsync(1, CancellationToken.None)).Error.Kind.ShouldBe(ErrorKind.Unavailable);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("[1,2]")]
    [InlineData("""{"results":"x"}""")]
    public async Task Bad_json_is_an_invalid_response(string body)
    {
        var (lookup, _, _) = Create(_ => Json(body));

        (await lookup.SearchAsync(new LookupQuery("xx"), CancellationToken.None)).Error.Kind.ShouldBe(
            ErrorKind.InvalidResponse
        );
    }

    [Fact]
    public async Task Network_failure_is_unavailable()
    {
        var (lookup, _, _) = Create(_ => throw new HttpRequestException("boom"));

        (await lookup.SearchAsync(new LookupQuery("xx"), CancellationToken.None)).Error.Kind.ShouldBe(
            ErrorKind.Unavailable
        );
    }

    [Fact]
    public async Task Timeout_is_unavailable()
    {
        var handler = new StubHandler((_, _) => throw new TaskCanceledException("timeout", new TimeoutException()));
        var (lookup, _, _) = Create(handler);

        (await lookup.SearchAsync(new LookupQuery("xx"), CancellationToken.None)).Error.Kind.ShouldBe(
            ErrorKind.Unavailable
        );
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        var handler = new StubHandler(
            async (_, ct) =>
            {
                await cts.CancelAsync();
                ct.ThrowIfCancellationRequested();
                return Json("{}");
            }
        );
        var (lookup, _, _) = Create(handler);

        await Should.ThrowAsync<OperationCanceledException>(() => lookup.SearchAsync(new LookupQuery("xx"), cts.Token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Without_a_token_nothing_is_sent_and_both_methods_report_not_configured(string? token)
    {
        var (lookup, handler, _) = Create(_ => Json("{}"), token);

        lookup.IsConfigured.ShouldBeFalse();
        (await lookup.SearchAsync(new LookupQuery("xx"), CancellationToken.None)).Error.Kind.ShouldBe(
            ErrorKind.NotConfigured
        );
        (await lookup.GetDetailsAsync(1, CancellationToken.None)).Error.Kind.ShouldBe(ErrorKind.NotConfigured);
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public void With_a_token_the_lookup_is_configured()
    {
        Create(_ => Json("{}")).Lookup.IsConfigured.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_ids_throw(int id)
    {
        var (lookup, _, _) = Create(_ => Json("{}"));

        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => lookup.GetDetailsAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task Logs_never_contain_the_token_the_typed_text_or_film_data()
    {
        var statuses = new[]
        {
            HttpStatusCode.Unauthorized,
            HttpStatusCode.TooManyRequests,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.OK,
        };
        var logger = new CapturingLogger();
        foreach (var status in statuses)
        {
            var handler = new StubHandler(_ =>
                Json("""{"results":[{"title":"SECRETFILM"}],"x":"SECRETBODY"}""", status)
            );
            var options = new TmdbOptions { ApiReadAccessToken = Token };
            var client = new HttpClient(handler);
            TmdbMovieLookup.Configure(client, options);
            var lookup = new TmdbMovieLookup(client, Options.Create(options), logger);
            await lookup.SearchAsync(new LookupQuery("TYPEDTEXT"), CancellationToken.None);
        }

        logger.Entries.ShouldNotBeEmpty();
        foreach (var (_, message) in logger.Entries)
        {
            message.ShouldNotContain(Token);
            message.ShouldNotContain("TYPEDTEXT");
            message.ShouldNotContain("SECRETFILM");
            message.ShouldNotContain("SECRETBODY");
        }
    }
}
