using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R106-io-chart-series-cross-sheet: a Column/Bar/Line/Area chart with two or more series where at
/// least one series' &lt;c:val&gt; formula points at the chart's own host sheet and at least one
/// OTHER series' &lt;c:val&gt; formula points at a DIFFERENT sheet (Excel's ordinary "Select Data
/// &gt; Add Series &gt; pick a range on any sheet" scenario — e.g. a dashboard chart with a
/// "Target" series sourced from a shared parameters sheet and an "Actual" series local to the
/// chart's own sheet) used to have the cross-sheet series completely dropped the next time FreeX
/// saved the file.
/// <para>
/// Root cause chain: (1) <see cref="XlsxChartSeriesRangeReader.TryReadSeriesValueColumn"/> returns
/// null for any series whose val range resolves to a different sheet than the chart's own, so it
/// never gets a <see cref="ChartSeriesColumnMapping"/> entry. (2) The safety net meant to catch
/// series that "can't be represented positionally" —
/// <see cref="XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas"/> — used to call the
/// resolver-less <c>TryParseFormulaRange</c> overload, so a cross-sheet formula like
/// <c>'Params'!$C$2:$C$4</c> "parsed" successfully against the WRONG (chart's own) sheet and was
/// never flagged as needing the verbatim bypass either — the series landed in neither
/// <see cref="ChartModel.SeriesColumnMappings"/> nor <see cref="ChartModel.VerbatimSeriesFormulas"/>.
/// (3) On write, <c>XlsxChartXmlWriter.Series.cs</c>'s <c>GetChartSeriesStripSequence</c> treats a
/// non-empty <see cref="ChartModel.SeriesColumnMappings"/> as the COMPLETE set of series to emit
/// the moment every entry present falls inside the value-strip span — it never checked that the
/// mapped count covered every series the chart actually has, so the un-mapped cross-sheet series
/// was silently skipped.
/// </para>
/// <para>
/// Fixed by (a) threading the sheet-name resolver into
/// <c>HasUnparsableFormula</c>/<c>TryCollectVerbatimFormulas</c> so a formula that resolves cleanly
/// but to a different sheet is now ALSO captured verbatim (formula + cache), exactly like a
/// genuinely-unparsable one, and (b) having <c>GetChartSeriesStripSequence</c> additionally yield
/// any series present in <see cref="ChartModel.VerbatimSeriesFormulas"/> with a captured
/// <c>ValFormula</c> but no column mapping, so it is still emitted (using its own verbatim
/// formula/cache instead of a recomputed strip range).
/// </para>
/// </summary>
public sealed class R106_ChartCrossSheetSeriesMixedSameSheetTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    // ---------------------------------------------------------------------------------------------
    // Full read -> write round trip through the REAL product entry points (XlsxFileAdapter.Load /
    // .Save). This is the fail-before / pass-after evidence for the whole fix.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void ColumnChart_MixedLocalAndCrossSheetSeries_BothSeriesSurviveLoadAndResave()
    {
        // Build a normal FreeX-authored workbook with TWO worksheets and a plain 2-series column
        // chart hosted on "Data" (well-formed chart1.xml, worksheet, styles, etc.), then hand-edit
        // ONLY series index 1's <c:val> to look like what real Excel writes when that series was
        // added from a DIFFERENT sheet ("Select Data > Add Series > pick a range on 'Params'") —
        // an ordinary, perfectly parseable range formula, just pointing at another sheet, with its
        // own real <c:numCache> of the last-computed values.
        var workbook = new Workbook("MixedCrossSheetSeries");
        var dataSheet = workbook.AddSheet("Data");
        var paramsSheet = workbook.AddSheet("Params");

        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 1), new TextValue("Cat"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 2), new TextValue("Actual"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 3), new TextValue("PlaceholderTarget"));
        for (uint row = 2; row <= 4; row++)
        {
            dataSheet.SetCell(new CellAddress(dataSheet.Id, row, 1), new TextValue($"C{row}"));
            dataSheet.SetCell(new CellAddress(dataSheet.Id, row, 2), new NumberValue((row - 1) * 10));
            // Column C is a placeholder — its live values (999/999/999) must NOT show up anywhere
            // in the resaved file once column C's series is rebound to the Params sheet below; if
            // they did, the writer would be silently ignoring the captured verbatim cache and
            // recomputing from the wrong (local) cells instead.
            dataSheet.SetCell(new CellAddress(dataSheet.Id, row, 3), new NumberValue(999));
        }
        for (uint row = 2; row <= 4; row++)
            paramsSheet.SetCell(new CellAddress(paramsSheet.Id, row, 2), new NumberValue((row - 1) * 100));

        dataSheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(dataSheet.Id, 1, 1), new CellAddress(dataSheet.Id, 4, 3)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            SeriesColumnMappings =
            [
                new ChartSeriesColumnMapping(0, 2),
                new ChartSeriesColumnMapping(1, 3),
            ],
        });

        var saved = SaveToBytes(workbook);
        var customized = RebindSeriesValueToOtherSheet(
            saved,
            seriesIdx: "1",
            crossSheetFormula: "'Params'!$B$2:$B$4",
            points: [("0", "100"), ("1", "200"), ("2", "300")]);

        // --- Real Load entry point ------------------------------------------------------------
        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Data");
        var reloadedChart = reloadedSheet.Charts.Should().ContainSingle().Subject;

        // Root-cause (2): the cross-sheet series must now be captured verbatim on load.
        reloadedChart.VerbatimSeriesFormulas.Should().NotBeNull(
            "the cross-sheet val formula must engage the verbatim bypass now that the resolver is threaded through");
        var verbatim = reloadedChart.VerbatimSeriesFormulas!.Should().ContainSingle(v => v.SeriesIndex == 1).Subject;
        verbatim.ValFormula.Should().Be("'Params'!$B$2:$B$4");
        verbatim.ValCacheXml.Should().NotBeNull();

        // --- Real Save entry point -------------------------------------------------------------
        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
        var seriesElements = chartDoc.Descendants(ChartNs + "ser").ToList();

        // THE BUG: pre-fix this was 1 (series 1 silently dropped).
        seriesElements.Should().HaveCount(2,
            "THE BUG: the cross-sheet series must survive the save — real Excel never removes a " +
            "series just because it isn't the only source sheet in play");

        var series0 = seriesElements.Single(s => s.Element(ChartNs + "idx")!.Attribute("val")!.Value == "0");
        var series1 = seriesElements.Single(s => s.Element(ChartNs + "idx")!.Attribute("val")!.Value == "1");

        var series0Val = series0.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!;
        series0Val.Element(ChartNs + "f")!.Value.Should().Be("Data!$B$2:$B$4",
            "series 0's own local formula is unaffected by the fix and keeps the recomputed strip range");

        var series1Val = series1.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!;
        series1Val.Element(ChartNs + "f")!.Value.Should().Be("'Params'!$B$2:$B$4",
            "the cross-sheet formula must round-trip verbatim, unchanged");

        var numCache = series1Val.Element(ChartNs + "numCache");
        numCache.Should().NotBeNull(
            "the source file's own cache for the cross-sheet series must be re-emitted, not fabricated " +
            "as null and not recomputed from the unrelated local placeholder column");
        var points = numCache!.Elements(ChartNs + "pt")
            .Select(pt => (Idx: pt.Attribute("idx")!.Value, Value: pt.Element(ChartNs + "v")!.Value))
            .OrderBy(p => p.Idx)
            .ToList();
        points.Should().BeEquivalentTo(
            [("0", "100"), ("1", "200"), ("2", "300")],
            "the exact cached values from the cross-sheet source must be re-emitted, not the local " +
            "placeholder column's live values (999/999/999)");
    }

    // Sibling no-regression: a chart where EVERY series is local (the overwhelmingly common case)
    // must keep going entirely through the ordinary positional/column-mapped path — the new
    // fallback in GetChartSeriesStripSequence must never fire, and no VerbatimSeriesFormulas entry
    // may be fabricated, for a chart that has no cross-sheet series at all.
    [Fact]
    public void ColumnChart_AllSeriesLocal_NoVerbatimEntriesAndBothSeriesKeepColumnMappings()
    {
        var workbook = new Workbook("AllLocalSeries");
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

        reloadedChart.VerbatimSeriesFormulas.Should().BeNull(
            "no series is cross-sheet or unparsable, so the verbatim bypass must not engage at all");

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
    /// Rewrites the given series' &lt;c:val&gt;&lt;c:numRef&gt; in xl/charts/chart1.xml to point at
    /// a DIFFERENT sheet with a real &lt;c:numCache&gt; of the given idx/value pairs — mimicking
    /// what real Excel writes for a series whose data range was picked on another worksheet via
    /// "Select Data > Add Series".
    /// </summary>
    private static byte[] RebindSeriesValueToOtherSheet(
        byte[] package,
        string seriesIdx,
        string crossSheetFormula,
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

            var series = chartDoc.Descendants(ChartNs + "ser")
                .Single(s => s.Element(ChartNs + "idx")!.Attribute("val")!.Value == seriesIdx);
            var numRef = series.Element(ChartNs + "val")!.Element(ChartNs + "numRef");
            numRef.Should().NotBeNull("the fixture chart must already emit <c:val><c:numRef> to rewrite");

            numRef!.RemoveNodes();
            numRef.Add(new XElement(ChartNs + "f", crossSheetFormula));
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
