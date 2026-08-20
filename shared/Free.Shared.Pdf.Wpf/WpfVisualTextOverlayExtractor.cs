using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Free.Shared.Pdf;

namespace Free.Shared.Pdf.Wpf;

/// <summary>
/// Extracts selectable-text overlays straight from a rendered page's drawing tree, by walking its
/// <see cref="GlyphRunDrawing"/>s.
///
/// <para>
/// This is the counterpart to <see cref="WpfXpsTextOverlayExtractor"/> and exists because the XPS
/// route cannot always run: WPF subsets every font it serializes, and
/// <c>MS.Internal.TrueTypeSubsetter</c> throws <see cref="System.IO.FileFormatException"/> on a font
/// it cannot parse. That is not hypothetical -- a stock Windows Calibri (6.27, 7048 glyphs, which
/// <see cref="GlyphTypeface"/> itself loads without complaint) fails it, and the whole text layer is
/// lost on that machine. Reading the glyph runs the page already carries needs no serialization, no
/// package and no subsetting, so it works regardless of what the fonts do.
/// </para>
///
/// <para>
/// It also recovers a real font family. The XPS extractor cannot: an XPS font resource URI
/// (<c>/Resources/Fonts/&lt;hash&gt;.odttf</c>) carries no family name, so it substitutes "Segoe UI".
/// A <see cref="GlyphRun"/> still has its <see cref="GlyphTypeface"/>, so the overlay can name the
/// typeface the page actually used.
/// </para>
/// </summary>
public static class WpfVisualTextOverlayExtractor
{
    /// <summary>
    /// Walks <paramref name="visual"/> and returns one overlay per glyph run, in page coordinates
    /// scaled by <paramref name="dipToPointScale"/>.
    /// </summary>
    public static IReadOnlyList<PdfTextOverlay> Extract(Visual visual, double dipToPointScale)
    {
        ArgumentNullException.ThrowIfNull(visual);

        var overlays = new List<PdfTextOverlay>();
        WalkVisual(visual, Matrix.Identity, dipToPointScale, overlays);
        return overlays;
    }

    private static void WalkVisual(
        Visual visual,
        Matrix inherited,
        double dipToPointScale,
        List<PdfTextOverlay> overlays)
    {
        // A visual contributes both an offset and (optionally) a transform, and the drawing it owns
        // sits under both. Compose in that order so nested content lands where it is painted.
        var transform = inherited;
        var offset = VisualTreeHelper.GetOffset(visual);
        if (offset.X != 0 || offset.Y != 0)
            transform = Matrix.Multiply(new Matrix(1, 0, 0, 1, offset.X, offset.Y), transform);
        if (VisualTreeHelper.GetTransform(visual) is { } visualTransform)
            transform = Matrix.Multiply(visualTransform.Value, transform);

        if (VisualTreeHelper.GetDrawing(visual) is { } drawing)
            WalkDrawing(drawing, transform, dipToPointScale, overlays);

        var childCount = VisualTreeHelper.GetChildrenCount(visual);
        for (var index = 0; index < childCount; index++)
        {
            if (VisualTreeHelper.GetChild(visual, index) is Visual child)
                WalkVisual(child, transform, dipToPointScale, overlays);
        }
    }

    private static void WalkDrawing(
        System.Windows.Media.Drawing drawing,
        Matrix inherited,
        double dipToPointScale,
        List<PdfTextOverlay> overlays)
    {
        switch (drawing)
        {
            case DrawingGroup group:
            {
                var transform = inherited;
                if (group.Transform is { } groupTransform)
                    transform = Matrix.Multiply(groupTransform.Value, transform);

                foreach (var child in group.Children)
                    WalkDrawing(child, transform, dipToPointScale, overlays);
                break;
            }

            case GlyphRunDrawing { GlyphRun: { } run }:
            {
                var foreground = ((GlyphRunDrawing)drawing).ForegroundBrush;

                // A run the host is deliberately hiding (a deleted/inserted revision run suppressed by
                // Display for Review > No Markup/Original/Simple Markup, or real Hidden/w:vanish text
                // under the current view) is painted with a fully transparent brush and a near-zero
                // FontRenderingEmSize -- see DocumentView.HideTextInlineWhenNeeded/IsTextHiddenInCurrentView
                // (wpf.Foreground = Brushes.Transparent; wpf.FontSize = 0.015). The raster page already
                // shows nothing there; skip the overlay too, or the exported PDF's searchable/selectable
                // text layer would recover content the user chose to hide even though nothing is visible
                // on the printed page.
                if (IsDeliberatelyInvisible(foreground, run))
                    break;

                var text = ReadText(run);
                if (!string.IsNullOrEmpty(text))
                    overlays.Add(BuildOverlay(run, text, foreground, inherited, dipToPointScale));
                break;
            }
        }
    }

    private static string ReadText(GlyphRun run)
    {
        if (run.Characters is not { Count: > 0 } characters)
            return string.Empty;

        var builder = new StringBuilder(characters.Count);
        foreach (var character in characters)
            builder.Append(character);
        return builder.ToString();
    }

    private static PdfTextOverlay BuildOverlay(
        GlyphRun run,
        string text,
        Brush? foreground,
        Matrix transform,
        double dipToPointScale)
    {
        var origin = transform.Transform(run.BaselineOrigin);
        var scale = PdfTransformMath.EstimateUniformScale(
            transform.M11,
            transform.M12,
            transform.M21,
            transform.M22);
        var fontSizeDip = (run.FontRenderingEmSize > 0 ? run.FontRenderingEmSize : 12) * scale;

        // GlyphRun.BaselineOrigin is the run's baseline, while WpfRasterPdfWriter.DrawTextOverlays
        // draws at (overlay.Y + overlay.FontSize) -- the "Y = top of the text box" convention the
        // other extractors use. Subtracting the scaled font size keeps every extractor's overlays
        // interchangeable through the same writer.
        return new PdfTextOverlay(
            X: origin.X * dipToPointScale,
            Y: (origin.Y - fontSizeDip) * dipToPointScale,
            FontSize: fontSizeDip * dipToPointScale,
            FontFamily: ResolveFontFamily(run),
            Bold: run.GlyphTypeface?.Weight.ToOpenTypeWeight() >= 600,
            Italic: run.GlyphTypeface is { } italicFace && italicFace.Style != FontStyles.Normal,
            Color: ResolveColor(foreground),
            RotationDegrees: 0,
            Text: text);
    }

    private static string ResolveFontFamily(GlyphRun run)
    {
        if (run.GlyphTypeface is not { } typeface)
            return "Segoe UI";

        foreach (var name in typeface.Win32FamilyNames.Values)
        {
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        foreach (var name in typeface.FamilyNames.Values)
        {
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }

        return "Segoe UI";
    }

    private static PdfColor ResolveColor(Brush? brush) =>
        brush is SolidColorBrush solid
            ? new PdfColor(solid.Color.R, solid.Color.G, solid.Color.B)
            : PdfColor.Black;

    // Matches the sentinel a host uses to hide a run's content while keeping it in the visual tree for
    // model round-trip (see the comment on the GlyphRunDrawing case above). A fully transparent brush
    // (alpha 0) or a sub-1-DIP font size never occurs for real, legible content, so either signal alone
    // is enough to treat the run as deliberately invisible -- no page ever needs a searchable/selectable
    // overlay for text nothing can see.
    private static bool IsDeliberatelyInvisible(Brush? foreground, GlyphRun run) =>
        foreground is SolidColorBrush { Color.A: 0 } || run.FontRenderingEmSize is > 0 and < 1.0;
}
