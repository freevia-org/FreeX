using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Legends;
using OxyPlot.Series;
using Freexcel.Core.Model;

namespace Freexcel.App.UI;

public static partial class ChartRenderer
{
    private static bool ShouldSkipScatterXColumn(ChartModel chart, uint col, uint dataStartCol) =>
        chart.Type == ChartType.Scatter
            && !chart.FirstColIsCategories
            && col == dataStartCol;

    private static int GetSeriesIndex(ChartModel chart, uint col, uint dataStartCol) =>
        (int)(col - dataStartCol - (chart.Type == ChartType.Scatter && !chart.FirstColIsCategories ? 1 : 0));

    private static ChartSeriesFormat? GetSeriesFormat(ChartModel chart, int seriesIndex) =>
        chart.SeriesFormats.LastOrDefault(format => format.SeriesIndex == seriesIndex);

    private static ChartPointDataLabelFormat? GetPointDataLabelFormat(ChartModel chart, int seriesIndex, int pointIndex) =>
        chart.PointDataLabelFormats.LastOrDefault(format => format.SeriesIndex == seriesIndex && format.PointIndex == pointIndex);

    private static void ApplyLineFormat(LineSeries series, ChartSeriesFormat? format, WorkbookTheme theme)
    {
        if (format is null)
            return;
        if (format.ResolveStrokeColor(theme) is { } stroke)
            series.Color = OxyColor.FromRgb(stroke.R, stroke.G, stroke.B);
        else if (format.ResolveFillColor(theme) is { } fill)
            series.Color = OxyColor.FromRgb(fill.R, fill.G, fill.B);
        if (format.StrokeThickness is { } thickness)
            series.StrokeThickness = thickness;
        if (format.DashStyle is { } dashStyle)
            series.LineStyle = ToOxyLineStyle(dashStyle);
        if (format.MarkerStyle is { } markerStyle)
            series.MarkerType = ToOxyMarkerType(markerStyle);
        if (format.MarkerSize is { } markerSize)
            series.MarkerSize = Math.Clamp(markerSize, 1, 20);
        if (format.ResolveFillColor(theme) is { } markerFill)
            series.MarkerFill = OxyColor.FromRgb(markerFill.R, markerFill.G, markerFill.B);
        if (format.ResolveStrokeColor(theme) is { } markerStroke)
            series.MarkerStroke = OxyColor.FromRgb(markerStroke.R, markerStroke.G, markerStroke.B);
        if (format.StrokeThickness is { } markerStrokeThickness)
            series.MarkerStrokeThickness = markerStrokeThickness;
    }

    private static void ApplyRectangleBarFormat(RectangleBarSeries series, ChartSeriesFormat? format, WorkbookTheme theme)
    {
        if (format is null)
            return;
        if (format.ResolveFillColor(theme) is { } fill)
            series.FillColor = OxyColor.FromRgb(fill.R, fill.G, fill.B);
        if (format.ResolveStrokeColor(theme) is { } stroke)
            series.StrokeColor = OxyColor.FromRgb(stroke.R, stroke.G, stroke.B);
        if (format.StrokeThickness is { } thickness)
            series.StrokeThickness = thickness;
    }

    private static void ApplyBarFormat(BarSeries series, ChartSeriesFormat? format, WorkbookTheme theme)
    {
        if (format is null)
            return;
        if (format.ResolveFillColor(theme) is { } fill)
            series.FillColor = OxyColor.FromRgb(fill.R, fill.G, fill.B);
        if (format.ResolveStrokeColor(theme) is { } stroke)
            series.StrokeColor = OxyColor.FromRgb(stroke.R, stroke.G, stroke.B);
        if (format.StrokeThickness is { } thickness)
            series.StrokeThickness = thickness;
    }

    private static void ApplyPieFormat(PieSeries series, ChartSeriesFormat? format, WorkbookTheme theme)
    {
        if (format is null)
            return;
        if (format.ResolveStrokeColor(theme) is { } stroke)
            series.Stroke = OxyColor.FromRgb(stroke.R, stroke.G, stroke.B);
        if (format.StrokeThickness is { } thickness)
            series.StrokeThickness = thickness;
    }

    private static void ApplyPieDataLabelStyle(PieSeries series, ChartModel chart, WorkbookTheme theme)
    {
        series.FontSize = chart.DataLabelFontSize;
        if (chart.ResolveDataLabelTextColor(theme) is not { } color)
            return;

        var oxyColor = OxyColor.FromRgb(color.R, color.G, color.B);
        series.TextColor = oxyColor;
        series.InsideLabelColor = oxyColor;
    }

    private static void ApplyAreaFormat(AreaSeries series, ChartSeriesFormat? format, WorkbookTheme theme)
    {
        if (format is null)
            return;
        if (format.ResolveStrokeColor(theme) is { } stroke)
            series.Color = OxyColor.FromRgb(stroke.R, stroke.G, stroke.B);
        if (format.ResolveFillColor(theme) is { } fill)
            series.Fill = OxyColor.FromRgb(fill.R, fill.G, fill.B);
        if (format.StrokeThickness is { } thickness)
            series.StrokeThickness = thickness;
        if (format.DashStyle is { } dashStyle)
            series.LineStyle = ToOxyLineStyle(dashStyle);
    }

