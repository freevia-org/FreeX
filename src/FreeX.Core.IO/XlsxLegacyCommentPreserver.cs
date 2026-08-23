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

    // VML namespaces used in note shapes
    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace ExcelVmlNs = "urn:schemas-microsoft-com:office:excel";
    private static readonly XNamespace OfficeNs = "urn:schemas-microsoft-com:office:office";

    public static void Preserve(
        Workbook workbook,
        XlsxSourcePackagePreservationContext? context)
    {
        if (context is null)
            return;

        var sourceArchive = context.SourceArchive;
        var targetArchive = context.TargetArchive;
        var workbookNs = context.WorkbookNs;
        var relNs = context.RelNs;
        var packageRelNs = context.PackageRelNs;
        var sourceSheets = context.SourceSheets;
        var targetSheets = context.TargetSheets;

        foreach (var (sheetName, sourceWorksheetPath) in sourceSheets)
        {
            if (!targetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue;

            var sheet = workbook.GetSheet(sheetName);
            if (sheet is null)
                continue;

            var sourceWorksheetEntry = sourceArchive.GetEntry(sourceWorksheetPath);
            var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
            if (sourceWorksheetEntry is null || targetWorksheetEntry is null)
                continue;

            var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceWorksheetPath)!;
            var sourceCommentsPath = GetLegacyCommentPartPath(sourceArchive, sourceWorksheetPath, packageRelNs);
            if (sourceCommentsPath is null)
                continue;

            var sourceCommentsEntry = sourceArchive.GetEntry(sourceCommentsPath);
            if (sourceCommentsEntry is null)
                continue;

            var sourceCommentsXml = XlsxPackageXmlEditor.LoadXml(sourceCommentsEntry);

            if (sheet.Comments.Count == 0)
            {
                // R94-io-comments-threaded-shim-note-free-1: addresses of threads that were
                // authored fresh THIS save (no entry at all -- shim or otherwise -- in the source
                // comments part). XlsxFileAdapter.Save's ClosedXML-population phase already minted
                // each one's FIRST legacy shim via CreateComment() (see the
                // R93-threaded-comment-extLst comment there); since this sheet has
                // Sheet.Comments.Count == 0, ClosedXML's rebuild writes a comments part containing
                // ONLY those fresh shims -- nothing for any OTHER, pre-existing thread that is
                // untouched this save -- so without merging them back in below, a sheet with both
                // an old thread and a new one would lose the old thread's shim even though nothing
                // about it changed (CopyUnknownPackageParts skips re-copying the stale-but-still-
                // needed source comments part once ClosedXML has written its own entry at that
                // same path).
                var newlyAuthoredThreadAddresses = GetNewlyAuthoredThreadShimAddresses(sourceCommentsXml, workbookNs, sheet);

                // GAP 6 fix: every REAL legacy note on this sheet was deleted (the source comments
                // part has at least one non-shim entry, but Sheet.Comments -- populated from it at
                // load time by XlsxWorksheetCommentReader -- is now empty). ClosedXML writes
                // nothing at the old comments/VML paths for a note-free sheet, but
                // XlsxPackageMetadataMerger's CopyUnknownPackageParts/MergeRelationshipParts
                // already ran (unconditionally, before this preserver) and copied the stale source
                // comments.xml -- with the deleted comment text -- back into the target package
                // with a live relationship, so the deletion never actually took effect on disk.
                // Skip sheets whose comments part contains ONLY Excel's legacy threaded-comment
                // compatibility shims (or blank-text entries) -- those are never loaded into
                // Sheet.Comments even when nothing was deleted (see
                // XlsxWorksheetCommentReader.IsLegacyThreadedCommentShim), so an empty model here
                // is normal and the shim must be left in place for older/non-Excel readers.
                var hasOnlyUnmodeledSourceEntries = SourceCommentsHaveOnlyUnmodeledEntries(
                    sourceCommentsXml, workbookNs, sheet, sourceArchive, sourceWorksheetPath);
                if (!hasOnlyUnmodeledSourceEntries || newlyAuthoredThreadAddresses.Count > 0)
                {
                    // R68-io-comment-note-6-1: at least one entry in this comments part is a real,
                    // deleted note -- but the part may ALSO hold a live-threaded-comment shim (see
                    // IsLegacyThreadedCommentShimEntry) whose thread is untouched. An all-or-nothing
                    // purge here would destroy that shim's legacy compatibility entry too, even
                    // though nothing about its own thread changed. Rebuild the part keeping only the
                    // shim(s) that still need preserving (plus any brand-new thread's shim from
                    // newlyAuthoredThreadAddresses); only fall back to the full purge when none
                    // exist to protect and nothing new was authored either.
                    var targetCommentsPathForNewThreads = GetLegacyCommentPartPath(targetArchive, targetWorksheetPath, packageRelNs);
                    var shimsOnlyCommentsXml = TryBuildShimsOnlyCommentsXml(
                        sourceCommentsXml, workbookNs, sheet, sourceArchive, sourceWorksheetPath,
                        targetArchive, targetCommentsPathForNewThreads, newlyAuthoredThreadAddresses,
                        out var keptShimAddresses, out var mergedNewThreadAddresses);
                    if (shimsOnlyCommentsXml is not null)
                    {
                        XlsxLegacyCommentFontNormalizer.SanitizeRunFontNames(shimsOnlyCommentsXml);
                        ReplacePackageXmlPart(targetArchive, sourceCommentsPath, shimsOnlyCommentsXml);

                        // The VML note-shape count must stay consistent with the reconciled
                        // comments part (a leftover shape for the deleted note, with no matching
                        // <comment> entry any more, makes the package unreadable by ClosedXML on
                        // the next load) -- rebuild the VML to keep only the shims' own shapes
                        // (plus ClosedXML's freshly-generated shape for each new thread).
                        ReconcileShimsOnlyVmlDrawing(
                            sourceArchive,
                            targetArchive,
                            sourceWorksheetPath,
                            targetWorksheetPath,
                            sourceWorksheetXml,
                            workbookNs,
                            relNs,
                            packageRelNs,
                            keptShimAddresses,
                            mergedNewThreadAddresses);
                    }
                    else if (!hasOnlyUnmodeledSourceEntries)
                    {
                        PurgeDeletedLegacyComments(
                            sourceArchive,
                            targetArchive,
                            sourceWorksheetPath,
                            targetWorksheetPath,
                            sourceCommentsPath,
                            sourceWorksheetXml,
                            workbookNs,
                            relNs,
                            packageRelNs);
                    }
                }

                continue;
            }

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
                targetCommentsPath,
                sourceArchive,
                sourceWorksheetPath);
            if (reconciledCommentsXml is null)
                continue;

            XlsxLegacyCommentFontNormalizer.SanitizeRunFontNames(reconciledCommentsXml);
            ReplacePackageXmlPart(targetArchive, sourceCommentsPath, reconciledCommentsXml);

            var (targetWorksheetRelsPath, targetWorksheetRelsXml) =
                context.LoadOrCreateTargetRelationships(targetWorksheetPath);
            EnsureSingleRelationshipForPackagePart(
                targetWorksheetRelsXml,
                packageRelNs,
                targetWorksheetPath,
                sourceCommentsPath,
                CommentsRelationshipType,
                new HashSet<string>(StringComparer.Ordinal));

            // GAP 4: reconcile VML drawing so unchanged notes keep source geometry + Visible
            // even when notes were added or deleted. New notes get ClosedXML's default shape.
            var sourceLegacyDrawing = sourceWorksheetXml.Root?.Element(workbookNs + "legacyDrawing");
            var preservedVmlRelId = PreserveReconciledVmlDrawing(
                sourceArchive,
                targetArchive,
                sourceWorksheetPath,
                targetWorksheetPath,
                sourceLegacyDrawing,
                packageRelNs,
                relNs,
                targetWorksheetRelsXml,
                sheet,
                sourceCommentsXml,
                workbookNs,
                ReadSourceThreadedCommentsByCell(sourceArchive, sourceWorksheetPath));

            context.ReplaceTargetPartXml(targetWorksheetRelsPath, targetWorksheetRelsXml);

            var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
            var targetRoot = targetWorksheetXml.Root;
            if (targetRoot is null)
                continue;

            targetRoot.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
            if (!string.IsNullOrWhiteSpace(preservedVmlRelId))
                SetSingleLegacyDrawingMarker(targetRoot, workbookNs, relNs, preservedVmlRelId);
            context.ReplaceTargetPartXml(targetWorksheetPath, targetWorksheetXml);

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
        string? targetCommentsPath,
        ZipArchive sourceArchive,
        string sourceWorksheetPath)
    {
        var sourceCommentElements = ReadLegacyCommentElementsByReference(sourceCommentsXml, workbookNs);
        if (sourceCommentElements.Count == 0)
            return null;

        // R74-io-comments-threaded-4-1: shift-aware lookup for the live-threaded-comment shim
        // check below -- a row/column insert/delete may have relocated a shim's thread to a NEW
        // address since the source package was written.
        var sourceThreadsByCell = ReadSourceThreadedCommentsByCell(sourceArchive, sourceWorksheetPath);

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
        var consumedAddresses = new HashSet<CellAddress>();

        // R32-io-hyperlink-comment-deep-3: source entries whose address is no longer modeled
        // are not necessarily deleted -- a row/column insert/delete may simply have shifted the
        // note to a new address (RowColumnShiftHelpers.ShiftCommentRows*/Columns* already
        // relocated the model's Sheet.Comments key). Queue them for a shift-aware, text-matching
        // fallback pass below instead of dropping them immediately, so rich-text formatting and
        // custom authors survive the shift (mirrors PreserveReconciledVmlDrawing's shift-aware
        // fallback pass for the VML side).
        var unmatchedSourceEntries = new List<(XElement Element, string PlainText, CellAddress OldAddress)>();

        foreach (var (sourceRef, sourceElement) in sourceCommentElements)
        {
            sourceRefsHandled.Add(sourceRef);
            if (!CellAddress.TryParse(sourceRef, sheet.Id, out var address))
                continue; // ref unparseable — drop it

            if (!sheet.Comments.TryGetValue(address, out var modelText))
            {
                // R32-io-hyperlink-comment-deep-1: Excel's legacy threaded-comment compatibility
                // shim is by design never modeled into Sheet.Comments even when its thread is
                // still alive (XlsxWorksheetCommentReader.IsLegacyThreadedCommentShim) -- keep it
                // untouched rather than treating it as a deleted note. R74-io-comments-threaded-
                // 4-1: shift-aware -- also keep it (re-anchored to the new address) when the
                // thread survived a row/column insert/delete instead of staying at this exact
                // (now stale) address.
                if (IsLegacyThreadedCommentShimEntry(sourceElement, workbookNs, sourceAuthors) &&
                    TryResolveShiftedThreadedCommentAddress(sourceThreadsByCell, address, sheet, out var shimAddress))
                {
                    var shimEntry = new XElement(sourceElement);
                    if (shimAddress != address)
                        shimEntry.SetAttributeValue("ref", shimAddress.ToA1());
                    reconciledEntries.Add(shimEntry);
                    matchedCount++;
                    continue;
                }

                unmatchedSourceEntries.Add((sourceElement, ReadCommentPlainText(sourceElement, workbookNs), address));
                continue; // note was deleted (or shifted — resolved in the fallback pass below)
            }

            consumedAddresses.Add(address);
            reconciledEntries.Add(ReconcileCommentEntry(
                sourceElement, modelText, address, sheet, sourceAuthors, reconciledAuthors, workbookNs));
            matchedCount++;
        }

        // Shift-aware fallback pass: match a still-unmatched source entry to a model note at a
        // currently-unconsumed address by comparing plain text (a stable key that survives an
        // address shift as long as the note's text itself was not also edited in the same save).
        //
        // R33-meta-2: when TWO OR MORE unmatched source entries share IDENTICAL text, matching by
        // text alone is ambiguous — picking by dictionary/list enumeration order can pair a source
        // entry to the WRONG shifted candidate, swapping rich-text/author between same-text notes.
        // Disambiguate by original relative position: a row/column insert or delete is a monotonic
        // shift, so notes that shared the same text also keep their relative (row, col) order after
        // the shift. Within each same-text group, sort the source entries by their OLD address and
        // the candidate addresses by their (new) address, then pair index-for-index — this preserves
        // each note's own identity instead of relying on enumeration order.
        if (unmatchedSourceEntries.Count > 0)
        {
            var candidateAddresses = sheet.Comments.Keys
                .Where(addr => !consumedAddresses.Contains(addr))
                .ToList();
            var claimedCandidateIndexes = new HashSet<int>();

            foreach (var group in unmatchedSourceEntries.GroupBy(e => e.PlainText, StringComparer.Ordinal))
            {
                var plainText = group.Key;
                var sourceGroupEntries = group.OrderBy(e => e.OldAddress).ToList();
                var candidateIndexesForText = candidateAddresses
                    .Select((addr, idx) => (addr, idx))
                    .Where(pair => !claimedCandidateIndexes.Contains(pair.idx) &&
                        string.Equals(sheet.Comments[pair.addr], plainText, StringComparison.Ordinal))
                    .OrderBy(pair => pair.addr)
                    .ToList();

                var pairCount = Math.Min(sourceGroupEntries.Count, candidateIndexesForText.Count);
                for (var i = 0; i < pairCount; i++)
                {
                    var (candidateAddress, candidateIndex) = candidateIndexesForText[i];
                    claimedCandidateIndexes.Add(candidateIndex);
                    consumedAddresses.Add(candidateAddress);
                    sourceRefsHandled.Add(candidateAddress.ToA1());

                    var shiftedEntry = new XElement(sourceGroupEntries[i].Element);
                    shiftedEntry.SetAttributeValue("ref", candidateAddress.ToA1());
                    reconciledEntries.Add(ReconcileCommentEntry(
                        shiftedEntry, plainText, candidateAddress, sheet, sourceAuthors, reconciledAuthors, workbookNs));
                    matchedCount++;
                }
            }
        }

        // Require at least one source entry to be usable.
        if (matchedCount == 0)
            return null;

        // For NEW notes (in model but not in source) try to copy from ClosedXML's target XML.
        // R93-threaded-comment-extLst: also covers a brand-new threaded comment (no shim entry in
        // the source at all) added to a sheet that ALSO has an existing legacy note requiring
        // reconciliation -- XlsxFileAdapter.Save's early ClosedXML population phase writes that
        // thread's legacy compatibility shim into the freshly-generated target comments part (see
        // the ThreadedComments loop there), but without including its address here, this
        // reconciliation pass would silently drop that freshly-written shim on the floor (it only
        // ever rebuilds commentList from source entries + this "new address" copy step).
        var newModelAddresses = sheet.Comments.Keys
            .Concat(sheet.ThreadedComments.Keys)
            .Distinct()
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

    /// <summary>
    /// Reconciles a single (deep-cloned) source <c>&lt;comment&gt;</c> element against its current
    /// model text and author: rewrites the text run(s) when the model text differs (losing
    /// rich-text formatting only for THAT change) and remaps <c>authorId</c> into
    /// <paramref name="reconciledAuthors"/> when the model's author differs from the source's.
    /// Shared by the direct-match path and the shift-aware fallback in
    /// <see cref="TryBuildReconciledCommentsXml"/> so both preserve rich text/author identically.
    /// </summary>
    private static XElement ReconcileCommentEntry(
        XElement sourceElement,
        string modelText,
        CellAddress address,
        Sheet sheet,
        List<string> sourceAuthors,
        List<string> reconciledAuthors,
        XNamespace workbookNs)
    {
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

        return entryToAdd;
    }

    /// <summary>
    /// True when this legacy <c>&lt;comment&gt;</c> element is Excel's backward-compat mirror of a
    /// threaded comment rather than a genuine, independently-authored Note: the legacy author is
    /// literally "tc={GUID}", or the text starts with the fixed "[Threaded comment]" compatibility
    /// banner. Mirrors <c>XlsxWorksheetCommentReader.IsLegacyThreadedCommentShim</c>.
    /// </summary>
    private static bool IsLegacyThreadedCommentShimEntry(
        XElement commentElement,
        XNamespace workbookNs,
        IReadOnlyList<string> authors)
    {
        var text = ReadCommentPlainText(commentElement, workbookNs);
        var author = "";
        if (int.TryParse(commentElement.Attribute("authorId")?.Value, out var authorIdx) &&
            authorIdx >= 0 && authorIdx < authors.Count)
        {
            author = authors[authorIdx];
        }

        return author.StartsWith("tc=", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("[Threaded comment]", StringComparison.Ordinal);
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
        ExtractCommentPlainText(commentElement.Element(workbookNs + "text"), workbookNs);

    /// <summary>
    /// Extracts a comment's visible plain text from its <c>&lt;text&gt;</c> (CT_Rst) element,
    /// concatenating direct <c>&lt;t&gt;</c> text and <c>&lt;r&gt;/&lt;t&gt;</c> run text but
    /// excluding any <c>&lt;rPh&gt;/&lt;t&gt;</c> phonetic-guide (furigana/pinyin reading-hint)
    /// text, which CT_Rst allows alongside the visible runs but which real Excel never displays
    /// as part of the comment's text.
    /// </summary>
    /// <remarks>
    /// R37-io-comments-legacy-vml-2-3: a plain <c>Descendants("t")</c> walks the entire subtree
    /// and would also pick up the <c>&lt;t&gt;</c> nested inside <c>&lt;rPh&gt;</c>, corrupting
    /// the modeled/compared text for any Japanese/Chinese-authored comment that carries a
    /// phonetic guide.
    /// </remarks>
    private static string ExtractCommentPlainText(XElement? textElement, XNamespace workbookNs) =>
        textElement is null
            ? ""
            : string.Concat(textElement
                .Descendants(workbookNs + "t")
                .Where(t => t.Parent?.Name != workbookNs + "rPh")
                .Select(t => t.Value));

    private static bool TryGetModeledCommentText(Sheet sheet, string reference, out string text)
    {
        text = "";
        if (!CellAddress.TryParse(reference, sheet.Id, out var address))
            return false;

        return sheet.Comments.TryGetValue(address, out text!);
    }

    /// <summary>
    /// R74-io-comments-threaded-4-1: reads every threaded-comment thread as it existed in the
    /// ORIGINAL source package, indexed by its (1-based) <c>(Row, Col)</c> cell -- used by
    /// <see cref="TryResolveShiftedThreadedCommentAddress"/> to find the stable
    /// <see cref="ThreadedComment.Id"/> a legacy compatibility shim's thread had BEFORE a
    /// row/column insert/delete may have shifted it. A no-op read (returns empty) when the
    /// worksheet has no threaded-comments part at all.
    /// </summary>
    private static Dictionary<(uint Row, uint Col), ThreadedComment> ReadSourceThreadedCommentsByCell(
        ZipArchive sourceArchive,
        string sourceWorksheetPath) =>
        XlsxWorksheetThreadedCommentMapper.Read(sourceArchive, sourceWorksheetPath)
            .ToDictionary(t => (t.Row, t.Col), t => t.Comment);

    /// <summary>
    /// R74-io-comments-threaded-4-1: resolves the address a legacy threaded-comment compatibility
    /// shim's thread NOW lives at. <paramref name="oldAddress"/> is the shim's own <c>ref</c>
    /// address (parsed straight from the source comments part, i.e. the address it had when the
    /// source package was written). When <see cref="Sheet.ThreadedComments"/> still has the thread
    /// at that SAME address, this is a same-address match (the common case). Otherwise -- a
    /// row/column insert/delete may have shifted it, since RowColumnShiftHelpers.ShiftCommentRows*/
    /// Columns* already relocated <see cref="Sheet.ThreadedComments"/>' key but this shim's own
    /// <c>ref</c> still names the OLD address -- match by the thread's stable
    /// <see cref="ThreadedComment.Id"/> instead (captured unconditionally from the source's own
    /// id/personId on load, so it survives the shift and, unlike matching by text, can never
    /// collide with a distinct thread that merely happens to share the same text). Returns
    /// <c>false</c> only when no thread with that id survives anywhere on the sheet -- i.e. the
    /// whole thread was genuinely deleted.
    /// </summary>
    private static bool TryResolveShiftedThreadedCommentAddress(
        IReadOnlyDictionary<(uint Row, uint Col), ThreadedComment> sourceThreadsByCell,
        CellAddress oldAddress,
        Sheet sheet,
        out CellAddress resolvedAddress)
    {
        sourceThreadsByCell.TryGetValue((oldAddress.Row, oldAddress.Col), out var sourceThread);

        // Same-address fast path -- but only when the thread now sitting at oldAddress is actually
        // the shim's OWN thread (matched by stable Id against the source). A row/col delete can
        // remove the shim's own thread while shifting an UNRELATED thread onto oldAddress; matching
        // by address alone would silently reattach the shim to that unrelated thread instead of
        // falling through to the Id-based search (which correctly purges it). When the source has no
        // recorded thread at this address at all, there is nothing to cross-check against, so keep
        // the address-only match (this mirrors the previous, pre-check behavior for that case).
        if (sheet.ThreadedComments.TryGetValue(oldAddress, out var currentThreadAtOldAddress) &&
            (string.IsNullOrEmpty(sourceThread?.Id) ||
             string.Equals(currentThreadAtOldAddress.Id, sourceThread!.Id, StringComparison.Ordinal)))
        {
            resolvedAddress = oldAddress;
            return true;
        }

        if (string.IsNullOrEmpty(sourceThread?.Id))
        {
            resolvedAddress = default;
            return false;
        }

        foreach (var (address, comment) in sheet.ThreadedComments)
        {
            if (string.Equals(comment.Id, sourceThread.Id, StringComparison.Ordinal))
            {
                resolvedAddress = address;
                return true;
            }
        }

        resolvedAddress = default;
        return false;
    }

    /// <summary>
    /// True when EVERY entry in the source comments part is one that
    /// <c>XlsxWorksheetCommentReader</c> would never surface into <see cref="Sheet.Comments"/> in
    /// the first place: Excel's legacy threaded-comment compatibility shim (mirrors
    /// <c>XlsxWorksheetCommentReader.IsLegacyThreadedCommentShim</c>) or a blank-text entry (which
    /// that same reader also skips). An unmodified sheet whose comments part looks like this
    /// legitimately round-trips with <c>Sheet.Comments.Count == 0</c> and nothing deleted, so GAP 6's
    /// purge in <see cref="Preserve"/> must not fire for it. Returns <c>false</c> (i.e. "go ahead
    /// and purge") when the part has no entries at all, since there is nothing there to protect.
    /// </summary>
    /// <remarks>
    /// R34-io-comments-threaded-mentions-1: a threaded-comment compatibility shim is only safe to
    /// leave untouched forever while its paired thread is still alive. This checks each shim entry's
    /// cell against <paramref name="sourceArchive"/>'s OWN <c>threadedComments</c> part (via
    /// <see cref="XlsxWorksheetThreadedCommentMapper.Read"/>) for the ORIGINAL, pre-edit set of
    /// threads: when that shows a live thread once existed at the shim's cell, but
    /// <see cref="Sheet.ThreadedComments"/> no longer has it (the user deleted the whole thread
    /// before saving), the shim is now stale and must be purged too -- mirroring the live-thread
    /// check the Comments.Count &gt; 0 reconciliation path already applies (~line 249). A shim with
    /// no paired thread in the source at all (e.g. a legacy-only file that never had one) is left
    /// alone exactly as before, since there is nothing there that could have been "deleted".
    /// </remarks>
    private static bool SourceCommentsHaveOnlyUnmodeledEntries(
        XDocument sourceCommentsXml,
        XNamespace workbookNs,
        Sheet sheet,
        ZipArchive sourceArchive,
        string sourceWorksheetPath)
    {
        var commentElements = sourceCommentsXml.Root?
            .Element(workbookNs + "commentList")?
            .Elements(workbookNs + "comment")
            .ToList() ?? [];
        if (commentElements.Count == 0)
            return false;

        var authors = sourceCommentsXml.Root?
            .Element(workbookNs + "authors")?
            .Elements(workbookNs + "author")
            .Select(a => a.Value)
            .ToList() ?? [];

        Dictionary<(uint Row, uint Col), ThreadedComment>? sourceThreadsByCell = null;

        foreach (var comment in commentElements)
        {
            var text = ReadCommentPlainText(comment, workbookNs);
            var author = "";
            if (int.TryParse(comment.Attribute("authorId")?.Value, out var authorIdx) &&
                authorIdx >= 0 && authorIdx < authors.Count)
            {
                author = authors[authorIdx];
            }

            if (!IsUnmodeledLegacyCommentEntry(author, text))
                return false;

            var isShim = author.StartsWith("tc=", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("[Threaded comment]", StringComparison.Ordinal);
            if (!isShim)
                continue;

            var reference = comment.Attribute("ref")?.Value;
            if (string.IsNullOrEmpty(reference) || !CellAddress.TryParse(reference, sheet.Id, out var address))
                continue;

            sourceThreadsByCell ??= ReadSourceThreadedCommentsByCell(sourceArchive, sourceWorksheetPath);

            // R74-io-comments-threaded-4-1: deliberately NOT shift-aware here -- this is only the
            // gate that decides whether the shims-only reconciliation pass below (Preserve's
            // TryBuildShimsOnlyCommentsXml, which IS shift-aware) needs to run at all. Returning
            // "true" (only-unmodeled, no purge) whenever the thread is simply not at its own
            // (source-file) address any more -- whether it was shifted OR genuinely deleted --
            // would skip that pass entirely and leave a shifted shim's `ref` stale at its pre-shift
            // address, since Preserve() only calls it when this method returns false.
            var sourceThreadOnceExisted = sourceThreadsByCell.ContainsKey((address.Row, address.Col));
            if (sourceThreadOnceExisted && !sheet.ThreadedComments.ContainsKey(address))
                return false;
        }

        return true;
    }

    /// <summary>
    /// R94-io-comments-threaded-shim-note-free-1: identifies threads on this sheet that have NO
    /// entry at all -- shim or otherwise -- in the source comments part: a brand-new threaded
    /// comment added this save (its <see cref="ThreadedComment.Id"/> was <c>null</c> before the
    /// save) never had a legacy compatibility shim to preserve or reconcile in the first place.
    /// These are exactly the addresses XlsxFileAdapter.Save's ClosedXML-population phase minted a
    /// FIRST shim for via <c>xlSheet.Cell(...).CreateComment()</c> (see the
    /// R93-threaded-comment-extLst comment there); the <see cref="Sheet.Comments"/>-empty branch
    /// of <see cref="Preserve"/> uses this to know which freshly-written target shim entries must
    /// be merged back in alongside any pre-existing, untouched thread's own shim (see
    /// <see cref="TryBuildShimsOnlyCommentsXml"/>).
    /// </summary>
    private static List<CellAddress> GetNewlyAuthoredThreadShimAddresses(
        XDocument sourceCommentsXml,
        XNamespace workbookNs,
        Sheet sheet)
    {
        if (sheet.ThreadedComments.Count == 0)
            return [];

        var sourceRefs = new HashSet<string>(
            ReadLegacyCommentElementsByReference(sourceCommentsXml, workbookNs).Keys,
            StringComparer.OrdinalIgnoreCase);

        return sheet.ThreadedComments.Keys
            .Where(address => !sourceRefs.Contains(address.ToA1()))
            .ToList();
    }

    /// <summary>
    /// R68-io-comment-note-6-1: when every REAL legacy note on a sheet has been deleted (so
    /// <see cref="Sheet.Comments"/> is empty and <see cref="SourceCommentsHaveOnlyUnmodeledEntries"/>
    /// returned <c>false</c>), builds a comments XML that keeps ONLY the source entries that are a
    /// live-threaded-comment compatibility shim (<see cref="IsLegacyThreadedCommentShimEntry"/> and
    /// its thread is still present in <see cref="Sheet.ThreadedComments"/>) -- mirroring the same
    /// shim-preservation rule the <c>Sheet.Comments.Count &gt; 0</c> reconciliation path already
    /// applies (see <see cref="TryBuildReconciledCommentsXml"/> ~line 249). Every other entry
    /// (a deleted note, a dead shim, or a blank-text entry) is dropped.
    /// R94-io-comments-threaded-shim-note-free-1: ALSO merges in a legacy compatibility shim for
    /// every address in <paramref name="newlyAuthoredThreadAddresses"/> -- a brand-new threaded
    /// comment (no source entry at all) that XlsxFileAdapter.Save's ClosedXML-population phase
    /// minted its FIRST shim for via <c>xlSheet.Cell(...).CreateComment()</c> (see the
    /// R93-threaded-comment-extLst comment there), copied from the freshly-generated TARGET
    /// comments part at <paramref name="targetCommentsPath"/> the same way
    /// <see cref="TryBuildReconciledCommentsXml"/>'s "for NEW notes" step does. Without this,
    /// because this sheet has <c>Sheet.Comments.Count == 0</c>, ClosedXML's rebuild writes a
    /// comments part containing ONLY that fresh shim (nothing for any OTHER, untouched thread),
    /// and <c>XlsxPackageMetadataMerger.CopyUnknownPackageParts</c> then skips re-copying the
    /// stale-but-still-needed source comments part back in (the target already has an entry at
    /// that same path) -- silently dropping a pre-existing, untouched thread's own shim even
    /// though nothing about it changed. Returns <c>null</c> when no entry (old or new) needs
    /// preserving, signalling the caller to fall back to the all-or-nothing purge.
    /// </summary>
    private static XDocument? TryBuildShimsOnlyCommentsXml(
        XDocument sourceCommentsXml,
        XNamespace workbookNs,
        Sheet sheet,
        ZipArchive sourceArchive,
        string sourceWorksheetPath,
        ZipArchive targetArchive,
        string? targetCommentsPath,
        IReadOnlyList<CellAddress> newlyAuthoredThreadAddresses,
        out List<(CellAddress OldAddress, CellAddress NewAddress)> keptShimAddresses,
        out List<CellAddress> mergedNewThreadAddresses)
    {
        var sourceAuthors = sourceCommentsXml.Root?
            .Element(workbookNs + "authors")?
            .Elements(workbookNs + "author")
            .Select(a => a.Value)
            .ToList() ?? [];
        var sourceThreadsByCell = ReadSourceThreadedCommentsByCell(sourceArchive, sourceWorksheetPath);

        var keptEntries = new List<XElement>();
        var keptAuthors = new List<string>();
        keptShimAddresses = [];
        mergedNewThreadAddresses = [];

        foreach (var (sourceRef, sourceElement) in ReadLegacyCommentElementsByReference(sourceCommentsXml, workbookNs))
        {
            if (!CellAddress.TryParse(sourceRef, sheet.Id, out var address))
                continue;

            // R74-io-comments-threaded-4-1: shift-aware -- keep (and re-anchor) the shim when its
            // thread survives at a NEW address after a row/column insert/delete, not only when it
            // is still exactly at its own OLD `ref` address.
            if (!IsLegacyThreadedCommentShimEntry(sourceElement, workbookNs, sourceAuthors) ||
                !TryResolveShiftedThreadedCommentAddress(sourceThreadsByCell, address, sheet, out var shimAddress))
            {
                continue; // deleted note, dead shim, or unmodeled blank entry -- drop it
            }

            var entryToAdd = new XElement(sourceElement); // deep-clone
            if (shimAddress != address)
                entryToAdd.SetAttributeValue("ref", shimAddress.ToA1());

            var sourceAuthorName = "";
            if (int.TryParse(entryToAdd.Attribute("authorId")?.Value, out var sourceAuthorIdx) &&
                sourceAuthorIdx >= 0 && sourceAuthorIdx < sourceAuthors.Count)
            {
                sourceAuthorName = sourceAuthors[sourceAuthorIdx];
            }

            var newAuthorIdx = keptAuthors.FindIndex(a => string.Equals(a, sourceAuthorName, StringComparison.Ordinal));
            if (newAuthorIdx < 0)
            {
                newAuthorIdx = keptAuthors.Count;
                keptAuthors.Add(sourceAuthorName);
            }
            entryToAdd.SetAttributeValue("authorId", newAuthorIdx.ToString());

            keptEntries.Add(entryToAdd);
            keptShimAddresses.Add((address, shimAddress));
        }

        if (newlyAuthoredThreadAddresses.Count > 0 && !string.IsNullOrEmpty(targetCommentsPath) &&
            targetArchive.GetEntry(targetCommentsPath) is { } targetCommentsEntry)
        {
            var targetCommentsXml = XlsxPackageXmlEditor.LoadXml(targetCommentsEntry);
            var targetAuthors = targetCommentsXml.Root?
                .Element(workbookNs + "authors")?
                .Elements(workbookNs + "author")
                .Select(a => a.Value)
                .ToList() ?? [];
            var targetElements = ReadLegacyCommentElementsByReference(targetCommentsXml, workbookNs);

            foreach (var address in newlyAuthoredThreadAddresses)
            {
                if (!targetElements.TryGetValue(address.ToA1(), out var targetElement))
                    continue;

                var clonedEntry = new XElement(targetElement); // deep-clone
                var targetAuthorIdStr = clonedEntry.Attribute("authorId")?.Value;
                if (int.TryParse(targetAuthorIdStr, out var targetAuthorIdx) &&
                    targetAuthorIdx >= 0 && targetAuthorIdx < targetAuthors.Count)
                {
                    var targetAuthorName = targetAuthors[targetAuthorIdx];
                    var newIdx = keptAuthors.FindIndex(a =>
                        string.Equals(a, targetAuthorName, StringComparison.Ordinal));
                    if (newIdx < 0)
                    {
                        newIdx = keptAuthors.Count;
                        keptAuthors.Add(targetAuthorName);
                    }
                    clonedEntry.SetAttributeValue("authorId", newIdx.ToString());
                }

                keptEntries.Add(clonedEntry);
                mergedNewThreadAddresses.Add(address);
            }
        }

        if (keptEntries.Count == 0)
            return null;

        var result = new XDocument(sourceCommentsXml); // deep-clone preserves namespace declarations
        var resultRoot = result.Root!;

        var authorsElement = resultRoot.Element(workbookNs + "authors");
        if (authorsElement is null)
        {
            authorsElement = new XElement(workbookNs + "authors");
            resultRoot.AddFirst(authorsElement);
        }
        authorsElement.RemoveNodes();
        foreach (var authorName in keptAuthors)
            authorsElement.Add(new XElement(workbookNs + "author", authorName));

        var resultList = resultRoot.Element(workbookNs + "commentList");
        if (resultList is null)
        {
            resultList = new XElement(workbookNs + "commentList");
            resultRoot.Add(resultList);
        }
        resultList.RemoveNodes();
        foreach (var entry in keptEntries)
            resultList.Add(entry); // already deep-cloned above

        return result;
    }

    /// <summary>
    /// R68-io-comment-note-6-1: rebuilds the source VML drawing to keep only the note shapes for
    /// <paramref name="keptShimAddresses"/> (the live-threaded-comment shims
    /// <see cref="TryBuildShimsOnlyCommentsXml"/> preserved in the comments part), dropping the
    /// deleted note's shape. Without this, the VML would still carry a shape with no matching
    /// <c>&lt;comment&gt;</c> entry any more, which ClosedXML fails to parse on the next load.
    /// A no-op when there is no source VML to reconcile (nothing to fix).
    /// </summary>
    private static void ReconcileShimsOnlyVmlDrawing(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string sourceWorksheetPath,
        string targetWorksheetPath,
        XDocument sourceWorksheetXml,
        XNamespace workbookNs,
        XNamespace relNs,
        XNamespace packageRelNs,
        IReadOnlyList<(CellAddress OldAddress, CellAddress NewAddress)> keptShimAddresses,
        IReadOnlyList<CellAddress> newlyAuthoredThreadAddresses)
    {
        var sourceLegacyDrawing = sourceWorksheetXml.Root?.Element(workbookNs + "legacyDrawing");
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
            return; // no source VML to reconcile
        }

        var sourceVmlEntry = sourceArchive.GetEntry(sourceVmlPath);
        if (sourceVmlEntry is null)
            return;

        XDocument sourceVml;
        try
        {
            sourceVml = OpcXml.LoadXml(sourceVmlEntry);
        }
        catch
        {
            return; // unparseable -- leave whatever is already in the target archive alone
        }

        var shapesByCell = IndexNoteShapesByCell(sourceVml);
        var keptShapes = new List<XElement>();
        foreach (var (oldAddress, newAddress) in keptShimAddresses)
        {
            // The VML shape itself was never shifted -- it is still anchored at the shim's OLD
            // cell in the source VML -- so look it up there, then retarget the clone to the NEW
            // (possibly shifted) cell so it renders at the thread's current location.
            var oldKey = (Row: oldAddress.Row - 1, Col: oldAddress.Col - 1);
            if (!shapesByCell.TryGetValue(oldKey, out var shape))
                continue;

            var clonedShape = new XElement(shape); // deep-clone; preserves geometry
            if (newAddress != oldAddress)
                RetargetNoteShapeToCell(clonedShape, newAddress.Row - 1, newAddress.Col - 1);
            keptShapes.Add(clonedShape);
        }

        // R94-io-comments-threaded-shim-note-free-1: a brand-new thread's shim (see
        // GetNewlyAuthoredThreadShimAddresses/TryBuildShimsOnlyCommentsXml) has NO shape in the
        // SOURCE VML at all -- ClosedXML generated its shape fresh, into a target-archive VML part
        // (found by scanning every VML entry other than the source path, mirroring
        // PreserveReconciledVmlDrawing's Pass 3 fallback for genuinely new notes). Without this,
        // the comments-part entry TryBuildShimsOnlyCommentsXml merged in above would have no
        // matching VML shape at all once this method rebuilds the VML from source-only shapes.
        if (newlyAuthoredThreadAddresses.Count > 0)
        {
            var targetShapesByCell = IndexAllTargetNoteShapes(targetArchive, sourceVmlPath);
            foreach (var address in newlyAuthoredThreadAddresses)
            {
                var key = (Row: address.Row - 1, Col: address.Col - 1);
                if (targetShapesByCell.TryGetValue(key, out var newShape))
                    keptShapes.Add(new XElement(newShape)); // deep-clone
            }
        }

        var reconciledVml = BuildReconciledVml(sourceVml, keptShapes);
        ReplacePackageXmlPart(targetArchive, sourceVmlPath, reconciledVml);

        var targetWorksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
        var targetWorksheetRelsXml = targetArchive.GetEntry(targetWorksheetRelsPath) is { } targetWorksheetRelsEntry
            ? XlsxPackageXmlEditor.LoadXml(targetWorksheetRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        var vmlRelId = EnsureSingleRelationshipForPackagePart(
            targetWorksheetRelsXml,
            packageRelNs,
            targetWorksheetPath,
            sourceVmlPath,
            VmlDrawingRelationshipType,
            GetHeaderFooterLegacyDrawingRelationshipIds(targetArchive, targetWorksheetPath, packageRelNs, relNs));
        XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);

        // The target worksheet's own <legacyDrawing> marker must actually reference the VML
        // relationship, or a later orphan-part cleanup pass would treat it as unused and strip
        // the relationship/part right back out again (mirrors the equivalent step in the
        // Sheet.Comments.Count > 0 reconciliation path below).
        if (!string.IsNullOrWhiteSpace(vmlRelId) &&
            targetArchive.GetEntry(targetWorksheetPath) is { } targetWorksheetEntryForVml)
        {
            var targetWorksheetXmlForVml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntryForVml);
            var targetRootForVml = targetWorksheetXmlForVml.Root;
            if (targetRootForVml is not null)
            {
                targetRootForVml.SetAttributeValue(XNamespace.Xmlns + "r", relNs.NamespaceName);
                SetSingleLegacyDrawingMarker(targetRootForVml, workbookNs, relNs, vmlRelId);
                XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetPath, targetWorksheetXmlForVml);
            }
        }
    }

    /// <summary>
    /// Mirrors <c>XlsxWorksheetCommentReader</c>'s "never load this into the model" rules: a
    /// blank-text entry, or Excel's legacy threaded-comment compatibility shim (author literally
    /// "tc={GUID}", or text starting with the fixed "[Threaded comment]" banner).
    /// </summary>
    private static bool IsUnmodeledLegacyCommentEntry(string author, string text) =>
        text.Length == 0 ||
        author.StartsWith("tc=", StringComparison.OrdinalIgnoreCase) ||
        text.StartsWith("[Threaded comment]", StringComparison.Ordinal);

    /// <summary>
    /// GAP 6 fix: when every legacy note on a sheet was deleted, ClosedXML writes nothing at the
    /// comments/VML paths for it -- but <c>XlsxPackageMetadataMerger.CopyUnknownPackageParts</c> and
    /// <c>MergeRelationshipParts</c> run unconditionally BEFORE this preserver and already
    /// resurrected the stale source comments part (plus, when nothing else still needs it, its VML
    /// note shapes) into the target package with a live relationship. This removes that resurrected
    /// part and relationship so the deletion actually sticks. The VML part is only removed when the
    /// target worksheet has no <c>&lt;legacyDrawing&gt;</c> marker at all -- a legacy form control on
    /// the same sheet shares that same marker/part, in which case it is left untouched (any
    /// header/footer VML, which uses a distinct &lt;legacyDrawingHF&gt; marker/relationship, is
    /// always excluded via <see cref="GetHeaderFooterLegacyDrawingRelationshipIds"/>).
    /// </summary>
    private static void PurgeDeletedLegacyComments(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string sourceWorksheetPath,
        string targetWorksheetPath,
        string sourceCommentsPath,
        XDocument sourceWorksheetXml,
        XNamespace workbookNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var hasChange = targetArchive.GetEntry(sourceCommentsPath) is not null;
        if (hasChange)
            DeletePackagePartCaseInsensitive(targetArchive, sourceCommentsPath);

        var targetWorksheetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
        var targetWorksheetRelsEntry = targetArchive.GetEntry(targetWorksheetRelsPath);
        if (targetWorksheetRelsEntry is null)
            return;

        var targetWorksheetRelsXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetRelsEntry);
        var relsRoot = targetWorksheetRelsXml.Root;
        if (relsRoot is null)
            return;

        foreach (var relationship in relsRoot.Elements(packageRelNs + "Relationship").ToList())
        {
            if (!string.Equals(relationship.Attribute("Type")?.Value, CommentsRelationshipType, StringComparison.OrdinalIgnoreCase))
                continue;

            var target = relationship.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target) ||
                !string.Equals(
                    XlsxPackagePath.ResolveRelationshipTarget(targetWorksheetPath, target),
                    sourceCommentsPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            relationship.Remove();
            hasChange = true;
        }

        // Only remove the VML note-shape part when nothing else on the sheet still needs it.
        var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
        var targetHasLegacyDrawingMarker = targetWorksheetEntry is not null &&
            XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry).Root?.Element(workbookNs + "legacyDrawing") is not null;

        if (!targetHasLegacyDrawingMarker)
        {
            var sourceVmlRelId = sourceWorksheetXml.Root?
                .Element(workbookNs + "legacyDrawing")?
                .Attribute(relNs + "id")?
                .Value;
            if (!string.IsNullOrWhiteSpace(sourceVmlRelId) &&
                TryGetInternalRelationshipTarget(
                    sourceArchive,
                    XlsxPackagePath.GetRelationshipPartPath(sourceWorksheetPath),
                    sourceWorksheetPath,
                    sourceVmlRelId,
                    VmlDrawingRelationshipType,
                    packageRelNs,
                    out var sourceVmlPath))
            {
                var headerFooterRelationshipIds = GetHeaderFooterLegacyDrawingRelationshipIds(
                    targetArchive, targetWorksheetPath, packageRelNs, relNs);

                foreach (var relationship in relsRoot.Elements(packageRelNs + "Relationship").ToList())
                {
                    if (!string.Equals(relationship.Attribute("Type")?.Value, VmlDrawingRelationshipType, StringComparison.OrdinalIgnoreCase) ||
                        headerFooterRelationshipIds.Contains(relationship.Attribute("Id")?.Value ?? ""))
                    {
                        continue;
                    }

                    var target = relationship.Attribute("Target")?.Value;
                    if (string.IsNullOrWhiteSpace(target) ||
                        !string.Equals(
                            XlsxPackagePath.ResolveRelationshipTarget(targetWorksheetPath, target),
                            sourceVmlPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    relationship.Remove();
                    hasChange = true;
                }

                if (targetArchive.GetEntry(sourceVmlPath) is not null)
                {
                    DeletePackagePartCaseInsensitive(targetArchive, sourceVmlPath);
                    hasChange = true;
                }
            }
        }

        if (hasChange)
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, targetWorksheetRelsPath, targetWorksheetRelsXml);
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

    /// <summary>
    /// GAP 4 fix: builds a reconciled VML drawing that preserves the source <c>&lt;v:shape&gt;</c>
    /// (including style geometry and <c>&lt;x:Visible/&gt;</c>) for every unchanged note, drops
    /// shapes for deleted notes, and takes ClosedXML's generated shape for new notes.
    /// Matching is primarily by <c>&lt;x:Row&gt;</c>/<c>&lt;x:Column&gt;</c> (0-based) in
    /// ClientData; when a note's cell address changed since the source package was written
    /// (row/column insert, delete, sort, or move — RowColumnShiftHelpers.ShiftCommentRows*/
    /// Columns* already relocated the model's <see cref="Sheet.Comments"/> key, but the on-disk
    /// VML shape is still anchored to the OLD cell), the shape is instead matched to its new
    /// cell by comment text — a stable key that survives the address shift — and its ClientData
    /// Row/Column/Anchor are retargeted to the new cell.
    /// Returns the relationship id of the VML part wired into the target worksheet rels, or
    /// <c>null</c> if no source VML exists (leave ClosedXML's output untouched).
    /// </summary>
    private static string? PreserveReconciledVmlDrawing(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string sourceWorksheetPath,
        string targetWorksheetPath,
        XElement? sourceLegacyDrawing,
        XNamespace packageRelNs,
        XNamespace relNs,
        XDocument targetWorksheetRelsXml,
        Sheet sheet,
        XDocument sourceCommentsXml,
        XNamespace workbookNs,
        IReadOnlyDictionary<(uint Row, uint Col), ThreadedComment> sourceThreadsByCell)
    {
        // Resolve source VML path.
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
            // No source VML — leave ClosedXML's VML untouched, just wire the relationship.
            return WireTargetVmlRelationship(
                targetArchive, targetWorksheetPath, targetWorksheetRelsXml, packageRelNs, relNs);
        }

        var sourceVmlEntry = sourceArchive.GetEntry(sourceVmlPath);
        if (sourceVmlEntry is null)
        {
            return WireTargetVmlRelationship(
                targetArchive, targetWorksheetPath, targetWorksheetRelsXml, packageRelNs, relNs);
        }

        // Load source VML as text (VML is not well-formed XML in the XLINQ sense due to namespace
        // prefix aliases, but in practice Excel writes it as parseable XML). We use XDocument with
        // explicit namespace resolver. If it fails to parse, fall back to verbatim source copy.
        XDocument sourceVml;
        try
        {
            sourceVml = OpcXml.LoadXml(sourceVmlEntry);
        }
        catch
        {
            // Unparseable VML — fall back to verbatim source copy (old behavior).
            ReplacePackagePart(targetArchive, sourceVmlEntry, sourceVmlPath);
            return EnsureSingleRelationshipForPackagePart(
                targetWorksheetRelsXml, packageRelNs, targetWorksheetPath, sourceVmlPath,
                VmlDrawingRelationshipType,
                GetHeaderFooterLegacyDrawingRelationshipIds(targetArchive, targetWorksheetPath, packageRelNs, relNs));
        }

        // Index source note shapes by 0-based (row, col).
        var sourceShapesByCell = IndexNoteShapesByCell(sourceVml);

        // Index the pristine source comments' text by their OLD 0-based (row, col), so a note
        // whose cell moved since the source package was written can still be matched to its
        // source shape (see the shift-aware fallback pass below).
        var sourceTextByOldCell = IndexSourceCommentTextByCell(sourceCommentsXml, sheet, workbookNs);

        // Find ClosedXML's generated VML for new notes by scanning ALL VML entries in the target
        // archive. We cannot rely on the target worksheet rels here because XlsxWorksheetVml
        // ReferencePreserver may have already updated them to point to the source VML copy.
        // Instead, collect every distinct VML part from the target archive and merge the shapes
        // from any that are NOT the source path (i.e. ClosedXML's generated file).
        var targetShapesByCell = IndexAllTargetNoteShapes(targetArchive, sourceVmlPath);

        // Build reconciled shape list: for each current note address, prefer the source shape at
        // the SAME cell; if none (the note's address shifted), try to find the source shape at
        // its OLD cell by matching comment text and retarget it to the new cell; fall back to the
        // target shape for genuinely new notes.
        //
        // Pass 1: direct (row, col) match against the unshifted source index.
        var reconciledShapes = new List<XElement>(sheet.Comments.Count);
        var consumedSourceCells = new HashSet<(uint Row, uint Col)>();
        var unmatched = new List<(CellAddress Address, string ModelText, (uint Row, uint Col) Key)>();
        foreach (var (address, modelText) in sheet.Comments)
        {
            // CellAddress rows/cols are 1-based; VML ClientData uses 0-based.
            var key = (Row: address.Row - 1, Col: address.Col - 1);
            // R32-io-hyperlink-comment-deep-2: a direct (row,col) match against the UNSHIFTED
            // source index is only valid when the shape actually belongs to THIS comment. When a
            // row/col insert shifts two adjacent notes, one note's NEW cell can coincide with a
            // sibling note's OLD cell — verify via sourceTextByOldCell (and honor
            // consumedSourceCells) before accepting it, else fall through to the shift-aware match.
            if (!consumedSourceCells.Contains(key) &&
                sourceShapesByCell.TryGetValue(key, out var sourceShape) &&
                (!sourceTextByOldCell.TryGetValue(key, out var sourceTextAtKey) ||
                 string.Equals(sourceTextAtKey, modelText, StringComparison.Ordinal)))
            {
                var shape = new XElement(sourceShape); // deep-clone; preserves geometry
                consumedSourceCells.Add(key);
                ApplyVisibleFlag(shape, sheet.ShownComments.Contains(address));
                reconciledShapes.Add(shape);
            }
            else
            {
                unmatched.Add((address, modelText, key));
            }
        }

        // Pass 2: shift-aware fallback, grouped by text so identical-text notes are disambiguated
        // by relative address order instead of by dictionary/enumeration order (mirrors the
        // R33-meta-2 fix in TryBuildReconciledCommentsXml above — see its comment for rationale).
        // Within each same-text group, sort the current (new) addresses ascending and the
        // candidate OLD addresses ascending, then pair index-for-index so each note keeps its own
        // geometry across the shift rather than risking a swap with a same-text sibling.
        var stillUnmatched = new List<(CellAddress Address, (uint Row, uint Col) Key)>();
        foreach (var group in unmatched.GroupBy(u => u.ModelText, StringComparer.Ordinal))
        {
            var modelText = group.Key;
            var newEntries = group.OrderBy(u => u.Address).ToList();
            var candidateKeys = sourceTextByOldCell
                .Where(pair => !consumedSourceCells.Contains(pair.Key) &&
                    string.Equals(pair.Value, modelText, StringComparison.Ordinal) &&
                    sourceShapesByCell.ContainsKey(pair.Key))
                .Select(pair => pair.Key)
                .OrderBy(k => k)
                .ToList();

            var pairCount = Math.Min(newEntries.Count, candidateKeys.Count);
            for (var i = 0; i < pairCount; i++)
            {
                var (address, _, key) = newEntries[i];
                var oldKey = candidateKeys[i];
                var shape = new XElement(sourceShapesByCell[oldKey]); // deep-clone; preserves geometry across the shift
                RetargetNoteShapeToCell(shape, key.Row, key.Col);
                consumedSourceCells.Add(oldKey);
                ApplyVisibleFlag(shape, sheet.ShownComments.Contains(address));
                reconciledShapes.Add(shape);
            }

            for (var i = pairCount; i < newEntries.Count; i++)
                stillUnmatched.Add((newEntries[i].Address, newEntries[i].Key));
        }

        // Pass 3: genuinely new notes — fall back to ClosedXML's generated target shape.
        foreach (var (address, key) in stillUnmatched)
        {
            if (!targetShapesByCell.TryGetValue(key, out var targetShape))
            {
                // If neither source nor target has a shape for this address, skip (ClosedXML may not
                // have generated one yet — the package will still be valid, just missing a box).
                continue;
            }

            var shape = new XElement(targetShape); // deep-clone; new note default geometry
            ApplyVisibleFlag(shape, sheet.ShownComments.Contains(address));
            reconciledShapes.Add(shape);
        }

        // R32-io-hyperlink-comment-deep-1: keep the VML shape for Excel's legacy
        // threaded-comment compatibility shim when its thread is still alive, mirroring the
        // equivalent fix in TryBuildReconciledCommentsXml -- the shim's cell is never a key in
        // sheet.Comments, so the loop above never visits it and BuildReconciledVml would
        // otherwise silently drop its shape (it strips every existing Note shape from the
        // source and only re-adds shapes for sheet.Comments entries).
        var sourceCommentAuthorsForShim = sourceCommentsXml.Root?
            .Element(workbookNs + "authors")?
            .Elements(workbookNs + "author")
            .Select(a => a.Value)
            .ToList() ?? [];
        foreach (var (sourceRef, sourceCommentElement) in ReadLegacyCommentElementsByReference(sourceCommentsXml, workbookNs))
        {
            // R74-io-comments-threaded-4-1: shift-aware -- the shim's thread may have moved to a
            // NEW address since the source package was written (a row/column insert/delete); its
            // VML shape is still anchored at the OLD cell in the source VML (looked up below via
            // oldShimKey) but must be retargeted to the NEW cell when the two differ.
            if (!CellAddress.TryParse(sourceRef, sheet.Id, out var oldShimAddress) ||
                !IsLegacyThreadedCommentShimEntry(sourceCommentElement, workbookNs, sourceCommentAuthorsForShim) ||
                !TryResolveShiftedThreadedCommentAddress(sourceThreadsByCell, oldShimAddress, sheet, out var shimAddress))
            {
                continue;
            }

            var oldShimKey = (Row: oldShimAddress.Row - 1, Col: oldShimAddress.Col - 1);
            if (consumedSourceCells.Contains(oldShimKey) || !sourceShapesByCell.TryGetValue(oldShimKey, out var shimShape))
                continue;

            consumedSourceCells.Add(oldShimKey);
            var clonedShimShape = new XElement(shimShape); // deep-clone; geometry preserved, retargeted below if shifted
            if (shimAddress != oldShimAddress)
                RetargetNoteShapeToCell(clonedShimShape, shimAddress.Row - 1, shimAddress.Col - 1);
            reconciledShapes.Add(clonedShimShape); // no Visible-flag rewrite, matching prior behavior
        }

        // Build the reconciled VML document: keep source header boilerplate (shapelayout,
        // shapetype) and replace note shapes with the reconciled set.
        var reconciledVml = BuildReconciledVml(sourceVml, reconciledShapes);

        // Write reconciled VML to the target package at the source path.
        ReplacePackageXmlPart(targetArchive, sourceVmlPath, reconciledVml);
        return EnsureSingleRelationshipForPackagePart(
            targetWorksheetRelsXml, packageRelNs, targetWorksheetPath, sourceVmlPath,
            VmlDrawingRelationshipType,
            GetHeaderFooterLegacyDrawingRelationshipIds(targetArchive, targetWorksheetPath, packageRelNs, relNs));
    }

    /// <summary>
    /// Sets or clears the <c>&lt;x:Visible/&gt;</c> element within the first
    /// <c>ObjectType="Note"</c> ClientData child of the given VML shape, AND keeps the shape's
    /// CSS <c>visibility</c> style property in sync with it.
    /// </summary>
    /// <remarks>
    /// R37-io-comments-legacy-vml-2-1: real Excel always writes both the ClientData
    /// <c>&lt;x:Visible/&gt;</c> flag AND a matching <c>style="...;visibility:visible|hidden"</c>
    /// CSS property on the shape, and Excel (and any other VML-conformant renderer) treats the CSS
    /// property as the box's actual paint state. Previously only the ClientData flag was toggled,
    /// so a pin/unpin round trip left the shape's real on-screen visibility unchanged in Excel even
    /// though FreeX's own reader (which only looks at ClientData) believed the toggle worked.
    /// </remarks>
    private static void ApplyVisibleFlag(XElement shape, bool isPinned)
    {
        var clientData = shape.Elements(ExcelVmlNs + "ClientData")
            .FirstOrDefault(cd => string.Equals(
                cd.Attribute("ObjectType")?.Value, "Note",
                StringComparison.OrdinalIgnoreCase));
        if (clientData is null)
            return;

        var visibleElement = clientData.Element(ExcelVmlNs + "Visible");
        if (isPinned && visibleElement is null)
            clientData.Add(new XElement(ExcelVmlNs + "Visible"));
        else if (!isPinned && visibleElement is not null)
            visibleElement.Remove();

        XlsxVmlStylePolicy.SetVisibility(shape, isPinned);
    }

    /// <summary>
    /// Rewrites (or appends) the <c>visibility:</c> CSS property inside the VML shape's
    /// <c>style</c> attribute so it matches <paramref name="isPinned"/> — <c>visible</c> when
    /// pinned, <c>hidden</c> otherwise — without disturbing any other CSS properties already
    /// present (position, margins, size, z-index, etc).
    /// </summary>
    /// <summary>
    /// Indexes VML note shapes by their 0-based (row, col) ClientData anchor.
    /// Only shapes with <c>ObjectType="Note"</c> ClientData are indexed.
    /// </summary>
    private static Dictionary<(uint Row, uint Col), XElement> IndexNoteShapesByCell(XDocument vml)
    {
        var result = new Dictionary<(uint Row, uint Col), XElement>();
        if (vml.Root is null)
            return result;

        foreach (var shape in vml.Root.Elements(VmlNs + "shape"))
        {
            var clientData = shape.Elements(ExcelVmlNs + "ClientData")
                .FirstOrDefault(cd => string.Equals(
                    cd.Attribute("ObjectType")?.Value, "Note",
                    StringComparison.OrdinalIgnoreCase));
            if (clientData is null)
                continue;

            var rowText = clientData.Element(ExcelVmlNs + "Row")?.Value;
            var colText = clientData.Element(ExcelVmlNs + "Column")?.Value;
            if (!uint.TryParse(rowText, out var row0) || !uint.TryParse(colText, out var col0))
                continue;

            result[(row0, col0)] = shape;
        }

        return result;
    }

    /// <summary>
    /// Indexes the pristine source comments XML's plain text by the comment's OLD 0-based
    /// (row, col), using the same <c>&lt;t&gt;</c>-concatenation text extraction as
    /// <see cref="XlsxWorksheetCommentReader"/> uses when loading notes into the model, so an
    /// unchanged note's text compares equal regardless of which side it was read from.
    /// </summary>
    private static Dictionary<(uint Row, uint Col), string> IndexSourceCommentTextByCell(
        XDocument sourceCommentsXml,
        Sheet sheet,
        XNamespace workbookNs)
    {
        var result = new Dictionary<(uint Row, uint Col), string>();
        foreach (var (reference, text) in ReadLegacyCommentPlainTextByReference(sourceCommentsXml, workbookNs))
        {
            if (!CellAddress.TryParse(reference, sheet.Id, out var address))
                continue;

            result[(address.Row - 1, address.Col - 1)] = text;
        }

        return result;
    }

    /// <summary>
    /// Retargets a note shape reused from its OLD cell (via the shift-aware fallback pass in
    /// <see cref="PreserveReconciledVmlDrawing"/>)
    /// to its NEW 0-based (row, col): updates ClientData <c>&lt;x:Row&gt;</c>/<c>&lt;x:Column&gt;</c>
    /// (which <see cref="XlsxWorksheetCommentVisibilityReader"/> reads back as the authoritative
    /// cell a pinned note belongs to) and shifts the <c>&lt;x:Anchor&gt;</c> cell-offset pair by the
    /// same row/column delta so the box still renders at its new cell rather than the stale old one.
    /// </summary>
    private static void RetargetNoteShapeToCell(XElement shape, uint newRow0, uint newCol0)
    {
        var clientData = shape.Elements(ExcelVmlNs + "ClientData")
            .FirstOrDefault(cd => string.Equals(
                cd.Attribute("ObjectType")?.Value, "Note",
                StringComparison.OrdinalIgnoreCase));
        if (clientData is null)
            return;

        var rowElement = clientData.Element(ExcelVmlNs + "Row");
        var colElement = clientData.Element(ExcelVmlNs + "Column");
        if (rowElement is null || colElement is null ||
            !uint.TryParse(rowElement.Value, out var oldRow0) ||
            !uint.TryParse(colElement.Value, out var oldCol0))
        {
            return;
        }

        rowElement.Value = newRow0.ToString();
        colElement.Value = newCol0.ToString();

        // <x:Anchor> is "col1, colOffset1, row1, rowOffset1, col2, colOffset2, row2, rowOffset2" —
        // shift the two row/col pairs by the same delta the cell itself moved by, preserving the
        // box's relative offsets (and therefore its custom size/position).
        var anchorElement = clientData.Element(ExcelVmlNs + "Anchor");
        var anchorParts = anchorElement?.Value.Split(',').Select(part => part.Trim()).ToArray();
        if (anchorParts is not { Length: 8 } ||
            !int.TryParse(anchorParts[0], out var col1) || !int.TryParse(anchorParts[2], out var row1) ||
            !int.TryParse(anchorParts[4], out var col2) || !int.TryParse(anchorParts[6], out var row2))
        {
            return;
        }

        var rowDelta = (int)newRow0 - (int)oldRow0;
        var colDelta = (int)newCol0 - (int)oldCol0;
        anchorParts[0] = (col1 + colDelta).ToString();
        anchorParts[2] = (row1 + rowDelta).ToString();
        anchorParts[4] = (col2 + colDelta).ToString();
        anchorParts[6] = (row2 + rowDelta).ToString();
        anchorElement!.Value = string.Join(", ", anchorParts);
    }

    /// <summary>
    /// Scans ALL VML entries in the target archive (except <paramref name="excludeVmlPath"/>, which
    /// is the source VML already indexed separately) and merges the note shapes found into a single
    /// lookup. This finds ClosedXML's generated VML regardless of what path it was written to,
    /// without relying on the worksheet relationship that may have already been updated to point
    /// to the source VML copy.
    /// </summary>
    private static Dictionary<(uint Row, uint Col), XElement> IndexAllTargetNoteShapes(
        ZipArchive targetArchive,
        string excludeVmlPath)
    {
        var result = new Dictionary<(uint Row, uint Col), XElement>();
        foreach (var entry in targetArchive.Entries)
        {
            var fullName = XlsxPackagePath.NormalizeEntryPath(entry);
            if (!fullName.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase) ||
                !fullName.EndsWith(".vml", StringComparison.OrdinalIgnoreCase))
                continue;

            // Skip the source VML we already indexed.
            if (string.Equals(fullName, excludeVmlPath, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                XDocument vml;
                vml = OpcXml.LoadXml(entry);

                foreach (var (key, shape) in IndexNoteShapesByCell(vml))
                {
                    // Do not overwrite a shape already found in an earlier VML entry.
                    if (!result.ContainsKey(key))
                        result[key] = shape;
                }
            }
            catch
            {
                // Unparseable VML — skip.
            }
        }

        return result;
    }

    /// <summary>
    /// Builds a reconciled VML document: copies the source header elements (everything that is
    /// not a note <c>&lt;v:shape&gt;</c>: the <c>&lt;o:shapelayout&gt;</c> and
    /// <c>&lt;v:shapetype&gt;</c>) then appends the reconciled note shapes.
    /// </summary>
    private static XDocument BuildReconciledVml(XDocument sourceVml, IReadOnlyList<XElement> reconciledShapes)
    {
        // Deep-clone the source document to preserve namespace declarations on the root.
        var result = new XDocument(sourceVml);
        var root = result.Root!;

        // Remove all existing note shapes (keep non-shape boilerplate: shapelayout, shapetype).
        foreach (var shape in root.Elements(VmlNs + "shape").ToList())
        {
            var hasNoteClientData = shape.Elements(ExcelVmlNs + "ClientData")
                .Any(cd => string.Equals(
                    cd.Attribute("ObjectType")?.Value, "Note",
                    StringComparison.OrdinalIgnoreCase));
            if (hasNoteClientData)
                shape.Remove();
        }

        // Append the reconciled note shapes (already deep-cloned by the caller).
        foreach (var shape in reconciledShapes)
            root.Add(shape);

        return result;
    }

    /// <summary>
    /// Resolves the VML drawing path already present in the target worksheet's relationships,
    /// returning it if found, or null if the target has no VML part.
    /// </summary>
    private static string? GetTargetVmlPath(
        ZipArchive targetArchive,
        string targetWorksheetPath,
        XNamespace packageRelNs,
        XNamespace relNs)
    {
        var relsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
        var relsEntry = targetArchive.GetEntry(relsPath);
        if (relsEntry is null)
            return null;

        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
        var vmlRel = relsXml.Root?.Elements(packageRelNs + "Relationship")
            .FirstOrDefault(r => string.Equals(
                r.Attribute("Type")?.Value, VmlDrawingRelationshipType,
                StringComparison.OrdinalIgnoreCase));
        var target = vmlRel?.Attribute("Target")?.Value;
        return string.IsNullOrWhiteSpace(target)
            ? null
            : XlsxPackagePath.ResolveRelationshipTarget(targetWorksheetPath, target);
    }

    /// <summary>
    /// Wires the target worksheet's existing VML relationship into <paramref name="targetWorksheetRelsXml"/>
    /// (ensuring a single relationship entry) and returns the relationship id.
    /// Used when there is no source VML — we simply ensure ClosedXML's VML stays wired correctly.
    /// </summary>
    private static string? WireTargetVmlRelationship(
        ZipArchive targetArchive,
        string targetWorksheetPath,
        XDocument targetWorksheetRelsXml,
        XNamespace packageRelNs,
        XNamespace relNs)
    {
        var targetVmlPath = GetTargetVmlPath(targetArchive, targetWorksheetPath, packageRelNs, relNs);
        if (targetVmlPath is null)
            return null;

        return EnsureSingleRelationshipForPackagePart(
            targetWorksheetRelsXml, packageRelNs, targetWorksheetPath, targetVmlPath,
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
            XlsxWorksheetElementOrder.Insert(worksheetRoot, marker);
        }

        foreach (var extraMarker in existingMarkers.Skip(1))
            extraMarker.Remove();

        marker.RemoveAttributes();
        marker.RemoveNodes();
        marker.SetAttributeValue(relNs + "id", relationshipId);
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
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var comment in commentsXml.Root?
                     .Element(workbookNs + "commentList")?
                     .Elements(workbookNs + "comment") ?? [])
        {
            var reference = comment.Attribute("ref")?.Value;
            if (string.IsNullOrWhiteSpace(reference))
                continue;

            // The comments part is expected to have at most one <comment> per ref, but tolerate
            // duplicates (e.g. from third-party tools or a prior lossy repair/merge) rather than
            // throwing: real Excel simply displays the last one it reads, so the last duplicate
            // wins here too.
            result[reference] = ExtractCommentPlainText(comment.Element(workbookNs + "text"), workbookNs);
        }

        return result;
    }

    private static Dictionary<string, XElement> ReadLegacyCommentElementsByReference(
        XDocument commentsXml,
        XNamespace workbookNs)
    {
        var result = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var comment in commentsXml.Root?
                     .Element(workbookNs + "commentList")?
                     .Elements(workbookNs + "comment") ?? [])
        {
            var reference = comment.Attribute("ref")?.Value;
            if (string.IsNullOrWhiteSpace(reference))
                continue;

            // Same duplicate-ref tolerance as ReadLegacyCommentPlainTextByReference above: keep
            // the last matching <comment> element instead of throwing on a duplicate key.
            result[reference] = comment;
        }

        return result;
    }
}
