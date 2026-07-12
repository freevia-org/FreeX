using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R30-io-chart-series-cache-deep-1/2: trendline/error-bars are a chart-GLOBAL capture with no
/// source-series index (always reattached to series 0 on save), and the secondary value axis's own
/// title/min/max/number-format were never read (the writer cloned the primary axis's settings onto
/// it). Each test below pairs the fixed bug scenario with an already-working sibling case to guard
/// against over-correcting the common (series-0 / primary-axis-only) path.
/// </summary>
public sealed class R30_ChartSeriesCacheDeepTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Fact]
    public void ColumnChart_TrendlineOnNonZeroSeries_RoundTripsOntoThatSeries()
    {
        var workbook = CreateThreeSeriesColumnWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.ShowLinearTrendline = true;
        chart.TrendlineSeriesIndex = 2;
        chart.TrendlineType = ChartTrendlineType.Linear;

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var seriesElements = chartXml.Descendants(ChartNs + "barChart").Single().Elements(ChartNs + "ser").ToList();
        seriesElements.Should().HaveCount(3);
        seriesElements[0].Element(ChartNs + "trendline").Should().BeNull();
        seriesElements[1].Element(ChartNs + "trendline").Should().BeNull();
        seriesElements[2].Element(ChartNs + "trendline").Should().NotBeNull();

        var reloaded = ReloadSingleChart(saved);
        reloaded.ShowLinearTrendline.Should().BeTrue();
        reloaded.TrendlineSeriesIndex.Should().Be(2);
    }

    [Fact]
    public void ColumnChart_TrendlineOnSeriesZero_StillRoundTripsOntoSeriesZero()
    {
        // Already-working sibling: the common case (trendline on the first/only series) must keep
        // working exactly as before the per-series fix.
        var workbook = CreateThreeSeriesColumnWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.ShowLinearTrendline = true;
        chart.TrendlineSeriesIndex = 0;
        chart.TrendlineType = ChartTrendlineType.Linear;

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var seriesElements = chartXml.Descendants(ChartNs + "barChart").Single().Elements(ChartNs + "ser").ToList();
        seriesElements[0].Element(ChartNs + "trendline").Should().NotBeNull();
        seriesElements[1].Element(ChartNs + "trendline").Should().BeNull();
        seriesElements[2].Element(ChartNs + "trendline").Should().BeNull();

        var reloaded = ReloadSingleChart(saved);
        reloaded.ShowLinearTrendline.Should().BeTrue();
        reloaded.TrendlineSeriesIndex.Should().Be(0);
    }

    [Fact]
    public void ColumnChart_ErrorBarsOnNonZeroSeries_RoundTripOntoThatSeries()
    {
        var workbook = CreateThreeSeriesColumnWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.ShowErrorBars = true;
        chart.ErrorBarSeriesIndex = 1;
        chart.ErrorBarKind = ChartErrorBarKind.Percentage;
        chart.ErrorBarValue = 10;

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var seriesElements = chartXml.Descendants(ChartNs + "barChart").Single().Elements(ChartNs + "ser").ToList();
        seriesElements[0].Element(ChartNs + "errBars").Should().BeNull();
        seriesElements[1].Element(ChartNs + "errBars").Should().NotBeNull();
        seriesElements[2].Element(ChartNs + "errBars").Should().BeNull();

        var reloaded = ReloadSingleChart(saved);
        reloaded.ShowErrorBars.Should().BeTrue();
        reloaded.ErrorBarSeriesIndex.Should().Be(1);
    }

    [Fact]
    public void ColumnChart_ErrorBarsOnSeriesZero_StillRoundTripOntoSeriesZero()
    {
        // Already-working sibling: the default/common case (error bars on the first series, the only
        // layout the reader/writer supported before this fix) must not regress.
        var workbook = CreateThreeSeriesColumnWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.ShowErrorBars = true;
        chart.ErrorBarKind = ChartErrorBarKind.Percentage;
        chart.ErrorBarValue = 10;

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var seriesElements = chartXml.Descendants(ChartNs + "barChart").Single().Elements(ChartNs + "ser").ToList();
        seriesElements[0].Element(ChartNs + "errBars").Should().NotBeNull();
        seriesElements[1].Element(ChartNs + "errBars").Should().BeNull();
        seriesElements[2].Element(ChartNs + "errBars").Should().BeNull();

        var reloaded = ReloadSingleChart(saved);
        reloaded.ShowErrorBars.Should().BeTrue();
        reloaded.ErrorBarSeriesIndex.Should().Be(0);
    }

    [Fact]
    public void ComboChart_SecondaryAxisOwnTitleMinMaxFormat_RoundTripsIndependentlyOfPrimary()
    {
        var workbook = CreateColumnLineComboWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.YAxisTitle = "Revenue";
        chart.YAxisMinimum = 0;
        chart.YAxisMaximum = 100;
        chart.YAxisNumberFormatCode = "#,##0";
        chart.YAxisNumberFormatSourceLinked = false;
        chart.SecondaryAxisTitle = "Growth %";
        chart.SecondaryAxisMinimum = 0;
        chart.SecondaryAxisMaximum = 1;
        chart.SecondaryAxisNumberFormatCode = "0%";
        chart.SecondaryAxisNumberFormatSourceLinked = false;

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var valueAxes = chartXml.Descendants(ChartNs + "valAx").ToList();
        valueAxes.Should().HaveCount(2);
        var primaryAxis = valueAxes[0];
        var secondaryAxis = valueAxes[1];

        primaryAxis.Element(ChartNs + "numFmt")!.Attribute("formatCode")!.Value.Should().Be("#,##0");
        primaryAxis.Element(ChartNs + "scaling")!.Element(ChartNs + "max")!.Attribute("val")!.Value.Should().Be("100");

        secondaryAxis.Element(ChartNs + "numFmt")!.Attribute("formatCode")!.Value.Should().Be("0%");
        secondaryAxis.Element(ChartNs + "scaling")!.Element(ChartNs + "min")!.Attribute("val")!.Value.Should().Be("0");
        secondaryAxis.Element(ChartNs + "scaling")!.Element(ChartNs + "max")!.Attribute("val")!.Value.Should().Be("1");
        var secondaryTitleText = secondaryAxis.Element(ChartNs + "title")!
            .Descendants(XNamespace.Get("http://schemas.openxmlformats.org/drawingml/2006/main") + "t")
            .Select(element => element.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        secondaryTitleText.Should().Be("Growth %");

        var reloaded = ReloadSingleChart(saved);
        reloaded.YAxisTitle.Should().Be("Revenue");
        reloaded.YAxisMaximum.Should().Be(100);
        reloaded.YAxisNumberFormatCode.Should().Be("#,##0");
        reloaded.SecondaryAxisTitle.Should().Be("Growth %");
        reloaded.SecondaryAxisMinimum.Should().Be(0);
        reloaded.SecondaryAxisMaximum.Should().Be(1);
        reloaded.SecondaryAxisNumberFormatCode.Should().Be("0%");
    }

    [Fact]
    public void ComboChart_SecondaryAxisWithoutOwnSettings_StillClonesPrimaryAsBefore()
    {
        // Already-working sibling: a secondary axis with no explicit settings of its own (the only
        // shape this writer supported before the fix) must keep falling back to the primary axis's
        // min/max, not silently drop to defaults.
        var workbook = CreateColumnLineComboWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.YAxisMinimum = 0;
        chart.YAxisMaximum = 100;

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var valueAxes = chartXml.Descendants(ChartNs + "valAx").ToList();
        valueAxes.Should().HaveCount(2);
        var secondaryAxis = valueAxes[1];
        secondaryAxis.Element(ChartNs + "scaling")!.Element(ChartNs + "max")!.Attribute("val")!.Value.Should().Be("100");
        secondaryAxis.Element(ChartNs + "title").Should().BeNull();

        var reloaded = ReloadSingleChart(saved);
        reloaded.SecondaryAxisTitle.Should().BeNull();
    }

    private static Workbook CreateThreeSeriesColumnWorkbook()
    {
        var workbook = new Workbook("ChartSeriesCacheDeep");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("C"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"Item{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(row * 20));
            sheet.SetCell(new CellAddress(sheet.Id, row, 4), new NumberValue(row * 30));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true
        });

        return workbook;
    }

    private static Workbook CreateColumnLineComboWorkbook()
    {
        var workbook = new Workbook("ChartSecondaryAxisDeep");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Units"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Growth"));
        for (uint row = 2; row <= 5; row++)
        {
            var offset = row - 1;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"M{offset}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(offset * 100));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(70 + (offset * 8)));
            sheet.SetCell(new CellAddress(sheet.Id, row, 4), new NumberValue(0.15 + (offset * 0.02)));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            Title = "Sales, units, and growth",
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4)),
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [2],
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [1, 2]
        });

        return workbook;
    }

    private static ChartModel ReloadSingleChart(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        return new XlsxFileAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
    }

    private static XDocument LoadChartXml(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.Entries.Single(e => e.FullName == "xl/charts/chart1.xml");
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }
}
