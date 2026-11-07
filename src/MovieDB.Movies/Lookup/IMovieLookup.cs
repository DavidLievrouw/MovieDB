using MovieDB.Common;

namespace MovieDB.Movies.Lookup;

/// <summary>
/// Searches an online movie database. The interface is provider-neutral: no provider type leaks through it, so the
/// provider can be swapped later.
/// </summary>
internal interface IMovieLookup
{
    /// <summary>Gets a value that tells whether the lookup is set up. When <c>false</c>, both methods fail without any network access.</summary>
    bool IsConfigured { get; }

    /// <summary>Searches movies by a fuzzy title.</summary>
    /// <param name="query">The title and optional release year.</param>
    /// <param name="cancellationToken">Cancels the request; the cancellation is not reported as an error.</param>
    /// <returns>The hits in relevance order (an empty list when nothing matches), or the failure.</returns>
    Task<Result<IReadOnlyList<LookupHit>>> SearchAsync(LookupQuery query, CancellationToken cancellationToken);

    /// <summary>Gets the details of one movie.</summary>
    /// <param name="id">The provider's numeric id of the movie.</param>
    /// <param name="cancellationToken">Cancels the request; the cancellation is not reported as an error.</param>
    /// <returns>The details, or the failure (<see cref="ErrorKind.NotFound"/> when the movie does not exist).</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is not positive.</exception>
    Task<Result<LookupDetails>> GetDetailsAsync(int id, CancellationToken cancellationToken);
}
