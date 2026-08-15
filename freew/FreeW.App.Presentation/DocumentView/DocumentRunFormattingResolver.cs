using System.Globalization;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Resolves effective character formatting through the document default and a paragraph's complete
/// based-on style chain. Renderers and accessibility projections consume this single cascade instead
/// of maintaining subtly different WPF/Avalonia copies.
/// </summary>
public static class DocumentRunFormattingResolver
{
    public static RunFormatting Resolve(
        TextDocument document,
        Paragraph paragraph,
        RunFormatting directFormatting)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(directFormatting);

        var resolved = document.DefaultRun;
        foreach (var style in StyleChain(document, paragraph.StyleId))
            resolved = Overlay(resolved, style.Run);
        return Overlay(resolved, directFormatting);
    }

    private static IEnumerable<DocumentStyle> StyleChain(TextDocument document, string? styleId)
    {
        if (string.IsNullOrWhiteSpace(styleId))
            yield break;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var chain = new List<DocumentStyle>();
        var current = styleId;
        while (!string.IsNullOrWhiteSpace(current)
            && seen.Add(current)
            && document.Styles.TryGetValue(current, out var style))
        {
            chain.Add(style);
            current = style.BasedOnStyleId;
        }

        for (var index = chain.Count - 1; index >= 0; index--)
            yield return chain[index];
    }

    private static RunFormatting Overlay(RunFormatting inherited, RunFormatting direct) => inherited with
    {
        FontFamily = direct.FontFamily ?? inherited.FontFamily,
        FontSizePt = direct.FontSizePt ?? inherited.FontSizePt,
        ColorHex = direct.ColorHex ?? inherited.ColorHex,
        ThemeColor = direct.ColorHex is not null ? direct.ThemeColor : inherited.ThemeColor,
        HighlightColorHex = direct.HighlightColorHex ?? inherited.HighlightColorHex,
        CharacterBorder = direct.CharacterBorder ?? inherited.CharacterBorder,
        CharacterShadingHex = direct.CharacterShadingHex ?? inherited.CharacterShadingHex,
        CharacterShadingPattern = direct.CharacterShadingHex is not null
            ? direct.CharacterShadingPattern
            : inherited.CharacterShadingPattern,
        LanguageTag = direct.LanguageTag ?? inherited.LanguageTag,
        VerticalAlign = direct.VerticalAlign != VerticalAlign.Baseline
            ? direct.VerticalAlign
            : inherited.VerticalAlign,
        Rtl = inherited.Rtl || direct.Rtl,
        CharacterSpacingPt = direct.CharacterSpacingPt != 0
            ? direct.CharacterSpacingPt
            : inherited.CharacterSpacingPt,
        KerningMinSizePt = direct.KerningMinSizePt ?? inherited.KerningMinSizePt,
        PositionPt = direct.PositionPt != 0 ? direct.PositionPt : inherited.PositionPt,
        Ligatures = direct.Ligatures != LigatureMode.None ? direct.Ligatures : inherited.Ligatures,
        NumberForm = direct.NumberForm != NumberForm.Default ? direct.NumberForm : inherited.NumberForm,
        NumberSpacing = direct.NumberSpacing != NumberSpacing.Default ? direct.NumberSpacing : inherited.NumberSpacing,
        StylisticSet = direct.StylisticSet ?? inherited.StylisticSet,
        Bold = inherited.Bold || direct.Bold,
        Italic = inherited.Italic || direct.Italic,
        Underline = inherited.Underline || direct.Underline,
        Strikethrough = inherited.Strikethrough || direct.Strikethrough,
        DoubleStrikethrough = inherited.DoubleStrikethrough || direct.DoubleStrikethrough,
        Hidden = inherited.Hidden || direct.Hidden,
        WebHidden = inherited.WebHidden || direct.WebHidden,
        NoProof = inherited.NoProof || direct.NoProof,
        SmallCaps = inherited.SmallCaps || direct.SmallCaps,
        AllCaps = inherited.AllCaps || direct.AllCaps,
    };
}

