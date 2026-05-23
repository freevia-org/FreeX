using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxUnsupportedSheetReferencePreserver
{
    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var sourceWorkbookEntry = sourceArchive.GetEntry("xl/workbook.xml");
        var sourceWorkbookRelsEntry = sourceArchive.GetEntry("xl/_rels/workbook.xml.rels");
        var targetWorkbookEntry = targetArchive.GetEntry("xl/workbook.xml");
        var targetWorkbookRelsEntry = targetArchive.GetEntry("xl/_rels/workbook.xml.rels");
        if (sourceWorkbookEntry is null || sourceWorkbookRelsEntry is null ||
            targetWorkbookEntry is null || targetWorkbookRelsEntry is null)
        {
            return;
        }

        var sourceWorkbookXml = XlsxPackageXmlEditor.LoadXml(sourceWorkbookEntry);
        var sourceWorkbookRelsXml = XlsxPackageXmlEditor.LoadXml(sourceWorkbookRelsEntry);
        var targetWorkbookXml = XlsxPackageXmlEditor.LoadXml(targetWorkbookEntry);
        var targetWorkbookRelsXml = XlsxPackageXmlEditor.LoadXml(targetWorkbookRelsEntry);
        var sourceSheets = sourceWorkbookXml.Root?.Element(workbookNs + "sheets");
        var targetSheets = targetWorkbookXml.Root?.Element(workbookNs + "sheets");
        if (sourceSheets is null || targetSheets is null)
            return;

        var sourceRelationships = sourceWorkbookRelsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(relationship => !string.IsNullOrWhiteSpace(relationship.Attribute("Id")?.Value))
            .ToDictionary(
                relationship => relationship.Attribute("Id")!.Value,
                relationship => relationship,
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        var targetSheetNames = targetSheets
            .Elements(workbookNs + "sheet")
            .Select(sheet => sheet.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedSheetIds = targetSheets
            .Elements(workbookNs + "sheet")
            .Select(sheet => ReadIntAttribute(sheet, "sheetId"))
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .ToHashSet();
        var nextSheetId = usedSheetIds.Count == 0 ? 1 : usedSheetIds.Max() + 1;
        var changed = false;

        foreach (var sourceSheet in sourceSheets.Elements(workbookNs + "sheet"))
        {
            var name = sourceSheet.Attribute("name")?.Value;
            var sourceRelId = sourceSheet.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(sourceRelId) ||
                targetSheetNames.Contains(name) ||
                !sourceRelationships.TryGetValue(sourceRelId, out var sourceRelationship))
            {
                continue;
            }

            var relationshipType = sourceRelationship.Attribute("Type")?.Value;
            var target = sourceRelationship.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(relationshipType) ||
                string.IsNullOrWhiteSpace(target) ||
                IsWorksheetRelationshipType(relationshipType))
            {
                continue;
            }

            var targetPart = XlsxPackagePath.ResolveRelationshipTarget("xl/workbook.xml", target);
            if (targetArchive.GetEntry(targetPart) is null)
                continue;

            while (usedSheetIds.Contains(nextSheetId))
                nextSheetId++;

            var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                targetWorkbookRelsXml,
                packageRelNs,
                "xl/workbook.xml",
                targetPart,
                relationshipType);
            var preservedSheet = new XElement(sourceSheet);
            preservedSheet.SetAttributeValue(relNs + "id", targetRelId);
            preservedSheet.SetAttributeValue("sheetId", nextSheetId.ToString(CultureInfo.InvariantCulture));
            targetSheets.Add(preservedSheet);
            targetSheetNames.Add(name);
            usedSheetIds.Add(nextSheetId);
            changed = true;
        }

        if (!changed)
            return;

        XlsxPackageXmlEditor.ReplaceXml(targetArchive, "xl/workbook.xml", targetWorkbookXml);
        XlsxPackageXmlEditor.ReplaceXml(targetArchive, "xl/_rels/workbook.xml.rels", targetWorkbookRelsXml);
    }

    private static int? ReadIntAttribute(XElement element, string name)
    {
        var value = element.Attribute(name)?.Value;
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static bool IsWorksheetRelationshipType(string relationshipType) =>
        relationshipType.EndsWith("/worksheet", StringComparison.OrdinalIgnoreCase);
}
