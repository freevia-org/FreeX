using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal readonly record struct XlsxClosedXmlLoadSanitizationHints(
    bool? HasPivotPackageMetadata,
    bool? HasChartExChartParts,
    bool? HasDrawingPackageParts,
    bool? HasConditionalFormattingBlocks,
    bool? HasUnsupportedConditionalFormattingBlocks,
    bool? HasWorksheetDynamicFilters,
    bool? HasWorksheetGridXmlSchemaIssues,
    bool? HasWorksheetPageLayoutSchemaIssues,
    bool? HasWorksheetPageBreakSchemaIssues,
    bool? HasWorksheetAutoFilterSchemaIssues,
    bool? HasStructuredTableAutoFilterSchemaIssues,
    bool? HasStructuredTableSortStateSchemaIssues,
    bool? HasStructuredTableMetadataSchemaIssues,
    bool? HasDocumentPropertiesPackageGraphIssues,
    bool? HasCustomRibbonPackageGraphIssues,
    bool? HasWorksheetSheetViewSchemaIssues,
    bool? HasWorkbookViewSchemaIssues,
    bool? HasWorkbookCalculationPropertySchemaIssues,
    bool? HasWorkbookFileSharingSchemaIssues,
    bool? HasWorkbookFileRecoveryPropertySchemaIssues,
    bool? HasWorkbookProtectionSchemaIssues,
    bool? HasWorkbookWebPublishingSchemaIssues,
    bool? HasWorkbookSmartTagSchemaIssues,
    bool? HasWorkbookNativeMetadataSchemaIssues,
    bool? HasWorksheetRelationshipMarkerSchemaIssues,
    bool? HasWorksheetNativeMetadataSchemaIssues,
    IReadOnlySet<string>? MergeCellWorksheetPathsToStrip,
    bool? HasCalculationChainPackagePart = null);

internal static class XlsxClosedXmlLoadPackageSanitizer
{
    private const string CalculationChainRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/calcChain";

    // Test-only observation seam (never influences production behavior: the default value is
    // null, so this is a no-op unless a test explicitly sets it). Lets tests capture the transient
    // in-memory copy this method allocates for a non-mutating call, so they can assert it gets
    // disposed when sanitization throws mid-rewrite (R60-missing-dispose-sweep-1). AsyncLocal so
    // the hook only flows within the calling test's own execution context and cannot race with
    // unrelated tests invoking this same method concurrently elsewhere in the assembly.
    internal static readonly AsyncLocal<Action<MemoryStream>?> TransientSanitizedStreamCreatedForTests = new();

    public static MemoryStream Create(MemoryStream sourcePackage) =>
        Create(sourcePackage, removeUnsupportedConditionalFormatting: false);

