using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R143-meta-sort-warning-sheet-wide-clamp: unit coverage for the new
/// <see cref="Sheet.GetUsedRangeInColumns"/>/<see cref="Sheet.GetUsedRangeInRows"/> helpers added so
/// <c>QuickSortRangePlanner.ClampToUsedRange</c> can scope its whole-column/whole-row clamp to the
/// columns (or rows) actually selected, instead of <see cref="Sheet.GetUsedRange"/>'s sheet-wide
/// bounding box.
/// </summary>
public sealed class R143_SheetUsedRangeInBandTests
{
    [Fact]
    public void GetUsedRangeInColumns_IgnoresDataInOtherColumns()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new NumberValue(2));
        // Far stray cell in an unrelated column (26 = Z).
        sheet.SetCell(new CellAddress(sheet.Id, 5000, 26), new NumberValue(3));

        var result = sheet.GetUsedRangeInColumns(1, 1);

        result.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 6, 1)),
            "the far stray cell sits in column 26, outside the [1,1] band being queried");
    }

    [Fact]
    public void GetUsedRangeInColumns_IncludesStrayCellWithinTheBand()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new NumberValue(2));
        // Stray cell far below, but still inside column 1 -- the band being queried.
        sheet.SetCell(new CellAddress(sheet.Id, 5000, 1), new NumberValue(3));

        var result = sheet.GetUsedRangeInColumns(1, 1);

        result.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5000, 1)),
            "a stray cell genuinely inside the queried column band must still be picked up");
    }

    [Fact]
    public void GetUsedRangeInColumns_MultiColumnBand_UnionsAcrossTheBand()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 3), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new NumberValue(3)); // outside band [2,3]

        var result = sheet.GetUsedRangeInColumns(2, 3);

        result.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 2),
            new CellAddress(sheet.Id, 10, 3)),
            "column 1's data is outside the [2,3] band and must not affect the result");
    }

    [Fact]
    public void GetUsedRangeInColumns_NoDataInBand_ReturnsNull()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 26), new NumberValue(1));

        sheet.GetUsedRangeInColumns(1, 1).Should().BeNull();
    }

    [Fact]
    public void GetUsedRangeInRows_IgnoresDataInOtherRows()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(2));
        // Far stray cell in an unrelated row.
        sheet.SetCell(new CellAddress(sheet.Id, 100, 500), new NumberValue(3));

        var result = sheet.GetUsedRangeInRows(2, 3);

        result.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 3, 3)),
            "the far stray cell sits in row 100, outside the [2,3] band being queried");
    }

    [Fact]
    public void GetUsedRangeInRows_IncludesStrayCellWithinTheBand()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        // Stray cell far to the right, but still inside row 2 -- the band being queried.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 500), new NumberValue(2));

        var result = sheet.GetUsedRangeInRows(2, 2);

        result.Should().Be(new GridRange(
            new CellAddress(sheet.Id, 2, 1),
            new CellAddress(sheet.Id, 2, 500)),
            "a stray cell genuinely inside the queried row band must still be picked up");
    }
}
