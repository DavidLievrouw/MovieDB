namespace MovieDB.AccessImport;

internal sealed record DuplicateGroup(int KeptId, IReadOnlyList<int> DroppedIds);

/// <summary>One of the fixed, IMDb-verified attribute corrections, applied only when the source still holds the expected value.</summary>
/// <param name="FilmId">The Access FilmID of the row to correct.</param>
/// <param name="Field">The name of the field to correct.</param>
/// <param name="Expected">The value the source must still hold for the correction to apply.</param>
/// <param name="Replacement">The corrected value.</param>
internal sealed record AttributeCorrection(int FilmId, string Field, string Expected, string Replacement);

internal sealed record CorrectionOutcome(AttributeCorrection Correction, bool Applied, string Reason);

internal static class DuplicateResolver
{
    /// <summary>Groups on Title + Year + Director and keeps the earliest Datum, then the lowest FilmID.</summary>
    /// <param name="movies">The cleaned movies to de-duplicate.</param>
    public static (IReadOnlyList<CleanMovie> Kept, IReadOnlyList<DuplicateGroup> Groups) Resolve(
        IEnumerable<CleanMovie> movies
    )
    {
        var kept = new List<CleanMovie>();
        var groups = new List<DuplicateGroup>();
        foreach (
            var group in movies
                .GroupBy(m => CollationKey.Of(m.Title, m.ReleaseYear, m.Director))
                .OrderBy(g => g.Min(m => m.Id))
        )
        {
            var ordered = group.OrderBy(m => m.WatchedOn).ThenBy(m => m.Id).ToList();
            kept.Add(ordered[0]);
            if (ordered.Count > 1)
            {
                groups.Add(new DuplicateGroup(ordered[0].Id, ordered.Skip(1).Select(m => m.Id).Order().ToList()));
            }
        }

        return (kept.OrderBy(m => m.Id).ToList(), groups);
    }
}

internal static class AttributeCorrections
{
    public static readonly IReadOnlyList<AttributeCorrection> Known =
    [
        new(33, "Title", "Goldeneye", "GoldenEye"),
        new(33, "Runtime", "145", "130"),
        new(148, "Runtime", "132", "133"),
        new(308, "Genre", "Actie", "Sci-fi"),
        new(328, "Runtime", "116", "117"),
        new(765, "Runtime", "99", "83"),
    ];

    public static (IReadOnlyList<CleanMovie> Movies, IReadOnlyList<CorrectionOutcome> Outcomes) Apply(
        IReadOnlyList<CleanMovie> movies,
        IReadOnlyList<AttributeCorrection> corrections
    )
    {
        var byId = movies.ToDictionary(m => m.Id);
        var outcomes = new List<CorrectionOutcome>();
        foreach (var correction in corrections)
        {
            if (!byId.TryGetValue(correction.FilmId, out var movie))
            {
                outcomes.Add(new CorrectionOutcome(correction, false, "row is not imported"));
                continue;
            }

            var current = correction.Field switch
            {
                "Title" => movie.Title,
                "Runtime" => movie.RuntimeMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "Genre" => movie.Genre,
                _ => throw new ImportException($"Unknown correction field '{correction.Field}'."),
            };
            if (!string.Equals(current, correction.Expected, StringComparison.Ordinal))
            {
                outcomes.Add(new CorrectionOutcome(correction, false, "source value differs from the expected value"));
                continue;
            }

            byId[correction.FilmId] = correction.Field switch
            {
                "Title" => movie with { Title = correction.Replacement },
                "Runtime" => movie with
                {
                    RuntimeMinutes = int.Parse(
                        correction.Replacement,
                        System.Globalization.CultureInfo.InvariantCulture
                    ),
                },
                _ => movie with { Genre = correction.Replacement },
            };
            outcomes.Add(new CorrectionOutcome(correction, true, "applied"));
        }

        return (movies.Select(m => byId[m.Id]).ToList(), outcomes);
    }
}
