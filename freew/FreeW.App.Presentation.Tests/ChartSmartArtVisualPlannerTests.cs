using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class ChartSmartArtVisualPlannerTests
{
    [Fact]
    public void ChartPlan_ResolvesPaletteStyleAndLayoutFlags()
    {
        var chart = Chart.Create(ChartKind.Column, ["A", "B"], [1.0, 2.0], title: "Revenue");
        chart.ColorSchemeId = " mono-blue ";
        chart.StyleId = 7;
        chart.QuickLayoutId = 9;
        chart.CategoryAxisTitle = "Quarter";
        chart.ValueAxisTitle = "USD";

        var plan = ChartSmartArtVisualPlanner.BuildChartPlan(chart);

        plan.ColorSchemeId.Should().Be("mono-blue");
        plan.StyleId.Should().Be(7);
        plan.QuickLayoutId.Should().Be(9);
        plan.PaletteHex[0].Should().Be("#214A82");
        plan.ShowTitle.Should().BeTrue();
        plan.ShowLegend.Should().BeTrue();
        plan.ShowGridlines.Should().BeTrue();
        plan.PlotAreaFill.Should().BeTrue();
        plan.ShowMarkers.Should().BeTrue();
        plan.ShowDataLabels.Should().BeTrue();
        plan.CategoryAxisTitle.Should().Be("Quarter");
        plan.ValueAxisTitle.Should().Be("USD");
        plan.Categories.Should().ContainInOrder("A", "B");
        plan.Series.Should().ContainSingle()
            .Which.Values.Should().ContainInOrder(1.0, 2.0);

        var signature = ChartSmartArtVisualPlanner.BuildChartVisualSignature(plan);
        signature.Should().Contain("colorScheme=mono-blue");
        signature.Should().Contain("quickLayout=9");
        signature.Should().Contain("plotFill=1");
        signature.Should().Contain("dataLabels=1");
        signature.Should().Contain("axisTitles=1");
        signature.Should().Contain("palette=#214A82,#2E5FAA,#4472C4,#6C8FD1,#A9C1E7,#D6E4F4");

        ChartSmartArtVisualPlanner.BuildChartDataSignature(plan)
            .Should().Be("kind=Column|categories=2|categoryLabels=A,B|series=1|points=2|seriesData=0:-=1,2");
    }

    [Fact]
    public void ChartPlan_QuickLayoutCanSuppressModelLegendAndAxisTitles()
    {
        var chart = Chart.Create(ChartKind.Line, ["A"], [1.0], title: "Hidden");
        chart.ShowLegend = true;
        chart.CategoryAxisTitle = "Category";
        chart.ValueAxisTitle = "Value";
        chart.QuickLayoutId = 1;

        var plan = ChartSmartArtVisualPlanner.BuildChartPlan(chart);

        plan.ShowTitle.Should().BeFalse();
        plan.ShowLegend.Should().BeFalse();
        plan.ShowDataLabels.Should().BeFalse();
        plan.CategoryAxisTitle.Should().BeNull();
        plan.ValueAxisTitle.Should().BeNull();
    }

    [Fact]
    public void ChartPlan_MultiSeriesLegendFollowsExplicitModelFlag()
    {
        var chart = Chart.Create(ChartKind.Column, ["A", "B"], [1.0, 2.0], seriesName: "Series 1");
        chart.Series.Add(new ChartSeries("Series 2", [3.0, 4.0]));

        ChartSmartArtVisualPlanner.BuildChartPlan(chart).ShowLegend.Should().BeFalse();

        chart.ShowLegend = true;

        ChartSmartArtVisualPlanner.BuildChartPlan(chart).ShowLegend.Should().BeTrue();
    }

    [Fact]
    public void ChartDataSignatures_CaptureCategorySeriesAndValueShape()
    {
        var chart = Chart.Create(ChartKind.Line, ["Q1", "Q2"], [1.25, 2.5], seriesName: "Actual");
        chart.Series.Add(new ChartSeries("Forecast", [1.5, 2.75]));

        var signatures = ChartSmartArtVisualPlanner.BuildChartDataSignatures(
            [ChartSmartArtVisualPlanner.BuildChartPlan(chart)]);

        signatures.Should().ContainSingle()
            .Which.Should().Be("kind=Line|categories=2|categoryLabels=Q1,Q2|series=2|points=4|seriesData=0:Actual=1.25,2.5;1:Forecast=1.5,2.75");
    }

    [Fact]
    public void ChartElementCommandState_ReportsLegendVisibilityAndToggleAvailability()
    {
        var chart = Chart.Create(ChartKind.Column, ["A"], [1.0], seriesName: "Series 1");
        chart.ShowLegend = true;

        var state = ChartSmartArtVisualPlanner.BuildChartElementCommandState(chart);

        state.CanToggleLegend.Should().BeTrue();
        state.IsLegendVisible.Should().BeTrue();
        state.CanEditAxisTitles.Should().BeTrue();
        state.HasChartTitle.Should().BeFalse();
        state.HasAxisTitles.Should().BeFalse();
    }

    [Fact]
    public void ChartPlan_ScatterUsesMarkerOnlyGeometry()
    {
        var chart = Chart.Create(ChartKind.Scatter, ["155", "160"], [52.0, 58.0]);

        var plan = ChartSmartArtVisualPlanner.BuildChartPlan(chart);

        plan.GeometryKind.Should().Be(ChartVisualGeometryKind.MarkerOnly);
        plan.ShowMarkers.Should().BeTrue();
        ChartSmartArtVisualPlanner.BuildChartVisualSignature(plan)
            .Should().Contain("geometry=MarkerOnly|style=1|colorScheme=colorful1|quickLayout=0");
    }

    [Fact]
    public void ChartPlan_PieFamilySuppressesAxisTitles()
    {
        var chart = Chart.Create(ChartKind.Pie, ["A", "B"], [1.0, 2.0]);
        chart.CategoryAxisTitle = "Category";
        chart.ValueAxisTitle = "Value";

        var plan = ChartSmartArtVisualPlanner.BuildChartPlan(chart);

        plan.GeometryKind.Should().Be(ChartVisualGeometryKind.Pie);
        plan.ShowAxisTitles.Should().BeFalse();
        plan.CategoryAxisTitle.Should().BeNull();
        plan.ValueAxisTitle.Should().BeNull();
    }

    [Fact]
    public void SmartArtPlan_ResolvesLayoutColorStyleAndNodeFillSequence()
    {
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["Plan", "Build", "Verify"]);
        smartArt.LayoutId = "stepup1";
        smartArt.ColorSchemeId = "accent1";
        smartArt.StyleId = "intense1";

        var plan = ChartSmartArtVisualPlanner.BuildSmartArtPlan(smartArt);

        plan.LayoutId.Should().Be("stepup1");
        plan.Kind.Should().Be(SmartArtKind.Process);
        plan.Layout.Kind.Should().Be(SmartArtKind.Process);
        plan.ColorScheme.Id.Should().Be("accent1");
        plan.Style.Id.Should().Be("intense1");
        plan.Nodes.Select(n => n.FillHex).Should().ContainInOrder("#38517D", "#486DAF", "#679AD6");

        var signature = ChartSmartArtVisualPlanner.BuildSmartArtVisualSignature(plan);
        signature.Should().Contain("layout=stepup1");
        signature.Should().Contain("colorScheme=accent1");
        signature.Should().Contain("style=intense1");
        signature.Should().Contain("#38517D");
        signature.Should().Contain("#486DAF");
        signature.Should().Contain("#679AD6");
    }

    [Theory]
    [InlineData("subtle2", "#4E81BD", "#20538F", 0.5, 4, 0.15, 5.2, 1.8)]
    [InlineData("intense1", "#679AD6", "#396CA8", 1.5, 0, 0.30, 6.4, 2.1)]
    [InlineData("3d1", "#81B4F0", "#5386C2", 1.0, 8, 0.40, 7.2, 2.3)]
    public void SmartArtPlan_ProjectsStyleAndEffectValuesIntoNodePlans(
        string styleId,
        string expectedFill,
        string expectedBorder,
        double expectedBorderThickness,
        double expectedCornerRadius,
        double expectedShadowOpacity,
        double expectedShadowBlur,
        double expectedShadowDepth)
    {
        var smartArt = SmartArt.Create(SmartArtKind.Process, ["Plan", "Build"]);
        smartArt.StyleId = styleId;

        var node = ChartSmartArtVisualPlanner.BuildSmartArtPlan(smartArt).Nodes[0];

        node.FillHex.Should().Be(expectedFill);
        node.BorderHex.Should().Be(expectedBorder);
        node.BorderThickness.Should().Be(expectedBorderThickness);
        node.CornerRadius.Should().Be(expectedCornerRadius);
        node.ShadowOpacity.Should().Be(expectedShadowOpacity);
        node.ShadowBlur.Should().BeApproximately(expectedShadowBlur, 0.01);
        node.ShadowDepth.Should().BeApproximately(expectedShadowDepth, 0.01);
        node.ConnectorHex.Should().NotBe(node.FillHex);
    }

    [Fact]
    public void SmartArtPlan_DefaultsLayoutFromKindAndFlattensHierarchy()
    {
        var root = new SmartArtNode("CEO");
        root.Children.Add(new SmartArtNode("Ops"));
        root.Children.Add(new SmartArtNode("Sales"));
        var smartArt = new SmartArt { Kind = SmartArtKind.Hierarchy };
        smartArt.Nodes.Add(root);

        var plan = ChartSmartArtVisualPlanner.BuildSmartArtPlan(smartArt);

        plan.LayoutId.Should().Be("hierarchy1");
        plan.Nodes.Select(n => n.Text).Should().ContainInOrder("CEO", "Ops", "Sales");
        plan.Nodes.Select(n => n.Depth).Should().ContainInOrder(0, 1, 1);
    }

    [Fact]
    public void SmartArtPlan_HierarchyLayoutProvidesReusableGeometry()
    {
        var root = new SmartArtNode("CEO");
        var ops = root.AddChild("Ops");
        ops.AddChild("Lead");
        root.AddChild("Sales");
        var smartArt = new SmartArt { Kind = SmartArtKind.Hierarchy };
        smartArt.LayoutId = "orgchart1";
        smartArt.Nodes.Add(root);
        smartArt.Nodes.Add(new SmartArtNode("Advisor"));

        var plan = ChartSmartArtVisualPlanner.BuildSmartArtPlan(smartArt);

        plan.HierarchyGeometry.Should().NotBeNull();
        var geometry = plan.HierarchyGeometry!;
        plan.Nodes.Select(n => n.Text).Should().ContainInOrder("CEO", "Ops", "Lead", "Sales", "Advisor");
        geometry.MaxDepth.Should().Be(2);
        geometry.Nodes.Select(n => n.NodeIndex).Should().ContainInOrder(0, 1, 2, 3, 4);
        geometry.Nodes.Select(n => n.ParentNodeIndex).Should().ContainInOrder(null, 0, 1, 0, null);
        geometry.Nodes.Select(n => n.Depth).Should().ContainInOrder(0, 1, 2, 1, 0);
        geometry.Connectors.Select(c => (c.ParentNodeIndex, c.ChildNodeIndex))
            .Should().BeEquivalentTo([(0, 1), (1, 2), (0, 3)]);
        geometry.Nodes[1].Y.Should().BeGreaterThan(geometry.Nodes[0].Y);
        geometry.Nodes[2].Y.Should().BeGreaterThan(geometry.Nodes[1].Y);
        geometry.NaturalWidth.Should().BeGreaterThan(0);
        geometry.NaturalHeight.Should().BeGreaterThan(0);

        var signature = ChartSmartArtVisualPlanner.BuildSmartArtVisualSignature(plan);
        signature.Should().Contain("hierarchy=maxDepth=2/nodes=5/connectors=3");
        signature.Should().Contain("boxes=0:root:0");
        signature.Should().Contain("2:1:2");
        signature.Should().Contain("lines=");
    }

    [Theory]
    [InlineData("list1", "BasicList", 4, 0, 128, 154)]
    [InlineData("vertbullet1", "VerticalBulletList", 4, 0, 128, 154)]
    [InlineData("process1", "BasicProcess", 4, 3, 344, 46)]
    [InlineData("cycle1", "Cycle", 4, 4, 200, 160)]
    [InlineData("pyramid1", "Pyramid", 4, 0, 176, 148)]
    [InlineData("radial1", "Radial", 4, 3, 220, 180)]
    [InlineData("matrix1", "Matrix", 4, 0, 182, 94)]
    [InlineData("horizbullet1", "HorizontalList", 4, 0, 320, 46)]
    [InlineData("continuousBlockProcess", "ContinuousBlockProcess", 4, 3, 332, 50)]
    [InlineData("stepup1", "StepUp", 4, 3, 266, 130)]
    [InlineData("stepdown1", "StepDown", 4, 3, 266, 130)]
    public void SmartArtPlan_ProvidesReusableLayoutGeometryForBreadthLayouts(
        string layoutId,
        string expectedKind,
        int expectedNodes,
        int expectedConnectors,
        double expectedWidth,
        double expectedHeight)
    {
        var smartArt = SmartArt.Create(SmartArtKind.List, ["Alpha", "Beta", "Gamma", "Delta"]);
        smartArt.LayoutId = layoutId;

        var plan = ChartSmartArtVisualPlanner.BuildSmartArtPlan(smartArt);

        plan.LayoutGeometry.Should().NotBeNull();
        var geometry = plan.LayoutGeometry!;
        geometry.Kind.ToString().Should().Be(expectedKind);
        geometry.Nodes.Should().HaveCount(expectedNodes);
        geometry.Connectors.Should().HaveCount(expectedConnectors);
        geometry.NaturalWidth.Should().BeApproximately(expectedWidth, 0.01);
        geometry.NaturalHeight.Should().BeApproximately(expectedHeight, 0.01);

        var signature = ChartSmartArtVisualPlanner.BuildSmartArtVisualSignature(plan);
        signature.Should().Contain(
            $"geometry=kind={expectedKind}/nodes={expectedNodes}/connectors={expectedConnectors}/size={expectedWidth}x{expectedHeight}");
    }

    [Fact]
    public void SmartArtPlan_LayoutGeometryUsesStableNodePlacements()
    {
        var cycle = SmartArt.Create(SmartArtKind.List, ["North", "East", "South", "West"]);
        cycle.LayoutId = "cycle1";
        var cycleGeometry = ChartSmartArtVisualPlanner.BuildSmartArtPlan(cycle).LayoutGeometry!;
        cycleGeometry.Nodes[0].X.Should().BeApproximately(74, 0.01);
        cycleGeometry.Nodes[0].Y.Should().BeApproximately(11, 0.01);
        cycleGeometry.Connectors.Select(c => (c.SourceNodeIndex, c.TargetNodeIndex))
            .Should().ContainInOrder((0, 1), (1, 2), (2, 3), (3, 0));

        var radial = SmartArt.Create(SmartArtKind.List, ["Hub", "North", "East", "West"]);
        radial.LayoutId = "radial1";
        var radialGeometry = ChartSmartArtVisualPlanner.BuildSmartArtPlan(radial).LayoutGeometry!;
        radialGeometry.Nodes[0].X.Should().BeApproximately(82, 0.01);
        radialGeometry.Nodes[0].Y.Should().BeApproximately(72, 0.01);
        radialGeometry.Nodes[1].X.Should().BeApproximately(86, 0.01);
        radialGeometry.Nodes[1].Y.Should().BeApproximately(20, 0.01);
        radialGeometry.Connectors.Select(c => (c.SourceNodeIndex, c.TargetNodeIndex))
            .Should().ContainInOrder((0, 1), (0, 2), (0, 3));

        var pyramid = SmartArt.Create(SmartArtKind.List, ["Top", "Middle", "Lower", "Base"]);
        pyramid.LayoutId = "pyramid1";
        var pyramidPlan = ChartSmartArtVisualPlanner.BuildSmartArtPlan(pyramid);
        var pyramidGeometry = pyramidPlan.LayoutGeometry!;
        pyramidGeometry.Kind.Should().Be(SmartArtLayoutGeometryKind.Pyramid);
        pyramidGeometry.Nodes[0].X.Should().BeApproximately(61, 0.01);
        pyramidGeometry.Nodes[0].Width.Should().BeApproximately(54, 0.01);
        pyramidGeometry.Nodes[3].X.Should().BeApproximately(8, 0.01);
        pyramidGeometry.Nodes[3].Width.Should().BeApproximately(160, 0.01);
        pyramidGeometry.Nodes.Select(n => n.Y).Should().ContainInOrder(8, 42, 76, 110);
        pyramidGeometry.Nodes.Should().OnlyContain(n => n.HasPolygon);
        pyramidGeometry.Nodes[0].PolygonPoints.Select(p => (p.X, p.Y)).Should().ContainInOrder(
            (61, 8),
            (115, 8),
            (128.25, 38),
            (47.75, 38));
        pyramidGeometry.Nodes[3].PolygonPoints.Select(p => (p.X, p.Y)).Should().ContainInOrder(
            (21.25, 110),
            (154.75, 110),
            (168, 140),
            (8, 140));
        pyramidGeometry.Connectors.Should().BeEmpty();
        ChartSmartArtVisualPlanner.BuildSmartArtVisualSignature(pyramidPlan)
            .Should().Contain("polygons=0=61:8;115:8;128.25:38;47.75:38");

        var matrix = SmartArt.Create(SmartArtKind.List, ["A", "B", "C", "D"]);
        matrix.LayoutId = "matrix1";
        var matrixGeometry = ChartSmartArtVisualPlanner.BuildSmartArtPlan(matrix).LayoutGeometry!;
        matrixGeometry.Nodes[2].X.Should().BeApproximately(8, 0.01);
        matrixGeometry.Nodes[2].Y.Should().BeApproximately(52, 0.01);

        var list = SmartArt.Create(SmartArtKind.List, ["One", "Two", "Three"]);
        list.LayoutId = "list1";
        var listGeometry = ChartSmartArtVisualPlanner.BuildSmartArtPlan(list).LayoutGeometry!;
        listGeometry.Kind.Should().Be(SmartArtLayoutGeometryKind.BasicList);
        listGeometry.Nodes.Select(n => n.X).Should().OnlyContain(x => Math.Abs(x - 8) < 0.01);
        listGeometry.Nodes[1].Y.Should().BeApproximately(44, 0.01);
        listGeometry.Connectors.Should().BeEmpty();

        var verticalBullet = SmartArt.Create(SmartArtKind.List, ["One", "Two", "Three"]);
        verticalBullet.LayoutId = "vertbullet1";
        var verticalBulletGeometry = ChartSmartArtVisualPlanner.BuildSmartArtPlan(verticalBullet).LayoutGeometry!;
        verticalBulletGeometry.Kind.Should().Be(SmartArtLayoutGeometryKind.VerticalBulletList);
        verticalBulletGeometry.Nodes.Select(n => n.X).Should().OnlyContain(x => Math.Abs(x - 8) < 0.01);
        verticalBulletGeometry.Nodes[2].Y.Should().BeApproximately(80, 0.01);
        verticalBulletGeometry.Connectors.Should().BeEmpty();

        var process = SmartArt.Create(SmartArtKind.Process, ["Plan", "Build", "Verify"]);
        process.LayoutId = "process1";
        var processGeometry = ChartSmartArtVisualPlanner.BuildSmartArtPlan(process).LayoutGeometry!;
        processGeometry.Kind.Should().Be(SmartArtLayoutGeometryKind.BasicProcess);
        processGeometry.Nodes.Select(n => n.Y).Should().OnlyContain(y => Math.Abs(y - 8) < 0.01);
        processGeometry.Nodes[1].X.Should().BeApproximately(94, 0.01);
        processGeometry.Connectors.Select(c => (c.SourceNodeIndex, c.TargetNodeIndex, c.Kind))
            .Should().ContainInOrder(
                (0, 1, SmartArtLayoutConnectorKind.Arrow),
                (1, 2, SmartArtLayoutConnectorKind.Arrow));

        var continuous = SmartArt.Create(SmartArtKind.Process, ["Plan", "Build", "Verify"]);
        continuous.LayoutId = "continuousBlockProcess";
        var continuousGeometry = ChartSmartArtVisualPlanner.BuildSmartArtPlan(continuous).LayoutGeometry!;
        continuousGeometry.Kind.Should().Be(SmartArtLayoutGeometryKind.ContinuousBlockProcess);
        continuousGeometry.Nodes.Select(n => n.Y).Should().OnlyContain(y => Math.Abs(y - 8) < 0.01);
        continuousGeometry.Nodes[1].X.Should().BeApproximately(88, 0.01);
        continuousGeometry.Connectors.Select(c => (c.SourceNodeIndex, c.TargetNodeIndex, c.Kind))
            .Should().ContainInOrder(
                (0, 1, SmartArtLayoutConnectorKind.Arrow),
                (1, 2, SmartArtLayoutConnectorKind.Arrow));
    }

    [Fact]
    public void SmartArtPlan_ResolvedLayoutKindOverridesStaleModelKind()
    {
        var root = new SmartArtNode("Root");
        var child = root.AddChild("Child");
        child.AddChild("Grandchild");
        var smartArt = new SmartArt { Kind = SmartArtKind.Process };
        smartArt.LayoutId = "orgchart1";
        smartArt.Nodes.Add(root);

        var plan = ChartSmartArtVisualPlanner.BuildSmartArtPlan(smartArt);

        plan.Kind.Should().Be(SmartArtKind.Hierarchy);
        plan.Layout.Kind.Should().Be(SmartArtKind.Hierarchy);
        plan.HierarchyGeometry.Should().NotBeNull();
        plan.HierarchyGeometry!.MaxDepth.Should().Be(2);
        plan.HierarchyGeometry.Connectors.Count.Should().Be(2);
        ChartSmartArtVisualPlanner.BuildSmartArtVisualSignature(plan)
            .Should().Contain("kind=Hierarchy|layout=orgchart1");
    }
}
