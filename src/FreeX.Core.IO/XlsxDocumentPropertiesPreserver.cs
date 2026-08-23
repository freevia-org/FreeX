using System.IO.Compression;
using System.Xml.Linq;
using Free.Shared.Opc;

namespace FreeX.Core.IO;

internal static class XlsxDocumentPropertiesPreserver
{
    private const string CorePropertiesPart = OpcPackageProperties.CorePropertiesZipEntry;
    private const string CorePropertiesContentType = OpcPackageProperties.CorePropertiesContentType;
    private const string CorePropertiesRelationshipType = OpcPackageProperties.CorePropertiesRelationshipType;
    private const string ExtendedPropertiesPart = OpcPackageProperties.ExtendedPropertiesZipEntry;
    private const string ExtendedPropertiesRelationshipType = OpcPackageProperties.ExtendedPropertiesRelationshipType;
    private const string CustomPropertiesPart = OpcPackageProperties.CustomPropertiesZipEntry;
    private const string CustomPropertiesRelationshipType = OpcPackageProperties.CustomPropertiesRelationshipType;
    private const string CorePropertiesServicePartPrefix = "package/services/metadata/core-properties/";
    private static readonly OpcCanonicalRelationship[] RootDocumentPropertyRelationships =
    [
        new(CorePropertiesPart, CorePropertiesRelationshipType),
        new(ExtendedPropertiesPart, ExtendedPropertiesRelationshipType),
        new(CustomPropertiesPart, CustomPropertiesRelationshipType)
    ];

    public static void Preserve(ZipArchive sourceArchive, ZipArchive targetArchive, DateTimeOffset? saveTimestamp = null)
    {
        PreserveDocumentPropertyElements(
            sourceArchive,
            targetArchive,
            CorePropertiesPart,
            OpcDocumentProperties.WorkbookStableCorePropertyElementNames);

        PreserveDocumentPropertyElements(
            sourceArchive,
            targetArchive,
            ExtendedPropertiesPart,
            OpcDocumentProperties.StableExtendedPropertyElementNames);

        PreserveDocumentPropertyPart(sourceArchive, targetArchive, CustomPropertiesPart);

        // Excel updates dcterms:modified and increments cp:revision on every save; the
        // element-preservation pass above (and the wholesale part copy that happens earlier
        // in the pipeline for a brand-new target part) otherwise carries the SOURCE file's
        // stamp through unchanged forever. dcterms:created is intentionally left untouched.
        UpdateCorePropertiesOnSave(targetArchive, saveTimestamp ?? DateTimeOffset.UtcNow);
    }

