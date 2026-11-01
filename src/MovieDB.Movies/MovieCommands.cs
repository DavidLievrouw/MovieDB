using System.Data;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using MovieDB.Common;
using MovieDB.Infrastructure;
using MovieDB.Infrastructure.Entities;

namespace MovieDB.Movies;

/// <summary>The outcome of a successful save.</summary>
/// <param name="Id">The id of the saved movie.</param>
/// <param name="Version">The new version of the movie.</param>
internal sealed record SavedMovie(MovieId Id, int Version);

/// <summary>Write operations of the movies feature.</summary>
internal sealed class MovieCommands
{
    private readonly IDbContextFactory<MovieDbContext> _factory;
    private readonly IValidator<MovieInput> _validator;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates the commands.</summary>
    /// <param name="factory">The context factory.</param>
    /// <param name="validator">The validator of the input.</param>
    /// <param name="timeProvider">The source of the current UTC time.</param>
    public MovieCommands(
        IDbContextFactory<MovieDbContext> factory,
        IValidator<MovieInput> validator,
        TimeProvider timeProvider
    )
    {
        _factory = factory;
        _validator = validator;
        _timeProvider = timeProvider;
    }

    /// <summary>Validates the input and returns the errors per property name.</summary>
    /// <param name="input">The (normalized) input.</param>
    /// <returns>The messages per property name; empty when the input is valid.</returns>
    public IReadOnlyDictionary<string, string[]> Validate(MovieInput input) =>
        _validator
            .Validate(input)
            .Errors.GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

    /// <summary>Creates a movie, or updates an existing one when its version still matches.</summary>
    /// <param name="id">The id of the movie to update, or <c>null</c> to create a movie.</param>
    /// <param name="expectedVersion">The version the edit was based on (ignored when creating).</param>
    /// <param name="input">The values to save.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>
    /// The saved movie, or a validation error (per property), a not-found error, or a conflict error when the movie
    /// was changed by someone else.
    /// </returns>
    public async Task<Result<SavedMovie>> SaveAsync(
        MovieId? id,
        int expectedVersion,
        MovieInput input,
        CancellationToken cancellationToken
    )
    {
        var values = input.Normalized();
        var errors = Validate(values);
        if (errors.Count > 0)
        {
            return Error.Validation(errors);
        }

        // Read committed, so a medium that a racing transaction has just committed is visible on re-read.
        return await _factory.InTransactionAsync(
            (db, ct) => SaveInTransactionAsync(db, id, expectedVersion, values, ct),
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
    }

    private async Task<Result<SavedMovie>> SaveInTransactionAsync(
        MovieDbContext db,
        MovieId? id,
        int expectedVersion,
        MovieInput input,
        CancellationToken cancellationToken
    )
    {
        var title = input.Title!;
        var year = input.ReleaseYear!.Value;
        var genreIds = await ResolveGenresAsync(db, input.Genres, cancellationToken);
        if (genreIds.Error is { } genreError)
        {
            return genreError;
        }

        var directors = await ResolveDirectorsAsync(
            db,
            input.Directors,
            id,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken
        );
        if (directors.Error is { } directorError)
        {
            return directorError;
        }

        var directorsKey = DirectorsKey.Of(directors.Ids);
        var duplicates = db.Movies.Where(m =>
            m.Title == title && m.ReleaseYear == year && m.DirectorsKey == directorsKey
        );
        if (id is { } ownId)
        {
            duplicates = duplicates.Where(m => m.Id != ownId);
        }

        if (await duplicates.AnyAsync(cancellationToken))
        {
            return DuplicateError();
        }

        Movie movie;
        List<DirectorId> previousDirectorIds = [];
        if (id is { } existingId)
        {
            var found = await db.Movies.SingleOrDefaultAsync(m => m.Id == existingId, cancellationToken);
            if (found is null)
            {
                return Error.NotFound(MovieMessages.NotFound);
            }

            if (found.Version != expectedVersion)
            {
                return Error.Conflict(MovieMessages.Changed);
            }

            movie = found;
            previousDirectorIds = await db
                .MovieDirectorLinks.AsNoTracking()
                .Where(l => l.MovieId == existingId)
                .Select(l => l.DirectorId)
                .ToListAsync(cancellationToken);
        }
        else
        {
            movie = new Movie();
        }

        // Resolving saves new medium rows, so it must happen before the movie has pending changes.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        movie.MediumId = input.Medium is null
            ? null
            : await ResolveMediumAsync(db, input.Medium, now, cancellationToken);
        movie.Title = title;
        movie.ReleaseYear = year;
        movie.DirectorsKey = directorsKey;
        movie.RuntimeMinutes = input.RuntimeMinutes!.Value;
        movie.WatchedOn = input.WatchedOn!.Value;
        movie.OverallScore = input.OverallScore;
        movie.StoryScore = input.StoryScore;
        movie.PictureScore = input.PictureScore;
        movie.MusicScore = input.MusicScore;
        movie.SoundScore = input.SoundScore;
        movie.Review = input.Review;
        movie.UpdatedAt = now;
        if (id is null)
        {
            movie.Version = 1;
            movie.CreatedAt = now;
            db.Movies.Add(movie);
        }
        else
        {
            movie.Version++;
            // Replace the links: delete first, so the new rows cannot collide with the old primary keys.
            await db.MovieGenreLinks.Where(l => l.MovieId == movie.Id).ExecuteDeleteAsync(cancellationToken);
            await db.MovieDirectorLinks.Where(l => l.MovieId == movie.Id).ExecuteDeleteAsync(cancellationToken);
        }

        movie.GenreLinks = [.. genreIds.Ids.Select(genreId => new MovieGenre { GenreId = genreId })];
        movie.DirectorLinks =
        [
            .. directors.Ids.Select(
                (directorId, index) => new MovieDirector { DirectorId = directorId, Position = index + 1 }
            ),
        ];

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict(MovieMessages.Changed);
        }
        catch (DbUpdateException ex) when (DbOperations.Classify(ex) == DbFailure.Duplicate)
        {
            return DuplicateError();
        }

        await DeleteOrphanDirectorsAsync(db, previousDirectorIds.Except(directors.Ids).ToList(), cancellationToken);

        return Result<SavedMovie>.Ok(new SavedMovie(movie.Id, movie.Version));
    }

