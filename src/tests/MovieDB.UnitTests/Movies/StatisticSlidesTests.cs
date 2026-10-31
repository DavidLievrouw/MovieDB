using MovieDB.Movies;
using Shouldly;

namespace MovieDB.UnitTests.Movies;

public class StatisticSlidesTests
{
    private static MovieStatistics Stats(
        IReadOnlyList<NamedCount>? media = null,
        double? average = 7.5,
        int genres = 1
    ) =>
        new(
            media ?? [],
            [.. Enumerable.Range(1, genres).Select(i => new NamedCount($"G{i}", i))],
            [new NamedCount("Director", 2)],
            WatchedThisMonth: 1,
            WatchedThisYear: 12,
            WatchedLastYear: 1204,
            TotalRuntimeMinutes: 6_000,
            AverageScore: average,
            HighlyRated: 3
        );

    [Fact]
    public void No_movies_gives_no_slides() => StatisticSlides.Build(Stats(genres: 0)).ShouldBeEmpty();

    [Fact]
    public void Slides_follow_a_fixed_order_and_skip_an_empty_media_slide()
    {
        var withoutMedia = StatisticSlides.Build(Stats());
        var withMedia = StatisticSlides.Build(Stats(media: [new NamedCount("Streaming", 104)]));

        withoutMedia.Select(s => s.Title).ShouldBe(["Top genres", "Top regisseurs", "Recent gezien", "In cijfers"]);
        withMedia
            .Select(s => s.Title)
            .ShouldBe(["Gezien op/in", "Top genres", "Top regisseurs", "Recent gezien", "In cijfers"]);
    }

    [Fact]
    public void Counts_are_formatted_with_their_name()
    {
        var media = StatisticSlides
            .Build(Stats(media: [new NamedCount("Streaming", 1204), new NamedCount("Bioscoop", 55)]))
            .Single(s => s.Title == "Gezien op/in");

        media.Items.ShouldBe([new StatisticItem("1.204", "Streaming"), new StatisticItem("55", "Bioscoop")]);
    }

    [Fact]
    public void The_figures_slide_shows_hours_average_and_high_scores()
    {
        var figures = StatisticSlides.Build(Stats()).Single(s => s.Title == "In cijfers");

        figures.Items.ShouldBe([
            new StatisticItem("100 uur", "speelduur"),
            new StatisticItem("7,5", "gemiddeld totaal"),
            new StatisticItem("3", "met een 9 of meer"),
        ]);
    }

    [Fact]
    public void The_average_is_left_out_when_no_movie_has_a_score()
    {
        var figures = StatisticSlides.Build(Stats(average: null)).Single(s => s.Title == "In cijfers");

        figures.Items.Select(i => i.Label).ShouldBe(["speelduur", "met een 9 of meer"]);
    }

    [Fact]
    public void The_recent_slide_shows_month_year_and_previous_year()
    {
        var recent = StatisticSlides.Build(Stats()).Single(s => s.Title == "Recent gezien");

        recent.Items.ShouldBe([
            new StatisticItem("1", "deze maand"),
            new StatisticItem("12", "dit jaar"),
            new StatisticItem("1.204", "vorig jaar"),
        ]);
    }
}
