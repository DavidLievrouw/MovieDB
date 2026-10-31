using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MovieDB.Common;
using MovieDB.Infrastructure;
using MovieDB.Infrastructure.Entities;

namespace MovieDB.Movies;

/// <summary>One row of the overview.</summary>
/// <param name="Id">The movie id.</param>
/// <param name="Title">The title.</param>
/// <param name="ReleaseYear">The release year.</param>
/// <param name="Director">The director.</param>
/// <param name="Genre">The genre name.</param>
/// <param name="RuntimeMinutes">The runtime in minutes.</param>
/// <param name="WatchedOn">The viewing date.</param>
/// <param name="Medium">The medium name, if any.</param>
/// <param name="OverallScore">The overall score, if any.</param>
internal sealed record MovieOverviewRow(
    MovieId Id,
    string Title,
    int ReleaseYear,
    string Director,
    string Genre,
    int RuntimeMinutes,
    DateOnly WatchedOn,
    string? Medium,
    int? OverallScore
);

/// <summary>One page of the overview.</summary>
/// <param name="Rows">The rows of the page.</param>
/// <param name="Page">The effective 1-based page number (clamped to the last page).</param>
/// <param name="PageCount">The number of pages (at least 1).</param>
/// <param name="TotalMatching">The number of movies matching the search.</param>
/// <param name="TotalMovies">The number of movies without any search.</param>
internal sealed record MovieOverviewPage(
    IReadOnlyList<MovieOverviewRow> Rows,
    int Page,
    int PageCount,
    int TotalMatching,
    int TotalMovies
);

/// <summary>An existing movie that has the same title and release year as the movie being saved.</summary>
/// <param name="Id">The movie id.</param>
/// <param name="Director">The director of the existing movie.</param>
internal sealed record SameTitleMovie(MovieId Id, string Director);

