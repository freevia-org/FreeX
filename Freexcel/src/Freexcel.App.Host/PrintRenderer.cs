using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

/// <summary>
/// Renders a worksheet as a WPF <see cref="FixedDocument"/> for printing or XPS export.
/// </summary>
public static partial class PrintRenderer
{
    private const double PrintFontSize = 9.0;
    private const double MinimumPrintColumnWidth = 40.0;

    public static FixedDocument RenderWorksheet(
        Workbook workbook,
        SheetId sheetId,
        IViewportService viewportService,
        GridRange? printRangeOverride = null,
        bool ignorePrintArea = false,
        double pageWidthInches = 8.27,
        double pageHeightInches = 11.69)
    {
        const double dpi = 96.0;
        double pageW = pageWidthInches * dpi;
        double pageH = pageHeightInches * dpi;
        var doc = new FixedDocument();

        var sheet = workbook.GetSheet(sheetId);
        if (sheet == null) return doc;

        (pageW, pageH) = GetPaperSizeInches(sheet.PaperSize);
        pageW *= dpi;
        pageH *= dpi;
        if (sheet.PageOrientation == WorksheetPageOrientation.Landscape)
            (pageW, pageH) = (pageH, pageW);

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
        double rowHeight = 20.0;

        uint rowsPerPage = (uint)Math.Floor(printableH / rowHeight);
        if (rowsPerPage < 1) rowsPerPage = 1;
        uint columnsPerPage = (uint)Math.Floor(printableW / MinimumPrintColumnWidth);
        if (columnsPerPage < 1) columnsPerPage = 1;

        var rowPlans = PrintLayoutPlanner.BuildRowPlans(usedRange.Value, sheet.PrintTitleRows, rowsPerPage);
        var columnPlans = PrintLayoutPlanner.BuildColumnPlans(usedRange.Value, sheet.PrintTitleColumns, columnsPerPage);
        var totalPages = rowPlans.Count * columnPlans.Count;
        var pageNumber = sheet.FirstPageNumber ?? 1;

        if (sheet.PageOrder == WorksheetPageOrder.OverThenDown)
        {
            foreach (var rowPlan in rowPlans)
            {
                foreach (var columnPlan in columnPlans)
                    AddPrintPage(rowPlan, columnPlan);
            }
        }
        else
        {
            foreach (var columnPlan in columnPlans)
            {
                foreach (var rowPlan in rowPlans)
                    AddPrintPage(rowPlan, columnPlan);
            }
        }

        if (sheet.PrintComments == WorksheetPrintComments.AtEnd &&
            (sheet.Comments.Count > 0 || sheet.ThreadedComments.Count > 0))
            AddCommentSummaryPage();

        void AddPrintPage(PrintPageRowPlan rowPlan, PrintPageColumnPlan columnPlan)
        {
            var pageRows = rowPlan.TitleRows.Concat(rowPlan.BodyRows).ToList();
            var pageColumns = columnPlan.TitleColumns.Concat(columnPlan.BodyColumns).ToList();
            if (pageRows.Count == 0 || pageColumns.Count == 0)
                return;

            var measurement = PrintLayoutPlanner.MeasurePrintableGrid(
                printableW,
                printableH,
                (uint)pageRows.Count,
                (uint)pageColumns.Count,
                sheet.PrintHeadings);
            var (pageHeader, pageFooter, pageHeaderPictures, pageFooterPictures) = ResolveHeaderFooterForPage(sheet, pageNumber);
            var visual = RenderPageVisual(
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
                sheet.PrintGridlines,
                sheet.PrintHeadings,
                pageHeader,
                pageFooter,
                pageHeaderPictures,
                pageFooterPictures,
                workbook.Name,
                sheet.Name,
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
                    totalPages);
            pageNumber++;

            var container = new VisualHost { Visual = visual };
            var fixedPage = new FixedPage { Width = pageW, Height = pageH };
            fixedPage.Children.Add(container);
            FixedPage.SetLeft(container, 0);
            FixedPage.SetTop(container, 0);

            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(fixedPage);
            doc.Pages.Add(pageContent);
        }

        void AddCommentSummaryPage()
        {
            var visual = RenderCommentSummaryPageVisual(
                pageW,
                pageH,
                marginLeft,
                marginTop,
                sheet.Comments,
                sheet.ThreadedComments);

            var container = new VisualHost { Visual = visual };
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

        var clone = new PageContent();
        ((IAddChild)clone).AddChild(fixedPage);
        return clone;
    }

    private sealed class WorkbookDocumentPaginator : DocumentPaginator
    {
        private readonly IReadOnlyList<DocumentPaginator> _paginators;
        private Size _pageSize;

        public WorkbookDocumentPaginator(IReadOnlyList<DocumentPaginator> paginators)
        {
            _paginators = paginators;
            _pageSize = paginators.FirstOrDefault()?.PageSize ?? new Size(8.27 * 96.0, 11.69 * 96.0);
        }

        public override bool IsPageCountValid => _paginators.All(paginator => paginator.IsPageCountValid);

        public override int PageCount => _paginators.Sum(paginator => paginator.PageCount);

        public override Size PageSize
        {
            get => _pageSize;
            set => _pageSize = value;
        }

        public override IDocumentPaginatorSource? Source => null;

        public override DocumentPage GetPage(int pageNumber)
        {
            if (pageNumber < 0)
                throw new ArgumentOutOfRangeException(nameof(pageNumber));

            var offset = pageNumber;
            foreach (var paginator in _paginators)
            {
                if (offset < paginator.PageCount)
                    return paginator.GetPage(offset);

                offset -= paginator.PageCount;
            }

            throw new ArgumentOutOfRangeException(nameof(pageNumber));
        }
    }

    private static (double Width, double Height) GetPaperSizeInches(WorksheetPaperSize paperSize) =>
        paperSize switch
        {
            WorksheetPaperSize.Letter => (8.5, 11.0),
            WorksheetPaperSize.Legal => (8.5, 14.0),
            _ => (8.27, 11.69)
        };
}

/// <summary>
/// Minimal UIElement wrapper that hosts an arbitrary <see cref="DrawingVisual"/>
/// inside a <see cref="FixedPage"/>.
/// </summary>
internal sealed class VisualHost : UIElement
{
    public Visual? Visual { get; init; }

    protected override int VisualChildrenCount => Visual != null ? 1 : 0;

    protected override Visual GetVisualChild(int index)
    {
        if (index != 0 || Visual == null)
            throw new ArgumentOutOfRangeException(nameof(index));
        return Visual;
    }

    protected override void OnRender(DrawingContext drawingContext) { }
}
