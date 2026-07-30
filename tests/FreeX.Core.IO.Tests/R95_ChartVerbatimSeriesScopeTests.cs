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
/// R95-io-chart-series-verbatim-scope: <c>TryCollectVerbatimFormulas</c> used to trigger a
/// chart-level bool the moment ANY series had an unparsable tx/cat/val (or xVal/yVal/bubbleSize)
/// formula — a named range, a multi-area reference, or an external-workbook link — and then, once
/// triggered, built a <see cref="ChartSeriesVerbatimFormulas"/> record for EVERY series in the
/// chart, not just the one that was actually unparsable. On write,
/// <c>XlsxChartXmlWriter.Series.cs</c> treats a non-null verbatim record as "no cache, no
/// recomputed numeric category" for that series, so every OTHER (perfectly parseable) series in
/// the same chart silently lost its numCache/strCache and had its numeric/date category axis
/// downgraded from &lt;c:cat&gt;&lt;c:numRef&gt; to &lt;c:cat&gt;&lt;c:strRef&gt; on save, even
/// though its own formula never touched a named range/multi-area/external link.
/// <para>
/// Fixed by scoping <c>TryCollectVerbatimFormulas</c> per series: only a series whose own
/// formula(s) are unparsable gets an entry in the returned list, so <c>GetVerbatimFormulas</c>
/// correctly returns null for unaffected series and they keep going through the ordinary
/// positional/cached write path.
/// </para>
/// </summary>
public sealed class R95_ChartVerbatimSeriesScopeTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    private static XElement ParseSeries(string xml) => XElement.Parse(xml);

    // --- Reader-level: the actual fix (fail-before / pass-after) --------------------------------

    [Fact]
    public void R95_TryCollectVerbatimFormulas_OnlyFlagsSeriesWithOwnUnparsableFormula_NotChartWide()
    {
        var sheetId = new SheetId(Guid.NewGuid());

        // Series 0: an ordinary, fully-parseable value/category range — the overwhelmingly common
        // case (a plain worksheet column reference, no named range/multi-area/external link).
        var series0 = ParseSeries("""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:cat><c:numRef><c:f>Data!$A$2:$A$4</c:f></c:numRef></c:cat>
              <c:val><c:numRef><c:f>Data!$B$2:$B$4</c:f></c:numRef></c:val>
            </c:ser>
            """);

        // Series 1: bound to a defined name (dynamic-range chart) — genuinely unparsable as a
        // rectangular range, so it legitimately needs the verbatim bypass.
        var series1 = ParseSeries("""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="1"/>
              <c:order val="1"/>
              <c:cat><c:numRef><c:f>Data!$A$2:$A$4</c:f></c:numRef></c:cat>
              <c:val><c:numRef><c:f>rngDynamicSales</c:f></c:numRef></c:val>
            </c:ser>
            """);

        var result = XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas([series0, series1], sheetId);

        result.Should().NotBeNull(
            "series 1's named-range val formula must still engage the verbatim bypass for itself");
        result!.Should().HaveCount(1,
            "only the series whose OWN formula is unparsable should be captured verbatim — a sibling " +
            "series' fully-parseable formula must not be swept into the verbatim bypass just because " +
            "another series in the same chart has a named-range/multi-area/external-link formula");
        result.Should().NotContain(entry => entry.SeriesIndex == 0,
            "series 0's plain range formula was perfectly parseable and must not get a verbatim record " +
            "— doing so previously stripped its numCache and downgraded its numeric category to strRef " +
            "on save even though nothing about series 0 itself was unparsable");
        result[0].SeriesIndex.Should().Be(1);
        result[0].ValFormula.Should().Be("rngDynamicSales");
    }

    // Sibling no-regression: when EVERY series in the chart genuinely needs the verbatim bypass
    // (the legitimate dense case this mechanism exists for), all of them must still be captured —
    // the fix must not turn into "only ever capture the first unparsable series".
    [Fact]
    public void R95_TryCollectVerbatimFormulas_AllSeriesUnparsable_StillCapturesEveryOne()
    {
        var sheetId = new SheetId(Guid.NewGuid());

        var series0 = ParseSeries("""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:val><c:numRef><c:f>rngSalesA</c:f></c:numRef></c:val>
            </c:ser>
            """);
        var series1 = ParseSeries("""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="1"/>
              <c:order val="1"/>
              <c:val><c:numRef><c:f>rngSalesB</c:f></c:numRef></c:val>
            </c:ser>
            """);

        var result = XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas([series0, series1], sheetId);

        result.Should().NotBeNull();
        result!.Should().HaveCount(2,
            "when every series legitimately needs the verbatim bypass, all of them must still be captured");
        result.Select(e => e.SeriesIndex).Should().BeEquivalentTo([0, 1]);
        result.Single(e => e.SeriesIndex == 0).ValFormula.Should().Be("rngSalesA");
        result.Single(e => e.SeriesIndex == 1).ValFormula.Should().Be("rngSalesB");
    }

    // --- Writer-level: the visible symptom, exercised through the real Save entry point ---------

    // Confirms the full downstream effect through XlsxFileAdapter.Save: given the (now-correct)
    // sparse VerbatimSeriesFormulas shape the fixed reader produces — only the genuinely-unparsable
    // series has an entry — the OTHER series must keep its real numCache and its numeric category
    // axis as <c:cat><c:numRef>, not be silently downgraded.
    [Fact]
    public void R95_ColumnChart_SparseVerbatimFormulas_UnaffectedSeriesKeepsNumCacheAndNumericCategory()
    {
        var workbook = new Workbook("SparseVerbatim");
        var sheet = workbook.AddSheet("Data");

        // Numeric category column (e.g. years) — Excel emits <c:cat><c:numRef><c:numCache> for this.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2020));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2021));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(2022));

        // Series 0 (column B) — an ordinary series untouched by any unparsable formula elsewhere.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        // Series 1 (column C) — the one the reader flagged as needing the verbatim bypass.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(300));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 4, 3)),
            FirstColIsCategories = true,
            FirstRowIsHeader = false,
            SeriesColumnMappings =
            [
                new ChartSeriesColumnMapping(0, 2),
                new ChartSeriesColumnMapping(1, 3),
            ],
            // Mirrors the FIXED reader's output shape: only series 1 (bound to a named range in the
            // source file) gets a verbatim record — series 0 does not, unlike the pre-fix behavior
            // that would have populated an entry for series 0 too.
            VerbatimSeriesFormulas = [new ChartSeriesVerbatimFormulas(1, "rngDynamicSales", null, null)],
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);
        var seriesElements = chartDoc.Descendants(ChartNs + "ser").ToList();
        seriesElements.Should().HaveCount(2);

        var series0 = seriesElements.Single(s => s.Element(ChartNs + "idx")!.Attribute("val")!.Value == "0");
        var series1 = seriesElements.Single(s => s.Element(ChartNs + "idx")!.Attribute("val")!.Value == "1");

        // Series 0 (unaffected) must keep its real numCache — this is the concrete symptom the bug
        // caused: a sibling series' named-range formula silently stripped this series' cache too.
        series0.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!.Element(ChartNs + "numCache")
            .Should().NotBeNull("series 0's own value formula was fully parseable and must keep its numCache");

        var series0Cat = series0.Element(ChartNs + "cat")!;
        series0Cat.Element(ChartNs + "numRef").Should().NotBeNull(
            "the category column is numeric (years) and series 0's own category formula is parseable, " +
            "so it must stay <c:cat><c:numRef>, not be downgraded to <c:cat><c:strRef>");
        series0Cat.Element(ChartNs + "numRef")!.Element(ChartNs + "numCache").Should().NotBeNull();

        // Series 1 (verbatim) keeps its verbatim formula text and fabricates no cache for it —
        // unchanged from the pre-existing (already-correct) single-series verbatim behavior.
        var series1Val = series1.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!;
        series1Val.Element(ChartNs + "f")!.Value.Should().Be("rngDynamicSales");
        series1Val.Element(ChartNs + "numCache").Should().BeNull();
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
