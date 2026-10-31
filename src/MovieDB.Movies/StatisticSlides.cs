namespace MovieDB.Movies;

/// <summary>One figure of a statistics slide.</summary>
/// <param name="Value">The figure, already formatted.</param>
/// <param name="Label">What the figure counts.</param>
internal sealed record StatisticItem(string Value, string Label);

/// <summary>One slide of the statistics carousel.</summary>
/// <param name="Title">The slide title.</param>
/// <param name="Items">The figures on the slide.</param>
internal sealed record StatisticSlide(string Title, IReadOnlyList<StatisticItem> Items);

/// <summary>Turns <see cref="MovieStatistics"/> into the slides of the statistics carousel.</summary>
internal static class StatisticSlides
{
    /// <summary>Builds the slides; slides without data are left out.</summary>
    /// <param name="stats">The statistics.</param>
    /// <returns>The slides, or none when there are no movies.</returns>
    public static IReadOnlyList<StatisticSlide> Build(MovieStatistics stats)
    {
        if (stats.Genres.Count == 0)
        {
            return [];
        }

        var slides = new List<StatisticSlide>();
        AddCounts(slides, "Gezien op/in", stats.Media);
        AddCounts(slides, "Top genres", stats.Genres);
        AddCounts(slides, "Top regisseurs", stats.Directors);
        slides.Add(
            new StatisticSlide(
                "Recent gezien",
                [
                    new StatisticItem(Formats.Count(stats.WatchedThisMonth), "deze maand"),
                    new StatisticItem(Formats.Count(stats.WatchedThisYear), "dit jaar"),
                    new StatisticItem(Formats.Count(stats.WatchedLastYear), "vorig jaar"),
                ]
            )
        );

        var figures = new List<StatisticItem> { new(Formats.Hours(stats.TotalRuntimeMinutes), "speelduur") };
        if (stats.AverageScore is { } average)
        {
            figures.Add(new StatisticItem(Formats.AverageScore(average), "gemiddeld totaal"));
        }

        figures.Add(new StatisticItem(Formats.Count(stats.HighlyRated), "met een 9 of meer"));
        slides.Add(new StatisticSlide("In cijfers", figures));
        return slides;
    }

    private static void AddCounts(List<StatisticSlide> slides, string title, IReadOnlyList<NamedCount> counts)
    {
        if (counts.Count > 0)
        {
            slides.Add(
                new StatisticSlide(
                    title,
                    counts.Select(c => new StatisticItem(Formats.Count(c.Count), c.Name)).ToList()
                )
            );
        }
    }
}
