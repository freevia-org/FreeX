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
        foreach (var relationship in OpcRelationships.Load(relationshipsXml, packageRelNs))
        {
            if (string.IsNullOrWhiteSpace(relationship.Target) ||
                IsExternalRelationship(relationship))
            {
                continue;
            }

            ref var targetPath = ref CollectionsMarshal.GetValueRefOrAddDefault(targets, relationship.Id, out var exists);
            if (exists)
                continue;

            targetPath = resolveTarget(relationship.Target);
        }

        return targets;
    }

    private static bool IsExternalRelationship(OpcRelationship relationship)
    {
        if (relationship.IsExternal)
            return true;

        return Uri.TryCreate(relationship.Target, UriKind.Absolute, out var uri) &&
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

        var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
        return ReadTargets(
            relationshipsXml,
            packageRelNs,
            target => XlsxPackagePath.ResolveRelationshipTarget(sourcePart, target));
    }
}
