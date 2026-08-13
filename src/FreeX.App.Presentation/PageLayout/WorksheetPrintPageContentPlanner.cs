using FreeX.App.Presentation;
using FreeX.App.Presentation.Comments;
using FreeX.App.Presentation.Rendering;
using FreeX.App.Presentation.Text;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Explicit host capabilities for worksheet print materialization. Geometry and content policy stay
/// shared; native hosts opt into the richer materializers they already support.
/// </summary>
public sealed record WorksheetPrintMaterializationProfile(
    bool BakeScaleIntoGeometry,
    bool RenderHeaderFooterPictures,
    bool RenderDisplayedComments,
    bool RenderPrintableLinks,
    bool PreserveNativeCellFidelity,
    bool PreserveNativeChartFidelity,
    double HeaderFooterBaseLineHeight,
    bool SizeHeaderFooterBandsToContent)
{
    public static WorksheetPrintMaterializationProfile WpfNative { get; } = new(
        BakeScaleIntoGeometry: false,
        RenderHeaderFooterPictures: true,
        RenderDisplayedComments: true,
        RenderPrintableLinks: true,
        PreserveNativeCellFidelity: true,
        PreserveNativeChartFidelity: true,
        HeaderFooterBaseLineHeight: 18.0,
        SizeHeaderFooterBandsToContent: true);

    public static WorksheetPrintMaterializationProfile AvaloniaPreview { get; } = new(
        BakeScaleIntoGeometry: true,
        RenderHeaderFooterPictures: false,
        RenderDisplayedComments: false,
        RenderPrintableLinks: false,
        PreserveNativeCellFidelity: false,
        PreserveNativeChartFidelity: false,
        HeaderFooterBaseLineHeight: 16.0,
        SizeHeaderFooterBandsToContent: false);
}

public sealed record WorksheetPrintTransformPlan(
    double ScaleRatio,
    double HeaderFooterFontScale,
    LayoutPoint Anchor,
    bool ApplyNativeTransform,
    LayoutRect PageClip);

public sealed record WorksheetPrintCellLayerPlan(
    PrintGridMeasurement Measurement,
    IReadOnlyList<uint> Rows,
    IReadOnlyList<uint> Columns,
    IReadOnlyList<uint> BodyRows,
    IReadOnlyList<uint> BodyColumns,
    LayoutRect ContentBounds,
    LayoutRect GridBounds,
    bool PrintGridlines,
    bool PrintHeadings,
    bool BlackAndWhite,
    WorksheetPrintErrorValue PrintErrorValue,
    IReadOnlyList<PageCellBlock> PortableCells);

public sealed record WorksheetPrintHeaderFooterVariant(
    WorksheetHeaderFooter Header,
    WorksheetHeaderFooter Footer,
    WorksheetHeaderFooterPictureSet HeaderPictures,
    WorksheetHeaderFooterPictureSet FooterPictures);

public sealed record WorksheetPrintHeaderFooterBandGeometry(
    LayoutRect Left,
    LayoutRect Center,
    LayoutRect Right,
    double TextLineHeight);

public sealed record WorksheetPrintHeaderFooterPlan(
    WorksheetHeaderFooter Header,
    WorksheetHeaderFooter Footer,
    WorksheetHeaderFooterPictureSet HeaderPictures,
    WorksheetHeaderFooterPictureSet FooterPictures,
    bool AlignWithMargins,
    WorksheetPrintHeaderFooterBandGeometry HeaderBand,
    WorksheetPrintHeaderFooterBandGeometry FooterBand,
    IReadOnlyList<PageHeaderFooterRun> HeaderRuns,
    IReadOnlyList<PageHeaderFooterRun> FooterRuns);

public sealed record WorksheetPrintDrawingLayerPlan(
    bool RenderCharts,
    IReadOnlyList<PageChartBlock> PortableCharts,
    IReadOnlyList<PagePictureBlock> Pictures,
    IReadOnlyList<PageTextBoxBlock> TextBoxes);

