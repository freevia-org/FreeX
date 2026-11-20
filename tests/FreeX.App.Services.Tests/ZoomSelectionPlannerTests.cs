using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class ZoomSelectionPlannerTests
{
    private static readonly SheetId SheetId = SheetId.New();

    // R79-render-namebar-statusbar-5-4: Zoom-to-Selection must fit the bounding box of the WHOLE
    // multi-area (Ctrl+click) selection, not just the last-clicked active range.
    [Fact]
    public void ResolveFitRange_MultiAreaSelection_ReturnsBoundingBoxOfAllAreas()
    {
        var activeRange = new GridRange(new CellAddress(SheetId, 50, 26), new CellAddress(SheetId, 55, 27)); // Z50:AA55
        var selectedRanges = new[]
        {
            new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 2, 2)), // A1:B2
            activeRange,
        };

        var result = ZoomSelectionPlanner.ResolveFitRange(activeRange, selectedRanges);

        result.Should().Be(new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 55, 27)));
    }

    // No-regression sibling: a single active range (the common case) must be returned unchanged,
    // not widened by some spurious union with itself or a stale SelectedRanges list.
    [Fact]
    public void ResolveFitRange_SingleActiveRange_ReturnsPrimaryRangeUnchanged()
    {
        var activeRange = new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 10, 3));

        ZoomSelectionPlanner.ResolveFitRange(activeRange, selectedRanges: null)
            .Should().Be(activeRange);
        ZoomSelectionPlanner.ResolveFitRange(activeRange, selectedRanges: new[] { activeRange })
            .Should().Be(activeRange);
    }

    [Fact]
    public void CalculateFitWholePercent_RoundsSharedSelectionFitForCommandApplication()
    {
        ZoomSelectionPlanner.CalculateFitWholePercent(
                gridWidth: 813,
                gridHeight: 359,
                selectedColumns: 6,
                selectedRows: 7)
            .Should()
            .Be(169);
    }

    [Theory]
    [InlineData(10, 10, 100, 100, 10)]
    [InlineData(10000, 10000, 1, 1, 400)]
    public void CalculateFitWholePercent_ClampsToSupportedZoomRange(
        double gridWidth,
        double gridHeight,
        uint selectedColumns,
        uint selectedRows,
        int expected)
    {
        ZoomSelectionPlanner.CalculateFitWholePercent(gridWidth, gridHeight, selectedColumns, selectedRows)
            .Should()
            .Be(expected);
    }

    [Fact]
    public void CalculateFitWholePercent_UsesVisibleSizesAcrossLargeSelections()
    {
        var sheet = new Sheet(SheetId, "Data");
        var range = new GridRange(new CellAddress(SheetId, 1, 1), new CellAddress(SheetId, 5000, 3));
        sheet.ColumnWidths[1] = 20;
        sheet.HiddenCols.Add(2);
        sheet.GroupHiddenCols.Add(3);
        sheet.HiddenRows.UnionWith(Enumerable.Range(2, 4996).Select(row => (uint)row));
        sheet.FilterHiddenRows.Add(4998);
        sheet.GroupHiddenRows.Add(4999);
        sheet.RowHeights[1] = 75;
        sheet.RowHeights[5000] = 25;

        // The visible rows total 100 pixels. Even with a custom width, height determines the fit.
        ZoomSelectionPlanner.CalculateFitWholePercent(600, 175, sheet, range)
            .Should().Be(175);
    }
}
