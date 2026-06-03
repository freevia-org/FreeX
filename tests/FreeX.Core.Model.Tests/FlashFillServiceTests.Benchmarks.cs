using System.Diagnostics;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class FlashFillServiceTests
{
    [Fact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_FillManyFileExtensions_ReportsTimingAndAllocatedBytes()
    {
        const int rows = 20_000;
        const int iterations = 5;
        (string Source, string Expected)[] examples =
        [
            ("report.final.xlsx", "xlsx"),
            ("budget.2026.csv", "csv")
        ];
        var remaining = Enumerable
            .Range(0, rows)
            .Select(row => $"archive/client-{row:D5}.q{(row % 4) + 1}.txt")
            .ToArray();

        var warmup = FlashFillService.Fill(examples, remaining);
        warmup.Should().NotBeNull();
        warmup![0].Should().Be("txt");

        var (totalMs, meanMs, p95Ms, maxMs, allocatedBytes, result) = MeasureFlashFill(
            iterations,
            () => FlashFillService.Fill(examples, remaining));

        result.Should().NotBeNull();
        result!.Should().HaveCount(rows);
        result[0].Should().Be("txt");
        result[^1].Should().Be("txt");

        Console.WriteLine(
            "PERF FLASHFILL_FILE_EXTENSIONS " +
            $"rows={rows} steps={iterations} total_ms={totalMs:F2} mean_ms={meanMs:F2} " +
            $"p95_ms={p95Ms:F2} max_ms={maxMs:F2} allocated_bytes={allocatedBytes:N0}");
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_FillManyFirstTokens_ReportsTimingAndAllocatedBytes()
    {
        const int rows = 20_000;
        const int iterations = 5;
        (string Source, string Expected)[] examples =
        [
            ("Alice Smith", "Alice"),
            ("Bob Jones", "Bob")
        ];
        var remaining = Enumerable
            .Range(0, rows)
            .Select(row => $"Name{row:D5} Surname{row:D5}")
            .ToArray();

        var warmup = FlashFillService.Fill(examples, remaining);
        warmup.Should().NotBeNull();
        warmup![0].Should().Be("Name00000");

        var (totalMs, meanMs, p95Ms, maxMs, allocatedBytes, result) = MeasureFlashFill(
            iterations,
            () => FlashFillService.Fill(examples, remaining));

        result.Should().NotBeNull();
        result!.Should().HaveCount(rows);
        result[0].Should().Be("Name00000");
        result[^1].Should().Be("Name19999");

        Console.WriteLine(
            "PERF FLASHFILL_FIRST_TOKENS " +
            $"rows={rows} steps={iterations} total_ms={totalMs:F2} mean_ms={meanMs:F2} " +
            $"p95_ms={p95Ms:F2} max_ms={maxMs:F2} allocated_bytes={allocatedBytes:N0}");
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_FillFromColumnsEmail_ReportsTimingAndAllocatedBytes()
    {
        const int rows = 20_000;
        const int iterations = 5;
        IReadOnlyList<string>[] exampleSources =
        [
            ["Alice", "Smith"],
            ["Bob", "Jones"]
        ];
        string[] exampleOutputs =
        [
            "alice.smith@example.com",
            "bob.jones@example.com"
        ];
        var remainingSources = Enumerable
            .Range(0, rows)
            .Select(row => (IReadOnlyList<string>)[$"First{row:D5}", $"Last{row:D5}"])
            .ToArray();

        var warmup = FlashFillService.FillFromColumns(exampleSources, exampleOutputs, remainingSources);
        warmup.Should().NotBeNull();
        warmup![0].Should().Be("first00000.last00000@example.com");

        var (totalMs, meanMs, p95Ms, maxMs, allocatedBytes, result) = MeasureFlashFill(
            iterations,
            () => FlashFillService.FillFromColumns(exampleSources, exampleOutputs, remainingSources));

        result.Should().NotBeNull();
        result!.Should().HaveCount(rows);
        result[0].Should().Be("first00000.last00000@example.com");
        result[^1].Should().Be("first19999.last19999@example.com");

        Console.WriteLine(
            "PERF FLASHFILL_COLUMNS_EMAIL " +
            $"rows={rows} steps={iterations} total_ms={totalMs:F2} mean_ms={meanMs:F2} " +
            $"p95_ms={p95Ms:F2} max_ms={maxMs:F2} allocated_bytes={allocatedBytes:N0}");
    }

    private static (double TotalMs, double MeanMs, double P95Ms, double MaxMs, long AllocatedBytes, IReadOnlyList<string>? Result)
        MeasureFlashFill(int iterations, Func<IReadOnlyList<string>?> action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new List<double>(iterations);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        IReadOnlyList<string>? result = null;
        for (var i = 0; i < iterations; i++)
        {
            var step = Stopwatch.StartNew();
            result = action();
            step.Stop();
            timings.Add(step.Elapsed.TotalMilliseconds);
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var ordered = timings.OrderBy(value => value).ToArray();
        var p95 = ordered[Math.Clamp((int)Math.Ceiling(ordered.Length * 0.95) - 1, 0, ordered.Length - 1)];

        return (total.Elapsed.TotalMilliseconds, timings.Average(), p95, ordered[^1], allocatedBytes, result);
    }
}
