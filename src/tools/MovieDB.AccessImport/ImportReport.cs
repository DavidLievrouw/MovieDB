using System.Globalization;

namespace MovieDB.AccessImport;

/// <summary>Builds the human-readable report. It only contains counts, ids and field names, never film content.</summary>
internal static class ImportReport
{
    public static IReadOnlyList<string> Render(ImportPlan plan, DataStats? database, bool dryRun)
    {
        var lines = new List<string>
        {
            dryRun ? "Import report (DRY RUN - nothing was written)" : "Import report",
            $"Source rows read:      {plan.SourceRows}",
            $"Duplicate rows dropped: {plan.DroppedRows}",
            $"Movies planned:        {plan.Movies.Count}",
            $"Genres:                {plan.Genres.Count}",
            $"Media:                 {plan.Media.Count}",
            "",
            $"Duplicate groups ({plan.DuplicateGroups.Count}), kept id <- dropped ids:",
        };
        lines.AddRange(plan.DuplicateGroups.Select(g => $"  {g.KeptId} <- {string.Join(", ", g.DroppedIds)}"));

        lines.Add("");
        lines.Add("Attribute corrections:");
        lines.AddRange(
            plan.Corrections.Select(c =>
                $"  FilmID {c.Correction.FilmId} {c.Correction.Field}: "
                + (c.Applied ? "applied" : $"SKIPPED ({c.Reason})")
            )
        );

        lines.Add("");
        lines.Add($"Title had extra lines/tabs (first part kept): {Ids(plan.TitleExtraPartsIds)}");
        lines.Add($"Director had extra lines/tabs (first part kept): {Ids(plan.DirectorExtraPartsIds)}");
        lines.Add($"Rows with whitespace cleanup: {plan.WhitespaceCleanedIds.Count}");

        lines.Add("");
        lines.Add("Figures (plan | database):");
        var planned = plan.Stats.Lines().ToList();
        var actual = database?.Lines().ToList();
        for (var i = 0; i < planned.Count; i++)
        {
            var db = actual is null ? "n/a (dry run)" : actual[i].Value;
            var flag = actual is not null && actual[i].Value != planned[i].Value ? "  <-- MISMATCH" : "";
            lines.Add(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {planned[i].Name, -22} {planned[i].Value, 12} | {db}{flag}"
                )
            );
        }

        return lines;
    }

    private static string Ids(IReadOnlyList<int> ids) =>
        ids.Count == 0 ? "none" : $"{ids.Count} row(s): {string.Join(", ", ids)}";
}
