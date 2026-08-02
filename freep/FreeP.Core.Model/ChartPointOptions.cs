namespace FreeP.Core.Model;

/// <summary>Editable formatting overrides for one chart data point.</summary>
public sealed record ChartPointOptions(
    int SeriesIndex,
    int PointIndex,
    ThemeAwareColor? FillColor,
    ShapeFill? Fill,
    ThemeAwareColor? StrokeColor,
    double? StrokeWidthPt,
    ChartMarkerSymbol? MarkerSymbol,
    double? MarkerSizePt,
    ChartDataLabels? DataLabels = null,
    bool ShowBubbleSize = false,
    int? ExplosionPercent = null);
