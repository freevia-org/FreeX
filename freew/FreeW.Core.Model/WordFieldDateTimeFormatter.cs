using System.Globalization;
using System.Text;

namespace FreeW.Core.Model;

/// <summary>
/// Applies Word field date-time picture switches (<c>\@</c>) without depending on either UI host.
/// </summary>
public static class WordFieldDateTimeFormatter
{
    public static bool TryFormat(
        DateTime value,
        string instruction,
        CultureInfo culture,
        out string formatted)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(culture);

        var picture = ComplexFieldEngine.SwitchValue(instruction, '@');
        if (picture is null || !TryConvertWordPicture(picture, out var netPicture))
        {
            formatted = string.Empty;
            return false;
        }

        formatted = value.ToString(netPicture, culture);
        return true;
    }

    public static bool TryParseAndFormat(
        string value,
        string instruction,
        CultureInfo culture,
        out string formatted)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(culture);

        if ((!DateTime.TryParse(value, culture, DateTimeStyles.AllowWhiteSpaces, out var moment)
                && !DateTime.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out moment))
            || !TryFormat(moment, instruction, culture, out formatted))
        {
            formatted = string.Empty;
            return false;
        }

        return true;
    }

    private static bool TryConvertWordPicture(string picture, out string netPicture)
    {
        var builder = new StringBuilder(picture.Length + 4);
        for (var i = 0; i < picture.Length;)
        {
            if (picture.AsSpan(i).StartsWith("AM/PM", StringComparison.Ordinal)
                || picture.AsSpan(i).StartsWith("am/pm", StringComparison.Ordinal))
            {
                builder.Append("tt");
                i += 5;
                continue;
            }

            var ch = picture[i];
            if (ch == '\'')
            {
                var closingQuote = picture.IndexOf('\'', i + 1);
                if (closingQuote < 0)
                {
                    netPicture = string.Empty;
                    return false;
                }

                builder.Append(picture, i, closingQuote - i + 1);
                i = closingQuote + 1;
                continue;
            }

            if (!char.IsLetter(ch))
            {
                if (ch is '/' or ':')
                    builder.Append('\\');
                builder.Append(ch);
                i++;
                continue;
            }

            var end = i + 1;
            while (end < picture.Length && picture[end] == ch)
                end++;
            var length = end - i;
            var valid = ch switch
            {
                'd' or 'M' => length is >= 1 and <= 4,
                'y' => length is >= 1 and <= 4,
                'h' or 'H' or 'm' or 's' => length is >= 1 and <= 2,
                _ => false
            };
            if (!valid)
            {
                netPicture = string.Empty;
                return false;
            }

            builder.Append(ch, length);
            i = end;
        }

        netPicture = builder.Length == 1
            ? "%" + builder.ToString()
            : builder.ToString();
        return netPicture.Length > 0;
    }
}