    private static void ApplyScatterFormat(ScatterSeries series, ChartSeriesFormat? format, WorkbookTheme theme)
    {
        if (format is null)
            return;
        if (format.ResolveFillColor(theme) is { } fill)
            series.MarkerFill = OxyColor.FromRgb(fill.R, fill.G, fill.B);
        if (format.ResolveStrokeColor(theme) is { } stroke)
            series.MarkerStroke = OxyColor.FromRgb(stroke.R, stroke.G, stroke.B);
        if (format.StrokeThickness is { } thickness)
            series.MarkerStrokeThickness = thickness;
        if (format.MarkerStyle is { } markerStyle)
            series.MarkerType = ToOxyMarkerType(markerStyle);
        if (format.MarkerSize is { } markerSize)
            series.MarkerSize = Math.Clamp(markerSize, 1, 30);
    }

    private static bool ShouldUseNativeValueLabels(ChartModel chart) =>
        ChartDataLabelFormatter.ShouldUseNativeValueLabels(chart);

    private static void ApplyNativeDataLabelStyle(PlotElement element, ChartModel chart, WorkbookTheme theme)
    {
        if (!ShouldUseNativeValueLabels(chart))
            return;

        element.FontSize = chart.DataLabelFontSize;
        if (chart.ResolveDataLabelTextColor(theme) is { } color)
            element.TextColor = OxyColor.FromRgb(color.R, color.G, color.B);
    }

    private static bool ShouldUseAnnotationLabels(ChartModel chart) =>
        ChartDataLabelFormatter.ShouldUseAnnotationLabels(chart);

    private static void AddDataLabelAnnotation(
        PlotModel model,
        ChartModel chart,
        WorkbookTheme theme,
        string seriesName,
        int seriesIndex,
        int pointIndex,
        string categoryName,
        double x,
        double y,
        double value)
    {
        var pointFormat = GetPointDataLabelFormat(chart, seriesIndex, pointIndex);
        var textColor = pointFormat?.ResolveTextColor(theme) ?? chart.ResolveDataLabelTextColor(theme);
        var borderColor = pointFormat?.ResolveBorderColor(theme) ?? chart.ResolveDataLabelBorderColor(theme);
        var fillColor = pointFormat?.ResolveFillColor(theme) ?? chart.ResolveDataLabelFillColor(theme);
        model.Annotations.Add(new TextAnnotation
        {
            Text = ChartDataLabelFormatter.FormatDataLabel(chart, seriesName, categoryName, value),
            TextPosition = new DataPoint(x, y),
            TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
            TextVerticalAlignment = chart.DataLabelPosition == ChartDataLabelPosition.InsideEnd
                ? OxyPlot.VerticalAlignment.Top
                : OxyPlot.VerticalAlignment.Bottom,
            TextColor = ToOxyColor(textColor) ?? OxyColors.Automatic,
            FontSize = pointFormat?.FontSize ?? chart.DataLabelFontSize,
            Stroke = ToOxyColor(borderColor) ?? (chart.ShowDataLabelCallouts ? OxyColors.Gray : OxyColors.Transparent),
            StrokeThickness = pointFormat?.BorderThickness ?? (chart.DataLabelBorderThickness > 0 ? chart.DataLabelBorderThickness : chart.ShowDataLabelCallouts ? 1 : 0),
            Background = ToOxyColor(fillColor) ?? (chart.ShowDataLabelCallouts ? OxyColor.FromAColor(235, OxyColors.White) : OxyColors.Transparent),
            TextRotation = chart.DataLabelAngle,
            Padding = new OxyThickness(chart.ShowDataLabelCallouts ? 4 : 2)
        });
    }

    private static OxyColor? ToOxyColor(CellColor? color) =>
        color is { } value ? OxyColor.FromRgb(value.R, value.G, value.B) : null;

    private static void AddLineDataLabelAnnotations(
        PlotModel model,
        ChartModel chart,
        WorkbookTheme theme,
        LineSeries series,
        string seriesName,
        int seriesIndex,
        IReadOnlyList<string> categories)
    {
        if (!ShouldUseAnnotationLabels(chart))
            return;

        for (var pointIndex = 0; pointIndex < series.Points.Count; pointIndex++)
        {
            var point = series.Points[pointIndex];
            AddDataLabelAnnotation(
                model,
                chart,
                theme,
                seriesName,
                seriesIndex,
                pointIndex,
                ChartDataLabelFormatter.GetCategory(categories, (int)Math.Round(point.X)),
                point.X,
                point.Y,
                point.Y);
        }
    }

    private static bool UsesSecondaryAxis(ChartModel chart, int seriesIndex)
    {
        if (!chart.ShowSecondaryAxis || seriesIndex <= 0)
            return false;

        return chart.SecondaryAxisSeriesIndexes.Count == 0 ||
               chart.SecondaryAxisSeriesIndexes.Contains(seriesIndex);
    }

