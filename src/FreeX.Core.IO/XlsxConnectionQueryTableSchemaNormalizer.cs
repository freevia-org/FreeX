using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxConnectionQueryTableSchemaNormalizer
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string QueryTableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/queryTable";

    private static readonly string[] ConnectionUnsignedIntAttributes =
    [
        "id",
        "minRefreshableVersion",
        "refreshedVersion",
        "reconnectionMethod",
        "interval"
    ];

    private static readonly string[] ConnectionBooleanAttributes =
    [
        "deleted",
        "onlyUseConnectionFile",
        "background",
        "refreshOnLoad",
        "saveData",
        "savePassword",
        "new",
        "keepAlive"
    ];

    private static readonly string[] QueryTableUnsignedIntAttributes =
    [
        "connectionId",
        "autoFormatId"
    ];

    private static readonly string[] QueryTableBooleanAttributes =
    [
        "headers",
        "rowNumbers",
        "disableRefresh",
        "backgroundRefresh",
        "firstBackgroundRefresh",
        "refreshOnLoad",
        "fillFormulas",
        "removeDataOnSave",
        "disableEdit",
        "preserveFormatting",
        "adjustColumnWidth",
        "intermediate",
        "applyNumberFormats",
        "applyBorderFormats",
        "applyFontFormats",
        "applyPatternFormats",
        "applyAlignmentFormats",
        "applyWidthHeightFormats"
    ];

    public static void NormalizePackage(ZipArchive archive)
    {
        NormalizeConnections(archive);
        NormalizeQueryTables(archive);
        NormalizeWorksheetQueryTableParts(archive);
    }

    private static void NormalizeConnections(ZipArchive archive)
    {
        var entry = archive.GetEntry("xl/connections.xml");
        if (entry is null)
            return;

        var document = XlsxPackageXmlEditor.LoadXml(entry);
        var root = document.Root;
        if (root is null)
            return;

        var changed = false;
        var count = root.Attribute("count");
        if (count is not null)
        {
            count.Remove();
            changed = true;
        }

        var connections = root.Elements(WorksheetNs + "connection").ToArray();
        for (var index = 0; index < connections.Length; index++)
            changed |= NormalizeConnection(connections[index], index + 1);

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(archive, "xl/connections.xml", document);
    }

    private static bool NormalizeConnection(XElement connection, int fallbackId)
    {
        var changed = NormalizeRequiredUnsignedIntAttribute(connection, "id", fallbackId);
        changed |= NormalizeRequiredUnsignedIntAttribute(connection, "refreshedVersion", 0);

        foreach (var attributeName in ConnectionUnsignedIntAttributes)
        {
            if (attributeName is "id" or "refreshedVersion")
                continue;

            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(connection, attributeName, XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
        }

        foreach (var attributeName in ConnectionBooleanAttributes)
            changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(connection, attributeName, XlsxXmlNormalizationHelpers.NormalizeBoolean);

        return changed;
    }

    private static void NormalizeQueryTables(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.Where(IsQueryTableXmlEntry).ToList())
        {
            var document = XlsxPackageXmlEditor.LoadXml(entry);
            var root = document.Root;
            if (root is null)
                continue;

            var changed = NormalizeRequiredUnsignedIntAttribute(root, "connectionId", 1);
            foreach (var attributeName in QueryTableUnsignedIntAttributes)
            {
                if (attributeName == "connectionId")
                    continue;

                changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(root, attributeName, XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull);
            }

            foreach (var attributeName in QueryTableBooleanAttributes)
                changed |= XlsxXmlNormalizationHelpers.NormalizeAttribute(root, attributeName, XlsxXmlNormalizationHelpers.NormalizeBoolean);

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, entry.FullName, document);
        }
    }

    private static void NormalizeWorksheetQueryTableParts(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var document = XlsxPackageXmlEditor.LoadXml(entry);
            var root = document.Root;
            if (root is null)
                continue;

            var changed = false;
            var worksheetPath = XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/'));
            var validRelationshipIds = GetValidQueryTableRelationshipIds(archive, worksheetPath);
            foreach (var queryTableParts in root.Elements(WorksheetNs + "queryTableParts").ToList())
                changed |= NormalizeQueryTablePartsElement(queryTableParts, validRelationshipIds);

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, entry.FullName, document);
        }
    }

    private static bool NormalizeQueryTablePartsElement(
        XElement queryTableParts,
        IReadOnlySet<string> validRelationshipIds)
    {
        var changed = false;
        foreach (var queryTablePart in queryTableParts.Elements(WorksheetNs + "queryTablePart").ToList())
        {
            var relationshipId = queryTablePart.Attribute(RelNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(relationshipId) ||
                !validRelationshipIds.Contains(relationshipId))
            {
                queryTablePart.Remove();
                changed = true;
            }
        }

        var queryTablePartCount = queryTableParts
            .Elements(WorksheetNs + "queryTablePart")
            .Count();
        if (queryTablePartCount == 0)
        {
            queryTableParts.Remove();
            return true;
        }

        changed |= XlsxXmlNormalizationHelpers.SetAttributeIfChanged(
            queryTableParts,
            "count",
            queryTablePartCount.ToString(CultureInfo.InvariantCulture));
        return changed;
    }

    private static IReadOnlySet<string> GetValidQueryTableRelationshipIds(ZipArchive archive, string worksheetPath)
    {
        var relationshipsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
        if (relationshipsEntry is null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        XDocument relationshipsXml;
        try
        {
            relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var relationshipIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in relationshipsXml.Root?.Elements(PackageRelNs + "Relationship") ?? [])
        {
            var id = relationship.Attribute("Id")?.Value;
            if (string.IsNullOrWhiteSpace(id) ||
                !string.Equals(relationship.Attribute("Type")?.Value, QueryTableRelationshipType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = relationship.Attribute("Target")?.Value;
            var targetPart = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target ?? "");
            if (IsQueryTablePartPath(targetPart) &&
                archive.GetEntry(targetPart) is not null)
            {
                relationshipIds.Add(id);
            }
        }

        return relationshipIds;
    }

    private static bool NormalizeRequiredUnsignedIntAttribute(XElement element, string attributeName, int fallbackValue)
    {
        var normalized = XlsxXmlNormalizationHelpers.NormalizeUnsignedIntOrNull(element.Attribute(attributeName)?.Value) ??
                         fallbackValue.ToString(CultureInfo.InvariantCulture);
        return XlsxXmlNormalizationHelpers.SetAttributeIfChanged(element, attributeName, normalized);
    }

    private static bool IsQueryTableXmlEntry(ZipArchiveEntry entry) =>
        XlsxPackagePath.IsXmlEntryInDirectory(entry, "xl/queryTables/");

    private static bool IsQueryTablePartPath(string path) =>
        path.StartsWith("xl/queryTables/", StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);
}
