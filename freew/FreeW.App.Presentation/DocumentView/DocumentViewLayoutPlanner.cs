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

public sealed record DocumentTableCellEffectiveFillPlan(
    string? ExplicitFillHex,
    string? StyleDerivedFillSource,
    string? StyleDerivedFillHex,
    string? EffectiveFillSource,
    string? EffectiveFillHex,
    bool StyleDerivedBold,
    bool EffectiveBold)
{
    public static DocumentTableCellEffectiveFillPlan Empty { get; } = new(
        ExplicitFillHex: null,
        StyleDerivedFillSource: null,
        StyleDerivedFillHex: null,
        EffectiveFillSource: null,
        EffectiveFillHex: null,
        StyleDerivedBold: false,
        EffectiveBold: false);

    public bool HasExplicitFill => !string.IsNullOrWhiteSpace(ExplicitFillHex);
    public bool HasStyleDerivedFill => !string.IsNullOrWhiteSpace(StyleDerivedFillHex);
    public bool HasEffectiveFill => !string.IsNullOrWhiteSpace(EffectiveFillHex);
}

public sealed record DocumentTableCellLayoutPlan(
    int RowIndex,
    int CellIndex,
    int GridColumnIndex,
    int GridSpan,
    int RowSpan,
    bool IsVerticalMergeContinuation,
    string? ShadingColorHex,
    bool HasCustomBorders,
    string TextDirection,
    string VerticalAlignment,
    double? PreferredWidthDip,
    double? HeightDip)
{
    public DocumentTableCellEffectiveFillPlan EffectiveFill { get; init; } =
        DocumentTableCellEffectiveFillPlan.Empty;
}

public sealed record DocumentTablePaginationRowPlan(
    int RowIndex,
    bool IsHeaderRow,
    bool RepeatsAsHeader,
    bool AllowBreakAcrossPages,
    bool KeepTogether,
    bool IsBandedBodyRow,
    string HeightRule,
    double EstimatedHeightDip,
    int AssignedPageNumber);

public sealed record DocumentTablePaginationRenderRowPlan(
    int SourceRowIndex,
    int PageNumber,
    int VisualRowIndexOnPage,
    bool IsRepeatedHeader,
    bool StartsPlannedPage,
    double PageOffsetYDip,
    double EstimatedHeightDip);

public sealed record DocumentTablePaginationPagePlan(
    int PageNumber,
    IReadOnlyList<int> SourceRowIndexes,
    IReadOnlyList<int> RepeatedHeaderRowIndexes,
    IReadOnlyList<int> KeepTogetherRowIndexes,
    double UsedHeightDip,
    double AvailableHeightDip,
    IReadOnlyList<DocumentTablePaginationRenderRowPlan> RenderRows)
{
    public bool IncludesRepeatedHeader => RepeatedHeaderRowIndexes.Count > 0;
    public int SourceStartRowIndex => SourceRowIndexes.Count == 0 ? -1 : SourceRowIndexes.Min();
    public int SourceEndRowIndex => SourceRowIndexes.Count == 0 ? -1 : SourceRowIndexes.Max();
}

public sealed record DocumentTablePaginationPlan(
    int TableIndex,
    int EstimatedPageCount,
    double AvailableBodyHeightDip,
    double HeaderHeightDip,
    bool RepeatsHeaderRows,
    bool HasKeepTogetherRows,
    bool SplitsRowsAllowed,
    IReadOnlyList<int> HeaderRowIndexes,
    IReadOnlyList<DocumentTablePaginationRowPlan> Rows,
    IReadOnlyList<DocumentTablePaginationPagePlan> Pages);

public sealed record DocumentTableLayoutPlan(
    int TableIndex,
    int RowCount,
    int GridColumnCount,
    bool HasHeaderRow,
    bool RepeatsHeaderRow,
    bool HasBandedRows,
    bool HasBandedColumns,
    bool HasFirstColumn,
    bool HasLastColumn,
    bool HasLastRow,
    bool HasMergedCells,
    bool HasVerticalMerges,
    bool HasCellShading,
    bool HasCustomCellBorders,
    bool HasCellMargins,
    bool HasCellSpacing,
    bool HasVerticalText,
    bool HasVerticalAlignment,
    bool HasPreferredWidths,
    bool HasNamedStyle,
    bool HasFloatingTextWrap,
    string Alignment,
    string AutoFit,
    string? TableStyleId,
    IReadOnlyList<double> ColumnWidthsDip,
    IReadOnlyList<DocumentTableCellLayoutPlan> Cells,
    DocumentTablePaginationPlan Pagination);

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

/// <summary>
/// One composed drawing-group transform. Parent transforms are supplied from the nearest owning
/// group outward so the shared pointer/handle helpers can apply the exact same order as rendering.
/// </summary>
public sealed record DocumentFloatTransform(
    DocumentFloatRect Rect,
    double RotationAngle = 0,
    bool FlipH = false,
    bool FlipV = false);

public sealed record DocumentDropCapLayoutPlan(
    int BlockIndex,
    int RunIndex,
    DropCapPosition Position,
    string LeadingGlyph,
    int LineSpan,
    double FontSizeDip,
    double DistanceFromTextDip,
    DocumentFloatRect CapBox,
    DocumentFloatRect TextReservation,
    double BodyTextLeftInsetDip,
    double BodyTextWidthDip)
{
    public bool IsDropped => Position == DropCapPosition.Dropped;
    public bool IsInMargin => Position == DropCapPosition.InMargin;
}

public sealed record DocumentFloatingHandleRect(
    DocumentFloatingHandle Handle,
    DocumentFloatRect Rect);

public sealed record DocumentFloatingWrapExclusionZone(
    DocumentFloatRect Rect,
    ImageWrapping Wrapping,
    FloatingWrapTextSide WrapTextSide = FloatingWrapTextSide.BothSides);

public sealed record DocumentFloatingWrapReservationPlan(
    DocumentFloatingObjectKind Kind,
    double WidthDip,
    double HeightDip,
    ImageWrapping Wrapping);

public sealed record DocumentFloatingLineExclusionPlan(
    double LeftDeltaDip,
    double RightShrinkDip);

/// <summary>
/// A left-to-right pair of usable text fragments around one square/tight float.
/// The second start is relative to the owning column's left edge.
/// </summary>
public sealed record DocumentFloatingSplitLinePlan(
    double FirstWidthDip,
    double SecondStartDeltaDip,
    double SecondWidthDip)
{
    public double EffectiveTextWidthDip => FirstWidthDip + SecondWidthDip;
}

public sealed record DocumentFloatingTextWrapLinePlan(
    double RequestedContentYDip,
    double PlannedContentYDip,
    double PageSpaceYDip,
    int ColumnIndex,
    double ColumnLeftDip,
    double ColumnWidthDip,
    double BaseTextWidthDip,
    double LeftDeltaDip,
    double RightShrinkDip,
    double EffectiveTextWidthDip,
    double? TopAndBottomExclusionBottomDip,
    DocumentFloatingSplitLinePlan? SplitLine = null)
{
    public bool HasLateralExclusion => LeftDeltaDip > 0 || RightShrinkDip > 0;

    public bool HasTopAndBottomAdvance =>
        TopAndBottomExclusionBottomDip is not null && PlannedContentYDip > RequestedContentYDip;

    public bool HasSplitTextFragments => SplitLine is not null;

    public double TextLeftDip(double leftInsetDip = 0) =>
        ColumnLeftDip + leftInsetDip + LeftDeltaDip;

    public double TextRightDip(double leftInsetDip = 0) =>
        TextLeftDip(leftInsetDip) + EffectiveTextWidthDip;
}

public enum DocumentFloatingObjectKind
{
    Image,
    Shape,
    Chart,
    WordArt,
    SmartArt,
    Group
}

public sealed record DocumentFloatingObjectSnapshot(
    DocumentFloatingObjectKind Kind,
    int BlockIndex,
    int RunIndex,
    DocumentFloatRect Rect,
    bool BehindText,
    int ZOrderIndex,
    ImageWrapping Wrapping,
    double RotationAngle = 0,
    bool FlipH = false,
    bool FlipV = false,
    FloatingWrapTextSide WrapTextSide = FloatingWrapTextSide.BothSides)
{
    public string TypeTag => Kind switch
    {
        DocumentFloatingObjectKind.SmartArt => "SmartArt",
        _ => Kind.ToString()
    };
}

public sealed record DocumentFloatingGroupChildSnapshot(
    DocumentFloatingObjectKind Kind,
    int ChildIndex,
    DocumentFloatRect Rect);