    private static bool IsComboLineSeries(ChartModel chart, int seriesIndex)
    {
        if (!ChartTypeSupport.SupportsComboLineOverlay(chart.Type) || !chart.UseComboLineForSecondarySeries || seriesIndex <= 0)
            return false;

        return chart.ComboLineSeriesIndexes.Contains(seriesIndex);
    }

    private static LabelPlacement ToOxyLabelPlacement(ChartDataLabelPosition position) =>
        position switch
        {
            ChartDataLabelPosition.Center => LabelPlacement.Middle,
            ChartDataLabelPosition.InsideEnd => LabelPlacement.Inside,
            ChartDataLabelPosition.OutsideEnd => LabelPlacement.Outside,
            _ => LabelPlacement.Outside
        };

    private static double ToLabelMargin(ChartDataLabelPosition position) =>
        position switch
        {
            ChartDataLabelPosition.Center => -8,
            ChartDataLabelPosition.InsideEnd => -4,
            ChartDataLabelPosition.OutsideEnd => 8,
            _ => 4
        };

    private static void AddLinePoints(
        LineSeries series,
        ChartModel chart,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        uint dataStartRow,
        uint endRow,
        uint col,
        List<DataPoint>? trendPoints,
        out List<DataPoint>? capturedTrendPoints)
    {
        var i = 0;
        for (uint r = dataStartRow; r <= endRow; r++, i++)
        {
            if (cellLookup.TryGetValue((r, col), out var cell)
                && double.TryParse(cell.DisplayText, out var v))
            {
                var point = new DataPoint(i, v);
                series.Points.Add(point);
                trendPoints?.Add(point);
            }
            else if (cellLookup.TryGetValue((r, col), out cell) && string.IsNullOrWhiteSpace(cell.DisplayText))
            {
                if (chart.BlankDisplayMode == ChartBlankDisplayMode.Zero)
                {
                    var point = new DataPoint(i, 0);
                    series.Points.Add(point);
                    trendPoints?.Add(point);
                }
                else if (chart.BlankDisplayMode == ChartBlankDisplayMode.Gap)
                {
                    series.Points.Add(new DataPoint(i, double.NaN));
                }
            }
        }

        capturedTrendPoints = trendPoints;
    }

    private static void AddSecondaryAxisIfRequested(PlotModel model, ChartModel chart)
    {
        if (!chart.ShowSecondaryAxis || !ChartTypeSupport.SupportsSecondaryAxis(chart.Type))
            return;

        if (!HasAnySecondaryAxisSeries(chart))
            return;

        if (model.Axes.Any(axis => axis.Key == SecondaryYAxisKey))
            return;

        model.Axes.Add(new LinearAxis
        {
            Key = SecondaryYAxisKey,
            Position = AxisPosition.Right,
            Title = "Secondary"
        });
    }

    private static bool HasAnySecondaryAxisSeries(ChartModel chart)
    {
        var seriesCount = ChartTypeSupport.GetDataSeriesCount(chart);
        if (seriesCount < 2)
            return false;

        return chart.SecondaryAxisSeriesIndexes.Count == 0
            ? seriesCount > 1
            : chart.SecondaryAxisSeriesIndexes.Any(index => index > 0 && index < seriesCount);
    }

    private static void ConfigureLegend(PlotModel model, ChartModel chart, WorkbookTheme theme)
    {
        if (!chart.ShowLegend || chart.LegendPosition == ChartLegendPosition.None)
            return;

        var legend = new Legend
        {
            LegendPlacement = chart.LegendOverlay ? LegendPlacement.Inside : LegendPlacement.Outside,
            LegendTextColor = ToOxyColor(chart.ResolveLegendTextColor(theme)) ?? OxyColors.Automatic,
            LegendFontSize = chart.LegendFontSize,
            LegendBackground = ToOxyColor(chart.ResolveLegendFillColor(theme)) ?? OxyColors.Undefined,
            LegendBorder = ToOxyColor(chart.ResolveLegendBorderColor(theme)) ?? OxyColors.Undefined,
            LegendBorderThickness = chart.LegendBorderThickness,
            LegendPosition = GetLegendPosition(chart.LegendPosition, chart.LegendOverlay)
        };
        model.Legends.Add(legend);
    }

    private static OxyPlot.Legends.LegendPosition GetLegendPosition(ChartLegendPosition position, bool overlay) =>
        position switch
        {
            ChartLegendPosition.Left => overlay ? OxyPlot.Legends.LegendPosition.LeftTop : OxyPlot.Legends.LegendPosition.LeftMiddle,
            ChartLegendPosition.Top => OxyPlot.Legends.LegendPosition.TopCenter,
            ChartLegendPosition.Bottom => OxyPlot.Legends.LegendPosition.BottomCenter,
            _ => overlay ? OxyPlot.Legends.LegendPosition.RightTop : OxyPlot.Legends.LegendPosition.RightMiddle
        };
}
