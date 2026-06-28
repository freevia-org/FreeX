using System.IO.Compression;
using System.Xml.Linq;

namespace Free.Shared.Opc;

public readonly record struct OpcRelationship(
    string Id,
    string Type,
    string Target,
    bool IsExternal = false);

public static class OpcRelationships
{
    public static readonly XNamespace Namespace =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    public static XElement CreateRoot(params object?[] content) =>
        new(Namespace + "Relationships", content);

    public static XElement CreateRelationship(
        string id,
        string type,
        string target,
        bool external = false)
    {
        var element = new XElement(
            Namespace + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", type),
            new XAttribute("Target", target));

        if (external)
            element.Add(new XAttribute("TargetMode", "External"));

        return element;
    }

    public static XDocument CreateDocument(params object?[] relationships) =>
        new(new XDeclaration("1.0", "UTF-8", "yes"), CreateRoot(relationships));

    public static IReadOnlyList<OpcRelationship> Load(
        ZipArchive archive,
        string relsPath,
        bool ignoreMalformed = false)
    {
        var document = ignoreMalformed
            ? OpcXml.TryLoadXml(archive, relsPath)
            : OpcXml.LoadXmlOrNull(archive, relsPath);

        return document?.Root?
            .Elements(Namespace + "Relationship")
            .Select(element => new OpcRelationship(
                element.Attribute("Id")?.Value ?? string.Empty,
                element.Attribute("Type")?.Value ?? string.Empty,
                element.Attribute("Target")?.Value ?? string.Empty,
                string.Equals(element.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase)))
            .Where(relationship => !string.IsNullOrEmpty(relationship.Id))
            .ToList()
            ?? [];
    }

    public static string NextRelationshipId(XDocument relsXml, XNamespace? relationshipsNamespace = null)
    {
        var ns = relationshipsNamespace ?? Namespace;
        var used = relsXml.Root?
            .Elements(ns + "Relationship")
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

    public static string EnsureRelationshipForPackagePart(
        XDocument relsXml,
        XNamespace relationshipsNamespace,
        string sourcePart,
        string targetPart,
        string relationshipType,
        Func<string, string, string> resolveRelationshipTarget,
        Func<string, string, string> createRelationshipTarget)
    {
        var root = relsXml.Root;
        if (root is null)
        {
            root = new XElement(relationshipsNamespace + "Relationships");
            relsXml.Add(root);
        }

        foreach (var relationship in root.Elements(relationshipsNamespace + "Relationship"))
        {
            var type = relationship.Attribute("Type")?.Value;
            var target = relationship.Attribute("Target")?.Value;
            if (!string.Equals(type, relationshipType, StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            var resolvedTarget = resolveRelationshipTarget(sourcePart, target);
            if (string.Equals(resolvedTarget, targetPart, StringComparison.OrdinalIgnoreCase))
                return relationship.Attribute("Id")?.Value ?? string.Empty;
        }

        var id = NextRelationshipId(relsXml, relationshipsNamespace);
        root.Add(new XElement(
            relationshipsNamespace + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", relationshipType),
            new XAttribute("Target", createRelationshipTarget(sourcePart, targetPart))));
        return id;
    }
}

public sealed class OpcRelationshipDocument
{
    private readonly List<OpcRelationship> _relationships = [];
    private readonly string _preservedIdPrefix;

    public OpcRelationshipDocument(string preservedIdPrefix = "rIdPreserved")
    {
        _preservedIdPrefix = preservedIdPrefix;
    }

    public void Add(string id, string type, string target, bool external = false) =>
        _relationships.Add(new OpcRelationship(id, type, target, external));

    public void AddUnique(string id, string type, string target, bool external = false)
    {
        if (_relationships.Any(r =>
                string.Equals(r.Type, type, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(r.Target, target, StringComparison.OrdinalIgnoreCase) &&
                r.IsExternal == external))
        {
            return;
        }

        var uniqueId = id;
        if (_relationships.Any(r => string.Equals(r.Id, uniqueId, StringComparison.OrdinalIgnoreCase)))
            uniqueId = NextRelationshipId();

        Add(uniqueId, type, target, external);
    }

    public XDocument ToXDocument() =>
        OpcRelationships.CreateDocument(_relationships.Select(r =>
            OpcRelationships.CreateRelationship(r.Id, r.Type, r.Target, r.IsExternal)));

    private string NextRelationshipId()
    {
        var counter = 1;
        string id;
        do
        {
            id = $"{_preservedIdPrefix}{counter++}";
        }
        while (_relationships.Any(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)));

        return id;
    }
}
