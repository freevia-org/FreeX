using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Excel's "Switch Row/Column" (<see cref="ChartModel.SeriesInRows"/>): the XLSX writer must emit
/// one horizontal row strip per series (series names from the first column, categories from the
/// first row), and the reader must re-detect the orientation from those formulas so the switch
/// survives a save/load round-trip.
/// </summary>
public sealed class XlsxChartSwitchRowColumnTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Fact]
    public void Save_SwitchedColumnChartWritesRowStripSeriesFormulas()
    {
        using var saved = XlsxPackageTestHelper.SaveWorkbook(CreateSwitchedColumnWorkbook());
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);

        var chartXml = XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/charts/chart1.xml", "xl/charts/chart1.xml");
        var barChart = chartXml.Descendants(ChartNs + "barChart").Should().ContainSingle().Subject;
        var series = barChart.Elements(ChartNs + "ser").ToList();
        series.Should().HaveCount(2);

        ReadFormula(series[0], "val").Should().Be("Data!$B$2:$D$2");
        ReadFormula(series[1], "val").Should().Be("Data!$B$3:$D$3");
        ReadFormula(series[0], "cat").Should().Be("Data!$B$1:$D$1");
        ReadFormula(series[0], "tx").Should().Be("Data!$A$2");
        ReadFormula(series[1], "tx").Should().Be("Data!$A$3");
    }

    [Fact]
    public void SaveLoad_SwitchedColumnChartRoundTripsSeriesInRows()
    {
        using var saved = XlsxPackageTestHelper.SaveWorkbook(CreateSwitchedColumnWorkbook());
        var loaded = new XlsxFileAdapter().Load(saved);

        var chart = (ChartModel)loaded.Sheets[0].Charts.Should().ContainSingle().Subject!;
        chart.SeriesInRows.Should().BeTrue("row-strip series formulas identify a switched chart");
        chart.FirstRowIsHeader.Should().BeTrue();
        chart.FirstColIsCategories.Should().BeTrue();
        chart.DataRange.Start.Row.Should().Be(1u);
        chart.DataRange.Start.Col.Should().Be(1u);
        chart.DataRange.End.Row.Should().Be(3u);
        chart.DataRange.End.Col.Should().Be(4u);

        // A second save/load must be stable (writer and reader agree on the orientation).
        using var resaved = XlsxPackageTestHelper.SaveWorkbook(loaded);
        var reloaded = new XlsxFileAdapter().Load(resaved);
        var rereadChart = (ChartModel)reloaded.Sheets[0].Charts.Single()!;
        rereadChart.SeriesInRows.Should().BeTrue();
    }

    [Fact]
    public void SaveLoad_DefaultColumnChartStaysColumnMajor()
    {
        var workbook = CreateSwitchedColumnWorkbook();
        ((ChartModel)workbook.Sheets[0].Charts.Single()!).SeriesInRows = false;

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);
        var loaded = new XlsxFileAdapter().Load(saved);

        var chart = (ChartModel)loaded.Sheets[0].Charts.Single()!;
        chart.SeriesInRows.Should().BeFalse("column-strip series formulas keep the default orientation");
    }

    //   A       B    C    D
    // 1         Q1   Q2   Q3
    // 2 Sales   10   20   30
    // 3 Costs    5    8   13
    private static Workbook CreateSwitchedColumnWorkbook()
    {
        var workbook = new Workbook("SwitchRowColumn");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Q3"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Costs"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new NumberValue(13));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            Title = "Switched",
            SeriesInRows = true,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 4)),
        });
        return workbook;
    }

    private static string? ReadFormula(XElement series, string containerName) =>
        series.Elements()
            .Single(element => element.Name.LocalName == containerName)
            .Descendants()
            .SingleOrDefault(element => element.Name.LocalName == "f")?
            .Value;
}
