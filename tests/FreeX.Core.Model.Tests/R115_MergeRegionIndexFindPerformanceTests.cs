using System.Diagnostics;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R115: MergeRegionIndex.Find (backing Sheet.GetMergeRegion/IsMerged) must not degrade to
/// O(total merged regions on the sheet) per lookup just because one merged region spans far
/// past the start rows of many other, unrelated, smaller merges.
///
/// The previous implementation sorted regions by start row and kept a running prefix-max of
/// End.Row, answering Find() by binary-searching to the last region starting at-or-before the
/// query row and scanning BACKWARD until the prefix max fell below the query row. Because the
/// prefix max is monotonically non-decreasing, one region with a very large End.Row kept every
/// later region's prefix-max entry at or above that End.Row for the rest of the array, so a
/// query landing in that region's row-shadow but matching no intervening region had to walk
/// backward through every intervening region before it could even reach (or rule out) the large
/// one. See Sheet.Merges.cs (MergeRegionIndex) for the row-bucket replacement.
/// </summary>
public class R115_MergeRegionIndexFindPerformanceTests
{
    [Fact]
    public void GetMergeRegion_QueryInShadowOfLargeRegionButMatchingNoSmallerRegion_ReturnsNull()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // One region spanning almost the whole sheet, in column 100.
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 100),
            new CellAddress(sheet.Id, 900_000, 100)));

        // Many small 2-row merges scattered through that span, in columns 5-6.
        for (uint row = 2; row < 900_000; row += 40)
        {
            sheet.AddMergedRegion(new GridRange(
                new CellAddress(sheet.Id, row, 5),
                new CellAddress(sheet.Id, row + 1, 6)));
        }

        // Column 200 matches neither the big region (col 100) nor the small ones (cols 5-6),
        // so the correct answer is "not merged" -- but a probe deep inside the big region's
        // row-shadow is exactly the case the old backward scan handled slowly.
        var probe = new CellAddress(sheet.Id, 450_000, 200);
        sheet.GetMergeRegion(probe).Should().BeNull();
        sheet.IsMerged(probe).Should().BeFalse();
    }

    [Fact]
    public void GetMergeRegion_FindsLargeAndSmallRegionsCorrectly_AcrossRowBucketBoundaries()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        var bigRegion = new GridRange(
            new CellAddress(sheet.Id, 1, 100),
            new CellAddress(sheet.Id, 900_000, 100));
        sheet.AddMergedRegion(bigRegion);

        // A small region straddling a row-bucket boundary (bucket size is 256 rows internally),
        // to make sure a merge that spans two buckets is still found from either bucket.
        var straddlingRegion = new GridRange(
            new CellAddress(sheet.Id, 255, 5),
            new CellAddress(sheet.Id, 257, 6));
        sheet.AddMergedRegion(straddlingRegion);

        var smallRegion = new GridRange(
            new CellAddress(sheet.Id, 500_000, 5),
            new CellAddress(sheet.Id, 500_001, 6));
        sheet.AddMergedRegion(smallRegion);

        sheet.GetMergeRegion(new CellAddress(sheet.Id, 500_000, 100)).Should().Be(bigRegion);
        sheet.GetMergeRegion(new CellAddress(sheet.Id, 500_000, 5)).Should().Be(smallRegion);
        sheet.GetMergeRegion(new CellAddress(sheet.Id, 500_001, 6)).Should().Be(smallRegion);
        sheet.GetMergeRegion(new CellAddress(sheet.Id, 255, 5)).Should().Be(straddlingRegion);
        sheet.GetMergeRegion(new CellAddress(sheet.Id, 256, 6)).Should().Be(straddlingRegion);
        sheet.GetMergeRegion(new CellAddress(sheet.Id, 257, 5)).Should().Be(straddlingRegion);
        sheet.GetMergeRegion(new CellAddress(sheet.Id, 1, 1)).Should().BeNull();
        sheet.IsMerged(new CellAddress(sheet.Id, 500_000, 100)).Should().BeTrue();
    }

    /// <summary>
    /// Performance regression guard, gated behind FREEX_RUN_BENCHMARK_TESTS like the rest of the
    /// codebase's timing-sensitive tests. With the pre-fix prefix-max backward scan, 5,000 queries
    /// landing in the shadow of a sheet-spanning merge (but matching neither it nor any of 300,000
    /// unrelated small merges) walk essentially every registered region on every call -- roughly
    /// 1.5 billion backward-scan iterations, which takes well over a second. With the row-bucket
    /// index, the same queries only touch the handful of regions overlapping each query row's
    /// 256-row band, regardless of how many other merges exist on the sheet.
    /// </summary>
    [BenchmarkFact]
    public void Benchmark_GetMergeRegion_QueriesInShadowOfLargeRegion_CompletesWithinTimeBudget()
    {
        var workbook = new Workbook("Benchmark");
        var sheet = workbook.AddSheet("Sheet1");

        // One region spanning rows 1..MaxRow in column 100.
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 100),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 100)));

        // 300,000 small 2-row merges scattered in columns 5-6 through that span.
        const int smallMergeCount = 300_000;
        var step = CellAddress.MaxRow / (uint)(smallMergeCount + 1);
        for (uint i = 1; i <= smallMergeCount; i++)
        {
            var row = i * step;
            if (row < 1 || row >= CellAddress.MaxRow) continue;
            sheet.AddMergedRegion(new GridRange(
                new CellAddress(sheet.Id, row, 5),
                new CellAddress(sheet.Id, row + 1, 6)));
        }

        // Build the index once, outside the timed region (mirrors real usage: edits invalidate
        // the index, then many lookups reuse the built index).
        sheet.IsMerged(new CellAddress(sheet.Id, 1, 1));

        const int queryCount = 5_000;
        var probes = new CellAddress[queryCount];
        var rnd = new Random(20260731);
        for (var i = 0; i < queryCount; i++)
        {
            var row = (uint)rnd.Next(1, (int)CellAddress.MaxRow);
            probes[i] = new CellAddress(sheet.Id, row, 200); // col 200 matches neither big nor small regions
        }

        var sw = Stopwatch.StartNew();
        foreach (var probe in probes)
        {
            sheet.GetMergeRegion(probe).Should().BeNull();
        }
        sw.Stop();

        Console.WriteLine(
            $"GetMergeRegion {queryCount} queries in the shadow of a {CellAddress.MaxRow}-row merge " +
            $"with {smallMergeCount} unrelated small merges: {sw.ElapsedMilliseconds}ms");

        sw.ElapsedMilliseconds.Should().BeLessThan(800,
            "Find() must not degrade into an O(total merged regions) backward scan just because " +
            "one region spans far past the start rows of many unrelated smaller merges");
    }
}
