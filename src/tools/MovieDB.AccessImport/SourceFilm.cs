namespace MovieDB.AccessImport;

/// <summary>One raw <c>tblFilms</c> record, as stored in Access: <c>Duur</c>, <c>Jaar</c> and <c>Extras</c> stay text.</summary>
/// <param name="FilmId">The Access FilmID.</param>
/// <param name="Title">The raw title.</param>
/// <param name="Genre">The raw genre.</param>
/// <param name="Duration">The raw runtime text (Duur).</param>
/// <param name="Year">The raw release year text (Jaar).</param>
/// <param name="Director">The raw director.</param>
/// <param name="WatchedOn">The viewing date (Datum).</param>
/// <param name="OverallScore">The raw overall score; 0 means empty.</param>
/// <param name="StoryScore">The raw story score; 0 means empty.</param>
/// <param name="PictureScore">The raw picture score; 0 means empty.</param>
/// <param name="MusicScore">The raw music score; 0 means empty.</param>
/// <param name="SoundScore">The raw sound score; 0 means empty.</param>
/// <param name="Extras">The raw extras score text; "n/a" means empty.</param>
/// <param name="Medium">The raw medium (Gezien op).</param>
/// <param name="Review">The raw review.</param>
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
