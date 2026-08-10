using System.Text;
using System.Xml.Linq;

namespace Free.Shared.Opc;

/// <summary>
/// Drops characters that XML 1.0 cannot represent from model-originated text on the way out to an
/// OOXML part.
/// <para>
/// A document can legitimately contain characters the format cannot: C0/C1 control codes and lone
/// UTF-16 surrogates arrive by pasting from another application or by importing a file, and nothing
/// in the editors rejects them. <c>XmlWriter</c> validates on write, so a single such character
/// aborts the whole save with an ArgumentException rather than degrading — the user loses the save,
/// not the character. Dropping it is what Word and PowerPoint do with the same input.
/// </para>
/// </summary>
public static class OoxmlXmlText
{
    /// <summary>
    /// Sanitizes every text node and attribute value in <paramref name="document"/> in place.
    /// <para>
    /// Applied once at the serialization boundary rather than at each of the dozens of places that
    /// build an element from model text, so a write site added later cannot reintroduce the crash.
    /// </para>
    /// </summary>
    public static void SanitizeInPlace(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // Materialize before mutating: assigning Value while enumerating the same tree is undefined.
        foreach (var text in document.DescendantNodes().OfType<XText>().ToList())
        {
            var sanitized = Sanitize(text.Value);
            if (!ReferenceEquals(sanitized, text.Value))
                text.Value = sanitized;
        }

        foreach (var attribute in document.Descendants().Attributes().ToList())
        {
            var sanitized = Sanitize(attribute.Value);
            if (!ReferenceEquals(sanitized, attribute.Value))
                attribute.Value = sanitized;
        }
    }

    /// <summary>
    /// Returns <paramref name="text"/> with XML-1.0-illegal characters removed, or the same instance
    /// when there is nothing to remove.
    /// </summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return text ?? string.Empty;

        if (!NeedsSanitizing(text))
            return text;

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                // Keep a valid pair (astral-plane characters such as emoji); drop a lone high surrogate.
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    builder.Append(c);
                    builder.Append(text[++i]);
                }
            }
            else if (!char.IsLowSurrogate(c) && !IsXml10Illegal(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static bool NeedsSanitizing(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    i++;
                    continue;
                }
                return true;
            }

            if (char.IsLowSurrogate(c) || IsXml10Illegal(c))
                return true;
        }

        return false;
    }

    // XML 1.0 permits #x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD]; ￾ and ￿ are not
    // characters at all. Matches the rule FreeW's DOCX writer already applies.
    private static bool IsXml10Illegal(char c) =>
        c != '\t' && c != '\n' && c != '\r' && (c < ' ' || c == '￾' || c == '￿');
}
