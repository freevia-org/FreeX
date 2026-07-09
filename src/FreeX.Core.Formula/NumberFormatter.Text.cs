using FreeX.Core.Model;

namespace FreeX.Core.Formula;

public static partial class NumberFormatter
{
    private static FormatResult FormatTextWithColor(
        string text,
        string[] sections,
        WorkbookIndexedColorPalette? indexedColors,
        WorkbookTheme? theme)
    {
        var parsedSections = ParseSections(sections, indexedColors, theme, out _);
        if (sections.Length == 1)
        {
            var firstSection = parsedSections[0];
            return firstSection.Format.Contains('@', StringComparison.Ordinal)
                ? new FormatResult(ApplyTextSection(firstSection.Format, text), firstSection.ColorHex)
                : new FormatResult(text);
        }

        if (sections.Length <= 3)
        {
            // A 2- or 3-section format (positive[;negative[;zero]]) has no dedicated 4th
            // (text) section. Per Excel's rule, text values are unaffected by such a format
            // -- even if the first (positive) section happens to contain '@' -- so the raw
            // text always passes through unmodified.
            return new FormatResult(text);
        }

        var parsed = parsedSections[3];
        if (parsed.Format == "")
            return new FormatResult("", parsed.ColorHex);

        return new FormatResult(ApplyTextSection(parsed.Format, text), parsed.ColorHex);
    }

    private static string ApplyTextSection(string section, string text)
    {
        // `@` is the text placeholder; surrounding quotes and escaped characters are literals.
        // Spacing/fill directives affect layout in Excel, not the displayed text payload.
        var result = new System.Text.StringBuilder();
        bool inQuote = false;
        for (int i = 0; i < section.Length; i++)
        {
            char c = section[i];
            if (c == '"') { inQuote = !inQuote; continue; }
            if (inQuote) { result.Append(c); continue; }

            if (c == '\\' && i + 1 < section.Length)
            {
                result.Append(section[++i]);
                continue;
            }

            if (c == '[')
            {
                int close = section.IndexOf(']', i + 1);
                if (close >= 0)
                {
                    i = close;
                    continue;
                }
            }

            if (c is '_' or '*' && i + 1 < section.Length)
            {
                i++;
                continue;
            }

            if (c == '@') result.Append(text);
            else result.Append(c);
        }
        return result.ToString();
    }
}
