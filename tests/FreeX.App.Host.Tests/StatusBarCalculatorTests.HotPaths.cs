using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class StatusBarCalculatorTests
{
    private static string ReadSharedCalculatorSource() =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Services", "WorkbookSelectionStatsCalculator.cs");

    [Fact]
    public void Calculate_SingleCellStatusBypassesRangeScanSetup()
    {
        // The Host now consumes the shared selection-stats
        // calculator, so the single-cell fast path lives with the shared implementation.
        var source = ReadSharedCalculatorSource();

        source.Should().Contain("range.Start == range.End");
        source.Should().Contain("CalculateSingleCell(sheet.GetValue(range.Start.Row, range.Start.Col))");
    }

    [Fact]
    public void Calculate_LargeSelections_ScansSparseCellsWithoutCopyingUsedCellDictionary()
    {
        var source = ReadSharedCalculatorSource();

        source.Should().Contain(
            "GetUsedRange()",
            "status-bar refreshes for selections outside the used range should avoid scanning occupied cells");
        source.Should().NotContain(
            "GetUsedCells()",
            "status-bar refreshes happen during navigation and should not allocate a full used-cell dictionary");
        source.Should().NotContain(
            ".Where(",
            "whole-column status calculations should avoid LINQ iterator chains in the hot path");
        source.Should().NotContain(
            ".Select(",
            "whole-column status calculations should avoid LINQ iterator chains in the hot path");
        source.Should().Contain(
            "sheet.CellCount + sheet.SpillValueCount < totalCells",
            "status calculations should choose the cheaper scan direction without excluding spill-overlay values");
        source.Should().Contain(
            "sheet.EnumerateValueBearingCells()",
            "sparse status-bar selections should enumerate both stored cells and dynamic-array spill values");
        source.Should().NotContain(
            "sheet.GetOccupiedCellMap()",
            "the primary-cell map omits dynamic-array spill values");
        source.Should().Contain(
            "sheet.GetValue(row, col)",
            "small status-bar selections should clip to the used range and scan by primitive coordinates");
        source.Should().NotContain(
            "scanRange.AllCells()",
            "status-bar hot paths should avoid iterator and CellAddress allocation");
        source.Should().NotContain(
            "sheet.EnumerateCells()",
            "status-bar hot paths should avoid address tuple allocation while scanning occupied cells");
    }
}