public sealed record WorksheetPrintCommentLayerPlan(
    bool RenderDisplayedComments,
    IReadOnlyDictionary<CellAddress, string> DisplayedCommentText,
    IReadOnlyList<PageDisplayedCommentBlock> DisplayedComments);

public sealed record WorksheetPrintHyperlinkPlan(
    string Target,
    HyperlinkTargetKind TargetKind,
    CellAddress SourceAddress,
    CellAddress? TargetAddress);

/// <summary>
/// Complete UI-free policy for one worksheet page. Hosts materialize this plan with WPF or Avalonia
/// primitives and do not repeat page setup, variant, drawing, comment, link, clip, or transform rules.
/// </summary>
public sealed record WorksheetPrintPageContentPlan(
    WorksheetPrintMaterializationProfile Profile,
    WorksheetPrintPagePlan Page,
    WorksheetPrintRenderMetrics Metrics,
    PageContentLayout PortableLayout,
    WorksheetPrintTransformPlan Transform,
    WorksheetPrintCellLayerPlan Cells,
    WorksheetPrintHeaderFooterPlan HeaderFooter,
    WorksheetPrintDrawingLayerPlan Drawings,
    WorksheetPrintCommentLayerPlan Comments,
    IReadOnlyDictionary<(uint Row, uint Col), WorksheetPrintHyperlinkPlan> Hyperlinks,
    IReadOnlyDictionary<(uint Row, uint Col), CellAddress> CellDestinations,
    int DisplayedPageNumber,
    int TotalPageCount);

public static class WorksheetPrintPageContentPlanner
{
    private static readonly IReadOnlyDictionary<CellAddress, ThreadedComment> EmptyThreadedComments =
        new Dictionary<CellAddress, ThreadedComment>();

    public static WorksheetPrintPageContentPlan? Build(
        Workbook workbook,
        Sheet sheet,
        WorksheetPrintRenderPlan renderPlan,
        WorksheetPrintPagePlan page,
        ITextMeasurer textMeasurer,
        WorksheetPrintMaterializationProfile profile,
        DateTime? now = null,
        string workbookDirectory = "",
        int pageNumberOffset = 0,
        int? totalPageCountOverride = null)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(renderPlan);
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(textMeasurer);
        ArgumentNullException.ThrowIfNull(profile);

        if (page.AreaIndex < 0 || page.AreaIndex >= renderPlan.AreaPlans.Count)
            return null;

        var area = renderPlan.AreaPlans[page.AreaIndex];
        var pagination = new PagePaginationResult(
            PagePaginationPlanner.BuildSegments(area.Pagination.RowPlans),
            PagePaginationPlanner.BuildSegments(area.Pagination.ColumnPlans),
            area.Pagination.EffectiveScalePercent);
        var displayedPageNumber = page.PageNumber + pageNumberOffset;
        var totalPages = totalPageCountOverride ?? renderPlan.GridPageCount;
        var layout = PageContentRenderModelBuilder.Build(
            workbook,
            sheet,
            pagination,
            page.AreaPageIndex,
            textMeasurer,
            now,
            workbookDirectory,
            displayedPageNumber,
            totalPages);
        if (layout is null)
            return null;

        var metrics = renderPlan.Metrics;
        var bodyTop = PageGeometryRules.ResolveBodyEdge(metrics.MarginTop, metrics.HeaderMargin);
        var bodyBottom = PageGeometryRules.ResolveBodyEdge(metrics.MarginBottom, metrics.FooterMargin);
        var bodyHeight = Math.Max(0, metrics.PageHeight - bodyTop - bodyBottom);
        var measurement = PrintLayoutPlanner.MeasurePrintableGrid(
            metrics.PrintableWidth,
            profile.BakeScaleIntoGeometry ? bodyHeight : metrics.PrintableHeight,
            page.Rows,
            page.Columns,
            sheet.RowHeights,
            BuildColumnWidthsPixels(sheet),
            sheet.PrintHeadings);

