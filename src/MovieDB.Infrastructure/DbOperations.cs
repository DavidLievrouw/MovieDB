using System.Data;
using Microsoft.EntityFrameworkCore;
using MovieDB.Common;
using MySqlConnector;

namespace MovieDB.Infrastructure;

internal enum DbFailure
{
    None = 0,
    Duplicate = 1,
    ForeignKey = 2,
    Busy = 3,
    TooLong = 4,
}

/// <summary>One context + one transaction per operation, with database errors mapped to results (never a 500).</summary>
internal static class DbOperations
{
    private const int MaxAttempts = 5;

    public const string BusyMessage = "The data is being changed by someone else right now. Try again.";
    public const string DuplicateMessage = "The same item was created or changed by someone else at the same time.";
    public const string ReferencedMessage = "The item is in use or was changed by someone else at the same time.";

    public static DbFailure Classify(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is MySqlException mysql)
            {
                return mysql.ErrorCode switch
                {
                    MySqlErrorCode.DuplicateKeyEntry => DbFailure.Duplicate,
                    MySqlErrorCode.RowIsReferenced2 or MySqlErrorCode.NoReferencedRow2 => DbFailure.ForeignKey,
                    MySqlErrorCode.LockDeadlock or MySqlErrorCode.LockWaitTimeout => DbFailure.Busy,
                    MySqlErrorCode.DataTooLong => DbFailure.TooLong,
                    _ => DbFailure.None,
                };
            }
        }

        return DbFailure.None;
    }

    /// <summary>
    /// Runs <paramref name="work"/> in a transaction. A failed result rolls back, a successful one commits.
    /// Deadlocks are retried a few times; constraint violations become conflict results.
    /// </summary>
    public static async Task<Result<T>> InTransactionAsync<T>(
        this IDbContextFactory<MovieDbContext> factory,
        Func<MovieDbContext, CancellationToken, Task<Result<T>>> work,
        IsolationLevel isolation = IsolationLevel.RepeatableRead,
        CancellationToken cancellationToken = default
    )
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                await using var context = await factory.CreateDbContextAsync(cancellationToken);
                await using var transaction = await context.Database.BeginTransactionAsync(
                    isolation,
                    cancellationToken
                );
                var result = await work(context, cancellationToken);
                if (!result.IsSuccess)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return result;
                }

                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch (Exception ex)
                when (ex is DbUpdateException or MySqlException or InvalidOperationException
                    && Classify(ex) != DbFailure.None
                )
            {
                switch (Classify(ex))
                {
                    case DbFailure.Busy when attempt < MaxAttempts:
                        await Task.Delay(Random.Shared.Next(10, 40 * attempt), cancellationToken);
                        continue;
                    case DbFailure.Busy:
                        break;
                    case DbFailure.Duplicate:
                        return Error.Conflict(DuplicateMessage);
                    case DbFailure.ForeignKey:
                        return Error.Conflict(ReferencedMessage);
                    case DbFailure.TooLong:
                        return Error.Validation("", "A value is too long.");
                    default:
                        throw;
                }
            }
        }

        return Error.Conflict(BusyMessage);
    }
}
