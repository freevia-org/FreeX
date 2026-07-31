using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R103-io-chart-series-verbatim-cache: when a series' val/cat/xVal/yVal/bubbleSize formula is
/// unparsable as a single rectangular range (bound to a defined name such as an OFFSET-based
/// dynamic range, a multi-area union, or an external-workbook link),
/// <see cref="XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas"/> used to capture only the
/// formula text into <see cref="ChartSeriesVerbatimFormulas"/> — which had no cache fields at all.
/// On write, <c>XlsxChartXmlWriter.BuildChartSeries</c> (and its Scatter/Bubble/Pie-family
/// siblings) set the numCache/strCache to null whenever a verbatim formula was present, so the
/// emitted &lt;c:numRef&gt;/&lt;c:strRef&gt; carried only &lt;c:f&gt;, with NO cache at all — even
/// though the source file's own cache values were read transiently for in-app rendering
/// (<see cref="XlsxChartSeriesRangeReader.TryReadEmbeddedSeriesData"/>) but never persisted back
/// for re-serialization. Real Excel always writes a cache alongside such formulas so the chart
/// still shows last-known data when the named range/external link can't be resolved (manual
/// calculation, or a non-recalculating consumer). Fixed by capturing the source
/// &lt;c:numCache&gt;/&lt;c:strCache&gt; verbatim alongside the formula text at load time (new
/// ValCacheXml/CatCacheXml/BubbleSizeCacheXml fields on <see cref="ChartSeriesVerbatimFormulas"/>)
/// and re-emitting it unchanged on save.
/// </summary>
public sealed class R103_ChartSeriesVerbatimCacheRoundTripTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    // ---------------------------------------------------------------------------------------
    // Reader-level: the numCache is captured alongside the formula, scoped per-container exactly
    // like the pre-existing formula capture (R99-io-chart-series-verbatim-container-scope).
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TryCollectVerbatimFormulas_NamedRangeValWithNumCache_CapturesCacheAlongsideFormula()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var series = XElement.Parse("""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:cat><c:strRef><c:f>Data!$A$2:$A$4</c:f></c:strRef></c:cat>
              <c:val>
                <c:numRef>
                  <c:f>rngDynamicSales</c:f>
                  <c:numCache>
                    <c:formatCode>General</c:formatCode>
                    <c:ptCount val="3"/>
                    <c:pt idx="0"><c:v>10</c:v></c:pt>
                    <c:pt idx="1"><c:v>20</c:v></c:pt>
                    <c:pt idx="2"><c:v>30</c:v></c:pt>
                  </c:numCache>
                </c:numRef>
              </c:val>
            </c:ser>
            """);

        var result = XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas([series], sheetId);

        result.Should().NotBeNull();
        var entry = result!.Should().ContainSingle().Subject;
        entry.ValFormula.Should().Be("rngDynamicSales");
        entry.ValCacheXml.Should().NotBeNull(
            "the source file's own numCache must be captured alongside the unparsable val formula");
        entry.ValCacheXml.Should().Contain("<c:v>10</c:v>").And.Contain("<c:v>20</c:v>").And.Contain("<c:v>30</c:v>");
        // The category formula IS parseable, so no cache capture is needed for it — it goes through
        // the ordinary positional/live-cell path instead.
        entry.CatCacheXml.Should().BeNull();
    }

    [Fact]
    public void TryCollectVerbatimFormulas_NamedRangeWithNoCache_LeavesCacheXmlNull()
    {
        // Sibling no-regression: a named range that genuinely has no cached values in the source
        // (e.g. never calculated, or a full-column reference) must not fabricate one — real Excel
        // also simply omits the cache in that case.
        var sheetId = new SheetId(Guid.NewGuid());
        var series = XElement.Parse("""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:val><c:numRef><c:f>rngNoCache</c:f></c:numRef></c:val>
            </c:ser>
            """);

        var result = XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas([series], sheetId);

        result.Should().NotBeNull();
        var entry = result!.Should().ContainSingle().Subject;
        entry.ValFormula.Should().Be("rngNoCache");
        entry.ValCacheXml.Should().BeNull();
    }

    // ---------------------------------------------------------------------------------------
    // Full read -> write round trip through the real product entry point (XlsxFileAdapter).
    // This is the fail-before/pass-after evidence for the writer-side fix.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ColumnChart_NamedRangeValFormulaWithNumCache_SurvivesLoadAndResave()
    {
        // Build a normal FreeX-authored workbook+chart first (well-formed chart1.xml, worksheet,
        // styles, etc.), then hand-edit ONLY the sole series' <c:val><c:numRef> to look like what
        // real Excel writes for a series bound to a dynamic (OFFSET-based) named range: an
        // unparsable <c:f> plus a real <c:numCache> of the last-computed values — exactly the shape
        // XlsxChartPartReader/XlsxChartSeriesRangeReader must read back and XlsxChartXmlWriter must
        // re-emit.
        var workbook = new Workbook("VerbatimValCacheRoundTrip");
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
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
        });

        var saved = SaveToBytes(workbook);
        var customized = InjectNamedRangeValWithCache(
            saved,
            "rngDynamicSales",
            [("0", "100"), ("1", "200"), ("2", "300")]);

        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedChart = reloadedWorkbook.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        reloadedChart.VerbatimSeriesFormulas.Should().NotBeNull(
            "the named-range val formula is unparsable and must engage the verbatim bypass");
        var verbatim = reloadedChart.VerbatimSeriesFormulas!.Should().ContainSingle(v => v.SeriesIndex == 0).Subject;
        verbatim.ValFormula.Should().Be("rngDynamicSales");
        verbatim.ValCacheXml.Should().NotBeNull(
            "the source file's numCache must be captured on load so it can be re-emitted on save");

        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
        var series = chartDoc.Descendants(ChartNs + "ser").Single();
        var numRef = series.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!;

        numRef.Element(ChartNs + "f")!.Value.Should().Be("rngDynamicSales",
            "the verbatim named-range formula must round-trip unchanged");

        var numCache = numRef.Element(ChartNs + "numCache");
        numCache.Should().NotBeNull(
            "THE BUG: real Excel always pairs a series formula — even a named-range/multi-area/" +
            "external-link one — with a cache of its last-computed values, so the chart still shows " +
            "last-known data under manual calculation or in a non-recalculating consumer. Before the " +
            "fix this was always null for a verbatim series.");

        var points = numCache!.Elements(ChartNs + "pt")
            .Select(pt => (Idx: pt.Attribute("idx")!.Value, Value: pt.Element(ChartNs + "v")!.Value))
            .OrderBy(p => p.Idx)
            .ToList();
        points.Should().BeEquivalentTo(
            [("0", "100"), ("1", "200"), ("2", "300")],
            "the exact cached values from the source file must be re-emitted unchanged, not recomputed " +
            "from the live worksheet cells (which have unrelated values: 10/20/30)");
    }

    // Sibling no-regression: an ordinary, fully-parseable series (the overwhelmingly common case)
    // must keep computing its numCache live from the worksheet exactly as before — this fix must
    // only change behavior for the verbatim (unparsable-formula) path.
    [Fact]
    public void ColumnChart_OrdinaryParseableSeries_StillComputesLiveNumCache_NotAffectedByFix()
    {
        var workbook = new Workbook("OrdinarySeriesUnaffected");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Series A"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue((row - 1) * 5));
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
        var numCache = series.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!.Element(ChartNs + "numCache");

        numCache.Should().NotBeNull();
        var values = numCache!.Elements(ChartNs + "pt").Select(pt => pt.Element(ChartNs + "v")!.Value).ToList();
        values.Should().BeEquivalentTo(["5", "10", "15"]);
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

    /// <summary>
    /// Rewrites the sole series' &lt;c:val&gt;&lt;c:numRef&gt; in xl/charts/chart1.xml to reference
    /// a named range (an unparsable formula) with a real &lt;c:numCache&gt; of the given
    /// idx/value pairs, mimicking what real Excel writes for a series bound to a dynamic
    /// (OFFSET-based) named range.
    /// </summary>
    private static byte[] InjectNamedRangeValWithCache(
        byte[] package,
        string namedRangeFormula,
        (string Idx, string Value)[] points)
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

            var series = chartDoc.Descendants(ChartNs + "ser").Single();
            var numRef = series.Element(ChartNs + "val")!.Element(ChartNs + "numRef");
            numRef.Should().NotBeNull("the fixture chart must already emit <c:val><c:numRef> to rewrite");

            numRef!.RemoveNodes();
            numRef.Add(new XElement(ChartNs + "f", namedRangeFormula));
            var numCache = new XElement(ChartNs + "numCache",
                new XElement(ChartNs + "formatCode", "General"),
                new XElement(ChartNs + "ptCount", new XAttribute("val", points.Length)));
            foreach (var (idx, value) in points)
            {
                numCache.Add(new XElement(ChartNs + "pt",
                    new XAttribute("idx", idx),
                    new XElement(ChartNs + "v", value)));
            }
            numRef.Add(numCache);

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/charts/chart1.xml", CompressionLevel.Optimal);
            using var newEntryStream = newEntry.Open();
            chartDoc.Save(newEntryStream);
        }

        return stream.ToArray();
    }
}