        var printedWidth = measurement.HeaderWidth + measurement.TotalColumnWidth(page.Columns.Count);
        var printedHeight = measurement.HeaderHeight + measurement.TotalRowHeight(page.Rows.Count);
        var scaleRatio = ResolveScaleRatio(
            area.Pagination.EffectiveScalePercent,
            printedWidth,
            printedHeight,
            metrics.PrintableWidth,
            profile.BakeScaleIntoGeometry ? bodyHeight : metrics.PrintableHeight);
        if (profile.BakeScaleIntoGeometry)
            measurement = ScaleMeasurement(measurement, scaleRatio);

        var materializedWidth = measurement.HeaderWidth + measurement.TotalColumnWidth(page.Columns.Count);
        var materializedHeight = measurement.HeaderHeight + measurement.TotalRowHeight(page.Rows.Count);
        var centeringWidth = profile.BakeScaleIntoGeometry ? materializedWidth : materializedWidth * scaleRatio;
        var centeringHeight = profile.BakeScaleIntoGeometry ? materializedHeight : materializedHeight * scaleRatio;
        var availableHeight = profile.BakeScaleIntoGeometry ? bodyHeight : metrics.PrintableHeight;
        var xOffset = sheet.CenterHorizontallyOnPage
            ? Math.Max(0, (metrics.PrintableWidth - centeringWidth) / 2)
            : 0;
        var yOffset = sheet.CenterVerticallyOnPage
            ? Math.Max(0, (availableHeight - centeringHeight) / 2)
            : 0;
        var contentLeft = metrics.MarginLeft + xOffset;
        var contentTop = bodyTop + yOffset;
        var gridLeft = contentLeft + measurement.HeaderWidth * (profile.BakeScaleIntoGeometry ? 1 : scaleRatio);
        var gridTop = contentTop + measurement.HeaderHeight * (profile.BakeScaleIntoGeometry ? 1 : scaleRatio);
        var gridBounds = new LayoutRect(
            gridLeft,
            gridTop,
            measurement.TotalColumnWidth(page.Columns.Count),
            measurement.TotalRowHeight(page.Rows.Count));
        var contentBounds = new LayoutRect(
            contentLeft,
            contentTop,
            materializedWidth,
            materializedHeight);

        var headerFooter = ResolveHeaderFooterVariant(sheet, page.PageNumber);
        var headerFooterFontScale = PageGeometryRules.ResolveHeaderFooterFontScale(
            sheet.HeaderFooterScaleWithDocument,
            scaleRatio);
        var headerPictures = profile.RenderHeaderFooterPictures
            ? headerFooter.HeaderPictures
            : WorksheetHeaderFooterPictureSet.Empty;
        var footerPictures = profile.RenderHeaderFooterPictures
            ? headerFooter.FooterPictures
            : WorksheetHeaderFooterPictureSet.Empty;
        var headerBand = WorksheetPrintHeaderFooterGeometryPlanner.BuildBand(
            headerFooter.Header,
            headerPictures,
            metrics.PageWidth,
            metrics.PageHeight,
            metrics.MarginLeft,
            metrics.MarginRight,
            metrics.MarginBottom,
            metrics.HeaderMargin,
            sheet.HeaderFooterAlignWithMargins,
            isFooter: false,
            draftQuality: sheet.PrintDraftQuality,
            fontScale: headerFooterFontScale,
            baseLineHeight: profile.HeaderFooterBaseLineHeight,
            sizeToContent: profile.SizeHeaderFooterBandsToContent);
        var footerBand = WorksheetPrintHeaderFooterGeometryPlanner.BuildBand(
            headerFooter.Footer,
            footerPictures,
            metrics.PageWidth,
            metrics.PageHeight,
            metrics.MarginLeft,
            metrics.MarginRight,
            metrics.MarginBottom,
            metrics.FooterMargin,
            sheet.HeaderFooterAlignWithMargins,
            isFooter: true,
            draftQuality: sheet.PrintDraftQuality,
            fontScale: headerFooterFontScale,
            baseLineHeight: profile.HeaderFooterBaseLineHeight,
            sizeToContent: profile.SizeHeaderFooterBandsToContent);
        var displayedCommentText = ResolveDisplayedCommentText(sheet);
        var displayedComments = profile.RenderDisplayedComments &&
                                !sheet.PrintDraftQuality &&
                                sheet.PrintComments == WorksheetPrintComments.AsDisplayed
            ? BuildDisplayedComments(
                displayedCommentText,
                sheet.ShownComments,
                page.Rows,
                page.Columns,
                measurement,
                gridLeft,
                gridTop,
                metrics.PageWidth,
                metrics.PageHeight)
            : [];
        layout = layout with { Comments = displayedComments };

