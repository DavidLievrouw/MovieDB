using System.Text;
using Microsoft.EntityFrameworkCore;
using MovieDB.Common;
using MovieDB.Infrastructure;
using MovieDB.Infrastructure.Entities;

namespace MovieDB.Movies;

/// <summary>One row of the overview.</summary>
/// <param name="Id">The movie id.</param>
/// <param name="Title">The title.</param>
/// <param name="ReleaseYear">The release year.</param>
/// <param name="Directors">The director names, in the order they were entered.</param>
/// <param name="Genres">The genre names, alphabetically.</param>
/// <param name="RuntimeMinutes">The runtime in minutes.</param>
/// <param name="WatchedOn">The viewing date.</param>
/// <param name="Medium">The medium name, if any.</param>
/// <param name="OverallScore">The overall score, if any.</param>
internal sealed record MovieOverviewRow(
    MovieId Id,
    string Title,
    int ReleaseYear,
    IReadOnlyList<string> Directors,
    IReadOnlyList<string> Genres,
    int RuntimeMinutes,
    DateOnly WatchedOn,
    string? Medium,
    int? OverallScore
)
{
    /// <summary>The directors as shown in one cell: in the entered order, separated by a comma.</summary>
    public string DirectorText => string.Join(", ", Directors);

    /// <summary>The genres as shown in one cell: alphabetical, separated by a comma.</summary>
    public string GenreText => string.Join(", ", Genres);
}

/// <summary>One page of the overview.</summary>
/// <param name="Rows">The rows of the page.</param>
/// <param name="Page">The effective 1-based page number (clamped to the last page).</param>
/// <param name="PageCount">The number of pages (at least 1).</param>
/// <param name="TotalMatching">The number of movies matching the search and the genre filter.</param>
/// <param name="TotalMovies">The number of movies without any search or filter.</param>
internal sealed record MovieOverviewPage(
    IReadOnlyList<MovieOverviewRow> Rows,
    int Page,
    int PageCount,
    int TotalMatching,
    int TotalMovies
);

/// <summary>An existing movie that has the same title and release year as the movie being saved.</summary>
/// <param name="Id">The movie id.</param>
/// <param name="Directors">The directors of the existing movie, as one text.</param>
internal sealed record SameTitleMovie(MovieId Id, string Directors);

/// <summary>A movie as shown on the detail page.</summary>
/// <param name="Id">The movie id.</param>
/// <param name="Title">The title.</param>
/// <param name="ReleaseYear">The release year.</param>
/// <param name="Directors">The director names, in the order they were entered.</param>
/// <param name="Genres">The genre names, alphabetically.</param>
/// <param name="RuntimeMinutes">The runtime in minutes.</param>
/// <param name="WatchedOn">The viewing date.</param>
/// <param name="Medium">The medium name, if any.</param>
/// <param name="OverallScore">The overall score.</param>
/// <param name="StoryScore">The story score.</param>
/// <param name="PictureScore">The picture score.</param>
/// <param name="MusicScore">The music score.</param>
/// <param name="SoundScore">The sound score.</param>
/// <param name="Review">The written review.</param>
internal sealed record MovieDetail(
    MovieId Id,
    string Title,
    int ReleaseYear,
    IReadOnlyList<string> Directors,
    IReadOnlyList<string> Genres,
    int RuntimeMinutes,
    DateOnly WatchedOn,
    string? Medium,
    int? OverallScore,
    int? StoryScore,
    int? PictureScore,
    int? MusicScore,
    int? SoundScore,
    string? Review
);

/// <summary>A movie loaded for editing.</summary>
/// <param name="Id">The movie id.</param>
/// <param name="Version">The concurrency version.</param>
/// <param name="CreatedAt">When the movie was created (UTC).</param>
/// <param name="UpdatedAt">When the movie was last changed (UTC).</param>
/// <param name="Input">The editable values.</param>
internal sealed record MovieEdit(MovieId Id, int Version, DateTime CreatedAt, DateTime UpdatedAt, MovieInput Input);

