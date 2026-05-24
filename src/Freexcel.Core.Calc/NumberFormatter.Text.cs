namespace Freexcel.Core.Calc;

public static partial class NumberFormatter
{
    private static FormatResult FormatTextWithColor(string text, string[] sections)
    {
        if (sections.Length <= 3)
        {
            var firstSection = ParseSection(sections[0]);
            return firstSection.Format.Contains('@', StringComparison.Ordinal)
                ? new FormatResult(ApplyTextSection(firstSection.Format, text), firstSection.ColorHex)
                : new FormatResult(text);
        }

        var parsed = ParseSection(sections[3]);
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
