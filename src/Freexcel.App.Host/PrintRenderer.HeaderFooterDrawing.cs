using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static partial class PrintRenderer
{
    private static void DrawHeaderFooter(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
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
        DrawHeaderFooterLine(dc, textOverlays, header, headerPictures, pageW, leftInset, rightInset, headerY, headerHeight, typeface, pageNumber, totalPages, workbookName, sheetName);
        DrawHeaderFooterLine(dc, textOverlays, footer, footerPictures, pageW, leftInset, rightInset, footerY, footerHeight, typeface, pageNumber, totalPages, workbookName, sheetName);
    }

    private static void DrawHeaderFooterLine(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
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
        DrawHeaderFooterText(dc, textOverlays, left, CalculateHeaderFooterTextRect(leftRect, leftPicture, TextAlignment.Left), typeface, TextAlignment.Left);
        DrawHeaderFooterText(dc, textOverlays, center, CalculateHeaderFooterTextRect(centerRect, centerPicture, TextAlignment.Center), typeface, TextAlignment.Center);
        DrawHeaderFooterText(dc, textOverlays, right, CalculateHeaderFooterTextRect(rightRect, rightPicture, TextAlignment.Right), typeface, TextAlignment.Right);
    }

    private static void DrawHeaderFooterText(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        string text,
        Rect rect,
        Typeface typeface,
        TextAlignment alignment)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var maxTextWidth = Math.Max(1, rect.Width - 4);
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            PrintFontSize,
            Brushes.Black,
            1.0)
        {
            MaxTextWidth = maxTextWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment
        };

        var drawPoint = new Point(rect.Left + 2, rect.Top + (rect.Height - ft.Height) / 2);
        dc.DrawText(ft, drawPoint);
        textOverlays.Add(new PdfTextOverlay(
            text,
            ResolveAlignedTextOverlayX(drawPoint.X, maxTextWidth, ft.WidthIncludingTrailingWhitespace, alignment),
            drawPoint.Y,
            PrintFontSize,
            "Segoe UI",
            Bold: false,
            Italic: false,
            Colors.Black));
    }

    private static double ResolveAlignedTextOverlayX(
        double drawX,
        double maxTextWidth,
        double textWidth,
        TextAlignment alignment)
    {
        var boundedTextWidth = Math.Min(textWidth, maxTextWidth);
        return alignment switch
        {
            TextAlignment.Center => drawX + Math.Max(0, (maxTextWidth - boundedTextWidth) / 2),
            TextAlignment.Right => drawX + Math.Max(0, maxTextWidth - boundedTextWidth),
            _ => drawX
        };
    }
}
