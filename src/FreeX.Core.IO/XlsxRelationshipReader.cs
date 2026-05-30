using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxRelationshipReader
{
    public static Dictionary<string, string> ReadTargets(
        XDocument relationshipsXml,
        XNamespace packageRelNs,
        Func<string, string> resolveTarget)
    {
        var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in relationshipsXml.Root?.Elements(packageRelNs + "Relationship") ?? [])
        {
            var id = element.Attribute("Id")?.Value;
            var target = element.Attribute("Target")?.Value;
            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(target) ||
                IsExternalRelationship(element, target))
            {
                continue;
            }

            ref var targetPath = ref CollectionsMarshal.GetValueRefOrAddDefault(targets, id, out var exists);
            if (exists)
                continue;

            targetPath = resolveTarget(target);
        }

        return targets;
    }

    private static bool IsExternalRelationship(XElement relationship, string target)
    {
        if (string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
            return true;

        return Uri.TryCreate(target, UriKind.Absolute, out var uri) &&
               !string.IsNullOrWhiteSpace(uri.Scheme);
    }

    public static Dictionary<string, string> LoadTargets(
        ZipArchive archive,
        string relationshipsPath,
        string sourcePart,
        XNamespace packageRelNs)
    {
        var relationshipsEntry = archive.GetEntry(relationshipsPath);
        if (relationshipsEntry is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var relationshipsXml = LoadXml(relationshipsEntry);
        return ReadTargets(
            relationshipsXml,
            packageRelNs,
            target => XlsxPackagePath.ResolveRelationshipTarget(sourcePart, target));
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }
}
