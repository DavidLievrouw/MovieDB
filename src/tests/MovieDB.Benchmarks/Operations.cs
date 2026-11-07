using MovieDB.Common;
using MovieDB.Movies;
using MySqlConnector;

namespace MovieDB.Benchmarks;

/// <summary>
/// The operations that are benchmarked and whose round trips are counted (PLAN-4 §5.3). Each runs the real query or
/// command class against the seeded database. The write operations leave the data as it was after <see cref="ResetWrites"/>.
/// </summary>
internal sealed class Operations
{
    private const int PageSize = OverviewState.DefaultPageSize;

    private static readonly (string Name, Func<Operations, Task> Run)[] Definitions =
    [
        ("Overview: default sort", o => o.Overview(Sorted(OverviewSortField.WatchedOn, descending: true))),
        ("Overview: sort on title", o => o.Overview(Sorted(OverviewSortField.Title, descending: false))),
        ("Overview: sort on director", o => o.Overview(Sorted(OverviewSortField.Director, descending: false))),
        ("Overview: sort on genre", o => o.Overview(Sorted(OverviewSortField.Genre, descending: false))),
        ("Overview: title search", o => o.Overview(OverviewState.Default with { Search = o.Sample.SearchTerm })),
        ("Overview: one genre", o => o.Overview(OverviewState.Default with { Genres = [o.Sample.TopGenres[0]] })),
        (
            "Overview: two genres",
            o => o.Overview(OverviewState.Default with { Genres = [.. o.Sample.TopGenres.Order()] })
        ),
        ("Overview: last page", o => o.Overview(OverviewState.Default with { Page = o.LastPage })),
        ("Navigation: default", o => o.Navigate(OverviewState.Default)),
        ("Navigation: with search", o => o.Navigate(OverviewState.Default with { Search = o.Sample.SearchTerm })),
        ("Navigation: with genre", o => o.Navigate(OverviewState.Default with { Genres = [o.Sample.TopGenres[0]] })),
        ("Statistics", o => o._queries.GetStatisticsAsync(o._database.Clock.Today, default)),
        ("Detail (FindAsync)", o => o._queries.FindAsync(MovieId.From(o.Sample.MovieId), default)),
        ("Edit load (FindForEditAsync)", o => o._queries.FindForEditAsync(MovieId.From(o.Sample.MovieId), default)),
        ("Duplicate check", o => o.DuplicateCheck()),
        ("Same title and year", o => o.SameTitleAndYear()),
        ("Suggest directors", o => o._queries.SuggestDirectorsAsync("Jan", 10, default)),
        ("Suggest media", o => o._queries.SuggestMediaAsync("D", 10, default)),
        ("List genres", o => o._database.Genres.ListAsync(default)),
        ("Save new movie", o => o.SaveNew()),
        ("Save existing movie", o => o.SaveExisting()),
    ];

    private readonly SeededDatabase _database;
    private readonly MovieQueries _queries;
    private readonly MovieCommands _commands;
    private int _newMovieNumber;

    public Operations(SeededDatabase database)
    {
        _database = database;
        _queries = database.Queries;
        _commands = database.Commands;
    }

    /// <summary>The names of the operations, in report order.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Definitions.Select(d => d.Name)];

    private SampleData Sample => _database.Sample;

    private int LastPage => (Sample.MovieCount + PageSize - 1) / PageSize;

    /// <summary>Runs an operation once.</summary>
    /// <param name="name">The name of the operation, one of <see cref="Names"/>.</param>
    public Task Run(string name) => Definitions.Single(d => d.Name == name).Run(this);

    /// <summary>Removes what the write operations added, so every iteration starts from the same data.</summary>
    public async Task ResetWrites()
    {
        var sample = Sample;
        await using var connection = new MySqlConnection(_database.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM movie_genres WHERE movie_id > @movie;
            DELETE FROM movie_directors WHERE movie_id > @movie;
            DELETE FROM movies WHERE id > @movie;
            DELETE FROM directors WHERE id > @director;
            DELETE FROM media WHERE id > @medium;
            """;
        command.Parameters.AddWithValue("@movie", sample.MaxMovieId);
        command.Parameters.AddWithValue("@director", sample.MaxDirectorId);
        command.Parameters.AddWithValue("@medium", sample.MaxMediumId);
        await command.ExecuteNonQueryAsync();
    }

    private static OverviewState Sorted(OverviewSortField field, bool descending) =>
        new("", [new SortKey(field, descending)], 1, PageSize);

    private Task Overview(OverviewState state) => _queries.ListOverviewAsync(state, default);

    private Task Navigate(OverviewState state) =>
        _queries.GetNavigationAsync(MovieId.From(Sample.MiddleMovieId), state, default);

    private Task DuplicateCheck() =>
        _queries.FindDuplicateAsync(Sample.Title, Sample.Year, [.. Sample.Directors], null, default);

    private Task SameTitleAndYear() =>
        _queries.FindSameTitleAndYearAsync(Sample.Title, Sample.Year, [.. Sample.Directors], null, 5, default);

    private async Task SaveNew()
    {
        var number = Interlocked.Increment(ref _newMovieNumber);
        var input = NewInput("Benchmark film " + number, [Sample.Directors[0], "Benchmark regisseur " + number]);
        _ = (await _commands.SaveAsync(null, 0, input, default)).Value;
    }

    private async Task SaveExisting()
    {
        var sample = Sample;
        var input = NewInput(sample.Title, [.. sample.Directors]);
        input.ReleaseYear = sample.Year;
        var saved = (
            await _commands.SaveAsync(MovieId.From(sample.MovieId), _database.UpdateVersion, input, default)
        ).Value;
        _database.UpdateVersion = saved.Version;
    }

    private MovieInput NewInput(string title, List<string> directors) =>
        new()
        {
            Title = title,
            Genres = [.. Sample.TopGenreNames],
            RuntimeMinutes = 100,
            ReleaseYear = 1999,
            Directors = directors,
            WatchedOn = _database.Clock.Today,
            Medium = "DVD",
            Review = "Benchmark review.",
            OverallScore = 7,
            StoryScore = 7,
            PictureScore = 7,
            MusicScore = 7,
            SoundScore = 7,
        };
}
