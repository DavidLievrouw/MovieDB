using BenchmarkDotNet.Attributes;

namespace MovieDB.Benchmarks;

/// <summary>
/// Times every operation of <see cref="Operations"/> against a seeded MariaDB database, once for the size of the real
/// database and once ten times larger (PLAN-4 §5.2). Memory allocation is reported through the memory diagnoser.
/// </summary>
[MemoryDiagnoser]
public class MovieBenchmarks
{
    private Operations _operations = null!;

    /// <summary>The number of seeded movies: the real size, and ten times that.</summary>
    [Params(1203, 12030)]
    public int MovieCount { get; set; }

    /// <summary>The name of the operation to time.</summary>
    [ParamsSource(nameof(OperationNames))]
    public string Operation { get; set; } = "";

    /// <summary>The names of the operations, taken from <see cref="Operations"/>.</summary>
    public static IEnumerable<string> OperationNames => Operations.Names;

    /// <summary>Opens the shared database for the current size and picks the operation.</summary>
    [GlobalSetup]
    public async Task Setup()
    {
        var database = await BenchmarkEnvironment.GetDatabase(MovieCount);
        _operations = new Operations(database);
        await _operations.ResetWrites();
    }

    /// <summary>Removes the rows that the write operations added.</summary>
    [IterationCleanup]
    public void Cleanup() => _operations.ResetWrites().GetAwaiter().GetResult();

    /// <summary>Runs the operation.</summary>
    [Benchmark]
    public Task Run() => _operations.Run(Operation);
}
