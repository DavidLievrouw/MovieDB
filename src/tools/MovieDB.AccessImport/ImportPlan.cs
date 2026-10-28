namespace MovieDB.AccessImport;

internal sealed class ImportPlan
{
    public int SourceRows { get; init; }
    public IReadOnlyList<RowError> Errors { get; init; } = [];
    public IReadOnlyList<CleanMovie> Movies { get; init; } = [];
    public IReadOnlyList<DuplicateGroup> DuplicateGroups { get; init; } = [];
    public IReadOnlyList<CorrectionOutcome> Corrections { get; init; } = [];
    public IReadOnlyList<string> Genres { get; init; } = [];
    public IReadOnlyList<string> Media { get; init; } = [];
    public IReadOnlyList<int> TitleExtraPartsIds { get; init; } = [];
    public IReadOnlyList<int> DirectorExtraPartsIds { get; init; } = [];
    public IReadOnlyList<int> WhitespaceCleanedIds { get; init; } = [];

    public bool HasErrors => Errors.Count > 0;
    public int DroppedRows => DuplicateGroups.Sum(g => g.DroppedIds.Count);
    public DataStats Stats => DataStats.Of(Movies.ToList(), Genres.Count, Media.Count);
}

internal static class ImportPlanner
{
    /// <summary>Genre suggestions known from the Access form's value list.</summary>
    public static readonly IReadOnlyList<string> KnownGenres =
    [
        "Actie",
        "Animatie",
        "Avontuur",
        "Bond",
        "Detective",
        "Detective-thriller",
        "Documentaire",
        "Drama",
        "Horror",
        "Kinder",
        "Komedie",
        "Melodrama",
        "Misdaad",
        "Oorlogs",
        "Rampen",
        "Romantisch",
        "Sci-fi",
        "Thriller",
        "Western",
        "Zwarte komedie",
        "Andere",
    ];

    /// <summary>Medium suggestions known from the Access form's value list.</summary>
    public static readonly IReadOnlyList<string> KnownMedia = ["Blu-ray", "DVD", "Video", "TV", "Bioscoop", "Andere"];

    public static ImportPlan Plan(IReadOnlyList<SourceFilm> rows)
    {
        var results = rows.Select(MovieCleaner.Clean).ToList();
        var errors = results.SelectMany(r => r.Errors).ToList();
        var duplicateFilmIds = rows.GroupBy(r => r.FilmId).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        errors.AddRange(duplicateFilmIds.Select(id => new RowError(id, "FilmID", "occurs more than once")));

        var cleaned = results
            .Where(r => r.Movie is not null)
            .Select(r => r.Movie!)
            .GroupBy(m => m.Id)
            .Select(g => g.First())
            .ToList();
        var (kept, groups) = DuplicateResolver.Resolve(cleaned);
        var (corrected, outcomes) = AttributeCorrections.Apply(kept, AttributeCorrections.Known);

        var genres = new LookupResolver(KnownGenres);
        var media = new LookupResolver(KnownMedia);
        var resolved = corrected
            .Select(m =>
                m with
                {
                    Genre = genres.Resolve(m.Genre),
                    Medium = m.Medium is null ? null : media.Resolve(m.Medium),
                }
            )
            .ToList();

        return new ImportPlan
        {
            SourceRows = rows.Count,
            Errors = errors,
            Movies = resolved,
            DuplicateGroups = groups,
            Corrections = outcomes,
            Genres = genres.Names,
            Media = media.Names,
            TitleExtraPartsIds = results.Where(r => r.TitleExtraPartsDropped).Select(r => r.FilmId).ToList(),
            DirectorExtraPartsIds = results.Where(r => r.DirectorExtraPartsDropped).Select(r => r.FilmId).ToList(),
            WhitespaceCleanedIds = results.Where(r => r.WhitespaceCleaned).Select(r => r.FilmId).ToList(),
        };
    }

    /// <summary>Resolves values to a canonical suggestion name using the collation key; unknown values are appended.</summary>
    private sealed class LookupResolver
    {
        private readonly Dictionary<string, string> _byKey = new(StringComparer.Ordinal);
        private readonly List<string> _names = [];

        public LookupResolver(IEnumerable<string> seed)
        {
            foreach (var name in seed)
            {
                Resolve(name);
            }
        }

        public IReadOnlyList<string> Names => _names;

        public string Resolve(string value)
        {
            var key = CollationKey.Of(value);
            if (_byKey.TryGetValue(key, out var existing))
            {
                return existing;
            }

            _byKey[key] = value;
            _names.Add(value);
            return value;
        }
    }
}
