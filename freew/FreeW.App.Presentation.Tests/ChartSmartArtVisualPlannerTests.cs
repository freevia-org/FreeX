using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests;

public sealed class ChartSmartArtVisualPlannerTests
{
    [Fact]
    public void SceneSlice_ProjectsSharedArcEndpointsAndFlags()
    {
        var slice = new ChartSceneSlice(
            100, 80, 40, 20, 0, Math.PI * 1.5, "#FFFFFF", "#000000");

        slice.OuterStart.Should().Be(new ChartScenePoint(140, 80));
        slice.OuterEnd.X.Should().BeApproximately(100, 0.0001);
        slice.OuterEnd.Y.Should().BeApproximately(40, 0.0001);
        slice.InnerStart.Should().Be(new ChartScenePoint(120, 80));
        slice.HasInnerRadius.Should().BeTrue();
        slice.IsLargeArc.Should().BeTrue();
    }

    [Fact]
    public void ChartScene_ColumnGolden_CentralizesFrameAxesBarsLabelsAndLegend()
    {
        var chart = Chart.Create(ChartKind.Column, ["Q1", "Q2"], [1.0, 2.0], "Actual");
        chart.Series.Add(new ChartSeries("Forecast", [2.0, 3.0]));
        chart.Title = "Revenue";
        chart.ShowLegend = true;
        chart.QuickLayoutId = 5;

        var scene = ChartSmartArtVisualPlanner.BuildChartScene(chart, 240, 180);

        scene.FrameBounds.Should().Be(new ChartSceneRect(0, 0, 240, 180));
        scene.PlotBounds.Should().Be(new ChartSceneRect(32, 54, 200, 86));
        scene.Bars.Should().HaveCount(4);
        scene.GridLines.Should().HaveCount(3);
        scene.AxisLines.Should().ContainSingle();
        scene.Legend.Should().HaveCount(2);
        scene.Texts.Count(text => text.Kind == ChartSceneTextKind.ValueAxis).Should().Be(4);
        scene.Texts.Count(text => text.Kind == ChartSceneTextKind.CategoryAxis).Should().Be(2);
        scene.Texts.Count(text => text.Kind == ChartSceneTextKind.DataLabel).Should().Be(4);
    }

    [Fact]
    public void ChartScene_HorizontalBarValueAxisLabels_FollowVerticalGridlines()
    {
        var chart = Chart.Create(ChartKind.Bar, ["A", "B"], [1.0, 2.0]);

        var scene = ChartSmartArtVisualPlanner.BuildChartScene(chart, 240, 180);
        var labels = scene.Texts.Where(text => text.Kind == ChartSceneTextKind.ValueAxis).ToList();
        var gridlines = scene.GridLines.ToList();

        gridlines.Should().HaveCount(4);
        labels.Should().HaveCount(5);
        labels.Should().OnlyContain(label => label.Anchor == ChartSceneTextAnchor.TopCenter);
        labels.Select(label => label.X).Should().Equal(
            new[] { scene.PlotBounds.X }
                .Concat(gridlines.Select(line => line.X1)));
        labels.Should().OnlyContain(label => label.Y == scene.PlotBounds.Bottom + 2);
        gridlines.Should().OnlyContain(line => line.X1 == line.X2);
    }

    [Fact]
    public void ChartScene_DataLabels_PreserveFourSignificantDigits()
    {
        var chart = Chart.Create(ChartKind.Column, ["A"], [1.234]);
        chart.QuickLayoutId = 5;

        var scene = ChartSmartArtVisualPlanner.BuildChartScene(chart, 240, 180);

        scene.Texts.Single(text => text.Kind == ChartSceneTextKind.DataLabel).Text.Should().Be("1.234");
    }

