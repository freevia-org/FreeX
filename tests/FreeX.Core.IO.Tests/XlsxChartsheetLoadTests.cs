using System.IO;
using System.IO.Compression;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public class XlsxChartsheetLoadTests
{
    private static string ChartsheetCorpusPath() =>
        TestWorkspaceFiles.FindWorkspaceFile(
            "test-corpus", "public", "tealeg-xlsx", "testchartsheet.xlsx");

    [Fact]
    public void Load_Testchartsheet_LoadsBothSheetsIncludingTheChartsheet()
    {
        using var stream = File.OpenRead(ChartsheetCorpusPath());
        var workbook = new XlsxFileAdapter().Load(stream);

        workbook.Sheets.Select(s => s.Name).Should().BeEquivalentTo(["Chart1", "Sheet1"]);
    }

    [Fact]
    public void Load_Testchartsheet_ChartsheetCarriesItsChartModel()
    {
        using var stream = File.OpenRead(ChartsheetCorpusPath());
        var workbook = new XlsxFileAdapter().Load(stream);

        var chartSheet = workbook.GetSheet("Chart1");
        chartSheet.Should().NotBeNull();
        chartSheet!.IsChartsheet.Should().BeTrue();
        chartSheet.ChartsheetChart.Should().NotBeNull();
        chartSheet.ChartsheetChart!.Type.Should().Be(ChartType.Line);
    }

    [Fact]
    public void Load_Testchartsheet_NormalWorksheetIsNotAChartsheet()
    {
        using var stream = File.OpenRead(ChartsheetCorpusPath());
        var workbook = new XlsxFileAdapter().Load(stream);

        workbook.GetSheet("Sheet1")!.IsChartsheet.Should().BeFalse();
    }

    [Fact]
    public void Inspect_Testchartsheet_DoesNotFlagUnsupportedSheetTypes()
    {
        using var stream = File.OpenRead(ChartsheetCorpusPath());
        using var package = new ZipArchive(stream, ZipArchiveMode.Read);

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Select(f => f.Kind)
            .Should().NotContain(XlsxUnsupportedFeatureKind.UnsupportedSheetTypes);
    }

    [Fact]
    public void Save_AfterEditingAnotherSheet_PreservesTheChartsheetReference()
    {
        var adapter = new XlsxFileAdapter();
        using var source = File.OpenRead(ChartsheetCorpusPath());
        var workbook = adapter.Load(source);

        // Edit a normal worksheet so the save path regenerates workbook.xml.
        var worksheet = workbook.GetSheet("Sheet1")!;
        worksheet.SetCell(new CellAddress(worksheet.Id, 10, 1), new TextValue("edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        archive.GetEntry("xl/chartsheets/sheet1.xml").Should().NotBeNull(
            "the chartsheet package part must survive a save");
        ChartsheetReferenceTargets(archive)
            .Should().ContainSingle()
            .Which.Should().EndWith("chartsheets/sheet1.xml",
                "the workbook <sheet> entry must still point at the chartsheet part");
    }

    private static IEnumerable<string> ChartsheetReferenceTargets(ZipArchive archive)
    {
        var workbookXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry("xl/workbook.xml")!);
        var relsXml = XlsxPackageXmlEditor.LoadXml(archive.GetEntry("xl/_rels/workbook.xml.rels")!);
        System.Xml.Linq.XNamespace mainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        System.Xml.Linq.XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        System.Xml.Linq.XNamespace pkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var byId = relsXml.Root!
            .Elements(pkgRelNs + "Relationship")
            .Where(r => r.Attribute("Type")?.Value
                .EndsWith("/chartsheet", System.StringComparison.OrdinalIgnoreCase) == true)
            .ToDictionary(r => r.Attribute("Id")!.Value, r => r.Attribute("Target")!.Value);

        return workbookXml.Root!
            .Element(mainNs + "sheets")!
            .Elements(mainNs + "sheet")
            .Select(s => s.Attribute(relNs + "id")?.Value)
            .Where(id => id is not null && byId.ContainsKey(id))
            .Select(id => byId[id!]);
    }
}
