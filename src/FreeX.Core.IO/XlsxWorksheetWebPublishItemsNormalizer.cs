using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetWebPublishItemsNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private const string WebPublishItemsPath = "xl/webPublishItems.xml";
    private const string WorksheetWebPublishItemsRelationshipTarget = "../webPublishItems.xml";
    private const string WebPublishItemsContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.webPublishItems+xml";
    private const string WebPublishItemsRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/webPublishItems";

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
        var worksheetPathsWithWebPublishItems = new List<string>();
        foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (NormalizeWorksheetRoot(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);

            if (root.Elements(WorksheetNs + "webPublishItems").Any())
                worksheetPathsWithWebPublishItems.Add(worksheetEntry.FullName);
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

        NormalizePackageMetadata(archive, worksheetPathsWithWebPublishItems);
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
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(webPublishItem);
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
        return path.Equals(WebPublishItemsPath, StringComparison.OrdinalIgnoreCase);
    }

    private static void NormalizePackageMetadata(ZipArchive archive, IReadOnlyCollection<string> worksheetPathsWithWebPublishItems)
    {
        if (worksheetPathsWithWebPublishItems.Count == 0 ||
            archive.GetEntry(WebPublishItemsPath) is null)
        {
            return;
        }

        EnsureWebPublishItemsContentType(archive);
        foreach (var worksheetPath in worksheetPathsWithWebPublishItems)
            EnsureWorksheetWebPublishItemsRelationship(archive, worksheetPath);
    }

    private static void EnsureWebPublishItemsContentType(ZipArchive archive)
    {
        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            return;

        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        var hasCorrectOverride = contentTypesXml.Root?
            .Elements(ContentTypeNs + "Override")
            .Any(element =>
                string.Equals(element.Attribute("PartName")?.Value, "/" + WebPublishItemsPath, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(element.Attribute("ContentType")?.Value, WebPublishItemsContentType, StringComparison.Ordinal))
            == true;
        if (hasCorrectOverride)
            return;

        XlsxPackageXmlEditor.EnsureSpecificContentType(archive, WebPublishItemsPath, WebPublishItemsContentType);
    }

    private static void EnsureWorksheetWebPublishItemsRelationship(ZipArchive archive, string worksheetPath)
    {
        var relationshipsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relationshipsXml = archive.GetEntry(relationshipsPath) is { } relationshipsEntry
            ? XlsxPackageXmlEditor.LoadXml(relationshipsEntry)
            : new XDocument(new XElement(PackageRelNs + "Relationships"));

        if (HasWebPublishItemsRelationship(relationshipsXml, worksheetPath))
            return;

        var root = relationshipsXml.Root;
        if (root is null)
            return;

        root.Add(new XElement(
            PackageRelNs + "Relationship",
            new XAttribute("Id", XlsxPackageXmlEditor.NextRelationshipId(relationshipsXml, PackageRelNs)),
            new XAttribute("Type", WebPublishItemsRelationshipType),
            new XAttribute("Target", WorksheetWebPublishItemsRelationshipTarget)));

        XlsxPackageXmlEditor.ReplaceXml(archive, relationshipsPath, relationshipsXml);
    }

    private static bool HasWebPublishItemsRelationship(XDocument relationshipsXml, string worksheetPath) =>
        relationshipsXml.Root?
            .Elements(PackageRelNs + "Relationship")
            .Any(relationship =>
            {
                if (!string.Equals(
                        relationship.Attribute("Type")?.Value,
                        WebPublishItemsRelationshipType,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var target = relationship.Attribute("Target")?.Value;
                return !string.IsNullOrWhiteSpace(target) &&
                       string.Equals(
                           XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target),
                           WebPublishItemsPath,
                           StringComparison.OrdinalIgnoreCase);
            }) == true;
}
