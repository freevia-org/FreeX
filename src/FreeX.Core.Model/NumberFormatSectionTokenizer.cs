namespace FreeX.Core.Model;

/// <summary>Splits number formats on active section separators while preserving format syntax.</summary>
public static class NumberFormatSectionTokenizer
{
    public static string[] Split(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (format.IndexOf(';') < 0)
            return [format];

        var separators = new List<int>();
        ScanSeparators(format, separators);
        if (separators.Count == 0)
            return [format];

        var sections = new string[separators.Count + 1];
        var start = 0;
        for (var index = 0; index < separators.Count; index++)
        {
            sections[index] = format[start..separators[index]];
            start = separators[index] + 1;
        }
        sections[^1] = format[start..];
        return sections;
    }

    public static int Count(string format)
    {
        ArgumentNullException.ThrowIfNull(format);
        return format.IndexOf(';') < 0 ? 1 : ScanSeparators(format, separators: null) + 1;
    }

    private static int ScanSeparators(string format, List<int>? separators)
    {
        var inQuote = false;
        var inBracket = false;
        var count = 0;

        for (var index = 0; index < format.Length; index++)
        {
            var character = format[index];
            if (character == '"' && !inBracket)
            {
                inQuote = !inQuote;
            }
            else if (character == '\\' && !inQuote && index + 1 < format.Length)
            {
                index++;
            }
            else if (character == '[' && !inQuote)
            {
                inBracket = true;
            }
            else if (character == ']' && !inQuote)
            {
                inBracket = false;
            }
            else if (character == ';' && !inQuote && !inBracket)
            {
                separators?.Add(index);
                count++;
            }
        }

        return count;
    }
}
