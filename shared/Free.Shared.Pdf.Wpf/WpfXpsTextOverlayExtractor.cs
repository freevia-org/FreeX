using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Free.Shared.Pdf.Wpf;

/// <summary>
/// Extracts selectable-text overlays from a <see cref="FixedPage"/> produced by WPF's own XPS
/// serializer (<see cref="System.Windows.Xps.XpsDocumentWriter"/>), by walking its <see cref="Glyphs"/>
/// runs.
///
/// <para>
/// XPS serialization is WPF's in-box, already-shipped mechanism for turning arbitrary content (a
/// <c>FlowDocument</c>'s wrapped text, a <c>DrawingVisual</c> composed of <c>VisualBrush</c> tiles,
/// etc.) into real vector glyph runs with an absolute page-space origin and a Unicode string. A host
/// whose print/export pages are not made of simple text controls (so
/// <c>PdfDocumentExporter.PdfTextOverlayExtractor</c>'s control-tree walk does not apply — e.g. FreeW's
/// FlowDocument-based editor) can round-trip its paginator through XPS once to recover an equivalent
/// text layer, then feed the resulting overlays into the same shared
/// <see cref="Free.Shared.Pdf.PdfRasterPage.TextOverlays"/> contract <see cref="WpfRasterPdfWriter"/>
/// already knows how to draw.
/// </para>
/// </summary>
public static class WpfXpsTextOverlayExtractor
{
    /// <param name="page">A <see cref="FixedPage"/> read back from a written XPS package.</param>
    /// <param name="dipToPointScale">
    /// Scale factor from WPF device-independent pixels (1/96&quot;) to PDF points (1/72&quot;), i.e.
    /// <c>72.0 / 96.0</c>.
    /// </param>
    public static IReadOnlyList<PdfTextOverlay> Extract(FixedPage page, double dipToPointScale)
    {
        ArgumentNullException.ThrowIfNull(page);

        var overlays = new List<PdfTextOverlay>();
        foreach (UIElement child in page.Children)
            Extract(child, Matrix.Identity, dipToPointScale, overlays);

        return overlays;
    }

    private static void Extract(UIElement element, Matrix parentTransform, double dipToPointScale, List<PdfTextOverlay> overlays)
    {
        if (element.Visibility != Visibility.Visible)
            return;

        var transform = AppendElementTransform(element, parentTransform);

        if (element is Glyphs glyphs && !string.IsNullOrEmpty(glyphs.UnicodeString))
            overlays.Add(BuildOverlay(glyphs, transform, dipToPointScale));

        if (element is Panel panel)
        {
            foreach (UIElement child in panel.Children)
                Extract(child, transform, dipToPointScale, overlays);
        }
        else if (element is Decorator { Child: UIElement decoratorChild })
        {
            Extract(decoratorChild, transform, dipToPointScale, overlays);
        }
    }

    private static PdfTextOverlay BuildOverlay(Glyphs glyphs, Matrix transform, double dipToPointScale)
    {
        var origin = transform.Transform(new Point(glyphs.OriginX, glyphs.OriginY));
        var scale = EstimateUniformScale(transform);
        var fontSizeDip = (glyphs.FontRenderingEmSize > 0 ? glyphs.FontRenderingEmSize : 12) * scale;

        // Glyphs.OriginX/Y is the run's baseline; WpfRasterPdfWriter.DrawTextOverlays draws at
        // (overlay.Y + overlay.FontSize), matching the "Y = top of the text box" convention the
        // control-tree overlay extractor (FreeX's PdfTextOverlayExtractor) already uses. Subtracting the
        // (scaled) font size here keeps the two extractors' overlays interchangeable through the same
        // shared writer.
        return new PdfTextOverlay(
            X: origin.X * dipToPointScale,
            Y: (origin.Y - fontSizeDip) * dipToPointScale,
            FontSize: fontSizeDip * dipToPointScale,
            FontFamily: ResolveFontFamily(glyphs),
            Bold: glyphs.StyleSimulations is StyleSimulations.BoldSimulation or StyleSimulations.BoldItalicSimulation,
            Italic: glyphs.StyleSimulations is StyleSimulations.ItalicSimulation or StyleSimulations.BoldItalicSimulation,
            Color: ResolveColor(glyphs.Fill),
            RotationDegrees: 0,
            Text: glyphs.UnicodeString);
    }

    private static Matrix AppendElementTransform(UIElement element, Matrix parentTransform)
    {
        var transform = Matrix.Identity;
        if (TryGetFiniteMatrix(element.RenderTransform, out var renderTransform) && !renderTransform.IsIdentity)
            transform.Append(renderTransform);

        var x = ReadLeft(element);
        var y = ReadTop(element);
        if (element is FrameworkElement frameworkElement)
        {
            x += frameworkElement.Margin.Left;
            y += frameworkElement.Margin.Top;
        }

        transform.Append(new Matrix(1, 0, 0, 1, x, y));
        transform.Append(parentTransform);
        return transform;
    }

    private static double EstimateUniformScale(Matrix transform)
    {
        var scaleX = Math.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12);
        var scaleY = Math.Sqrt(transform.M21 * transform.M21 + transform.M22 * transform.M22);
        var scale = (scaleX + scaleY) / 2.0;
        return IsFinite(scale) && scale > 0 ? scale : 1.0;
    }

    private static double ReadLeft(UIElement element)
    {
        var left = Canvas.GetLeft(element);
        return double.IsNaN(left) ? 0 : left;
    }

    private static double ReadTop(UIElement element)
    {
        var top = Canvas.GetTop(element);
        return double.IsNaN(top) ? 0 : top;
    }

    private static bool TryGetFiniteMatrix(Transform? transform, out Matrix matrix)
    {
        if (transform is null || transform == Transform.Identity)
        {
            matrix = Matrix.Identity;
            return true;
        }

        matrix = transform.Value;
        return IsFinite(matrix);
    }

    private static bool IsFinite(Matrix matrix) =>
        IsFinite(matrix.M11) && IsFinite(matrix.M12) &&
        IsFinite(matrix.M21) && IsFinite(matrix.M22) &&
        IsFinite(matrix.OffsetX) && IsFinite(matrix.OffsetY);

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static PdfColor ResolveColor(Brush? brush) =>
        brush is SolidColorBrush solid
            ? new PdfColor(solid.Color.R, solid.Color.G, solid.Color.B)
            : PdfColor.Black;

    // The XPS font resource URI (e.g. "/Resources/Fonts/<hash>.odttf") carries no usable family name,
    // so this falls back to a font that ships on every supported Windows install. The overlay text is
    // only there for search/selection/accessibility, so an approximate substitute typeface does not
    // affect what the reader sees (the raster page beneath it already carries the true glyphs).
    private static string ResolveFontFamily(Glyphs glyphs) => "Segoe UI";
}