public static class DocumentViewLayoutPlanner
{
    private const double DefaultWrapGapDip = 9.0;
    private const double DefaultMinimumLineWidthDip = 20.0;
    private const double DefaultTableRowHeightDip = 24.0;
    private const double MinimumTableRowHeightDip = 14.0;
    private const double EstimatedTableLineHeightDip = 18.0;
    private const double EstimatedTableVerticalPaddingDip = 8.0;

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
        DocumentViewLayoutOptions? options = null,
        bool collapsePageBoundaries = false)
    {
        ArgumentNullException.ThrowIfNull(page);

        options ??= DocumentViewLayoutOptions.AvaloniaDefault;
        var width = double.IsFinite(availableWidthDip) && availableWidthDip > 0
            ? availableWidthDip
            : options.MinPrintPageWidthDip;

        return kind switch
        {
            DocumentViewLayoutKind.PrintLayout => BuildPrintSurfacePlan(page, width, options, collapsePageBoundaries),
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

    public static DocumentDropCapLayoutPlan? BuildDropCapLayoutPlan(
        Paragraph paragraph,
        int blockIndex,
        double paragraphLeftDip,
        double paragraphTopDip,
        double textWidthDip,
        double defaultLineHeightDip)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        if (paragraph.DropCap is not { } intent)
            return null;

        var runIndex = paragraph.Runs.FindIndex(run => run.Text.Length > 0);
        if (runIndex < 0)
            return null;

        var safeTextWidth = Math.Max(DefaultMinimumLineWidthDip, textWidthDip);
        var lineSpan = Math.Max(1, intent.LineSpan);
        var lineHeight = Math.Max(1, defaultLineHeightDip);
        var fontSizeDip = RoundDip(PageLayout.PointsToDip(Math.Max(1, intent.SizePt)));
        var distanceDip = RoundDip(PageLayout.PointsToDip(Math.Max(0, intent.DistanceFromTextPt)));
        var capHeightDip = RoundDip(Math.Max(fontSizeDip, lineHeight * lineSpan));
        var capWidthDip = RoundDip(Math.Max(lineHeight, fontSizeDip * 0.62));
        var reservationHeightDip = RoundDip(lineHeight * lineSpan);

        var (capX, bodyInset, reservationX, reservationWidth) = intent.Position switch
        {
            DropCapPosition.InMargin => (
                paragraphLeftDip - capWidthDip - distanceDip,
                0.0,
                paragraphLeftDip - capWidthDip - distanceDip,
                capWidthDip + distanceDip),
            _ => (
                paragraphLeftDip,
                capWidthDip + distanceDip,
                paragraphLeftDip,
                capWidthDip + distanceDip)
        };
        var bodyWidth = Math.Max(DefaultMinimumLineWidthDip, safeTextWidth - bodyInset);

        return new DocumentDropCapLayoutPlan(
            blockIndex,
            runIndex,
            intent.Position,
            paragraph.Runs[runIndex].Text[0].ToString(),
            lineSpan,
            fontSizeDip,
            distanceDip,
            new DocumentFloatRect(RoundDip(capX), RoundDip(paragraphTopDip), capWidthDip, capHeightDip),
            new DocumentFloatRect(
                RoundDip(reservationX),
                RoundDip(paragraphTopDip),
                RoundDip(reservationWidth),
                reservationHeightDip),
            RoundDip(bodyInset),
            RoundDip(bodyWidth));
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

    public static IReadOnlyList<DocumentTableLayoutPlan> BuildTableLayoutPlans(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var plans = new List<DocumentTableLayoutPlan>();
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            if (document.Blocks[blockIndex] is Table table)
            {
                var leadingContentHeightDip = EstimateLeadingContentHeightDip(document, blockIndex);
                plans.Add(BuildTableLayoutPlan(
                    table,
                    plans.Count,
                    document.Page,
                    leadingContentHeightDip));
            }
        }

        return plans;
    }

    public static double EstimateLeadingContentHeightDip(TextDocument document, int sourceBlockIndex)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (sourceBlockIndex <= 0)
            return 0;

        var dipPerPoint = PageLayout.PointsToDip(1);
        var defaultFontSizeDip = Math.Max(8, document.DefaultRun.FontSizePt ?? 11) * dipPerPoint;
        var columnCount = Math.Max(1, document.Page.ColumnCount);
        var charsPerColumnLine = columnCount == 1
            ? 92
            : Math.Max(
                16,
                (int)Math.Floor(
                    BuildColumnPlan(
                        document.Page,
                        PageLayout.ContentAreaDip(document.Page).Width,
                        usePageColumns: true).WidthDip
                    / Math.Max(4.5, defaultFontSizeDip * 0.50)));
        var height = 0.0;
        foreach (var block in document.Blocks.Take(sourceBlockIndex))
        {
            if (block is not Paragraph paragraph)
                continue;

            var charsPerLine = paragraph.StyleId?.StartsWith("Heading", StringComparison.OrdinalIgnoreCase) == true
                ? Math.Max(16, (int)Math.Round(charsPerColumnLine * 0.78))
                : charsPerColumnLine;
            var lineCount = Math.Max(1, (int)Math.Ceiling(
                Math.Max(1, paragraph.PlainText.Length) / (double)charsPerLine));
            var lineHeightDip = paragraph.Formatting.LineRule switch
            {
                LineSpacingRule.Exact or LineSpacingRule.AtLeast when paragraph.Formatting.LineHeightPt > 0
                    => paragraph.Formatting.LineHeightPt * dipPerPoint,
                _ => defaultFontSizeDip * Math.Max(1, paragraph.Formatting.LineSpacing)
            };
            height += lineCount * lineHeightDip
                + paragraph.Formatting.SpaceBeforePt * dipPerPoint
                + paragraph.Formatting.SpaceAfterPt * dipPerPoint;

            if (paragraph.StyleId?.Equals("Heading1", StringComparison.OrdinalIgnoreCase) == true)
                height += defaultFontSizeDip * 0.6;
        }

        // The page renderer reserves a bottom band for detached footnote bodies. Keep this shared
        // pagination contract aligned with that first-page reservation.
        if (document.Footnotes.Count > 0)
            height += 80;

        return Math.Max(0, height);
    }

    public static DocumentTableLayoutPlan BuildTableLayoutPlan(
        Table table,
        int tableIndex = 0,
        PageSettings? page = null,
        double firstPageLeadingContentHeightDip = 0)
    {
        ArgumentNullException.ThrowIfNull(table);

        var rowCount = table.Rows.Count;
        var gridColumnCount = Math.Max(
            table.ColumnWidthsPt.Count,
            table.Rows.Count == 0
                ? 0
                : table.Rows.Max(row => row.Cells.Sum(cell => Math.Max(1, cell.GridSpan))));
        var cells = new List<DocumentTableCellLayoutPlan>();
        var hasMergedCells = false;
        var hasVerticalMerges = false;
        var hasCellShading = false;
        var hasCustomCellBorders = false;
        var hasCellMargins = table.DefaultCellMargins is not null;
        var hasVerticalText = false;
        var hasVerticalAlignment = false;
        var hasPreferredWidths = table.PreferredWidthPt is not null || table.ColumnWidthsPt.Count > 0;

        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var gridColumnIndex = 0;
            for (var cellIndex = 0; cellIndex < row.Cells.Count; cellIndex++)
            {
                var cell = row.Cells[cellIndex];
                var gridSpan = Math.Max(1, cell.GridSpan);
                var rowSpan = cell.VerticalMerge == VerticalMergeState.Restart
                    ? CountVerticalMergeSpan(table, rowIndex, gridColumnIndex)
                    : 1;
                var isVerticalContinuation = cell.VerticalMerge == VerticalMergeState.Continue;

                hasMergedCells |= gridSpan > 1 || rowSpan > 1;
                hasVerticalMerges |= cell.VerticalMerge != VerticalMergeState.None;
                hasCellShading |= !string.IsNullOrWhiteSpace(cell.ShadingColorHex);
                hasCustomCellBorders |= cell.Borders is { IsEmpty: false };
                hasCellMargins |= cell.Margins is not null;
                hasVerticalText |= cell.TextDirection != CellTextDirection.Horizontal;
                hasVerticalAlignment |= cell.VerticalAlignment != TableCellVerticalAlignment.Top;
                hasPreferredWidths |= cell.WidthPt is > 0 || row.HeightPt is > 0;

                cells.Add(new DocumentTableCellLayoutPlan(
                    rowIndex,
                    cellIndex,
                    gridColumnIndex,
                    gridSpan,
                    rowSpan,
                    isVerticalContinuation,
                    NormalizeHexColorOrNull(cell.ShadingColorHex),
                    cell.Borders is { IsEmpty: false },
                    cell.TextDirection.ToString(),
                    cell.VerticalAlignment.ToString(),
                    cell.WidthPt is > 0 ? RoundDip(PageLayout.PointsToDip(cell.WidthPt.Value)) : null,
                    row.HeightPt is > 0 ? RoundDip(PageLayout.PointsToDip(row.HeightPt.Value)) : null)
                {
                    EffectiveFill = BuildTableCellEffectiveFillPlan(
                        table,
                        rowIndex,
                        cellIndex,
                        gridColumnIndex,
                        gridSpan,
                        gridColumnCount)
                });

                gridColumnIndex += gridSpan;
            }
        }

        return new DocumentTableLayoutPlan(
            Math.Max(0, tableIndex),
            rowCount,
            gridColumnCount,
            table.Formatting.HeaderRow,
            table.Formatting.RepeatHeaderRow,
            table.Formatting.BandedRows,
            table.Formatting.BandedColumns,
            table.Formatting.FirstColumn,
            table.Formatting.LastColumn,
            table.Formatting.LastRow,
            hasMergedCells,
            hasVerticalMerges,
            hasCellShading,
            hasCustomCellBorders,
            hasCellMargins,
            table.CellSpacingPt is > 0,
            hasVerticalText,
            hasVerticalAlignment,
            hasPreferredWidths,
            !string.IsNullOrWhiteSpace(table.TableStyleId),
            table.TextWrapping,
            table.Alignment.ToString(),
            table.AutoFit.ToString(),
            table.TableStyleId,
            table.ColumnWidthsPt
                .Take(Math.Max(0, gridColumnCount))
                .Select(width => RoundDip(PageLayout.PointsToDip(Math.Max(0, width))))
                .ToList(),
            cells,
            BuildTablePaginationPlan(
                table,
                page ?? new PageSettings(),
                tableIndex,
                firstPageLeadingContentHeightDip));
    }

    public static DocumentTableCellEffectiveFillPlan BuildTableCellEffectiveFillPlan(
        Table table,
        int rowIndex,
        int cellIndex,
        int gridColumnIndex,
        int gridSpan,
        int gridColumnCount)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (rowIndex < 0 || rowIndex >= table.Rows.Count)
            return DocumentTableCellEffectiveFillPlan.Empty;
        if (cellIndex < 0 || cellIndex >= table.Rows[rowIndex].Cells.Count)
            return DocumentTableCellEffectiveFillPlan.Empty;

        var cell = table.Rows[rowIndex].Cells[cellIndex];
        return BuildTableCellEffectiveFillPlan(
            NormalizeHexColorOrNull(cell.ShadingColorHex),
            table.TableStyleId,
            table.Formatting,
            rowIndex,
            table.Rows.Count,
            gridColumnIndex,
            Math.Max(1, gridSpan),
            Math.Max(0, gridColumnCount));
    }

    public static DocumentTableCellEffectiveFillPlan BuildTableCellEffectiveFillPlan(
        DocumentTableLayoutPlan table,
        DocumentTableCellLayoutPlan cell)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(cell);

        return BuildTableCellEffectiveFillPlan(
            NormalizeHexColorOrNull(cell.ShadingColorHex),
            table.TableStyleId,
            BuildTableFormatting(table),
            cell.RowIndex,
            Math.Max(0, table.RowCount),
            cell.GridColumnIndex,
            Math.Max(1, cell.GridSpan),
            Math.Max(0, table.GridColumnCount));
    }

    private static DocumentTableCellEffectiveFillPlan BuildTableCellEffectiveFillPlan(
        string? explicitFillHex,
        string? tableStyleId,
        TableFormatting formatting,
        int rowIndex,
        int rowCount,
        int gridColumnIndex,
        int gridSpan,
        int gridColumnCount)
    {
        var styleFillSource = (string?)null;
        var styleFillHex = (string?)null;
        var styleDerivedBold = false;

        var catalogStyle = tableStyleId is { Length: > 0 }
            ? DocumentTableStyle.FindById(tableStyleId)
            : null;
        if (catalogStyle is not null)
        {
            var isFirstColumn = gridColumnIndex == 0;
            var isLastColumn = gridColumnCount > 0
                && gridColumnIndex + Math.Max(1, gridSpan) >= gridColumnCount;
            var (fillHex, bold) = catalogStyle.ResolveCellStyle(
                rowIndex,
                Math.Max(0, rowCount),
                isFirstColumn,
                isLastColumn,
                formatting);
            styleFillHex = NormalizeHexColorOrNull(fillHex);
            styleDerivedBold = bold;
            if (styleFillHex is not null)
                styleFillSource = ResolveStyleDerivedCellFillSource(
                    formatting,
                    rowIndex,
                    Math.Max(0, rowCount),
                    gridColumnIndex,
                    Math.Max(1, gridSpan),
                    gridColumnCount,
                    isFirstColumn,
                    isLastColumn);
        }

        var legacyFillSource = (string?)null;
        var legacyFillHex = (string?)null;
        var isHeaderRow = formatting.HeaderRow && rowIndex == 0;
        var isBandedRow = formatting.BandedRows
            && !isHeaderRow
            && TableBanding.IsBandedBodyRow(rowIndex, formatting.HeaderRow);
        if (catalogStyle is null)
        {
            if (isHeaderRow)
            {
                legacyFillSource = "legacy-header-row";
                legacyFillHex = LegacyHeaderRowFillHex;
            }
            else if (isBandedRow)
            {
                legacyFillSource = "legacy-banded-row";
                legacyFillHex = LegacyBandedRowFillHex;
            }
        }

        var effectiveFillSource = (string?)null;
        var effectiveFillHex = (string?)null;
        if (explicitFillHex is not null)
        {
            effectiveFillSource = "explicit-cell";
            effectiveFillHex = explicitFillHex;
        }
        else if (styleFillHex is not null)
        {
            effectiveFillSource = styleFillSource;
            effectiveFillHex = styleFillHex;
        }
        else if (legacyFillHex is not null)
        {
            effectiveFillSource = legacyFillSource;
            effectiveFillHex = legacyFillHex;
        }

        return new DocumentTableCellEffectiveFillPlan(
            ExplicitFillHex: explicitFillHex,
            StyleDerivedFillSource: styleFillSource,
            StyleDerivedFillHex: styleFillHex,
            EffectiveFillSource: effectiveFillSource,
            EffectiveFillHex: effectiveFillHex,
            StyleDerivedBold: styleDerivedBold,
            EffectiveBold: styleDerivedBold || (catalogStyle is null && isHeaderRow));
    }

    private static string ResolveStyleDerivedCellFillSource(
        TableFormatting formatting,
        int rowIndex,
        int rowCount,
        int gridColumnIndex,
        int gridSpan,
        int gridColumnCount,
        bool isFirstColumn,
        bool isLastColumn)
    {
        if (formatting.HeaderRow && rowIndex == 0)
            return "style-derived-header";
        if (formatting.LastRow && rowIndex == Math.Max(0, rowCount - 1))
            return "style-derived-last-row";
        if (formatting.FirstColumn && isFirstColumn)
            return "style-derived-first-column";
        if (formatting.LastColumn && isLastColumn)
            return "style-derived-last-column";
        if (formatting.BandedRows && TableBanding.BodyRowIndex(rowIndex, formatting.HeaderRow) >= 0)
            return "style-derived-banded-row";

        return "style-derived-cell";
    }

    private static TableFormatting BuildTableFormatting(DocumentTableLayoutPlan table) =>
        new()
        {
            HeaderRow = table.HasHeaderRow,
            RepeatHeaderRow = table.RepeatsHeaderRow,
            BandedRows = table.HasBandedRows,
            BandedColumns = table.HasBandedColumns,
            FirstColumn = table.HasFirstColumn,
            LastColumn = table.HasLastColumn,
            LastRow = table.HasLastRow
        };

    public const string LegacyHeaderRowFillHex = "#D9E2F3";
    public const string LegacyBandedRowFillHex = "#F2F2F2";

    public static DocumentTablePaginationPlan BuildTablePaginationPlan(
        Table table,
        PageSettings page,
        int tableIndex = 0,
        double firstPageLeadingContentHeightDip = 0)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(page);

        var (_, contentHeightDip) = PageLayout.ContentAreaDip(page);
        var availableBodyHeightDip = RoundDip(Math.Max(MinimumTableRowHeightDip, contentHeightDip));
        var firstPageAvailableBodyHeightDip = RoundDip(Math.Max(
            MinimumTableRowHeightDip,
            availableBodyHeightDip - Math.Max(0, firstPageLeadingContentHeightDip)));
        var headerRowIndexes = table.Formatting.RepeatHeaderRow && table.Rows.Count > 0
            ? new[] { 0 }
            : [];
        var headerRowSet = headerRowIndexes.ToHashSet();
        var estimatedHeights = table.Rows
            .Select(EstimateTableRowHeightDip)
            .ToArray();
        var headerHeightDip = RoundDip(headerRowIndexes.Sum(index => estimatedHeights[index]));

        var pageRows = new List<DocumentTablePaginationPagePlan>();
        var assignedPages = new int[table.Rows.Count];
        var currentRows = new List<int>();
        var currentKeepRows = new List<int>();
        var currentUsed = 0.0;
        var currentPageNumber = 1;
        var repeatedHeaderRows = Array.Empty<int>();
        var pageAvailable = availableBodyHeightDip;

        void StartPage(int pageNumber)
        {
            currentPageNumber = pageNumber;
            currentRows.Clear();
            currentKeepRows.Clear();
            repeatedHeaderRows = pageNumber > 1 && headerRowIndexes.Length > 0
                ? headerRowIndexes
                : [];
            currentUsed = repeatedHeaderRows.Sum(index => estimatedHeights[index]);
            pageAvailable = pageNumber == 1
                ? firstPageAvailableBodyHeightDip
                : availableBodyHeightDip;
        }

        void FinishPage()
        {
            if (currentRows.Count == 0)
                return;

            var renderRows = new List<DocumentTablePaginationRenderRowPlan>();
            var pageOffset = 0.0;
            void AddRenderRow(int rowIndex, bool isRepeatedHeader)
            {
                if (rowIndex < 0 || rowIndex >= estimatedHeights.Length)
                    return;

                renderRows.Add(new DocumentTablePaginationRenderRowPlan(
                    rowIndex,
                    currentPageNumber,
                    renderRows.Count,
                    isRepeatedHeader,
                    currentPageNumber > 1 && renderRows.Count == 0,
                    RoundDip(pageOffset),
                    estimatedHeights[rowIndex]));
                pageOffset += estimatedHeights[rowIndex];
            }

            foreach (var headerRowIndex in repeatedHeaderRows)
                AddRenderRow(headerRowIndex, isRepeatedHeader: true);
            foreach (var sourceRowIndex in currentRows)
                AddRenderRow(sourceRowIndex, isRepeatedHeader: false);

            pageRows.Add(new DocumentTablePaginationPagePlan(
                currentPageNumber,
                currentRows.ToList(),
                repeatedHeaderRows.ToList(),
                currentKeepRows.ToList(),
                RoundDip(currentUsed),
                pageAvailable,
                renderRows));
        }

        StartPage(1);
        for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
        {
            var row = table.Rows[rowIndex];
            var height = estimatedHeights[rowIndex];
            var wouldOverflow = currentRows.Count > 0 && currentUsed + height > pageAvailable;
            if (wouldOverflow)
            {
                FinishPage();
                StartPage(currentPageNumber + 1);
            }

            currentRows.Add(rowIndex);
            assignedPages[rowIndex] = currentPageNumber;
            currentUsed += height;
            if (!row.AllowBreakAcrossPages)
                currentKeepRows.Add(rowIndex);
        }
        FinishPage();

        var rowPlans = table.Rows
            .Select((row, rowIndex) => new DocumentTablePaginationRowPlan(
                rowIndex,
                table.Formatting.HeaderRow && rowIndex == 0,
                headerRowSet.Contains(rowIndex),
                row.AllowBreakAcrossPages,
                !row.AllowBreakAcrossPages,
                table.Formatting.BandedRows && TableBanding.IsBandedBodyRow(rowIndex, table.Formatting.HeaderRow),
                row.HeightRule.ToString(),
                estimatedHeights[rowIndex],
                table.Rows.Count == 0 ? 1 : Math.Max(1, assignedPages[rowIndex])))
            .ToList();

        return new DocumentTablePaginationPlan(
            Math.Max(0, tableIndex),
            Math.Max(1, pageRows.Count),
            availableBodyHeightDip,
            headerHeightDip,
            headerRowIndexes.Length > 0,
            table.Rows.Any(row => !row.AllowBreakAcrossPages),
            table.Rows.Any(row => row.AllowBreakAcrossPages),
            headerRowIndexes,
            rowPlans,
            pageRows);
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

    public static IReadOnlyList<DocumentFloatingObjectSnapshot> BuildFloatingObjectSnapshots(
        TextDocument document,
        DocumentViewSurfacePlan surface,
        int columnCount,
        double anchorContentYDip = 0)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(surface);

        var snapshots = new List<DocumentFloatingObjectSnapshot>();
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            if (document.Blocks[blockIndex] is not Paragraph paragraph)
                continue;

            snapshots.AddRange(BuildFloatingObjectSnapshots(
                paragraph,
                blockIndex,
                anchorContentYDip,
                surface,
                columnCount));
        }

        return snapshots;
    }

    public static IReadOnlyList<DocumentFloatingObjectSnapshot> BuildFloatingObjectSnapshots(
        Paragraph paragraph,
        int blockIndex,
        double anchorContentYDip,
        DocumentViewSurfacePlan surface,
        int columnCount)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(surface);

        var snapshots = new List<DocumentFloatingObjectSnapshot>();
        for (var runIndex = 0; runIndex < paragraph.Runs.Count; runIndex++)
        {
            var run = paragraph.Runs[runIndex];
            if (run.Image is { IsFloating: true } image)
            {
                AddSnapshot(
                    snapshots,
                    DocumentFloatingObjectKind.Image,
                    blockIndex,
                    runIndex,
                    surface,
                    anchorContentYDip,
                    columnCount,
                    image.WidthPt,
                    image.HeightPt,
                    defaultWidthPt: 120,
                    defaultHeightPt: 80,
                    image.Wrapping,
                    image.WrapTextSide,
                    image.ZOrderIndex,
                    image.HorizontalAnchor,
                    image.HorizontalOffsetPt,
                    image.VerticalAnchor,
                    image.VerticalOffsetPt,
                    image.RotationAngle,
                    image.FlipH,
                    image.FlipV);
            }
            else if (run.Shape is { IsFloating: true, Placement: { } shapePlacement } shape)
            {
                AddSnapshot(
                    snapshots,
                    DocumentFloatingObjectKind.Shape,
                    blockIndex,
                    runIndex,
                    surface,
                    anchorContentYDip,
                    columnCount,
                    shape.WidthPt,
                    shape.HeightPt,
                    defaultWidthPt: 120,
                    defaultHeightPt: 80,
                    shapePlacement,
                    shape.RotationAngle,
                    shape.FlipH,
                    shape.FlipV);
            }
            else if (run.Chart is { IsFloating: true, Placement: { } chartPlacement } chart)
            {
                AddSnapshot(
                    snapshots,
                    DocumentFloatingObjectKind.Chart,
                    blockIndex,
                    runIndex,
                    surface,
                    anchorContentYDip,
                    columnCount,
                    chart.WidthPt,
                    chart.HeightPt,
                    defaultWidthPt: 360,
                    defaultHeightPt: 216,
                    chartPlacement);
            }
            else if (run.WordArt is { IsFloating: true, Placement: { } wordArtPlacement } wordArt)
            {
                AddSnapshot(
                    snapshots,
                    DocumentFloatingObjectKind.WordArt,
                    blockIndex,
                    runIndex,
                    surface,
                    anchorContentYDip,
                    columnCount,
                    EstimateWordArtWidthPt(wordArt),
                    EstimateWordArtHeightPt(wordArt),
                    defaultWidthPt: 72,
                    defaultHeightPt: 40,
                    wordArtPlacement,
                    wordArt.RotationAngle,
                    wordArt.FlipH,
                    wordArt.FlipV);
            }
            else if (run.SmartArt is { IsFloating: true, Placement: { } smartArtPlacement } smartArt)
            {
                AddSnapshot(
                    snapshots,
                    DocumentFloatingObjectKind.SmartArt,
                    blockIndex,
                    runIndex,
                    surface,
                    anchorContentYDip,
                    columnCount,
                    smartArt.WidthPt,
                    smartArt.HeightPt,
                    defaultWidthPt: 468,
                    defaultHeightPt: 216,
                    smartArtPlacement);
            }
            else if (run.DrawingGroup is { } group)
            {
                AddSnapshot(
                    snapshots,
                    DocumentFloatingObjectKind.Group,
                    blockIndex,
                    runIndex,
                    surface,
                    anchorContentYDip,
                    columnCount,
                    group.WidthPt,
                    group.HeightPt,
                    defaultWidthPt: 144,
                    defaultHeightPt: 72,
                    group.Placement,
                    group.RotationAngle,
                    group.FlipH,
                    group.FlipV);
            }
        }

        return snapshots;
    }

    public static IReadOnlyList<DocumentFloatingObjectSnapshot> BuildFloatingObjectDrawOrder(
        IEnumerable<DocumentFloatingObjectSnapshot> snapshots,
        bool behindText)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        return snapshots
            .Where(snapshot => snapshot.BehindText == behindText)
            .OrderBy(snapshot => snapshot.ZOrderIndex)
            .ThenBy(snapshot => snapshot.BlockIndex)
            .ThenBy(snapshot => snapshot.RunIndex)
            .ToList();
    }

    public static DocumentFloatingObjectSnapshot? HitTestFloatingObject(
        IEnumerable<DocumentFloatingObjectSnapshot> snapshots,
        DocumentFloatPoint point)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        return snapshots
            .Where(snapshot => snapshot.Rect.Contains(
                UnTransformPoint(point, snapshot.Rect, snapshot.RotationAngle, snapshot.FlipH, snapshot.FlipV)))
            .OrderBy(snapshot => snapshot.BehindText ? 1 : 0)
            .ThenByDescending(snapshot => snapshot.ZOrderIndex)
            .ThenByDescending(snapshot => snapshot.BlockIndex)
            .ThenByDescending(snapshot => snapshot.RunIndex)
            .FirstOrDefault();
    }

    /// <summary>
    /// Maps a SCREEN/page-space <paramref name="point"/> into the LOCAL (un-rotated, un-flipped) frame
    /// of a floating object whose visible bounds are <paramref name="rect"/> rotated by
    /// <paramref name="rotationAngle"/> degrees and flipped per <paramref name="flipH"/>/<paramref name="flipV"/>,
    /// all about the rect's own centre. Mirrors the render transform applied in DrawFloatingShape
    /// (translate to centre, flip, rotate, translate back) — this is that transform's inverse, so the
    /// returned point can be tested against the plain axis-aligned <paramref name="rect"/> with
    /// <see cref="DocumentFloatRect.Contains"/>. Rotation and flip are both self-inverse (flip is its own
    /// inverse; rotating by -angle undoes +angle), so the inverse is applied in reverse order: undo the
    /// rotation first, then undo the flip.
    /// </summary>
    public static DocumentFloatPoint UnTransformPoint(
        DocumentFloatPoint point,
        DocumentFloatRect rect,
        double rotationAngle,
        bool flipH,
        bool flipV)
    {
        if (rotationAngle == 0 && !flipH && !flipV)
            return point;

        var cx = rect.CenterXDip;
        var cy = rect.CenterYDip;
        var x = point.XDip - cx;
        var y = point.YDip - cy;

        if (rotationAngle != 0)
        {
            // Undo the render's +rotationAngle by rotating the point by -rotationAngle.
            var rad = -rotationAngle * Math.PI / 180.0;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);
            (x, y) = (x * cos - y * sin, x * sin + y * cos);
        }

        // Undo the render's flip(s) — flip is applied before rotation when drawing, so it is undone
        // after un-rotating here (reverse order of the forward transform).
        if (flipH) x = -x;
        if (flipV) y = -y;

        return new DocumentFloatPoint(x + cx, y + cy);
    }

    public static IReadOnlyList<DocumentFloatingWrapExclusionZone> BuildFloatingWrapExclusionZones(
        IEnumerable<DocumentFloatingObjectSnapshot> snapshots)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        return snapshots
            .Select(snapshot => BuildWrapExclusionZone(snapshot.Rect, snapshot.Wrapping, snapshot.WrapTextSide))
            .OfType<DocumentFloatingWrapExclusionZone>()
            .ToList();
    }

    public static IReadOnlyList<DocumentFloatingGroupChildSnapshot> BuildFloatingGroupChildSnapshots(
        DrawingGroup group,
        DocumentFloatRect groupRect)
    {
        ArgumentNullException.ThrowIfNull(group);

        var children = new List<DocumentFloatingGroupChildSnapshot>();
        for (var childIndex = 0; childIndex < group.Children.Count; childIndex++)
        {
            if (!TryGetFloatingKind(group.Children[childIndex], out var kind))
                continue;

            var (offsetXPt, offsetYPt) = childIndex < group.ChildOffsets.Count
                ? group.ChildOffsets[childIndex]
                : (0.0, 0.0);
            var widthDip = PageLayout.PointsToDip(group.ChildWidthPt(childIndex));
            var heightDip = PageLayout.PointsToDip(group.ChildHeightPt(childIndex));
            children.Add(new DocumentFloatingGroupChildSnapshot(
                kind,
                childIndex,
                new DocumentFloatRect(
                    groupRect.XDip + PageLayout.PointsToDip(offsetXPt),
                    groupRect.YDip + PageLayout.PointsToDip(offsetYPt),
                    widthDip,
                    heightDip)));
        }

        return children;
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
        ImageWrapping wrapping,
        FloatingWrapTextSide wrapTextSide = FloatingWrapTextSide.BothSides)
    {
        return wrapping is ImageWrapping.Square or ImageWrapping.Tight or ImageWrapping.TopAndBottom
            ? new DocumentFloatingWrapExclusionZone(pageSpaceRect, wrapping, wrapTextSide)
            : null;
    }

    public static DocumentFloatingWrapReservationPlan? BuildFloatingWrapReservation(
        Run run,
        double? topAndBottomReservationWidthDip = null)
    {
        ArgumentNullException.ThrowIfNull(run);

        if (run.Image is { IsFloating: true } image)
            return BuildFloatingWrapReservation(
                DocumentFloatingObjectKind.Image,
                image.WidthPt,
                image.HeightPt,
                defaultWidthPt: 120,
                defaultHeightPt: 80,
                image.Wrapping,
                topAndBottomReservationWidthDip);

        if (run.Shape is { IsFloating: true, Placement: { } shapePlacement } shape)
            return BuildFloatingWrapReservation(
                DocumentFloatingObjectKind.Shape,
                shape.WidthPt,
                shape.HeightPt,
                defaultWidthPt: 120,
                defaultHeightPt: 80,
                shapePlacement.Wrapping,
                topAndBottomReservationWidthDip);

        if (run.Chart is { IsFloating: true, Placement: { } chartPlacement } chart)
            return BuildFloatingWrapReservation(
                DocumentFloatingObjectKind.Chart,
                chart.WidthPt,
                chart.HeightPt,
                defaultWidthPt: 360,
                defaultHeightPt: 216,
                chartPlacement.Wrapping,
                topAndBottomReservationWidthDip);

        if (run.WordArt is { IsFloating: true, Placement: { } wordArtPlacement } wordArt)
            return BuildFloatingWrapReservation(
                DocumentFloatingObjectKind.WordArt,
                EstimateWordArtWidthPt(wordArt),
                EstimateWordArtHeightPt(wordArt),
                defaultWidthPt: 72,
                defaultHeightPt: 40,
                wordArtPlacement.Wrapping,
                topAndBottomReservationWidthDip);

        if (run.SmartArt is { IsFloating: true, Placement: { } smartArtPlacement } smartArt)
            return BuildFloatingWrapReservation(
                DocumentFloatingObjectKind.SmartArt,
                smartArt.WidthPt,
                smartArt.HeightPt,
                defaultWidthPt: 468,
                defaultHeightPt: 216,
                smartArtPlacement.Wrapping,
                topAndBottomReservationWidthDip);

        if (run.DrawingGroup is { } group)
            return BuildFloatingWrapReservation(
                DocumentFloatingObjectKind.Group,
                group.WidthPt,
                group.HeightPt,
                defaultWidthPt: 144,
                defaultHeightPt: 72,
                group.Placement.Wrapping,
                topAndBottomReservationWidthDip);

        return null;
    }

    public static double BuildFloatingWrapReservationTextWidthDip(PageSettings page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var metrics = BuildPageMetrics(page);
        if (page.ColumnCount <= 1)
            return metrics.ContentWidthDip;

        return BuildColumnPlan(page, metrics.ContentWidthDip, usePageColumns: true).WidthDip;
    }

    private static DocumentFloatingWrapReservationPlan? BuildFloatingWrapReservation(
        DocumentFloatingObjectKind kind,
        double widthPt,
        double heightPt,
        double defaultWidthPt,
        double defaultHeightPt,
        ImageWrapping wrapping,
        double? topAndBottomReservationWidthDip)
    {
        if (wrapping is not (ImageWrapping.Square or ImageWrapping.Tight or ImageWrapping.TopAndBottom))
            return null;

        var resolvedWidthPt = widthPt > 0 ? widthPt : defaultWidthPt;
        var resolvedHeightPt = heightPt > 0 ? heightPt : defaultHeightPt;
        var widthDip = wrapping == ImageWrapping.TopAndBottom && topAndBottomReservationWidthDip is > 0
            ? topAndBottomReservationWidthDip.Value
            : PageLayout.PointsToDip(resolvedWidthPt);

        // WPF represents the wrap reservation as a transparent Floater. Raster images using square
        // or tight wrapping need their authored height in that floater so WPF has a vertical band
        // around which paragraph lines can flow; a zero-height placeholder lets the overlay image
        // paint over text. Complex drawing objects retain the established overlay-only behavior until
        // their own flow geometry is baselined. Top-and-bottom always reserves its authored height.
        var reservationHeightDip = wrapping == ImageWrapping.TopAndBottom || kind == DocumentFloatingObjectKind.Image
            ? PageLayout.PointsToDip(resolvedHeightPt)
            : 0;

        return new DocumentFloatingWrapReservationPlan(
            kind,
            Math.Max(1, widthDip),
            reservationHeightDip,
            wrapping);
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

            if (zone.WrapTextSide == FloatingWrapTextSide.Left)
            {
                var shrinkToDip = Math.Max(
                    columnLeftDip + minimumLineWidthDip,
                    rect.LeftDip - wrapGapDip);
                maxRightShrinkDip = Math.Max(maxRightShrinkDip, columnRightDip - shrinkToDip);
            }
            else if (zone.WrapTextSide == FloatingWrapTextSide.Right)
            {
                var pushToDip = Math.Min(
                    columnRightDip - minimumLineWidthDip,
                    rect.RightDip + wrapGapDip);
                maxLeftDeltaDip = Math.Max(maxLeftDeltaDip, pushToDip - columnLeftDip);
            }
            else if (freeLeftDip >= freeRightDip)
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

    public static DocumentFloatingSplitLinePlan? BuildBothSidesSplitLinePlan(
        IEnumerable<DocumentFloatingWrapExclusionZone> zones,
        double lineTopDip,
        double lineHeightDip,
        double columnLeftDip,
        double baseTextWidthDip,
        double wrapGapDip = DefaultWrapGapDip,
        double minimumLineWidthDip = DefaultMinimumLineWidthDip)
    {
        ArgumentNullException.ThrowIfNull(zones);

        var columnRightDip = columnLeftDip + Math.Max(0, baseTextWidthDip);
        var lineBottomDip = lineTopDip + Math.Max(1, lineHeightDip);
        var active = zones
            .Where(zone =>
                zone.Wrapping is ImageWrapping.Square or ImageWrapping.Tight
                && zone.Rect.BottomDip > lineTopDip
                && zone.Rect.TopDip < lineBottomDip
                && zone.Rect.RightDip > columnLeftDip
                && zone.Rect.LeftDip < columnRightDip)
            .ToList();

        // Multiple contemporaneous exclusions need a full interval solver. Keep the established
        // one-fragment behavior until that can be represented without dropping text.
        if (active.Count != 1 || active[0].WrapTextSide != FloatingWrapTextSide.BothSides)
            return null;

        var rect = active[0].Rect;
        var firstRightDip = Math.Clamp(rect.LeftDip - wrapGapDip, columnLeftDip, columnRightDip);
        var secondLeftDip = Math.Clamp(rect.RightDip + wrapGapDip, columnLeftDip, columnRightDip);
        var firstWidthDip = firstRightDip - columnLeftDip;
        var secondWidthDip = columnRightDip - secondLeftDip;
        if (firstWidthDip < minimumLineWidthDip || secondWidthDip < minimumLineWidthDip)
            return null;

        return new DocumentFloatingSplitLinePlan(
            firstWidthDip,
            secondLeftDip - columnLeftDip,
            secondWidthDip);
    }

    public static DocumentFloatingTextWrapLinePlan BuildFloatingTextWrapLinePlan(
        IEnumerable<DocumentFloatingWrapExclusionZone> zones,
        DocumentViewSurfacePlan surface,
        double currentContentYDip,
        double lineContentYDip,
        double lineHeightDip,
        double contentLeftDip,
        int columnCount,
        double columnWidthDip,
        double columnGapDip,
        double baseTextWidthDip,
        double wrapGapDip = DefaultWrapGapDip,
        double minimumLineWidthDip = DefaultMinimumLineWidthDip)
    {
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(surface);

        var zoneList = zones as IReadOnlyList<DocumentFloatingWrapExclusionZone>
            ?? zones.ToList();
        var safeColumnCount = Math.Max(1, columnCount);
        var safeColumnWidthDip = Math.Max(0, columnWidthDip);
        var safeColumnGapDip = Math.Max(0, columnGapDip);
        var safeLineHeightDip = Math.Max(1, lineHeightDip);
        var minimumWidthDip = Math.Max(1, minimumLineWidthDip);
        var safeBaseTextWidthDip = double.IsFinite(baseTextWidthDip)
            ? Math.Max(minimumWidthDip, baseTextWidthDip)
            : minimumWidthDip;
        var requestedContentYDip = double.IsFinite(lineContentYDip)
            ? Math.Max(0, lineContentYDip)
            : 0;
        var plannedContentYDip = requestedContentYDip;
        var mutableCurrentContentYDip = double.IsFinite(currentContentYDip)
            ? Math.Max(0, currentContentYDip)
            : requestedContentYDip;
        double? appliedTopAndBottomBottomDip = null;

        for (var guard = 0; guard < 200; guard++)
        {
            var pageSpaceYDip = surface.ContentYToPageSpaceY(plannedContentYDip, safeColumnCount);
            var exclusionBottomDip = BuildTopAndBottomWrapExclusionBottom(
                zoneList,
                pageSpaceYDip,
                safeLineHeightDip,
                contentLeftDip,
                safeColumnCount,
                safeColumnWidthDip,
                safeColumnGapDip,
                minimumWidthDip);
            if (exclusionBottomDip is null)
                break;

            var targetContentYDip = BuildContentYAfterTopAndBottomWrapExclusion(
                surface,
                mutableCurrentContentYDip,
                plannedContentYDip,
                exclusionBottomDip.Value,
                safeColumnCount);
            if (!double.IsFinite(targetContentYDip) || targetContentYDip <= mutableCurrentContentYDip)
                break;

            appliedTopAndBottomBottomDip = exclusionBottomDip.Value;
            mutableCurrentContentYDip = targetContentYDip;
            plannedContentYDip = targetContentYDip;
        }

        var safeTextAreaHeightDip = Math.Max(1, surface.TextAreaHeightDip);
        var slot = Math.Max(0, (int)(plannedContentYDip / safeTextAreaHeightDip));
        var columnIndex = safeColumnCount > 1 ? slot % safeColumnCount : 0;
        var columnLeftDip = contentLeftDip + columnIndex * (safeColumnWidthDip + safeColumnGapDip);
        var plannedPageSpaceYDip = surface.ContentYToPageSpaceY(plannedContentYDip, safeColumnCount);
        var lateral = BuildSquareTightWrapExclusion(
            zoneList,
            plannedPageSpaceYDip,
            safeLineHeightDip,
            columnLeftDip,
            safeColumnWidthDip,
            wrapGapDip,
            minimumWidthDip);
        var effectiveTextWidthDip = Math.Max(
            minimumWidthDip,
            safeBaseTextWidthDip - lateral.LeftDeltaDip - lateral.RightShrinkDip);
        var splitLine = BuildBothSidesSplitLinePlan(
            zoneList,
            plannedPageSpaceYDip,
            safeLineHeightDip,
            columnLeftDip,
            safeBaseTextWidthDip,
            wrapGapDip,
            minimumWidthDip);

        return new DocumentFloatingTextWrapLinePlan(
            requestedContentYDip,
            plannedContentYDip,
            plannedPageSpaceYDip,
            columnIndex,
            columnLeftDip,
            safeColumnWidthDip,
            safeBaseTextWidthDip,
            lateral.LeftDeltaDip,
            lateral.RightShrinkDip,
            effectiveTextWidthDip,
            appliedTopAndBottomBottomDip,
            splitLine);
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

    /// <summary>
    /// Builds the eight resize-handle squares (corners + edge midpoints) for a selection
    /// <paramref name="rect"/>. When <paramref name="rotationAngle"/> is non-zero or
    /// <paramref name="flipH"/>/<paramref name="flipV"/> is set, each handle's centre point is carried
    /// through the SAME forward transform DrawFloatingShape uses to render the object (flip about the
    /// rect centre, then rotate by <paramref name="rotationAngle"/>, both about the rect centre) so the
    /// drawn handle sits on the VISIBLE rotated/flipped corner instead of the plain axis-aligned one.
    /// The <see cref="DocumentFloatingHandle"/> tag stays the model-space corner it represents (e.g.
    /// <see cref="DocumentFloatingHandle.TopLeft"/> is always the model's top-left corner, wherever it is
    /// now drawn) — hit-testing against these transformed rects is what lets a click on the visible
    /// corner resolve to the correct model handle.
    /// </summary>
    public static IReadOnlyList<DocumentFloatingHandleRect> BuildFloatingHandleRects(
        DocumentFloatRect rect,
        double handleSizeDip,
        double rotationAngle = 0,
        bool flipH = false,
        bool flipV = false)
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

                var (px, py) = TransformPoint(x[col], y[row], rect, rotationAngle, flipH, flipV);
                handles.Add(new DocumentFloatingHandleRect(
                    handle,
                    new DocumentFloatRect(
                        px - halfDip,
                        py - halfDip,
                        sizeDip,
                        sizeDip)));
            }
        }

        return handles;
    }

    /// <summary>
    /// Applies the forward render transform (flip about the rect centre, then rotate by
    /// <paramref name="rotationAngle"/> degrees, both about <paramref name="rect"/>'s centre) to a single
    /// model-space point. This is the exact inverse of <see cref="UnTransformPoint"/> and mirrors the
    /// matrix DrawFloatingShape builds: translate to centre, flip, rotate, translate back.
    /// </summary>
    public static DocumentFloatPoint TransformPoint(
        DocumentFloatPoint point,
        DocumentFloatRect rect,
        double rotationAngle,
        bool flipH,
        bool flipV)
    {
        var (x, y) = TransformPoint(point.XDip, point.YDip, rect, rotationAngle, flipH, flipV);
        return new DocumentFloatPoint(x, y);
    }

    private static (double X, double Y) TransformPoint(
        double xDip,
        double yDip,
        DocumentFloatRect rect,
        double rotationAngle,
        bool flipH,
        bool flipV)
    {
        if (rotationAngle == 0 && !flipH && !flipV)
            return (xDip, yDip);

        var cx = rect.CenterXDip;
        var cy = rect.CenterYDip;
        var x = xDip - cx;
        var y = yDip - cy;

        if (flipH) x = -x;
        if (flipV) y = -y;

        if (rotationAngle != 0)
        {
            var rad = rotationAngle * Math.PI / 180.0;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);
            (x, y) = (x * cos - y * sin, x * sin + y * cos);
        }

        return (x + cx, y + cy);
    }

    /// <summary>Maps a page-space delta into the local axes of a rotated/flipped object.</summary>
    public static DocumentFloatPoint UnTransformVector(
        DocumentFloatPoint vector,
        double rotationAngle,
        bool flipH,
        bool flipV)
    {
        var x = vector.XDip;
        var y = vector.YDip;
        if (rotationAngle != 0)
        {
            var radians = -rotationAngle * Math.PI / 180.0;
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            (x, y) = (x * cos - y * sin, x * sin + y * cos);
        }

        if (flipH) x = -x;
        if (flipV) y = -y;
        return new DocumentFloatPoint(x, y);
    }

    /// <summary>Applies a child transform followed by parent transforms nearest-to-outer.</summary>
    public static DocumentFloatPoint TransformPointThroughGroupChain(
        DocumentFloatPoint point,
        DocumentFloatRect childRect,
        double childRotationAngle,
        bool childFlipH,
        bool childFlipV,
        IReadOnlyList<DocumentFloatTransform> parentTransforms)
    {
        var transformed = TransformPoint(
            point,
            childRect,
            childRotationAngle,
            childFlipH,
            childFlipV);
        foreach (var parent in parentTransforms)
            transformed = TransformPoint(
                transformed,
                parent.Rect,
                parent.RotationAngle,
                parent.FlipH,
                parent.FlipV);
        return transformed;
    }

    /// <summary>Maps a screen point back through parent transforms and then the child transform.</summary>
    public static DocumentFloatPoint UnTransformPointThroughGroupChain(
        DocumentFloatPoint point,
        DocumentFloatRect childRect,
        double childRotationAngle,
        bool childFlipH,
        bool childFlipV,
        IReadOnlyList<DocumentFloatTransform> parentTransforms)
    {
        var transformed = point;
        for (var index = parentTransforms.Count - 1; index >= 0; index--)
        {
            var parent = parentTransforms[index];
            transformed = UnTransformPoint(
                transformed,
                parent.Rect,
                parent.RotationAngle,
                parent.FlipH,
                parent.FlipV);
        }

        return UnTransformPoint(
            transformed,
            childRect,
            childRotationAngle,
            childFlipH,
            childFlipV);
    }

    /// <summary>Maps a screen-space delta into the selected child's local axes.</summary>
    public static DocumentFloatPoint UnTransformVectorThroughGroupChain(
        DocumentFloatPoint vector,
        IReadOnlyList<DocumentFloatTransform> parentTransforms)
    {
        var transformed = vector;
        for (var index = parentTransforms.Count - 1; index >= 0; index--)
        {
            var parent = parentTransforms[index];
            transformed = UnTransformVector(
                transformed,
                parent.RotationAngle,
                parent.FlipH,
                parent.FlipV);
        }
        return transformed;
    }

    public static IReadOnlyList<DocumentFloatingHandleRect> BuildFloatingGroupChildHandleRectsThroughGroupChain(
        DocumentFloatRect childRect,
        double handleSizeDip,
        double childRotationAngle,
        bool childFlipH,
        bool childFlipV,
        IReadOnlyList<DocumentFloatTransform> parentTransforms)
    {
        // Build raw model-space handle centres, then apply the leaf and ancestor transforms once.
        // Applying the leaf transform here and again through the composed helper moves handles away
        // from the rendered corners for rotated or flipped nested children.
        return BuildFloatingHandleRects(
                childRect,
                handleSizeDip)
            .Select(handle =>
            {
                var center = TransformPointThroughGroupChain(
                    new DocumentFloatPoint(handle.Rect.CenterXDip, handle.Rect.CenterYDip),
                    childRect,
                    childRotationAngle,
                    childFlipH,
                    childFlipV,
                    parentTransforms);
                return new DocumentFloatingHandleRect(
                    handle.Handle,
                    new DocumentFloatRect(
                        center.XDip - handle.Rect.WidthDip / 2,
                        center.YDip - handle.Rect.HeightDip / 2,
                        handle.Rect.WidthDip,
                        handle.Rect.HeightDip));
            })
            .ToList();
    }

    public static DocumentFloatingHandle HitTestFloatingGroupChildHandleThroughGroupChain(
        DocumentFloatRect childRect,
        DocumentFloatPoint point,
        double handleSizeDip,
        double hitPaddingDip,
        double childRotationAngle,
        bool childFlipH,
        bool childFlipV,
        IReadOnlyList<DocumentFloatTransform> parentTransforms)
    {
        foreach (var handle in BuildFloatingGroupChildHandleRectsThroughGroupChain(
            childRect,
            handleSizeDip,
            childRotationAngle,
            childFlipH,
            childFlipV,
            parentTransforms))
        {
            if (handle.Rect.Inflate(Math.Max(0, hitPaddingDip)).Contains(point))
                return handle.Handle;
        }

        return DocumentFloatingHandle.None;
    }

    public static bool ContainsFloatingGroupChildPointThroughGroupChain(
        DocumentFloatRect childRect,
        DocumentFloatPoint point,
        double childRotationAngle,
        bool childFlipH,
        bool childFlipV,
        IReadOnlyList<DocumentFloatTransform> parentTransforms)
    {
        var corners = new[]
        {
            new DocumentFloatPoint(childRect.LeftDip, childRect.TopDip),
            new DocumentFloatPoint(childRect.RightDip, childRect.TopDip),
            new DocumentFloatPoint(childRect.RightDip, childRect.BottomDip),
            new DocumentFloatPoint(childRect.LeftDip, childRect.BottomDip)
        }
        .Select(corner => TransformPointThroughGroupChain(
            corner,
            childRect,
            childRotationAngle,
            childFlipH,
            childFlipV,
            parentTransforms))
        .ToArray();

        var inside = false;
        for (var index = 0; index < corners.Length; index++)
        {
            var next = corners[(index + 1) % corners.Length];
            var crosses = (corners[index].YDip > point.YDip) != (next.YDip > point.YDip);
            if (crosses
                && point.XDip < (next.XDip - corners[index].XDip)
                    * (point.YDip - corners[index].YDip)
                    / (next.YDip - corners[index].YDip)
                    + corners[index].XDip)
                inside = !inside;
        }
        return inside;
    }

    public static DocumentFloatRect BuildFloatingGroupChildMoveRectThroughGroupChain(
        DocumentFloatRect childRect,
        DocumentFloatPoint pointerDown,
        DocumentFloatPoint pointer,
        IReadOnlyList<DocumentFloatTransform> parentTransforms)
    {
        var localDown = UnTransformPointThroughGroupChain(
            pointerDown,
            childRect,
            0,
            false,
            false,
            parentTransforms);
        var localPointer = UnTransformPointThroughGroupChain(
            pointer,
            childRect,
            0,
            false,
            false,
            parentTransforms);
        return BuildFloatingMoveRect(childRect, localDown, localPointer);
    }

    public static DocumentFloatRect BuildFloatingGroupChildResizeRectThroughGroupChain(
        DocumentFloatRect childRect,
        DocumentFloatingHandle handle,
        DocumentFloatPoint pointer,
        bool preserveAspect,
        double minimumSizeDip,
        double childRotationAngle,
        bool childFlipH,
        bool childFlipV,
        IReadOnlyList<DocumentFloatTransform> parentTransforms)
    {
        var localPointer = UnTransformPointThroughGroupChain(
            pointer,
            childRect,
            0,
            false,
            false,
            parentTransforms);
        return BuildFloatingResizeRect(
            childRect,
            handle,
            localPointer,
            preserveAspect,
            minimumSizeDip,
            childRotationAngle,
            childFlipH,
            childFlipV);
    }

    public static IReadOnlyList<DocumentFloatingHandleRect> BuildFloatingGroupChildHandleRects(
        DocumentFloatRect groupRect,
        DocumentFloatRect childRect,
        double handleSizeDip,
        double childRotationAngle = 0,
        bool childFlipH = false,
        bool childFlipV = false,
        double groupRotationAngle = 0,
        bool groupFlipH = false,
        bool groupFlipV = false)
    {
        return BuildFloatingHandleRects(childRect, handleSizeDip,
                childRotationAngle, childFlipH, childFlipV)
            .Select(handle =>
            {
                var center = new DocumentFloatPoint(
                    handle.Rect.CenterXDip,
                    handle.Rect.CenterYDip);
                var transformed = TransformPoint(
                    new DocumentFloatPoint(center.XDip, center.YDip),
                    groupRect,
                    groupRotationAngle,
                    groupFlipH,
                    groupFlipV);
                return new DocumentFloatingHandleRect(
                    handle.Handle,
                    new DocumentFloatRect(
                        transformed.XDip - handle.Rect.WidthDip / 2,
                        transformed.YDip - handle.Rect.HeightDip / 2,
                        handle.Rect.WidthDip,
                        handle.Rect.HeightDip));
            })
            .ToList();
    }

    public static DocumentFloatingHandle HitTestFloatingGroupChildHandle(
        DocumentFloatRect groupRect,
        DocumentFloatRect childRect,
        DocumentFloatPoint point,
        double handleSizeDip,
        double hitPaddingDip,
        double childRotationAngle = 0,
        bool childFlipH = false,
        bool childFlipV = false,
        double groupRotationAngle = 0,
        bool groupFlipH = false,
        bool groupFlipV = false)
    {
        var groupLocalPoint = UnTransformPoint(point, groupRect,
            groupRotationAngle, groupFlipH, groupFlipV);
        return HitTestFloatingHandle(childRect, groupLocalPoint, handleSizeDip,
            hitPaddingDip, childRotationAngle, childFlipH, childFlipV);
    }

    /// <summary>Tests a pointer against the visible child polygon using the group render transforms.</summary>
    public static bool ContainsFloatingGroupChildPoint(
        DocumentFloatRect groupRect,
        DocumentFloatRect childRect,
        DocumentFloatPoint point,
        double childRotationAngle = 0,
        bool childFlipH = false,
        bool childFlipV = false,
        double groupRotationAngle = 0,
        bool groupFlipH = false,
        bool groupFlipV = false)
    {
        var corners = new[]
        {
            new DocumentFloatPoint(childRect.LeftDip, childRect.TopDip),
            new DocumentFloatPoint(childRect.RightDip, childRect.TopDip),
            new DocumentFloatPoint(childRect.RightDip, childRect.BottomDip),
            new DocumentFloatPoint(childRect.LeftDip, childRect.BottomDip)
        }
        .Select(corner => TransformPoint(corner, childRect,
            childRotationAngle, childFlipH, childFlipV))
        .Select(corner => TransformPoint(corner, groupRect,
            groupRotationAngle, groupFlipH, groupFlipV))
        .ToArray();

        var inside = false;
        for (var index = 0; index < corners.Length; index++)
        {
            var next = corners[(index + 1) % corners.Length];
            var crosses = (corners[index].YDip > point.YDip) != (next.YDip > point.YDip);
            if (crosses
                && point.XDip < (next.XDip - corners[index].XDip)
                    * (point.YDip - corners[index].YDip)
                    / (next.YDip - corners[index].YDip)
                    + corners[index].XDip)
                inside = !inside;
        }

        return inside;
    }

    public static DocumentFloatRect BuildFloatingGroupChildMoveRect(
        DocumentFloatRect groupRect,
        DocumentFloatRect childRect,
        DocumentFloatPoint pointerDown,
        DocumentFloatPoint pointer,
        double groupRotationAngle = 0,
        bool groupFlipH = false,
        bool groupFlipV = false)
    {
        var localDown = UnTransformPoint(pointerDown, groupRect,
            groupRotationAngle, groupFlipH, groupFlipV);
        var localPointer = UnTransformPoint(pointer, groupRect,
            groupRotationAngle, groupFlipH, groupFlipV);
        return BuildFloatingMoveRect(childRect, localDown, localPointer);
    }

    public static DocumentFloatRect BuildFloatingGroupChildResizeRect(
        DocumentFloatRect groupRect,
        DocumentFloatRect childRect,
        DocumentFloatingHandle handle,
        DocumentFloatPoint pointer,
        bool preserveAspect,
        double minimumSizeDip,
        double childRotationAngle = 0,
        bool childFlipH = false,
        bool childFlipV = false,
        double groupRotationAngle = 0,
        bool groupFlipH = false,
        bool groupFlipV = false)
    {
        var groupLocalPointer = UnTransformPoint(pointer, groupRect,
            groupRotationAngle, groupFlipH, groupFlipV);
        return BuildFloatingResizeRect(childRect, handle, groupLocalPointer,
            preserveAspect, minimumSizeDip,
            childRotationAngle, childFlipH, childFlipV);
    }

    public static DocumentFloatingHandle HitTestFloatingHandle(
        DocumentFloatRect selectionRect,
        DocumentFloatPoint point,
        double handleSizeDip,
        double hitPaddingDip,
        double rotationAngle = 0,
        bool flipH = false,
        bool flipV = false)
    {
        foreach (var handleRect in BuildFloatingHandleRects(selectionRect, handleSizeDip, rotationAngle, flipH, flipV))
        {
            if (handleRect.Rect.Inflate(Math.Max(0, hitPaddingDip)).Contains(point))
                return handleRect.Handle;
        }

        var localPoint = UnTransformPoint(point, selectionRect, rotationAngle, flipH, flipV);
        return selectionRect.Contains(localPoint)
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

    /// <summary>
    /// Computes the resized axis-aligned rect for a handle drag, working entirely in the floating
    /// object's OWN (model) frame so the result composes correctly with rotation/flip at render time.
    /// When <paramref name="rotationAngle"/> and/or <paramref name="flipH"/>/<paramref name="flipV"/> are
    /// set, <paramref name="pointer"/> (a SCREEN/page-space point) is first mapped into that local frame
    /// via <see cref="UnTransformPoint"/> — the inverse of DrawFloatingShape's render transform — so the
    /// object grows along its OWN axes instead of the screen axes, and the opposite (anchored) corner in
    /// LOCAL space stays fixed (which is also the correct anchor once the caller re-applies the
    /// rotation/flip for rendering). The returned rect is in the SAME local frame as <paramref
    /// name="baseRect"/>. Rotation/flip default to zero/false so existing unrotated callers are unaffected.
    /// </summary>
    public static DocumentFloatRect BuildFloatingResizeRect(
        DocumentFloatRect baseRect,
        DocumentFloatingHandle handle,
        DocumentFloatPoint pointer,
        bool preserveAspect,
        double minimumSizeDip,
        double rotationAngle = 0,
        bool flipH = false,
        bool flipV = false)
    {
        var localPointer = UnTransformPoint(pointer, baseRect, rotationAngle, flipH, flipV);

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
            leftDip = Math.Min(localPointer.XDip, rightDip - minimumDip);
        if (movesRight)
            rightDip = Math.Max(localPointer.XDip, leftDip + minimumDip);
        if (movesTop)
            topDip = Math.Min(localPointer.YDip, bottomDip - minimumDip);
        if (movesBottom)
            bottomDip = Math.Max(localPointer.YDip, topDip + minimumDip);

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

    private static void AddSnapshot(
        ICollection<DocumentFloatingObjectSnapshot> snapshots,
        DocumentFloatingObjectKind kind,
        int blockIndex,
        int runIndex,
        DocumentViewSurfacePlan surface,
        double anchorContentYDip,
        int columnCount,
        double widthPt,
        double heightPt,
        double defaultWidthPt,
        double defaultHeightPt,
        FloatingPlacement placement,
        double rotationAngle = 0,
        bool flipH = false,
        bool flipV = false)
    {
        AddSnapshot(
            snapshots,
            kind,
            blockIndex,
            runIndex,
            surface,
            anchorContentYDip,
            columnCount,
            widthPt,
            heightPt,
            defaultWidthPt,
            defaultHeightPt,
            placement.Wrapping,
            placement.WrapTextSide,
            placement.ZOrderIndex,
            placement.HorizontalAnchor,
            placement.HorizontalOffsetPt,
            placement.VerticalAnchor,
            placement.VerticalOffsetPt,
            rotationAngle,
            flipH,
            flipV);
    }

    private static void AddSnapshot(
        ICollection<DocumentFloatingObjectSnapshot> snapshots,
        DocumentFloatingObjectKind kind,
        int blockIndex,
        int runIndex,
        DocumentViewSurfacePlan surface,
        double anchorContentYDip,
        int columnCount,
        double widthPt,
        double heightPt,
        double defaultWidthPt,
        double defaultHeightPt,
        ImageWrapping wrapping,
        FloatingWrapTextSide wrapTextSide,
        int zOrderIndex,
        HorizontalAnchor horizontalAnchor,
        double horizontalOffsetPt,
        VerticalAnchor verticalAnchor,
        double verticalOffsetPt,
        double rotationAngle = 0,
        bool flipH = false,
        bool flipV = false)
    {
        var placement = BuildFloatingObjectPlacement(
            surface,
            anchorContentYDip,
            columnCount,
            horizontalAnchor,
            horizontalOffsetPt,
            verticalAnchor,
            verticalOffsetPt);
        var widthDip = PageLayout.PointsToDip(widthPt > 0 ? widthPt : defaultWidthPt);
        var heightDip = PageLayout.PointsToDip(heightPt > 0 ? heightPt : defaultHeightPt);

        snapshots.Add(new DocumentFloatingObjectSnapshot(
            kind,
            blockIndex,
            runIndex,
            new DocumentFloatRect(placement.XDip, placement.YDip, widthDip, heightDip),
            wrapping == ImageWrapping.Behind,
            zOrderIndex,
            wrapping,
            rotationAngle,
            flipH,
            flipV,
            wrapTextSide));
    }

    private static double EstimateWordArtWidthPt(WordArt wordArt) =>
        wordArt.WidthPt is > 0 ? wordArt.WidthPt.Value
            : Math.Max(72, wordArt.FontSizePt * Math.Max(1, wordArt.Text.Length) * 0.62);

    private static double EstimateWordArtHeightPt(WordArt wordArt) =>
        wordArt.HeightPt is > 0 ? wordArt.HeightPt.Value
            : Math.Max(40, wordArt.FontSizePt * 1.6);

    private static bool TryGetFloatingKind(object modelObject, out DocumentFloatingObjectKind kind)
    {
        kind = modelObject switch
        {
            InlineImage => DocumentFloatingObjectKind.Image,
            Shape => DocumentFloatingObjectKind.Shape,
            Chart => DocumentFloatingObjectKind.Chart,
            WordArt => DocumentFloatingObjectKind.WordArt,
            SmartArt => DocumentFloatingObjectKind.SmartArt,
            DrawingGroup => DocumentFloatingObjectKind.Group,
            _ => default
        };

        return modelObject is InlineImage or Shape or Chart or WordArt or SmartArt or DrawingGroup;
    }

    private static double EstimateTableRowHeightDip(TableRow row)
    {
        var authoredHeight = row.HeightPt is { } heightPt && heightPt > 0
            ? PageLayout.PointsToDip(heightPt)
            : 0;
        var textHeight = EstimateTableRowTextHeightDip(row);

        return row.HeightRule == TableRowHeightRule.Exact && authoredHeight > 0
            ? RoundDip(Math.Max(MinimumTableRowHeightDip, authoredHeight))
            : RoundDip(Math.Max(DefaultTableRowHeightDip, Math.Max(authoredHeight, textHeight)));
    }

    private static double EstimateTableRowTextHeightDip(TableRow row)
    {
        if (row.Cells.Count == 0)
            return DefaultTableRowHeightDip;

        var maxLines = row.Cells
            .Select(cell => Math.Max(1, cell.Paragraphs.Sum(EstimateParagraphLineCount)))
            .DefaultIfEmpty(1)
            .Max();
        return Math.Max(
            MinimumTableRowHeightDip,
            maxLines * EstimatedTableLineHeightDip + EstimatedTableVerticalPaddingDip);
    }

    private static int EstimateParagraphLineCount(Paragraph paragraph)
    {
        var text = paragraph.PlainText;
        if (string.IsNullOrEmpty(text))
            return 1;

        var explicitLines = text.Count(ch => ch == '\n') + 1;
        var wrappedLines = Math.Max(1, (int)Math.Ceiling(text.Length / 48.0));
        return Math.Max(explicitLines, wrappedLines);
    }

    private static int CountVerticalMergeSpan(Table table, int restartRow, int gridColumn)
    {
        var span = 1;
        for (var rowIndex = restartRow + 1; rowIndex < table.Rows.Count; rowIndex++)
        {
            var continuation = CellAtGridColumn(table.Rows[rowIndex], gridColumn);
            if (continuation?.VerticalMerge == VerticalMergeState.Continue)
                span++;
            else
                break;
        }

        return span;
    }

    private static TableCell? CellAtGridColumn(TableRow row, int targetGridColumn)
    {
        var gridColumn = 0;
        foreach (var cell in row.Cells)
        {
            var span = Math.Max(1, cell.GridSpan);
            if (targetGridColumn >= gridColumn && targetGridColumn < gridColumn + span)
                return cell;

            gridColumn += span;
        }

        return null;
    }

    private static string? NormalizeHexColorOrNull(string? hex) =>
        string.IsNullOrWhiteSpace(hex) ? null : NormalizeHexColor(hex);

    private static string NormalizeHexColor(string hex)
    {
        var value = hex.Trim();
        if (value.StartsWith('#'))
            value = value[1..];
        if (value.Length == 8)
            value = value[2..];

        return value.Length == 6
            ? "#" + value.ToUpperInvariant()
            : value;
    }

    private static double RoundDip(double value) =>
        double.IsFinite(value) ? Math.Round(value, 3, MidpointRounding.AwayFromZero) : 0;

    private static DocumentViewSurfacePlan BuildPrintSurfacePlan(
        PageSettings page,
        double availableWidthDip,
        DocumentViewLayoutOptions options,
        bool collapsePageBoundaries)
    {
        var pageWidthDip = Math.Max(options.MinPrintPageWidthDip, PageLayout.PointsToDip(page.WidthPt));
        var pageHeightDip = Math.Max(options.MinPrintPageHeightDip, PageLayout.PointsToDip(page.HeightPt));
        var (marginLeftDip, marginTopDip, marginRightDip, marginBottomDip) = PageLayout.MarginsDip(page);
        var pageLeftDip = Math.Max(options.MinHorizontalGutterDip, (availableWidthDip - pageWidthDip) / 2);
        var contentWidthDip = Math.Max(options.MinContentWidthDip, pageWidthDip - marginLeftDip - marginRightDip);
        var textAreaHeightDip = Math.Max(options.MinPrintTextAreaHeightDip, pageHeightDip - marginTopDip - marginBottomDip);
        var displayedPageHeightDip = collapsePageBoundaries ? textAreaHeightDip : pageHeightDip;
        var displayedMarginTopDip = collapsePageBoundaries ? 0 : marginTopDip;
        var displayedMarginBottomDip = collapsePageBoundaries ? 0 : marginBottomDip;
        var displayedPageGapDip = collapsePageBoundaries ? 0 : options.PageGapDip;

        return new DocumentViewSurfacePlan(
            DocumentViewLayoutKind.PrintLayout,
            pageWidthDip,
            displayedPageHeightDip,
            marginLeftDip,
            displayedMarginTopDip,
            marginRightDip,
            displayedMarginBottomDip,
            pageLeftDip,
            pageLeftDip + marginLeftDip,
            contentWidthDip,
            textAreaHeightDip,
            options.DeskPaddingDip,
            displayedPageGapDip);
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
