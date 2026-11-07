using System.Globalization;
using System.Text;
using BenchmarkDotNet.Running;
using MovieDB.Benchmarks;

// Two modes (PLAN-4 §5):
//   dotnet run -c Release --project src/tests/MovieDB.Benchmarks -- --filter '*' --artifacts artifacts/benchmarks/<name>
//   dotnet run -c Release --project src/tests/MovieDB.Benchmarks -- --roundtrips artifacts/benchmarks/<name>
// Both start one throwaway MariaDB container (Docker must be running) and drop it afterwards.
await BenchmarkEnvironment.StartAsync();
try
{
    var roundTripsAt = Array.IndexOf(args, "--roundtrips");
    if (roundTripsAt >= 0)
    {
        var directory = roundTripsAt + 1 < args.Length ? args[roundTripsAt + 1] : "artifacts/benchmarks/roundtrips";
        await WriteRoundTripsAsync(directory);
    }
    else
    {
        // In-process, so the benchmarks share the container and the seeded databases, and the build keeps the
        // solution's analyzers and central package management.
        string[] benchmarkArgs = args.Contains("--inProcess") ? args : [.. args, "--inProcess"];
        BenchmarkSwitcher.FromAssembly(typeof(MovieBenchmarks).Assembly).Run(benchmarkArgs);
    }
}
finally
{
    await BenchmarkEnvironment.StopAsync();
}

static async Task WriteRoundTripsAsync(string directory)
{
    var report = new StringBuilder()
        .AppendLine("# Database round trips per operation")
        .AppendLine()
        .AppendLine("| Operation | 1,203 movies | 12,030 movies |")
        .AppendLine("|---|---:|---:|");

    var counts = new Dictionary<string, int[]>();
    var column = 0;
    foreach (var movieCount in new[] { 1203, 12030 })
    {
        var database = await BenchmarkEnvironment.GetDatabase(movieCount);
        var operations = new Operations(database);
        foreach (var name in Operations.Names)
        {
            await operations.Run(name);
            await operations.ResetWrites();
            database.Counter.Reset();
            await operations.Run(name);
            var count = database.Counter.Count;
            await operations.ResetWrites();

            if (!counts.TryGetValue(name, out var row))
            {
                row = new int[2];
                counts[name] = row;
            }

            row[column] = count;
        }

        column++;
    }

    foreach (var name in Operations.Names)
    {
        var row = counts[name];
        report.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {name} | {row[0]} | {row[1]} |"));
    }

    Directory.CreateDirectory(directory);
    var path = Path.Combine(directory, "roundtrips.md");
    await File.WriteAllTextAsync(path, report.ToString(), new UTF8Encoding(false));
    Console.WriteLine(report);
    Console.WriteLine("Written to " + path);
}
