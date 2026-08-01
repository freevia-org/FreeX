using System.IO.Compression;
using System.Xml.Linq;

namespace Free.Shared.Opc;

public sealed record CoreDocumentProperties(
    string? Title = null,
    string? Author = null,
    string? Subject = null,
    string? Keywords = null,
    string? Comments = null,
    string? LastModifiedBy = null,
    DateTimeOffset? Created = null,
    DateTimeOffset? Modified = null,
    string? Category = null,
    string? ContentStatus = null,
    string? Language = null,
    string? Version = null);

public sealed record ExtendedDocumentProperties(
    string? Application = null,
    string? Company = null,
    string? Manager = null,
    string? PresentationFormat = null,
    string? Template = null);

public static class OpcDocumentProperties
{
    public static readonly XNamespace CorePropertiesNamespace =
        "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    public static readonly XNamespace DublinCoreNamespace = "http://purl.org/dc/elements/1.1/";
    public static readonly XNamespace DublinCoreTermsNamespace = "http://purl.org/dc/terms/";
    public static readonly XNamespace DublinCoreTypeNamespace = "http://purl.org/dc/dcmitype/";
    public static readonly XNamespace ExtendedPropertiesNamespace =
        "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
    public static readonly XNamespace XmlSchemaInstanceNamespace = "http://www.w3.org/2001/XMLSchema-instance";

    public static readonly IReadOnlySet<XName> ModeledCorePropertyElementNames = new HashSet<XName>
    {
        DublinCoreNamespace + "title",
        DublinCoreNamespace + "creator",
        DublinCoreNamespace + "subject",
        CorePropertiesNamespace + "keywords",
        DublinCoreNamespace + "description",
        CorePropertiesNamespace + "lastModifiedBy",
        DublinCoreTermsNamespace + "created",
        DublinCoreTermsNamespace + "modified",
        CorePropertiesNamespace + "category",
        CorePropertiesNamespace + "contentStatus",
        DublinCoreNamespace + "language",
        CorePropertiesNamespace + "version",
    };

    public static readonly IReadOnlyList<XName> WorkbookStableCorePropertyElementNames =
    [
        DublinCoreNamespace + "subject",
        CorePropertiesNamespace + "keywords",
        CorePropertiesNamespace + "category",
        CorePropertiesNamespace + "contentStatus",
        DublinCoreNamespace + "language",
        CorePropertiesNamespace + "version",
        CorePropertiesNamespace + "lastPrinted",
        CorePropertiesNamespace + "revision"
    ];

    public static readonly IReadOnlyList<XName> StableExtendedPropertyElementNames =
    [
        ExtendedPropertiesNamespace + "Application",
        ExtendedPropertiesNamespace + "AppVersion",
        ExtendedPropertiesNamespace + "Company",
        ExtendedPropertiesNamespace + "Manager",
        ExtendedPropertiesNamespace + "PresentationFormat",
        ExtendedPropertiesNamespace + "Template",
        ExtendedPropertiesNamespace + "HyperlinkBase",
        ExtendedPropertiesNamespace + "HLinks",
        ExtendedPropertiesNamespace + "LinksUpToDate",
        ExtendedPropertiesNamespace + "SharedDoc",
        ExtendedPropertiesNamespace + "HyperlinksChanged"
    ];

    public static CoreDocumentProperties ReadCoreProperties(
        ZipArchive archive,
        string entryPath = OpcPackageProperties.CorePropertiesZipEntry) =>
        ReadCoreProperties(OpcXml.LoadXmlOrNull(archive, entryPath));

