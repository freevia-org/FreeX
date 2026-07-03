using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class TableCellBorderVisualPlannerTests
{
    [Fact]
    public void Build_PreservesPerEdgeStyleColorAndScaledWidth()
    {
        var plan = TableCellBorderVisualPlanner.Build(new CellBorders
        {
            Top = new CellBorderEdge(BorderLineStyle.Dashed, "#FF0000", 1.25),
            Bottom = new CellBorderEdge(BorderLineStyle.Dotted, "00B050", 0.75),
            Left = new CellBorderEdge(BorderLineStyle.Double, "#1F4E79", 1.0),
            Right = new CellBorderEdge(BorderLineStyle.Single, "#7030A0", 0.5)
        }, dipPerPoint: 2);

        plan.HasVisibleEdges.Should().BeTrue();
        plan.HasWordVisibleStyleEdges.Should().BeTrue();
        plan.HasMixedVisibleColors.Should().BeTrue();

        plan.Edge(TableCellBorderVisualEdge.Top).Style.Should().Be(BorderLineStyle.Dashed);
        plan.Edge(TableCellBorderVisualEdge.Top).ColorHex.Should().Be("#FF0000");
        plan.Edge(TableCellBorderVisualEdge.Top).WidthDip.Should().BeApproximately(2.5, 0.001);

        plan.Edge(TableCellBorderVisualEdge.Bottom).Style.Should().Be(BorderLineStyle.Dotted);
        plan.Edge(TableCellBorderVisualEdge.Bottom).ColorHex.Should().Be("#00B050");
        plan.Edge(TableCellBorderVisualEdge.Bottom).WidthDip.Should().BeApproximately(1.5, 0.001);

        plan.Edge(TableCellBorderVisualEdge.Left).IsDouble.Should().BeTrue();
        plan.Edge(TableCellBorderVisualEdge.Right).Style.Should().Be(BorderLineStyle.Single);
    }

    [Fact]
    public void Build_ThickEdgesUseVisibleMinimumWidth()
    {
        var plan = TableCellBorderVisualPlanner.Build(new CellBorders
        {
            Left = new CellBorderEdge(BorderLineStyle.Thick, "#000000", 0.25)
        });

        var left = plan.Edge(TableCellBorderVisualEdge.Left);
        left.IsVisible.Should().BeTrue();
        left.Style.Should().Be(BorderLineStyle.Thick);
        left.WidthDip.Should().Be(TableCellBorderVisualPlanner.MinimumThickBorderWidthDip);
    }

    [Fact]
    public void Build_NullEdgesRemainInPlanAsInvisibleEdges()
    {
        var plan = TableCellBorderVisualPlanner.Build(new CellBorders
        {
            Top = new CellBorderEdge(BorderLineStyle.Single, "#000000", 1.0)
        });

        plan.Edges.Should().HaveCount(4);
        plan.Edge(TableCellBorderVisualEdge.Top).IsVisible.Should().BeTrue();
        plan.Edge(TableCellBorderVisualEdge.Bottom).IsVisible.Should().BeFalse();
        plan.Edge(TableCellBorderVisualEdge.Left).IsVisible.Should().BeFalse();
        plan.Edge(TableCellBorderVisualEdge.Right).IsVisible.Should().BeFalse();
    }

    [Fact]
    public void Build_WaveEdgesCarryHostFallbackNote()
    {
        var plan = TableCellBorderVisualPlanner.Build(new CellBorders
        {
            Top = new CellBorderEdge(BorderLineStyle.Wave, "#C00000", 1.0)
        });

        plan.Edge(TableCellBorderVisualEdge.Top).FallbackNote.Should().Contain("Wave cell borders");
    }
}
