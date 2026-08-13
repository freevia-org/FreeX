using System.IO.Compression;
using System.Xml.Linq;
using static FreeX.Core.IO.XlsxSlicerTimelineRelationshipTypes;

namespace FreeX.Core.IO;

internal static class XlsxPackageMetadataMerger
{
    private const string SpreadsheetRelationshipPrefix = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";
    private const string ExternalLinkPathRelationshipType = SpreadsheetRelationshipPrefix + "externalLinkPath";
    private const string ImageRelationshipType = SpreadsheetRelationshipPrefix + "image";
    private const string PackageRelationshipType = SpreadsheetRelationshipPrefix + "package";
    private const string PivotCacheDefinitionRelationshipType = SpreadsheetRelationshipPrefix + "pivotCacheDefinition";
    private const string PivotCacheRecordsRelationshipType = SpreadsheetRelationshipPrefix + "pivotCacheRecords";
    private const string HyperlinkRelationshipType = SpreadsheetRelationshipPrefix + "hyperlink";
    private const string CustomXmlRelationshipType = SpreadsheetRelationshipPrefix + "customXml";
    private const string CustomXmlPropertiesRelationshipType = SpreadsheetRelationshipPrefix + "customXmlProps";
    private const string CustomXmlPropertiesContentType = "application/vnd.openxmlformats-officedocument.customXmlProperties+xml";
    private const string ThreadedCommentsRelationshipType = "http://schemas.microsoft.com/office/2017/10/relationships/threadedComment";
    private const string PersonRelationshipType = "http://schemas.microsoft.com/office/2017/10/relationships/person";
    private const string ChartExStyleRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/chartStyle";
    private const string ChartExColorStyleRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/chartColorStyle";
    private const string WebExtensionTaskpanesRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/webextensiontaskpanes";
    private const string WebExtensionRelationshipType = "http://schemas.microsoft.com/office/2011/relationships/webextension";
    private const string SlicerCachesWorkbookExtensionUri = "{BBE1A952-AA13-448e-AADC-164F8A28A991}";
    private const string TimelineCachesWorkbookExtensionUri = "{D0CA8CA8-9F24-4464-BF8E-62219DCF47F9}";
    // R70-io-vba-6-1: the macro-enabled workbook content-type (.xlsm/.xltm) -- must never be
    // carried into a plain (non-macro) .xlsx/.xltx target's [Content_Types].xml.
    private const string MacroEnabledWorkbookContentType = "application/vnd.ms-excel.sheet.macroEnabled.main+xml";

    public static IReadOnlySet<string> CopyUnknownPackageParts(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        IReadOnlySet<string>? excludedSourceParts = null)
    {
        var targetIndex = ArchiveEntryIndex.Create(targetArchive);
        var generatedEntriesBeforeMerge = targetIndex.EntryNames();

        // OPC part names are compared case-insensitively. A source part whose name differs only by
        // case from one already in the generated package (e.g. Excel's xl/drawings/vmlDrawing2.vml
        // vs ClosedXML's xl/drawings/vmldrawing2.vml for the same legacy comment) must NOT be copied:
        // two case-colliding parts make the package unreadable ("Format error in package"), so Excel
        // drops PivotTables/formulas on repair. Track existing names case-insensitively (zip entry
        // lookups via GetEntry are case-sensitive and miss the collision).
        var existingPartNames = new HashSet<string>(generatedEntriesBeforeMerge, StringComparer.OrdinalIgnoreCase);

        foreach (var sourceEntry in sourceArchive.Entries)
        {
            if (!TryNormalizeCopyableEntryName(sourceEntry.FullName, out var sourceEntryName))
                continue;
            if (IsExcludedSourcePart(sourceEntryName, excludedSourceParts))
                continue;
            if (IsPackageMetadataEntry(sourceEntryName))
                continue;
            if (IsInvalidCustomXmlSidecar(sourceEntryName, sourceEntry))
                continue;
            if (!existingPartNames.Add(sourceEntryName))
                continue;

            CopyEntry(sourceEntry, targetIndex, sourceEntryName);
        }

        return generatedEntriesBeforeMerge;
    }

    public static void CopyEntry(ZipArchiveEntry sourceEntry, ZipArchive targetArchive)
        => CopyEntry(sourceEntry, ArchiveEntryIndex.Create(targetArchive), sourceEntry.FullName);

    private static void CopyEntry(ZipArchiveEntry sourceEntry, ArchiveEntryIndex targetIndex, string targetEntryName)
    {
        targetIndex.Delete(targetEntryName);
        var targetEntry = targetIndex.Create(targetEntryName, CompressionLevel.Optimal);
        targetEntry.LastWriteTime = sourceEntry.LastWriteTime;
        using var sourceStream = sourceEntry.Open();
        using var targetStream = targetEntry.Open();
        sourceStream.CopyTo(targetStream);
    }

