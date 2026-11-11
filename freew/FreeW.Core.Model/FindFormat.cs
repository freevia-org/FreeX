namespace FreeW.Core.Model;

/// <summary>
/// Formatting criteria for Find what, or formatting to apply for Replace with (Word's Format button
/// in the Find and Replace dialog). Every member is optional: null means "don't care" when finding
/// and "leave alone" when replacing.
/// </summary>
public sealed record FindFormat
{
    public bool? Bold { get; init; }
    public bool? Italic { get; init; }
    public bool? Underline { get; init; }
    public string? FontFamily { get; init; }
    public double? FontSizePt { get; init; }

    /// <summary>Font colour as RRGGBB hex, with or without a leading '#'.</summary>
    public string? ColorHex { get; init; }

    /// <summary>
    /// True finds highlighted text (of <see cref="HighlightColorHex"/> when that is set, otherwise of
    /// any colour) and, when replacing, applies that colour (yellow by default). False finds text
    /// that is not highlighted and, when replacing, removes highlighting.
    /// </summary>
    public bool? Highlight { get; init; }

    public string? HighlightColorHex { get; init; }

    /// <summary>Paragraph style id (a key of <see cref="TextDocument.Styles"/>).</summary>
    public string? ParagraphStyleId { get; init; }

    public const string DefaultParagraphStyleId = "Normal";
    public const string DefaultHighlightColorHex = "#FFFF00";

    public static FindFormat None { get; } = new();

    public bool IsEmpty => !HasCharacterFormatting && ParagraphStyleId is null;

    public bool HasCharacterFormatting =>
        Bold is not null
        || Italic is not null
        || Underline is not null
        || FontFamily is not null
        || FontSizePt is not null
        || ColorHex is not null
        || Highlight is not null;

    /// <summary>
    /// Whether text with the given <em>effective</em> character formatting, in a paragraph of the
    /// given style, satisfies these criteria.
    /// </summary>
    public bool Matches(RunFormatting formatting, string? paragraphStyleId)
    {
        ArgumentNullException.ThrowIfNull(formatting);

        if (Bold is { } bold && formatting.Bold != bold)
            return false;
        if (Italic is { } italic && formatting.Italic != italic)
            return false;
        if (Underline is { } underline && formatting.Underline != underline)
            return false;
        if (FontFamily is { } family
            && !string.Equals(family.Trim(), formatting.FontFamily?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (FontSizePt is { } size
            && (formatting.FontSizePt is not { } actualSize || Math.Abs(actualSize - size) > 0.01))
        {
            return false;
        }

        if (ColorHex is { } color && !SameColor(color, formatting.ColorHex))
            return false;

        if (Highlight is { } highlight)
        {
            var highlighted = !string.IsNullOrEmpty(formatting.HighlightColorHex);
            if (highlighted != highlight)
                return false;
            if (highlight && HighlightColorHex is { } highlightColor
                && !SameColor(highlightColor, formatting.HighlightColorHex))
            {
                return false;
            }
        }

        return ParagraphStyleId is not { } styleId || SameStyle(styleId, paragraphStyleId);
    }

    /// <summary>Applies the character members that are set onto <paramref name="formatting"/>.</summary>
    public RunFormatting ApplyTo(RunFormatting formatting)
    {
        ArgumentNullException.ThrowIfNull(formatting);

        var result = formatting;
        if (Bold is { } bold)
            result = result with { Bold = bold };
        if (Italic is { } italic)
            result = result with { Italic = italic };
        if (Underline is { } underline)
            result = result with { Underline = underline };
        if (FontFamily is { } family)
            result = result with { FontFamily = family.Trim() };
        if (FontSizePt is { } size)
            result = result with { FontSizePt = size };
        if (ColorHex is { } color)
            result = result with { ColorHex = NormalizeColor(color), ThemeColor = null };
        if (Highlight is { } highlight)
        {
            result = result with
            {
                HighlightColorHex = highlight
                    ? NormalizeColor(HighlightColorHex ?? DefaultHighlightColorHex)
                    : null,
            };
        }

        return result;
    }

    public static bool SameStyle(string? left, string? right) =>
        string.Equals(
            string.IsNullOrEmpty(left) ? DefaultParagraphStyleId : left,
            string.IsNullOrEmpty(right) ? DefaultParagraphStyleId : right,
            StringComparison.OrdinalIgnoreCase);

    public static bool SameColor(string? left, string? right) =>
        NormalizeColor(left) is { } a
        && NormalizeColor(right) is { } b
        && string.Equals(a, b, StringComparison.Ordinal);

    /// <summary>Normalises "ff0000" / "#FF0000" / "#FFFF0000" to "#FF0000"; null when not a colour.</summary>
    public static string? NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
            return null;

        var hex = color.Trim().TrimStart('#');
        if (hex.Length == 8)
            hex = hex[2..];
        if (hex.Length != 6 || !hex.All(Uri.IsHexDigit))
            return null;
        return "#" + hex.ToUpperInvariant();
    }

    /// <summary>
    /// The formatting actually in effect for <paramref name="run"/>: its direct formatting, then its
    /// character style, then the paragraph style chain, then the document default. Toggle properties
    /// inherit by logical OR, exactly as <see cref="RunFormatting"/> documents.
    /// </summary>
    public static RunFormatting EffectiveFormatting(TextDocument? document, Paragraph paragraph, Run run)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(run);

        var formatting = run.Formatting;
        if (document is null)
            return formatting;

        foreach (var style in StyleChain(document, run.StyleId))
            formatting = Inherit(formatting, style.Run);
        foreach (var style in StyleChain(document, paragraph.StyleId ?? DefaultParagraphStyleId))
            formatting = Inherit(formatting, style.Run);
        return Inherit(formatting, document.DefaultRun);
    }