/// <summary>Where a movie is in the ordered list of movies.</summary>
/// <param name="Position">The 1-based position.</param>
/// <param name="Total">The number of movies in the list.</param>
/// <param name="PreviousId">The id of the previous movie, if any.</param>
/// <param name="NextId">The id of the next movie, if any.</param>
/// <param name="FirstId">The id of the first movie.</param>
/// <param name="LastId">The id of the last movie.</param>
/// <param name="State">The overview state the navigation applies to (the search and genre filter are dropped when they exclude the movie).</param>
internal sealed record MovieNavigation(
    int Position,
    int Total,
    MovieId? PreviousId,
    MovieId? NextId,
    MovieId FirstId,
    MovieId LastId,
    OverviewState State
);

/// <summary>A genre from the fixed genre list.</summary>
/// <param name="Id">The genre id, as used in the URL.</param>
/// <param name="Name">The genre name.</param>
internal sealed record GenreOption(int Id, string Name);

/// <summary>Raw result row of the overview count query.</summary>
internal sealed class OverviewCounts
{
    /// <summary>The number of movies without any search or filter.</summary>
    public int Total { get; set; }

    /// <summary>The number of movies matching the search and the genre filter.</summary>
    public int Matching { get; set; }
}

/// <summary>Raw result row of the overview page query. The director and genre names are separated by U+001F.</summary>
internal sealed class OverviewPageRow
{
    /// <summary>The movie id.</summary>
    public int Id { get; set; }

    /// <summary>The title.</summary>
    public string Title { get; set; } = "";

    /// <summary>The release year.</summary>
    public int ReleaseYear { get; set; }

    /// <summary>The director names, in the entered order.</summary>
    public string Directors { get; set; } = "";

    /// <summary>The genre names, alphabetically.</summary>
    public string Genres { get; set; } = "";

    /// <summary>The runtime in minutes.</summary>
    public int RuntimeMinutes { get; set; }

    /// <summary>The viewing date.</summary>
    public DateOnly WatchedOn { get; set; }

    /// <summary>The medium name, if any.</summary>
    public string? Medium { get; set; }

    /// <summary>The overall score, if any.</summary>
    public int? OverallScore { get; set; }
}

/// <summary>Raw result row of the navigation query.</summary>
internal sealed class NavigationRow
{
    /// <summary>The 1-based position.</summary>
    public long Position { get; set; }

    /// <summary>The number of rows in the list.</summary>
    public long Total { get; set; }

    /// <summary>The id of the previous row.</summary>
    public int? PreviousId { get; set; }

    /// <summary>The id of the next row.</summary>
    public int? NextId { get; set; }

    /// <summary>The id of the first row.</summary>
    public int FirstId { get; set; }

    /// <summary>The id of the last row.</summary>
    public int LastId { get; set; }
}

/// <summary>Read queries of the movies feature. Every call uses a short-lived context and no-tracking projections.</summary>
internal sealed class MovieQueries
{
    /// <summary>The number of genres and directors in the statistics.</summary>
    public const int TopCount = 5;

    /// <summary>The largest number of media in the statistics.</summary>
    public const int MaxMedia = 8;

    private const char NameSeparator = '\u001f';

    private readonly IDbContextFactory<MovieDbContext> _factory;

    /// <summary>Creates the queries.</summary>
    /// <param name="factory">The context factory.</param>
    public MovieQueries(IDbContextFactory<MovieDbContext> factory)
    {
        _factory = factory;
    }