    [Fact]
    public void ChartScene_Categories_PreserveRenderedText()
    {
        var chart = Chart.Create(ChartKind.Column, ["  North | East: Q1; retail  "], [1.0]);

        var scene = ChartSmartArtVisualPlanner.BuildChartScene(chart, 240, 180);

        scene.Categories.Should().ContainSingle()
            .Which.Should().Be("  North | East: Q1; retail  ");
        scene.Texts.Should().Contain(text =>
            text.Kind == ChartSceneTextKind.CategoryAxis &&
            text.Text == "  North | East: Q1; retail  ");
    }

    [Fact]
    public void ChartScene_LineAreaAndScatter_UseSharedPointAndMarkerPrimitives()
    {
        var area = Chart.Create(ChartKind.Area, ["A", "B", "C"], [1.0, 3.0, 2.0]);
        area.StyleId = 4;
        var areaScene = ChartSmartArtVisualPlanner.BuildChartScene(area, 300, 200);

        areaScene.LineSeries.Should().ContainSingle().Which.FillArea.Should().BeTrue();
        areaScene.LineSeries[0].Points.Should().HaveCount(3);
        areaScene.Markers.Should().HaveCount(3);

        var scatter = Chart.Create(ChartKind.Scatter, ["155", "160", "165", "170"], [52.0, 58.0, 63.0, 68.0]);
        var scatterScene = ChartSmartArtVisualPlanner.BuildChartScene(scatter, 300, 200);

        scatterScene.LineSeries.Should().BeEmpty();
        scatterScene.Markers.Select(marker => marker.Kind).Should().ContainInOrder(
            ChartSceneMarkerKind.Diamond,
            ChartSceneMarkerKind.Square,
            ChartSceneMarkerKind.Triangle,
            ChartSceneMarkerKind.Cross);
    }

    [Fact]
    public void ChartScene_NativeVisualSettings_OverrideSyntheticStyleAndPreserveScatterLine()
    {
        var column = Chart.Create(ChartKind.Column, ["Q1", "Q2"], [1.0, 2.0]);
        column.StyleId = 7;
        column.QuickLayoutId = 9;
        column.NativeVisualSettings = new ChartNativeVisualSettings(
            ShowGridlines: false,
            HasPlotAreaFill: false,
            ShowDataLabels: false,
            ScatterConnectsPoints: false);

        var columnScene = ChartSmartArtVisualPlanner.BuildChartScene(column, 240, 180);
        columnScene.PlotFillHex.Should().BeNull();
        columnScene.GridLines.Should().BeEmpty();
        columnScene.Texts.Should().NotContain(text => text.Kind == ChartSceneTextKind.DataLabel);

        var scatter = Chart.Create(ChartKind.Scatter, ["155", "160", "165"], [52.0, 58.0, 63.0]);
        scatter.NativeVisualSettings = new ChartNativeVisualSettings(false, false, false, true);
        ChartSmartArtVisualPlanner.BuildChartScene(scatter, 300, 200).LineSeries.Should().ContainSingle()
            .Which.Points.Should().HaveCount(3);
    }

    [Fact]
    public void ChartScene_PieAndDoughnut_UseSharedSlicesAndCategoryLegend()
    {
        var chart = Chart.Create(ChartKind.Doughnut, ["A", "B", "C"], [1.0, 2.0, 3.0]);
        chart.ShowLegend = true;
        chart.QuickLayoutId = 5;

        var scene = ChartSmartArtVisualPlanner.BuildChartScene(chart, 240, 180);

        scene.Slices.Should().HaveCount(3);
        scene.Slices.Should().OnlyContain(slice => slice.InnerRadius > 0);
        scene.Legend.Select(entry => entry.Text).Should().ContainInOrder("A", "B", "C");
        scene.GridLines.Should().BeEmpty();
        scene.AxisLines.Should().BeEmpty();
    }

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
    public void ChartPlan_ImportedNativeColumnStyle_UsesWordOfficeThemePalette()
    {
        var column = Chart.Create(ChartKind.Column, ["Q1", "Q2"], [1.0, 2.0]);
        column.StyleId = 7;
        column.ColorSchemeId = "mono-blue";
        column.NativeVisualSettings = new ChartNativeVisualSettings(false, false, false, false);
        ChartSmartArtVisualPlanner.BuildChartPlan(column).PaletteHex
            .Should().Equal("#4679A7", "#5591C7", "#84AEDC", "#B8CDE8");
    }

