using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxRelationshipReader
{
    public static Dictionary<string, string> ReadTargets(
        XDocument relationshipsXml,
        XNamespace packageRelNs,
        Func<string, string> resolveTarget)
        => OpcRelationships.LoadInternalTargetMap(relationshipsXml, packageRelNs, resolveTarget);

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

    public static Dictionary<string, string> LoadTargetsStrict(
        ZipArchive archive,
        string relationshipsPath,
        Func<string, string> resolveTarget,
        XNamespace packageRelNs)
    {
        var relationshipsEntry = archive.GetEntry(relationshipsPath);
        if (relationshipsEntry is null)
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
        return relationshipsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Where(element => element.Attribute("Id") is not null && element.Attribute("Target") is not null)
            .ToDictionary(
                element => element.Attribute("Id")!.Value,
                element => resolveTarget(element.Attribute("Target")!.Value),
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
}
