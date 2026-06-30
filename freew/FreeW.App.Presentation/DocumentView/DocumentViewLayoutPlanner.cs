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

public enum DocumentFloatingHandle
{
    None,
    Body,
    TopLeft,
    Top,
    TopRight,
    Left,
    Right,
    BottomLeft,
    Bottom,
    BottomRight,
}

public sealed record DocumentFloatPoint(double XDip, double YDip);

public sealed record DocumentFloatRect(double XDip, double YDip, double WidthDip, double HeightDip)
{
    public double LeftDip => XDip;
    public double TopDip => YDip;
    public double RightDip => XDip + WidthDip;
    public double BottomDip => YDip + HeightDip;
    public double CenterXDip => XDip + WidthDip / 2;
    public double CenterYDip => YDip + HeightDip / 2;

    public bool Contains(DocumentFloatPoint point) =>
        point.XDip >= LeftDip
        && point.XDip <= RightDip
        && point.YDip >= TopDip
        && point.YDip <= BottomDip;

    public DocumentFloatRect Inflate(double paddingDip) =>
        new(
            XDip - paddingDip,
            YDip - paddingDip,
            WidthDip + 2 * paddingDip,
            HeightDip + 2 * paddingDip);
}

public sealed record DocumentFloatingHandleRect(
    DocumentFloatingHandle Handle,
    DocumentFloatRect Rect);

public sealed record DocumentFloatingWrapExclusionZone(
    DocumentFloatRect Rect,
    ImageWrapping Wrapping);

public sealed record DocumentFloatingLineExclusionPlan(
    double LeftDeltaDip,
    double RightShrinkDip);

public static class DocumentViewLayoutPlanner
{
    private const double DefaultWrapGapDip = 9.0;
    private const double DefaultMinimumLineWidthDip = 20.0;

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

    public static DocumentViewSurfacePlan BuildFloatingOverlaySurfacePlan(
        PageSettings page,
        bool printLayout,
        double plainInsetDip)
    {
        ArgumentNullException.ThrowIfNull(page);

        var metrics = BuildPageMetrics(page);
        if (printLayout)
        {
            return new DocumentViewSurfacePlan(
                DocumentViewLayoutKind.PrintLayout,
                metrics.PageWidthDip,
                metrics.PageHeightDip,
                metrics.MarginLeftDip,
                metrics.MarginTopDip,
                metrics.MarginRightDip,
                metrics.MarginBottomDip,
                PageLeftDip: 0,
                ContentLeftDip: metrics.MarginLeftDip,
                metrics.ContentWidthDip,
                metrics.ContentHeightDip,
                DeskPaddingDip: 0,
                PageGapDip: 0);
        }

        var inset = Math.Max(0, plainInsetDip);
        return new DocumentViewSurfacePlan(
            DocumentViewLayoutKind.WebLayout,
            metrics.PageWidthDip,
            double.MaxValue / 2,
            MarginLeftDip: inset,
            MarginTopDip: inset,
            MarginRightDip: inset,
            MarginBottomDip: inset,
            PageLeftDip: 0,
            ContentLeftDip: inset,
            metrics.ContentWidthDip,
            double.MaxValue / 2,
            DeskPaddingDip: 0,
            PageGapDip: 0);
    }

    public static DocumentFloatingWrapExclusionZone? BuildWrapExclusionZone(
        DocumentFloatRect pageSpaceRect,
        ImageWrapping wrapping)
    {
        return wrapping is ImageWrapping.Square or ImageWrapping.Tight or ImageWrapping.TopAndBottom
            ? new DocumentFloatingWrapExclusionZone(pageSpaceRect, wrapping)
            : null;
    }

