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
