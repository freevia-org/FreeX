using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetCalculationPropertyNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly IReadOnlySet<string> CalculationPropertyAttributes =
        new HashSet<string>(StringComparer.Ordinal) { "fullCalcOnLoad" };

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var calculationProperties = worksheetRoot.Elements(WorksheetNs + "sheetCalcPr").ToList();
        if (calculationProperties.Count == 0)
            return false;

        var changed = false;
        var keptCalculationProperties = false;
        foreach (var sheetCalcPr in calculationProperties)
        {
            if (keptCalculationProperties)
            {
                sheetCalcPr.Remove();
                changed = true;
                continue;
            }

            changed |= RemoveUnknownAttributes(sheetCalcPr, CalculationPropertyAttributes);
            changed |= NormalizeAttribute(sheetCalcPr, "fullCalcOnLoad", NormalizeBoolean);
            changed |= RemoveAllChildren(sheetCalcPr);

            if (!sheetCalcPr.HasAttributes)
            {
                sheetCalcPr.Remove();
                changed = true;
                continue;
            }

            keptCalculationProperties = true;
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

        return SetAttributeIfChanged(element, attributeName, normalized);
    }

    private static bool SetAttributeIfChanged(XElement element, string attributeName, string value)
    {
        var attribute = element.Attribute(attributeName);
        if (attribute is not null && string.Equals(attribute.Value, value, StringComparison.Ordinal))
            return false;

        element.SetAttributeValue(attributeName, value);
        return true;
    }

    private static string? NormalizeBoolean(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed switch
        {
            "0" or "1" => trimmed,
            "true" => "1",
            "false" => "0",
            _ => null
        };
    }

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }
}
