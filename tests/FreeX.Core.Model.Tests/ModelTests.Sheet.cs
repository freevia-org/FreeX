using FreeX.Core.Model;
using FluentAssertions;
using System.Diagnostics;

namespace FreeX.Core.Model.Tests;

public partial class SheetTests
{
    [Fact]
    public void SetCell_GetValue_Roundtrips()
    {
        var sheet = new Sheet(SheetId.New(), "Test");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new NumberValue(42));
        sheet.GetValue(addr).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void GetValue_EmptyCell_ReturnsBlank()
    {
        var sheet = new Sheet(SheetId.New(), "Test");
        sheet.GetValue(1, 1).Should().BeOfType<BlankValue>();
    }

    [Fact]
    public void GetMergeRegion_FindsMergeInLargeList()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        for (uint r = 1; r <= 500; r++)
        {
            var start = new CellAddress(sheet.Id, r * 2, 1);
            var end   = new CellAddress(sheet.Id, r * 2, 2);
            sheet.AddMergedRegion(new GridRange(start, end));
        }
        var target = new CellAddress(sheet.Id, 500, 1);
        var found  = sheet.GetMergeRegion(target);
        found.Should().NotBeNull("cell at row 500 col 1 is inside a merge region");
        found!.Value.Start.Row.Should().Be(500);
    }

    [Fact]
    public void GetMergeRegion_DoesNotExpandTallMergedRegionsPerCell()
    {
        var sheet = new Sheet(SheetId.New(), "Test");
        var region = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 2));
        sheet.AddMergedRegion(region);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();

        var found = sheet.GetMergeRegion(new CellAddress(sheet.Id, CellAddress.MaxRow, 2));

        stopwatch.Stop();
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        found.Should().Be(region);
        allocatedBytes.Should().BeLessThan(1_000_000);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public void ReplaceMergedRegions_MaterializesLazyProjectionBeforeReplacing()
    {
        var sheet = new Sheet(SheetId.New(), "Test");
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 2)));

        sheet.ReplaceMergedRegions(sheet.MergedRegions.Select(region => new GridRange(
            new CellAddress(region.Start.Sheet, region.Start.Row + 1, region.Start.Col),
            new CellAddress(region.End.Sheet, region.End.Row + 1, region.End.Col))));

        sheet.MergedRegions.Should().ContainSingle().Which.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 3, 1),
            new CellAddress(sheet.Id, 4, 2)));
    }

    [Fact]
    public void GetUsedRange_RecomputesAfterBoundaryCellsAreCleared()
    {
        var sheet = new Sheet(SheetId.New(), "Test");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("inside"));
        sheet.SetCell(new CellAddress(sheet.Id, 20, 30), new TextValue("edge"));
        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 20, 30)));

        sheet.ClearCell(new CellAddress(sheet.Id, 20, 30));

        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 2, 3)));
    }

    [Fact]
    public void GetUsedRange_ExpandsAfterCachedRangeIsRead()
    {
        var sheet = new Sheet(SheetId.New(), "Test");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("inside"));
        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 2, 3)));

        sheet.SetCell(new CellAddress(sheet.Id, 20, 30), new TextValue("edge"));

        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 20, 30)));
    }

    [Fact]
    public void GetUsedRange_KeepsCachedBoundsAfterInteriorCellsChange()
    {
        var sheet = new Sheet(SheetId.New(), "Test");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("start"));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 20), new TextValue("interior"));
        sheet.SetCell(new CellAddress(sheet.Id, 20, 30), new TextValue("end"));
        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 20, 30)));

        sheet.SetCell(new CellAddress(sheet.Id, 10, 20), new TextValue("updated"));
        sheet.ClearCell(new CellAddress(sheet.Id, 10, 20));

        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 3),
            new CellAddress(sheet.Id, 20, 30)));
    }

    [Fact]
    public void GetUsedRange_RepeatedCallsReuseCachedBounds()
    {
        var sheet = new Sheet(SheetId.New(), "Large");
        for (uint row = 1; row <= 200; row++)
        {
            for (uint col = 1; col <= 100; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row + col));
        }

        sheet.GetUsedRange().Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 200, 100)));
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int repetitions = 10_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        GridRange? range = null;
        for (var i = 0; i < repetitions; i++)
            range = sheet.GetUsedRange();
        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        range.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 200, 100)));
        Console.WriteLine(
            $"GetUsedRange cached repeated {repetitions}x over {sheet.CellCount:N0} cells: {stopwatch.Elapsed.TotalMilliseconds:F2} ms, {allocated:N0} bytes allocated.");
        allocated.Should().BeLessThan(1_000);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void GetUsedRange_InterleavedInteriorWritesReuseCachedBounds()
    {
        var sheet = new Sheet(SheetId.New(), "Large");
        for (uint row = 1; row <= 200; row++)
        {
            for (uint col = 1; col <= 100; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(row + col));
        }

        var expected = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 200, 100));
        sheet.GetUsedRange().Should().Be(expected);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        const int repetitions = 10_000;
        var replacement = new NumberValue(123);
        var address = new CellAddress(sheet.Id, 100, 50);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        GridRange? range = null;
        for (var i = 0; i < repetitions; i++)
        {
            sheet.SetCell(address, replacement);
            range = sheet.GetUsedRange();
        }
        stopwatch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        range.Should().Be(expected);
        Console.WriteLine(
            $"GetUsedRange interleaved interior writes {repetitions}x over {sheet.CellCount:N0} cells: {stopwatch.Elapsed.TotalMilliseconds:F2} ms, {allocated:N0} bytes allocated.");
        allocated.Should().BeLessThan(1_000);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
    }
}
