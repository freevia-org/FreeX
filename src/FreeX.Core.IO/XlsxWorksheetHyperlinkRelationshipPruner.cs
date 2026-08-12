using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

/// <summary>
/// R100-io-hyperlink-1: removing a cell's external hyperlink (RemoveHyperlinksCommand /
/// ClearHyperlinksCommand, or simply reassigning <c>Sheet.Hyperlinks</c>) always forces a FULL
/// (ClosedXML) save -- <c>XlsxWorksheetHyperlinkPatch.TryCreate</c> bails onto the full-save path
/// whenever the hyperlink count changes. On that full save,
/// <c>XlsxPackageMetadataMerger.MergeRelationshipParts</c> merges relationship parts from the
/// session's tracked pre-edit source package into the freshly-regenerated package. An external
/// relationship's target lives outside the package entirely, so there is no corresponding package
/// part to check for survival, and the merger has always preserved every external relationship
/// unconditionally -- including the deleted hyperlink's now-dangling relationship entry
/// (Type=.../hyperlink, TargetMode=External, still pointing at the removed URL). Because
/// ApplyPackagePostProcessing re-captures the saved package as the NEXT session's source-package
/// snapshot, that orphan then survives every subsequent save of the session too, forever.
///
/// This prunes a worksheet-scoped external hyperlink relationship only when it demonstrably WAS a
/// live, model-tracked hyperlink in the pre-edit source worksheet (some <c>&lt;hyperlink
/// r:id="..."/&gt;</c> element referenced this exact relationship Id there) but no
/// <c>&lt;hyperlink&gt;</c> element anywhere in the final regenerated worksheet references it
/// anymore. Requiring the "was previously live" proof (rather than just "nothing references it
/// now") is essential: some fixtures/third-party tooling intentionally stash an opaque, FreeX-model-
/// unaware relationship under the standard hyperlink relationship type purely to exercise
/// unknown-package-graph round-trip fidelity (see
/// XlsxPackagePreservingSaveValidationTests.LoadEditSave_PreservesUnknownPackagePartsContentTypesAndRelationships)
/// -- such a relationship never had a corresponding &lt;hyperlink&gt; element even in the source and
/// must be left completely alone, not misread as an "orphan".
///
/// This must run as a dedicated FINAL pass, after every worksheet-content preserver (and
/// <see cref="XlsxWorksheetSinglePassNormalizer"/>) has finished writing/normalizing worksheet XML --
/// NOT inline inside <c>ShouldPreserveRelationship</c>, which runs too early in the save pipeline:
/// some preservers (e.g. <c>XlsxWorksheetMetadataPreserver.CellMetadata</c>'s reemission of a
/// whole-column/row hyperlink the load-time ClosedXML-input copy had to strip) rewrite a worksheet's
/// <c>&lt;hyperlink&gt;</c> elements well after relationships are merged, so checking liveness at
/// merge time would misclassify those still-live, not-yet-reemitted hyperlinks as orphans.
///
/// R107-io-hyperlink-edit-1: EDITING (not removing) an external hyperlink's target also always
/// forces the same full-save path (<c>XlsxWorksheetHyperlinkPatch.TryCreate</c> bails whenever the
/// source hyperlink carries a relationship Id at all, not just on a count change). On that full
/// save ClosedXML re-derives every relationship Id purely from emission order/count, so a same-shape
/// worksheet (same cell, same hyperlink count, only the target URL changed) is reassigned the exact
/// same Id the pre-edit relationship used. <c>MergeRelationshipParts</c> then detects that Id
/// collision against the freshly-written replacement relationship and remaps the copied (stale,
/// pre-edit) relationship onto a BRAND-NEW Id that never appeared anywhere pre-edit -- so the
/// Id-based "was previously live" check above can never match it, and the stale copy survives
/// forever. Because the copy's Id is new, but its Target URL is not (that URL WAS the live target of
/// a previously-live hyperlink Id), a second check keyed on Target URL rather than Id catches this
/// case without weakening the "must have demonstrably been live pre-edit" safety property: a target
/// only enters <see cref="PruneWorksheetRelationships"/>'s previously-live-target set by being the
/// Target of a relationship whose Id a real pre-edit &lt;hyperlink r:id="..."/&gt; referenced.
/// </summary>
internal static class XlsxWorksheetHyperlinkRelationshipPruner
{
    private const string HyperlinkRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace WorksheetRelationshipNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static void PruneOrphanedHyperlinkRelationships(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext? context)
    {
        // No positive proof of "was previously a live hyperlink" is possible without the
        // source/target sheet-path mapping, so there is nothing safe to prune.
        if (context is null)
            return;

        foreach (var (sheetName, sourceWorksheetPath) in context.SourceSheets)
        {
            // R124-io-hyperlink-rename-1: sheetName here is the LOAD-TIME name; context.TargetSheets
            // is keyed by the CURRENT (post-rename) name, so a plain lookup of sheetName against it
            // fails unconditionally for any sheet renamed in this same edit session -- silently
            // skipping the prune for that sheet forever (see class doc comment: an un-pruned orphan
            // is re-captured as the next session's source snapshot and can never be pruned
            // afterward). Resolve via the same rename-tolerant fallback every sibling preserver in
            // this file set already uses (name match, then rename-stable worksheet-path match, then
            // Sheet.Id-verified name when available).
            if (!XlsxRenamedSourceSheetResolver.TryResolveTargetWorksheetPath(
                    context, sheetName, sourceWorksheetPath, out var targetWorksheetPath))
            {
                continue;
            }

            var targetRelsPath = XlsxPackagePath.GetRelationshipPartPath(targetWorksheetPath);
            var relsEntry = targetArchive.GetEntry(targetRelsPath);
            if (relsEntry is null)
                continue;

            try
            {
                PruneWorksheetRelationships(sourceArchive, targetArchive, context, sourceWorksheetPath, targetWorksheetPath, relsEntry);
            }
            catch
            {
                // Unparsable worksheet or .rels XML: nothing to positively prove is orphaned, so
                // leave this worksheet's relationships untouched rather than risk pruning a live one.
            }
        }
    }

