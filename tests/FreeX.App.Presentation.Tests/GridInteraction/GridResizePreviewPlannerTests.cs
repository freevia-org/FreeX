using FluentAssertions;
using FreeX.App.Presentation.GridInteraction;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.GridInteraction;

public sealed class GridResizePreviewPlannerTests
{
    [Fact]
    public void GetColumnResizeRange_SelectedColumnInsideMultiColumnSelection_UsesSelection()
    {
        var sheet = CreateSheet();
        var range = Range(sheet, startRow: 1, startCol: 2, endRow: CellAddress.MaxRow, endCol: 4);

        GridResizePreviewPlanner.GetColumnResizeRange(sheet, range, column: 3)
            .Should()
            .Be((2u, 4u));
    }

    [Fact]
    public void GetColumnResizeRange_RowSpanningCellSelection_DoesNotBecomeWholeColumnBand()
    {
        var sheet = CreateSheet();
        var range = Range(sheet, startRow: 1, startCol: 2, endRow: 8, endCol: 4);

        GridResizePreviewPlanner.GetColumnResizeRange(sheet, range, column: 3)
            .Should()
            .Be((3u, 3u));
    }

    [Fact]
    public void GetColumnResizeRange_HiddenColumn_UsesContiguousHiddenRange()
    {
        var sheet = CreateSheet();
        sheet.HiddenCols.Add(2);
        sheet.HiddenCols.Add(3);
        sheet.HiddenCols.Add(4);

        GridResizePreviewPlanner.GetColumnResizeRange(sheet, Range(sheet, 1, 1, 1, 5), column: 3)
            .Should()
            .Be((2u, 4u));
    }

    [Fact]
    public void ApplyColumnResizePreview_ZeroWidthHidesColumns()
    {
        var sheet = CreateSheet();
        sheet.ColumnWidths[2] = 12;
        sheet.ColumnWidths[3] = 14;

        GridResizePreviewPlanner.ApplyColumnResizePreview(sheet, startColumn: 2, endColumn: 3, widthPixels: 0);

        sheet.ColumnWidths.Should().NotContainKeys(2u, 3u);
        sheet.HiddenCols.Should().Contain([2u, 3u]);
    }

    [Fact]
    public void RestoreColumnResizePreview_RestoresWidthsAndHiddenColumns()
    {
        var sheet = CreateSheet();
        sheet.ColumnWidths[2] = 12;
        sheet.HiddenCols.Add(3);
        var snapshot = GridResizePreviewPlanner.CaptureColumnSnapshot(sheet, startColumn: 2, endColumn: 3);

        GridResizePreviewPlanner.ApplyColumnResizePreview(sheet, startColumn: 2, endColumn: 3, widthPixels: 96);
        GridResizePreviewPlanner.RestoreColumnResizePreview(sheet, snapshot).Should().BeTrue();

        sheet.ColumnWidths.Should().Contain(2u, 12);
        sheet.ColumnWidths.Should().NotContainKey(3u);
        sheet.HiddenCols.Should().Equal(3u);
    }

    [Fact]
    public void GetRowResizeRange_HiddenRow_UsesContiguousHiddenRange()
    {
        var sheet = CreateSheet();
        sheet.HiddenRows.Add(5);
        sheet.HiddenRows.Add(6);

        GridResizePreviewPlanner.GetRowResizeRange(sheet, Range(sheet, 1, 1, 8, 1), row: 5)
            .Should()
            .Be((5u, 6u));
    }

    [Fact]
    public void GetRowResizeRange_ColumnSpanningCellSelection_DoesNotBecomeWholeRowBand()
    {
        var sheet = CreateSheet();
        var range = Range(sheet, startRow: 2, startCol: 1, endRow: 4, endCol: 8);

        GridResizePreviewPlanner.GetRowResizeRange(sheet, range, row: 3)
            .Should()
            .Be((3u, 3u));
    }

    [Fact]
    public void ApplyAndRestoreRowResizePreview_RestoresHeightsAndHiddenRows()
    {
        var sheet = CreateSheet();
        sheet.RowHeights[2] = 20;
        sheet.HiddenRows.Add(3);
        var snapshot = GridResizePreviewPlanner.CaptureRowSnapshot(sheet, startRow: 2, endRow: 3);

        GridResizePreviewPlanner.ApplyRowResizePreview(sheet, startRow: 2, endRow: 3, heightPixels: 0);
        sheet.RowHeights.Should().NotContainKeys(2u, 3u);
        sheet.HiddenRows.Should().Contain([2u, 3u]);

        GridResizePreviewPlanner.RestoreRowResizePreview(sheet, snapshot).Should().BeTrue();

        sheet.RowHeights.Should().Contain(2u, 20);
        sheet.RowHeights.Should().NotContainKey(3u);
        sheet.HiddenRows.Should().Equal(3u);
    }

    [Fact]
    public void ApplyColumnResizePreview_ConvertsPixelsToColumnWidth()
    {
        var sheet = CreateSheet();

        GridResizePreviewPlanner.ApplyColumnResizePreview(sheet, startColumn: 4, endColumn: 4, widthPixels: 144);

        sheet.ColumnWidths[4].Should().BeApproximately(ColumnWidthPixelMapper.PixelsToColumnWidth(144), 0.0001);
    }

    private static Sheet CreateSheet() => new(SheetId.New(), "Sheet1");

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(
            new CellAddress(sheet.Id, startRow, startCol),
            new CellAddress(sheet.Id, endRow, endCol));
}
