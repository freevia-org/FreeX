using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxPivotPackageCleaner
{
    public static void RemovePivotPackageMetadata(ZipArchive archive)
    {
        RemoveWorkbookPivotCacheReferences(archive);
        RemoveWorksheetPivotTableReferences(archive);
        RemovePivotRelationships(archive);
        RemovePivotPackageParts(archive);
    }

    private static void RemoveWorkbookPivotCacheReferences(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        workbookXml.Root?.Elements(workbookNs + "pivotCaches").Remove();
        XlsxPackageXmlEditor.ReplaceXml(archive, "xl/workbook.xml", workbookXml);
    }

    private static void RemoveWorksheetPivotTableReferences(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var worksheetEntry in archive.Entries
                     .Where(entry =>
                         entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var pivotReferences = root.Elements(workbookNs + "pivotTableDefinition").ToList();
            if (pivotReferences.Count == 0)
                continue;

            pivotReferences.Remove();
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    private static void RemovePivotRelationships(ZipArchive archive)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        foreach (var relsEntry in archive.Entries
                     .Where(entry => entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
            var root = relsXml.Root;
            if (root is null)
                continue;

            var pivotRelationships = root
                .Elements(packageRelNs + "Relationship")
                .Where(relationship =>
                {
                    var type = relationship.Attribute("Type")?.Value ?? "";
                    return type.EndsWith("/pivotCacheDefinition", StringComparison.OrdinalIgnoreCase) ||
                           type.EndsWith("/pivotTable", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
            if (pivotRelationships.Count == 0)
                continue;

            pivotRelationships.Remove();
            XlsxPackageXmlEditor.ReplaceXml(archive, relsEntry.FullName, relsXml);
        }
    }

    private static void RemovePivotPackageParts(ZipArchive archive)
    {
        foreach (var entry in archive.Entries
                     .Where(entry =>
                         entry.FullName.StartsWith("xl/pivotCache/", StringComparison.OrdinalIgnoreCase) ||
                         entry.FullName.StartsWith("xl/pivotTables/", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            entry.Delete();
        }

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            return;

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        var root = contentTypesXml.Root;
        if (root is null)
            return;

        var pivotOverrides = root
            .Elements(contentTypeNs + "Override")
            .Where(element =>
            {
                var partName = element.Attribute("PartName")?.Value ?? "";
                return partName.StartsWith("/xl/pivotCache/", StringComparison.OrdinalIgnoreCase) ||
                       partName.StartsWith("/xl/pivotTables/", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();
        if (pivotOverrides.Count == 0)
            return;

        pivotOverrides.Remove();
        XlsxPackageXmlEditor.ReplaceXml(archive, "[Content_Types].xml", contentTypesXml);
    }
}
