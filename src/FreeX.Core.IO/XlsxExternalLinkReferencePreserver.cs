using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxExternalLinkReferencePreserver
{
    public static void Preserve(XlsxSourcePackagePreservationContext? context)
    {
        if (context is null || !context.HasTargetWorkbookRelationshipsPart)
            return;

        var workbookNs = context.WorkbookNs;
        var relNs = context.RelNs;
        var packageRelNs = context.PackageRelNs;
        var sourceExternalReferences = context.SourceWorkbookXml.Root?
            .Element(workbookNs + "externalReferences")?
            .Elements(workbookNs + "externalReference")
            .ToList()
            ?? [];
        if (sourceExternalReferences.Count == 0)
            return;

        var sourceWorkbookRels = context.SourceWorkbookRels;
        var targetWorkbookXml = context.LoadCurrentTargetWorkbookXml();
        var targetWorkbookRelsXml = context.LoadCurrentTargetWorkbookRelationshipsXml();
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

        // R101-io-external-link-preserver-duplicate-rid: mirrors XlsxExternalLinkMetadataReader's
        // seenRelationshipIds guard on the read side. Nothing in ECMA-376 (CT_ExternalReference,
        // §18.13 externalReference) requires r:id to be unique across sibling <externalReference>
        // elements -- only that it be present -- so a source workbook.xml can (and, observed in the
        // wild, does) carry two <externalReference> elements with the IDENTICAL r:id, each its own
        // ordinal '[n]' slot. The read side already treats the second occurrence of a repeated
        // r:id as its own (blank-placeholder) slot rather than resolving it again. Before this fix,
        // the save side disagreed: EnsureRelationshipForPackagePart resolves the same sourceRelId to
        // the same targetRelId both times, and the old post-hoc ".Any(...targetRelId)" dedup below
        // then silently skipped emitting the second <externalReference> element entirely --
        // collapsing two ordinal slots into one and shifting every later '[n]' index down by one.
        // Track which sourceRelIds have already been consumed and divert a repeat into the same
        // placeholder-reservation branch used for a blank/unresolvable r:id, so it still reserves
        // its own ordinal slot (unbacked by a Relationship, exactly like the read side's blank
        // placeholder for the same case) instead of disappearing.
        var seenSourceRelIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceReference in sourceExternalReferences)
        {
            var sourceRelId = sourceReference.Attribute(relNs + "id")?.Value.Trim();
            if (string.IsNullOrWhiteSpace(sourceRelId) ||
                !seenSourceRelIds.Add(sourceRelId) ||
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
            // R101: always emit one <externalReference> per distinct source ordinal slot now that
            // seenSourceRelIds above already diverts a repeated sourceRelId away from this branch --
            // this branch only ever runs once per distinct sourceRelId. Two DIFFERENT sourceRelIds
            // that both happen to resolve to the same externalLinkPath (so EnsureRelationshipForPackagePart
            // reuses the same target Relationship/targetRelId for both, per its dedup-by-target-path
            // contract) must still each get their own <externalReference> element -- the source had
            // two real ordinal slots there, and the read side (XlsxExternalLinkMetadataReader) will
            // resolve both independently since its own seenRelationshipIds guard keys on the
            // (distinct) sourceRelId, not on the resolved target path. A prior post-hoc dedup here
            // that skipped re-adding an element for an already-seen targetRelId collapsed that case
            // into a single ordinal slot; removed.
            targetExternalReferences.Add(new XElement(
                workbookNs + "externalReference",
                new XAttribute(relNs + "id", targetRelId)));
        }

        if (!targetExternalReferences.HasElements)
            targetExternalReferences.Remove();

        context.ReplaceTargetWorkbookXml(targetWorkbookXml);
        context.ReplaceTargetWorkbookRelationshipsXml(targetWorkbookRelsXml);
    }
}
