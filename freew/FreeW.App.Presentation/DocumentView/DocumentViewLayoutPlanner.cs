using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

public enum DocumentViewLayoutKind
{
    PrintLayout,
    WebLayout,
    Draft
}

public sealed record DocumentViewLayoutOptions(
    double MinPrintPageWidthDip,
    double MinPrintPageHeightDip,
    double MinContentWidthDip,
    double MinPrintTextAreaHeightDip,
    double MinHorizontalGutterDip,
    double DeskPaddingDip,
    double PageGapDip,
    double WebMaxContentWidthDip,
    double WebInsetDip,
    double DraftInsetDip)
{
    public static DocumentViewLayoutOptions AvaloniaDefault { get; } = new(
        MinPrintPageWidthDip: 320,
        MinPrintPageHeightDip: 400,
        MinContentWidthDip: 120,
        MinPrintTextAreaHeightDip: 40,
        MinHorizontalGutterDip: 24,
        DeskPaddingDip: 24,
        PageGapDip: 20,
        WebMaxContentWidthDip: 1000,
        WebInsetDip: 24,
        DraftInsetDip: 16);
}

public sealed record DocumentPageMetricsPlan(
    double PageWidthDip,
    double PageHeightDip,
    double MarginLeftDip,
    double MarginTopDip,
    double MarginRightDip,
    double MarginBottomDip,
    double ContentWidthDip,
    double ContentHeightDip);

public sealed record DocumentColumnLayoutPlan(
    int Count,
    double WidthDip,
    double GapDip,
    bool LineBetween)
{
    public double LeftDip(double contentLeftDip, int columnIndex) =>
        contentLeftDip + Math.Clamp(columnIndex, 0, Math.Max(0, Count - 1)) * (WidthDip + GapDip);
}

public sealed record DocumentGridlineSegment(double X1, double Y1, double X2, double Y2);

public sealed record DocumentViewSurfacePlan(
    DocumentViewLayoutKind Kind,
    double PageWidthDip,
    double PageHeightDip,
    double MarginLeftDip,
    double MarginTopDip,
    double MarginRightDip,
    double MarginBottomDip,
    double PageLeftDip,
    double ContentLeftDip,
    double ContentWidthDip,
    double TextAreaHeightDip,
    double DeskPaddingDip,
    double PageGapDip)
{
    public bool IsPrintLayout => Kind == DocumentViewLayoutKind.PrintLayout;

    public double PageStrideDip => PageHeightDip + PageGapDip;

    public double PageTopDip(int pageIndex) =>
        IsPrintLayout ? DeskPaddingDip + Math.Max(0, pageIndex) * PageStrideDip : 0;

    public double ScrollableHeightForPages(int pageCount, double trailingExtentDip = 0) =>
        IsPrintLayout
            ? Math.Max(1, pageCount) * PageStrideDip + DeskPaddingDip + MarginBottomDip + Math.Max(0, trailingExtentDip)
            : trailingExtentDip;

    public double ContentYToPageSpaceY(double contentY, int columnCount)
    {
        if (!IsPrintLayout)
            return MarginTopDip + contentY;

        if (TextAreaHeightDip <= 0)
            return MarginTopDip + contentY;

        var safeColumnCount = Math.Max(1, columnCount);
        var slot = (int)(contentY / TextAreaHeightDip);
        var pageIndex = slot / safeColumnCount;
        var offsetWithinPage = contentY - slot * TextAreaHeightDip;
        return PageTopDip(pageIndex) + MarginTopDip + offsetWithinPage;
    }

    public int PageIndexFromPageSpaceY(double pageSpaceY)
    {
        if (!IsPrintLayout)
            return 0;

        var rel = pageSpaceY - DeskPaddingDip;
        if (rel < 0)
            return 0;

        return Math.Max(0, (int)(rel / PageStrideDip));
    }
}

public sealed record DocumentFloatingObjectPlacementPlan(
    double XDip,
    double YDip,
    int AnchorPageIndex);

public static class DocumentViewLayoutPlanner
{
    public static DocumentPageMetricsPlan BuildPageMetrics(PageSettings page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var (pageWidthDip, pageHeightDip) = PageLayout.PageSizeDip(page);
        var (marginLeftDip, marginTopDip, marginRightDip, marginBottomDip) = PageLayout.MarginsDip(page);
        var (contentWidthDip, contentHeightDip) = PageLayout.ContentAreaDip(page);
        return new DocumentPageMetricsPlan(
            pageWidthDip,
            pageHeightDip,
            marginLeftDip,
            marginTopDip,
            marginRightDip,
            marginBottomDip,
            contentWidthDip,
            contentHeightDip);
    }