    [Fact]
    public void ChartPlan_ImportedQuickLayoutColumn_PreservesSerializedMonoBluePalette()
    {
        var column = Chart.Create(ChartKind.Column, ["Q1", "Q2"], [1.0, 2.0]);
        column.StyleId = 7;
        column.QuickLayoutId = 9;
        column.ColorSchemeId = "mono-blue";
        column.NativeVisualSettings = new ChartNativeVisualSettings(false, false, false, false);

        ChartSmartArtVisualPlanner.BuildChartPlan(column).PaletteHex
            .Should().StartWith("#214A82", "#2E5FAA", "#4472C4", "#6C8FD1");
    }

    [Fact]
    public void ChartPlan_ImportedNativeScatterStyle_UsesWordBlueGrayPointPalette()
    {
        var scatter = Chart.Create(ChartKind.Scatter, ["155", "160", "165", "170"], [52.0, 58.0, 62.0, 66.0]);
        scatter.StyleId = 4;
        scatter.ColorSchemeId = "colorful1";
        scatter.NativeVisualSettings = new ChartNativeVisualSettings(false, false, false, true);

        ChartSmartArtVisualPlanner.BuildChartPlan(scatter).PaletteHex
            .Should().Equal("#234075", "#2B4E8C", "#7180AA", "#B0B7CB");
    }

    [Fact]
    public void ChartScene_ImportedNativeChartStyles_UseDarkAxisStrokes()
    {
        var column = Chart.Create(ChartKind.Column, ["Q1", "Q2"], [1.0, 2.0]);
        column.StyleId = 7;
        column.ColorSchemeId = "mono-blue";
        column.NativeVisualSettings = new ChartNativeVisualSettings(false, false, false, false);

        ChartSmartArtVisualPlanner.BuildChartScene(column, 240, 180).AxisLines
            .Should().OnlyContain(line => line.StrokeHex == "#000000");

        var scatter = Chart.Create(ChartKind.Scatter, ["155", "160"], [52.0, 58.0]);
        scatter.StyleId = 4;
        scatter.ColorSchemeId = "colorful1";
        scatter.NativeVisualSettings = new ChartNativeVisualSettings(false, false, false, true);

        ChartSmartArtVisualPlanner.BuildChartScene(scatter, 240, 180).AxisLines
            .Should().OnlyContain(line => line.StrokeHex == "#000000");
    }

    [Fact]
    public void ChartScene_ImportedNativeColumnStyle_UsesCompactCenteredCategoryLegend()
    {
        var chart = Chart.Create(ChartKind.Column, ["Q1", "Q2", "Q3", "Q4"], [1.4, 1.8, 1.6, 2.2]);
        chart.StyleId = 7;
        chart.ColorSchemeId = "mono-blue";
        chart.QuickLayoutId = 9;
        chart.ShowLegend = true;
        chart.CategoryAxisTitle = "Quarter";
        chart.ValueAxisTitle = "USD";
        chart.NativeVisualSettings = new ChartNativeVisualSettings(true, false, false, false);

        var legend = ChartSmartArtVisualPlanner.BuildChartScene(chart, 400, 224).Legend;
        legend.Select(entry => entry.SwatchX).Should().Equal(136, 171, 206, 241);
        legend.Select(entry => entry.SwatchY).Should().AllSatisfy(y => y.Should().Be(199));
        legend.Select(entry => entry.SwatchSize).Should().AllSatisfy(size => size.Should().Be(8));
    }