    internal static void UpdateCorePropertiesOnSave(ZipArchive targetArchive, DateTimeOffset saveTimestamp)
    {
        var targetEntry = targetArchive.GetEntry(CorePropertiesPart);
        if (targetEntry is null)
            return;

        var targetXml = XlsxPackageXmlEditor.LoadXml(targetEntry);
        var targetRoot = targetXml.Root;
        if (targetRoot is null)
            return;

        var modifiedName = OpcDocumentProperties.DublinCoreTermsNamespace + "modified";
        var modifiedValue = OpcPackageProperties.ToW3CDtf(saveTimestamp);
        var modifiedElement = targetRoot.Element(modifiedName);
        if (modifiedElement is null)
        {
            var dcTermsPrefix = EnsureNamespaceDeclared(
                targetRoot,
                OpcDocumentProperties.DublinCoreTermsNamespace,
                "dcterms");
            targetRoot.Add(new XElement(
                modifiedName,
                new XAttribute(OpcDocumentProperties.XmlSchemaInstanceNamespace + "type", $"{dcTermsPrefix}:W3CDTF"),
                modifiedValue));
        }
        else
        {
            modifiedElement.SetValue(modifiedValue);
        }

        var revisionName = OpcDocumentProperties.CorePropertiesNamespace + "revision";
        var revisionElement = targetRoot.Element(revisionName);
        var currentRevision = int.TryParse(
            revisionElement?.Value,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsedRevision)
            ? parsedRevision
            : 0;
        var nextRevision = (currentRevision + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (revisionElement is null)
            targetRoot.Add(new XElement(revisionName, nextRevision));
        else
            revisionElement.SetValue(nextRevision);

        XlsxPackageXmlEditor.ReplaceXml(targetArchive, CorePropertiesPart, targetXml);
    }

    private static string EnsureNamespaceDeclared(XElement element, XNamespace ns, string preferredPrefix)
    {
        var existingPrefix = element.GetPrefixOfNamespace(ns);
        if (existingPrefix is not null)
            return existingPrefix;

        element.SetAttributeValue(XNamespace.Xmlns + preferredPrefix, ns.NamespaceName);
        return preferredPrefix;
    }

    public static void NormalizePackageGraph(Stream packageStream)
    {
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        NormalizePackageGraph(archive);
    }

    public static bool NeedsPackageGraphNormalization(Stream packageStream)
    {
        var previousPosition = packageStream.CanSeek ? packageStream.Position : 0;
        try
        {
            if (packageStream.CanSeek)
                packageStream.Position = 0;

            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
            return NeedsPackageGraphNormalization(archive);
        }
        finally
        {
            if (packageStream.CanSeek)
                packageStream.Position = previousPosition;
        }
    }

    internal static bool NormalizePackageGraph(ZipArchive archive)
    {
        var changed = false;
        foreach (var serviceEntry in archive.Entries
                     .Where(entry => entry.FullName.StartsWith(CorePropertiesServicePartPrefix, StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            serviceEntry.Delete();
            changed = true;
        }

        changed |= RemoveCorePropertiesServiceContentTypes(archive);
        changed |= NormalizeRootRelationships(archive);
        return changed;
    }

    internal static bool NeedsPackageGraphNormalization(ZipArchive archive) =>
        archive.Entries.Any(entry => entry.FullName.StartsWith(CorePropertiesServicePartPrefix, StringComparison.OrdinalIgnoreCase)) ||
        NeedsCorePropertiesServiceContentTypeRemoval(archive) ||
        NeedsRootRelationshipsNormalization(archive);

    private static bool NeedsRootRelationshipsNormalization(ZipArchive archive)
    {
        var relationshipsEntry = archive.GetEntry("_rels/.rels");
        var relationshipsXml = relationshipsEntry is null
            ? new XDocument(new XElement(OpcRelationships.Namespace + "Relationships"))
            : XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
        if (relationshipsXml.Root is null ||
            relationshipsXml.Root.Name != OpcRelationships.Namespace + "Relationships")
        {
            return false;
        }

        return RootDocumentPropertyRelationships.Any(relationship =>
            OpcRelationships.NeedsCanonicalPackageRelationshipNormalization(
                relationshipsXml,
                relationship,
                archive.GetEntry(relationship.PartName) is not null,
                ResolveRootRelationshipTarget));
    }

    private static bool NormalizeRootRelationships(ZipArchive archive)
    {
        var relationshipsEntry = archive.GetEntry("_rels/.rels");
        var relationshipsXml = relationshipsEntry is null
            ? new XDocument(new XElement(OpcRelationships.Namespace + "Relationships"))
            : XlsxPackageXmlEditor.LoadXml(relationshipsEntry);
        if (relationshipsXml.Root is null ||
            relationshipsXml.Root.Name != OpcRelationships.Namespace + "Relationships")
        {
            return false;
        }

        var changed = false;
        foreach (var relationship in RootDocumentPropertyRelationships)
        {
            changed |= OpcRelationships.NormalizeCanonicalPackageRelationship(
                relationshipsXml,
                relationship,
                archive.GetEntry(relationship.PartName) is not null,
                ResolveRootRelationshipTarget);
        }

        if (changed || (relationshipsEntry is null && relationshipsXml.Root.HasElements))
            XlsxPackageXmlEditor.ReplaceXml(archive, "_rels/.rels", relationshipsXml);

        return changed;
    }

    private static string ResolveRootRelationshipTarget(string target) =>
        XlsxPackagePath.ResolveRelationshipTarget("", target);

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
                var partName = NormalizeContentTypePartName(element.Attribute("PartName")?.Value);
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

    private static bool NeedsCorePropertiesServiceContentTypeRemoval(ZipArchive archive)
    {
        var contentTypesEntry = archive.GetEntry("[Content_Types].xml");
        if (contentTypesEntry is null)
            return false;

        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = XlsxPackageXmlEditor.LoadXml(contentTypesEntry);
        var root = contentTypesXml.Root;
        if (root is null)
            return false;

        return root
            .Elements(contentTypeNs + "Override")
            .Any(element =>
            {
                var partName = NormalizeContentTypePartName(element.Attribute("PartName")?.Value);
                return !string.IsNullOrWhiteSpace(partName) &&
                    partName.StartsWith(CorePropertiesServicePartPrefix, StringComparison.OrdinalIgnoreCase);
            }) ||
            root
                .Elements(contentTypeNs + "Default")
                .Any(element =>
                    string.Equals(element.Attribute("Extension")?.Value?.Trim(), "psmdcp", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(element.Attribute("ContentType")?.Value?.Trim(), CorePropertiesContentType, StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeContentTypePartName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : XlsxPackagePath.NormalizePackagePath(value.Trim());

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

        var changed = OpcDocumentProperties.PreservePropertyElements(sourceRoot, targetRoot, stableElementNames);
        if (changed)
            XlsxPackageXmlEditor.ReplaceXml(targetArchive, partName, targetXml);
    }
}
