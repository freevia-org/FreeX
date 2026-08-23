using System.IO.Compression;
using System.Xml.Linq;

namespace Free.Shared.Opc;

public readonly record struct OpcRelationship(
    string Id,
    string Type,
    string Target,
    bool IsExternal = false);

public readonly record struct OpcCanonicalRelationship(
    string PartName,
    string RelationshipType);

public readonly record struct OpcRelationshipTarget(
    string Id,
    string Type,
    string Target,
    bool IsExternal = false)
{
    public void Deconstruct(out string id, out string type, out string target)
    {
        id = Id;
        type = Type;
        target = Target;
    }
}

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

    public static bool IsStructurallyValidRelationship(XElement relationship)
    {
        if (relationship.Attributes().Any(attribute =>
                !attribute.IsNamespaceDeclaration &&
                attribute.Name.NamespaceName.Length != 0))
        {
            return false;
        }

        if (relationship.Attributes().Any(attribute =>
                !attribute.IsNamespaceDeclaration &&
                attribute.Name.LocalName is not "Id" and not "Type" and not "Target" and not "TargetMode"))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(relationship.Attribute("Id")?.Value) ||
            string.IsNullOrWhiteSpace(relationship.Attribute("Type")?.Value) ||
            string.IsNullOrWhiteSpace(relationship.Attribute("Target")?.Value))
        {
            return false;
        }

        var targetMode = relationship.Attribute("TargetMode")?.Value;
        return string.IsNullOrWhiteSpace(targetMode) ||
               string.Equals(targetMode.Trim(), "External", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(targetMode.Trim(), "Internal", StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<OpcRelationship> Load(
        ZipArchive archive,
        string relsPath,
        bool ignoreMalformed = false)
    {
        var document = ignoreMalformed
            ? OpcXml.TryLoadXml(archive, relsPath)
            : OpcXml.LoadXmlOrNull(archive, relsPath);

        return document is null ? [] : Load(document);
    }

    public static IReadOnlyList<OpcRelationship> Load(
        XDocument relationshipsXml,
        XNamespace relationshipsNamespace)
    {
        return relationshipsXml.Root?
            .Elements(relationshipsNamespace + "Relationship")
            .Select(element => new OpcRelationship(
                element.Attribute("Id")?.Value ?? string.Empty,
                element.Attribute("Type")?.Value ?? string.Empty,
                element.Attribute("Target")?.Value ?? string.Empty,
                string.Equals(element.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase)))
            .Where(relationship => !string.IsNullOrEmpty(relationship.Id))
            .ToList()
        ?? [];
    }

    public static IReadOnlyList<OpcRelationship> Load(XDocument relationshipsXml) =>
        Load(relationshipsXml, Namespace);

    /// <summary>
    /// Enumerates internal relationship targets after filtering external/absolute targets and
    /// retaining the first occurrence of each relationship ID.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string>> EnumerateInternalTargetMap(
        XDocument relationshipsXml,
        XNamespace relationshipsNamespace,
        Func<string, string> resolveTarget,
        IEqualityComparer<string>? idComparer = null)
    {
        ArgumentNullException.ThrowIfNull(relationshipsXml);
        ArgumentNullException.ThrowIfNull(resolveTarget);

        var seenIds = new HashSet<string>(idComparer ?? StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in Load(relationshipsXml, relationshipsNamespace))
        {
            if (string.IsNullOrWhiteSpace(relationship.Target) ||
                IsExternalRelationship(relationship) ||
                !seenIds.Add(relationship.Id))
            {
                continue;
            }

            yield return new KeyValuePair<string, string>(
                relationship.Id,
                resolveTarget(relationship.Target));
        }
    }

    /// <summary>Loads <see cref="EnumerateInternalTargetMap"/> into a reusable ID-to-path map.</summary>
    public static Dictionary<string, string> LoadInternalTargetMap(
        XDocument relationshipsXml,
        XNamespace relationshipsNamespace,
        Func<string, string> resolveTarget,
        IEqualityComparer<string>? idComparer = null)
    {
        var comparer = idComparer ?? StringComparer.OrdinalIgnoreCase;
        var map = new Dictionary<string, string>(comparer);
        foreach (var pair in EnumerateInternalTargetMap(
                     relationshipsXml,
                     relationshipsNamespace,
                     resolveTarget,
                     comparer))
        {
            map.Add(pair.Key, pair.Value);
        }

        return map;
    }

    public static List<OpcRelationshipTarget> LoadTargets(
        ZipArchive archive,
        string relsPath,
        bool ignoreMalformed = false) =>
        Load(archive, relsPath, ignoreMalformed)
            .Select(relationship => new OpcRelationshipTarget(
                relationship.Id,
                relationship.Type,
                relationship.Target,
                relationship.IsExternal))
            .ToList();

    public static string? FirstTargetByType(
        IEnumerable<OpcRelationshipTarget> relationships,
        string relationshipType) =>
        relationships.FirstOrDefault(relationship =>
            string.Equals(relationship.Type, relationshipType, StringComparison.Ordinal)).Target is { Length: > 0 } target
                ? target
                : null;

    public static Dictionary<string, OpcRelationship> LoadById(
        ZipArchive archive,
        string relsPath,
        bool ignoreMalformed = false,
        IEqualityComparer<string>? idComparer = null)
    {
        var map = new Dictionary<string, OpcRelationship>(idComparer ?? StringComparer.Ordinal);
        foreach (var relationship in Load(archive, relsPath, ignoreMalformed))
        {
            if (string.IsNullOrEmpty(relationship.Type) ||
                string.IsNullOrEmpty(relationship.Target))
            {
                continue;
            }

            map[relationship.Id] = relationship;
        }

        return map;
    }

    private static bool IsExternalRelationship(OpcRelationship relationship)
    {
        if (relationship.IsExternal)
            return true;

        // OPC package-root targets such as /xl/worksheets/sheet1.xml are absolute paths within
        // the package, not URI references to the host filesystem. Uri classifying them differs
        // by platform (Linux can expose them as file: URIs), so keep the package-root form internal.
        if (relationship.Target.StartsWith("/", StringComparison.Ordinal) &&
            !relationship.Target.StartsWith("//", StringComparison.Ordinal))
            return false;

        return Uri.TryCreate(relationship.Target, UriKind.Absolute, out var uri) &&
               !string.IsNullOrWhiteSpace(uri.Scheme);
    }

    public static Dictionary<string, string> LoadTargetMap(
        ZipArchive archive,
        string relsPath,
        Func<OpcRelationship, string?> resolveTarget,
        Func<OpcRelationship, bool>? predicate = null,
        bool ignoreMalformed = false,
        IEqualityComparer<string>? idComparer = null)
    {
        var map = new Dictionary<string, string>(idComparer ?? StringComparer.Ordinal);
        foreach (var relationship in Load(archive, relsPath, ignoreMalformed))
        {
            if (string.IsNullOrEmpty(relationship.Target) ||
                predicate?.Invoke(relationship) == false)
            {
                continue;
            }

            var target = resolveTarget(relationship);
            if (!string.IsNullOrEmpty(target))
                map[relationship.Id] = target;
        }

        return map;
    }

    public static Dictionary<string, string> LoadTypeByTargetMap(
        ZipArchive archive,
        string relsPath,
        bool ignoreMalformed = false,
        IEqualityComparer<string>? targetComparer = null)
    {
        var map = new Dictionary<string, string>(targetComparer ?? StringComparer.Ordinal);
        foreach (var relationship in Load(archive, relsPath, ignoreMalformed))
        {
            if (!string.IsNullOrEmpty(relationship.Target) &&
                !string.IsNullOrEmpty(relationship.Type))
            {
                map[relationship.Target] = relationship.Type;
            }
        }

        return map;
    }

    public static string NextRelationshipId(
        XDocument relsXml,
        XNamespace? relationshipsNamespace = null,
        IReadOnlyCollection<string>? additionalReservedIds = null)
    {
        var ns = relationshipsNamespace ?? Namespace;
        var used = relsXml.Root?
            .Elements(ns + "Relationship")
            .Select(e => e.Attribute("Id")?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? [];

        // R102-io-external-link-authoring-mint-collision-1: a caller can know about r:id values that
        // are already claimed elsewhere in the package (e.g. an unbacked <externalReference>
        // placeholder in workbook.xml that deliberately has no Relationship element yet) but that
        // this document's own Relationship elements can't reveal on their own. Folding those into the
        // "used" set here keeps the newly minted id from colliding with one of them.
        if (additionalReservedIds is not null)
        {
            foreach (var reservedId in additionalReservedIds)
            {
                if (!string.IsNullOrWhiteSpace(reservedId))
                    used.Add(reservedId);
            }
        }

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
        Func<string, string, string> createRelationshipTarget,
        IReadOnlyCollection<string>? additionalReservedIdsForMinting = null)
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

        var id = NextRelationshipId(relsXml, relationshipsNamespace, additionalReservedIdsForMinting);
        root.Add(new XElement(
            relationshipsNamespace + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", relationshipType),
            new XAttribute("Target", createRelationshipTarget(sourcePart, targetPart))));
        return id;
    }

    public static bool NeedsCanonicalPackageRelationshipNormalization(
        XDocument relationshipsXml,
        OpcCanonicalRelationship canonicalRelationship,
        bool partExists,
        Func<string, string?> resolveRelationshipTarget,
        XNamespace? relationshipsNamespace = null)
    {
        var clone = new XDocument(relationshipsXml);
        return NormalizeCanonicalPackageRelationship(
            clone,
            canonicalRelationship,
            partExists,
            resolveRelationshipTarget,
            relationshipsNamespace);
    }

    public static bool NormalizeCanonicalPackageRelationship(
        XDocument relationshipsXml,
        OpcCanonicalRelationship canonicalRelationship,
        bool partExists,
        Func<string, string?> resolveRelationshipTarget,
        XNamespace? relationshipsNamespace = null)
    {
        var ns = relationshipsNamespace ?? Namespace;
        var root = relationshipsXml.Root;
        if (root is null || root.Name != ns + "Relationships")
            return false;

        var changed = false;
        var relatedRelationships = root
            .Elements(ns + "Relationship")
            .Where(relationship =>
                RelationshipTypeMatches(relationship, canonicalRelationship.RelationshipType) ||
                RelationshipTargetsPart(
                    relationship,
                    canonicalRelationship.PartName,
                    resolveRelationshipTarget))
            .ToList();

        XElement? keptRelationship = null;
        foreach (var relationship in relatedRelationships)
        {
            if (!partExists)
            {
                relationship.Remove();
                changed = true;
                continue;
            }

            var target = relationship.Attribute("Target")?.Value?.Trim();
            var targetMode = relationship.Attribute("TargetMode")?.Value?.Trim();
            var targetsPart = RelationshipTargetsPart(
                relationship,
                canonicalRelationship.PartName,
                resolveRelationshipTarget);

            if (targetsPart &&
                keptRelationship is null &&
                RelationshipTypeMatches(relationship, canonicalRelationship.RelationshipType))
            {
                keptRelationship = relationship;
                if (!string.Equals(target, canonicalRelationship.PartName, StringComparison.Ordinal))
                {
                    relationship.SetAttributeValue("Target", canonicalRelationship.PartName);
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(targetMode))
                {
                    relationship.SetAttributeValue("TargetMode", null);
                    changed = true;
                }

                continue;
            }

            relationship.Remove();
            changed = true;
        }

        if (!partExists || keptRelationship is not null)
            return changed;

        root.Add(CreateRelationship(
            NextRelationshipId(relationshipsXml, ns),
            canonicalRelationship.RelationshipType,
            canonicalRelationship.PartName));
        return true;
    }

    public static bool RelationshipTypeMatches(XElement relationship, string relationshipType) =>
        string.Equals(
            relationship.Attribute("Type")?.Value?.Trim(),
            relationshipType,
            StringComparison.OrdinalIgnoreCase);

    public static bool RelationshipTargetsPart(
        XElement relationship,
        string partName,
        Func<string, string?> resolveRelationshipTarget)
    {
        var target = relationship.Attribute("Target")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(target))
            return false;

        var targetMode = relationship.Attribute("TargetMode")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(targetMode) &&
            !string.Equals(targetMode, "Internal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(
            resolveRelationshipTarget(target),
            partName,
            StringComparison.OrdinalIgnoreCase);
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
