using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookViewportScrollPlannerTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(-0.25, -1)]
    [InlineData(3, 3)]
    [InlineData(-3.75, -3)]
    public void NormalizePointerWheelNotches_PreservesCoalescedPointerDelta(double delta, int expected)
    {
        WorkbookViewportScrollPlanner.NormalizePointerWheelNotches(delta).Should().Be(expected);
    }

    [Fact]
    public void CalculateViewportOrigin_DoesNotScrollToFrozenPaneBoundary()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1") { FrozenRows = 1, FrozenCols = 2 };

        WorkbookViewportScrollPlanner.CalculateViewportOrigin(
                sheet,
                verticalScrollValue: 1,
                horizontalScrollValue: 1)
            .Should().Be((2u, 3u));
    }

    [Fact]
    public void Create_MapsFrozenViewportOriginToScrollbarValue()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1")
        {
            FrozenRows = 1,
            FrozenCols = 1,
            ViewTopRow = 5,
            ViewLeftCol = 4
        };
        var viewport = new ViewportModel(
            [],
            [
                new RowMetric(1, 20, 0),
                new RowMetric(5, 20, 20),
                new RowMetric(6, 20, 40),
                new RowMetric(7, 20, 60),
            ],
            [
                new ColMetric(1, 64, 0),
                new ColMetric(4, 64, 64),
                new ColMetric(5, 64, 128),
            ]);

        var state = WorkbookViewportScrollPlanner.Create(sheet, viewport);

        state.Vertical.Value.Should().Be(4);
        state.Vertical.ViewportSize.Should().Be(3);
        state.Vertical.LargeChange.Should().Be(2);
        state.Horizontal.Value.Should().Be(3);
        state.Horizontal.ViewportSize.Should().Be(2);
        state.Horizontal.LargeChange.Should().Be(1);
    }

    [Fact]
    public void Create_UsesVisibleSpanAsMinimumScrollbarMaximum()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        var viewport = new ViewportModel(
            [],
            Enumerable.Range(1, 40)
                .Select(row => new RowMetric((uint)row, 20, (row - 1) * 20))
                .ToList(),
            Enumerable.Range(1, 20)
                .Select(col => new ColMetric((uint)col, 64, (col - 1) * 64))
                .ToList());

        var state = WorkbookViewportScrollPlanner.Create(sheet, viewport);

        state.Vertical.Maximum.Should().Be(40);
        state.Horizontal.Maximum.Should().Be(20);
        state.Vertical.IsEnabled.Should().BeTrue();
        state.Horizontal.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Create_UsesUsedRangeForScrollbarMaximum()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 80, 35), new TextValue("tail"));
        var viewport = new ViewportModel(
            [],
            Enumerable.Range(1, 40)
                .Select(row => new RowMetric((uint)row, 20, (row - 1) * 20))
                .ToList(),
            Enumerable.Range(1, 20)
                .Select(col => new ColMetric((uint)col, 64, (col - 1) * 64))
                .ToList());

        var state = WorkbookViewportScrollPlanner.Create(sheet, viewport);

        state.Vertical.Maximum.Should().Be(80);
        state.Horizontal.Maximum.Should().Be(35);
        state.Vertical.IsEnabled.Should().BeTrue();
        state.Horizontal.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Create_ClampsCurrentValueToMaximumWhenViewportShowsWholeAxis()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1") { ViewLeftCol = 10 };
        var viewport = new ViewportModel(
            [],
            [new RowMetric(1, 20, 0)],
            Enumerable.Range(1, (int)CellAddress.MaxCol)
                .Select(col => new ColMetric((uint)col, 64, (col - 1) * 64))
                .ToList());

        var state = WorkbookViewportScrollPlanner.Create(sheet, viewport);

        state.Horizontal.Maximum.Should().Be(1);
        state.Horizontal.Value.Should().Be(1);
        state.Horizontal.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void PlanCellReveal_PlansScrollbarValuesAcrossFrozenPanes()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1") { FrozenRows = 2, FrozenCols = 1 };
        var viewport = new ViewportModel(
            [],
            [
                new RowMetric(1, 20, 0),
                new RowMetric(2, 20, 20),
                new RowMetric(10, 20, 40),
                new RowMetric(11, 20, 60),
                new RowMetric(12, 20, 80),
            ],
            [
                new ColMetric(1, 64, 0),
                new ColMetric(4, 64, 64),
                new ColMetric(5, 64, 128),
                new ColMetric(6, 64, 192),
            ],
            FrozenPanes: new FrozenPaneState(sheet.FrozenRows, sheet.FrozenCols));

        var plan = WorkbookViewportScrollPlanner.PlanCellReveal(
            viewport,
            sheet,
            new CellAddress(sheet.Id, 18, 9),
            currentVerticalMaximum: 12,
            currentHorizontalMaximum: 5);

        plan.Vertical.ShouldScroll.Should().BeTrue();
        plan.Vertical.Value.Should().Be(14);
        plan.Vertical.Maximum.Should().Be(14);
        plan.Horizontal.ShouldScroll.Should().BeTrue();
        plan.Horizontal.Value.Should().Be(6);
        plan.Horizontal.Maximum.Should().Be(6);
    }

    [Fact]
    public void PlanCellReveal_LeavesFrozenOrVisibleTargetsUnchanged()
    {
        var sheet = new Sheet(SheetId.New(), "Sheet1") { FrozenRows = 2, FrozenCols = 1 };
        var viewport = new ViewportModel(
            [],
            [
                new RowMetric(1, 20, 0),
                new RowMetric(2, 20, 20),
                new RowMetric(10, 20, 40),
                new RowMetric(11, 20, 60),
                new RowMetric(12, 20, 80),
            ],
            [
                new ColMetric(1, 64, 0),
                new ColMetric(4, 64, 64),
                new ColMetric(5, 64, 128),
                new ColMetric(6, 64, 192),
            ],
            FrozenPanes: new FrozenPaneState(sheet.FrozenRows, sheet.FrozenCols));

        var plan = WorkbookViewportScrollPlanner.PlanCellReveal(
            viewport,
            sheet,
            new CellAddress(sheet.Id, 2, 5),
            currentVerticalMaximum: 12,
            currentHorizontalMaximum: 5);

        plan.Vertical.ShouldScroll.Should().BeFalse();
        plan.Vertical.Maximum.Should().Be(12);
        plan.Horizontal.ShouldScroll.Should().BeFalse();
        plan.Horizontal.Maximum.Should().Be(5);
    }
}
