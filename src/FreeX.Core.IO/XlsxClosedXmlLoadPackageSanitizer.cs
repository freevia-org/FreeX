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
    bool? HasDocumentPropertiesPackageGraphIssues,
    IReadOnlySet<string>? MergeCellWorksheetPathsToStrip);

internal static class XlsxClosedXmlLoadPackageSanitizer
{
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
        if (!requirements.RequiresAny)
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
        using (var archive = new ZipArchive(sanitized, ZipArchiveMode.Update, leaveOpen: true))
        {
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
            if (requirements.MergeCellWorksheetPathsToStrip is { Count: > 0 } mergeCellWorksheetPaths)
                RemoveWorksheetMergeCells(archive, mergeCellWorksheetPaths);
            if (requirements.HasDocumentPropertiesPackageGraphIssues)
                XlsxDocumentPropertiesPreserver.NormalizePackageGraph(archive);
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
                ResolveKnownOrScan(knownHints.HasDocumentPropertiesPackageGraphIssues, archive, HasDocumentPropertiesPackageGraphIssues),
                knownHints.MergeCellWorksheetPathsToStrip);
        }
        catch
        {
            return new SanitizationRequirements(true, true, true, scanAllConditionalFormatting, true, true, true, null);
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
            hints.HasDocumentPropertiesPackageGraphIssues is not { })
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
            hints.HasDocumentPropertiesPackageGraphIssues.GetValueOrDefault(),
            hints.MergeCellWorksheetPathsToStrip);
        return true;
    }

    private static bool ResolveKnownOrScan(
        bool? knownValue,
        ZipArchive archive,
        Func<ZipArchive, bool> scan) =>
        knownValue ?? scan(archive);

    private static bool HasDocumentPropertiesPackageGraphIssues(ZipArchive archive)
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

            return HasDocumentPropertyRelationshipIssue(
                    archive,
                    root,
                    relationshipNs,
                    "docProps/core.xml",
                    "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties") ||
                HasDocumentPropertyRelationshipIssue(
                    archive,
                    root,
                    relationshipNs,
                    "docProps/app.xml",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties") ||
                HasDocumentPropertyRelationshipIssue(
                    archive,
                    root,
                    relationshipNs,
                    "docProps/custom.xml",
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties");
        }
        catch
        {
            return false;
        }
    }

    private static bool HasDocumentPropertyRelationshipIssue(
        ZipArchive archive,
        XElement relationshipsRoot,
        XNamespace relationshipNs,
        string partName,
        string relationshipType)
    {
        if (archive.GetEntry(partName) is null)
            return false;

        var relationships = relationshipsRoot
            .Elements(relationshipNs + "Relationship")
            .Where(relationship => RelationshipTargetsPart(relationship, partName))
            .ToArray();
        if (relationships.Length == 0)
            return false;
        if (relationships.Length > 1)
            return true;

        var relationship = relationships[0];
        if (!string.Equals(
                relationship.Attribute("Type")?.Value?.Trim(),
                relationshipType,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.Equals(
                relationship.Attribute("Target")?.Value?.Trim(),
                partName,
                StringComparison.Ordinal) ||
            !string.IsNullOrWhiteSpace(relationship.Attribute("TargetMode")?.Value);
    }

    private static bool RelationshipTargetsPart(XElement relationship, string partName)
    {
        var target = relationship.Attribute("Target")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(target))
            return false;

        var targetMode = relationship.Attribute("TargetMode")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(targetMode) &&
            !string.Equals(targetMode, "Internal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(
            XlsxPackagePath.ResolveRelationshipTarget("", target),
            partName,
            StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct SanitizationRequirements(
        bool HasPivotPackageMetadata,
        bool HasChartExChartParts,
        bool HasDrawingPackageParts,
        bool HasAllConditionalFormattingBlocks,
        bool HasUnsupportedConditionalFormattingBlocks,
        bool HasWorksheetDynamicFilters,
        bool HasDocumentPropertiesPackageGraphIssues,
        IReadOnlySet<string>? MergeCellWorksheetPathsToStrip)
    {
        public bool RequiresAny =>
            HasPivotPackageMetadata ||
            HasChartExChartParts ||
            HasDrawingPackageParts ||
            HasAllConditionalFormattingBlocks ||
            HasUnsupportedConditionalFormattingBlocks ||
            HasWorksheetDynamicFilters ||
            HasDocumentPropertiesPackageGraphIssues ||
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
            if (requirements.HasDocumentPropertiesPackageGraphIssues)
            {
                using var archive = new ZipArchive(targetPackage, ZipArchiveMode.Update, leaveOpen: true);
                XlsxDocumentPropertiesPreserver.NormalizePackageGraph(archive);
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
                requirements.HasDrawingPackageParts && IsClosedXmlDrawingPackageEntry(normalizedPath))
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

        if (requirements.HasPivotPackageMetadata &&
            string.Equals(normalizedPath, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase))
        {
            WriteTransformedWorkbookEntry(sourceEntry, targetArchive);
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
        ShouldStripMergeCells(requirements, normalizedPath);

    private static bool ShouldTransformRelationshipEntry(
        string normalizedPath,
        SanitizationRequirements requirements,
        IReadOnlySet<string> removedParts,
        IReadOnlySet<string> chartExParts)
    {
        if (requirements.HasPivotPackageMetadata)
            return true;

        if (requirements.HasDrawingPackageParts && removedParts.Count > 0)
            return GetSheetPathFromRelationshipPath(normalizedPath) is not null;

        return requirements.HasChartExChartParts &&
            chartExParts.Count > 0 &&
            IsDrawingRelationshipEntry(normalizedPath);
    }

    private static void WriteTransformedWorkbookEntry(
        ZipArchiveEntry sourceEntry,
        ZipArchive targetArchive)
    {
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        workbookXml.Root?.Elements(workbookNs + "pivotCaches").Remove();
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
            return false;
        }

        var resolvedTarget = XlsxPackagePath.ResolveRelationshipTarget(sourcePart, target);
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
        var targetEntry = targetArchive.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
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
        XlsxPackagePath.NormalizeZipPath(partName.Trim().Replace('\\', '/').TrimStart('/'));

    private static string NormalizeEntryPath(string path) =>
        XlsxPackagePath.NormalizeZipPath(path.Replace('\\', '/').TrimStart('/'));

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
