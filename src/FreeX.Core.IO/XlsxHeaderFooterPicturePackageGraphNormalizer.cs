using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxHeaderFooterPicturePackageGraphNormalizer
{
    private const string ImageRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    private const string VmlDrawingContentType =
        "application/vnd.openxmlformats-officedocument.vmlDrawing";

    private static readonly XNamespace PackageRelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace VmlNs = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace OfficeNs = "urn:schemas-microsoft-com:office:office";
    private static readonly XNamespace ContentTypeNs =
        "http://schemas.openxmlformats.org/package/2006/content-types";

    public static bool IsPatchSafe(ZipArchive archive, string vmlPath) =>
        TryReadLiveImageRelationships(archive, vmlPath, allowPruning: false, out _, out var liveRelationships, out _) &&
        HasEffectiveContentType(archive, vmlPath, VmlDrawingContentType) &&
        liveRelationships.All(relationship =>
            HasEffectiveContentType(archive, relationship.TargetPath, XlsxPackagePath.GetImageContentType(relationship.TargetPath)));

    public static bool Normalize(ZipArchive archive, string vmlPath)
    {
        if (!TryReadLiveImageRelationships(
                archive,
                vmlPath,
                allowPruning: true,
                out var relationshipsXml,
                out var liveRelationships,
                out var changed))
        {
            return false;
        }

        if (changed && relationshipsXml is not null)
            XlsxPackageXmlEditor.ReplaceXml(archive, XlsxPackagePath.GetRelationshipPartPath(vmlPath), relationshipsXml);

        EnsureEffectiveContentType(archive, vmlPath, VmlDrawingContentType);
        foreach (var relationship in liveRelationships)
            EnsureEffectiveContentType(archive, relationship.TargetPath, XlsxPackagePath.GetImageContentType(relationship.TargetPath));

        return true;
    }

    private static bool TryReadLiveImageRelationships(
        ZipArchive archive,
        string vmlPath,
        bool allowPruning,
        out XDocument? relationshipsXml,
        out List<LiveImageRelationship> liveRelationships,
        out bool changed)
    {
        relationshipsXml = null;
        liveRelationships = [];
        changed = false;

        if (archive.GetEntry(vmlPath) is not { } vmlEntry)
            return false;

        XDocument vmlXml;
        try
        {
            vmlXml = XlsxPackageXmlEditor.LoadXml(vmlEntry);
        }
        catch
        {
            return false;
        }

        var referencedRelationshipIds = vmlXml
            .Descendants(VmlNs + "shape")
            .Where(shape => XlsxHeaderFooterPicturePackagePlanner.Slots.Any(
                slot => string.Equals(slot.ShapeId, shape.Attribute("id")?.Value, StringComparison.OrdinalIgnoreCase)))
            .Select(shape => shape.Element(VmlNs + "imagedata")?.Attribute(OfficeNs + "relid")?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);
        if (referencedRelationshipIds.Count == 0)
            return false;

        var relationshipsPath = XlsxPackagePath.GetRelationshipPartPath(vmlPath);
        if (archive.GetEntry(relationshipsPath) is not { } relationshipsEntry)
            return false;

        try
        {
            relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
        }
        catch
        {
            return false;
        }

        var root = relationshipsXml.Root;
        if (root?.Name != PackageRelNs + "Relationships")
            return false;

        var relationships = root.Elements(PackageRelNs + "Relationship").ToList();
        var keptRelationshipIds = new HashSet<string>(StringComparer.Ordinal);
        var keptRelationships = new List<XElement>();

        foreach (var relationshipId in referencedRelationshipIds)
        {
            var candidates = relationships
                .Where(relationship =>
                    string.Equals(relationship.Attribute("Id")?.Value, relationshipId, StringComparison.Ordinal) &&
                    string.Equals(relationship.Attribute("Type")?.Value, ImageRelationshipType, StringComparison.OrdinalIgnoreCase) &&
                    relationship.Attribute("TargetMode") is null)
                .ToList();
            if (candidates.Count == 0)
                return false;

            var liveCandidates = candidates
                .Select(relationship => new
                {
                    Relationship = relationship,
                    TargetPath = ResolveInternalImagePath(vmlPath, relationship.Attribute("Target")?.Value)
                })
                .Where(candidate =>
                    !string.IsNullOrWhiteSpace(candidate.TargetPath) &&
                    candidate.TargetPath.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase) &&
                    archive.GetEntry(candidate.TargetPath) is not null)
                .ToList();

            if (liveCandidates.Count != 1)
                return false;

            var live = liveCandidates[0];
            keptRelationshipIds.Add(relationshipId);
            keptRelationships.Add(live.Relationship);
            liveRelationships.Add(new LiveImageRelationship(live.TargetPath));
        }

        var staleRelationships = relationships
            .Where(relationship =>
                relationship.Attribute("Id")?.Value is not { } id ||
                !keptRelationshipIds.Contains(id) ||
                !string.Equals(relationship.Attribute("Type")?.Value, ImageRelationshipType, StringComparison.OrdinalIgnoreCase) ||
                relationship.Attribute("TargetMode") is not null)
            .ToList();

        if (staleRelationships.Count > 0 || relationships.Count != keptRelationships.Count)
        {
            if (!allowPruning)
                return false;

            root.ReplaceNodes(keptRelationships.Select(relationship => new XElement(relationship)));
            changed = true;
        }

        return true;
    }

    private static string ResolveInternalImagePath(string vmlPath, string? target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return "";

        return XlsxPackagePath.ResolveRelationshipTarget(vmlPath, target);
    }

    private static void EnsureEffectiveContentType(ZipArchive archive, string partPath, string contentType)
    {
        const string contentTypesPath = "[Content_Types].xml";
        var entry = archive.GetEntry(contentTypesPath);
        if (entry is null)
            return;

        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(entry);
        var root = contentTypesXml.Root;
        if (root is null)
            return;

        var normalizedPartName = $"/{partPath.TrimStart('/')}";
        var overrideElement = root
            .Elements(ContentTypeNs + "Override")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("PartName")?.Value,
                normalizedPartName,
                StringComparison.OrdinalIgnoreCase));
        if (overrideElement is not null)
        {
            if (string.Equals(overrideElement.Attribute("ContentType")?.Value, contentType, StringComparison.OrdinalIgnoreCase))
                return;

            overrideElement.Remove();
        }
        else if (HasMatchingDefault(root, partPath, contentType))
        {
            return;
        }

        root.Add(new XElement(
            ContentTypeNs + "Override",
            new XAttribute("PartName", normalizedPartName),
            new XAttribute("ContentType", contentType)));
        XlsxPackageXmlEditor.ReplaceXml(archive, contentTypesPath, contentTypesXml);
    }

    private static bool HasEffectiveContentType(ZipArchive archive, string partPath, string contentType)
    {
        if (archive.GetEntry("[Content_Types].xml") is not { } entry)
            return false;

        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(entry);
        var root = contentTypesXml.Root;
        if (root is null)
            return false;

        var normalizedPartName = $"/{partPath.TrimStart('/')}";
        var overrideElement = root
            .Elements(ContentTypeNs + "Override")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("PartName")?.Value,
                normalizedPartName,
                StringComparison.OrdinalIgnoreCase));
        if (overrideElement is not null)
            return string.Equals(overrideElement.Attribute("ContentType")?.Value, contentType, StringComparison.OrdinalIgnoreCase);

        return HasMatchingDefault(root, partPath, contentType);
    }

    private static bool HasMatchingDefault(XElement contentTypesRoot, string partPath, string contentType)
    {
        var extension = Path.GetExtension(partPath).TrimStart('.');
        return !string.IsNullOrWhiteSpace(extension) &&
               contentTypesRoot
                   .Elements(ContentTypeNs + "Default")
                   .Any(element =>
                       string.Equals(element.Attribute("Extension")?.Value, extension, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(element.Attribute("ContentType")?.Value, contentType, StringComparison.OrdinalIgnoreCase));
    }

    private sealed record LiveImageRelationship(string TargetPath);
}
