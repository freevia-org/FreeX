using FluentAssertions;
using Freexcel.Core.Model;
using System.Windows;

namespace Freexcel.App.Host.Tests;

public sealed class QuickAnalysisMenuPlacementPlannerTests
{
    [Fact]
    public void BuildAnchor_PlacesMenuAtVisibleSelectionBottomRight()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 4, 4));
        var viewport = new ViewportModel(
            [],
            [
                new RowMetric(1, 20, 0),
                new RowMetric(2, 20, 20),
                new RowMetric(3, 20, 40),
                new RowMetric(4, 20, 60)
            ],
            [
                new ColMetric(1, 80, 0),
                new ColMetric(2, 80, 80),
                new ColMetric(3, 80, 160),
                new ColMetric(4, 80, 240)
            ]);

        var anchor = QuickAnalysisMenuPlacementPlanner.BuildAnchor(
            selection,
            viewport,
            rowHeaderWidth: 44,
            columnHeaderHeight: 24);

        anchor.Should().Be(new Point(368, 108));
    }

    [Fact]
    public void BuildAnchor_UsesVisibleSelectionEdgeWhenRangeExtendsOffscreen()
    {
        var sheetId = SheetId.New();
        var selection = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 20, 20));
        var viewport = new ViewportModel(
            [],
            [
                new RowMetric(2, 20, 20),
                new RowMetric(3, 20, 40),
                new RowMetric(4, 20, 60)
            ],
            [
                new ColMetric(2, 80, 80),
                new ColMetric(3, 80, 160)
            ]);

        var anchor = QuickAnalysisMenuPlacementPlanner.BuildAnchor(
            selection,
            viewport,
            rowHeaderWidth: 44,
            columnHeaderHeight: 24);

        anchor.Should().Be(new Point(288, 108));
    }
}