    [Fact]
    public void ColumnScene_UsesWordSizedCategoryBarSlots()
    {
        var chart = Chart.Create(ChartKind.Column, ["Q1", "Q2", "Q3", "Q4"], [1.0, 2.0, 1.5, 2.5]);

        var scene = ChartSmartArtVisualPlanner.BuildChartScene(chart, 400, 224);

        scene.Bars.Should().HaveCount(4);
        var groupWidth = scene.PlotBounds.Width / 4;
        scene.Bars.Select(bar => bar.Bounds.Width)
            .Should().AllSatisfy(width => width.Should().BeApproximately(groupWidth * 0.4 - 1, 1.0));
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
    public void ChartPlan_EveryQuickLayoutMatchesTheSharedCatalogFlags()
    {
        foreach (var layout in ChartQuickLayout.Catalog)
        {
            var chart = Chart.Create(ChartKind.Column, ["A", "B"], [1.0, 2.0], title: "Revenue");
            chart.ShowLegend = !layout.ShowLegend;
            chart.CategoryAxisTitle = "Category";
            chart.ValueAxisTitle = "Value";
            chart.QuickLayoutId = layout.Id;

            var plan = ChartSmartArtVisualPlanner.BuildChartPlan(chart);

            plan.QuickLayoutId.Should().Be(layout.Id);
            plan.ShowTitle.Should().Be(layout.ShowTitle);
            plan.ShowLegend.Should().Be(layout.ShowLegend);
            plan.ShowDataLabels.Should().Be(layout.ShowDataLabels);
            plan.ShowGridlines.Should().Be(layout.ShowGridlines);
            (plan.CategoryAxisTitle is not null).Should().Be(layout.ShowAxisTitles);
            (plan.ValueAxisTitle is not null).Should().Be(layout.ShowAxisTitles);
        }
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
    public void ChartScene_SingleSeriesWordStyleUsesCategoryLegend()
    {
        var chart = Chart.Create(ChartKind.Column, ["Q1", "Q2", "Q3", "Q4"],
            [1.4, 1.8, 1.6, 2.2], seriesName: "Revenue");
        chart.StyleId = 7;
        chart.QuickLayoutId = 9;
        chart.ShowLegend = true;

        var scene = ChartSmartArtVisualPlanner.BuildChartScene(chart, 300, 168);

        scene.Legend.Select(entry => entry.Text).Should().ContainInOrder("Q1", "Q2", "Q3", "Q4");
    }

    [Fact]
    public void ChartScene_ImportedDefaultQuarterlyRevenueUsesMeasuredWordLegend()
    {
        var chart = Chart.Create(ChartKind.Column, ["Q1", "Q2", "Q3", "Q4"],
            [1.2, 1.7, 1.4, 2.1], seriesName: "Revenue", title: "Quarterly revenue");
        chart.WidthPt = 210;
        chart.HeightPt = 126;
        chart.ShowLegend = true;
        chart.CategoryAxisTitle = "Quarter";
        chart.ValueAxisTitle = "USD";

        var scene = ChartSmartArtVisualPlanner.BuildChartScene(chart, 280, 168);

        scene.PaletteHex.Should().Equal("#000000", "#2F5496", "#1F3864", "#FFC000");
        scene.Legend.Should().BeEquivalentTo(
        [
            new ChartSceneLegendEntry("Q1", 76, 144, 9, 82, 144),
            new ChartSceneLegendEntry("Q2", 111, 144, 9, 117, 144),
            new ChartSceneLegendEntry("Q3", 146, 144, 9, 152, 144),
            new ChartSceneLegendEntry("Q4", 181, 144, 9, 187, 144)
        ]);
    }

    [Fact]
    public void ChartScene_WordAxisTitleLayoutReservesCompactPlotBand()
    {
        var chart = Chart.Create(ChartKind.Column, ["Q1", "Q2", "Q3", "Q4"],
            [1.4, 1.8, 1.6, 2.2], seriesName: "Revenue", title: "Revenue by quarter");
        chart.StyleId = 7;
        chart.QuickLayoutId = 9;
        chart.ShowLegend = true;
        chart.CategoryAxisTitle = "Quarter";
        chart.ValueAxisTitle = "USD";

        var scene = ChartSmartArtVisualPlanner.BuildChartScene(chart, 400, 224);

        scene.PlotBounds.Should().Be(new ChartSceneRect(68, 54, 316, 64));
        scene.Texts.Single(text => text.Kind == ChartSceneTextKind.AxisTitle && text.Text == "Quarter")
            .Y.Should().Be(145);
        scene.Legend.Should().OnlyContain(entry => entry.SwatchY == 201 && entry.TextY == 201);
    }

    [Fact]
    public void ChartScene_ScatterUsesWordPaddedNumericCategoryAxis()
    {
        var chart = Chart.Create(ChartKind.Scatter, ["155", "160", "165", "170"],
            [52, 58, 62, 66], seriesName: "Sample", title: "Height and weight");
        chart.CategoryAxisTitle = "Height";
        chart.ValueAxisTitle = "Weight";

        var scene = ChartSmartArtVisualPlanner.BuildChartScene(chart, 360, 200);

        scene.PlotBounds.Should().Be(new ChartSceneRect(68, 54, 267, 72));
        scene.Texts.Where(text => text.Kind == ChartSceneTextKind.CategoryAxis)
            .Select(text => text.Text)
            .Should().ContainInOrder("150", "155", "160", "165", "170", "175");
        scene.AxisLines.Should().Contain(line => line.X1 == scene.PlotBounds.X && line.X2 == scene.PlotBounds.X);
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
    public void ChartPlan_SharesSignedValueAxisGeometryForMixedNegativeSeries()
    {
        var chart = Chart.Create(ChartKind.Column, ["A", "B", "C"], [-4.0, 6.0, -2.0]);

        var axis = ChartSmartArtVisualPlanner.BuildChartPlan(chart).ValueAxis;

        axis.Minimum.Should().Be(-5);
        axis.Maximum.Should().Be(10);
        axis.Range.Should().Be(15);
        axis.ZeroFraction.Should().BeApproximately(1.0 / 3.0, 0.001);
        axis.ValueFraction(-5).Should().Be(0);
        axis.ValueFraction(10).Should().Be(1);
    }

    [Fact]
    public void ChartPlan_UsesWordFriendlyPositiveMajorUnits()
    {
        var chart = Chart.Create(ChartKind.Column, ["Q1", "Q2", "Q3", "Q4"], [1.4, 1.8, 1.6, 2.2]);

        var axis = ChartSmartArtVisualPlanner.BuildChartPlan(chart).ValueAxis;

        axis.Minimum.Should().Be(0);
        axis.Maximum.Should().Be(3);
        axis.Range.Should().Be(3);
        axis.MajorUnit.Should().Be(1);
    }

    [Fact]
    public void ChartPlan_AllNegativeValueAxisStillIncludesZero()
    {
        var chart = Chart.Create(ChartKind.Line, ["A", "B"], [-8.0, -2.0]);

        var axis = ChartSmartArtVisualPlanner.BuildChartPlan(chart).ValueAxis;

        axis.Minimum.Should().Be(-8);
        axis.Maximum.Should().Be(0);
        axis.ZeroFraction.Should().Be(1);
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

    [Fact]
    public void SmartArtPlan_NativeOrgChartChainMatchesWordGeometryAndStyle()
    {
        var root = new SmartArtNode("Plan");
        var build = root.AddChild("Build");
        build.AddChild("Verify");
        var smartArt = new SmartArt { Kind = SmartArtKind.Hierarchy };
        smartArt.LayoutId = "orgchart1";
        smartArt.ColorSchemeId = "accent1";
        smartArt.StyleId = "intense1";
        smartArt.Nodes.Add(root);

        var plan = ChartSmartArtVisualPlanner.BuildSmartArtPlan(smartArt);

        plan.Nodes.Select(node => node.FillHex).Should().ContainInOrder("#1F3864", "#1F3864", "#1F3864");
        plan.Nodes.Select(node => node.BorderHex).Should().OnlyContain(hex => hex == "#1F3864");
        plan.Nodes.Select(node => node.ConnectorHex).Should().OnlyContain(hex => hex == "#1F3864");
        plan.Nodes.Should().OnlyContain(node => node.ShadowOpacity == 0);
        plan.Nodes.Select(node => node.FontSizeDip)
            .Should().OnlyContain(size => Math.Abs(size - 11 * 96.0 / 72.0) < 0.001);

        plan.HierarchyGeometry.Should().NotBeNull();
        var geometry = plan.HierarchyGeometry!;
        geometry.NaturalWidth.Should().Be(320);
        geometry.NaturalHeight.Should().Be(140);
        geometry.Nodes[0].X.Should().BeApproximately(169.288503937008, 0.000001);
        geometry.Nodes[0].Y.Should().BeApproximately(0.0624409448818898, 0.000001);
        geometry.Nodes[1].X.Should().BeApproximately(77.859842519685, 0.000001);
        geometry.Nodes[1].Y.Should().BeApproximately(51.7870866141732, 0.000001);
        geometry.Nodes[2].X.Should().BeApproximately(125.213307086614, 0.000001);
        geometry.Nodes[2].Y.Should().BeApproximately(103.511653543307, 0.000001);
        geometry.Connectors.Should().OnlyContain(connector => connector.Points.Count == 3);
        geometry.Connectors[0].Points[0].X.Should().BeApproximately(205.714251968504, 0.000001);
        geometry.Connectors[0].Points[2].X.Should().BeApproximately(150.711338582677, 0.000001);
        geometry.Connectors[1].Points[0].X.Should().BeApproximately(114.285590551181, 0.000001);
        geometry.Connectors[1].Points[2].X.Should().BeApproximately(125.213307086614, 0.000001);
    }

    [Fact]
    public void SmartArtPlan_NativePyramidMatchesWordGeometryAndTextTreatment()
    {
        var smartArt = SmartArt.Create(SmartArtKind.List, ["Top", "Middle", "Lower", "Base"]);
        smartArt.LayoutId = "pyramid1";
        smartArt.ColorSchemeId = "accent2";
        smartArt.StyleId = "flat1";

        var plan = ChartSmartArtVisualPlanner.BuildSmartArtPlan(smartArt);

        plan.Nodes.Select(node => node.FillHex).Should().OnlyContain(hex => hex == "#7F0000");
        plan.Nodes.Select(node => node.TextHex).Should().OnlyContain(hex => hex == "#000000");
        plan.Nodes.Select(node => node.FontSizeDip)
            .Should().OnlyContain(size => Math.Abs(size - 18.48 * 96.0 / 72.0) < 0.001);
        plan.LayoutGeometry.Should().NotBeNull();
        var geometry = plan.LayoutGeometry!;
        geometry.NaturalWidth.Should().Be(300);
        geometry.NaturalHeight.Should().Be(150);
        geometry.Nodes.Select(node => (node.X, node.Y, node.Width, node.Height))
            .Should().ContainInOrder(
                (114, 6, 72, 33),
                (78, 41, 144, 33),
                (42, 76, 216, 33),
                (6, 111, 288, 33));
        geometry.Nodes[0].PolygonPoints.Select(point => (point.X, point.Y))
            .Should().ContainInOrder((136.5, 6), (163.5, 6), (186, 39), (114, 39));
    }

    [Fact]
    public void SmartArtPlan_CurrentWordPyramidUsesCachedDrawingSignatureAndThemeAccent()
    {
        var smartArt = SmartArt.Create(SmartArtKind.List, ["Top", "Middle", "Lower", "Base"]);
        smartArt.LayoutId = "pyramid1";
        smartArt.ColorSchemeId = "accent1_2";
        smartArt.StyleId = "simple1";
        smartArt.WidthPt = 300;
        smartArt.HeightPt = 150;
        var theme = DocumentTheme.Default with { PrimaryColorHex = "#156082", BodyFont = "Aptos" };

        var plan = ChartSmartArtVisualPlanner.BuildSmartArtPlan(smartArt, theme);

        plan.Nodes.Should().OnlyContain(node =>
            node.FillHex == "#156082"
            && node.TextHex == "#000000"
            && node.BorderHex == "#FFFFFF"
            && Math.Abs(node.FontSizeDip - 18.48 * 96.0 / 72.0) < 0.001
            && node.FontFamilyName == "Aptos");
        var geometry = plan.LayoutGeometry!;
        geometry.NaturalWidth.Should().Be(300);
        geometry.NaturalHeight.Should().Be(150);
        geometry.Nodes.Select(node => (node.X, node.Y, node.Width, node.Height))
            .Should().ContainInOrder(
                (112.5, 0, 75, 37.5),
                (75, 37.5, 150, 37.5),
                (37.5, 75, 225, 37.5),
                (0, 112.5, 300, 37.5));
    }

    [Fact]
    public void SmartArtPlan_CurrentWordPyramidPreservesImportedAnchorAspect()
    {
        var smartArt = SmartArt.Create(SmartArtKind.List, ["Top", "Middle", "Lower", "Base"]);
        smartArt.LayoutId = "pyramid1";
        smartArt.ColorSchemeId = "accent1_2";
        smartArt.StyleId = "simple1";
        smartArt.WidthPt = 432;
        smartArt.HeightPt = 252;

        var geometry = ChartSmartArtVisualPlanner.BuildSmartArtPlan(smartArt, DocumentTheme.Default).LayoutGeometry!;

        geometry.NaturalWidth.Should().Be(432);
        geometry.NaturalHeight.Should().Be(252);
        geometry.Nodes.Select(node => (node.X, node.Y, node.Width, node.Height))
            .Should().ContainInOrder(
                (162, 0, 108, 63),
                (108, 63, 216, 63),
                (54, 126, 324, 63),
                (0, 189, 432, 63));
        ChartSmartArtVisualPlanner.BuildSmartArtPlan(smartArt, DocumentTheme.Default).Nodes
            .Should().OnlyContain(node => Math.Abs(node.FontSizeDip - 18.48 * 96.0 / 72.0) < 0.001);
    }

    [Theory]
    [InlineData("list1", "BasicList", 4, 0, 128, 154)]
    [InlineData("vertbullet1", "VerticalBulletList", 4, 0, 128, 154)]
    [InlineData("process1", "BasicProcess", 4, 3, 344, 46)]
    [InlineData("cycle1", "Cycle", 4, 4, 200, 160)]
    [InlineData("pyramid1", "Pyramid", 4, 0, 300, 150)]
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
        pyramidGeometry.Nodes[0].X.Should().BeApproximately(114, 0.01);
        pyramidGeometry.Nodes[0].Width.Should().BeApproximately(72, 0.01);
        pyramidGeometry.Nodes[3].X.Should().BeApproximately(6, 0.01);
        pyramidGeometry.Nodes[3].Width.Should().BeApproximately(288, 0.01);
        pyramidGeometry.Nodes.Select(n => n.Y).Should().ContainInOrder(6, 41, 76, 111);
        pyramidGeometry.Nodes.Should().OnlyContain(n => n.HasPolygon);
        pyramidGeometry.Nodes[0].PolygonPoints.Select(p => (p.X, p.Y)).Should().ContainInOrder(
            (136.5, 6),
            (163.5, 6),
            (186, 39),
            (114, 39));
        pyramidGeometry.Nodes[3].PolygonPoints.Select(p => (p.X, p.Y)).Should().ContainInOrder(
            (28.5, 111),
            (271.5, 111),
            (294, 144),
            (6, 144));
        pyramidGeometry.Connectors.Should().BeEmpty();
        ChartSmartArtVisualPlanner.BuildSmartArtVisualSignature(pyramidPlan)
            .Should().Contain("polygons=0=136.5:6;163.5:6;186:39;114:39");

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