/// <summary>
/// Converts effective character formatting into a stable spoken fallback for automation stacks that
/// cannot expose WPF's TextPattern formatting attributes.
/// </summary>
public static class DocumentRunAccessibilityFormatter
{
    public static string Describe(RunFormatting formatting, Run run)
    {
        ArgumentNullException.ThrowIfNull(formatting);
        ArgumentNullException.ThrowIfNull(run);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(formatting.FontFamily))
            parts.Add(formatting.FontFamily.Trim());
        if (formatting.FontSizePt is { } size)
            parts.Add($"{size.ToString("0.##", CultureInfo.InvariantCulture)} point");
        Add(formatting.Bold, "bold");
        Add(formatting.Italic, "italic");
        Add(formatting.Underline, "underlined");
        Add(formatting.DoubleStrikethrough, "double strikethrough");
        Add(!formatting.DoubleStrikethrough && formatting.Strikethrough, "strikethrough");
        Add(formatting.SmallCaps, "small caps");
        Add(formatting.AllCaps, "all caps");
        Add(formatting.Rtl, "right to left");
        Add(formatting.Hidden, "hidden text");
        Add(formatting.WebHidden, "hidden in web layout");
        Add(formatting.NoProof, "proofing disabled");
        if (formatting.VerticalAlign != VerticalAlign.Baseline)
            parts.Add(formatting.VerticalAlign == VerticalAlign.Superscript ? "superscript" : "subscript");
        if (formatting.PositionPt != 0)
            parts.Add(formatting.PositionPt > 0
                ? $"raised {Points(formatting.PositionPt)}"
                : $"lowered {Points(-formatting.PositionPt)}");
        if (!string.IsNullOrWhiteSpace(formatting.ColorHex))
            parts.Add($"text color {formatting.ColorHex}");
        if (formatting.ThemeColor is { } themeColor)
            parts.Add(DescribeThemeColor(themeColor));
        if (!string.IsNullOrWhiteSpace(formatting.CharacterShadingHex))
            parts.Add(DescribeShading(formatting.CharacterShadingHex, formatting.CharacterShadingPattern));
        else if (!string.IsNullOrWhiteSpace(formatting.HighlightColorHex))
            parts.Add($"highlight {formatting.HighlightColorHex}");
        if (formatting.CharacterBorder is { } border)
            parts.Add(DescribeBorder(border));
        if (formatting.CharacterSpacingPt != 0)
            parts.Add(formatting.CharacterSpacingPt > 0
                ? $"expanded by {Points(formatting.CharacterSpacingPt)}"
                : $"condensed by {Points(-formatting.CharacterSpacingPt)}");
        if (formatting.KerningMinSizePt is { } kerning)
            parts.Add(kerning <= 0
                ? "kerning disabled"
                : $"kerning at {Points(kerning)} and above");
        if (formatting.Ligatures != LigatureMode.None)
            parts.Add(DescribeLigatures(formatting.Ligatures));
        if (formatting.NumberForm != NumberForm.Default)
            parts.Add(formatting.NumberForm == NumberForm.OldStyle ? "old-style numerals" : "lining numerals");
        if (formatting.NumberSpacing != NumberSpacing.Default)
            parts.Add(formatting.NumberSpacing == NumberSpacing.Tabular
                ? "tabular numeral spacing"
                : "proportional numeral spacing");
        if (formatting.StylisticSet is { } stylisticSet)
            parts.Add($"stylistic set {stylisticSet.ToString(CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(formatting.LanguageTag))
            parts.Add($"language {formatting.LanguageTag}");
        if (run.Revision != RevisionKind.None)
            parts.Add(run.Revision == RevisionKind.Inserted ? "tracked insertion" : "tracked deletion");
        if (run.FormatRevision is not null)
            parts.Add("tracked formatting change");

        return parts.Count == 0 ? "Character formatting" : string.Join(", ", parts);

        void Add(bool condition, string description)
        {
            if (condition)
                parts.Add(description);
        }
    }

    private static string Points(double value) =>
        $"{value.ToString("0.##", CultureInfo.InvariantCulture)} point";

    private static string DescribeThemeColor(WordThemeColor themeColor)
    {
        var description = $"theme color {themeColor.ThemeToken}";
        if (!string.IsNullOrWhiteSpace(themeColor.TintHex))
            description += $" tint {themeColor.TintHex}";
        if (!string.IsNullOrWhiteSpace(themeColor.ShadeHex))
            description += $" shade {themeColor.ShadeHex}";
        return description;
    }

    private static string DescribeShading(string colorHex, ShadingPattern pattern) => pattern switch
    {
        ShadingPattern.Pct10 => $"character shading {colorHex}, 10 percent pattern",
        ShadingPattern.Pct25 => $"character shading {colorHex}, 25 percent pattern",
        ShadingPattern.Pct50 => $"character shading {colorHex}, 50 percent pattern",
        ShadingPattern.Solid => $"character shading {colorHex}, solid pattern",
        _ => $"character shading {colorHex}",
    };

    private static string DescribeBorder(ParagraphBorder border)
    {
        var edges = border.BottomOnly || (!border.Top && !border.Left && border.Bottom && !border.Right)
            ? "bottom edge"
            : border.Top && border.Left && border.Bottom && border.Right
                ? "box"
                : DescribeBorderEdges(border);
        return $"character border {BorderLineStyles.ToToken(border.LineStyle)}, " +
               $"{Points(border.WidthPt)}, {border.ColorHex}, {edges}";
    }

    private static string DescribeBorderEdges(ParagraphBorder border)
    {
        var edges = new List<string>(4);
        if (border.Top)
            edges.Add("top");
        if (border.Left)
            edges.Add("left");
        if (border.Bottom)
            edges.Add("bottom");
        if (border.Right)
            edges.Add("right");
        return edges.Count == 0 ? "no edges" : $"{string.Join(" and ", edges)} edges";
    }

    private static string DescribeLigatures(LigatureMode mode) => mode switch
    {
        LigatureMode.NoneExplicit => "ligatures disabled",
        LigatureMode.Standard => "standard ligatures",
        LigatureMode.Contextual => "contextual ligatures",
        LigatureMode.StandardContextual => "standard and contextual ligatures",
        LigatureMode.Historical => "historical ligatures",
        LigatureMode.Discretional => "discretionary ligatures",
        LigatureMode.StandardHistorical => "standard and historical ligatures",
        LigatureMode.ContextualHistorical => "contextual and historical ligatures",
        LigatureMode.StandardContextualHistorical => "standard, contextual, and historical ligatures",
        LigatureMode.ContextualDiscretional => "contextual and discretionary ligatures",
        LigatureMode.StandardDiscretional => "standard and discretionary ligatures",
        LigatureMode.StandardContextualDiscretional => "standard, contextual, and discretionary ligatures",
        LigatureMode.HistoricalDiscretional => "historical and discretionary ligatures",
        LigatureMode.StandardHistoricalDiscretional => "standard, historical, and discretionary ligatures",
        LigatureMode.ContextualHistoricalDiscretional => "contextual, historical, and discretionary ligatures",
        LigatureMode.All => "all ligatures",
        _ => "ligatures inherited",
    };
}
