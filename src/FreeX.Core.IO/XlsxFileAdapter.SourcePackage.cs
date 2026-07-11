using System.IO.Compression;
using System.Xml.Linq;

using FreeX.Core.Model;

namespace FreeX.Core.IO;

public sealed partial class XlsxFileAdapter
{
    private const string ChartExStyleRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/chartStyle";
    private const string ChartExColorStyleRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/chartColorStyle";
    private const string QueryTableRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/queryTable";

    // Source package snapshot and native package-part preservation for loaded workbook saves.
    private static SourcePackagePartSummary PreserveSourcePackageParts(Workbook workbook, Stream generatedPackage)
    {
        if (!SourcePackages.TryGetValue(workbook, out var sourcePackage))
            return default;

        using var sourceStream = sourcePackage.OpenRead();
        using var sourceArchive = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false);
        using var generatedArchive = new ZipArchive(generatedPackage, ZipArchiveMode.Update, leaveOpen: true);
        var context = XlsxSourcePackagePreservationContext.TryCreate(sourceArchive, generatedArchive);
        var sourceParts = InspectSourcePackageParts(sourceArchive);
        var removedWorksheetPackageParts = GetExcludedWorksheetPackagePartPaths(sourceArchive, context, workbook);
        var excludedSourceParts = removedWorksheetPackageParts
            .Concat(XlsxWorksheetThreadedCommentMapper.GetSourcePackagePartExclusions(sourceArchive, workbook))
            .Concat(XlsxDigitalSignaturePackagePolicy.GetEditedSaveExclusions(sourceArchive))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var generatedEntriesBeforeMerge = XlsxPackageMetadataMerger.CopyUnknownPackageParts(
            sourceArchive,
            generatedArchive,
            excludedSourceParts);

        XlsxPackageMetadataMerger.MergeContentTypes(sourceArchive, generatedArchive, excludedSourceParts);
        PreserveSourceChartExParts(workbook, sourceArchive, generatedArchive, generatedEntriesBeforeMerge);
        XlsxPackageMetadataMerger.MergeRelationshipParts(
            sourceArchive,
            generatedArchive,
            generatedEntriesBeforeMerge,
            excludedSourceParts);
        XlsxCustomRibbonPackageGraphNormalizer.NormalizePackage(generatedArchive);
        XlsxPackageMetadataMerger.NormalizeCustomXmlPackageGraph(generatedArchive);
        XlsxDocumentPropertiesPreserver.Preserve(sourceArchive, generatedArchive);
        XlsxWorkbookMetadataPreserver.Preserve(sourceArchive, generatedArchive, workbook);
        XlsxStylesheetMetadataPreserver.Preserve(sourceArchive, generatedArchive);
        if (sourceParts.HasPivotPackageParts)
            XlsxPivotXmlReferencePreserver.Preserve(sourceArchive, generatedArchive, context);
        if (sourceParts.HasStructuredTables)
            XlsxStructuredTableReferencePreserver.Preserve(sourceArchive, generatedArchive, context);
        if (sourceParts.HasQueryTables)
            PreserveRenumberedWorksheetQueryTableRelationships(sourceArchive, generatedArchive, context);
        if (sourceParts.HasExternalLinks)
            XlsxExternalLinkReferencePreserver.Preserve(sourceArchive, generatedArchive);
        if (sourceParts.HasUnsupportedSheetParts)
            XlsxUnsupportedSheetReferencePreserver.Preserve(sourceArchive, generatedArchive, context);
        if (sourceParts.HasDrawings)
        {
            var drawingPaths = XlsxWorksheetDrawingPartMerger.MergeAndGetDrawingPaths(sourceArchive, generatedArchive, context);
            XlsxWorksheetDrawingReferencePreserver.Preserve(sourceArchive, generatedArchive, context, drawingPaths);
        }
        if (sourcePackage.WorksheetsWithPreservableSourceMetadata?.Count != 0)
        {
            XlsxWorksheetMetadataPreserver.Preserve(
                sourceArchive,
                generatedArchive,
                workbook,
                context,
                sourcePackage.WorksheetsWithPreservableSourceMetadata);
        }
        XlsxWorksheetPrinterSettingsReferencePreserver.Preserve(sourceArchive, generatedArchive);
        if (sourceParts.HasDrawings)
            XlsxWorksheetVmlReferencePreserver.Preserve(sourceArchive, generatedArchive, context, workbook);
        if (sourceParts.HasFormControls)
            XlsxWorksheetFormControlPreserver.Preserve(sourceArchive, generatedArchive, context, workbook);
        if (sourceParts.HasLegacyComments)
            XlsxLegacyCommentPreserver.Preserve(sourceArchive, generatedArchive, workbook);
        if (sourceParts.HasSharedStrings)
            XlsxSharedStringMetadataPreserver.PreserveRichTextAndPhonetics(sourceArchive, generatedArchive);
        if (sourcePackage.HasUnsupportedConditionalFormatting ?? HasUnsupportedConditionalFormatting(sourceArchive))
            XlsxUnsupportedConditionalFormattingPreserver.Preserve(sourceArchive, generatedArchive);

        XlsxWorksheetSinglePassNormalizer.NormalizeWorksheets(generatedArchive);
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
        Workbook workbook)
    {
        var excludedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (context is null)
            return excludedPaths;

        var sourceWorksheetPaths = context.SourceSheets
            .Select(pair => new
            {
                pair.Key,
                SourcePath = XlsxPackagePath.NormalizePackagePath(pair.Value)
            })
            .Where(pair => IsWorksheetPartPath(pair.SourcePath))
            .ToList();

        foreach (var sourceSheet in sourceWorksheetPaths)
        {
            if (!context.TargetSheets.TryGetValue(sourceSheet.Key, out var targetPath) ||
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
            .Where(pair => !context.TargetSheets.ContainsKey(pair.Key))
            .Select(pair => pair.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (removedWorksheetPaths.Count == 0)
            return excludedPaths;

        // Compute relationship-dependency paths per retained sheet once (O(N) archive reads total).
        // The original code called GetRelationshipDependencyPaths for every (sheet, candidate) pair in the
        // second loop — O(N²) archive reads for N retained sheets. We instead memoize each retained sheet's
        // dep set here and derive the "outside this sheet" predicate from reference-count data.
        var retainedDepsBySheetPath = sourceWorksheetPaths
            .Where(pair => context.TargetSheets.ContainsKey(pair.Key))
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
            if (!context.TargetSheets.ContainsKey(sourceSheet.Key))
                continue;

            var sheet = workbook.GetSheet(sourceSheet.Key);
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
