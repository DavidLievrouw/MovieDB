namespace MovieDB.AccessImport;

/// <summary>One raw <c>tblFilms</c> record, as stored in Access: <c>Duur</c>, <c>Jaar</c> and <c>Extras</c> stay text.</summary>
internal sealed record SourceFilm(
    int FilmId,
    string? Title,
    string? Genre,
    string? Duration,
    string? Year,
    string? Director,
    DateTime? WatchedOn,
    int? OverallScore,
    int? StoryScore,
    int? PictureScore,
    int? MusicScore,
    int? SoundScore,
    string? Extras,
    string? Medium,
    string? Review
);

/// <summary>Abstraction over the Access table, so the rules run on synthetic rows in tests and on any platform.</summary>
internal interface IFilmSource
{
    IReadOnlyList<SourceFilm> ReadFilms();
}

/// <summary>A source or loading problem. Messages never contain row contents (titles, reviews, ...).</summary>
public sealed class ImportException : Exception
{
    public ImportException() { }

    public ImportException(string message)
        : base(message) { }

    public ImportException(string message, Exception innerException)
        : base(message, innerException) { }
}