    public static DocumentViewSurfacePlan BuildSurfacePlan(
        PageSettings page,
        DocumentViewLayoutKind kind,
        double availableWidthDip,
        DocumentViewLayoutOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(page);

        options ??= DocumentViewLayoutOptions.AvaloniaDefault;
        var width = double.IsFinite(availableWidthDip) && availableWidthDip > 0
            ? availableWidthDip
            : options.MinPrintPageWidthDip;

        return kind switch
        {
            DocumentViewLayoutKind.PrintLayout => BuildPrintSurfacePlan(page, width, options),
            DocumentViewLayoutKind.WebLayout => BuildWebSurfacePlan(width, options),
            DocumentViewLayoutKind.Draft => BuildDraftSurfacePlan(width, options),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    public static DocumentColumnLayoutPlan BuildColumnPlan(
        PageSettings page,
        double contentWidthDip,
        bool usePageColumns)
    {
        ArgumentNullException.ThrowIfNull(page);

        var columns = usePageColumns ? Math.Max(1, page.ColumnCount) : 1;
        if (columns <= 1)
            return new DocumentColumnLayoutPlan(1, contentWidthDip, 0, false);

        var gapDip = Math.Max(0, PageLayout.PointsToDip(page.ColumnSpacingPt));
        double columnWidthDip;
        if (page.ColumnWidthsPt is { Count: > 1 } widths && widths.Count == columns)
        {
            columnWidthDip = PageLayout.PointsToDip(widths.Min());
        }
        else
        {
            columnWidthDip = (contentWidthDip - (columns - 1) * gapDip) / columns;
        }

        return new DocumentColumnLayoutPlan(
            columns,
            Math.Max(1, columnWidthDip),
            gapDip,
            page.ColumnsLineBetween);
    }

    public static IReadOnlyList<DocumentGridlineSegment> BuildGridlines(
        DocumentViewSurfacePlan surface,
        int pageCount,
        double stepDip)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (!surface.IsPrintLayout || pageCount <= 0 || stepDip <= 0)
            return [];

        var lines = new List<DocumentGridlineSegment>();
        var areaLeft = surface.ContentLeftDip;
        var areaRight = surface.ContentLeftDip + surface.ContentWidthDip;
        for (var pageIndex = 0; pageIndex < pageCount; pageIndex++)
        {
            var pageTop = surface.PageTopDip(pageIndex);
            var areaTop = pageTop + surface.MarginTopDip;
            var areaBottom = pageTop + surface.PageHeightDip - surface.MarginBottomDip;

            for (var y = areaTop; y <= areaBottom + 0.01; y += stepDip)
                lines.Add(new DocumentGridlineSegment(areaLeft, y, areaRight, y));

            for (var x = areaLeft; x <= areaRight + 0.01; x += stepDip)
                lines.Add(new DocumentGridlineSegment(x, areaTop, x, areaBottom));
        }

        return lines;
    }

    public static IReadOnlyList<double> BuildRulerTicks(DocumentViewSurfacePlan surface, double tickStepDip)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (!surface.IsPrintLayout || tickStepDip <= 0)
            return [];

        var ticks = new List<double>();
        for (var x = surface.PageLeftDip; x <= surface.PageLeftDip + surface.PageWidthDip + 0.01; x += tickStepDip)
            ticks.Add(x);
        return ticks;
    }

    public static DocumentFloatingObjectPlacementPlan BuildFloatingObjectPlacement(
        DocumentViewSurfacePlan surface,
        double anchorContentYDip,
        int columnCount,
        FloatingPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);

