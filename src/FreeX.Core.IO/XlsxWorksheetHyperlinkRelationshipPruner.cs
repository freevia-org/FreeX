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

        foreach (var (sheetName, targetWorksheetPath) in context.TargetSheets)
        {
            if (!context.SourceSheets.TryGetValue(sheetName, out var sourceWorksheetPath))
                continue;

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
        var sourceWorksheetXml = context.GetSourceWorksheetXml(sourceArchive, sourceWorksheetPath);
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

        var orphans = root.Elements(PackageRelationshipNs + "Relationship")
            .Where(element => IsOrphanedHyperlinkRelationship(element, previouslyLiveHyperlinkIds, currentlyLiveHyperlinkIds))
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
        IReadOnlySet<string> currentlyLiveHyperlinkIds)
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

        // Only prune when this Id demonstrably WAS a live, referenced hyperlink pre-edit -- a
        // relationship no <hyperlink> ever pointed to (source or target) is left alone regardless
        // of whether anything currently references it (see class doc comment).
        return previouslyLiveHyperlinkIds.Contains(id) && !currentlyLiveHyperlinkIds.Contains(id);
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
