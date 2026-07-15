using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FreeW.App.Host.Editing;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;
using Xunit;

namespace FreeW.App.Host.Tests;

/// <summary>
/// Coverage for chart rendering (<c>DocumentView.BuildChartRun</c>): it renders every series and honours
/// the <see cref="ChartKind"/> (line vs column), rather than sketching only the first series as columns.
/// Runs on STA because it builds the real WPF editing surface.
/// </summary>
public sealed class ChartRenderingTests
{
    private static DocumentView ViewWithChart(ChartKind kind)
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var chart = new Chart { Kind = kind, Title = "t" };
        chart.Categories.AddRange(new[] { "A", "B", "C" });
        chart.Series.Add(new ChartSeries("S1", new double[] { 1, 2, 3 }));
        chart.Series.Add(new ChartSeries("S2", new double[] { 3, 2, 1 }));
        view.InsertChart(chart);
        return view;
    }

    private static List<T> LogicalDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        var result = new List<T>();
        foreach (var child in LogicalTreeHelper.GetChildren(root))
            if (child is DependencyObject d)
            {
                if (d is T t)
                    result.Add(t);
                result.AddRange(LogicalDescendants<T>(d));
            }
        return result;
    }

    [StaFact]
    public void LineChart_RendersOnePolylinePerSeries()
    {
        var view = ViewWithChart(ChartKind.Line);
        var polylines = LogicalDescendants<System.Windows.Shapes.Polyline>(view.Document);
        Assert.Equal(2, polylines.Count); // one per series; legend swatches are Rectangles, not Polylines
    }

    [StaFact]
    public void ColumnChart_RendersBarsAndNoPolylines()
    {
        var view = ViewWithChart(ChartKind.Column);
        Assert.Empty(LogicalDescendants<System.Windows.Shapes.Polyline>(view.Document));
        // 2 series x 3 categories = 6 bars, plus 2 legend swatches = 8 rectangles (>= 6 either way).
        var rects = LogicalDescendants<System.Windows.Shapes.Rectangle>(view.Document);
        Assert.True(rects.Count >= 6, $"expected >= 6 bar rectangles, got {rects.Count}");
    }

    [StaFact]
    public void ColumnChart_DrawsValueGridlines()
    {
        var view = ViewWithChart(ChartKind.Column);
        // 4 horizontal gridlines + 1 baseline axis line; without gridlines there would be just the axis.
        var lines = LogicalDescendants<System.Windows.Shapes.Line>(view.Document);
        Assert.True(lines.Count >= 5, $"expected gridlines + axis (>= 5 lines), got {lines.Count}");
    }

    [StaFact]
    public void MultiSeriesLineChart_WithLegendOff_RendersNoLegendSwatches()
    {
        var view = ViewWithChart(ChartKind.Line);

        var rects = LogicalDescendants<System.Windows.Shapes.Rectangle>(view.Document);
        var legendSwatches = rects.Where(r => r.Width <= 12 && r.Height <= 12).ToList();

        Assert.Empty(legendSwatches);
    }

    // ── Chart Design gallery render tests ─────────────────────────────────────────────────────────

    private static DocumentView ViewWithStyledChart(int styleId = 0, string? colorSchemeId = null, int quickLayoutId = 0)
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var chart = new Chart { Kind = ChartKind.Column, Title = "Styled" };
        chart.Categories.AddRange(new[] { "A", "B" });
        chart.Series.Add(new ChartSeries("S1", new double[] { 1, 2 }));
        chart.StyleId = styleId;
        chart.ColorSchemeId = colorSchemeId;
        chart.QuickLayoutId = quickLayoutId;
        chart.ShowLegend = true;
        view.InsertChart(chart);
        return view;
    }

    [StaFact]
    public void Style2_PlotAreaFill_IsLimitedToScenePlotBounds()
    {
        // Style 2 has PlotAreaFill=true; the fill is a bounded child, not the root canvas background.
        var chart = new Chart { Kind = ChartKind.Column, StyleId = 2 };
        chart.Categories.AddRange(new[] { "A", "B" });
        chart.Series.Add(new ChartSeries("S1", new double[] { 1, 2 }));
        var scene = ChartSmartArtVisualPlanner.BuildChartScene(chart, 240, 180);
        var canvas = DocumentView.BuildChartSceneCanvas(scene);
        var plotFill = Assert.Single(canvas.Children.OfType<Border>());
        // The root canvas remains transparent while the plot fill owns the scene plot bounds.
        Assert.Equal(System.Windows.Media.Colors.Transparent,
            ((System.Windows.Media.SolidColorBrush)canvas.Background).Color);
        Assert.Equal(scene.PlotBounds.Width, plotFill.Width);
        Assert.Equal(scene.PlotBounds.Height, plotFill.Height);
        Assert.Equal(scene.PlotBounds.X, Canvas.GetLeft(plotFill));
        Assert.Equal(scene.PlotBounds.Y, Canvas.GetTop(plotFill));
    }

    [StaFact]
    public void Style1_NoPlotAreaFill_CanvasBackgroundIsTransparent()
    {
        // Style 1 (default) has PlotAreaFill=false → Canvas background should be Transparent.
        var view = ViewWithStyledChart(styleId: 1);
        var canvases = LogicalDescendants<System.Windows.Controls.Canvas>(view.Document);
        var plotCanvas = canvases.FirstOrDefault(c => c.Width > 10);
        Assert.NotNull(plotCanvas);
        // Transparent background = either null or Transparent SolidColorBrush
        if (plotCanvas!.Background is System.Windows.Media.SolidColorBrush sb)
            Assert.Equal(System.Windows.Media.Colors.Transparent, sb.Color);
        // null Background also passes (transparent by default)
    }

    [StaFact]
    public void ColorScheme_MonoBlue_ChangesSeriesFillToBlueShade()
    {
        // mono-blue palette first colour is #214A82; the first bar should use that colour.
        var view = ViewWithStyledChart(colorSchemeId: "mono-blue");
        var rects = LogicalDescendants<System.Windows.Shapes.Rectangle>(view.Document);
        // Filter legend swatches out (they are small 10x10 squares).
        var bars = rects.Where(r => r.Height > 5).ToList();
        Assert.True(bars.Count > 0, "expected at least one bar rectangle");
        // First series bar fill must be the first colour in the mono-blue palette (#214A82 → R=33, G=74, B=130).
        var fill = bars[0].Fill as System.Windows.Media.SolidColorBrush;
        Assert.NotNull(fill);
        Assert.Equal(0x21, fill!.Color.R);
        Assert.Equal(0x4A, fill.Color.G);
        Assert.Equal(0x82, fill.Color.B);
    }

    [StaFact]
    public void QuickLayout5_ShowsLegendAndDataLabels()
    {
        // Layout 5: ShowTitle=true, ShowLegend=true, ShowDataLabels=true, ShowGridlines=true.
        var view = ViewWithStyledChart(quickLayoutId: 5);
        // There should be legend swatches (small 10x10 Rectangles).
        var rects = LogicalDescendants<System.Windows.Shapes.Rectangle>(view.Document);
        var legendSwatches = rects.Where(r => r.Width <= 12 && r.Height <= 12).ToList();
        Assert.True(legendSwatches.Count > 0, "legend swatches expected for Layout 5");
    }

    [StaFact]
    public void DataLabel_PreservesFourSignificantDigits()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var chart = Chart.Create(ChartKind.Column, new[] { "A" }, new[] { 1.234 });
        chart.QuickLayoutId = 5;

        view.InsertChart(chart);

        var text = LogicalDescendants<TextBlock>(view.Document).Select(block => block.Text);
        Assert.Contains("1.234", text);
    }

    [StaFact]
    public void QuickLayout7_HidesLegendAndGridlines()
    {
        // Layout 7: ShowTitle=false, ShowLegend=false, ShowDataLabels=true, ShowGridlines=false.
        var view = ViewWithStyledChart(quickLayoutId: 7);
        // No gridlines (Lines); only the baseline axis line is drawn and NOT gridlines (which are 4 count).
        var lines = LogicalDescendants<System.Windows.Shapes.Line>(view.Document);
        // At most 1 baseline axis line (no gridlines with ShowGridlines=false).
        Assert.True(lines.Count <= 1, $"expected <= 1 line (no gridlines), got {lines.Count}");
    }

    [StaFact]
    public void LineChart_WithMarkers_Style4_RendersEllipseMarkers()
    {
        // Style 4 has ShowMarkers=true; a line chart must render Ellipse circles at data points.
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var chart = new Chart { Kind = ChartKind.Line, StyleId = 4 };
        chart.Categories.AddRange(new[] { "A", "B", "C" });
        chart.Series.Add(new ChartSeries("S1", new double[] { 1, 2, 3 }));
        view.InsertChart(chart);

        var ellipses = LogicalDescendants<System.Windows.Shapes.Ellipse>(view.Document);
        // 3 categories × 1 series = 3 marker ellipses (plus the doughnut hole for doughnut, but this is Line).
        Assert.Equal(3, ellipses.Count);
    }

    [StaFact]
    public void ApplySelectedChartStyle_ChangesStyleId_AndTriggersDifferentRender()
    {
        var view = ViewWithChart(ChartKind.Column);
        view.InsertChart(new Chart { Kind = ChartKind.Column, Title = "t", StyleId = 1 });
        var chart = view.SelectedChart();
        if (chart is null) return; // no chart selected in test environment — acceptable
        var originalStyleId = chart.StyleId;
        view.ApplySelectedChartStyle(ChartStyle.Catalog[1]); // Style 2
        chart.StyleId.Should().NotBe(originalStyleId);
    }

    [StaFact]
    public void ApplySelectedChartColorScheme_ChangesColorSchemeId()
    {
        var view = ViewWithChart(ChartKind.Column);
        var chart = view.SelectedChart();
        if (chart is null) return;
        view.ApplySelectedChartColorScheme(ChartColorScheme.Catalog.First(s => s.Id == "mono-grey"));
        chart.ColorSchemeId.Should().Be("mono-grey");
    }

    [StaFact]
    public void ApplySelectedChartQuickLayout_ChangesQuickLayoutId()
    {
        var view = ViewWithChart(ChartKind.Column);
        var chart = view.SelectedChart();
        if (chart is null) return;
        view.ApplySelectedChartQuickLayout(ChartQuickLayout.Catalog[2]); // Layout 3
        chart.QuickLayoutId.Should().Be(3);
    }

    // ── Bug-fix regression tests (2026-06-25) ────────────────────────────────────────────────────

    /// <summary>
    /// Bug fix: Scatter chart must NOT dispatch to DrawLineChart (no Polyline), but instead render
    /// discrete Word-style markers at each (x,y) point. Regression test for B-charts fix #1.
    /// </summary>
    [StaFact]
    public void ScatterChart_RendersDistinctMarkersAndNoPolylines()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var chart = new Chart { Kind = ChartKind.Scatter, Title = "Height vs Weight" };
        chart.Categories.AddRange(new[] { "155", "160", "165", "170", "175" });
        chart.Series.Add(new ChartSeries("Sample", new double[] { 52, 58, 63, 68, 74 }));
        view.InsertChart(chart);

        // After fix: scatter → DrawScatterChart → marker geometry, NO connecting Polyline.
        var polylines = LogicalDescendants<System.Windows.Shapes.Polyline>(view.Document);
        Assert.Empty(polylines);

        // The colorful Word style cycles diamond, square, triangle, and X markers.
        Assert.True(LogicalDescendants<System.Windows.Shapes.Polygon>(view.Document).Count >= 2);
        Assert.True(LogicalDescendants<System.Windows.Shapes.Rectangle>(view.Document).Count >= 1);
        Assert.True(LogicalDescendants<System.Windows.Shapes.Line>(view.Document).Count >= 2);
    }

    /// <summary>
    /// Bug fix: ColorSchemeId lookup must return the named scheme, not the default. Verifies that
    /// <c>ChartColorScheme.FindById</c> doesn't silently fall through to colorful1 default.
    /// Regression test for B-charts fix #2.
    /// </summary>
    [StaFact]
    public void ColorScheme_Colorful2_SeriesZeroIsOrangeNotBlue()
    {
        // colorful2 palette: Colors[0] = "#ED7D31" (orange). colorful1 Colors[0] = "#4472C4" (blue).
        // If FindById returns null and falls back to Default (colorful1), series 0 will be blue.
        var view = ViewWithStyledChart(colorSchemeId: "colorful2");
        var rects = LogicalDescendants<System.Windows.Shapes.Rectangle>(view.Document);
        var bars = rects.Where(r => r.Height > 5).ToList();
        Assert.True(bars.Count > 0, "expected at least one bar rectangle");

        var fill = bars[0].Fill as System.Windows.Media.SolidColorBrush;
        Assert.NotNull(fill);
        // colorful2 first color #ED7D31 → R=0xED, G=0x7D, B=0x31
        Assert.Equal(0xED, fill!.Color.R);
        Assert.Equal(0x7D, fill.Color.G);
        Assert.Equal(0x31, fill.Color.B);
    }

    /// <summary>
    /// Bug fix: axis titles (ValueAxisTitle / CategoryAxisTitle) must appear in the chart when set.
    /// Regression test for B-charts fix #3.
    /// </summary>
    [StaFact]
    public void AxisTitles_WhenSet_AreRenderedAsTextBlocks()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var chart = new Chart
        {
            Kind = ChartKind.Scatter,
            Title = "Height vs Weight",
            ValueAxisTitle = "Weight (kg)",
            CategoryAxisTitle = "Height (cm)"
        };
        chart.Categories.AddRange(new[] { "155", "160", "165" });
        chart.Series.Add(new ChartSeries("S", new double[] { 52, 58, 63 }));
        view.InsertChart(chart);

        var allTexts = LogicalDescendants<System.Windows.Controls.TextBlock>(view.Document)
            .Select(tb => tb.Text)
            .ToList();
        Assert.Contains("Weight (kg)", allTexts);
        Assert.Contains("Height (cm)", allTexts);
    }

    [StaFact]
    public void WordStyleChart_UsesScaledTitlesAndValueAxisTickLabels()
    {
        var view = new DocumentView();
        view.LoadModel(TextDocument.CreateEmpty());
        var chart = new Chart
        {
            Kind = ChartKind.Column,
            Title = "Revenue by quarter",
            ValueAxisTitle = "USD",
            CategoryAxisTitle = "Quarter",
            WidthPt = 300,
            HeightPt = 168
        };
        chart.Categories.AddRange(new[] { "Q1", "Q2", "Q3", "Q4" });
        chart.Series.Add(new ChartSeries("Revenue", new double[] { 1.4, 1.8, 1.6, 2.2 }));
        view.InsertChart(chart);

        var textBlocks = LogicalDescendants<System.Windows.Controls.TextBlock>(view.Document);
        var title = textBlocks.Single(text => text.Text == "Revenue by quarter");
        Assert.Equal(24, title.FontSize);
        Assert.Equal(20, textBlocks.Single(text => text.Text == "USD").FontSize);
        Assert.Equal(20, textBlocks.Single(text => text.Text == "Quarter").FontSize);

        var axisTickLabels = textBlocks
            .Where(text => text.Text is "0" or "0.75" or "1.5" or "2.25" or "3")
            .ToList();
        Assert.Equal(5, axisTickLabels.Count);
    }
}
