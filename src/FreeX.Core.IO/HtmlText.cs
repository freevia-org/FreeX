using System.Text;

namespace FreeX.Core.IO;

/// <summary>Encoding detection + HTML entity decode/encode helpers shared by the HTML reader/writer.</summary>
internal static class HtmlText
{
    /// <summary>Read the full stream as text, honoring a UTF-8/UTF-16 BOM and defaulting to UTF-8.</summary>
    public static string ReadAll(Stream stream)
    {
        byte[] bytes;
        if (stream is MemoryStream ms && ms.TryGetBuffer(out var buffer))
        {
            var pos = (int)Math.Min(ms.Position, ms.Length);
            var len = (int)(ms.Length - pos);
            bytes = new byte[len];
            Array.Copy(buffer.Array!, buffer.Offset + pos, bytes, 0, len);
            ms.Position = ms.Length;
        }
        else
        {
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            bytes = copy.ToArray();
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        return new UTF8Encoding(false).GetString(bytes);
    }

    /// <summary>Decode a small, common set of HTML entities plus numeric (&amp;#NN; / &amp;#xNN;) references.</summary>
    public static string DecodeEntities(string s)
    {
        if (s.IndexOf('&') < 0)
            return s;

        var sb = new StringBuilder(s.Length);
        int i = 0;
        while (i < s.Length)
        {
            char c = s[i];
            if (c != '&')
            {
                sb.Append(c);
                i++;
                continue;
            }

            int semi = s.IndexOf(';', i + 1);
            if (semi < 0 || semi - i > 12)
            {
                sb.Append(c);
                i++;
                continue;
            }

            var entity = s.Substring(i + 1, semi - i - 1);
            if (TryDecodeEntity(entity, out var decoded))
            {
                sb.Append(decoded);
                i = semi + 1;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    private static bool TryDecodeEntity(string entity, out string decoded)
    {
        decoded = "";
        if (entity.Length == 0)
            return false;

        if (entity[0] == '#')
        {
            var numberPart = entity.AsSpan(1);
            int code;
            bool ok = numberPart.Length > 0 && (numberPart[0] is 'x' or 'X')
                ? int.TryParse(numberPart[1..], System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out code)
                : int.TryParse(numberPart, out code);
            if (!ok || code < 0 || code > 0x10FFFF)
                return false;
            decoded = char.ConvertFromUtf32(code);
            return true;
        }

        decoded = entity switch
        {
            "amp" => "&",
            "lt" => "<",
            "gt" => ">",
            "quot" => "\"",
            "apos" => "'",
            "nbsp" => " ",
            _ => "",
        };
        return decoded.Length > 0;
    }

    /// <summary>HTML-escape text content for safe emission inside a table cell or attribute.</summary>
    public static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