        var pictures = profile.BakeScaleIntoGeometry
            ? layout.Pictures
            : sheet.PrintDraftQuality
                ? []
                : PagePictureLayoutPlanner.Build(
                    sheet.Pictures,
                    page.Rows,
                    page.Columns,
                    gridLeft,
                    gridTop,
                    measurement);
        var textBoxes = profile.BakeScaleIntoGeometry
            ? layout.TextBoxes
            : PageTextBoxLayoutPlanner.Build(
                sheet.TextBoxes,
                workbook.Theme,
                page.Rows,
                page.Columns,
                gridLeft,
                gridTop,
                measurement);

        return new WorksheetPrintPageContentPlan(
            profile,
            page,
            metrics,
            layout,
            new WorksheetPrintTransformPlan(
                scaleRatio,
                headerFooterFontScale,
                new LayoutPoint(contentLeft, contentTop),
                ApplyNativeTransform: !profile.BakeScaleIntoGeometry && scaleRatio != 1.0,
                PageClip: new LayoutRect(0, 0, metrics.PageWidth, metrics.PageHeight)),
            new WorksheetPrintCellLayerPlan(
                measurement,
                page.Rows,
                page.Columns,
                page.RowPlan.BodyRows,
                page.ColumnPlan.BodyColumns,
                contentBounds,
                gridBounds,
                sheet.PrintGridlines,
                sheet.PrintHeadings,
                sheet.PrintBlackAndWhite,
                sheet.PrintErrorValue,
                layout.Cells),
            new WorksheetPrintHeaderFooterPlan(
                headerFooter.Header,
                headerFooter.Footer,
                headerPictures,
                footerPictures,
                sheet.HeaderFooterAlignWithMargins,
                headerBand,
                footerBand,
                layout.HeaderRuns,
                layout.FooterRuns),
            new WorksheetPrintDrawingLayerPlan(
                RenderCharts: !sheet.PrintDraftQuality,
                PortableCharts: layout.Charts,
                Pictures: pictures,
                TextBoxes: textBoxes),
            new WorksheetPrintCommentLayerPlan(
                displayedComments.Count > 0,
                displayedCommentText,
                displayedComments),
            profile.RenderPrintableLinks
                ? WorksheetPrintHyperlinkPlanner.BuildPrintableHyperlinks(workbook, sheet)
                : new Dictionary<(uint Row, uint Col), WorksheetPrintHyperlinkPlan>(),
            profile.RenderPrintableLinks
                ? WorksheetPrintHyperlinkPlanner.BuildPrintableCellDestinations(workbook, sheet)
                : new Dictionary<(uint Row, uint Col), CellAddress>(),
            displayedPageNumber,
            totalPages);
    }

    public static int ComputeTotalPageCount(Sheet sheet, WorksheetPrintRenderPlan renderPlan)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(renderPlan);
        if (sheet.PrintComments != WorksheetPrintComments.AtEnd)
            return renderPlan.GridPageCount;

        var printed = PrintCommentSummaryPlanner.FilterToPrintedCells(sheet, renderPlan);
        return renderPlan.GridPageCount + PrintCommentSummaryPlanner.BuildPages(
            printed.Comments,
            printed.ThreadedComments,
            renderPlan.Metrics.PageHeight,
            renderPlan.Metrics.MarginTop).Count;
    }

    public static IReadOnlyList<PrintCommentSummaryPagePlan> BuildCommentSummaryPages(
        Sheet sheet,
        WorksheetPrintRenderPlan renderPlan)
    {
        if (sheet.PrintComments != WorksheetPrintComments.AtEnd)
            return [];

        var printed = PrintCommentSummaryPlanner.FilterToPrintedCells(sheet, renderPlan);
        return PrintCommentSummaryPlanner.BuildPages(
            printed.Comments,
            printed.ThreadedComments,
            renderPlan.Metrics.PageHeight,
            renderPlan.Metrics.MarginTop);
    }

    public static double ResolveScaleRatio(
        double effectiveScalePercent,
        double printedWidth,
        double printedHeight,
        double printableWidth,
        double printableHeight)
    {
        var scaleRatio = double.IsFinite(effectiveScalePercent) && effectiveScalePercent > 0
            ? Math.Max(0.001, effectiveScalePercent / 100.0)
            : 1.0;
        var scaledWidth = printedWidth * scaleRatio;
        var scaledHeight = printedHeight * scaleRatio;
        var widthFitScale = scaledWidth > printableWidth && scaledWidth > 0
            ? printableWidth / scaledWidth
            : 1.0;
        var heightFitScale = scaledHeight > printableHeight && scaledHeight > 0
            ? printableHeight / scaledHeight
            : 1.0;
        scaleRatio *= PageGeometryRules.ResolveUniformScale(widthFitScale, heightFitScale);
        return double.IsFinite(scaleRatio) && scaleRatio > 0 ? scaleRatio : 1.0;
    }

    public static PrintGridMeasurement ScaleMeasurement(PrintGridMeasurement measurement, double scaleRatio)
    {
        if (scaleRatio == 1.0)
            return measurement;

        return measurement with
        {
            HeaderWidth = measurement.HeaderWidth * scaleRatio,
            HeaderHeight = measurement.HeaderHeight * scaleRatio,
            ColumnWidth = measurement.ColumnWidth * scaleRatio,
            RowHeight = measurement.RowHeight * scaleRatio,
            ColumnOffsets = measurement.ColumnOffsets?.Select(offset => offset * scaleRatio).ToArray(),
            RowOffsets = measurement.RowOffsets?.Select(offset => offset * scaleRatio).ToArray(),
        };
    }

    public static IReadOnlyDictionary<uint, double> BuildColumnWidthsPixels(Sheet sheet)
    {
        var pixels = new Dictionary<uint, double>(sheet.ColumnWidths.Count);
        foreach (var (column, width) in sheet.ColumnWidths)
            pixels[column] = ColumnWidthPixelMapper.ColumnWidthToPixels(width);
        return pixels;
    }

    public static WorksheetPrintHeaderFooterVariant ResolveHeaderFooterVariant(Sheet sheet, int pageNumber)
    {
        ArgumentNullException.ThrowIfNull(sheet);

        if (sheet.DifferentFirstPageHeaderFooter && pageNumber == (sheet.FirstPageNumber ?? 1))
        {
            return new WorksheetPrintHeaderFooterVariant(
                sheet.FirstPageHeader,
                sheet.FirstPageFooter,
                sheet.FirstPageHeaderPictures,
                sheet.FirstPageFooterPictures);
        }

        if (sheet.DifferentOddEvenHeaderFooter && pageNumber % 2 == 0)
        {
            return new WorksheetPrintHeaderFooterVariant(
                sheet.EvenPageHeader,
                sheet.EvenPageFooter,
                sheet.EvenPageHeaderPictures,
                sheet.EvenPageFooterPictures);
        }

        return new WorksheetPrintHeaderFooterVariant(
            sheet.PageHeader,
            sheet.PageFooter,
            sheet.PageHeaderPictures,
            sheet.PageFooterPictures);
    }

    private static IReadOnlyDictionary<CellAddress, string> ResolveDisplayedCommentText(Sheet sheet)
    {
        if (sheet.ThreadedComments.Count == 0)
            return sheet.Comments;

        var result = new Dictionary<CellAddress, string>(sheet.Comments);
        foreach (var (address, comment) in sheet.ThreadedComments)
            result[address] = CommentNavigationPlanner.FormatThreadedComment(comment);
        return result;
    }

    private static IReadOnlyList<PageDisplayedCommentBlock> BuildDisplayedComments(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlySet<CellAddress> shownComments,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        PrintGridMeasurement measurement,
        double gridLeft,
        double gridTop,
        double pageWidth,
        double pageHeight)
    {
        var overlays = WorksheetPageLayout.GetDisplayedCommentOverlays(
            comments,
            EmptyThreadedComments,
            pageRows,
            pageColumns,
            shownComments);
        var result = new List<PageDisplayedCommentBlock>(overlays.Count);
        foreach (var overlay in overlays)
        {
            var columnWidth = measurement.ColumnWidthAt(overlay.ColumnIndex);
            var cellLeft = gridLeft + measurement.ColumnOffset(overlay.ColumnIndex);
            var cellTop = gridTop + measurement.RowOffset(overlay.RowIndex);
            var boxWidth = Math.Min(180, Math.Max(80, columnWidth * 2.2));
            const double boxHeight = 48.0;
            var boxLeft = Math.Min(pageWidth - boxWidth - 8, cellLeft + columnWidth + 4);
            var boxTop = Math.Min(pageHeight - boxHeight - 8, cellTop + 4);
            result.Add(new PageDisplayedCommentBlock(
                overlay.Kind,
                overlay.Text,
                [
                    new LayoutPoint(cellLeft + columnWidth - 7, cellTop),
                    new LayoutPoint(cellLeft + columnWidth, cellTop),
                    new LayoutPoint(cellLeft + columnWidth, cellTop + 7),
                ],
                new LayoutRect(Math.Max(8, boxLeft), Math.Max(8, boxTop), boxWidth, boxHeight)));
        }

        return result;
    }
}