    /// <summary>Escapes <c>\</c>, <c>%</c> and <c>_</c> so the text matches literally inside a <c>LIKE</c> pattern.</summary>
    /// <param name="text">The text to match.</param>
    /// <returns>A <c>LIKE</c> pattern matching rows that contain the text.</returns>
    public static string ContainsPattern(string text) =>
        "%" + text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    /// <summary>Loads one page of the overview, using two queries: the counts, and the page with its rows.</summary>
    /// <param name="state">The view state.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page; the page number is clamped to the last page.</returns>
    public async Task<MovieOverviewPage> ListOverviewAsync(OverviewState state, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var (where, filterParameters) = FilterSql(state);

        // The filter text is composed only from the fixed fragments of FilterSql; every value is a parameter.
        var countSql =
            where.Length == 0
                ? "SELECT COUNT(*) AS Total, COUNT(*) AS Matching FROM movies"
                : "SELECT (SELECT COUNT(*) FROM movies) AS Total, "
                    + $"(SELECT COUNT(*) FROM movies m WHERE 1 = 1{where}) AS Matching";
        var counts = await db
            .Database.SqlQueryRaw<OverviewCounts>(countSql, filterParameters)
            .SingleAsync(cancellationToken);

        var pageCount = Math.Max(1, (int)Math.Ceiling(counts.Matching / (double)state.PageSize));
        var page = Math.Clamp(state.Page, 1, pageCount);
        if (counts.Matching == 0)
        {
            return new MovieOverviewPage([], page, pageCount, counts.Matching, counts.Total);
        }

        // The inner query filters, sorts and cuts the page (OrderSql is the one place that defines the ordering), so the
        // per-row director and genre names are only concatenated for the rows of the page. Those names are separated
        // by a control character that cannot be part of a name, and split again below.
        var order = OrderSql(state);
        var offsetIndex = filterParameters.Length;
        object[] pageParameters = [.. filterParameters, state.PageSize, (page - 1) * state.PageSize];
        var pageSql =
            "SELECT m.id AS Id, m.title AS Title, m.release_year AS ReleaseYear, "
            + "COALESCE((SELECT GROUP_CONCAT(d.name ORDER BY mdl.position SEPARATOR '\u001f') FROM movie_directors mdl "
            + "JOIN directors d ON d.id = mdl.director_id WHERE mdl.movie_id = m.id), '') AS Directors, "
            + "COALESCE((SELECT GROUP_CONCAT(g.name ORDER BY g.name SEPARATOR '\u001f') FROM movie_genres mgl "
            + "JOIN genres g ON g.id = mgl.genre_id WHERE mgl.movie_id = m.id), '') AS Genres, "
            + "m.runtime_minutes AS RuntimeMinutes, m.watched_on AS WatchedOn, md.name AS Medium, "
            + "m.overall_score AS OverallScore FROM ("
            + "SELECT m.id, m.title, m.release_year, m.runtime_minutes, m.watched_on, m.medium_id, m.overall_score "
            + "FROM movies m LEFT JOIN media md ON md.id = m.medium_id "
            + $"WHERE 1 = 1{where} ORDER BY {order} LIMIT {{{offsetIndex}}} OFFSET {{{offsetIndex + 1}}}"
            + ") m LEFT JOIN media md ON md.id = m.medium_id "
            + $"ORDER BY {order}";
        var rows = await db
            .Database.SqlQueryRaw<OverviewPageRow>(pageSql, pageParameters)
            .ToListAsync(cancellationToken);

        return new MovieOverviewPage([.. rows.Select(ToOverviewRow)], page, pageCount, counts.Matching, counts.Total);
    }

    private static MovieOverviewRow ToOverviewRow(OverviewPageRow row) =>
        new(
            MovieId.From(row.Id),
            row.Title,
            row.ReleaseYear,
            SplitNames(row.Directors),
            SplitNames(row.Genres),
            row.RuntimeMinutes,
            row.WatchedOn,
            row.Medium,
            row.OverallScore
        );

    private static string[] SplitNames(string names) => names.Length == 0 ? [] : names.Split(NameSeparator);

    /// <summary>
    /// The extra <c>WHERE</c> conditions for the search and the genre filter (each starting with <c>AND</c>), and their
    /// parameters (numbered from <c>{0}</c>). A movie must have all selected genres.
    /// </summary>
    /// <param name="state">The overview state.</param>
    /// <returns>The SQL fragment (empty when there is no filter) and its parameters.</returns>
    private static (string Where, object[] Parameters) FilterSql(OverviewState state)
    {
        var where = new StringBuilder();
        var parameters = new List<object>();
        if (state.Search.Length > 0)
        {
            where.Append(" AND m.title LIKE {0}");
            parameters.Add(ContainsPattern(state.Search));
        }

        if (state.Genres.Count > 0)
        {
            var placeholders = string.Join(", ", state.Genres.Select((_, i) => $"{{{parameters.Count + i}}}"));
            where.Append(
                " AND (SELECT COUNT(DISTINCT mg.genre_id) FROM movie_genres mg WHERE mg.movie_id = m.id "
                    + $"AND mg.genre_id IN ({placeholders})) = {state.Genres.Count}"
            );
            parameters.AddRange(state.Genres.Cast<object>());
        }

        return (where.ToString(), [.. parameters]);
    }

