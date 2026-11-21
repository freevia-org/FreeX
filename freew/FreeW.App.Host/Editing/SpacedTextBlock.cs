using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Host.Editing;

/// <summary>
/// A text box that paints its text with Word's expanded/condensed character spacing (<c>w:spacing</c>): a fixed
/// extra advance after every character. WPF text has no letter-spacing property, so a page-only render
/// (never read back into the model) hosts each word of a spaced run in one of these inside an
/// <see cref="InlineUIContainer"/>; the editor keeps the plain, editable run. Every input is a dependency
/// property (the <see cref="TextBlock"/> text/font/brush/decoration slots are re-owned) so the box clones
/// through XAML for print and pagination, and it reports the text baseline through
/// <see cref="TextBlock.BaselineOffsetProperty"/> so an inline host aligns it like an ordinary text box.
/// </summary>
public sealed class SpacedTextBlock : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = TextBlock.TextProperty.AddOwner(
        typeof(SpacedTextBlock),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontFamilyProperty = TextBlock.FontFamilyProperty.AddOwner(typeof(SpacedTextBlock));
    public static readonly DependencyProperty FontSizeProperty = TextBlock.FontSizeProperty.AddOwner(typeof(SpacedTextBlock));
    public static readonly DependencyProperty FontStyleProperty = TextBlock.FontStyleProperty.AddOwner(typeof(SpacedTextBlock));
    public static readonly DependencyProperty FontWeightProperty = TextBlock.FontWeightProperty.AddOwner(typeof(SpacedTextBlock));
    public static readonly DependencyProperty FontStretchProperty = TextBlock.FontStretchProperty.AddOwner(typeof(SpacedTextBlock));
    public static readonly DependencyProperty ForegroundProperty = TextBlock.ForegroundProperty.AddOwner(typeof(SpacedTextBlock));
    public static readonly DependencyProperty BackgroundProperty = TextBlock.BackgroundProperty.AddOwner(typeof(SpacedTextBlock));
    public static readonly DependencyProperty TextDecorationsProperty = TextBlock.TextDecorationsProperty.AddOwner(typeof(SpacedTextBlock));
    public static readonly DependencyProperty BaselineOffsetProperty = TextBlock.BaselineOffsetProperty.AddOwner(typeof(SpacedTextBlock));

    public static readonly DependencyProperty CharacterAdvanceProperty = DependencyProperty.Register(
        nameof(CharacterAdvance),
        typeof(double),
        typeof(SpacedTextBlock),
        new FrameworkPropertyMetadata(
            0.0,
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private TextLayoutCache? _cache;

    public SpacedTextBlock()
    {
        IsHitTestVisible = false;
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public FontStyle FontStyle
    {
        get => (FontStyle)GetValue(FontStyleProperty);
        set => SetValue(FontStyleProperty, value);
    }

    public FontWeight FontWeight
    {
        get => (FontWeight)GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }

    public FontStretch FontStretch
    {
        get => (FontStretch)GetValue(FontStretchProperty);
        set => SetValue(FontStretchProperty, value);
    }

    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public TextDecorationCollection? TextDecorations
    {
        get => (TextDecorationCollection?)GetValue(TextDecorationsProperty);
        set => SetValue(TextDecorationsProperty, value);
    }

    public double BaselineOffset
    {
        get => (double)GetValue(BaselineOffsetProperty);
        set => SetValue(BaselineOffsetProperty, value);
    }

    /// <summary>The extra advance in DIPs added after every character (negative condenses).</summary>
    public double CharacterAdvance
    {
        get => (double)GetValue(CharacterAdvanceProperty);
        set => SetValue(CharacterAdvanceProperty, value);
    }

    protected override Size MeasureOverride(Size constraint)
    {
        _cache = null;
        var layout = GetLayout();
        if (layout is null)
            return new Size(0, 0);

        SetCurrentValue(BaselineOffsetProperty, layout.Baseline);
        return new Size(layout.TotalWidth, layout.Height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var layout = GetLayout();
        if (layout is null)
            return;

        var brush = Foreground ?? Brushes.Black;
        if (Background is { } background)
            drawingContext.DrawRectangle(background, null, new Rect(0, 0, layout.TotalWidth, layout.Height));
        for (var i = 0; i < layout.Elements.Count; i++)
            drawingContext.DrawText(layout.Elements[i], new Point(layout.Offsets[i], 0));

        DrawDecorations(drawingContext, layout, brush);
    }

    private void DrawDecorations(DrawingContext drawingContext, TextLayoutCache layout, Brush brush)
    {
        if (TextDecorations is not { Count: > 0 } decorations)
            return;

        var typeface = layout.Typeface;
        var size = FontSize;
        foreach (var decoration in decorations)
        {
            double position;
            double thickness;
            switch (decoration.Location)
            {
                case TextDecorationLocation.Underline:
                    position = -typeface.UnderlinePosition;
                    thickness = typeface.UnderlineThickness;
                    break;
                case TextDecorationLocation.Strikethrough:
                    position = -typeface.StrikethroughPosition;
                    thickness = typeface.StrikethroughThickness;
                    break;
                case TextDecorationLocation.OverLine:
                    position = -FontFamily.Baseline;
                    thickness = typeface.UnderlineThickness;
                    break;
                default:
                    continue;
            }

            var penBrush = decoration.Pen?.Brush ?? brush;
            var penThickness = decoration.Pen is { } pen && decoration.PenThicknessUnit == TextDecorationUnit.Pixel
                ? pen.Thickness
                : Math.Max(1.0, thickness * size);
            var y = layout.Baseline + position * size;
            drawingContext.DrawLine(
                new Pen(penBrush, penThickness),
                new Point(0, y),
                new Point(layout.TotalWidth, y));
        }
    }

    private TextLayoutCache? GetLayout()
    {
        if (_cache is not null)
            return _cache;

        var text = Text;
        var advance = CharacterAdvance;
        if (string.IsNullOrEmpty(text) || !double.IsFinite(advance))
            return null;

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var brush = Foreground ?? Brushes.Black;
        var dpi = 1.0;
        try
        {
            dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        }
        catch (InvalidOperationException)
        {
            // Detached elements fall back to the default 96 DPI metric.
        }

        FormattedText Format(string value) => new(
            value,
            CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            typeface,
            FontSize,
            brush,
            dpi);

        // Word does not pair-kern unless asked to (w:kern), and a plain WPF run lays out at the font's ideal glyph
        // advances; FormattedText always kerns. Unkerned runs therefore take their advances straight from the
        // glyph typeface so a spaced box is exactly as wide as the plain run plus the spacing.
        var kerning = Typography.GetKerning(this);
        GlyphTypeface? glyphs = null;
        if (!kerning)
            typeface.TryGetGlyphTypeface(out glyphs);

        var elements = new List<FormattedText>();
        var offsets = new List<double>();
        var x = 0.0;
        var consumed = string.Empty;
        var previousWidth = 0.0;
        var height = 0.0;
        var baseline = 0.0;
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
        {
            var element = enumerator.GetTextElement();
            var formatted = Format(element);
            double elementAdvance;
            if (kerning)
            {
                consumed += element;
                var width = Format(consumed).WidthIncludingTrailingWhitespace;
                elementAdvance = Math.Max(0, width - previousWidth);
                previousWidth = width;
            }
            else if (glyphs is not null
                     && element.Length == 1
                     && glyphs.CharacterToGlyphMap.TryGetValue(element[0], out var glyph))
            {
                elementAdvance = glyphs.AdvanceWidths[glyph] * FontSize;
            }
            else
            {
                elementAdvance = formatted.WidthIncludingTrailingWhitespace;
            }

            height = Math.Max(height, formatted.Height);
            baseline = Math.Max(baseline, formatted.Baseline);
            elements.Add(formatted);
            offsets.Add(x);
            x += elementAdvance + (RunCharacterSpacingPlanner.IsSpacedElement(element) ? advance : 0);
        }

        _cache = new TextLayoutCache(elements, offsets, Math.Max(0, x), height, baseline, typeface);
        return _cache;
    }

    private sealed record TextLayoutCache(
        IReadOnlyList<FormattedText> Elements,
        IReadOnlyList<double> Offsets,
        double TotalWidth,
        double Height,
        double Baseline,
        Typeface Typeface);
}
