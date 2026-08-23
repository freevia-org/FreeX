using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxWorksheetWebPublishItemsNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = OpcRelationships.Namespace;
    private const string WebPublishItemsPath = "xl/webPublishItems.xml";
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
        foreach (var worksheetEntry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
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
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(webPublishItems, WebPublishItemsAttributes, RelNs + "id");
        changed |= XlsxXmlNormalizationHelpers.RemoveChildElementsExcept(webPublishItems, WorksheetNs + "webPublishItem");

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
        changed |= XlsxXmlNormalizationHelpers.RemoveUnknownAttributes(webPublishItem, WebPublishItemAttributes, RelNs + "id");
        changed |= XlsxXmlNormalizationHelpers.RemoveAllNodes(webPublishItem);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(webPublishItem, "id", XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(webPublishItem, "sourceType", value => XlsxXmlNormalizationHelpers.NormalizeToken(value, SourceTypeValues));
        changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(webPublishItem, "autoRepublish", XlsxXmlNormalizationHelpers.NormalizeBoolean);

        foreach (var attributeName in TextAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(webPublishItem, attributeName, XlsxXmlNormalizationHelpers.NormalizeOptionalText);

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

    private static bool IsWebPublishItemsPartEntry(ZipArchiveEntry entry)
    {
        var path = XlsxPackagePath.NormalizeEntryPath(entry);
        return path.Equals(WebPublishItemsPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes standalone webPublishItems.xml part entries and ensures package-level
    /// metadata (content-types, worksheet relationships). Called by the single-pass normalizer
    /// after all worksheet XML has been written back.
    /// </summary>
    internal static void NormalizePackageResidual(ZipArchive archive, IReadOnlyCollection<string> worksheetPathsWithWebPublishItems)
    {
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

    private static void NormalizePackageMetadata(ZipArchive archive, IReadOnlyCollection<string> worksheetPathsWithWebPublishItems)
    {
        if (worksheetPathsWithWebPublishItems.Count == 0 ||
            archive.GetEntry(WebPublishItemsPath) is null)
        {
            return;
        }

        XlsxPackageXmlEditor.EnsureSpecificContentType(archive, WebPublishItemsPath, WebPublishItemsContentType);
        foreach (var worksheetPath in worksheetPathsWithWebPublishItems)
            EnsureWorksheetWebPublishItemsRelationship(archive, worksheetPath);
    }

    private static void EnsureWorksheetWebPublishItemsRelationship(ZipArchive archive, string worksheetPath)
    {
        var relationshipsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relationshipsXml = archive.GetEntry(relationshipsPath) is { } relationshipsEntry
            ? XlsxPackageXmlEditor.LoadXml(relationshipsEntry)
            : OpcRelationships.CreateDocument();

        if (HasWebPublishItemsRelationship(relationshipsXml, worksheetPath))
            return;

        var root = relationshipsXml.Root;
        if (root is null)
            return;

        root.Add(OpcRelationships.CreateRelationship(
            OpcRelationships.NextRelationshipId(relationshipsXml, PackageRelNs),
            WebPublishItemsRelationshipType,
            XlsxPackagePath.GetRelationshipTarget(worksheetPath, WebPublishItemsPath)));

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