    private static void PruneWorksheetRelationships(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        XlsxSourcePackagePreservationContext context,
        string sourceWorksheetPath,
        string targetWorksheetPath,
        ZipArchiveEntry relsEntry)
    {
        var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceWorksheetPath);
        var previouslyLiveHyperlinkIds = ReadHyperlinkRelationshipIds(sourceWorksheetXml);
        if (previouslyLiveHyperlinkIds.Count == 0)
            return;

        var targetWorksheetEntry = targetArchive.GetEntry(targetWorksheetPath);
        if (targetWorksheetEntry is null)
            return;

        var targetWorksheetXml = XlsxPackageXmlEditor.LoadXml(targetWorksheetEntry);
        var currentlyLiveHyperlinkIds = ReadHyperlinkRelationshipIds(targetWorksheetXml);

        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
        var root = relsXml.Root;
        if (root is null)
            return;

        // R107-io-hyperlink-edit-1: resolve the previously-live Ids to the Target URLs they pointed
        // at via the PRE-EDIT source .rels part. A stale relationship carried forward by an edit
        // (rather than a removal) may have been remapped onto a brand-new Id during the merge (see
        // class doc comment), so it can no longer be found by Id alone -- but its Target URL is
        // still the same URL a demonstrably-live pre-edit hyperlink pointed to, which is what makes
        // it safe to recognize by Target without resurrecting the "never was a live hyperlink"
        // false-positive risk the Id-based check already guards against.
        var sourceRelsEntry = sourceArchive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(sourceWorksheetPath));
        var previouslyLiveHyperlinkTargets = ReadExternalHyperlinkTargetsByIds(sourceRelsEntry, previouslyLiveHyperlinkIds);
        var currentlyLiveHyperlinkTargets = ReadExternalHyperlinkTargetsByIds(root, currentlyLiveHyperlinkIds);

        var orphans = root.Elements(PackageRelationshipNs + "Relationship")
            .Where(element => IsOrphanedHyperlinkRelationship(
                element,
                previouslyLiveHyperlinkIds,
                currentlyLiveHyperlinkIds,
                previouslyLiveHyperlinkTargets,
                currentlyLiveHyperlinkTargets))
            .ToList();
        if (orphans.Count == 0)
            return;

        foreach (var orphan in orphans)
            orphan.Remove();

        if (root.Elements(PackageRelationshipNs + "Relationship").Any())
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, relsEntry.FullName, relsXml);
        else
            relsEntry.Delete();
    }

    private static bool IsOrphanedHyperlinkRelationship(
        XElement relationship,
        IReadOnlySet<string> previouslyLiveHyperlinkIds,
        IReadOnlySet<string> currentlyLiveHyperlinkIds,
        IReadOnlySet<string> previouslyLiveHyperlinkTargets,
        IReadOnlySet<string> currentlyLiveHyperlinkTargets)
    {
        if (!string.Equals(
                relationship.Attribute("Type")?.Value.Trim(),
                HyperlinkRelationshipType,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // R107-io-internal-hyperlink-rel: no TargetMode filter. This used to prune only
        // TargetMode="External" relationships, because the merger's package-part survival check
        // silently dropped every TargetMode-less hyperlink relationship anyway (its "Sheet1!A1"-style
        // document-location target never resolves to a real part), so no internal one could ever
        // reach here to be orphaned. The merger now preserves both modes alike -- a hyperlink target
        // is never a package part regardless of TargetMode -- so an internal ("Place in This
        // Document") relationship whose cell hyperlink was removed must be pruned on exactly the same
        // "was demonstrably live pre-edit, is referenced by nothing now" proof.
        var id = relationship.Attribute("Id")?.Value;
        if (string.IsNullOrWhiteSpace(id))
            return false;

        // Deletion case: this exact Id demonstrably WAS a live, referenced hyperlink pre-edit and is
        // no longer referenced by any <hyperlink> in the regenerated worksheet.
        if (previouslyLiveHyperlinkIds.Contains(id) && !currentlyLiveHyperlinkIds.Contains(id))
            return true;

        // Edit case (R107-io-hyperlink-edit-1): the Id itself may be a merge-time remap that never
        // appeared pre-edit (so the check above can't match it), but its Target URL is exactly the
        // URL a demonstrably-live pre-edit hyperlink pointed to. Only prune when that URL is no
        // longer the target of ANY currently-live hyperlink -- preserving the "must have
        // demonstrably been live pre-edit" safety property (an opaque, FreeX-model-unaware
        // relationship that no <hyperlink> ever referenced never enters previouslyLiveHyperlinkTargets)
        // while also not disturbing a second, untouched hyperlink that still legitimately targets the
        // same URL.
        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
            return false;

        return previouslyLiveHyperlinkTargets.Contains(target) && !currentlyLiveHyperlinkTargets.Contains(target);
    }

    private static IReadOnlySet<string> ReadExternalHyperlinkTargetsByIds(ZipArchiveEntry? relsEntry, IReadOnlySet<string> ids)
    {
        if (relsEntry is null || ids.Count == 0)
            return new HashSet<string>(StringComparer.Ordinal);

        XDocument relsXml;
        try
        {
            relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
        }
        catch
        {
            return new HashSet<string>(StringComparer.Ordinal);
        }

        return ReadExternalHyperlinkTargetsByIds(relsXml.Root, ids);
    }

    private static IReadOnlySet<string> ReadExternalHyperlinkTargetsByIds(XElement? relsRoot, IReadOnlySet<string> ids)
    {
        var targets = new HashSet<string>(StringComparer.Ordinal);
        if (relsRoot is null || ids.Count == 0)
            return targets;

        foreach (var relationship in relsRoot.Elements(PackageRelationshipNs + "Relationship"))
        {
            var id = relationship.Attribute("Id")?.Value;
            if (string.IsNullOrWhiteSpace(id) || !ids.Contains(id))
                continue;

            if (!string.Equals(
                    relationship.Attribute("Type")?.Value.Trim(),
                    HyperlinkRelationshipType,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(
                    relationship.Attribute("TargetMode")?.Value.Trim(),
                    "External",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = relationship.Attribute("Target")?.Value;
            if (!string.IsNullOrWhiteSpace(target))
                targets.Add(target);
        }

        return targets;
    }

    private static IReadOnlySet<string> ReadHyperlinkRelationshipIds(XDocument? worksheetXml)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var root = worksheetXml?.Root;
        if (root is null)
            return ids;

        foreach (var hyperlink in root.Descendants().Where(element => element.Name.LocalName == "hyperlink"))
        {
            var id = hyperlink.Attribute(WorksheetRelationshipNs + "id")?.Value;
            if (!string.IsNullOrWhiteSpace(id))
                ids.Add(id);
        }

        return ids;
    }
}
