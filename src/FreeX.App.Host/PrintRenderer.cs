using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host;

/// <summary>
/// Renders a worksheet as a WPF <see cref="FixedDocument"/> for printing or XPS export.
/// </summary>
public static partial class PrintRenderer
{
    private const double PrintFontSize = 9.0;

    public static FixedDocument RenderWorksheet(
        Workbook workbook,
        SheetId sheetId,
        IViewportService viewportService,
        GridRange? printRangeOverride = null,
        bool ignorePrintArea = false,
        double pageWidthInches = 8.27,
        double pageHeightInches = 11.69)
    {
        var doc = new FixedDocument();

        var sheet = workbook.GetSheet(sheetId);
        if (sheet == null) return doc;

        var initialMetrics = WorksheetPrintRenderPlanner.BuildMetrics(sheet);
        doc.DocumentPaginator.PageSize = new Size(initialMetrics.PageWidth, initialMetrics.PageHeight);

        if (!WorksheetPrintRenderPlanner.TryBuild(sheet, printRangeOverride, ignorePrintArea, out var printPlan))
            return doc;

        var pageW = printPlan.Metrics.PageWidth;
        var pageH = printPlan.Metrics.PageHeight;
        var marginLeft = printPlan.Metrics.MarginLeft;
        var marginRight = printPlan.Metrics.MarginRight;
        var marginTop = printPlan.Metrics.MarginTop;
        var headerMargin = printPlan.Metrics.HeaderMargin;
        var footerMargin = printPlan.Metrics.FooterMargin;

        var viewport = viewportService.GetViewport(workbook, sheetId,
            new ViewportRequest(
                TopRow: 1,
                LeftCol: 1,
                AvailableHeight: printPlan.Viewport.RequestHeight,
                AvailableWidth: printPlan.Viewport.RequestWidth));

        var cellLookup = viewport.Cells.ToDictionary(c => (c.Row, c.Col));
        var columnWidthsPixels = BuildColumnWidthsPixels(sheet);

        IReadOnlyList<PrintCommentSummaryPagePlan> commentSummaryPages =
            sheet.PrintComments == WorksheetPrintComments.AtEnd
                ? PrintCommentSummaryPlanner.BuildPages(sheet.Comments, sheet.ThreadedComments, pageH, marginTop)
                : [];
        var totalPages = printPlan.GridPageCount + commentSummaryPages.Count;
        var printableHyperlinks = BuildPrintableHyperlinkLookup(workbook, sheet);
        var printableCellDestinations = BuildPrintableCellDestinationLookup(workbook, sheet);

        foreach (var page in printPlan.Pages)
            AddPrintPage(page);

        if (commentSummaryPages.Count > 0)
        {
            foreach (var commentsForPage in commentSummaryPages)
                AddCommentSummaryPage(commentsForPage);
        }

        void AddPrintPage(WorksheetPrintPagePlan page)
        {
            var rowPlan = page.RowPlan;
            var columnPlan = page.ColumnPlan;
            var pageRows = page.Rows;
            var pageColumns = page.Columns;
            if (pageRows.Count == 0 || pageColumns.Count == 0)
                return;

            var measurement = PrintLayoutPlanner.MeasurePrintableGrid(
                printPlan.Metrics.PrintableWidth,
                printPlan.Metrics.PrintableHeight,
                pageRows,
                pageColumns,
                sheet.RowHeights,
                columnWidthsPixels,
                sheet.PrintHeadings);
            var pageNumber = page.PageNumber;
            var (pageHeader, pageFooter, pageHeaderPictures, pageFooterPictures) = ResolveHeaderFooterForPage(sheet, pageNumber);
            // Same effective scale percent (explicit Scale% or the ratio implied by Fit-to-pages) that
            // PagePaginationPlanner already used to decide this area's page capacity/count -- feeding it
            // through here keeps the drawn scale in lockstep with the portable/Skia PDF export path instead of
            // re-deriving an independent per-page ratio from each page's own geometry (P97).
            var configuredScalePercent = printPlan.AreaPlans[page.AreaIndex].Pagination.EffectiveScalePercent;
            var (visual, textOverlays, linkOverlays, cellDestinationOverlays) = RenderPageVisual(
                sheet,
                pageW,
                pageH,
                marginLeft,
                marginRight,
                marginTop,
                headerMargin,
                footerMargin,
                measurement,
                pageRows,
                pageColumns,
                cellLookup,
                printableHyperlinks,
                printableCellDestinations,
                sheet.PrintGridlines,
                sheet.PrintHeadings,
                pageHeader,
                pageFooter,
                pageHeaderPictures,
                pageFooterPictures,
                workbook.Name,
                sheet.Name,
                workbook.Theme,
                sheet.TextBoxes,
                sheet.Charts,
                viewport,
                rowPlan.BodyRows,
                columnPlan.BodyColumns,
                sheet.HeaderFooterAlignWithMargins,
                sheet.CenterHorizontallyOnPage,
                sheet.CenterVerticallyOnPage,
                sheet.PrintErrorValue,
                sheet.PrintComments,
                sheet.Comments,
                sheet.ThreadedComments,
                printPlan.Metrics.PrintableWidth,
                printPlan.Metrics.PrintableHeight,
                pageNumber,
                totalPages,
                sheet.PrintDraftQuality,
                sheet.PrintBlackAndWhite,
                configuredScalePercent);

            var container = new VisualHost
            {
                Visual = visual,
                TextOverlays = textOverlays,
                LinkOverlays = linkOverlays,
                CellDestinationOverlays = cellDestinationOverlays
            };
            var fixedPage = new FixedPage { Width = pageW, Height = pageH };
            fixedPage.Children.Add(container);
            FixedPage.SetLeft(container, 0);
            FixedPage.SetTop(container, 0);

            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(fixedPage);
            doc.Pages.Add(pageContent);
        }

        void AddCommentSummaryPage(PrintCommentSummaryPagePlan page)
        {
            var (visual, textOverlays) = RenderCommentSummaryPageVisual(
                pageW,
                pageH,
                marginLeft,
                marginTop,
                page.Entries);

            var container = new VisualHost { Visual = visual, TextOverlays = textOverlays };
            var fixedPage = new FixedPage { Width = pageW, Height = pageH };
            fixedPage.Children.Add(container);
            FixedPage.SetLeft(container, 0);
            FixedPage.SetTop(container, 0);

            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(fixedPage);
            doc.Pages.Add(pageContent);
        }

        return doc;
    }

