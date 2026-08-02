namespace FreeP.Core.Model;

/// <summary>Working values for a PowerPoint-style chart series formatting edit.</summary>
public sealed record ChartSeriesOptions(
    int SeriesIndex,
    bool SmoothLine,
    bool OnSecondaryAxis,
    double? LineWidthPt,
    ChartMarkerSymbol MarkerSymbol,
    double? MarkerSizePt,
    ThemeAwareColor? FillColor = null,
    ShapeFill? Fill = null,
    ThemeAwareColor? LineColor = null,
    OutlineDash LineDash = OutlineDash.Solid,
    bool NoLine = false,
    ChartDataLabels? DataLabels = null,
    bool ShowBubbleSize = false,
    ChartErrorBars? ErrorBars = null,
    ChartTrendline? Trendline = null,
    ChartType? OverrideChartType = null,
    bool? InvertIfNegative = null);