public static class WorksheetPrintHeaderFooterGeometryPlanner
{
    private const double UnalignedMargin = 0.3 * 96.0;

    public static WorksheetPrintHeaderFooterBandGeometry BuildBand(
        WorksheetHeaderFooter value,
        WorksheetHeaderFooterPictureSet pictures,
        double pageWidth,
        double pageHeight,
        double marginLeft,
        double marginRight,
        double marginBottom,
        double bandMargin,
        bool alignWithMargins,
        bool isFooter,
        bool draftQuality,
        double fontScale,
        double baseLineHeight,
        bool sizeToContent)
    {
        var lineHeight = ResolveLineHeight(
            value,
            pictures,
            draftQuality,
            fontScale,
            baseLineHeight,
            sizeToContent);
        var y = isFooter
            ? Math.Max(
                Math.Max(4, pageHeight - bandMargin - lineHeight),
                pageHeight - PageGeometryRules.ResolveBodyEdge(marginBottom, bandMargin))
            : Math.Max(4, bandMargin - lineHeight);
        var leftInset = alignWithMargins ? marginLeft : UnalignedMargin;
        var rightInset = alignWithMargins ? marginRight : UnalignedMargin;
        return ResolveSectionBounds(
            pageWidth,
            leftInset,
            rightInset,
            y,
            lineHeight,
            baseLineHeight * fontScale);
    }

