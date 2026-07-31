using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R108-io-chart-series-embedded-fastpath: a Bar/Column chart (and a Bar+Line combo chart) whose
/// series &lt;c:val&gt;/&lt;c:cat&gt; formulas are ALL named ranges (e.g. an OFFSET-based dynamic
/// range like <c>'Sheet1'!rngCount</c>) or ALL cross-sheet cell references takes an early-return
/// "embedded data" fast path in <c>XlsxChartPartReader.Bar.cs</c> (<c>TryReadBarChart</c> /
/// <c>TryReadBarLineComboChart</c>) that populates <see cref="ChartModel.EmbeddedSeriesData"/> for
/// on-screen rendering but used to return BEFORE calling
/// <c>ApplyVerbatimSeriesFormulasIfNeeded</c>/<c>DetectSeriesInRows</c> — the same two calls every
/// OTHER chart-reading path in the file makes. <c>XlsxChartXmlWriter</c> never reads
/// <see cref="ChartModel.EmbeddedSeriesData"/> at all; on save it relies purely on
/// <see cref="ChartModel.SeriesColumnMappings"/>/<see cref="ChartModel.VerbatimSeriesFormulas"/>,
/// both empty/null in this shape, so <c>XlsxChartXmlWriter.Series.cs</c>'s
/// <c>GetChartSeriesStripSequence</c> fell back to a positional strip scan driven by the reader's
/// recomputed (frequently degenerate — e.g. collapsed to just the &lt;c:tx&gt; title cell)
/// <see cref="ChartModel.DataRange"/>, silently emitting a &lt;c:barChart&gt; with ZERO
/// &lt;c:ser&gt; elements.
/// <para>
/// Fixed by (a) calling <c>ApplyVerbatimSeriesFormulasIfNeeded</c>/<c>DetectSeriesInRows</c> before
/// the embedded-data fast-path return in both reader functions, and (b) teaching
/// <c>GetChartSeriesStripSequence</c> to emit directly from
/// <see cref="ChartModel.VerbatimSeriesFormulas"/> when <see cref="ChartModel.SeriesColumnMappings"/>
/// is empty and the chart is not row-major, instead of trusting the (possibly degenerate) positional
/// strip scan.
/// </para>
/// </summary>
public sealed class R108_ChartEmbeddedFastPathSeriesLossTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    // ---------------------------------------------------------------------------------------------
    // Full read -> write round trip through the REAL product entry points (XlsxFileAdapter.Load /
    // .Save). This is the fail-before / pass-after evidence for the whole fix.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ColumnChart_NamedRangeCatAndValFormulas_SeriesSurvivesLoadAndResave()
    {
        // Build a normal FreeX-authored workbook with a plain 1-series column chart (well-formed
        // chart1.xml, worksheet, styles, etc.), then hand-edit ONLY that series' <c:cat>/<c:val>
        // formulas to look like real Excel's "auto-expanding chart" pattern (Chart20-style OFFSET
        // dynamic named ranges) — leaving <c:tx> as an ordinary direct cell reference, exactly the
        // shape demonstrated by the pre-existing
        // XlsxChartPartReaderTests.BarMetadata.TryReadSupportedChart_NamedRangeValCat_PopulatesEmbeddedSeriesData
        // unit test (which only asserted the read side, never round-tripped through the writer).
        var workbook = new Workbook("NamedRangeSeries");
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
            Type = ChartType.Column,
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

        // --- Real Load entry point ------------------------------------------------------------
        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Data");
        var reloadedChart = reloadedSheet.Charts.Should().ContainSingle().Subject;

        // The reader must still populate EmbeddedSeriesData for on-screen rendering...
        reloadedChart.EmbeddedSeriesData.Should().NotBeNull().And.HaveCount(1);

        // ...AND (root-cause fix) must ALSO capture the verbatim formula/cache so the writer has
        // something to re-emit from other than the (degenerate) recomputed DataRange.
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

        // THE BUG: pre-fix this was 0 (the whole series silently dropped — FirstValueStrip >
        // LastStrip because DataRange collapsed to just the <c:tx> title cell).
        seriesElements.Should().HaveCount(1,
            "THE BUG: a named-range-sourced series must survive the save — real Excel always " +
            "preserves an OFFSET-based dynamic-range chart's formulas verbatim on every save");

        var series0 = seriesElements.Single();
        var val = series0.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!;
        val.Element(ChartNs + "f")!.Value.Should().Be("'Data'!rngCount",
            "the named-range val formula must round-trip verbatim, unchanged");
        var cat = series0.Element(ChartNs + "cat")!.Element(ChartNs + "strRef")!;
        cat.Element(ChartNs + "f")!.Value.Should().Be("'Data'!rngGroups",
            "the named-range cat formula must round-trip verbatim, unchanged");

        var numCache = val.Element(ChartNs + "numCache");
        numCache.Should().NotBeNull("the source file's own cache for the named-range series must be re-emitted");
        var points = numCache!.Elements(ChartNs + "pt")
            .Select(pt => (Idx: pt.Attribute("idx")!.Value, Value: pt.Element(ChartNs + "v")!.Value))
            .OrderBy(p => p.Idx)
            .ToList();
        points.Should().BeEquivalentTo([("0", "64"), ("1", "36")]);
    }

    // Sibling no-regression: an ordinary chart with NO named-range/cross-sheet series (the
    // overwhelmingly common case) must keep going entirely through the pre-existing positional
    // strip-scan path — the new VerbatimSeriesFormulas-only branch in GetChartSeriesStripSequence
    // must never fire, and no VerbatimSeriesFormulas entry may be fabricated, for a chart that never
    // engaged the embedded-data fast path at all.
    [Fact]
    public void ColumnChart_OrdinaryCellRangeSeries_UnaffectedByFastPathFix()
    {
        var workbook = new Workbook("OrdinarySeries");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Series A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Series B"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue((row - 1) * 10));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue((row - 1) * 20));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            SeriesColumnMappings =
            [
                new ChartSeriesColumnMapping(0, 2),
                new ChartSeriesColumnMapping(1, 3),
            ],
        });

        var saved = SaveToBytes(workbook);
        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(saved, writable: false));
        var reloadedChart = reloadedWorkbook.Sheets.Single(s => s.Name == "Data").Charts.Should().ContainSingle().Subject;

        reloadedChart.EmbeddedSeriesData.Should().BeNull("no series formula is a named range or cross-sheet reference");
        reloadedChart.VerbatimSeriesFormulas.Should().BeNull(
            "no series is a named range or cross-sheet reference, so the verbatim bypass must not engage");
        reloadedChart.SeriesInRows.Should().BeFalse();

        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
        var seriesElements = chartDoc.Descendants(ChartNs + "ser").ToList();
        seriesElements.Should().HaveCount(2);

        var values = seriesElements
            .Select(s => s.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!.Element(ChartNs + "f")!.Value)
            .OrderBy(f => f)
            .ToList();
        values.Should().BeEquivalentTo(["Data!$B$2:$B$4", "Data!$C$2:$C$4"]);
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
    /// Rewrites the given series' &lt;c:cat&gt;&lt;c:strRef&gt; and &lt;c:val&gt;&lt;c:numRef&gt; in
    /// xl/charts/chart1.xml to point at named-range formulas (leaving &lt;c:tx&gt; untouched as an
    /// ordinary direct cell reference) with real strCache/numCache values — mimicking what real
    /// Excel writes for a chart bound to OFFSET-based dynamic named ranges (Chart20-style).
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
