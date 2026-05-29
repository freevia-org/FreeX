using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static partial class PrintRenderer
{
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
        WorksheetHeaderFooterPictureSet pictures,
        bool draftQuality = false)
    {
        var height = 18.0;
        if (draftQuality)
            return height;

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
}
