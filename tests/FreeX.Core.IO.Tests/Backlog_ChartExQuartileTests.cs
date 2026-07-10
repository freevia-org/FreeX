using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Backlog item chartex-quartile: BoxAndWhisker cx:layoutPr/cx:statistics/@quartileMethod must be
/// sourced from <see cref="ChartModel.QuartileMethod"/> (not hardcoded "exclusive"), must round-trip
/// through a full save/load, and must not corrupt the chartEx package.
/// </summary>
public sealed class Backlog_chartex_quartile_Tests
{
    private static readonly XNamespace ChartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";

    private static MemoryStream SaveBoxAndWhiskerWorkbook(string? quartileMethod)
    {
        var workbook = new Workbook("ChartExQuartileTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        var chart = new ChartModel
        {
            Type = ChartType.BoxAndWhisker,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Title = "BoxAndWhisker",
            QuartileMethod = quartileMethod,
        };
        sheet.Charts.Add(chart);

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        return saved;
    }

    private static XDocument LoadChartXml(ZipArchive archive) =>
        XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/charts/chart1.xml");

    [Fact]
    public void Save_WritesInclusiveQuartileMethodWhenSetOnModel()
    {
        var saved = SaveBoxAndWhiskerWorkbook("inclusive");

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadChartXml(archive);
        var series = chartXml.Root!
            .Element(ChartExNs + "chart")!
            .Element(ChartExNs + "plotArea")!
            .Element(ChartExNs + "plotAreaRegion")!
            .Elements(ChartExNs + "series")
            .Should().ContainSingle().Subject;

        var layoutPr = series.Element(ChartExNs + "layoutPr")!;
        layoutPr.Element(ChartExNs + "statistics")!.Attribute("quartileMethod")!.Value
            .Should().Be("inclusive");
    }

    [Fact]
    public void Save_DefaultsToExclusiveWhenQuartileMethodUnset()
    {
        var saved = SaveBoxAndWhiskerWorkbook(quartileMethod: null);

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadChartXml(archive);
        var series = chartXml.Root!
            .Element(ChartExNs + "chart")!
            .Element(ChartExNs + "plotArea")!
            .Element(ChartExNs + "plotAreaRegion")!
            .Elements(ChartExNs + "series")
            .Should().ContainSingle().Subject;

        var layoutPr = series.Element(ChartExNs + "layoutPr")!;
        layoutPr.Element(ChartExNs + "statistics")!.Attribute("quartileMethod")!.Value
            .Should().Be("exclusive");
    }

    [Fact]
    public void SaveLoad_InclusiveQuartileMethodSurvivesFullRoundTripWithoutCorruption()
    {
        var saved = SaveBoxAndWhiskerWorkbook("inclusive");

        // (a) full XlsxFileAdapter reload must not throw WorkbookInvalidException (i.e. the
        // cx:statistics element must be positioned correctly within cx:layoutPr per the
        // ECMA chartex CT_SeriesLayoutProperties schema order).
        var loadAct = () => new XlsxFileAdapter().Load(saved);
        loadAct.Should().NotThrow();

        saved.Position = 0;
        var loaded = new XlsxFileAdapter().Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        // (b) quartileMethod survives as "inclusive".
        loadedChart.QuartileMethod.Should().Be("inclusive");

        // Resave + reload again to confirm the round-trip is stable across multiple cycles.
        var resaved = new MemoryStream();
        new XlsxFileAdapter().Save(loaded, resaved);
        resaved.Position = 0;

        var resaveLoadAct = () => new XlsxFileAdapter().Load(resaved);
        resaveLoadAct.Should().NotThrow();

        resaved.Position = 0;
        var reloaded = new XlsxFileAdapter().Load(resaved);
        reloaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject.QuartileMethod.Should().Be("inclusive");
    }

    [Fact]
    public void SaveLoad_ExclusiveQuartileMethodRoundTrips()
    {
        var saved = SaveBoxAndWhiskerWorkbook("exclusive");

        var loaded = new XlsxFileAdapter().Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.QuartileMethod.Should().Be("exclusive");
    }
}