    /// <summary>Loads the statistics over all movies, using server-side aggregation only.</summary>
    /// <param name="today">The owner's current date, which decides what "this month" and "this year" are.</param>
    /// <param name="cancellationToken">Cancels the queries.</param>
    /// <returns>The statistics; counts are zero and lists are empty when there are no movies.</returns>
    public async Task<MovieStatistics> GetStatisticsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var yearStart = new DateOnly(today.Year, 1, 1);
        var lastYearStart = yearStart.AddYears(-1);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        IQueryable<Movie> all = db.Movies.AsNoTracking();

        // A movie counts once for each of its genres.
        var genres = await TopAsync(
            db.MovieGenreLinks.AsNoTracking().GroupBy(l => l.Genre.Name),
            TopCount,
            cancellationToken
        );
        // A movie counts once for each of its directors.
        var directors = await TopAsync(
            db.MovieDirectorLinks.AsNoTracking().GroupBy(l => l.Director.Name),
            TopCount,
            cancellationToken
        );
        var media = await TopAsync(
            all.Where(m => m.Medium != null).GroupBy(m => m.Medium!.Name),
            MaxMedia,
            cancellationToken
        );

        var totals = await all.GroupBy(_ => 1)
            .Select(g => new
            {
                Runtime = g.Sum(m => (long)m.RuntimeMinutes),
                Average = g.Average(m => (double?)m.OverallScore),
                HighlyRated = g.Count(m => m.OverallScore >= 9),
                ThisMonth = g.Count(m => m.WatchedOn >= monthStart),
                ThisYear = g.Count(m => m.WatchedOn >= yearStart),
                LastYear = g.Count(m => m.WatchedOn >= lastYearStart && m.WatchedOn < yearStart),
            })
            .SingleOrDefaultAsync(cancellationToken);

