using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void CustomXmlPackageMetadata_ProducesSchemaValidWorkbook()
    {
        using var source = CreateCustomXmlSourcePackage();

        SchemaErrors(source).Should().BeEmpty();
        AssertCustomXmlPackage(source);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithCustomXmlPackageMetadata_ProducesSchemaValidWorkbook()
    {
        using var source = CreateCustomXmlSourcePackage();
        var sourceRootRelationships = ReadPackageRootElement(source, "_rels/.rels");
        var sourceItemText = ReadPackageEntryText(source, "customXml/item1.xml");
        var sourceItemProperties = ReadPackageRootElement(source, "customXml/itemProps1.xml");
        var sourceItemRelationships = ReadPackageRootElement(source, "customXml/_rels/item1.xml.rels");
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
        AssertCustomXmlPackage(saved);
        ReadPackageRootElement(saved, "_rels/.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceRootRelationships.ToString(SaveOptions.DisableFormatting));
        ReadPackageEntryText(saved, "customXml/item1.xml").Should().Be(sourceItemText);
        ReadPackageRootElement(saved, "customXml/itemProps1.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceItemProperties.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "customXml/_rels/item1.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceItemRelationships.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void LoadedWorkbookFullSave_WithValidCustomXmlPackageGraph_PreservesRelationshipsAndContentTypes()
    {
        using var source = CreateCustomXmlSourcePackageWithSecondItem();
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var newSheet = workbook.AddSheet("New Sheet");
        newSheet.SetCell(new CellAddress(newSheet.Id, 1, 1), new TextValue("forces full save"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.FullSave);
        adapter.LastSaveDiagnostics.Reason.Should().Be("change_sheet_count");
        SchemaErrors(saved).Should().BeEmpty();
        AssertCustomXmlPackage(saved);
        AssertSecondCustomXmlPackageItem(saved);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithInvalidCustomXmlProperties_DropsInvalidSidecarGraph()
    {
        using var source = CreateCustomXmlSourcePackage();
        ReplaceCustomXmlPropertiesWithInvalidRoot(source);
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
        ReadPackageEntryText(saved, "customXml/item1.xml")
            .Should()
            .Contain("retained-custom-xml");
        PackageEntryNames(saved).Should().NotContain("customXml/itemProps1.xml");
        PackageEntryNames(saved).Should().NotContain("customXml/_rels/item1.xml.rels");
        ContentTypeOverridePartNames(saved).Should().NotContain("/customXml/itemProps1.xml");
    }

    private static MemoryStream CreateCustomXmlSourcePackage()
    {
        var workbook = new Workbook("CustomXmlPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("custom xml"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));

        var stream = Save(workbook);
        AddCustomXmlPackage(stream);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateCustomXmlSourcePackageWithSecondItem()
    {
        var stream = CreateCustomXmlSourcePackage();
        stream.Position = 0;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace customXmlNs = "http://schemas.openxmlformats.org/officeDocument/2006/customXml";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            WritePackageEntry(archive, "customXml/item2.xml", """
                <root xmlns="urn:freex:customXml">
                  <value>retained-second-custom-xml</value>
                </root>
                """);
            ReplacePackageXml(archive, "customXml/itemProps2.xml", new XDocument(
                new XElement(
                    customXmlNs + "datastoreItem",
                    new XAttribute(customXmlNs + "itemID", "{11111111-2222-3333-4444-555555555555}"))));
            ReplacePackageXml(archive, "customXml/_rels/item2.xml.rels", new XDocument(
                new XElement(
                    packageRelNs + "Relationships",
                    new XElement(
                        packageRelNs + "Relationship",
                        new XAttribute("Id", "rIdFreeXSecondItemProps"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"),
                        new XAttribute("Target", "itemProps2.xml")))));
            AddPackageContentTypeOverride(
                archive,
                "/customXml/itemProps2.xml",
                "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
            AddCustomXmlPackageRootRelationship(
                archive,
                "rIdFreeXSecondCustomXml",
                "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml",
                "customXml/item2.xml");
        }

        stream.Position = 0;
        return stream;
    }

    private static void AddCustomXmlPackageRootRelationship(
        ZipArchive archive,
        string id,
        string type,
        string target)
    {
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var relationshipsXml = LoadPackageXml(archive, "_rels/.rels");
        var matching = relationshipsXml.Root!
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                string.Equals(relationship.Attribute("Id")?.Value, id, StringComparison.OrdinalIgnoreCase) ||
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

    private static void AddCustomXmlPackage(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace customXmlNs = "http://schemas.openxmlformats.org/officeDocument/2006/customXml";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        WritePackageEntry(archive, "customXml/item1.xml", """
            <root xmlns="urn:freex:customXml">
              <value>retained-custom-xml</value>
            </root>
            """);
        ReplacePackageXml(archive, "customXml/itemProps1.xml", new XDocument(
            new XElement(
                customXmlNs + "datastoreItem",
                new XAttribute(customXmlNs + "itemID", "{01234567-89AB-CDEF-0123-456789ABCDEF}"))));
        ReplacePackageXml(archive, "customXml/_rels/item1.xml.rels", new XDocument(
            new XElement(
                packageRelNs + "Relationships",
                new XElement(
                    packageRelNs + "Relationship",
                    new XAttribute("Id", "rIdFreeXItemProps"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"),
                    new XAttribute("Target", "itemProps1.xml")))));
        AddPackageContentTypeOverride(
            archive,
            "/customXml/itemProps1.xml",
            "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
        AddPackageRootRelationship(
            archive,
            "rIdFreeXCustomXml",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml",
            "customXml/item1.xml");
    }

    private static void ReplaceCustomXmlPropertiesWithInvalidRoot(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        ReplacePackageXml(archive, "customXml/itemProps1.xml", new XDocument(new XElement("notDatastoreItem")));
    }

    private static void AssertCustomXmlPackage(Stream stream)
    {
        XNamespace customXmlNs = "http://schemas.openxmlformats.org/officeDocument/2006/customXml";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        ReadPackageEntryText(stream, "customXml/item1.xml")
            .Should()
            .Contain("retained-custom-xml");
        ReadPackageRootElement(stream, "customXml/itemProps1.xml")
            .Attribute(customXmlNs + "itemID")!
            .Value
            .Should()
            .Be("{01234567-89AB-CDEF-0123-456789ABCDEF}");
        ReadPackageRootElement(stream, "customXml/itemProps1.xml")
            .Name
            .Should()
            .Be(customXmlNs + "datastoreItem");

        ReadPackageRootElement(stream, "_rels/.rels")
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Id")?.Value == "rIdFreeXCustomXml" &&
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml" &&
                relationship.Attribute("Target")?.Value == "customXml/item1.xml")
            .Should()
            .ContainSingle();

        ReadPackageRootElement(stream, "customXml/_rels/item1.xml.rels")
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Id")?.Value == "rIdFreeXItemProps" &&
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps" &&
                relationship.Attribute("Target")?.Value == "itemProps1.xml")
            .Should()
            .ContainSingle();
    }

    private static void AssertSecondCustomXmlPackageItem(Stream stream)
    {
        XNamespace customXmlNs = "http://schemas.openxmlformats.org/officeDocument/2006/customXml";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        ReadPackageEntryText(stream, "customXml/item2.xml")
            .Should()
            .Contain("retained-second-custom-xml");
        ReadPackageRootElement(stream, "customXml/itemProps2.xml")
            .Attribute(customXmlNs + "itemID")!
            .Value
            .Should()
            .Be("{11111111-2222-3333-4444-555555555555}");
        ReadPackageRootElement(stream, "customXml/itemProps2.xml")
            .Name
            .Should()
            .Be(customXmlNs + "datastoreItem");
        ReadPackageRootElement(stream, "_rels/.rels")
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Id")?.Value == "rIdFreeXSecondCustomXml" &&
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml" &&
                relationship.Attribute("Target")?.Value == "customXml/item2.xml")
            .Should()
            .ContainSingle();
        ReadPackageRootElement(stream, "customXml/_rels/item2.xml.rels")
            .Elements(packageRelNs + "Relationship")
            .Where(relationship =>
                relationship.Attribute("Id")?.Value == "rIdFreeXSecondItemProps" &&
                relationship.Attribute("Type")?.Value == "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps" &&
                relationship.Attribute("Target")?.Value == "itemProps2.xml")
            .Should()
            .ContainSingle();
        ContentTypeOverridePartNames(stream).Should().Contain("/customXml/itemProps2.xml");
    }

    private static void WritePackageEntry(ZipArchive archive, string entryName, string content)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }

    private static string ReadPackageEntryText(Stream stream, string entryName)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry(entryName)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static List<string> PackageEntryNames(Stream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        return archive.Entries.Select(entry => entry.FullName).ToList();
    }

    private static List<string> ContentTypeOverridePartNames(Stream stream)
    {
        XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
        return ReadPackageRootElement(stream, "[Content_Types].xml")
            .Elements(contentTypeNs + "Override")
            .Select(element => element.Attribute("PartName")?.Value)
            .OfType<string>()
            .ToList();
    }
}
