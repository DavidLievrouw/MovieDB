using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using MovieDB.Common;
using MovieDB.Infrastructure;
using MovieDB.Infrastructure.Entities;

namespace MovieDB.AccessImport;

internal sealed record LoadResult(DataStats Database, IReadOnlyList<string> Problems);

/// <summary>Writes a plan to the database in a single transaction and verifies it by reading the figures back.</summary>
internal sealed class ImportLoader
{
    private readonly IDbContextFactory<MovieDbContext> _factory;
    private readonly TimeProvider _clock;

    public ImportLoader(IDbContextFactory<MovieDbContext> factory, TimeProvider clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public async Task<LoadResult> LoadAsync(ImportPlan plan, bool reset, CancellationToken cancellationToken)
    {
        if (plan.HasErrors)
        {
            throw new ImportException("The plan contains errors and cannot be loaded.");
        }

        await using var context = await _factory.CreateDbContextAsync(cancellationToken);
        await context.Database.OpenConnectionAsync(cancellationToken);
        var connection = context.Database.GetDbConnection();

        await VerifySeededGenresAsync(connection, cancellationToken);
        var existing = await CountAllAsync(connection, cancellationToken);
        if (existing > 0 && !reset)
        {
            throw new ImportException(
                "The movies, movie_genres, movie_directors, directors or media tables already contain data. Use --reset to clear them first."
            );
        }

        if (reset)
        {
            await ResetAsync(connection, cancellationToken);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            var genreIds = MovieGenres.All.ToDictionary(g => g.Name, g => GenreId.From(g.Id), StringComparer.Ordinal);
            var media = plan.Media.ToDictionary(n => n, n => new Medium { Name = n, CreatedAt = now });
            context.Media.AddRange(media.Values);
            var directors = plan.Directors.ToDictionary(
                n => n,
                n => new Director { Name = n, CreatedAt = now },
                StringComparer.Ordinal
            );
            context.Directors.AddRange(directors.Values);
            await context.SaveChangesAsync(cancellationToken);

            context.Movies.AddRange(
                plan.Movies.Select(m => new Movie
                {
                    Id = MovieId.From(m.Id),
                    Title = m.Title,
                    ReleaseYear = m.ReleaseYear,
                    DirectorsKey = DirectorsKey.Of(m.Directors.Select(d => directors[d].Id)),
                    DirectorLinks =
                    [
                        .. m.Directors.Select(
                            (d, index) => new MovieDirector { DirectorId = directors[d].Id, Position = index + 1 }
                        ),
                    ],
                    GenreLinks = [new MovieGenre { GenreId = genreIds[m.Genre] }],
                    RuntimeMinutes = m.RuntimeMinutes,
                    WatchedOn = m.WatchedOn,
                    MediumId = m.Medium is null ? null : media[m.Medium].Id,
                    OverallScore = m.OverallScore,
                    StoryScore = m.StoryScore,
                    PictureScore = m.PictureScore,
                    MusicScore = m.MusicScore,
                    SoundScore = m.SoundScore,
                    Review = m.Review,
                    CreatedAt = now,
                    UpdatedAt = now,
                    Version = 1,
                })
            );
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        context.ChangeTracker.Clear();
        return await VerifyAsync(connection, plan, cancellationToken);
    }

    /// <summary>The tables the import fills. <c>genres</c> is not one of them: it is seeded by the migration.</summary>
    private static readonly string[] DomainTables = ["movie_directors", "movie_genres", "movies", "directors", "media"];

    /// <summary>Fails unless the genres table holds exactly the seeded list, with the ids the URLs rely on.</summary>
    /// <param name="connection">An open connection to the target database.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <exception cref="ImportException">The genres table differs from the seeded list.</exception>
    internal static async Task VerifySeededGenresAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var actual = new List<(int Id, string Name)>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT `id`, `name` FROM `genres` ORDER BY `id`";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                actual.Add((Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture), reader.GetString(1)));
            }
        }

