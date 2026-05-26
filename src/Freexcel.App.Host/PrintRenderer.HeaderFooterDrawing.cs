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
        int totalPages,
        bool draftQuality)
    {
        var typeface = new Typeface("Segoe UI");
        var headerHeight = CalculateHeaderFooterLineHeight(header, headerPictures, draftQuality);
        var footerHeight = CalculateHeaderFooterLineHeight(footer, footerPictures, draftQuality);
        var headerY = Math.Max(4, headerMargin - headerHeight);
        var footerY = Math.Max(4, pageH - footerMargin - footerHeight);
        var leftInset = alignWithMargins ? marginLeft : 0.3 * 96.0;
        var rightInset = alignWithMargins ? marginRight : 0.3 * 96.0;
        DrawHeaderFooterLine(dc, textOverlays, header, headerPictures, pageW, leftInset, rightInset, headerY, headerHeight, typeface, pageNumber, totalPages, workbookName, sheetName, draftQuality);
        DrawHeaderFooterLine(dc, textOverlays, footer, footerPictures, pageW, leftInset, rightInset, footerY, footerHeight, typeface, pageNumber, totalPages, workbookName, sheetName, draftQuality);
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
        string sheetName,
        bool draftQuality)
    {
        var left = ExpandHeaderFooterText(value.Left, pageNumber, totalPages, workbookName, sheetName, DateTime.Now);
        var center = ExpandHeaderFooterText(value.Center, pageNumber, totalPages, workbookName, sheetName, DateTime.Now);
        var right = ExpandHeaderFooterText(value.Right, pageNumber, totalPages, workbookName, sheetName, DateTime.Now);
        var availableWidth = Math.Max(1, pageW - leftInset - rightInset);
        var sectionWidth = Math.Max(1, availableWidth / 3);

        var leftRect = new Rect(leftInset, y, sectionWidth, lineHeight);
        var centerRect = new Rect((pageW - sectionWidth) / 2, y, sectionWidth, lineHeight);
        var rightRect = new Rect(pageW - rightInset - sectionWidth, y, sectionWidth, lineHeight);

        var leftPicture = !draftQuality && HasHeaderFooterPictureToken(value.Left) ? pictures.Left : null;
        var centerPicture = !draftQuality && HasHeaderFooterPictureToken(value.Center) ? pictures.Center : null;
        var rightPicture = !draftQuality && HasHeaderFooterPictureToken(value.Right) ? pictures.Right : null;

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

        var textPoint = new Point(rect.Left + 2, rect.Top + (rect.Height - ft.Height) / 2);
        dc.DrawText(ft, textPoint);
        AddHeaderFooterTextOverlay(textOverlays, text, textPoint, ft.MaxTextWidth, typeface, alignment);
    }

    private static void AddHeaderFooterTextOverlay(
        ICollection<PdfTextOverlay> textOverlays,
        string text,
        Point textPoint,
        double maxTextWidth,
        Typeface typeface,
        TextAlignment alignment)
    {
        var overlayText = BoundPrintedSingleLineOverlayText(text, maxTextWidth, typeface);
        if (string.IsNullOrEmpty(overlayText))
            return;

        var overlayX = textPoint.X;
        var overlayWidth = MeasurePrintedSingleLineText(overlayText, typeface).WidthIncludingTrailingWhitespace;
        if (alignment == TextAlignment.Center)
            overlayX += Math.Max(0, (maxTextWidth - overlayWidth) / 2);
        else if (alignment == TextAlignment.Right)
            overlayX += Math.Max(0, maxTextWidth - overlayWidth);

        textOverlays.Add(new PdfTextOverlay(
            overlayText,
            overlayX,
            textPoint.Y,
            PrintFontSize,
            typeface.FontFamily.Source,
            typeface.Weight >= FontWeights.SemiBold,
            typeface.Style == FontStyles.Italic || typeface.Style == FontStyles.Oblique,
            Colors.Black));
    }

}