    public static DocumentFloatingLineExclusionPlan BuildSquareTightWrapExclusion(
        IEnumerable<DocumentFloatingWrapExclusionZone> zones,
        double lineTopDip,
        double lineHeightDip,
        double columnLeftDip,
        double columnWidthDip,
        double wrapGapDip = DefaultWrapGapDip,
        double minimumLineWidthDip = DefaultMinimumLineWidthDip)
    {
        ArgumentNullException.ThrowIfNull(zones);

        var lineBottomDip = lineTopDip + lineHeightDip;
        var columnRightDip = columnLeftDip + columnWidthDip;
        var maxLeftDeltaDip = 0.0;
        var maxRightShrinkDip = 0.0;

        foreach (var zone in zones)
        {
            if (zone.Wrapping == ImageWrapping.TopAndBottom)
                continue;

            var rect = zone.Rect;
            if (rect.BottomDip <= lineTopDip || rect.TopDip >= lineBottomDip)
                continue;

            if (rect.RightDip <= columnLeftDip || rect.LeftDip >= columnRightDip)
                continue;

            var freeLeftDip = rect.LeftDip - columnLeftDip;
            var freeRightDip = columnRightDip - rect.RightDip;
            if (freeLeftDip < minimumLineWidthDip && freeRightDip < minimumLineWidthDip)
                continue;

            if (freeLeftDip >= freeRightDip)
            {
                var shrinkToDip = columnRightDip - Math.Max(
                    rect.LeftDip - wrapGapDip,
                    columnLeftDip + minimumLineWidthDip);
                maxRightShrinkDip = Math.Max(maxRightShrinkDip, shrinkToDip);
            }
            else
            {
                var pushToDip = Math.Min(
                    rect.RightDip + wrapGapDip,
                    columnRightDip - minimumLineWidthDip) - columnLeftDip;
                maxLeftDeltaDip = Math.Max(maxLeftDeltaDip, pushToDip);
            }
        }

        var totalShrinkDip = maxLeftDeltaDip + maxRightShrinkDip;
        var maxShrinkDip = Math.Max(0, columnWidthDip - minimumLineWidthDip);
        if (totalShrinkDip > maxShrinkDip && totalShrinkDip > 0)
        {
            var scale = maxShrinkDip / totalShrinkDip;
            maxLeftDeltaDip *= scale;
            maxRightShrinkDip *= scale;
        }

        return new DocumentFloatingLineExclusionPlan(maxLeftDeltaDip, maxRightShrinkDip);
    }

    public static double? BuildTopAndBottomWrapExclusionBottom(
        IEnumerable<DocumentFloatingWrapExclusionZone> zones,
        double lineTopDip,
        double lineHeightDip,
        double contentLeftDip,
        int columnCount,
        double columnWidthDip,
        double columnGapDip,
        double minimumSideWidthDip = DefaultMinimumLineWidthDip)
    {
        ArgumentNullException.ThrowIfNull(zones);

        var lineBottomDip = lineTopDip + lineHeightDip;
        var safeColumnCount = Math.Max(1, columnCount);
        var safeColumnWidthDip = Math.Max(0, columnWidthDip);
        var safeColumnGapDip = Math.Max(0, columnGapDip);
        var maxBottomDip = (double?)null;

        foreach (var zone in zones)
        {
            var rect = zone.Rect;
            if (rect.BottomDip <= lineTopDip || rect.TopDip >= lineBottomDip)
                continue;

            if (zone.Wrapping == ImageWrapping.TopAndBottom)
            {
                maxBottomDip = Math.Max(maxBottomDip ?? double.MinValue, rect.BottomDip);
                continue;
            }

            var columnIndex = 0;
            var columnStrideDip = safeColumnWidthDip + safeColumnGapDip;
            if (safeColumnCount > 1 && columnStrideDip > 0)
            {
                columnIndex = Math.Clamp(
                    (int)Math.Round((rect.LeftDip - contentLeftDip) / columnStrideDip),
                    0,
                    safeColumnCount - 1);
            }

            var columnLeftDip = contentLeftDip + columnIndex * columnStrideDip;
            var freeLeftDip = rect.LeftDip - columnLeftDip;
            var freeRightDip = columnLeftDip + safeColumnWidthDip - rect.RightDip;
            if (freeLeftDip < minimumSideWidthDip && freeRightDip < minimumSideWidthDip)
                maxBottomDip = Math.Max(maxBottomDip ?? double.MinValue, rect.BottomDip);
        }

        return maxBottomDip;
    }

    public static double BuildContentYAfterTopAndBottomWrapExclusion(
        DocumentViewSurfacePlan surface,
        double currentContentYDip,
        double peekContentYDip,
        double exclusionBottomDip,
        int columnCount)
    {
        ArgumentNullException.ThrowIfNull(surface);

        var safeColumnCount = Math.Max(1, columnCount);
        var safeTextAreaHeightDip = Math.Max(1, surface.TextAreaHeightDip);
        var slot = (int)(peekContentYDip / safeTextAreaHeightDip);
        var pageIndex = safeColumnCount > 1 ? slot / safeColumnCount : slot;
        var pageTopDip = surface.IsPrintLayout ? surface.PageTopDip(pageIndex) : 0;
        var offsetInPageDip = exclusionBottomDip - pageTopDip - surface.MarginTopDip;
        var clampedOffsetDip = Math.Clamp(offsetInPageDip, 0, safeTextAreaHeightDip);
        var lastSlotOnPage = (pageIndex + 1) * safeColumnCount - 1;
        var targetContentYDip = lastSlotOnPage * safeTextAreaHeightDip + clampedOffsetDip;
        return Math.Max(currentContentYDip, targetContentYDip);
    }

