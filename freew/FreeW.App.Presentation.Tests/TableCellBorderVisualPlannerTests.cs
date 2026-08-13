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
    public void Build_WaveEdgesCarrySharedRasterPlan()
    {
        var plan = TableCellBorderVisualPlanner.Build(new CellBorders
        {
            Top = new CellBorderEdge(BorderLineStyle.Wave, "#C00000", 1.0)
        });

        var top = plan.Edge(TableCellBorderVisualEdge.Top);
        top.IsWave.Should().BeTrue();
        top.FallbackNote.Should().BeNull();
        top.StrokeOpacity.Should().BeApproximately(86.0 / 255.0, 0.001);

        var offsets = TableCellBorderVisualPlanner.BuildWaveOffsets(16);
        offsets.Should().ContainInOrder(
            new TableCellBorderWavePoint(0, 0),
            new TableCellBorderWavePoint(1, TableCellBorderVisualPlanner.WaveAmplitudeDip * (1 - Math.Sqrt(0.5)) / 2));
        offsets.Single(point => point.AlongDip == 4).OutwardDip
            .Should().BeApproximately(TableCellBorderVisualPlanner.WaveAmplitudeDip, 0.001);
        offsets.Single(point => point.AlongDip == 8).OutwardDip.Should().BeApproximately(0, 0.001);
        offsets[^1].Should().Be(new TableCellBorderWavePoint(16, 0));
    }

    [Theory]
    [InlineData(TableCellBorderVisualEdge.Top, 10, 21, 40, 21)]
    [InlineData(TableCellBorderVisualEdge.Bottom, 10, 49, 40, 49)]
    [InlineData(TableCellBorderVisualEdge.Left, 11, 20, 11, 50)]
    [InlineData(TableCellBorderVisualEdge.Right, 39, 20, 39, 50)]
    public void ProjectEdgeSegment_MapsEachEdgeWithInwardOffset(
        TableCellBorderVisualEdge edge,
        double expectedX1,
        double expectedY1,
        double expectedX2,
        double expectedY2)
    {
        var segment = TableCellBorderVisualPlanner.ProjectEdgeSegment(
            edge,
            leftDip: 10,
            topDip: 20,
            rightDip: 40,
            bottomDip: 50,
            inwardOffsetDip: 1);

        segment.Should().Be(new TableCellBorderVisualSegment(
            expectedX1,
            expectedY1,
            expectedX2,
            expectedY2));
    }

    [Fact]
    public void ProjectEdgeSegment_NegativeOffsetMapsOutsideEdge()
    {
        var segment = TableCellBorderVisualPlanner.ProjectEdgeSegment(
            TableCellBorderVisualEdge.Right,
            leftDip: 10,
            topDip: 20,
            rightDip: 40,
            bottomDip: 50,
            inwardOffsetDip: -2);

        segment.Should().Be(new TableCellBorderVisualSegment(42, 20, 42, 50));
    }

    [Fact]
    public void ProjectEdgeSegment_UnknownEdgePreservesTopEdgeFallback()
    {
        var segment = TableCellBorderVisualPlanner.ProjectEdgeSegment(
            (TableCellBorderVisualEdge)99,
            leftDip: 10,
            topDip: 20,
            rightDip: 40,
            bottomDip: 50,
            inwardOffsetDip: 3);

        segment.Should().Be(new TableCellBorderVisualSegment(10, 20, 40, 20));
    }

    [Fact]
    public void BuildStrokeSegments_ProjectsDoubleBordersOnceForBothRenderers()
    {
        var edge = new TableCellBorderEdgeVisualPlan(
            TableCellBorderVisualEdge.Top,
            IsVisible: true,
            BorderLineStyle.Double,
            "#000000",
            WidthDip: 2,
            FallbackNote: null);

        var segments = TableCellBorderVisualPlanner.BuildStrokeSegments(
            edge,
            leftDip: 10,
            topDip: 20,
            rightDip: 40,
            bottomDip: 50,
            waveRegistrationDip: 0);

        segments.Should().Equal(
            new TableCellBorderVisualSegment(10, 18.5, 40, 18.5),
            new TableCellBorderVisualSegment(10, 21.5, 40, 21.5));
    }

    [Fact]
    public void BuildStrokeSegments_PreservesRendererSuppliedWaveRegistration()
    {
        var edge = new TableCellBorderEdgeVisualPlan(
            TableCellBorderVisualEdge.Top,
            IsVisible: true,
            BorderLineStyle.Wave,
            "#000000",
            WidthDip: 1,
            FallbackNote: null);

        var wpf = TableCellBorderVisualPlanner.BuildStrokeSegments(
            edge, 10, 20, 18, 50, waveRegistrationDip: 2);
        var avalonia = TableCellBorderVisualPlanner.BuildStrokeSegments(
            edge, 10, 20, 18, 50, waveRegistrationDip: -4);

        wpf.Should().HaveCount(8);
        avalonia.Should().HaveCount(8);
        wpf[0].Y1Dip.Should().Be(18);
        avalonia[0].Y1Dip.Should().Be(24);
        wpf[0].X1Dip.Should().Be(avalonia[0].X1Dip);
        wpf[0].X2Dip.Should().Be(avalonia[0].X2Dip);
    }

    [Fact]
    public void BuildStrokeSegments_SuppressesInvisibleEdges()
    {
        var edge = new TableCellBorderEdgeVisualPlan(
            TableCellBorderVisualEdge.Left,
            IsVisible: false,
            BorderLineStyle.Single,
            "#000000",
            WidthDip: 1,
            FallbackNote: null);

        TableCellBorderVisualPlanner.BuildStrokeSegments(
            edge, 10, 20, 40, 50, waveRegistrationDip: 0).Should().BeEmpty();
    }
}
