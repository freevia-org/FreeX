using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxSharedStringPackageGraphNormalizer
{
    private const string SharedStringsPath = "xl/sharedStrings.xml";
    private const string WorkbookRelationshipsPath = "xl/_rels/workbook.xml.rels";
    private const string SharedStringsContentType =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml";
    private const string SharedStringsRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings";

    private static readonly XNamespace ContentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace PackageRelationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static void NormalizePackage(ZipArchive archive)
    {
        if (archive.GetEntry(SharedStringsPath) is null)
        {
            RemoveContentTypeOverride(archive);
            RemoveWorkbookRelationships(archive);
            return;
        }

        EnsureContentTypeOverride(archive);
        EnsureWorkbookRelationship(archive);
    }

    private static void EnsureContentTypeOverride(ZipArchive archive)
    {
        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            return;

        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        var root = contentTypesXml.Root;
        if (root is null)
            return;

        var overrides = root
            .Elements(ContentTypeNs + "Override")
            .Where(element => string.Equals(
                element.Attribute("PartName")?.Value,
                "/xl/sharedStrings.xml",
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        var validOverride = overrides.FirstOrDefault(element => string.Equals(
            element.Attribute("ContentType")?.Value,
            SharedStringsContentType,
            StringComparison.OrdinalIgnoreCase));
        var changed = false;

        if (validOverride is null)
        {
            if (overrides.Count > 0)
            {
                var first = overrides[0];
                first.SetAttributeValue("ContentType", SharedStringsContentType);
                validOverride = first;
                changed = true;
            }
            else
            {
                root.Add(new XElement(
                    ContentTypeNs + "Override",
                    new XAttribute("PartName", "/xl/sharedStrings.xml"),
                    new XAttribute("ContentType", SharedStringsContentType)));
                changed = true;
            }
        }

        foreach (var duplicate in overrides.Where(element => !ReferenceEquals(element, validOverride)).ToList())
        {
            duplicate.Remove();
            changed = true;
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(archive, "[Content_Types].xml", contentTypesXml);
    }

    private static void EnsureWorkbookRelationship(ZipArchive archive)
    {
        var relationshipsEntry = archive.GetEntry(WorkbookRelationshipsPath);
        var relationshipsXml = relationshipsEntry is null
            ? new XDocument(new XElement(PackageRelationshipNs + "Relationships"))
            : XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
        var root = relationshipsXml.Root;
        if (root is null)
        {
            root = new XElement(PackageRelationshipNs + "Relationships");
            relationshipsXml.Add(root);
        }

        var relationships = root.Elements(PackageRelationshipNs + "Relationship").ToList();
        var validRelationship = relationships.FirstOrDefault(IsSharedStringsRelationshipToCurrentPart);
        var changed = false;
        foreach (var relationship in relationships.Where(IsSharedStringsRelationship).ToList())
        {
            if (ReferenceEquals(relationship, validRelationship))
                continue;

            relationship.Remove();
            changed = true;
        }

        if (validRelationship is null)
        {
            XlsxPackageXmlEditor.EnsureRelationshipForPackagePart(
                relationshipsXml,
                PackageRelationshipNs,
                "xl/workbook.xml",
                SharedStringsPath,
                SharedStringsRelationshipType);
            changed = true;
        }

        if (changed || relationshipsEntry is null)
            XlsxPackageXmlEditor.ReplaceXml(archive, WorkbookRelationshipsPath, relationshipsXml);
    }

    private static void RemoveContentTypeOverride(ZipArchive archive)
    {
        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            return;

        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        var root = contentTypesXml.Root;
        if (root is null)
            return;

        var overrides = root
            .Elements(ContentTypeNs + "Override")
            .Where(element => string.Equals(
                element.Attribute("PartName")?.Value,
                "/xl/sharedStrings.xml",
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (overrides.Count == 0)
            return;

        foreach (var element in overrides)
            element.Remove();
        XlsxPackageXmlEditor.ReplaceXml(archive, "[Content_Types].xml", contentTypesXml);
    }

    private static void RemoveWorkbookRelationships(ZipArchive archive)
    {
        var relationshipsEntry = archive.GetEntry(WorkbookRelationshipsPath);
        if (relationshipsEntry is null)
            return;

        var relationshipsXml = XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
        var root = relationshipsXml.Root;
        if (root is null)
            return;

        var sharedStringRelationships = root
            .Elements(PackageRelationshipNs + "Relationship")
            .Where(IsSharedStringsRelationship)
            .ToList();
        if (sharedStringRelationships.Count == 0)
            return;

        foreach (var relationship in sharedStringRelationships)
            relationship.Remove();
        XlsxPackageXmlEditor.ReplaceXml(archive, WorkbookRelationshipsPath, relationshipsXml);
    }

    private static bool IsSharedStringsRelationshipToCurrentPart(XElement relationship)
    {
        if (!IsSharedStringsRelationship(relationship) ||
            string.Equals(relationship.Attribute("TargetMode")?.Value, "External", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var target = relationship.Attribute("Target")?.Value;
        return !string.IsNullOrWhiteSpace(target) &&
               string.Equals(
                   XlsxPackagePath.ResolveRelationshipTarget("xl/workbook.xml", target),
                   SharedStringsPath,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSharedStringsRelationship(XElement relationship) =>
        string.Equals(
            relationship.Attribute("Type")?.Value,
            SharedStringsRelationshipType,
            StringComparison.OrdinalIgnoreCase);
}
