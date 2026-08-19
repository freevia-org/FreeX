using System.IO.Compression;
using System.Xml.Linq;

using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class XlsxFileAdapter
{
    private const string ChartExStyleRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/chartStyle";
    private const string ChartExColorStyleRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/chartColorStyle";
    private const string QueryTableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/queryTable";
    private const string QueryTableContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.queryTable+xml";

    // R70-io-vba-6-1: parts making up a source package's VBA project. xl/vbaProject.bin is the
    // macro project itself; a digitally-signed macro project also carries
    // xl/vbaProjectSignature.bin (referenced only from vbaProject.bin's own .rels). Both are
    // excluded together whenever the target must be a plain (non-macro) package -- leaving either
    // one behind would either resurrect the VBA project or dangle a signature part with no project
    // to sign.
    private static readonly string[] VbaProjectPackagePartPaths =
    [
        "xl/vbaProject.bin",
        "xl/vbaProjectSignature.bin"
    ];

    // Source package snapshot and native package-part preservation for loaded workbook saves.
    // preserveVbaProject: when false, a source vbaProject.bin (and its digital-signature sidecar)
    // is dropped instead of carried through, and the workbook's content-type is left at the plain
    // spreadsheetml type ClosedXML wrote rather than flipped back to macroEnabled.main -- matching
    // Excel's own behavior when a macro-enabled workbook is saved as a plain .xlsx/.xltx.
    private static SourcePackagePartSummary PreserveSourcePackageParts(
        Workbook workbook,
        Stream generatedPackage,
        bool preserveVbaProject = true)
    {
        if (!SourcePackages.TryGetValue(workbook, out var sourcePackage))
            return default;

        using var sourceStream = sourcePackage.OpenRead();
        using var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false);
        using var generatedArchive = new ZipArchive(generatedPackage, ZipArchiveMode.Update, leaveOpen: true);
        var context = XlsxSourcePackagePreservationContext.TryCreate(
            sourceArchive,
            generatedArchive,
            workbook,
            sourcePackage.SourceSheetIdsByLocalId);
        var sourceParts = InspectSourcePackageParts(sourceArchive);
        var removedWorksheetPackageParts = GetExcludedWorksheetPackagePartPaths(
            sourceArchive,
            context,
            workbook,
            sourcePackage.SourceSheetIdsByLocalId ?? []);
        var excludedSourceParts = removedWorksheetPackageParts
            .Concat(XlsxWorksheetThreadedCommentMapper.GetSourcePackagePartExclusions(sourceArchive, workbook))
            .Concat(XlsxDigitalSignaturePackagePolicy.GetEditedSaveExclusions(sourceArchive))
            .Concat(preserveVbaProject ? Array.Empty<string>() : VbaProjectPackagePartPaths)
            .Concat(GetExcludedDeletedChartPartPaths(sourceArchive, context, workbook))
            .Concat(GetExcludedDeletedPicturePartPaths(sourceArchive, context, workbook))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(
            sourceArchive,
            generatedArchive,
            excludedSourceParts);

        XlsxPackageMetadataMerger.MergeContentTypes(
            sourceArchive,
            generatedArchive,
            excludedSourceParts,
            preserveMacroEnabledWorkbookContentType: preserveVbaProject);
        PreserveSourceChartExParts(workbook, sourceArchive, generatedArchive, generatedEntriesBeforeMerge);
        XlsxPackageMetadataMerger.MergeRelationshipParts(
            sourceArchive,
            generatedArchive,
            generatedEntriesBeforeMerge,
            excludedSourceParts);
        XlsxCustomRibbonPackageGraphNormalizer.NormalizePackage(generatedArchive);
        XlsxPackageMetadataMerger.NormalizeCustomXmlPackageGraph(generatedArchive);
        XlsxDocumentPropertiesPreserver.Preserve(sourceArchive, generatedArchive);
        XlsxWorkbookMetadataPreserver.Preserve(
            context,
            workbook,
            sourcePackage.SourceSheetIdsByLocalId ?? []);
        XlsxStylesheetMetadataPreserver.Preserve(sourceArchive, generatedArchive);
        if (sourceParts.HasPivotPackageParts)
            XlsxPivotXmlReferencePreserver.Preserve(context);
        if (sourceParts.HasStructuredTables)
            XlsxStructuredTableReferencePreserver.Preserve(context);
        if (sourceParts.HasQueryTables)
        {
            PreserveRenumberedWorksheetQueryTableRelationships(sourceArchive, generatedArchive, context);
            CloneQueryTablesForDuplicatedSheets(sourceArchive, generatedArchive, context, workbook);
        }
        if (sourceParts.HasExternalLinks)
            XlsxExternalLinkReferencePreserver.Preserve(context);
        // R96-io-external-link-writer-1: runs unconditionally (not gated on HasExternalLinks) since
        // this is about a freshly TYPED bracketed external-workbook reference the loaded source
        // package never carried at all -- the exact "workbook that had none" case the preserver
        // above can't help with. Placed after the preserver so its own idempotency scan (over the
        // package's own already-written external-link infrastructure) sees whatever the preserver
        // just carried forward and never double-backs the same book.
        XlsxExternalLinkAuthoringWriter.Save(generatedArchive, workbook);
        if (sourceParts.HasUnsupportedSheetParts)
            XlsxUnsupportedSheetReferencePreserver.Preserve(context, workbook);
        if (sourceParts.HasDrawings)
        {
            var drawingPaths = XlsxWorksheetDrawingPartMerger.MergeAndGetDrawingPaths(sourceArchive, generatedArchive, context, workbook);
            XlsxWorksheetDrawingReferencePreserver.Preserve(context, drawingPaths);
        }
        if (sourcePackage.WorksheetsWithPreservableSourceMetadata?.Count != 0)
        {
            XlsxWorksheetMetadataPreserver.Preserve(
                workbook,
                context,
                sourcePackage.WorksheetsWithPreservableSourceMetadata);
        }
        XlsxWorksheetPrinterSettingsReferencePreserver.Preserve(context);
        if (sourceParts.HasDrawings)
            XlsxWorksheetVmlReferencePreserver.Preserve(context, workbook);
        if (sourceParts.HasFormControls)
        {
            XlsxWorksheetFormControlPreserver.Preserve(context, workbook);
            CloneFormControlsForDuplicatedSheets(context, workbook);
        }
        if (sourceParts.HasLegacyComments)
            XlsxLegacyCommentPreserver.Preserve(workbook, context);
        if (sourceParts.HasFormControls && sourceParts.HasLegacyComments)
        {
            // R112-io-formcontrol-vml-anchor-comment-reorder-1: XlsxLegacyCommentPreserver.Preserve
            // just unconditionally rebuilt the shared legacyDrawing VML part from the pristine source
            // archive whenever the sheet has any Notes (see its own doc comments), discarding the
            // Form Control anchor sync XlsxWorksheetFormControlPreserver.Preserve wrote into the
            // target moments earlier. Re-apply the anchor sync now, last, so it always wins.
            XlsxWorksheetFormControlPreserver.ReapplyVmlAnchorsAfterCommentReconciliation(
                context, workbook);
        }
        if (sourceParts.HasSharedStrings)
            XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics(sourceArchive, generatedArchive);
        if (sourcePackage.HasUnsupportedConditionalFormatting ?? HasUnsupportedConditionalFormatting(sourceArchive))
            XlsxUnsupportedConditionalFormattingPreserver.Preserve(sourceArchive, generatedArchive);

        XlsxWorksheetSinglePassNormalizer.NormalizeWorksheets(generatedArchive);
        // R100-io-hyperlink-1: must run after every worksheet-content preserver above AND the
        // single-pass normalizer, once each worksheet's <hyperlink> elements are fully finalized
        // (including any late reemission, e.g. a stripped whole-column/row hyperlink written back
        // by XlsxWorksheetMetadataPreserver.Preserve) -- see
        // XlsxWorksheetHyperlinkRelationshipPruner's doc comment for why this cannot run any
        // earlier (e.g. inline in XlsxPackageMetadataMerger.MergeRelationshipParts).
        XlsxWorksheetHyperlinkRelationshipPruner.PruneOrphanedHyperlinkRelationships(sourceArchive, generatedArchive, context);
        XlsxRichTextFontNormalizer.NormalizePackage(generatedArchive);
        XlsxSharedStringPackageGraphNormalizer.NormalizePackage(generatedArchive);
        XlsxDocumentThumbnailPackageGraphNormalizer.NormalizePackage(generatedArchive);
        XlsxThemeTypefaceNormalizer.NormalizePackage(generatedArchive);
        XlsxLegacyCommentFontNormalizer.NormalizePackage(generatedArchive);
        XlsxStructuredTableSchemaNormalizer.NormalizePackage(generatedArchive);
        XlsxExternalLinkSchemaNormalizer.NormalizePackage(generatedArchive);
        XlsxWorksheetSingleXmlCellMapper.NormalizePackage(generatedArchive);
        return sourceParts;
    }

    private struct SourcePackagePartSummary
    {
        public bool HasPivotPackageParts;
        public bool HasStructuredTables;
        public bool HasExternalLinks;
        public bool HasUnsupportedSheetParts;
        public bool HasDrawings;
        public bool HasPrinterSettings;
        public bool HasSharedStrings;
        public bool HasLegacyComments;
        public bool HasFormControls;
        public bool HasQueryTables;
    }

    private static SourcePackagePartSummary InspectSourcePackageParts(ZipArchive archive)
    {
        var summary = new SourcePackagePartSummary();
        foreach (var entry in archive.Entries)
        {
            var fullName = entry.FullName;
            summary.HasPivotPackageParts |=
                fullName.StartsWith("xl/pivotCache/", StringComparison.OrdinalIgnoreCase) ||
                fullName.StartsWith("xl/pivotTables/", StringComparison.OrdinalIgnoreCase);
            summary.HasStructuredTables |= fullName.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase);
            summary.HasExternalLinks |= fullName.StartsWith("xl/externalLinks/", StringComparison.OrdinalIgnoreCase);
            summary.HasUnsupportedSheetParts |=
                fullName.StartsWith("xl/dialogSheets/", StringComparison.OrdinalIgnoreCase) ||
                fullName.StartsWith("xl/chartsheets/", StringComparison.OrdinalIgnoreCase) ||
                fullName.StartsWith("xl/macrosheets/", StringComparison.OrdinalIgnoreCase);
            summary.HasDrawings |= fullName.StartsWith("xl/drawings/", StringComparison.OrdinalIgnoreCase);
            summary.HasPrinterSettings |= fullName.StartsWith("xl/printerSettings/", StringComparison.OrdinalIgnoreCase);
            summary.HasSharedStrings |= fullName.Equals("xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase);
            summary.HasLegacyComments |= fullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase);
            summary.HasFormControls |= fullName.StartsWith("xl/ctrlProps/", StringComparison.OrdinalIgnoreCase);
            summary.HasQueryTables |= fullName.StartsWith("xl/queryTables/", StringComparison.OrdinalIgnoreCase);

            if (summary.HasPivotPackageParts &&
                summary.HasStructuredTables &&
                summary.HasExternalLinks &&
                summary.HasUnsupportedSheetParts &&
                summary.HasDrawings &&
                summary.HasPrinterSettings &&
                summary.HasSharedStrings &&
                summary.HasLegacyComments &&
                summary.HasFormControls &&
                summary.HasQueryTables)
            {
                break;
            }
        }

        return summary;
    }

    private static IReadOnlySet<string> GetExcludedWorksheetPackagePartPaths(
        ZipArchive sourceArchive,
        XlsxSourcePackagePreservationContext? context,
        Workbook workbook,
        IReadOnlyList<SheetId> sourceSheetIdsByLocalId)
    {
        var excludedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (context is null)
            return excludedPaths;

        // R102-io-rename-worksheet-exclusion-1: context.SourceSheets is keyed by each sheet's name AS
        // LOADED, while context.TargetSheets is keyed by its name in the FRESHLY GENERATED package --
        // i.e. AFTER any in-session rename has already been applied and re-serialized. A plain lookup
        // of the old (load-time) name against the new-name-keyed dictionary therefore fails for every
        // renamed sheet, exactly like a genuine delete, and every part that survives only via this
        // source-preservation passthrough (e.g. a legacy queryTable/"Get External Data" binding, which
        // FreeX has no in-model representation of at all) gets silently dropped on save whenever its
        // sheet is renamed. Resolve each load-time name to its CURRENT name first, via the same
        // rename-stable Sheet.Id identity XlsxWorkbookMetadataPreserver's defined-name-scope remap
        // already relies on (sourceSheetIdsByLocalId) -- a sheet that still exists keeps its Sheet.Id
        // across any number of renames, so looking that Id up in the live workbook recovers its
        // current name regardless of how it's been renamed since load. A sheet whose Sheet.Id is gone
        // from the live workbook has genuinely been deleted, and correctly falls through unresolved.
        var currentNameByLoadTimeName = ResolveCurrentSheetNamesByLoadTimeName(context, workbook, sourceSheetIdsByLocalId);

        var sourceWorksheetPaths = context.SourceSheets
            .Select(pair => new
            {
                pair.Key,
                SourcePath = XlsxPackagePath.NormalizePackagePath(pair.Value),
                CurrentName = currentNameByLoadTimeName.TryGetValue(pair.Key, out var mapped) ? mapped : pair.Key
            })
            .Where(pair => IsWorksheetPartPath(pair.SourcePath))
            .ToList();

        foreach (var sourceSheet in sourceWorksheetPaths)
        {
            if (!context.TargetSheets.TryGetValue(sourceSheet.CurrentName, out var targetPath) ||
                !string.Equals(
                    sourceSheet.SourcePath,
                    XlsxPackagePath.NormalizePackagePath(targetPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                excludedPaths.Add(sourceSheet.SourcePath);
                excludedPaths.Add(XlsxPackagePath.GetRelationshipPartPath(sourceSheet.SourcePath));
            }
        }

        var removedWorksheetPaths = sourceWorksheetPaths
            .Where(pair => !context.TargetSheets.ContainsKey(pair.CurrentName))
            .Select(pair => pair.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (removedWorksheetPaths.Count == 0)
            return excludedPaths;

        // Compute relationship-dependency paths per retained sheet once (O(N) archive reads total).
        // The original code called GetRelationshipDependencyPaths for every (sheet, candidate) pair in the
        // second loop — O(N²) archive reads for N retained sheets. We instead memoize each retained sheet's
        // dep set here and derive the "outside this sheet" predicate from reference-count data.
        var retainedDepsBySheetPath = sourceWorksheetPaths
            .Where(pair => context.TargetSheets.ContainsKey(pair.CurrentName))
            .ToDictionary(
                pair => pair.SourcePath,
                pair => (IReadOnlySet<string>)GetRelationshipDependencyPaths(sourceArchive, pair.SourcePath, context.PackageRelNs)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

        // retainedTargets: union of all retained sheets' deps (used for removed-sheet exclusion).
        var retainedTargets = retainedDepsBySheetPath.Values
            .SelectMany(deps => deps)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // retainedRefCount: how many retained sheets reference each path.
        // A path is "referenced by at least one OTHER retained sheet" iff refCount > 1 or this sheet doesn't own it.
        var retainedRefCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var deps in retainedDepsBySheetPath.Values)
            foreach (var path in deps)
                retainedRefCount[path] = retainedRefCount.TryGetValue(path, out var c) ? c + 1 : 1;

        foreach (var worksheetPath in removedWorksheetPaths)
        {
            foreach (var targetPath in GetRelationshipDependencyPaths(sourceArchive, worksheetPath, context.PackageRelNs))
            {
                if (!retainedTargets.Contains(targetPath))
                    excludedPaths.Add(targetPath);
            }
        }

        foreach (var sourceSheet in sourceWorksheetPaths)
        {
            if (!context.TargetSheets.ContainsKey(sourceSheet.CurrentName))
                continue;

            // Look the sheet up by its CURRENT name (see currentNameByLoadTimeName above) so a
            // renamed sheet's own legacy-drawing header/footer dependencies are still resolved
            // against the live Sheet object, not silently skipped because sourceSheet.Key (the
            // load-time name) no longer names anything in the live workbook.
            var sheet = workbook.GetSheet(sourceSheet.CurrentName);
            if (sheet is null || XlsxHeaderFooterPictureReaderWriter.HasPictures(sheet))
                continue;

            var ownDeps = retainedDepsBySheetPath.TryGetValue(sourceSheet.SourcePath, out var d) ? d : null;

            foreach (var targetPath in GetLegacyDrawingHfDependencyPaths(
                         sourceArchive,
                         sourceSheet.SourcePath,
                         context.WorkbookNs,
                         context.RelNs,
                         context.PackageRelNs))
            {
                // Equivalent to: !retainedTargetsOutsideSheet.Contains(targetPath), where
                // retainedTargetsOutsideSheet = union of deps of all retained sheets OTHER than sourceSheet.
                // A path is in that set iff: some retained sheet other than this one references it, i.e.
                // refCount > 1, or ownDeps does not contain it (it's retained by another sheet entirely).
                var inOutsideSet = retainedRefCount.TryGetValue(targetPath, out var refCount) &&
                                   (refCount > 1 || ownDeps is null || !ownDeps.Contains(targetPath));
                if (!inOutsideSet)
                    excludedPaths.Add(targetPath);
            }
        }

        return excludedPaths;
    }

    // R102-io-rename-worksheet-exclusion-1: maps each sheet's load-time name (as it appeared in the
    // pristine source package) to its CURRENT name in the live workbook, using the same rename-stable
    // Sheet.Id identity XlsxWorkbookMetadataPreserver.MergeDefinedNames already relies on to
    // disambiguate a rename from a delete+add-a-different-sheet. sourceSheetIdsByLocalId[i] is the
    // Sheet.Id the sheet at position i had at the moment this source snapshot became the pristine
    // baseline (see XlsxFileAdapter.SourcePackageSnapshot's SourceSheetIdsByLocalId doc comment); a
    // sheet whose Id no longer exists in workbook.Sheets has genuinely been deleted and is simply
    // absent from the returned map (callers fall back to the load-time name, which then correctly
    // fails to match anything in context.TargetSheets).
    private static Dictionary<string, string> ResolveCurrentSheetNamesByLoadTimeName(
        XlsxSourcePackagePreservationContext context,
        Workbook workbook,
        IReadOnlyList<SheetId> sourceSheetIdsByLocalId)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (sourceSheetIdsByLocalId.Count == 0)
            return map;

        var sheetElements = context.SourceWorkbookXml.Root?
            .Element(context.WorkbookNs + "sheets")?
            .Elements(context.WorkbookNs + "sheet")
            .ToList()
            ?? [];

        for (var localId = 0; localId < sheetElements.Count && localId < sourceSheetIdsByLocalId.Count; localId++)
        {
            var loadTimeName = sheetElements[localId].Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(loadTimeName))
                continue;

            var originalSheetId = sourceSheetIdsByLocalId[localId];
            var currentSheet = workbook.GetSheet(originalSheetId);
            if (currentSheet is not null)
                map[loadTimeName] = currentSheet.Name;
        }

        return map;
    }

    // R127-io-drawing-relationship-orphan-1: a picture/chart/shape/text box that was originally loaded
    // from the source .xlsx and then deleted this session (DeleteDrawingObjectCommand tombstones its
    // cNvPr@name onto Sheet.DeletedSourceDrawingObjectNames -- see that command and
    // XlsxWorksheetDrawingObjectWriter.GetRewrittenSourceObjectNames) has its ORIGINAL anchor correctly
    // dropped from the merged drawing part (XlsxWorksheetDrawingPartMerger.MergeDrawingPart's
    // supersededSourceNames check) and, as of the sibling fix in that same merger, its now-orphaned
    // drawing-relationship entry is pruned too. For a deleted CHART specifically that still leaves the
    // chart's own part set sitting in the package: xl/charts/chartN.xml is never written by ClosedXML
    // (FreeX has no in-model concept of it once deleted) nor excluded from excludedSourceParts above, so
    // CopyUnknownPackageParts -- which runs BEFORE the drawing merge -- blindly copies it (and its own
    // .rels, colorsN.xml, styleN.xml) into the generated package as an "unknown" part regardless. Because
    // ApplyPackagePostProcessing re-captures every saved package as the next save's source snapshot, an
    // un-excluded chart part would be resurrected as a passthrough part forever. Resolve each sheet's
    // DeletedSourceDrawingObjectNames against the SOURCE drawing part (never the target/generated one --
    // that never had the deleted object's chart anchor to begin with) to find any deleted name that WAS a
    // chart, and add its part set to excludedSourceParts before CopyUnknownPackageParts runs at all.
    private static IReadOnlySet<string> GetExcludedDeletedChartPartPaths(
        ZipArchive sourceArchive,
        XlsxSourcePackagePreservationContext? context,
        Workbook workbook)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (context is null)
            return excluded;

        foreach (var (sheetName, sourceWorksheetPath) in context.SourceSheets)
        {
            if (!IsWorksheetPartPath(sourceWorksheetPath))
                continue;
            if (!XlsxRenamedSourceSheetResolver.TryResolveCurrentSheet(
                    context, sheetName, sourceWorksheetPath, out var currentSheetName, out _))
            {
                continue; // Sheet genuinely deleted -- its source drawing (and any chart in it) is
                          // already excluded wholesale via removedWorksheetPackageParts.
            }

            var sheet = workbook.GetSheet(currentSheetName);
            if (sheet is null || sheet.DeletedSourceDrawingObjectNames.Count == 0)
                continue;

            var sourceDrawingPath = XlsxWorksheetDrawingPartMerger.GetWorksheetDrawingPath(
                sourceArchive, sourceWorksheetPath, context.WorkbookNs, context.RelNs, context.PackageRelNs, context);
            if (string.IsNullOrWhiteSpace(sourceDrawingPath))
                continue;

            var deletedNames = sheet.DeletedSourceDrawingObjectNames.ToHashSet(StringComparer.Ordinal);
            foreach (var chartPartPath in GetDeletedChartPartPaths(sourceArchive, sourceDrawingPath, deletedNames, context.RelNs, context.PackageRelNs))
            {
                excluded.Add(chartPartPath);
                excluded.Add(XlsxPackagePath.GetRelationshipPartPath(chartPartPath));
                foreach (var dependencyPath in GetRelationshipDependencyPaths(sourceArchive, chartPartPath, context.PackageRelNs))
                    excluded.Add(dependencyPath);
            }
        }

        return excluded;
    }

    // Resolved chart-part paths (e.g. "xl/charts/chart3.xml") for every graphicFrame anchor in the
    // SOURCE drawing part whose cNvPr@name matches one of deletedNames. Mirrors
    // XlsxWorksheetDrawingPartMerger.ResolveAnchorChartTargetFromRels, kept as a self-contained copy here
    // since that method operates on an already-loaded anchor/rels pair the merger builds internally,
    // while this needs to read directly from the pristine SOURCE package before any merge has run.
    private static IEnumerable<string> GetDeletedChartPartPaths(
        ZipArchive sourceArchive,
        string sourceDrawingPath,
        IReadOnlySet<string> deletedNames,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var drawingEntry = sourceArchive.GetEntry(sourceDrawingPath);
        if (drawingEntry is null)
            yield break;

        var drawingXml = XlsxPackageXmlEditor.LoadXml(drawingEntry);
        if (drawingXml.Root is null)
            yield break;

        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XNamespace chartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";

        var drawingRels = XlsxRelationshipReader.LoadTargets(
            sourceArchive, XlsxPackagePath.GetRelationshipPartPath(sourceDrawingPath), sourceDrawingPath, packageRelNs);

        foreach (var anchor in drawingXml.Root.Elements())
        {
            var name = anchor.Descendants(spreadsheetDrawingNs + "cNvPr")
                .Select(element => element.Attribute("name")?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (name is null || !deletedNames.Contains(name))
                continue;

            foreach (var chartElement in anchor.Descendants()
                         .Where(element => element.Name == chartNs + "chart" || element.Name == chartExNs + "chart"))
            {
                var relId = chartElement.Attribute(relNs + "id")?.Value;
                if (!string.IsNullOrWhiteSpace(relId) && drawingRels.TryGetValue(relId, out var chartPartPath))
                    yield return chartPartPath;
            }
        }
    }

    // R147-io-drawing-media-orphan-1: the sibling gap left by R127-io-drawing-relationship-orphan-1
    // above. That fix excludes a deleted CHART's own part set (xl/charts/chartN.xml) from
    // CopyUnknownPackageParts; it never touched a deleted PICTURE's own binary
    // (xl/media/freexPictureN.<ext> or, for a real Excel-authored source package, imageN.<ext>).
    // XlsxWorksheetDrawingPartMerger correctly drops the deleted picture's <xdr:pic> anchor
    // (supersededSourceNames) and prunes the now-dangling image relationship from the drawing part's
    // own .rels (PruneUnreferencedDrawingRelationships), but the underlying media part itself was never
    // excluded, so CopyUnknownPackageParts (which runs BEFORE the drawing merge, over the untouched
    // SOURCE package) copies it forward as an "unknown" part regardless -- and since every save
    // re-captures the written package as the next save's source, the orphan persists across every
    // subsequent save indefinitely. Resolve each sheet's DeletedSourceDrawingObjectNames against the
    // SOURCE drawing part to find the media target(s) a deleted picture anchor referenced, exactly the
    // way GetExcludedDeletedChartPartPaths already does for a deleted chart's own part.
    // <para>
    // Unlike a chart part (never shared between anchors), the SAME xl/media/* file can legitimately be
    // referenced by more than one surviving picture anchor -- e.g. the same image inserted twice, or
    // present on two different sheets -- so a candidate media target is only ever excluded if no
    // SURVIVING (non-tombstoned) picture anchor anywhere else in the source package still resolves to
    // it. Excluding a still-referenced media part would trade the original orphan-file bug for a worse
    // one: a broken image on a picture the user never touched.
    // </para>
    private static IReadOnlySet<string> GetExcludedDeletedPicturePartPaths(
        ZipArchive sourceArchive,
        XlsxSourcePackagePreservationContext? context,
        Workbook workbook)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (context is null)
            return excluded;

        // First pass: every media target still reachable from a SURVIVING (non-deleted) picture
        // anchor anywhere in the source package. A sheet that was itself deleted this session
        // contributes nothing here -- its entire drawing (and any media only it used) is already
        // excluded wholesale via removedWorksheetPackageParts, and none of its anchors can keep a
        // media part "alive" for some other, still-live sheet either.
        var aliveMediaTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sheetsWithDeletions = new List<(string SourceDrawingPath, HashSet<string> DeletedNames)>();
        foreach (var (sheetName, sourceWorksheetPath) in context.SourceSheets)
        {
            if (!IsWorksheetPartPath(sourceWorksheetPath))
                continue;
            if (!XlsxRenamedSourceSheetResolver.TryResolveCurrentSheet(
                    context, sheetName, sourceWorksheetPath, out var currentSheetName, out _))
            {
                continue; // Sheet genuinely deleted -- see the doc comment above.
            }

            var sheet = workbook.GetSheet(currentSheetName);
            if (sheet is null)
                continue;

            var sourceDrawingPath = XlsxWorksheetDrawingPartMerger.GetWorksheetDrawingPath(
                sourceArchive, sourceWorksheetPath, context.WorkbookNs, context.RelNs, context.PackageRelNs, context);
            if (string.IsNullOrWhiteSpace(sourceDrawingPath))
                continue;

            var deletedNames = sheet.DeletedSourceDrawingObjectNames;
            foreach (var (anchorName, mediaTarget) in GetPictureAnchorMediaTargets(sourceArchive, sourceDrawingPath, context.RelNs, context.PackageRelNs))
            {
                if (deletedNames.Count == 0 || !deletedNames.Contains(anchorName))
                    aliveMediaTargets.Add(mediaTarget);
            }

            if (deletedNames.Count > 0)
                sheetsWithDeletions.Add((sourceDrawingPath, deletedNames.ToHashSet(StringComparer.Ordinal)));
        }

        if (sheetsWithDeletions.Count == 0)
            return excluded;

        foreach (var (sourceDrawingPath, deletedNames) in sheetsWithDeletions)
        {
            foreach (var (anchorName, mediaTarget) in GetPictureAnchorMediaTargets(sourceArchive, sourceDrawingPath, context.RelNs, context.PackageRelNs))
            {
                if (!deletedNames.Contains(anchorName) || aliveMediaTargets.Contains(mediaTarget))
                    continue;

                excluded.Add(mediaTarget);
                excluded.Add(XlsxPackagePath.GetRelationshipPartPath(mediaTarget));
                foreach (var dependencyPath in GetRelationshipDependencyPaths(sourceArchive, mediaTarget, context.PackageRelNs))
                    excluded.Add(dependencyPath);
            }
        }

        return excluded;
    }

    // Resolved (anchor cNvPr@name, media target path) pairs for every picture anchor's <a:blip r:embed>
    // in the SOURCE drawing part. Mirrors GetDeletedChartPartPaths's own resolution shape but yields
    // every picture anchor (not just tombstoned ones) so GetExcludedDeletedPicturePartPaths above can
    // reuse this single scan both to find deleted anchors' media targets and to know which media
    // targets a SURVIVING anchor still needs.
    private static IEnumerable<(string AnchorName, string MediaTarget)> GetPictureAnchorMediaTargets(
        ZipArchive sourceArchive,
        string sourceDrawingPath,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var drawingEntry = sourceArchive.GetEntry(sourceDrawingPath);
        if (drawingEntry is null)
            yield break;

        var drawingXml = XlsxPackageXmlEditor.LoadXml(drawingEntry);
        if (drawingXml.Root is null)
            yield break;

        XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        XNamespace drawingMlNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        var drawingRels = XlsxRelationshipReader.LoadTargets(
            sourceArchive, XlsxPackagePath.GetRelationshipPartPath(sourceDrawingPath), sourceDrawingPath, packageRelNs);

        foreach (var anchor in drawingXml.Root.Elements())
        {
            var name = anchor.Descendants(spreadsheetDrawingNs + "cNvPr")
                .Select(element => element.Attribute("name")?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (name is null)
                continue;

            foreach (var blip in anchor.Descendants(drawingMlNs + "blip"))
            {
                var relId = blip.Attribute(relNs + "embed")?.Value;
                if (!string.IsNullOrWhiteSpace(relId) && drawingRels.TryGetValue(relId, out var mediaTarget))
                    yield return (name, mediaTarget);
            }
        }
    }

    // R28-io-connections-querytable-deep-1: when a retained sheet's worksheet part is renumbered on
    // save (e.g. an earlier sheet is deleted/reordered so the generated package writes worksheetN.xml
    // under a different N), GetExcludedWorksheetPackagePartPaths above excludes the OLD worksheet-rels
    // part wholesale -- its literal path no longer matches any target sheet's own rels path, so
    // MergeRelationshipParts never even inspects it. Every other per-sheet feature (tables, pivots,
    // drawings, VML, form controls, legacy comments, unsupported-sheet parts) has its own by-name
    // preserver that re-attaches its worksheet relationship(s) at the sheet's NEW path; queryTable /
    // External Data Range never did, so the worksheet -> queryTable relationship (and with it the
    // range's refresh / Data > Connections binding) was silently dropped even though xl/queryTables/*.xml
    // and xl/connections.xml themselves survive untouched (via the generic unknown-part passthrough) as
    // now-orphaned parts. Re-attach the relationship by sheet NAME, the same pattern every sibling
    // preserver above already uses.
    private static void PreserveRenumberedWorksheetQueryTableRelationships(
        ZipArchive sourceArchive,
        ZipArchive generatedArchive,
        XlsxSourcePackagePreservationContext? context)
    {
        if (context is null)
            return;

        foreach (var (sheetName, sourceWorksheetPath) in context.SourceSheets)
        {
            if (!IsWorksheetPartPath(sourceWorksheetPath))
                continue;
            if (!context.TargetSheets.TryGetValue(sheetName, out var targetWorksheetPath))
                continue; // Sheet was removed entirely -- nothing to re-attach.

            var normalizedSourcePath = XlsxPackagePath.NormalizePackagePath(sourceWorksheetPath);
            var normalizedTargetPath = XlsxPackagePath.NormalizePackagePath(targetWorksheetPath);
            if (string.Equals(normalizedSourcePath, normalizedTargetPath, StringComparison.OrdinalIgnoreCase))
                continue; // Unchanged path -- the normal same-path relationship merge already covers it.

            var sourceRelsPath = XlsxPackagePath.GetRelationshipPartPath(normalizedSourcePath);
            var sourceRelsEntry = sourceArchive.GetEntry(sourceRelsPath);
            if (sourceRelsEntry is null)
                continue;

            var sourceRelsXml = XlsxPackageXmlEditor.LoadXml(sourceRelsEntry);
            var queryTableRelationships = sourceRelsXml.Root?
                .Elements(context.PackageRelNs + "Relationship")
                .Where(relationship => string.Equals(
                    relationship.Attribute("Type")?.Value?.Trim(),
                    QueryTableRelationshipType,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (queryTableRelationships is null || queryTableRelationships.Count == 0)
                continue;

            var targetRelsPath = XlsxPackagePath.GetRelationshipPartPath(normalizedTargetPath);
            var targetRelsEntry = generatedArchive.GetEntry(targetRelsPath);
            var targetRelsXml = targetRelsEntry is not null
                ? XlsxPackageXmlEditor.LoadXml(targetRelsEntry)
                : new XDocument(new XElement(context.PackageRelNs + "Relationships"));
            var targetRoot = targetRelsXml.Root;
            if (targetRoot is null)
                continue;

            var existingIds = targetRoot
                .Elements(context.PackageRelNs + "Relationship")
                .Select(element => element.Attribute("Id")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var changed = false;
            foreach (var sourceRelationship in queryTableRelationships)
            {
                var target = sourceRelationship.Attribute("Target")?.Value;
                if (string.IsNullOrWhiteSpace(target))
                    continue;

                // Already present (e.g. this preserver ran more than once, or the target already had
                // its own equivalent relationship) -- avoid adding a duplicate.
                var alreadyPresent = targetRoot
                    .Elements(context.PackageRelNs + "Relationship")
                    .Any(existing =>
                        string.Equals(existing.Attribute("Target")?.Value, target, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(existing.Attribute("Type")?.Value?.Trim(), QueryTableRelationshipType, StringComparison.OrdinalIgnoreCase));
                if (alreadyPresent)
                    continue;

                // Both the old and new worksheet parts live directly under xl/worksheets/, so a
                // relative Target string (e.g. "../queryTables/queryTable1.xml") resolves identically
                // from either path -- copy the relationship verbatim rather than recomputing its Target.
                var copy = new XElement(sourceRelationship);
                var id = copy.Attribute("Id")?.Value;
                if (string.IsNullOrWhiteSpace(id) || existingIds.Contains(id))
                {
                    id = XlsxPackageXmlEditor.NextRelationshipId(targetRelsXml, context.PackageRelNs);
                    copy.SetAttributeValue("Id", id);
                }

                targetRoot.Add(copy);
                existingIds.Add(id!);
                changed = true;
            }

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(generatedArchive, targetRelsPath, targetRelsXml);
        }
    }

    // R77-io-duplicate-sheet-querytable-1: FreeX has no in-model representation of a legacy/classic
    // "Get External Data" queryTable at all -- it survives a save purely via the source-package
    // passthrough above, keyed by matching SHEET NAME between the loaded source package and the
    // freshly generated one. Duplicating a sheet (Home > Sheet > Duplicate Sheet / "Create a copy")
    // gives the copy a brand-new name that never existed in the source package, so that name-keyed
    // matching has nothing to attach a queryTable relationship to -- the copy silently loses its
    // query-table binding even though real Excel duplicates the queryTable part (and its worksheet
    // relationship) for the copied sheet. Since nothing in the model records "this sheet was
    // duplicated from that one", identify the duplication by CONTENT instead: a brand-new target
    // sheet whose cells are identical to a retained source sheet that itself carries a queryTable
    // relationship is treated as a copy of that sheet, and gets its own clone of the queryTable
    // part(s) -- a fresh, distinct part per copy, never a second relationship aimed at the
    // original's part (two sheets sharing one queryTable part would corrupt on independent edits;
    // Excel itself always writes a distinct queryTableN.xml per sheet). The underlying data-source
    // connection (xl/connections.xml, referenced by the queryTable's own connectionId attribute) is
    // deliberately left shared between the original and the clone, matching Excel's own behavior.
    private static void CloneQueryTablesForDuplicatedSheets(
        ZipArchive sourceArchive,
        ZipArchive generatedArchive,
        XlsxSourcePackagePreservationContext? context,
        Workbook workbook)
    {
        if (context is null)
            return;

        // Brand-new target sheet names: present in the generated workbook but absent from the
        // loaded source package entirely (added, or duplicated, during this edit).
        var newSheetNames = context.TargetSheets.Keys
            .Where(name => !context.SourceSheets.ContainsKey(name))
            .ToList();
        if (newSheetNames.Count == 0)
            return;

        foreach (var (candidateName, candidateSourcePath) in context.SourceSheets)
        {
            if (!IsWorksheetPartPath(candidateSourcePath))
                continue;
            if (!context.TargetSheets.ContainsKey(candidateName))
                continue; // The candidate sheet itself was removed -- nothing left to duplicate from.

            var candidateQueryTableRelationships = GetQueryTableRelationships(sourceArchive, candidateSourcePath, context.PackageRelNs);
            if (candidateQueryTableRelationships.Count == 0)
                continue;

            var candidateSheet = workbook.GetSheet(candidateName);
            if (candidateSheet is null)
                continue;

            foreach (var newSheetName in newSheetNames)
            {
                var newWorksheetPath = context.TargetSheets[newSheetName];
                if (!IsWorksheetPartPath(newWorksheetPath))
                    continue;

                var newSheet = workbook.GetSheet(newSheetName);
                if (newSheet is null || !SheetContentsMatch(candidateSheet, newSheet))
                    continue;

                CloneQueryTableRelationshipsOntoSheet(
                    sourceArchive,
                    generatedArchive,
                    candidateSourcePath,
                    newWorksheetPath,
                    candidateQueryTableRelationships,
                    context.PackageRelNs);
            }
        }
    }

    // R118-io-duplicate-sheet-form-control-1: mirrors CloneQueryTablesForDuplicatedSheets above.
    // FreeX's Form Control model IS faithfully cloned in memory onto a duplicated sheet by
    // DuplicateSheetDrawingCloner.CopyDrawingCollections (same ShapeId, remapped Anchor), but the
    // package-level <controls>/<legacyDrawing>/ctrlProps triad that actually makes a legacy Form
    // Control visible/interactive in Excel is written ONLY by
    // XlsxWorksheetFormControlPreserver.Preserve, whose per-sheet loop iterates exclusively over
    // context.SourceSheets -- names/paths present in the ORIGINALLY LOADED package. A sheet created
    // this session via Duplicate Sheet / "Move or Copy... Create a copy" never had an on-disk
    // counterpart at load time, so it is never a key there and the control silently vanishes from the
    // saved package even though the in-memory model still carries it. Identify the duplication by
    // CONTENT (same technique as the queryTable clone above, since nothing in the model records "this
    // sheet was duplicated from that one") and delegate the actual part/relationship cloning to
    // XlsxWorksheetFormControlPreserver.CloneOntoDuplicatedSheet.
    private static void CloneFormControlsForDuplicatedSheets(
        XlsxSourcePackagePreservationContext? context,
        Workbook workbook)
    {
        if (context is null)
            return;

        var newSheetNames = context.TargetSheets.Keys
            .Where(name => !context.SourceSheets.ContainsKey(name))
            .ToList();
        if (newSheetNames.Count == 0)
            return;

        foreach (var (candidateName, candidateSourcePath) in context.SourceSheets)
        {
            if (!IsWorksheetPartPath(candidateSourcePath))
                continue;
            if (!context.TargetSheets.ContainsKey(candidateName))
                continue; // The candidate sheet itself was removed -- nothing left to duplicate from.

            var candidateSheet = workbook.GetSheet(candidateName);
            if (candidateSheet is null || candidateSheet.FormControls.Count == 0)
                continue;

            foreach (var newSheetName in newSheetNames)
            {
                var newWorksheetPath = context.TargetSheets[newSheetName];
                if (!IsWorksheetPartPath(newWorksheetPath))
                    continue;

                var newSheet = workbook.GetSheet(newSheetName);
                if (newSheet is null || newSheet.FormControls.Count == 0 || !SheetContentsMatch(candidateSheet, newSheet))
                    continue;

                XlsxWorksheetFormControlPreserver.CloneOntoDuplicatedSheet(
                    context,
                    candidateSourcePath,
                    newWorksheetPath,
                    newSheet);
            }
        }
    }

    private static List<XElement> GetQueryTableRelationships(ZipArchive sourceArchive, string worksheetPath, XNamespace packageRelNs)
    {
        var relsPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relsEntry = sourceArchive.GetEntry(relsPath);
        if (relsEntry is null)
            return [];

        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
        return relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(relationship => string.Equals(
                relationship.Attribute("Type")?.Value?.Trim(),
                QueryTableRelationshipType,
                StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];
    }

    /// <summary>
    /// Cheap structural-duplicate check used to recognize a freshly duplicated sheet purely from
    /// its content, since Duplicate Sheet leaves no lineage breadcrumb any IO-layer code can read.
    /// Requires an exact cell-for-cell match (value, formula text, and style) between every
    /// populated cell on both sheets, and rejects an empty candidate outright so two blank sheets
    /// are never mistaken for a duplication pair.
    /// </summary>
    private static bool SheetContentsMatch(Sheet candidate, Sheet other)
    {
        var candidateCells = candidate.EnumerateCells().ToList();
        if (candidateCells.Count == 0)
            return false;

        var otherCellsByPosition = other.EnumerateCells()
            .ToDictionary(pair => (pair.Address.Row, pair.Address.Col), pair => pair.Cell);
        if (otherCellsByPosition.Count != candidateCells.Count)
            return false;

        foreach (var (address, cell) in candidateCells)
        {
            if (!otherCellsByPosition.TryGetValue((address.Row, address.Col), out var otherCell))
                return false;
            if (!Equals(cell.Value, otherCell.Value) ||
                !string.Equals(cell.FormulaText, otherCell.FormulaText, StringComparison.Ordinal) ||
                !cell.StyleId.Equals(otherCell.StyleId))
            {
                return false;
            }
        }

        return true;
    }

    private static void CloneQueryTableRelationshipsOntoSheet(
        ZipArchive sourceArchive,
        ZipArchive generatedArchive,
        string candidateSourcePath,
        string newWorksheetPath,
        IReadOnlyList<XElement> candidateQueryTableRelationships,
        XNamespace packageRelNs)
    {
        var newRelsPath = XlsxPackagePath.GetRelationshipPartPath(newWorksheetPath);
        var newRelsEntry = generatedArchive.GetEntry(newRelsPath);
        var newRelsXml = newRelsEntry is not null
            ? XlsxPackageXmlEditor.LoadXml(newRelsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        var newRoot = newRelsXml.Root;
        if (newRoot is null)
            return;

        // Don't re-clone if this sheet somehow already carries a queryTable relationship (e.g. this
        // preserver running more than once over the same save).
        if (newRoot.Elements(packageRelNs + "Relationship")
            .Any(relationship => string.Equals(
                relationship.Attribute("Type")?.Value?.Trim(),
                QueryTableRelationshipType,
                StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var changed = false;
        foreach (var candidateRelationship in candidateQueryTableRelationships)
        {
            var target = candidateRelationship.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target))
                continue;

            var candidatePartPath = XlsxPackagePath.ResolveRelationshipTarget(candidateSourcePath, target);
            var candidatePartEntry = sourceArchive.GetEntry(candidatePartPath);
            if (candidatePartEntry is null)
                continue;

            var clonedPartPath = AllocateClonedQueryTablePartPath(generatedArchive);
            CopyQueryTablePartContent(candidatePartEntry, generatedArchive, clonedPartPath);
            CloneQueryTableContentTypeOverride(sourceArchive, generatedArchive, candidatePartPath, clonedPartPath);

            // xl/queryTables/ is NOT one of the folders XlsxPackagePath.GetRelationshipTarget's
            // whitelist treats as relative-from-xl/worksheets/ (only media/drawings/tables/... are),
            // so that generic helper would wrongly compute a package-absolute-looking target here.
            // Both worksheet parts and queryTable parts live directly under xl/, so the correct
            // worksheet-relative target is always "../queryTables/<file>.xml" -- exactly what
            // PreserveRenumberedWorksheetQueryTableRelationships above already relies on when it
            // copies a same-shape Target string verbatim.
            var clonedFileName = clonedPartPath["xl/queryTables/".Length..];
            var newTarget = "../queryTables/" + clonedFileName;
            var newId = XlsxPackageXmlEditor.NextRelationshipId(newRelsXml, packageRelNs);
            newRoot.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", newId),
                new XAttribute("Type", QueryTableRelationshipType),
                new XAttribute("Target", newTarget)));
            changed = true;
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(generatedArchive, newRelsPath, newRelsXml);
    }

    // Picks the next unused xl/queryTables/queryTableN.xml part name in the GENERATED package
    // (re-scanned on every call so cloning several duplicated sheets in the same save never
    // collides two clones on the same fresh name).
    private static string AllocateClonedQueryTablePartPath(ZipArchive generatedArchive)
    {
        const string prefix = "xl/queryTables/queryTable";
        const string suffix = ".xml";
        var highestExisting = generatedArchive.Entries
            .Select(entry => entry.FullName)
            .Where(name =>
                name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .Select(name => int.TryParse(name[prefix.Length..^suffix.Length], out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}{highestExisting + 1}{suffix}";
    }

    private static void CopyQueryTablePartContent(ZipArchiveEntry sourceEntry, ZipArchive generatedArchive, string targetPartPath)
    {
        generatedArchive.GetEntry(targetPartPath)?.Delete();
        var targetEntry = generatedArchive.CreateEntry(targetPartPath, CompressionLevel.Optimal);
        using var sourceStream = sourceEntry.Open();
        using var targetStream = targetEntry.Open();
        sourceStream.CopyTo(targetStream);
    }

    // Adds a [Content_Types].xml Override for the newly cloned part, reusing whichever ContentType
    // value the original part's own Override carries (checking both the generated package -- where
    // it was already merged in by MergeContentTypes -- and the source package as a fallback), or the
    // standard queryTable content type if neither package happens to carry an explicit Override.
    private static void CloneQueryTableContentTypeOverride(
        ZipArchive sourceArchive,
        ZipArchive generatedArchive,
        string sourcePartPath,
        string clonedPartPath)
    {
        var contentTypesEntry = generatedArchive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            return;

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        var root = contentTypesXml.Root;
        if (root is null)
            return;

        if (root.Elements(contentTypeNs + "Override")
            .Any(element => IsOverrideForPart(element, clonedPartPath)))
        {
            return;
        }

        var contentType =
            FindOverrideContentType(root, contentTypeNs, sourcePartPath) ??
            FindSourceOverrideContentType(sourceArchive, contentTypeNs, sourcePartPath) ??
            QueryTableContentType;

        root.Add(new XElement(
            contentTypeNs + "Override",
            new XAttribute("PartName", "/" + clonedPartPath),
            new XAttribute("ContentType", contentType)));
        XlsxPackageXmlEditor.ReplaceXml(generatedArchive, "[Content_Types].xml", contentTypesXml);
    }

    private static string? FindSourceOverrideContentType(ZipArchive sourceArchive, XNamespace contentTypeNs, string partPath)
    {
        var sourceContentTypesEntry = sourceArchive.GetEntry("[Content_Types].xml");
        if (sourceContentTypesEntry is null)
            return null;

        var sourceRoot = XlsxPackageXmlEditor.LoadXml(sourceContentTypesEntry).Root;
        return sourceRoot is null ? null : FindOverrideContentType(sourceRoot, contentTypeNs, partPath);
    }

    private static string? FindOverrideContentType(XElement root, XNamespace contentTypeNs, string partPath) =>
        root.Elements(contentTypeNs + "Override")
            .FirstOrDefault(element => IsOverrideForPart(element, partPath))
            ?.Attribute("ContentType")?.Value;

    private static bool IsOverrideForPart(XElement overrideElement, string partPath) =>
        string.Equals(
            overrideElement.Attribute("PartName")?.Value?.TrimStart('/'),
            partPath,
            StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> GetLegacyDrawingHfDependencyPaths(
        ZipArchive archive,
        string worksheetPath,
        XNamespace workbookNs,
        XNamespace relNs,
        XNamespace packageRelNs)
    {
        var worksheetEntry = archive.GetEntry(worksheetPath);
        if (worksheetEntry is null)
            yield break;

        var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
        var legacyDrawingRelId = worksheetXml.Root?
            .Element(workbookNs + "legacyDrawingHF")?
            .Attribute(relNs + "id")?
            .Value;

        var relationshipPath = XlsxPackagePath.GetRelationshipPartPath(worksheetPath);
        var relationshipEntry = archive.GetEntry(relationshipPath);
        if (relationshipEntry is null)
            yield break;

        var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipEntry);
        foreach (var relationship in relationshipsXml.Root?.Elements(packageRelNs + "Relationship") ?? [])
        {
            if (!IsLegacyDrawingHfRelationship(relationship, legacyDrawingRelId))
                continue;

            var target = relationship.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target))
                continue;

            var vmlPath = XlsxPackagePath.ResolveRelationshipTarget(worksheetPath, target);
            yield return vmlPath;
            yield return XlsxPackagePath.GetRelationshipPartPath(vmlPath);
            foreach (var dependencyPath in GetRelationshipDependencyPaths(archive, vmlPath, packageRelNs))
                yield return dependencyPath;
        }
    }

    private static bool IsLegacyDrawingHfRelationship(XElement relationship, string? legacyDrawingRelId) =>
        (!string.IsNullOrEmpty(legacyDrawingRelId) &&
         string.Equals(relationship.Attribute("Id")?.Value, legacyDrawingRelId, StringComparison.Ordinal)) ||
        string.Equals(
            relationship.Attribute("Type")?.Value,
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing",
            StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> GetRelationshipDependencyPaths(
        ZipArchive archive,
        string sourcePartPath,
        XNamespace packageRelNs)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        pending.Enqueue(sourcePartPath);

        while (pending.Count > 0)
        {
            var currentPath = pending.Dequeue();
            foreach (var targetPath in GetDirectRelationshipTargets(archive, currentPath, packageRelNs))
            {
                if (!visited.Add(targetPath))
                    continue;

                yield return targetPath;
                var targetRelationshipsPath = XlsxPackagePath.GetRelationshipPartPath(targetPath);
                if (archive.GetEntry(targetRelationshipsPath) is not null)
                {
                    yield return targetRelationshipsPath;
                    pending.Enqueue(targetPath);
                }
            }
        }
    }

    private static IEnumerable<string> GetDirectRelationshipTargets(
        ZipArchive archive,
        string sourcePartPath,
        XNamespace packageRelNs)
    {
        var relationshipPath = XlsxPackagePath.GetRelationshipPartPath(sourcePartPath);
        var relationshipEntry = archive.GetEntry(relationshipPath);
        if (relationshipEntry is null)
            yield break;

        var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipEntry);
        foreach (var relationship in relationshipsXml.Root?.Elements(packageRelNs + "Relationship") ?? [])
        {
            if (string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
                continue;

            var target = relationship.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target))
                continue;

            yield return XlsxPackagePath.ResolveRelationshipTarget(sourcePartPath, target);
        }
    }

    private static bool IsWorksheetPartPath(string path) =>
        path.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static void PreserveSourceChartExParts(
        Workbook workbook,
        ZipArchive sourceArchive,
        ZipArchive generatedArchive,
        IReadOnlySet<string> generatedEntriesBeforeMerge)
    {
        foreach (var chartExPartPath in GetChartExPartPaths(sourceArchive))
        {
            var sourceEntry = sourceArchive.GetEntry(chartExPartPath);
            if (sourceEntry is null)
                continue;

            if (!WorkbookStillContainsSourceChartModel(workbook, sourceEntry))
                continue;

            var generatedEntry = generatedArchive.GetEntry(chartExPartPath);
            if (generatedEntry is not null &&
                !GeneratedChartIsCompatibleWithSourceChartEx(sourceEntry, generatedEntry))
            {
                continue;
            }

            if (generatedEntry is not null)
                CopyChartExWithModeledContent(sourceEntry, generatedEntry, generatedArchive);
            else
            {
                generatedArchive.GetEntry(chartExPartPath)?.Delete();
                XlsxPackageMetadataMerger.CopyEntry(sourceEntry, generatedArchive);
            }

            PreserveSourceChartExStyleColorPackageGraph(sourceArchive, generatedArchive, chartExPartPath);
        }
    }

    private static void PreserveSourceChartExStyleColorPackageGraph(
        ZipArchive sourceArchive,
        ZipArchive generatedArchive,
        string chartExPartPath)
    {
        var sourceRelationshipsPath = XlsxPackagePath.GetRelationshipPartPath(chartExPartPath);
        var sourceRelationshipsEntry = sourceArchive.GetEntry(sourceRelationshipsPath);
        if (sourceRelationshipsEntry is null)
            return;

        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var sourceRelationshipsXml = XlsxPackageXmlEditor.LoadXml(sourceRelationshipsEntry);
        var sourceStyleRelationships = sourceRelationshipsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(IsChartExStyleOrColorStyleRelationship)
            .ToList();
        if (sourceStyleRelationships is null || sourceStyleRelationships.Count == 0)
            return;

        var styleRelationshipCount = 0;
        var colorStyleRelationshipCount = 0;
        var sourceSidecarEntries = new List<ZipArchiveEntry>();
        foreach (var relationship in sourceStyleRelationships)
        {
            var relationshipType = relationship.Attribute("Type")?.Value.Trim();
            var target = relationship.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(target) ||
                relationship.Attribute("TargetMode") is not null)
            {
                return;
            }

            var expectedRootName = XName.Get("chartStyle", "http://schemas.microsoft.com/office/drawing/2012/chartStyle");
            if (string.Equals(relationshipType, ChartExStyleRelationshipType, StringComparison.OrdinalIgnoreCase))
            {
                styleRelationshipCount++;
            }
            else
            {
                colorStyleRelationshipCount++;
                expectedRootName = XName.Get("colorStyle", "http://schemas.microsoft.com/office/drawing/2012/chartStyle");
            }

            var sidecarPath = XlsxPackagePath.ResolveRelationshipTarget(chartExPartPath, target.Trim());
            if (!IsChartExStyleSidecarPath(sidecarPath) ||
                sourceArchive.GetEntry(sidecarPath) is not { } sourceSidecarEntry)
            {
                return;
            }

            var sourceSidecarXml = XlsxPackageXmlEditor.LoadXml(sourceSidecarEntry);
            if (sourceSidecarXml.Root?.Name != expectedRootName)
                return;

            sourceSidecarEntries.Add(sourceSidecarEntry);
        }

        if (styleRelationshipCount != 1 || colorStyleRelationshipCount != 1)
            return;

        foreach (var sourceSidecarEntry in sourceSidecarEntries)
            XlsxPackageMetadataMerger.CopyEntry(sourceSidecarEntry, generatedArchive);

        var generatedRelationshipsPath = XlsxPackagePath.GetRelationshipPartPath(chartExPartPath);
        var generatedRelationshipsXml = generatedArchive.GetEntry(generatedRelationshipsPath) is { } generatedRelationshipsEntry
            ? XlsxPackageXmlEditor.LoadXml(generatedRelationshipsEntry)
            : new XDocument(new XElement(packageRelNs + "Relationships"));
        var generatedRoot = generatedRelationshipsXml.Root;
        if (generatedRoot is null)
            return;

        generatedRoot
            .Elements(packageRelNs + "Relationship")
            .Where(IsChartExStyleOrColorStyleRelationship)
            .Remove();

        var existingIds = generatedRoot
            .Elements(packageRelNs + "Relationship")
            .Select(element => element.Attribute("Id")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceRelationship in sourceStyleRelationships)
        {
            var copy = new XElement(sourceRelationship);
            var id = copy.Attribute("Id")?.Value;
            if (string.IsNullOrWhiteSpace(id) || existingIds.Contains(id))
                copy.SetAttributeValue("Id", XlsxPackageXmlEditor.NextRelationshipId(generatedRelationshipsXml, packageRelNs));

            generatedRoot.Add(copy);
            existingIds.Add(copy.Attribute("Id")!.Value);
        }

        XlsxPackageXmlEditor.ReplaceXml(generatedArchive, generatedRelationshipsPath, generatedRelationshipsXml);
    }

    private static bool IsChartExStyleOrColorStyleRelationship(XElement relationship)
    {
        var type = relationship.Attribute("Type")?.Value.Trim();
        return string.Equals(type, ChartExStyleRelationshipType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, ChartExColorStyleRelationshipType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChartExStyleSidecarPath(string path) =>
        path.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase) &&
        path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
        !path.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);

    private static bool WorkbookStillContainsSourceChartModel(Workbook workbook, ZipArchiveEntry sourceEntry)
    {
        var sheetId = SheetId.New();
        var sourceXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        return XlsxChartPartReader.TryReadSupportedChart(sourceXml, sheetId, out var sourceChart) &&
               workbook.Sheets
                   .SelectMany(sheet => sheet.Charts)
                   .Any(chart => sourceChart.Type == chart.Type);
    }

    private static bool GeneratedChartIsCompatibleWithSourceChartEx(ZipArchiveEntry sourceEntry, ZipArchiveEntry generatedEntry)
    {
        var sheetId = SheetId.New();
        var sourceXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var generatedXml = XlsxPackageXmlEditor.LoadXml(generatedEntry);
        if (!XlsxChartPartReader.TryReadSupportedChart(sourceXml, sheetId, out var sourceChart) ||
            !XlsxChartPartReader.TryReadSupportedChart(generatedXml, sheetId, out var generatedChart))
        {
            return false;
        }

        return sourceChart.Type == generatedChart.Type;
    }

    private static bool ChartModelsMatch(ChartModel sourceChart, ChartModel candidate) =>
        sourceChart.Type == candidate.Type &&
        RangesMatchIgnoringSheet(sourceChart.DataRange, candidate.DataRange) &&
        sourceChart.FirstRowIsHeader == candidate.FirstRowIsHeader &&
        sourceChart.FirstColIsCategories == candidate.FirstColIsCategories &&
        string.Equals(sourceChart.Title ?? "", candidate.Title ?? "", StringComparison.Ordinal);

    private static void CopyChartExWithModeledContent(
        ZipArchiveEntry sourceEntry,
        ZipArchiveEntry generatedEntry,
        ZipArchive generatedArchive)
    {
        XNamespace chartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";
        var sourceXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var generatedXml = XlsxPackageXmlEditor.LoadXml(generatedEntry);
        var sourceChart = sourceXml.Root?.Element(chartExNs + "chart");
        var generatedTitle = generatedXml.Root?.Element(chartExNs + "chart")?.Element(chartExNs + "title");
        var generatedLegend = generatedXml.Root?.Element(chartExNs + "chart")?.Element(chartExNs + "legend");
        var generatedChartData = generatedXml.Root?.Element(chartExNs + "chartData");
        if (sourceChart is not null)
        {
            sourceChart.Element(chartExNs + "title")?.Remove();
            if (generatedTitle is not null)
                sourceChart.AddFirst(new XElement(generatedTitle));

            sourceChart.Element(chartExNs + "legend")?.Remove();
            if (generatedLegend is not null)
            {
                // CT_Chart child order is (title, plotArea, legend, extLst) — the legend must be
                // inserted before any trailing extLst, never blindly appended, or Excel treats the
                // chartEx part as invalid and repairs (discards) the chart on open.
                var sourceExtLst = sourceChart.Element(chartExNs + "extLst");
                if (sourceExtLst is not null)
                    sourceExtLst.AddBeforeSelf(new XElement(generatedLegend));
                else
                    sourceChart.Add(new XElement(generatedLegend));
            }

            MergeChartExSeries(sourceXml, sourceChart, generatedXml, chartExNs);
        }

        var sourceRoot = sourceXml.Root;
        if (generatedChartData is not null && sourceRoot is not null)
            MergeChartExData(sourceRoot, generatedChartData, chartExNs);

        generatedArchive.GetEntry(sourceEntry.FullName)?.Delete();
        XlsxPackageXmlEditor.ReplaceXml(generatedArchive, sourceEntry.FullName, sourceXml);
    }

    // R19-chartex-deep-1: the source cx:series carries content FreeX never models at all (dataPt,
    // dataLabels, spPr, marker, valueColors, axisId, extLst, ...). Wholesale Remove()+Add(generated)
    // silently destroyed all of it on every save of an untouched chart. Merge in place instead:
    // keep the ORIGINAL series element and only refresh the parts FreeX actually generates.
    //
    // R20-meta-3: pairing source<->generated series purely by list POSITION silently misassigns
    // preserved formatting whenever a NON-trailing series is added or removed (every series after
    // the edit point shifts by one slot). Pair by IDENTITY first -- the data range each series'
    // cx:dataId resolves to via cx:chartData's cx:numDim/cx:f formula -- and only fall back to
    // positional pairing for series where identity can't be resolved on both sides (e.g. Pareto's
    // synthetic ownerIdx-based "paretoLine" series, which has no cx:dataId of its own).
    private static void MergeChartExSeries(
        XDocument sourceXml,
        XElement sourceChart,
        XDocument generatedXml,
        XNamespace chartExNs)
    {
        var sourceRegion = sourceChart
            .Element(chartExNs + "plotArea")
            ?.Element(chartExNs + "plotAreaRegion");
        var generatedSeries = generatedXml.Root?
            .Element(chartExNs + "chart")
            ?.Element(chartExNs + "plotArea")
            ?.Element(chartExNs + "plotAreaRegion")
            ?.Elements(chartExNs + "series")
            .Select(element => new XElement(element))
            .ToList();
        if (sourceRegion is null || generatedSeries is null)
            return;

        var sourceSeries = sourceRegion.Elements(chartExNs + "series").ToList();
        var sourceFormulasById = BuildChartExDataFormulaMap(sourceXml, chartExNs);
        var generatedFormulasById = BuildChartExDataFormulaMap(generatedXml, chartExNs);

        var generatedQueuesByIdentity = new Dictionary<string, Queue<XElement>>(StringComparer.Ordinal);
        foreach (var generated in generatedSeries)
        {
            var identity = GetChartExSeriesIdentity(generated, generatedFormulasById, chartExNs);
            if (identity is null)
                continue;
            if (!generatedQueuesByIdentity.TryGetValue(identity, out var queue))
                generatedQueuesByIdentity[identity] = queue = new Queue<XElement>();
            queue.Enqueue(generated);
        }

        var pairs = new List<(XElement Source, XElement Generated)>();
        var claimedGenerated = new HashSet<XElement>();
        var unmatchedSource = new List<XElement>();
        foreach (var source in sourceSeries)
        {
            var identity = GetChartExSeriesIdentity(source, sourceFormulasById, chartExNs);
            if (identity is not null &&
                generatedQueuesByIdentity.TryGetValue(identity, out var queue) &&
                queue.Count > 0)
            {
                var generated = queue.Dequeue();
                pairs.Add((source, generated));
                claimedGenerated.Add(generated);
            }
            else
            {
                unmatchedSource.Add(source);
            }
        }

        // Anything left over (no resolvable identity on one or both sides -- e.g. an untouched
        // chart of a type FreeX doesn't stamp a resolvable dataId formula for) still needs pairing
        // so behavior for those charts matches the pre-R20 positional merge exactly. Pair the
        // leftovers positionally, in original relative order.
        var unmatchedGenerated = generatedSeries.Where(series => !claimedGenerated.Contains(series)).ToList();
        var fallbackCount = Math.Min(unmatchedSource.Count, unmatchedGenerated.Count);
        for (var i = 0; i < fallbackCount; i++)
        {
            pairs.Add((unmatchedSource[i], unmatchedGenerated[i]));
            claimedGenerated.Add(unmatchedGenerated[i]);
        }

        foreach (var (source, generated) in pairs)
            MergeChartExSeriesElement(source, generated, chartExNs);

        // A source series left unpaired describes a series that no longer exists in the generated
        // chart -- drop it. A generated series left unclaimed is brand new -- append it.
        var pairedSource = new HashSet<XElement>(pairs.Select(pair => pair.Source));
        foreach (var source in sourceSeries)
            if (!pairedSource.Contains(source))
                source.Remove();

        foreach (var generated in generatedSeries)
            if (!claimedGenerated.Contains(generated))
                sourceRegion.Add(generated);
    }

    // Identity key for a cx:series: the value-range formula (cx:numDim/cx:f) that its cx:dataId
    // resolves to via cx:chartData. Stable across a non-trailing series add/remove as long as the
    // survivors' own underlying value ranges don't move -- exactly the "remove series 1 via the
    // data-range/series editor" scenario R20-meta-3 flagged (series 2/3 keep referencing their
    // original columns; only the dataId indices get renumbered).
    private static string? GetChartExSeriesIdentity(
        XElement series,
        IReadOnlyDictionary<string, string> dataFormulasById,
        XNamespace chartExNs)
    {
        var dataIdValue = series.Element(chartExNs + "dataId")?.Attribute("val")?.Value;
        if (dataIdValue is not null && dataFormulasById.TryGetValue(dataIdValue, out var formula))
            return formula;

        // Series with no resolvable dataId (e.g. Pareto's synthetic ownerIdx-based "paretoLine"
        // series) carry no data of their own to key off -- fall back to an explicit series-name
        // formula when present, otherwise report "no identity" so the caller pairs positionally.
        return series.Element(chartExNs + "tx")?.Element(chartExNs + "txData")?.Element(chartExNs + "f")?.Value;
    }

    // Maps every cx:chartData/cx:data/@id to the value-range formula its cx:numDim/cx:f carries.
    private static Dictionary<string, string> BuildChartExDataFormulaMap(XDocument doc, XNamespace chartExNs)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var dataElements = doc.Root?.Element(chartExNs + "chartData")?.Elements(chartExNs + "data");
        if (dataElements is null)
            return map;

        foreach (var data in dataElements)
        {
            var id = data.Attribute("id")?.Value;
            var formula = data.Element(chartExNs + "numDim")?.Element(chartExNs + "f")?.Value;
            if (id is not null && formula is not null)
                map[id] = formula;
        }

        return map;
    }

    // Only layoutId/uniqueId (attributes) and tx/dataId/layoutPr (elements) are ever produced by
    // BuildChartExSeries (XlsxChartXmlWriter.ChartEx.cs) -- refresh exactly those parts in place and
    // leave every other attribute/child on the source series element untouched.
    private static void MergeChartExSeriesElement(XElement sourceSeries, XElement generatedSeries, XNamespace chartExNs)
    {
        var layoutId = generatedSeries.Attribute("layoutId")?.Value;
        if (layoutId is not null)
            sourceSeries.SetAttributeValue("layoutId", layoutId);
        // ToChartExSeriesUniqueIdAttribute only emits uniqueId for BoxAndWhisker -- mirror its
        // presence/absence rather than always keeping whatever the source happened to have.
        sourceSeries.SetAttributeValue("uniqueId", generatedSeries.Attribute("uniqueId")?.Value);

        ReplaceChartExSeriesChild(sourceSeries, generatedSeries, chartExNs + "tx", insertBeforeCandidates: null);
        ReplaceChartExSeriesChild(
            sourceSeries,
            generatedSeries,
            chartExNs + "dataId",
            insertBeforeCandidates: [chartExNs + "layoutPr", chartExNs + "axisId", chartExNs + "extLst"]);
        ReplaceChartExSeriesChild(
            sourceSeries,
            generatedSeries,
            chartExNs + "layoutPr",
            insertBeforeCandidates: [chartExNs + "axisId", chartExNs + "extLst"]);
    }

    // CT_Series child order is tx?, spPr?, valueColors?, valueColorPositions?, dataPt*, dataLabels?,
    // dataId, layoutPr?, axisId*, extLst? -- replacing a modeled element in place (or inserting it
    // right before the first following sibling that still exists) keeps that order intact so Excel
    // doesn't treat the part as invalid on open. A null insertBeforeCandidates means "always first"
    // (used for tx, which precedes everything else FreeX can generate).
    private static void ReplaceChartExSeriesChild(
        XElement sourceSeries,
        XElement generatedSeries,
        XName childName,
        XName[]? insertBeforeCandidates)
    {
        var existing = sourceSeries.Element(childName);
        var generatedChild = generatedSeries.Element(childName);
        if (generatedChild is null)
        {
            existing?.Remove();
            return;
        }

        var replacement = new XElement(generatedChild);
        if (existing is not null)
        {
            existing.ReplaceWith(replacement);
            return;
        }

        if (insertBeforeCandidates is null)
        {
            sourceSeries.AddFirst(replacement);
            return;
        }

        foreach (var candidate in insertBeforeCandidates)
        {
            var anchor = sourceSeries.Element(candidate);
            if (anchor is not null)
            {
                anchor.AddBeforeSelf(replacement);
                return;
            }
        }

        sourceSeries.Add(replacement);
    }

    // R19-chartex-deep-2: the source cx:chartData can carry cached point values (cx:pt) or extra
    // hierarchy levels (cx:lvl) inside a numDim/strDim beyond the bare cx:f formula reference FreeX
    // models. Wholesale Remove()+substitute silently destroyed all of it on every save of an
    // untouched chart. Merge positionally (by <cx:data> order) instead, refreshing only the formula
    // reference / header number format each dimension carries.
    private static void MergeChartExData(XElement sourceRoot, XElement generatedChartData, XNamespace chartExNs)
    {
        var sourceChartData = sourceRoot.Element(chartExNs + "chartData");
        if (sourceChartData is null)
        {
            sourceRoot.AddFirst(new XElement(generatedChartData));
            return;
        }

        var sourceDataList = sourceChartData.Elements(chartExNs + "data").ToList();
        var generatedDataList = generatedChartData.Elements(chartExNs + "data")
            .Select(element => new XElement(element))
            .ToList();
        var mergedCount = Math.Min(sourceDataList.Count, generatedDataList.Count);
        for (var i = 0; i < mergedCount; i++)
            MergeChartExDataElement(sourceDataList[i], generatedDataList[i], chartExNs);

        for (var i = sourceDataList.Count - 1; i >= mergedCount; i--)
            sourceDataList[i].Remove();
        if (generatedDataList.Count > mergedCount)
            sourceChartData.Add(generatedDataList.Skip(mergedCount));
    }

    private static void MergeChartExDataElement(XElement sourceData, XElement generatedData, XNamespace chartExNs)
    {
        // cx:data/@id is purely positional (ToChartExDataId) -- always take the generated value.
        var id = generatedData.Attribute("id")?.Value;
        if (id is not null)
            sourceData.SetAttributeValue("id", id);

        MergeChartExDimension(sourceData, generatedData, chartExNs + "strDim", chartExNs);
        MergeChartExDimension(sourceData, generatedData, chartExNs + "numDim", chartExNs);
    }

    // CT_NumericDimension / CT_StringDimension sequence is f?, nf?, lvl*, pt* -- only f/nf are ever
    // generated by BuildChartExData, so lvl (hierarchy levels) and pt (cached point values) the
    // source carried must survive completely untouched.
    private static void MergeChartExDimension(
        XElement sourceData,
        XElement generatedData,
        XName dimensionName,
        XNamespace chartExNs)
    {
        var generatedDim = generatedData.Element(dimensionName);
        if (generatedDim is null)
            return;

        var sourceDim = sourceData.Element(dimensionName);
        if (sourceDim is null)
        {
            sourceData.Add(new XElement(generatedDim));
            return;
        }

        var type = generatedDim.Attribute("type")?.Value;
        if (type is not null)
            sourceDim.SetAttributeValue("type", type);

        var generatedF = generatedDim.Element(chartExNs + "f");
        var existingF = sourceDim.Element(chartExNs + "f");
        if (generatedF is not null)
        {
            var replacementF = new XElement(generatedF);
            if (existingF is not null)
                existingF.ReplaceWith(replacementF);
            else
                sourceDim.AddFirst(replacementF);
        }
        else
        {
            existingF?.Remove();
        }

        var generatedNf = generatedDim.Element(chartExNs + "nf");
        var existingNf = sourceDim.Element(chartExNs + "nf");
        if (generatedNf is not null)
        {
            var replacementNf = new XElement(generatedNf);
            if (existingNf is not null)
            {
                existingNf.ReplaceWith(replacementNf);
            }
            else
            {
                // nf immediately follows f per CT_NumericDimension/CT_StringDimension.
                var anchorF = sourceDim.Element(chartExNs + "f");
                if (anchorF is not null)
                    anchorF.AddAfterSelf(replacementNf);
                else
                    sourceDim.AddFirst(replacementNf);
            }
        }
        else
        {
            existingNf?.Remove();
        }
    }

    private static bool RangesMatchIgnoringSheet(GridRange left, GridRange right) =>
        left.Start.Row == right.Start.Row &&
        left.Start.Col == right.Start.Col &&
        left.End.Row == right.End.Row &&
        left.End.Col == right.End.Col;

    private static IEnumerable<string> GetChartExPartPaths(ZipArchive archive)
    {
        const string chartExContentType = "application/vnd.ms-office.chartex+xml";
        XNamespace contentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            yield break;

        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        foreach (var partName in contentTypesXml.Root?
                     .Elements(contentTypesNs + "Override")
                     .Where(element => string.Equals(element.Attribute("ContentType")?.Value, chartExContentType, StringComparison.OrdinalIgnoreCase))
                     .Select(element => element.Attribute("PartName")?.Value)
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                 ?? [])
        {
            yield return partName!.TrimStart('/');
        }

        foreach (var chartEntry in archive.Entries.Where(entry =>
                     entry.FullName.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase) &&
                     entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
        {
            var chartXml = XlsxPackageXmlEditor.LoadXml(chartEntry);
            if (chartXml.Root?.Name.NamespaceName == "http://schemas.microsoft.com/office/drawing/2014/chartex")
                yield return chartEntry.FullName;
        }
    }

    private static bool HasUnsupportedConditionalFormatting(ZipArchive archive) =>
        XlsxConditionalFormatRuleSupport.HasUnsupportedRuleInWorksheets(archive, allowBlankType: true);
}
