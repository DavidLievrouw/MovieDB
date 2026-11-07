using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MovieDB.Benchmarks;

/// <summary>Counts the database commands (round trips) that EF Core executes through a context factory.</summary>
internal sealed class QueryCounter : DbCommandInterceptor
{
    private int _count;

    /// <summary>The number of commands executed since the last <see cref="Reset"/>.</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>Sets the count back to zero.</summary>
    public void Reset() => Interlocked.Exchange(ref _count, 0);

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    )
    {
        Increment();
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Increment();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result
    )
    {
        Increment();
        return result;
    }

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        Increment();
        return ValueTask.FromResult(result);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result
    )
    {
        Increment();
        return result;
    }

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default
    )
    {
        Increment();
        return ValueTask.FromResult(result);
    }

    private void Increment() => Interlocked.Increment(ref _count);
}
