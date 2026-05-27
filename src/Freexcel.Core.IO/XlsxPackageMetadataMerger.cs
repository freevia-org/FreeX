using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxPackageMetadataMerger
{
    public static IReadOnlySet<string> CopyUnknownPackageParts(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        IReadOnlySet<string>? excludedSourceParts = null)
    {
        var generatedEntriesBeforeMerge = targetArchive.Entries
            .Select(entry => XlsxPackagePath.NormalizeZipPath(entry.FullName.Replace('\\', '/')))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceEntry in sourceArchive.Entries)
        {
            if (IsExcludedSourcePart(sourceEntry.FullName, excludedSourceParts))
                continue;
            if (IsPackageMetadataEntry(sourceEntry.FullName))
                continue;
            if (targetArchive.GetEntry(sourceEntry.FullName) is not null)
                continue;

            CopyEntry(sourceEntry, targetArchive);
        }

        return generatedEntriesBeforeMerge;
    }

    public static void CopyEntry(ZipArchiveEntry sourceEntry, ZipArchive targetArchive)
    {
        var targetEntry = targetArchive.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
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
        var targetEntry = targetArchive.GetEntry("[Content_Types].xml");
        if (sourceEntry is null || targetEntry is null)
            return;

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var sourceXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var targetXml = XlsxPackageXmlEditor.LoadXml(targetEntry);
        var targetRoot = targetXml.Root;
        var sourceRoot = sourceXml.Root;
        if (targetRoot is null || sourceRoot is null)
            return;

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
                targetRoot.Add(new XElement(sourceDefault));
        }

        var existingOverrides = targetRoot
            .Elements(contentTypeNs + "Override")
            .Select(element => element.Attribute("PartName")?.Value)
            .OfType<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeContentTypePartName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var sourceOverride in sourceRoot.Elements(contentTypeNs + "Override"))
        {
            var partName = sourceOverride.Attribute("PartName")?.Value;
            if (IsExcludedSourcePart(partName, excludedSourceParts))
                continue;
            if (!string.IsNullOrWhiteSpace(partName) && existingOverrides.Add(NormalizeContentTypePartName(partName)))
                targetRoot.Add(new XElement(sourceOverride));
        }

        XlsxPackageXmlEditor.ReplaceXml(targetArchive, "[Content_Types].xml", targetXml);
    }

    public static void MergeRelationshipParts(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        IReadOnlySet<string> generatedEntriesBeforeMerge,
        IReadOnlySet<string>? excludedSourceParts = null)
    {
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        foreach (var sourceEntry in sourceArchive.Entries.Where(entry =>
                     entry.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            if (IsExcludedSourcePart(sourceEntry.FullName, excludedSourceParts))
                continue;

            var targetEntry = targetArchive.GetEntry(sourceEntry.FullName);
            if (targetEntry is null)
            {
                if (RelationshipsPartTargetsOnlyExcludedParts(sourceEntry, excludedSourceParts))
                    continue;

                CopyEntry(sourceEntry, targetArchive);
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

            foreach (var sourceRelationship in sourceRoot.Elements(relationshipNs + "Relationship"))
            {
                if (!ShouldPreserveRelationship(
                        sourceEntry.FullName,
                        sourceRelationship,
                        targetArchive,
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
            }

            XlsxPackageXmlEditor.ReplaceXml(targetArchive, sourceEntry.FullName, targetXml);
        }
    }

    private static bool IsPackageMetadataEntry(string entryName) =>
        string.Equals(entryName, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase) ||
        entryName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeContentTypePartName(string value) =>
        XlsxPackagePath.NormalizeZipPath(value.Trim().Replace('\\', '/').TrimStart('/'));

    private static string NormalizeContentTypeExtension(string value) =>
        value.Trim().TrimStart('.');

    private static bool ShouldPreserveRelationship(
        string relationshipPartPath,
        XElement relationship,
        ZipArchive targetArchive,
        IReadOnlySet<string> generatedEntriesBeforeMerge,
        IReadOnlySet<string>? excludedSourceParts)
    {
        var relationshipType = relationship.Attribute("Type")?.Value;
        if (string.Equals(
                relationshipType,
                "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
            return false;

        if (IsExternalRelationship(relationship))
            return true;

        var targetPart = XlsxPackagePath.ResolveRelationshipTarget(RelationshipPartToSourcePart(relationshipPartPath), target);
        if (IsExcludedSourcePart(targetPart, excludedSourceParts))
            return false;

        return !string.IsNullOrWhiteSpace(targetPart) &&
               !generatedEntriesBeforeMerge.Contains(targetPart) &&
               targetArchive.GetEntry(targetPart) is not null;
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

            var target = relationship.Attribute("Target")?.Value;
            return !string.IsNullOrWhiteSpace(target) &&
                   IsExcludedSourcePart(XlsxPackagePath.ResolveRelationshipTarget(sourcePart, target), excludedSourceParts);
        });
    }

    private static bool IsExcludedSourcePart(string? path, IReadOnlySet<string>? excludedSourceParts)
    {
        if (excludedSourceParts is null || excludedSourceParts.Count == 0 || string.IsNullOrWhiteSpace(path))
            return false;

        return excludedSourceParts.Contains(XlsxPackagePath.NormalizeZipPath(path.Replace('\\', '/').TrimStart('/')));
    }

    private static bool IsExternalRelationship(XElement relationship) =>
        string.Equals(NormalizeRelationshipTargetMode(relationship), "External", StringComparison.OrdinalIgnoreCase);

    private static string RelationshipSignature(XElement relationship) =>
        string.Join("|",
            relationship.Attribute("Type")?.Value ?? "",
            relationship.Attribute("Target")?.Value ?? "",
            NormalizeRelationshipTargetMode(relationship));

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
}
