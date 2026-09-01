using System.Collections;
using System.Diagnostics;
using System.Reflection;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class PivotTableRefreshServiceTests
{
    [BenchmarkFact]
    [Trait("Category", "Benchmark")]
    public void Benchmark_BuildRowGroupsManyNoDataCombinations_ReportsTimingAndAllocations()
    {
        const int itemsPerField = 20;
        const int fieldCount = 3;
        const int expectedCombinations = itemsPerField * itemsPerField * itemsPerField;
        const int iterations = 3;

        var workbook = new Workbook("PivotNoDataCombinationPerf");
        workbook.AddSheet("Data");
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            Fields =
            {
                CreateCacheField("Field1", itemsPerField),
                CreateCacheField("Field2", itemsPerField),
                CreateCacheField("Field3", itemsPerField)
            }
        });

        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            ShowItemsWithNoDataOnRows = true
        };
        var rowFields = Enumerable.Range(0, fieldCount)
            .Select(index => new PivotFieldModel(index))
            .ToList();
        var rows = Array.Empty<IReadOnlyList<ScalarValue>>();
        InvokeBuildRowGroups(workbook, pivot, rows, rowFields)
            .Count.Should().Be(expectedCombinations);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var timings = new double[iterations];
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var total = Stopwatch.StartNew();
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var step = Stopwatch.StartNew();
            var groups = InvokeBuildRowGroups(workbook, pivot, rows, rowFields);
            step.Stop();

            groups.Count.Should().Be(expectedCombinations);
            timings[iteration] = step.Elapsed.TotalMilliseconds;
        }

        total.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Console.WriteLine(
            "PERF PIVOT_NO_DATA_ROW_GROUPS " +
            $"fields={fieldCount} items_per_field={itemsPerField} combinations={expectedCombinations} " +
            $"iterations={iterations} total_ms={total.Elapsed.TotalMilliseconds:F2} " +
            $"mean_ms={timings.Average():F2} max_ms={timings.Max():F2} " +
            $"allocated_bytes={allocatedBytes}");

        allocatedBytes.Should().BeLessThan(100_000_000);
        total.Elapsed.Should().BeGreaterThan(TimeSpan.Zero);
    }

    private static PivotCacheFieldModel CreateCacheField(string name, int itemCount) =>
        new(name, SharedItems: Enumerable.Range(0, itemCount).Select(index => $"Item {index}").ToList());

    private static ICollection InvokeBuildRowGroups(
        Workbook workbook,
        PivotTableModel pivot,
        IReadOnlyList<IReadOnlyList<ScalarValue>> rows,
        IReadOnlyList<PivotFieldModel> rowFields) =>
        PivotTableRefreshService.BuildRowGroups(workbook, pivot, rows, rowFields);
}