    public static void MergeContentTypes(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        IReadOnlySet<string>? excludedSourceParts = null,
        bool preserveMacroEnabledWorkbookContentType = true)
    {
        var sourceEntry = sourceArchive.GetEntry("[Content_Types].xml");
        var targetIndex = ArchiveEntryIndex.Create(targetArchive);
        var targetEntry = targetIndex.Get("[Content_Types].xml");
        if (sourceEntry is null || targetEntry is null)
            return;

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var sourceXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var targetXml = XlsxPackageXmlEditor.LoadXml(targetEntry);
        var targetRoot = targetXml.Root;
        var sourceRoot = sourceXml.Root;
        if (targetRoot is null || sourceRoot is null)
            return;

        var changed = false;
        var existingDefaults = targetRoot
            .Elements(contentTypeNs + "Default")
            .Select(element => element.Attribute("Extension")?.Value)
            .OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeContentTypeExtension)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceDefault in sourceRoot.Elements(contentTypeNs + "Default"))
        {
            var extension = sourceDefault.Attribute("Extension")?.Value;
            if (!string.IsNullOrWhiteSpace(extension) && existingDefaults.Add(NormalizeContentTypeExtension(extension)))
            {
                targetRoot.Add(new XElement(sourceDefault));
                changed = true;
            }
        }

        var existingOverrides = targetRoot
            .Elements(contentTypeNs + "Override")
            .Select(element => element.Attribute("PartName")?.Value)
            .OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => TryNormalizeContentTypePartName(value, out var normalized) ? normalized : "")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetPartNames = targetIndex.ValidPackageEntryNames();
        foreach (var sourceOverride in sourceRoot.Elements(contentTypeNs + "Override"))
        {
            var partName = sourceOverride.Attribute("PartName")?.Value;
            if (IsExcludedSourcePart(partName, excludedSourceParts))
                continue;

            // R70-io-vba-6-1: only carry a macroEnabled workbook content-type override over when
            // the target must actually stay macro-enabled (.xlsm/.xltm). A plain .xlsx/.xltx target
            // must keep the plain spreadsheetml content-type ClosedXML already wrote -- carrying
            // this override through (whether by the wholesale "missing override" copy just below,
            // or by flipping an existing target override in TryPreserveMacroEnabledWorkbookContentType)
            // would relabel a package that (correctly) no longer carries a VBA project as if it
            // still did. A freshly-generated ClosedXML package normally has NO explicit Override
            // for xl/workbook.xml at all (it relies on the Default Extension="xml" entry, which
            // already carries the plain type) -- so without this early skip, the wholesale-copy
            // branch below would freely copy the source's macroEnabled override into the target
            // since the target has nothing there to say it already has one.
            if (!preserveMacroEnabledWorkbookContentType &&
                TryNormalizeContentTypePartName(partName, out var earlyNormalizedPartName) &&
                string.Equals(earlyNormalizedPartName, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(sourceOverride.Attribute("ContentType")?.Value, MacroEnabledWorkbookContentType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryNormalizeContentTypePartName(partName, out var normalizedPartName) &&
                targetPartNames.Contains(normalizedPartName) &&
                existingOverrides.Add(normalizedPartName))
            {
                var mergedOverride = new XElement(sourceOverride);
                mergedOverride.SetAttributeValue("PartName", $"/{normalizedPartName}");
                targetRoot.Add(mergedOverride);
                changed = true;
                continue;
            }

            if (preserveMacroEnabledWorkbookContentType &&
                TryNormalizeContentTypePartName(partName, out normalizedPartName) &&
                targetPartNames.Contains(normalizedPartName) &&
                TryPreserveMacroEnabledWorkbookContentType(targetRoot, sourceOverride, normalizedPartName, contentTypeNs))
            {
                changed = true;
            }
        }

        if (changed)
            WriteXml(targetIndex, "[Content_Types].xml", targetXml, targetEntry.LastWriteTime, SaveOptions.DisableFormatting);
    }

    public static void MergeRelationshipParts(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        IReadOnlySet<string> generatedEntriesBeforeMerge,
        IReadOnlySet<string>? excludedSourceParts = null)
    {
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var targetIndex = ArchiveEntryIndex.Create(targetArchive);

        foreach (var sourceEntry in sourceArchive.Entries.Where(entry =>
                     entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            if (IsExcludedSourcePart(sourceEntry.FullName, excludedSourceParts))
                continue;

            var targetEntry = targetIndex.Get(sourceEntry.FullName);
            if (targetEntry is null)
            {
                var filteredRelationships = CreateFilteredRelationshipPart(
                    sourceEntry,
                    targetIndex,
                    generatedEntriesBeforeMerge,
                    excludedSourceParts,
                    relationshipNs,
                    out var relationshipsChanged);
                if (filteredRelationships is null)
                    continue;

                if (relationshipsChanged)
                    WriteXml(targetIndex, sourceEntry.FullName, filteredRelationships, sourceEntry.LastWriteTime);
                else
                    CopyEntry(sourceEntry, targetIndex, sourceEntry.FullName);
                continue;
            }

            var sourceXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
            var targetXml = XlsxPackageXmlEditor.LoadXml(targetEntry);
            var sourceRoot = sourceXml.Root;
            var targetRoot = targetXml.Root;
            if (sourceRoot is null || targetRoot is null)
                continue;

            var existingRelationships = targetRoot
                .Elements(relationshipNs + "Relationship")
                .Select(RelationshipSignature)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var existingIds = targetRoot
                .Elements(relationshipNs + "Relationship")
                .Select(element => element.Attribute("Id")?.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var changed = false;
            Dictionary<string, string>? copiedPartRelationshipIdMap = null;
            foreach (var sourceRelationship in sourceRoot.Elements(relationshipNs + "Relationship"))
            {
                if (!IsStructurallyValidPackageRelationship(sourceRelationship) ||
                    !ShouldPreserveRelationship(
                        sourceEntry.FullName,
                        sourceRelationship,
                        targetIndex,
                        generatedEntriesBeforeMerge,
                        excludedSourceParts))
                    continue;

                if (!existingRelationships.Add(RelationshipSignature(sourceRelationship)))
                    continue;

                var copy = new XElement(sourceRelationship);
                var id = copy.Attribute("Id")?.Value;
                if (!string.IsNullOrWhiteSpace(id) && existingIds.Contains(id))
                {
                    copy.SetAttributeValue("Id", XlsxPackageXmlEditor.NextRelationshipId(targetXml, relationshipNs));
                    var remappedId = copy.Attribute("Id")?.Value;
                    if (!string.IsNullOrWhiteSpace(remappedId) &&
                        ShouldRebindRelationshipReferenceOnCopiedPart(sourceRelationship))
                    {
                        copiedPartRelationshipIdMap ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        copiedPartRelationshipIdMap[id] = remappedId;
                    }
                }
                targetRoot.Add(copy);
                var copiedId = copy.Attribute("Id")?.Value;
                if (!string.IsNullOrWhiteSpace(copiedId))
                    existingIds.Add(copiedId);
                changed = true;
            }

            if (changed)
            {
                WriteXml(targetIndex, targetEntry.FullName, targetXml, targetEntry.LastWriteTime, SaveOptions.DisableFormatting);
                RebindCopiedPartRelationshipReferences(
                    targetIndex,
                    sourceEntry.FullName,
                    generatedEntriesBeforeMerge,
                    copiedPartRelationshipIdMap);
            }
        }

        EnsureSlicerTimelineWorkbookExtensionReferences(targetIndex);
    }

    public static void NormalizeCustomXmlPackageGraph(ZipArchive archive)
    {
        var targetIndex = ArchiveEntryIndex.Create(archive);
        var removedPartNames = targetIndex.EntryNames()
            .Where(entryName => IsCustomXmlItemPart(entryName) || IsCustomXmlPropertiesPart(entryName))
            .Where(entryName =>
                targetIndex.Get(entryName) is { } entry &&
                IsInvalidCustomXmlSidecar(entryName, entry))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entryName in removedPartNames)
            targetIndex.Delete(entryName);

        RemoveOrphanCustomXmlItemRelationshipParts(targetIndex);
        RemoveDanglingCustomXmlPackageRelationships(targetIndex);

        var referencedPropertiesParts = NormalizeCustomXmlItemPropertyRelationships(targetIndex);
        var orphanPropertiesParts = targetIndex.EntryNames()
            .Where(IsCustomXmlPropertiesPart)
            .Where(entryName => !referencedPropertiesParts.Contains(entryName))
            .ToList();
        foreach (var entryName in orphanPropertiesParts)
        {
            targetIndex.Delete(entryName);
            removedPartNames.Add(entryName);
        }

        if (removedPartNames.Count != 0)
        {
            RemoveContentTypeOverrides(targetIndex, removedPartNames);
            RemoveRelationshipsTargetingParts(targetIndex, removedPartNames);
            RemoveOrphanCustomXmlItemRelationshipParts(targetIndex);
            RemoveDanglingCustomXmlPackageRelationships(targetIndex);
        }

        EnsureCustomXmlPropertiesContentTypeOverrides(targetIndex, referencedPropertiesParts);
    }

    private static XDocument? CreateFilteredRelationshipPart(
        ZipArchiveEntry sourceEntry,
        ArchiveEntryIndex targetIndex,
        IReadOnlySet<string> generatedEntriesBeforeMerge,
        IReadOnlySet<string>? excludedSourceParts,
        XNamespace relationshipNs,
        out bool changed)
    {
        changed = false;
        var sourceXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var sourceRoot = sourceXml.Root;
        if (sourceRoot is null)
            return null;

        var targetXml = new XDocument(new XElement(relationshipNs + "Relationships"));
        var existingRelationships = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceRelationship in sourceRoot.Elements(relationshipNs + "Relationship"))
        {
            if (!IsStructurallyValidPackageRelationship(sourceRelationship) ||
                !ShouldPreserveRelationship(
                    sourceEntry.FullName,
                    sourceRelationship,
                    targetIndex,
                    generatedEntriesBeforeMerge,
                    excludedSourceParts))
            {
                changed = true;
                continue;
            }

            if (!existingRelationships.Add(RelationshipSignature(sourceRelationship)))
            {
                changed = true;
                continue;
            }

            var copy = new XElement(sourceRelationship);
            var id = copy.Attribute("Id")?.Value;
            if (!string.IsNullOrWhiteSpace(id) && existingIds.Contains(id))
            {
                copy.SetAttributeValue("Id", XlsxPackageXmlEditor.NextRelationshipId(targetXml, relationshipNs));
                changed = true;
            }

            targetXml.Root!.Add(copy);
            var copiedId = copy.Attribute("Id")?.Value;
            if (!string.IsNullOrWhiteSpace(copiedId))
                existingIds.Add(copiedId);
        }

        return targetXml.Root!.Elements(relationshipNs + "Relationship").Any() && changed
            ? targetXml
            : changed
                ? null
                : new XDocument(sourceXml);
    }

    private static void WriteXml(
        ArchiveEntryIndex targetIndex,
        string targetEntryName,
        XDocument xml,
        DateTimeOffset lastWriteTime,
        SaveOptions saveOptions = SaveOptions.None)
    {
        targetIndex.Delete(targetEntryName);
        var targetEntry = targetIndex.Create(targetEntryName, CompressionLevel.Optimal);
        targetEntry.LastWriteTime = lastWriteTime;
        using var stream = targetEntry.Open();
        xml.Save(stream, saveOptions);
    }

    private static bool IsPackageMetadataEntry(string entryName) =>
        string.Equals(entryName, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase) ||
        entryName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);

    private static bool IsInvalidCustomXmlSidecar(string entryName, ZipArchiveEntry entry)
    {
        if (!IsCustomXmlItemPart(entryName) && !IsCustomXmlPropertiesPart(entryName))
            return false;

        XDocument xml;
        try
        {
            xml = XlsxPackageXmlEditor.LoadXml(entry);
        }
        catch
        {
            return true;
        }

        return IsCustomXmlPropertiesPart(entryName) && !IsValidCustomXmlProperties(xml);
    }

    private static bool IsCustomXmlItemPart(string entryName) =>
        entryName.StartsWith("customXml/item", StringComparison.OrdinalIgnoreCase) &&
        entryName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
        !IsCustomXmlPropertiesPart(entryName);

    private static bool IsCustomXmlPropertiesPart(string entryName) =>
        entryName.StartsWith("customXml/itemProps", StringComparison.OrdinalIgnoreCase) &&
        entryName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static bool IsCustomXmlItemRelationshipPart(string entryName) =>
        entryName.StartsWith("customXml/_rels/", StringComparison.OrdinalIgnoreCase) &&
        entryName.EndsWith(".xml.rels", StringComparison.OrdinalIgnoreCase) &&
        IsCustomXmlItemPart(RelationshipPartToSourcePart(entryName));

    private static bool IsValidCustomXmlProperties(XDocument xml)
    {
        XNamespace customXmlNs = "http://schemas.openxmlformats.org/officeDocument/2006/customXml";
        var root = xml.Root;
        var itemId = root?.Attribute(customXmlNs + "itemID")?.Value ??
                     root?.Attribute("itemID")?.Value;
        return root?.Name == customXmlNs + "datastoreItem" &&
               !string.IsNullOrWhiteSpace(itemId);
    }

    private static void RemoveOrphanCustomXmlItemRelationshipParts(ArchiveEntryIndex targetIndex)
    {
        foreach (var relationshipPartPath in targetIndex.EntryNames()
                     .Where(IsCustomXmlItemRelationshipPart)
                     .ToList())
        {
            if (!targetIndex.Contains(RelationshipPartToSourcePart(relationshipPartPath)))
                targetIndex.Delete(relationshipPartPath);
        }
    }

    private static void RemoveDanglingCustomXmlPackageRelationships(ArchiveEntryIndex targetIndex)
    {
        var rootRelationshipsEntry = targetIndex.Get("_rels/.rels");
        if (rootRelationshipsEntry is null)
            return;

        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationshipsXml = XlsxPackageXmlEditor.LoadXml(rootRelationshipsEntry);
        var root = relationshipsXml.Root;
        if (root is null)
            return;

        var existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var customXmlTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var relationship in root.Elements(relationshipNs + "Relationship").ToList())
        {
            var id = relationship.Attribute("Id")?.Value;
            if (!string.Equals(NormalizeRelationshipType(relationship), CustomXmlRelationshipType, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(id))
                    existingIds.Add(id);
                continue;
            }

            if (IsExternalRelationship(relationship))
            {
                relationship.Remove();
                changed = true;
                continue;
            }

            var targetPart = XlsxPackagePath.ResolveRelationshipTarget("", NormalizeRelationshipTarget(relationship));
            if (!IsCustomXmlItemPart(targetPart) ||
                !targetIndex.Contains(targetPart) ||
                !customXmlTargets.Add(targetPart))
            {
                relationship.Remove();
                changed = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(id) || !existingIds.Add(id))
            {
                relationship.SetAttributeValue("Id", XlsxPackageXmlEditor.NextRelationshipId(relationshipsXml, relationshipNs));
                existingIds.Add(relationship.Attribute("Id")!.Value);
                changed = true;
            }
        }

        if (changed)
            WriteXml(targetIndex, "_rels/.rels", relationshipsXml, rootRelationshipsEntry.LastWriteTime, SaveOptions.DisableFormatting);
    }

    private static HashSet<string> NormalizeCustomXmlItemPropertyRelationships(ArchiveEntryIndex targetIndex)
    {
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var referencedPropertiesParts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var itemParts = targetIndex.EntryNames().Where(IsCustomXmlItemPart).ToList();

        // Pre-pass: for every item, find the properties part it unambiguously (exactly one
        // candidate) already relates to via its own, untouched relationship graph, and count how
        // many items claim each such target. A target claimed by exactly one item is that item's
        // real, authoritative OPC relationship -- not merely a same-numbered filename coincidence
        // -- and must be trusted as-is below rather than overridden by the paired-by-number guess.
        // A target claimed by two or more items is a genuine conflict (e.g. a "misbound" source
        // file) that the paired-by-number guess exists to repair.
        var ownTargetUsageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var itemPart in itemParts)
        {
            var ownTarget = FindOwnUnambiguousExistingCustomXmlPropertiesTarget(targetIndex, itemPart);
            if (string.IsNullOrWhiteSpace(ownTarget))
                continue;

            ownTargetUsageCounts[ownTarget] = ownTargetUsageCounts.TryGetValue(ownTarget, out var existing)
                ? existing + 1
                : 1;
        }

        foreach (var itemPart in itemParts)
        {
            var relationshipPartPath = XlsxPackagePath.GetRelationshipPartPath(itemPart);
            var relationshipEntry = targetIndex.Get(relationshipPartPath);
            XDocument relationshipXml;
            if (relationshipEntry is null)
            {
                relationshipXml = new XDocument(new XElement(relationshipNs + "Relationships"));
            }
            else
            {
                try
                {
                    relationshipXml = XlsxPackageXmlEditor.LoadXml(relationshipEntry);
                }
                catch
                {
                    relationshipXml = new XDocument(new XElement(relationshipNs + "Relationships"));
                }
            }

            var root = relationshipXml.Root;
            if (root is null)
            {
                targetIndex.Delete(relationshipPartPath);
                continue;
            }

            var customXmlPropertiesRelationships = root
                .Elements(relationshipNs + "Relationship")
                .Where(relationship => string.Equals(
                    NormalizeRelationshipType(relationship),
                    CustomXmlPropertiesRelationshipType,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();

            var existingTargetRelationships = customXmlPropertiesRelationships
                .Where(relationship => TargetsExistingCustomXmlPropertiesPart(relationship, targetIndex, itemPart))
                .ToList();
            var ownUniqueTarget = existingTargetRelationships.Count == 1
                ? ResolveRelationshipTarget(itemPart, existingTargetRelationships[0])
                : null;

            var pairedPropertiesPart = GetPairedCustomXmlPropertiesPart(itemPart);
            var selectedPropertiesPart = "";
            var selectedRelationship = FindExistingCustomXmlPropertiesRelationship(
                customXmlPropertiesRelationships,
                targetIndex,
                itemPart);

            if (!string.IsNullOrWhiteSpace(ownUniqueTarget) &&
                ownTargetUsageCounts.TryGetValue(ownUniqueTarget, out var usageCount) &&
                usageCount == 1)
            {
                selectedPropertiesPart = ownUniqueTarget;
                selectedRelationship = existingTargetRelationships[0];
            }
            else if (!string.IsNullOrWhiteSpace(pairedPropertiesPart) && targetIndex.Contains(pairedPropertiesPart))
            {
                selectedPropertiesPart = pairedPropertiesPart;
                selectedRelationship = FindCustomXmlPropertiesRelationshipTargeting(
                    customXmlPropertiesRelationships,
                    itemPart,
                    pairedPropertiesPart) ?? selectedRelationship;
            }
            else if (selectedRelationship is not null)
            {
                selectedPropertiesPart = ResolveRelationshipTarget(itemPart, selectedRelationship);
            }

            if (string.IsNullOrWhiteSpace(selectedPropertiesPart))
            {
                targetIndex.Delete(relationshipPartPath);
                continue;
            }

            var selectedId = selectedRelationship?.Attribute("Id")?.Value;
            var normalizedRelationship = new XElement(
                relationshipNs + "Relationship",
                new XAttribute("Id", string.IsNullOrWhiteSpace(selectedId) ? "rIdFreeXItemProps" : selectedId),
                new XAttribute("Type", CustomXmlPropertiesRelationshipType),
                new XAttribute("Target", GetCustomXmlPropertiesRelationshipTarget(selectedPropertiesPart)));

            root.RemoveNodes();
            root.Add(normalizedRelationship);
            referencedPropertiesParts.Add(selectedPropertiesPart);

            var lastWriteTime = relationshipEntry?.LastWriteTime ?? DateTimeOffset.Now;
            WriteXml(targetIndex, relationshipPartPath, relationshipXml, lastWriteTime, SaveOptions.DisableFormatting);
        }

        return referencedPropertiesParts;
    }

    private static string? FindOwnUnambiguousExistingCustomXmlPropertiesTarget(ArchiveEntryIndex targetIndex, string itemPart)
    {
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationshipEntry = targetIndex.Get(XlsxPackagePath.GetRelationshipPartPath(itemPart));
        if (relationshipEntry is null)
            return null;

        XDocument relationshipXml;
        try
        {
            relationshipXml = XlsxPackageXmlEditor.LoadXml(relationshipEntry);
        }
        catch
        {
            return null;
        }

        var root = relationshipXml.Root;
        if (root is null)
            return null;

        var existingTargetRelationships = root
            .Elements(relationshipNs + "Relationship")
            .Where(relationship => string.Equals(
                NormalizeRelationshipType(relationship),
                CustomXmlPropertiesRelationshipType,
                StringComparison.OrdinalIgnoreCase))
            .Where(relationship => TargetsExistingCustomXmlPropertiesPart(relationship, targetIndex, itemPart))
            .ToList();

        return existingTargetRelationships.Count == 1
            ? ResolveRelationshipTarget(itemPart, existingTargetRelationships[0])
            : null;
    }

    private static XElement? FindExistingCustomXmlPropertiesRelationship(
        IEnumerable<XElement> relationships,
        ArchiveEntryIndex targetIndex,
        string itemPart)
    {
        foreach (var relationship in relationships)
        {
            if (TargetsExistingCustomXmlPropertiesPart(relationship, targetIndex, itemPart))
                return relationship;
        }

        return null;
    }

    private static XElement? FindCustomXmlPropertiesRelationshipTargeting(
        IEnumerable<XElement> relationships,
        string itemPart,
        string propertiesPart)
    {
        foreach (var relationship in relationships)
        {
            if (RelationshipTargetsPart(relationship, itemPart, propertiesPart))
                return relationship;
        }

        return null;
    }

    private static bool TargetsExistingCustomXmlPropertiesPart(
        XElement relationship,
        ArchiveEntryIndex targetIndex,
        string itemPart)
    {
        if (IsExternalRelationship(relationship))
            return false;

        var targetPart = ResolveRelationshipTarget(itemPart, relationship);
        return IsCustomXmlPropertiesPart(targetPart) &&
               targetIndex.Contains(targetPart);
    }

    private static bool RelationshipTargetsPart(
        XElement relationship,
        string sourcePart,
        string targetPart) =>
        string.Equals(
            ResolveRelationshipTarget(sourcePart, relationship),
            targetPart,
            StringComparison.OrdinalIgnoreCase);

    private static string ResolveRelationshipTarget(string sourcePart, XElement relationship)
    {
        var target = NormalizeRelationshipTarget(relationship);
        return string.IsNullOrWhiteSpace(target)
            ? ""
            : XlsxPackagePath.ResolveRelationshipTarget(sourcePart, target);
    }

    private static string GetPairedCustomXmlPropertiesPart(string itemPart)
    {
        const string itemPrefix = "customXml/item";
        const string itemSuffix = ".xml";
        if (!IsCustomXmlItemPart(itemPart) ||
            itemPart.Length <= itemPrefix.Length + itemSuffix.Length)
        {
            return "";
        }

        var itemNumber = itemPart[itemPrefix.Length..^itemSuffix.Length];
        return string.IsNullOrWhiteSpace(itemNumber)
            ? ""
            : $"customXml/itemProps{itemNumber}.xml";
    }

    private static string GetCustomXmlPropertiesRelationshipTarget(string propertiesPart)
    {
        var slash = propertiesPart.LastIndexOf('/');
        return slash >= 0 ? propertiesPart[(slash + 1)..] : propertiesPart;
    }

    private static void EnsureCustomXmlPropertiesContentTypeOverrides(
        ArchiveEntryIndex targetIndex,
        IReadOnlySet<string> propertiesParts)
    {
        if (propertiesParts.Count == 0)
            return;

        var contentTypesEntry = targetIndex.Get("[Content_Types].xml");
        if (contentTypesEntry is null)
            return;

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        var root = contentTypesXml.Root;
        if (root is null)
            return;

        var changed = false;
        var existingOverrides = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in root.Elements(contentTypeNs + "Override").ToList())
        {
            if (!TryNormalizeContentTypePartName(element.Attribute("PartName")?.Value, out var partName) ||
                !propertiesParts.Contains(partName))
            {
                continue;
            }

            if (existingOverrides.TryAdd(partName, element))
                continue;

            element.Remove();
            changed = true;
        }

        foreach (var propertiesPart in propertiesParts)
        {
            if (existingOverrides.TryGetValue(propertiesPart, out var existingOverride))
            {
                if (!string.Equals(existingOverride.Attribute("ContentType")?.Value, CustomXmlPropertiesContentType, StringComparison.OrdinalIgnoreCase))
                {
                    existingOverride.SetAttributeValue("ContentType", CustomXmlPropertiesContentType);
                    changed = true;
                }

                continue;
            }

            root.Add(new XElement(
                contentTypeNs + "Override",
                new XAttribute("PartName", $"/{propertiesPart}"),
                new XAttribute("ContentType", CustomXmlPropertiesContentType)));
            changed = true;
        }

        if (changed)
            WriteXml(targetIndex, "[Content_Types].xml", contentTypesXml, contentTypesEntry.LastWriteTime, SaveOptions.DisableFormatting);
    }

    private static void RemoveContentTypeOverrides(
        ArchiveEntryIndex targetIndex,
        IReadOnlySet<string> removedPartNames)
    {
        var contentTypesEntry = targetIndex.Get("[Content_Types].xml");
        if (contentTypesEntry is null)
            return;

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        var root = contentTypesXml.Root;
        if (root is null)
            return;

        var changed = false;
        foreach (var element in root.Elements(contentTypeNs + "Override").ToList())
        {
            if (!TryNormalizeContentTypePartName(element.Attribute("PartName")?.Value, out var partName) ||
                !removedPartNames.Contains(partName))
            {
                continue;
            }

            element.Remove();
            changed = true;
        }

        if (changed)
            WriteXml(targetIndex, "[Content_Types].xml", contentTypesXml, contentTypesEntry.LastWriteTime, SaveOptions.DisableFormatting);
    }

    private static void RemoveRelationshipsTargetingParts(
        ArchiveEntryIndex targetIndex,
        IReadOnlySet<string> removedPartNames)
    {
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        foreach (var relationshipPartPath in targetIndex.EntryNames()
                     .Where(entryName => entryName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            var relationshipEntry = targetIndex.Get(relationshipPartPath);
            if (relationshipEntry is null)
                continue;

            var relationshipXml = XlsxPackageXmlEditor.LoadXml(relationshipEntry);
            var root = relationshipXml.Root;
            if (root is null)
                continue;

            var sourcePart = RelationshipPartToSourcePart(relationshipPartPath);
            var changed = false;
            foreach (var relationship in root.Elements(relationshipNs + "Relationship").ToList())
            {
                if (IsExternalRelationship(relationship))
                    continue;

                var target = NormalizeRelationshipTarget(relationship);
                if (string.IsNullOrWhiteSpace(target))
                    continue;

                var targetPart = XlsxPackagePath.ResolveRelationshipTarget(sourcePart, target);
                if (removedPartNames.Contains(targetPart))
                {
                    relationship.Remove();
                    changed = true;
                }
            }

            if (!changed)
                continue;

            if (root.Elements(relationshipNs + "Relationship").Any())
                WriteXml(targetIndex, relationshipPartPath, relationshipXml, relationshipEntry.LastWriteTime, SaveOptions.DisableFormatting);
            else
                targetIndex.Delete(relationshipPartPath);
        }
    }

    private static bool TryNormalizeCopyableEntryName(string entryName, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(entryName))
            return false;

        var trimmed = entryName.Trim();
        if (trimmed.Length != entryName.Length ||
            trimmed[0] is '/' or '\\' ||
            trimmed.Contains('\\') ||
            trimmed.Contains(':'))
        {
            return false;
        }

        var segments = trimmed.Split('/');
        if (segments.Length == 0 ||
            segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            return false;
        }

        var normalizedPath = XlsxPackagePath.NormalizeZipPath(trimmed);
        if (!string.Equals(normalizedPath, trimmed, StringComparison.Ordinal))
            return false;

        normalized = normalizedPath;
        return true;
    }

    private static bool TryNormalizeContentTypePartName(string? value, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.StartsWith('/'))
            trimmed = trimmed[1..];

        return TryNormalizeCopyableEntryName(trimmed, out normalized);
    }

    private static string NormalizeContentTypeExtension(string value) =>
        value.Trim().TrimStart('.');

    private static bool TryPreserveMacroEnabledWorkbookContentType(
        XElement targetRoot,
        XElement sourceOverride,
        string normalizedPartName,
        XNamespace contentTypeNs)
    {
        if (!string.Equals(normalizedPartName, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase))
            return false;

        var sourceContentType = sourceOverride.Attribute("ContentType")?.Value;
        if (!string.Equals(
                sourceContentType,
                MacroEnabledWorkbookContentType,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var targetOverride = FindContentTypeOverride(targetRoot, contentTypeNs, normalizedPartName);
        if (targetOverride is null ||
            string.Equals(targetOverride.Attribute("ContentType")?.Value, sourceContentType, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        targetOverride.SetAttributeValue("ContentType", sourceContentType);
        return true;
    }

    private static XElement? FindContentTypeOverride(
        XElement root,
        XNamespace contentTypeNs,
        string normalizedPartName)
    {
        foreach (var element in root.Elements(contentTypeNs + "Override"))
        {
            if (IsContentTypeOverrideForPart(element, normalizedPartName))
                return element;
        }

        return null;
    }

    private static bool IsContentTypeOverrideForPart(XElement element, string normalizedPartName) =>
        TryNormalizeContentTypePartName(element.Attribute("PartName")?.Value, out var targetPartName) &&
        string.Equals(targetPartName, normalizedPartName, StringComparison.OrdinalIgnoreCase);

    private static bool ShouldPreserveRelationship(
        string relationshipPartPath,
        XElement relationship,
        ArchiveEntryIndex targetIndex,
        IReadOnlySet<string> generatedEntriesBeforeMerge,
        IReadOnlySet<string>? excludedSourceParts)
    {
        if (string.Equals(
                NormalizeRelationshipType(relationship),
                "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var target = NormalizeRelationshipTarget(relationship);
        if (string.IsNullOrWhiteSpace(target))
            return false;

        if (IsExternalRelationship(relationship))
            return true;

        // R107-io-internal-hyperlink-rel: a HYPERLINK relationship's Target is a hyperlink
        // destination, never a package part -- for an internal ("Place in This Document") target it
        // is a document location such as "Sheet1!A1" or "#'My Sheet'!A1", written WITHOUT a
        // TargetMode attribute (OPC's default Internal). The package-part survival check below
        // resolves that text as a relative part path (yielding e.g. "xl/drawings/Sheet1!A1"), finds
        // nothing at it, and drops the relationship -- and when it was the part's only relationship,
        // CreateFilteredRelationshipPart then omits the whole .rels part. That silently orphaned the
        // <a:hlinkClick r:id="..."/> still present in a preserved-verbatim drawing part (Duplicate
        // Sheet on a sheet holding a shape/text box/picture with an internal hyperlink left the
        // UNTOUCHED original sheet's drawing pointing at a relationship that no longer existed).
        // The external sibling never hit this only because IsExternalRelationship short-circuits
        // above; TargetMode is irrelevant to whether a hyperlink target is a package part, so both
        // modes are preserved the same way here.
        if (string.Equals(NormalizeRelationshipType(relationship), HyperlinkRelationshipType, StringComparison.OrdinalIgnoreCase))
            return true;

        var targetPart = XlsxPackagePath.ResolveRelationshipTarget(RelationshipPartToSourcePart(relationshipPartPath), target);
        var isModernCommentPackageGraphRelationship =
            IsModernCommentPackageGraphRelationship(relationshipPartPath, relationship, targetPart);
        // excludedSourceParts wins UNLESS the target package already has a live part at targetPart
        // that the workbook model itself asked for (tracked via generatedEntriesBeforeMerge, a
        // snapshot taken after XlsxWorksheetThreadedCommentMapper.Save ran but before this source
        // package merge). That distinguishes two cases that both exclude
        // xl/threadedComments/threadedComment*.xml + xl/persons/person.xml:
        //  - the model still has threaded comments: Save wrote fresh replacements, so a sidecar
        //    (e.g. a modern threadedCommentMetadata part) relationship pointing at them must still
        //    be preserved -- it references a live part, not a stale/deleted one.
        //  - the model has NO threaded comments left (the user deleted every one): Save wrote
        //    nothing, so targetPart is genuinely absent, and the relationship must NOT be
        //    resurrected from the source package (round-12 R12-comments-notes-1).
        if (IsExcludedSourcePart(targetPart, excludedSourceParts) &&
            !(isModernCommentPackageGraphRelationship && generatedEntriesBeforeMerge.Contains(targetPart)))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(targetPart) &&
               targetIndex.Contains(targetPart) &&
               (!generatedEntriesBeforeMerge.Contains(targetPart) ||
                IsChartExStyleColorPackageGraphRelationship(relationshipPartPath, relationship, targetPart) ||
                IsDataModelPackageGraphRelationship(relationshipPartPath, relationship, targetPart) ||
                IsQueryTablePackageGraphRelationship(relationshipPartPath, relationship, targetPart) ||
                IsXmlMapsPackageGraphRelationship(relationshipPartPath, relationship, targetPart) ||
                IsWebExtensionPackageGraphRelationship(relationshipPartPath, relationship, targetPart) ||
                IsPivotCacheRecordsPackageGraphRelationship(relationshipPartPath, relationship, targetPart) ||
                isModernCommentPackageGraphRelationship ||
                IsSlicerTimelinePackageGraphRelationship(relationshipPartPath, relationship, targetPart) ||
                IsCustomXmlPackageGraphRelationship(relationshipPartPath, relationship, targetPart));
    }

    private static bool IsModernCommentPackageGraphRelationship(
        string relationshipPartPath,
        XElement relationship,
        string targetPart)
    {
        var relationshipType = NormalizeRelationshipType(relationship);
        if (string.Equals(relationshipType, ThreadedCommentsRelationshipType, StringComparison.OrdinalIgnoreCase))
        {
            return IsWorksheetOrModernCommentPart(RelationshipPartToSourcePart(relationshipPartPath)) &&
                   IsThreadedCommentPart(targetPart);
        }

        if (string.Equals(relationshipType, PersonRelationshipType, StringComparison.OrdinalIgnoreCase))
        {
            return IsWorkbookOrModernCommentPart(RelationshipPartToSourcePart(relationshipPartPath)) &&
                   IsPersonPart(targetPart);
        }

        return false;
    }

    private static bool IsSlicerTimelinePackageGraphRelationship(
        string relationshipPartPath,
        XElement relationship,
        string targetPart)
    {
        var sourcePart = RelationshipPartToSourcePart(relationshipPartPath);
        var relationshipType = NormalizeRelationshipType(relationship);
        if (string.Equals(sourcePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase))
        {
            return (targetPart.StartsWith("xl/slicerCaches/", StringComparison.OrdinalIgnoreCase) &&
                    targetPart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(relationshipType, SlicerCacheRelationshipType, StringComparison.OrdinalIgnoreCase)) ||
                   (targetPart.StartsWith("xl/timelineCaches/", StringComparison.OrdinalIgnoreCase) &&
                    targetPart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                    IsTimelineCache(relationshipType));
        }

        if (sourcePart.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
            sourcePart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return (targetPart.StartsWith("xl/slicers/", StringComparison.OrdinalIgnoreCase) &&
                    targetPart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(relationshipType, SlicerRelationshipType, StringComparison.OrdinalIgnoreCase)) ||
                   (targetPart.StartsWith("xl/timelines/", StringComparison.OrdinalIgnoreCase) &&
                    targetPart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                    IsTimeline(relationshipType));
        }

        return (sourcePart.StartsWith("xl/slicers/", StringComparison.OrdinalIgnoreCase) &&
                sourcePart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                targetPart.StartsWith("xl/slicerCaches/", StringComparison.OrdinalIgnoreCase) &&
                targetPart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(relationshipType, SlicerCacheRelationshipType, StringComparison.OrdinalIgnoreCase)) ||
               (sourcePart.StartsWith("xl/timelines/", StringComparison.OrdinalIgnoreCase) &&
                sourcePart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                targetPart.StartsWith("xl/timelineCaches/", StringComparison.OrdinalIgnoreCase) &&
                targetPart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                IsTimelineCache(relationshipType));
    }

    private static bool IsQueryTablePackageGraphRelationship(
        string relationshipPartPath,
        XElement relationship,
        string targetPart)
    {
        if (!targetPart.StartsWith("xl/queryTables/", StringComparison.OrdinalIgnoreCase) ||
            !targetPart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sourcePart = RelationshipPartToSourcePart(relationshipPartPath);
        return sourcePart.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
               sourcePart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   NormalizeRelationshipType(relationship),
                   SpreadsheetRelationshipPrefix + "queryTable",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChartExStyleColorPackageGraphRelationship(
        string relationshipPartPath,
        XElement relationship,
        string targetPart)
    {
        var sourcePart = RelationshipPartToSourcePart(relationshipPartPath);
        if (!sourcePart.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase) ||
            !sourcePart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
            !targetPart.StartsWith("xl/charts/", StringComparison.OrdinalIgnoreCase) ||
            !targetPart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relationshipType = NormalizeRelationshipType(relationship);
        return string.Equals(relationshipType, ChartExStyleRelationshipType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(relationshipType, ChartExColorStyleRelationshipType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPivotCacheRecordsPackageGraphRelationship(
        string relationshipPartPath,
        XElement relationship,
        string targetPart)
    {
        if (!targetPart.StartsWith("xl/pivotCache/pivotCacheRecords", StringComparison.OrdinalIgnoreCase) ||
            !targetPart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sourcePart = RelationshipPartToSourcePart(relationshipPartPath);
        if (!sourcePart.StartsWith("xl/pivotCache/pivotCacheDefinition", StringComparison.OrdinalIgnoreCase) ||
            !sourcePart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(
            NormalizeRelationshipType(relationship),
            PivotCacheRecordsRelationshipType,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsXmlMapsPackageGraphRelationship(
        string relationshipPartPath,
        XElement relationship,
        string targetPart)
    {
        if (!string.Equals(targetPart, "xl/xmlMaps.xml", StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.Equals(
                RelationshipPartToSourcePart(relationshipPartPath),
                "xl/workbook.xml",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(
            NormalizeRelationshipType(relationship),
            SpreadsheetRelationshipPrefix + "xmlMaps",
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWebExtensionPackageGraphRelationship(
        string relationshipPartPath,
        XElement relationship,
        string targetPart)
    {
        var sourcePart = RelationshipPartToSourcePart(relationshipPartPath);
        var relationshipType = NormalizeRelationshipType(relationship);
        if (string.Equals(sourcePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(targetPart, "xl/webextensions/taskpanes.xml", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(relationshipType, WebExtensionTaskpanesRelationshipType, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(sourcePart, "xl/webextensions/taskpanes.xml", StringComparison.OrdinalIgnoreCase) &&
               targetPart.StartsWith("xl/webextensions/webextension", StringComparison.OrdinalIgnoreCase) &&
               targetPart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(relationshipType, WebExtensionRelationshipType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCustomXmlPackageGraphRelationship(
        string relationshipPartPath,
        XElement relationship,
        string targetPart)
    {
        var relationshipType = NormalizeRelationshipType(relationship);
        var sourcePart = RelationshipPartToSourcePart(relationshipPartPath);
        if (string.Equals(sourcePart, "", StringComparison.Ordinal))
        {
            return IsCustomXmlItemPart(targetPart) &&
                   string.Equals(
                       relationshipType,
                       CustomXmlRelationshipType,
                       StringComparison.OrdinalIgnoreCase);
        }

        return IsCustomXmlItemPart(sourcePart) &&
               IsCustomXmlPropertiesPart(targetPart) &&
               string.Equals(
                   relationshipType,
                   CustomXmlPropertiesRelationshipType,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDataModelPackageGraphRelationship(
        string relationshipPartPath,
        XElement relationship,
        string targetPart)
    {
        if (IsDataModelPackagePart(targetPart))
            return true;

        if (!string.Equals(
                RelationshipPartToSourcePart(relationshipPartPath),
                "xl/workbook.xml",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relationshipType = NormalizeRelationshipType(relationship);
        return string.Equals(targetPart, "xl/connections.xml", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   relationshipType,
                   SpreadsheetRelationshipPrefix + "connections",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDataModelPackagePart(string targetPart)
    {
        if (!targetPart.StartsWith("xl/model/", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }

    private static bool IsWorkbookOrModernCommentPart(string sourcePart) =>
        string.Equals(sourcePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase) ||
        IsModernCommentPart(sourcePart);

    private static bool IsWorksheetOrModernCommentPart(string sourcePart) =>
        sourcePart.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
        sourcePart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
        IsModernCommentPart(sourcePart);

    private static bool IsModernCommentPart(string part) =>
        IsThreadedCommentPart(part) ||
        IsPersonPart(part) ||
        part.StartsWith("xl/threadedComments/", StringComparison.OrdinalIgnoreCase) ||
        part.StartsWith("xl/commentsExtensible/", StringComparison.OrdinalIgnoreCase) ||
        part.StartsWith("xl/commentAuthors/", StringComparison.OrdinalIgnoreCase) ||
        part.StartsWith("xl/people/", StringComparison.OrdinalIgnoreCase);

    private static bool IsThreadedCommentPart(string part) =>
        part.StartsWith("xl/threadedComments/", StringComparison.OrdinalIgnoreCase) &&
        part.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static bool IsPersonPart(string part) =>
        part.StartsWith("xl/persons/", StringComparison.OrdinalIgnoreCase) &&
        part.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static bool ShouldRebindRelationshipReferenceOnCopiedPart(XElement relationship)
    {
        var relationshipType = NormalizeRelationshipType(relationship);
        return string.Equals(relationshipType, ExternalLinkPathRelationshipType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(relationshipType, ImageRelationshipType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(relationshipType, PackageRelationshipType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(relationshipType, PivotCacheDefinitionRelationshipType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(relationshipType, PivotCacheRecordsRelationshipType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(relationshipType, WebExtensionRelationshipType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(relationshipType, SlicerRelationshipType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(relationshipType, SlicerCacheRelationshipType, StringComparison.OrdinalIgnoreCase) ||
               IsTimeline(relationshipType) ||
               IsTimelineCache(relationshipType);
    }

    private static void EnsureSlicerTimelineWorkbookExtensionReferences(ArchiveEntryIndex targetIndex)
    {
        var workbookEntry = targetIndex.Get("xl/workbook.xml");
        var relationshipsEntry = targetIndex.Get("xl/_rels/workbook.xml.rels");
        if (workbookEntry is null || relationshipsEntry is null)
            return;

        XDocument workbookXml;
        XDocument relationshipsXml;
        try
        {
            workbookXml = XlsxPackageXmlEditor.LoadXml(workbookEntry);
            relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
        }
        catch
        {
            return;
        }

        var workbookRoot = workbookXml.Root;
        var relationshipsRoot = relationshipsXml.Root;
        if (workbookRoot is null || relationshipsRoot is null)
            return;

        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
        XNamespace x15Ns = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";

        var slicerCacheRelationshipIds = FindWorkbookRelationshipIds(
            relationshipsRoot,
            packageRelNs,
            SlicerCacheRelationshipType,
            targetPart => targetPart.StartsWith("xl/slicerCaches/", StringComparison.OrdinalIgnoreCase));
        var timelineCacheRelationshipIds = FindWorkbookRelationshipIds(
            relationshipsRoot,
            packageRelNs,
            relationshipType => IsTimelineCache(relationshipType),
            targetPart => targetPart.StartsWith("xl/timelineCaches/", StringComparison.OrdinalIgnoreCase));

        if (slicerCacheRelationshipIds.Count == 0 && timelineCacheRelationshipIds.Count == 0)
            return;

        var changed = false;
        var extensionList = workbookRoot.Element(workbookNs + "extLst");
        if (extensionList is null)
        {
            extensionList = new XElement(workbookNs + "extLst");
            workbookRoot.Add(extensionList);
            changed = true;
        }

        changed |= EnsureWorkbookRelationshipExtensionReferences(
            extensionList,
            workbookNs,
            x14Ns,
            relNs,
            SlicerCachesWorkbookExtensionUri,
            x14Ns + "slicerCaches",
            x14Ns + "slicerCache",
            slicerCacheRelationshipIds);
        changed |= EnsureWorkbookRelationshipExtensionReferences(
            extensionList,
            workbookNs,
            x15Ns,
            relNs,
            TimelineCachesWorkbookExtensionUri,
            x15Ns + "timelineCacheRefs",
            x15Ns + "timelineCacheRef",
            timelineCacheRelationshipIds);

        if (changed)
            WriteXml(targetIndex, "xl/workbook.xml", workbookXml, workbookEntry.LastWriteTime, SaveOptions.DisableFormatting);
    }

    private static List<string> FindWorkbookRelationshipIds(
        XElement relationshipsRoot,
        XNamespace packageRelNs,
        string relationshipType,
        Func<string, bool> targetPredicate) =>
        FindWorkbookRelationshipIds(
            relationshipsRoot,
            packageRelNs,
            candidate => string.Equals(candidate, relationshipType, StringComparison.OrdinalIgnoreCase),
            targetPredicate);

    private static List<string> FindWorkbookRelationshipIds(
        XElement relationshipsRoot,
        XNamespace packageRelNs,
        Func<string?, bool> relationshipTypePredicate,
        Func<string, bool> targetPredicate)
    {
        var ids = new List<string>();
        foreach (var relationship in relationshipsRoot.Elements(packageRelNs + "Relationship"))
        {
            var id = relationship.Attribute("Id")?.Value;
            var target = NormalizeRelationshipTarget(relationship);
            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(target) ||
                !relationshipTypePredicate(NormalizeRelationshipType(relationship)))
            {
                continue;
            }

            var targetPart = XlsxPackagePath.ResolveRelationshipTarget("xl/workbook.xml", target);
            if (targetPredicate(targetPart))
                ids.Add(id);
        }

        return ids;
    }

    private static bool EnsureWorkbookRelationshipExtensionReferences(
        XElement extensionList,
        XNamespace workbookNs,
        XNamespace extensionNs,
        XNamespace relNs,
        string extensionUri,
        XName containerName,
        XName referenceName,
        IReadOnlyList<string> relationshipIds)
    {
        if (relationshipIds.Count == 0)
            return false;

        var extension = extensionList
            .Elements(workbookNs + "ext")
            .FirstOrDefault(element => string.Equals(element.Attribute("uri")?.Value, extensionUri, StringComparison.OrdinalIgnoreCase));
        var changed = false;
        if (extension is null)
        {
            extension = new XElement(
                workbookNs + "ext",
                new XAttribute("uri", extensionUri),
                new XAttribute(XNamespace.Xmlns + PreferredExtensionPrefix(extensionNs), extensionNs.NamespaceName));
            extensionList.Add(extension);
            changed = true;
        }
        else if (!string.Equals(extension.Attribute("uri")?.Value, extensionUri, StringComparison.Ordinal))
        {
            extension.SetAttributeValue("uri", extensionUri);
            changed = true;
        }

        var container = extension.Element(containerName);
        if (container is null)
        {
            container = new XElement(containerName);
            extension.Add(container);
            changed = true;
        }

        var existingIds = container
            .Elements(referenceName)
            .Select(element => element.Attribute(relNs + "id")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var relationshipId in relationshipIds)
        {
            if (existingIds.Contains(relationshipId))
                continue;

            container.Add(new XElement(referenceName, new XAttribute(relNs + "id", relationshipId)));
            existingIds.Add(relationshipId);
            changed = true;
        }

        return changed;
    }

    private static string PreferredExtensionPrefix(XNamespace extensionNs) =>
        string.Equals(extensionNs.NamespaceName, "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main", StringComparison.Ordinal)
            ? "x15"
            : "x14";

    private static void RebindCopiedPartRelationshipReferences(
        ArchiveEntryIndex targetIndex,
        string relationshipPartPath,
        IReadOnlySet<string> generatedEntriesBeforeMerge,
        IReadOnlyDictionary<string, string>? relationshipIdMap)
    {
        if (relationshipIdMap is null || relationshipIdMap.Count == 0)
            return;

        var sourcePart = RelationshipPartToSourcePart(relationshipPartPath);
        if (string.IsNullOrWhiteSpace(sourcePart) ||
            !sourcePart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (generatedEntriesBeforeMerge.Contains(sourcePart))
        {
            if (IsPivotCacheDefinitionPart(sourcePart))
                RebindGeneratedPivotCacheRecordsRelationshipReference(targetIndex, sourcePart, relationshipIdMap);
            else if (IsWebExtensionTaskpanesPart(sourcePart))
                RebindGeneratedWebExtensionTaskpanesRelationshipReference(targetIndex, sourcePart, relationshipIdMap);

            RebindGeneratedWorksheetPictureRelationshipReference(targetIndex, sourcePart, relationshipIdMap);
            RebindGeneratedSlicerTimelineRelationshipReferences(targetIndex, sourcePart, relationshipIdMap);
            return;
        }

        var targetEntry = targetIndex.Get(sourcePart);
        if (targetEntry is null)
            return;

        XDocument xml;
        try
        {
            xml = XlsxPackageXmlEditor.LoadXml(targetEntry);
        }
        catch
        {
            return;
        }

        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var changed = false;
        foreach (var attribute in xml.Descendants().Attributes().Where(attribute => attribute.Name.Namespace == relNs))
        {
            if (!relationshipIdMap.TryGetValue(attribute.Value, out var replacementId))
                continue;

            attribute.Value = replacementId;
            changed = true;
        }

        if (changed)
            WriteXml(targetIndex, sourcePart, xml, targetEntry.LastWriteTime, SaveOptions.DisableFormatting);
    }

    private static bool IsPivotCacheDefinitionPart(string sourcePart) =>
        sourcePart.StartsWith("xl/pivotCache/pivotCacheDefinition", StringComparison.OrdinalIgnoreCase) &&
        sourcePart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static bool IsWebExtensionTaskpanesPart(string sourcePart) =>
        string.Equals(sourcePart, "xl/webextensions/taskpanes.xml", StringComparison.OrdinalIgnoreCase);

    private static void RebindGeneratedPivotCacheRecordsRelationshipReference(
        ArchiveEntryIndex targetIndex,
        string sourcePart,
        IReadOnlyDictionary<string, string> relationshipIdMap)
    {
        var targetEntry = targetIndex.Get(sourcePart);
        if (targetEntry is null)
            return;

        XDocument xml;
        try
        {
            xml = XlsxPackageXmlEditor.LoadXml(targetEntry);
        }
        catch
        {
            return;
        }

        XNamespace pivotNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var root = xml.Root;
        if (root?.Name != pivotNs + "pivotCacheDefinition")
            return;

        var idAttribute = root.Attribute(relNs + "id");
        if (idAttribute is null ||
            !relationshipIdMap.TryGetValue(idAttribute.Value, out var replacementId))
        {
            return;
        }

        idAttribute.Value = replacementId;
        WriteXml(targetIndex, sourcePart, xml, targetEntry.LastWriteTime, SaveOptions.DisableFormatting);
    }

    private static void RebindGeneratedWebExtensionTaskpanesRelationshipReference(
        ArchiveEntryIndex targetIndex,
        string sourcePart,
        IReadOnlyDictionary<string, string> relationshipIdMap)
    {
        var targetEntry = targetIndex.Get(sourcePart);
        if (targetEntry is null)
            return;

        XDocument xml;
        try
        {
            xml = XlsxPackageXmlEditor.LoadXml(targetEntry);
        }
        catch
        {
            return;
        }

        XNamespace taskpanesNs = "http://schemas.microsoft.com/office/webextensions/taskpanes/2010/11";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var changed = false;
        foreach (var webExtensionRef in xml.Descendants(taskpanesNs + "webextensionref"))
        {
            var idAttribute = webExtensionRef.Attribute(relNs + "id");
            if (idAttribute is null ||
                !relationshipIdMap.TryGetValue(idAttribute.Value, out var replacementId))
            {
                continue;
            }

            idAttribute.Value = replacementId;
            changed = true;
        }

        if (changed)
            WriteXml(targetIndex, sourcePart, xml, targetEntry.LastWriteTime, SaveOptions.DisableFormatting);
    }

    private static void RebindGeneratedWorksheetPictureRelationshipReference(
        ArchiveEntryIndex targetIndex,
        string sourcePart,
        IReadOnlyDictionary<string, string> relationshipIdMap)
    {
        if (!sourcePart.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase))
            return;

        var targetEntry = targetIndex.Get(sourcePart);
        if (targetEntry is null)
            return;

        XDocument xml;
        try
        {
            xml = XlsxPackageXmlEditor.LoadXml(targetEntry);
        }
        catch
        {
            return;
        }

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var changed = false;
        foreach (var picture in xml.Root?.Elements(worksheetNs + "picture") ?? [])
        {
            var idAttribute = picture.Attribute(relNs + "id");
            if (idAttribute is null ||
                !relationshipIdMap.TryGetValue(idAttribute.Value, out var replacementId))
            {
                continue;
            }

            idAttribute.Value = replacementId;
            changed = true;
        }

        if (changed)
            WriteXml(targetIndex, sourcePart, xml, targetEntry.LastWriteTime, SaveOptions.DisableFormatting);
    }

    private static void RebindGeneratedSlicerTimelineRelationshipReferences(
        ArchiveEntryIndex targetIndex,
        string sourcePart,
        IReadOnlyDictionary<string, string> relationshipIdMap)
    {
        var isWorkbook = string.Equals(sourcePart, "xl/workbook.xml", StringComparison.OrdinalIgnoreCase);
        var isWorksheet = sourcePart.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase) &&
                          sourcePart.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
        if (!isWorkbook && !isWorksheet)
            return;

        var targetEntry = targetIndex.Get(sourcePart);
        if (targetEntry is null)
            return;

        XDocument xml;
        try
        {
            xml = XlsxPackageXmlEditor.LoadXml(targetEntry);
        }
        catch
        {
            return;
        }

        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var referenceNames = isWorkbook
            ? new HashSet<string>(["slicerCache", "timelineCacheRef"], StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(["slicer", "timelineRef"], StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var reference in xml.Descendants()
                     .Where(element => referenceNames.Contains(element.Name.LocalName)))
        {
            var idAttribute = reference.Attribute(relNs + "id");
            if (idAttribute is null ||
                !relationshipIdMap.TryGetValue(idAttribute.Value, out var replacementId))
            {
                continue;
            }

            idAttribute.Value = replacementId;
            changed = true;
        }

        if (changed)
            WriteXml(targetIndex, sourcePart, xml, targetEntry.LastWriteTime, SaveOptions.DisableFormatting);
    }

    private static bool IsStructurallyValidPackageRelationship(XElement relationship)
    {
        if (relationship.Attributes().Any(attribute =>
                !attribute.IsNamespaceDeclaration &&
                attribute.Name.NamespaceName.Length != 0))
        {
            return false;
        }

        if (relationship.Attributes().Any(attribute =>
                !attribute.IsNamespaceDeclaration &&
                attribute.Name.LocalName is not "Id" and not "Type" and not "Target" and not "TargetMode"))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(relationship.Attribute("Id")?.Value) ||
            string.IsNullOrWhiteSpace(relationship.Attribute("Type")?.Value) ||
            string.IsNullOrWhiteSpace(relationship.Attribute("Target")?.Value))
        {
            return false;
        }

        var targetMode = relationship.Attribute("TargetMode")?.Value;
        return string.IsNullOrWhiteSpace(targetMode) ||
               string.Equals(targetMode.Trim(), "External", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(targetMode.Trim(), "Internal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool RelationshipsPartTargetsOnlyExcludedParts(
        ZipArchiveEntry relationshipEntry,
        IReadOnlySet<string>? excludedSourceParts)
    {
        if (excludedSourceParts is null || excludedSourceParts.Count == 0)
            return false;

        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipEntry);
        var sourcePart = RelationshipPartToSourcePart(relationshipEntry.FullName);
        var relationships = relationshipsXml.Root?.Elements(relationshipNs + "Relationship").ToList() ?? [];
        return relationships.Count > 0 && relationships.All(relationship =>
        {
            if (IsExternalRelationship(relationship))
                return false;

            var target = NormalizeRelationshipTarget(relationship);
            return !string.IsNullOrWhiteSpace(target) &&
                   IsExcludedSourcePart(XlsxPackagePath.ResolveRelationshipTarget(sourcePart, target), excludedSourceParts);
        });
    }

    private static bool IsExcludedSourcePart(string? path, IReadOnlySet<string>? excludedSourceParts)
    {
        if (excludedSourceParts is null || excludedSourceParts.Count == 0 || string.IsNullOrWhiteSpace(path))
            return false;

        return excludedSourceParts.Contains(XlsxPackagePath.NormalizePackagePath(path.Trim()));
    }

    private static bool IsExternalRelationship(XElement relationship) =>
        string.Equals(NormalizeRelationshipTargetMode(relationship), "External", StringComparison.OrdinalIgnoreCase);

    private static string RelationshipSignature(XElement relationship) =>
        string.Join("|",
            NormalizeRelationshipType(relationship),
            NormalizeRelationshipTarget(relationship),
            NormalizeRelationshipTargetMode(relationship));

    private static string NormalizeRelationshipType(XElement relationship) =>
        relationship.Attribute("Type")?.Value.Trim() ?? "";

    private static string NormalizeRelationshipTarget(XElement relationship)
    {
        var target = relationship.Attribute("Target")?.Value.Trim() ?? "";
        return IsExternalRelationship(relationship) ? target : target.Replace('\\', '/');
    }

    private static string NormalizeRelationshipTargetMode(XElement relationship) =>
        relationship.Attribute("TargetMode")?.Value.Trim() ?? "";

    private static string RelationshipPartToSourcePart(string relationshipPartPath)
    {
        var normalized = XlsxPackagePath.NormalizePackagePath(relationshipPartPath);
        if (string.Equals(normalized, "_rels/.rels", StringComparison.OrdinalIgnoreCase))
            return "";

        const string relsSegment = "/_rels/";
        var relsIndex = normalized.IndexOf(relsSegment, StringComparison.OrdinalIgnoreCase);
        if (relsIndex < 0 || !normalized.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            return normalized;

        var directory = normalized[..relsIndex];
        var fileName = normalized[(relsIndex + relsSegment.Length)..^".rels".Length];
        return string.IsNullOrEmpty(directory) ? fileName : $"{directory}/{fileName}";
    }

    private sealed class ArchiveEntryIndex
    {
        private readonly ZipArchive _archive;
        private readonly Dictionary<string, List<ZipArchiveEntry>> _entries;

        private ArchiveEntryIndex(ZipArchive archive)
        {
            _archive = archive;
            _entries = new Dictionary<string, List<ZipArchiveEntry>>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in archive.Entries)
                Add(entry);
        }

        public static ArchiveEntryIndex Create(ZipArchive archive) => new(archive);

        public HashSet<string> EntryNames() => _entries.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> ValidPackageEntryNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in _entries.Keys)
            {
                if (TryNormalizeCopyableEntryName(name, out var normalized))
                    names.Add(normalized);
            }

            return names;
        }

        public ZipArchiveEntry? Get(string entryName)
        {
            var normalizedEntryName = NormalizeEntryName(entryName);
            return _entries.TryGetValue(normalizedEntryName, out var matches) ? FirstEntryMatch(matches) : null;
        }

        public bool Contains(string entryName) => Get(entryName) is not null;

        public ZipArchiveEntry Create(string entryName, CompressionLevel compressionLevel)
        {
            var entry = _archive.CreateEntry(entryName, compressionLevel);
            Add(entry);
            return entry;
        }

        public void Delete(string entryName)
        {
            var normalizedEntryName = NormalizeEntryName(entryName);
            if (!_entries.Remove(normalizedEntryName, out var matches))
                return;

            foreach (var entry in matches)
                entry.Delete();
        }

        private void Add(ZipArchiveEntry entry)
        {
            var normalizedEntryName = NormalizeEntryName(entry.FullName);
            if (!_entries.TryGetValue(normalizedEntryName, out var matches))
            {
                matches = [];
                _entries.Add(normalizedEntryName, matches);
            }

            matches.Add(entry);
        }

        private static string NormalizeEntryName(string entryName) =>
            XlsxPackagePath.NormalizePackagePath(entryName);

        private static ZipArchiveEntry? FirstEntryMatch(List<ZipArchiveEntry> matches) =>
            matches.Count == 0 ? null : matches[0];
    }
}
