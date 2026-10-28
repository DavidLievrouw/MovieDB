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

        var existing = await CountAllAsync(connection, cancellationToken);
        if (existing > 0 && !reset)
        {
            throw new ImportException(
                "The movies, genres or media tables already contain data. Use --reset to clear them first."
            );
        }

        if (reset)
        {
            await ResetAsync(connection, cancellationToken);
        }

        var now = _clock.GetUtcNow().UtcDateTime;
        await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken))
        {
            var genres = plan.Genres.ToDictionary(n => n, n => new Genre { Name = n, CreatedAt = now });
            var media = plan.Media.ToDictionary(n => n, n => new Medium { Name = n, CreatedAt = now });
            context.Genres.AddRange(genres.Values);
            context.Media.AddRange(media.Values);
            await context.SaveChangesAsync(cancellationToken);

            context.Movies.AddRange(
                plan.Movies.Select(m => new Movie
                {
                    Id = MovieId.From(m.Id),
                    Title = m.Title,
                    ReleaseYear = m.ReleaseYear,
                    Director = m.Director,
                    GenreId = genres[m.Genre].Id,
                    RuntimeMinutes = m.RuntimeMinutes,
                    WatchedOn = m.WatchedOn,
                    MediumId = m.Medium is null ? null : media[m.Medium].Id,
                    OverallScore = m.OverallScore,
                    StoryScore = m.StoryScore,
                    PictureScore = m.PictureScore,
                    MusicScore = m.MusicScore,
                    SoundScore = m.SoundScore,
                    ExtrasScore = m.ExtrasScore,
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

    private static async Task<int> CountAllAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var total = 0;
        foreach (var table in new[] { "movies", "genres", "media" })
        {
            total += Convert.ToInt32(
                await ScalarAsync(connection, $"SELECT COUNT(*) FROM `{table}`", cancellationToken),
                CultureInfo.InvariantCulture
            );
        }

        return total;
    }

    /// <summary>Clears only the three domain tables. Users, data-protection keys and throttling data are untouched.</summary>
    private static async Task ResetAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, "SET FOREIGN_KEY_CHECKS=0", cancellationToken);
        try
        {
            foreach (var table in new[] { "movies", "genres", "media" })
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
                   COALESCE(SUM(`sound_score` IS NULL), 0), COALESCE(SUM(`extras_score` IS NULL), 0),
                   COALESCE(SUM(`medium_id` IS NULL), 0), COALESCE(SUM(`review` IS NULL), 0),
                   COALESCE(MAX(CHAR_LENGTH(`title`)), 0), COALESCE(MAX(CHAR_LENGTH(`director`)), 0),
                   COALESCE(MAX(CHAR_LENGTH(`review`)), 0),
                   DATE_FORMAT(MIN(`watched_on`), '%Y-%m-%d'), DATE_FORMAT(MAX(`watched_on`), '%Y-%m-%d'),
                   COALESCE(MAX(`id`), 0),
                   (SELECT COUNT(*) FROM `genres`), (SELECT COUNT(*) FROM `media`)
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
            Genres: Int(15),
            Media: Int(16),
            NullOverall: Int(1),
            NullStory: Int(2),
            NullPicture: Int(3),
            NullMusic: Int(4),
            NullSound: Int(5),
            NullExtras: Int(6),
            NullMedium: Int(7),
            NullReview: Int(8),
            MaxTitleLength: Int(9),
            MaxDirectorLength: Int(10),
            MaxReviewLength: Int(11),
            MinWatchedOn: Date(12),
            MaxWatchedOn: Date(13),
            MaxId: Int(14)
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
