using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static partial class PrintRenderer
{
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
