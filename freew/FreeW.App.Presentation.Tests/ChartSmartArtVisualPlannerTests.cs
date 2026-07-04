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

        plan.PaletteHex[0].Should().Be("#214A82");
        plan.ShowTitle.Should().BeTrue();
        plan.ShowLegend.Should().BeTrue();
        plan.ShowGridlines.Should().BeTrue();
        plan.PlotAreaFill.Should().BeTrue();
        plan.ShowMarkers.Should().BeTrue();
        plan.ShowDataLabels.Should().BeTrue();
        plan.CategoryAxisTitle.Should().Be("Quarter");
        plan.ValueAxisTitle.Should().Be("USD");
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
        plan.Layout.Kind.Should().Be(SmartArtKind.Process);
        plan.ColorScheme.Id.Should().Be("accent1");
        plan.Style.Id.Should().Be("intense1");
        plan.Nodes.Select(n => n.FillHex).Should().ContainInOrder("#38517D", "#486DAF", "#679AD6");
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
}
