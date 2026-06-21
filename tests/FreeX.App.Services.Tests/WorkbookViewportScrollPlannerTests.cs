using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookViewportScrollPlannerTests
{
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
}
