using System.Globalization;
using System.IO.Compression;
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

    public static void NormalizeWorksheets(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (NormalizeWorksheetRoot(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    public static bool NormalizeElement(XElement phoneticPr)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(phoneticPr, PhoneticPropertyAttributes);
        changed |= NormalizeAttribute(phoneticPr, "fontId", NormalizeUnsignedInt);
        changed |= NormalizeAttribute(phoneticPr, "type", value => NormalizeToken(value, ValidTypes));
        changed |= NormalizeAttribute(phoneticPr, "alignment", value => NormalizeToken(value, ValidAlignments));
        changed |= RemoveAllChildren(phoneticPr);
        return changed;
    }

    private static bool RemoveUnknownAttributes(XElement element, IReadOnlySet<string> allowedNames)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && allowedNames.Contains(attribute.Name.LocalName)))
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveAllChildren(XElement element)
    {
        if (!element.HasElements)
            return false;

        element.Elements().Remove();
        return true;
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
        if (value is null)
            return null;

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

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
