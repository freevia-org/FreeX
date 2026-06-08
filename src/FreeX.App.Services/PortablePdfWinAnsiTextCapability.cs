using System.Text;

namespace FreeX.App.Services;

internal static class PortablePdfWinAnsiTextCapability
{
    public const string DeferredUnicodePdfPathRequirements =
        "Unicode PDF export requires a real licensed TrueType/OpenType font subset, Type0/Identity-H text, ToUnicode mappings, and parser, render, and text extraction validation.";

    public const string UnsupportedUnicodeTextMessage =
        "Portable PDF export currently supports ASCII and WinAnsi text only; characters outside the built-in Helvetica/WinAnsi set require the deferred embedded-font Unicode PDF path. " +
        DeferredUnicodePdfPathRequirements;

    public static string NormalizePdfText(string text)
    {
        var normalized = new StringBuilder(text.Length);
        foreach (var ch in text)
            normalized.Append(ch is '\r' or '\n' or '\t' ? ' ' : ch);

        return normalized.ToString();
    }

    public static string Truncate(string text, int maximumLength)
    {
        if (maximumLength <= 3 || text.Length <= maximumLength)
            return text;

        var truncatedLength = maximumLength - 3;
        if (char.IsHighSurrogate(text[truncatedLength - 1]))
            truncatedLength--;

        return text[..truncatedLength] + "...";
    }

    public static IReadOnlyList<PortablePdfUnsupportedUnicodeScalar> FindUnsupportedUnicodeScalars(string text)
    {
        var unsupportedScalars = new List<PortablePdfUnsupportedUnicodeScalar>();
        for (var index = 0; index < text.Length;)
        {
            var ch = text[index];
            if (char.IsHighSurrogate(ch) &&
                index + 1 < text.Length &&
                char.IsLowSurrogate(text[index + 1]))
            {
                var scalarValue = char.ConvertToUtf32(ch, text[index + 1]);
                unsupportedScalars.Add(new PortablePdfUnsupportedUnicodeScalar(
                    index,
                    $"U+{scalarValue:X4}",
                    text.Substring(index, 2)));
                index += 2;
                continue;
            }

            if (!TryEncodeWinAnsiByte(ch, out _))
            {
                unsupportedScalars.Add(new PortablePdfUnsupportedUnicodeScalar(
                    index,
                    $"U+{(int)ch:X4}",
                    ch.ToString()));
            }

            index++;
        }

        return unsupportedScalars;
    }

    public static bool TryEncodeWinAnsiByte(char ch, out byte value)
    {
        if (ch is >= ' ' and <= '~')
        {
            value = (byte)ch;
            return true;
        }

        if (ch is >= '\u00a0' and <= '\u00ff')
        {
            value = (byte)ch;
            return true;
        }

        value = ch switch
        {
            '\u20ac' => 0x80,
            '\u201a' => 0x82,
            '\u0192' => 0x83,
            '\u201e' => 0x84,
            '\u2026' => 0x85,
            '\u2020' => 0x86,
            '\u2021' => 0x87,
            '\u02c6' => 0x88,
            '\u2030' => 0x89,
            '\u0160' => 0x8A,
            '\u2039' => 0x8B,
            '\u0152' => 0x8C,
            '\u017D' => 0x8E,
            '\u2018' => 0x91,
            '\u2019' => 0x92,
            '\u201C' => 0x93,
            '\u201D' => 0x94,
            '\u2022' => 0x95,
            '\u2013' => 0x96,
            '\u2014' => 0x97,
            '\u02dc' => 0x98,
            '\u2122' => 0x99,
            '\u0161' => 0x9A,
            '\u203A' => 0x9B,
            '\u0153' => 0x9C,
            '\u017E' => 0x9E,
            '\u0178' => 0x9F,
            _ => 0
        };

        return value != 0;
    }
}
