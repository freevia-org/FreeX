using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Regression tests for finding J18: WorkbookSelectionStatsCalculator must include
/// DateTimeValue cells in Sum/Average/Min/Max/NumericalCount (as their underlying serial
/// value), matching Excel's behavior of treating dates as numbers in these aggregates.
/// </summary>
public sealed class KStatusbarDateAggregationTests
{
    [Fact]
    public void Calculate_SingleDateCellSelection_IncludesSerialValueInAllStats()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var dateValue = DateTimeValue.FromDateTime(new DateTime(2026, 6, 6));
        sheet.SetCell(address, Cell.FromValue(dateValue));

        var stats = WorkbookSelectionStatsCalculator.Calculate(sheet, new GridRange(address, address));

        stats.Count.Should().Be(1);
        stats.NumericalCount.Should().Be(1);
        stats.Sum.Should().Be(dateValue.Value);
        stats.Average.Should().Be(dateValue.Value);
        stats.Min.Should().Be(dateValue.Value);
        stats.Max.Should().Be(dateValue.Value);
        stats.HasNumericalValues.Should().BeTrue();
    }

    [Fact]
    public void Calculate_AllDateSelection_ProducesNonZeroNumericalCountAndStats()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var firstDate = DateTimeValue.FromDateTime(new DateTime(2026, 1, 1));
        var secondDate = DateTimeValue.FromDateTime(new DateTime(2026, 12, 31));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(firstDate));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(secondDate));

        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 1)));

        stats.Count.Should().Be(2);
        stats.NumericalCount.Should().Be(2);
        stats.Sum.Should().Be(firstDate.Value + secondDate.Value);
        stats.Average.Should().Be((firstDate.Value + secondDate.Value) / 2);
        stats.Min.Should().Be(firstDate.Value);
        stats.Max.Should().Be(secondDate.Value);
        stats.HasNumericalValues.Should().BeTrue();
    }

    [Fact]
    public void Calculate_MixedNumbersAndDates_CombinesSerialValuesIntoAggregates()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        var dateValue = DateTimeValue.FromDateTime(new DateTime(2026, 6, 6));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(dateValue));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(5)));

        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 3)));

        stats.Count.Should().Be(3);
        stats.NumericalCount.Should().Be(3);
        stats.Sum.Should().Be(10 + dateValue.Value + 5);
        stats.Min.Should().Be(5);
        stats.Max.Should().Be(dateValue.Value);
    }

    [Fact]
    public void Calculate_MultiRangeSelectionWithDates_IncludesDateSerialValues()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        var dateValue = DateTimeValue.FromDateTime(new DateTime(2026, 3, 15));
        sheet.SetCell(a1, Cell.FromValue(new NumberValue(2)));
        sheet.SetCell(c1, Cell.FromValue(dateValue));

        var stats = WorkbookSelectionStatsCalculator.Calculate(
            sheet,
            new List<GridRange> { new(a1, a1), new(c1, c1) });

        stats.Count.Should().Be(2);
        stats.NumericalCount.Should().Be(2);
        stats.Sum.Should().Be(2 + dateValue.Value);
        stats.Min.Should().Be(2);
        stats.Max.Should().Be(dateValue.Value);
    }
}
