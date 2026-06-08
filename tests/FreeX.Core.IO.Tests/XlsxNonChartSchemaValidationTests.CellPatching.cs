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

        saved.Position = 0;
        var reloadedSheet = adapter.Load(saved).GetSheetAt(0);
        reloadedSheet.GetCell(1, 1)!.Value.Should().Be(new TextValue("kept"));
        reloadedSheet.GetCell(1, 2)!.Value.Should().Be(new TextValue("inserted"));
    }

    [Fact]
    public void LoadedWorkbookPatchSave_RewritingStyleOnlyCellWithExtensionList_ProducesSchemaValidWorkbook()
    {
        using var source = CreateStyleOnlyCellExtensionPatchSourceWorkbook();
        SchemaErrors(source).Should().BeEmpty();
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        var styleOnlyStyleId = sheet.GetStyleOnly(1, 1);
        styleOnlyStyleId.Should().NotBeNull();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell
        {
            Value = new TextValue("filled"),
            StyleId = styleOnlyStyleId!.Value
        });

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();

        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cell = ReadCellElement(saved, "A1");
        cell.Elements().Select(element => element.Name.LocalName).Should().Equal("is", "extLst");
        cell.Element(worksheetNs + "is")!.Value.Should().Be("filled");
        cell.Element(worksheetNs + "extLst")!
            .Element(worksheetNs + "ext")
            .Should()
            .NotBeNull();

        saved.Position = 0;
        var reloadedSheet = adapter.Load(saved).GetSheetAt(0);
        var reloadedCell = reloadedSheet.GetCell(1, 1);
        reloadedCell.Should().NotBeNull();
        reloadedCell.Value.Should().Be(new TextValue("filled"));
        reloadedCell.StyleId.Should().Be(styleOnlyStyleId.Value);
        reloadedSheet.GetStyleOnly(1, 1).Should().BeNull();
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

    private static MemoryStream CreateStyleOnlyCellExtensionPatchSourceWorkbook()
    {
        var workbook = new Workbook("StyleOnlyCellExtensionPatchSave");
        var styleId = workbook.RegisterStyle(new CellStyle { Bold = true });
        workbook.AddSheet("Data").SetStyleOnly(1, 1, styleId);

        var stream = Save(workbook);
        AddStyleOnlyCellExtensionList(stream);
        stream.Position = 0;
        return stream;
    }

    private static void AddFirstRowExtensionList(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace freexNs = "urn:freex:test";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
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

    private static void AddStyleOnlyCellExtensionList(MemoryStream stream)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace freexNs = "urn:freex:test";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var cell = worksheetXml
            .Root!
            .Element(worksheetNs + "sheetData")!
            .Descendants(worksheetNs + "c")
            .Single(element => element.Attribute("r")?.Value == "A1");

        cell.Elements(worksheetNs + "extLst").Remove();
        cell.Add(new XElement(
            worksheetNs + "extLst",
            new XElement(
                worksheetNs + "ext",
                new XAttribute("uri", "{FREEX-STYLE-ONLY-CELL-EXTENSION}"),
                new XElement(freexNs + "cellExt", new XAttribute("value", "style-only-cell-extension")))));

        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
    }

    private static XElement ReadSheetDataRow(MemoryStream stream, uint rowNumber)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return new XElement(LoadPackageXml(archive, "xl/worksheets/sheet1.xml")
            .Root!
            .Element(worksheetNs + "sheetData")!
            .Elements(worksheetNs + "row")
            .Single(element => element.Attribute("r")?.Value == rowNumber.ToString()));
    }
}
