using MovieDB.Movies;
using Shouldly;

namespace MovieDB.UnitTests.Movies;

public class OverviewStateTests
{
    [Fact]
    public void Missing_values_give_the_default_view()
    {
        var state = OverviewState.FromQuery(null, null, null, null);

        state.ShouldBe(OverviewState.Default);
        state.Sorts.ShouldBe([new SortKey(OverviewSortField.WatchedOn, true)]);
        state.Page.ShouldBe(1);
        state.PageSize.ShouldBe(50);
        state.ToQueryString().ShouldBe("");
    }

    [Theory]
    [InlineData("title", "Title")]
    [InlineData("TITLE", "Title")]
    [InlineData("releaseYear", "ReleaseYear")]
    [InlineData("runtimeMinutes", "RuntimeMinutes")]
    [InlineData("overallScore", "OverallScore")]
    [InlineData("bogus", "WatchedOn")]
    [InlineData("", "WatchedOn")]
    public void Sort_names_are_case_insensitive_and_fall_back_to_the_default(string sort, string expected) =>
        OverviewState.FromQuery(null, sort, null, null).PrimarySort.Field.ToString().ShouldBe(expected);

    [Theory]
    [InlineData("title", false)]
    [InlineData("watchedOn", true)]
    [InlineData("title:desc", true)]
    [InlineData("watchedOn:asc", false)]
    [InlineData("title:sideways", false)]
    public void The_direction_defaults_per_column(string sort, bool descending) =>
        OverviewState.FromQuery(null, sort, null, null).PrimarySort.Descending.ShouldBe(descending);

    [Fact]
    public void Multiple_sort_columns_are_parsed_in_order_ignoring_unknown_and_duplicate_items()
    {
        var state = OverviewState.FromQuery(null, "genre,bogus,title:desc,GENRE:desc,,releaseYear", null, null);

        state.Sorts.ShouldBe([
            new SortKey(OverviewSortField.Genre, false),
            new SortKey(OverviewSortField.Title, true),
            new SortKey(OverviewSortField.ReleaseYear, false),
        ]);
    }

    [Fact]
    public void Multiple_sort_columns_are_written_with_only_the_non_default_directions()
    {
        var state = new OverviewState(
            "",
            [new SortKey(OverviewSortField.Genre, false), new SortKey(OverviewSortField.WatchedOn, false)],
            1,
            50
        );

        state.ToQueryString().ShouldBe("?sort=genre,watchedOn:asc");
    }

    [Fact]
    public void A_plain_header_click_sorts_on_that_column_alone_and_then_reverses_it()
    {
        var state = OverviewState.Default with { Page = 4 };

        var title = state.WithHeaderClick(OverviewSortField.Title, additive: false);
        var reversed = title.WithHeaderClick(OverviewSortField.Title, additive: false);

        title.Sorts.ShouldBe([new SortKey(OverviewSortField.Title, false)]);
        title.Page.ShouldBe(1);
        reversed.Sorts.ShouldBe([new SortKey(OverviewSortField.Title, true)]);
    }

    [Fact]
    public void A_plain_click_on_a_column_of_a_multi_sort_replaces_the_whole_sort()
    {
        var state = OverviewState.Default.WithHeaderClick(OverviewSortField.Genre, additive: true);

        state
            .WithHeaderClick(OverviewSortField.Genre, additive: false)
            .Sorts.ShouldBe([new SortKey(OverviewSortField.Genre, false)]);
    }

    [Fact]
    public void A_ctrl_click_adds_reverses_and_removes_a_column()
    {
        var added = OverviewState.Default.WithHeaderClick(OverviewSortField.Title, additive: true);
        var reversed = added.WithHeaderClick(OverviewSortField.Title, additive: true);
        var removed = reversed.WithHeaderClick(OverviewSortField.Title, additive: true);

        added.Sorts.ShouldBe([
            new SortKey(OverviewSortField.WatchedOn, true),
            new SortKey(OverviewSortField.Title, false),
        ]);
        reversed.Sorts.ShouldBe([
            new SortKey(OverviewSortField.WatchedOn, true),
            new SortKey(OverviewSortField.Title, true),
        ]);
        removed.Sorts.ShouldBe([new SortKey(OverviewSortField.WatchedOn, true)]);
    }

    [Fact]
    public void Removing_the_last_sort_column_goes_back_to_the_default_sort()
    {
        var state = OverviewState
            .Default.WithHeaderClick(OverviewSortField.Title, additive: false)
            .WithHeaderClick(OverviewSortField.Title, additive: true)
            .WithHeaderClick(OverviewSortField.Title, additive: true);

        state.Sorts.ShouldBe(OverviewState.Default.Sorts);
    }

    [Fact]
    public void The_mobile_controls_sort_on_a_single_column()
    {
        var multi = OverviewState.Default.WithHeaderClick(OverviewSortField.Title, additive: true);

        multi.SortedOn(OverviewSortField.Genre).Sorts.ShouldBe([new SortKey(OverviewSortField.Genre, false)]);
        multi
            .WithPrimaryDirectionReversed()
            .Sorts.ShouldBe([
                new SortKey(OverviewSortField.WatchedOn, false),
                new SortKey(OverviewSortField.Title, false),
            ]);
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData("0", 1)]
    [InlineData("-2", 1)]
    [InlineData("x", 1)]
    [InlineData(null, 1)]
    [InlineData("99999999999", 1)]
    public void Invalid_pages_fall_back_to_the_first_page(string? page, int expected) =>
        OverviewState.FromQuery(null, null, page, null).Page.ShouldBe(expected);

    [Theory]
    [InlineData("25", 25)]
    [InlineData("100", 100)]
    [InlineData("30", 50)]
    [InlineData("abc", 50)]
    public void Only_the_allowed_page_sizes_are_accepted(string size, int expected) =>
        OverviewState.FromQuery(null, null, null, size).PageSize.ShouldBe(expected);

    [Fact]
    public void Search_is_trimmed_and_capped()
    {
        OverviewState.FromQuery("  léon  ", null, null, null).Search.ShouldBe("léon");
        OverviewState.FromQuery(new string('a', 250), null, null, null).Search.Length.ShouldBe(100);
    }

    [Fact]
    public void The_query_string_leaves_out_the_defaults()
    {
        var state = new OverviewState("Amélie & co", [new SortKey(OverviewSortField.Title, false)], 3, 25);

        state.ToQueryString().ShouldBe("?q=Am%C3%A9lie%20%26%20co&sort=title&page=3&size=25");
    }

    [Fact]
    public void The_direction_is_written_only_when_it_differs_from_the_column_default()
    {
        new OverviewState("", [new SortKey(OverviewSortField.Title, true)], 1, 50)
            .ToQueryString()
            .ShouldBe("?sort=title:desc");
        new OverviewState("", [new SortKey(OverviewSortField.WatchedOn, false)], 1, 50)
            .ToQueryString()
            .ShouldBe("?sort=watchedOn:asc");
    }

    [Fact]
    public void A_state_survives_a_round_trip_through_the_query_string()
    {
        var state = new OverviewState(
            "100%_\\",
            [new SortKey(OverviewSortField.OverallScore, true), new SortKey(OverviewSortField.Title, false)],
            7,
            100
        );
        var query = System.Web.HttpUtility.ParseQueryString(state.ToQueryString());

        OverviewState.FromQuery(query["q"], query["sort"], query["page"], query["size"]).ShouldBe(state);
    }
}
