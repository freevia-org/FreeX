using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxUnsupportedSheetReferencePreserver
{
    public static void Preserve(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext? context)
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

        var sourceRelationships = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in sourceWorkbookRelsXml.Root?.Elements(packageRelNs + "Relationship") ?? [])
        {
            var id = relationship.Attribute("Id")?.Value;
            if (!string.IsNullOrWhiteSpace(id))
                sourceRelationships.TryAdd(id, relationship);
        }

        var worksheetPathRebindings = CreateWorksheetPathRebindings(context);
        // Map sheet name -> generated <sheet> element so chartsheets that FreeX now models as Sheets
        // (so ClosedXML emits a placeholder worksheet for them) can be reclaimed: the generated
        // <sheet> entry is re-pointed at the preserved chartsheet part and the stray worksheet part
        // is removed below.
        var targetSheetsByName = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var targetSheet in targetSheets.Elements(workbookNs + "sheet"))
        {
            var targetName = targetSheet.Attribute("name")?.Value;
            if (!string.IsNullOrWhiteSpace(targetName))
                targetSheetsByName.TryAdd(targetName, targetSheet);
        }
        var targetSheetNames = new HashSet<string>(targetSheetsByName.Keys, StringComparer.OrdinalIgnoreCase);

        // R76-io-chartsheet-4-2/4-3: a chartsheet (unlike a dialog/macro sheet) IS modeled as a
        // live Sheet, so a name mismatch here can mean either "the user renamed it" (its generated
        // placeholder now lives under a different name) or "the user deleted it" (no placeholder
        // exists at all anymore) -- name alone can't tell those apart, and the source archive is
        // immutable so re-checking the OLD name is not the same as consulting the live model.
        // Instead, pair every source sheet name that ClosedXML did NOT reproduce in the target
        // against every target sheet name that has NO counterpart anywhere in the source -- the
        // latter set is exactly the placeholders ClosedXML wrote under a brand-new name for a
        // renamed sheet (a genuinely deleted sheet leaves no such orphaned target name behind, so
        // the queue is correctly empty for it). Pairing is by relative order among these "orphaned"
        // names only, so unrelated sheets that kept their name (or were added/removed elsewhere)
        // never shift the match, unlike pairing by raw absolute position would.
        var sourceSheetNamesAll = sourceSheets
            .Elements(workbookNs + "sheet")
            .Select(sheet => sheet.Attribute("name")?.Value)
            .Where(sheetName => !string.IsNullOrWhiteSpace(sheetName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unclaimedRenamedTargetSheets = new Queue<XElement>(
            targetSheets.Elements(workbookNs + "sheet")
                .Where(sheet =>
                {
                    var targetName = sheet.Attribute("name")?.Value;
                    return !string.IsNullOrWhiteSpace(targetName) && !sourceSheetNamesAll.Contains(targetName);
                }));

        var usedSheetIds = targetSheets
            .Elements(workbookNs + "sheet")
            .Select(sheet => XlsxXmlAttributeReader.ReadIntAttribute(sheet, "sheetId"))
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .ToHashSet();
        var nextSheetId = usedSheetIds.Count == 0 ? 1 : usedSheetIds.Max() + 1;
        var reclaimedWorksheetParts = new List<(string Part, string? RelId)>();
        var changed = false;

        foreach (var sourceSheet in sourceSheets.Elements(workbookNs + "sheet"))
        {
            var name = sourceSheet.Attribute("name")?.Value;
            var sourceRelId = sourceSheet.Attribute(relNs + "id")?.Value;
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(sourceRelId) ||
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

            // Case 1: the generated workbook already lists this sheet name (FreeX modeled the
            // chartsheet as a Sheet, so ClosedXML emitted a placeholder worksheet). Re-point that
            // entry at the preserved chartsheet part and schedule the stray worksheet for removal.
            var collidingTargetSheet = targetSheetsByName.TryGetValue(name, out var sameNameTargetSheet)
                ? sameNameTargetSheet
                : IsChartsheetRelationshipType(relationshipType) && unclaimedRenamedTargetSheets.Count > 0
                    ? unclaimedRenamedTargetSheets.Dequeue()
                    : null;

            if (collidingTargetSheet is not null)
            {
                var collidingRelId = collidingTargetSheet.Attribute(relNs + "id")?.Value;
                var collidingWorksheetPart = ResolveSheetRelationshipPart(
                    targetWorkbookRelsXml, packageRelNs, collidingRelId);

                var reboundRelId = XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                    targetWorkbookRelsXml,
                    packageRelNs,
                    "xl/workbook.xml",
                    targetPart,
                    relationshipType);
                collidingTargetSheet.SetAttributeValue(relNs + "id", reboundRelId);

                if (!string.IsNullOrWhiteSpace(collidingWorksheetPart) &&
                    IsWorksheetPartPath(collidingWorksheetPart) &&
                    !string.Equals(collidingWorksheetPart, targetPart, StringComparison.OrdinalIgnoreCase))
                {
                    reclaimedWorksheetParts.Add((collidingWorksheetPart, collidingRelId));
                }

                changed = true;
                continue;
            }

            // R76-io-chartsheet-4-3: a chartsheet has no surviving placeholder under any name
            // (Case 1 and the rename queue above both missed) -- the user removed it from the
            // live model. Re-adding it here would resurrect a sheet the user deleted, so unlike a
            // dialog/macro sheet (which is never modeled and always falls through to the
            // unconditional re-add below), a chartsheet is simply dropped.
            if (IsChartsheetRelationshipType(relationshipType))
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

        changed |= RemoveReclaimedWorksheetParts(
            targetArchive,
            targetWorkbookRelsXml,
            reclaimedWorksheetParts,
            packageRelNs);

        changed |= RebindUnsupportedSheetSidecarRelationships(
            sourceArchive,
            targetArchive,
            worksheetPathRebindings,
            packageRelNs);

        if (!changed)
            return;

        XlsxPackageXmlEditor.ReplaceXml(targetArchive, "xl/workbook.xml", targetWorkbookXml);
        XlsxPackageXmlEditor.ReplaceXml(targetArchive, "xl/_rels/workbook.xml.rels", targetWorkbookRelsXml);
    }

    private static bool IsWorksheetRelationshipType(string relationshipType) =>
        relationshipType.EndsWith("/worksheet", StringComparison.OrdinalIgnoreCase);

    // Chartsheets are the only unsupported-sheet-type kind FreeX models as a live Sheet (dialog
    // sheets and macro sheets are never modeled for XLSX -- see Sheet.Kind/SheetKind), so only a
    // chartsheet can genuinely be renamed or deleted from the model between load and save.
    private static bool IsChartsheetRelationshipType(string relationshipType) =>
        relationshipType.EndsWith("/chartsheet", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveSheetRelationshipPart(
        XDocument workbookRelsXml,
        XNamespace packageRelNs,
        string? relId)
    {
        if (string.IsNullOrWhiteSpace(relId))
            return null;

        foreach (var relationship in workbookRelsXml.Root?.Elements(packageRelNs + "Relationship") ?? [])
        {
            if (!string.Equals(relationship.Attribute("Id")?.Value, relId, StringComparison.OrdinalIgnoreCase))
                continue;

            var target = relationship.Attribute("Target")?.Value;
            return string.IsNullOrWhiteSpace(target)
                ? null
                : XlsxPackagePath.ResolveRelationshipTarget("xl/workbook.xml", target);
        }

        return null;
    }

    // Removes placeholder worksheet parts that ClosedXML generated for sheets which are actually
    // chartsheets (or other unsupported sheet types). The workbook <sheet> entry has already been
    // re-pointed at the preserved chartsheet part, so the generated worksheet part, its sidecar
    // relationships, the workbook relationship, and the [Content_Types].xml override are now dead.
    private static bool RemoveReclaimedWorksheetParts(
        ZipArchive targetArchive,
        XDocument targetWorkbookRelsXml,
        IReadOnlyList<(string Part, string? RelId)> reclaimedWorksheetParts,
        XNamespace packageRelNs)
    {
        if (reclaimedWorksheetParts.Count == 0)
            return false;

        // A worksheet part is only safe to remove when no surviving workbook <sheet> still points at
        // it (Excel allows two sheets to share neither name nor part, but guard against aliasing).
        var changed = false;
        foreach (var (part, relId) in reclaimedWorksheetParts)
        {
            if (!string.IsNullOrWhiteSpace(relId))
            {
                var relationship = targetWorkbookRelsXml.Root?
                    .Elements(packageRelNs + "Relationship")
                    .FirstOrDefault(r => string.Equals(r.Attribute("Id")?.Value, relId, StringComparison.OrdinalIgnoreCase));
                relationship?.Remove();
            }

            targetArchive.GetEntry(part)?.Delete();
            targetArchive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(part))?.Delete();
            RemoveContentTypeOverride(targetArchive, "/" + part);
            changed = true;
        }

        return changed;
    }

    private static void RemoveContentTypeOverride(ZipArchive targetArchive, string partName)
    {
        var contentTypesEntry = targetArchive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            return;

        XNamespace contentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        var overrideElement = contentTypesXml.Root?
            .Elements(contentTypesNs + "Override")
            .FirstOrDefault(element =>
                string.Equals(element.Attribute("PartName")?.Value, partName, StringComparison.OrdinalIgnoreCase));
        if (overrideElement is null)
            return;

        overrideElement.Remove();
        XlsxPackageXmlEditor.ReplaceXml(targetArchive, "[Content_Types].xml", contentTypesXml);
    }

    private static IReadOnlyDictionary<string, string> CreateWorksheetPathRebindings(
        XlsxSourcePackagePreservationContext? context)
    {
        if (context is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var rebindings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceSheet in context.SourceSheets)
        {
            if (!IsWorksheetPartPath(sourceSheet.Value) ||
                !context.TargetSheets.TryGetValue(sourceSheet.Key, out var targetPath) ||
                !IsWorksheetPartPath(targetPath) ||
                string.Equals(sourceSheet.Value, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rebindings[sourceSheet.Value] = targetPath;
        }

        return rebindings;
    }

    private static bool RebindUnsupportedSheetSidecarRelationships(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        IReadOnlyDictionary<string, string> worksheetPathRebindings,
        XNamespace packageRelNs)
    {
        var changed = false;
        foreach (var sheetPath in targetArchive.Entries
                     .Select(entry => entry.FullName)
                     .Where(IsUnsupportedSheetPartPath)
                     .ToArray())
        {
            var relsPath = XlsxPackagePath.GetRelationshipPartPath(sheetPath);
            var relsEntry = targetArchive.GetEntry(relsPath);
            var relsXml = relsEntry is not null
                ? XlsxPackageXmlEditor.LoadXml(relsEntry)
                : CreateReboundUnsupportedSheetRelationshipPart(
                    sourceArchive,
                    targetArchive,
                    sheetPath,
                    worksheetPathRebindings,
                    packageRelNs);
            if (relsXml is null)
                continue;

            var relsChanged = relsEntry is null;
            relsChanged |= MergeReboundUnsupportedSheetRelationships(
                sourceArchive,
                targetArchive,
                sheetPath,
                relsXml,
                worksheetPathRebindings,
                packageRelNs);
            foreach (var relationship in relsXml.Root?.Elements(packageRelNs + "Relationship") ?? [])
            {
                if (string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
                    continue;

                var target = relationship.Attribute("Target")?.Value;
                if (string.IsNullOrWhiteSpace(target))
                    continue;

                var resolvedTarget = XlsxPackagePath.ResolveRelationshipTarget(sheetPath, target);
                if (!worksheetPathRebindings.TryGetValue(resolvedTarget, out var reboundTarget))
                    continue;

                relationship.SetAttributeValue("Target", GetUnsupportedSheetRelationshipTarget(sheetPath, reboundTarget));
                relsChanged = true;
            }

            var sheetChanged = RebindUnsupportedSheetRelationshipReferenceIds(
                sourceArchive,
                targetArchive,
                sheetPath,
                relsXml,
                worksheetPathRebindings,
                packageRelNs);

            if (!relsChanged)
            {
                changed |= sheetChanged;
                continue;
            }

            XlsxPackageXmlEditor.ReplaceXml(targetArchive, relsPath, relsXml);
            changed = true;
        }

        return changed;
    }

    private static bool RebindUnsupportedSheetRelationshipReferenceIds(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string sheetPath,
        XDocument targetRelsXml,
        IReadOnlyDictionary<string, string> worksheetPathRebindings,
        XNamespace packageRelNs)
    {
        var sourceRelsEntry = sourceArchive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(sheetPath));
        var targetSheetEntry = targetArchive.GetEntry(sheetPath);
        if (sourceRelsEntry is null || targetSheetEntry is null)
            return false;

        var targetRelationshipsBySignature = targetRelsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .GroupBy(RelationshipSignature, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        if (targetRelationshipsBySignature is null || targetRelationshipsBySignature.Count == 0)
            return false;

        var sourceRelsXml = XlsxPackageXmlEditor.LoadXml(sourceRelsEntry);
        Dictionary<string, string>? relationshipIdMap = null;
        foreach (var sourceRelationship in sourceRelsXml.Root?.Elements(packageRelNs + "Relationship") ?? [])
        {
            var sourceId = sourceRelationship.Attribute("Id")?.Value;
            if (string.IsNullOrWhiteSpace(sourceId))
                continue;

            var expectedRelationship = TryCreateReboundRelationship(
                sourceRelationship,
                targetArchive,
                sheetPath,
                worksheetPathRebindings);
            if (expectedRelationship is null ||
                !targetRelationshipsBySignature.TryGetValue(RelationshipSignature(expectedRelationship), out var targetRelationship))
            {
                continue;
            }

            var targetId = targetRelationship.Attribute("Id")?.Value;
            if (string.IsNullOrWhiteSpace(targetId) ||
                string.Equals(sourceId, targetId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            relationshipIdMap ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            relationshipIdMap[sourceId] = targetId;
        }

        if (relationshipIdMap is null || relationshipIdMap.Count == 0)
            return false;

        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var sheetXml = XlsxPackageXmlEditor.LoadXml(targetSheetEntry);
        var changed = false;
        foreach (var attribute in sheetXml.Descendants().Attributes().Where(attribute => attribute.Name.Namespace == relNs))
        {
            if (!relationshipIdMap.TryGetValue(attribute.Value, out var replacementId))
                continue;

            attribute.Value = replacementId;
            changed = true;
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, sheetPath, sheetXml);

        return changed;
    }

    private static bool MergeReboundUnsupportedSheetRelationships(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string sheetPath,
        XDocument targetRelsXml,
        IReadOnlyDictionary<string, string> worksheetPathRebindings,
        XNamespace packageRelNs)
    {
        var sourceRelsEntry = sourceArchive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(sheetPath));
        if (sourceRelsEntry is null)
            return false;

        var targetRoot = targetRelsXml.Root;
        if (targetRoot is null)
            return false;

        var sourceRelsXml = XlsxPackageXmlEditor.LoadXml(sourceRelsEntry);
        var usedIds = targetRoot
            .Elements(packageRelNs + "Relationship")
            .Select(relationship => relationship.Attribute("Id")?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var signatures = targetRoot
            .Elements(packageRelNs + "Relationship")
            .Select(RelationshipSignature)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var sourceRelationship in sourceRelsXml.Root?.Elements(packageRelNs + "Relationship") ?? [])
        {
            var rebound = TryCreateReboundRelationship(
                sourceRelationship,
                targetArchive,
                sheetPath,
                worksheetPathRebindings);
            if (rebound is null)
                continue;

            if (!signatures.Add(RelationshipSignature(rebound)))
                continue;

            var id = rebound.Attribute("Id")?.Value;
            if (string.IsNullOrWhiteSpace(id) || !usedIds.Add(id))
                rebound.SetAttributeValue("Id", XlsxPackageXmlEditor.NextRelationshipId(targetRelsXml, packageRelNs));

            id = rebound.Attribute("Id")?.Value;
            if (!string.IsNullOrWhiteSpace(id))
                usedIds.Add(id);

            targetRoot.Add(rebound);
            changed = true;
        }

        return changed;
    }

    private static XDocument? CreateReboundUnsupportedSheetRelationshipPart(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string sheetPath,
        IReadOnlyDictionary<string, string> worksheetPathRebindings,
        XNamespace packageRelNs)
    {
        var sourceRelsEntry = sourceArchive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(sheetPath));
        if (sourceRelsEntry is null)
            return null;

        var sourceRelsXml = XlsxPackageXmlEditor.LoadXml(sourceRelsEntry);
        var targetRelsXml = new XDocument(new XElement(packageRelNs + "Relationships"));
        var usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var signatures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceRelationship in sourceRelsXml.Root?.Elements(packageRelNs + "Relationship") ?? [])
        {
            var copy = TryCreateReboundRelationship(
                sourceRelationship,
                targetArchive,
                sheetPath,
                worksheetPathRebindings);
            if (copy is null)
                continue;

            var signature = RelationshipSignature(copy);
            if (!signatures.Add(signature))
                continue;

            var id = copy.Attribute("Id")?.Value;
            if (string.IsNullOrWhiteSpace(id))
                copy.SetAttributeValue("Id", XlsxPackageXmlEditor.NextRelationshipId(targetRelsXml, packageRelNs));
            else if (!usedIds.Add(id))
                copy.SetAttributeValue("Id", XlsxPackageXmlEditor.NextRelationshipId(targetRelsXml, packageRelNs));

            id = copy.Attribute("Id")?.Value;
            if (!string.IsNullOrWhiteSpace(id))
                usedIds.Add(id);

            targetRelsXml.Root!.Add(copy);
        }

        return targetRelsXml.Root!.HasElements ? targetRelsXml : null;
    }

    private static XElement? TryCreateReboundRelationship(
        XElement sourceRelationship,
        ZipArchive targetArchive,
        string sheetPath,
        IReadOnlyDictionary<string, string> worksheetPathRebindings)
    {
        var copy = new XElement(sourceRelationship);
        var target = copy.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
            return null;

        if (!string.Equals(copy.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
        {
            var resolvedTarget = XlsxPackagePath.ResolveRelationshipTarget(sheetPath, target);
            if (worksheetPathRebindings.TryGetValue(resolvedTarget, out var reboundTarget))
                copy.SetAttributeValue("Target", GetUnsupportedSheetRelationshipTarget(sheetPath, reboundTarget));
            else if (targetArchive.GetEntry(resolvedTarget) is null)
                return null;
        }

        return copy;
    }

    private static string RelationshipSignature(XElement relationship) =>
        string.Join("|",
            relationship.Attribute("Type")?.Value.Trim() ?? "",
            relationship.Attribute("Target")?.Value.Trim().Replace('\\', '/') ?? "",
            relationship.Attribute("TargetMode")?.Value.Trim() ?? "");

    private static string GetUnsupportedSheetRelationshipTarget(string sheetPath, string targetPath)
    {
        var sourceDirectory = sheetPath.Replace('\\', '/');
        var slash = sourceDirectory.LastIndexOf('/');
        sourceDirectory = slash >= 0 ? sourceDirectory[..slash] : "";
        if ((sourceDirectory.Equals("xl/chartsheets", StringComparison.OrdinalIgnoreCase) ||
             sourceDirectory.Equals("xl/dialogSheets", StringComparison.OrdinalIgnoreCase) ||
             sourceDirectory.Equals("xl/macroSheets", StringComparison.OrdinalIgnoreCase)) &&
            targetPath.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase))
        {
            return $"../worksheets/{targetPath["xl/worksheets/".Length..]}";
        }

        return XlsxPackagePath.GetRelationshipTarget(sheetPath, targetPath);
    }

    private static bool IsUnsupportedSheetPartPath(string path) =>
        path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
        (path.StartsWith("xl/chartsheets/", StringComparison.OrdinalIgnoreCase) ||
         path.StartsWith("xl/dialogSheets/", StringComparison.OrdinalIgnoreCase) ||
         path.StartsWith("xl/macroSheets/", StringComparison.OrdinalIgnoreCase));

    private static bool IsWorksheetPartPath(string path) =>
        path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
}
