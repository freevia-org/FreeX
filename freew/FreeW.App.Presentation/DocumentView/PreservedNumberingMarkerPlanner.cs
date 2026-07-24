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

        if (document.Preserved.OriginalNumbering is not { } numbering)
            return Empty;

        var definitions = ReadDefinitions(numbering);
        if (definitions.Count == 0)
            return Empty;

        var counters = new Dictionary<int, int[]>();
        var result = new Dictionary<int, PreservedNumberingMarkerPlan>();
        for (var blockIndex = 0; blockIndex < document.Blocks.Count; blockIndex++)
        {
            if (document.Blocks[blockIndex] is not Paragraph paragraph
                || paragraph.Formatting.ListKind != ListKind.None
                || !TryResolveNumbering(document, paragraph, out var preserved)
                || !definitions.TryGetValue(preserved.NumId, out var definition))
            {
                continue;
            }

            var level = Math.Clamp(preserved.Ilvl, 0, LevelCount - 1);
            if (!definition.Levels.TryGetValue(level, out var levelDefinition))
                continue;

            if (!counters.TryGetValue(preserved.NumId, out var state))
                counters[preserved.NumId] = state = new int[LevelCount];

            state[level] = state[level] == 0 ? levelDefinition.StartAt : state[level] + 1;
            for (var deeper = level + 1; deeper < LevelCount; deeper++)
                state[deeper] = 0;

            var marker = FormatMarker(levelDefinition.LevelText, definition, state, level);
            if (!string.IsNullOrEmpty(marker))
                result[blockIndex] = new PreservedNumberingMarkerPlan(marker, level);
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
                    || levelIndex is < 0 or >= LevelCount
                    || !levels.TryGetValue(levelIndex, out var baseLevel))
                {
                    continue;
                }

                if (overrideElement.Element(W + "lvl") is { } overriddenLevel
                    && TryReadLevel(overriddenLevel, out _, out var replacement))
                {
                    levels[levelIndex] = replacement;
                    continue;
                }

                if (TryReadInt(overrideElement.Element(W + "startOverride")?.Attribute(W + "val")?.Value, out var startAt))
                    levels[levelIndex] = baseLevel with { StartAt = Math.Max(1, startAt) };
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

        var startAt = TryReadInt(level.Element(W + "start")?.Attribute(W + "val")?.Value, out var start)
            ? Math.Max(1, start)
            : 1;
        var numberFormat = level.Element(W + "numFmt")?.Attribute(W + "val")?.Value ?? "decimal";
        var levelText = level.Element(W + "lvlText")?.Attribute(W + "val")?.Value ?? "%" + (levelIndex + 1);
        definition = new NumberingLevelDefinition(startAt, numberFormat, levelText);
        return true;
    }

    private static string FormatMarker(
        string template,
        NumberingDefinition definition,
        IReadOnlyList<int> counters,
        int currentLevel)
    {
        var marker = template;
        for (var referencedLevel = 0; referencedLevel < LevelCount; referencedLevel++)
        {
            var token = "%" + (referencedLevel + 1);
            if (!marker.Contains(token, StringComparison.Ordinal))
                continue;

            var level = definition.Levels.TryGetValue(referencedLevel, out var referencedDefinition)
                ? referencedDefinition
                : new NumberingLevelDefinition(1, "decimal", token);
            var value = referencedLevel > currentLevel || counters[referencedLevel] == 0
                ? level.StartAt
                : counters[referencedLevel];
            marker = marker.Replace(token, FormatNumber(value, level.NumberFormat), StringComparison.Ordinal);
        }

        return marker;
    }

    private static string FormatNumber(int value, string numberFormat) =>
        string.Equals(numberFormat, "bullet", StringComparison.OrdinalIgnoreCase)
            ? "•"
            : MultiLevelListMarkerFormatter.FormatNumber(
                value,
                MultiLevelListMarkerFormatter.FromOoxmlToken(numberFormat));

    private static bool TryReadInt(string? value, out int result) =>
        int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out result);

    private static IReadOnlyDictionary<int, PreservedNumberingMarkerPlan> Empty { get; } =
        new Dictionary<int, PreservedNumberingMarkerPlan>();

    private sealed record NumberingDefinition(IReadOnlyDictionary<int, NumberingLevelDefinition> Levels);

    private readonly record struct NumberingLevelDefinition(int StartAt, string NumberFormat, string LevelText);
}

public readonly record struct PreservedNumberingMarkerPlan(string Text, int Level);
