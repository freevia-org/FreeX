using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R99-io-chart-series-verbatim-container-scope: <c>TryCollectVerbatimFormulas</c> was scoped
/// per-series (R95) but not per-CONTAINER. Once ANY one container in a series (tx/cat/val, or
/// xVal/yVal/bubbleSize for Scatter/Bubble) failed to parse as a rectangular range, every OTHER
/// container in that SAME series was also swept into the verbatim record with its raw text —
/// even when that sibling container's own formula was a perfectly ordinary, resolvable range.
/// <para>
/// <c>XlsxChartXmlWriter.Series.cs</c> keys its cache/ref-type decisions purely on
/// <c>verbatim?.ValFormula is null</c> / <c>verbatim?.CatFormula is null</c>. Because the reader
/// always populated every field once the series was flagged, a container that was never actually
/// unparsable still lost its numCache/strCache, and — for the category container — had its ref
/// type force-downgraded from &lt;c:cat&gt;&lt;c:numRef&gt; to &lt;c:cat&gt;&lt;c:strRef&gt;.
/// </para>
/// <para>
/// Fixed by having <c>TryCollectVerbatimFormulas</c> populate a given field only when THAT
/// SPECIFIC container's own formula fails to parse, leaving it null when the container itself is
/// parseable — even if a sibling container in the same series needed the verbatim bypass.
/// </para>
/// </summary>
public sealed class R99_ChartVerbatimContainerScopeTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    private static XElement ParseSeries(string xml) => XElement.Parse(xml);

    // --- Reader-level: the actual fix (fail-before / pass-after) --------------------------------

    [Fact]
    public void R99_TryCollectVerbatimFormulas_CatParseableValUnparsable_CatFormulaStaysNull()
    {
        var sheetId = new SheetId(Guid.NewGuid());

        // A series whose category formula is an ordinary, fully-parseable range (Data!$A$2:$A$4)
        // but whose value formula is bound to a defined name (rngDynamicSales) — genuinely
        // unparsable, so the series as a whole is (correctly) flagged. The category container's
        // OWN formula, however, was never unparsable and must not be captured verbatim.
        var series = ParseSeries("""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:cat><c:numRef><c:f>Data!$A$2:$A$4</c:f><c:numCache><c:pt idx="0"><c:v>2020</c:v></c:pt></c:numCache></c:numRef></c:cat>
              <c:val><c:numRef><c:f>rngDynamicSales</c:f><c:numCache><c:pt idx="0"><c:v>10</c:v></c:pt></c:numCache></c:numRef></c:val>
            </c:ser>
            """);

        var result = XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas([series], sheetId);

        result.Should().NotBeNull(
            "the series' own val formula (rngDynamicSales) is unparsable, so it must still get an entry");
        var entry = result!.Single();
        entry.ValFormula.Should().Be("rngDynamicSales",
            "the val container's OWN formula is genuinely unparsable and must be captured verbatim");
        entry.CatFormula.Should().BeNull(
            "the cat container's OWN formula (Data!$A$2:$A$4) is perfectly parseable — it must NOT be " +
            "swept into the verbatim record just because a sibling container (val) in the same series " +
            "needed the bypass. A non-null CatFormula here would make the writer drop this container's " +
            "numCache and downgrade its numeric category from <c:cat><c:numRef> to <c:cat><c:strRef>.");
    }

    // Sibling no-regression: when the FLAGGED container itself is the category (and val is
    // parseable), ValFormula must stay null while CatFormula is captured — proves the fix is
    // symmetric, not just special-cased for val.
    [Fact]
    public void R99_TryCollectVerbatimFormulas_ValParseableCatUnparsable_ValFormulaStaysNull()
    {
        var sheetId = new SheetId(Guid.NewGuid());

        var series = ParseSeries("""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:cat><c:strRef><c:f>rngCategoryNames</c:f></c:strRef></c:cat>
              <c:val><c:numRef><c:f>Data!$B$2:$B$4</c:f></c:numRef></c:val>
            </c:ser>
            """);

        var result = XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas([series], sheetId);

        result.Should().NotBeNull();
        var entry = result!.Single();
        entry.CatFormula.Should().Be("rngCategoryNames");
        entry.ValFormula.Should().BeNull(
            "the val container's own formula (Data!$B$2:$B$4) is parseable and must not be captured " +
            "verbatim just because the sibling cat container needed the bypass");
    }

    // Sibling no-regression: bubble chart whose ONLY unparsable part is bubbleSize must keep both
    // xVal and yVal (repurposed as CatFormula/ValFormula) un-captured, so the writer still builds
    // fresh numCache for both.
    [Fact]
    public void R99_TryCollectVerbatimFormulas_BubbleChart_OnlyBubbleSizeUnparsable_XValYValStayNull()
    {
        var sheetId = new SheetId(Guid.NewGuid());

        var series = ParseSeries("""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:xVal><c:numRef><c:f>Data!$A$2:$A$4</c:f></c:numRef></c:xVal>
              <c:yVal><c:numRef><c:f>Data!$B$2:$B$4</c:f></c:numRef></c:yVal>
              <c:bubbleSize><c:numRef><c:f>[1]Sheet1!$C$2:$C$4</c:f></c:numRef></c:bubbleSize>
            </c:ser>
            """);

        var result = XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas([series], sheetId);

        result.Should().NotBeNull("the external-link bubbleSize formula is genuinely unparsable");
        var entry = result!.Single();
        entry.BubbleSizeFormula.Should().Be("[1]Sheet1!$C$2:$C$4");
        entry.CatFormula.Should().BeNull(
            "xVal (repurposed as CatFormula) is a perfectly ordinary resolvable range — it must not be " +
            "captured verbatim just because bubbleSize needed the bypass, or BuildBubbleChartSeries would " +
            "wrongly drop xValueCache too");
        entry.ValFormula.Should().BeNull(
            "yVal (repurposed as ValFormula) is a perfectly ordinary resolvable range — it must not be " +
            "captured verbatim just because bubbleSize needed the bypass, or BuildBubbleChartSeries would " +
            "wrongly drop yValueCache too");
    }

    // Sibling no-regression (matches R95's existing coverage): when EVERY container in a series is
    // genuinely unparsable, all of them must still be captured — the fix must not turn into
    // "never capture more than one field".
    [Fact]
    public void R99_TryCollectVerbatimFormulas_AllContainersUnparsable_AllStillCaptured()
    {
        var sheetId = new SheetId(Guid.NewGuid());

        var series = ParseSeries("""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:tx><c:strRef><c:f>rngSeriesName</c:f></c:strRef></c:tx>
              <c:cat><c:numRef><c:f>rngCategoryValues</c:f></c:numRef></c:cat>
              <c:val><c:numRef><c:f>rngDynamicSales</c:f></c:numRef></c:val>
            </c:ser>
            """);

        var result = XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas([series], sheetId);

        result.Should().NotBeNull();
        var entry = result!.Single();
        entry.TxFormula.Should().Be("rngSeriesName");
        entry.CatFormula.Should().Be("rngCategoryValues");
        entry.ValFormula.Should().Be("rngDynamicSales");
    }

    // --- Writer-level: the visible symptom, exercised through the REAL Load -> Save pipeline ----

    // This is the true reader+writer round trip the R95 test's hand-built VerbatimSeriesFormulas
    // sidestepped: a real xlsx package (produced by FreeX's own writer, then mutated to swap only
    // the val formula for a named range, exactly as a real-world dynamic-range chart would look)
    // is loaded through XlsxFileAdapter.Load (the real product entry point that calls
    // XlsxChartPartReader -> TryCollectVerbatimFormulas), then re-saved through
    // XlsxFileAdapter.Save. The category container's own formula was never unparsable, so it must
    // survive the round trip with its numCache intact and its ref type still <c:cat><c:numRef>.
    [Fact]
    public void R99_RealLoadSaveRoundTrip_CatParseableValNamedRange_CatKeepsNumCacheAndNumericRef()
    {
        var workbook = new Workbook("VerbatimContainerScope");
        var sheet = workbook.AddSheet("Data");

        // Numeric category column (years) — Excel/FreeX emit <c:cat><c:numRef><c:numCache> for this.
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2020));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2021));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(2022));

        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstColIsCategories = true,
            FirstRowIsHeader = false,
        });

        // Step 1: real writer produces an ordinary chart (both cat and val fully parseable ranges).
        var initialPackage = SaveToBytes(workbook);

        // Step 2: mutate ONLY the val formula in the packaged chart XML to a defined-name reference
        // — simulating a real-world file where the value series is bound to a dynamic named range
        // while the category axis stays a plain worksheet reference.
        var mutatedPackage = RewriteSeriesValFormula(initialPackage, "rngDynamicSales");
        var mutatedChartXml = LoadChartXml(mutatedPackage);

        // Step 3: real reader entry point — XlsxChartPartReader.TryReadSupportedChart, the exact
        // method XlsxFileAdapter.Load calls per chart part (XlsxFileAdapter.
        // LoadSheetXmlLayoutApplication.cs) — which internally invokes
        // XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas. Going through the full
        // XlsxFileAdapter.Load()/Save() round trip here would hit the adapter's fidelity-
        // preserving "model unchanged since load" and cell-patch fast paths, which copy the
        // ORIGINAL chart1.xml bytes straight through untouched by anything the reader/writer
        // would otherwise produce — masking the bug this test targets. Calling the reader's own
        // public entry point directly (as Load does) and then handing the resulting REAL
        // ChartModel to a fresh Workbook exercises the true reader-then-writer contract without
        // that unrelated short-circuit machinery in the way.
        XlsxChartPartReader.TryReadSupportedChart(mutatedChartXml, sheet.Id, out var reloadedChart)
            .Should().BeTrue("the mutated chart XML must still be a supported Column chart");

        var reloadedWorkbook = new Workbook("VerbatimContainerScopeReloaded");
        var reloadedSheet = reloadedWorkbook.AddSheet("Data");
        reloadedSheet.SetCell(new CellAddress(reloadedSheet.Id, 2, 1), new NumberValue(2020));
        reloadedSheet.SetCell(new CellAddress(reloadedSheet.Id, 3, 1), new NumberValue(2021));
        reloadedSheet.SetCell(new CellAddress(reloadedSheet.Id, 4, 1), new NumberValue(2022));
        reloadedSheet.SetCell(new CellAddress(reloadedSheet.Id, 2, 2), new NumberValue(10));
        reloadedSheet.SetCell(new CellAddress(reloadedSheet.Id, 3, 2), new NumberValue(20));
        reloadedSheet.SetCell(new CellAddress(reloadedSheet.Id, 4, 2), new NumberValue(30));
        reloadedChart!.DataRange = new GridRange(
            new CellAddress(reloadedSheet.Id, 2, 1), new CellAddress(reloadedSheet.Id, 4, 2));
        reloadedSheet.Charts.Add(reloadedChart);

        // Step 4: real writer entry point again (a fresh Workbook has no source package, so this
        // always takes the full model-driven rebuild path), using whatever VerbatimSeriesFormulas
        // the real reader actually produced (not a hand-constructed stand-in).
        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
        var series = chartDoc.Descendants(ChartNs + "ser").Single();

        var cat = series.Element(ChartNs + "cat")!;
        cat.Element(ChartNs + "numRef").Should().NotBeNull(
            "the category column is numeric and its own formula was always parseable — it must stay " +
            "<c:cat><c:numRef>, not be downgraded to <c:cat><c:strRef>, just because the sibling val " +
            "formula is a named range");
        cat.Element(ChartNs + "numRef")!.Element(ChartNs + "numCache").Should().NotBeNull(
            "the category container's numCache must be re-fabricated since its own formula was never unparsable");

        var val = series.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!;
        val.Element(ChartNs + "f")!.Value.Should().Be("rngDynamicSales",
            "the val formula is genuinely unparsable (a defined name) and must round-trip verbatim");
        val.Element(ChartNs + "numCache").Should().BeNull(
            "a verbatim (named-range) val formula has no known live strip, so no cache should be fabricated for it");
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
    /// Rewrites the &lt;c:val&gt;'s &lt;c:f&gt; formula text (and drops its numCache, matching how
    /// a real dynamic-named-range chart from Excel would look — the cache would reflect whatever
    /// the name currently resolves to, which is irrelevant here) inside the packaged chart1.xml,
    /// leaving every other part of the package (including &lt;c:cat&gt;) untouched.
    /// </summary>
    private static byte[] RewriteSeriesValFormula(byte[] package, string newFormula)
    {
        using var stream = new MemoryStream();
        stream.Write(package, 0, package.Length);
        stream.Position = 0;

        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/charts/chart1.xml")!;
            XDocument chartXml;
            using (var entryStream = entry.Open())
                chartXml = XDocument.Load(entryStream);

            var valFormulaElement = chartXml.Descendants(ChartNs + "val")
                .Descendants(ChartNs + "f")
                .Single();
            valFormulaElement.Value = newFormula;

            var numCache = chartXml.Descendants(ChartNs + "val")
                .Descendants(ChartNs + "numCache")
                .SingleOrDefault();
            numCache?.Remove();

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/charts/chart1.xml", CompressionLevel.Optimal);
            using var writer = new StreamWriter(newEntry.Open(), Encoding.UTF8);
            chartXml.Save(writer, SaveOptions.DisableFormatting);
        }

        return stream.ToArray();
    }
}