    public static WorksheetPrintHeaderFooterBandGeometry ResolveSectionBounds(
        double pageWidth,
        double leftInset,
        double rightInset,
        double y,
        double lineHeight,
        double textLineHeight)
    {
        var availableWidth = Math.Max(1, pageWidth - leftInset - rightInset);
        var sectionWidth = Math.Max(1, availableWidth / 3);
        return new WorksheetPrintHeaderFooterBandGeometry(
            new LayoutRect(leftInset, y, sectionWidth, lineHeight),
            new LayoutRect(leftInset + sectionWidth, y, sectionWidth, lineHeight),
            new LayoutRect(pageWidth - rightInset - sectionWidth, y, sectionWidth, lineHeight),
            textLineHeight);
    }

    public static double ResolveLineHeight(
        WorksheetHeaderFooter value,
        WorksheetHeaderFooterPictureSet pictures,
        bool draftQuality,
        double fontScale,
        double baseLineHeight,
        bool sizeToContent)
    {
        var normalizedBaseHeight = double.IsFinite(baseLineHeight) && baseLineHeight > 0
            ? baseLineHeight
            : 1.0;
        if (!sizeToContent || draftQuality)
            return normalizedBaseHeight;

        var maxLines = Math.Max(1, Math.Max(
            PagePrintTextPlanner.CountSectionLines(value.Left),
            Math.Max(
                PagePrintTextPlanner.CountSectionLines(value.Center),
                PagePrintTextPlanner.CountSectionLines(value.Right))));
        var height = normalizedBaseHeight * fontScale * maxLines;

        if (HasPictureToken(value.Left) && pictures.Left is { } left)
            height = Math.Max(height, Math.Max(1, left.Height));
        if (HasPictureToken(value.Center) && pictures.Center is { } center)
            height = Math.Max(height, Math.Max(1, center.Height));
        if (HasPictureToken(value.Right) && pictures.Right is { } right)
            height = Math.Max(height, Math.Max(1, right.Height));
        return height;
    }

