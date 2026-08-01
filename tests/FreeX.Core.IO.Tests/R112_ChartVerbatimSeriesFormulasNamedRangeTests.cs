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
/// R112-io-chart-deferred-named-range-verbatim: r111 stopped
/// <c>TryReadDeferredAdvancedChart</c> (the eighth reader for named-range chart series — see
/// <see cref="R111_ChartDeferredAdvancedEmbeddedFastPathTests"/>) from silently DROPPING a chart
/// whose every series formula is an unresolvable named range, by populating
/// <c>chart.EmbeddedSeriesData</c> so the chart could still be rendered — but it never called
/// <c>ApplyVerbatimSeriesFormulasIfNeeded</c> (or a chartEx-native equivalent) the way every one of
/// the seven r108-fixed readers do (e.g. <c>XlsxChartPartReader.Bar.cs</c>), so
/// <c>chart.VerbatimSeriesFormulas</c> stayed null. Without it, re-saving such a chart made
/// <c>XlsxChartXmlWriter</c> re-derive the series positionally from the synthetic 1x1 placeholder
/// <c>chart.DataRange</c> the reader sets in this branch — which, being 1x1, collapses the series
/// count to (at most) one and replaces every named-range formula with a nonsense recomputed cell
/// reference, corrupting or dropping the rest.
///
/// This covers BOTH halves of the eighth reader named in the finding:
/// <list type="bullet">
/// <item>The classic &lt;c:ser&gt;-shaped half (ThreeDColumn/ThreeDBar/Surface/ThreeDSurface, via
/// <c>TryReadThreeDBarChart</c> and the surfaceChart/surface3DChart branches of
/// <c>FindDeferredAdvancedChart</c>) — fixed by calling the SAME
/// <c>ApplyVerbatimSeriesFormulasIfNeeded</c> the seven r108-fixed readers already call, so the
/// EXISTING classic <c>BuildSeriesStripSequence</c>/<c>GetChartSeriesStripSequence</c> verbatim path
/// in <c>XlsxChartXmlWriter.Series.cs</c> picks it up with no writer changes at all.</item>
/// <item>The true &lt;cx:series&gt;-shaped chartEx-native half (Waterfall/Histogram/BoxAndWhisker/
/// Treemap/Sunburst/Funnel/Pareto) — fixed by a NEW chartEx-native capture
/// (<c>BuildChartExVerbatimSeriesFormulas</c>) plus a NEW verbatim-aware branch in
/// <c>XlsxChartXmlWriter.ChartEx.cs</c>'s <c>BuildChartExData</c>, since that writer had (and — for
/// any chart that never engaged the reader's fallback — still has) zero references to
/// <c>VerbatimSeriesFormulas</c>/<c>EmbeddedSeriesData</c> at all.</item>
/// </list>
///
/// Every fixture below is produced by the product's OWN writer (via <see cref="XlsxFileAdapter"/>)
/// and then surgically rewritten to swap the writer's plain cell-range formulas for named-range
/// formulas plus an embedded cache — mirroring exactly what real Excel emits for a workbook using
/// OFFSET-based dynamic named ranges, per the r112 round-trip fixture rule.
///
/// TWO REACHABILITY GOTCHAS investigated for this round (neither is part of the fix, but both had to
/// be worked around to write a test that actually exercises the regenerating writer at all):
/// <list type="bullet">
/// <item><b>Patch-save short-circuit.</b> Loading a workbook and immediately re-saving it with
/// ZERO edits takes FreeX's "nothing changed" patch-save fast path, which never calls
/// <c>XlsxWorksheetChartWriter.Save</c>/<c>XlsxChartXmlWriter.ToChartXml</c> at all -- it just
/// copies the chart part's ORIGINAL bytes through unchanged. A naive
/// "Load(customized) -> Save()" test therefore "passes" even on the UNFIXED code, for the wrong
/// reason (the broken code path is simply never reached), not because the fix works. Every resave
/// below first makes an unrelated, real cell edit (<c>SetCell</c> on a throwaway cell) to force the
/// full chart-regenerating save path.</item>
/// <item><b>IsSupportedXlsxChart's degenerate-DataRange gate.</b> Even once the full save path
/// runs, <c>XlsxChartXmlWriter.IsSupportedXlsxChart</c> gates every chart on
/// <c>ChartTypeSupport.GetDataSeriesCount</c>/<c>GetDataPointCount</c>, which derive purely from
/// <c>chart.DataRange</c>'s row/column SPAN -- never from <c>EmbeddedSeriesData</c>/
/// <c>VerbatimSeriesFormulas</c>. Since this reader's fallback always sets a synthetic 1x1
/// <c>DataRange</c>, that span is always zero, and the hard-coded header-row/category-column offsets
/// subtract from a span that is already zero -- so GetDataPointCount is UNCONDITIONALLY 0 whenever
/// <c>chart.FirstRowIsHeader</c> is true, and GetDataSeriesCount is 0 whenever a category column is
/// counted too. When IsSupportedXlsxChart is false, the chart's sheet is skipped by the writer and
/// (again) its ORIGINAL bytes pass through unchanged. A header row is extremely common on a real
/// Excel chart, so this second gap ALSO silently defeats not just this fix but every one of the
/// pre-existing r108 readers' <c>ApplyVerbatimSeriesFormulasIfNeeded</c> calls, for any named-range
/// chart with "First Row as Header" set -- every fixture below therefore has NO header row, and (for
/// the classic-&lt;ser&gt; and Waterfall cases) no category column either, to keep
/// IsSupportedXlsxChart true. Flagged in siblingLeads for a dedicated follow-up (teaching
/// GetDataSeriesCount/GetDataPointCount to consult EmbeddedSeriesData when DataRange is degenerate)
/// rather than folded into this defect's scope.</item>
/// </list>
/// </summary>
public sealed class R112_ChartVerbatimSeriesFormulasNamedRangeTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace ChartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";

    // ---------------------------------------------------------------------------------------------
    // THE FIX, half 1: classic <c:ser>-shaped family member (ThreeDColumn stands in for
    // ThreeDColumn/ThreeDBar/Surface/ThreeDSurface, which all share TryReadDeferredAdvancedChart's
    // classic-<ser> branch). TWO named-range series (no category, no header -- see the class-level
    // reachability note) prove both the formula-preservation AND the series-COUNT preservation (a
    // 1x1 placeholder DataRange can otherwise collapse the strip loop to a single series, silently
    // dropping the second) through the REAL regenerating writer path.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void ThreeDColumnChart_TwoNamedRangeSeries_FormulasCacheAndCountSurviveResave()
    {
        var saved = SaveThreeDColumnValueOnlyTwoSeriesChart();

        var customized = RewriteSeriesValToNamedRange(
            saved, seriesIdx: "0",
            valFormula: "'Data'!rngCountX", valPoints: [("0", "64"), ("1", "36")]);
        customized = RewriteSeriesValToNamedRange(
            customized, seriesIdx: "1",
            valFormula: "'Data'!rngCountY", valPoints: [("0", "80"), ("1", "20")]);

        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Data");
        var reloadedChart = reloadedSheet.Charts.Should().ContainSingle().Subject;
        reloadedChart.EmbeddedSeriesData.Should().NotBeNull().And.HaveCount(2);

        FreeX.Core.IO.XlsxChartXmlWriter.IsSupportedXlsxChart(reloadedChart).Should().BeTrue(
            "this fixture (no category, no header) must reach the REAL regenerating writer path -- " +
            "see the class-level reachability note -- or this test would prove nothing");

        // Force the full chart-regenerating save path -- see the class-level "patch-save
        // short-circuit" reachability note: an unedited resave never calls the writer at all.
        reloadedSheet.SetCell(new CellAddress(reloadedSheet.Id, 20, 20), new TextValue("unrelated edit"));

        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
        var resavedSeries = chartDoc.Descendants(ChartNs + "ser").ToList();

        // THE BUG: pre-fix, chart.VerbatimSeriesFormulas stayed null, so GetChartSeriesStripSequence
        // fell through to the legacy positional scan of the synthetic 1x1 placeholder DataRange —
        // which only ever yields ONE (strip, seriesIndex) pair, silently dropping the second series.
        resavedSeries.Should().HaveCount(2,
            "both named-range series must survive a resave — THE BUG collapsed the strip scan to a " +
            "single series because the synthetic 1x1 placeholder DataRange has only one strip");

        var series0 = resavedSeries.Single(s => s.Element(ChartNs + "idx")!.Attribute("val")!.Value == "0");
        var series1 = resavedSeries.Single(s => s.Element(ChartNs + "idx")!.Attribute("val")!.Value == "1");

        // THE BUG: pre-fix, the surviving series' <c:val><c:f> was recomputed from the placeholder
        // range (e.g. "Data!$A$1") instead of the ORIGINAL named-range formula.
        series0.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!.Element(ChartNs + "f")!.Value
            .Should().Be("'Data'!rngCountX", "the original named-range formula must round-trip verbatim, not a recomputed positional range");
        series1.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!.Element(ChartNs + "f")!.Value
            .Should().Be("'Data'!rngCountY");

        var cache0 = series0.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!.Element(ChartNs + "numCache")!;
        cache0.Elements(ChartNs + "pt").Select(pt => pt.Element(ChartNs + "v")!.Value)
            .Should().Equal(["64", "36"], "the cached values captured at load time must round-trip verbatim");
    }

    // Sibling no-regression: an ordinary (non-named-range) two-series 3D-Column chart with a real
    // header row and category column must be completely unaffected — no VerbatimSeriesFormulas
    // engages, and the strip scan finds both series positionally exactly as before this fix.
    [Fact]
    public void ThreeDColumnChart_OrdinaryTwoSeries_UnaffectedByVerbatimFix()
    {
        var saved = SaveThreeDColumnHeaderedTwoSeriesChart();
        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(saved, writable: false));
        var reloadedSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Data");
        var reloadedChart = reloadedSheet.Charts.Should().ContainSingle().Subject;

        reloadedChart.EmbeddedSeriesData.Should().BeNull("no series formula is a named range");
        reloadedChart.VerbatimSeriesFormulas.Should().BeNull("no series formula is unparsable");

        // Force the full chart-regenerating save path -- see the class-level "patch-save
        // short-circuit" reachability note: an unedited resave never calls the writer at all.
        reloadedSheet.SetCell(new CellAddress(reloadedSheet.Id, 20, 20), new TextValue("unrelated edit"));

        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
        chartDoc.Descendants(ChartNs + "ser").Should().HaveCount(2, "both ordinary series must still round-trip");
    }

    // ---------------------------------------------------------------------------------------------
    // THE FIX, half 2: true chartEx <cx:series>-shaped family member. Waterfall stands in for the
    // "val" branch of ToChartExNumericDimensionType (Histogram/Pareto/BoxAndWhisker/Waterfall/
    // Funnel); R112_ChartExTreemapSunburstNumDimSizeTests already covers the sibling "size" branch
    // (Treemap/Sunburst) for the read-only half of this defect, so this file focuses the chartEx
    // coverage on the round-trip (write) half that file does not test at all.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void WaterfallChart_TwoNamedRangeSeries_FormulasCacheAndCountSurviveResave()
    {
        var saved = SaveChartExValueOnlyTwoSeriesChart(ChartType.Waterfall);

        var customized = RewriteChartExNumDimToNamedRange(
            saved, dataId: "0",
            numFormula: "'Data'!rngDeltaX", numPoints: [("0", "100"), ("1", "-40")]);
        customized = RewriteChartExNumDimToNamedRange(
            customized, dataId: "1",
            numFormula: "'Data'!rngDeltaY", numPoints: [("0", "50"), ("1", "-10")]);

        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Data");
        var reloadedChart = reloadedSheet.Charts.Should().ContainSingle().Subject;
        reloadedChart.EmbeddedSeriesData.Should().NotBeNull().And.HaveCount(2);

        FreeX.Core.IO.XlsxChartXmlWriter.IsSupportedXlsxChart(reloadedChart).Should().BeTrue(
            "this fixture (no category, no header) must reach the REAL regenerating writer path -- " +
            "see the class-level reachability note -- or this test would prove nothing");

        // Force the full chart-regenerating save path -- see the class-level "patch-save
        // short-circuit" reachability note: an unedited resave never calls the writer at all.
        reloadedSheet.SetCell(new CellAddress(reloadedSheet.Id, 20, 20), new TextValue("unrelated edit"));

        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
        var resavedData = chartDoc.Descendants(ChartExNs + "data").ToList();

        // THE BUG: pre-fix, BuildChartExData unconditionally derived a positional strip range from
        // GetSeriesStripLayout, which for the synthetic 1x1 placeholder DataRange collapses to a
        // single strip — dropping the second series entirely.
        resavedData.Should().HaveCount(2,
            "both named-range chartEx series must survive a resave — THE BUG collapsed the strip " +
            "derivation to a single <cx:data> because the synthetic 1x1 placeholder DataRange has " +
            "only one strip, and BuildChartExData had no VerbatimSeriesFormulas/EmbeddedSeriesData " +
            "awareness at all to fall back on");

        var data0 = resavedData.Single(d => d.Attribute("id")!.Value == "0");
        var data1 = resavedData.Single(d => d.Attribute("id")!.Value == "1");

        // THE BUG: pre-fix, the surviving <cx:numDim><cx:f> was recomputed from the placeholder
        // range instead of the ORIGINAL named-range formula.
        data0.Element(ChartExNs + "numDim")!.Element(ChartExNs + "f")!.Value.Should().Be("'Data'!rngDeltaX",
            "the original named-range formula must round-trip verbatim, not a recomputed positional range");
        data1.Element(ChartExNs + "numDim")!.Element(ChartExNs + "f")!.Value.Should().Be("'Data'!rngDeltaY");

        var lvl0 = data0.Element(ChartExNs + "numDim")!.Element(ChartExNs + "lvl")!;
        lvl0.Elements(ChartExNs + "pt").Select(pt => pt.Value)
            .Should().Equal(["100", "-40"], "the cached values captured at load time must round-trip verbatim");
    }

    // Sibling no-regression: an ordinary (non-named-range) chartEx chart with a real header row and
    // category column must be completely unaffected by the new verbatim-aware branch in
    // BuildChartExData.
    [Fact]
    public void WaterfallChart_OrdinaryTwoSeries_UnaffectedByVerbatimFix()
    {
        var saved = SaveChartExHeaderedTwoSeriesChart(ChartType.Waterfall);
        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(saved, writable: false));
        var reloadedSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Data");
        var reloadedChart = reloadedSheet.Charts.Should().ContainSingle().Subject;

        reloadedChart.EmbeddedSeriesData.Should().BeNull("no series formula is a named range");
        reloadedChart.VerbatimSeriesFormulas.Should().BeNull("no series formula is unparsable");

        // Force the full chart-regenerating save path -- see the class-level "patch-save
        // short-circuit" reachability note: an unedited resave never calls the writer at all.
        reloadedSheet.SetCell(new CellAddress(reloadedSheet.Id, 20, 20), new TextValue("unrelated edit"));

        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
        chartDoc.Descendants(ChartExNs + "data").Should().HaveCount(2, "both ordinary series must still round-trip");
    }

    // ---------------------------------------------------------------------------------------------
    // FAMILY-COMPLETENESS sibling: BoxAndWhisker/Histogram/Pareto get a SPECIAL carve-out in
    // ChartTypeSupport.HasCategoryStrip (a category column doesn't count against
    // GetDataSeriesCount for these three types when the series span is degenerate), so a
    // category-bearing BoxAndWhisker chart CAN still reach the real writer even from this reader's
    // synthetic-1x1-DataRange fallback -- exercising CatFormula/CatCacheXml preservation, the other
    // half of BuildChartExVerbatimSeriesFormulas this file's other tests do not cover (they have no
    // category at all). Still no header row (see the class-level reachability note: ANY header row
    // makes GetDataPointCount 0 unconditionally, with no BoxAndWhisker/Histogram/Pareto carve-out).
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void BoxAndWhiskerChart_NamedRangeSeriesWithCategory_CategoryAndValueSurviveResave()
    {
        var saved = SaveChartExCategoryOnlyTwoSeriesChart(ChartType.BoxAndWhisker);

        var customized = RewriteChartExDataToNamedRange(
            saved, dataId: "0",
            catFormula: "'Data'!rngGroups", catPoints: [("0", "G1"), ("1", "G2")],
            numFormula: "'Data'!rngDeltaX", numPoints: [("0", "100"), ("1", "-40")]);

        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Data");
        var reloadedChart = reloadedSheet.Charts.Should().ContainSingle().Subject;
        reloadedChart.VerbatimSeriesFormulas.Should().NotBeNull()
            .And.Contain(f => f.SeriesIndex == 0 && f.CatFormula == "'Data'!rngGroups" && f.ValFormula == "'Data'!rngDeltaX");

        FreeX.Core.IO.XlsxChartXmlWriter.IsSupportedXlsxChart(reloadedChart).Should().BeTrue(
            "BoxAndWhisker gets HasCategoryStrip's special carve-out, so a category-bearing " +
            "degenerate-DataRange chart of this ONE type must still reach the real writer");

        // Force the full chart-regenerating save path -- see the class-level "patch-save
        // short-circuit" reachability note: an unedited resave never calls the writer at all.
        reloadedSheet.SetCell(new CellAddress(reloadedSheet.Id, 20, 20), new TextValue("unrelated edit"));

        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
        var data0 = chartDoc.Descendants(ChartExNs + "data").Single(d => d.Attribute("id")!.Value == "0");

        data0.Element(ChartExNs + "strDim")!.Element(ChartExNs + "f")!.Value.Should().Be("'Data'!rngGroups",
            "the category named-range formula must round-trip verbatim");
        data0.Element(ChartExNs + "strDim")!.Element(ChartExNs + "lvl")!
            .Elements(ChartExNs + "pt").Select(pt => pt.Value).Should().Equal("G1", "G2");
        data0.Element(ChartExNs + "numDim")!.Element(ChartExNs + "f")!.Value.Should().Be("'Data'!rngDeltaX");
    }

    // ---------------------------------------------------------------------------------------------
    // Reader-only sibling: proves the READER half of the title fix (BuildChartExVerbatimSeriesFormulas
    // capturing the numDim <cx:nf> as TxFormula) independent of the writer-side reachability gap
    // documented in the class-level note above (a header row always makes this specific chart
    // "unsupported" for resave, so ToChartExSeriesTitleXml's own consumption of TxFormula cannot be
    // proven through a real resave yet -- see siblingLeads). Still real product entry point
    // (XlsxFileAdapter.Load), just not carried through to a resave assertion.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void BoxAndWhiskerChart_NamedRangeSeriesWithHeader_ReaderCapturesNameFormula()
    {
        var saved = SaveChartExHeaderedTwoSeriesChart(ChartType.BoxAndWhisker);
        var customized = RewriteChartExDataToNamedRange(
            saved, dataId: "0",
            catFormula: "'Data'!rngGroups", catPoints: [("0", "G2"), ("1", "G3")],
            numFormula: "'Data'!rngDeltaX", numPoints: [("0", "100"), ("1", "-40")],
            nameFormula: "'Data'!rngSeriesXName");
        customized = RewriteChartExDataToNamedRange(
            customized, dataId: "1",
            catFormula: "'Data'!rngGroups", catPoints: [("0", "G2"), ("1", "G3")],
            numFormula: "'Data'!rngDeltaY", numPoints: [("0", "50"), ("1", "-10")]);

        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedChart = reloadedWorkbook.Sheets.Single(s => s.Name == "Data").Charts.Should().ContainSingle().Subject;

        // THE BUG: pre-fix, chart.VerbatimSeriesFormulas stayed null entirely -- there was no
        // chartEx-native capture at all, so the name-formula was silently discarded from the model,
        // not merely unable to reach a resave.
        reloadedChart.VerbatimSeriesFormulas.Should().NotBeNull()
            .And.Contain(f => f.SeriesIndex == 0 && f.TxFormula == "'Data'!rngSeriesXName");
        reloadedChart.EmbeddedSeriesData.Should().NotBeNull();
        reloadedChart.EmbeddedSeriesData!.Single(d => d.SeriesIndex == 0).SeriesName.Should().Be("SeriesX",
            "the series' own cached name (from <cx:tx>/<cx:txData>/<cx:v>) must still be captured");
    }

    private static byte[] SaveThreeDColumnHeaderedTwoSeriesChart()
    {
        var workbook = new Workbook("ThreeDColumnHeaderedTwoSeries");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Group"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("SeriesX"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("SeriesY"));
        for (uint row = 2; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"G{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue((row - 1) * 32));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue((row - 1) * 8));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.ThreeDColumn,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            SeriesColumnMappings = [new ChartSeriesColumnMapping(0, 2), new ChartSeriesColumnMapping(1, 3)],
        });

        return SaveToBytes(workbook);
    }

    /// <summary>
    /// No header row, no category column -- see the class-level reachability note: this is
    /// required for the reloaded named-range chart's resave to reach the REAL regenerating writer
    /// path (IsSupportedXlsxChart) rather than being silently preserved byte-for-byte unmodified.
    /// </summary>
    private static byte[] SaveThreeDColumnValueOnlyTwoSeriesChart()
    {
        var workbook = new Workbook("ThreeDColumnValueOnlyTwoSeries");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 1; row <= 2; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 32));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 8));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.ThreeDColumn,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            FirstRowIsHeader = false,
            FirstColIsCategories = false,
            SeriesColumnMappings = [new ChartSeriesColumnMapping(0, 1), new ChartSeriesColumnMapping(1, 2)],
        });

        return SaveToBytes(workbook);
    }

    private static byte[] SaveChartExHeaderedTwoSeriesChart(ChartType chartType)
    {
        var workbook = new Workbook($"{chartType}HeaderedTwoSeries");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Group"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("SeriesX"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("SeriesY"));
        for (uint row = 2; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"G{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue((row - 1) * 32));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue((row - 1) * 8));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = chartType,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            SeriesColumnMappings = [new ChartSeriesColumnMapping(0, 2), new ChartSeriesColumnMapping(1, 3)],
        });

        return SaveToBytes(workbook);
    }

    /// <summary>
    /// No header row, no category column -- see the class-level reachability note.
    /// </summary>
    private static byte[] SaveChartExValueOnlyTwoSeriesChart(ChartType chartType)
    {
        var workbook = new Workbook($"{chartType}ValueOnlyTwoSeries");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 1; row <= 2; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 32));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 8));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = chartType,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            FirstRowIsHeader = false,
            FirstColIsCategories = false,
            SeriesColumnMappings = [new ChartSeriesColumnMapping(0, 1), new ChartSeriesColumnMapping(1, 2)],
        });

        return SaveToBytes(workbook);
    }

    /// <summary>
    /// Category column present, no header row -- see the class-level reachability note and
    /// BoxAndWhiskerChart_NamedRangeSeriesWithCategory_CategoryAndValueSurviveResave's own comment
    /// for why this specific combination (category WITH a degenerate DataRange) still reaches the
    /// real writer only for BoxAndWhisker/Histogram/Pareto.
    /// </summary>
    private static byte[] SaveChartExCategoryOnlyTwoSeriesChart(ChartType chartType)
    {
        var workbook = new Workbook($"{chartType}CategoryOnlyTwoSeries");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 1; row <= 2; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"G{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 32));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = chartType,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            FirstRowIsHeader = false,
            FirstColIsCategories = true,
            SeriesColumnMappings = [new ChartSeriesColumnMapping(0, 2)],
        });

        return SaveToBytes(workbook);
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
    /// Rewrites the given classic &lt;c:ser&gt;'s &lt;c:val&gt;&lt;c:numRef&gt; to a named-range
    /// formula with real numCache values — mimicking what real Excel writes for a chart bound to an
    /// OFFSET-based dynamic named range. The fixture has no &lt;c:cat&gt;/&lt;c:tx&gt; at all (see
    /// SaveThreeDColumnValueOnlyTwoSeriesChart), so there is nothing else to rewrite.
    /// </summary>
    private static byte[] RewriteSeriesValToNamedRange(
        byte[] package,
        string seriesIdx,
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

    /// <summary>
    /// Rewrites the given &lt;cx:data id="dataId"&gt;'s &lt;cx:numDim&gt; (leaving @type exactly as
    /// the writer produced it) &lt;cx:f&gt; formula to a named-range formula, and injects a real
    /// &lt;cx:lvl&gt;/&lt;cx:pt&gt; cache. The fixture has no &lt;cx:strDim&gt; at all (see
    /// SaveChartExValueOnlyTwoSeriesChart), so there is nothing else to rewrite.
    /// </summary>
    private static byte[] RewriteChartExNumDimToNamedRange(
        byte[] package,
        string dataId,
        string numFormula,
        (string Idx, string Value)[] numPoints)
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

            var data = chartDoc.Descendants(ChartExNs + "data").Single(d => d.Attribute("id")!.Value == dataId);
            var numDim = data.Elements(ChartExNs + "numDim").Single();
            RewriteDimension(numDim, numFormula, numPoints, nameFormula: null);

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/charts/chart1.xml", CompressionLevel.Optimal);
            using var newEntryStream = newEntry.Open();
            chartDoc.Save(newEntryStream);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Rewrites the given &lt;cx:data id="dataId"&gt;'s &lt;cx:strDim type="cat"&gt; and
    /// &lt;cx:numDim&gt; (leaving @type exactly as the writer produced it) &lt;cx:f&gt; formulas to
    /// named-range formulas, and injects a real &lt;cx:lvl&gt;/&lt;cx:pt&gt; cache under each.
    /// </summary>
    private static byte[] RewriteChartExDataToNamedRange(
        byte[] package,
        string dataId,
        string catFormula,
        (string Idx, string Value)[] catPoints,
        string numFormula,
        (string Idx, string Value)[] numPoints,
        string? nameFormula = null)
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

            var data = chartDoc.Descendants(ChartExNs + "data").Single(d => d.Attribute("id")!.Value == dataId);

            var strDim = data.Elements(ChartExNs + "strDim").Single(e => e.Attribute("type")?.Value == "cat");
            RewriteDimension(strDim, catFormula, catPoints, nameFormula: null);

            var numDim = data.Elements(ChartExNs + "numDim").Single();
            RewriteDimension(numDim, numFormula, numPoints, nameFormula);

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/charts/chart1.xml", CompressionLevel.Optimal);
            using var newEntryStream = newEntry.Open();
            chartDoc.Save(newEntryStream);
        }

        return stream.ToArray();
    }

    private static void RewriteDimension(XElement dimension, string formula, (string Idx, string Value)[] points, string? nameFormula)
    {
        var f = dimension.Element(ChartExNs + "f");
        f.Should().NotBeNull("the fixture chart must already emit a <cx:f> formula to rewrite");
        f!.Value = formula;

        // By default drop any writer-emitted <cx:nf> (name-formula) -- irrelevant to most rewrites
        // here. When nameFormula is given
        // (BoxAndWhiskerChart_NamedRangeSeriesWithHeader_ReaderCapturesNameFormula), rewrite <cx:nf>'s
        // text to another UNRESOLVABLE named-range formula instead of removing it, so
        // chart.FirstRowIsHeader still comes back true (nf stays non-blank) on reload without also
        // resolving as a real cell range (which would otherwise divert the whole chart onto the
        // ordinary, non-fallback read path).
        var existingNf = dimension.Elements(ChartExNs + "nf").FirstOrDefault();
        if (nameFormula is null)
            existingNf?.Remove();
        else if (existingNf is not null)
            existingNf.Value = nameFormula;

        dimension.Add(new XElement(ChartExNs + "lvl",
            new XAttribute("ptCount", points.Length.ToString()),
            points.Select(p => new XElement(ChartExNs + "pt",
                new XAttribute("idx", p.Idx), p.Value))));
    }
}
