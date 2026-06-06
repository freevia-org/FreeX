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
    public void LoadedWorkbookPatchSave_InsertingCellIntoRowWithExtensionList_ProducesSchemaValidWorkbook()
    {
        using var source = CreateRowExtensionInsertSourceWorkbook();
        SchemaErrors(source).Should().BeEmpty();
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("inserted"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var row = ReadSheetDataRow(saved, 1);
        row.Elements().Select(element => element.Name.LocalName).Should().Equal("c", "c", "extLst");
        row.Elements().Last().Element(worksheetNs + "ext")
            .Should()
            .NotBeNull();

        var insertedCell = ReadCellElement(saved, "B1");
        insertedCell.Attribute("t")!.Value.Should().Be("inlineStr");
        insertedCell.Element(worksheetNs + "is")!
            .Value
            .Should()
            .Be("inserted");
    }

    private static MemoryStream CreateRowExtensionInsertSourceWorkbook()
    {
        var workbook = new Workbook("RowExtensionInsertPatchSave");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kept"));

        var stream = Save(workbook);
        AddFirstRowExtensionList(stream);
        stream.Position = 0;
        return stream;
    }

    private static void AddFirstRowExtensionList(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace freexNs = "urn:freex:test";
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var row = worksheetXml
            .Root!
            .Element(worksheetNs + "sheetData")!
            .Elements(worksheetNs + "row")
            .Single(element => element.Attribute("r")?.Value == "1");

        row.Elements(worksheetNs + "extLst").Remove();
        row.Add(new XElement(
            worksheetNs + "extLst",
            new XElement(
                worksheetNs + "ext",
                new XAttribute("uri", "{FREEX-ROW-EXTENSION}"),
                new XElement(freexNs + "rowExt", new XAttribute("value", "row-extension")))));

        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static XElement ReadSheetDataRow(MemoryStream stream, uint rowNumber)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return new XElement(LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!)
            .Root!
            .Element(worksheetNs + "sheetData")!
            .Elements(worksheetNs + "row")
            .Single(element => element.Attribute("r")?.Value == rowNumber.ToString()));
    }
}
