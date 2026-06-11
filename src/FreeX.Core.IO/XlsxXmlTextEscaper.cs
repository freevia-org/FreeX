using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace FreeX.Core.IO;

/// <summary>
/// Escapes model-originated text strings for safe placement in XLSX XML (OOXML) element text and
/// attribute values via the patch-save path.
///
/// The full-save path (ClosedXML) handles escaping automatically.  The patch-save path writes model
/// strings directly into XLinq nodes, which means:
///
///   1. XML-invalid control characters (those for which <see cref="XmlConvert.IsXmlChar"/> returns
///      false, e.g. U+000B VERTICAL TAB, U+0001 SOH) cause <see cref="System.Xml.XDocument.Save"/>
///      to throw <see cref="ArgumentException"/> and abort the save entirely.
///
///   2. Carriage returns (U+000D) inside element text are normalised to LF by XML parsers on the
///      next load — silently mutating cell text.
///
///   3. Literal strings that look like OOXML escape sequences (e.g. the six characters
///      <c>_x000D_</c>) are decoded on load by ClosedXML, turning them into the character they
///      represent (CR in that example) — another silent mutation.
///
/// This class applies the same escaping convention as ClosedXML / Excel:
///   • Any substring matching <c>_x[0-9A-Fa-f]{4}_</c> has its leading underscore replaced with
///     <c>_x005F_</c> so the literal string survives a round-trip through ClosedXML load.
///   • U+000D (CR) and every character for which <see cref="XmlConvert.IsXmlChar"/> returns false
///     are encoded as <c>_xHHHH_</c> (uppercase four-hex-digit BMP form).
///
/// A matching Unescape is intentionally absent: load goes through ClosedXML which already decodes
/// <c>_xHHHH_</c> sequences correctly.
/// </summary>
internal static class XlsxXmlTextEscaper
{
    // Pattern that matches an OOXML-style underscore escape sequence: _xHHHH_
    // The leading underscore is what we pre-escape to _x005F_ so the literal
    // text round-trips instead of being decoded by ClosedXML on load.
    private static readonly Regex OoxmlEscapeSequencePattern =
        new(@"_x[0-9A-Fa-f]{4}_", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Encodes <paramref name="value"/> so it is safe to store as XML element text or attribute
    /// value in the patch-save XLSX path.
    ///
    /// The following transformations are applied in order:
    ///   1. Any literal <c>_xHHHH_</c> substring is escaped to <c>_x005F_xHHHH_</c>.
    ///   2. U+000D (CR) and any character for which <see cref="XmlConvert.IsXmlChar"/> returns
    ///      false are replaced with <c>_xHHHH_</c>.
    ///
    /// Characters that are valid XML chars (other than CR) are written verbatim; tab (U+0009),
    /// LF (U+000A), and the entire Basic Multilingual Plane printable range pass through unchanged.
    /// </summary>
    public static string EscapeForXml(string value)
    {
        if (value.Length == 0)
            return value;

        // Fast path: if the string contains no characters that require escaping and no
        // OOXML-style sequences, return it unchanged without allocating.
        if (!NeedsEscaping(value))
            return value;

        // Step 1: pre-escape any literal _xHHHH_ sequences so they survive ClosedXML load.
        var step1 = OoxmlEscapeSequencePattern.Replace(value, m => "_x005F_" + m.Value[1..]);

        // Step 2: encode CR and XML-invalid characters.
        var sb = new StringBuilder(step1.Length + 16);
        foreach (var ch in step1)
        {
            if (ch == '\r' || !XmlConvert.IsXmlChar(ch))
            {
                sb.Append('_');
                sb.Append('x');
                sb.Append(((int)ch).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append('_');
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Quick scan: returns true if <paramref name="value"/> contains at least one character that
    /// <see cref="EscapeForXml"/> would transform, or an OOXML-escape-sequence substring.
    /// </summary>
    private static bool NeedsEscaping(string value)
    {
        foreach (var ch in value)
        {
            if (ch == '\r' || !XmlConvert.IsXmlChar(ch))
                return true;
        }

        // Check for _xHHHH_ patterns that would be decoded by ClosedXML.
        return OoxmlEscapeSequencePattern.IsMatch(value);
    }
}
