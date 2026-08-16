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
/// R137-io-chart-series-multiarea: a discontiguous ("multi-area"/union) chart series formula such
/// as <c>Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5</c> was silently truncated to its LAST area only.
/// <para>
/// <see cref="XlsxChartSeriesRangeReader.TryParseFormulaRange"/> located the sheet-name boundary
/// with <c>local.LastIndexOf('!')</c> and then split the remainder on ':'. For a union, that lands
/// on the LAST area's own sheet separator, so the tail (e.g. <c>$C$1:$C$5</c>) splits cleanly and
/// the whole formula "parses" successfully as that final area alone — the earlier area(s) are
/// discarded with no trace. Because the parse SUCCEEDS, <see cref="XlsxChartSeriesRangeReader.HasUnparsableFormula"/>
/// never returned true for it (even though its own doc comment already promised "Multi-area
/// formulas ... also trigger this path"), so the verbatim-formula bypass
/// (<see cref="XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas"/>) never engaged. The chart
/// rendered only half its data, and the truncated single-area <see cref="GridRange"/> was recorded
/// as the series' real range — so the very next save regenerated the series formula from that
/// truncated range, permanently losing the first area.
/// </para>
/// <para>
/// Fixed by rejecting a formula containing a ',' outside any quoted sheet name up front in
/// <c>TryParseFormulaRange</c>, so it returns false (matching a named range or an external-workbook
/// link) and the pre-existing verbatim-capture machinery — already correct once it's told the
/// formula is unparsable — takes over.
/// </para>
/// <para>
/// Family coverage: every classic (non-chartEx) chart-family reader
/// (<c>XlsxChartPartReader.Bar/Line/Area/PieBubble/Scatter.cs</c>) funnels through the SAME shared
/// <c>ApplyVerbatimSeriesFormulasIfNeeded</c> → <c>TryCollectVerbatimFormulas</c> →
/// <c>HasUnparsableFormula</c> → <c>TryParseFormulaRange</c> call chain in this file, so the fix
/// covers Column/Bar/Line/Area/Pie/Doughnut/Stock/Scatter/Bubble uniformly — verified directly here
/// for an ordinary tx/cat/val series (ColumnChart-shaped) AND for a Scatter/Bubble-shaped series
/// (xVal/yVal containers), which <see cref="XlsxChartSeriesRangeReader.GetSeriesRangeContainerNames"/>
/// routes to the same <c>TryParseFormulaRange</c> call. The chartEx (Treemap/Sunburst/Funnel/
/// Waterfall/BoxAndWhisker/…) family is NOT affected by this bug at all: its own verbatim-capture
/// path (<c>XlsxChartPartReader.Deferred.cs</c>'s <c>BuildChartExVerbatimSeriesFormulas</c>) never
/// calls <c>TryParseFormulaRange</c> — it captures every &lt;cx:f&gt; formula unconditionally,
/// regardless of shape.
/// </para>
/// </summary>
public sealed class R137_ChartSeriesMultiAreaTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    // ---------------------------------------------------------------------------------------
    // Reader-level, direct: TryParseFormulaRange itself must reject a union rather than "succeed"
    // against its last area. This is the exact fail-before/pass-after boundary of the bug.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TryParseFormulaRange_TwoAreasOnOneSheet_ReturnsFalse_NotTheLastAreaAlone()
    {
        var sheetId = new SheetId(Guid.NewGuid());

        var parsed = XlsxChartSeriesRangeReader.TryParseFormulaRange(
            "Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5", sheetId, out var range);

        parsed.Should().BeFalse(
            "a discontiguous union is not a single rectangle; pre-fix this returned true with " +
            "range == just the LAST area ($C$1:$C$5), silently discarding $A$1:$A$5");
    }

    [Fact]
    public void TryParseFormulaRange_AreasOnDifferentSheets_ReturnsFalse()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var sheet2Id = new SheetId(Guid.NewGuid());
        var resolver = new Dictionary<string, SheetId> { ["Sheet1"] = sheetId, ["Sheet2"] = sheet2Id };

        var parsed = XlsxChartSeriesRangeReader.TryParseFormulaRange(
            "Sheet1!$A$1:$A$5,Sheet2!$C$1:$C$5", sheetId, resolver, out var range);

        parsed.Should().BeFalse(
            "pre-fix this resolved to Sheet2's area alone (the last area's sheet qualifier wins " +
            "the LastIndexOf('!') scan), dropping Sheet1's area with no trace");
    }

    [Fact]
    public void TryParseFormulaRange_MoreThanTwoAreas_ReturnsFalse()
    {
        var sheetId = new SheetId(Guid.NewGuid());

        var parsed = XlsxChartSeriesRangeReader.TryParseFormulaRange(
            "Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5,Sheet1!$E$1:$F$5", sheetId, out _);

        parsed.Should().BeFalse();
    }

    // Sibling no-regression: a comma INSIDE a quoted sheet name is not a union separator and must
    // keep parsing as a single ordinary area.
    [Fact]
    public void TryParseFormulaRange_QuotedSheetNameContainingComma_StillParsesAsSingleArea()
    {
        var sheetId = new SheetId(Guid.NewGuid());

        var parsed = XlsxChartSeriesRangeReader.TryParseFormulaRange(
            "'Budget, Q1'!$A$1:$A$5", sheetId, out var range);

        parsed.Should().BeTrue("a comma inside a quoted sheet name is not a union separator");
        range.Start.Row.Should().Be(1);
        range.End.Row.Should().Be(5);
    }

    // Sibling no-regression: the ordinary single-area case (the overwhelming common case) must keep
    // parsing successfully.
    [Fact]
    public void TryParseFormulaRange_OrdinarySingleArea_StillParsesSuccessfully()
    {
        var sheetId = new SheetId(Guid.NewGuid());

        var parsed = XlsxChartSeriesRangeReader.TryParseFormulaRange(
            "Sheet1!$B$2:$B$6", sheetId, out var range);

        parsed.Should().BeTrue();
        range.Start.Col.Should().Be(2);
        range.End.Row.Should().Be(6);
    }

    // ---------------------------------------------------------------------------------------
    // HasUnparsableFormula: must flag the multi-area series (matching its own doc comment's
    // pre-existing promise), for BOTH the ordinary tx/cat/val container shape and the Scatter/
    // Bubble xVal/yVal container shape.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void HasUnparsableFormula_MultiAreaValFormula_ReturnsTrue_OrdinarySeriesShape()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var series = XElement.Parse("""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:cat><c:strRef><c:f>Data!$A$2:$A$6</c:f></c:strRef></c:cat>
              <c:val><c:numRef><c:f>Data!$B$2:$B$3,Data!$B$5:$B$6</c:f></c:numRef></c:val>
            </c:ser>
            """);

        XlsxChartSeriesRangeReader.HasUnparsableFormula(series, sheetId).Should().BeTrue(
            "the val formula is a discontiguous union and must engage the verbatim bypass, " +
            "matching HasUnparsableFormula's own doc comment promise");
    }

    [Fact]
    public void HasUnparsableFormula_MultiAreaYValFormula_ReturnsTrue_ScatterSeriesShape()
    {
        // Family coverage: Scatter/Bubble series carry point data in xVal/yVal, not cat/val.
        // GetSeriesRangeContainerNames routes these through the SAME TryParseFormulaRange call.
        var sheetId = new SheetId(Guid.NewGuid());
        var series = XElement.Parse("""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:xVal><c:numRef><c:f>Data!$A$2:$A$6</c:f></c:numRef></c:xVal>
              <c:yVal><c:numRef><c:f>Data!$B$2:$B$3,Data!$B$5:$B$6</c:f></c:numRef></c:yVal>
            </c:ser>
            """);

        XlsxChartSeriesRangeReader.HasUnparsableFormula(series, sheetId).Should().BeTrue();
    }

    // Sibling no-regression: an ordinary parseable series must NOT be flagged.
    [Fact]
    public void HasUnparsableFormula_OrdinarySingleAreaSeries_ReturnsFalse()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var series = XElement.Parse("""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:cat><c:strRef><c:f>Data!$A$2:$A$4</c:f></c:strRef></c:cat>
              <c:val><c:numRef><c:f>Data!$B$2:$B$4</c:f></c:numRef></c:val>
            </c:ser>
            """);

        XlsxChartSeriesRangeReader.HasUnparsableFormula(series, sheetId).Should().BeFalse();
    }

    // ---------------------------------------------------------------------------------------
    // TryCollectVerbatimFormulas: the sharpest symptom. Pre-fix, this returned an entry whose
    // ValFormula/GridRange had already been silently truncated by the caller (HasUnparsableFormula
    // returned false, so no entry at all was produced and the caller fell through to the ordinary
    // positional path using the truncated GridRange from elsewhere in the reader). Post-fix, the
    // FULL union text is captured verbatim, unmodified.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TryCollectVerbatimFormulas_MultiAreaValFormula_CapturesFullUnionTextVerbatim()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        const string union = "Data!$B$2:$B$3,Data!$B$5:$B$6";
        var series = XElement.Parse($"""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:val><c:numRef><c:f>{union}</c:f></c:numRef></c:val>
            </c:ser>
            """);

        var result = XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas([series], sheetId);

        result.Should().NotBeNull(
            "the multi-area val formula must engage the verbatim bypass instead of silently " +
            "resolving to (and then re-saving) only its last area");
        var entry = result!.Should().ContainSingle().Subject;
        entry.ValFormula.Should().Be(union,
            "the FULL union text — both areas — must be captured; pre-fix, this path was never " +
            "reached at all because HasUnparsableFormula wrongly said the formula parsed fine");
    }

    // Sibling no-regression: an ordinary series in the same chart is untouched (mirrors R95's
    // per-series scoping guarantee — a sibling series' union must not sweep this one in).
    [Fact]
    public void TryCollectVerbatimFormulas_MultiAreaSeriesAlongsideOrdinarySeries_OnlyFlagsTheUnion()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var ordinary = XElement.Parse("""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:val><c:numRef><c:f>Data!$B$2:$B$4</c:f></c:numRef></c:val>
            </c:ser>
            """);
        var union = XElement.Parse("""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="1"/>
              <c:order val="1"/>
              <c:val><c:numRef><c:f>Data!$C$2:$C$3,Data!$C$5:$C$6</c:f></c:numRef></c:val>
            </c:ser>
            """);

        var result = XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas([ordinary, union], sheetId);

        result.Should().NotBeNull();
        result!.Should().ContainSingle().Which.SeriesIndex.Should().Be(1);
    }

    // Sibling no-regression: a series bound to a defined NAME (whose own definition might resolve
    // to a multi-area union) was already correctly captured verbatim before this fix — the chart
    // series formula itself is just the bare name text (no comma at all), so this reader never had
    // to look up what the name resolves to. Confirms the fix does not disturb this pre-existing,
    // already-correct path.
    [Fact]
    public void TryCollectVerbatimFormulas_NamedRangeFormula_StillCapturedVerbatim_UnaffectedByFix()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var series = XElement.Parse("""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:val><c:numRef><c:f>rngMultiAreaSales</c:f></c:numRef></c:val>
            </c:ser>
            """);

        var result = XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas([series], sheetId);

        result.Should().NotBeNull();
        result!.Should().ContainSingle().Which.ValFormula.Should().Be("rngMultiAreaSales");
    }

    // ---------------------------------------------------------------------------------------
    // Full read -> write round trip through the real product entry point (XlsxFileAdapter). This
    // is the fail-before/pass-after evidence for the writer/save side: the union formula must
    // survive unchanged through a load + resave, never collapsing to its last area.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void ColumnChart_MultiAreaValFormula_SurvivesLoadAndResave()
    {
        var workbook = new Workbook("MultiAreaRoundTrip");
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
        const string union = "Data!$B$2:$B$3,Data!$B$4:$B$4";
        var customized = InjectMultiAreaValFormula(saved, union);

        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedChart = reloadedWorkbook.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        reloadedChart.VerbatimSeriesFormulas.Should().NotBeNull(
            "THE BUG: the multi-area val formula must engage the verbatim bypass on load — pre-fix, " +
            "it silently parsed as the LAST area alone and never reached this list at all");
        var verbatim = reloadedChart.VerbatimSeriesFormulas!.Should().ContainSingle(v => v.SeriesIndex == 0).Subject;
        verbatim.ValFormula.Should().Be(union,
            "the full two-area union must be captured on load, unmodified");

        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
        var resavedSeries = chartDoc.Descendants(ChartNs + "ser").Single();
        var numRef = resavedSeries.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!;

        numRef.Element(ChartNs + "f")!.Value.Should().Be(union,
            "THE BUG (save side): pre-fix, the truncated single-area range silently overwrote the " +
            "on-disk formula on the very next save, permanently losing the first area ($B$2:$B$3)");
    }

    // Sibling no-regression: an ordinary (non-union) chart must still round-trip its formula through
    // the normal positional/cached path, completely unaffected by this fix.
    [Fact]
    public void ColumnChart_OrdinarySingleAreaFormula_StillRoundTripsThroughNormalPath()
    {
        var workbook = new Workbook("OrdinaryRoundTripUnaffected");
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
        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(saved, writable: false));
        var reloadedChart = reloadedWorkbook.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        reloadedChart.VerbatimSeriesFormulas.Should().BeNull(
            "an ordinary single-area series must never engage the verbatim bypass");

        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
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
    /// Rewrites the sole series' &lt;c:val&gt;&lt;c:numRef&gt;&lt;c:f&gt; in xl/charts/chart1.xml
    /// to the given multi-area union formula text, mimicking a chart series bound to a
    /// discontiguous (Ctrl-click) selection the way real Excel would emit it. No numCache is
    /// injected — this exercises the plain formula-capture path, mirroring how R95's fixture chart
    /// starts out with no cache before InjectNamedRangeValWithCache adds one.
    /// </summary>
    private static byte[] InjectMultiAreaValFormula(byte[] package, string unionFormula)
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

            var f = numRef!.Element(ChartNs + "f");
            f.Should().NotBeNull();
            f!.Value = unionFormula;

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/charts/chart1.xml");
            using var writeStream = newEntry.Open();
            chartDoc.Save(writeStream);
        }

        return stream.ToArray();
    }
}
