using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Toolkit-neutral ruler hit-testing and edit planning shared by the WPF and Avalonia document views.
/// Renderers provide pointer coordinates and commit the returned model values through their command bus.
/// </summary>
public static class DocumentRulerInteractionPlanner
{
    public const double HitRadiusDip = 7;
    public const double TabGridPt = 6;

    public static DocumentRulerHorizontalMetrics? TryBuildCenteredHorizontalMetrics(
        double rulerWidthDip,
        PageSettings page,
        double zoom)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (rulerWidthDip <= 0 || zoom <= 0)
            return null;

        var pageWidth = PageLayout.PointsToDip(page.WidthPt) * zoom;
        var pageLeft = Math.Max(0, (rulerWidthDip - pageWidth) / 2);
        return TryBuildHorizontalMetrics(
            pageLeft + PageLayout.PointsToDip(page.MarginLeftPt) * zoom,
            pageLeft + pageWidth - PageLayout.PointsToDip(page.MarginRightPt) * zoom,
            zoom);
    }

    public static DocumentRulerHorizontalMetrics? TryBuildHorizontalMetrics(
        double contentStartDip,
        double contentEndDip,
        double zoom) =>
        zoom <= 0 || contentEndDip <= contentStartDip
            ? null
            : new DocumentRulerHorizontalMetrics(contentStartDip, contentEndDip, zoom);

    public static DocumentRulerVerticalMetrics? TryBuildVerticalMetrics(
        PageSettings page,
        double zoom,
        double pageTopDip = 0)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (zoom <= 0)
            return null;

        var pageHeightDip = PageLayout.PointsToDip(page.HeightPt) * zoom;
        return new DocumentRulerVerticalMetrics(
            pageTopDip + PageLayout.PointsToDip(page.MarginTopPt) * zoom,
            pageTopDip + pageHeightDip - PageLayout.PointsToDip(page.MarginBottomPt) * zoom,
            page.HeightPt,
            zoom,
            pageTopDip);
    }

    public static DocumentRulerDragKind HitTestHorizontal(
        DocumentRulerPoint point,
        double rulerThicknessDip,
        DocumentRulerHorizontalMetrics metrics,
        ParagraphFormatting formatting,
        out int tabIndex)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(formatting);
        tabIndex = -1;
        if (point.X < metrics.ContentStart || point.X > metrics.ContentEnd)
            return DocumentRulerDragKind.None;

        var leftX = metrics.ContentPointToX(formatting.IndentLeftPt);
        var firstX = metrics.ContentPointToX(formatting.IndentLeftPt + formatting.FirstLineIndentPt);
        var rightX = metrics.ContentEnd - PageLayout.PointsToDip(formatting.IndentRightPt) * metrics.Zoom;

        if (Math.Abs(point.X - firstX) <= HitRadiusDip && point.Y <= rulerThicknessDip * 0.55)
            return DocumentRulerDragKind.FirstLineIndent;
        if (Math.Abs(point.X - leftX) <= HitRadiusDip && point.Y >= rulerThicknessDip * 0.45)
            return DocumentRulerDragKind.LeftIndent;
        if (Math.Abs(point.X - rightX) <= HitRadiusDip && point.Y >= rulerThicknessDip * 0.45)
            return DocumentRulerDragKind.RightIndent;

        for (var i = 0; i < formatting.TabStops.Count; i++)
        {
            var x = metrics.ContentPointToX(formatting.TabStops[i].PositionPt);
            if (Math.Abs(point.X - x) > HitRadiusDip)
                continue;
            tabIndex = i;
            return DocumentRulerDragKind.TabStop;
        }

        return DocumentRulerDragKind.NewTabStop;
    }

    public static DocumentRulerDragKind HitTestVertical(double y, DocumentRulerVerticalMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        if (Math.Abs(y - metrics.TopBoundaryY) <= HitRadiusDip)
            return DocumentRulerDragKind.TopMargin;
        if (Math.Abs(y - metrics.BottomBoundaryY) <= HitRadiusDip)
            return DocumentRulerDragKind.BottomMargin;
        return DocumentRulerDragKind.None;
    }

    public static ParagraphFormatting BuildIndentFormatting(
        ParagraphFormatting start,
        DocumentRulerDragKind kind,
        double pointPt) => kind switch
    {
        DocumentRulerDragKind.LeftIndent =>
            Indentation.SetIndents(start, SnapPoint(pointPt), start.IndentRightPt, start.FirstLineIndentPt),
        DocumentRulerDragKind.FirstLineIndent =>
            Indentation.SetIndents(
                start,
                start.IndentLeftPt,
                start.IndentRightPt,
                Math.Max(-start.IndentLeftPt, SnapSignedPoint(pointPt - start.IndentLeftPt))),
        DocumentRulerDragKind.RightIndent =>
            Indentation.SetIndents(start, start.IndentLeftPt, SnapPoint(pointPt), start.FirstLineIndentPt),
        _ => start
    };

    public static IReadOnlyList<TabStop> MoveOrAddTabStop(
        IReadOnlyList<TabStop> stops,
        int index,
        double positionPt,
        TabStopAlignment alignment)
    {
        ArgumentNullException.ThrowIfNull(stops);
        var snapped = SnapPoint(positionPt);
        var result = stops.ToList();
        var replacement = new TabStop(snapped, alignment);
        if (index >= 0 && index < result.Count)
        {
            replacement = result[index] with { PositionPt = snapped };
            result[index] = replacement;
        }
        else
        {
            result.Add(replacement);
        }

        return result
            .Where(stop => stop.PositionPt >= 0)
            .OrderBy(stop => stop.PositionPt)
            .ThenBy(stop => stop.Alignment)
            .ThenBy(stop => stop.Leader)
            .ToArray();
    }

    public static IReadOnlyList<TabStop> RemoveTabStop(IReadOnlyList<TabStop> stops, int index)
    {
        ArgumentNullException.ThrowIfNull(stops);
        if (index < 0 || index >= stops.Count)
            return stops.ToArray();

        var result = stops.ToList();
        result.RemoveAt(index);
        return result.ToArray();
    }

    public static bool IsTabStopRemovalDrop(double y, double rulerHeightDip) =>
        y < -HitRadiusDip || y > rulerHeightDip + HitRadiusDip;

    public static double ResolveVerticalMargin(
        DocumentRulerDragKind kind,
        double startMarginPt,
        double pointerDeltaDip,
        double otherMarginPt,
        DocumentRulerVerticalMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        var deltaPt = metrics.DipDeltaToPointsDelta(pointerDeltaDip);
        var candidate = kind switch
        {
            DocumentRulerDragKind.TopMargin => startMarginPt + deltaPt,
            DocumentRulerDragKind.BottomMargin => startMarginPt - deltaPt,
            _ => startMarginPt
        };
        return ClampVerticalMargin(candidate, otherMarginPt, metrics.PageHeightPt);
    }

    public static double ClampVerticalMargin(double newMarginPt, double otherMarginPt, double pageHeightPt)
    {
        var clamped = Math.Max(0, newMarginPt);
        var maxAllowed = Math.Max(0, pageHeightPt - otherMarginPt - 1);
        return Math.Min(clamped, maxAllowed);
    }

    public static double SnapPoint(double pointPt) =>
        Math.Max(0, Math.Round(pointPt / TabGridPt, MidpointRounding.AwayFromZero) * TabGridPt);

    private static double SnapSignedPoint(double pointPt) =>
        Math.Round(pointPt / TabGridPt, MidpointRounding.AwayFromZero) * TabGridPt;
}

public enum DocumentRulerDragKind
{
    None,
    LeftIndent,
    FirstLineIndent,
    RightIndent,
    TabStop,
    NewTabStop,
    TopMargin,
    BottomMargin
}

public readonly record struct DocumentRulerPoint(double X, double Y);

public sealed record DocumentRulerHorizontalMetrics(double ContentStart, double ContentEnd, double Zoom)
{
    public double XToContentPoint(double x) =>
        Math.Clamp(
            (x - ContentStart) / (PageLayout.DipPerPoint * Zoom),
            0,
            Math.Max(0, (ContentEnd - ContentStart) / (PageLayout.DipPerPoint * Zoom)));

    public double ContentPointToX(double pointPt) =>
        ContentStart + PageLayout.PointsToDip(pointPt) * Zoom;
}

public sealed record DocumentRulerVerticalMetrics(
    double TopBoundaryY,
    double BottomBoundaryY,
    double PageHeightPt,
    double Zoom,
    double PageTopDip)
{
    public double DipDeltaToPointsDelta(double dipDelta) =>
        dipDelta / (PageLayout.DipPerPoint * Zoom);
}