    public static FixedDocument RenderWorkbook(Workbook workbook, IViewportService viewportService, bool ignorePrintAreas = false)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(viewportService);

        var result = new FixedDocument();
        foreach (var sheet in workbook.Sheets.Where(sheet => !sheet.IsHidden && !sheet.IsVeryHidden))
        {
            var sheetDocument = RenderWorksheet(workbook, sheet.Id, viewportService, ignorePrintArea: ignorePrintAreas);
            if (result.Pages.Count == 0)
                result.DocumentPaginator.PageSize = sheetDocument.DocumentPaginator.PageSize;

            foreach (var page in sheetDocument.Pages.ToList())
                result.Pages.Add(ClonePageAsBitmap(sheetDocument, page));
        }

        return result;
    }

    public static DocumentPaginator CreateWorkbookPaginator(Workbook workbook, IViewportService viewportService, bool ignorePrintAreas = false)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(viewportService);

        var paginators = workbook.Sheets
            .Where(sheet => !sheet.IsHidden && !sheet.IsVeryHidden)
            .Select(sheet => RenderWorksheet(workbook, sheet.Id, viewportService, ignorePrintArea: ignorePrintAreas).DocumentPaginator)
            .Where(paginator => paginator.PageCount > 0)
            .ToList();
        return new WorkbookDocumentPaginator(paginators);
    }

    private static PageContent ClonePageAsBitmap(FixedDocument document, PageContent pageContent)
    {
        pageContent.GetPageRoot(forceReload: false);
        var sourcePage = pageContent.Child ??
            throw new InvalidOperationException("FixedDocument page content did not contain a FixedPage.");
        var width = sourcePage.Width > 0 && !double.IsNaN(sourcePage.Width)
            ? sourcePage.Width
            : document.DocumentPaginator.PageSize.Width;
        var height = sourcePage.Height > 0 && !double.IsNaN(sourcePage.Height)
            ? sourcePage.Height
            : document.DocumentPaginator.PageSize.Height;
        var size = new Size(width, height);
        sourcePage.Measure(size);
        sourcePage.Arrange(new Rect(size));
        sourcePage.UpdateLayout();
        var textOverlays = PdfTextOverlayExtractor.Extract(sourcePage);
        var linkOverlays = PdfLinkOverlayExtractor.Extract(sourcePage);
        var cellDestinationOverlays = PdfCellDestinationOverlayExtractor.Extract(sourcePage);

        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(width)),
            Math.Max(1, (int)Math.Ceiling(height)),
            96,
            96,
            PixelFormats.Pbgra32);
        bitmap.Render(sourcePage);
        bitmap.Freeze();

        var fixedPage = new FixedPage { Width = width, Height = height };
        fixedPage.Children.Add(new Image
        {
            Source = bitmap,
            Width = width,
            Height = height
        });
        if (textOverlays.Count > 0 || linkOverlays.Count > 0 || cellDestinationOverlays.Count > 0)
        {
            fixedPage.Children.Add(new VisualHost
            {
                TextOverlays = textOverlays,
                LinkOverlays = linkOverlays,
                CellDestinationOverlays = cellDestinationOverlays
            });
        }

        var clone = new PageContent();
        ((IAddChild)clone).AddChild(fixedPage);
        return clone;
    }

    /// <summary>
    /// Converts the sheet's character-unit column widths to pixels (matching
    /// <see cref="PagePaginationPlanner.AverageColumnWidthPixels"/>'s per-column conversion), so
    /// <see cref="PrintLayoutPlanner.MeasurePrintableGrid(double, double, IReadOnlyList{uint}, IReadOnlyList{uint}, IReadOnlyDictionary{uint, double}, IReadOnlyDictionary{uint, double}, bool)"/>
    /// can measure each printed page from the sheet's real per-column pixel sizes.
    /// </summary>
    private static IReadOnlyDictionary<uint, double> BuildColumnWidthsPixels(Sheet sheet)
    {
        var pixels = new Dictionary<uint, double>(sheet.ColumnWidths.Count);
        foreach (var (col, width) in sheet.ColumnWidths)
            pixels[col] = ColumnWidthPixelMapper.ColumnWidthToPixels(width);

        return pixels;
    }

    private sealed record PdfLinkTarget(
        string Target,
        HyperlinkTargetKind TargetKind,
        CellAddress SourceAddress,
        CellAddress? TargetAddress);

    private static IReadOnlyDictionary<(uint Row, uint Col), PdfLinkTarget> BuildPrintableHyperlinkLookup(Workbook workbook, Sheet sheet)
    {
        if (sheet.Hyperlinks.Count == 0)
            return new Dictionary<(uint Row, uint Col), PdfLinkTarget>();

        var result = new Dictionary<(uint Row, uint Col), PdfLinkTarget>();
        foreach (var (address, target) in sheet.Hyperlinks)
        {
            if (address.Sheet != sheet.Id || string.IsNullOrWhiteSpace(target))
                continue;
            sheet.HyperlinkMetadata.TryGetValue(address, out var metadata);
            var targetKind = metadata?.LinkType ?? HyperlinkTargetKind.ExistingFileOrWebPage;
            if (targetKind == HyperlinkTargetKind.PlaceInThisDocument)
            {
                if (!TryResolveInternalHyperlinkDestination(workbook, sheet, target, metadata, out var targetAddress))
                    continue;

                result[(address.Row, address.Col)] = new PdfLinkTarget(target, targetKind, address, targetAddress);
                continue;
            }

            result[(address.Row, address.Col)] = new PdfLinkTarget(target, targetKind, address, null);
        }

        return result;
    }

    private static IReadOnlyDictionary<(uint Row, uint Col), CellAddress> BuildPrintableCellDestinationLookup(Workbook workbook, Sheet destinationSheet)
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
                    !TryResolveInternalHyperlinkDestination(workbook, sourceSheet, target, metadata, out var targetAddress) ||
                    targetAddress.Sheet != destinationSheet.Id)
                {
                    continue;
                }

                result[(targetAddress.Row, targetAddress.Col)] = targetAddress;
            }
        }

        return result;
    }

    private static bool TryResolveInternalHyperlinkDestination(
        Workbook workbook,
        Sheet sourceSheet,
        string target,
        HyperlinkMetadata? metadata,
        out CellAddress address)
    {
        address = default;
        var reference = !string.IsNullOrWhiteSpace(metadata?.Bookmark)
            ? metadata.Bookmark
            : target;
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

    private static SheetId? ResolveSheetIdByName(Workbook workbook, string sheetName)
    {
        foreach (var sheet in workbook.Sheets)
        {
            if (string.Equals(sheet.Name, sheetName, StringComparison.OrdinalIgnoreCase))
                return sheet.Id;
        }

        return null;
    }
}
