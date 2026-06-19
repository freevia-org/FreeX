using System.Text;

namespace Free.Shared.Pdf;

/// <summary>
/// App-agnostic WinAnsi (Helvetica) text-encoding rules for the dependency-free PDF writer:
/// normalization, truncation, per-char WinAnsi byte mapping, and Unicode-coverage diagnostics.
/// Moved out of FreeX.App.Services so FreeX, FreeW, and future apps share one capability surface.
/// </summary>
public static class PdfWinAnsiTextCapability
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

    public static IReadOnlyList<PdfUnsupportedUnicodeScalar> FindUnsupportedUnicodeScalars(string text)
    {
        var unsupportedScalars = new List<PdfUnsupportedUnicodeScalar>();
        for (var index = 0; index < text.Length;)
        {
            var ch = text[index];
            if (char.IsHighSurrogate(ch) &&
                index + 1 < text.Length &&
                char.IsLowSurrogate(text[index + 1]))
            {
                var scalarValue = char.ConvertToUtf32(ch, text[index + 1]);
                unsupportedScalars.Add(new PdfUnsupportedUnicodeScalar(
                    index,
                    $"U+{scalarValue:X4}",
                    text.Substring(index, 2)));
                index += 2;
                continue;
            }

            if (!TryEncodeWinAnsiByte(ch, out _))
            {
                unsupportedScalars.Add(new PdfUnsupportedUnicodeScalar(
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

        if (ch is >= ' ' and <= 'ÿ')
        {
            value = (byte)ch;
            return true;
        }

        value = ch switch
        {
            '€' => 0x80,
            '‚' => 0x82,
            'ƒ' => 0x83,
            '„' => 0x84,
            '…' => 0x85,
            '†' => 0x86,
            '‡' => 0x87,
            'ˆ' => 0x88,
            '‰' => 0x89,
            'Š' => 0x8A,
            '‹' => 0x8B,
            'Œ' => 0x8C,
            'Ž' => 0x8E,
            '‘' => 0x91,
            '’' => 0x92,
            '“' => 0x93,
            '”' => 0x94,
            '•' => 0x95,
            '–' => 0x96,
            '—' => 0x97,
            '˜' => 0x98,
            '™' => 0x99,
            'š' => 0x9A,
            '›' => 0x9B,
            'œ' => 0x9C,
            'ž' => 0x9E,
            'Ÿ' => 0x9F,
            _ => 0
        };

        return value != 0;
    }
}

/// <summary>One Unicode scalar in a text run that the WinAnsi (Helvetica) path cannot encode.</summary>
public sealed record PdfUnsupportedUnicodeScalar(
    int TextIndex,
    string CodePoint,
    string TextElement);
