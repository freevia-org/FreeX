using System.IO.Compression;
using System.Xml.Linq;

namespace Freexcel.Core.IO;

internal static class XlsxPackageXmlEditor
{
    public static void ReplaceXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        document.Save(stream);
    }

    public static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    public static string NextRelationshipId(XDocument relsXml, XNamespace packageRelNs)
    {
        var used = relsXml.Root?
            .Elements(packageRelNs + "Relationship")
            .Select(e => e.Attribute("Id")?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];

        for (var i = 1; ; i++)
        {
            var candidate = $"rId{i}";
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    public static void EnsureDefaultContentType(ZipArchive archive, string extension, string contentType)
    {
        const string contentTypesPath = "[Content_Types].xml";
        var entry = archive.GetEntry(contentTypesPath);
        if (entry is null)
            return;

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var xml = LoadXml(entry);
        var hasDefault = xml.Root?
            .Elements(contentTypeNs + "Default")
            .Any(e => string.Equals(e.Attribute("Extension")?.Value, extension, StringComparison.OrdinalIgnoreCase))
            == true;
        if (hasDefault)
            return;

        xml.Root?.Add(new XElement(
            contentTypeNs + "Default",
            new XAttribute("Extension", extension),
            new XAttribute("ContentType", contentType)));

        ReplaceXml(archive, contentTypesPath, xml);
    }

    public static void EnsureSpecificContentType(ZipArchive archive, string partName, string contentType)
    {
        const string contentTypesPath = "[Content_Types].xml";
        var entry = archive.GetEntry(contentTypesPath);
        if (entry is null)
            return;

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var xml = LoadXml(entry);
        var root = xml.Root;
        if (root is null)
            return;

        var normalizedPartName = partName.StartsWith('/') ? partName : $"/{partName}";
        root.Elements(contentTypeNs + "Override")
            .Where(element => string.Equals(element.Attribute("PartName")?.Value, normalizedPartName, StringComparison.OrdinalIgnoreCase))
            .Remove();
        root.Add(new XElement(
            contentTypeNs + "Override",
            new XAttribute("PartName", normalizedPartName),
            new XAttribute("ContentType", contentType)));

        ReplaceXml(archive, contentTypesPath, xml);
    }

    public static string EnsureRelationshipForPackagePart(
        XDocument relsXml,
        XNamespace packageRelNs,
        string sourcePart,
        string targetPart,
        string relationshipType)
    {
        var root = relsXml.Root;
        if (root is null)
        {
            root = new XElement(packageRelNs + "Relationships");
            relsXml.Add(root);
        }

        foreach (var relationship in root.Elements(packageRelNs + "Relationship"))
        {
            var type = relationship.Attribute("Type")?.Value;
            var target = relationship.Attribute("Target")?.Value;
            if (!string.Equals(type, relationshipType, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            var resolvedTarget = XlsxPackagePath.ResolveRelationshipTarget(sourcePart, target);
            if (string.Equals(resolvedTarget, targetPart, StringComparison.OrdinalIgnoreCase))
                return relationship.Attribute("Id")?.Value ?? "";
        }

        var id = NextRelationshipId(relsXml, packageRelNs);
        root.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", relationshipType),
            new XAttribute("Target", XlsxPackagePath.GetRelationshipTarget(sourcePart, targetPart))));
        return id;
    }
}
