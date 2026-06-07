using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxCustomRibbonPackageGraphNormalizer
{
    private const string RootRelationshipsPath = "_rels/.rels";
    private const string ContentTypesPath = "[Content_Types].xml";
    private const string CustomUiRelationshipType = "http://schemas.microsoft.com/office/2006/relationships/ui/extensibility";
    private const string CustomUi14RelationshipType = "http://schemas.microsoft.com/office/2007/relationships/ui/extensibility";

    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

    public static bool NormalizePackage(ZipArchive archive)
    {
        var changed = NormalizeRootRelationships(archive);
        changed |= RemoveDanglingCustomUiContentTypes(archive);
        return changed;
    }

    private static bool NormalizeRootRelationships(ZipArchive archive)
    {
        var entry = archive.GetEntry(RootRelationshipsPath);
        if (entry is null)
            return false;

        XDocument relationshipsXml;
        try
        {
            relationshipsXml = XlsxPackageXmlEditor.LoadXml(entry);
        }
        catch
        {
            return false;
        }

        var root = relationshipsXml.Root;
        if (root is null || root.Name != PackageRelationshipNs + "Relationships")
            return false;

        var relationships = root.Elements(PackageRelationshipNs + "Relationship").ToList();
        var nonCustomUiIds = relationships
            .Where(relationship => !IsCustomUiRelationship(relationship))
            .Select(relationship => relationship.Attribute("Id")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var usedCustomUiIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = false;
        foreach (var relationship in relationships)
        {
            var id = relationship.Attribute("Id")?.Value;
            var isCustomUiRelationship = IsCustomUiRelationship(relationship);
            if (isCustomUiRelationship && !TargetsExistingCustomUiPart(archive, relationship))
            {
                relationship.Remove();
                changed = true;
                continue;
            }

            if (!isCustomUiRelationship)
                continue;

            if (!string.IsNullOrWhiteSpace(id) &&
                !nonCustomUiIds.Contains(id) &&
                usedCustomUiIds.Add(id))
            {
                continue;
            }

            var replacementId = XlsxPackageXmlEditor.NextRelationshipId(relationshipsXml, PackageRelationshipNs);
            relationship.SetAttributeValue("Id", replacementId);
            usedCustomUiIds.Add(replacementId);
            changed = true;
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(archive, RootRelationshipsPath, relationshipsXml);

        return changed;
    }

    private static bool RemoveDanglingCustomUiContentTypes(ZipArchive archive)
    {
        var entry = archive.GetEntry(ContentTypesPath);
        if (entry is null)
            return false;

        XDocument contentTypesXml;
        try
        {
            contentTypesXml = XlsxPackageXmlEditor.LoadXml(entry);
        }
        catch
        {
            return false;
        }

        var root = contentTypesXml.Root;
        if (root is null)
            return false;

        var changed = false;
        foreach (var contentType in root.Elements(ContentTypeNs + "Override").ToList())
        {
            var partName = NormalizePartName(contentType.Attribute("PartName")?.Value);
            if (!IsCustomUiPart(partName) || archive.GetEntry(partName) is not null)
                continue;

            contentType.Remove();
            changed = true;
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(archive, ContentTypesPath, contentTypesXml);

        return changed;
    }

    private static bool IsCustomUiRelationship(XElement relationship)
    {
        var type = relationship.Attribute("Type")?.Value.Trim();
        return string.Equals(type, CustomUiRelationshipType, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, CustomUi14RelationshipType, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TargetsExistingCustomUiPart(ZipArchive archive, XElement relationship)
    {
        if (relationship.Attribute("TargetMode") is { } targetMode &&
            !string.Equals(targetMode.Value.Trim(), "Internal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var target = relationship.Attribute("Target")?.Value;
        if (string.IsNullOrWhiteSpace(target))
            return false;

        var targetPart = XlsxPackagePath.ResolveRelationshipTarget("", target.Trim().Replace('\\', '/'));
        return IsCustomUiPart(targetPart) && archive.GetEntry(targetPart) is not null;
    }

    private static bool IsCustomUiPart(string partName) =>
        partName.StartsWith("customUI/", StringComparison.OrdinalIgnoreCase) &&
        partName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    private static string NormalizePartName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        return XlsxPackagePath.NormalizeZipPath(value.Trim().TrimStart('/').Replace('\\', '/'));
    }
}