    private static Error DuplicateError() => Error.Validation(nameof(MovieInput.Title), MovieMessages.Duplicate);

    /// <summary>
    /// Maps the director names onto director rows. A name that equals an existing director (by the database
    /// collation) reuses that row, so the same director is never stored twice. When only the movie being saved uses a
    /// matched director, the typed spelling replaces the stored one in place (same id). Unknown names create rows.
    /// </summary>
    /// <param name="db">The context of the current save.</param>
    /// <param name="names">The typed director names, in order.</param>
    /// <param name="ownId">The id of the movie being saved, or <c>null</c> for a new movie.</param>
    /// <param name="now">The creation time of new directors.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The director ids in the typed order, or the validation error.</returns>
    private static async Task<(IReadOnlyList<DirectorId> Ids, Error? Error)> ResolveDirectorsAsync(
        MovieDbContext db,
        IReadOnlyList<string> names,
        MovieId? ownId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var ids = new List<DirectorId>(names.Count);
        foreach (var name in names)
        {
            var director = await FindOrCreateDirectorAsync(db, name, now, cancellationToken);
            if (ids.Contains(director.Id))
            {
                return ([], Error.Validation(nameof(MovieInput.Directors), "Een regisseur komt maar één keer voor."));
            }

            ids.Add(director.Id);
            if (!string.Equals(director.Name, name, StringComparison.Ordinal))
            {
                var directorId = director.Id;
                var otherLinks = db.MovieDirectorLinks.Where(l => l.DirectorId == directorId);
                if (ownId is { } own)
                {
                    otherLinks = otherLinks.Where(l => l.MovieId != own);
                }

                if (!await otherLinks.AnyAsync(cancellationToken))
                {
                    director.Name = name;
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
        }

        return (ids, null);
    }

    private static async Task<Director> FindOrCreateDirectorAsync(
        MovieDbContext db,
        string name,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        // The collation makes the comparison case- and accent-insensitive.
        var existing = await db.Directors.SingleOrDefaultAsync(d => d.Name == name, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var director = new Director { Name = name, CreatedAt = now };
        db.Directors.Add(director);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return director;
        }
        catch (DbUpdateException ex) when (DbOperations.Classify(ex) == DbFailure.Duplicate)
        {
            db.Entry(director).State = EntityState.Detached;
            return await db.Directors.SingleAsync(d => d.Name == name, cancellationToken);
        }
    }

    /// <summary>
    /// Deletes the directors that the movie no longer has and that no other movie uses. A foreign key failure means
    /// another save has just linked the director, so it is in use and is left alone.
    /// </summary>
    /// <param name="db">The context of the current save.</param>
    /// <param name="candidates">The directors that were removed from the movie.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    private static async Task DeleteOrphanDirectorsAsync(
        MovieDbContext db,
        List<DirectorId> candidates,
        CancellationToken cancellationToken
    )
    {
        if (candidates.Count == 0)
        {
            return;
        }

        try
        {
            await db
                .Directors.Where(d =>
                    candidates.Contains(d.Id) && !db.MovieDirectorLinks.Any(l => l.DirectorId == d.Id)
                )
                .ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception ex) when (DbOperations.Classify(ex) == DbFailure.ForeignKey)
        {
            // Noop
        }
    }

    /// <summary>
    /// Maps the genre names onto the ids of the closed genre list. A name that is not in the list (compared with the
    /// database collation), or two names that resolve to the same genre, is a validation error: genres are never created.
    /// </summary>
    /// <param name="db">The context of the current save.</param>
    /// <param name="names">The typed genre names.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The distinct genre ids, or the validation error.</returns>
    private static async Task<(IReadOnlyList<GenreId> Ids, Error? Error)> ResolveGenresAsync(
        MovieDbContext db,
        IReadOnlyList<string> names,
        CancellationToken cancellationToken
    )
    {
        var ids = new List<GenreId>(names.Count);
        foreach (var name in names)
        {
            var found = await db
                .Genres.AsNoTracking()
                .Where(g => g.Name == name)
                .Select(g => (GenreId?)g.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (found is not { } genreId)
            {
                return ([], Error.Validation(nameof(MovieInput.Genres), $"Onbekend genre: {name}."));
            }

            if (ids.Contains(genreId))
            {
                return ([], Error.Validation(nameof(MovieInput.Genres), "Een genre komt maar één keer voor."));
            }

            ids.Add(genreId);
        }

        return (ids, null);
    }

    private static async Task<MediumId> ResolveMediumAsync(
        MovieDbContext db,
        string name,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var existing = await db
            .Media.AsNoTracking()
            .Where(m => m.Name == name)
            .Select(m => (MediumId?)m.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is { } id)
        {
            return id;
        }

        var medium = new Medium { Name = name, CreatedAt = now };
        db.Media.Add(medium);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return medium.Id;
        }
        catch (DbUpdateException ex) when (DbOperations.Classify(ex) == DbFailure.Duplicate)
        {
            db.Entry(medium).State = EntityState.Detached;
            return await db
                .Media.AsNoTracking()
                .Where(m => m.Name == name)
                .Select(m => m.Id)
                .SingleAsync(cancellationToken);
        }
    }
}