    public static IReadOnlyList<DocumentFloatingHandleRect> BuildFloatingHandleRects(
        DocumentFloatRect rect,
        double handleSizeDip)
    {
        var sizeDip = Math.Max(0, handleSizeDip);
        var halfDip = sizeDip / 2;
        var x = new[] { rect.LeftDip, rect.CenterXDip, rect.RightDip };
        var y = new[] { rect.TopDip, rect.CenterYDip, rect.BottomDip };
        var map = new[,]
        {
            { DocumentFloatingHandle.TopLeft, DocumentFloatingHandle.Top, DocumentFloatingHandle.TopRight },
            { DocumentFloatingHandle.Left, DocumentFloatingHandle.None, DocumentFloatingHandle.Right },
            { DocumentFloatingHandle.BottomLeft, DocumentFloatingHandle.Bottom, DocumentFloatingHandle.BottomRight },
        };

        var handles = new List<DocumentFloatingHandleRect>(capacity: 8);
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 3; col++)
            {
                var handle = map[row, col];
                if (handle == DocumentFloatingHandle.None)
                    continue;

                handles.Add(new DocumentFloatingHandleRect(
                    handle,
                    new DocumentFloatRect(
                        x[col] - halfDip,
                        y[row] - halfDip,
                        sizeDip,
                        sizeDip)));
            }
        }

        return handles;
    }

    public static DocumentFloatingHandle HitTestFloatingHandle(
        DocumentFloatRect selectionRect,
        DocumentFloatPoint point,
        double handleSizeDip,
        double hitPaddingDip)
    {
        foreach (var handleRect in BuildFloatingHandleRects(selectionRect, handleSizeDip))
        {
            if (handleRect.Rect.Inflate(Math.Max(0, hitPaddingDip)).Contains(point))
                return handleRect.Handle;
        }

        return selectionRect.Contains(point)
            ? DocumentFloatingHandle.Body
            : DocumentFloatingHandle.None;
    }

    public static DocumentFloatRect BuildFloatingMoveRect(
        DocumentFloatRect baseRect,
        DocumentFloatPoint pointerDown,
        DocumentFloatPoint pointer)
    {
        var dxDip = pointer.XDip - pointerDown.XDip;
        var dyDip = pointer.YDip - pointerDown.YDip;
        return new DocumentFloatRect(
            baseRect.XDip + dxDip,
            baseRect.YDip + dyDip,
            baseRect.WidthDip,
            baseRect.HeightDip);
    }

    public static DocumentFloatRect BuildFloatingResizeRect(
        DocumentFloatRect baseRect,
        DocumentFloatingHandle handle,
        DocumentFloatPoint pointer,
        bool preserveAspect,
        double minimumSizeDip)
    {
        var minimumDip = Math.Max(0, minimumSizeDip);
        var leftDip = baseRect.LeftDip;
        var topDip = baseRect.TopDip;
        var rightDip = baseRect.RightDip;
        var bottomDip = baseRect.BottomDip;

        var movesLeft = handle is DocumentFloatingHandle.TopLeft
            or DocumentFloatingHandle.Left
            or DocumentFloatingHandle.BottomLeft;
        var movesRight = handle is DocumentFloatingHandle.TopRight
            or DocumentFloatingHandle.Right
            or DocumentFloatingHandle.BottomRight;
        var movesTop = handle is DocumentFloatingHandle.TopLeft
            or DocumentFloatingHandle.Top
            or DocumentFloatingHandle.TopRight;
        var movesBottom = handle is DocumentFloatingHandle.BottomLeft
            or DocumentFloatingHandle.Bottom
            or DocumentFloatingHandle.BottomRight;

        if (movesLeft)
            leftDip = Math.Min(pointer.XDip, rightDip - minimumDip);
        if (movesRight)
            rightDip = Math.Max(pointer.XDip, leftDip + minimumDip);
        if (movesTop)
            topDip = Math.Min(pointer.YDip, bottomDip - minimumDip);
        if (movesBottom)
            bottomDip = Math.Max(pointer.YDip, topDip + minimumDip);

        var widthDip = rightDip - leftDip;
        var heightDip = bottomDip - topDip;
        var isCorner = handle is DocumentFloatingHandle.TopLeft
            or DocumentFloatingHandle.TopRight
            or DocumentFloatingHandle.BottomLeft
            or DocumentFloatingHandle.BottomRight;

        if (preserveAspect && isCorner && baseRect.WidthDip > 0 && baseRect.HeightDip > 0)
        {
            var ratio = baseRect.WidthDip / baseRect.HeightDip;
            if (widthDip / baseRect.WidthDip >= heightDip / baseRect.HeightDip)
                heightDip = widthDip / ratio;
            else
                widthDip = heightDip * ratio;

            widthDip = Math.Max(minimumDip, widthDip);
            heightDip = Math.Max(minimumDip, heightDip);
            if (movesLeft)
                leftDip = rightDip - widthDip;
            else
                rightDip = leftDip + widthDip;

            if (movesTop)
                topDip = bottomDip - heightDip;
            else
                bottomDip = topDip + heightDip;
        }

        return new DocumentFloatRect(
            leftDip,
            topDip,
            Math.Max(minimumDip, rightDip - leftDip),
            Math.Max(minimumDip, bottomDip - topDip));
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
