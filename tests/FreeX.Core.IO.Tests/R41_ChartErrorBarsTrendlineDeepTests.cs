using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R41-io-chart-errorbars-trendline-3-1/2/3: (1) errValType="stdDev" (Standard Deviation, with a
/// user-configurable multiplier) was silently remapped to ChartErrorBarKind.StandardError (and its
/// &lt;c:val&gt; multiplier dropped) because the model enum had no StdDev member; (2) a series with
/// BOTH horizontal (X) and vertical (Y) error bars lost one of them, because the model only had one
/// scalar error-bar slot per chart and the reader used <c>Element()</c> (first match only); (3)
/// trendlines on a second (or later) series were silently and completely dropped, because the model
/// only had one chart-wide trendline slot (first-series-wins). Each test below pairs the fixed bug
/// scenario with an already-working sibling case to guard against over-correcting the common path.
/// </summary>
public sealed class R41_ChartErrorBarsTrendlineDeepTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    [Fact]
    public void ErrorBars_StdDevKind_RoundTripsAsStdDevNotStandardError()
    {
        var workbook = CreateThreeSeriesColumnWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.ShowErrorBars = true;
        chart.ErrorBarKind = ChartErrorBarKind.StdDev;
        chart.ErrorBarValue = 2;
        chart.SeriesErrorBars = [ChartSeriesErrorBars.FromChart(chart, 0)];

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var errorBars = chartXml.Descendants(ChartNs + "errBars").Single();
        errorBars.Element(ChartNs + "errValType")!.Attribute("val")!.Value.Should().Be("stdDev");
        errorBars.Element(ChartNs + "val")!.Attribute("val")!.Value.Should().Be("2");

        var reloaded = ReloadSingleChart(saved);
        reloaded.ErrorBarKind.Should().Be(ChartErrorBarKind.StdDev);
        reloaded.ErrorBarValue.Should().Be(2);
    }

    [Fact]
    public void ErrorBars_StandardErrorKind_StillRoundTripsWithoutValElement()
    {
        // Already-working sibling: the default/common case (Standard Error, which has no
        // user-configurable multiplier) must keep round-tripping without a <c:val> element.
        var workbook = CreateThreeSeriesColumnWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.ShowErrorBars = true;
        chart.ErrorBarKind = ChartErrorBarKind.StandardError;
        chart.SeriesErrorBars = [ChartSeriesErrorBars.FromChart(chart, 0)];

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var errorBars = chartXml.Descendants(ChartNs + "errBars").Single();
        errorBars.Element(ChartNs + "errValType")!.Attribute("val")!.Value.Should().Be("stdErr");
        errorBars.Element(ChartNs + "val").Should().BeNull();

        var reloaded = ReloadSingleChart(saved);
        reloaded.ErrorBarKind.Should().Be(ChartErrorBarKind.StandardError);
    }

    [Fact]
    public void ErrorBars_BothXAndYOnSameSeries_BothRoundTripInsteadOfLosingOne()
    {
        var workbook = CreateThreeSeriesColumnWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.ShowErrorBars = true;
        chart.ErrorBarSeriesIndex = 0;
        chart.ErrorBarAxisDirection = ChartErrorBarAxisDirection.X;
        chart.ErrorBarKind = ChartErrorBarKind.Percentage;
        chart.ErrorBarValue = 10;
        chart.SeriesErrorBars = [ChartSeriesErrorBars.FromChart(chart, 0)];
        // Simulate Excel's second sibling <c:errBars> (the Y direction) that the old single-slot
        // model/writer had no way to carry.
        chart.AdditionalSeriesErrorBarsXml.Add(new ChartSeriesRawXmlEntry(0,
            "<errBars xmlns=\"http://schemas.openxmlformats.org/drawingml/2006/chart\">" +
            "<errDir val=\"y\"/><errBarType val=\"both\"/><errValType val=\"fixedVal\"/><val val=\"5\"/>" +
            "</errBars>"));

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var seriesElements = chartXml.Descendants(ChartNs + "barChart").Single().Elements(ChartNs + "ser").ToList();
        var errBarsOnSeriesZero = seriesElements[0].Elements(ChartNs + "errBars").ToList();
        errBarsOnSeriesZero.Should().HaveCount(2);
        errBarsOnSeriesZero[0].Element(ChartNs + "errDir")!.Attribute("val")!.Value.Should().Be("x");
        errBarsOnSeriesZero[1].Element(ChartNs + "errDir")!.Attribute("val")!.Value.Should().Be("y");
        seriesElements[1].Elements(ChartNs + "errBars").Should().BeEmpty();

        var reloaded = ReloadSingleChart(saved);
        reloaded.ShowErrorBars.Should().BeTrue();
        reloaded.ErrorBarAxisDirection.Should().Be(ChartErrorBarAxisDirection.X);
        reloaded.AdditionalSeriesErrorBarsXml.Should().ContainSingle(entry => entry.SeriesIndex == 0);
        reloaded.AdditionalSeriesErrorBarsXml.Single().RawXml.Should().Contain("errDir val=\"y\"");
    }

    [Fact]
    public void ErrorBars_SingleDirectionOnly_StillRoundTripsWithNoPassthroughEntries()
    {
        // Already-working sibling: a series with only ONE set of error bars (the common case) must
        // not spuriously gain a passthrough entry.
        var workbook = CreateThreeSeriesColumnWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.ShowErrorBars = true;
        chart.ErrorBarKind = ChartErrorBarKind.Percentage;
        chart.ErrorBarValue = 10;
        chart.SeriesErrorBars = [ChartSeriesErrorBars.FromChart(chart, 0)];

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var seriesElements = chartXml.Descendants(ChartNs + "barChart").Single().Elements(ChartNs + "ser").ToList();
        seriesElements[0].Elements(ChartNs + "errBars").Should().ContainSingle();

        var reloaded = ReloadSingleChart(saved);
        reloaded.AdditionalSeriesErrorBarsXml.Should().BeEmpty();
    }

    [Fact]
    public void Trendline_OnSecondSeries_RoundTripsInsteadOfBeingDropped()
    {
        var workbook = CreateThreeSeriesColumnWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.ShowLinearTrendline = true;
        chart.TrendlineSeriesIndex = 0;
        chart.TrendlineType = ChartTrendlineType.Linear;
        // A different trendline on a DIFFERENT series: now a first-class per-series entry (it used to be
        // preserved as opaque raw XML that FreeX could neither render nor edit).
        chart.SeriesTrendlines =
        [
            ChartSeriesTrendline.FromChart(chart, 0),
            new ChartSeriesTrendline { SeriesIndex = 1, Type = ChartTrendlineType.Polynomial, Order = 3, ShowRSquared = true },
        ];

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var seriesElements = chartXml.Descendants(ChartNs + "barChart").Single().Elements(ChartNs + "ser").ToList();
        seriesElements[0].Element(ChartNs + "trendline").Should().NotBeNull();
        var secondTrendline = seriesElements[1].Element(ChartNs + "trendline");
        secondTrendline.Should().NotBeNull();
        secondTrendline!.Element(ChartNs + "trendlineType")!.Attribute("val")!.Value.Should().Be("poly");
        secondTrendline.Element(ChartNs + "order")!.Attribute("val")!.Value.Should().Be("3");
        seriesElements[2].Element(ChartNs + "trendline").Should().BeNull();

        var reloaded = ReloadSingleChart(saved);
        reloaded.ShowLinearTrendline.Should().BeTrue();
        reloaded.TrendlineSeriesIndex.Should().Be(0);
        reloaded.TrendlineType.Should().Be(ChartTrendlineType.Linear);
        reloaded.SeriesTrendlines.Select(t => t.SeriesIndex).Should().Equal(0, 1);
        var second = reloaded.SeriesTrendlines.Single(t => t.SeriesIndex == 1);
        second.Type.Should().Be(ChartTrendlineType.Polynomial);
        second.Order.Should().Be(3);
        second.ShowRSquared.Should().BeTrue();
        reloaded.AdditionalSeriesTrendlinesXml.Should().BeEmpty();
    }

    [Fact]
    public void Trendline_SecondTrendlineOnSameSeries_IsStillPreservedAsPassthrough()
    {
        // The model holds one trendline per series; Excel allows several on one series, so the extra one
        // keeps riding along verbatim.
        var workbook = CreateThreeSeriesColumnWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.ShowLinearTrendline = true;
        chart.SeriesTrendlines = [ChartSeriesTrendline.FromChart(chart, 0)];
        chart.AdditionalSeriesTrendlinesXml.Add(new ChartSeriesRawXmlEntry(0,
            "<trendline xmlns=\"http://schemas.openxmlformats.org/drawingml/2006/chart\">" +
            "<trendlineType val=\"poly\"/><order val=\"3\"/><dispRSqr val=\"1\"/>" +
            "</trendline>"));

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var seriesElements = LoadChartXml(saved).Descendants(ChartNs + "barChart").Single().Elements(ChartNs + "ser").ToList();
        seriesElements[0].Elements(ChartNs + "trendline").Should().HaveCount(2);

        var reloaded = ReloadSingleChart(saved);
        reloaded.SeriesTrendlines.Should().ContainSingle().Which.SeriesIndex.Should().Be(0);
        reloaded.AdditionalSeriesTrendlinesXml.Should().ContainSingle(entry => entry.SeriesIndex == 0);
        reloaded.AdditionalSeriesTrendlinesXml.Single().RawXml.Should().Contain("val=\"poly\"");
    }

    [Fact]
    public void Trendline_OnlyOnFirstSeries_StillRoundTripsWithNoPassthroughEntries()
    {
        // Already-working sibling: a chart with only ONE series' trendline (the common case) must
        // not spuriously gain a passthrough entry.
        var workbook = CreateThreeSeriesColumnWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.ShowLinearTrendline = true;
        chart.TrendlineSeriesIndex = 0;
        chart.TrendlineType = ChartTrendlineType.Linear;
        chart.SeriesTrendlines = [ChartSeriesTrendline.FromChart(chart, 0)];

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var seriesElements = chartXml.Descendants(ChartNs + "barChart").Single().Elements(ChartNs + "ser").ToList();
        seriesElements[0].Element(ChartNs + "trendline").Should().NotBeNull();
        seriesElements[1].Element(ChartNs + "trendline").Should().BeNull();

        var reloaded = ReloadSingleChart(saved);
        reloaded.ShowLinearTrendline.Should().BeTrue();
        reloaded.AdditionalSeriesTrendlinesXml.Should().BeEmpty();
    }

    [Fact]
    public void PerSeriesTrendlinesAndErrorBars_RoundTripWithEachSeriesOwnSettings()
    {
        // Excel stores these per series: series 0 and 2 carry different trendlines, only series 1 has
        // error bars, and every series keeps its own formatting through save and reload.
        var workbook = CreateThreeSeriesColumnWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.ShowLinearTrendline = true;
        chart.ShowErrorBars = true;
        chart.SeriesTrendlines =
        [
            new ChartSeriesTrendline { SeriesIndex = 0, Type = ChartTrendlineType.Linear, ShowEquation = true, Thickness = 3, DashStyle = ChartLineDashStyle.Solid, Color = new CellColor(255, 0, 0) },
            new ChartSeriesTrendline { SeriesIndex = 2, Type = ChartTrendlineType.Polynomial, Order = 4, ShowRSquared = true, Forward = 2, Name = "Poly fit" },
        ];
        chart.SeriesErrorBars =
        [
            new ChartSeriesErrorBars { SeriesIndex = 1, Kind = ChartErrorBarKind.Percentage, Value = 12, Direction = ChartErrorBarDirection.Plus, EndCaps = false },
        ];

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var seriesElements = LoadChartXml(saved).Descendants(ChartNs + "barChart").Single().Elements(ChartNs + "ser").ToList();

        seriesElements[0].Elements(ChartNs + "trendline").Should().ContainSingle();
        seriesElements[1].Elements(ChartNs + "trendline").Should().BeEmpty();
        seriesElements[2].Elements(ChartNs + "trendline").Should().ContainSingle();
        seriesElements[0].Elements(ChartNs + "errBars").Should().BeEmpty();
        seriesElements[1].Elements(ChartNs + "errBars").Should().ContainSingle();
        seriesElements[2].Elements(ChartNs + "errBars").Should().BeEmpty();

        var reloaded = ReloadSingleChart(saved);
        reloaded.SeriesTrendlines.Select(t => t.SeriesIndex).Should().Equal(0, 2);
        var first = reloaded.SeriesTrendlines[0];
        first.Type.Should().Be(ChartTrendlineType.Linear);
        first.ShowEquation.Should().BeTrue();
        first.Thickness.Should().Be(3);
        first.DashStyle.Should().Be(ChartLineDashStyle.Solid);
        first.Color.Should().Be(new CellColor(255, 0, 0));
        var third = reloaded.SeriesTrendlines[1];
        third.Type.Should().Be(ChartTrendlineType.Polynomial);
        third.Order.Should().Be(4);
        third.ShowRSquared.Should().BeTrue();
        third.Forward.Should().Be(2);
        third.Name.Should().Be("Poly fit");
        // Series 0 has no unmodeled leftovers: the chart-wide members mirror the first trendline.
        reloaded.TrendlineSeriesIndex.Should().Be(0);
        reloaded.AdditionalSeriesTrendlinesXml.Should().BeEmpty();

        var bars = reloaded.SeriesErrorBars.Should().ContainSingle().Subject;
        bars.SeriesIndex.Should().Be(1);
        bars.Kind.Should().Be(ChartErrorBarKind.Percentage);
        bars.Value.Should().Be(12);
        bars.Direction.Should().Be(ChartErrorBarDirection.Plus);
        bars.EndCaps.Should().BeFalse();
        reloaded.AdditionalSeriesErrorBarsXml.Should().BeEmpty();
    }

    [Fact]
    public void ChartWideTrendlineAndErrorBars_StillMeanEverySeries_WhenWritten()
    {
        // Old files / legacy commands only carry the chart-wide flags: they keep applying to all series.
        var workbook = CreateThreeSeriesColumnWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.ShowLinearTrendline = true;
        chart.ShowErrorBars = true;

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var seriesElements = LoadChartXml(saved).Descendants(ChartNs + "barChart").Single().Elements(ChartNs + "ser").ToList();

        seriesElements.Should().OnlyContain(ser => ser.Elements(ChartNs + "trendline").Count() == 1);
        seriesElements.Should().OnlyContain(ser => ser.Elements(ChartNs + "errBars").Count() == 1);

        var reloaded = ReloadSingleChart(saved);
        reloaded.SeriesTrendlines.Should().HaveCount(3);
        reloaded.SeriesErrorBars.Should().HaveCount(3);
    }

    private static Workbook CreateThreeSeriesColumnWorkbook()
    {
        var workbook = new Workbook("ChartErrorBarsTrendlineDeep");
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
