using System.Text.RegularExpressions;

namespace Free.Shared.AppServices;

public static partial class NumberFormatDecimalAdjuster
{
    /// <summary>
    /// The canonical Special-category format codes (ZIP code, ZIP+4, phone number, SSN). These are
    /// digit-mask layouts, not numeric magnitudes with a decimal component, so real Excel treats
    /// Increase/Decrease Decimal as a no-op on them rather than mutating the mask.
    /// </summary>
    private static readonly string[] SpecialFormatCodes =
    [
        "00000",
        "00000-0000",
        "[<=9999999]###-####;(###) ###-####",
        "000-00-0000"
    ];

    public static string AddDecimalPlace(string? format)
    {
        if (string.IsNullOrEmpty(format) || format == "General")
            return "0.0";

        if (IsUnadjustableFormat(format))
            return format;

        var adjusted = AdjustSections(format, addDecimalPlace: true, out var changed);
        return changed ? adjusted : format + ".0";
    }

    public static string RemoveDecimalPlace(string? format)
    {
        if (string.IsNullOrEmpty(format) || format == "General")
            return "0";

        if (IsUnadjustableFormat(format))
            return format;

        return AdjustSections(format, addDecimalPlace: false, out _);
    }

    /// <summary>
    /// True when the whole format is a Special (ZIP/ZIP+4/phone/SSN) digit-mask code, or any section
    /// of it is a Fraction layout (e.g. "# ?/?", "# ??/??", "# ?/4"). Increase/Decrease Decimal Places
    /// is a no-op on both categories in Excel: fractions have no decimal component to adjust, and the
    /// Special masks are literal digit layouts that must not be reshaped.
    /// </summary>
    private static bool IsUnadjustableFormat(string format)
    {
        if (SpecialFormatCodes.Contains(format))
            return true;

        foreach (var section in SplitSections(format))
        {
            if (IsFractionSection(section))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Recognizes a Fraction-category section: an (optionally quoted/spaced) integer part followed by
    /// '/' and a run of '?' or '0' denominator placeholders (with an optional fixed-digit denominator,
    /// e.g. "# ?/4"), all outside quoted literals and bracketed conditions. This mirrors the shapes
    /// produced by <c>NumberFormatMetadata.FractionFormatCode</c> in FreeX.App.Presentation.
    /// </summary>
    private static bool IsFractionSection(string section)
    {
        var match = FractionShapeRegex().Match(section);
        return match.Success && IsEditablePlaceholder(section, match.Index);
    }

    private static string AdjustSections(string format, bool addDecimalPlace, out bool changed)
    {
        changed = false;
        var sections = SplitSections(format);
        for (var index = 0; index < sections.Count; index++)
        {
            var adjusted = addDecimalPlace
                ? AddDecimalPlaceToSection(sections[index], out var sectionChanged)
                : RemoveDecimalPlaceFromSection(sections[index], out sectionChanged);
            if (!sectionChanged)
                continue;

            sections[index] = adjusted;
            changed = true;
        }

        return string.Join(';', sections);
    }

    private static List<string> SplitSections(string format)
    {
        var sections = new List<string>();
        var sectionStart = 0;
        var inQuote = false;
        var inBracket = false;

        for (var index = 0; index < format.Length; index++)
        {
            var ch = format[index];
            if (ch == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (!inQuote && ch == '[')
            {
                inBracket = true;
                continue;
            }

            if (!inQuote && ch == ']')
            {
                inBracket = false;
                continue;
            }

            if (!inQuote && !inBracket && ch == ';')
            {
                sections.Add(format[sectionStart..index]);
                sectionStart = index + 1;
            }
        }

        sections.Add(format[sectionStart..]);
        return sections;
    }

    private static string AddDecimalPlaceToSection(string section, out bool changed)
    {
        foreach (Match match in DecimalPlacesRegex().Matches(section))
        {
            if (!IsEditablePlaceholder(section, match.Index))
                continue;

            changed = true;
            return section.Insert(match.Index + match.Length, "0");
        }

        foreach (Match match in IntegerDigitsRegex().Matches(section))
        {
            if (!IsEditablePlaceholder(section, match.Index))
                continue;

            changed = true;
            return section.Insert(match.Index + match.Length, ".0");
        }

        return AdjustQuestionPlaceholder(section, addPlaceholder: true, out changed);
    }

    private static string RemoveDecimalPlaceFromSection(string section, out bool changed)
    {
        foreach (Match match in RemoveDecimalPlacesRegex().Matches(section))
        {
            if (!IsEditablePlaceholder(section, match.Index))
                continue;

            changed = true;
            return match.Groups[1].Value.Length <= 1
                ? section.Remove(match.Index, match.Length)
                : section.Remove(match.Index + match.Length - 1, 1);
        }

        return AdjustQuestionPlaceholder(section, addPlaceholder: false, out changed);
    }

    private static string AdjustQuestionPlaceholder(string section, bool addPlaceholder, out bool changed)
    {
        for (var index = section.Length - 1; index >= 0; index--)
        {
            if (section[index] != '?' || !IsEditablePlaceholder(section, index))
                continue;

            changed = true;
            return addPlaceholder
                ? section.Insert(index + 1, "?")
                : section.Remove(index, 1);
        }

        changed = false;
        return section;
    }

    private static bool IsEditablePlaceholder(string section, int index)
    {
        var inQuote = false;
        var inBracket = false;
        for (var current = 0; current < index; current++)
        {
            var ch = section[current];
            if (ch == '"')
            {
                inQuote = !inQuote;
                continue;
            }

            if (inQuote)
                continue;

            if (ch == '[')
            {
                inBracket = true;
                continue;
            }

            if (ch == ']')
            {
                inBracket = false;
                continue;
            }

            if ((ch == '\\' || ch == '_' || ch == '*') && current + 1 < section.Length)
                current++;
        }

        return !inQuote && !inBracket;
    }

    [GeneratedRegex(@"(\d*)(\.(\d*))")]
    private static partial Regex DecimalPlacesRegex();

    [GeneratedRegex(@"(\d+)")]
    private static partial Regex IntegerDigitsRegex();

    [GeneratedRegex(@"\.(\d+)")]
    private static partial Regex RemoveDecimalPlacesRegex();

    /// <summary>
    /// Matches a fraction numerator/denominator run, e.g. the "?/?" in "# ?/?" or the "?/4" in "# ?/4".
    /// Requires at least one '?' placeholder on either side of the slash so ordinary digit-mask codes
    /// (which use '0'/'#' but never '?') are not mistaken for a fraction.
    /// </summary>
    [GeneratedRegex(@"[?0]*\?[?0]*/[?0-9]+")]
    private static partial Regex FractionShapeRegex();
}
