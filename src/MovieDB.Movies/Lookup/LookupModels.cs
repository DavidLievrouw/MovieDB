namespace MovieDB.Movies.Lookup;

/// <summary>What the owner searches for.</summary>
/// <param name="Title">The title to search for; not empty.</param>
/// <param name="Year">The release year to filter on, if the owner typed one.</param>
internal sealed record LookupQuery(string Title, int? Year = null);

/// <summary>One search result.</summary>
/// <param name="Id">The provider's numeric id.</param>
/// <param name="Title">The English title.</param>
/// <param name="OriginalTitle">The original title, if known.</param>
/// <param name="Year">The release year, if known.</param>
/// <param name="Overview">A short plot summary (cut to about 200 characters), if any.</param>
/// <param name="SourceUrl">The provider's page of the movie.</param>
internal sealed record LookupHit(
    int Id,
    string Title,
    string? OriginalTitle,
    int? Year,
    string? Overview,
    Uri SourceUrl
);

/// <summary>
/// The provider's data about one movie, cleaned of provider specifics but not yet of application rules (see
/// <see cref="LookupFill"/>).
/// </summary>
/// <param name="Title">The original title (the English title when the original is empty), trimmed.</param>
/// <param name="Year">The release year; <c>null</c> when the release date is missing or invalid.</param>
/// <param name="RuntimeMinutes">The raw runtime in minutes; <c>null</c> when missing.</param>
/// <param name="Genres">The application genre names, in the provider's order, without duplicates and not capped.</param>
/// <param name="Directors">The director names, in the provider's order, without duplicates and not capped.</param>
/// <param name="SourceUrl">The provider's page of the movie.</param>
/// <param name="ImdbUrl">The IMDb page of the movie, when the provider knows a valid IMDb id.</param>
internal sealed record LookupDetails(
    string Title,
    int? Year,
    int? RuntimeMinutes,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Directors,
    Uri SourceUrl,
    Uri? ImdbUrl
);