    public static void ReadCoreProperties(
        ZipArchive archive,
        DocumentProperties target,
        string entryPath = OpcPackageProperties.CorePropertiesZipEntry,
        bool emptyStringsAsNull = false)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.ApplyCoreProperties(ReadCoreProperties(archive, entryPath), emptyStringsAsNull);
    }

    public static CoreDocumentProperties ReadCoreProperties(XDocument? document)
    {
        var root = document?.Root;
        if (root is null)
            return new CoreDocumentProperties();

        return new CoreDocumentProperties(
            Title: ElementValue(root, DublinCoreNamespace + "title"),
            Author: ElementValue(root, DublinCoreNamespace + "creator"),
            Subject: ElementValue(root, DublinCoreNamespace + "subject"),
            Keywords: ElementValue(root, CorePropertiesNamespace + "keywords"),
            Comments: ElementValue(root, DublinCoreNamespace + "description"),
            LastModifiedBy: ElementValue(root, CorePropertiesNamespace + "lastModifiedBy"),
            Created: OpcPackageProperties.ParseW3CDtf(ElementValue(root, DublinCoreTermsNamespace + "created")),
            Modified: OpcPackageProperties.ParseW3CDtf(ElementValue(root, DublinCoreTermsNamespace + "modified")),
            Category: ElementValue(root, CorePropertiesNamespace + "category"),
            ContentStatus: ElementValue(root, CorePropertiesNamespace + "contentStatus"),
            Language: ElementValue(root, DublinCoreNamespace + "language"),
            Version: ElementValue(root, CorePropertiesNamespace + "version"));
    }

    public static XDocument BuildCorePropertiesDocument(
        CoreDocumentProperties properties,
        bool includeEmptyStrings = false,
        bool includeDcmiTypeNamespace = false,
        bool includeXmlDeclaration = false)
    {
        var core = new XElement(
            CorePropertiesNamespace + "coreProperties",
            new XAttribute(XNamespace.Xmlns + "cp", CorePropertiesNamespace.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dc", DublinCoreNamespace.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "dcterms", DublinCoreTermsNamespace.NamespaceName),
            includeDcmiTypeNamespace
                ? new XAttribute(XNamespace.Xmlns + "dcmitype", DublinCoreTypeNamespace.NamespaceName)
                : null,
            new XAttribute(XNamespace.Xmlns + "xsi", XmlSchemaInstanceNamespace.NamespaceName));

        AddString(core, DublinCoreNamespace + "title", properties.Title, includeEmptyStrings);
        AddString(core, DublinCoreNamespace + "creator", properties.Author, includeEmptyStrings);
        AddString(core, DublinCoreNamespace + "subject", properties.Subject, includeEmptyStrings);
        AddString(core, CorePropertiesNamespace + "keywords", properties.Keywords, includeEmptyStrings);
        AddString(core, DublinCoreNamespace + "description", properties.Comments, includeEmptyStrings);
        AddString(core, CorePropertiesNamespace + "lastModifiedBy", properties.LastModifiedBy, includeEmptyStrings);
        AddTimestamp(core, DublinCoreTermsNamespace + "created", properties.Created);
        AddTimestamp(core, DublinCoreTermsNamespace + "modified", properties.Modified);
        AddString(core, CorePropertiesNamespace + "category", properties.Category, includeEmptyStrings);
        AddString(core, CorePropertiesNamespace + "contentStatus", properties.ContentStatus, includeEmptyStrings);
        AddString(core, DublinCoreNamespace + "language", properties.Language, includeEmptyStrings);
        AddString(core, CorePropertiesNamespace + "version", properties.Version, includeEmptyStrings);

        var document = new XDocument(core);
        if (includeXmlDeclaration)
            document.Declaration = new XDeclaration("1.0", "UTF-8", "yes");
        return document;
    }

    public static XDocument BuildCorePropertiesDocument(
        DocumentProperties properties,
        bool includeEmptyStrings = false,
        bool includeDcmiTypeNamespace = false,
        bool includeXmlDeclaration = false)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return BuildCorePropertiesDocument(
            properties.ToCoreProperties(),
            includeEmptyStrings,
            includeDcmiTypeNamespace,
            includeXmlDeclaration);
    }

    public static void WriteCoreProperties(
        ZipArchive archive,
        CoreDocumentProperties properties,
        string entryPath = OpcPackageProperties.CorePropertiesZipEntry,
        bool includeEmptyStrings = false,
        bool includeDcmiTypeNamespace = false,
        bool includeXmlDeclaration = false) =>
        OpcXml.ReplaceXmlEntry(
            archive,
            entryPath,
            BuildCorePropertiesDocument(
                properties,
                includeEmptyStrings,
                includeDcmiTypeNamespace,
                includeXmlDeclaration));

    public static void WriteCoreProperties(
        ZipArchive archive,
        DocumentProperties properties,
        string entryPath = OpcPackageProperties.CorePropertiesZipEntry,
        bool includeEmptyStrings = false,
        bool includeDcmiTypeNamespace = false,
        bool includeXmlDeclaration = false)
    {
        ArgumentNullException.ThrowIfNull(properties);
        WriteCoreProperties(
            archive,
            properties.ToCoreProperties(),
            entryPath,
            includeEmptyStrings,
            includeDcmiTypeNamespace,
            includeXmlDeclaration);
    }

    public static ExtendedDocumentProperties ReadExtendedProperties(
        ZipArchive archive,
        string entryPath = OpcPackageProperties.ExtendedPropertiesZipEntry) =>
        ReadExtendedProperties(OpcXml.LoadXmlOrNull(archive, entryPath));

    public static ExtendedDocumentProperties ReadExtendedProperties(XDocument? document)
    {
        var root = document?.Root;
        if (root is null)
            return new ExtendedDocumentProperties();

        return new ExtendedDocumentProperties(
            Application: ElementValue(root, ExtendedPropertiesNamespace + "Application"),
            Company: ElementValue(root, ExtendedPropertiesNamespace + "Company"),
            Manager: ElementValue(root, ExtendedPropertiesNamespace + "Manager"),
            PresentationFormat: ElementValue(root, ExtendedPropertiesNamespace + "PresentationFormat"),
            Template: ElementValue(root, ExtendedPropertiesNamespace + "Template"));
    }

    public static XDocument BuildExtendedPropertiesDocument(
        ExtendedDocumentProperties properties,
        bool includeEmptyStrings = false,
        bool includeXmlDeclaration = false)
    {
        var root = new XElement(
            ExtendedPropertiesNamespace + "Properties",
            new XAttribute(XNamespace.Xmlns + "vt",
                "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes"));

        AddString(root, ExtendedPropertiesNamespace + "Application", properties.Application, includeEmptyStrings);
        AddString(root, ExtendedPropertiesNamespace + "Company", properties.Company, includeEmptyStrings);
        AddString(root, ExtendedPropertiesNamespace + "Manager", properties.Manager, includeEmptyStrings);
        AddString(root, ExtendedPropertiesNamespace + "PresentationFormat", properties.PresentationFormat, includeEmptyStrings);
        AddString(root, ExtendedPropertiesNamespace + "Template", properties.Template, includeEmptyStrings);

        var document = new XDocument(root);
        if (includeXmlDeclaration)
            document.Declaration = new XDeclaration("1.0", "UTF-8", "yes");
        return document;
    }

    public static void WriteExtendedProperties(
        ZipArchive archive,
        ExtendedDocumentProperties properties,
        string entryPath = OpcPackageProperties.ExtendedPropertiesZipEntry,
        bool includeEmptyStrings = false,
        bool includeXmlDeclaration = false) =>
        OpcXml.ReplaceXmlEntry(
            archive,
            entryPath,
            BuildExtendedPropertiesDocument(properties, includeEmptyStrings, includeXmlDeclaration));

    public static bool PreservePropertyElements(
        XElement sourceRoot,
        XElement targetRoot,
        IEnumerable<XName> propertyElementNames)
    {
        var changed = false;
        foreach (var propertyElementName in propertyElementNames)
        {
            var sourceElement = sourceRoot.Element(propertyElementName);
            if (sourceElement is null)
                continue;

            var targetElement = targetRoot.Element(propertyElementName);
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

        return changed;
    }

    private static string? ElementValue(XElement root, XName name) =>
        root.Element(name)?.Value;

    private static void AddString(XElement parent, XName name, string? value, bool includeEmptyStrings)
    {
        if (value is null || (!includeEmptyStrings && value.Length == 0))
            return;

        parent.Add(new XElement(name, value));
    }

    private static void AddTimestamp(XElement parent, XName name, DateTimeOffset? value)
    {
        if (value is not { } timestamp)
            return;

        parent.Add(new XElement(
            name,
            new XAttribute(XmlSchemaInstanceNamespace + "type", "dcterms:W3CDTF"),
            OpcPackageProperties.ToW3CDtf(timestamp)));
    }
}
