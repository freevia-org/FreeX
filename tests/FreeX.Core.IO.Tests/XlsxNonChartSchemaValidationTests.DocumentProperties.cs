using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void DocumentPropertiesPackageMetadata_ProducesSchemaValidWorkbook()
    {
        using var source = CreateDocumentPropertiesSourcePackage();

        SchemaErrors(source).Should().BeEmpty();
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithDocumentPropertiesPackageMetadata_ProducesSchemaValidWorkbook()
    {
        using var source = CreateDocumentPropertiesSourcePackage();
        var sourceCustomProperties = ReadPackageRootElement(source, "docProps/custom.xml");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        AssertDocumentPropertiesValues(saved);
        ReadPackageRootElement(saved, "docProps/custom.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceCustomProperties.ToString(SaveOptions.DisableFormatting));
        AssertDocumentPropertiesRootRelationships(saved);
    }

    private static MemoryStream CreateDocumentPropertiesSourcePackage()
    {
        var workbook = new Workbook("DocumentPropertiesPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("docprops"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));

        var stream = Save(workbook);
        AddDocumentPropertiesPackageMetadata(stream);
        stream.Position = 0;
        return stream;
    }

    private static void AddDocumentPropertiesPackageMetadata(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);

        XNamespace dcNs = "http://purl.org/dc/elements/1.1/";
        XNamespace corePropsNs = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        XNamespace appPropsNs = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
        XNamespace customPropsNs = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
        XNamespace vtNs = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";

        var coreXml = archive.GetEntry("docProps/core.xml") is { } coreEntry
            ? LoadPackageXml(coreEntry)
            : new XDocument(new XElement(corePropsNs + "coreProperties"));
        SetPackageElementValue(coreXml.Root!, dcNs + "subject", "FreeX schema subject");
        SetPackageElementValue(coreXml.Root!, corePropsNs + "keywords", "freex,xlsx,schema");
        SetPackageElementValue(coreXml.Root!, corePropsNs + "category", "XLSX Fidelity");
        SetPackageElementValue(coreXml.Root!, corePropsNs + "contentStatus", "Validated");
        SetPackageElementValue(coreXml.Root!, dcNs + "language", "en-US");
        SetPackageElementValue(coreXml.Root!, corePropsNs + "version", "2026.06");
        ReplacePackageXml(archive, "docProps/core.xml", coreXml);
        AddPackageContentTypeOverride(
            archive,
            "/docProps/core.xml",
            "application/vnd.openxmlformats-package.core-properties+xml");
        AddPackageRootRelationship(
            archive,
            "rIdFreeXCoreProperties",
            "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties",
            "docProps/core.xml");

        var appXml = archive.GetEntry("docProps/app.xml") is { } appEntry
            ? LoadPackageXml(appEntry)
            : new XDocument(new XElement(appPropsNs + "Properties"));
        SetPackageElementValue(appXml.Root!, appPropsNs + "Application", "Microsoft Excel");
        SetPackageElementValue(appXml.Root!, appPropsNs + "Company", "FreeX Test Lab");
        SetPackageElementValue(appXml.Root!, appPropsNs + "Manager", "XLSX Fidelity");
        SetPackageElementValue(appXml.Root!, appPropsNs + "Template", "SchemaTemplate.xltx");
        SetPackageElementValue(appXml.Root!, appPropsNs + "PresentationFormat", "Workbook");
        ReplacePackageXml(archive, "docProps/app.xml", appXml);
        AddPackageContentTypeOverride(
            archive,
            "/docProps/app.xml",
            "application/vnd.openxmlformats-officedocument.extended-properties+xml");
        AddPackageRootRelationship(
            archive,
            "rIdFreeXExtendedProperties",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties",
            "docProps/app.xml");

        ReplacePackageXml(archive, "docProps/custom.xml", new XDocument(
            new XElement(
                customPropsNs + "Properties",
                new XAttribute(XNamespace.Xmlns + "vt", vtNs),
                new XElement(
                    customPropsNs + "property",
                    new XAttribute("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"),
                    new XAttribute("pid", "2"),
                    new XAttribute("name", "FreeXCustomProperty"),
                    new XElement(vtNs + "lpwstr", "kept")),
                new XElement(
                    customPropsNs + "property",
                    new XAttribute("fmtid", "{D5CDD505-2E9C-101B-9397-08002B2CF9AE}"),
                    new XAttribute("pid", "3"),
                    new XAttribute("name", "FreeXBuildNumber"),
                    new XElement(vtNs + "i4", "606")))));
        AddPackageContentTypeOverride(
            archive,
            "/docProps/custom.xml",
            "application/vnd.openxmlformats-officedocument.custom-properties+xml");
        AddPackageRootRelationship(
            archive,
            "rIdFreeXCustomProperties",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties",
            "docProps/custom.xml");
    }

    private static void AssertDocumentPropertiesValues(Stream stream)
    {
        XNamespace dcNs = "http://purl.org/dc/elements/1.1/";
        XNamespace corePropsNs = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        XNamespace appPropsNs = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
        XNamespace customPropsNs = "http://schemas.openxmlformats.org/officeDocument/2006/custom-properties";
        XNamespace vtNs = "http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes";

        var coreXml = ReadPackageRootElement(stream, "docProps/core.xml");
        coreXml.Element(dcNs + "subject")!.Value.Should().Be("FreeX schema subject");
        coreXml.Element(corePropsNs + "keywords")!.Value.Should().Be("freex,xlsx,schema");
        coreXml.Element(corePropsNs + "category")!.Value.Should().Be("XLSX Fidelity");
        coreXml.Element(corePropsNs + "contentStatus")!.Value.Should().Be("Validated");
        coreXml.Element(dcNs + "language")!.Value.Should().Be("en-US");
        coreXml.Element(corePropsNs + "version")!.Value.Should().Be("2026.06");

        var appXml = ReadPackageRootElement(stream, "docProps/app.xml");
        appXml.Element(appPropsNs + "Application")!.Value.Should().Be("Microsoft Excel");
        appXml.Element(appPropsNs + "Company")!.Value.Should().Be("FreeX Test Lab");
        appXml.Element(appPropsNs + "Manager")!.Value.Should().Be("XLSX Fidelity");
        appXml.Element(appPropsNs + "Template")!.Value.Should().Be("SchemaTemplate.xltx");
        appXml.Element(appPropsNs + "PresentationFormat")!.Value.Should().Be("Workbook");

        var customXml = ReadPackageRootElement(stream, "docProps/custom.xml");
        customXml.Elements(customPropsNs + "property")
            .Single(element => element.Attribute("name")?.Value == "FreeXCustomProperty")
            .Element(vtNs + "lpwstr")!
            .Value
            .Should()
            .Be("kept");
        customXml.Elements(customPropsNs + "property")
            .Single(element => element.Attribute("name")?.Value == "FreeXBuildNumber")
            .Element(vtNs + "i4")!
            .Value
            .Should()
            .Be("606");
    }

    private static void AssertDocumentPropertiesRootRelationships(Stream stream)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationships = ReadPackageRootElement(stream, "_rels/.rels")
            .Elements(packageRelNs + "Relationship")
            .ToList();

        relationships.Where(relationship =>
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" &&
                relationship.Attribute("Target")?.Value == "docProps/core.xml" &&
                relationship.Attribute("TargetMode") is null)
            .Should()
            .ContainSingle();
        relationships.Where(relationship =>
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" &&
                relationship.Attribute("Target")?.Value == "docProps/app.xml" &&
                relationship.Attribute("TargetMode") is null)
            .Should()
            .ContainSingle();
        relationships.Where(relationship =>
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/custom-properties" &&
                relationship.Attribute("Target")?.Value == "docProps/custom.xml" &&
                relationship.Attribute("TargetMode") is null)
            .Should()
            .ContainSingle();
    }

    private static void SetPackageElementValue(XElement root, XName name, string value)
    {
        var element = root.Element(name);
        if (element is null)
        {
            element = new XElement(name);
            root.Add(element);
        }

        element.Value = value;
    }

    private static void AddPackageContentTypeOverride(
        ZipArchive archive,
        string partName,
        string contentType)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
        var overrideElement = contentTypesXml.Root!
            .Elements(contentTypeNs + "Override")
            .FirstOrDefault(element =>
                string.Equals(element.Attribute("PartName")?.Value, partName, StringComparison.OrdinalIgnoreCase));
        if (overrideElement is null)
        {
            contentTypesXml.Root.Add(new XElement(
                contentTypeNs + "Override",
                new XAttribute("PartName", partName),
                new XAttribute("ContentType", contentType)));
        }
        else
        {
            overrideElement.SetAttributeValue("ContentType", contentType);
        }

        ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);
    }

    private static void AddPackageRootRelationship(
        ZipArchive archive,
        string id,
        string type,
        string target)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationshipsXml = LoadPackageXml(archive.GetEntry("_rels/.rels")!);
        var matching = relationshipsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                string.Equals(relationship.Attribute("Id")?.Value, id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(relationship.Attribute("Type")?.Value, type, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    relationship.Attribute("Target")?.Value.TrimStart('/'),
                    target.TrimStart('/'),
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var relationship in matching)
            relationship.Remove();

        relationshipsXml.Root!.Add(new XElement(
            packageRelNs + "Relationship",
            new XAttribute("Id", id),
            new XAttribute("Type", type),
            new XAttribute("Target", target)));
        ReplacePackageXml(archive, "_rels/.rels", relationshipsXml);
    }
}
