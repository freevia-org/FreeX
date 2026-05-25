using System.Diagnostics;
using FluentAssertions;
using Freexcel.Core.Model;
using Xunit.Abstractions;

namespace Freexcel.App.Host.Tests;

public sealed class SparklineValueCacheTests
{
    private readonly ITestOutputHelper _output;

    public SparklineValueCacheTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void GetOrCreate_ReusesSparklineValuesWhenSheetAndRevisionAreUnchanged()
    {
        var cache = new SparklineValueCache();
        var sheet = CreateSheetWithSparkline();
        var calls = 0;

        var first = cache.GetOrCreate(sheet, revision: 2, CreateValues);
        var second = cache.GetOrCreate(sheet, revision: 2, CreateValues);

        calls.Should().Be(1);
        second.Should().BeSameAs(first);
        second.Values.Single().Should().Equal(1, 2, 3);
        return;

        IReadOnlyDictionary<Guid, IReadOnlyList<double>> CreateValues()
        {
            calls++;
            return SparklineValuePlanner.BuildValues(sheet);
        }
    }

    [Fact]
    public void GetOrCreate_RebuildsSparklineValuesWhenRevisionChanges()
    {
        var cache = new SparklineValueCache();
        var sheet = CreateSheetWithSparkline();
        var calls = 0;

        cache.GetOrCreate(sheet, revision: 2, CreateValues);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));
        var second = cache.GetOrCreate(sheet, revision: 3, CreateValues);

        calls.Should().Be(2);
        second.Values.Single().Should().StartWith(42);
        return;

        IReadOnlyDictionary<Guid, IReadOnlyList<double>> CreateValues()
        {
            calls++;
            return SparklineValuePlanner.BuildValues(sheet);
        }
    }

    [Fact]
    public void GetOrCreate_KeepsSameRevisionValuesSeparateForDifferentSheetInstances()
    {
        var cache = new SparklineValueCache();
        var firstSheet = CreateSheetWithSparkline();
        var secondSheet = CreateSheetWithSparkline();
        secondSheet.SetCell(new CellAddress(secondSheet.Id, 1, 1), new NumberValue(42));
        var calls = 0;

        var first = cache.GetOrCreate(firstSheet, revision: 2, () => CreateValues(firstSheet));
        var second = cache.GetOrCreate(secondSheet, revision: 2, () => CreateValues(secondSheet));

        calls.Should().Be(2);
        first.Should().NotBeSameAs(second);
        first.Values.Single().Should().StartWith(1);
        second.Values.Single().Should().StartWith(42);
        return;

        IReadOnlyDictionary<Guid, IReadOnlyList<double>> CreateValues(Sheet sheet)
        {
            calls++;
            return SparklineValuePlanner.BuildValues(sheet);
        }
    }

    [Fact]
    public void Clear_DropsCachedSparklineValues()
    {
        var cache = new SparklineValueCache();
        var sheet = CreateSheetWithSparkline();
        var calls = 0;

        cache.GetOrCreate(sheet, revision: 2, CreateValues);
        cache.Clear();
        cache.GetOrCreate(sheet, revision: 2, CreateValues);

        calls.Should().Be(2);
        return;

        IReadOnlyDictionary<Guid, IReadOnlyList<double>> CreateValues()
        {
            calls++;
            return SparklineValuePlanner.BuildValues(sheet);
        }
    }

    [Fact]
    public void GetOrCreate_CacheHitAvoidsRepeatedPlannerWork()
    {
        var sheet = CreateSheetWithManySparklines(sparklineCount: 200, valuesPerSparkline: 25);
        var cache = new SparklineValueCache();
        const int repetitions = 100;

        var uncached = Measure(repetitions, () => SparklineValuePlanner.BuildValues(sheet));
        var cached = Measure(repetitions, () => cache.GetOrCreate(sheet, revision: 9, () => SparklineValuePlanner.BuildValues(sheet)));

        _output.WriteLine(
            $"Sparkline values repeated {repetitions}x over {sheet.Sparklines.Count:N0} sparklines: " +
            $"uncached {uncached.Elapsed.TotalMilliseconds:F2} ms, {uncached.Allocated:N0} bytes; " +
            $"cached {cached.Elapsed.TotalMilliseconds:F2} ms, {cached.Allocated:N0} bytes.");
        cached.Allocated.Should().BeLessThan(uncached.Allocated);
    }

    private static (TimeSpan Elapsed, long Allocated) Measure(
        int repetitions,
        Func<IReadOnlyDictionary<Guid, IReadOnlyList<double>>> action)
    {
        action();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < repetitions; i++)
            action();
        stopwatch.Stop();
        return (stopwatch.Elapsed, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static Sheet CreateSheetWithSparkline()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(3));
        sheet.Sparklines.Add(new SparklineModel
        {
            Id = Guid.NewGuid(),
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 3)),
            Location = new CellAddress(sheet.Id, 1, 4),
            Kind = SparklineKind.Line
        });
        return sheet;
    }

    private static Sheet CreateSheetWithManySparklines(int sparklineCount, int valuesPerSparkline)
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= sparklineCount; row++)
        {
            for (uint col = 1; col <= valuesPerSparkline; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row + col));

            sheet.Sparklines.Add(new SparklineModel
            {
                Id = Guid.NewGuid(),
                DataRange = new GridRange(
                    new CellAddress(sheet.Id, row, 1),
                    new CellAddress(sheet.Id, row, (uint)valuesPerSparkline)),
                Location = new CellAddress(sheet.Id, row, (uint)valuesPerSparkline + 1),
                Kind = SparklineKind.Line
            });
        }

        return sheet;
    }
}
