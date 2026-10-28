using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MovieDB.AccessImport;

internal sealed record TableFingerprint(string Table, int Rows, string Hash)
{
    public override string ToString() => $"{Table} rows={Rows} sha256={Hash}";
}

/// <summary>Content hash per table (ordered by id), so a restored dump can be compared with its source without printing rows.</summary>
internal static class Fingerprint
{
    /// <summary>Table identifiers cannot be parameters, so each table has its own fixed statement.</summary>
    private static readonly (string Table, string Sql)[] Queries =
    [
        ("genres", "SELECT * FROM `genres` ORDER BY `id`"),
        ("media", "SELECT * FROM `media` ORDER BY `id`"),
        ("movies", "SELECT * FROM `movies` ORDER BY `id`"),
    ];

    public static async Task<IReadOnlyList<TableFingerprint>> ComputeAsync(
        DbConnection connection,
        CancellationToken cancellationToken
    )
    {
        var result = new List<TableFingerprint>();
        foreach (var (table, sql) in Queries)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var rows = 0;
            while (await reader.ReadAsync(cancellationToken))
            {
                rows++;
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    hash.AppendData(Encoding.UTF8.GetBytes(Format(reader.GetValue(i))));
                    hash.AppendData([0x1F]);
                }

                hash.AppendData([0x1E]);
            }

            result.Add(new TableFingerprint(table, rows, Convert.ToHexString(hash.GetHashAndReset())));
        }

        return result;
    }

    private static string Format(object value) =>
        value switch
        {
            DBNull => "\0NULL",
            DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
            byte[] bytes => Convert.ToHexString(bytes),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
        };
}

internal static class DumpComposer
{
    /// <summary>
    /// Wraps a <c>mariadb-dump</c> data body into a guarded script: it aborts unless the schema is at the expected
    /// migration and the three domain tables are empty, and loads everything in one transaction.
    /// </summary>
    /// <param name="body">The <c>mariadb-dump</c> data statements.</param>
    /// <param name="sourceName">The file name of the source, for the header.</param>
    /// <param name="createdAt">When the dump was made.</param>
    /// <param name="migrationId">The latest migration the schema must be at.</param>
    /// <param name="stats">The row counts, for the header.</param>
    public static string Compose(
        string body,
        string sourceName,
        DateTimeOffset createdAt,
        string migrationId,
        DataStats stats
    )
    {
        var created = createdAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var header = string.Create(
            CultureInfo.InvariantCulture,
            $"""
            -- MovieDB data dump (contains personal reviews: never commit, delete after deployment)
            -- Source: {sourceName}
            -- Created: {created} UTC
            -- Schema (latest migration): {migrationId}
            -- Rows: movies={stats.Movies}, genres={stats.Genres}, media={stats.Media}
            SET NAMES utf8mb4;
            DELIMITER //
            BEGIN NOT ATOMIC
              DECLARE latest VARCHAR(150);
              SELECT MAX(`MigrationId`) INTO latest FROM `__EFMigrationsHistory`;
              IF latest IS NULL OR latest <> '{migrationId}' THEN
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Refusing to load: the database schema is not at the migration this dump was made for.';
              END IF;
              IF (SELECT COUNT(*) FROM `movies`) > 0 OR (SELECT COUNT(*) FROM `genres`) > 0 OR (SELECT COUNT(*) FROM `media`) > 0 THEN
                SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'Refusing to load: movies, genres or media already contain data.';
              END IF;
            END //
            DELIMITER ;
            START TRANSACTION;

            """
        );
        return header + body.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\nCOMMIT;\n";
    }
}
