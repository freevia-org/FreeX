using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class NumberFormatter
{
    private static readonly Regex SectionConditionRegex = new(
        @"^\s*(>=|<=|<>|>|<|=)\s*([+-]?(?:(?:\d+(?:\.\d*)?)|(?:\.\d+))(?:[eE][+-]?\d+)?)\s*$");

    private sealed record ParsedSection(string Format, string? ColorHex, FormatCondition? Condition);

    private const int SplitSectionsCacheSize = 1024;
    private static readonly SplitSectionsCacheEntry?[] SplitSectionsCache = new SplitSectionsCacheEntry[SplitSectionsCacheSize];
    private const int ParsedSectionsCacheSize = 1024;
    private static readonly ParsedSectionsCacheEntry?[] ParsedSectionsCache = new ParsedSectionsCacheEntry[ParsedSectionsCacheSize];

    private sealed record SplitSectionsCacheEntry(string Format, string[] Sections);
    private sealed record ParsedSectionsCacheEntry(
        string[] Sections,
        WorkbookIndexedColorPalette? IndexedColors,
        WorkbookTheme? Theme,
        ParsedSection[] ParsedSections,
        bool HasConditions);

    private sealed record FormatCondition(string Operator, double Value)
    {
        public bool Matches(double value) => Operator switch
        {
            ">"  => value > Value,
            ">=" => value >= Value,
            "<"  => value < Value,
            "<=" => value <= Value,
            "="  => value == Value,
            "<>" => value != Value,
            _    => false
        };
    }

    // Split format into sections separated by ';' that are not inside "" or []
    private static string[] SplitSections(string format)
    {
        var slot = StringComparer.Ordinal.GetHashCode(format) & (SplitSectionsCacheSize - 1);
        var cached = Volatile.Read(ref SplitSectionsCache[slot]);
        if (cached is not null && string.Equals(cached.Format, format, StringComparison.Ordinal))
            return cached.Sections;

        var sections = SplitSectionsUncached(format);
        Volatile.Write(ref SplitSectionsCache[slot], new SplitSectionsCacheEntry(format, sections));
        return sections;
    }

    private static string[] SplitSectionsUncached(string format) =>
        NumberFormatSectionTokenizer.Split(format);

    private static (ParsedSection Section, double DisplayValue) SelectPositionalSection(
        double value,
        ParsedSection[] sections)
    {
        if (value > 0 || sections.Length == 1)
            return (sections[0], value);

        if (value < 0)
        {
            if (sections.Length >= 2)
                return (sections[1], Math.Abs(value));

            return (sections[0], value);
        }

        if (sections.Length >= 3)
            return (sections[2], value);

        return (sections[0], value);
    }

    private static ParsedSection ParseSection(string section)
        => ParseSection(section, null, null);

    private static ParsedSection ParseSection(string section, WorkbookIndexedColorPalette? indexedColors)
        => ParseSection(section, indexedColors, null);

    private static ParsedSection[] ParseSections(
        string[] sections,
        WorkbookIndexedColorPalette? indexedColors,
        WorkbookTheme? theme,
        out bool hasConditions)
    {
        if (TryGetCachedParsedSections(sections, indexedColors, theme, out var cachedSections, out hasConditions))
            return cachedSections;

        var parsedSections = new ParsedSection[sections.Length];
        hasConditions = false;

        for (var i = 0; i < sections.Length; i++)
        {
            var parsedSection = ParseSection(sections[i], indexedColors, theme);
            parsedSections[i] = parsedSection;
            hasConditions |= parsedSection.Condition is not null;
        }

        if (CanCacheParsedSections(sections, indexedColors))
            StoreCachedParsedSections(sections, indexedColors, theme, parsedSections, hasConditions);

        return parsedSections;
    }

    private static bool TryGetCachedParsedSections(
        string[] sections,
        WorkbookIndexedColorPalette? indexedColors,
        WorkbookTheme? theme,
        out ParsedSection[] parsedSections,
        out bool hasConditions)
    {
        var slot = GetParsedSectionsCacheSlot(sections, indexedColors, theme);
        var cached = Volatile.Read(ref ParsedSectionsCache[slot]);
        if (cached is not null &&
            ReferenceEquals(cached.Sections, sections) &&
            ReferenceEquals(cached.IndexedColors, indexedColors) &&
            ReferenceEquals(cached.Theme, theme))
        {
            parsedSections = cached.ParsedSections;
            hasConditions = cached.HasConditions;
            return true;
        }

        parsedSections = [];
        hasConditions = false;
        return false;
    }

    private static void StoreCachedParsedSections(
        string[] sections,
        WorkbookIndexedColorPalette? indexedColors,
        WorkbookTheme? theme,
        ParsedSection[] parsedSections,
        bool hasConditions)
    {
        var slot = GetParsedSectionsCacheSlot(sections, indexedColors, theme);
        Volatile.Write(
            ref ParsedSectionsCache[slot],
            new ParsedSectionsCacheEntry(sections, indexedColors, theme, parsedSections, hasConditions));
    }

    private static int GetParsedSectionsCacheSlot(
        string[] sections,
        WorkbookIndexedColorPalette? indexedColors,
        WorkbookTheme? theme)
        => HashCode.Combine(
            RuntimeHelpers.GetHashCode(sections),
            indexedColors is null ? 0 : RuntimeHelpers.GetHashCode(indexedColors),
            theme is null ? 0 : RuntimeHelpers.GetHashCode(theme)) & (ParsedSectionsCacheSize - 1);

    private static bool CanCacheParsedSections(string[] sections, WorkbookIndexedColorPalette? indexedColors)
        => indexedColors is null || !ContainsIndexedColorDirective(sections);

    private static bool ContainsIndexedColorDirective(string[] sections)
    {
        for (var sectionIndex = 0; sectionIndex < sections.Length; sectionIndex++)
        {
            var section = sections[sectionIndex];
            for (var i = 0; i < section.Length; i++)
            {
                if (section[i] != '[')
                    continue;

                var close = section.IndexOf(']', i + 1);
                if (close < 0)
                    break;

                if (IsIndexedColorDirective(section, i + 1, close))
                    return true;

                i = close;
            }
        }

        return false;
    }

    private static bool IsIndexedColorDirective(string section, int tokenStart, int tokenEnd)
    {
        while (tokenStart < tokenEnd && char.IsWhiteSpace(section[tokenStart]))
            tokenStart++;
        while (tokenEnd > tokenStart && char.IsWhiteSpace(section[tokenEnd - 1]))
            tokenEnd--;

        const string colorPrefix = "Color";
        if (tokenEnd - tokenStart <= colorPrefix.Length ||
            string.Compare(
                section,
                tokenStart,
                colorPrefix,
                0,
                colorPrefix.Length,
                StringComparison.OrdinalIgnoreCase) != 0)
        {
            return false;
        }

        var index = tokenStart + colorPrefix.Length;
        while (index < tokenEnd && char.IsWhiteSpace(section[index]))
            index++;

        if (index == tokenEnd)
            return false;

        while (index < tokenEnd)
        {
            if (section[index] is < '0' or > '9')
                return false;

            index++;
        }

        return true;
    }

    private static ParsedSection ParseSection(
        string section,
        WorkbookIndexedColorPalette? indexedColors,
        WorkbookTheme? theme)
    {
        string? color = null;
        FormatCondition? condition = null;
        int index = 0;
        var retainedDirectives = new System.Text.StringBuilder();

        while (index < section.Length && section[index] == '[')
        {
            int close = section.IndexOf(']', index + 1);
            if (close < 0)
                break;

            string token = section[(index + 1)..close];
            if (NumberFormatColorMapper.TryMapColor(token, indexedColors, theme, out var tokenColor))
            {
                color = tokenColor;
                index = SkipInterDirectiveWhitespace(section, close + 1);
                continue;
            }

            if (NumberFormatColorMapper.IsThemeColorDirective(token))
            {
                index = SkipInterDirectiveWhitespace(section, close + 1);
                continue;
            }

            if (TryParseCondition(token, out var tokenCondition))
            {
                condition = tokenCondition;
                index = SkipInterDirectiveWhitespace(section, close + 1);
                continue;
            }

            if (IsRetainedSectionDirective(token))
            {
                retainedDirectives.Append(section, index, close - index + 1);
                index = SkipInterDirectiveWhitespace(section, close + 1);
                continue;
            }

            break;
        }

        return new ParsedSection(retainedDirectives + section[index..], color, condition);
    }

    private static int SkipInterDirectiveWhitespace(string section, int index)
    {
        int next = index;
        while (next < section.Length && char.IsWhiteSpace(section[next]))
            next++;

        return next < section.Length && section[next] == '['
            ? next
            : index;
    }

    private static bool TryParseCondition(string token, out FormatCondition? condition)
    {
        var match = SectionConditionRegex.Match(token);
        if (match.Success &&
            double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            condition = new FormatCondition(match.Groups[1].Value, value);
            return true;
        }

        condition = null;
        return false;
    }

    private static bool IsRetainedSectionDirective(string token)
    {
        var trimmed = token.Trim();
        return trimmed.StartsWith('$') ||
            trimmed.StartsWith("DBNum", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("NatNum", StringComparison.OrdinalIgnoreCase);
    }
}
