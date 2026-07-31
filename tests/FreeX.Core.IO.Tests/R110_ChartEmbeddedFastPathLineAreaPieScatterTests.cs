using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R110-io-chart-series-embedded-line/area/pie/scatter: Round 108 fixed the "all val/cat formulas
/// are named ranges" fast-path silently dropping a Bar/Column chart's series
/// (<see cref="R108_ChartEmbeddedFastPathSeriesLossTests"/>), but only in
/// <c>XlsxChartPartReader.Bar.cs</c>. Every other chart-reading function shares the exact same
/// "build ranges from tx/cat/val (or tx/xVal/yVal) formulas, then bail if ranges.Count==0" shape —
/// <c>TryReadLineChart</c>/<c>TryReadLineLikeChart</c> (Line.cs), <c>TryReadAreaChart</c>/
/// <c>TryReadAreaLineComboChart</c> (Area.cs), <c>TryReadPieFamilyChart</c>/
/// <c>TryReadBubbleChart</c> (PieBubble.cs), and <c>TryReadScatterChart</c> (Scatter.cs) — and none
/// of them had the embedded numCache/strCache fallback that Bar.cs got. A Line/Area/Pie/Doughnut/
/// Bubble/Scatter/Radar/Stock/3D chart whose series reference an OFFSET-based dynamic named range
/// (Excel's classic "auto-expanding chart" pattern) or an Excel-Table-driven range with no directly
/// parseable cell address silently vanished from the workbook on load.
/// </summary>
public sealed class R110_ChartEmbeddedFastPathLineAreaPieScatterTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    // ---------------------------------------------------------------------------------------------
    // Full read -> write round trip through the REAL product entry points (XlsxFileAdapter.Load /
    // .Save), mirroring R108_ChartEmbeddedFastPathSeriesLossTests exactly but for a Line chart —
    // this is the fail-before / pass-after evidence for the whole fix.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void LineChart_NamedRangeCatAndValFormulas_SeriesSurvivesLoadAndResave()
    {
        var workbook = new Workbook("NamedRangeLineSeries");
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
            Type = ChartType.Line,
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
        // Also strip <c:tx> down to a literal string (no <c:f> at all) — the "default/common case
        // for a chart that doesn't need a custom title" called out in the defect. With cat AND val
        // ALSO named ranges, this makes `ranges` genuinely empty pre-fix, so TryReadLineChart's
        // `if (ranges.Count == 0) return false` fires and the WHOLE chart vanishes from
        // sheet.Charts on load — the strongest form of the bug, not just a degraded DataRange.
        customized = StripSeriesTxToLiteral(customized, seriesIdx: "0", literalName: "Frequency");

        // --- Real Load entry point ------------------------------------------------------------
        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Data");

        // THE BUG: pre-fix, TryReadLineChart returned false when ranges.Count == 0 (tx is a literal
        // with no formula, and both cat and val formulas are named ranges, so TryParseFormulaRange
        // never succeeds for any of them), so the chart was never added to sheet.Charts at all.
        var reloadedChart = reloadedSheet.Charts.Should().ContainSingle(
            "a named-range-sourced Line chart must survive load, exactly like Bar/Column already does"
        ).Subject;

        reloadedChart.Type.Should().Be(ChartType.Line);
        reloadedChart.EmbeddedSeriesData.Should().NotBeNull().And.HaveCount(1);
        reloadedChart.VerbatimSeriesFormulas.Should().NotBeNull(
            "every series formula here is a named range, so the verbatim bypass must engage " +
            "even though the embedded-data fast path also fires");
        var verbatim = reloadedChart.VerbatimSeriesFormulas!.Should().ContainSingle(v => v.SeriesIndex == 0).Subject;
        verbatim.ValFormula.Should().Be("'Data'!rngCount");
        verbatim.CatFormula.Should().Be("'Data'!rngGroups");

        // --- Real Save entry point -------------------------------------------------------------
        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
        var seriesElements = chartDoc.Descendants(ChartNs + "ser").ToList();

        seriesElements.Should().HaveCount(1,
            "a named-range-sourced Line series must survive the save, matching real Excel");

        var series0 = seriesElements.Single();
        var val = series0.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!;
        val.Element(ChartNs + "f")!.Value.Should().Be("'Data'!rngCount");
        var cat = series0.Element(ChartNs + "cat")!.Element(ChartNs + "strRef")!;
        cat.Element(ChartNs + "f")!.Value.Should().Be("'Data'!rngGroups");
    }

    // Sibling no-regression: an ordinary Line chart with direct cell-range series (the overwhelming
    // common case) must be completely unaffected by the new embedded-data fallback branch.
    [Fact]
    public void LineChart_OrdinaryCellRangeSeries_UnaffectedByEmbeddedFallback()
    {
        var workbook = new Workbook("OrdinaryLineSeries");
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
            Type = ChartType.Line,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            SeriesColumnMappings = [new ChartSeriesColumnMapping(0, 2)],
        });

        var saved = SaveToBytes(workbook);
        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(saved, writable: false));
        var reloadedChart = reloadedWorkbook.Sheets.Single(s => s.Name == "Data").Charts.Should().ContainSingle().Subject;

        reloadedChart.EmbeddedSeriesData.Should().BeNull("no series formula is a named range or cross-sheet reference");
        reloadedChart.VerbatimSeriesFormulas.Should().BeNull();

        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
        chartDoc.Descendants(ChartNs + "ser").Should().ContainSingle();
    }

    // ---------------------------------------------------------------------------------------------
    // Breadth coverage for the other chart families named in the defect, via the same
    // TryReadSupportedChart seam XlsxChartPartReaderTests.BarMetadata's pre-existing Bar-only
    // regression test used (real parser entry point, hand-authored chart XML input).
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void AreaChart_NamedRangeCatAndValFormulas_PopulatesEmbeddedSeriesData()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:areaChart>
                    <c:grouping val="standard"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat>
                        <c:strRef>
                          <c:f>'Sheet1'!rngGroups</c:f>
                          <c:strCache>
                            <c:ptCount val="2"/>
                            <c:pt idx="0"><c:v>Q1</c:v></c:pt>
                            <c:pt idx="1"><c:v>Q2</c:v></c:pt>
                          </c:strCache>
                        </c:strRef>
                      </c:cat>
                      <c:val>
                        <c:numRef>
                          <c:f>'Sheet1'!rngCount</c:f>
                          <c:numCache>
                            <c:formatCode>General</c:formatCode>
                            <c:ptCount val="2"/>
                            <c:pt idx="0"><c:v>10</c:v></c:pt>
                            <c:pt idx="1"><c:v>20</c:v></c:pt>
                          </c:numCache>
                        </c:numRef>
                      </c:val>
                    </c:ser>
                  </c:areaChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue("an Area chart must load even when formulas reference named ranges — THE BUG dropped it entirely");

        chart.Type.Should().Be(ChartType.Area);
        chart.EmbeddedSeriesData.Should().NotBeNull().And.HaveCount(1);
        var series0 = chart.EmbeddedSeriesData![0];
        series0.Categories.Should().Equal("Q1", "Q2");
        series0.Values.Should().Equal(10.0, 20.0);
    }

    [Fact]
    public void PieChart_NamedRangeCatAndValFormulas_PopulatesEmbeddedSeriesData()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:pieChart>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat>
                        <c:strRef>
                          <c:f>'Sheet1'!rngGroups</c:f>
                          <c:strCache>
                            <c:ptCount val="2"/>
                            <c:pt idx="0"><c:v>Red</c:v></c:pt>
                            <c:pt idx="1"><c:v>Blue</c:v></c:pt>
                          </c:strCache>
                        </c:strRef>
                      </c:cat>
                      <c:val>
                        <c:numRef>
                          <c:f>'Sheet1'!rngCount</c:f>
                          <c:numCache>
                            <c:formatCode>General</c:formatCode>
                            <c:ptCount val="2"/>
                            <c:pt idx="0"><c:v>7</c:v></c:pt>
                            <c:pt idx="1"><c:v>3</c:v></c:pt>
                          </c:numCache>
                        </c:numRef>
                      </c:val>
                    </c:ser>
                  </c:pieChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue("a Pie chart must load even when formulas reference named ranges — THE BUG dropped it entirely");

        chart.Type.Should().Be(ChartType.Pie);
        chart.EmbeddedSeriesData.Should().NotBeNull().And.HaveCount(1);
        var series0 = chart.EmbeddedSeriesData![0];
        series0.Categories.Should().Equal("Red", "Blue");
        series0.Values.Should().Equal(7.0, 3.0);
    }

    [Fact]
    public void ScatterChart_NamedRangeXValYValFormulas_PopulatesEmbeddedSeriesData()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:scatterChart>
                    <c:scatterStyle val="lineMarker"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:xVal>
                        <c:numRef>
                          <c:f>'Sheet1'!rngX</c:f>
                          <c:numCache>
                            <c:formatCode>General</c:formatCode>
                            <c:ptCount val="2"/>
                            <c:pt idx="0"><c:v>1</c:v></c:pt>
                            <c:pt idx="1"><c:v>2</c:v></c:pt>
                          </c:numCache>
                        </c:numRef>
                      </c:xVal>
                      <c:yVal>
                        <c:numRef>
                          <c:f>'Sheet1'!rngY</c:f>
                          <c:numCache>
                            <c:formatCode>General</c:formatCode>
                            <c:ptCount val="2"/>
                            <c:pt idx="0"><c:v>5</c:v></c:pt>
                            <c:pt idx="1"><c:v>9</c:v></c:pt>
                          </c:numCache>
                        </c:numRef>
                      </c:yVal>
                    </c:ser>
                  </c:scatterChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue("a Scatter chart must load even when xVal/yVal formulas reference named ranges — THE BUG dropped it entirely");

        chart.Type.Should().Be(ChartType.Scatter);
        chart.EmbeddedSeriesData.Should().NotBeNull().And.HaveCount(1);
        var series0 = chart.EmbeddedSeriesData![0];
        series0.Values.Should().Equal(5.0, 9.0);
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
    /// Rewrites the given series' &lt;c:cat&gt;&lt;c:strRef&gt; and &lt;c:val&gt;&lt;c:numRef&gt; in
    /// xl/charts/chart1.xml to point at named-range formulas with real strCache/numCache values —
    /// mimicking what real Excel writes for a chart bound to OFFSET-based dynamic named ranges.
    /// Chart-type-agnostic: matches by series &lt;c:idx&gt; regardless of the enclosing plot element
    /// (works for lineChart/areaChart/barChart alike).
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
