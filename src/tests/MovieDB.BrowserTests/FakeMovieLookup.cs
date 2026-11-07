using MovieDB.Common;
using MovieDB.Movies.Lookup;

namespace MovieDB.BrowserTests;

/// <summary>A scripted stand-in for the online movie database, so that no test reaches the real service.</summary>
internal sealed class FakeMovieLookup : IMovieLookup
{
    private readonly Lock _lock = new();
    private readonly List<LookupQuery> _queries = [];

    /// <summary>Gets or sets whether the lookup counts as configured.</summary>
    public bool IsConfigured { get; set; } = true;

    /// <summary>Gets or sets the answer to a search.</summary>
    public Func<LookupQuery, Result<IReadOnlyList<LookupHit>>> Search { get; set; } =
        _ => Result<IReadOnlyList<LookupHit>>.Ok([]);

    /// <summary>Gets or sets the answer to a details request, by hit id.</summary>
    public Func<int, Result<LookupDetails>> Details { get; set; } = _ => new Error(ErrorKind.NotFound, "not scripted");

    /// <summary>Gets the searches that were made since the last <see cref="Reset"/>.</summary>
    public IReadOnlyList<LookupQuery> Queries
    {
        get
        {
            lock (_lock)
            {
                return [.. _queries];
            }
        }
    }

    /// <summary>Restores the default (configured, no results) behavior; tests share the one host.</summary>
    public void Reset()
    {
        IsConfigured = true;
        Search = _ => Result<IReadOnlyList<LookupHit>>.Ok([]);
        Details = _ => new Error(ErrorKind.NotFound, "not scripted");
        lock (_lock)
        {
            _queries.Clear();
        }
    }

    public Task<Result<IReadOnlyList<LookupHit>>> SearchAsync(LookupQuery query, CancellationToken cancellationToken)
    {
        lock (_lock)
        {
            _queries.Add(query);
        }

        return Task.FromResult(Search(query));
    }

    public Task<Result<LookupDetails>> GetDetailsAsync(int id, CancellationToken cancellationToken) =>
        Task.FromResult(Details(id));
}