    public static LayoutRect ResolvePictureBounds(
        WorksheetHeaderFooterPicture picture,
        LayoutRect section,
        PageTextAlignment alignment)
    {
        var width = Math.Min(Math.Max(1, picture.Width), section.Width);
        var height = Math.Min(Math.Max(1, picture.Height), section.Height);
        var left = alignment switch
        {
            PageTextAlignment.Center => section.Left + (section.Width - width) / 2,
            PageTextAlignment.Right => Math.Max(section.Left, section.Right - width - 2),
            _ => section.Left + 2,
        };
        return new LayoutRect(left, section.Top + (section.Height - height) / 2, width, height);
    }

    public static LayoutRect ResolveTextBounds(
        LayoutRect section,
        WorksheetHeaderFooterPicture? picture,
        PageTextAlignment alignment)
    {
        if (picture is null)
            return section;

        var pictureWidth = Math.Min(Math.Max(1, picture.Width), section.Width);
        const double gap = 4;
        return alignment switch
        {
            PageTextAlignment.Left => new LayoutRect(
                section.Left + pictureWidth + gap,
                section.Top,
                Math.Max(1, section.Width - pictureWidth - gap),
                section.Height),
            PageTextAlignment.Right => new LayoutRect(
                section.Left,
                section.Top,
                Math.Max(1, section.Width - pictureWidth - gap),
                section.Height),
            _ => section,
        };
    }

    public static bool HasPictureToken(string text) =>
        text.Contains("&[Picture]", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("&G", StringComparison.OrdinalIgnoreCase);
}

public static class WorksheetPrintHyperlinkPlanner
{
    public static IReadOnlyDictionary<(uint Row, uint Col), WorksheetPrintHyperlinkPlan> BuildPrintableHyperlinks(
        Workbook workbook,
        Sheet sheet)
    {
        var result = new Dictionary<(uint Row, uint Col), WorksheetPrintHyperlinkPlan>();
        foreach (var (address, target) in sheet.Hyperlinks)
        {
            if (address.Sheet != sheet.Id || string.IsNullOrWhiteSpace(target))
                continue;

            sheet.HyperlinkMetadata.TryGetValue(address, out var metadata);
            var targetKind = metadata?.LinkType ?? HyperlinkTargetKind.ExistingFileOrWebPage;
            CellAddress? targetAddress = null;
            if (targetKind == HyperlinkTargetKind.PlaceInThisDocument)
            {
                if (!TryResolveInternalDestination(workbook, sheet, target, metadata, out var resolved))
                    continue;
                targetAddress = resolved;
            }

            result[(address.Row, address.Col)] = new WorksheetPrintHyperlinkPlan(
                target,
                targetKind,
                address,
                targetAddress);
        }

        return result;
    }

    public static IReadOnlyDictionary<(uint Row, uint Col), CellAddress> BuildPrintableCellDestinations(
        Workbook workbook,
        Sheet destinationSheet)
    {
        var result = new Dictionary<(uint Row, uint Col), CellAddress>();
        foreach (var sourceSheet in workbook.Sheets)
        {
            foreach (var (address, target) in sourceSheet.Hyperlinks)
            {
                if (address.Sheet != sourceSheet.Id || string.IsNullOrWhiteSpace(target))
                    continue;

                sourceSheet.HyperlinkMetadata.TryGetValue(address, out var metadata);
                if ((metadata?.LinkType ?? HyperlinkTargetKind.ExistingFileOrWebPage) != HyperlinkTargetKind.PlaceInThisDocument ||
                    !TryResolveInternalDestination(workbook, sourceSheet, target, metadata, out var targetAddress) ||
                    targetAddress.Sheet != destinationSheet.Id)
                {
                    continue;
                }

                result[(targetAddress.Row, targetAddress.Col)] = targetAddress;
            }
        }

        return result;
    }

