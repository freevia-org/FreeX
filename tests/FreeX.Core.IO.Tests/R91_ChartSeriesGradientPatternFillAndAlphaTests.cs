using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 91 io-chart-series-format findings:
/// <list type="bullet">
///   <item>
///   R91-render-chart-series-format-5-1: a series' <c>&lt;a:gradFill&gt;</c>/<c>&lt;a:pattFill&gt;</c>
///   fill had no dedicated model representation, so <c>XlsxChartSeriesFormatReader.TryReadSeriesFill</c>
///   silently returned false and the entire <c>&lt;c:spPr&gt;</c> was dropped on the next FreeX save
///   (<c>XlsxChartXmlWriter.Series.cs</c> <c>ToSeriesShapeProperties</c> returns null with no format).
///   Fixed via a verbatim <c>ChartSeriesFormat.RawFillXml</c> passthrough that the writer re-emits
///   as-is. Picture fills (<c>&lt;a:blipFill&gt;</c>) are NOT covered — round-tripping those also
///   needs their embedded-image relationship/media re-plumbed through the chart part's own .rels on
///   write, which is out of scope for this passthrough (named for the orchestrator as a follow-up).
///   </item>
///   <item>
///   R91-render-chart-series-format-5-4: the <c>&lt;a:alpha&gt;</c> transparency child of a series
///   fill's color element was never parsed, so a semi-transparent fill came back fully opaque after
///   any FreeX save. Fixed via <c>ChartSeriesFormat.FillAlpha</c> (0..1 opacity fraction), read by
///   <c>XlsxDrawingColorReader.TryReadFillAlpha</c> and re-applied on write via
///   <c>XlsxDrawingColorAlpha.ApplyTo</c>. Scoped to the series-level fill (the concrete failure
///   scenario); the same <c>&lt;a:alpha&gt;</c> gap exists on every other <c>ToShapeProperties</c>
///   consumer (marker fills, legend, data labels, axis, up/down bars, trendlines, ...) and is named
///   here for the orchestrator rather than fixed, since threading alpha through that shared helper
///   touches ~15 call sites across the chart writer.
///   </item>
/// </list>
/// </summary>
public sealed class R91_ChartSeriesGradientPatternFillAndAlphaTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    // --- R91-render-chart-series-format-5-1 --------------------------------------------------

    [Fact]
    public void ColumnChart_SeriesGradientFill_SurvivesOpenThenSaveRoundTrip()
    {
        var workbook = new Workbook("GradientSeries");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 2; row <= 4; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row));

        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                      <c:spPr>
                        <a:gradFill rotWithShape="1">
                          <a:gsLst>
                            <a:gs pos="0"><a:srgbClr val="FF0000"/></a:gs>
                            <a:gs pos="100000"><a:srgbClr val="0000FF"/></a:gs>
                          </a:gsLst>
                          <a:lin ang="5400000" scaled="0"/>
                        </a:gradFill>
                      </c:spPr>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        // The real reader entry point: this is what runs when FreeX opens the .xlsx.
        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheet.Id, out var chart).Should().BeTrue();
        chart.SeriesFormats.Should().ContainSingle(f => f.SeriesIndex == 0 && f.RawFillXml != null && f.RawFillXml.Contains("gradFill"),
            "the gradient fill must be captured as a raw passthrough since it has no dedicated model representation");

        sheet.Charts.Add(chart);

        // The real writer entry point: any FreeX-triggered save regenerates the chart part
        // unconditionally whenever the workbook has a supported chart.
        var saved = SaveToBytes(workbook);
        var savedSeries = LoadChartXml(saved).Descendants(ChartNs + "ser").Single();
        var gradFill = savedSeries.Element(ChartNs + "spPr")?.Element(DrawingNs + "gradFill");
        gradFill.Should().NotBeNull("the authored gradient fill must survive a FreeX save instead of collapsing to the theme-palette default");
        gradFill!.Descendants(DrawingNs + "gs").Select(gs => gs.Attribute("pos")!.Value).Should().Equal("0", "100000");
        gradFill.Descendants(DrawingNs + "srgbClr").Select(c => c.Attribute("val")!.Value).Should().Equal("FF0000", "0000FF");
    }

    // Sibling no-regression: an ordinary solid-color series fill (the overwhelmingly common case)
    // must NOT be diverted into the raw passthrough path and must keep round-tripping through the
    // normal modeled FillColor field.
    [Fact]
    public void ColumnChart_SeriesSolidFill_StillRoundTripsThroughModeledColorNotRawPassthrough()
    {
        var workbook = new Workbook("SolidSeries");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 2; row <= 4; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row));

        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                      <c:spPr><a:solidFill><a:srgbClr val="4472C4"/></a:solidFill></c:spPr>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheet.Id, out var chart).Should().BeTrue();
        var format = chart.SeriesFormats.Single(f => f.SeriesIndex == 0);
        format.RawFillXml.Should().BeNull();
        format.FillColor.Should().Be(new CellColor(0x44, 0x72, 0xC4));

        sheet.Charts.Add(chart);
        var saved = SaveToBytes(workbook);
        var savedSeries = LoadChartXml(saved).Descendants(ChartNs + "ser").Single();
        savedSeries.Element(ChartNs + "spPr")?.Element(DrawingNs + "gradFill").Should().BeNull();
        savedSeries.Element(ChartNs + "spPr")?.Element(DrawingNs + "solidFill")?.Element(DrawingNs + "srgbClr")?.Attribute("val")?.Value
            .Should().Be("4472C4");
    }

    // --- R91-render-chart-series-format-5-4 --------------------------------------------------

    [Fact]
    public void ColumnChart_SeriesFillWithAlpha_SurvivesOpenThenSaveRoundTrip()
    {
        var workbook = new Workbook("AlphaSeries");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 2; row <= 4; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row));

        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                      <c:spPr><a:solidFill><a:srgbClr val="4472C4"><a:alpha val="50000"/></a:srgbClr></a:solidFill></c:spPr>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheet.Id, out var chart).Should().BeTrue();
        var format = chart.SeriesFormats.Single(f => f.SeriesIndex == 0);
        format.FillAlpha.Should().Be(0.5, "the 50% transparency slider must be parsed instead of silently ignored");
        format.FillColor.Should().Be(new CellColor(0x44, 0x72, 0xC4));

        sheet.Charts.Add(chart);
        var saved = SaveToBytes(workbook);
        var savedSeries = LoadChartXml(saved).Descendants(ChartNs + "ser").Single();
        var srgbClr = savedSeries.Element(ChartNs + "spPr")?.Element(DrawingNs + "solidFill")?.Element(DrawingNs + "srgbClr");
        srgbClr.Should().NotBeNull();
        srgbClr!.Attribute("val")!.Value.Should().Be("4472C4");
        srgbClr.Element(DrawingNs + "alpha")?.Attribute("val")?.Value.Should().Be("50000",
            "the 50% transparency must be re-emitted, not silently dropped, on the next FreeX save");
    }

    // Sibling no-regression: an ordinary fully-opaque fill (no authored <a:alpha>, the
    // overwhelmingly common case) must NOT gain a spurious <a:alpha> element on save.
    [Fact]
    public void ColumnChart_SeriesFillWithoutAlpha_WriterOmitsAlphaElement()
    {
        var workbook = new Workbook("OpaqueSeries");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 2; row <= 4; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row));

        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                      <c:spPr><a:solidFill><a:srgbClr val="4472C4"/></a:solidFill></c:spPr>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheet.Id, out var chart).Should().BeTrue();
        chart.SeriesFormats.Single(f => f.SeriesIndex == 0).FillAlpha.Should().BeNull();

        sheet.Charts.Add(chart);
        var saved = SaveToBytes(workbook);
        var savedSeries = LoadChartXml(saved).Descendants(ChartNs + "ser").Single();
        savedSeries.Element(ChartNs + "spPr")?.Element(DrawingNs + "solidFill")?.Element(DrawingNs + "srgbClr")?.Element(DrawingNs + "alpha")
            .Should().BeNull();
    }

    private static byte[] SaveToBytes(Workbook workbook)
    {
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        return stream.ToArray();
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
