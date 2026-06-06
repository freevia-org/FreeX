using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetSheetFormatNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace X14AcNs = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/ac";

    private static readonly IReadOnlySet<string> SheetFormatAttributes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "baseColWidth",
            "defaultColWidth",
            "defaultRowHeight",
            "customHeight",
            "zeroHeight",
            "thickTop",
            "thickBottom",
            "outlineLevelRow",
            "outlineLevelCol"
        };

    private static readonly string[] BooleanAttributes =
    [
        "customHeight",
        "zeroHeight",
        "thickTop",
        "thickBottom"
    ];

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var sheetFormats = worksheetRoot.Elements(WorksheetNs + "sheetFormatPr").ToList();
        if (sheetFormats.Count == 0)
            return false;

        var changed = false;
        var sheetFormat = sheetFormats[0];
        foreach (var duplicate in sheetFormats.Skip(1))
        {
            duplicate.Remove();
            changed = true;
        }

        changed |= NormalizeElement(sheetFormat);
        return changed;
    }

    public static bool NormalizeElement(XElement sheetFormat)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(sheetFormat);
        changed |= NormalizeAttribute(sheetFormat, "baseColWidth", NormalizeUnsignedInt);
        changed |= NormalizeAttribute(sheetFormat, "defaultColWidth", NormalizeNonNegativeDouble);
        changed |= NormalizeAttribute(sheetFormat, "defaultRowHeight", NormalizeNonNegativeDouble);
        changed |= NormalizeAttribute(sheetFormat, "outlineLevelRow", NormalizeOutlineLevel);
        changed |= NormalizeAttribute(sheetFormat, "outlineLevelCol", NormalizeOutlineLevel);
        changed |= NormalizeAttribute(sheetFormat, X14AcNs + "dyDescent", NormalizeNonNegativeDouble);

        foreach (var attributeName in BooleanAttributes)
            changed |= NormalizeAttribute(sheetFormat, attributeName, NormalizeBoolean);

        changed |= RemoveAllNodes(sheetFormat);
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

    private static bool NormalizeAttribute(
        XElement element,
        XName attributeName,
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

    private static string? NormalizeBoolean(string? value)
    {
        var trimmed = value?.Trim();
        if (string.Equals(trimmed, "1", StringComparison.Ordinal) ||
            string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase))
        {
            return "1";
        }

        if (string.Equals(trimmed, "0", StringComparison.Ordinal) ||
            string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase))
        {
            return "0";
        }

        return null;
    }

    private static string? NormalizeNonNegativeDouble(string? value)
    {
        var trimmed = value?.Trim();
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
            !double.IsFinite(parsed) ||
            parsed < 0)
        {
            return null;
        }

        return parsed.ToString("G17", CultureInfo.InvariantCulture);
    }

    private static string? NormalizeOutlineLevel(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) &&
               parsed <= 7
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static string? NormalizeUnsignedInt(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static bool RemoveUnknownAttributes(XElement element)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                (attribute.Name.NamespaceName.Length == 0 && SheetFormatAttributes.Contains(attribute.Name.LocalName)) ||
                attribute.Name == X14AcNs + "dyDescent")
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveAllNodes(XElement element)
    {
        if (!element.Nodes().Any())
            return false;

        element.RemoveNodes();
        return true;
    }

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
