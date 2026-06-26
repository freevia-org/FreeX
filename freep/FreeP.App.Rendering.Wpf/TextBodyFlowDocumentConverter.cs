using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using FreeP.Core.Model;
using WpfParagraph = System.Windows.Documents.Paragraph;
using WpfRun       = System.Windows.Documents.Run;
using ModelParagraph = FreeP.Core.Model.Paragraph;
using ModelRun       = FreeP.Core.Model.Run;

namespace FreeP.App.Rendering.Wpf;

/// <summary>
/// Static helper for converting between a FreeP <see cref="TextBody"/> and a WPF
/// <see cref="FlowDocument"/> so that <see cref="InCanvasTextEditor"/> can use a
/// <see cref="System.Windows.Controls.RichTextBox"/> to preserve per-run formatting while editing.
///
/// Design: the conversion is entirely pure / framework-independent at the model level.
/// Only WPF types appear at the FlowDocument end — no live RichTextBox required — so
/// the round-trip can be exercised in unit tests with no STA constraint (the FlowDocument
/// itself is created on whatever thread it's needed; the RichTextBox wrapping it must be STA).
///
/// Wave 10A: initial implementation.
/// Deferred: IME/RTL, per-run super/subscript (TypographyProperties), list continuity.
/// </summary>
internal static class TextBodyFlowDocumentConverter
{
    // Default fallback font when a run carries no explicit family.
    private const string FallbackFont = "Calibri";

    // WPF uses DIPs; PowerPoint font size is in points. 1pt = 96/72 DIPs.
    private const double PtToDip = 96.0 / 72.0;
    private const double DipToPt = 72.0 / 96.0;

    // ── TextBody → FlowDocument ───────────────────────────────────────────────

    /// <summary>
    /// Converts a <see cref="TextBody"/> to a WPF <see cref="FlowDocument"/>.
    /// Each model <see cref="ModelParagraph"/> becomes one WPF <see cref="WpfParagraph"/>;
    /// each model <see cref="ModelRun"/> becomes one WPF <see cref="WpfRun"/>.
    /// Paragraph alignment, font family/size, bold, italic, underline, strikethrough, and color
    /// are all mapped.
    ///
    /// If <paramref name="body"/> is null or empty an empty single-paragraph document is returned.
    /// </summary>
    public static FlowDocument ToFlowDocument(TextBody? body, double fallbackFontSizePt = 14)
    {
        // 100000 DIPs (~1041 feet) is large enough that the FlowDocument never paginates
        // inside a RichTextBox, while staying within WPF's accepted finite range.
        const double VeryLargeWidth = 100_000.0;

        var doc = new FlowDocument
        {
            // Disable pagination — we render in a RichTextBox / scroll viewer.
            PageWidth   = VeryLargeWidth,
            ColumnWidth = VeryLargeWidth,
            FontFamily  = new FontFamily(FallbackFont),
            FontSize    = fallbackFontSizePt * PtToDip,
        };

        if (body is null || body.Paragraphs.Count == 0)
        {
            doc.Blocks.Add(new WpfParagraph());
            return doc;
        }

        foreach (var mp in body.Paragraphs)
        {
            var wp = new WpfParagraph
            {
                // Remove default paragraph margins so rendering stays tight.
                Margin = new Thickness(0)
            };

            // Paragraph alignment.
            if (mp.Align.HasValue)
            {
                wp.TextAlignment = mp.Align.Value switch
                {
                    TextAlign.Left        => TextAlignment.Left,
                    TextAlign.Center      => TextAlignment.Center,
                    TextAlign.Right       => TextAlignment.Right,
                    TextAlign.Justify     => TextAlignment.Justify,
                    TextAlign.Distributed => TextAlignment.Justify,
                    _                     => TextAlignment.Left
                };
            }

            if (mp.SpaceBeforePt.HasValue || mp.SpaceAfterPt.HasValue)
            {
                wp.Margin = new Thickness(
                    0,
                    mp.SpaceBeforePt.HasValue ? mp.SpaceBeforePt.Value * PtToDip : 0,
                    0,
                    mp.SpaceAfterPt.HasValue  ? mp.SpaceAfterPt.Value  * PtToDip : 0);
            }

            if (mp.Runs.Count == 0)
            {
                // Preserve empty paragraph as a run with no text.
                wp.Inlines.Add(new WpfRun(string.Empty));
            }
            else
            {
                foreach (var mr in mp.Runs)
                    wp.Inlines.Add(ModelRunToWpfRun(mr));
            }

            doc.Blocks.Add(wp);
        }

        return doc;
    }