    private static IEnumerable<DocumentStyle> StyleChain(TextDocument document, string? styleId)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (styleId is { Length: > 0 }
            && seen.Add(styleId)
            && document.Styles.TryGetValue(styleId, out var style))
        {
            yield return style;
            styleId = style.BasedOnStyleId;
        }
    }

    private static RunFormatting Inherit(RunFormatting direct, RunFormatting inherited) =>
        direct with
        {
            Bold = direct.Bold || inherited.Bold,
            Italic = direct.Italic || inherited.Italic,
            Underline = direct.Underline || inherited.Underline,
            FontFamily = direct.FontFamily ?? inherited.FontFamily,
            FontSizePt = direct.FontSizePt ?? inherited.FontSizePt,
            ColorHex = direct.ColorHex ?? inherited.ColorHex,
            HighlightColorHex = direct.HighlightColorHex ?? inherited.HighlightColorHex,
        };
}

/// <summary>What to look for: text (with options) and/or formatting.</summary>
public sealed record FindCriteria(string Term, TextSearchOptions Options, FindFormat? Format = null)
{
    public bool HasFormat => Format is { IsEmpty: false };

    /// <summary>Word's formatting-only search: an empty Find what box with formatting set.</summary>
    public bool IsFormatOnly => Term.Length == 0 && HasFormat;

    public bool IsEmpty => Term.Length == 0 && !HasFormat;
}

/// <summary>
/// One uniformly formatted stretch of a search haystack. <see cref="IsStructural"/> marks a paragraph
/// mark, cell mark or object placeholder: such a segment never has to satisfy formatting criteria and
/// always ends a formatting-only match.
/// </summary>
public readonly record struct FindTextSegment(
    string Text,
    RunFormatting Formatting,
    string? ParagraphStyleId,
    bool IsStructural = false);

/// <summary>
/// Text-and-formatting matching over a segmented haystack. Shared by every Find/Replace consumer: a
/// host projects whatever it searches (a model paragraph, a rendered story) into
/// <see cref="FindTextSegment"/>s and gets back spans over the concatenated segment text.
/// </summary>
public static class FormattedTextSearch
{
    public static IReadOnlyList<(int Start, int Length)> FindAll(
        IReadOnlyList<FindTextSegment> segments,
        FindCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(criteria);

        if (criteria.IsEmpty || segments.Count == 0)
            return [];
        if (criteria.IsFormatOnly)
            return FindFormattedSpans(segments, criteria.Format!);

        var text = string.Concat(segments.Select(segment => segment.Text));
        var matches = TextSearch.FindAll(text, criteria.Term, criteria.Options);
        if (!criteria.HasFormat)
            return matches.ToList();

        var starts = SegmentStarts(segments);
        return matches
            .Where(match => RangeSatisfies(segments, starts, match.Start, match.Length, criteria.Format!))
            .ToList();
    }

