using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        var headerHeight = CalculateHeaderFooterLineHeight(header, headerPictures);
        var footerHeight = CalculateHeaderFooterLineHeight(footer, footerPictures);
        var headerY = Math.Max(4, headerMargin - headerHeight);
        var footerY = Math.Max(4, pageH - footerMargin - footerHeight);
        var leftInset = alignWithMargins ? marginLeft : 0.3 * 96.0;
        var rightInset = alignWithMargins ? marginRight : 0.3 * 96.0;
        DrawHeaderFooterLine(dc, header, headerPictures, pageW, leftInset, rightInset, headerY, headerHeight, typeface, pageNumber, totalPages, workbookName, sheetName);
        DrawHeaderFooterLine(dc, footer, footerPictures, pageW, leftInset, rightInset, footerY, footerHeight, typeface, pageNumber, totalPages, workbookName, sheetName);
    }

    private static void DrawHeaderFooterLine(
        DrawingContext dc,
        WorksheetHeaderFooter value,
        WorksheetHeaderFooterPictureSet pictures,
        double pageW,
        double leftInset,
        double rightInset,
        double y,
        double lineHeight,
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

        var leftRect = new Rect(leftInset, y, sectionWidth, lineHeight);
        var centerRect = new Rect((pageW - sectionWidth) / 2, y, sectionWidth, lineHeight);
        var rightRect = new Rect(pageW - rightInset - sectionWidth, y, sectionWidth, lineHeight);

        var leftPicture = HasHeaderFooterPictureToken(value.Left) ? pictures.Left : null;
        var centerPicture = HasHeaderFooterPictureToken(value.Center) ? pictures.Center : null;
        var rightPicture = HasHeaderFooterPictureToken(value.Right) ? pictures.Right : null;

        DrawHeaderFooterPicture(dc, leftPicture, leftRect, TextAlignment.Left);
        DrawHeaderFooterPicture(dc, centerPicture, centerRect, TextAlignment.Center);
        DrawHeaderFooterPicture(dc, rightPicture, rightRect, TextAlignment.Right);
        DrawHeaderFooterText(dc, left, CalculateHeaderFooterTextRect(leftRect, leftPicture, TextAlignment.Left), typeface, TextAlignment.Left);
        DrawHeaderFooterText(dc, center, CalculateHeaderFooterTextRect(centerRect, centerPicture, TextAlignment.Center), typeface, TextAlignment.Center);
        DrawHeaderFooterText(dc, right, CalculateHeaderFooterTextRect(rightRect, rightPicture, TextAlignment.Right), typeface, TextAlignment.Right);
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
        dc.DrawImage(image, CalculateHeaderFooterPictureRect(picture, sectionRect, alignment));
    }

    internal static Rect CalculateHeaderFooterPictureRect(
        WorksheetHeaderFooterPicture picture,
        Rect sectionRect,
        TextAlignment alignment)
    {
        var width = Math.Min(Math.Max(1, picture.Width), sectionRect.Width);
        var height = Math.Min(Math.Max(1, picture.Height), sectionRect.Height);
        var left = alignment switch
        {
            TextAlignment.Center => sectionRect.Left + (sectionRect.Width - width) / 2,
            TextAlignment.Right => Math.Max(sectionRect.Left, sectionRect.Right - width - 2),
            _ => sectionRect.Left + 2
        };
        return new Rect(left, sectionRect.Top + (sectionRect.Height - height) / 2, width, height);
    }

    internal static Rect CalculateHeaderFooterTextRect(
        Rect sectionRect,
        WorksheetHeaderFooterPicture? picture,
        TextAlignment alignment)
    {
        if (picture is null)
            return sectionRect;

        var pictureWidth = Math.Min(Math.Max(1, picture.Width), sectionRect.Width);
        const double gap = 4;
        return alignment switch
        {
            TextAlignment.Left => new Rect(
                sectionRect.Left + pictureWidth + gap,
                sectionRect.Top,
                Math.Max(1, sectionRect.Width - pictureWidth - gap),
                sectionRect.Height),
            TextAlignment.Right => new Rect(
                sectionRect.Left,
                sectionRect.Top,
                Math.Max(1, sectionRect.Width - pictureWidth - gap),
                sectionRect.Height),
            _ => sectionRect
        };
    }

    internal static double CalculateHeaderFooterLineHeight(
        WorksheetHeaderFooter value,
        WorksheetHeaderFooterPictureSet pictures)
    {
        var height = 18.0;
        if (HasHeaderFooterPictureToken(value.Left) && pictures.Left is { } left)
            height = Math.Max(height, Math.Max(1, left.Height));
        if (HasHeaderFooterPictureToken(value.Center) && pictures.Center is { } center)
            height = Math.Max(height, Math.Max(1, center.Height));
        if (HasHeaderFooterPictureToken(value.Right) && pictures.Right is { } right)
            height = Math.Max(height, Math.Max(1, right.Height));
        return height;
    }

    private static bool HasHeaderFooterPictureToken(string text) =>
        text.Contains("&[Picture]", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("&G", StringComparison.OrdinalIgnoreCase);

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
            .Replace("&G", "", StringComparison.OrdinalIgnoreCase)
            .Replace("&P", pageNumber.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("&N", totalPages.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("&D", now.ToString("d", CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("&T", now.ToString("t", CultureInfo.CurrentCulture), StringComparison.OrdinalIgnoreCase)
            .Replace("&F", workbookName, StringComparison.OrdinalIgnoreCase)
            .Replace("&Z", workbookName, StringComparison.OrdinalIgnoreCase)
            .Replace("&A", sheetName, StringComparison.OrdinalIgnoreCase);
}
