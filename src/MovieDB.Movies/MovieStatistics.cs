namespace MovieDB.Movies;

/// <summary>A name with the number of movies that have it.</summary>
/// <param name="Name">The genre, director or medium name.</param>
/// <param name="Count">The number of movies.</param>
internal sealed record NamedCount(string Name, int Count);

/// <summary>Statistics over the whole movie table, independent of any search or paging.</summary>
/// <param name="Media">The media movies were watched on or in, most used first (at most <see cref="MovieQueries.MaxMedia"/>).</param>
/// <param name="Genres">The most common genres, most common first (at most <see cref="MovieQueries.TopCount"/>).</param>
/// <param name="Directors">The most common directors, most common first (at most <see cref="MovieQueries.TopCount"/>).</param>
/// <param name="WatchedThisMonth">The number of movies watched in the current month.</param>
/// <param name="WatchedThisYear">The number of movies watched in the current year.</param>
/// <param name="WatchedLastYear">The number of movies watched in the previous year.</param>
/// <param name="TotalRuntimeMinutes">The summed runtime of all movies, in minutes.</param>
/// <param name="AverageScore">The average overall score, or <c>null</c> when no movie has one.</param>
/// <param name="HighlyRated">The number of movies with an overall score of 9 or more.</param>
internal sealed record MovieStatistics(
    IReadOnlyList<NamedCount> Media,
    IReadOnlyList<NamedCount> Genres,
    IReadOnlyList<NamedCount> Directors,
    int WatchedThisMonth,
    int WatchedThisYear,
    int WatchedLastYear,
    int TotalRuntimeMinutes,
    double? AverageScore,
    int HighlyRated
);
