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
        if (changed)
            return adjusted;

        // No adjustable numeric placeholder was found anywhere in the format. Real Excel treats
        // Increase/Decrease Decimal as a no-op in that case (e.g. date/time formats like
        // "mm/dd/yyyy" or "h:mm", and the Text format "@"). Blindly appending ".0" would inject a
        // literal '0' into the code, which flips date formats into the numeric rendering path
        // (IsDateTimeFormat rejects any format containing '0'/'#') and produces garbage like
        // "mm/dd/yyyy46108.0" or, for Text, "hello.0".
        return format;
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

            if (!inBracket && ch == '\\' && index + 1 < format.Length)
            {
                // A backslash escapes exactly the next character (e.g. "0\;0" is ONE section: the
                // ';' is a literal, not a positive/negative/zero/text separator). Skip it so the
                // escaped character is never mistaken for a real section boundary.
                index++;
                continue;
            }

            if (!inBracket && ch == ';')
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
            var placeholders = match.Groups[3].Value;
            // Append a placeholder matching the run's own style ('0' for "0.00" -> "0.000",
            // '#' for "0.##" -> "0.###") at the END of the decimal-placeholder run, not
            // immediately after the literal digits: '#'/'?' are valid decimal placeholders too
            // and must not be skipped over, or the inserted '0' lands mid-run (e.g. "0.##" would
            // become "0.0##" instead of Excel's "0.###").
            var placeholderChar = placeholders.Length > 0 ? placeholders[^1] : '0';
            return section.Insert(match.Index + match.Length, placeholderChar.ToString());
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
            {
                // The character right after '\' (a literal escape) or '_'/'*' (a spacer-width
                // argument) is never a real placeholder. When that escaped character IS the
                // position being asked about, it is a literal -- not the decimal separator or a
                // digit run to grow/shrink -- so report it as non-editable directly instead of
                // just skipping past it, which previously let the scan exit the loop (current <
                // index becomes false) without ever recording that `index` itself was escaped.
                if (current + 1 == index)
                    return false;

                current++;
            }
        }

        return !inQuote && !inBracket;
    }

    // Matches the whole decimal-placeholder run after the dot, not just literal digits: '#' and
    // '?' are equally valid decimal placeholders (e.g. "0.0#", "0.##", "#.##") and must be
    // included so Increase Decimal appends its new placeholder at the end of the run instead of
    // splicing it in immediately after the literal digits (which would turn "0.##" into the
    // mis-shaped "0.0##" rather than Excel's "0.###").
    [GeneratedRegex(@"(\d*)(\.([0#?]*))")]
    private static partial Regex DecimalPlacesRegex();

    [GeneratedRegex(@"(\d+)")]
    private static partial Regex IntegerDigitsRegex();

    // Matches the whole decimal-placeholder run after the dot, not just literal digits: '#' and '?'
    // are equally valid decimal placeholders (e.g. "0.0#", "0.##", "#.##") and must be included so
    // Decrease Decimal trims the run correctly instead of either treating '0#' as a two-digit integer
    // mask (dropping the whole ".0" and leaving a corrupt "0#") or matching nothing at all ("0.##").
    [GeneratedRegex(@"\.([0#?]+)")]
    private static partial Regex RemoveDecimalPlacesRegex();

    /// <summary>
    /// Matches a fraction numerator/denominator run, e.g. the "?/?" in "# ?/?" or the "?/4" in "# ?/4".
    /// Requires at least one '?' placeholder on either side of the slash so ordinary digit-mask codes
    /// (which use '0'/'#' but never '?') are not mistaken for a fraction.
    /// </summary>
    [GeneratedRegex(@"[?0]*\?[?0]*/[?0-9]+")]
    private static partial Regex FractionShapeRegex();
}