    /// <summary>Matches inside one model paragraph, as spans over <see cref="Paragraph.PlainText"/>.</summary>
    public static IReadOnlyList<(int Start, int Length)> FindAll(
        TextDocument? document,
        Paragraph paragraph,
        FindCriteria criteria) =>
        FindAll(ProjectParagraph(document, paragraph, criteria.HasFormat), criteria);

    /// <summary>
    /// Projects a model paragraph so that segment offsets equal <see cref="Paragraph.PlainText"/>
    /// offsets. A drawing run's text is only a mirror of content that lives elsewhere (a text box is
    /// searched as its own story), so it is masked with <see cref="FindSpecialCodes.ObjectPlaceholder"/>.
    /// </summary>
    public static IReadOnlyList<FindTextSegment> ProjectParagraph(
        TextDocument? document,
        Paragraph paragraph,
        bool resolveFormatting = true)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        var segments = new List<FindTextSegment>(paragraph.Runs.Count);
        foreach (var run in paragraph.Runs)
        {
            var text = run.Text;
            if (text.Length == 0)
                continue;

            if (IsDrawingMirror(run))
            {
                segments.Add(new FindTextSegment(
                    new string(FindSpecialCodes.ObjectPlaceholder, text.Length),
                    RunFormatting.Default,
                    paragraph.StyleId,
                    IsStructural: true));
                continue;
            }

            segments.Add(new FindTextSegment(
                text,
                resolveFormatting
                    ? FindFormat.EffectiveFormatting(document, paragraph, run)
                    : run.Formatting,
                paragraph.StyleId));
        }

        return segments;
    }

    private static bool IsDrawingMirror(Run run) =>
        run.Shape is not null
        || run.WordArt is not null
        || run.DrawingGroup is not null
        || run.Equation is not null;

    private static List<(int Start, int Length)> FindFormattedSpans(
        IReadOnlyList<FindTextSegment> segments,
        FindFormat format)
    {
        var spans = new List<(int Start, int Length)>();
        var offset = 0;
        var spanStart = -1;
        foreach (var segment in segments)
        {
            var matches = !segment.IsStructural
                && segment.Text.Length > 0
                && format.Matches(segment.Formatting, segment.ParagraphStyleId);
            if (matches)
            {
                if (spanStart < 0)
                    spanStart = offset;
            }
            else if (spanStart >= 0 && (segment.IsStructural || segment.Text.Length > 0))
            {
                spans.Add((spanStart, offset - spanStart));
                spanStart = -1;
            }

            offset += segment.Text.Length;
        }

        if (spanStart >= 0)
            spans.Add((spanStart, offset - spanStart));
        return spans;
    }

    private static int[] SegmentStarts(IReadOnlyList<FindTextSegment> segments)
    {
        var starts = new int[segments.Count];
        var offset = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            starts[i] = offset;
            offset += segments[i].Text.Length;
        }

        return starts;
    }

    private static bool RangeSatisfies(
        IReadOnlyList<FindTextSegment> segments,
        int[] starts,
        int start,
        int length,
        FindFormat format)
    {
        var end = start + length;
        var index = Array.BinarySearch(starts, start);
        if (index < 0)
            index = Math.Max(0, ~index - 1);

        for (; index < segments.Count && starts[index] < end; index++)
        {
            var segment = segments[index];
            if (segment.Text.Length == 0 || starts[index] + segment.Text.Length <= start)
                continue;
            if (!segment.IsStructural && !format.Matches(segment.Formatting, segment.ParagraphStyleId))
                return false;
        }

        return true;
    }
}