    // ── FlowDocument → TextBody ───────────────────────────────────────────────

    /// <summary>
    /// Converts a WPF <see cref="FlowDocument"/> back to a <see cref="TextBody"/>.
    /// Walks every <see cref="Block"/> (expected to be <see cref="WpfParagraph"/>) and every
    /// inline within it (expected to be <see cref="WpfRun"/> or a nested <see cref="Span"/>).
    ///
    /// Contiguous WPF Runs that share identical properties within the same logical span are
    /// preserved as distinct model runs; merging is not performed (keeping round-trip lossless).
    ///
    /// The returned body has <c>Wrap = true</c>; alignment, font, color, bold, italic, underline,
    /// and strikethrough are extracted. Color is stored as a resolved sRGB <see cref="ThemeAwareColor"/>
    /// (scheme ref not available during editing, by design).
    /// </summary>
    public static TextBody FromFlowDocument(FlowDocument doc, TextBody? originalBody = null)
    {
        var body = new TextBody
        {
            Wrap          = true,
            Anchor        = originalBody?.Anchor,
            InsetLeftPt   = originalBody?.InsetLeftPt,
            InsetRightPt  = originalBody?.InsetRightPt,
            InsetTopPt    = originalBody?.InsetTopPt,
            InsetBottomPt = originalBody?.InsetBottomPt,
        };

        int modelParaIndex = 0;
        foreach (var block in doc.Blocks)
        {
            var mp = new ModelParagraph();

            // Restore paragraph alignment.
            if (block is WpfParagraph wpPara)
            {
                mp.Align = wpPara.TextAlignment switch
                {
                    TextAlignment.Center  => TextAlign.Center,
                    TextAlignment.Right   => TextAlign.Right,
                    TextAlignment.Justify => TextAlign.Justify,
                    _                     => TextAlign.Left
                };
            }

            // Restore level/bullet/spacing from original body where count allows.
            if (originalBody is not null &&
                modelParaIndex < originalBody.Paragraphs.Count)
            {
                var orig          = originalBody.Paragraphs[modelParaIndex];
                mp.Level          = orig.Level;
                mp.BulletKind     = orig.BulletKind;
                mp.BulletChar     = orig.BulletChar;
                mp.SpaceBeforePt  = orig.SpaceBeforePt;
                mp.SpaceAfterPt   = orig.SpaceAfterPt;
            }

            if (block is WpfParagraph wp2)
            {
                foreach (var leaf in EnumerateLeafInlines(wp2.Inlines))
                {
                    var mr = WpfInlineToModelRun(leaf);
                    mp.Runs.Add(mr);
                }
            }

            // Ensure at least one (empty) run so the paragraph is not lost.
            if (mp.Runs.Count == 0)
                mp.Runs.Add(new ModelRun { Text = string.Empty });

            body.Paragraphs.Add(mp);
            modelParaIndex++;
        }

        // Ensure at least one paragraph.
        if (body.Paragraphs.Count == 0)
        {
            var para = new ModelParagraph();
            para.Runs.Add(new ModelRun { Text = string.Empty });
            body.Paragraphs.Add(para);
        }

        return body;
    }

    // ── Internal helpers ──────────────────────────────────────────────────────

