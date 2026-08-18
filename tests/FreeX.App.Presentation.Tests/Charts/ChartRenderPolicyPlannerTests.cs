using FluentAssertions;

using FreeX.App.Presentation.Charts;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ChartRenderPolicyPlannerTests
{
    [Fact]
    public void BarGeometry_ResolvesGapOverlapAndClusterOffsetsOnce()
    {
        var chart = new ChartModel { Type = ChartType.Column };

        ChartRenderPolicyPlanner.ResolveBarHalfWidth(chart).Should().Be(0.35);
        ChartRenderPolicyPlanner.ResolveEffectiveBarOverlap(chart).Should().Be(-27);

        var first = ChartRenderPolicyPlanner.ResolveClusteredBarOffsets(0.35, 0, 2, -27);
        var second = ChartRenderPolicyPlanner.ResolveClusteredBarOffsets(0.35, 1, 2, -27);
        first.Left.Should().BeApproximately(-0.35, 1e-12);
        first.Right.Should().BeApproximately(-0.0416299559471366, 1e-12);
        second.Left.Should().BeApproximately(0.0416299559471366, 1e-12);
        second.Right.Should().BeApproximately(0.35, 1e-12);

        chart.BarGapWidth = 0;
        ChartRenderPolicyPlanner.ResolveBarHalfWidth(chart).Should().Be(0.5);
    }

    [Fact]
    public void SeriesMappingAndComboRouting_UseAuthoredIndexes()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            UseComboLineForSecondarySeries = true,
            ShowSecondaryAxis = true,
            SeriesColumnMappings =
            [
                new ChartSeriesColumnMapping(5, 2),
                new ChartSeriesColumnMapping(9, 4),
            ],
            ComboLineSeriesIndexes = [9],
            SecondaryAxisSeriesIndexes = [5],
        };

        ChartRenderPolicyPlanner.ShouldRenderSourceColumn(chart, 2, 2, 4).Should().BeTrue();
        ChartRenderPolicyPlanner.ShouldRenderSourceColumn(chart, 3, 2, 4).Should().BeFalse();
        ChartRenderPolicyPlanner.ResolveSeriesIndex(chart, 4, 2, 4).Should().Be(9);
        ChartRenderPolicyPlanner.CountClusteredSourceSeries(chart, 2, 4).Should().Be(1);
        ChartRenderPolicyPlanner.IsComboLineSeries(chart, 9).Should().BeTrue();
        ChartRenderPolicyPlanner.UsesSecondaryAxis(chart, 5).Should().BeTrue();
        ChartRenderPolicyPlanner.UsesSecondaryAxis(chart, 9).Should().BeFalse();
    }

    [Fact]
    public void LegendAndPiePointPolicy_ResolveDeclarationOrderAndAllExplosions()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            SeriesPlotOrder = [7, 3],
            LegendEntries = [new ChartLegendEntryModel(0, true)],
            ExplodedSliceIndex = 1,
            ExplodedSlices = [new ChartPointExplosion(0, 3, 0.2)],
        };

        ChartRenderPolicyPlanner.IsLegendEntryDeleted(chart, 7).Should().BeTrue();
        ChartRenderPolicyPlanner.IsLegendEntryDeleted(chart, 3).Should().BeFalse();
        ChartRenderPolicyPlanner.IsPieSliceExploded(chart, 0, 1).Should().BeTrue();
        ChartRenderPolicyPlanner.IsPieSliceExploded(chart, 0, 3).Should().BeTrue();
        ChartRenderPolicyPlanner.IsPieSliceExploded(chart, 0, 2).Should().BeFalse();
    }

    [Fact]
    public void ViewportAccessor_UsesChartDataPrecedenceAndFiniteNumericCoercion()
    {
        var sheetId = SheetId.New();
        var otherSheet = SheetId.New();
        var viewport = new ViewportModel(
            Cells:
            [
                new DisplayCell(2, 2, new NumberValue(10), "10", null, StyleId.Default, null),
                new DisplayCell(2, 3, new BoolValue(true), "TRUE", null, StyleId.Default, null),
            ],
            RowMetrics: [],
            ColMetrics: [],
            ChartDataCells:
            [
                new ChartDataCell(sheetId, 2, 2, "20", new NumberValue(20)),
                new ChartDataCell(otherSheet, 2, 2, "30", new NumberValue(30)),
            ]);

        var lookup = ChartViewportCellAccessorBuilder.Resolve(viewport, sheetId);
        lookup[(2, 2)].DisplayText.Should().Be("20");
        lookup[(2, 3)].DisplayText.Should().Be("TRUE");

        var accessor = ChartViewportCellAccessorBuilder.BuildValueAccessor(lookup);
        accessor(2, 2, out var rawNumber, out var number, out _).Should().BeTrue();
        rawNumber.Should().Be(new NumberValue(20));
        number.Should().Be(20);
        accessor(2, 3, out var rawBoolean, out var boolean, out _).Should().BeTrue();
        rawBoolean.Should().Be(new BoolValue(true));
        boolean.Should().Be(1);
    }

    /// <summary>
    /// R141 (MED, chart-hidden-merge-anchor-leak): a merged region's anchor cell can be exposed
    /// into the general viewport (<see cref="ViewportModel.Cells"/>) even while its own row is
    /// hidden, because the visible remainder of the merge still needs to render. When a chart has
    /// "Show data in hidden rows and columns" OFF, that exposure must not let the anchor's value
    /// leak back into the chart through <see cref="ChartViewportCellAccessorBuilder"/>'s
    /// viewport.Cells fallback -- the real <see cref="ViewportService"/> is used here (not a
    /// hand-built ViewportModel) because the fix lives in how it populates
    /// <see cref="ViewportModel.ChartDataCells"/> for exactly this case.
    /// </summary>
    [Fact]
    public void HiddenMergeAnchor_ExcludedFromChartLookupWhenShowHiddenDataIsOff()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        // B3:B4 merged, with B3 (the anchor, holding the real value) hidden and B4 visible --
        // exactly the "hidden anchor, visible remainder" shape that keeps the anchor exposed in
        // the general viewport for grid rendering.
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(42));
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 3, 2),
            new CellAddress(sheet.Id, 4, 2)));
        sheet.HiddenRows.Add(3);

        var chart = new ChartModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 3, 2), new CellAddress(sheet.Id, 4, 2)),
            ShowDataInHiddenRowsAndColumns = false,
        };
        sheet.Charts.Add(chart);

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 200, 300));

        var lookup = ChartViewportCellAccessorBuilder.Resolve(viewport, sheet.Id, chart.DataRange);

        // The anchor cell must not resolve to its real, hidden value: either the key is absent, or
        // it resolves to a blank placeholder -- never the leaked NumberValue(42).
        if (lookup.TryGetValue((3, 2), out var anchorCell))
        {
            anchorCell.RawValue.Should().NotBe(new NumberValue(42));
        }

        var accessor = ChartViewportCellAccessorBuilder.BuildValueAccessor(lookup);
        accessor(3, 2, out _, out var anchorValue, out _).Should().BeFalse();
        anchorValue.Should().Be(0);
    }

    /// <summary>
    /// Sibling of <see cref="HiddenMergeAnchor_ExcludedFromChartLookupWhenShowHiddenDataIsOff"/>:
    /// with "Show data in hidden rows and columns" ON, the same hidden merge anchor's value must
    /// still reach the chart -- the fix must not touch this case.
    /// </summary>
    [Fact]
    public void HiddenMergeAnchor_IncludedInChartLookupWhenShowHiddenDataIsOn()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(42));
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 3, 2),
            new CellAddress(sheet.Id, 4, 2)));
        sheet.HiddenRows.Add(3);

        var chart = new ChartModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 3, 2), new CellAddress(sheet.Id, 4, 2)),
            ShowDataInHiddenRowsAndColumns = true,
        };
        sheet.Charts.Add(chart);

        var viewport = new ViewportService().GetViewport(
            workbook,
            sheet.Id,
            new ViewportRequest(1, 1, 200, 300));

        var lookup = ChartViewportCellAccessorBuilder.Resolve(viewport, sheet.Id, chart.DataRange);

        var accessor = ChartViewportCellAccessorBuilder.BuildValueAccessor(lookup);
        accessor(3, 2, out var rawValue, out var anchorValue, out _).Should().BeTrue();
        rawValue.Should().Be(new NumberValue(42));
        anchorValue.Should().Be(42);
    }

    [Fact]
    public void DataPlan_PrefersEmbeddedCacheWithoutCallingTheViewport()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(
                new CellAddress(default, 1, 1),
                new CellAddress(default, 1, 1)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(4, "Actual", ["A", "B"], [10, 20]),
                new ChartEmbeddedSeriesData(8, "Plan", ["A", "B", "C"], [5, 15, 25]),
            ],
        };

        var plan = ChartLayoutRequestBuilder.TryResolveData(
            chart,
            (uint _, uint _, out double value, out string text) =>
            {
                value = 0;
                text = "";
                throw new InvalidOperationException("Embedded data must be authoritative.");
            });

        plan.Should().NotBeNull();
        plan!.Categories.Should().Equal("A", "B", "C");
        plan.Series.Select(series => series.SeriesIndex).Should().Equal(4, 8);
        plan.Series[1].Values.Should().Equal(5, 15, 25);
    }

    [Fact]
    public void DataPlan_AllowsTheNativeFallbackScatterOriginAsANarrowParameter()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstRowIsHeader = false,
            DataRange = new GridRange(
                new CellAddress(default, 1, 1),
                new CellAddress(default, 2, 2)),
        };
        ChartLayoutRequestBuilder.ChartCellValueAccessor accessor = (
            uint row,
            uint column,
            out ScalarValue? rawValue,
            out double value,
            out string displayText) =>
        {
            rawValue = column == 2 ? new NumberValue(row * 10) : null;
            value = column == 2 ? row * 10 : 0;
            displayText = rawValue is NumberValue number ? number.Value.ToString() : "";
            return rawValue is not null;
        };

        var plan = ChartLayoutRequestBuilder.TryResolveData(
            chart,
            accessor,
            missingScatterXOffset: 1);

        plan.Should().NotBeNull();
        plan!.Series.Should().ContainSingle();
        plan.Series[0].XValues.Should().Equal(1, 2);
        plan.Series[0].Values.Should().Equal(10, 20);
    }

    [Fact]
    public void AxisAndFamilyGeometryPolicy_IsDeterministic()
    {
        var chart = new ChartModel { Type = ChartType.Line, XAxisIsDateAxis = true };
        ChartRenderPolicyPlanner.TryResolveDateCategoryPositions(
            chart,
            ["2026-01-01", "2026-01-10"],
            out var positions,
            out var minimum,
            out var maximum).Should().BeTrue();
        positions.Should().HaveCount(2);
        (maximum - minimum).Should().Be(9);

        ChartRenderPolicyPlanner.ResolveAxisSide(AxisSide.Bottom, ChartAxisCrosses.Maximum)
            .Should().Be(AxisSide.Top);
        ChartRenderPolicyPlanner.ResolveAxisDisplayUnitDivisor(ChartAxisDisplayUnit.Millions, null)
            .Should().Be(1e6);
        ChartRenderPolicyPlanner.ResolveAxisDisplayUnitLabel(null, 2500)
            .Should().Be("2500");
        ChartRenderPolicyPlanner.ResolveBubbleRadius(25, 100, ChartBubbleSizeRepresents.Area)
            .Should().Be(10);
        ChartRenderPolicyPlanner.ResolvePieLabelRadiusFraction(ChartDataLabelPosition.OutsideEnd)
            .Should().Be(1.15);
        ChartRenderPolicyPlanner.ResolveSurfaceCellColor(50, 0, 100)
            .Should().Be(new CellColor(162, 153, 98));
        ChartRenderPolicyPlanner.CalculatePercentile([1, 3, 9], 25)
            .Should().Be(2);

        var box = ChartRenderPolicyPlanner.PlanBoxAndWhisker(
            new double?[] { 1, 2, 3, 4, 100 });
        box.Should().NotBeNull();
        box!.Median.Should().Be(3);
        box.Outliers.Should().Equal(100);

        chart.ErrorBarKind = ChartErrorBarKind.StdDev;
        chart.ErrorBarValue = 2;
        var errorAmounts = ChartRenderPolicyPlanner.ResolveErrorBarAmounts(
            chart,
            [10, 20],
            0,
            null,
            null);
        errorAmounts.Plus.Should().BeApproximately(2 * Math.Sqrt(50), 1e-12);
        errorAmounts.Minus.Should().Be(errorAmounts.Plus);
        ChartRenderPolicyPlanner.ParseErrorBarRangeCache(
            "<numCache><pt idx=\"1\"><v>2.5</v></pt><pt idx=\"0\"><v>1.5</v></pt></numCache>")
            .Should().Equal(1.5, 2.5);
    }

    [Fact]
    public void NativeRenderers_ContainOnlyAdaptersForSharedChartPolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var wpfRoot = Path.Combine(root, "src", "FreeX.App.UI");
        var wpfMain = File.ReadAllText(Path.Combine(wpfRoot, "ChartRenderer.cs"));
        var wpfSeries = File.ReadAllText(Path.Combine(wpfRoot, "ChartRenderer.SeriesFormatting.cs"));
        var wpfAxes = File.ReadAllText(Path.Combine(wpfRoot, "ChartRenderer.Axes.cs"));
        var wpfBubble = File.ReadAllText(Path.Combine(wpfRoot, "ChartRenderer.Bubble.cs"));
        var wpfSurface = File.ReadAllText(Path.Combine(wpfRoot, "ChartRenderer.Surface.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "src", "FreeX.App.Avalonia", "Charts", "MainWindow.Charts.cs"));
        var engine = File.ReadAllText(Path.Combine(root, "src", "FreeX.App.Presentation", "Charts", "ChartLayoutEngine.cs"));

        wpfMain.Should().Contain("ChartLayoutRequestBuilder.TryResolveData");
        wpfMain.Should().Contain("ChartViewportCellAccessorBuilder.Resolve");
        wpfMain.Should().NotContain("foreach (var cell in viewport.ChartDataCells)");
        wpfMain.Should().NotContain("NumberFormatter.Format");
        wpfSeries.Should().Contain("ChartRenderPolicyPlanner.ResolveBarHalfWidth");
        wpfSeries.Should().Contain("ChartRenderPolicyPlanner.ResolveSeriesIndex");
        wpfSeries.Should().Contain("ChartRenderPolicyPlanner.ResolvePieLabelRadiusFraction");
        wpfSeries.Should().Contain("ChartRenderPolicyPlanner.ResolveErrorBarAmounts");
        wpfSeries.Should().NotContain("chart.BarGapWidth is int");
        wpfSeries.Should().NotContain("chart.SecondaryAxisSeriesIndexes.Count == 0");
        wpfSeries.Should().NotContain("CalculateStandardError");
        wpfAxes.Should().Contain("ChartRenderPolicyPlanner.TryResolveDateCategoryPositions");
        wpfAxes.Should().Contain("ChartRenderPolicyPlanner.ResolveAxisDisplayUnitDivisor");
        wpfBubble.Should().Contain("ChartRenderPolicyPlanner.ResolveBubbleRadius");
        wpfBubble.Should().NotContain("Math.Sqrt(fraction)");
        wpfSurface.Should().Contain("ChartRenderPolicyPlanner.ResolveSurfaceCellColor");
        var wpfAdvanced = File.ReadAllText(Path.Combine(wpfRoot, "ChartRenderer.AdvancedFamilies.cs"));
        wpfAdvanced.Should().Contain("ChartRenderPolicyPlanner.PlanBoxAndWhisker");
        wpfAdvanced.Should().NotContain("lowerFence =");
        avalonia.Should().Contain("ChartViewportCellAccessorBuilder.BuildValueAccessor");
        avalonia.Should().NotContain("BuildChartCellAccessor(");
        engine.Should().Contain("ChartRenderPolicyPlanner.ResolveClusteredBarOffsets");
        engine.Should().Contain("ChartRenderPolicyPlanner.ResolveWaterfallBarColor");
        engine.Should().Contain("ChartRenderPolicyPlanner.PlanBoxAndWhisker");
        engine.Should().Contain("ChartRenderPolicyPlanner.ResolveErrorBarAmounts");
    }
}
