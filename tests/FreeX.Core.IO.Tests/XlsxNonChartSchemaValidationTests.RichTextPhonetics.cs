using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxNonChartSchemaValidationTests
{
    [Fact]
    public void LoadedWorkbookPatchSave_WithRichSharedStringPhonetics_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateTextWorkbook("RichSharedStringPhonetics", "Rich phonetic"));
        AddSharedStringRichTextAndPhonetics(source);
        var sourceSharedString = ReadSharedStringByText(source, "Rich phonetic");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadSharedStringByText(saved, "Rich phonetic")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceSharedString.ToString(SaveOptions.DisableFormatting));

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.GetValue(new CellAddress(reloadedSheet.Id, 1, 1))
            .Should()
            .Be(new TextValue("Rich phonetic"));
        reloadedSheet.GetValue(new CellAddress(reloadedSheet.Id, 2, 2))
            .Should()
            .Be(new NumberValue(42));
    }

    [Fact]
    public void LoadedWorkbookPatchSave_WithRichInlineStringPhonetics_ProducesSchemaValidWorkbook()
    {
        using var source = Save(CreateTextWorkbook("RichInlineStringPhonetics", "Inline phonetic"));
        AddInlineStringRichTextAndPhonetics(source);
        var sourceCell = ReadWorksheetCell(source, "A1");
        source.Position = 0;

        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(source);
        XlsxFileAdapter.TryPrepareLoadedPackageSnapshotForEdit(workbook, out var blockReason)
            .Should()
            .BeTrue(blockReason);

        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(42));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);

        adapter.LastSaveDiagnostics.Path.Should().Be(XlsxSavePath.SourcePatch);
        adapter.LastSaveDiagnostics.Reason.Should().Be("patch_applied");
        SchemaErrors(saved).Should().BeEmpty();
        ReadWorksheetCell(saved, "A1")
            .ToString(SaveOptions.DisableFormatting)
            .Should()
            .Be(sourceCell.ToString(SaveOptions.DisableFormatting));

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        reloadedSheet.GetValue(new CellAddress(reloadedSheet.Id, 1, 1))
            .Should()
            .Be(new TextValue("Inline phonetic"));
        reloadedSheet.GetValue(new CellAddress(reloadedSheet.Id, 2, 2))
            .Should()
            .Be(new NumberValue(42));
    }

    private static Workbook CreateTextWorkbook(string name, string text)
    {
        var workbook = new Workbook(name);
        workbook.AddSheet("Data").SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 1, 1), new TextValue(text));
        return workbook;
    }

    private static void AddSharedStringRichTextAndPhonetics(MemoryStream packageStream)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var sharedStringsXml = LoadPackageXml(archive, "xl/sharedStrings.xml");
        var sharedString = sharedStringsXml.Root!
            .Elements(worksheetNs + "si")
            .Single(element => element.Element(worksheetNs + "t")?.Value == "Rich phonetic");
        sharedString.ReplaceNodes(
            new XElement(
                worksheetNs + "r",
                new XElement(
                    worksheetNs + "rPr",
                    new XElement(worksheetNs + "b"),
                    new XElement(worksheetNs + "rFont", new XAttribute("val", "FreeXRich"))),
                new XElement(worksheetNs + "t", "Rich ")),
            new XElement(
                worksheetNs + "r",
                new XElement(worksheetNs + "t", "phonetic")),
            new XElement(
                worksheetNs + "rPh",
                new XAttribute("sb", "0"),
                new XAttribute("eb", "4"),
                new XElement(worksheetNs + "t", "ri-chi")),
            new XElement(
                worksheetNs + "phoneticPr",
                new XAttribute("fontId", "1"),
                new XAttribute("type", "noConversion")));
        ReplacePackageXml(archive, "xl/sharedStrings.xml", sharedStringsXml);
        packageStream.Position = 0;
    }

    private static void AddInlineStringRichTextAndPhonetics(MemoryStream packageStream)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var cell = worksheetXml.Root!
            .Element(worksheetNs + "sheetData")!
            .Descendants(worksheetNs + "c")
            .Single(element => element.Attribute("r")?.Value == "A1");
        cell.SetAttributeValue("t", "inlineStr");
        cell.Elements(worksheetNs + "v").Remove();
        cell.Add(new XElement(
            worksheetNs + "is",
            new XElement(
                worksheetNs + "r",
                new XElement(
                    worksheetNs + "rPr",
                    new XElement(worksheetNs + "i"),
                    new XElement(worksheetNs + "rFont", new XAttribute("val", "FreeXInline"))),
                new XElement(worksheetNs + "t", "Inline ")),
            new XElement(
                worksheetNs + "r",
                new XElement(worksheetNs + "t", "phonetic")),
            new XElement(
                worksheetNs + "rPh",
                new XAttribute("sb", "0"),
                new XAttribute("eb", "6"),
                new XElement(worksheetNs + "t", "in-line")),
            new XElement(
                worksheetNs + "phoneticPr",
                new XAttribute("fontId", "1"),
                new XAttribute("type", "noConversion"))));
        ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        packageStream.Position = 0;
    }

    private static XElement ReadSharedStringByText(Stream stream, string plainText)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return new XElement(ReadPackageRootElement(stream, "xl/sharedStrings.xml")
            .Elements(worksheetNs + "si")
            .Single(element => ReadSharedStringPlainText(element, worksheetNs) == plainText));
    }

    private static string ReadSharedStringPlainText(XElement sharedString, XNamespace worksheetNs)
    {
        var runs = sharedString.Elements(worksheetNs + "r").ToList();
        return runs.Count == 0
            ? sharedString.Element(worksheetNs + "t")?.Value ?? string.Empty
            : string.Concat(runs.Select(run => run.Element(worksheetNs + "t")?.Value ?? string.Empty));
    }

    private static XElement ReadWorksheetCell(Stream stream, string reference)
    {
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        return new XElement(ReadPackageRootElement(stream, "xl/worksheets/sheet1.xml")
            .Element(worksheetNs + "sheetData")!
            .Descendants(worksheetNs + "c")
            .Single(element => element.Attribute("r")?.Value == reference));
    }
}
