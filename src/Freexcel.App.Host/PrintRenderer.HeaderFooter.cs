using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static partial class PrintRenderer
{
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

}
