namespace MovieDB.Infrastructure;

/// <summary>
/// The closed list of genres. The <c>Initial</c> migration seeds these rows with exactly these ids, and the ids appear in
/// the overview URL (<c>genre=3,7</c>), so they are fixed and stable: only append new genres with the next id.
/// </summary>
public static class MovieGenres
{
    /// <summary>The longest genre name the <c>genres.name</c> column allows.</summary>
    public const int MaxNameLength = 50;

    /// <summary>The largest number of genres a movie can have.</summary>
    public const int MaxPerMovie = 4;

    /// <summary>All genres, in id order.</summary>
    public static readonly IReadOnlyList<(int Id, string Name)> All =
    [
        (1, "Actie"),
        (2, "Andere"),
        (3, "Animatie"),
        (4, "Avontuur"),
        (5, "Bond"),
        (6, "Detective"),
        (7, "Detective-thriller"),
        (8, "Documentaire"),
        (9, "Drama"),
        (10, "Horror"),
        (11, "Kinder"),
        (12, "Komedie"),
        (13, "Melodrama"),
        (14, "Misdaad"),
        (15, "Oorlogs"),
        (16, "Rampen"),
        (17, "Romantisch"),
        (18, "Sci-fi"),
        (19, "Thriller"),
        (20, "Western"),
        (21, "Zwarte komedie"),
        (22, "Musical"),
        (23, "Mystery"),
        (24, "Film noir"),
        (25, "Sport"),
        (26, "Kortfilm"),
        (27, "Biografie"),
        (28, "Familie"),
        (29, "Fantasy"),
        (30, "Historisch"),
        (31, "Superhelden"),
        (32, "Romantische komedie"),
        (33, "Psychologische thriller"),
        (34, "Roadmovie"),
        (35, "Tragikomedie"),
    ];
}
