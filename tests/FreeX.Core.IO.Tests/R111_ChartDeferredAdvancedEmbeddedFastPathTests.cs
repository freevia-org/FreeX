using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R111-io-chart-deferred-named-range-1: r110 added the embedded numCache/strCache fallback for
/// named-range chart series to seven reader functions (Bar, Line/LineLike, Area, AreaLineCombo,
/// PieFamily, Bubble, Scatter — see <see cref="R110_ChartEmbeddedFastPathLineAreaPieScatterTests"/>)
/// but missed the eighth: <c>TryReadDeferredAdvancedChart</c> in
/// <c>XlsxChartPartReader.Deferred.cs</c>, which serves every chartEx-family chart type (Waterfall,
/// Histogram, Box &amp; Whisker, Treemap, Sunburst, Funnel, Pareto, Surface/3D-Surface — see
/// <c>FindDeferredAdvancedChart</c>) AND the ordinary bar3DChart (3D-Column/3D-Bar, via
/// <c>TryReadThreeDBarChart</c>, which always passes <c>fallbackDataRange:null</c>). When every
/// series' val/cat (classic &lt;c:ser&gt;) or numDim/strDim (true chartEx &lt;cx:series&gt;) formula
/// is a defined name that <c>TryParseFormulaRange</c> cannot resolve — the OFFSET-based
/// "auto-expanding chart" pattern — this reader used to drop the whole chart object unconditionally.
/// </summary>
public sealed class R111_ChartDeferredAdvancedEmbeddedFastPathTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    // ---------------------------------------------------------------------------------------------
    // Direct reader-seam evidence (real parser entry point TryReadSupportedChart, hand-authored
    // chart XML input) — mirrors XlsxChartPartReaderTests.AdvancedCharts.cs's existing coverage of
    // this same chart-element family.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void WaterfallChart_NamedRangeCatAndValFormulas_PopulatesEmbeddedSeriesData()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:waterfallChart>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat>
                        <c:strRef>
                          <c:f>'Sheet1'!rngMonths</c:f>
                          <c:strCache>
                            <c:ptCount val="2"/>
                            <c:pt idx="0"><c:v>Jan</c:v></c:pt>
                            <c:pt idx="1"><c:v>Feb</c:v></c:pt>
                          </c:strCache>
                        </c:strRef>
                      </c:cat>
                      <c:val>
                        <c:numRef>
                          <c:f>'Sheet1'!rngDelta</c:f>
                          <c:numCache>
                            <c:formatCode>General</c:formatCode>
                            <c:ptCount val="2"/>
                            <c:pt idx="0"><c:v>100</c:v></c:pt>
                            <c:pt idx="1"><c:v>-40</c:v></c:pt>
                          </c:numCache>
                        </c:numRef>
                      </c:val>
                    </c:ser>
                  </c:waterfallChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue("a Waterfall chart must load even when its series formulas reference named ranges — THE BUG dropped it entirely");

        chart.Type.Should().Be(ChartType.Waterfall);
        chart.EmbeddedSeriesData.Should().NotBeNull().And.HaveCount(1);
        var series0 = chart.EmbeddedSeriesData![0];
        series0.Categories.Should().Equal("Jan", "Feb");
        series0.Values.Should().Equal(100.0, -40.0);
    }

    [Theory]
    [InlineData("histogramChart", ChartType.Histogram)]
    [InlineData("boxWhiskerChart", ChartType.BoxAndWhisker)]
    [InlineData("funnelChart", ChartType.Funnel)]
    [InlineData("treemapChart", ChartType.Treemap)]
    [InlineData("sunburstChart", ChartType.Sunburst)]
    public void AdvancedChartFamily_NamedRangeFormulas_PopulatesEmbeddedSeriesData(string chartElementName, ChartType expectedType)
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml($$"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:{{chartElementName}}>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat>
                        <c:strRef>
                          <c:f>'Sheet1'!rngLabels</c:f>
                          <c:strCache>
                            <c:ptCount val="2"/>
                            <c:pt idx="0"><c:v>A</c:v></c:pt>
                            <c:pt idx="1"><c:v>B</c:v></c:pt>
                          </c:strCache>
                        </c:strRef>
                      </c:cat>
                      <c:val>
                        <c:numRef>
                          <c:f>'Sheet1'!rngValues</c:f>
                          <c:numCache>
                            <c:formatCode>General</c:formatCode>
                            <c:ptCount val="2"/>
                            <c:pt idx="0"><c:v>5</c:v></c:pt>
                            <c:pt idx="1"><c:v>15</c:v></c:pt>
                          </c:numCache>
                        </c:numRef>
                      </c:val>
                    </c:ser>
                  </c:{{chartElementName}}>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue($"a {expectedType} chart must load even when its series formulas reference named ranges — THE BUG dropped it entirely");

        chart.Type.Should().Be(expectedType);
        chart.EmbeddedSeriesData.Should().NotBeNull().And.HaveCount(1);
        var series0 = chart.EmbeddedSeriesData![0];
        series0.Categories.Should().Equal("A", "B");
        series0.Values.Should().Equal(5.0, 15.0);
    }

    // ---------------------------------------------------------------------------------------------
    // True chartEx <cx:series>/<cx:dataId>/<cx:data> shape (dataId + numDim/strDim with a real
    // cx:lvl/cx:pt cache) — the OTHER series shape TryReadDeferredAdvancedChart serves, distinct
    // from the classic <c:ser> shape above. Exercises the new TryReadChartExEmbeddedSeriesData path.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ChartExSeriesShape_NamedRangeNumDimStrDimFormulas_PopulatesEmbeddedSeriesData()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <cx:chartSpace xmlns:cx="http://schemas.microsoft.com/office/drawing/2014/chartex"
                           xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <cx:chartData>
                <cx:data id="0">
                  <cx:strDim type="cat">
                    <cx:f>'Sheet1'!rngCategories</cx:f>
                    <cx:lvl ptCount="2">
                      <cx:pt idx="0">North</cx:pt>
                      <cx:pt idx="1">South</cx:pt>
                    </cx:lvl>
                  </cx:strDim>
                  <cx:numDim type="val">
                    <cx:f>'Sheet1'!rngValues</cx:f>
                    <cx:lvl ptCount="2">
                      <cx:pt idx="0">12</cx:pt>
                      <cx:pt idx="1">27</cx:pt>
                    </cx:lvl>
                  </cx:numDim>
                </cx:data>
              </cx:chartData>
              <cx:chart>
                <cx:plotArea>
                  <cx:plotAreaRegion>
                    <cx:series layoutId="treemap">
                      <cx:tx>
                        <cx:txData>
                          <cx:f>'Sheet1'!rngSeriesName</cx:f>
                          <cx:v>Region</cx:v>
                        </cx:txData>
                      </cx:tx>
                      <cx:dataId val="0"/>
                    </cx:series>
                  </cx:plotAreaRegion>
                </cx:plotArea>
              </cx:chart>
            </cx:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue("a true chartEx Treemap chart must load even when its numDim/strDim formulas reference named ranges — THE BUG dropped it entirely");

        chart.Type.Should().Be(ChartType.Treemap);
        chart.EmbeddedSeriesData.Should().NotBeNull().And.HaveCount(1);
        var series0 = chart.EmbeddedSeriesData![0];
        series0.SeriesName.Should().Be("Region");
        series0.Categories.Should().Equal("North", "South");
        series0.Values.Should().Equal(12.0, 27.0);
    }

    // ---------------------------------------------------------------------------------------------
    // Full read -> write round trip through the REAL product entry points (XlsxFileAdapter.Load /
    // .Save) for the ordinary bar3DChart (3D-Column) — TryReadThreeDBarChart always passes
    // fallbackDataRange:null, so this chart type had NO fallback at all pre-fix, not even the narrow
    // "_xlchart." one. This is the strongest fail-before / pass-after evidence for that half of the
    // defect, matching R110_ChartEmbeddedFastPathLineAreaPieScatterTests' Line-chart round trip.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ThreeDColumnChart_NamedRangeCatAndValFormulas_SeriesSurvivesLoadAndResave()
    {
        var workbook = new Workbook("NamedRange3DColumnSeries");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Group"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Frequency"));
        for (uint row = 2; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"G{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue((row - 1) * 32));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.ThreeDColumn,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            SeriesColumnMappings = [new ChartSeriesColumnMapping(0, 2)],
        });

        var saved = SaveToBytes(workbook);
        var customized = RewriteSeriesCatAndValToNamedRanges(
            saved,
            seriesIdx: "0",
            catFormula: "'Data'!rngGroups",
            catPoints: [("0", "G2"), ("1", "G3")],
            valFormula: "'Data'!rngCount",
            valPoints: [("0", "64"), ("1", "36")]);
        customized = StripSeriesTxToLiteral(customized, seriesIdx: "0", literalName: "Frequency");

        // --- Real Load entry point ------------------------------------------------------------
        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Data");

        // THE BUG: pre-fix, TryReadDeferredAdvancedChart returned false when ranges.Count == 0 for
        // bar3DChart (tx is a literal with no formula, and both cat and val formulas are named
        // ranges, so TryParseFormulaRange never succeeds for any of them, and TryReadThreeDBarChart
        // always passes fallbackDataRange:null so even the narrow "_xlchart." fallback could not
        // engage), so the chart was never added to sheet.Charts at all.
        var reloadedChart = reloadedSheet.Charts.Should().ContainSingle(
            "a named-range-sourced 3D-Column chart must survive load, exactly like Bar/Column already does"
        ).Subject;

        reloadedChart.Type.Should().Be(ChartType.ThreeDColumn);
        reloadedChart.EmbeddedSeriesData.Should().NotBeNull().And.HaveCount(1);
        var series0 = reloadedChart.EmbeddedSeriesData![0];
        series0.Categories.Should().Equal("G2", "G3");
        series0.Values.Should().Equal(64.0, 36.0);

        // --- Real Save entry point -------------------------------------------------------------
        // Round-tripping through save must not throw and must still describe a bar3DChart — full
        // save-side named-range formula preservation for this family is a separate, deeper concern
        // (VerbatimSeriesFormulas plumbing) tracked as a sibling lead; this assertion only proves
        // the chart itself is no longer silently dropped end to end.
        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
        chartDoc.Descendants(ChartNs + "bar3DChart").Should().ContainSingle(
            "the chart object must still be present after a save round trip");
    }

    // Sibling no-regression: an ordinary 3D-Column chart with direct cell-range series (the
    // overwhelming common case) must be completely unaffected by the new embedded-data fallback.
    [Fact]
    public void ThreeDColumnChart_OrdinaryCellRangeSeries_UnaffectedByEmbeddedFallback()
    {
        var workbook = new Workbook("Ordinary3DColumnSeries");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Series A"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue((row - 1) * 10));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.ThreeDColumn,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            SeriesColumnMappings = [new ChartSeriesColumnMapping(0, 2)],
        });

        var saved = SaveToBytes(workbook);
        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(saved, writable: false));
        var reloadedChart = reloadedWorkbook.Sheets.Single(s => s.Name == "Data").Charts.Should().ContainSingle().Subject;

        reloadedChart.Type.Should().Be(ChartType.ThreeDColumn);
        reloadedChart.EmbeddedSeriesData.Should().BeNull("no series formula is a named range or cross-sheet reference");

        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
        chartDoc.Descendants(ChartNs + "bar3DChart").Should().ContainSingle();
    }

    // Sibling no-regression: the classic waterfallChart element with direct (non-named-range)
    // cat/val formulas must still resolve via the ordinary ranges.Count > 0 path, not the new
    // embedded-data fallback, and its DataRange must still be the real parsed range.
    [Fact]
    public void WaterfallChart_OrdinaryCellRangeSeries_UnaffectedByEmbeddedFallback()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:waterfallChart>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:waterfallChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Waterfall);
        chart.EmbeddedSeriesData.Should().BeNull("no series formula is a named range");
        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2)));
    }

    private static XDocument ParseChartXml(string xml) => XDocument.Parse(xml);

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

    /// <summary>
    /// Rewrites the given series' &lt;c:tx&gt; to a plain literal string (&lt;c:v&gt;, no
    /// &lt;c:f&gt;/&lt;c:strRef&gt; at all) — mimicking a chart that was never given a custom
    /// series-name cell reference, the common case real Excel emits for most auto-expanding charts.
    /// </summary>
    private static byte[] StripSeriesTxToLiteral(byte[] package, string seriesIdx, string literalName)
    {
        using var stream = new MemoryStream();
        stream.Write(package, 0, package.Length);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.Entries.Single(e => e.FullName == "xl/charts/chart1.xml");
            XDocument chartDoc;
            using (var entryStream = entry.Open())
                chartDoc = XDocument.Load(entryStream);

            var series = chartDoc.Descendants(ChartNs + "ser")
                .Single(s => s.Element(ChartNs + "idx")!.Attribute("val")!.Value == seriesIdx);

            var tx = series.Element(ChartNs + "tx");
            tx.Should().NotBeNull("the fixture chart must already emit <c:ser><c:tx> to rewrite");
            tx!.RemoveNodes();
            tx.Add(new XElement(ChartNs + "v", literalName));

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/charts/chart1.xml", CompressionLevel.Optimal);
            using var newEntryStream = newEntry.Open();
            chartDoc.Save(newEntryStream);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Rewrites the given series' &lt;c:cat&gt;&lt;c:strRef&gt; and &lt;c:val&gt;&lt;c:numRef&gt; in
    /// xl/charts/chart1.xml to point at named-range formulas with real strCache/numCache values —
    /// mimicking what real Excel writes for a chart bound to OFFSET-based dynamic named ranges.
    /// </summary>
    private static byte[] RewriteSeriesCatAndValToNamedRanges(
        byte[] package,
        string seriesIdx,
        string catFormula,
        (string Idx, string Value)[] catPoints,
        string valFormula,
        (string Idx, string Value)[] valPoints)
    {
        using var stream = new MemoryStream();
        stream.Write(package, 0, package.Length);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.Entries.Single(e => e.FullName == "xl/charts/chart1.xml");
            XDocument chartDoc;
            using (var entryStream = entry.Open())
                chartDoc = XDocument.Load(entryStream);

            var series = chartDoc.Descendants(ChartNs + "ser")
                .Single(s => s.Element(ChartNs + "idx")!.Attribute("val")!.Value == seriesIdx);

            var strRef = series.Element(ChartNs + "cat")!.Element(ChartNs + "strRef");
            strRef.Should().NotBeNull("the fixture chart must already emit <c:cat><c:strRef> to rewrite");
            strRef!.RemoveNodes();
            strRef.Add(new XElement(ChartNs + "f", catFormula));
            var strCache = new XElement(ChartNs + "strCache",
                new XElement(ChartNs + "ptCount", new XAttribute("val", catPoints.Length)));
            foreach (var (idx, value) in catPoints)
                strCache.Add(new XElement(ChartNs + "pt", new XAttribute("idx", idx), new XElement(ChartNs + "v", value)));
            strRef.Add(strCache);

            var numRef = series.Element(ChartNs + "val")!.Element(ChartNs + "numRef");
            numRef.Should().NotBeNull("the fixture chart must already emit <c:val><c:numRef> to rewrite");
            numRef!.RemoveNodes();
            numRef.Add(new XElement(ChartNs + "f", valFormula));
            var numCache = new XElement(ChartNs + "numCache",
                new XElement(ChartNs + "formatCode", "General"),
                new XElement(ChartNs + "ptCount", new XAttribute("val", valPoints.Length)));
            foreach (var (idx, value) in valPoints)
                numCache.Add(new XElement(ChartNs + "pt", new XAttribute("idx", idx), new XElement(ChartNs + "v", value)));
            numRef.Add(numCache);

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/charts/chart1.xml", CompressionLevel.Optimal);
            using var newEntryStream = newEntry.Open();
            chartDoc.Save(newEntryStream);
        }

        return stream.ToArray();
    }
}
