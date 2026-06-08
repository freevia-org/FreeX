using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxDocumentThumbnailPackageGraphNormalizer
{
    private const string ContentTypesPath = "[Content_Types].xml";
    private const string RootRelationshipsPath = "_rels/.rels";
    private const string ThumbnailRelationshipType =
        "http://schemas.openxmlformats.org/package/2006/relationships/metadata/thumbnail";

    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static void NormalizePackage(ZipArchive archive)
    {
        var thumbnailParts = archive.Entries
            .Select(XlsxPackagePath.NormalizeEntryPath)
            .Where(IsThumbnailPart)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ThumbnailPartRank)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var activeThumbnailPart = NormalizeRootRelationships(archive, thumbnailParts);
        NormalizeContentTypes(archive, activeThumbnailPart, thumbnailParts);
    }

    private static string? NormalizeRootRelationships(ZipArchive archive, IReadOnlyList<string> thumbnailParts)
    {
        var relationshipsEntry = archive.GetEntry(RootRelationshipsPath);
        var relationshipsXml = relationshipsEntry is null
            ? new XDocument(new XElement(PackageRelationshipNs + "Relationships"))
            : XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
        var root = relationshipsXml.Root;
        if (root is null ||
            root.Name != PackageRelationshipNs + "Relationships")
        {
            return null;
        }

        var changed = false;
        var thumbnailRelationships = root
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(IsThumbnailRelationship)
            .ToList();
        var canonicalRelationship = thumbnailRelationships
            .FirstOrDefault(relationship => TryResolveValidThumbnailTarget(relationship, archive, out _));
        string? activeThumbnailPart = null;

        foreach (var relationship in thumbnailRelationships)
        {
            if (!ReferenceEquals(relationship, canonicalRelationship))
            {
                relationship.Remove();
                changed = true;
                continue;
            }

            activeThumbnailPart = ResolveThumbnailTarget(relationship);
            if (activeThumbnailPart is null)
            {
                relationship.Remove();
                changed = true;
                canonicalRelationship = null;
                continue;
            }

            var target = relationship.Attribute("Target")?.Value;
            var targetMode = relationship.Attribute("TargetMode")?.Value;
            if (!string.Equals(target, activeThumbnailPart, StringComparison.Ordinal))
            {
                relationship.SetAttributeValue("Target", activeThumbnailPart);
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(targetMode))
            {
                relationship.SetAttributeValue("TargetMode", null);
                changed = true;
            }
        }

        if (canonicalRelationship is null && thumbnailParts.Count > 0)
        {
            activeThumbnailPart = thumbnailParts[0];
            root.Add(new XElement(
                PackageRelationshipNs + "Relationship",
                new XAttribute("Id", XlsxPackageXmlEditor.NextRelationshipId(relationshipsXml, PackageRelationshipNs)),
                new XAttribute("Type", ThumbnailRelationshipType),
                new XAttribute("Target", activeThumbnailPart)));
            changed = true;
        }

        if (changed || (relationshipsEntry is null && root.HasElements))
            XlsxPackageXmlEditor.ReplaceXml(archive, RootRelationshipsPath, relationshipsXml);

        return activeThumbnailPart;
    }

    private static void NormalizeContentTypes(
        ZipArchive archive,
        string? activeThumbnailPart,
        IReadOnlyCollection<string> existingThumbnailParts)
    {
        var contentTypesEntry = archive.GetEntry(ContentTypesPath);
        if (contentTypesEntry is null)
            return;

        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        var root = contentTypesXml.Root;
        if (root is null)
            return;

        var changed = false;
        var existingParts = existingThumbnailParts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var overridesByPart = root
            .Elements(ContentTypeNs + "Override")
            .Where(element => TryNormalizePartName(element.Attribute("PartName")?.Value, out var partName) &&
                              IsThumbnailPart(partName))
            .GroupBy(element => NormalizePartName(element.Attribute("PartName")!.Value), StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var group in overridesByPart)
        {
            if (!existingParts.Contains(group.Key))
            {
                foreach (var staleOverride in group)
                {
                    staleOverride.Remove();
                    changed = true;
                }

                continue;
            }

            XElement? keeper = null;
            var expectedContentType = GetThumbnailContentType(group.Key);
            foreach (var element in group)
            {
                if (keeper is null)
                {
                    keeper = element;
                    var partName = element.Attribute("PartName")?.Value;
                    if (!string.Equals(partName, "/" + group.Key, StringComparison.Ordinal))
                    {
                        element.SetAttributeValue("PartName", "/" + group.Key);
                        changed = true;
                    }

                    if (!string.Equals(element.Attribute("ContentType")?.Value, expectedContentType, StringComparison.OrdinalIgnoreCase))
                    {
                        element.SetAttributeValue("ContentType", expectedContentType);
                        changed = true;
                    }

                    continue;
                }

                element.Remove();
                changed = true;
            }
        }

        if (!string.IsNullOrWhiteSpace(activeThumbnailPart) &&
            !HasEffectiveContentType(root, activeThumbnailPart, GetThumbnailContentType(activeThumbnailPart)))
        {
            root.Add(new XElement(
                ContentTypeNs + "Override",
                new XAttribute("PartName", "/" + activeThumbnailPart),
                new XAttribute("ContentType", GetThumbnailContentType(activeThumbnailPart))));
            changed = true;
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(archive, ContentTypesPath, contentTypesXml);
    }

    private static bool HasEffectiveContentType(XElement root, string partName, string contentType)
    {
        var overrideContentType = root
            .Elements(ContentTypeNs + "Override")
            .Where(element => TryNormalizePartName(element.Attribute("PartName")?.Value, out var overridePartName) &&
                              string.Equals(overridePartName, partName, StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Attribute("ContentType")?.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(overrideContentType))
            return string.Equals(overrideContentType, contentType, StringComparison.OrdinalIgnoreCase);

        var extension = Path.GetExtension(partName).TrimStart('.');
        return root
            .Elements(ContentTypeNs + "Default")
            .Any(element =>
                string.Equals(element.Attribute("Extension")?.Value, extension, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(element.Attribute("ContentType")?.Value, contentType, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryResolveValidThumbnailTarget(
        XElement relationship,
        ZipArchive archive,
        out string thumbnailPart)
    {
        thumbnailPart = ResolveThumbnailTarget(relationship) ?? "";
        return !string.IsNullOrWhiteSpace(thumbnailPart) &&
               archive.GetEntry(thumbnailPart) is not null &&
               IsThumbnailPart(thumbnailPart);
    }

    private static string? ResolveThumbnailTarget(XElement relationship)
    {
        var targetMode = relationship.Attribute("TargetMode")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(targetMode) &&
            !string.Equals(targetMode, "Internal", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var target = relationship.Attribute("Target")?.Value?.Trim();
        if (string.IsNullOrWhiteSpace(target))
            return null;

        return XlsxPackagePath.ResolveRelationshipTarget("", target);
    }

    private static bool IsThumbnailRelationship(XElement relationship) =>
        string.Equals(
            relationship.Attribute("Type")?.Value?.Trim(),
            ThumbnailRelationshipType,
            StringComparison.OrdinalIgnoreCase);

    private static bool IsThumbnailPart(string path)
    {
        var normalized = XlsxPackagePath.NormalizePackagePath(path);
        if (!normalized.StartsWith("docProps/thumbnail.", StringComparison.OrdinalIgnoreCase))
            return false;

        return IsSupportedThumbnailExtension(normalized);
    }

    private static string GetThumbnailContentType(string partName)
    {
        var extension = Path.GetExtension(partName);
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
        {
            return "image/jpeg";
        }

        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            return "image/png";
        if (extension.Equals(".gif", StringComparison.OrdinalIgnoreCase))
            return "image/gif";
        if (extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            return "image/bmp";
        if (extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
        {
            return "image/tiff";
        }

        if (extension.Equals(".emf", StringComparison.OrdinalIgnoreCase))
            return "image/x-emf";
        if (extension.Equals(".wmf", StringComparison.OrdinalIgnoreCase))
            return "image/x-wmf";

        return "application/octet-stream";
    }

    private static bool IsSupportedThumbnailExtension(string partName)
    {
        var extension = Path.GetExtension(partName);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".emf", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".wmf", StringComparison.OrdinalIgnoreCase);
    }

    private static int ThumbnailPartRank(string partName)
    {
        var extension = Path.GetExtension(partName);
        if (extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            return 2;
        return 3;
    }

    private static bool TryNormalizePartName(string? partName, out string normalizedPartName)
    {
        normalizedPartName = "";
        if (string.IsNullOrWhiteSpace(partName))
            return false;

        normalizedPartName = NormalizePartName(partName);
        return !string.IsNullOrWhiteSpace(normalizedPartName);
    }

    private static string NormalizePartName(string partName) =>
        XlsxPackagePath.NormalizePackagePath(partName.Trim());
}
