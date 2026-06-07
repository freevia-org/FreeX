using System.IO.Compression;
using System.Xml.Linq;
using FreeX.Core.Model;

namespace FreeX.Core.IO;

internal static class XlsxLegacyCommentPreserver
{
    private const string CommentsRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/comments";
    private const string VmlDrawingRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing";

    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive, Workbook workbook)
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
        var targetWorkbookXml = XlsxPackageXmlEditor.LoadXml(targetWorkbookEntry);
        var sourceWorkbookRels = XlsxRelationshipReader.LoadTargets(
            sourceArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);
        var targetWorkbookRels = XlsxRelationshipReader.LoadTargets(
            targetArchive,
            "xl/_rels/workbook.xml.rels",
            "xl/workbook.xml",
            packageRelNs);

        var sourceSheets = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(sourceWorkbookXml, sourceWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);
        var targetSheets = XlsxWorkbookSheetPathReader.GetWorkbookSheetPaths(targetWorkbookXml, targetWorkbookRels, workbookNs, relNs)
            .ToDictionary(pair => pair.SheetName, pair => pair.WorksheetPath, StringComparer.OrdinalIgnoreCase);

        foreach (var (sheetName, sourceWorksheetPath) in sourceSheets)
        {
            if (!targetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var sheet = workbook.GetSheet(sheetName);
            if (sheet is null || sheet.Comments.Count == 0)
                continue;

            var sourceWorksheetEntry = sourceArchive.GetEntry(sourceWorksheetPath);
            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (sourceWorksheetEntry is null || targetWorksheetEntry is null)
                continue;

            var sourceWorksheetXml = XlsxPackageXmlEditor.LoadXml(sourceWorksheetEntry);
            var sourceCommentsPath = GetLegacyCommentPartPath(sourceArchive, sourceWorksheetPath, packageRelNs);
            if (sourceCommentsPath is null)
                continue;

            var sourceCommentsEntry = sourceArchive.GetEntry(sourceCommentsPath);
            if (sourceCommentsEntry is null)
                continue;

            var sourceCommentsXml = XlsxPackageXmlEditor.LoadXml(sourceCommentsEntry);
            if (!CanRestoreLegacyCommentPart(sourceCommentsXml, sheet, workbookNs))
                continue;

            XlsxLegacyCommentFontNormalizer.SanitizeRunFontNames(sourceCommentsXml);
            ReplacePackageXmlPart(targetArchive, sourceCommentsPath, sourceCommentsXml);

            var targetWorksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
            var targetWorksheetRelsXml = targetArchive.GetEntry(targetWorksheetRelsPath) is { } targetWorksheetRelsEntry
                ? XlsxPackageXmlEditor.LoadXml(targetWorksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            EnsureSingleRelationshipForPackagePart(
                targetWorksheetRelsXml,
                packageRelNs,
                targetWorksheetPath,
                sourceCommentsPath,
                CommentsRelationshipType,
                new HashSet<string>(StringComparer.Ordinal));

            var sourceLegacyDrawing = sourceWorksheetXml.Root?.Element(workbookNs + "legacyDrawing");
            var preservedVmlRelId = PreserveCommentVmlDrawing(
                sourceArchive,
                targetArchive,
                sourceWorksheetPath,
                targetWorksheetPath,
                sourceLegacyDrawing,
                packageRelNs,
                relNs,
                targetWorksheetRelsXml);

            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null)
                continue;

            targetRoot.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
            if (!string.IsNullOrWhiteSpace(preservedVmlRelId))
                SetSingleLegacyDrawingMarker(targetRoot, workbookNs, relNs, preservedVmlRelId);
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXml);

        }
    }

    private static string? GetLegacyCommentPartPath(
        ZipArchive archive,
        string worksheetPath,
        XNamespace packageRelNs)
    {
        var relsEntry = archive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(worksheetPath));
        if (relsEntry is null)
            return null;

        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
        var target = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .FirstOrDefault(relationship =>
                (relationship.Attribute("Type")?.Value ?? "").EndsWith("/comments", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("Target")
            ?.Value;
        return string.IsNullOrWhiteSpace(target)
            ? null
            : XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
    }

    private static bool CanRestoreLegacyCommentPart(
        XDocument sourceCommentsXml,
        Sheet sheet,
        XNamespace workbookNs)
    {
        var sourceComments = ReadLegacyCommentPlainTextByReference(sourceCommentsXml, workbookNs);
        return sourceComments.Count > 0 &&
               sourceComments.Count == sheet.Comments.Count &&
               sourceComments.All(pair =>
                   TryGetModeledCommentText(sheet, pair.Key, out var targetText) &&
                   string.Equals(pair.Value, targetText, StringComparison.Ordinal));
    }

    private static bool TryGetModeledCommentText(Sheet sheet, string reference, out string text)
    {
        text = "";
        if (!CellAddress.TryParse(reference, sheet.Id, out var address))
            return false;

        return sheet.Comments.TryGetValue(address, out text!);
    }

    private static string? PreserveCommentVmlDrawing(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string sourceWorksheetPath,
        string targetWorksheetPath,
        XElement? sourceLegacyDrawing,
        XNamespace packageRelNs,
        XNamespace relNs,
        XDocument targetWorksheetRelsXml)
    {
        var sourceVmlRelId = sourceLegacyDrawing?.Attribute(relNs + "id")?.Value;
        if (string.IsNullOrWhiteSpace(sourceVmlRelId) ||
            !TryGetInternalRelationshipTarget(
                sourceArchive,
                XlsxPackagePath.GetRelationshipPartPath(sourceWorksheetPath),
                sourceWorksheetPath,
                sourceVmlRelId,
                VmlDrawingRelationshipType,
                packageRelNs,
                out var sourceVmlPath))
        {
            return null;
        }

        var sourceVmlEntry = sourceArchive.GetEntry(sourceVmlPath);
        if (sourceVmlEntry is null)
            return null;

        ReplacePackagePart(targetArchive, sourceVmlEntry, sourceVmlPath);
        return EnsureSingleRelationshipForPackagePart(
            targetWorksheetRelsXml,
            packageRelNs,
            targetWorksheetPath,
            sourceVmlPath,
            VmlDrawingRelationshipType,
            GetHeaderFooterLegacyDrawingRelationshipIds(targetArchive, targetWorksheetPath, packageRelNs, relNs));
    }

    private static bool TryGetInternalRelationshipTarget(
        ZipArchive archive,
        string relationshipsPath,
        string sourcePartPath,
        string relationshipId,
        string relationshipType,
        XNamespace packageRelNs,
        out string targetPath)
    {
        targetPath = "";
        var relationshipsEntry = archive.GetEntry(relationshipsPath);
        if (relationshipsEntry is null)
            return false;

        var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
        var relationship = relationshipsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal) &&
                string.Equals(candidate.Attribute("Type")?.Value, relationshipType, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(candidate.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase));
        var target = relationship?.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
            return false;

        targetPath = XlsxPackagePath.ResolveRelationshipTarget(sourcePartPath, target);
        return !string.IsNullOrWhiteSpace(targetPath);
    }

    private static string EnsureSingleRelationshipForPackagePart(
        XDocument relsXml,
        XNamespace packageRelNs,
        string sourcePart,
        string targetPart,
        string relationshipType,
        IReadOnlySet<string> preservedRelationshipIds)
    {
        var root = relsXml.Root;
        if (root is null)
        {
            root = new XElement(packageRelNs + "Relationships");
            relsXml.Add(root);
        }

        string? activeId = null;
        foreach (var relationship in root.Elements(packageRelNs + "Relationship").ToList())
        {
            if (!string.Equals(relationship.Attribute("Type")?.Value, relationshipType, StringComparison.OrdinalIgnoreCase))
                continue;
            if (preservedRelationshipIds.Contains(relationship.Attribute("Id")?.Value ?? ""))
                continue;

            var target = relationship.Attribute("Target")?.Value;
            if (activeId is null &&
                !string.IsNullOrWhiteSpace(target) &&
                string.Equals(
                    XlsxPackagePath.ResolveRelationshipTarget(sourcePart, target),
                    targetPart,
                    StringComparison.OrdinalIgnoreCase))
            {
                activeId = relationship.Attribute("Id")?.Value;
                continue;
            }

            relationship.Remove();
        }

        if (!string.IsNullOrWhiteSpace(activeId))
            return activeId;

        return XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
            relsXml,
            packageRelNs,
            sourcePart,
            targetPart,
            relationshipType);
    }

    private static IReadOnlySet<string> GetHeaderFooterLegacyDrawingRelationshipIds(
        ZipArchive archive,
        string worksheetPath,
        XNamespace packageRelNs,
        XNamespace relNs)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            return new HashSet<string>(StringComparer.Ordinal);

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
        return worksheetXml.Root?
            .Elements(workbookNs + "legacyDrawingHF")
            .Select(element => element.Attribute(relNs + "id")?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal)
            ?? new HashSet<string>(StringComparer.Ordinal);
    }

    private static void SetSingleLegacyDrawingMarker(
        XElement worksheetRoot,
        XNamespace workbookNs,
        XNamespace relNs,
        string relationshipId)
    {
        var markerName = workbookNs + "legacyDrawing";
        var existingMarkers = worksheetRoot.Elements(markerName).ToList();
        var marker = existingMarkers.FirstOrDefault();
        if (marker is null)
        {
            marker = new XElement(markerName);
            InsertLegacyDrawingMarkerInWorksheetOrder(worksheetRoot, marker);
        }

        foreach (var extraMarker in existingMarkers.Skip(1))
            extraMarker.Remove();

        marker.RemoveAttributes();
        marker.RemoveNodes();
        marker.SetAttributeValue(relNs + "id", relationshipId);
    }

    private static void InsertLegacyDrawingMarkerInWorksheetOrder(XElement worksheetRoot, XElement marker)
    {
        var laterElementNames = new[] { "legacyDrawingHF", "picture", "oleObjects", "controls", "webPublishItems", "tableParts", "extLst" };
        var insertionPoint = worksheetRoot.Elements()
            .FirstOrDefault(element =>
                element.Name.Namespace == marker.Name.Namespace &&
                laterElementNames.Contains(element.Name.LocalName, StringComparer.Ordinal));
        if (insertionPoint is null)
            worksheetRoot.Add(marker);
        else
            insertionPoint.AddBeforeSelf(marker);
    }

    private static void ReplacePackageXmlPart(ZipArchive archive, string path, XDocument xml)
    {
        DeletePackagePartCaseInsensitive(archive, path);
        XlsxPackageXmlEditor.ReplaceXml(archive, path, xml);
    }

    private static void ReplacePackagePart(ZipArchive archive, ZipArchiveEntry sourceEntry, string targetPath)
    {
        DeletePackagePartCaseInsensitive(archive, targetPath);
        var targetEntry = archive.CreateEntry(targetPath, CompressionLevel.Optimal);
        targetEntry.LastWriteTime = sourceEntry.LastWriteTime;
        using var sourceStream = sourceEntry.Open();
        using var targetStream = targetEntry.Open();
        sourceStream.CopyTo(targetStream);
    }

    private static void DeletePackagePartCaseInsensitive(ZipArchive archive, string path)
    {
        foreach (var entry in archive.Entries
                     .Where(entry => string.Equals(entry.FullName, path, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            entry.Delete();
        }
    }

    private static Dictionary<string, string> ReadLegacyCommentPlainTextByReference(
        XDocument commentsXml,
        XNamespace workbookNs)
    {
        return commentsXml.Root?
            .Element(workbookNs + "commentList")?
            .Elements(workbookNs + "comment")
            .Where(comment => !string.IsNullOrWhiteSpace(comment.Attribute("ref")?.Value))
            .ToDictionary(
                comment => comment.Attribute("ref")!.Value,
                comment => string.Concat(comment.Element(workbookNs + "text")?.Descendants(workbookNs + "t").Select(text => text.Value) ?? []),
                StringComparer.OrdinalIgnoreCase) ?? [];
    }
}