    private static bool TryResolveInternalDestination(
        Workbook workbook,
        Sheet sourceSheet,
        string target,
        HyperlinkMetadata? metadata,
        out CellAddress address)
    {
        address = default;
        var reference = !string.IsNullOrWhiteSpace(metadata?.Bookmark) ? metadata.Bookmark : target;
        reference = reference.Trim();
        if (reference.StartsWith("#", StringComparison.Ordinal))
            reference = reference[1..].Trim();
        if (reference.Length == 0)
            return false;

        if (!WorkbookRangeTextCodec.TryParse(
                sourceSheet.Id,
                reference,
                sheetName => ResolveSheetIdByName(workbook, sheetName),
                out var range) ||
            range.Start.Row != range.End.Row ||
            range.Start.Col != range.End.Col)
        {
            return false;
        }

        address = range.Start;
        return true;
    }

    private static SheetId? ResolveSheetIdByName(Workbook workbook, string sheetName) =>
        workbook.Sheets.FirstOrDefault(
            sheet => string.Equals(sheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))?.Id;
}

public static class WorksheetPrintCellGeometryPlanner
{
    public static double MeasureMergedColumnSpan(
        PrintGridMeasurement measurement,
        IReadOnlyList<uint> pageColumns,
        int columnIndex,
        uint mergeEndColumn)
    {
        var width = measurement.ColumnWidthAt(columnIndex);
        for (var index = columnIndex + 1;
             index < pageColumns.Count && pageColumns[index] <= mergeEndColumn;
             index++)
        {
            width += measurement.ColumnWidthAt(index);
        }

        return width;
    }

    public static double MeasureMergedRowSpan(
        PrintGridMeasurement measurement,
        IReadOnlyList<uint> pageRows,
        int rowIndex,
        uint mergeEndRow)
    {
        var height = measurement.RowHeightAt(rowIndex);
        for (var index = rowIndex + 1;
             index < pageRows.Count && pageRows[index] <= mergeEndRow;
             index++)
        {
            height += measurement.RowHeightAt(index);
        }

        return height;
    }

    public static double MeasureOverflowWidth(
        PrintGridMeasurement measurement,
        IReadOnlyList<uint> pageColumns,
        int columnIndex,
        uint row,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cells,
        Sheet? sheet,
        bool scanLeft)
    {
        var width = measurement.ColumnWidthAt(columnIndex);
        for (var index = columnIndex + (scanLeft ? -1 : 1);
             index >= 0 && index < pageColumns.Count;
             index += scanLeft ? -1 : 1)
        {
            var column = pageColumns[index];
            if (sheet?.GetMergeRegion(new CellAddress(sheet.Id, row, column)) is not null)
                break;
            if (cells.TryGetValue((row, column), out var cell) &&
                CellTextOverflowPlanner.IsOverflowOccupied(cell, editingCell: null, merge: null))
            {
                break;
            }

            width += measurement.ColumnWidthAt(index);
        }

        return width;
    }

    public static CellBorder ResolveNeighborBorder(
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cells,
        uint row,
        uint column,
        Func<CellStyle, CellBorder> selector) =>
        cells.TryGetValue((row, column), out var neighbor) && neighbor.Style is { } style
            ? selector(style)
            : default;

    public static CellBorder ResolveBorderWinner(CellBorder mine, CellBorder neighbor) =>
        CellBorderVisualPlanner.ResolveEdgeWinner(mine, neighbor);

    public static bool HasVisibleFill(CellStyle style) =>
        style.FillColor.HasValue ||
        style.FillPatternStyle != CellFillPatternStyle.None ||
        style.GradientFill is not null;
}