    public static MemoryStream Create(
        MemoryStream sourcePackage,
        bool removeUnsupportedConditionalFormatting = false,
        bool removeAllConditionalFormatting = false,
        XlsxClosedXmlLoadSanitizationHints? hints = null,
        bool mutateSourcePackage = false)
    {
        sourcePackage.Position = 0;
        var requirements = GetSanitizationRequirements(
            sourcePackage,
            removeUnsupportedConditionalFormatting,
            removeAllConditionalFormatting,
            hints);
        var hasRangeHyperlinks = HasRangeHyperlinkRefs(sourcePackage);
        if (!requirements.RequiresAny && !hasRangeHyperlinks)
        {
            sourcePackage.Position = 0;
            return sourcePackage;
        }

        MemoryStream sanitized;
        if (mutateSourcePackage)
        {
            sanitized = sourcePackage;
        }
        else
        {
            sourcePackage.Position = 0;
            sanitized = new MemoryStream();
            TransientSanitizedStreamCreatedForTests.Value?.Invoke(sanitized);
            if (sourcePackage.TryGetBuffer(out var sourceBuffer) &&
                sourceBuffer.Array is not null &&
                sourcePackage.Length <= int.MaxValue &&
                sourceBuffer.Offset + (int)sourcePackage.Length <= sourceBuffer.Array.Length)
            {
                sanitized.Write(sourceBuffer.Array, sourceBuffer.Offset, (int)sourcePackage.Length);
            }
            else
            {
                sourcePackage.WriteTo(sanitized);
            }
        }

        sanitized.Position = 0;
        ZipArchive archive;
        try
        {
            archive = new ZipArchive(sanitized, ZipArchiveMode.Update, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            // The catch-all below in GetSanitizationRequirements forces every requirement flag to
            // true whenever the source package couldn't even be scanned (e.g. it isn't a valid zip
            // at all — a truncated download or a non-OOXML file renamed to .xlsx). Reopening those
            // same unreadable bytes here for writing is guaranteed to fail identically; surface a
            // clear, actionable error instead of letting a low-level zip exception propagate from a
            // spot the caller's format-error fallback never reaches.
            if (!ReferenceEquals(sanitized, sourcePackage))
                sanitized.Dispose();

            throw new WorkbookInvalidException(
                "The workbook could not be read because the file is not a valid .xlsx package (it may be corrupted, truncated, or not actually an Excel file).");
        }

        try
        {
            using (archive)
            {
                if (hasRangeHyperlinks)
                    StripRangeHyperlinkRefs(archive);
                if (requirements.HasPivotPackageMetadata)
                    XlsxPivotPackageCleaner.RemovePivotPackageMetadata(archive);
                if (requirements.HasChartExChartParts)
                    RemoveChartExDrawingRelationships(archive);
                if (requirements.HasDrawingPackageParts)
                    RemoveDrawingPackageParts(archive);
                if (requirements.HasAllConditionalFormattingBlocks)
                    RemoveAllConditionalFormattingBlocks(archive);
                else if (requirements.HasUnsupportedConditionalFormattingBlocks)
                    RemoveUnsupportedConditionalFormattingBlocks(archive);
                if (requirements.HasWorksheetDynamicFilters)
                    RemoveWorksheetDynamicFilters(archive);
                if (requirements.HasWorksheetGridXmlSchemaIssues)
                    NormalizeWorksheetGridXml(archive);
                if (requirements.HasWorksheetPageLayoutSchemaIssues)
                    NormalizeWorksheetPageLayout(archive);
                if (requirements.HasWorksheetPageBreakSchemaIssues)
                    NormalizeWorksheetPageBreaks(archive);
                if (requirements.HasWorksheetAutoFilterSchemaIssues)
                    NormalizeWorksheetAutoFilters(archive);
                if (requirements.HasStructuredTableAutoFilterSchemaIssues)
                    NormalizeStructuredTableAutoFilters(archive);
                if (requirements.HasStructuredTableSortStateSchemaIssues)
                    NormalizeStructuredTableSortStates(archive);
                if (requirements.HasStructuredTableMetadataSchemaIssues)
                    NormalizeStructuredTableMetadata(archive);
                if (requirements.HasWorksheetSheetViewSchemaIssues)
                    NormalizeWorksheetSheetViews(archive);
                if (requirements.HasWorkbookViewSchemaIssues)
                    NormalizeWorkbookViews(archive);
                if (requirements.HasWorkbookCalculationPropertySchemaIssues)
                    NormalizeWorkbookCalculationProperties(archive);
                if (requirements.HasWorkbookFileSharingSchemaIssues)
                    NormalizeWorkbookFileSharing(archive);
                if (requirements.HasWorkbookFileRecoveryPropertySchemaIssues)
                    NormalizeWorkbookFileRecoveryProperties(archive);
                if (requirements.HasWorkbookProtectionSchemaIssues)
                    NormalizeWorkbookProtection(archive);
                if (requirements.HasWorkbookWebPublishingSchemaIssues)
                    NormalizeWorkbookWebPublishing(archive);
                if (requirements.HasWorkbookSmartTagSchemaIssues)
                    NormalizeWorkbookSmartTags(archive);
                if (requirements.HasWorkbookNativeMetadataSchemaIssues)
                    NormalizeWorkbookNativeMetadata(archive);
                if (requirements.HasWorksheetRelationshipMarkerSchemaIssues)
                    NormalizeWorksheetRelationshipMarkers(archive);
                if (requirements.HasWorksheetNativeMetadataSchemaIssues)
                    NormalizeWorksheetNativeMetadata(archive);
                if (requirements.HasCalculationChainPackagePart)
                    RemoveCalculationChainPackagePart(archive);
                if (requirements.MergeCellWorksheetPathsToStrip is { Count: > 0 } mergeCellWorksheetPaths)
                    RemoveWorksheetMergeCells(archive, mergeCellWorksheetPaths);
                if (requirements.HasDocumentPropertiesPackageGraphIssues)
                    XlsxDocumentPropertiesPreserver.NormalizePackageGraph(archive);
                if (requirements.HasCustomRibbonPackageGraphIssues)
                    XlsxCustomRibbonPackageGraphNormalizer.NormalizePackage(archive);
            }
        }
        catch when (!ReferenceEquals(sanitized, sourcePackage))
        {
            // Mirrors CreateFusedTransientPackage's try/finally in this same file: any exception
            // raised while rewriting the untrusted XML (e.g. an XmlException from a malformed part
            // this sanitizer exists to recover from) must not leak the transient in-memory copy
            // this method allocated. `archive` is already disposed by the `using` above
            // (leaveOpen: true, so `sanitized` itself is untouched by that); only dispose
            // `sanitized` here, and never when it's the caller's own sourcePackage instance
            // (mutateSourcePackage), which this method does not own.
            sanitized.Dispose();
            throw;
        }

        sanitized.Position = 0;
        return sanitized;
    }

    public static MemoryStream Create(
        MemoryStream sourcePackage,
        IReadOnlySet<string>? styleOnlyWorksheetPathsToStrip,
        bool removeUnsupportedConditionalFormatting = false,
        bool removeAllConditionalFormatting = false,
        XlsxClosedXmlLoadSanitizationHints? hints = null)
    {
        var shouldStripStyleOnlyCells = styleOnlyWorksheetPathsToStrip is not { Count: 0 };
        if (!shouldStripStyleOnlyCells)
        {
            return Create(
                sourcePackage,
                removeUnsupportedConditionalFormatting,
                removeAllConditionalFormatting,
                hints);
        }

        sourcePackage.Position = 0;
        var requirements = GetSanitizationRequirements(
            sourcePackage,
            removeUnsupportedConditionalFormatting,
            removeAllConditionalFormatting,
            hints);
        if (!requirements.RequiresAny)
            return XlsxClosedXmlStyleOnlyCellStripper.Create(sourcePackage, styleOnlyWorksheetPathsToStrip);

        MemoryStream? fusedPackage = null;
        try
        {
            fusedPackage = CreateFusedTransientPackage(
                sourcePackage,
                styleOnlyWorksheetPathsToStrip,
                requirements);
            var result = fusedPackage;
            fusedPackage = null;
            return result;
        }
        catch
        {
            fusedPackage?.Dispose();
            var styleOptimizedPackage = XlsxClosedXmlStyleOnlyCellStripper.Create(
                sourcePackage,
                styleOnlyWorksheetPathsToStrip);
            try
            {
                var canMutateStyleOptimizedPackage = !ReferenceEquals(styleOptimizedPackage, sourcePackage);
                var sanitizedPackage = Create(
                    styleOptimizedPackage,
                    removeUnsupportedConditionalFormatting,
                    removeAllConditionalFormatting,
                    hints,
                    mutateSourcePackage: canMutateStyleOptimizedPackage);
                if (!ReferenceEquals(sanitizedPackage, styleOptimizedPackage) &&
                    !ReferenceEquals(styleOptimizedPackage, sourcePackage))
                {
                    styleOptimizedPackage.Dispose();
                }

                return sanitizedPackage;
            }
            catch
            {
                if (!ReferenceEquals(styleOptimizedPackage, sourcePackage))
                    styleOptimizedPackage.Dispose();
                throw;
            }
        }
    }

    private static SanitizationRequirements GetSanitizationRequirements(
        Stream sourcePackage,
        bool scanUnsupportedConditionalFormatting = true,
        bool scanAllConditionalFormatting = false,
        XlsxClosedXmlLoadSanitizationHints? hints = null)
    {
        var knownHints = hints.GetValueOrDefault();
        if (TryCreateSanitizationRequirementsFromHints(
                knownHints,
                scanUnsupportedConditionalFormatting,
                scanAllConditionalFormatting,
                out var knownRequirements))
        {
            return knownRequirements;
        }

        try
        {
            using var archive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
            return new SanitizationRequirements(
                ResolveKnownOrScan(knownHints.HasPivotPackageMetadata, archive, HasPivotPackageMetadata),
                ResolveKnownOrScan(knownHints.HasChartExChartParts, archive, HasChartExChartParts),
                ResolveKnownOrScan(knownHints.HasDrawingPackageParts, archive, HasDrawingPackageParts),
                scanAllConditionalFormatting &&
                ResolveKnownOrScan(knownHints.HasConditionalFormattingBlocks, archive, HasConditionalFormattingBlocks),
                scanUnsupportedConditionalFormatting &&
                ResolveKnownOrScan(knownHints.HasUnsupportedConditionalFormattingBlocks, archive, HasUnsupportedConditionalFormattingBlocks),
                ResolveKnownOrScan(knownHints.HasWorksheetDynamicFilters, archive, HasWorksheetDynamicFilters),
                ResolveKnownOrScan(knownHints.HasWorksheetGridXmlSchemaIssues, archive, HasWorksheetGridXmlSchemaIssues),
                ResolveKnownOrScan(knownHints.HasWorksheetPageLayoutSchemaIssues, archive, HasWorksheetPageLayoutSchemaIssues),
                ResolveKnownOrScan(knownHints.HasWorksheetPageBreakSchemaIssues, archive, HasWorksheetPageBreakSchemaIssues),
                ResolveKnownOrScan(knownHints.HasWorksheetAutoFilterSchemaIssues, archive, HasWorksheetAutoFilterSchemaIssues),
                ResolveKnownOrScan(knownHints.HasStructuredTableAutoFilterSchemaIssues, archive, HasStructuredTableAutoFilterSchemaIssues),
                ResolveKnownOrScan(knownHints.HasStructuredTableSortStateSchemaIssues, archive, HasStructuredTableSortStateSchemaIssues),
                ResolveKnownOrScan(knownHints.HasStructuredTableMetadataSchemaIssues, archive, HasStructuredTableMetadataSchemaIssues),
                ResolveKnownOrScan(knownHints.HasDocumentPropertiesPackageGraphIssues, archive, HasDocumentPropertiesPackageGraphIssues),
                ResolveKnownOrScan(knownHints.HasCustomRibbonPackageGraphIssues, archive, HasCustomRibbonPackageGraphIssues),
                ResolveKnownOrScan(knownHints.HasWorksheetSheetViewSchemaIssues, archive, HasWorksheetSheetViewSchemaIssues),
                ResolveKnownOrScan(knownHints.HasWorkbookViewSchemaIssues, archive, HasWorkbookViewSchemaIssues),
                ResolveKnownOrScan(knownHints.HasWorkbookCalculationPropertySchemaIssues, archive, HasWorkbookCalculationPropertySchemaIssues),
                ResolveKnownOrScan(knownHints.HasWorkbookFileSharingSchemaIssues, archive, HasWorkbookFileSharingSchemaIssues),
                ResolveKnownOrScan(knownHints.HasWorkbookFileRecoveryPropertySchemaIssues, archive, HasWorkbookFileRecoveryPropertySchemaIssues),
                ResolveKnownOrScan(knownHints.HasWorkbookProtectionSchemaIssues, archive, HasWorkbookProtectionSchemaIssues),
                ResolveKnownOrScan(knownHints.HasWorkbookWebPublishingSchemaIssues, archive, HasWorkbookWebPublishingSchemaIssues),
                ResolveKnownOrScan(knownHints.HasWorkbookSmartTagSchemaIssues, archive, HasWorkbookSmartTagSchemaIssues),
                ResolveKnownOrScan(knownHints.HasWorkbookNativeMetadataSchemaIssues, archive, HasWorkbookNativeMetadataSchemaIssues),
                ResolveKnownOrScan(knownHints.HasWorksheetRelationshipMarkerSchemaIssues, archive, HasWorksheetRelationshipMarkerSchemaIssues),
                ResolveKnownOrScan(knownHints.HasWorksheetNativeMetadataSchemaIssues, archive, HasWorksheetNativeMetadataSchemaIssues),
                knownHints.MergeCellWorksheetPathsToStrip,
                ResolveKnownOrScan(knownHints.HasCalculationChainPackagePart, archive, HasCalculationChainPackagePart));
        }
        catch
        {
            return new SanitizationRequirements(true, true, true, scanAllConditionalFormatting, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, true, null, true);
        }
        finally
        {
            if (sourcePackage.CanSeek)
                sourcePackage.Position = 0;
        }
    }

    private static bool TryCreateSanitizationRequirementsFromHints(
        XlsxClosedXmlLoadSanitizationHints hints,
        bool scanUnsupportedConditionalFormatting,
        bool scanAllConditionalFormatting,
        out SanitizationRequirements requirements)
    {
        requirements = default;
        if (hints.HasPivotPackageMetadata is not { } hasPivotPackageMetadata ||
            hints.HasChartExChartParts is not { } hasChartExChartParts ||
            hints.HasDrawingPackageParts is not { } hasDrawingPackageParts ||
            hints.HasWorksheetDynamicFilters is not { } hasWorksheetDynamicFilters ||
            hints.HasWorksheetGridXmlSchemaIssues is not { } hasWorksheetGridXmlSchemaIssues ||
            hints.HasWorksheetPageLayoutSchemaIssues is not { } hasWorksheetPageLayoutSchemaIssues ||
            hints.HasWorksheetPageBreakSchemaIssues is not { } hasWorksheetPageBreakSchemaIssues ||
            hints.HasWorksheetAutoFilterSchemaIssues is not { } hasWorksheetAutoFilterSchemaIssues ||
            hints.HasStructuredTableAutoFilterSchemaIssues is not { } hasStructuredTableAutoFilterSchemaIssues ||
            hints.HasStructuredTableSortStateSchemaIssues is not { } hasStructuredTableSortStateSchemaIssues ||
            hints.HasStructuredTableMetadataSchemaIssues is not { } hasStructuredTableMetadataSchemaIssues ||
            hints.HasDocumentPropertiesPackageGraphIssues is not { } hasDocumentPropertiesPackageGraphIssues ||
            hints.HasCustomRibbonPackageGraphIssues is not { } hasCustomRibbonPackageGraphIssues ||
            hints.HasWorksheetSheetViewSchemaIssues is not { } hasWorksheetSheetViewSchemaIssues ||
            hints.HasWorkbookViewSchemaIssues is not { } hasWorkbookViewSchemaIssues ||
            hints.HasWorkbookCalculationPropertySchemaIssues is not { } hasWorkbookCalculationPropertySchemaIssues ||
            hints.HasWorkbookFileSharingSchemaIssues is not { } hasWorkbookFileSharingSchemaIssues ||
            hints.HasWorkbookFileRecoveryPropertySchemaIssues is not { } hasWorkbookFileRecoveryPropertySchemaIssues ||
            hints.HasWorkbookProtectionSchemaIssues is not { } hasWorkbookProtectionSchemaIssues ||
            hints.HasWorkbookWebPublishingSchemaIssues is not { } hasWorkbookWebPublishingSchemaIssues ||
            hints.HasWorkbookSmartTagSchemaIssues is not { } hasWorkbookSmartTagSchemaIssues ||
            hints.HasWorkbookNativeMetadataSchemaIssues is not { } hasWorkbookNativeMetadataSchemaIssues ||
            hints.HasWorksheetRelationshipMarkerSchemaIssues is not { } hasWorksheetRelationshipMarkerSchemaIssues ||
            hints.HasWorksheetNativeMetadataSchemaIssues is not { } hasWorksheetNativeMetadataSchemaIssues ||
            hints.HasCalculationChainPackagePart is not { } hasCalculationChainPackagePart)
        {
            return false;
        }

        if (scanAllConditionalFormatting &&
            hints.HasConditionalFormattingBlocks is not { })
        {
            return false;
        }

        if (scanUnsupportedConditionalFormatting &&
            hints.HasUnsupportedConditionalFormattingBlocks is not { })
        {
            return false;
        }

        requirements = new SanitizationRequirements(
            hasPivotPackageMetadata,
            hasChartExChartParts,
            hasDrawingPackageParts,
            scanAllConditionalFormatting && hints.HasConditionalFormattingBlocks.GetValueOrDefault(),
            scanUnsupportedConditionalFormatting && hints.HasUnsupportedConditionalFormattingBlocks.GetValueOrDefault(),
            hasWorksheetDynamicFilters,
            hasWorksheetGridXmlSchemaIssues,
            hasWorksheetPageLayoutSchemaIssues,
            hasWorksheetPageBreakSchemaIssues,
            hasWorksheetAutoFilterSchemaIssues,
            hasStructuredTableAutoFilterSchemaIssues,
            hasStructuredTableSortStateSchemaIssues,
            hasStructuredTableMetadataSchemaIssues,
            hasDocumentPropertiesPackageGraphIssues,
            hasCustomRibbonPackageGraphIssues,
            hasWorksheetSheetViewSchemaIssues,
            hasWorkbookViewSchemaIssues,
            hasWorkbookCalculationPropertySchemaIssues,
            hasWorkbookFileSharingSchemaIssues,
            hasWorkbookFileRecoveryPropertySchemaIssues,
            hasWorkbookProtectionSchemaIssues,
            hasWorkbookWebPublishingSchemaIssues,
            hasWorkbookSmartTagSchemaIssues,
            hasWorkbookNativeMetadataSchemaIssues,
            hasWorksheetRelationshipMarkerSchemaIssues,
            hasWorksheetNativeMetadataSchemaIssues,
            hints.MergeCellWorksheetPathsToStrip,
            hasCalculationChainPackagePart);
        return true;
    }

    private static bool ResolveKnownOrScan(
        bool? knownValue,
        ZipArchive archive,
        Func<ZipArchive, bool> scan) =>
        knownValue ?? scan(archive);

    private static bool HasDocumentPropertiesPackageGraphIssues(ZipArchive archive)
    {
        try
        {
            return XlsxDocumentPropertiesPreserver.NeedsPackageGraphNormalization(archive);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasCustomRibbonPackageGraphIssues(ZipArchive archive)
    {
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationshipsEntry = archive.GetEntry("_rels/.rels");
        if (relationshipsEntry is null)
            return false;

        try
        {
            var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
            var root = relationshipsXml.Root;
            if (root is null || root.Name != relationshipNs + "Relationships")
                return false;

            var relationships = root.Elements(relationshipNs + "Relationship").ToList();
            var idCounts = relationships
                .Select(relationship => relationship.Attribute("Id")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .GroupBy(value => value!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
            foreach (var relationship in relationships)
            {
                var isCustomUiRelationship = IsCustomRibbonRootRelationship(relationship);
                var id = relationship.Attribute("Id")?.Value;
                if (!string.IsNullOrWhiteSpace(id) &&
                    idCounts.TryGetValue(id, out var count) &&
                    count > 1 &&
                    isCustomUiRelationship)
                {
                    return true;
                }

                if (isCustomUiRelationship && !CustomRibbonRootRelationshipTargetsExistingPart(archive, relationship))
                    return true;
            }
        }
        catch
        {
            return false;
        }

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            return false;

        try
        {
            var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            foreach (var contentType in contentTypesXml.Root?.Elements(contentTypeNs + "Override") ?? [])
            {
                var partName = contentType.Attribute("PartName")?.Value;
                if (string.IsNullOrWhiteSpace(partName))
                    continue;

                var normalizedPartName = XlsxPackagePath.NormalizePackagePath(partName.Trim());
                if (IsCustomRibbonPart(normalizedPartName) && archive.GetEntry(normalizedPartName) is null)
                    return true;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool IsCustomRibbonRootRelationship(XElement relationship)
    {
        var relationshipType = relationship.Attribute("Type")?.Value?.Trim();
        return string.Equals(
                   relationshipType,
                   "http://schemas.microsoft.com/office/2006/relationships/ui/extensibility",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   relationshipType,
                   "http://schemas.microsoft.com/office/2007/relationships/ui/extensibility",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool CustomRibbonRootRelationshipTargetsExistingPart(ZipArchive archive, XElement relationship)
    {
        var targetMode = relationship.Attribute("TargetMode")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(targetMode) &&
            !string.Equals(targetMode, "Internal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var target = relationship.Attribute("Target")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(target))
            return false;

        var targetPart = XlsxPackagePath.ResolveRelationshipTarget("", target.Replace('\\', '/'));
        return IsCustomRibbonPart(targetPart) && archive.GetEntry(targetPart) is not null;
    }

    private static bool IsCustomRibbonPart(string partName) =>
        partName.StartsWith("customUI/", StringComparison.OrdinalIgnoreCase) &&
        partName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static bool HasRangeHyperlinkRefs(MemoryStream sourcePackage)
    {
        sourcePackage.Position = 0;
        try
        {
            using var archive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true);
            foreach (var entry in archive.Entries)
            {
                if (!XlsxPackagePath.IsWorksheetXmlEntry(entry))
                    continue;

                // Route through the same hardened, char-capped reader every other package part in
                // this codebase uses (DtdProcessing.Prohibit, XmlResolver=null,
                // MaxCharactersInDocument) instead of a raw StreamReader.ReadToEnd(). This method
                // runs unconditionally as the very first step of Create(), on every single .xlsx
                // load's main path. WorkbookOpenSizeGuard only validates the zip central
                // directory's *declared* entry Length/CompressedLength -- fields fully controlled
                // by an attacker -- and never verifies what DeflateStream actually yields when
                // read, so an unbounded ReadToEnd() here let a crafted worksheet part with a small
                // compressed size but a huge real decompressed size (a zip bomb) exhaust memory on
                // every open. A part that hits the cap or fails to parse falls through to the catch
                // below and this scan conservatively reports "no range hyperlink refs" -- matching
                // StripRangeHyperlinkRefs, which applies this exact same cap when it actually
                // rewrites worksheets further down, so this pre-check gate never silently skips a
                // strip that the real rewrite pass would otherwise have performed successfully.
                var worksheetXml = XlsxPackageXmlEditor.LoadXml(entry);
                if (worksheetXml.Root is { } root && XlsxWorksheetHyperlinkNormalizer.ContainsRangeHyperlinkRef(root))
                    return true;
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            sourcePackage.Position = 0;
        }

        return false;
    }

    private static void StripRangeHyperlinkRefs(ZipArchive archive)
    {
        foreach (var entry in archive.Entries.Where(XlsxPackagePath.IsWorksheetXmlEntry).ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(entry);
            if (worksheetXml.Root is { } root && XlsxWorksheetHyperlinkNormalizer.StripRangeHyperlinkRefs(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, entry.FullName, worksheetXml);
        }
    }

    private readonly record struct SanitizationRequirements(
        bool HasPivotPackageMetadata,
        bool HasChartExChartParts,
        bool HasDrawingPackageParts,
        bool HasAllConditionalFormattingBlocks,
        bool HasUnsupportedConditionalFormattingBlocks,
        bool HasWorksheetDynamicFilters,
        bool HasWorksheetGridXmlSchemaIssues,
        bool HasWorksheetPageLayoutSchemaIssues,
        bool HasWorksheetPageBreakSchemaIssues,
        bool HasWorksheetAutoFilterSchemaIssues,
        bool HasStructuredTableAutoFilterSchemaIssues,
        bool HasStructuredTableSortStateSchemaIssues,
        bool HasStructuredTableMetadataSchemaIssues,
        bool HasDocumentPropertiesPackageGraphIssues,
        bool HasCustomRibbonPackageGraphIssues,
        bool HasWorksheetSheetViewSchemaIssues,
        bool HasWorkbookViewSchemaIssues,
        bool HasWorkbookCalculationPropertySchemaIssues,
        bool HasWorkbookFileSharingSchemaIssues,
        bool HasWorkbookFileRecoveryPropertySchemaIssues,
        bool HasWorkbookProtectionSchemaIssues,
        bool HasWorkbookWebPublishingSchemaIssues,
        bool HasWorkbookSmartTagSchemaIssues,
        bool HasWorkbookNativeMetadataSchemaIssues,
        bool HasWorksheetRelationshipMarkerSchemaIssues,
        bool HasWorksheetNativeMetadataSchemaIssues,
        IReadOnlySet<string>? MergeCellWorksheetPathsToStrip,
        bool HasCalculationChainPackagePart)
    {
        public bool RequiresAny =>
            HasPivotPackageMetadata ||
            HasCalculationChainPackagePart ||
            HasChartExChartParts ||
            HasDrawingPackageParts ||
            HasAllConditionalFormattingBlocks ||
            HasUnsupportedConditionalFormattingBlocks ||
            HasWorksheetDynamicFilters ||
            HasWorksheetGridXmlSchemaIssues ||
            HasWorksheetPageLayoutSchemaIssues ||
            HasWorksheetPageBreakSchemaIssues ||
            HasWorksheetAutoFilterSchemaIssues ||
            HasStructuredTableAutoFilterSchemaIssues ||
            HasStructuredTableSortStateSchemaIssues ||
            HasStructuredTableMetadataSchemaIssues ||
            HasDocumentPropertiesPackageGraphIssues ||
            HasCustomRibbonPackageGraphIssues ||
            HasWorksheetSheetViewSchemaIssues ||
            HasWorkbookViewSchemaIssues ||
            HasWorkbookCalculationPropertySchemaIssues ||
            HasWorkbookFileSharingSchemaIssues ||
            HasWorkbookFileRecoveryPropertySchemaIssues ||
            HasWorkbookProtectionSchemaIssues ||
            HasWorkbookWebPublishingSchemaIssues ||
            HasWorkbookSmartTagSchemaIssues ||
            HasWorkbookNativeMetadataSchemaIssues ||
            HasWorksheetRelationshipMarkerSchemaIssues ||
            HasWorksheetNativeMetadataSchemaIssues ||
            MergeCellWorksheetPathsToStrip is { Count: > 0 };
    }

    private static MemoryStream CreateFusedTransientPackage(
        MemoryStream sourcePackage,
        IReadOnlySet<string>? styleOnlyWorksheetPathsToStrip,
        SanitizationRequirements requirements)
    {
        sourcePackage.Position = 0;
        MemoryStream? targetPackage = null;
        ZipArchive? targetArchive = null;
        var returnTargetPackage = false;

        try
        {
            targetPackage = new MemoryStream();
            using (var sourceArchive = new ZipArchive(sourcePackage, ZipArchiveMode.Read, leaveOpen: true))
            {
                targetArchive = new ZipArchive(targetPackage, ZipArchiveMode.Create, leaveOpen: true);
                var removedParts = CollectRemovedPackageParts(sourceArchive, requirements);
                var chartExParts = requirements.HasChartExChartParts
                    ? GetChartExPartNames(sourceArchive)
                    : [];

                foreach (var sourceEntry in sourceArchive.Entries)
                {
                    var normalizedPath = NormalizeEntryPath(sourceEntry.FullName);
                    if (removedParts.Contains(normalizedPath))
                        continue;

                    if (TryWriteFusedEntry(
                            sourceEntry,
                            normalizedPath,
                            targetArchive,
                            styleOnlyWorksheetPathsToStrip,
                            requirements,
                            removedParts,
                            chartExParts))
                    {
                        continue;
                    }

                    CopyEntry(sourceEntry, targetArchive);
                }
            }

            targetArchive?.Dispose();
            targetArchive = null;
            targetPackage.Position = 0;
            if (requirements.HasDocumentPropertiesPackageGraphIssues || requirements.HasCustomRibbonPackageGraphIssues)
            {
                using var archive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);
                if (requirements.HasDocumentPropertiesPackageGraphIssues)
                    XlsxDocumentPropertiesPreserver.NormalizePackageGraph(archive);
                if (requirements.HasCustomRibbonPackageGraphIssues)
                    XlsxCustomRibbonPackageGraphNormalizer.NormalizePackage(archive);
                targetPackage.Position = 0;
            }

            returnTargetPackage = true;
            return targetPackage;
        }
        finally
        {
            targetArchive?.Dispose();
            sourcePackage.Position = 0;
            if (!returnTargetPackage)
                targetPackage?.Dispose();
        }
    }

    private static HashSet<string> CollectRemovedPackageParts(
        ZipArchive sourceArchive,
        SanitizationRequirements requirements)
    {
        var removedParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in sourceArchive.Entries)
        {
            var normalizedPath = NormalizeEntryPath(entry.FullName);
            if (requirements.HasPivotPackageMetadata && IsPivotPackageEntry(normalizedPath) ||
                requirements.HasDrawingPackageParts && IsClosedXmlDrawingPackageEntry(normalizedPath) ||
                requirements.HasCalculationChainPackagePart &&
                string.Equals(normalizedPath, "xl/calcChain.xml", StringComparison.OrdinalIgnoreCase))
            {
                removedParts.Add(normalizedPath);
            }
        }

        return removedParts;
    }

    private static bool TryWriteFusedEntry(
        ZipArchiveEntry sourceEntry,
        string normalizedPath,
        ZipArchive targetArchive,
        IReadOnlySet<string>? styleOnlyWorksheetPathsToStrip,
        SanitizationRequirements requirements,
        IReadOnlySet<string> removedParts,
        IReadOnlySet<string> chartExParts)
    {
        if (IsWorksheetXml(sourceEntry))
        {
            var shouldStripStyleOnlyCells = XlsxClosedXmlStyleOnlyCellStripper.ShouldStripWorksheet(
                sourceEntry,
                styleOnlyWorksheetPathsToStrip);
            if (ShouldTransformWorksheetXml(requirements, normalizedPath))
            {
                return WriteTransformedWorksheetEntry(
                    sourceEntry,
                    normalizedPath,
                    targetArchive,
                    shouldStripStyleOnlyCells,
                    requirements);
            }

            if (shouldStripStyleOnlyCells)
            {
                var targetEntry = CreateTargetEntry(sourceEntry, targetArchive);
                using var targetStream = targetEntry.Open();
                using var sourceStream = sourceEntry.Open();
                XlsxClosedXmlStyleOnlyCellStripper.StripRedundantStyleOnlyCells(sourceStream, targetStream);
                return true;
            }

            return false;
        }

        if (IsChartsheetXml(sourceEntry) &&
            requirements.HasDrawingPackageParts)
        {
            return WriteTransformedChartsheetEntry(sourceEntry, targetArchive);
        }

        if ((requirements.HasStructuredTableAutoFilterSchemaIssues ||
             requirements.HasStructuredTableSortStateSchemaIssues ||
             requirements.HasStructuredTableMetadataSchemaIssues) &&
            IsStructuredTableXml(normalizedPath))
        {
            return WriteTransformedStructuredTableEntry(sourceEntry, targetArchive, requirements);
        }

        if ((requirements.HasPivotPackageMetadata ||
             requirements.HasWorkbookViewSchemaIssues ||
             requirements.HasWorkbookCalculationPropertySchemaIssues ||
             requirements.HasWorkbookFileSharingSchemaIssues ||
             requirements.HasWorkbookFileRecoveryPropertySchemaIssues ||
             requirements.HasWorkbookProtectionSchemaIssues ||
             requirements.HasWorkbookWebPublishingSchemaIssues ||
             requirements.HasWorkbookSmartTagSchemaIssues ||
             requirements.HasWorkbookNativeMetadataSchemaIssues) &&
            string.Equals(normalizedPath, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase))
        {
            WriteTransformedWorkbookEntry(sourceEntry, targetArchive, requirements);
            return true;
        }

        if (removedParts.Count > 0 &&
            string.Equals(normalizedPath, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
        {
            return WriteTransformedContentTypesEntry(sourceEntry, targetArchive, removedParts);
        }

        if (normalizedPath.EndsWith(".rels", StringComparison.OrdinalIgnoreCase) &&
            ShouldTransformRelationshipEntry(normalizedPath, requirements, removedParts, chartExParts))
        {
            return WriteTransformedRelationshipEntry(
                sourceEntry,
                normalizedPath,
                targetArchive,
                requirements,
                removedParts,
                chartExParts);
        }

        return false;
    }

    private static bool ShouldTransformWorksheetXml(SanitizationRequirements requirements, string normalizedPath) =>
        requirements.HasPivotPackageMetadata ||
        requirements.HasDrawingPackageParts ||
        requirements.HasAllConditionalFormattingBlocks ||
        requirements.HasUnsupportedConditionalFormattingBlocks ||
        requirements.HasWorksheetDynamicFilters ||
        requirements.HasWorksheetGridXmlSchemaIssues ||
        requirements.HasWorksheetPageLayoutSchemaIssues ||
        requirements.HasWorksheetPageBreakSchemaIssues ||
        requirements.HasWorksheetAutoFilterSchemaIssues ||
        requirements.HasWorksheetSheetViewSchemaIssues ||
        requirements.HasWorksheetRelationshipMarkerSchemaIssues ||
        requirements.HasWorksheetNativeMetadataSchemaIssues ||
        ShouldStripMergeCells(requirements, normalizedPath);

    private static bool ShouldTransformRelationshipEntry(
        string normalizedPath,
        SanitizationRequirements requirements,
        IReadOnlySet<string> removedParts,
        IReadOnlySet<string> chartExParts)
    {
        if (requirements.HasPivotPackageMetadata)
            return true;

        if (requirements.HasDrawingPackageParts &&
            removedParts.Count > 0 &&
            GetSheetPathFromRelationshipPath(normalizedPath) is not null)
        {
            return true;
        }

        if (requirements.HasCalculationChainPackagePart && removedParts.Count > 0)
            return string.Equals(normalizedPath, "xl/_rels/workbook.xml.rels", StringComparison.OrdinalIgnoreCase);

        return requirements.HasChartExChartParts &&
            chartExParts.Count > 0 &&
            IsDrawingRelationshipEntry(normalizedPath);
    }

    private static void WriteTransformedWorkbookEntry(
        ZipArchiveEntry sourceEntry,
        ZipArchive targetArchive,
        SanitizationRequirements requirements)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        if (workbookXml.Root is { } root)
        {
            if (requirements.HasPivotPackageMetadata)
                root.Elements(workbookNs + "pivotCaches").Remove();
            else
                XlsxWorkbookPivotCachesNormalizer.NormalizeWorkbookRoot(root, workbookNs);
        }
        if (workbookXml.Root?.Element(workbookNs + "workbookPr") is { } workbookPr)
            XlsxWorkbookPropertiesNormalizer.NormalizeElement(workbookPr);
        if (workbookXml.Root?.Element(workbookNs + "fileVersion") is { } fileVersion)
            XlsxWorkbookFileVersionNormalizer.NormalizeElement(fileVersion);
        if (workbookXml.Root?.Element(workbookNs + "functionGroups") is { } functionGroups)
            XlsxWorkbookFunctionGroupsNormalizer.NormalizeElement(functionGroups);
        if (workbookXml.Root?.Element(workbookNs + "smartTagPr") is { } smartTagPr)
            XlsxWorkbookSmartTagNormalizer.NormalizeSmartTagPropertiesElement(smartTagPr);
        if (workbookXml.Root?.Element(workbookNs + "smartTagTypes") is { } smartTagTypes)
        {
            XlsxWorkbookSmartTagNormalizer.NormalizeSmartTagTypesElement(smartTagTypes);
            if (XlsxWorkbookSmartTagNormalizer.ShouldRemoveSmartTagTypesElement(smartTagTypes))
                smartTagTypes.Remove();
        }
        if (workbookXml.Root?.Element(workbookNs + "bookViews") is { } bookViews)
            XlsxWorkbookViewNormalizer.NormalizeBookViewsElement(bookViews);
        if (workbookXml.Root?.Element(workbookNs + "calcPr") is { } calcPr)
            XlsxWorkbookCalculationPropertyNormalizer.NormalizeElement(calcPr);
        if (workbookXml.Root?.Element(workbookNs + "fileSharing") is { } fileSharing)
            XlsxWorkbookFileSharingNormalizer.NormalizeElement(fileSharing);
        foreach (var fileRecoveryPr in workbookXml.Root?.Elements(workbookNs + "fileRecoveryPr") ?? [])
            XlsxWorkbookFileRecoveryPropertyNormalizer.NormalizeElement(fileRecoveryPr);
        if (workbookXml.Root?.Element(workbookNs + "workbookProtection") is { } workbookProtection)
            XlsxWorkbookProtectionNormalizer.NormalizeElement(workbookProtection);
        if (workbookXml.Root is { } workbookRoot)
        {
            foreach (var customWorkbookViews in workbookRoot.Elements(workbookNs + "customWorkbookViews").ToList())
            {
                XlsxWorkbookCustomViewNormalizer.NormalizeCustomWorkbookViewsElement(customWorkbookViews);
                if (XlsxWorkbookCustomViewNormalizer.ShouldRemoveCustomWorkbookViewsElement(customWorkbookViews))
                    customWorkbookViews.Remove();
            }
            XlsxWorkbookExternalReferencesNormalizer.NormalizeWorkbookRoot(workbookRoot, workbookNs);
            foreach (var definedNames in workbookRoot.Elements(workbookNs + "definedNames").ToList())
            {
                XlsxWorkbookDefinedNameNormalizer.NormalizeDefinedNamesElement(definedNames);
                if (XlsxWorkbookDefinedNameNormalizer.ShouldRemoveDefinedNamesElement(definedNames))
                    definedNames.Remove();
            }
            XlsxWorkbookOleSizeNormalizer.NormalizeWorkbookRoot(workbookRoot, workbookNs);
            XlsxWorkbookWebPublishingNormalizer.NormalizeWorkbookRoot(workbookRoot, workbookNs);
            XlsxWorkbookWebPublishObjectsNormalizer.NormalizeWorkbookRoot(workbookRoot, workbookNs);
            XlsxWorkbookExtensionListNormalizer.NormalizeWorkbookRoot(workbookRoot, workbookNs);
        }
        WriteXmlEntry(sourceEntry, targetArchive, workbookXml);
    }

    private static bool WriteTransformedWorksheetEntry(
        ZipArchiveEntry sourceEntry,
        string normalizedPath,
        ZipArchive targetArchive,
        bool stripStyleOnlyCells,
        SanitizationRequirements requirements)
    {
        var worksheetXml = stripStyleOnlyCells
            ? LoadStyleStrippedWorksheetXml(sourceEntry)
            : XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var changed = TransformWorksheetXml(worksheetXml, requirements, normalizedPath);
        if (!stripStyleOnlyCells && !changed)
            return false;

        WriteXmlEntry(sourceEntry, targetArchive, worksheetXml);
        return true;
    }

    private static bool WriteTransformedChartsheetEntry(
        ZipArchiveEntry sourceEntry,
        ZipArchive targetArchive)
    {
        var chartsheetXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var changed = TransformChartsheetXml(chartsheetXml);
        if (!changed)
            return false;

        WriteXmlEntry(sourceEntry, targetArchive, chartsheetXml);
        return true;
    }

    private static bool WriteTransformedStructuredTableEntry(
        ZipArchiveEntry sourceEntry,
        ZipArchive targetArchive,
        SanitizationRequirements requirements)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var tableXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var changed = false;

        if (requirements.HasStructuredTableAutoFilterSchemaIssues &&
            tableXml.Root?.Element(worksheetNs + "autoFilter") is { } autoFilter)
        {
            changed |= XlsxWorksheetAutoFilterNormalizer.NormalizeElement(autoFilter);
        }

        if (requirements.HasStructuredTableSortStateSchemaIssues &&
            tableXml.Root?.Element(worksheetNs + "sortState") is { } sortState)
        {
            changed |= XlsxWorksheetSortStateNormalizer.NormalizeElement(sortState);
        }

        if (requirements.HasStructuredTableMetadataSchemaIssues &&
            tableXml.Root is { } root)
        {
            changed |= XlsxStructuredTableSchemaNormalizer.NormalizeElement(root, sourceEntry.FullName);
        }

        if (!changed)
        {
            return false;
        }

        WriteXmlEntry(sourceEntry, targetArchive, tableXml);
        return true;
    }

    private static XDocument LoadStyleStrippedWorksheetXml(ZipArchiveEntry sourceEntry)
    {
        using var strippedWorksheet = new MemoryStream();
        using (var sourceStream = sourceEntry.Open())
        {
            XlsxClosedXmlStyleOnlyCellStripper.StripRedundantStyleOnlyCells(sourceStream, strippedWorksheet);
        }

        strippedWorksheet.Position = 0;
        return XlsxPackageXmlEditor.LoadXml(strippedWorksheet);
    }

    private static bool TransformWorksheetXml(
        XDocument worksheetXml,
        SanitizationRequirements requirements,
        string normalizedPath)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var root = worksheetXml.Root;
        if (root is null)
            return false;

        var changed = false;

        if (requirements.HasDrawingPackageParts)
            changed |= RemoveElements(root.Elements(worksheetNs + "drawing"));

        if (requirements.HasPivotPackageMetadata)
            changed |= RemoveElements(root.Elements(worksheetNs + "pivotTableDefinition"));

        if (requirements.HasAllConditionalFormattingBlocks)
        {
            changed |= RemoveElements(root.Elements(worksheetNs + "conditionalFormatting"));
        }
        else if (requirements.HasUnsupportedConditionalFormattingBlocks)
        {
            changed |= RemoveElements(root.Elements(worksheetNs + "conditionalFormatting")
                .Where(block => XlsxConditionalFormatRuleSupport.ConditionalFormattingHasUnsupportedRule(block, worksheetNs, allowBlankType: false))
                .ToList());
        }

        if (requirements.HasWorksheetDynamicFilters)
        {
            changed |= RemoveElements(root.Descendants(worksheetNs + "dynamicFilter").ToList());
        }

        if (requirements.HasWorksheetGridXmlSchemaIssues)
        {
            changed |= XlsxWorksheetGridXmlNormalizer.NormalizeWorksheetRoot(root);
        }

        if (requirements.HasWorksheetPageLayoutSchemaIssues)
        {
            changed |= XlsxWorksheetPageLayoutNormalizer.NormalizeWorksheetRoot(root);
        }

        if (requirements.HasWorksheetPageBreakSchemaIssues)
        {
            if (root.Element(worksheetNs + "rowBreaks") is { } rowBreaks)
                changed |= XlsxWorksheetPageBreakNormalizer.NormalizeElement(rowBreaks);
            if (root.Element(worksheetNs + "colBreaks") is { } columnBreaks)
                changed |= XlsxWorksheetPageBreakNormalizer.NormalizeElement(columnBreaks);
        }

        if (requirements.HasWorksheetAutoFilterSchemaIssues &&
            root.Element(worksheetNs + "autoFilter") is { } autoFilter)
        {
            changed |= XlsxWorksheetAutoFilterNormalizer.NormalizeElement(autoFilter);
        }

        if (requirements.HasWorksheetSheetViewSchemaIssues &&
            root.Element(worksheetNs + "sheetViews") is { } sheetViews)
        {
            changed |= XlsxWorksheetSheetViewNormalizer.NormalizeSheetViewsElement(sheetViews);
        }

        if (requirements.HasWorksheetRelationshipMarkerSchemaIssues)
        {
            changed |= XlsxWorksheetRelationshipMarkerNormalizer.NormalizeWorksheetRoot(root);
        }

        if (requirements.HasWorksheetNativeMetadataSchemaIssues)
        {
            changed |= NormalizeWorksheetNativeMetadataRoot(root);
        }

        if (ShouldStripMergeCells(requirements, normalizedPath))
            changed |= RemoveElements(root.Elements(worksheetNs + "mergeCells"));

        return changed;
    }

    private static bool TransformChartsheetXml(XDocument chartsheetXml)
    {
        XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return chartsheetXml.Root is { } root &&
            RemoveElements(root.Elements(sheetNs + "drawing"));
    }

    private static bool ShouldStripMergeCells(
        SanitizationRequirements requirements,
        string normalizedWorksheetPath) =>
        requirements.MergeCellWorksheetPathsToStrip?.Contains(normalizedWorksheetPath) == true;

    private static bool RemoveElements(IEnumerable<XElement> elements)
    {
        var removable = elements as ICollection<XElement> ?? elements.ToList();
        if (removable.Count == 0)
            return false;

        removable.Remove();
        return true;
    }

    private static bool WriteTransformedContentTypesEntry(
        ZipArchiveEntry sourceEntry,
        ZipArchive targetArchive,
        IReadOnlySet<string> removedParts)
    {
        XNamespace contentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var overrides = contentTypesXml.Root?
            .Elements(contentTypesNs + "Override")
            .Where(element =>
                element.Attribute("PartName")?.Value is { Length: > 0 } partName &&
                removedParts.Contains(NormalizePartName(partName)))
            .ToList()
            ?? [];
        if (overrides.Count == 0)
            return false;

        overrides.Remove();
        WriteXmlEntry(sourceEntry, targetArchive, contentTypesXml);
        return true;
    }

    private static bool WriteTransformedRelationshipEntry(
        ZipArchiveEntry sourceEntry,
        string normalizedPath,
        ZipArchive targetArchive,
        SanitizationRequirements requirements,
        IReadOnlySet<string> removedParts,
        IReadOnlySet<string> chartExParts)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var sourcePart = GetPackagePartPathFromRelationshipPath(normalizedPath);
        var relsXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var relationships = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(relationship => ShouldRemoveRelationship(
                relationship,
                normalizedPath,
                sourcePart,
                requirements,
                removedParts,
                chartExParts))
            .ToList()
            ?? [];
        if (relationships.Count == 0)
            return false;

        relationships.Remove();
        WriteXmlEntry(sourceEntry, targetArchive, relsXml);
        return true;
    }

    private static bool ShouldRemoveRelationship(
        XElement relationship,
        string normalizedRelationshipPath,
        string? sourcePart,
        SanitizationRequirements requirements,
        IReadOnlySet<string> removedParts,
        IReadOnlySet<string> chartExParts)
    {
        if (requirements.HasPivotPackageMetadata)
        {
            var type = relationship.Attribute("Type")?.Value ?? "";
            if (type.EndsWith("/pivotCacheDefinition", StringComparison.OrdinalIgnoreCase) ||
                type.EndsWith("/pivotTable", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (sourcePart is null ||
            relationship.Attribute("Target")?.Value is not { Length: > 0 } target)
        {
            return requirements.HasCalculationChainPackagePart &&
                IsCalculationChainRelationship(relationship);
        }

        if (requirements.HasCalculationChainPackagePart &&
            IsCalculationChainRelationship(relationship))
        {
            return true;
        }

        var resolvedTarget = XlsxPackagePath.ResolveRelationshipTarget(sourcePart, target);
        if (requirements.HasCalculationChainPackagePart && removedParts.Contains(resolvedTarget))
            return true;

        if (requirements.HasDrawingPackageParts && removedParts.Contains(resolvedTarget))
            return true;

        if (!requirements.HasChartExChartParts ||
            chartExParts.Count == 0 ||
            !IsDrawingRelationshipEntry(normalizedRelationshipPath))
        {
            return false;
        }

        var typeValue = relationship.Attribute("Type")?.Value ?? "";
        return (typeValue.Equals("http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart", StringComparison.OrdinalIgnoreCase) ||
                typeValue.Equals("http://schemas.microsoft.com/office/2014/relationships/chartEx", StringComparison.OrdinalIgnoreCase)) &&
               chartExParts.Contains(resolvedTarget);
    }

    private static void WriteXmlEntry(
        ZipArchiveEntry sourceEntry,
        ZipArchive targetArchive,
        XDocument document)
    {
        var targetEntry = CreateTargetEntry(sourceEntry, targetArchive);
        using var targetStream = targetEntry.Open();
        document.Save(targetStream, SaveOptions.DisableFormatting);
    }

    private static void CopyEntry(ZipArchiveEntry sourceEntry, ZipArchive targetArchive)
    {
        var targetEntry = CreateTargetEntry(sourceEntry, targetArchive);
        using var targetStream = targetEntry.Open();
        using var sourceStream = sourceEntry.Open();
        sourceStream.CopyTo(targetStream);
    }

    private static ZipArchiveEntry CreateTargetEntry(
        ZipArchiveEntry sourceEntry,
        ZipArchive targetArchive)
    {
        var targetEntry = targetArchive.CreateEntry(sourceEntry.FullName, CompressionLevel.Fastest);
        targetEntry.LastWriteTime = sourceEntry.LastWriteTime;
        return targetEntry;
    }

    private static bool IsPivotPackageEntry(string normalizedPath) =>
        normalizedPath.StartsWith("xl/pivotCache/", StringComparison.OrdinalIgnoreCase) ||
        normalizedPath.StartsWith("xl/pivotTables/", StringComparison.OrdinalIgnoreCase);

    private static bool IsWorksheetXml(ZipArchiveEntry entry) =>
        entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
        entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static bool IsChartsheetXml(ZipArchiveEntry entry) =>
        entry.FullName.StartsWith("xl/chartsheets/", StringComparison.OrdinalIgnoreCase) &&
        entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static bool IsStructuredTableXml(string normalizedPath) =>
        normalizedPath.StartsWith("xl/tables/", StringComparison.OrdinalIgnoreCase) &&
        normalizedPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
        !normalizedPath.Contains("/_rels/", StringComparison.OrdinalIgnoreCase) &&
        !normalizedPath.StartsWith("xl/tables/tableSingleCells", StringComparison.OrdinalIgnoreCase);

    private static bool IsDrawingRelationshipEntry(string normalizedPath) =>
        normalizedPath.StartsWith("xl/drawings/_rels/", StringComparison.OrdinalIgnoreCase) &&
        normalizedPath.EndsWith(".xml.rels", StringComparison.OrdinalIgnoreCase);

    private static string? GetPackagePartPathFromRelationshipPath(string relationshipPath)
    {
        var normalizedPath = NormalizeEntryPath(relationshipPath);
        const string packageRelationshipRoot = "_rels/.rels";
        if (string.Equals(normalizedPath, packageRelationshipRoot, StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        const string marker = "/_rels/";
        const string suffix = ".rels";
        var markerIndex = normalizedPath.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0 ||
            !normalizedPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var directory = normalizedPath[..markerIndex];
        var fileName = normalizedPath[(markerIndex + marker.Length)..^suffix.Length];
        return string.IsNullOrEmpty(directory)
            ? fileName
            : $"{directory}/{fileName}";
    }

    private static bool HasPivotPackageMetadata(ZipArchive archive) =>
        archive.Entries.Any(entry =>
            entry.FullName.StartsWith("xl/pivotCache/", StringComparison.OrdinalIgnoreCase) ||
            entry.FullName.StartsWith("xl/pivotTables/", StringComparison.OrdinalIgnoreCase));

    private static bool HasChartExChartParts(ZipArchive archive) =>
        GetChartExPartNames(archive).Count > 0;

    private static bool HasDrawingPackageParts(ZipArchive archive) =>
        archive.Entries.Any(entry => IsClosedXmlDrawingPackageEntry(entry.FullName));

    private static bool HasCalculationChainPackagePart(ZipArchive archive) =>
        archive.GetEntry("xl/calcChain.xml") is not null;

    private static HashSet<string> GetChartExPartNames(ZipArchive archive)
    {
        const string chartExContentType = "application/vnd.ms-office.chartex+xml";
        XNamespace contentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";

        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            return [];

        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        return contentTypesXml.Root?
            .Elements(contentTypesNs + "Override")
            .Where(element => string.Equals(element.Attribute("ContentType")?.Value, chartExContentType, StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("PartName")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.TrimStart('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];
    }

    private static void RemoveChartExDrawingRelationships(ZipArchive archive)
    {
        var chartExParts = GetChartExPartNames(archive);
        if (chartExParts.Count == 0)
            return;

        const string chartRelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart";
        const string chartExRelationshipType = "http://schemas.microsoft.com/office/2014/relationships/chartEx";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        foreach (var relsEntry in archive.Entries
                     .Where(entry =>
                         entry.FullName.StartsWith("xl/drawings/_rels/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml.rels", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            var drawingPath = "xl/drawings/" + relsEntry.FullName["xl/drawings/_rels/".Length..^".rels".Length];
            var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
            var chartExRelationships = relsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .Where(element =>
                    (string.Equals(element.Attribute("Type")?.Value, chartRelationshipType, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(element.Attribute("Type")?.Value, chartExRelationshipType, StringComparison.OrdinalIgnoreCase)) &&
                    element.Attribute("Target")?.Value is { Length: > 0 } target &&
                    chartExParts.Contains(XlsxPackagePath.ResolveRelationshipTarget(drawingPath, target)))
                .ToList()
                ?? [];

            if (chartExRelationships.Count == 0)
                continue;

            chartExRelationships.Remove();
            XlsxPackageXmlEditor.ReplaceXml(archive, relsEntry.FullName, relsXml);
        }
    }

    private static void RemoveDrawingPackageParts(ZipArchive archive)
    {
        var removedParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries
                     .Where(entry => IsClosedXmlDrawingPackageEntry(entry.FullName))
                     .ToList())
        {
            removedParts.Add(NormalizeEntryPath(entry.FullName));
            entry.Delete();
        }

        if (removedParts.Count == 0)
            return;

        RemoveSheetDrawingReferences(archive);
        RemoveSheetDrawingRelationships(archive, removedParts);
        RemoveContentTypeOverrides(archive, removedParts);
    }

    private static void RemoveCalculationChainPackagePart(ZipArchive archive)
    {
        var calcChainEntry = archive.GetEntry("xl/calcChain.xml");
        if (calcChainEntry is null)
            return;

        calcChainEntry.Delete();
        var removedParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "xl/calcChain.xml"
        };
        RemoveWorkbookCalculationChainRelationship(archive, removedParts);
        RemoveContentTypeOverrides(archive, removedParts);
    }

    private static void RemoveWorkbookCalculationChainRelationship(
        ZipArchive archive,
        IReadOnlySet<string> removedParts)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
        if (relsEntry is null)
            return;

        var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
        var relationships = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                IsCalculationChainRelationship(relationship) ||
                relationship.Attribute("Target")?.Value is { Length: > 0 } target &&
                removedParts.Contains(XlsxPackagePath.ResolveRelationshipTarget("xl/workbook.xml", target)))
            .ToList()
            ?? [];
        if (relationships.Count == 0)
            return;

        relationships.Remove();
        XlsxPackageXmlEditor.ReplaceXml(archive, relsEntry.FullName, relsXml);
    }

    private static bool IsClosedXmlDrawingPackageEntry(string path)
    {
        var normalized = NormalizeEntryPath(path);
        return IsModernDrawingEntry(normalized) ||
            IsModernDrawingRelationshipEntry(normalized) ||
            normalized.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsModernDrawingEntry(string normalizedPath) =>
        normalizedPath.StartsWith("xl/drawings/drawing", StringComparison.OrdinalIgnoreCase) &&
        normalizedPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static bool IsModernDrawingRelationshipEntry(string normalizedPath) =>
        normalizedPath.StartsWith("xl/drawings/_rels/drawing", StringComparison.OrdinalIgnoreCase) &&
        normalizedPath.EndsWith(".xml.rels", StringComparison.OrdinalIgnoreCase);

    private static void RemoveSheetDrawingReferences(ZipArchive archive)
    {
        XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var sheetEntry in archive.Entries
                     .Where(entry => IsWorksheetXml(entry) || IsChartsheetXml(entry))
                     .ToList())
        {
            var sheetXml = XlsxPackageXmlEditor.LoadXml(sheetEntry);
            var root = sheetXml.Root;
            if (root is null)
                continue;

            var drawingReferences = root
                .Elements()
                .Where(element => element.Name == sheetNs + "drawing")
                .ToList();
            if (drawingReferences.Count == 0)
                continue;

            drawingReferences.Remove();
            XlsxPackageXmlEditor.ReplaceXml(archive, sheetEntry.FullName, sheetXml);
        }
    }

    private static void RemoveSheetDrawingRelationships(
        ZipArchive archive,
        IReadOnlySet<string> removedParts)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        foreach (var relsEntry in archive.Entries
                     .Where(entry => GetSheetPathFromRelationshipPath(entry.FullName) is not null)
                     .ToList())
        {
            var sheetPath = GetSheetPathFromRelationshipPath(relsEntry.FullName);
            if (sheetPath is null)
                continue;

            var relsXml = XlsxPackageXmlEditor.LoadXml(relsEntry);
            var relationships = relsXml.Root?
                .Elements(packageRelNs + "Relationship")
                .Where(relationship =>
                    relationship.Attribute("Target")?.Value is { Length: > 0 } target &&
                    removedParts.Contains(XlsxPackagePath.ResolveRelationshipTarget(sheetPath, target)))
                .ToList()
                ?? [];
            if (relationships.Count == 0)
                continue;

            relationships.Remove();
            XlsxPackageXmlEditor.ReplaceXml(archive, relsEntry.FullName, relsXml);
        }
    }

    private static string? GetSheetPathFromRelationshipPath(string relsPath)
    {
        var sourcePart = GetPackagePartPathFromRelationshipPath(relsPath);
        if (sourcePart is null)
            return null;

        return IsWorksheetPath(sourcePart) || IsChartsheetPath(sourcePart)
            ? sourcePart
            : null;
    }

    private static bool IsWorksheetPath(string normalizedPath) =>
        normalizedPath.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
        normalizedPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static bool IsChartsheetPath(string normalizedPath) =>
        normalizedPath.StartsWith("xl/chartsheets/", StringComparison.OrdinalIgnoreCase) &&
        normalizedPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static void RemoveContentTypeOverrides(
        ZipArchive archive,
        IReadOnlySet<string> removedParts)
    {
        XNamespace contentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            return;

        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        var overrides = contentTypesXml.Root?
            .Elements(contentTypesNs + "Override")
            .Where(element =>
                element.Attribute("PartName")?.Value is { Length: > 0 } partName &&
                removedParts.Contains(NormalizePartName(partName)))
            .ToList()
            ?? [];
        if (overrides.Count == 0)
            return;

        overrides.Remove();
        XlsxPackageXmlEditor.ReplaceXml(archive, "[Content_Types].xml", contentTypesXml);
    }

    private static string NormalizePartName(string partName) =>
        XlsxPackagePath.NormalizePackagePath(partName.Trim());

    private static bool IsCalculationChainRelationship(XElement relationship) =>
        string.Equals(
            relationship.Attribute("Type")?.Value,
            CalculationChainRelationshipType,
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeEntryPath(string path) =>
        XlsxPackagePath.NormalizePackagePath(path);

    private static bool HasUnsupportedConditionalFormattingBlocks(ZipArchive archive) =>
        XlsxConditionalFormatRuleSupport.HasUnsupportedRuleInWorksheets(archive, allowBlankType: false);

    private static bool HasConditionalFormattingBlocks(ZipArchive archive) =>
        archive.Entries
            .Where(entry =>
                entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Any(entry =>
            {
                using var stream = entry.Open();
                using var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = true,
                    IgnoreWhitespace = true,
                });

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element &&
                        string.Equals(reader.LocalName, "conditionalFormatting", StringComparison.Ordinal) &&
                        string.Equals(reader.NamespaceURI, "http://schemas.openxmlformats.org/spreadsheetml/2006/main", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            });

    private static bool HasWorksheetDynamicFilters(ZipArchive archive) =>
        archive.Entries
            .Where(entry =>
                entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Any(entry =>
            {
                using var stream = entry.Open();
                using var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    IgnoreComments = true,
                    IgnoreProcessingInstructions = true,
                    IgnoreWhitespace = true,
                });

                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element &&
                        string.Equals(reader.LocalName, "dynamicFilter", StringComparison.Ordinal) &&
                        string.Equals(reader.NamespaceURI, "http://schemas.openxmlformats.org/spreadsheetml/2006/main", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            });

    private static bool HasWorksheetSheetViewSchemaIssues(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries.Where(IsWorksheetXml))
        {
            try
            {
                var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
                var sheetViews = worksheetXml.Root?.Element(XName.Get(
                    "sheetViews",
                    "http://schemas.openxmlformats.org/spreadsheetml/2006/main"));
                if (sheetViews is not null &&
                    XlsxWorksheetSheetViewNormalizer.NormalizeSheetViewsElement(sheetViews))
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
        }

        return false;
    }

    private static void NormalizeWorksheetSheetViews(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var worksheetEntry in archive.Entries
                     .Where(IsWorksheetXml)
                     .ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var sheetViews = worksheetXml.Root?.Element(worksheetNs + "sheetViews");
            if (sheetViews is not null &&
                XlsxWorksheetSheetViewNormalizer.NormalizeSheetViewsElement(sheetViews))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
            }
        }
    }

    private static bool HasWorkbookViewSchemaIssues(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return false;

        try
        {
            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var bookViews = workbookXml.Root?.Element(XName.Get(
                "bookViews",
                "http://schemas.openxmlformats.org/spreadsheetml/2006/main"));
            return bookViews is not null &&
                   XlsxWorkbookViewNormalizer.NormalizeBookViewsElement(bookViews);
        }
        catch
        {
            return true;
        }
    }

    private static void NormalizeWorkbookViews(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var bookViews = workbookXml.Root?.Element(workbookNs + "bookViews");
        if (bookViews is not null &&
            XlsxWorkbookViewNormalizer.NormalizeBookViewsElement(bookViews))
        {
            XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
        }
    }

    private static bool HasWorkbookCalculationPropertySchemaIssues(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return false;

        try
        {
            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var calcPr = workbookXml.Root?.Element(XName.Get(
                "calcPr",
                "http://schemas.openxmlformats.org/spreadsheetml/2006/main"));
            return calcPr is not null &&
                   XlsxWorkbookCalculationPropertyNormalizer.NormalizeElement(calcPr);
        }
        catch
        {
            return true;
        }
    }

    private static void NormalizeWorkbookCalculationProperties(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var calcPr = workbookXml.Root?.Element(workbookNs + "calcPr");
        if (calcPr is not null &&
            XlsxWorkbookCalculationPropertyNormalizer.NormalizeElement(calcPr))
        {
            XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
        }
    }

    private static bool HasWorkbookFileSharingSchemaIssues(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return false;

        try
        {
            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var fileSharing = workbookXml.Root?.Element(XName.Get(
                "fileSharing",
                "http://schemas.openxmlformats.org/spreadsheetml/2006/main"));
            return fileSharing is not null &&
                   XlsxWorkbookFileSharingNormalizer.NormalizeElement(fileSharing);
        }
        catch
        {
            return true;
        }
    }

    private static void NormalizeWorkbookFileSharing(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var fileSharing = workbookXml.Root?.Element(workbookNs + "fileSharing");
        if (fileSharing is not null &&
            XlsxWorkbookFileSharingNormalizer.NormalizeElement(fileSharing))
        {
            XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
        }
    }

    private static bool HasWorkbookFileRecoveryPropertySchemaIssues(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return false;

        try
        {
            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var changed = false;
            foreach (var fileRecoveryPr in workbookXml.Root?.Elements(XName.Get(
                         "fileRecoveryPr",
                         "http://schemas.openxmlformats.org/spreadsheetml/2006/main")) ?? [])
            {
                changed |= XlsxWorkbookFileRecoveryPropertyNormalizer.NormalizeElement(fileRecoveryPr);
            }

            return changed;
        }
        catch
        {
            return true;
        }
    }

    private static void NormalizeWorkbookFileRecoveryProperties(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var changed = false;
        foreach (var fileRecoveryPr in workbookXml.Root?.Elements(workbookNs + "fileRecoveryPr") ?? [])
            changed |= XlsxWorkbookFileRecoveryPropertyNormalizer.NormalizeElement(fileRecoveryPr);

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
    }

    private static bool HasWorkbookProtectionSchemaIssues(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return false;

        try
        {
            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var workbookProtection = workbookXml.Root?.Element(XName.Get(
                "workbookProtection",
                "http://schemas.openxmlformats.org/spreadsheetml/2006/main"));
            return workbookProtection is not null &&
                   XlsxWorkbookProtectionNormalizer.NormalizeElement(workbookProtection);
        }
        catch
        {
            return true;
        }
    }

    private static void NormalizeWorkbookProtection(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var workbookProtection = workbookXml.Root?.Element(workbookNs + "workbookProtection");
        if (workbookProtection is not null &&
            XlsxWorkbookProtectionNormalizer.NormalizeElement(workbookProtection))
        {
            XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
        }
    }

    private static bool HasWorkbookWebPublishingSchemaIssues(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return false;

        try
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            var root = workbookXml.Root;
            return root is not null &&
                   (XlsxWorkbookWebPublishingNormalizer.NormalizeWorkbookRoot(root, workbookNs) |
                    XlsxWorkbookWebPublishObjectsNormalizer.NormalizeWorkbookRoot(root, workbookNs));
        }
        catch
        {
            return true;
        }
    }

    private static void NormalizeWorkbookWebPublishing(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        var root = workbookXml.Root;
        if (root is null)
            return;

        var changed =
            XlsxWorkbookWebPublishingNormalizer.NormalizeWorkbookRoot(root, workbookNs) |
            XlsxWorkbookWebPublishObjectsNormalizer.NormalizeWorkbookRoot(root, workbookNs);

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
    }

    private static bool HasWorkbookSmartTagSchemaIssues(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return false;

        try
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            return WorkbookSmartTagNormalizationWouldChange(workbookXml.Root, workbookNs);
        }
        catch
        {
            return true;
        }
    }

    private static void NormalizeWorkbookSmartTags(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        if (NormalizeWorkbookSmartTagsRoot(workbookXml.Root, workbookNs))
            XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
    }

    private static bool WorkbookSmartTagNormalizationWouldChange(XElement? root, XNamespace workbookNs)
    {
        if (root is null)
            return false;

        return NormalizeWorkbookSmartTagsRoot(new XElement(root), workbookNs);
    }

    private static bool NormalizeWorkbookSmartTagsRoot(XElement? root, XNamespace workbookNs)
    {
        if (root is null)
            return false;

        var changed = false;
        if (root.Element(workbookNs + "smartTagPr") is { } smartTagPr)
            changed |= XlsxWorkbookSmartTagNormalizer.NormalizeSmartTagPropertiesElement(smartTagPr);
        if (root.Element(workbookNs + "smartTagTypes") is { } smartTagTypes)
        {
            changed |= XlsxWorkbookSmartTagNormalizer.NormalizeSmartTagTypesElement(smartTagTypes);
            if (XlsxWorkbookSmartTagNormalizer.ShouldRemoveSmartTagTypesElement(smartTagTypes))
            {
                smartTagTypes.Remove();
                changed = true;
            }
        }

        return changed;
    }

    private static bool HasWorkbookNativeMetadataSchemaIssues(ZipArchive archive)
    {
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return false;

        try
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            return WorkbookNativeMetadataNormalizationWouldChange(workbookXml.Root, workbookNs);
        }
        catch
        {
            return true;
        }
    }

    private static void NormalizeWorkbookNativeMetadata(ZipArchive archive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookEntry = archive.GetEntry("xl/workbook.xml");
        if (workbookEntry is null)
            return;

        var workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
        if (NormalizeWorkbookNativeMetadataRoot(workbookXml.Root, workbookNs))
            XlsxPackageXmlEditor.ReplaceXml(archive, workbookEntry.FullName, workbookXml);
    }

    private static bool WorkbookNativeMetadataNormalizationWouldChange(XElement? root, XNamespace workbookNs)
    {
        if (root is null)
            return false;

        return NormalizeWorkbookNativeMetadataRoot(new XElement(root), workbookNs);
    }

    private static bool NormalizeWorkbookNativeMetadataRoot(XElement? root, XNamespace workbookNs)
    {
        if (root is null)
            return false;

        var changed = false;
        if (root.Element(workbookNs + "workbookPr") is { } workbookPr)
            changed |= XlsxWorkbookPropertiesNormalizer.NormalizeElement(workbookPr);
        foreach (var customWorkbookViews in root.Elements(workbookNs + "customWorkbookViews").ToList())
        {
            changed |= XlsxWorkbookCustomViewNormalizer.NormalizeCustomWorkbookViewsElement(customWorkbookViews);
            if (XlsxWorkbookCustomViewNormalizer.ShouldRemoveCustomWorkbookViewsElement(customWorkbookViews))
            {
                customWorkbookViews.Remove();
                changed = true;
            }
        }
        changed |= XlsxWorkbookExternalReferencesNormalizer.NormalizeWorkbookRoot(root, workbookNs);
        foreach (var definedNames in root.Elements(workbookNs + "definedNames").ToList())
        {
            changed |= XlsxWorkbookDefinedNameNormalizer.NormalizeDefinedNamesElement(definedNames);
            if (XlsxWorkbookDefinedNameNormalizer.ShouldRemoveDefinedNamesElement(definedNames))
            {
                definedNames.Remove();
                changed = true;
            }
        }
        changed |= XlsxWorkbookOleSizeNormalizer.NormalizeWorkbookRoot(root, workbookNs);
        changed |= XlsxWorkbookPivotCachesNormalizer.NormalizeWorkbookRoot(root, workbookNs);
        changed |= XlsxWorkbookExtensionListNormalizer.NormalizeWorkbookRoot(root, workbookNs);
        if (root.Element(workbookNs + "fileVersion") is { } fileVersion)
            changed |= XlsxWorkbookFileVersionNormalizer.NormalizeElement(fileVersion);
        if (root.Element(workbookNs + "functionGroups") is { } functionGroups)
            changed |= XlsxWorkbookFunctionGroupsNormalizer.NormalizeElement(functionGroups);

        return changed;
    }

    private static bool HasWorksheetPageLayoutSchemaIssues(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries
                     .Where(XlsxConditionalFormatRuleSupport.IsWorksheetEntry)
                     .ToList())
        {
            try
            {
                var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
                var root = worksheetXml.Root;
                if (root is not null &&
                    XlsxWorksheetPageLayoutNormalizer.NormalizeWorksheetRoot(root))
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
        }

        return false;
    }

    private static void NormalizeWorksheetPageLayout(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries
                     .Where(XlsxConditionalFormatRuleSupport.IsWorksheetEntry)
                     .ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            if (XlsxWorksheetPageLayoutNormalizer.NormalizeWorksheetRoot(root))
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    private static bool HasWorksheetPageBreakSchemaIssues(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries
                     .Where(XlsxConditionalFormatRuleSupport.IsWorksheetEntry)
                     .ToList())
        {
            try
            {
                var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
                var root = worksheetXml.Root;
                if (root is null)
                    continue;

                if (root.Element(XName.Get("rowBreaks", "http://schemas.openxmlformats.org/spreadsheetml/2006/main")) is { } rowBreaks &&
                    XlsxWorksheetPageBreakNormalizer.NormalizeElement(rowBreaks))
                {
                    return true;
                }

                if (root.Element(XName.Get("colBreaks", "http://schemas.openxmlformats.org/spreadsheetml/2006/main")) is { } columnBreaks &&
                    XlsxWorksheetPageBreakNormalizer.NormalizeElement(columnBreaks))
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
        }

        return false;
    }

    private static void NormalizeWorksheetPageBreaks(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var worksheetEntry in archive.Entries
                     .Where(XlsxConditionalFormatRuleSupport.IsWorksheetEntry)
                     .ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var changed = false;
            if (root.Element(worksheetNs + "rowBreaks") is { } rowBreaks)
                changed |= XlsxWorksheetPageBreakNormalizer.NormalizeElement(rowBreaks);
            if (root.Element(worksheetNs + "colBreaks") is { } columnBreaks)
                changed |= XlsxWorksheetPageBreakNormalizer.NormalizeElement(columnBreaks);

            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    private static bool HasWorksheetAutoFilterSchemaIssues(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries
                     .Where(XlsxConditionalFormatRuleSupport.IsWorksheetEntry)
                     .ToList())
        {
            try
            {
                var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
                var autoFilter = worksheetXml.Root?.Element(XName.Get(
                    "autoFilter",
                    "http://schemas.openxmlformats.org/spreadsheetml/2006/main"));
                if (autoFilter is not null &&
                    XlsxWorksheetAutoFilterNormalizer.NormalizeElement(autoFilter))
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
        }

        return false;
    }

    private static void NormalizeWorksheetAutoFilters(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var worksheetEntry in archive.Entries
                     .Where(XlsxConditionalFormatRuleSupport.IsWorksheetEntry)
                     .ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var autoFilter = root.Element(worksheetNs + "autoFilter");
            if (autoFilter is not null &&
                XlsxWorksheetAutoFilterNormalizer.NormalizeElement(autoFilter))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
            }
        }
    }

    private static bool HasStructuredTableAutoFilterSchemaIssues(ZipArchive archive)
    {
        foreach (var tableEntry in archive.Entries
                     .Where(entry => IsStructuredTableXml(NormalizeEntryPath(entry.FullName)))
                     .ToList())
        {
            try
            {
                var tableXml = XlsxPackageXmlEditor.LoadXml(tableEntry);
                var autoFilter = tableXml.Root?.Element(XName.Get(
                    "autoFilter",
                    "http://schemas.openxmlformats.org/spreadsheetml/2006/main"));
                if (autoFilter is not null &&
                    XlsxWorksheetAutoFilterNormalizer.NormalizeElement(autoFilter))
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
        }

        return false;
    }

    private static void NormalizeStructuredTableAutoFilters(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var tableEntry in archive.Entries
                     .Where(entry => IsStructuredTableXml(NormalizeEntryPath(entry.FullName)))
                     .ToList())
        {
            var tableXml = XlsxPackageXmlEditor.LoadXml(tableEntry);
            var autoFilter = tableXml.Root?.Element(worksheetNs + "autoFilter");
            if (autoFilter is not null &&
                XlsxWorksheetAutoFilterNormalizer.NormalizeElement(autoFilter))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, tableEntry.FullName, tableXml);
            }
        }
    }

    private static bool HasStructuredTableSortStateSchemaIssues(ZipArchive archive)
    {
        foreach (var tableEntry in archive.Entries
                     .Where(entry => IsStructuredTableXml(NormalizeEntryPath(entry.FullName)))
                     .ToList())
        {
            try
            {
                var tableXml = XlsxPackageXmlEditor.LoadXml(tableEntry);
                var sortState = tableXml.Root?.Element(XName.Get(
                    "sortState",
                    "http://schemas.openxmlformats.org/spreadsheetml/2006/main"));
                if (sortState is not null &&
                    XlsxWorksheetSortStateNormalizer.NormalizeElement(sortState))
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
        }

        return false;
    }

    private static void NormalizeStructuredTableSortStates(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var tableEntry in archive.Entries
                     .Where(entry => IsStructuredTableXml(NormalizeEntryPath(entry.FullName)))
                     .ToList())
        {
            var tableXml = XlsxPackageXmlEditor.LoadXml(tableEntry);
            var sortState = tableXml.Root?.Element(worksheetNs + "sortState");
            if (sortState is not null &&
                XlsxWorksheetSortStateNormalizer.NormalizeElement(sortState))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, tableEntry.FullName, tableXml);
            }
        }
    }

    private static bool HasStructuredTableMetadataSchemaIssues(ZipArchive archive)
    {
        foreach (var tableEntry in archive.Entries
                     .Where(entry => IsStructuredTableXml(NormalizeEntryPath(entry.FullName)))
                     .ToList())
        {
            try
            {
                var tableXml = XlsxPackageXmlEditor.LoadXml(tableEntry);
                if (tableXml.Root is not null &&
                    XlsxStructuredTableSchemaNormalizer.NormalizeElement(tableXml.Root, tableEntry.FullName))
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
        }

        return false;
    }

    private static void NormalizeStructuredTableMetadata(ZipArchive archive)
    {
        foreach (var tableEntry in archive.Entries
                     .Where(entry => IsStructuredTableXml(NormalizeEntryPath(entry.FullName)))
                     .ToList())
        {
            var tableXml = XlsxPackageXmlEditor.LoadXml(tableEntry);
            if (tableXml.Root is not null &&
                XlsxStructuredTableSchemaNormalizer.NormalizeElement(tableXml.Root, tableEntry.FullName))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, tableEntry.FullName, tableXml);
            }
        }
    }

    private static bool HasWorksheetGridXmlSchemaIssues(ZipArchive archive)
        // Streaming canonical scan instead of a full per-worksheet XDocument load: the previous
        // implementation materialized every cell of every sheet on each load just to answer this.
        => XlsxWorksheetGridXmlNormalizer.HasGridXmlSchemaIssues(archive);

    private static void NormalizeWorksheetGridXml(ZipArchive archive) =>
        XlsxWorksheetGridXmlNormalizer.NormalizeWorksheets(archive);

    private static bool HasWorksheetRelationshipMarkerSchemaIssues(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries
                     .Where(XlsxConditionalFormatRuleSupport.IsWorksheetEntry)
                     .ToList())
        {
            try
            {
                var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
                var root = worksheetXml.Root;
                if (root is not null &&
                    XlsxWorksheetRelationshipMarkerNormalizer.NormalizeWorksheetRoot(root))
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
        }

        return false;
    }

    private static void NormalizeWorksheetRelationshipMarkers(ZipArchive archive) =>
        XlsxWorksheetRelationshipMarkerNormalizer.NormalizeWorksheets(archive);

    private static bool HasWorksheetNativeMetadataSchemaIssues(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries
                     .Where(XlsxConditionalFormatRuleSupport.IsWorksheetEntry)
                     .ToList())
        {
            try
            {
                var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
                var root = worksheetXml.Root;
                if (root is not null &&
                    NormalizeWorksheetNativeMetadataRoot(new XElement(root)))
                {
                    return true;
                }
            }
            catch
            {
                return true;
            }
        }

        return false;
    }

    private static void NormalizeWorksheetNativeMetadata(ZipArchive archive)
    {
        foreach (var worksheetEntry in archive.Entries
                     .Where(XlsxConditionalFormatRuleSupport.IsWorksheetEntry)
                     .ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is not null &&
                NormalizeWorksheetNativeMetadataRoot(root))
            {
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
            }
        }
    }

    internal static bool NormalizeWorksheetNativeMetadataRoot(XElement root)
    {
        var changed = false;
        changed |= XlsxWorksheetProtectionNormalizer.NormalizeWorksheetRoot(root);
        changed |= XlsxWorksheetProtectedRangeNormalizer.NormalizeWorksheetRoot(root);
        changed |= XlsxWorksheetScenarioNormalizer.NormalizeWorksheetRoot(root);
        changed |= XlsxWorksheetSmartTagNormalizer.NormalizeWorksheetRoot(root);
        changed |= XlsxWorksheetCustomSheetViewExtensionListNormalizer.NormalizeWorksheetRoot(root);
        changed |= XlsxWorksheetPhoneticPropertyNormalizer.NormalizeWorksheetRoot(root);
        changed |= XlsxWorksheetCellWatchesNormalizer.NormalizeWorksheetRoot(root);
        changed |= XlsxWorksheetCustomPropertiesNormalizer.NormalizeWorksheetRoot(root);
        changed |= XlsxWorksheetIgnoredErrorsNormalizer.NormalizeWorksheetRoot(root);
        changed |= XlsxWorksheetHyperlinkNormalizer.NormalizeWorksheetRoot(root);
        changed |= XlsxWorksheetAutoFilterNormalizer.NormalizeWorksheetRoot(root);
        changed |= XlsxWorksheetSortStateNormalizer.NormalizeWorksheetRoot(root);
        changed |= XlsxWorksheetDataConsolidationNormalizer.NormalizeWorksheetRoot(root);
        changed |= XlsxWorksheetExtensionListNormalizer.NormalizeWorksheetRoot(root);
        changed |= XlsxWorksheetWebPublishItemsNormalizer.NormalizeWorksheetRoot(root);
        changed |= XlsxWorksheetOleControlNormalizer.NormalizeWorksheetRoot(root);
        changed |= XlsxWorksheetPageLayoutNormalizer.NormalizeWorksheetRoot(root);
        changed |= XlsxWorksheetPageBreakNormalizer.NormalizeWorksheetRoot(root);

        foreach (var dataValidations in root.Elements(root.Name.Namespace + "dataValidations").ToList())
            changed |= XlsxWorksheetDataValidationNormalizer.NormalizeElement(dataValidations);

        return changed;
    }

    private static void RemoveWorksheetDynamicFilters(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var worksheetEntry in archive.Entries
                     .Where(XlsxConditionalFormatRuleSupport.IsWorksheetEntry)
                     .ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var dynamicFilters = root
                .Descendants(worksheetNs + "dynamicFilter")
                .ToList();
            if (dynamicFilters.Count == 0)
                continue;

            dynamicFilters.Remove();
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    private static void RemoveWorksheetMergeCells(
        ZipArchive archive,
        IReadOnlySet<string> worksheetPathsToStrip)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var worksheetPath in worksheetPathsToStrip)
        {
            var worksheetEntry = archive.GetEntry(worksheetPath);
            if (worksheetEntry is null)
                continue;

            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var changed = RemoveElements(root.Elements(worksheetNs + "mergeCells"));
            if (changed)
                XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    private static void RemoveUnsupportedConditionalFormattingBlocks(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var worksheetEntry in archive.Entries
                     .Where(entry =>
                         entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var unsupportedBlocks = root
                .Elements(worksheetNs + "conditionalFormatting")
                .Where(block => XlsxConditionalFormatRuleSupport.ConditionalFormattingHasUnsupportedRule(block, worksheetNs, allowBlankType: false))
                .ToList();
            if (unsupportedBlocks.Count == 0)
                continue;

            unsupportedBlocks.Remove();
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

    private static void RemoveAllConditionalFormattingBlocks(ZipArchive archive)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        foreach (var worksheetEntry in archive.Entries
                     .Where(entry =>
                         entry.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                         entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            var worksheetXml = XlsxPackageXmlEditor.LoadXml(worksheetEntry);
            var root = worksheetXml.Root;
            if (root is null)
                continue;

            var blocks = root
                .Elements(worksheetNs + "conditionalFormatting")
                .ToList();
            if (blocks.Count == 0)
                continue;

            blocks.Remove();
            XlsxPackageXmlEditor.ReplaceXml(archive, worksheetEntry.FullName, worksheetXml);
        }
    }

}
