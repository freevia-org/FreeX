using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxPackageXmlEditor
{
    public static void ReplaceXml(ZipArchive archive, string entryName, XDocument document)
    {
        // Defense in depth: a crafted package may contain multiple entries with the same name
        // (ZipArchive tolerates duplicates; GetEntry returns only the first).  Delete all of them
        // before creating the authoritative replacement so no stale duplicate can be read back.
        OpcXml.ReplaceXmlEntry(archive, entryName, document);
    }

    public static XDocument LoadXml(ZipArchiveEntry entry)
        => OpcXml.LoadXml(entry);

    public static XDocument LoadXml(Stream stream, long maxCharactersInDocument = SecureXmlReaderSettings.DefaultMaxCharactersInDocument)
        => OpcXml.LoadXml(stream, maxCharactersInDocument);

    public static string NextRelationshipId(XDocument relsXml, XNamespace packageRelNs)
        => OpcRelationships.NextRelationshipId(relsXml, packageRelNs);

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
        => OpcRelationships.EnsureRelationshipForPackagePart(
            relsXml,
            packageRelNs,
            sourcePart,
            targetPart,
            relationshipType,
            XlsxPackagePath.ResolveRelationshipTarget,
            XlsxPackagePath.GetRelationshipTarget);
}
