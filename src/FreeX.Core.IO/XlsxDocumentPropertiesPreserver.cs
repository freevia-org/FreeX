using System.IO.Compression;
using System.Xml.Linq;

namespace FreeX.Core.IO;

internal static class XlsxDocumentPropertiesPreserver
{
    private const string CorePropertiesPart = "docProps/core.xml";
    private const string CorePropertiesContentType =
        "application/vnd.openxmlformats-package.core-properties+xml";
    private const string CorePropertiesRelationshipType =
        "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
    private const string ExtendedPropertiesPart = "docProps/app.xml";
    private const string ExtendedPropertiesRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties";
    private const string CustomPropertiesPart = "docProps/custom.xml";
    private const string CustomPropertiesRelationshipType =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties";
    private const string CorePropertiesServicePartPrefix = "package/services/metadata/core-properties/";

    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive)
    {
        PreserveDocumentPropertyElements(
            sourceArchive,
            targetArchive,
            CorePropertiesPart,
            [
                XName.Get("subject", "http://purl.org/dc/elements/1.1/"),
                XName.Get("keywords", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties"),
                XName.Get("category", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties"),
                XName.Get("contentStatus", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties"),
                XName.Get("language", "http://purl.org/dc/elements/1.1/"),
                XName.Get("version", "http://schemas.openxmlformats.org/package/2006/metadata/core-properties")
            ]);

        PreserveDocumentPropertyElements(
            sourceArchive,
            targetArchive,
            ExtendedPropertiesPart,
            [
                XName.Get("Application", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"),
                XName.Get("Company", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"),
                XName.Get("Manager", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"),
                XName.Get("PresentationFormat", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties"),
                XName.Get("Template", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties")
            ]);

        PreserveDocumentPropertyPart(sourceArchive, targetArchive, CustomPropertiesPart);
    }

    public static void NormalizePackageGraph(Stream packageStream)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        NormalizePackageGraph(archive);
    }

    internal static void NormalizePackageGraph(ZipArchive archive)
    {
        foreach (var serviceEntry in archive.Entries
                     .Where(entry => entry.FullName.StartsWith(CorePropertiesServicePartPrefix, StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            serviceEntry.Delete();
        }

        RemoveCorePropertiesServiceContentTypes(archive);
        NormalizeRootRelationships(archive);
    }

    private static bool NormalizeRootRelationships(ZipArchive archive)
    {
        XNamespace relationshipNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationshipsEntry = archive.GetEntry("_rels/.rels");
        var relationshipsXml = relationshipsEntry is null
            ? new XDocument(new XElement(relationshipNs + "Relationships"))
            : XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
        if (relationshipsXml.Root is null ||
            relationshipsXml.Root.Name != relationshipNs + "Relationships")
        {
            return false;
        }

        var changed = false;
        changed |= NormalizeRootRelationship(
            archive,
            relationshipsXml,
            relationshipNs,
            CorePropertiesPart,
            CorePropertiesRelationshipType);
        changed |= NormalizeRootRelationship(
            archive,
            relationshipsXml,
            relationshipNs,
            ExtendedPropertiesPart,
            ExtendedPropertiesRelationshipType);
        changed |= NormalizeRootRelationship(
            archive,
            relationshipsXml,
            relationshipNs,
            CustomPropertiesPart,
            CustomPropertiesRelationshipType);

        if (changed || (relationshipsEntry is null && relationshipsXml.Root.HasElements))
            XlsxPackageXmlEditor.ReplaceXml(archive, "_rels/.rels", relationshipsXml);

        return changed;
    }

    private static bool NormalizeRootRelationship(
        ZipArchive archive,
        XDocument relationshipsXml,
        XNamespace relationshipNs,
        string partName,
        string relationshipType)
    {
        var changed = false;
        var hasPart = archive.GetEntry(partName) is not null;
        var relationships = relationshipsXml.Root!
            .Elements(relationshipNs + "Relationship")
            .Where(relationship =>
                IsRelationshipType(relationship, relationshipType) ||
                TargetsInternalPart(relationship, partName))
            .ToList();

        XElement? canonicalRelationship = null;
        foreach (var relationship in relationships)
        {
            var target = relationship.Attribute("Target")?.Value?.Trim();
            var targetMode = relationship.Attribute("TargetMode")?.Value?.Trim();
            var targetsCanonicalPart = TargetsInternalPart(relationship, partName);
            if (!hasPart || !IsRelationshipType(relationship, relationshipType))
            {
                relationship.Remove();
                changed = true;
                continue;
            }

            if (targetsCanonicalPart && canonicalRelationship is null)
            {
                canonicalRelationship = relationship;
                if (!string.Equals(target, partName, StringComparison.Ordinal))
                {
                    relationship.SetAttributeValue("Target", partName);
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

        if (!hasPart || canonicalRelationship is not null)
            return changed;

        relationshipsXml.Root!.Add(new XElement(
            relationshipNs + "Relationship",
            new XAttribute("Id", XlsxPackageXmlEditor.NextRelationshipId(relationshipsXml, relationshipNs)),
            new XAttribute("Type", relationshipType),
            new XAttribute("Target", partName)));
        return true;
    }

    private static bool IsRelationshipType(XElement relationship, string relationshipType) =>
        string.Equals(
            relationship.Attribute("Type")?.Value?.Trim(),
            relationshipType,
            StringComparison.OrdinalIgnoreCase);

    private static bool TargetsInternalPart(XElement relationship, string partName)
    {
        var targetMode = relationship.Attribute("TargetMode")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(targetMode) &&
            !string.Equals(targetMode, "Internal", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var target = relationship.Attribute("Target")?.Value?.Trim();
        return !string.IsNullOrWhiteSpace(target) &&
               string.Equals(
                   XlsxPackagePath.ResolveRelationshipTarget("", target),
                   partName,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool RemoveCorePropertiesServiceContentTypes(ZipArchive archive)
    {
        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            return false;

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        var root = contentTypesXml.Root;
        if (root is null)
            return false;

        var changed = false;
        var serviceOverrides = root
            .Elements(contentTypeNs + "Override")
            .Where(element =>
            {
                var partName = element.Attribute("PartName")?.Value?.Trim().TrimStart('/');
                return !string.IsNullOrWhiteSpace(partName) &&
                    partName.StartsWith(CorePropertiesServicePartPrefix, StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();
        foreach (var serviceOverride in serviceOverrides)
        {
            serviceOverride.Remove();
            changed = true;
        }

        var serviceDefaults = root
            .Elements(contentTypeNs + "Default")
            .Where(element =>
                string.Equals(element.Attribute("Extension")?.Value?.Trim(), "psmdcp", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(element.Attribute("ContentType")?.Value?.Trim(), CorePropertiesContentType, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        foreach (var serviceDefault in serviceDefaults)
        {
            serviceDefault.Remove();
            changed = true;
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(archive, "[Content_Types].xml", contentTypesXml);

        return changed;
    }

    private static void PreserveDocumentPropertyPart(ZipArchive sourceArchive, ZipArchive targetArchive, string partName)
    {
        var sourceEntry = sourceArchive.GetEntry(partName);
        if (sourceEntry is null)
            return;

        targetArchive.GetEntry(partName)?.Delete();
        XlsxPackageMetadataMerger.CopyEntry(sourceEntry, targetArchive);
    }

    private static void PreserveDocumentPropertyElements(
        ZipArchive sourceArchive,
        ZipArchive targetArchive,
        string partName,
        IReadOnlyCollection<XName> stableElementNames)
    {
        var sourceEntry = sourceArchive.GetEntry(partName);
        var targetEntry = targetArchive.GetEntry(partName);
        if (sourceEntry is null)
            return;

        if (targetEntry is null)
        {
            XlsxPackageMetadataMerger.CopyEntry(sourceEntry, targetArchive);
            return;
        }

        var sourceXml = XlsxPackageXmlEditor.LoadXml(sourceEntry);
        var targetXml = XlsxPackageXmlEditor.LoadXml(targetEntry);
        var sourceRoot = sourceXml.Root;
        var targetRoot = targetXml.Root;
        if (sourceRoot is null || targetRoot is null)
            return;

        var changed = false;
        foreach (var stableElementName in stableElementNames)
        {
            var sourceElement = sourceRoot.Element(stableElementName);
            if (sourceElement is null)
                continue;

            var targetElement = targetRoot.Element(stableElementName);
            if (targetElement is null)
            {
                targetRoot.Add(new XElement(sourceElement));
                changed = true;
                continue;
            }

            if (XNode.DeepEquals(targetElement, sourceElement))
                continue;

            targetElement.ReplaceWith(new XElement(sourceElement));
            changed = true;
        }

        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, partName, targetXml);
    }
}
