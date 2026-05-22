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
public static class PrintRenderer
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

    private static (WorksheetHeaderFooter Header, WorksheetHeaderFooter Footer, WorksheetHeaderFooterPictureSet HeaderPictures, WorksheetHeaderFooterPictureSet FooterPictures) ResolveHeaderFooterForPage(
        Sheet sheet,
        int pageNumber)
    {
        if (sheet.DifferentFirstPageHeaderFooter && pageNumber == (sheet.FirstPageNumber ?? 1))
            return (sheet.FirstPageHeader, sheet.FirstPageFooter, sheet.FirstPageHeaderPictures, sheet.FirstPageFooterPictures);

        if (sheet.DifferentOddEvenHeaderFooter && pageNumber % 2 == 0)
            return (sheet.EvenPageHeader, sheet.EvenPageFooter, sheet.EvenPageHeaderPictures, sheet.EvenPageFooterPictures);

        return (sheet.PageHeader, sheet.PageFooter, sheet.PageHeaderPictures, sheet.PageFooterPictures);
    }

    private static DrawingVisual RenderPageVisual(
        double pageW,
        double pageH,
        double marginLeft,
        double marginRight,
        double marginTop,
        double headerMargin,
        double footerMargin,
        PrintGridMeasurement measurement,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        IReadOnlyDictionary<(uint Row, uint Col), DisplayCell> cellLookup,
        bool printGridlines,
        bool printHeadings,
        WorksheetHeaderFooter pageHeader,
        WorksheetHeaderFooter pageFooter,
        WorksheetHeaderFooterPictureSet pageHeaderPictures,
        WorksheetHeaderFooterPictureSet pageFooterPictures,
        string workbookName,
        string sheetName,
        bool alignHeaderFooterWithMargins,
        bool centerHorizontally,
        bool centerVertically,
        WorksheetPrintErrorValue printErrorValue,
        WorksheetPrintComments printComments,
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments,
        double printableW,
        double printableH,
        int pageNumber,
        int totalPages)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageW, pageH));
        DrawHeaderFooter(
            dc,
            pageW,
            pageH,
            marginLeft,
            marginRight,
            headerMargin,
            footerMargin,
            pageHeader,
            pageFooter,
            pageHeaderPictures,
            pageFooterPictures,
            workbookName,
            sheetName,
            alignHeaderFooterWithMargins,
            pageNumber,
            totalPages);

        var rowHeight = measurement.RowHeight;
        var colWidth = measurement.ColumnWidth;
        var printedWidth = measurement.HeaderWidth + colWidth * pageColumns.Count;
        var printedHeight = measurement.HeaderHeight + rowHeight * pageRows.Count;
        var xOffset = centerHorizontally ? Math.Max(0, (printableW - printedWidth) / 2) : 0;
        var yOffset = centerVertically ? Math.Max(0, (printableH - printedHeight) / 2) : 0;
        var contentLeft = marginLeft + xOffset;
        var contentTop = marginTop + yOffset;
        var gridLeft = contentLeft + measurement.HeaderWidth;
        var gridTop = contentTop + measurement.HeaderHeight;

        if (printHeadings)
            DrawPrintHeadings(dc, contentLeft, contentTop, measurement, pageRows, pageColumns);

        dc.DrawRectangle(null, new Pen(Brushes.Black, 0.5),
            new Rect(gridLeft, gridTop, colWidth * pageColumns.Count, rowHeight * pageRows.Count));

        for (var rowIndex = 0; rowIndex < pageRows.Count; rowIndex++)
        {
            var row = pageRows[rowIndex];
            for (var colIndex = 0; colIndex < pageColumns.Count; colIndex++)
            {
                var col = pageColumns[colIndex];
                double x = gridLeft + colIndex * colWidth;
                double y = gridTop + rowIndex * rowHeight;

                if (printGridlines)
                {
                    dc.DrawRectangle(null,
                        new Pen(Brushes.LightGray, 0.5),
                        new Rect(x, y, colWidth, rowHeight));
                }

                if (!cellLookup.TryGetValue((row, col), out var cell) ||
                    string.IsNullOrEmpty(cell.DisplayText))
                {
                    continue;
                }

                var displayText = FormatPrintedCellText(cell.DisplayText, printErrorValue);
                if (string.IsNullOrEmpty(displayText))
                    continue;

                var ft = new FormattedText(
                    displayText,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"),
                    PrintFontSize,
                    Brushes.Black,
                    1.0)
                {
                    MaxTextWidth = Math.Max(1, colWidth - 4),
                    MaxLineCount = 1,
                    Trimming = TextTrimming.CharacterEllipsis
                };

                dc.DrawText(ft, new Point(x + 2, y + (rowHeight - ft.Height) / 2));
            }
        }

        if (printComments == WorksheetPrintComments.AsDisplayed)
        {
            DrawDisplayedComments(
                dc,
                comments,
                threadedComments,
                pageRows,
                pageColumns,
                gridLeft,
                gridTop,
                colWidth,
                rowHeight,
                pageW,
                pageH);
        }

        return visual;
    }

    private static void DrawDisplayedComments(
        DrawingContext dc,
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        double gridLeft,
        double gridTop,
        double colWidth,
        double rowHeight,
        double pageW,
        double pageH)
    {
        var overlays = WorksheetPageLayout.GetDisplayedCommentOverlays(
            comments,
            threadedComments,
            pageRows,
            pageColumns);
        if (overlays.Count == 0)
            return;

        var fill = new SolidColorBrush(Color.FromRgb(255, 255, 225));
        var border = new Pen(new SolidColorBrush(Color.FromRgb(128, 128, 128)), 0.75);
        var indicator = new SolidColorBrush(Color.FromRgb(192, 0, 0));
        var typeface = new Typeface("Segoe UI");

        foreach (var overlay in overlays)
        {
            var cellLeft = gridLeft + overlay.ColumnIndex * colWidth;
            var cellTop = gridTop + overlay.RowIndex * rowHeight;
            var triangle = new StreamGeometry();
            using (var ctx = triangle.Open())
            {
                ctx.BeginFigure(new Point(cellLeft + colWidth - 7, cellTop), true, true);
                ctx.LineTo(new Point(cellLeft + colWidth, cellTop), true, false);
                ctx.LineTo(new Point(cellLeft + colWidth, cellTop + 7), true, false);
            }
            triangle.Freeze();
            dc.DrawGeometry(indicator, null, triangle);

            var boxWidth = Math.Min(180, Math.Max(80, colWidth * 2.2));
            var boxHeight = 48.0;
            var boxLeft = Math.Min(pageW - boxWidth - 8, cellLeft + colWidth + 4);
            var boxTop = Math.Min(pageH - boxHeight - 8, cellTop + 4);
            var rect = new Rect(Math.Max(8, boxLeft), Math.Max(8, boxTop), boxWidth, boxHeight);
            dc.DrawRectangle(fill, border, rect);
            DrawCommentText(
                dc,
                overlay.Text,
                new Point(rect.Left + 4, rect.Top + 4),
                typeface,
                PrintFontSize,
                FontWeights.Normal,
                rect.Width - 8);
        }
    }

    private static void DrawPrintHeadings(
        DrawingContext dc,
        double marginLeft,
        double marginTop,
        PrintGridMeasurement measurement,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns)
    {
        var headerBrush = new SolidColorBrush(Color.FromRgb(242, 242, 242));
        var headerPen = new Pen(Brushes.LightGray, 0.5);
        var typeface = new Typeface("Segoe UI");

        dc.DrawRectangle(headerBrush, headerPen,
            new Rect(marginLeft, marginTop, measurement.HeaderWidth, measurement.HeaderHeight));

        for (var colIndex = 0; colIndex < pageColumns.Count; colIndex++)
        {
            var rect = new Rect(
                marginLeft + measurement.HeaderWidth + colIndex * measurement.ColumnWidth,
                marginTop,
                measurement.ColumnWidth,
                measurement.HeaderHeight);
            dc.DrawRectangle(headerBrush, headerPen, rect);
            DrawCenteredText(dc, CellAddress.NumberToColumnName(pageColumns[colIndex]), rect, typeface);
        }

        for (var rowIndex = 0; rowIndex < pageRows.Count; rowIndex++)
        {
            var rect = new Rect(
                marginLeft,
                marginTop + measurement.HeaderHeight + rowIndex * measurement.RowHeight,
                measurement.HeaderWidth,
                measurement.RowHeight);
            dc.DrawRectangle(headerBrush, headerPen, rect);
            DrawCenteredText(dc, pageRows[rowIndex].ToString(CultureInfo.InvariantCulture), rect, typeface);
        }
    }

    private static void DrawCenteredText(DrawingContext dc, string text, Rect rect, Typeface typeface)
    {
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            PrintFontSize,
            Brushes.Black,
            1.0)
        {
            MaxTextWidth = Math.Max(1, rect.Width - 4),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center
        };

        dc.DrawText(ft, new Point(rect.Left + 2, rect.Top + (rect.Height - ft.Height) / 2));
    }

    private static string FormatPrintedCellText(string displayText, WorksheetPrintErrorValue printErrorValue)
    {
        if (!IsErrorDisplayText(displayText))
            return displayText;

        return printErrorValue switch
        {
            WorksheetPrintErrorValue.Blank => "",
            WorksheetPrintErrorValue.Dash => "--",
            WorksheetPrintErrorValue.NotAvailable => "#N/A",
            _ => displayText
        };
    }

    private static bool IsErrorDisplayText(string text) =>
        text is "#DIV/0!" or "#VALUE!" or "#REF!" or "#NAME?" or "#NULL!" or "#N/A" or "#NUM!";

    private static DrawingVisual RenderCommentSummaryPageVisual(
        double pageW,
        double pageH,
        double marginLeft,
        double marginTop,
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageW, pageH));

        var typeface = new Typeface("Segoe UI");
        DrawCommentText(dc, "Comments", new Point(marginLeft, marginTop), typeface, 14, FontWeights.SemiBold, pageW - marginLeft * 2);

        var y = marginTop + 34;
        foreach (var (address, comment) in GetPrintableComments(comments, threadedComments))
        {
            if (y > pageH - marginTop - 24)
                break;

            var line = $"{address.ToA1()}: {comment}";
            var height = DrawCommentText(dc, line, new Point(marginLeft, y), typeface, PrintFontSize, FontWeights.Normal, pageW - marginLeft * 2);
            y += Math.Max(18, height + 6);
        }

        return visual;
    }

    private static IEnumerable<KeyValuePair<CellAddress, string>> GetPrintableComments(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments)
    {
        return comments
            .Concat(threadedComments
                .Where(pair => !comments.ContainsKey(pair.Key))
                .Select(pair => new KeyValuePair<CellAddress, string>(pair.Key, pair.Value.Text)))
            .OrderBy(pair => pair.Key.Row)
            .ThenBy(pair => pair.Key.Col);
    }

    private static double DrawCommentText(
        DrawingContext dc,
        string text,
        Point point,
        Typeface typeface,
        double fontSize,
        FontWeight fontWeight,
        double maxWidth)
    {
        var weightedTypeface = new Typeface(typeface.FontFamily, typeface.Style, fontWeight, typeface.Stretch);
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            weightedTypeface,
            fontSize,
            Brushes.Black,
            1.0)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 3,
            Trimming = TextTrimming.CharacterEllipsis
        };

        dc.DrawText(ft, point);
        return ft.Height;
    }

    private static void DrawHeaderFooter(
        DrawingContext dc,
        double pageW,
        double pageH,
        double marginLeft,
        double marginRight,
        double headerMargin,
        double footerMargin,
        WorksheetHeaderFooter header,
        WorksheetHeaderFooter footer,
        WorksheetHeaderFooterPictureSet headerPictures,
        WorksheetHeaderFooterPictureSet footerPictures,
        string workbookName,
        string sheetName,
        bool alignWithMargins,
        int pageNumber,
        int totalPages)
    {
        var typeface = new Typeface("Segoe UI");
        var headerY = Math.Max(4, headerMargin - PrintFontSize);
        var footerY = Math.Max(4, pageH - footerMargin - PrintFontSize);
        var leftInset = alignWithMargins ? marginLeft : 0.3 * 96.0;
        var rightInset = alignWithMargins ? marginRight : 0.3 * 96.0;
        DrawHeaderFooterLine(dc, header, headerPictures, pageW, leftInset, rightInset, headerY, typeface, pageNumber, totalPages, workbookName, sheetName);
        DrawHeaderFooterLine(dc, footer, footerPictures, pageW, leftInset, rightInset, footerY, typeface, pageNumber, totalPages, workbookName, sheetName);
    }

    private static void DrawHeaderFooterLine(
        DrawingContext dc,
        WorksheetHeaderFooter value,
        WorksheetHeaderFooterPictureSet pictures,
        double pageW,
        double leftInset,
        double rightInset,
        double y,
        Typeface typeface,
        int pageNumber,
        int totalPages,
        string workbookName,
        string sheetName)
    {
        var left = ExpandHeaderFooterText(value.Left, pageNumber, totalPages, workbookName, sheetName, DateTime.Now);
        var center = ExpandHeaderFooterText(value.Center, pageNumber, totalPages, workbookName, sheetName, DateTime.Now);
        var right = ExpandHeaderFooterText(value.Right, pageNumber, totalPages, workbookName, sheetName, DateTime.Now);
        var availableWidth = Math.Max(1, pageW - leftInset - rightInset);
        var sectionWidth = Math.Max(1, availableWidth / 3);

        var leftRect = new Rect(leftInset, y, sectionWidth, 18);
        var centerRect = new Rect((pageW - sectionWidth) / 2, y, sectionWidth, 18);
        var rightRect = new Rect(pageW - rightInset - sectionWidth, y, sectionWidth, 18);

        DrawHeaderFooterPicture(dc, HasHeaderFooterPictureToken(value.Left) ? pictures.Left : null, leftRect, TextAlignment.Left);
        DrawHeaderFooterPicture(dc, HasHeaderFooterPictureToken(value.Center) ? pictures.Center : null, centerRect, TextAlignment.Center);
        DrawHeaderFooterPicture(dc, HasHeaderFooterPictureToken(value.Right) ? pictures.Right : null, rightRect, TextAlignment.Right);
        DrawHeaderFooterText(dc, left, leftRect, typeface, TextAlignment.Left);
        DrawHeaderFooterText(dc, center, centerRect, typeface, TextAlignment.Center);
        DrawHeaderFooterText(dc, right, rightRect, typeface, TextAlignment.Right);
    }

    private static void DrawHeaderFooterPicture(
        DrawingContext dc,
        WorksheetHeaderFooterPicture? picture,
        Rect sectionRect,
        TextAlignment alignment)
    {
        if (picture is null)
            return;

        using var stream = new MemoryStream(picture.ImageBytes);
        var image = BitmapFrame.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnLoad);
        var width = Math.Min(Math.Max(1, picture.Width), sectionRect.Width);
        var height = Math.Min(Math.Max(1, picture.Height), Math.Max(sectionRect.Height, picture.Height));
        var left = alignment switch
        {
            TextAlignment.Center => sectionRect.Left + (sectionRect.Width - width) / 2,
            TextAlignment.Right => sectionRect.Right - width - 2,
            _ => sectionRect.Left + 2
        };
        dc.DrawImage(image, new Rect(left, sectionRect.Top + (sectionRect.Height - Math.Min(height, sectionRect.Height)) / 2, width, height));
    }

    private static bool HasHeaderFooterPictureToken(string text) =>
        text.Contains("&[Picture]", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("&G", StringComparison.Ordinal);

    private static void DrawHeaderFooterText(
        DrawingContext dc,
        string text,
        Rect rect,
        Typeface typeface,
        TextAlignment alignment)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var ft = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            PrintFontSize,
            Brushes.Black,
            1.0)
        {
            MaxTextWidth = Math.Max(1, rect.Width - 4),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment
        };

        dc.DrawText(ft, new Point(rect.Left + 2, rect.Top + (rect.Height - ft.Height) / 2));
    }

    internal static string ExpandHeaderFooterText(
        string text,
        int pageNumber,
        int totalPages,
        string workbookName,
        string sheetName,
        DateTime now) =>
        text
            .Replace("&[Page]", pageNumber.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("&[Pages]", totalPages.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("&[Date]", now.ToString("d", CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("&[Time]", now.ToString("t", CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("&[File]", workbookName, StringComparison.OrdinalIgnoreCase)
            .Replace("&[Path]", workbookName, StringComparison.OrdinalIgnoreCase)
            .Replace("&[Tab]", sheetName, StringComparison.OrdinalIgnoreCase)
            .Replace("&[Picture]", "", StringComparison.OrdinalIgnoreCase)
            .Replace("&G", "", StringComparison.Ordinal)
            .Replace("&P", pageNumber.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("&N", totalPages.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

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
