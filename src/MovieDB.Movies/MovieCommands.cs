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

        // Read committed, so a genre or medium that a racing transaction has just committed is visible on re-read.
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
        var director = await ResolveDirectorAsync(db, input.Director!, id, cancellationToken);
        var duplicates = db.Movies.Where(m => m.Title == title && m.ReleaseYear == year && m.Director == director);
        if (id is { } ownId)
        {
            duplicates = duplicates.Where(m => m.Id != ownId);
        }

        if (await duplicates.AnyAsync(cancellationToken))
        {
            return DuplicateError();
        }

        Movie movie;
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
        }
        else
        {
            movie = new Movie();
        }

        // Resolving saves new lookup rows, so it must happen before the movie has pending changes.
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var genreId = await ResolveGenreAsync(db, input.Genre!, now, cancellationToken);
        var mediumId = input.Medium is null
            ? (MediumId?)null
            : await ResolveMediumAsync(db, input.Medium, now, cancellationToken);
        movie.GenreId = genreId;
        movie.MediumId = mediumId;
        movie.Title = title;
        movie.ReleaseYear = year;
        movie.Director = director;
        movie.RuntimeMinutes = input.RuntimeMinutes!.Value;
        movie.WatchedOn = input.WatchedOn!.Value;
        movie.OverallScore = input.OverallScore;
        movie.StoryScore = input.StoryScore;
        movie.PictureScore = input.PictureScore;
        movie.MusicScore = input.MusicScore;
        movie.SoundScore = input.SoundScore;
        movie.ExtrasScore = input.ExtrasScore;
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
        }

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

        return Result<SavedMovie>.Ok(new SavedMovie(movie.Id, movie.Version));
    }

    private static Error DuplicateError() => Error.Validation(nameof(MovieInput.Title), MovieMessages.Duplicate);

    /// <summary>
    /// Returns the spelling of the director that the other movies already use, so the same director is never stored
    /// twice with a different case or accents. A director that is new, or only used by this movie, keeps the typed
    /// spelling.
    /// </summary>
    /// <param name="db">The context of the current save.</param>
    /// <param name="director">The typed director.</param>
    /// <param name="ownId">The id of the movie being saved, or <c>null</c> for a new movie.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The director spelling to store.</returns>
    private static async Task<string> ResolveDirectorAsync(
        MovieDbContext db,
        string director,
        MovieId? ownId,
        CancellationToken cancellationToken
    )
    {
        // The collation makes the comparison case- and accent-insensitive.
        var others = db.Movies.AsNoTracking().Where(m => m.Director == director);
        if (ownId is { } own)
        {
            others = others.Where(m => m.Id != own);
        }

        var spellings = await others.OrderBy(m => m.Id).Select(m => m.Director).ToListAsync(cancellationToken);
        return spellings.Count == 0
            ? director
            : spellings.GroupBy(s => s, StringComparer.Ordinal).OrderByDescending(g => g.Count()).First().Key;
    }

    private static async Task<GenreId> ResolveGenreAsync(
        MovieDbContext db,
        string name,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        var existing = await db
            .Genres.AsNoTracking()
            .Where(g => g.Name == name)
            .Select(g => (GenreId?)g.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (existing is { } id)
        {
            return id;
        }

        var genre = new Genre { Name = name, CreatedAt = now };
        db.Genres.Add(genre);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return genre.Id;
        }
        catch (DbUpdateException ex) when (DbOperations.Classify(ex) == DbFailure.Duplicate)
        {
            // A concurrent save created the same genre first: use that one.
            db.Entry(genre).State = EntityState.Detached;
            return await db
                .Genres.AsNoTracking()
                .Where(g => g.Name == name)
                .Select(g => g.Id)
                .SingleAsync(cancellationToken);
        }
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