        return new MovieStatistics(
            media,
            genres,
            directors,
            totals?.ThisMonth ?? 0,
            totals?.ThisYear ?? 0,
            totals?.LastYear ?? 0,
            (int)(totals?.Runtime ?? 0),
            totals?.Average,
            totals?.HighlyRated ?? 0
        );
    }

    private static async Task<IReadOnlyList<NamedCount>> TopAsync<TElement>(
        IQueryable<IGrouping<string, TElement>> groups,
        int take,
        CancellationToken cancellationToken
    ) =>
        await groups
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(take)
            .Select(g => new NamedCount(g.Key, g.Count()))
            .ToListAsync(cancellationToken);

    /// <summary>Loads a movie with everything the edit page needs.</summary>
    /// <param name="id">The movie id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The movie, or <c>null</c> when there is none with that id.</returns>
    public async Task<MovieEdit?> FindForEditAsync(MovieId id, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await FindForEditCompiled(db, id, cancellationToken);
    }

    private static readonly Func<MovieDbContext, MovieId, CancellationToken, Task<MovieEdit?>> FindForEditCompiled =
        EF.CompileAsyncQuery(
            (MovieDbContext db, MovieId id, CancellationToken _) =>
                db
                    .Movies.AsNoTracking()
                    .Where(m => m.Id == id)
                    .Select(m => new MovieEdit(
                        m.Id,
                        m.Version,
                        m.CreatedAt,
                        m.UpdatedAt,
                        new MovieInput
                        {
                            Title = m.Title,
                            Genres = m.GenreLinks.Select(l => l.Genre.Name).Order().ToList(),
                            RuntimeMinutes = m.RuntimeMinutes,
                            ReleaseYear = m.ReleaseYear,
                            Directors = m.DirectorLinks.OrderBy(l => l.Position).Select(l => l.Director.Name).ToList(),
                            WatchedOn = m.WatchedOn,
                            Medium = m.Medium == null ? null : m.Medium.Name,
                            Review = m.Review,
                            OverallScore = m.OverallScore,
                            StoryScore = m.StoryScore,
                            PictureScore = m.PictureScore,
                            MusicScore = m.MusicScore,
                            SoundScore = m.SoundScore,
                        }
                    ))
                    .SingleOrDefault()
        );

    /// <summary>
    /// Finds another movie with the same title, year and set of directors (compared by the column collation, in any
    /// order).
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="year">The release year.</param>
    /// <param name="directors">The director names.</param>
    /// <param name="excludeId">The movie being edited, which is not a duplicate of itself; <c>null</c> for a new movie.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The id of the existing movie, or <c>null</c> when there is none.</returns>
    public async Task<MovieId?> FindDuplicateAsync(
        string title,
        int year,
        IReadOnlyList<string> directors,
        MovieId? excludeId,
        CancellationToken cancellationToken
    )
    {
        title = title.Trim();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var key = await FindDirectorsKeyAsync(db, directors, cancellationToken);
        if (key is null)
        {
            // A director that does not exist yet cannot be shared with another movie.
            return null;
        }

        var matches = db
            .Movies.AsNoTracking()
            .Where(m => m.Title == title && m.ReleaseYear == year && m.DirectorsKey == key);
        if (excludeId is { } own)
        {
            matches = matches.Where(m => m.Id != own);
        }

        return await matches.OrderBy(m => m.Id).Select(m => (MovieId?)m.Id).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Resolves director names (by the column collation) to their key, when all of them exist.</summary>
    /// <param name="db">The context to query.</param>
    /// <param name="names">The director names.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The directors key, or <see langword="null"/> when a name is unknown or none is given.</returns>
    private static async Task<string?> FindDirectorsKeyAsync(
        MovieDbContext db,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken
    )
    {
        var ids = new List<DirectorId>(names.Count);
        foreach (var raw in names)
        {
            var name = raw.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            var found = await db
                .Directors.AsNoTracking()
                .Where(d => d.Name == name)
                .Select(d => (DirectorId?)d.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (found is not { } id)
            {
                return null;
            }

            if (!ids.Contains(id))
            {
                ids.Add(id);
            }
        }

        return ids.Count == 0 ? null : DirectorsKey.Of(ids);
    }

    /// <summary>
    /// Finds other movies with the same title and release year but another set of directors (compared by the column
    /// collation). Movies with the same directors are left out: those are duplicates, which saving rejects.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="year">The release year.</param>
    /// <param name="directors">The director names of the movie that is being saved.</param>
    /// <param name="excludeId">The movie being edited; <c>null</c> for a new movie.</param>
    /// <param name="take">The largest number of movies to return.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The movies, oldest first.</returns>
    public async Task<IReadOnlyList<SameTitleMovie>> FindSameTitleAndYearAsync(
        string title,
        int year,
        IReadOnlyList<string> directors,
        MovieId? excludeId,
        int take,
        CancellationToken cancellationToken
    )
    {
        title = title.Trim();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var key = await FindDirectorsKeyAsync(db, directors, cancellationToken);
        var matches = db.Movies.AsNoTracking().Where(m => m.Title == title && m.ReleaseYear == year);
        if (key is not null)
        {
            matches = matches.Where(m => m.DirectorsKey != key);
        }

        if (excludeId is { } own)
        {
            matches = matches.Where(m => m.Id != own);
        }

        var ids = await matches.OrderBy(m => m.Id).Take(take).Select(m => m.Id).ToListAsync(cancellationToken);
        if (ids.Count == 0)
        {
            return [];
        }

        var names = (
            await db
                .MovieDirectorLinks.AsNoTracking()
                .Where(l => ids.Contains(l.MovieId))
                .OrderBy(l => l.Position)
                .Select(l => new { l.MovieId, l.Director.Name })
                .ToListAsync(cancellationToken)
        )
            .GroupBy(l => l.MovieId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(l => l.Name)));
        return [.. ids.Select(id => new SameTitleMovie(id, names.GetValueOrDefault(id, "")))];
    }

    /// <summary>
    /// Finds the stored spelling of the directors that already exist (matched by the column collation, so case and
    /// accent variants match).
    /// </summary>
    /// <param name="names">The director names to look up.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The stored names of the directors that exist; unknown names are absent. Empty when no name is given.</returns>
    public async Task<IReadOnlyList<string>> FindKnownDirectorsAsync(
        IReadOnlyList<string> names,
        CancellationToken cancellationToken
    )
    {
        var wanted = names.Select(n => n.Trim()).Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
        {
            return [];
        }

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db
            .Directors.AsNoTracking()
            .Where(d => wanted.Contains(d.Name))
            .OrderBy(d => d.Id)
            .Select(d => d.Name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Suggests directors that contain the text, alphabetically (by the column collation).</summary>
    /// <param name="text">The typed text.</param>
    /// <param name="take">The largest number of suggestions.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The director names.</returns>
    public async Task<IReadOnlyList<string>> SuggestDirectorsAsync(
        string text,
        int take,
        CancellationToken cancellationToken
    )
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var directors = db.Directors.AsNoTracking();
        if (text.Length > 0)
        {
            var pattern = ContainsPattern(text);
            directors = directors.Where(d => EF.Functions.Like(d.Name, pattern));
        }

        return await directors.OrderBy(d => d.Name).Take(take).Select(d => d.Name).ToListAsync(cancellationToken);
    }

    /// <summary>Suggests medium names that contain the text, sorted by name.</summary>
    /// <param name="text">The typed text.</param>
    /// <param name="take">The largest number of suggestions.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The medium names.</returns>
    public async Task<IReadOnlyList<string>> SuggestMediaAsync(
        string text,
        int take,
        CancellationToken cancellationToken
    )
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var media = db.Media.AsNoTracking();
        if (text.Length > 0)
        {
            var pattern = ContainsPattern(text);
            media = media.Where(g => EF.Functions.Like(g.Name, pattern));
        }

        return await media.OrderBy(g => g.Name).Take(take).Select(g => g.Name).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Finds the position of a movie in the list as the overview shows it, with its neighbours and the first and last
    /// movie. When the movie is not part of the searched or filtered list, the search and genre filter are dropped.
    /// </summary>
    /// <param name="id">The movie id.</param>
    /// <param name="state">The overview state (search and sort order).</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The navigation, or <c>null</c> when there is no movie with that id.</returns>
    public async Task<MovieNavigation?> GetNavigationAsync(
        MovieId id,
        OverviewState state,
        CancellationToken cancellationToken
    )
    {
        var navigation = await NavigateAsync(id, state, cancellationToken);
        if (navigation is null && state.IsFiltered)
        {
            state = state.WithoutFilters();
            navigation = await NavigateAsync(id, state, cancellationToken);
        }

        return navigation;
    }

    private async Task<MovieNavigation?> NavigateAsync(
        MovieId id,
        OverviewState state,
        CancellationToken cancellationToken
    )
    {
        var order = OrderSql(state);
        var (where, filterParameters) = FilterSql(state);
        // The ORDER BY text is composed only from the fixed fragments of OrderSql; every value is a parameter.
        var sql =
            "SELECT t.position AS Position, t.total AS Total, t.previous_id AS PreviousId, t.next_id AS NextId, "
            + "t.first_id AS FirstId, t.last_id AS LastId FROM ("
            + $"SELECT m.id AS id, ROW_NUMBER() OVER (ORDER BY {order}) AS position, COUNT(*) OVER () AS total, "
            + $"LAG(m.id) OVER (ORDER BY {order}) AS previous_id, LEAD(m.id) OVER (ORDER BY {order}) AS next_id, "
            + $"FIRST_VALUE(m.id) OVER (ORDER BY {order}) AS first_id, "
            + $"LAST_VALUE(m.id) OVER (ORDER BY {order} ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) AS last_id "
            + "FROM movies m LEFT JOIN media md ON md.id = m.medium_id "
            + $"WHERE 1 = 1{where}) t WHERE t.id = {{{filterParameters.Length}}}";

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        object[] parameters = [.. filterParameters, id.Value];
        var row = await db.Database.SqlQueryRaw<NavigationRow>(sql, parameters).SingleOrDefaultAsync(cancellationToken);
        if (row is null)
        {
            return null;
        }

        return new MovieNavigation(
            (int)row.Position,
            (int)row.Total,
            row.PreviousId is { } previous ? MovieId.From(previous) : null,
            row.NextId is { } next ? MovieId.From(next) : null,
            MovieId.From(row.FirstId),
            MovieId.From(row.LastId),
            state
        );
    }

    /// <summary>The SQL <c>ORDER BY</c> of the overview: columns, directions, NULL placement and tie-breakers.</summary>
    /// <param name="state">The overview state holding the sort order.</param>
    /// <exception cref="ArgumentOutOfRangeException">A sort column is unknown.</exception>
    private static string OrderSql(OverviewState state)
    {
        var parts = new List<string>();
        foreach (var key in state.Sorts)
        {
            var direction = key.Descending ? " DESC" : "";
            switch (key.Field)
            {
                case OverviewSortField.WatchedOn:
                    parts.Add("m.watched_on" + direction);
                    break;
                case OverviewSortField.Title:
                    parts.Add("m.title" + direction);
                    break;
                case OverviewSortField.ReleaseYear:
                    parts.Add("m.release_year" + direction);
                    break;
                case OverviewSortField.Director:
                    // The directors as the overview shows them: in the entered order, joined by a comma.
                    parts.Add(
                        "(SELECT GROUP_CONCAT(d.name ORDER BY md2.position SEPARATOR ', ') FROM movie_directors md2 "
                            + "JOIN directors d ON d.id = md2.director_id WHERE md2.movie_id = m.id)"
                            + direction
                    );
                    break;
                case OverviewSortField.Genre:
                    // The genres as the overview shows them: alphabetical, joined by a comma.
                    parts.Add(
                        "(SELECT GROUP_CONCAT(g.name ORDER BY g.name SEPARATOR ', ') FROM movie_genres mg "
                            + "JOIN genres g ON g.id = mg.genre_id WHERE mg.movie_id = m.id)"
                            + direction
                    );
                    break;
                case OverviewSortField.RuntimeMinutes:
                    parts.Add("m.runtime_minutes" + direction);
                    break;
                case OverviewSortField.Medium:
                    parts.Add("md.name IS NULL");
                    parts.Add("md.name" + direction);
                    break;
                case OverviewSortField.OverallScore:
                    parts.Add("m.overall_score IS NULL");
                    parts.Add("m.overall_score" + direction);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), key.Field, "Unknown sort column.");
            }
        }

        if (state.SortIndexOf(OverviewSortField.WatchedOn) >= 0 && state.SortIndexOf(OverviewSortField.Title) < 0)
        {
            parts.Add("m.title");
        }

        parts.Add("m.id" + (state.PrimarySort.Descending ? " DESC" : ""));
        return string.Join(", ", parts);
    }

    /// <summary>Loads a movie for the detail page.</summary>
    /// <param name="id">The movie id.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The movie, or <c>null</c> when there is none with that id.</returns>
    public async Task<MovieDetail?> FindAsync(MovieId id, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await FindCompiled(db, id, cancellationToken);
    }

    private static readonly Func<MovieDbContext, MovieId, CancellationToken, Task<MovieDetail?>> FindCompiled =
        EF.CompileAsyncQuery(
            (MovieDbContext db, MovieId id, CancellationToken _) =>
                db
                    .Movies.AsNoTracking()
                    .Where(m => m.Id == id)
                    .Select(m => new MovieDetail(
                        m.Id,
                        m.Title,
                        m.ReleaseYear,
                        m.DirectorLinks.OrderBy(l => l.Position).Select(l => l.Director.Name).ToList(),
                        m.GenreLinks.Select(l => l.Genre.Name).Order().ToList(),
                        m.RuntimeMinutes,
                        m.WatchedOn,
                        m.Medium == null ? null : m.Medium.Name,
                        m.OverallScore,
                        m.StoryScore,
                        m.PictureScore,
                        m.MusicScore,
                        m.SoundScore,
                        m.Review
                    ))
                    .SingleOrDefault()
        );
}
