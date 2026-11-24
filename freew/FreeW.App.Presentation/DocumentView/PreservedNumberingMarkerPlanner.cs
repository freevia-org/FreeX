using System.Xml.Linq;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.DocumentView;

/// <summary>
/// Produces display-only markers for Word numbering definitions that FreeW preserves verbatim rather
/// than mapping to one of its native <see cref="ListKind"/> values. The document model remains
/// authoritative: this planner never changes paragraph text, styles, or numbering XML.
/// </summary>
public static class PreservedNumberingMarkerPlanner
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const int LevelCount = MultiLevelListFormat.LevelCount;

    public static IReadOnlyDictionary<int, PreservedNumberingMarkerPlan> Build(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var paragraphMarkers = BuildByParagraph(document);
        var result = new Dictionary<int, PreservedNumberingMarkerPlan>();
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            if (document.Blocks[blockIndex] is Paragraph paragraph
                && paragraphMarkers.TryGetValue(paragraph, out var marker))
                result[blockIndex] = marker;
        }

        return result;
    }

    /// <summary>
    /// Produces the same display-only markers keyed by their model paragraph. Unlike the legacy
    /// top-level index map, this includes paragraphs inside table cells so Word numbering continues
    /// through ordinary body and table content in document order.
    /// </summary>
    public static IReadOnlyDictionary<Paragraph, PreservedNumberingMarkerPlan> BuildByParagraph(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Preserved.OriginalNumbering is not { } numbering)
            return EmptyByParagraph;

        var definitions = ReadDefinitions(numbering);
        if (definitions.Count == 0)
            return EmptyByParagraph;

        var counters = new Dictionary<int, NumberingCounterState>();
        var result = new Dictionary<Paragraph, PreservedNumberingMarkerPlan>();
        foreach (var paragraph in TextDocumentStoryTraversal.EnumerateBlockParagraphs(
                     document.Blocks,
                     TextDocumentStoryTraversalOptions.PreserveDuplicateParagraphs))
        {
            if (paragraph.Formatting.ListKind != ListKind.None
                || !TryResolveNumbering(document, paragraph, out var preserved)
                || !definitions.TryGetValue(preserved.NumId, out var definition))
            {
                continue;
            }

            var level = Math.Clamp(preserved.Ilvl, 0, LevelCount - 1);
            if (!definition.Levels.TryGetValue(level, out var levelDefinition))
                continue;

            if (!counters.TryGetValue(preserved.NumId, out var state))
                counters[preserved.NumId] = state = new NumberingCounterState();

            state.Values[level] = state.Initialized[level] ? state.Values[level] + 1 : levelDefinition.StartAt;
            state.Initialized[level] = true;
            for (var deeper = level + 1; deeper < LevelCount; deeper++)
            {
                if (definition.Levels.TryGetValue(deeper, out var deeperDefinition)
                    && ListLevelRestartPolicy.ShouldRestart(deeper, level, deeperDefinition.RestartAfterLevel))
                {
                    state.Values[deeper] = 0;
                    state.Initialized[deeper] = false;
                }
            }

            var marker = FormatMarker(levelDefinition.LevelText, definition, state, level);
            if (!string.IsNullOrEmpty(marker))
                result[paragraph] = new PreservedNumberingMarkerPlan(marker, level, levelDefinition.FontFamily);
        }

        return result;
    }

    private static bool TryResolveNumbering(
        TextDocument document,
        Paragraph paragraph,
        out PreservedNumbering numbering)
    {
        if (paragraph.PreservedNumbering is { } direct)
        {
            numbering = direct;
            return true;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var styleId = paragraph.StyleId;
        while (!string.IsNullOrWhiteSpace(styleId)
               && seen.Add(styleId)
               && document.Styles.TryGetValue(styleId, out var style))
        {
            if (style.PreservedNumbering is { } inherited)
            {
                numbering = inherited;
                return true;
            }

            styleId = style.BasedOnStyleId;
        }

        numbering = default;
        return false;
    }

    private static Dictionary<int, NumberingDefinition> ReadDefinitions(XElement numbering)
    {
        var abstractDefinitions = new Dictionary<int, IReadOnlyDictionary<int, NumberingLevelDefinition>>();
        foreach (var abstractNumbering in numbering.Elements(W + "abstractNum"))
        {
            if (!TryReadInt(abstractNumbering.Attribute(W + "abstractNumId")?.Value, out var abstractNumId))
                continue;

            var levels = new Dictionary<int, NumberingLevelDefinition>();
            foreach (var level in abstractNumbering.Elements(W + "lvl"))
            {
                if (TryReadLevel(level, out var levelIndex, out var levelDefinition))
                    levels[levelIndex] = levelDefinition;
            }

            abstractDefinitions[abstractNumId] = levels;
        }

        var definitions = new Dictionary<int, NumberingDefinition>();
        foreach (var number in numbering.Elements(W + "num"))
        {
            if (!TryReadInt(number.Attribute(W + "numId")?.Value, out var numId)
                || !TryReadInt(number.Element(W + "abstractNumId")?.Attribute(W + "val")?.Value, out var abstractNumId)
                || !abstractDefinitions.TryGetValue(abstractNumId, out var baseLevels))
            {
                continue;
            }

            var levels = baseLevels.ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (var overrideElement in number.Elements(W + "lvlOverride"))
            {
                if (!TryReadInt(overrideElement.Attribute(W + "ilvl")?.Value, out var levelIndex)
                    || levelIndex is < 0 or >= LevelCount)
                {
                    continue;
                }

                var baseLevel = levels.TryGetValue(levelIndex, out var inherited)
                    ? inherited
                    : new NumberingLevelDefinition(0, "decimal", "%" + (levelIndex + 1), null, null, false);
                var effectiveLevel = baseLevel;
                if (overrideElement.Element(W + "lvl") is { } overriddenLevel
                    && TryReadLevel(overriddenLevel, out _, out var replacement))
                {
                    // Word ignores w:start nested inside a full level override. Its separate
                    // startOverride takes precedence, including when both children are present.
                    effectiveLevel = replacement with
                    {
                        StartAt = baseLevel.StartAt,
                        // Word also ignores lvlRestart inside a full level override.
                        RestartAfterLevel = baseLevel.RestartAfterLevel,
                    };
                }

                if (overrideElement.Element(W + "startOverride") is { } startOverride)
                    effectiveLevel = effectiveLevel with { StartAt = ReadStartingValue(startOverride) };
                levels[levelIndex] = effectiveLevel;
            }

            definitions[numId] = new NumberingDefinition(levels);
        }

        return definitions;
    }

    private static bool TryReadLevel(XElement level, out int levelIndex, out NumberingLevelDefinition definition)
    {
        definition = default;
        if (!TryReadInt(level.Attribute(W + "ilvl")?.Value, out levelIndex)
            || levelIndex is < 0 or >= LevelCount)
        {
            return false;
        }

        var startAt = ReadStartingValue(level.Element(W + "start"));
        var numberFormat = level.Element(W + "numFmt")?.Attribute(W + "val")?.Value ?? "decimal";
        var levelText = level.Element(W + "lvlText")?.Attribute(W + "val")?.Value ?? "%" + (levelIndex + 1);
        // The level's own run font (w:lvl/w:rPr/w:rFonts) draws the marker: a Symbol/Wingdings bullet is a
        // private-use code point (U+F0B7) that only that font maps to a glyph.
        var fonts = level.Element(W + "rPr")?.Element(W + "rFonts");
        var fontFamily = fonts?.Attribute(W + "ascii")?.Value ?? fonts?.Attribute(W + "hAnsi")?.Value;
        var restartAfterLevel = TryReadInt(level.Element(W + "lvlRestart")?.Attribute(W + "val")?.Value, out var restart)
            ? restart : (int?)null;
        var legal = level.Element(W + "isLgl") is { } isLegal
            && isLegal.Attribute(W + "val")?.Value is not ("0" or "false" or "off");
        definition = new NumberingLevelDefinition(startAt, numberFormat, levelText,
            string.IsNullOrWhiteSpace(fontFamily) ? null : fontFamily, restartAfterLevel, legal);
        return true;
    }

    private static string FormatMarker(
        string template,
        NumberingDefinition definition,
        NumberingCounterState counters,
        int currentLevel)
    {
        // Word treats bullet text literally, including any apparent %x placeholders.
        if (definition.Levels[currentLevel].NumberFormat == "bullet")
            return template;

        // Word ignores the entire lvlText when it references a deeper level, rather than
        // substituting a default counter for that placeholder (MS-OI29500 §2.1.283).
        for (var index = 0; index + 1 < template.Length; index++)
        {
            if (template[index] == '%' && template[index + 1] is >= '1' and <= '9'
                && template[index + 1] - '1' > currentLevel)
            {
                return string.Empty;
            }
        }

        var marker = template;
        for (var referencedLevel = 0; referencedLevel < LevelCount; referencedLevel++)
        {
            var token = "%" + (referencedLevel + 1);
            if (!marker.Contains(token, StringComparison.Ordinal))
                continue;

            var level = definition.Levels.TryGetValue(referencedLevel, out var referencedDefinition)
                ? referencedDefinition
                : new NumberingLevelDefinition(1, "decimal", token, null, null, false);
            var value = referencedLevel > currentLevel || !counters.Initialized[referencedLevel]
                ? level.StartAt
                : counters.Values[referencedLevel];
            var format = definition.Levels[currentLevel].IsLegal && level.NumberFormat != "none"
                ? "decimal" : level.NumberFormat;
            marker = marker.Replace(token, FormatNumber(value, format), StringComparison.Ordinal);
        }

        return marker;
    }

    private static string FormatNumber(int value, string numberFormat)
    {
        if (string.Equals(numberFormat, "none", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        if (string.Equals(numberFormat, "bullet", StringComparison.OrdinalIgnoreCase))
            return "•";
        return MultiLevelListMarkerFormatter.FormatNumber(
            value, MultiLevelListMarkerFormatter.FromOoxmlToken(numberFormat));
    }

    private static int ReadStartingValue(XElement? element)
    {
        if (element is null)
            return 0;
        return TryReadInt(element.Attribute(W + "val")?.Value, out var value) && value >= 0 ? value : 1;
    }

    private static bool TryReadInt(string? value, out int result) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out result);

    private static IReadOnlyDictionary<int, PreservedNumberingMarkerPlan> Empty { get; } =
        new Dictionary<int, PreservedNumberingMarkerPlan>();

    private static IReadOnlyDictionary<Paragraph, PreservedNumberingMarkerPlan> EmptyByParagraph { get; } =
        new Dictionary<Paragraph, PreservedNumberingMarkerPlan>();

    private sealed record NumberingDefinition(IReadOnlyDictionary<int, NumberingLevelDefinition> Levels);

    private readonly record struct NumberingLevelDefinition(
        int StartAt, string NumberFormat, string LevelText, string? FontFamily, int? RestartAfterLevel, bool IsLegal);

    private sealed class NumberingCounterState
    {
        public int[] Values { get; } = new int[LevelCount];
        public bool[] Initialized { get; } = new bool[LevelCount];
    }
}

/// <param name="FontFamily">The numbering level's own font (<c>w:lvl/w:rPr/w:rFonts</c>), or null to use the paragraph's.</param>
public readonly record struct PreservedNumberingMarkerPlan(string Text, int Level, string? FontFamily = null);
