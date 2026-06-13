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
        using var source = CreateMultiItemCustomXmlSourcePackage();

        SchemaErrors(source).Should().BeEmpty();
        AssertCustomXmlPackage(source);
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithCustomXmlPackageMetadata_ProducesSchemaValidWorkbook()
    {
        using var source = CreateMultiItemCustomXmlSourcePackage();
        var sourceRootRelationships = ReadPackageRootElement(source, "_rels/.rels");
        var sourceItemText = ReadPackageEntryText(source, "customXml/item1.xml");
        var sourceSecondItemText = ReadPackageEntryText(source, "customXml/item2.xml");
        var sourceItemProperties = ReadPackageRootElement(source, "customXml/itemProps1.xml");
        var sourceSecondItemProperties = ReadPackageRootElement(source, "customXml/itemProps2.xml");
        var sourceItemRelationships = ReadPackageRootElement(source, "customXml/_rels/item1.xml.rels");
        var sourceSecondItemRelationships = ReadPackageRootElement(source, "customXml/_rels/item2.xml.rels");
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
        ReadPackageEntryText(saved, "customXml/item2.xml").Should().Be(sourceSecondItemText);
        ReadPackageRootElement(saved, "customXml/itemProps1.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceItemProperties.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "customXml/itemProps2.xml")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceSecondItemProperties.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "customXml/_rels/item1.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceItemRelationships.ToString(SaveOptions.DisableFormatting));
        ReadPackageRootElement(saved, "customXml/_rels/item2.xml.rels")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceSecondItemRelationships.ToString(SaveOptions.DisableFormatting));
    }

    [Fact]
    public void LoadedWorkbookFullSave_WithValidCustomXmlPackageGraph_PreservesRelationshipsAndContentTypes()
    {
        using var source = CreateMultiItemCustomXmlSourcePackage();
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
    }

    private static MemoryStream CreateCustomXmlSourcePackage()
    {
        var workbook = new Workbook("CustomXmlPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("custom xml"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));

        var stream = Save(workbook);
        AddCustomXmlPackage(stream, includeSecondItem: false);
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream CreateMultiItemCustomXmlSourcePackage()
    {
        var workbook = new Workbook("CustomXmlPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("custom xml"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(24));

        var stream = Save(workbook);
        AddCustomXmlPackage(stream, includeSecondItem: true);
        stream.Position = 0;
        return stream;
    }

    private static void AddCustomXmlPackage(MemoryStream stream, bool includeSecondItem)
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
        WritePackageEntry(archive, "customXml/item2.xml", """
            <root xmlns="urn:freex:customXml">
              <value>retained-second-custom-xml</value>
            </root>
            """);
        ReplacePackageXml(archive, "customXml/itemProps1.xml", new XDocument(
            new XElement(
                customXmlNs + "datastoreItem",
                new XAttribute(customXmlNs + "itemID", "{01234567-89AB-CDEF-0123-456789ABCDEF}"))));
        ReplacePackageXml(archive, "customXml/itemProps2.xml", new XDocument(
            new XElement(
                customXmlNs + "datastoreItem",
                new XAttribute(customXmlNs + "itemID", "{11111111-2222-3333-4444-555555555555}"))));
        ReplacePackageXml(archive, "customXml/_rels/item1.xml.rels", new XDocument(
            new XElement(
                packageRelNs + "Relationships",
                new XElement(
                    packageRelNs + "Relationship",
                    new XAttribute("Id", "rIdFreeXItemProps"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXmlProps"),
                    new XAttribute("Target", "itemProps1.xml")))));
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
            "/customXml/itemProps1.xml",
            "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
        AddPackageContentTypeOverride(
            archive,
            "/customXml/itemProps2.xml",
            "application/vnd.openxmlformats-officedocument.customXmlProperties+xml");
        AddPackageRootRelationship(
            archive,
            "rIdFreeXCustomXml",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml",
            "customXml/item1.xml");
        AddPackageRootRelationship(
            archive,
            "rIdFreeXSecondCustomXml",
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/customXml",
            "customXml/item2.xml");
    }

    private static void AssertCustomXmlPackage(Stream stream)
    {
        XNamespace customXmlNs = "http://schemas.openxmlformats.org/officeDocument/2006/customXml";
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        ReadPackageEntryText(stream, "customXml/item1.xml")
            .Should()
            .Contain("retained-custom-xml");
        ReadPackageEntryText(stream, "customXml/item2.xml")
            .Should()
            .Contain("retained-second-custom-xml");
        ReadPackageRootElement(stream, "customXml/itemProps1.xml")
            .Attribute(customXmlNs + "itemID")!
            .Value
            .Should()
            .Be("{01234567-89AB-CDEF-0123-456789ABCDEF}");
        ReadPackageRootElement(stream, "customXml/itemProps1.xml")
            .Name
            .Should()
            .Be(customXmlNs + "datastoreItem");
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

        ContentTypeOverridePartNames(stream)
            .Where(partName => partName.StartsWith("/customXml/itemProps", StringComparison.OrdinalIgnoreCase))
            .Should()
            .BeEquivalentTo("/customXml/itemProps1.xml", "/customXml/itemProps2.xml");
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
}