        return BuildFloatingObjectPlacement(
            surface,
            anchorContentYDip,
            columnCount,
            placement.HorizontalAnchor,
            placement.HorizontalOffsetPt,
            placement.VerticalAnchor,
            placement.VerticalOffsetPt);
    }

    public static DocumentFloatingObjectPlacementPlan BuildFloatingObjectPlacement(
        DocumentViewSurfacePlan surface,
        double anchorContentYDip,
        int columnCount,
        HorizontalAnchor horizontalAnchor,
        double horizontalOffsetPt,
        VerticalAnchor verticalAnchor,
        double verticalOffsetPt)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var horizontalOffsetDip = PageLayout.PointsToDip(horizontalOffsetPt);
        var verticalOffsetDip = PageLayout.PointsToDip(verticalOffsetPt);
        var anchorPageIndex = surface.IsPrintLayout && surface.TextAreaHeightDip > 0
            ? Math.Max(0, (int)(anchorContentYDip / surface.TextAreaHeightDip))
            : 0;
        var anchorPageTopDip = surface.IsPrintLayout ? surface.PageTopDip(anchorPageIndex) : 0;
        var paragraphYDip = surface.ContentYToPageSpaceY(anchorContentYDip, columnCount);

        var xDip = horizontalAnchor switch
        {
            HorizontalAnchor.Page => surface.PageLeftDip + horizontalOffsetDip,
            HorizontalAnchor.Margin => surface.ContentLeftDip + horizontalOffsetDip,
            _ => surface.ContentLeftDip + horizontalOffsetDip,
        };

        var yDip = verticalAnchor switch
        {
            VerticalAnchor.Paragraph => paragraphYDip + verticalOffsetDip,
            VerticalAnchor.Margin => anchorPageTopDip + surface.MarginTopDip + verticalOffsetDip,
            VerticalAnchor.Page => anchorPageTopDip + verticalOffsetDip,
            _ => paragraphYDip + verticalOffsetDip,
        };

        return new DocumentFloatingObjectPlacementPlan(xDip, yDip, anchorPageIndex);
    }

    private static DocumentViewSurfacePlan BuildPrintSurfacePlan(
        PageSettings page,
        double availableWidthDip,
        DocumentViewLayoutOptions options)
    {
        var pageWidthDip = Math.Max(options.MinPrintPageWidthDip, PageLayout.PointsToDip(page.WidthPt));
        var pageHeightDip = Math.Max(options.MinPrintPageHeightDip, PageLayout.PointsToDip(page.HeightPt));
        var marginLeftDip = Math.Max(0, PageLayout.PointsToDip(page.MarginLeftPt));
        var marginTopDip = Math.Max(0, PageLayout.PointsToDip(page.MarginTopPt));
        var marginRightDip = Math.Max(0, PageLayout.PointsToDip(page.MarginRightPt));
        var marginBottomDip = Math.Max(0, PageLayout.PointsToDip(page.MarginBottomPt));
        var pageLeftDip = Math.Max(options.MinHorizontalGutterDip, (availableWidthDip - pageWidthDip) / 2);
        var contentWidthDip = Math.Max(options.MinContentWidthDip, pageWidthDip - marginLeftDip - marginRightDip);
        var textAreaHeightDip = Math.Max(options.MinPrintTextAreaHeightDip, pageHeightDip - marginTopDip - marginBottomDip);

        return new DocumentViewSurfacePlan(
            DocumentViewLayoutKind.PrintLayout,
            pageWidthDip,
            pageHeightDip,
            marginLeftDip,
            marginTopDip,
            marginRightDip,
            marginBottomDip,
            pageLeftDip,
            pageLeftDip + marginLeftDip,
            contentWidthDip,
            textAreaHeightDip,
            options.DeskPaddingDip,
            options.PageGapDip);
    }

    private static DocumentViewSurfacePlan BuildWebSurfacePlan(
        double availableWidthDip,
        DocumentViewLayoutOptions options)
    {
        var columnWidthDip = Math.Min(availableWidthDip - 2 * options.WebInsetDip, options.WebMaxContentWidthDip);
        var pageWidthDip = Math.Max(options.MinPrintPageWidthDip, columnWidthDip);
        var contentWidthDip = Math.Max(options.MinContentWidthDip, columnWidthDip);

        return new DocumentViewSurfacePlan(
            DocumentViewLayoutKind.WebLayout,
            pageWidthDip,
            double.MaxValue / 2,
            0,
            options.WebInsetDip,
            0,
            options.WebInsetDip,
            options.WebInsetDip,
            options.WebInsetDip,
            contentWidthDip,
            double.MaxValue / 2,
            options.DeskPaddingDip,
            options.PageGapDip);
    }

    private static DocumentViewSurfacePlan BuildDraftSurfacePlan(
        double availableWidthDip,
        DocumentViewLayoutOptions options)
    {
        return new DocumentViewSurfacePlan(
            DocumentViewLayoutKind.Draft,
            Math.Max(options.MinPrintPageWidthDip, availableWidthDip - options.DraftInsetDip),
            double.MaxValue / 2,
            0,
            options.DraftInsetDip,
            0,
            options.DraftInsetDip,
            options.DraftInsetDip,
            options.DraftInsetDip,
            Math.Max(options.MinContentWidthDip, availableWidthDip - options.DraftInsetDip * 2),
            double.MaxValue / 2,
            options.DeskPaddingDip,
            options.PageGapDip);
    }
}