    private static WpfRun ModelRunToWpfRun(ModelRun mr)
    {
        var wr = new WpfRun(mr.Text ?? string.Empty);

        // Font family.
        if (!string.IsNullOrEmpty(mr.FontFamily))
            wr.FontFamily = new FontFamily(mr.FontFamily);

        // Font size (pt → DIP).
        if (mr.FontSizePt.HasValue)
            wr.FontSize = mr.FontSizePt.Value * PtToDip;

        // Bold / Italic.
        wr.FontWeight = mr.Bold   ? FontWeights.Bold   : FontWeights.Normal;
        wr.FontStyle  = mr.Italic ? FontStyles.Italic  : FontStyles.Normal;

        // Underline + Strikethrough as TextDecorations.
        if (mr.Underline || mr.Strikethrough)
        {
            var decorations = new TextDecorationCollection();
            if (mr.Underline)
                decorations.Add(TextDecorations.Underline[0].Clone());
            if (mr.Strikethrough)
                decorations.Add(TextDecorations.Strikethrough[0].Clone());
            wr.TextDecorations = decorations;
        }
        else
        {
            // Explicitly clear inherited decorations.
            wr.TextDecorations = new TextDecorationCollection();
        }

        // Color.
        var color = ResolveModelColor(mr.Color);
        if (color.HasValue)
            wr.Foreground = new SolidColorBrush(color.Value);

        return wr;
    }

    /// <summary>
    /// Recursively enumerates the leaf <see cref="Inline"/> elements of a paragraph,
    /// flattening nested <see cref="Span"/> containers that the RichTextBox editing engine
    /// may insert when a user applies formatting to a sub-range.
    /// </summary>
    internal static IEnumerable<Inline> EnumerateLeafInlines(InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            if (inline is Span span)
            {
                foreach (var child in EnumerateLeafInlines(span.Inlines))
                    yield return child;
            }
            else
            {
                yield return inline;
            }
        }
    }

    /// <summary>
    /// Reads formatting properties from a WPF <see cref="Inline"/> into a model <see cref="ModelRun"/>.
    /// Properties are read via dependency-property inheritance so that values set on a parent
    /// <see cref="Span"/> are correctly resolved.
    /// </summary>
    internal static ModelRun WpfInlineToModelRun(Inline inline)
    {
        var mr = new ModelRun();

        // Text — only WpfRun has text; LineBreaks become "\n".
        mr.Text = inline switch
        {
            WpfRun wr  => wr.Text ?? string.Empty,
            LineBreak _ => "\n",
            _           => string.Empty
        };

        // Font family — read the effective (inherited) value.
        var family = (FontFamily)inline.GetValue(Inline.FontFamilyProperty);
        mr.FontFamily = family?.Source;

        // Font size (DIP → pt).
        var sizeDip = (double)inline.GetValue(Inline.FontSizeProperty);
        if (!double.IsNaN(sizeDip) && sizeDip > 0)
            mr.FontSizePt = Math.Round(sizeDip * DipToPt, 4);

        // Bold.
        var weight = (FontWeight)inline.GetValue(Inline.FontWeightProperty);
        mr.Bold = weight == FontWeights.Bold
               || weight == FontWeights.SemiBold
               || weight == FontWeights.DemiBold;

        // Italic.
        var style = (FontStyle)inline.GetValue(Inline.FontStyleProperty);
        mr.Italic = style == FontStyles.Italic || style == FontStyles.Oblique;

        // Underline / Strikethrough from TextDecorations.
        var decorations = (TextDecorationCollection?)inline.GetValue(Inline.TextDecorationsProperty);
        if (decorations is not null)
        {
            foreach (var d in decorations)
            {
                if (d.Location == TextDecorationLocation.Underline)
                    mr.Underline = true;
                else if (d.Location == TextDecorationLocation.Strikethrough)
                    mr.Strikethrough = true;
            }
        }

        // Color — convert SolidColorBrush foreground to sRGB ThemeAwareColor.
        var brush = inline.GetValue(Inline.ForegroundProperty) as SolidColorBrush;
        if (brush is not null)
        {
            var c = brush.Color;
            mr.Color = new ThemeAwareColor(new SrgbColor(c.R, c.G, c.B));
        }

        return mr;
    }

    /// <summary>
    /// Resolves a <see cref="ThemeAwareColor"/> to a WPF <see cref="Color"/>.
    /// Only the sRGB channel is used (scheme refs are not available in the editor context).
    /// Returns null if the color is null.
    /// </summary>
    internal static Color? ResolveModelColor(ThemeAwareColor? color)
    {
        if (color is null) return null;
        var s = color.Resolved;
        return Color.FromRgb(s.R, s.G, s.B);
    }
}
