using System.Globalization;

namespace MovieDB.AccessImport;

/// <summary>Aggregate figures used to compare the plan with what the database holds after loading. Never contains content.</summary>
/// <param name="Movies">Number of movies.</param>
/// <param name="Genres">Number of genre rows (the seeded list).</param>
/// <param name="GenreLinks">Number of movie-genre links.</param>
/// <param name="Media">Number of medium suggestion rows.</param>
/// <param name="Directors">Number of distinct director rows.</param>
/// <param name="DirectorLinks">Number of movie-director links.</param>
/// <param name="NullOverall">Movies without an overall score.</param>
/// <param name="NullStory">Movies without a story score.</param>
/// <param name="NullPicture">Movies without a picture score.</param>
/// <param name="NullMusic">Movies without a music score.</param>
/// <param name="NullSound">Movies without a sound score.</param>
/// <param name="NullMedium">Movies without a medium.</param>
/// <param name="NullReview">Movies without a review.</param>
/// <param name="MaxTitleLength">Longest title, in characters.</param>
/// <param name="MaxDirectorLength">Longest director, in characters.</param>
/// <param name="MaxReviewLength">Longest review, in characters.</param>
/// <param name="MinWatchedOn">Earliest viewing date, if any.</param>
/// <param name="MaxWatchedOn">Latest viewing date, if any.</param>
/// <param name="MaxId">Highest movie id.</param>
internal sealed record DataStats(
    int Movies,
    int Genres,
    int GenreLinks,
    int Media,
    int Directors,
    int DirectorLinks,
    int NullOverall,
    int NullStory,
    int NullPicture,
    int NullMusic,
    int NullSound,
    int NullMedium,
    int NullReview,
    int MaxTitleLength,
    int MaxDirectorLength,
    int MaxReviewLength,
    DateOnly? MinWatchedOn,
    DateOnly? MaxWatchedOn,
    int MaxId
)
{
    /// <summary>Builds the figures of a plan: every movie has exactly one genre link and one link per director.</summary>
    /// <param name="movies">The planned movies.</param>
    /// <param name="genres">The number of seeded genres.</param>
    /// <param name="media">The number of medium rows.</param>
    /// <returns>The figures.</returns>
    public static DataStats Of(IReadOnlyCollection<CleanMovie> movies, int genres, int media) =>
        new(
            movies.Count,
            genres,
            movies.Count,
            media,
            movies.SelectMany(m => m.Directors).Distinct(StringComparer.Ordinal).Count(),
            movies.Sum(m => m.Directors.Count),
            movies.Count(m => m.OverallScore is null),
            movies.Count(m => m.StoryScore is null),
            movies.Count(m => m.PictureScore is null),
            movies.Count(m => m.MusicScore is null),
            movies.Count(m => m.SoundScore is null),
            movies.Count(m => m.Medium is null),
            movies.Count(m => m.Review is null),
            movies.Select(m => MovieCleaner.CharCount(m.Title)).DefaultIfEmpty(0).Max(),
            movies.SelectMany(m => m.Directors).Select(MovieCleaner.CharCount).DefaultIfEmpty(0).Max(),
            movies.Select(m => m.Review is null ? 0 : MovieCleaner.CharCount(m.Review)).DefaultIfEmpty(0).Max(),
            movies.Count == 0 ? null : movies.Min(m => m.WatchedOn),
            movies.Count == 0 ? null : movies.Max(m => m.WatchedOn),
            movies.Select(m => m.Id).DefaultIfEmpty(0).Max()
        );

    public IEnumerable<(string Name, string Value)> Lines()
    {
        yield return ("movies", Text(Movies));
        yield return ("genres", Text(Genres));
        yield return ("movie_genres", Text(GenreLinks));
        yield return ("media", Text(Media));
        yield return ("directors", Text(Directors));
        yield return ("movie_directors", Text(DirectorLinks));
        yield return ("NULL overall_score", Text(NullOverall));
        yield return ("NULL story_score", Text(NullStory));
        yield return ("NULL picture_score", Text(NullPicture));
        yield return ("NULL music_score", Text(NullMusic));
        yield return ("NULL sound_score", Text(NullSound));
        yield return ("NULL medium", Text(NullMedium));
        yield return ("NULL review", Text(NullReview));
        yield return ("max title length", Text(MaxTitleLength));
        yield return ("max director length", Text(MaxDirectorLength));
        yield return ("max review length", Text(MaxReviewLength));
        yield return ("min watched_on", Date(MinWatchedOn));
        yield return ("max watched_on", Date(MaxWatchedOn));
        yield return ("max id", Text(MaxId));
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Date(DateOnly? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";
}
