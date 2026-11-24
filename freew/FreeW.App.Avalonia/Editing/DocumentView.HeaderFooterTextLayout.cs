using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{
    // Literal paragraphs with representable run styles share one source-aware layout. Tabs and
    // semantic fields retain their established separate flow until their model/display mapping is exact.
    internal bool HeaderFooterUsesSharedLiteralLayout(Paragraph paragraph)
    {
        // Model-only command callers can create an empty H/F region before a rendering platform
        // exists. Do not introduce eager shaping/font lookup into that established path.
        if (!HeaderFooterTextServicesAvailable())
            return false;
        foreach (var run in paragraph.Runs)
        {
            if (!IsUnicodeLiteralRun(run) || run.Control is not null || run.TableFormula is not null
                || run.CrossReference is not null || run.Citation is not null || run.Ruby is not null
                || run.PreservedDrawing is not null || run.SubDocument is not null || run.IsCommentReference
                || run.IsPageBreak || run.IsColumnBreak || run.Revision != RevisionKind.None
                || run.FormatRevision is not null || run.MoveRevisionId is not null || run.Text.Contains('\t'))
                return false;
            var resolved = ResolveRunFmt(run, paragraph);
            if (IsTextHiddenInCurrentView(resolved) || resolved.VerticalAlign != VerticalAlign.Baseline
                || RunBaselinePositionPlanner.ResolveOffsetDip(resolved, PxPerPoint) != 0
                || RunCharacterSpacingPlanner.ResolveAdvanceDip(resolved, PxPerPoint) != 0)
                return false;
        }
        return true;
    }

    private static bool HeaderFooterTextServicesAvailable()
    {
        try
        {
            _ = FontManager.Current;
            _ = TextShaper.Current;
            return true;
        }
        catch (InvalidOperationException)
        {
            // Rendering services are optional for model-only command callers. Do not cache this:
            // a later UI/headless scope can have a fully initialized rendering platform.
            return false;
        }
    }

    private static bool HeaderFooterContainsSoftBreak(string text) => text.Any(IsBodySoftLineBreak);

    private GenericTextRunProperties HeaderFooterRunProperties(RunFormatting formatting)
    {
        var typeface = new Typeface(DocumentFontFamilyResolver.Resolve(formatting.FontFamily),
            formatting.Italic ? FontStyle.Italic : FontStyle.Normal,
            formatting.Bold ? FontWeight.Bold : FontWeight.Normal);
        var features = RunOpenTypeFeaturePlanner.Build(formatting).AvaloniaFeatureSettings;
        return new GenericTextRunProperties(typeface, FontSizePx(formatting),
            foregroundBrush: BrushFor(formatting.ColorHex), cultureInfo: CultureInfo.CurrentCulture,
            fontFeatures: features.Count > 0
                ? new FontFeatureCollection(features.Select(FontFeature.Parse)) : null);
    }

    private TextLayout BuildHeaderFooterLiteralLayout(string text, RunFormatting formatting)
    {
        var properties = HeaderFooterRunProperties(formatting);
        // The H/F surface has always used infinite paragraph width with explicit hard breaks.
        // NoWrap preserves that policy and keeps a structural EOF as a stop marker.
        return new TextLayout(new HeaderFooterTextSource(text, [(0, text.Length, properties)]),
            new GenericTextParagraphProperties(properties, textWrapping: TextWrapping.NoWrap));
    }

    private TextLayout BuildHeaderFooterParagraphLayout(Paragraph paragraph)
    {
        var spans = new List<(int Start, int End, TextRunProperties Properties)>();
        var offset = 0;
        foreach (var run in paragraph.Runs)
        {
            var properties = HeaderFooterRunProperties(ResolveRunFmt(run, paragraph));
            spans.Add((offset, offset + run.Text.Length, properties));
            offset += run.Text.Length;
        }
        var defaults = spans.Count > 0 ? spans[0].Properties
            : HeaderFooterRunProperties(ResolveRunFmt(RunFormatting.Default, paragraph, null));
        return new TextLayout(new HeaderFooterTextSource(paragraph.PlainText, spans),
            new GenericTextParagraphProperties(defaults, textWrapping: TextWrapping.NoWrap));
    }

    private sealed class HeaderFooterLineBreak(int length, TextRunProperties properties) : TextEndOfLine(length)
    {
        public override TextRunProperties Properties => properties;
    }

    private sealed class HeaderFooterTextSource(string text,
        IReadOnlyList<(int Start, int End, TextRunProperties Properties)> spans) : ITextSource
    {
        public TextRun? GetTextRun(int textSourceIndex)
        {
            if (textSourceIndex >= text.Length)
            {
                // Avalonia drops a zero-length EOF before constructing the trailing empty line.
                // Its standard one-unit paragraph terminator is layout-only and never painted;
                // every exposed hit/caret is clamped back to the original string length.
                return textSourceIndex == text.Length && text.Length > 0 && IsBodySoftLineBreak(text[^1])
                    ? new TextEndOfParagraph(1) : null;
            }
            var span = spans.First(item => textSourceIndex >= item.Start && textSourceIndex < item.End);
            if (IsBodySoftLineBreak(text[textSourceIndex]))
            {
                var length = text[textSourceIndex] == '\r' && textSourceIndex + 1 < text.Length
                    && text[textSourceIndex + 1] == '\n' ? 2 : 1;
                return new HeaderFooterLineBreak(length, span.Properties);
            }
            var end = textSourceIndex + 1;
            while (end < span.End && !IsBodySoftLineBreak(text[end]))
                end++;
            return new TextCharacters(text.AsMemory(textSourceIndex, end - textSourceIndex), span.Properties);
        }
    }

    private TextLayout? SharedHeaderFooterLayout(HfRenderItem item) =>
        item.Target is { } target && GetHfParagraph(target) is { } paragraph
            && HeaderFooterUsesSharedLiteralLayout(paragraph)
                ? BuildHeaderFooterParagraphLayout(paragraph) : null;

    private Point HeaderFooterLayoutOrigin(HfRenderItem item, TextLayout layout) => new(
        item.X + AlignmentOffset(item.Alignment, item.AvailableWidth,
            layout.WidthIncludingTrailingWhitespace, isLast: true),
        item.Y + RunBaselinePositionPlanner.ResolveOffsetDip(item.Fmt, PxPerPoint));

    private static Rect HeaderFooterInsertionBounds(TextLayout layout, string sourceText, int offset)
    {
        offset = HeaderFooterLayoutBoundary(sourceText, offset);
        var top = 0.0;
        var boundaries = StringInfo.ParseCombiningCharacters(sourceText);
        for (var index = 0; index < layout.TextLines.Count; index++)
        {
            var line = layout.TextLines[index];
            var end = Math.Min(sourceText.Length, line.FirstTextSourceIndex + line.Length);
            if (offset >= end && index + 1 < layout.TextLines.Count)
            {
                top += line.Height;
                continue;
            }
            // An insertion at a logical line end belongs to the trailing edge of the preceding
            // visible text element, not the leading bounds of the following line-control run.
            var hit = new CharacterHit(offset);
            if (offset > line.FirstTextSourceIndex)
            {
                var previous = Array.BinarySearch(boundaries, offset - 1);
                previous = previous >= 0 ? previous : ~previous - 1;
                var start = boundaries[Math.Max(0, previous)];
                hit = new CharacterHit(start, offset - start);
            }
            return new Rect(line.GetDistanceFromCharacterHit(hit), top, 0, line.Height);
        }
        return default;
    }

    // Legacy field/styled segments retain their established positioning, but a line-break-only
    // prefix has no advance. Never send that prefix to the formatter's no-glyph path.
    private static bool HeaderFooterHasOnlyNonprintingControls(string text) => text.All(character =>
        IsBodySoftLineBreak(character) || char.IsControl(character)
        || char.GetUnicodeCategory(character) == UnicodeCategory.Format);

    private double HeaderFooterTextWidth(string text, RunFormatting formatting)
    {
        var glyphless = HeaderFooterHasOnlyNonprintingControls(text);
        if (glyphless && !text.Contains('\t'))
            return 0;
        if (!glyphless && !HeaderFooterContainsSoftBreak(text))
            return Build(text, formatting).WidthIncludingTrailingWhitespace;
        // A tab retains its advance without painting a glyph. Soft-break-containing display
        // text also needs TextLayout: FormattedText can stall on an empty leading/interior line.
        using var layout = BuildHeaderFooterLiteralLayout(text, formatting);
        return layout.WidthIncludingTrailingWhitespace;
    }

    private static int HeaderFooterLayoutBoundary(string text, int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        if (offset == text.Length) return offset;
        var starts = StringInfo.ParseCombiningCharacters(text);
        var index = Array.BinarySearch(starts, offset);
        return index >= 0 ? offset : starts[Math.Max(0, ~index - 1)];
    }
}
