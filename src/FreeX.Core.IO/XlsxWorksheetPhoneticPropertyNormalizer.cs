using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPhoneticPropertyNormalizer
{
    private static readonly HashSet<string> ValidTypes =
    [
        "noConversion",
        "hiragana",
        "fullwidthKatakana",
        "halfwidthKatakana"
    ];

    private static readonly HashSet<string> ValidAlignments =
    [
        "noControl",
        "left",
        "center",
        "distributed"
    ];

    public static bool NormalizeElement(XElement phoneticPr)
    {
        var changed = false;
        changed |= NormalizeAttribute(phoneticPr, "fontId", NormalizeUnsignedInt);
        changed |= NormalizeAttribute(phoneticPr, "type", value => NormalizeToken(value, ValidTypes));
        changed |= NormalizeAttribute(phoneticPr, "alignment", value => NormalizeToken(value, ValidAlignments));
        return changed;
    }

    private static bool NormalizeAttribute(
        XElement element,
        string attributeName,
        Func<string?, string?> normalize)
    {
        var attribute = element.Attribute(attributeName);
        var normalized = normalize(attribute?.Value);
        if (normalized is null)
        {
            if (attribute is null)
                return false;

            attribute.Remove();
            return true;
        }

        if (attribute is not null && string.Equals(attribute.Value, normalized, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, normalized);
        return true;
    }

    private static string? NormalizeUnsignedInt(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : "0";
    }

    private static string? NormalizeToken(string? value, IReadOnlySet<string> allowedValues)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && allowedValues.Contains(trimmed) ? trimmed : null;
    }
}
