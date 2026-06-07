using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetWebPublishItemsNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static readonly HashSet<string> WebPublishItemsAttributes =
    [
        "count"
    ];

    private static readonly HashSet<string> WebPublishItemAttributes =
    [
        "id",
        "divId",
        "sourceType",
        "sourceRef",
        "sourceObject",
        "destinationFile",
        "title",
        "autoRepublish"
    ];

    private static readonly HashSet<string> SourceTypeValues =
    [
        "sheet",
        "printArea",
        "autoFilter",
        "range",
        "chart",
        "pivotTable",
        "query",
        "label"
    ];

    private static readonly string[] TextAttributes =
    [
        "divId",
        "sourceRef",
        "sourceObject",
        "destinationFile",
        "title"
    ];

    public static void NormalizePackage(ZipArchive archive)
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

        foreach (var webPublishItemsEntry in archive.Entries.Where(IsWebPublishItemsPartEntry).ToList())
        {
            var webPublishItemsXml = XlsxPackageXmlEditor.LoadXml(webPublishItemsEntry);
            var root = webPublishItemsXml.Root;
            if (root is null || root.Name != WorksheetNs + "webPublishItems")
                continue;

            if (NormalizeElement(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, webPublishItemsEntry.FullName, webPublishItemsXml);
        }
    }

    public static bool NormalizeWorksheetRoot(XElement worksheetRoot)
    {
        var changed = false;
        var keptWebPublishItems = false;
        foreach (var webPublishItems in worksheetRoot.Elements(WorksheetNs + "webPublishItems").ToList())
        {
            if (keptWebPublishItems)
            {
                webPublishItems.Remove();
                changed = true;
                continue;
            }

            changed |= NormalizeElement(webPublishItems);
            keptWebPublishItems = true;
        }

        return changed;
    }

    public static bool NormalizeElement(XElement webPublishItems)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(webPublishItems, WebPublishItemsAttributes);
        changed |= RemoveUnexpectedChildElements(webPublishItems, WorksheetNs + "webPublishItem");

        foreach (var webPublishItem in webPublishItems.Elements(WorksheetNs + "webPublishItem").ToList())
        {
            changed |= NormalizeWebPublishItemElement(webPublishItem);
            if (!ShouldRemoveWebPublishItemElement(webPublishItem))
                continue;

            webPublishItem.Remove();
            changed = true;
        }

        changed |= NormalizeCount(webPublishItems);
        return changed;
    }

    private static bool NormalizeWebPublishItemElement(XElement webPublishItem)
    {
        var changed = false;
        changed |= RemoveUnknownAttributes(webPublishItem, WebPublishItemAttributes);
        changed |= RemoveAllNodes(webPublishItem);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(webPublishItem, "id", NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(webPublishItem, "sourceType", NormalizeSourceType);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(webPublishItem, "autoRepublish", NormalizeBoolean);

        foreach (var attributeName in TextAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(webPublishItem, attributeName, NormalizeOptionalText);

        return changed;
    }

    private static bool ShouldRemoveWebPublishItemElement(XElement webPublishItem) =>
        webPublishItem.Attribute("id") is null && webPublishItem.Attribute(RelNs + "id") is null ||
        string.IsNullOrWhiteSpace(webPublishItem.Attribute("divId")?.Value) ||
        string.IsNullOrWhiteSpace(webPublishItem.Attribute("destinationFile")?.Value);

    private static bool NormalizeCount(XElement webPublishItems)
    {
        var count = webPublishItems.Elements(WorksheetNs + "webPublishItem").Count().ToString(CultureInfo.InvariantCulture);
        return XlsxXmlNormalizationHelpers.SetAttributeIfChanged(webPublishItems, "count", count);
    }

    private static bool RemoveUnknownAttributes(XElement element, IReadOnlySet<string> allowedNames)
    {
        var changed = false;
        foreach (var attribute in element.Attributes().ToList())
        {
            if (attribute.IsNamespaceDeclaration ||
                attribute.Name == RelNs + "id" ||
                (attribute.Name.NamespaceName.Length == 0 && allowedNames.Contains(attribute.Name.LocalName)))
            {
                continue;
            }

            attribute.Remove();
            changed = true;
        }

        return changed;
    }

    private static bool RemoveUnexpectedChildElements(XElement element, XName allowedChildName)
    {
        var changed = false;
        foreach (var child in element.Elements().Where(child => child.Name != allowedChildName).ToList())
        {
            child.Remove();
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

    private static string? NormalizeBoolean(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed switch
        {
            "0" or "1" => trimmed,
            "true" or "false" => trimmed,
            _ => null
        };
    }

    private static string? NormalizeOptionalText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? NormalizeSourceType(string? value)
    {
        var trimmed = value?.Trim();
        return trimmed is not null && SourceTypeValues.Contains(trimmed) ? trimmed : null;
    }

    private static string? NormalizeUnsignedIntOrNull(string? value)
    {
        var trimmed = value?.Trim();
        return uint.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    private static bool IsWorksheetXmlEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
               path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWebPublishItemsPartEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
        return path.Equals("xl/webPublishItems.xml", StringComparison.OrdinalIgnoreCase);
    }
}
