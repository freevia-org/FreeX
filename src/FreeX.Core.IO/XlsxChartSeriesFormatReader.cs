using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxChartSeriesFormatReader
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    public static bool TryReadSeriesFill(XElement series, int seriesIndex, out ChartSeriesFormat format)
    {
        format = default!;
        var shapeProperties = series.Element(ChartNs + "spPr");

        // Detect explicit <a:noFill/> on the series fill (not just absence of a fill).
        // An explicit noFill means the bar must render transparent; absence of spPr means
        // use the palette default instead.
        var hasNoFill = shapeProperties?.Element(DrawingNs + "noFill") is not null;

        var solidFill = shapeProperties?.Element(DrawingNs + "solidFill");
        CellColor? fillColor = null;
        WorkbookThemeColorReference? fillThemeColor = null;
        double? fillAlpha = null;
        if (solidFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, DrawingNs, out var themeColor))
        {
            fillThemeColor = themeColor;
            // R91-render-chart-series-format-5-4: <a:alpha> is lost on round-trip if never parsed.
            fillAlpha = XlsxDrawingColorReader.TryReadFillAlpha(solidFill, DrawingNs);
        }
        else if (solidFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(solidFill, DrawingNs, out var color))
        {
            fillColor = color;
            fillAlpha = XlsxDrawingColorReader.TryReadFillAlpha(solidFill, DrawingNs);
        }

        // R91-render-chart-series-format-5-1: a series fill of type <a:gradFill>/<a:pattFill> has
        // no dedicated model representation (only noFill/solidFill are modeled above). Rather than
        // silently dropping it (the whole <c:spPr> is destroyed on the next FreeX-triggered save —
        // see XlsxChartXmlWriter.Series.cs ToSeriesShapeProperties), preserve the authored element
        // verbatim so a gradient- or pattern-filled series survives a round trip. Picture fills
        // (<a:blipFill>) additionally need their embedded-image relationship/media re-plumbed
        // through the chart part's own .rels on write — out of scope here; still tracked as a gap.
        string? rawFillXml = null;
        if (!hasNoFill && solidFill is null)
        {
            var unmodeledFill = shapeProperties?.Element(DrawingNs + "gradFill")
                ?? shapeProperties?.Element(DrawingNs + "pattFill");
            if (unmodeledFill is not null)
                rawFillXml = unmodeledFill.ToString(SaveOptions.DisableFormatting);
        }

        var line = shapeProperties?.Element(DrawingNs + "ln");
        // An explicit <a:ln><a:noFill/> means the bar outline must NOT be drawn (e.g. a transparent
        // stacked spacer series like a target-band's "T_Low" helper, which sits invisibly beneath the
        // shaded band). Distinguish it from absence of a line (palette default outline).
        var hasNoLine = line?.Element(DrawingNs + "noFill") is not null;
        var lineFill = line?.Element(DrawingNs + "solidFill");
        CellColor? strokeColor = null;
        WorkbookThemeColorReference? strokeThemeColor = null;
        if (lineFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(lineFill, DrawingNs, out var lineThemeColor))
            strokeThemeColor = lineThemeColor;
        else if (lineFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(lineFill, DrawingNs, out var lineColor))
            strokeColor = lineColor;

        double? strokeThickness = null;
        if (int.TryParse(line?.Attribute("w")?.Value, out var emus))
            strokeThickness = Math.Clamp(emus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 0.5, 10);

        ChartLineDashStyle? dashStyle = line?.Element(DrawingNs + "prstDash") is { } dashElement
            ? XlsxChartTrendlineErrorBarReader.FromXlsxPresetDash(dashElement.Attribute("val")?.Value)
            : null;
        var invertIfNegative = XlsxChartScalarReader.ReadOptionalBool(series.Element(ChartNs + "invertIfNegative")?.Attribute("val")?.Value);
        if (!hasNoFill &&
            !hasNoLine &&
            fillColor is null &&
            fillThemeColor is null &&
            strokeColor is null &&
            strokeThemeColor is null &&
            strokeThickness is null &&
            dashStyle is null &&
            invertIfNegative is null &&
            rawFillXml is null &&
            fillAlpha is null)
        {
            return false;
        }

        format = new ChartSeriesFormat(
            seriesIndex,
            FillColor: fillColor,
            StrokeColor: strokeColor,
            StrokeThickness: strokeThickness,
            DashStyle: dashStyle,
            FillThemeColor: fillThemeColor,
            StrokeThemeColor: strokeThemeColor,
            InvertIfNegative: invertIfNegative,
            NoFill: hasNoFill,
            NoLine: hasNoLine,
            RawFillXml: rawFillXml,
            FillAlpha: fillAlpha);
        return true;
    }

    public static bool TryReadSeriesLine(XElement series, int seriesIndex, out ChartSeriesFormat format)
    {
        format = default!;
        var line = series
            .Element(ChartNs + "spPr")?
            .Element(DrawingNs + "ln");

        CellColor? strokeColor = null;
        WorkbookThemeColorReference? strokeThemeColor = null;
        var solidFill = line?.Element(DrawingNs + "solidFill");
        if (solidFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, DrawingNs, out var themeColor))
            strokeThemeColor = themeColor;
        else if (solidFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(solidFill, DrawingNs, out var color))
            strokeColor = color;

        double? strokeThickness = null;
        if (int.TryParse(line?.Attribute("w")?.Value, out var emus))
            strokeThickness = Math.Clamp(emus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 0.5, 10);

        ChartLineDashStyle? dashStyle = line?.Element(DrawingNs + "prstDash") is { } dashElement
            ? XlsxChartTrendlineErrorBarReader.FromXlsxPresetDash(dashElement.Attribute("val")?.Value)
            : null;
        var smooth = XlsxChartScalarReader.ReadOptionalBool(series.Element(ChartNs + "smooth")?.Attribute("val")?.Value);

        var marker = series.Element(ChartNs + "marker");
        var markerStyle = marker?.Element(ChartNs + "symbol") is { } symbolElement
            ? FromXlsxMarkerStyle(symbolElement.Attribute("val")?.Value)
            : (ChartMarkerStyle?)null;
        double? markerSize = null;
        if (int.TryParse(marker?.Element(ChartNs + "size")?.Attribute("val")?.Value, out var size))
            markerSize = Math.Clamp(size, 1, 30);
        CellColor? fillColor = null;
        WorkbookThemeColorReference? fillThemeColor = null;
        var markerFill = marker?
            .Element(ChartNs + "spPr")?
            .Element(DrawingNs + "solidFill");
        if (markerFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(markerFill, DrawingNs, out var markerThemeColor))
            fillThemeColor = markerThemeColor;
        else if (markerFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(markerFill, DrawingNs, out var markerColor))
            fillColor = markerColor;

        var markerLine = marker?
            .Element(ChartNs + "spPr")?
            .Element(DrawingNs + "ln");
        CellColor? markerBorderColor = null;
        WorkbookThemeColorReference? markerBorderThemeColor = null;
        var markerLineFill = markerLine?.Element(DrawingNs + "solidFill");
        if (markerLineFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(markerLineFill, DrawingNs, out var markerBorderTheme))
            markerBorderThemeColor = markerBorderTheme;
        else if (markerLineFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(markerLineFill, DrawingNs, out var markerBorder))
            markerBorderColor = markerBorder;

        double? markerBorderThickness = null;
        if (int.TryParse(markerLine?.Attribute("w")?.Value, out var markerLineEmus))
            markerBorderThickness = Math.Clamp(markerLineEmus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 0, 10);

        if (strokeColor is null &&
            strokeThemeColor is null &&
            strokeThickness is null &&
            dashStyle is null &&
            fillColor is null &&
            fillThemeColor is null &&
            markerStyle is null &&
            markerSize is null &&
            markerBorderColor is null &&
            markerBorderThemeColor is null &&
            markerBorderThickness is null &&
            smooth is null)
        {
            return false;
        }

        format = new ChartSeriesFormat(
            seriesIndex,
            FillColor: fillColor,
            StrokeColor: strokeColor,
            StrokeThickness: strokeThickness,
            DashStyle: dashStyle,
            MarkerStyle: markerStyle,
            MarkerSize: markerSize,
            FillThemeColor: fillThemeColor,
            StrokeThemeColor: strokeThemeColor,
            Smooth: smooth,
            MarkerBorderColor: markerBorderColor,
            MarkerBorderThemeColor: markerBorderThemeColor,
            MarkerBorderThickness: markerBorderThickness);
        return true;
    }

    /// <summary>
    /// Reads per-data-point fill colors from <c>&lt;c:dPt&gt;</c> elements within a series
    /// and appends them to <see cref="ChartModel.PointFillColors"/>. Despite the name (kept to avoid
    /// a cross-file rename of its pie/doughnut caller), this is generic over any chart family that
    /// emits <c>&lt;c:dPt&gt;</c> — it is also called from the bar/column, line, scatter, and combo
    /// series loops (R44-io-chart-datapoint-3-1) so a highlighted single point round-trips for those
    /// chart types too, not just pie/doughnut.
    /// </summary>
    public static void ApplyPiePointFills(XElement series, int seriesIndex, ChartModel chart)
    {
        foreach (var dPt in series.Elements(ChartNs + "dPt"))
        {
            if (!int.TryParse(dPt.Element(ChartNs + "idx")?.Attribute("val")?.Value, out var pointIndex) ||
                pointIndex < 0)
            {
                continue;
            }

            var spPr = dPt.Element(ChartNs + "spPr");
            var solidFill = spPr?.Element(DrawingNs + "solidFill");
            if (solidFill is not null)
            {
                CellColor? fillColor = null;
                WorkbookThemeColorReference? fillThemeColor = null;
                if (XlsxDrawingColorReader.TryReadThemeColorReference(solidFill, DrawingNs, out var themeColor))
                    fillThemeColor = themeColor;
                else if (XlsxDrawingColorReader.TryReadConcreteColor(solidFill, DrawingNs, out var color))
                    fillColor = color;

                if (fillColor is not null || fillThemeColor is not null)
                {
                    chart.PointFillColors.RemoveAll(existing =>
                        existing.SeriesIndex == seriesIndex && existing.PointIndex == pointIndex);
                    chart.PointFillColors.Add(new ChartPointFillFormat(seriesIndex, pointIndex, fillColor, fillThemeColor));
                }
            }

            // R82-io-chart-series-5-3: a dPt's own <c:marker> (Format Data Point > Marker Options)
            // is independent of its <c:spPr> fill above — a point can carry ONLY a marker override
            // (no fill override) and must still round-trip instead of being silently dropped.
            ApplyPointMarkerOverride(dPt, seriesIndex, pointIndex, chart);
        }
    }

    /// <summary>
    /// R82-io-chart-series-5-3: reads a &lt;c:dPt&gt;'s &lt;c:marker&gt; child (per-point marker
    /// symbol/size/fill/border override) into <see cref="ChartModel.PointMarkerFormats"/>. Mirrors
    /// the series-level marker reading in <see cref="TryReadSeriesLine"/>.
    /// </summary>
    private static void ApplyPointMarkerOverride(XElement dPt, int seriesIndex, int pointIndex, ChartModel chart)
    {
        var marker = dPt.Element(ChartNs + "marker");
        if (marker is null)
            return;

        var markerStyle = marker.Element(ChartNs + "symbol") is { } symbolElement
            ? FromXlsxMarkerStyle(symbolElement.Attribute("val")?.Value)
            : (ChartMarkerStyle?)null;
        double? markerSize = null;
        if (int.TryParse(marker.Element(ChartNs + "size")?.Attribute("val")?.Value, out var size))
            markerSize = Math.Clamp(size, 1, 30);

        var markerShapeProperties = marker.Element(ChartNs + "spPr");
        CellColor? fillColor = null;
        WorkbookThemeColorReference? fillThemeColor = null;
        var markerFill = markerShapeProperties?.Element(DrawingNs + "solidFill");
        if (markerFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(markerFill, DrawingNs, out var markerThemeColor))
            fillThemeColor = markerThemeColor;
        else if (markerFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(markerFill, DrawingNs, out var markerColor))
            fillColor = markerColor;

        var markerLine = markerShapeProperties?.Element(DrawingNs + "ln");
        CellColor? borderColor = null;
        WorkbookThemeColorReference? borderThemeColor = null;
        var markerLineFill = markerLine?.Element(DrawingNs + "solidFill");
        if (markerLineFill is not null && XlsxDrawingColorReader.TryReadThemeColorReference(markerLineFill, DrawingNs, out var borderTheme))
            borderThemeColor = borderTheme;
        else if (markerLineFill is not null && XlsxDrawingColorReader.TryReadConcreteColor(markerLineFill, DrawingNs, out var border))
            borderColor = border;

        double? borderThickness = null;
        if (int.TryParse(markerLine?.Attribute("w")?.Value, out var markerLineEmus))
            borderThickness = Math.Clamp(markerLineEmus / (double)DrawingMlCoordinateUnits.EmuPerPoint, 0, 10);

        if (markerStyle is null &&
            markerSize is null &&
            fillColor is null &&
            fillThemeColor is null &&
            borderColor is null &&
            borderThemeColor is null &&
            borderThickness is null)
        {
            return;
        }

        chart.PointMarkerFormats.RemoveAll(existing =>
            existing.SeriesIndex == seriesIndex && existing.PointIndex == pointIndex);
        chart.PointMarkerFormats.Add(new ChartPointMarkerFormat(
            seriesIndex,
            pointIndex,
            MarkerStyle: markerStyle,
            MarkerSize: markerSize,
            FillColor: fillColor,
            FillThemeColor: fillThemeColor,
            BorderColor: borderColor,
            BorderThemeColor: borderThemeColor,
            BorderThickness: borderThickness));
    }

    private static ChartMarkerStyle FromXlsxMarkerStyle(string? value) =>
        value switch
        {
            "none" => ChartMarkerStyle.None,
            "square" => ChartMarkerStyle.Square,
            "diamond" => ChartMarkerStyle.Diamond,
            "triangle" => ChartMarkerStyle.Triangle,
            // R65-default-fallback-swallow-sweep-2: the remaining ST_MarkerStyle values fell through
            // to Circle, silently losing the shape.
            "x" => ChartMarkerStyle.X,
            "star" => ChartMarkerStyle.Star,
            "plus" => ChartMarkerStyle.Plus,
            "dot" => ChartMarkerStyle.Dot,
            "dash" => ChartMarkerStyle.Dash,
            "auto" => ChartMarkerStyle.Auto,
            _ => ChartMarkerStyle.Circle
        };
}
