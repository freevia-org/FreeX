using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// Covers the consolidated neutral owners of what both renderers used to keep as private copies:
/// WorkbookViewportScrollPlanner.CountVisibleScrollableRows/Columns (was MainWindow.Viewport.cs's
/// and Avalonia MainWindow.cs's private CountScrollableRows/Columns) and
/// GetViewportRowOrigin/GetViewportColumnOrigin (was the `ViewTopRow ?? FrozenRows + 1` fallback
/// open-coded in Avalonia MainWindow.RibbonMenuWires.cs's ShiftScrollOriginForRowEdit/ColEdit).
/// </summary>
public sealed class WorkbookViewportScrollableCountTests
{
    private static ViewportModel Viewport(
        IReadOnlyList<RowMetric> rows,
        IReadOnlyList<ColMetric> cols,
        FrozenPaneState? frozenPanes = null) =>
        new([], rows, cols, FrozenPanes: frozenPanes);

    private static IReadOnlyList<RowMetric> Rows(params (uint Row, double Height)[] rows)
    {
        var result = new List<RowMetric>(rows.Length);
        double offset = 0;
        foreach (var (row, height) in rows)
        {
            result.Add(new RowMetric(row, height, offset));
            offset += height;
        }

        return result;
    }

    private static IReadOnlyList<ColMetric> Cols(params (uint Col, double Width)[] cols)
    {
        var result = new List<ColMetric>(cols.Length);
        double offset = 0;
        foreach (var (col, width) in cols)
        {
            result.Add(new ColMetric(col, width, offset));
            offset += width;
        }

        return result;
    }

    [Fact]
    public void CountVisibleScrollableRows_CountsEveryUnfrozenNonZeroHeightRow()
    {
        var viewport = Viewport(
            Rows((1, 20), (2, 20), (3, 20), (4, 20)),
            Cols((1, 64)));

        WorkbookViewportScrollPlanner.CountVisibleScrollableRows(viewport, 0u).Should().Be(4);
    }

    [Fact]
    public void CountVisibleScrollableColumns_CountsEveryUnfrozenNonZeroWidthColumn()
    {
        var viewport = Viewport(
            Rows((1, 20)),
            Cols((1, 64), (2, 64), (3, 64)));

        WorkbookViewportScrollPlanner.CountVisibleScrollableColumns(viewport, 0u).Should().Be(3);
    }

    [Fact]
    public void CountVisibleScrollableRows_ExcludesFrozenRows()
    {
        var viewport = Viewport(
            Rows((1, 20), (2, 20), (10, 20), (11, 20)),
            Cols((1, 64)));

        WorkbookViewportScrollPlanner.CountVisibleScrollableRows(viewport, 2u).Should().Be(2);
    }

    [Fact]
    public void CountVisibleScrollableColumns_ExcludesFrozenColumns()
    {
        var viewport = Viewport(
            Rows((1, 20)),
            Cols((1, 64), (5, 64), (6, 64)));

        WorkbookViewportScrollPlanner.CountVisibleScrollableColumns(viewport, 1u).Should().Be(2);
    }

    [Fact]
    public void CountVisibleScrollableRows_ExcludesHiddenAndScrolledPastMergeAnchorPlaceholders()
    {
        // R110: PrependScrolledPastMergeAnchorRows materializes zero-height placeholder metrics
        // for a merge anchor that scrolled above the window; hidden rows also arrive as height 0.
        var viewport = Viewport(
            Rows((5, 0), (6, 0), (7, 20), (8, 0), (9, 20)),
            Cols((1, 64)));

        WorkbookViewportScrollPlanner.CountVisibleScrollableRows(viewport, 0u).Should().Be(2);
    }

    [Fact]
    public void CountVisibleScrollableColumns_ExcludesHiddenAndScrolledPastMergeAnchorPlaceholders()
    {
        var viewport = Viewport(
            Rows((1, 20)),
            Cols((3, 0), (4, 0), (5, 64), (6, 0)));

        WorkbookViewportScrollPlanner.CountVisibleScrollableColumns(viewport, 0u).Should().Be(1);
    }

    [Fact]
    public void CountVisibleScrollableRows_EmptyViewportFloorsAtOne()
    {
        var viewport = Viewport(Rows(), Cols());

        WorkbookViewportScrollPlanner.CountVisibleScrollableRows(viewport, 0u).Should().Be(1);
        WorkbookViewportScrollPlanner.CountVisibleScrollableColumns(viewport, 0u).Should().Be(1);
    }

    [Fact]
    public void CountVisibleScrollableRows_AllRowsFrozenOrZeroHeightFloorsAtOne()
    {
        // Page Up/Down subtracts 1 from this count, so a zero would make the page step nonsensical;
        // the floor of 1 is what keeps Math.Max(1, count - 1) at a sane single-row step.
        var viewport = Viewport(
            Rows((1, 20), (2, 20)),
            Cols((1, 64), (2, 64)));

        WorkbookViewportScrollPlanner.CountVisibleScrollableRows(viewport, 5u).Should().Be(1);
        WorkbookViewportScrollPlanner.CountVisibleScrollableColumns(viewport, 5u).Should().Be(1);
    }

