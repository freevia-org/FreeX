namespace FreeW.Core.Model;

/// <summary>
/// One labelled property inside a <see cref="RevealFormattingSection"/> (e.g. Label "Font" → Value
/// "Calibri"). Mirrors a single line in Word's Reveal Formatting (Shift+F1) pane.
/// </summary>
/// <param name="Label">The property name shown on the left (e.g. "Alignment").</param>
/// <param name="Value">The formatted, human-readable value shown on the right (e.g. "Centered").</param>
public sealed record RevealFormattingItem(string Label, string Value);

/// <summary>
/// One heading-grouped block of the Reveal Formatting summary (Font / Paragraph / Section), holding the
/// label/value pairs under that heading. Mirrors the collapsible sections of Word's pane.
/// </summary>
/// <param name="Heading">The section heading (e.g. "FONT").</param>
/// <param name="Items">The properties listed under the heading, in display order.</param>
public sealed record RevealFormattingSection(string Heading, IReadOnlyList<RevealFormattingItem> Items);

/// <summary>
/// Pure (WPF-free) producer of the formatting summary shown in FreeW's Reveal Formatting side pane —
/// the read-only mirror of Word's Shift+F1 pane. Given the <em>effective</em> run formatting at the
/// caret/selection, the paragraph formatting of the caret's paragraph, and the section's page settings,
/// it describes the FONT, PARAGRAPH and SECTION groups as a list of heading-grouped label/value strings.
///
/// Keeping the description here — rather than in the WPF pane — means it can be unit-tested without a WPF
/// dependency, and the pane (MainWindow) just renders the returned sections. The run formatting passed in
/// is expected to be already resolved (run ?? style ?? document default), exactly as
/// <c>DocumentView.CurrentRunFormatting</c> reads it, so the pane shows the values actually in effect.
/// </summary>
public static class RevealFormatting
{
    /// <summary>
    /// Describe the effective formatting of the current selection as the FONT / PARAGRAPH / SECTION
    /// sections of the Reveal Formatting pane. All inputs are points (the model unit); sizes are shown in
    /// points, indents/margins in inches (Word's pane convention), so the strings read like Word's.
    /// </summary>
    /// <param name="run">The effective run (character) formatting at the caret/selection.</param>
    /// <param name="paragraph">The effective paragraph formatting of the caret's paragraph.</param>
    /// <param name="page">The page settings of the caret's section (margins / columns / paper).</param>
    public static IReadOnlyList<RevealFormattingSection> Describe(
        RunFormatting run, ParagraphFormatting paragraph, PageSettings page) =>
    [
        new RevealFormattingSection("FONT", DescribeFont(run)),
        new RevealFormattingSection("PARAGRAPH", DescribeParagraph(paragraph)),
        new RevealFormattingSection("SECTION", DescribeSection(page)),
    ];

    private static List<RevealFormattingItem> DescribeFont(RunFormatting run)
    {
        var items = new List<RevealFormattingItem>
        {
            new("Font", string.IsNullOrEmpty(run.FontFamily) ? "(default)" : run.FontFamily!),
            new("Size", FormatPoints(run.FontSizePt ?? 0)),
            new("Color", string.IsNullOrEmpty(run.ColorHex) ? "Automatic" : run.ColorHex!),
        };

        var effects = DescribeFontEffects(run);
        items.Add(new RevealFormattingItem("Effects", effects.Count == 0 ? "(none)" : string.Join(", ", effects)));

        if (!string.IsNullOrEmpty(run.HighlightColorHex))
            items.Add(new RevealFormattingItem("Highlight", run.HighlightColorHex!));

        return items;
    }

    private static List<string> DescribeFontEffects(RunFormatting run)
    {
        var effects = new List<string>();
        if (run.Bold) effects.Add("Bold");
        if (run.Italic) effects.Add("Italic");
        if (run.Underline) effects.Add("Underline");
        if (run.Strikethrough) effects.Add("Strikethrough");
        if (run.SmallCaps) effects.Add("Small caps");
        if (run.AllCaps) effects.Add("All caps");
        if (run.VerticalAlign == VerticalAlign.Superscript) effects.Add("Superscript");
        if (run.VerticalAlign == VerticalAlign.Subscript) effects.Add("Subscript");
        return effects;
    }

