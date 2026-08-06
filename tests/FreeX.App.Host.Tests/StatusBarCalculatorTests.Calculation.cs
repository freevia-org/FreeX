using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class StatusBarCalculatorTests
{
    [Fact]
    public void Calculate_SeparatesNonblankCountFromNumericalCount()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new TextValue("counted")));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(BlankValue.Instance));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), Cell.FromValue(new NumberValue(3)));

        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 4)));

        stats.Count.Should().Be(3);
        stats.NumericalCount.Should().Be(2);
        stats.Sum.Should().Be(4);
        stats.Average.Should().Be(2);
        stats.Min.Should().Be(1);
        stats.Max.Should().Be(3);
    }

    [Fact]
    public void Calculate_TextOnlySelectionStillReportsCountWithoutNumericStats()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("text")));

        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 2)));

        stats.Count.Should().Be(1);
        stats.NumericalCount.Should().Be(0);
        stats.Sum.Should().Be(0);
        stats.Average.Should().BeNull();
        stats.Min.Should().BeNull();
        stats.Max.Should().BeNull();
    }

    [Fact]
    public void Calculate_FilteredRowsAreExcludedFromStatusTipStats()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("Header")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), Cell.FromValue(new TextValue("visible")));
        sheet.FilterHiddenRows.Add(2);

        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 1)));

        stats.Count.Should().Be(3);
        stats.NumericalCount.Should().Be(1);
        stats.Sum.Should().Be(30);
        stats.Average.Should().Be(30);
        stats.Min.Should().Be(30);
        stats.Max.Should().Be(30);
    }

    [Fact]
    public void Calculate_SingleCellSelectionUsesDirectValueStats()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var address = new CellAddress(sheet.Id, 5, 3);
        sheet.SetCell(address, Cell.FromValue(new NumberValue(42)));

        var stats = WorkbookSelectionStatsCalculator.Calculate(sheet, new GridRange(address, address));

        stats.Should().Be(new WorkbookSelectionStats(42, 1, 1, 42, 42, 42));
    }

    [Fact]
    public void Calculate_SelectionOutsideUsedRangeReturnsEmptyStats()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        for (uint row = 1; row <= 1_000; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new NumberValue(row)));

        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 5),
                new CellAddress(sheet.Id, CellAddress.MaxRow, 5)));

        stats.Should().Be(new WorkbookSelectionStats(0, 0, 0, null, null, null));
    }

    [Fact]
    public void Calculate_ErrorCellAmongNumbers_SurfacesAggregateErrorCodeThroughHostAdapter()
    {
        // R67 backlog (status-bar-6-2): the WPF host's Stats adapter must carry the shared
        // calculator's AggregateErrorCode through, not drop it during the Stats<->shared
        // conversion -- otherwise the WPF status bar would silently keep showing a
        // plausible-but-wrong Sum for a selection containing an error cell.
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(ErrorValue.DivByZero));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(20)));

        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 3)));

        stats.AggregateErrorCode.Should().Be("#DIV/0!");
        stats.Count.Should().Be(3);
        stats.NumericalCount.Should().Be(2);
    }

    [Fact]
    public void Calculate_LargeSelections_UsesOnlyOccupiedCellsInsideRange()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 1_000_000, 1), Cell.FromValue(new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 1_000_000, 2), Cell.FromValue(new NumberValue(90)));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow, 1));

        var stats = WorkbookSelectionStatsCalculator.Calculate(sheet, range);

        stats.Count.Should().Be(2);
        stats.NumericalCount.Should().Be(2);
        stats.Sum.Should().Be(40);
        stats.Average.Should().Be(20);
        stats.Min.Should().Be(10);
        stats.Max.Should().Be(30);
    }
}
