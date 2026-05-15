using Freexcel.Core.Commands;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Model.Tests;

public class SelectionRangeServiceTests
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
}
