using System.Globalization;
using System.Text.RegularExpressions;

namespace Freexcel.Core.Calc;

public static partial class NumberFormatter
{
    private sealed record ParsedSection(string Format, string? ColorHex, FormatCondition? Condition);

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
        var sections = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool inQuote = false;
        bool inBracket = false;

        for (int i = 0; i < format.Length; i++)
        {
            char c = format[i];
            if (c == '"' && !inBracket)
            {
                inQuote = !inQuote;
                sb.Append(c);
            }
            else if (c == '\\' && !inQuote && i + 1 < format.Length)
            {
                sb.Append(c);
                sb.Append(format[++i]);
            }
            else if (c == '[' && !inQuote)
            {
                inBracket = true;
                sb.Append(c);
            }
            else if (c == ']' && !inQuote)
            {
                inBracket = false;
                sb.Append(c);
            }
            else if (c == ';' && !inQuote && !inBracket)
            {
                sections.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        sections.Add(sb.ToString());
        return [.. sections];
    }

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
    {
        string? color = null;
        FormatCondition? condition = null;
        int index = 0;

        while (index < section.Length && section[index] == '[')
        {
            int close = section.IndexOf(']', index + 1);
            if (close < 0)
                break;

            string token = section[(index + 1)..close];
            if (NumberFormatColorMapper.TryMapColor(token, out var tokenColor))
            {
                color = tokenColor;
                index = SkipInterDirectiveWhitespace(section, close + 1);
                continue;
            }

            if (TryParseCondition(token, out var tokenCondition))
            {
                condition = tokenCondition;
                index = SkipInterDirectiveWhitespace(section, close + 1);
                continue;
            }

            break;
        }

        return new ParsedSection(section[index..], color, condition);
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
        var match = Regex.Match(token, @"^\s*(>=|<=|<>|>|<|=)\s*([+-]?(?:(?:\d+(?:\.\d*)?)|(?:\.\d+))(?:[eE][+-]?\d+)?)\s*$");
        if (match.Success &&
            double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            condition = new FormatCondition(match.Groups[1].Value, value);
            return true;
        }

        condition = null;
        return false;
    }
}
