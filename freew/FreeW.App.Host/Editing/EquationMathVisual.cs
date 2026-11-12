using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Presentation.DocumentView.MathLayout;
using FreeW.Core.Model;

namespace FreeW.App.Host.Editing;

/// <summary>
/// Paints an equation laid out by the shared <see cref="EquationMathLayout"/> engine: glyph runs of Cambria
/// Math (including size variants and assembly parts), rules for fraction/radical/bar strokes. It reports its
/// math baseline through <see cref="TextBlock.BaselineOffsetProperty"/> so a hosting
/// <see cref="InlineUIContainer"/> with <see cref="BaselineAlignment.Baseline"/> sits the equation's
/// baseline on the text baseline, as Word does. The same visual serves the editor, print and XPS/PDF.
/// </summary>
internal sealed class EquationMathVisual : FrameworkElement
{
    public EquationMathVisual(Equation equation, double fontSize, bool? display = null)
    {
        Equation = equation;
        Layout = EquationMathLayout.Layout(equation, WpfMathLayoutFont.Instance, fontSize, display);
        TextBlock.SetBaselineOffset(this, Layout.Ascent);
        SnapsToDevicePixels = false;

        // Math is laid out left to right even inside a right-to-left paragraph (Word's default).
        FlowDirection = FlowDirection.LeftToRight;
    }

    /// <summary>The model equation this visual draws.</summary>
    public Equation Equation { get; }

    /// <summary>The shared layout being painted.</summary>
    public MathLayoutResult Layout { get; }

    protected override Size MeasureOverride(Size availableSize) =>
        new(Math.Max(0, Layout.Width), Math.Max(0, Layout.Height));

    protected override Size ArrangeOverride(Size finalSize) => finalSize;

    protected override void OnRender(DrawingContext drawingContext)
    {
        var brush = TextElement.GetForeground(this) ?? Brushes.Black;
        var pixelsPerDip = (float)VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var font = WpfMathLayoutFont.Instance;
        drawingContext.PushTransform(new TranslateTransform(0, Layout.Ascent));
        foreach (var item in Layout.Items)
        {
            switch (item)
            {
                case MathRuleDrawItem rule:
                    drawingContext.DrawRectangle(brush, null, new Rect(rule.X, rule.Y, rule.Width, rule.Height));
                    break;

                case MathGlyphDrawItem glyph when font.GlyphTypeface is { } typeface:
                    DrawGlyphs(drawingContext, typeface, [(ushort)glyph.GlyphId], glyph.X, glyph.BaselineY, glyph.FontSize, brush, pixelsPerDip, null);
                    break;

                case MathTextDrawItem text:
                    if (font.GlyphTypeface is { } glyphTypeface && font.TryMapGlyphs(text.Text, out var glyphs))
                    {
                        DrawGlyphs(drawingContext, glyphTypeface, glyphs, text.X, text.BaselineY, text.FontSize, brush, pixelsPerDip, text.Text);
                    }
                    else
                    {
                        var formatted = WpfMathLayoutFont.FallbackText(text.Text, text.FontSize, brush, pixelsPerDip);
                        drawingContext.DrawText(formatted, new Point(text.X, text.BaselineY - formatted.Baseline));
                    }

                    break;
            }
        }

        drawingContext.Pop();
    }

    private static void DrawGlyphs(
        DrawingContext drawingContext,
        GlyphTypeface typeface,
        ushort[] glyphs,
        double x,
        double baselineY,
        double fontSize,
        Brush brush,
        float pixelsPerDip,
        string? characters)
    {
        if (glyphs.Length == 0 || fontSize <= 0)
            return;

        var advances = new double[glyphs.Length];
        for (var i = 0; i < glyphs.Length; i++)
            advances[i] = typeface.AdvanceWidths[glyphs[i]] * fontSize;

        // Characters/cluster map are only supplied when they correspond 1:1 to the glyphs (BMP text).
        IList<char>? characterList = null;
        IList<ushort>? clusterMap = null;
        if (characters is not null && characters.Length == glyphs.Length)
        {
            characterList = characters.ToCharArray();
            clusterMap = Enumerable.Range(0, glyphs.Length).Select(index => (ushort)index).ToArray();
        }

        var run = new GlyphRun(
            typeface,
            bidiLevel: 0,
            isSideways: false,
            renderingEmSize: fontSize,
            pixelsPerDip: pixelsPerDip,
            glyphIndices: glyphs,
            baselineOrigin: new Point(x, baselineY),
            advanceWidths: advances,
            glyphOffsets: null,
            characters: characterList,
            deviceFontName: null,
            clusterMap: clusterMap,
            caretStops: null,
            language: null);
        drawingContext.DrawGlyphRun(brush, run);
    }
}
