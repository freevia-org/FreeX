using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxExtensionListPackageRelationshipRebinder
{
    private static readonly XNamespace PackageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static IReadOnlyDictionary<string, string> BuildRelationshipIdMap(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string sourcePartPath,
        string targetPartPath)
    {
        var sourceRelationshipsEntry = sourceArchive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(sourcePartPath));
        var targetRelationshipsEntry = targetArchive.GetEntry(XlsxPackagePath.GetRelationshipPartPath(targetPartPath));
        if (sourceRelationshipsEntry is null || targetRelationshipsEntry is null)
            return EmptyMap();

        var sourceRelationshipsXml = XlsxPackageXmlEditor.LoadXml(sourceRelationshipsEntry);
        var targetRelationshipsXml = XlsxPackageXmlEditor.LoadXml(targetRelationshipsEntry);
        var targetIdsBySignature = targetRelationshipsXml.Root?
            .Elements(PackageRelNs + "Relationship")
            .Select(relationship => new
            {
                Key = RelationshipSignature(relationship, targetPartPath),
                Id = relationship.Attribute("Id")?.Value
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.Key) && !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Id!, StringComparer.OrdinalIgnoreCase);
        if (targetIdsBySignature is null || targetIdsBySignature.Count == 0)
            return EmptyMap();

        Dictionary<string, string>? map = null;
        foreach (var sourceRelationship in sourceRelationshipsXml.Root?.Elements(PackageRelNs + "Relationship") ?? [])
        {
            var sourceId = sourceRelationship.Attribute("Id")?.Value;
            if (string.IsNullOrWhiteSpace(sourceId))
                continue;

            var key = RelationshipSignature(sourceRelationship, sourcePartPath);
            if (string.IsNullOrWhiteSpace(key) ||
                !targetIdsBySignature.TryGetValue(key, out var targetId) ||
                string.Equals(sourceId, targetId, StringComparison.Ordinal))
            {
                continue;
            }

            map ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            map[sourceId] = targetId;
        }

        return map ?? EmptyMap();
    }

    private static IReadOnlyDictionary<string, string> EmptyMap() =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static string RelationshipSignature(XElement relationship, string sourcePartPath)
    {
        var type = relationship.Attribute("Type")?.Value.Trim();
        var target = relationship.Attribute("Target")?.Value.Trim();
        if (string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(target))
            return "";

        var targetMode = relationship.Attribute("TargetMode")?.Value.Trim() ?? "";
        var normalizedTarget = string.Equals(targetMode, "External", StringComparison.OrdinalIgnoreCase)
            ? target
            : XlsxPackagePath.ResolveRelationshipTarget(sourcePartPath, target.Replace('\\', '/'));

        return $"{type}\u001f{targetMode}\u001f{normalizedTarget}";
    }
}
