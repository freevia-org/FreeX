using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Freexcel.Core.Model.Tests;

public class SelectionRangeServiceTests(ITestOutputHelper output)
{
    [Fact]
    public void GetCurrentRegion_ReturnsContiguousTableAroundActiveCell()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 2; row <= 4; row++)
        {
            for (uint col = 2; col <= 4; col++)
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue($"{row},{col}"));
        }
        sheet.SetCell(new CellAddress(sheet.Id, 6, 2), new TextValue("separate"));

        var region = SelectionRangeService.GetCurrentRegion(
            sheet,
            new CellAddress(sheet.Id, 3, 3));

        region.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 4, 4)));
    }

    [Fact]
    public void GetCurrentRegion_IncludesInternalBlankCellsInsideDataBlock()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("C2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("A3"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("B3"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new TextValue("C3"));

        var region = SelectionRangeService.GetCurrentRegion(
            sheet,
            new CellAddress(sheet.Id, 1, 1));

        region.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3)));
    }

    [Fact]
    public void GetCurrentRegion_ReturnsNullForBlankActiveCell()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));

        var region = SelectionRangeService.GetCurrentRegion(
            sheet,
            new CellAddress(sheet.Id, 5, 5));

        region.Should().BeNull();
    }

    [Fact]
    public void GetCurrentRegion_TreatsFormulaWithBlankValueAsContent()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var formula = Cell.FromFormula("\"\"");
        formula.Value = BlankValue.Instance;
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("header"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), formula);

        var region = SelectionRangeService.GetCurrentRegion(
            sheet,
            new CellAddress(sheet.Id, 3, 2));

        region.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 2)));
    }

    [Fact]
    public void GetCurrentRegion_PreservesFixedPointExpansionForNonRectangularShape()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        Set(sheet, 10, 10, "active");
        Set(sheet, 10, 11, "right");
        Set(sheet, 11, 10, "down");
        Set(sheet, 11, 12, "bridge");
        Set(sheet, 12, 12, "tail");
        Set(sheet, 14, 12, "separate");

        var region = SelectionRangeService.GetCurrentRegion(
            sheet,
            new CellAddress(sheet.Id, 10, 10));

        region.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 10, 10),
            new CellAddress(sheet.Id, 12, 12)));
    }

    [Fact]
    public void GetCurrentRegion_DoesNotCountStoredBlankValueAsContent()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("top"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), BlankValue.Instance);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("bottom"));

        var region = SelectionRangeService.GetCurrentRegion(
            sheet,
            new CellAddress(sheet.Id, 1, 1));

        region.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1)));
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void GetCurrentRegion_BenchmarkSparseWorstShape()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        const uint rows = 240;
        const uint cols = 240;
        for (uint index = 1; index < rows; index++)
        {
            Set(sheet, index, index, $"v{index}");
            Set(sheet, index + 1, index, $"bridge{index}");
        }

        Set(sheet, rows, cols, "tail");

        var activeCell = new CellAddress(sheet.Id, 120, 120);
        var (region, elapsed, allocated) = MeasureGetCurrentRegion(sheet, activeCell, iterations: 25);

        output.WriteLine(
            $"GetCurrentRegion sparse staircase {rows}x{cols}: {elapsed.TotalMilliseconds:F2} ms, {allocated:N0} bytes, region {region}");
        region.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, rows, cols)));
    }

    [Fact]
    [Trait("Category", "Benchmark")]
    public void GetCurrentRegion_BenchmarkDenseNormalCase()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        const uint rows = 120;
        const uint cols = 80;
        for (uint row = 1; row <= rows; row++)
        {
            for (uint col = 1; col <= cols; col++)
                Set(sheet, row, col, $"{row},{col}");
        }

        var activeCell = new CellAddress(sheet.Id, 60, 40);
        var (region, elapsed, allocated) = MeasureGetCurrentRegion(sheet, activeCell, iterations: 25);

        output.WriteLine(
            $"GetCurrentRegion dense rectangle {rows}x{cols}: {elapsed.TotalMilliseconds:F2} ms, {allocated:N0} bytes, region {region}");
        region.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, rows, cols)));
    }

    [Fact]
    public void IsWholeRowSelection_ReturnsTrueForFullWorksheetColumns()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 3, 1),
            new CellAddress(sheetId, 5, CellAddress.MaxCol));

        SelectionRangeService.IsWholeRowSelection(range).Should().BeTrue();
        SelectionRangeService.IsWholeColumnSelection(range).Should().BeFalse();
    }

    [Fact]
    public void IsWholeColumnSelection_ReturnsTrueForFullWorksheetRows()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 1, 2),
            new CellAddress(sheetId, CellAddress.MaxRow, 4));

        SelectionRangeService.IsWholeColumnSelection(range).Should().BeTrue();
        SelectionRangeService.IsWholeRowSelection(range).Should().BeFalse();
    }

    [Fact]
    public void GetWholeRows_ReturnsFullWidthRowsForSelection()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 3, 2),
            new CellAddress(sheetId, 5, 4));

        SelectionRangeService.GetWholeRows(range).Should().Be(new GridRange(
            new CellAddress(sheetId, 3, 1),
            new CellAddress(sheetId, 5, CellAddress.MaxCol)));
    }

    [Fact]
    public void GetWholeColumns_ReturnsFullHeightColumnsForSelection()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 3, 2),
            new CellAddress(sheetId, 5, 4));

        SelectionRangeService.GetWholeColumns(range).Should().Be(new GridRange(
            new CellAddress(sheetId, 1, 2),
            new CellAddress(sheetId, CellAddress.MaxRow, 4)));
    }

    [Fact]
    public void GetRowSpan_ReturnsSelectedStartAndEndRows()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 3, 2),
            new CellAddress(sheetId, 5, 4));

        SelectionRangeService.GetRowSpan(range).Should().Be((3u, 5u));
    }

    [Fact]
    public void GetColumnSpan_ReturnsSelectedStartAndEndColumns()
    {
        var sheetId = SheetId.New();
        var range = new GridRange(
            new CellAddress(sheetId, 3, 2),
            new CellAddress(sheetId, 5, 4));

        SelectionRangeService.GetColumnSpan(range).Should().Be((2u, 4u));
    }

    private static void Set(Sheet sheet, uint row, uint col, string value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue(value));

    private static (GridRange? Region, TimeSpan Elapsed, long Allocated) MeasureGetCurrentRegion(
        Sheet sheet,
        CellAddress activeCell,
        int iterations)
    {
        SelectionRangeService.GetCurrentRegion(sheet, activeCell).Should().NotBeNull();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        GridRange? region = null;
        for (var iteration = 0; iteration < iterations; iteration++)
            region = SelectionRangeService.GetCurrentRegion(sheet, activeCell);
        stopwatch.Stop();

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        return (region, stopwatch.Elapsed, allocated);
    }
}
