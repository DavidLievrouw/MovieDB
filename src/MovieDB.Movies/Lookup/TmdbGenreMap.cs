using System.Collections.Frozen;

namespace MovieDB.Movies.Lookup;

/// <summary>
/// Maps TMDB genre ids to the closed list of application genres (PLAN-3 §6). The map is keyed by the TMDB id, never by
/// the localized name. TMDB's "TV Movie" (10770) is deliberately not mapped.
/// </summary>
internal static class TmdbGenreMap
{
    /// <summary>The TMDB id of "TV Movie", which has no application genre.</summary>
    public const int TvMovieId = 10770;

    private static readonly FrozenDictionary<int, string> Names = new Dictionary<int, string>
    {
        [28] = "Actie",
        [12] = "Avontuur",
        [16] = "Animatie",
        [35] = "Komedie",
        [80] = "Misdaad",
        [99] = "Documentaire",
        [18] = "Drama",
        [10751] = "Familie",
        [14] = "Fantasy",
        [36] = "Historisch",
        [27] = "Horror",
        [10402] = "Musical",
        [9648] = "Mystery",
        [10749] = "Romantisch",
        [878] = "Sci-fi",
        [53] = "Thriller",
        [10752] = "Oorlogs",
        [37] = "Western",
    }.ToFrozenDictionary();

    /// <summary>Gets the TMDB genre ids that map to an application genre.</summary>
    public static IReadOnlyCollection<int> MappedIds => Names.Keys;

    /// <summary>Finds the application genre for a TMDB genre id.</summary>
    /// <param name="tmdbId">The TMDB genre id.</param>
    /// <param name="name">The application genre name when the method returns <c>true</c>.</param>
    /// <returns><c>true</c> when the id maps to an application genre.</returns>
    public static bool TryGetName(int tmdbId, out string name)
    {
        if (Names.TryGetValue(tmdbId, out var found))
        {
            name = found;
            return true;
        }

        name = "";
        return false;
    }
}