    private static List<RevealFormattingItem> DescribeParagraph(ParagraphFormatting p)
    {
        var items = new List<RevealFormattingItem>
        {
            new("Alignment", DescribeAlignment(p.Alignment)),
            new("Indentation", DescribeIndentation(p)),
            new("Spacing", DescribeSpacing(p)),
            new("Line spacing", DescribeLineSpacing(p)),
        };

        if (p.ListKind != ListKind.None)
            items.Add(new RevealFormattingItem("List", $"{DescribeList(p.ListKind)} (level {p.ListLevel + 1})"));

        if (p.Rtl)
            items.Add(new RevealFormattingItem("Direction", "Right-to-left"));

        return items;
    }

    private static string DescribeAlignment(TextAlignment alignment) => alignment switch
    {
        TextAlignment.Center => "Centered",
        TextAlignment.Right => "Right",
        TextAlignment.Justify => "Justified",
        _ => "Left",
    };

    private static string DescribeIndentation(ParagraphFormatting p)
    {
        var parts = new List<string>
        {
            $"Left {FormatInches(p.IndentLeftPt)}",
            $"Right {FormatInches(p.IndentRightPt)}",
        };
        if (p.FirstLineIndentPt > 0)
            parts.Add($"First line {FormatInches(p.FirstLineIndentPt)}");
        else if (p.FirstLineIndentPt < 0)
            parts.Add($"Hanging {FormatInches(-p.FirstLineIndentPt)}");
        return string.Join(", ", parts);
    }

    private static string DescribeSpacing(ParagraphFormatting p) =>
        $"Before {FormatPoints(p.SpaceBeforePt)}, After {FormatPoints(p.SpaceAfterPt)}";

    private static string DescribeLineSpacing(ParagraphFormatting p) => p.LineRule switch
    {
        LineSpacingRule.Exact => $"Exactly {FormatPoints(p.LineHeightPt)}",
        LineSpacingRule.AtLeast => $"At least {FormatPoints(p.LineHeightPt)}",
        _ => DescribeMultipleLineSpacing(p.LineSpacing),
    };

    private static string DescribeMultipleLineSpacing(double multiple) => multiple switch
    {
        <= 1.0 => "Single",
        1.5 => "1.5 lines",
        2.0 => "Double",
        _ => $"{Round(multiple)} lines",
    };

    private static string DescribeList(ListKind kind) => kind switch
    {
        ListKind.Bullet => "Bulleted",
        ListKind.Number => "Numbered",
        ListKind.MultiLevel => "Multilevel",
        _ => "None",
    };

    private static List<RevealFormattingItem> DescribeSection(PageSettings page)
    {
        var items = new List<RevealFormattingItem>
        {
            new("Margins",
                $"Top {FormatInches(page.MarginTopPt)}, Bottom {FormatInches(page.MarginBottomPt)}, " +
                $"Left {FormatInches(page.MarginLeftPt)}, Right {FormatInches(page.MarginRightPt)}"),
            new("Paper",
                $"{FormatInches(page.WidthPt)} × {FormatInches(page.HeightPt)} " +
                $"({(page.WidthPt > page.HeightPt ? "Landscape" : "Portrait")})"),
            new("Columns", DescribeColumns(page)),
        };
        return items;
    }

    private static string DescribeColumns(PageSettings page) =>
        page.ColumnCount <= 1
            ? "One"
            : $"{page.ColumnCount} columns (spacing {FormatInches(page.ColumnSpacingPt)})";

    /// <summary>Format a point value as e.g. "11 pt" / "11.5 pt", trimming a trailing ".0".</summary>
    private static string FormatPoints(double points) => $"{Round(points)} pt";

    /// <summary>Format a point measurement in inches (72 pt per inch), Word's pane unit for indents/margins.</summary>
    private static string FormatInches(double points) => $"{Round(points / 72.0)}\"";

    /// <summary>Round to two decimals and drop trailing zeros, so 0.50 → "0.5" and 1.00 → "1".</summary>
    private static string Round(double value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        return rounded.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
