using MovieDB.Common;
using MovieDB.Infrastructure;
using MovieDB.Movies;
using Shouldly;

namespace MovieDB.CharacteristicsTests;

[Collection(MariaDbCollection.Name)]
public sealed class MovieNavigationQueryTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly MariaDbFixture _fixture;

    public MovieNavigationQueryTests(MariaDbFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed record Seed(string Title, int Day, int? Score = null, string? Medium = null, int Year = 2000);

    private async Task<(MovieQueries Queries, MovieId[] Ids)> SeedAsync(params Seed[] seeds)
    {
        var connectionString = await _fixture.CreateMigratedDatabaseAsync();
        var factory = MariaDbFixture.CreateFactory(connectionString);
        var time = new FixedTimeProvider();
        var commands = new MovieCommands(
            factory,
            new MovieInputValidator(new UserClock(time, UserClock.ResolveZone("Europe/Brussels"))),
            time
        );
        var ids = new List<MovieId>();
        foreach (var seed in seeds)
        {
            var result = await commands.SaveAsync(
                null,
                1,
                new MovieInput
                {
                    Title = seed.Title,
                    ReleaseYear = seed.Year,
                    Director = "Director",
                    Genre = "Drama",
                    Medium = seed.Medium,
                    RuntimeMinutes = 100,
                    WatchedOn = new DateOnly(2026, 1, seed.Day),
                    OverallScore = seed.Score,
                },
                TestContext.Current.CancellationToken
            );
            ids.Add(result.Value.Id);
        }

        return (new MovieQueries(factory), [.. ids]);
    }

    private static readonly Seed[] Sample =
    [
        new("b", 1, 5, "dvd", 1999),
        new("a", 2, 5, "bioscoop", 2001),
        new("c", 3, 8, null, 2001),
        new("d", 4, null, "dvd", 1980),
        new("e", 5, null, null, 2010),
        new("f", 5, 9, "bioscoop", 2010),
    ];

    private static OverviewState State(string search, params SortKey[] sorts) => new(search, sorts, 1, 50);

    [Fact]
    public async Task The_first_movie_has_no_previous_and_the_last_has_no_next()
    {
        var (queries, _) = await SeedAsync(Sample);
        var state = OverviewState.Default;
        var ordered = (await queries.ListOverviewAsync(state, TestContext.Current.CancellationToken)).Rows;

        var first = await queries.GetNavigationAsync(ordered[0].Id, state, TestContext.Current.CancellationToken);
        var last = await queries.GetNavigationAsync(ordered[^1].Id, state, TestContext.Current.CancellationToken);

        first!.Position.ShouldBe(1);
        first.PreviousId.ShouldBeNull();
        first.NextId.ShouldBe(ordered[1].Id);
        last!.Position.ShouldBe(ordered.Count);
        last.NextId.ShouldBeNull();
        last.PreviousId.ShouldBe(ordered[^2].Id);
        last.Total.ShouldBe(6);
        last.FirstId.ShouldBe(ordered[0].Id);
        last.LastId.ShouldBe(ordered[^1].Id);
    }

    [Fact]
    public async Task A_single_movie_is_its_own_first_and_last()
    {
        var (queries, ids) = await SeedAsync(new Seed("only", 1));

        var navigation = await queries.GetNavigationAsync(
            ids[0],
            OverviewState.Default,
            TestContext.Current.CancellationToken
        );

        navigation!.Position.ShouldBe(1);
        navigation.Total.ShouldBe(1);
        navigation.PreviousId.ShouldBeNull();
        navigation.NextId.ShouldBeNull();
        navigation.FirstId.ShouldBe(ids[0]);
        navigation.LastId.ShouldBe(ids[0]);
    }

    [Fact]
    public async Task An_unknown_movie_has_no_navigation()
    {
        var (queries, _) = await SeedAsync(Sample);

        (
            await queries.GetNavigationAsync(
                MovieId.From(9999),
                OverviewState.Default,
                TestContext.Current.CancellationToken
            )
        ).ShouldBeNull();
    }

    public static TheoryData<int, bool> SingleSorts()
    {
        var data = new TheoryData<int, bool>();
        foreach (var field in Enum.GetValues<OverviewSortField>())
        {
            data.Add((int)field, true);
            data.Add((int)field, false);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(SingleSorts))]
    public async Task The_navigation_follows_the_order_of_the_overview_for_every_sort(int field, bool descending)
    {
        var (queries, _) = await SeedAsync(Sample);
        var state = State("", new SortKey((OverviewSortField)field, descending));

        await AssertConsistentAsync(queries, state);
    }

    [Fact]
    public async Task The_navigation_follows_the_overview_for_multiple_sort_columns()
    {
        var (queries, _) = await SeedAsync(Sample);
        var state = State(
            "",
            new SortKey(OverviewSortField.OverallScore, false),
            new SortKey(OverviewSortField.Medium, true),
            new SortKey(OverviewSortField.Title, false)
        );

        await AssertConsistentAsync(queries, state);
    }

    [Fact]
    public async Task The_navigation_is_limited_to_the_searched_list()
    {
        var (queries, ids) = await SeedAsync(
            new Seed("Alien", 1),
            new Seed("Aliens", 2),
            new Seed("Heat", 3),
            new Seed("Alienation", 4)
        );
        var state = State("alien", new SortKey(OverviewSortField.Title, false));

        var navigation = await queries.GetNavigationAsync(ids[1], state, TestContext.Current.CancellationToken);

        navigation!.Total.ShouldBe(3);
        navigation.Position.ShouldBe(3);
        navigation.State.Search.ShouldBe("alien");
        await AssertConsistentAsync(queries, state);
    }

    [Fact]
    public async Task The_search_is_dropped_when_it_excludes_the_movie()
    {
        var (queries, ids) = await SeedAsync(new Seed("Alien", 1), new Seed("Heat", 2));
        var state = State("alien", new SortKey(OverviewSortField.Title, false));

        var navigation = await queries.GetNavigationAsync(ids[1], state, TestContext.Current.CancellationToken);

        navigation!.Total.ShouldBe(2);
        navigation.Position.ShouldBe(2);
        navigation.State.Search.ShouldBeEmpty();
    }

    [Fact]
    public async Task Wildcards_in_the_search_are_literal()
    {
        var (queries, ids) = await SeedAsync(new Seed("100% Wolf", 1), new Seed("1000 Wolves", 2));
        var state = State("100%", new SortKey(OverviewSortField.Title, false));

        var navigation = await queries.GetNavigationAsync(ids[0], state, TestContext.Current.CancellationToken);

        navigation!.Total.ShouldBe(1);
        navigation.State.Search.ShouldBe("100%");
    }

    private static async Task AssertConsistentAsync(MovieQueries queries, OverviewState state)
    {
        var ordered = (await queries.ListOverviewAsync(state, TestContext.Current.CancellationToken)).Rows;
        for (var i = 0; i < ordered.Count; i++)
        {
            var navigation = await queries.GetNavigationAsync(
                ordered[i].Id,
                state,
                TestContext.Current.CancellationToken
            );

            navigation.ShouldNotBeNull();
            navigation.Position.ShouldBe(i + 1);
            navigation.Total.ShouldBe(ordered.Count);
            navigation.PreviousId.ShouldBe(i == 0 ? null : ordered[i - 1].Id);
            navigation.NextId.ShouldBe(i == ordered.Count - 1 ? null : ordered[i + 1].Id);
            navigation.FirstId.ShouldBe(ordered[0].Id);
            navigation.LastId.ShouldBe(ordered[^1].Id);
        }
    }
}
