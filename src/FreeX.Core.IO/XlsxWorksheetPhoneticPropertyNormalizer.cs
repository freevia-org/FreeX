using System.Globalization;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetPhoneticPropertyNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly IReadOnlySet<string> PhoneticPropertyAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "fontId", "type", "alignment" };

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

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var phoneticProperties = worksheetRoot.Elements(WorksheetNs + "phoneticPr").ToList();
        if (phoneticProperties.Count == 0)
            return false;

        var changed = false;
        var keptPhoneticProperties = false;
        foreach (var phoneticPr in phoneticProperties)
        {
            if (keptPhoneticProperties)
            {
                phoneticPr.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeElement(phoneticPr);
            if (!phoneticPr.HasAttributes)
            {
                phoneticPr.Remove();
                changed = true;
                continue;
            }

            keptPhoneticProperties = true;
        }

        return changed;
    }

    public static bool NormalizeElement(XElement phoneticPr)
    {
        var changed = false;
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(phoneticPr, PhoneticPropertyAttributes);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(phoneticPr, "fontId", NormalizeUnsignedInt);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(phoneticPr, "type", value => XlsxXmlNormalizationHelpers.NormalizeToken(value, ValidTypes));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(phoneticPr, "alignment", value => XlsxXmlNormalizationHelpers.NormalizeToken(value, ValidAlignments));
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElements(phoneticPr);
        return changed;
    }

    private static string? NormalizeUnsignedInt(string? value)
    {
        if (value is null)
            return null;

        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : "0";
    }

}
