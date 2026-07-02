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
        plan.Nodes.Select(n => n.FillHex).Should().ContainInOrder("#1F3864", "#2F5496", "#4E81BD");
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
