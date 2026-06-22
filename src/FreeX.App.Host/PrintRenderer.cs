using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.App.Presentation.PageLayout;
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
        const double dpi = PagePaginationPlanner.Dpi;
        double pageW = pageWidthInches * dpi;
        double pageH = pageHeightInches * dpi;
        var doc = new FixedDocument();

        var sheet = workbook.GetSheet(sheetId);
        if (sheet == null) return doc;

        var pageSize = WorksheetPageLayout.GetPageSizeInches(sheet.PaperSize, sheet.PageOrientation);
        pageW = pageSize.Width * dpi;
        pageH = pageSize.Height * dpi;

        var margins = sheet.PageMargins;
        double marginLeft = margins.Left * dpi;
        double marginRight = margins.Right * dpi;
        double marginTop = margins.Top * dpi;
        double marginBottom = margins.Bottom * dpi;
        double headerMargin = sheet.HeaderMargin * dpi;
        double footerMargin = sheet.FooterMargin * dpi;

        doc.DocumentPaginator.PageSize = new Size(pageW, pageH);

        var usedRange = printRangeOverride is { } range &&
                        range.Start.Sheet == sheetId &&
                        range.End.Sheet == sheetId
            ? range
            : ignorePrintArea
                ? sheet.GetUsedRange()
                : sheet.PrintArea ?? sheet.GetUsedRange();
        if (usedRange == null) return doc;

        uint endPrintRow = usedRange.Value.End.Row;
        uint endPrintCol = usedRange.Value.End.Col;
        var maxViewportRow = Math.Max(endPrintRow, sheet.PrintTitleRows?.End ?? 0);
        var maxViewportCol = Math.Max(endPrintCol, sheet.PrintTitleColumns?.End ?? 0);

        double printableW = pageW - marginLeft - marginRight;
        double printableH = pageH - marginTop - marginBottom;

        var viewport = viewportService.GetViewport(workbook, sheetId,
            new ViewportRequest(
                TopRow: 1,
                LeftCol: 1,
                AvailableHeight: (double)maxViewportRow * 9999,
                AvailableWidth: (double)maxViewportCol * 9999));

        var cellLookup = viewport.Cells.ToDictionary(c => (c.Row, c.Col));
        var paginationPlan = PagePaginationPlanner.BuildPlan(
            usedRange.Value,
            sheet.ScaleToFit,
            sheet.PrintTitleRows,
            sheet.PrintTitleColumns,
            sheet.PaperSize,
            sheet.PageOrientation,
            sheet.PageMargins,
            sheet.RowPageBreaks,
            sheet.ColumnPageBreaks);
        var rowPlans = paginationPlan.RowPlans;
        var columnPlans = paginationPlan.ColumnPlans;
        IReadOnlyList<IReadOnlyList<KeyValuePair<CellAddress, string>>> commentSummaryPages =
            sheet.PrintComments == WorksheetPrintComments.AtEnd
                ? BuildCommentSummaryPages(sheet.Comments, sheet.ThreadedComments, pageH, marginTop)
                : [];
        var totalPages = rowPlans.Count * columnPlans.Count + commentSummaryPages.Count;
        var nextPageNumber = sheet.FirstPageNumber ?? 1;
        var printableHyperlinks = BuildPrintableHyperlinkLookup(workbook, sheet);
        var printableCellDestinations = BuildPrintableCellDestinationLookup(workbook, sheet);

        foreach (var page in PrintPageGridPlanner.Build(rowPlans, columnPlans, sheet.PageOrder))
            AddPrintPage(page);

        if (commentSummaryPages.Count > 0)
        {
            foreach (var commentsForPage in commentSummaryPages)
                AddCommentSummaryPage(commentsForPage);
        }

        void AddPrintPage(PrintPageGridEntry page)
        {
            var rowPlan = page.RowPlan;
            var columnPlan = page.ColumnPlan;
            var pageRows = rowPlan.TitleRows.Concat(rowPlan.BodyRows).ToList();
            var pageColumns = columnPlan.TitleColumns.Concat(columnPlan.BodyColumns).ToList();
            if (pageRows.Count == 0 || pageColumns.Count == 0)
                return;

            var pageNumber = nextPageNumber++;
            var measurement = PrintLayoutPlanner.MeasurePrintableGrid(
                printableW,
                printableH,
                (uint)pageRows.Count,
                (uint)pageColumns.Count,
                sheet.PrintHeadings);
            var (pageHeader, pageFooter, pageHeaderPictures, pageFooterPictures) = ResolveHeaderFooterForPage(sheet, pageNumber);
            var (visual, textOverlays, linkOverlays, cellDestinationOverlays) = RenderPageVisual(
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
                printableW,
                printableH,
                pageNumber,
                totalPages,
                sheet.PrintDraftQuality,
                sheet.PrintBlackAndWhite);

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

        void AddCommentSummaryPage(IReadOnlyList<KeyValuePair<CellAddress, string>> commentsForPage)
        {
            var (visual, textOverlays) = RenderCommentSummaryPageVisual(
                pageW,
                pageH,
                marginLeft,
                marginTop,
                commentsForPage);

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
