using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace FreeW.Core.IO.Tests;

internal sealed class DocxPackageInventory
{
    private static readonly XNamespace Ct = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";

    private DocxPackageInventory(
        IReadOnlyDictionary<string, byte[]> parts,
        IReadOnlyDictionary<string, string> contentTypeDefaults,
        IReadOnlyDictionary<string, string> contentTypeOverrides,
        IReadOnlyDictionary<string, IReadOnlyList<DocxPackageRelationship>> relationshipsByPart)
    {
        Parts = parts;
        ContentTypeDefaults = contentTypeDefaults;
        ContentTypeOverrides = contentTypeOverrides;
        RelationshipsByPart = relationshipsByPart;
    }

    public IReadOnlyDictionary<string, byte[]> Parts { get; }

    public IReadOnlyDictionary<string, string> ContentTypeDefaults { get; }

    public IReadOnlyDictionary<string, string> ContentTypeOverrides { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<DocxPackageRelationship>> RelationshipsByPart { get; }

    public static DocxPackageInventory Read(byte[] docx)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        var parts = zip.Entries
            .Where(entry => !entry.FullName.EndsWith("/", StringComparison.Ordinal))
            .ToDictionary(entry => entry.FullName, ReadEntry, StringComparer.Ordinal);

        var contentTypeDefaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var contentTypeOverrides = new Dictionary<string, string>(StringComparer.Ordinal);
        if (parts.TryGetValue("[Content_Types].xml", out var contentTypesBytes))
        {
            var contentTypes = XDocument.Load(new MemoryStream(contentTypesBytes)).Root!;
            foreach (var item in contentTypes.Elements(Ct + "Default"))
                contentTypeDefaults[item.Attribute("Extension")!.Value] = item.Attribute("ContentType")!.Value;
            foreach (var item in contentTypes.Elements(Ct + "Override"))
                contentTypeOverrides[item.Attribute("PartName")!.Value] = item.Attribute("ContentType")!.Value;
        }

        var relationshipsByPart = parts
            .Where(part => part.Key.EndsWith(".rels", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                part => part.Key,
                part => (IReadOnlyList<DocxPackageRelationship>)XDocument.Load(new MemoryStream(part.Value))
                    .Root!
                    .Elements(Rel + "Relationship")
                    .Select(element => new DocxPackageRelationship(
                        element.Attribute("Id")?.Value ?? string.Empty,
                        element.Attribute("Type")?.Value ?? string.Empty,
                        element.Attribute("Target")?.Value ?? string.Empty,
                        element.Attribute("TargetMode")?.Value))
                    .ToList(),
                StringComparer.Ordinal);

        return new DocxPackageInventory(parts, contentTypeDefaults, contentTypeOverrides, relationshipsByPart);
    }

    public void ShouldPreserveVerbatim(DocxPackageInventory source, params string[] entryPaths)
    {
        foreach (var entryPath in entryPaths)
        {
            source.Parts.Should().ContainKey(entryPath);
            Parts.Should().ContainKey(entryPath);
            Parts[entryPath].Should().Equal(source.Parts[entryPath], $"package entry {entryPath} should survive byte-for-byte");
        }
    }

    public void ShouldDeclareDefault(string extension, string contentType)
    {
        ContentTypeDefaults.Should().ContainKey(extension);
        ContentTypeDefaults[extension].Should().Be(contentType);
    }

    public void ShouldDeclareOverride(string partName, string contentType)
    {
        ContentTypeOverrides.Should().ContainKey(partName);
        ContentTypeOverrides[partName].Should().Be(contentType);
    }

    public void ShouldContainRelationship(string relationshipPart, string type, string target)
    {
        RelationshipsByPart.Should().ContainKey(relationshipPart);
        RelationshipsByPart[relationshipPart].Should().Contain(relationship =>
            relationship.Type == type && relationship.Target == target);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

internal readonly record struct DocxPackageRelationship(
    string Id,
    string Type,
    string Target,
    string? TargetMode);
