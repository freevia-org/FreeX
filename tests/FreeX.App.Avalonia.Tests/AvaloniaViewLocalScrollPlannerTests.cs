using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaViewLocalScrollPlannerTests
{
    [Fact]
    public void Create_KeepsSiblingOriginsAndFreezeCountsIndependentOfSharedSheetState()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1")
        {
            ViewTopRow = 9,
            ViewLeftCol = 5,
            FrozenRows = 8,
            FrozenCols = 4
        };
        sheet.SetCell(new CellAddress(sheet.Id, 100, 40), new TextValue("used range"));
        var unfrozenViewport = new ViewportModel(
            [],
            [new RowMetric(20, 20, 0), new RowMetric(21, 20, 20), new RowMetric(22, 20, 40)],
            [new ColMetric(12, 64, 0), new ColMetric(13, 64, 64)]);
        var frozenViewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(5, 20, 20),
                new RowMetric(40, 20, 40), new RowMetric(41, 20, 60), new RowMetric(42, 20, 80)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64),
                new ColMetric(18, 64, 128), new ColMetric(19, 64, 192)]);

        var unfrozen = WorkbookViewportScrollPlanner.Create(sheet, unfrozenViewport, 20, 12, 0, 0);
        var frozen = WorkbookViewportScrollPlanner.Create(sheet, frozenViewport, 40, 18, 5, 2);

        unfrozen.Vertical.Value.Should().Be(20);
        unfrozen.Horizontal.Value.Should().Be(12);
        unfrozen.Vertical.Maximum.Should().Be(100);
        unfrozen.Horizontal.Maximum.Should().Be(40);
        frozen.Vertical.Value.Should().Be(35);
        frozen.Horizontal.Value.Should().Be(16);
        frozen.Vertical.Maximum.Should().Be(95);
        frozen.Horizontal.Maximum.Should().Be(38);

        // A different window can persist its own view without moving either live view's thumbs.
        sheet.ViewTopRow = 1;
        sheet.ViewLeftCol = 1;
        sheet.FrozenRows = 0;
        sheet.FrozenCols = 0;
        WorkbookViewportScrollPlanner.Create(sheet, unfrozenViewport, 20, 12, 0, 0).Should().Be(unfrozen);
        WorkbookViewportScrollPlanner.Create(sheet, frozenViewport, 40, 18, 5, 2).Should().Be(frozen);
    }

    [Fact]
    public void Create_UsesOnlyVisibleScrollableMetricsForThumbSizeAndPageStep()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20), new RowMetric(10, 0, 40),
                new RowMetric(20, 20, 40), new RowMetric(21, 20, 60), new RowMetric(22, 20, 80)],
            [new ColMetric(1, 64, 0), new ColMetric(10, 0, 64),
                new ColMetric(12, 64, 64), new ColMetric(13, 64, 128)]);

        var state = WorkbookViewportScrollPlanner.Create(sheet, viewport, 20, 12, 2, 1);

        state.Vertical.ViewportSize.Should().Be(3);
        state.Vertical.LargeChange.Should().Be(2);
        state.Horizontal.ViewportSize.Should().Be(2);
        state.Horizontal.LargeChange.Should().Be(1);
    }

    [Fact]
    public void Create_ClampsViewOriginSoTheScrollablePageStaysWithinWorksheetLimits()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20),
                new RowMetric(CellAddress.MaxRow - 2, 20, 40),
                new RowMetric(CellAddress.MaxRow - 1, 20, 60), new RowMetric(CellAddress.MaxRow, 20, 80)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64), new ColMetric(3, 64, 128),
                new ColMetric(CellAddress.MaxCol - 1, 64, 192), new ColMetric(CellAddress.MaxCol, 64, 256)]);

        var state = WorkbookViewportScrollPlanner.Create(
            sheet, viewport, CellAddress.MaxRow, CellAddress.MaxCol, 2, 3);

        state.Vertical.Maximum.Should().Be(CellAddress.MaxRow - 4);
        state.Vertical.Value.Should().Be(state.Vertical.Maximum);
        state.Vertical.Value.Should().BeGreaterThanOrEqualTo(state.Vertical.Minimum);
        (state.Vertical.Maximum + 2 + state.Vertical.ViewportSize - 1).Should().Be(CellAddress.MaxRow);
        state.Horizontal.Maximum.Should().Be(CellAddress.MaxCol - 4);
        state.Horizontal.Value.Should().Be(state.Horizontal.Maximum);
        state.Horizontal.Value.Should().BeGreaterThanOrEqualTo(state.Horizontal.Minimum);
        (state.Horizontal.Maximum + 3 + state.Horizontal.ViewportSize - 1).Should().Be(CellAddress.MaxCol);
    }

    [Fact]
    public void Create_RetainsLiveOriginBeyondUsedRangeWithoutChangingPersistedOrigin()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1") { ViewTopRow = 1, ViewLeftCol = 1 };
        var viewport = new ViewportModel(
            [],
            [new RowMetric(400, 20, 0), new RowMetric(401, 20, 20)],
            [new ColMetric(80, 64, 0), new ColMetric(81, 64, 64)]);

        var state = WorkbookViewportScrollPlanner.Create(sheet, viewport, 400, 80, 0, 0);

        state.Vertical.Value.Should().Be(400);
        state.Vertical.Maximum.Should().Be(400);
        state.Horizontal.Value.Should().Be(80);
        state.Horizontal.Maximum.Should().Be(80);
        sheet.ViewTopRow.Should().Be(1);
        sheet.ViewLeftCol.Should().Be(1);
    }

    [Fact]
    public void Create_SheetOverloadKeepsPersistedOriginsAndFirstScrollableCellFallback()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1") { FrozenRows = 2, FrozenCols = 3 };
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0), new RowMetric(2, 20, 20), new RowMetric(9, 20, 40)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 64, 64), new ColMetric(3, 64, 128),
                new ColMetric(10, 64, 192)]);

        var initial = WorkbookViewportScrollPlanner.Create(sheet, viewport);
        initial.Vertical.Value.Should().Be(1);
        initial.Horizontal.Value.Should().Be(1);

        sheet.ViewTopRow = 9;
        sheet.ViewLeftCol = 10;
        var persisted = WorkbookViewportScrollPlanner.Create(sheet, viewport);
        persisted.Vertical.Value.Should().Be(7);
        persisted.Horizontal.Value.Should().Be(7);
    }
}
