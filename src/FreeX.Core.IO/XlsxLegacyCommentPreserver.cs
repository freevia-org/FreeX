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

            // GAP 5: build a reconciled comments XML rather than the all-or-nothing guard.
            // When the note set is unchanged, the reconciled XML equals the source XML (same
            // author/rich-text preservation as before).  When notes were added or deleted, we
            // keep the source XML entries for every UNCHANGED note (preserving author and rich
            // text) and fall back to ClosedXML-generated entries for ADDED notes.
            var targetCommentsPath = GetLegacyCommentPartPath(targetArchive, targetWorksheetPath, packageRelNs);
            var reconciledCommentsXml = TryBuildReconciledCommentsXml(
                sourceCommentsXml,
                sheet,
                workbookNs,
                targetArchive,
                targetCommentsPath);
            if (reconciledCommentsXml is null)
                continue;

            // When the note count changed the VML (box geometry) is taken from ClosedXML's
            // output (it covers all current notes).  When the note set is identical we keep the
            // source VML so box geometry of unchanged notes is preserved.
            var noteCountChanged = sourceCommentsXml.Root?
                .Element(workbookNs + "commentList")?
                .Elements(workbookNs + "comment")
                .Count() != sheet.Comments.Count;

            XlsxLegacyCommentFontNormalizer.SanitizeRunFontNames(reconciledCommentsXml);
            ReplacePackageXmlPart(targetArchive, sourceCommentsPath, reconciledCommentsXml);

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

            // When the note count changed, leave ClosedXML's VML in place (it has a shape for
            // every current note, including the new ones).  Residual: box geometry of unchanged
            // notes is not restored in this case — that is deferred to Wave 3.
            var sourceLegacyDrawing = sourceWorksheetXml.Root?.Element(workbookNs + "legacyDrawing");
            var preservedVmlRelId = noteCountChanged
                ? null
                : PreserveCommentVmlDrawing(
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
        var target = FindCommentsRelationship(relsXml.Root, packageRelNs)?
            .Attribute("Target")?
            .Value;
        return string.IsNullOrWhiteSpace(target)
            ? null
            : XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
    }

    /// <summary>
    /// GAP 5 fix: builds a reconciled comments XML that preserves source XML entries for
    /// unchanged notes (keeping author and rich-text formatting) while removing deleted notes and
    /// copying new-note entries from the ClosedXML-generated target XML.
    /// Also applies author changes (GAP 2): if the model's <c>CommentAuthors</c> differs from
    /// the source XML's author, the <c>&lt;authors&gt;</c> list and <c>authorId</c> attribute
    /// are updated so the new author is written.
    /// Returns <c>null</c> if the source XML has no entries that match any modeled note (genuine
    /// no-match — fall through to ClosedXML output unchanged).
    /// </summary>
    private static XDocument? TryBuildReconciledCommentsXml(
        XDocument sourceCommentsXml,
        Sheet sheet,
        XNamespace workbookNs,
        ZipArchive targetArchive,
        string? targetCommentsPath)
    {
        var sourceCommentElements = ReadLegacyCommentElementsByReference(sourceCommentsXml, workbookNs);
        if (sourceCommentElements.Count == 0)
            return null;

        // Read the source authors list (index → name).
        var sourceAuthors = sourceCommentsXml.Root?
            .Element(workbookNs + "authors")?
            .Elements(workbookNs + "author")
            .Select(a => a.Value)
            .ToList() ?? [];

        // We will build a new authors list that covers all reconciled entries.
        // We start from source authors (to preserve existing authorIds) and add new ones as needed.
        var reconciledAuthors = new List<string>(sourceAuthors);

        // Classify each source comment as: matched, text-changed, or deleted.
        // Also track which model notes are NEW (not in source).
        var matchedCount = 0;
        var reconciledEntries = new List<XElement>(sheet.Comments.Count);
        var sourceRefsHandled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (sourceRef, sourceElement) in sourceCommentElements)
        {
            sourceRefsHandled.Add(sourceRef);
            if (!CellAddress.TryParse(sourceRef, sheet.Id, out var address))
                continue; // ref unparseable — drop it

            if (!sheet.Comments.TryGetValue(address, out var modelText))
                continue; // note was deleted — drop it

            var entryToAdd = new XElement(sourceElement); // deep-clone

            // Text reconciliation: update if text changed.
            if (!string.Equals(ReadCommentPlainText(entryToAdd, workbookNs), modelText, StringComparison.Ordinal))
                entryToAdd = UpdateCommentText(entryToAdd, modelText, workbookNs);

            // Author reconciliation (GAP 2): if the model's CommentAuthors has a different value
            // than the source XML author, update the authorId to point to the new author.
            var modelAuthor = sheet.CommentAuthors.TryGetValue(address, out var ma) ? ma : string.Empty;
            var sourceAuthorIdStr = entryToAdd.Attribute("authorId")?.Value;
            var sourceAuthorName = string.Empty;
            if (int.TryParse(sourceAuthorIdStr, out var sourceAuthorIdx) &&
                sourceAuthorIdx >= 0 && sourceAuthorIdx < sourceAuthors.Count)
            {
                sourceAuthorName = sourceAuthors[sourceAuthorIdx];
            }

            if (!string.Equals(modelAuthor, sourceAuthorName, StringComparison.Ordinal))
            {
                // Need to find or add the new author in the reconciled list.
                var newAuthorIdx = reconciledAuthors.FindIndex(a =>
                    string.Equals(a, modelAuthor, StringComparison.Ordinal));
                if (newAuthorIdx < 0)
                {
                    newAuthorIdx = reconciledAuthors.Count;
                    reconciledAuthors.Add(modelAuthor);
                }
                entryToAdd.SetAttributeValue("authorId", newAuthorIdx.ToString());
            }

            reconciledEntries.Add(entryToAdd);
            matchedCount++;
        }

        // Require at least one source entry to be usable.
        if (matchedCount == 0)
            return null;

        // For NEW notes (in model but not in source) try to copy from ClosedXML's target XML.
        var newModelAddresses = sheet.Comments.Keys
            .Where(addr => !sourceRefsHandled.Contains(addr.ToA1()))
            .ToList();

        if (newModelAddresses.Count > 0 && !string.IsNullOrEmpty(targetCommentsPath))
        {
            var targetCommentsEntry = targetArchive.GetEntry(targetCommentsPath);
            if (targetCommentsEntry is not null)
            {
                var targetCommentsXml = XlsxPackageXmlEditor.LoadXml(targetCommentsEntry);
                var targetAuthors = targetCommentsXml.Root?
                    .Element(workbookNs + "authors")?
                    .Elements(workbookNs + "author")
                    .Select(a => a.Value)
                    .ToList() ?? [];
                var targetElements = ReadLegacyCommentElementsByReference(targetCommentsXml, workbookNs);
                foreach (var addr in newModelAddresses)
                {
                    var cellRef = addr.ToA1();
                    if (!targetElements.TryGetValue(cellRef, out var targetElement))
                        continue;

                    // Re-map the target element's authorId into the reconciled authors list.
                    var clonedEntry = new XElement(targetElement);
                    var targetAuthorIdStr = clonedEntry.Attribute("authorId")?.Value;
                    if (int.TryParse(targetAuthorIdStr, out var targetAuthorIdx) &&
                        targetAuthorIdx >= 0 && targetAuthorIdx < targetAuthors.Count)
                    {
                        var targetAuthorName = targetAuthors[targetAuthorIdx];
                        var newIdx = reconciledAuthors.FindIndex(a =>
                            string.Equals(a, targetAuthorName, StringComparison.Ordinal));
                        if (newIdx < 0)
                        {
                            newIdx = reconciledAuthors.Count;
                            reconciledAuthors.Add(targetAuthorName);
                        }
                        clonedEntry.SetAttributeValue("authorId", newIdx.ToString());
                    }

                    reconciledEntries.Add(clonedEntry);
                }
            }
        }

        // Build the reconciled document from the source document's structure.
        var result = new XDocument(sourceCommentsXml); // deep-clone preserves namespace declarations
        var resultRoot = result.Root!;

        // Rebuild the <authors> list.
        var authorsElement = resultRoot.Element(workbookNs + "authors");
        if (authorsElement is null)
        {
            authorsElement = new XElement(workbookNs + "authors");
            resultRoot.AddFirst(authorsElement);
        }
        authorsElement.RemoveNodes();
        foreach (var authorName in reconciledAuthors)
            authorsElement.Add(new XElement(workbookNs + "author", authorName));

        // Rebuild the <commentList>.
        var resultList = resultRoot.Element(workbookNs + "commentList");
        if (resultList is null)
        {
            resultList = new XElement(workbookNs + "commentList");
            resultRoot.Add(resultList);
        }

        resultList.RemoveNodes();
        foreach (var entry in reconciledEntries)
            resultList.Add(entry); // already deep-cloned above

        return result;
    }

    private static XElement UpdateCommentText(XElement commentElement, string newText, XNamespace workbookNs)
    {
        // Simplest safe approach: replace the entire <text> element with a single plain-text run.
        // This loses rich-text formatting for this specific note, but preserves author and keeps
        // the entry (so deletion detection still works).
        var cloned = new XElement(commentElement);
        var textElement = cloned.Element(workbookNs + "text");
        if (textElement is not null)
        {
            textElement.RemoveNodes();
            textElement.Add(new XElement(workbookNs + "r",
                new XElement(workbookNs + "t", newText)));
        }
        return cloned;
    }

    private static string ReadCommentPlainText(XElement commentElement, XNamespace workbookNs) =>
        string.Concat(commentElement.Element(workbookNs + "text")?
            .Descendants(workbookNs + "t")
            .Select(t => t.Value) ?? []);

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
        var relationship = FindInternalRelationship(relationshipsXml.Root, packageRelNs, relationshipId, relationshipType);
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
        var marker = FirstLegacyDrawingMarker(existingMarkers);
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
        var insertionPoint = FindLegacyDrawingInsertionPoint(worksheetRoot, marker.Name.Namespace, laterElementNames);
        if (insertionPoint is null)
            worksheetRoot.Add(marker);
        else
            insertionPoint.AddBeforeSelf(marker);
    }

    private static XElement? FindCommentsRelationship(XElement? relationshipsRoot, XNamespace packageRelNs)
    {
        if (relationshipsRoot is null)
            return null;

        foreach (var relationship in relationshipsRoot.Elements(packageRelNs + "Relationship"))
        {
            if ((relationship.Attribute("Type")?.Value ?? "").EndsWith("/comments", StringComparison.OrdinalIgnoreCase))
                return relationship;
        }

        return null;
    }

    private static XElement? FindInternalRelationship(
        XElement? relationshipsRoot,
        XNamespace packageRelNs,
        string relationshipId,
        string relationshipType)
    {
        if (relationshipsRoot is null)
            return null;

        foreach (var candidate in relationshipsRoot.Elements(packageRelNs + "Relationship"))
        {
            if (string.Equals(candidate.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal) &&
                string.Equals(candidate.Attribute("Type")?.Value, relationshipType, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(candidate.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static XElement? FirstLegacyDrawingMarker(IReadOnlyList<XElement> markers) =>
        markers.Count == 0 ? null : markers[0];

    private static XElement? FindLegacyDrawingInsertionPoint(
        XElement worksheetRoot,
        XNamespace worksheetNs,
        IReadOnlyCollection<string> laterElementNames)
    {
        foreach (var element in worksheetRoot.Elements())
        {
            if (element.Name.Namespace == worksheetNs &&
                ContainsElementName(laterElementNames, element.Name.LocalName))
            {
                return element;
            }
        }

        return null;
    }

    private static bool ContainsElementName(IReadOnlyCollection<string> elementNames, string elementName)
    {
        foreach (var candidate in elementNames)
        {
            if (string.Equals(candidate, elementName, StringComparison.Ordinal))
                return true;
        }

        return false;
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

    private static Dictionary<string, XElement> ReadLegacyCommentElementsByReference(
        XDocument commentsXml,
        XNamespace workbookNs)
    {
        return commentsXml.Root?
            .Element(workbookNs + "commentList")?
            .Elements(workbookNs + "comment")
            .Where(comment => !string.IsNullOrWhiteSpace(comment.Attribute("ref")?.Value))
            .ToDictionary(
                comment => comment.Attribute("ref")!.Value,
                comment => comment,
                StringComparer.OrdinalIgnoreCase) ?? [];
    }
}
