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
        double pageHeightInches = 11.69,
        string workbookDirectory = "",
        int pageNumberOffset = 0,
        int? totalPageCountOverride = null)
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
        var marginTop = printPlan.Metrics.MarginTop;

        var viewport = viewportService.GetViewport(workbook, sheetId,
            new ViewportRequest(
                TopRow: 1,
                LeftCol: 1,
                AvailableHeight: printPlan.Viewport.RequestHeight,
                AvailableWidth: printPlan.Viewport.RequestWidth));

        var cellLookup = viewport.Cells.ToDictionary(c => (c.Row, c.Col));
        var commentSummaryPages = WorksheetPrintPageContentPlanner.BuildCommentSummaryPages(sheet, printPlan);
        // For a single-sheet render this is just this sheet's own page count. RenderWorkbook/
        // CreateWorkbookPaginator (Entire Workbook printing) override this with the combined
        // page count across every printed sheet so &N is the whole print job's total instead of
        // restarting at each sheet's own count (R60-services-print-preview-6-1).
        var totalPages = totalPageCountOverride ?? (printPlan.GridPageCount + commentSummaryPages.Count);
        foreach (var page in printPlan.Pages)
            AddPrintPage(page);

        if (commentSummaryPages.Count > 0)
        {
            foreach (var commentsForPage in commentSummaryPages)
                AddCommentSummaryPage(commentsForPage);
        }

        void AddPrintPage(WorksheetPrintPagePlan page)
        {
            var contentPlan = WorksheetPrintPageContentPlanner.Build(
                workbook,
                sheet,
                printPlan,
                page,
                PrintTextMeasurer,
                WorksheetPrintMaterializationProfile.WpfNative,
                workbookDirectory: workbookDirectory,
                pageNumberOffset: pageNumberOffset,
                totalPageCountOverride: totalPages);
            if (contentPlan is null)
                return;

            var (visual, textOverlays, linkOverlays, cellDestinationOverlays) = RenderPageVisual(
                workbook,
                sheet,
                contentPlan,
                cellLookup,
                viewport,
                workbookDirectory);

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

    public static FixedDocument RenderWorkbook(
        Workbook workbook,
        IViewportService viewportService,
        bool ignorePrintAreas = false,
        string workbookDirectory = "")
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(viewportService);

        var visibleSheets = workbook.Sheets.Where(sheet => !sheet.IsHidden && !sheet.IsVeryHidden).ToList();
        // Excel's default "Auto" First page number keeps &P/&N continuous across the whole Entire
        // Workbook print job instead of restarting at each sheet -- pre-compute every visible
        // sheet's own page count once so each sheet can be rendered with a running offset and the
        // combined grand total (R60-services-print-preview-6-1).
        var sheetPageCounts = visibleSheets.Select(sheet => ComputeSheetTotalPageCount(sheet, ignorePrintAreas)).ToList();
        var grandTotalPages = sheetPageCounts.Sum();

        var result = new FixedDocument();
        var pageNumberOffset = 0;
        for (var sheetIndex = 0; sheetIndex < visibleSheets.Count; sheetIndex++)
        {
            var sheet = visibleSheets[sheetIndex];
            var sheetDocument = RenderWorksheet(
                workbook,
                sheet.Id,
                viewportService,
                ignorePrintArea: ignorePrintAreas,
                workbookDirectory: workbookDirectory,
                pageNumberOffset: pageNumberOffset,
                totalPageCountOverride: grandTotalPages);
            if (result.Pages.Count == 0)
                result.DocumentPaginator.PageSize = sheetDocument.DocumentPaginator.PageSize;

            foreach (var page in sheetDocument.Pages.ToList())
                result.Pages.Add(ClonePageAsBitmap(sheetDocument, page));

            pageNumberOffset += sheetPageCounts[sheetIndex];
        }

        return result;
    }

    public static DocumentPaginator CreateWorkbookPaginator(
        Workbook workbook,
        IViewportService viewportService,
        bool ignorePrintAreas = false,
        string workbookDirectory = "")
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(viewportService);

        var visibleSheets = workbook.Sheets.Where(sheet => !sheet.IsHidden && !sheet.IsVeryHidden).ToList();
        var sheetPageCounts = visibleSheets.Select(sheet => ComputeSheetTotalPageCount(sheet, ignorePrintAreas)).ToList();
        var grandTotalPages = sheetPageCounts.Sum();

        var paginators = new List<DocumentPaginator>();
        var pageNumberOffset = 0;
        for (var sheetIndex = 0; sheetIndex < visibleSheets.Count; sheetIndex++)
        {
            var sheet = visibleSheets[sheetIndex];
            var paginator = RenderWorksheet(
                workbook,
                sheet.Id,
                viewportService,
                ignorePrintArea: ignorePrintAreas,
                workbookDirectory: workbookDirectory,
                pageNumberOffset: pageNumberOffset,
                totalPageCountOverride: grandTotalPages).DocumentPaginator;
            if (paginator.PageCount > 0)
                paginators.Add(paginator);

            pageNumberOffset += sheetPageCounts[sheetIndex];
        }

        return new WorkbookDocumentPaginator(paginators);
    }

    /// <summary>
    /// Computes a sheet's own printed page count (grid pages + any "Comments: At end of sheet"
    /// appendix pages) without doing the full visual render -- used by <see cref="RenderWorkbook"/>
    /// and <see cref="CreateWorkbookPaginator"/> to pre-derive each sheet's contribution to the
    /// Entire Workbook print job's running page-number offset and combined total before any sheet
    /// is actually rendered (a sheet's own footer needs to know the totals from sheets printed
    /// AFTER it too).
    /// </summary>
    private static int ComputeSheetTotalPageCount(Sheet sheet, bool ignorePrintArea)
    {
        if (!WorksheetPrintRenderPlanner.TryBuild(sheet, printRangeOverride: null, ignorePrintArea, out var printPlan))
            return 0;

        return WorksheetPrintPageContentPlanner.ComputeTotalPageCount(sheet, printPlan);
    }

    internal static PageContent ClonePageAsBitmap(FixedDocument document, PageContent pageContent)
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
}
