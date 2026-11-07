using MovieDB.Common;

namespace MovieDB.Movies.Lookup;

/// <summary>Every Dutch UI string of the online lookup, so the texts are tested and shared.</summary>
internal static class LookupMessages
{
    /// <summary>Shown when the search text is too short.</summary>
    public const string TooShort = "Typ minstens 2 tekens.";

    /// <summary>Shown when the search text is too long.</summary>
    public const string TooLong = "Maximaal 200 tekens.";

    /// <summary>Shown when the lookup is not set up.</summary>
    public const string NotConfigured = "Online zoeken is niet ingesteld.";

    /// <summary>Shown when the movie does not exist (any more) at the provider.</summary>
    public const string NotFound = "Deze film bestaat niet (meer) bij TMDB. Kies een andere.";

    /// <summary>Shown when the provider rejects our credentials.</summary>
    public const string Misconfigured = "Online zoeken is momenteel niet beschikbaar.";

    /// <summary>Shown when the provider refuses because of too many requests.</summary>
    public const string RateLimited = "Te veel aanvragen. Probeer het over een minuut opnieuw.";

    /// <summary>Shown when the provider cannot be reached or fails.</summary>
    public const string Unavailable = "De filmdatabase is momenteel niet bereikbaar. Probeer het later opnieuw.";

    /// <summary>Shown when the provider's answer cannot be understood.</summary>
    public const string InvalidResponse = "De gegevens konden niet worden gelezen. Probeer het later opnieuw.";

    /// <summary>Shown in addition to the error when fetching the details failed.</summary>
    public const string DetailsFailed = "De gegevens van deze film konden niet worden opgehaald.";

    /// <summary>Notice: the runtime is missing.</summary>
    public const string RuntimeMissing = "Duur ontbreekt bij TMDB; vul die zelf in.";

    /// <summary>Notice: more directors were found than a movie can have.</summary>
    public const string TooManyDirectors = "Meer dan 5 regisseurs; enkel de eerste 5 zijn overgenomen.";

    /// <summary>Notice: no director was found.</summary>
    public const string DirectorMissing = "Regisseur ontbreekt bij TMDB; vul die zelf in.";

    /// <summary>Notice: no genre could be mapped.</summary>
    public const string GenreMissing = "Geen passend genre gevonden; kies zelf een genre.";

    /// <summary>Notice: the release year is missing or out of range.</summary>
    public const string YearMissing = "Releasedatum ontbreekt; vul het jaar zelf in.";

    /// <summary>Gets the user message for a failure of the lookup.</summary>
    /// <param name="kind">The kind of the failure.</param>
    /// <returns>The Dutch message; never the technical <see cref="Error.Message"/>.</returns>
    public static string For(ErrorKind kind) =>
        kind switch
        {
            ErrorKind.NotConfigured => NotConfigured,
            ErrorKind.NotFound => NotFound,
            ErrorKind.Misconfigured => Misconfigured,
            ErrorKind.RateLimited => RateLimited,
            ErrorKind.InvalidResponse => InvalidResponse,
            _ => Unavailable,
        };

    /// <summary>Gets the message for a search without hits.</summary>
    /// <param name="text">The text that was searched for.</param>
    /// <returns>The Dutch message.</returns>
    public static string NoResults(string text) =>
        $"Geen films gevonden voor \"{text}\". Controleer de spelling of probeer de originele titel.";
}
