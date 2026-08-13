using System.IO.Compression;
using System.Xml.Linq;

using FluentAssertions;

using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public partial class FileAdapterSmokeTests
{
    [Fact]
    public void LoadedWorkbookFullSave_SharedPreservationContextRetainsMultiplePackageFamilies()
    {
        var workbook = new Workbook("Shared preservation context");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Value"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Alpha"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(7));
        sheet.Comments[new CellAddress(sheet.Id, 2, 2)] = "Keep this note";
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "DataTable",
            DisplayName = "DataTable",
            Range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 2)),
            HasAutoFilter = true,
            HeaderRowCount = 1,
            Columns =
            {
                new StructuredTableColumnModel(1, "Name"),
                new StructuredTableColumnModel(2, "Value")
            }
        });

        var adapter = new XlsxFileAdapter();
        using var source = new MemoryStream();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalPrinterSettingsPackage(source);
        AddMinimalExternalLinkPackage(source);
        AddMinimalVbaProjectPackage(source);
        AddUnknownPreservationContextPart(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheet("Data")!;
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 3, 1), new TextValue("full-save edit"));

        using var saved = new MemoryStream();
        adapter.SavePreservingVbaProject(loaded, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            archive.GetEntry("xl/vbaProject.bin").Should().NotBeNull();
            archive.GetEntry("xl/externalLinks/externalLink1.xml").Should().NotBeNull();
            archive.GetEntry("xl/printerSettings/printerSettings1.bin").Should().NotBeNull();
            archive.GetEntry("xl/tables/table1.xml").Should().NotBeNull();
            archive.Entries.Should().Contain(entry =>
                entry.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            archive.Entries.Should().Contain(entry =>
                entry.FullName.StartsWith("xl/drawings/vmlDrawing", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".vml", StringComparison.OrdinalIgnoreCase));

            var unknownEntry = archive.GetEntry("xl/customData/freeX-preserved.bin");
            unknownEntry.Should().NotBeNull();
            using (var stream = unknownEntry!.Open())
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                memory.ToArray().Should().Equal(0x46, 0x58, 0x50, 0x52, 0x45, 0x53);
            }

            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Element(worksheetNs + "tableParts").Should().NotBeNull();
            var pageSetupRelId = worksheetXml.Root.Element(worksheetNs + "pageSetup")?
                .Attribute(relNs + "id")?.Value;
            pageSetupRelId.Should().NotBeNullOrWhiteSpace();

            var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
            worksheetRelsXml.Root!
                .Elements(packageRelNs + "Relationship")
                .Should().Contain(relationship =>
                    (string?)relationship.Attribute("Id") == pageSetupRelId &&
                    (string?)relationship.Attribute("Type") ==
                        "http://schemas.openxmlformats.org/officeDocument/2006/relationships/printerSettings");

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!
                .Element(worksheetNs + "externalReferences")?
                .Elements(worksheetNs + "externalReference")
                .Should().NotBeEmpty();
        }

        saved.Position = 0;
        var roundTripped = adapter.Load(saved);
        var roundTrippedSheet = roundTripped.GetSheet("Data")!;
        roundTrippedSheet.StructuredTables.Should().ContainSingle(table => table.Name == "DataTable");
        roundTrippedSheet.Comments.Should().Contain(
            new CellAddress(roundTrippedSheet.Id, 2, 2),
            "Keep this note");
        roundTrippedSheet.GetValue(3, 1).Should().Be(new TextValue("full-save edit"));
    }

    private static void AddUnknownPreservationContextPart(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            const string partPath = "xl/customData/freeX-preserved.bin";

            archive.GetEntry(partPath)?.Delete();
            var partEntry = archive.CreateEntry(partPath);
            using (var stream = partEntry.Open())
                stream.Write([0x46, 0x58, 0x50, 0x52, 0x45, 0x53]);

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(
                contentTypesXml,
                contentTypeNs,
                "/" + partPath,
                "application/vnd.freex.preservation-context-test");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var workbookRelsPath = "xl/_rels/workbook.xml.rels";
            var workbookRelsXml = LoadPackageXml(archive.GetEntry(workbookRelsPath)!);
            workbookRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreeXPreservationContext"),
                new XAttribute("Type", "https://freex.example/relationships/preservation-context"),
                new XAttribute("Target", "customData/freeX-preserved.bin")));
            ReplacePackageXml(archive, workbookRelsPath, workbookRelsXml);
        }

        packageStream.Position = 0;
    }
}
