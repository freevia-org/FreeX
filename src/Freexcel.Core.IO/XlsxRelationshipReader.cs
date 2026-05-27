using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

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
                targets.ContainsKey(id) ||
                string.Equals(element.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            targets[id] = resolveTarget(target);
        }

        return targets;
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
