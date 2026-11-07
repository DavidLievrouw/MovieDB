using Microsoft.EntityFrameworkCore;
using MovieDB.Infrastructure;

namespace MovieDB.Movies;

/// <summary>
/// The closed list of genres, loaded once for the lifetime of the process. The list is seeded by the migrations and
/// only changes with a new migration (and a restart), so it needs no invalidation. This is the only cache of
/// database data in the application.
/// </summary>
internal sealed class GenreCatalog
{
    private readonly IDbContextFactory<MovieDbContext> _factory;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private volatile IReadOnlyList<GenreOption>? _genres;

    /// <summary>Initializes a new instance of the <see cref="GenreCatalog"/> class.</summary>
    /// <param name="factory">Creates the database contexts.</param>
    public GenreCatalog(IDbContextFactory<MovieDbContext> factory)
    {
        _factory = factory;
    }

    /// <summary>Lists all genres, alphabetically (by the column collation).</summary>
    /// <param name="cancellationToken">Cancels the load, when the catalog is not loaded yet.</param>
    /// <returns>The genre ids and names.</returns>
    public async Task<IReadOnlyList<GenreOption>> ListAsync(CancellationToken cancellationToken)
    {
        var loaded = _genres;
        if (loaded is not null)
        {
            return loaded;
        }

        await _loadLock.WaitAsync(cancellationToken);
        try
        {
            // A failed load throws before the assignment, so the next call tries again.
            return _genres ??= await LoadAsync(cancellationToken);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task<IReadOnlyList<GenreOption>> LoadAsync(CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        return await db
            .Genres.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new GenreOption(g.Id.Value, g.Name))
            .ToListAsync(cancellationToken);
    }
}