    [Fact]
    public void CountVisibleScrollableRows_HugeFrozenCountStillFloorsAtOne()
    {
        var viewport = Viewport(Rows((1, 20)), Cols((1, 64)));

        WorkbookViewportScrollPlanner
            .CountVisibleScrollableRows(viewport, CellAddress.MaxRow)
            .Should()
            .Be(1);
        WorkbookViewportScrollPlanner
            .CountVisibleScrollableColumns(viewport, CellAddress.MaxCol)
            .Should()
            .Be(1);
    }

    [Fact]
    public void CountVisibleScrollableRows_SheetOverloadUsesSheetFrozenCounts()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1") { FrozenRows = 2, FrozenCols = 1 };
        var viewport = Viewport(
            Rows((1, 20), (2, 20), (3, 20), (4, 20)),
            Cols((1, 64), (2, 64), (3, 64)));

        WorkbookViewportScrollPlanner.CountVisibleScrollableRows(viewport, sheet).Should().Be(2);
        WorkbookViewportScrollPlanner.CountVisibleScrollableColumns(viewport, sheet).Should().Be(2);
    }

    [Fact]
    public void CountVisibleScrollableRows_SheetOverloadTreatsNullSheetAsUnfrozen()
    {
        var viewport = Viewport(
            Rows((1, 20), (2, 20)),
            Cols((1, 64), (2, 64)));

        WorkbookViewportScrollPlanner.CountVisibleScrollableRows(viewport, (Sheet?)null).Should().Be(2);
        WorkbookViewportScrollPlanner.CountVisibleScrollableColumns(viewport, (Sheet?)null).Should().Be(2);
    }

    [Fact]
    public void CountVisibleScrollableRows_NullViewportThrows()
    {
        var act = () => WorkbookViewportScrollPlanner.CountVisibleScrollableRows(null!, 0u);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetViewportOrigin_UsesPersistedOriginWhenPresent()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1")
        {
            FrozenRows = 3,
            FrozenCols = 2,
            ViewTopRow = 42,
            ViewLeftCol = 7,
        };

        WorkbookViewportScrollPlanner.GetViewportRowOrigin(sheet).Should().Be(42u);
        WorkbookViewportScrollPlanner.GetViewportColumnOrigin(sheet).Should().Be(7u);
    }

    [Fact]
    public void GetViewportOrigin_FallsBackToFirstRowPastTheFrozenPane()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1") { FrozenRows = 3, FrozenCols = 2 };

        WorkbookViewportScrollPlanner.GetViewportRowOrigin(sheet).Should().Be(4u);
        WorkbookViewportScrollPlanner.GetViewportColumnOrigin(sheet).Should().Be(3u);
    }

    [Fact]
    public void GetViewportOrigin_UnfrozenSheetFallsBackToRowOneColumnOne()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");

        WorkbookViewportScrollPlanner.GetViewportRowOrigin(sheet).Should().Be(1u);
        WorkbookViewportScrollPlanner.GetViewportColumnOrigin(sheet).Should().Be(1u);
    }

    [Fact]
    public void GetViewportOrigin_FallbackClampsAtSheetBounds()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1")
        {
            FrozenRows = CellAddress.MaxRow,
            FrozenCols = CellAddress.MaxCol,
        };

        WorkbookViewportScrollPlanner.GetViewportRowOrigin(sheet).Should().Be(CellAddress.MaxRow);
        WorkbookViewportScrollPlanner.GetViewportColumnOrigin(sheet).Should().Be(CellAddress.MaxCol);
    }

    [Theory]
    [InlineData(10u, 5u, 3, 13u)]
    [InlineData(10u, 10u, 2, 12u)]
    [InlineData(10u, 5u, -3, 7u)]
    [InlineData(2u, 1u, -5, 1u)]
    public void PlanStructuralEditOriginShift_ShiftsAndClampsEditsAtOrAboveTheOrigin(
        uint origin,
        uint editIndex,
        int delta,
        uint expected)
    {
        WorkbookViewportScrollPlanner
            .PlanStructuralEditOriginShift(origin, editIndex, delta, CellAddress.MaxRow)
            .Should()
            .Be(expected);
    }

    [Theory]
    [InlineData(10u, 11u, 3)]
    [InlineData(10u, 5u, 0)]
    public void PlanStructuralEditOriginShift_LeavesTheOriginAloneBelowTheViewOrWithNoDelta(
        uint origin,
        uint editIndex,
        int delta)
    {
        WorkbookViewportScrollPlanner
            .PlanStructuralEditOriginShift(origin, editIndex, delta, CellAddress.MaxRow)
            .Should()
            .BeNull();
    }

    [Fact]
    public void PlanStructuralEditOriginShift_ClampsAtTheUpperSheetBound()
    {
        WorkbookViewportScrollPlanner
            .PlanStructuralEditOriginShift(CellAddress.MaxRow, 1, 100, CellAddress.MaxRow)
            .Should()
            .Be(CellAddress.MaxRow);
    }
}
