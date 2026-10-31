namespace MovieDB.Movies;

/// <summary>The columns the overview can be sorted on. The URL names are the camelCase member names.</summary>
internal enum OverviewSortField
{
    WatchedOn = 0,
    Title = 1,
    ReleaseYear = 2,
    Director = 3,
    Genre = 4,
    RuntimeMinutes = 5,
    Medium = 6,
    OverallScore = 7,
}
