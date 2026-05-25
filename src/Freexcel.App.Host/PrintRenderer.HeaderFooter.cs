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

}
