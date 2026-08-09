using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FreeX.App.Presentation.PageLayout;
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
        var bounds = WorksheetPrintHeaderFooterGeometryPlanner.ResolvePictureBounds(
            picture,
            ToLayoutRect(sectionRect),
            ToPageAlignment(alignment));
        return ToRect(bounds);
    }

    internal static Rect CalculateHeaderFooterTextRect(
        Rect sectionRect,
        WorksheetHeaderFooterPicture? picture,
        TextAlignment alignment)
    {
        var bounds = WorksheetPrintHeaderFooterGeometryPlanner.ResolveTextBounds(
            ToLayoutRect(sectionRect),
            picture,
            ToPageAlignment(alignment));
        return ToRect(bounds);
    }

    // R111-app-host-multiline-header-footer-1: the per-line row height used both to size the overall
    // header/footer band (below) and to lay out each individual line within it
    // (DrawHeaderFooterFormattedRuns in PrintRenderer.HeaderFooterDrawing.cs) -- kept as the historical
    // fixed single-line default (previously the unconditional band height) rather than derived from
    // any one run's actual font size, matching how this band's height was already independent of font
    // size before this fix.
    internal const double HeaderFooterSingleLineHeight = 18.0;

    internal static double CalculateHeaderFooterLineHeight(
        WorksheetHeaderFooter value,
        WorksheetHeaderFooterPictureSet pictures,
        bool draftQuality = false,
        double fontScale = 1.0)
    {
        return WorksheetPrintHeaderFooterGeometryPlanner.ResolveLineHeight(
            value,
            pictures,
            draftQuality,
            fontScale,
            HeaderFooterSingleLineHeight,
            sizeToContent: true);
    }

    private static LayoutRect ToLayoutRect(Rect rect) =>
        new(rect.Left, rect.Top, rect.Width, rect.Height);

    private static PageTextAlignment ToPageAlignment(TextAlignment alignment) =>
        alignment switch
        {
            TextAlignment.Center => PageTextAlignment.Center,
            TextAlignment.Right => PageTextAlignment.Right,
            _ => PageTextAlignment.Left,
        };
}
