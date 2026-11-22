using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Free.Shared.Drawing;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Selection;
using FreeX.App.Presentation.Text;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Legends;
using OxyPlot.Wpf;

namespace FreeX.App.UI;

/// <summary>
/// Chart element hit-test geometry for the WPF host. WPF paints charts through OxyPlot, whose own
/// auto-layout decides where the title, legend, plot area and axis gutters land, so those rectangles are
/// read back from the laid-out <see cref="PlotModel"/> (authoritative) and the portable
/// <see cref="ChartLayoutEngine"/> supplies the series/axis/gridline geometry inside that plot rectangle.
/// Everything is returned in the chart object's own unscaled pixel space.
/// </summary>
public static partial class ChartRenderer
{
    /// <summary>
    /// Builds the element map for <paramref name="chart"/> as it is currently painted at
    /// <paramref name="renderScale"/> (the same value <see cref="Render(ChartModel, ViewportModel, WorkbookTheme?, double)"/>
    /// is called with, because OxyPlot's gutters depend on the bitmap pixel density).
    /// </summary>
    public static ChartElementMap BuildElementMap(
        ChartModel chart,
        ViewportModel viewport,
        WorkbookTheme? theme,
        double renderScale)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(viewport);

        var measurer = WpfChartTextMeasurer.Instance;
        var width = Math.Max(1, chart.Width);
        var height = Math.Max(1, chart.Height);
        var scale = NormalizeRenderScale(renderScale);

        ChartElementFrame? frame = null;
        LayoutRect? plot = null;
        try
        {
            var model = BuildPlotModel(chart, viewport, theme ?? WorkbookTheme.Office);
            if (model is not null)
            {
                ApplyManualLayouts(model, chart, scale);

                // Exporting runs OxyPlot's layout pass, which fills PlotArea/TitleArea/LegendArea.
                using var stream = new MemoryStream();
                new PngExporter
                {
                    Width = Math.Max(1, (int)Math.Ceiling(chart.Width * scale)),
                    Height = Math.Max(1, (int)Math.Ceiling(chart.Height * scale)),
                }.Export(model, stream);

                plot = ToRect(model.PlotArea, scale);
                frame = new ChartElementFrame
                {
                    TitleBounds = string.IsNullOrWhiteSpace(model.Title) ? null : ToRectOrNull(model.TitleArea, scale),
                    LegendBounds = model.IsLegendVisible
                        ? ToRectOrNull(model.Legends.OfType<LegendBase>().FirstOrDefault()?.LegendArea, scale)
                        : null,
                    PlotAndAxisBounds = ToRect(model.PlotAndAxisArea, scale),
                };
            }
        }
        catch (Exception ex)
        {
            // Hit-testing must never break the click: fall back to the coarse regions below.
            GridRenderFaultReporter.Report(ex, "chart_element_map");
            plot = null;
            frame = null;
        }

        ChartLayout? layout = null;
        if (plot is { Width: > 0, Height: > 0 } p && ChartLayoutEngine.IsSupported(chart.Type))
            layout = TryLayoutInsidePlot(chart, viewport, p, measurer);

        return ChartElementMapPlanner.Plan(chart, layout, width, height, measurer, frame, plot);
    }

    private static ChartLayout? TryLayoutInsidePlot(
        ChartModel chart,
        ViewportModel viewport,
        LayoutRect plot,
        ITextMeasurer measurer)
    {
        try
        {
            var accessor = FreeX.App.Presentation.Charts.ChartViewportCellAccessorBuilder.BuildValueAccessor(
                viewport,
                chart.DataRange.Start.Sheet);
            var request = ChartLayoutRequestBuilder.TryBuild(
                chart,
                new PlotRect(plot.X, plot.Y, plot.Width, plot.Height),
                accessor,
                measurer);
            if (request is null)
                return null;

            return ChartLayoutEngine.Layout(new ChartLayoutRequest
            {
                Chart = request.Chart,
                Categories = request.Categories,
                Series = request.Series,
                PlotArea = request.PlotArea,
                TextMeasurer = request.TextMeasurer,
                ReserveLegendGutter = false,
            });
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    private static LayoutRect ToRect(OxyRect rect, double scale) =>
        new(rect.Left / scale, rect.Top / scale, rect.Width / scale, rect.Height / scale);

    private static LayoutRect? ToRectOrNull(OxyRect? rect, double scale) =>
        rect is { Width: > 0, Height: > 0 } r ? ToRect(r, scale) : null;

    private sealed class WpfChartTextMeasurer : ITextMeasurer
    {
        public static readonly WpfChartTextMeasurer Instance = new();

        public TextSize Measure(string? text, string? fontFamily, double fontSize, bool bold, bool italic)
        {
            if (string.IsNullOrEmpty(text))
                return TextSize.Empty;

            var typeface = new Typeface(
                new FontFamily(string.IsNullOrWhiteSpace(fontFamily) ? "Segoe UI" : fontFamily),
                italic ? FontStyles.Italic : FontStyles.Normal,
                bold ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal,
                FontStretches.Normal);
            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize > 0 ? fontSize : 9,
                Brushes.Black,
                1.0);
            return new TextSize(formatted.WidthIncludingTrailingWhitespace, formatted.Height);
        }
    }
}