/// <summary>A movie as shown on the detail page.</summary>
/// <param name="Id">The movie id.</param>
/// <param name="Title">The title.</param>
/// <param name="ReleaseYear">The release year.</param>
/// <param name="Director">The director.</param>
/// <param name="Genre">The genre name.</param>
/// <param name="RuntimeMinutes">The runtime in minutes.</param>
/// <param name="WatchedOn">The viewing date.</param>
/// <param name="Medium">The medium name, if any.</param>
/// <param name="OverallScore">The overall score.</param>
/// <param name="StoryScore">The story score.</param>
/// <param name="PictureScore">The picture score.</param>
/// <param name="MusicScore">The music score.</param>
/// <param name="SoundScore">The sound score.</param>
/// <param name="ExtrasScore">The extras score.</param>
/// <param name="Review">The written review.</param>
internal sealed record MovieDetail(
    MovieId Id,
    string Title,
    int ReleaseYear,
    string Director,
    string Genre,
    int RuntimeMinutes,
    DateOnly WatchedOn,
    string? Medium,
    int? OverallScore,
    int? StoryScore,
    int? PictureScore,
    int? MusicScore,
    int? SoundScore,
    int? ExtrasScore,
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
/// <param name="State">The overview state the navigation applies to (the search is dropped when it excludes the movie).</param>
internal sealed record MovieNavigation(
    int Position,
    int Total,
    MovieId? PreviousId,
    MovieId? NextId,
    MovieId FirstId,
    MovieId LastId,
    OverviewState State
);

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

    /// <summary>Loads one page of the overview.</summary>
    /// <param name="state">The view state.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The page; the page number is clamped to the last page.</returns>
    public async Task<MovieOverviewPage> ListOverviewAsync(OverviewState state, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        IQueryable<Movie> all = db.Movies.AsNoTracking();
        var totalMovies = await all.CountAsync(cancellationToken);

        var matching = all;
        var totalMatching = totalMovies;
        if (state.Search.Length > 0)
        {
            var pattern = ContainsPattern(state.Search);
            matching = all.Where(m => EF.Functions.Like(m.Title, pattern));
            totalMatching = await matching.CountAsync(cancellationToken);
        }

        var pageCount = Math.Max(1, (int)Math.Ceiling(totalMatching / (double)state.PageSize));
        var page = Math.Clamp(state.Page, 1, pageCount);

        var rows = await Order(matching, state)
            .Skip((page - 1) * state.PageSize)
            .Take(state.PageSize)
            .Select(m => new MovieOverviewRow(
                m.Id,
                m.Title,
                m.ReleaseYear,
                m.Director,
                m.Genre.Name,
                m.RuntimeMinutes,
                m.WatchedOn,
                m.Medium == null ? null : m.Medium.Name,
                m.OverallScore
            ))
            .ToListAsync(cancellationToken);

        return new MovieOverviewPage(rows, page, pageCount, totalMatching, totalMovies);
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

        var genres = await TopAsync(all.GroupBy(m => m.Genre.Name), TopCount, cancellationToken);
        var directors = await TopAsync(all.GroupBy(m => m.Director), TopCount, cancellationToken);
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

    private static async Task<IReadOnlyList<NamedCount>> TopAsync(
        IQueryable<IGrouping<string, Movie>> groups,
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
        return await db
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
                    Genre = m.Genre.Name,
                    RuntimeMinutes = m.RuntimeMinutes,
                    ReleaseYear = m.ReleaseYear,
                    Director = m.Director,
                    WatchedOn = m.WatchedOn,
                    Medium = m.Medium == null ? null : m.Medium.Name,
                    Review = m.Review,
                    OverallScore = m.OverallScore,
                    StoryScore = m.StoryScore,
                    PictureScore = m.PictureScore,
                    MusicScore = m.MusicScore,
                    SoundScore = m.SoundScore,
                    ExtrasScore = m.ExtrasScore,
                }
            ))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Finds another movie with the same title, year and director (compared by the column collation).</summary>
    /// <param name="title">The title.</param>
    /// <param name="year">The release year.</param>
    /// <param name="director">The director.</param>
    /// <param name="excludeId">The movie being edited, which is not a duplicate of itself; <c>null</c> for a new movie.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The id of the existing movie, or <c>null</c> when there is none.</returns>
    public async Task<MovieId?> FindDuplicateAsync(
        string title,
        int year,
        string director,
        MovieId? excludeId,
        CancellationToken cancellationToken
    )
    {
        title = title.Trim();
        director = director.Trim();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var matches = db
            .Movies.AsNoTracking()
            .Where(m => m.Title == title && m.ReleaseYear == year && m.Director == director);
        if (excludeId is { } own)
        {
            matches = matches.Where(m => m.Id != own);
        }

        return await matches.OrderBy(m => m.Id).Select(m => (MovieId?)m.Id).FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Finds other movies with the same title and release year but another director (compared by the column
    /// collation). Movies with the same director are left out: those are duplicates, which saving rejects.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="year">The release year.</param>
    /// <param name="director">The director of the movie that is being saved.</param>
    /// <param name="excludeId">The movie being edited; <c>null</c> for a new movie.</param>
    /// <param name="take">The largest number of movies to return.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The movies, oldest first.</returns>
    public async Task<IReadOnlyList<SameTitleMovie>> FindSameTitleAndYearAsync(
        string title,
        int year,
        string director,
        MovieId? excludeId,
        int take,
        CancellationToken cancellationToken
    )
    {
        title = title.Trim();
        director = director.Trim();
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var matches = db
            .Movies.AsNoTracking()
            .Where(m => m.Title == title && m.ReleaseYear == year && m.Director != director);
        if (excludeId is { } own)
        {
            matches = matches.Where(m => m.Id != own);
        }

        return await matches
            .OrderBy(m => m.Id)
            .Take(take)
            .Select(m => new SameTitleMovie(m.Id, m.Director))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Suggests genre names that contain the text; without text, the most used ones.</summary>
    /// <param name="text">The typed text.</param>
    /// <param name="take">The largest number of suggestions.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The genre names.</returns>
    public async Task<IReadOnlyList<string>> SuggestGenresAsync(
        string text,
        int take,
        CancellationToken cancellationToken
    )
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var genres = db.Genres.AsNoTracking();
        if (text.Length > 0)
        {
            var pattern = ContainsPattern(text);
            genres = genres.Where(g => EF.Functions.Like(g.Name, pattern));
        }

        return await genres
            .OrderByDescending(g => db.Movies.Count(m => m.GenreId == g.Id))
            .ThenBy(g => g.Name)
            .Take(take)
            .Select(g => g.Name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Suggests directors that contain the text; without text, the most used ones. Directors that only differ in case
    /// or accents (by the collation) are one suggestion.
    /// </summary>
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
        var movies = db.Movies.AsNoTracking();
        if (text.Length > 0)
        {
            var pattern = ContainsPattern(text);
            movies = movies.Where(m => EF.Functions.Like(m.Director, pattern));
        }

        return await movies
            .GroupBy(m => m.Director)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key)
            .Take(take)
            .Select(g => g.Key)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Suggests medium names that contain the text; without text, the most used ones.</summary>
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

        return await media
            .OrderByDescending(g => db.Movies.Count(m => m.MediumId == g.Id))
            .ThenBy(g => g.Name)
            .Take(take)
            .Select(g => g.Name)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Finds the position of a movie in the list as the overview shows it, with its neighbours and the first and last
    /// movie. When the movie is not part of the searched list, the search is dropped.
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
        if (navigation is null && state.Search.Length > 0)
        {
            state = state.WithoutSearch();
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
        var search = state.Search.Length > 0 ? " AND m.title LIKE {1}" : "";
        // The ORDER BY text is composed only from the fixed fragments of OrderSql; every value is a parameter.
        var sql =
            "SELECT t.position AS Position, t.total AS Total, t.previous_id AS PreviousId, t.next_id AS NextId, "
            + "t.first_id AS FirstId, t.last_id AS LastId FROM ("
            + $"SELECT m.id AS id, ROW_NUMBER() OVER (ORDER BY {order}) AS position, COUNT(*) OVER () AS total, "
            + $"LAG(m.id) OVER (ORDER BY {order}) AS previous_id, LEAD(m.id) OVER (ORDER BY {order}) AS next_id, "
            + $"FIRST_VALUE(m.id) OVER (ORDER BY {order}) AS first_id, "
            + $"LAST_VALUE(m.id) OVER (ORDER BY {order} ROWS BETWEEN UNBOUNDED PRECEDING AND UNBOUNDED FOLLOWING) AS last_id "
            + "FROM movies m JOIN genres g ON g.id = m.genre_id LEFT JOIN media md ON md.id = m.medium_id "
            + $"WHERE 1 = 1{search}) t WHERE t.id = {{0}}";

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        object[] parameters = state.Search.Length > 0 ? [id.Value, ContainsPattern(state.Search)] : [id.Value];
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

    /// <summary>The SQL counterpart of <see cref="Order"/>: the same columns, directions, NULL placement and tie-breakers.</summary>
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
                    parts.Add("m.director" + direction);
                    break;
                case OverviewSortField.Genre:
                    parts.Add("g.name" + direction);
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
        return await db
            .Movies.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new MovieDetail(
                m.Id,
                m.Title,
                m.ReleaseYear,
                m.Director,
                m.Genre.Name,
                m.RuntimeMinutes,
                m.WatchedOn,
                m.Medium == null ? null : m.Medium.Name,
                m.OverallScore,
                m.StoryScore,
                m.PictureScore,
                m.MusicScore,
                m.SoundScore,
                m.ExtrasScore,
                m.Review
            ))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Orders by the chosen columns in turn, then by the tie-breakers that keep paging stable. Nullable columns sort
    /// their NULLs last in both directions; a sort that includes the viewing date but not the title falls back to
    /// the title; the id follows the direction of the most significant column.
    /// </summary>
    /// <param name="query">The query to order.</param>
    /// <param name="state">The overview state holding the sort order.</param>
    /// <exception cref="ArgumentOutOfRangeException">A sort column is unknown.</exception>
    private static IOrderedQueryable<Movie> Order(IQueryable<Movie> query, OverviewState state)
    {
        IOrderedQueryable<Movie>? ordered = null;
        foreach (var key in state.Sorts)
        {
            ordered = Apply(query, ordered, key);
        }

        if (state.SortIndexOf(OverviewSortField.WatchedOn) >= 0 && state.SortIndexOf(OverviewSortField.Title) < 0)
        {
            ordered = Then(ordered!, m => m.Title, false);
        }

        return Then(ordered!, m => m.Id, state.PrimarySort.Descending);
    }

    /// <summary>Adds one sort column to an ordering.</summary>
    /// <param name="query">The unordered query, used when there is no ordering yet.</param>
    /// <param name="ordered">The ordering so far, or <c>null</c> for the first column.</param>
    /// <param name="sort">The column to add.</param>
    /// <exception cref="ArgumentOutOfRangeException">The sort column is unknown.</exception>
    private static IOrderedQueryable<Movie> Apply(
        IQueryable<Movie> query,
        IOrderedQueryable<Movie>? ordered,
        SortKey sort
    )
    {
        var desc = sort.Descending;

        IOrderedQueryable<Movie> Column<TKey>(Expression<Func<Movie, TKey>> key) =>
            ordered is null ? By(query, key, desc) : Then(ordered, key, desc);

        IOrderedQueryable<Movie> NullsLast<TKey>(
            Expression<Func<Movie, bool>> isMissing,
            Expression<Func<Movie, TKey>> key
        ) => Then(ordered is null ? query.OrderBy(isMissing) : ordered.ThenBy(isMissing), key, desc);

        return sort.Field switch
        {
            OverviewSortField.WatchedOn => Column(m => m.WatchedOn),
            OverviewSortField.Title => Column(m => m.Title),
            OverviewSortField.ReleaseYear => Column(m => m.ReleaseYear),
            OverviewSortField.Director => Column(m => m.Director),
            OverviewSortField.Genre => Column(m => m.Genre.Name),
            OverviewSortField.RuntimeMinutes => Column(m => m.RuntimeMinutes),
            OverviewSortField.Medium => NullsLast(m => m.Medium == null, m => m.Medium!.Name),
            OverviewSortField.OverallScore => NullsLast(m => m.OverallScore == null, m => m.OverallScore),
            _ => throw new ArgumentOutOfRangeException(nameof(sort), sort.Field, "Unknown sort column."),
        };
    }

    private static IOrderedQueryable<Movie> By<TKey>(
        IQueryable<Movie> query,
        Expression<Func<Movie, TKey>> key,
        bool descending
    ) => descending ? query.OrderByDescending(key) : query.OrderBy(key);

    private static IOrderedQueryable<Movie> Then<TKey>(
        IOrderedQueryable<Movie> query,
        Expression<Func<Movie, TKey>> key,
        bool descending
    ) => descending ? query.ThenByDescending(key) : query.ThenBy(key);
}
