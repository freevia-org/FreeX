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
/// R112-charttypesupport-embedded-fallback-1: a chart preserved by the named-range embedded-cache
/// fallback (the seven r108-fixed readers in <c>XlsxChartPartReader.*</c> plus the eighth reader,
/// <c>TryReadDeferredAdvancedChart</c> in <c>XlsxChartPartReader.Deferred.cs</c>) carries a synthetic
/// 1x1 placeholder <see cref="ChartModel.DataRange"/> — its real series/point data lives in
/// <see cref="ChartModel.EmbeddedSeriesData"/> instead. <see cref="ChartTypeSupport.GetDataSeriesCount"/>
/// and <see cref="ChartTypeSupport.GetDataPointCount"/> derived PURELY from
/// <c>chart.DataRange</c>'s row/column span, never consulting <c>EmbeddedSeriesData</c>, so for the
/// synthetic 1x1 placeholder they returned a degenerate value regardless of how many series/points the
/// chart's own embedded cache actually carries. This was flagged (but explicitly deferred as a
/// follow-up, not fixed) by
/// <see cref="R112_ChartVerbatimSeriesFormulasNamedRangeTests"/>'s class-level "IsSupportedXlsxChart's
/// degenerate-DataRange gate" note — every fixture in that file had to avoid a header row specifically
/// to dodge this bug.
///
/// The blast radius is large because <c>XlsxChartXmlWriter.IsSupportedXlsxChart</c> gates a chart's
/// entire sheet on <c>GetDataSeriesCount(chart) &gt; 0 &amp;&amp; GetDataPointCount(chart) &gt; 0</c>: a
/// fallback-loaded chart with a header row (extremely common in real Excel workbooks) got
/// <c>GetDataPointCount == 0</c> UNCONDITIONALLY, so <c>IsSupportedXlsxChart</c> came back false and
/// the writer's whole chart-regenerating path was skipped for the workbook — even though the chart's
/// data was fully preserved in <c>EmbeddedSeriesData</c>/<c>VerbatimSeriesFormulas</c> by r110/r111/r112.
/// A three-round effort to stop "silently dropping the chart series" would have delivered nothing
/// visible, because the very same degenerate DataRange that fed the fallback also starved every
/// DataRange-derived consumer of a real series/point count.
///
/// ROUND-TRIP FIXTURE RULE: the fixture below is produced by the product's OWN writer (via
/// <see cref="XlsxFileAdapter"/>) and then surgically rewritten to swap the writer's plain cell-range
/// series-name/value formulas for named-range formulas plus embedded caches — mirroring exactly what
/// real Excel emits for a workbook using OFFSET-based dynamic named ranges, per the r112 fixture rule
/// (do not hand-author chart XML from scratch).
/// </summary>
public sealed class R112_ChartTypeSupportEmbeddedFallbackCountTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    // ---------------------------------------------------------------------------------------------
    // THE FIX: a fallback-loaded chart WITH a header row (the exact shape the sibling verbatim-
    // formula test file's fixtures deliberately avoided) must report its REAL series/point counts —
    // not the degenerate counts derived from the synthetic 1x1 placeholder DataRange — and must be
    // treated as a supported (writable) chart again.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void FallbackLoadedChart_WithHeaderRow_ReportsRealSeriesAndPointCountsAndIsSupported()
    {
        var saved = SaveThreeDColumnHeaderedNoCategoryTwoSeriesChart();

        // Rewrite BOTH series' <c:tx> (title/header) AND <c:val> to unresolvable named-range
        // formulas with real caches, exactly mirroring what real Excel emits for an OFFSET-based
        // dynamic named range bound to both a series' name and its values. This drives EVERY
        // series/tx/val/cat formula in the chart to fail TryParseFormulaRange, forcing
        // TryReadDeferredAdvancedChart's numCache/strCache embedded-data fallback branch (the same
        // branch R112_ChartVerbatimSeriesFormulasNamedRangeTests exercises without a header row).
        var customized = RewriteSeriesTxToNamedRange(saved, seriesIdx: "0", txFormula: "'Data'!rngNameX", title: "SeriesX");
        customized = RewriteSeriesValToNamedRange(customized, seriesIdx: "0", valFormula: "'Data'!rngValX", valPoints: [("0", "64"), ("1", "36")]);
        customized = RewriteSeriesTxToNamedRange(customized, seriesIdx: "1", txFormula: "'Data'!rngNameY", title: "SeriesY");
        customized = RewriteSeriesValToNamedRange(customized, seriesIdx: "1", valFormula: "'Data'!rngValY", valPoints: [("0", "80"), ("1", "20")]);

        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Data");
        var reloadedChart = reloadedSheet.Charts.Should().ContainSingle().Subject;

        reloadedChart.EmbeddedSeriesData.Should().NotBeNull().And.HaveCount(2,
            "both named-range series' caches must be captured by the reader's embedded-data fallback");
        reloadedChart.DataRange.Start.Should().Be(reloadedChart.DataRange.End,
            "the fallback path sets a synthetic 1x1 placeholder DataRange (see TryReadDeferredAdvancedChart) " +
            "purely to carry the chart's sheet id -- the real data lives in EmbeddedSeriesData");
        reloadedChart.FirstRowIsHeader.Should().BeTrue(
            "the fixture's <c:tx> formulas (even unresolved) must still be detected as a title/header row");

        // THE BUG, exact pre-fix values (captured via the cp-backup technique -- see report):
        // GetDataSeriesCount degenerated to 1 (a single 1x1 strip minus zero skipped-category
        // strips), NOT the real count of 2 named-range series; GetDataPointCount degenerated to 0
        // UNCONDITIONALLY because chart.FirstRowIsHeader is true and the placeholder's point span is
        // already zero (0 + 1 <= 1 skipped-header-row strip).
        ChartTypeSupport.GetDataSeriesCount(reloadedChart).Should().Be(2,
            "the chart's own EmbeddedSeriesData carries exactly 2 series -- THE BUG reported 1, derived " +
            "purely from the synthetic 1x1 placeholder DataRange's degenerate column span");
        ChartTypeSupport.GetDataPointCount(reloadedChart).Should().Be(2,
            "each embedded series has 2 cached values -- THE BUG UNCONDITIONALLY reported 0 whenever " +
            "chart.FirstRowIsHeader is true, because the placeholder DataRange's row span is always zero");

        // The blast radius: IsSupportedXlsxChart gates the ENTIRE writer chart-regeneration path for
        // the workbook. Pre-fix, a fallback-loaded chart with a header row was UNCONDITIONALLY
        // "unsupported" (GetDataPointCount == 0), so a three-round effort to preserve its series in
        // EmbeddedSeriesData/VerbatimSeriesFormulas delivered nothing: the writer would never even
        // attempt to re-derive real <c:ser> XML from that preserved data on the next save.
        XlsxChartXmlWriter.IsSupportedXlsxChart(reloadedChart).Should().BeTrue(
            "a fallback-loaded chart with real cached series/point data must be treated as supported " +
            "(writable) again, not silently skipped by the whole chart-regenerating save path");
    }

    // ---------------------------------------------------------------------------------------------
    // Sibling no-regression: an ordinary (non-named-range) chart -- no EmbeddedSeriesData at all --
    // must report EXACTLY the same counts as before this fix (derived from the real DataRange span).
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void OrdinaryCellRangeChart_UnaffectedByEmbeddedFallbackFix()
    {
        var saved = SaveThreeDColumnHeaderedNoCategoryTwoSeriesChart();
        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(saved, writable: false));
        var reloadedSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Data");
        var reloadedChart = reloadedSheet.Charts.Should().ContainSingle().Subject;

        reloadedChart.EmbeddedSeriesData.Should().BeNull("no series formula is a named range");
        reloadedChart.DataRange.Start.Should().NotBe(reloadedChart.DataRange.End,
            "an ordinary chart keeps its real, non-degenerate DataRange");

        ChartTypeSupport.GetDataSeriesCount(reloadedChart).Should().Be(2,
            "two ordinary value columns -- computed the same way as before this fix (from DataRange span)");
        ChartTypeSupport.GetDataPointCount(reloadedChart).Should().Be(2,
            "two data rows below the header -- computed the same way as before this fix (from DataRange span)");
        XlsxChartXmlWriter.IsSupportedXlsxChart(reloadedChart).Should().BeTrue();
    }

    /// <summary>Header row present (so FirstRowIsHeader is true on reload), no category column.</summary>
    private static byte[] SaveThreeDColumnHeaderedNoCategoryTwoSeriesChart()
    {
        var workbook = new Workbook("ThreeDColumnHeaderedNoCategoryTwoSeries");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("SeriesX"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("SeriesY"));
        for (uint row = 2; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue((row - 1) * 32));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue((row - 1) * 8));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.ThreeDColumn,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = false,
            SeriesColumnMappings = [new ChartSeriesColumnMapping(0, 1), new ChartSeriesColumnMapping(1, 2)],
        });

        return SaveToBytes(workbook);
    }

    private static byte[] SaveToBytes(Workbook workbook)
    {
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        return stream.ToArray();
    }

    /// <summary>
    /// Rewrites the given classic &lt;c:ser&gt;'s &lt;c:val&gt;&lt;c:numRef&gt; to a named-range
    /// formula with real numCache values, mirroring what real Excel writes for a chart bound to an
    /// OFFSET-based dynamic named range.
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
    /// Rewrites the given classic &lt;c:ser&gt;'s &lt;c:tx&gt;&lt;c:strRef&gt; (the series
    /// name/header-cell reference) to a named-range formula with a real strCache single value,
    /// mirroring what real Excel writes when a series' name is bound to a named-range cell.
    /// </summary>
    private static byte[] RewriteSeriesTxToNamedRange(
        byte[] package,
        string seriesIdx,
        string txFormula,
        string title)
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

            var strRef = series.Element(ChartNs + "tx")!.Element(ChartNs + "strRef");
            strRef.Should().NotBeNull("the fixture chart must already emit <c:tx><c:strRef> (FirstRowIsHeader) to rewrite");
            strRef!.RemoveNodes();
            strRef.Add(new XElement(ChartNs + "f", txFormula));
            strRef.Add(new XElement(ChartNs + "strCache",
                new XElement(ChartNs + "ptCount", new XAttribute("val", 1)),
                new XElement(ChartNs + "pt", new XAttribute("idx", 0), new XElement(ChartNs + "v", title))));

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/charts/chart1.xml", CompressionLevel.Optimal);
            using var newEntryStream = newEntry.Open();
            chartDoc.Save(newEntryStream);
        }

        return stream.ToArray();
    }
}
