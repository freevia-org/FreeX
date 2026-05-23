using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxExternalLinkReferencePreserver
{
    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var sourceWorkbookEntry = sourceArchive.GetEntry("xl/workbook.xml");
        var targetWorkbookEntry = targetArchive.GetEntry("xl/workbook.xml");
        var targetWorkbookRelsEntry = targetArchive.GetEntry("xl/_rels/workbook.xml.rels");
        if (sourceWorkbookEntry is null || targetWorkbookEntry is null || targetWorkbookRelsEntry is null)
            return;

        var sourceWorkbookXml = XlsxPackageXmlEditor.LoadXml(sourceWorkbookEntry);
        var sourceExternalReferences = sourceWorkbookXml.Root?
            .Element(workbookNs + "externalReferences")?
            .Elements(workbookNs + "externalReference")
            .ToList()
            ?? [];
        if (sourceExternalReferences.Count == 0)
            return;

        var sourceWorkbookRels = XlsxRelationshipReader.LoadTargets(
            sourceArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var targetWorkbookXml = XlsxPackageXmlEditor.LoadXml(targetWorkbookEntry);
        var targetWorkbookRelsXml = XlsxPackageXmlEditor.LoadXml(targetWorkbookRelsEntry);
        var targetRoot = targetWorkbookXml.Root;
        if (targetRoot is null)
            return;

        var targetExternalReferences = targetRoot.Element(workbookNs + "externalReferences");
        if (targetExternalReferences is null)
        {
            targetExternalReferences = new XElement(workbookNs + "externalReferences");
            targetRoot.Add(targetExternalReferences);
        }

        foreach (var sourceReference in sourceExternalReferences)
        {
            var sourceRelId = sourceReference.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(sourceRelId) ||
                !sourceWorkbookRels.TryGetValue(sourceRelId, out var externalLinkPath))
            {
                continue;
            }

            var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                targetWorkbookRelsXml,
                packageRelNs,
                "xl/workbook.xml",
                externalLinkPath,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink");
            if (!targetExternalReferences
                    .Elements(workbookNs + "externalReference")
                    .Any(reference => string.Equals(reference.Attribute(relNs + "id")?.Value, targetRelId, StringComparison.OrdinalIgnoreCase)))
            {
                targetExternalReferences.Add(new XElement(
                    workbookNs + "externalReference",
                    new XAttribute(relNs + "id", targetRelId)));
            }
        }

        if (!targetExternalReferences.HasElements)
            targetExternalReferences.Remove();

        XlsxPackageXmlEditor.ReplaceXml(targetArchive, "xl/workbook.xml", targetWorkbookXml);
        XlsxPackageXmlEditor.ReplaceXml(targetArchive, "xl/_rels/workbook.xml.rels", targetWorkbookRelsXml);
    }
}
