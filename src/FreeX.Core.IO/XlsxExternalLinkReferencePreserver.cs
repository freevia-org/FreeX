using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

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

        // R96-io-external-link-preserver-1: ids a placeholder slot must never collide with. A
        // placeholder is deliberately left unbacked by any Relationship element (see below), so it
        // can't be tracked by adding it to targetWorkbookRelsXml the way EnsureRelationshipForPackagePart
        // tracks real relationships -- NextRelationshipId only scans ids actually IN that document, so
        // repeated calls would keep returning the same "next" id for every placeholder in this save.
        // Seed this set with every id already used in the target rels (so a placeholder can never
        // collide with -- and thereby accidentally resolve against -- an unrelated real relationship,
        // such as the worksheet/styles/theme rIds ClosedXML always writes) and keep adding each
        // newly minted placeholder id to it as they're handed out.
        var reservedRelIds = new HashSet<string>(
            targetWorkbookRelsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .Select(element => element.Attribute("Id")?.Value)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!)
                ?? [],
            StringComparer.OrdinalIgnoreCase);

        foreach (var sourceReference in sourceExternalReferences)
        {
            var sourceRelId = sourceReference.Attribute(relNs + "id")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(sourceRelId) ||
                !sourceWorkbookRels.TryGetValue(sourceRelId, out var externalLinkPath))
            {
                // Excel's '[n]' formula syntax addresses external references by their fixed ordinal
                // position in workbook.xml's <externalReference> list, not by how many of them
                // resolved (see XlsxExternalLinkMetadataReader, fixed the same way on the read side).
                // A blank/unresolvable r:id was already a broken reference in the SOURCE package;
                // dropping its <externalReference> element here would still be observable damage --
                // every later externalReference the source encoded would shift down one ordinal
                // slot, so a formula like '[3]Sheet1'!A1 that correctly addressed the source's third
                // external workbook would silently start addressing whatever became the new third
                // entry after the save. Reserve the slot instead: emit an <externalReference> whose
                // r:id is a freshly minted id guaranteed unused in the target rels (so it can't
                // accidentally collide with -- and thereby resolve against -- an unrelated real
                // target relationship) and intentionally leave it unbacked by any Relationship
                // element, exactly mirroring the dangling reference already present in the source.
                var placeholderIndex = 1;
                var placeholderRelId = $"rId{placeholderIndex}";
                while (!reservedRelIds.Add(placeholderRelId))
                {
                    placeholderIndex++;
                    placeholderRelId = $"rId{placeholderIndex}";
                }

                targetExternalReferences.Add(new XElement(
                    workbookNs + "externalReference",
                    new XAttribute(relNs + "id", placeholderRelId)));
                continue;
            }

            var targetRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                targetWorkbookRelsXml,
                packageRelNs,
                "xl/workbook.xml",
                externalLinkPath,
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/externalLink");
            reservedRelIds.Add(targetRelId);
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