        if (!actual.SequenceEqual(MovieGenres.All))
        {
            throw new ImportException(
                "The genres table does not hold exactly the seeded genres. Run the migrations on a fresh database."
            );
        }
    }

    private static async Task<int> CountAllAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var total = 0;
        foreach (var table in DomainTables)
        {
            total += Convert.ToInt32(
                await ScalarAsync(connection, $"SELECT COUNT(*) FROM `{table}`", cancellationToken),
                CultureInfo.InvariantCulture
            );
        }

        return total;
    }

    /// <summary>Clears only the domain tables (never the seeded genres). Users, data-protection keys and throttling data are untouched.</summary>
    /// <param name="connection">An open connection to the target database.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    private static async Task ResetAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, "SET FOREIGN_KEY_CHECKS=0", cancellationToken);
        try
        {
            foreach (var table in DomainTables)
            {
                await ExecuteAsync(connection, $"TRUNCATE TABLE `{table}`", cancellationToken);
            }
        }
        finally
        {
            await ExecuteAsync(connection, "SET FOREIGN_KEY_CHECKS=1", cancellationToken);
        }
    }

    private static async Task<LoadResult> VerifyAsync(
        DbConnection connection,
        ImportPlan plan,
        CancellationToken cancellationToken
    )
    {
        var problems = new List<string>();
        var stats = await ReadStatsAsync(connection, cancellationToken);
        if (stats != plan.Stats)
        {
            problems.Add("The figures read back from the database differ from the plan.");
        }

        var ids = new HashSet<int>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT `id` FROM `movies`";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture));
            }
        }

        var missing = plan.Movies.Count(m => !ids.Contains(m.Id));
        if (missing > 0 || ids.Count != plan.Movies.Count)
        {
            problems.Add($"{missing} planned movie id(s) are missing from the database.");
        }

        var next = Convert.ToInt64(
            await ScalarAsync(
                connection,
                "SELECT `AUTO_INCREMENT` FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'movies'",
                cancellationToken
            ),
            CultureInfo.InvariantCulture
        );
        if (next <= stats.MaxId)
        {
            problems.Add("The movies AUTO_INCREMENT counter is not past the highest imported id.");
        }

        return new LoadResult(stats, problems);
    }

    internal static async Task<DataStats> ReadStatsAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*),
                   COALESCE(SUM(`overall_score` IS NULL), 0), COALESCE(SUM(`story_score` IS NULL), 0),
                   COALESCE(SUM(`picture_score` IS NULL), 0), COALESCE(SUM(`music_score` IS NULL), 0),
                   COALESCE(SUM(`sound_score` IS NULL), 0),
                   COALESCE(SUM(`medium_id` IS NULL), 0), COALESCE(SUM(`review` IS NULL), 0),
                   COALESCE(MAX(CHAR_LENGTH(`title`)), 0), (SELECT COALESCE(MAX(CHAR_LENGTH(`name`)), 0) FROM `directors`),
                   COALESCE(MAX(CHAR_LENGTH(`review`)), 0),
                   DATE_FORMAT(MIN(`watched_on`), '%Y-%m-%d'), DATE_FORMAT(MAX(`watched_on`), '%Y-%m-%d'),
                   COALESCE(MAX(`id`), 0),
                   (SELECT COUNT(*) FROM `genres`), (SELECT COUNT(*) FROM `media`),
                   (SELECT COUNT(*) FROM `movie_genres`),
                   (SELECT COUNT(*) FROM `directors`), (SELECT COUNT(*) FROM `movie_directors`)
            FROM `movies`
            """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        int Int(int i) => Convert.ToInt32(reader.GetValue(i), CultureInfo.InvariantCulture);
        DateOnly? Date(int i) =>
            reader.IsDBNull(i)
                ? null
                : DateOnly.ParseExact(reader.GetString(i), "yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new DataStats(
            Movies: Int(0),
            Genres: Int(14),
            GenreLinks: Int(16),
            Media: Int(15),
            Directors: Int(17),
            DirectorLinks: Int(18),
            NullOverall: Int(1),
            NullStory: Int(2),
            NullPicture: Int(3),
            NullMusic: Int(4),
            NullSound: Int(5),
            NullMedium: Int(6),
            NullReview: Int(7),
            MaxTitleLength: Int(8),
            MaxDirectorLength: Int(9),
            MaxReviewLength: Int(10),
            MinWatchedOn: Date(11),
            MaxWatchedOn: Date(12),
            MaxId: Int(13)
        );
    }

    internal static async Task<object?> ScalarAsync(
        DbConnection connection,
        string sql,
        CancellationToken cancellationToken
    )
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    internal static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
