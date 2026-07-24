using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R82-io-chart-series-5: three chart-series XLSX round-trip defects.
/// <list type="bullet">
///   <item>
///   R82-io-chart-series-5-1: &lt;c:ser&gt;'s &lt;c:order&gt; was always forced equal to &lt;c:idx&gt;
///   on write (XlsxChartXmlWriter.Series.cs), discarding a source file's independent display order
///   (Excel's Move Up/Down reordering, or a gap left by a deleted series). Fixed by capturing a
///   divergent order into <see cref="ChartModel.SeriesOrderOverrides"/> on read and re-emitting it
///   on write instead of always recomputing order == idx.
///   </item>
///   <item>
///   R82-io-chart-series-5-2: a grouped/multi-level category axis (&lt;c:multiLvlStrRef&gt;) was
///   never read, so it collapsed to a flat &lt;c:strRef&gt;/&lt;c:numRef&gt; on save. Fixed by
///   capturing the verbatim &lt;c:cat&gt; XML into <see cref="ChartModel.MultiLevelCategoryXml"/>
///   and re-emitting it unchanged.
///   </item>
///   <item>
///   R82-io-chart-series-5-3: a &lt;c:dPt&gt;'s &lt;c:marker&gt; child (Format Data Point &gt; Marker
///   Options) was never read (XlsxChartSeriesFormatReader.ApplyPiePointFills only looked at
///   dPt/spPr/solidFill), so a point whose only override was its marker silently disappeared on
///   save. Fixed by reading it into <see cref="ChartModel.PointMarkerFormats"/> and re-emitting it.
///   </item>
/// </list>
/// </summary>
public sealed class R82_ChartSeriesOrderMultiLvlCatDPtMarkerTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static XDocument ParseChartXml(string xml) => XDocument.Parse(xml);

    // ---------------------------------------------------------------------------------------
    // Finding 1: <c:order> divergent from <c:idx>
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TryReadSupportedChart_BarChart_DivergentOrder_IsCapturedOnSeriesOrderOverrides()
    {
        // Mirrors the finding's scenario: a 3-series chart where the user moved the last series to
        // the front via Move Up/Down. idx stays the stable identity; order is the new display order.
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="1"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$5</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$5</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="2"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$5</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$5</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:ser>
                      <c:idx val="2"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$5</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$D$2:$D$5</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.SeriesOrderOverrides.Should().BeEquivalentTo(new[]
        {
            new ChartSeriesOrderOverride(0, 1),
            new ChartSeriesOrderOverride(1, 2),
            new ChartSeriesOrderOverride(2, 0),
        });
    }

    [Fact]
    public void TryReadSupportedChart_BarChart_OrderEqualsIdx_LeavesSeriesOrderOverridesEmpty()
    {
        // Sibling no-regression case: the ordinary case (order == idx for every series) must not
        // spuriously populate SeriesOrderOverrides.
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$5</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$5</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$5</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$5</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.SeriesOrderOverrides.Should().BeEmpty();
    }

    [Fact]
    public void BarChart_DivergentSeriesOrder_SurvivesSaveAndReload()
    {
        var workbook = new Workbook("BarSeriesOrderWriteBack");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("B"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(row * 10));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            // Series 0 (idx 0) was moved to the end of the display order; series 1 keeps order == idx.
            SeriesOrderOverrides = [new ChartSeriesOrderOverride(0, 1)],
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var series = chartDoc.Descendants(ChartNs + "ser").ToList();
        series.Should().HaveCount(2);
        series[0].Element(ChartNs + "idx")!.Attribute("val")!.Value.Should().Be("0");
        series[0].Element(ChartNs + "order")!.Attribute("val")!.Value
            .Should().Be("1", "the divergent order must round-trip instead of being forced to == idx");
        series[1].Element(ChartNs + "idx")!.Attribute("val")!.Value.Should().Be("1");
        series[1].Element(ChartNs + "order")!.Attribute("val")!.Value.Should().Be("1");

        using var stream = new MemoryStream(saved, writable: false);
        var reloaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        reloaded.SeriesOrderOverrides.Should().ContainSingle(o => o.SeriesIndex == 0 && o.Order == 1);
    }

    [Fact]
    public void BarChart_NoOrderOverride_EmitsOrderEqualToIdx()
    {
        // Sibling no-regression case: without any override the writer must keep emitting
        // order == idx (the ordinary Excel case), not regress to some other default.
        var workbook = new Workbook("BarSeriesOrderDefault");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("A"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);
        var series = chartDoc.Descendants(ChartNs + "ser").Single();
        series.Element(ChartNs + "idx")!.Attribute("val")!.Value.Should().Be("0");
        series.Element(ChartNs + "order")!.Attribute("val")!.Value.Should().Be("0");

        using var stream = new MemoryStream(saved, writable: false);
        var reloaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        reloaded.SeriesOrderOverrides.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------
    // Finding 2: multi-level category axis (<c:multiLvlStrRef>)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TryReadSupportedChart_BarChart_MultiLevelCategoryAxis_IsCapturedVerbatim()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat>
                        <c:multiLvlStrRef>
                          <c:f>Sheet1!$A$2:$B$5</c:f>
                          <c:multiLvlStrCache>
                            <c:ptCount val="4"/>
                            <c:lvl>
                              <c:pt idx="0"><c:v>Chicago</c:v></c:pt>
                              <c:pt idx="1"><c:v>Denver</c:v></c:pt>
                            </c:lvl>
                            <c:lvl>
                              <c:pt idx="0"><c:v>East</c:v></c:pt>
                              <c:pt idx="1"><c:v>West</c:v></c:pt>
                            </c:lvl>
                          </c:multiLvlStrCache>
                        </c:multiLvlStrRef>
                      </c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$5</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        var entry = chart.MultiLevelCategoryXml.Should().ContainSingle(e => e.SeriesIndex == 0).Subject;
        var parsed = XElement.Parse(entry.RawXml);
        parsed.Name.LocalName.Should().Be("cat");
        var multiLvl = parsed.Descendants().Should()
            .ContainSingle(e => e.Name.LocalName == "multiLvlStrRef").Subject;
        multiLvl.Descendants().Count(e => e.Name.LocalName == "lvl").Should().Be(2);
    }

    [Fact]
    public void TryReadSupportedChart_BarChart_FlatCategoryAxis_LeavesMultiLevelCategoryXmlEmpty()
    {
        // Sibling no-regression case: an ordinary flat <c:strRef> category container must not
        // spuriously populate MultiLevelCategoryXml.
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$5</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$5</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.MultiLevelCategoryXml.Should().BeEmpty();
    }

    [Fact]
    public void BarChart_MultiLevelCategoryAxis_SurvivesSaveAndReload()
    {
        var workbook = new Workbook("BarMultiLvlCatWriteBack");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Val"));
        for (uint row = 2; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        const string rawCatXml = """
            <c:cat xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:multiLvlStrRef>
                <c:f>Sheet1!$A$2:$B$5</c:f>
                <c:multiLvlStrCache>
                  <c:ptCount val="4"/>
                  <c:lvl>
                    <c:pt idx="0"><c:v>Chicago</c:v></c:pt>
                    <c:pt idx="1"><c:v>Denver</c:v></c:pt>
                  </c:lvl>
                  <c:lvl>
                    <c:pt idx="0"><c:v>East</c:v></c:pt>
                    <c:pt idx="1"><c:v>West</c:v></c:pt>
                  </c:lvl>
                </c:multiLvlStrCache>
              </c:multiLvlStrRef>
            </c:cat>
            """;

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            MultiLevelCategoryXml = [new ChartSeriesRawXmlEntry(0, rawCatXml)],
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var series = chartDoc.Descendants(ChartNs + "ser").Single();
        var cat = series.Element(ChartNs + "cat")!;
        cat.Element(ChartNs + "strRef").Should().BeNull("the multi-level category must not be flattened to a strRef");
        cat.Element(ChartNs + "multiLvlStrRef").Should().NotBeNull();
        cat.Descendants(ChartNs + "lvl").Should().HaveCount(2);

        using var stream = new MemoryStream(saved, writable: false);
        var reloaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        var entry = reloaded.MultiLevelCategoryXml.Should().ContainSingle(e => e.SeriesIndex == 0).Subject;
        XElement.Parse(entry.RawXml).Descendants().Count(e => e.Name.LocalName == "lvl").Should().Be(2);
    }

    // ---------------------------------------------------------------------------------------
    // Finding 3: per-point dPt marker override
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TryReadSupportedChart_ScatterChart_DPtMarkerOnlyOverride_IsCapturedOnPointMarkerFormats()
    {
        // Uses a Scatter chart: its reader already wires ApplyPiePointFills (R44-io-chart-
        // datapoint-3-1), and ChartTypeSupport.SupportsSeriesMarkers includes Scatter, so this
        // exercises the fix in isolation without the standalone-Line-chart reader's separate,
        // pre-existing (out-of-scope) gap of never calling ApplyPiePointFills at all.
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:scatterChart>
                    <c:scatterStyle val="lineMarker"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:dPt>
                        <c:idx val="3"/>
                        <c:marker>
                          <c:symbol val="square"/>
                          <c:size val="10"/>
                          <c:spPr><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill></c:spPr>
                        </c:marker>
                      </c:dPt>
                      <c:xVal><c:numRef><c:f>Sheet1!$A$2:$A$6</c:f></c:numRef></c:xVal>
                      <c:yVal><c:numRef><c:f>Sheet1!$B$2:$B$6</c:f></c:numRef></c:yVal>
                    </c:ser>
                  </c:scatterChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        // No fill override was declared on the dPt itself (only a marker), so PointFillColors must
        // stay empty — the marker must not be silently dropped just because it has no fill sibling.
        chart.PointFillColors.Should().BeEmpty();
        var point = chart.PointMarkerFormats.Should()
            .ContainSingle(p => p.SeriesIndex == 0 && p.PointIndex == 3).Subject;
        point.MarkerStyle.Should().Be(ChartMarkerStyle.Square);
        point.MarkerSize.Should().Be(10);
        point.FillColor.Should().Be(new CellColor(0xFF, 0x00, 0x00));
    }

    [Fact]
    public void TryReadSupportedChart_ScatterChart_DPtFillOnlyOverride_LeavesPointMarkerFormatsEmpty()
    {
        // Sibling no-regression case: a dPt with only a fill (spPr) override and no marker child
        // must not spuriously populate PointMarkerFormats.
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:scatterChart>
                    <c:scatterStyle val="lineMarker"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:dPt>
                        <c:idx val="1"/>
                        <c:spPr><a:solidFill><a:srgbClr val="00FF00"/></a:solidFill></c:spPr>
                      </c:dPt>
                      <c:xVal><c:numRef><c:f>Sheet1!$A$2:$A$6</c:f></c:numRef></c:xVal>
                      <c:yVal><c:numRef><c:f>Sheet1!$B$2:$B$6</c:f></c:numRef></c:yVal>
                    </c:ser>
                  </c:scatterChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.PointFillColors.Should().ContainSingle(p => p.SeriesIndex == 0 && p.PointIndex == 1);
        chart.PointMarkerFormats.Should().BeEmpty();
    }

    [Fact]
    public void ScatterChart_PerPointMarkerOverride_SurvivesSaveAndReload()
    {
        var workbook = new Workbook("ScatterPointMarkerWriteBack");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Y"));
        for (uint row = 2; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Scatter,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            FirstRowIsHeader = true,
            // Mirrors Excel's "Format Data Point > Marker Options" override highlighting one point,
            // with no per-point fill override alongside it.
            PointMarkerFormats = [new ChartPointMarkerFormat(0, 2, ChartMarkerStyle.Square, MarkerSize: 10)],
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var series = chartDoc.Descendants(ChartNs + "ser").Single();
        var dPt = series.Elements(ChartNs + "dPt").Should()
            .ContainSingle("the per-point marker override must be written as a <c:dPt> element").Subject;
        dPt.Element(ChartNs + "idx")!.Attribute("val")!.Value.Should().Be("2");
        var marker = dPt.Element(ChartNs + "marker").Should().NotBeNull().And.Subject as XElement;
        marker!.Element(ChartNs + "symbol")!.Attribute("val")!.Value.Should().Be("square");
        marker.Element(ChartNs + "size")!.Attribute("val")!.Value.Should().Be("10");

        using var stream = new MemoryStream(saved, writable: false);
        var reloaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        var point = reloaded.PointMarkerFormats.Should()
            .ContainSingle(p => p.SeriesIndex == 0 && p.PointIndex == 2).Subject;
        point.MarkerStyle.Should().Be(ChartMarkerStyle.Square);
        point.MarkerSize.Should().Be(10);
    }

    [Fact]
    public void ScatterChart_NoPerPointMarkerOverride_EmitsNoDataPointElements()
    {
        // Sibling no-regression case: a series with no per-point marker override must not
        // spuriously gain a <c:dPt> element now that ToDataPointsXml also considers
        // PointMarkerFormats.
        var workbook = new Workbook("ScatterNoPointMarker");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("X"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Y"));
        for (uint row = 2; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Scatter,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 2)),
            FirstRowIsHeader = true,
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);
        var series = chartDoc.Descendants(ChartNs + "ser").Single();
        series.Elements(ChartNs + "dPt").Should().BeEmpty();

        using var stream = new MemoryStream(saved, writable: false);
        var reloaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        reloaded.PointMarkerFormats.Should().BeEmpty();
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
