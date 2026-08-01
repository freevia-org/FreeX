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
        if (draftQuality)
            return HeaderFooterSingleLineHeight;

        // R111-app-host-multiline-header-footer-1: a section may contain a literal line break
        // (Alt+Enter in Excel's Header/Footer editor, preserved verbatim as an embedded '\n' by
        // TokenizeSectionText). Grow the band to fit the tallest section's line count so
        // DrawHeaderFooterFormattedRuns has room to draw every line -- previously this always
        // returned a fixed single-line height regardless of content, so WPF's
        // FormattedText.MaxLineCount = 1 silently dropped every line after the first even though the
        // band itself had no room reserved for them either.
        //
        // R111-app-host-headerfooter-scale-with-document-1: fontScale (Sheet.
        // HeaderFooterScaleWithDocument's resolved multiplier, 1.0 when the flag is off or the page's
        // print scale is 100%) must grow/shrink the reserved TEXT band by the exact same factor as the
        // font size DrawHeaderFooterFormattedRuns actually draws at, so a larger scaled font never gets
        // clipped by an unscaled band and a smaller one never leaves an oversized gap. Picture-driven
        // height (below) is deliberately left unscaled -- Excel's "Scale with document" only affects
        // header/footer TEXT, never an inserted picture's own size.
        var maxLines = Math.Max(1, Math.Max(
            PagePrintTextPlanner.CountSectionLines(value.Left),
            Math.Max(PagePrintTextPlanner.CountSectionLines(value.Center), PagePrintTextPlanner.CountSectionLines(value.Right))));
        var height = HeaderFooterSingleLineHeight * fontScale * maxLines;

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
