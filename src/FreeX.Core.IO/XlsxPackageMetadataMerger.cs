using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxPackageMetadataMerger
{
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
        IReadOnlySet<string>? excludedSourceParts = null)
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
            if (TryNormalizeContentTypePartName(partName, out var normalizedPartName) &&
                targetPartNames.Contains(normalizedPartName) &&
                existingOverrides.Add(normalizedPartName))
            {
                var mergedOverride = new XElement(sourceOverride);
                mergedOverride.SetAttributeValue("PartName", $"/{normalizedPartName}");
                targetRoot.Add(mergedOverride);
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
                if (RelationshipsPartTargetsOnlyExcludedParts(sourceEntry, excludedSourceParts))
                    continue;

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
                    copy.SetAttributeValue("Id", XlsxPackageXmlEditor.NextRelationshipId(targetXml, relationshipNs));
                targetRoot.Add(copy);
                var copiedId = copy.Attribute("Id")?.Value;
                if (!string.IsNullOrWhiteSpace(copiedId))
                    existingIds.Add(copiedId);
                changed = true;
            }

            if (changed)
                WriteXml(targetIndex, targetEntry.FullName, targetXml, targetEntry.LastWriteTime, SaveOptions.DisableFormatting);
        }
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

        var targetPart = XlsxPackagePath.ResolveRelationshipTarget(RelationshipPartToSourcePart(relationshipPartPath), target);
        if (IsExcludedSourcePart(targetPart, excludedSourceParts))
            return false;

        return !string.IsNullOrWhiteSpace(targetPart) &&
               !generatedEntriesBeforeMerge.Contains(targetPart) &&
               targetIndex.Contains(targetPart);
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

        return excludedSourceParts.Contains(XlsxPackagePath.NormalizeZipPath(path.Trim().Replace('\\', '/').TrimStart('/')));
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
        var normalized = XlsxPackagePath.NormalizeZipPath(relationshipPartPath.Replace('\\', '/'));
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
            return _entries.TryGetValue(normalizedEntryName, out var matches) ? matches.FirstOrDefault() : null;
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
            XlsxPackagePath.NormalizeZipPath(entryName.Replace('\\', '/'));
    }
}
