using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for cleanup batch MED16:
///  - P87: series-level chart data-label separator must be written as CT_DLbls
///    element text (per CT_DLbls' xsd:string content), not as a `val` attribute,
///    or Excel silently reverts to the default ", " separator on every round-trip.
///  - P17: scenario changing-cells whose captured value is blank (an empty cell)
///    must still be written to xlsx `inputCells` (with an empty val="") instead of
///    being silently dropped, matching the native .fxl format's round-trip fidelity.
/// </summary>
public sealed class FreeXCleanupMED16Tests
{
    private static XDocument LoadPackageXml(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName)!;
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    [Fact]
    public void XlsxAdapter_Save_WritesSeriesDataLabelSeparatorAsElementTextNotAttribute()
    {
        var workbook = new Workbook("SeriesSeparatorElementText");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            Title = "Sales",
            ShowDataLabels = true,
            SeriesDataLabelFormats =
            [
                new ChartSeriesDataLabelFormat(0, SeparatorText: "; ")
            ],
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        var chartXml = LoadPackageXml(archive, "xl/charts/chart1.xml");
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        var separator = chartXml.Descendants(chartNs + "ser")
            .Single()
            .Element(chartNs + "dLbls")!
            .Element(chartNs + "separator")!;

        // The correct CT_DLbls representation carries the separator as element
        // text, not a `val` attribute (which Excel does not read for this element).
        separator.Attribute("val").Should().BeNull();
        separator.Value.Should().Be("; ");

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.SeriesDataLabelFormats.Should().ContainSingle()
            .Which.SeparatorText.Should().Be("; ");
    }

    [Fact]
    public void XlsxAdapter_Save_RetainsBlankScenarioChangingCellInsteadOfDroppingIt()
    {
        var workbook = new Workbook("ScenarioBlankRetention");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        workbook.Scenarios.Add(new WorkbookScenario(
            "BestCase",
            [
                new ScenarioCellValue(new CellAddress(sheet.Id, 1, 1), new NumberValue(10)),
                new ScenarioCellValue(new CellAddress(sheet.Id, 1, 2), BlankValue.Instance)
            ]));

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive, "xl/worksheets/sheet1.xml");
        var inputCells = worksheetXml.Root!
            .Element(worksheetNs + "scenarios")!
            .Element(worksheetNs + "scenario")!
            .Elements(worksheetNs + "inputCells")
            .ToList();

        inputCells.Should().HaveCount(2, "the blank changing cell must be written, not silently dropped");
        var blankCell = inputCells.Single(cell => cell.Attribute("r")!.Value == "B1");
        blankCell.Attribute("val")!.Value.Should().BeEmpty();

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        var loadedScenario = loaded.Scenarios.Should().ContainSingle().Subject;
        loadedScenario.ChangingCells.Should().HaveCount(2);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedScenario.ChangingCells.Should().Contain(new ScenarioCellValue(
            new CellAddress(loadedSheet.Id, 1, 2),
            BlankValue.Instance));
    }
}
