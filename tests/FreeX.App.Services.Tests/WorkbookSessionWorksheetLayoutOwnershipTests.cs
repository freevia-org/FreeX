using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookSessionWorksheetLayoutOwnershipTests
{
    [Fact]
    public void SizingAndVisibility_ApplyToGroupedSheetsAndPreserveSelection()
    {
        using var session = new WorkbookSessionFactory().CreateNew(240, 320);
        var active = session.ActiveSheet;
        var grouped = session.Workbook.AddSheet("Grouped");
        var range = Range(active.Id, 2, 3, 4, 5);
        session.SelectRange(range);
        session.SelectAllVisibleSheets();

        session.SetSelectedRowsHeight(30).Success.Should().BeTrue();
        session.SetSelectedColumnsWidth(12).Success.Should().BeTrue();
        session.SetSelectedRowsHidden(true).Success.Should().BeTrue();
        session.SetSelectedColumnsHidden(true).Success.Should().BeTrue();

        foreach (var sheet in new[] { active, grouped })
        {
            sheet.RowHeights[2].Should().BeApproximately(40, 0.001);
            sheet.RowHeights[4].Should().BeApproximately(40, 0.001);
            sheet.ColumnWidths[3].Should().Be(12);
            sheet.ColumnWidths[5].Should().Be(12);
            sheet.HiddenRows.Should().Contain([2u, 3u, 4u]);
            sheet.HiddenCols.Should().Contain([3u, 4u, 5u]);
        }

        session.SelectedRange.Should().Be(range);
        session.ActiveCell.Should().Be(range.Start);
    }

    [Fact]
    public void VisibilityRepeat_ReReadsTheLiveSelectionAcrossGroupedSheets()
    {
        using var session = new WorkbookSessionFactory().CreateNew(240, 320);
        var active = session.ActiveSheet;
        var grouped = session.Workbook.AddSheet("Grouped");
        session.SelectAllVisibleSheets();
        session.SelectRange(WholeRows(active.Id, 2, 3));

        session.SetSelectedRowsHidden(true).Success.Should().BeTrue();
        var repeatedRange = WholeRows(active.Id, 8, 8);
        session.SelectRange(repeatedRange);
        session.RepeatLastAction().Success.Should().BeTrue();

        foreach (var sheet in new[] { active, grouped })
        {
            sheet.HiddenRows.Should().Contain([2u, 3u, 8u]);
            sheet.HiddenRows.Should().NotContain(4u);
        }

        session.SelectedRange.Should().Be(repeatedRange);
    }

    [Fact]
    public void OutlineOwnership_PreservesHierarchyAndGroupedSheetTargets()
    {
        using var session = new WorkbookSessionFactory().CreateNew(240, 320);
        var active = session.ActiveSheet;
        var grouped = session.Workbook.AddSheet("Grouped");
        session.SelectAllVisibleSheets();
        session.SelectRange(WholeRows(active.Id, 2, 4));

        session.GroupSelectedOutline().Success.Should().BeTrue();
        session.SelectRange(WholeRows(active.Id, 3, 3));
        session.RepeatLastAction().Success.Should().BeTrue();

        foreach (var sheet in new[] { active, grouped })
        {
            sheet.RowOutlineLevels[2].Should().Be(1);
            sheet.RowOutlineLevels[3].Should().Be(2);
            sheet.RowOutlineLevels[4].Should().Be(1);
        }

        session.UngroupSelectedOutline().Success.Should().BeTrue();
        foreach (var sheet in new[] { active, grouped })
            sheet.RowOutlineLevels[3].Should().Be(1);

        session.ClearActiveWorksheetOutline().Success.Should().BeTrue();
        active.RowOutlineLevels.Should().BeEmpty();
        grouped.RowOutlineLevels.Should().BeEmpty();
    }

    [Fact]
    public void SplitToggle_DerivesTargetOnceAndAppliesItToGroupedSheets()
    {
        using var session = new WorkbookSessionFactory().CreateNew(240, 320);
        var active = session.ActiveSheet;
        var grouped = session.Workbook.AddSheet("Grouped");
        var address = new CellAddress(active.Id, 5, 4);
        session.SelectCell(address);
        session.SelectAllVisibleSheets();

        session.ToggleSplitPanesAtActiveCell().Success.Should().BeTrue();

        foreach (var sheet in new[] { active, grouped })
        {
            sheet.SplitRow.Should().Be(5);
            sheet.SplitColumn.Should().Be(4);
        }
        session.ActiveCell.Should().Be(address);

        session.ToggleSplitPanesAtActiveCell().Success.Should().BeTrue();
        foreach (var sheet in new[] { active, grouped })
        {
            sheet.SplitRow.Should().BeNull();
            sheet.SplitColumn.Should().BeNull();
        }
    }

    private static GridRange Range(SheetId sheetId, uint row1, uint col1, uint row2, uint col2) =>
        new(new CellAddress(sheetId, row1, col1), new CellAddress(sheetId, row2, col2));

    private static GridRange WholeRows(SheetId sheetId, uint startRow, uint endRow) =>
        Range(sheetId, startRow, 1, endRow, CellAddress.MaxCol);
}
