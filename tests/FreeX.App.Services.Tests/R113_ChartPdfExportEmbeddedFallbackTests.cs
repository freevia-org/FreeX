using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Free.Shared.Pdf;
using Xunit;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R113-services-chart-pdf-embedded-fallback-1: r110-r112 taught the XLSX readers to fall back to a
/// chart's embedded <c>&lt;c:numCache&gt;</c>/<c>&lt;c:strCache&gt;</c> values when a series formula is
/// an unresolvable named range (the OFFSET auto-expanding-chart pattern), storing the recovered data in
/// <see cref="ChartModel.EmbeddedSeriesData"/> and (per r112) making
/// <see cref="ChartTypeSupport"/>'s series/point counts consult it instead of the synthetic 1x1
/// placeholder <see cref="ChartModel.DataRange"/> those readers set. The data survives load and
/// (per r112) is reported correctly by the counters, but the portable chart layout path --
/// <c>FreeX.App.Presentation.Charts.ChartLayoutRequestBuilder.TryBuild</c>, which feeds both
/// <c>ChartLayoutEngine</c> and this class's <see cref="WorkbookPdfContentBuilder"/> -- had ZERO
/// EmbeddedSeriesData awareness: for a fallback-loaded chart with a header row, TryBuild's
/// <c>dataStartRow &gt; endRow</c> guard against the placeholder's degenerate 1x1 range returned null
/// outright, so the chart's plot area was rendered PDF as an empty box instead of the (correctly
/// preserved) data. This test drives the real PDF export entry point end-to-end
/// (<see cref="WorkbookExportPrintPlanner"/> -&gt; <see cref="PortablePdfExportPlanner"/> -&gt;
/// <see cref="WorkbookPdfContentBuilder"/>), matching the shape of
/// <c>R111_PdfHeaderFooterScaleWithDocumentTests</c>, and asserts on the actual drawn
/// <see cref="PdfFillRect"/> bar ops.
///
/// ROUND-TRIP FIXTURE RULE: the fixture is produced by the product's own writer
/// (<see cref="XlsxFileAdapter"/>) and then surgically rewritten to swap the writer's plain cell-range
/// series formulas for named-range formulas plus embedded caches -- mirroring exactly what real Excel
/// emits for a workbook using OFFSET-based dynamic named ranges, per the pattern established in
/// <c>tests/FreeX.Core.IO.Tests/R112_ChartTypeSupportEmbeddedFallbackCountTests.cs</c>.
/// </summary>
public sealed class R113_ChartPdfExportEmbeddedFallbackTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    // ---------------------------------------------------------------------------------------------
    // THE FIX: a fallback-loaded Column chart with a header row must render its embedded series data
    // as real bar fills in the exported PDF, not an empty plot area.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void BuildWithPageSetup_FallbackLoadedColumnChartWithHeaderRow_RendersEmbeddedSeriesAsBars()
    {
        var saved = SaveColumnHeaderedTwoSeriesChart();

        // Rewrite both series' <c:tx> (header) and <c:val> to unresolvable named-range formulas with
        // real numCache/strCache values, exactly mirroring what real Excel emits for a chart bound to
        // an OFFSET-based dynamic named range. This forces every val/tx formula in the chart to fail
        // TryParseFormulaRange, driving the reader's embedded-data fallback branch.
        var customized = RewriteSeriesTxToNamedRange(saved, seriesIdx: "0", txFormula: "'Data'!rngNameX", title: "SeriesX");
        customized = RewriteSeriesValToNamedRange(customized, seriesIdx: "0", valFormula: "'Data'!rngValX", valPoints: [("0", "64"), ("1", "36")]);
        customized = RewriteSeriesTxToNamedRange(customized, seriesIdx: "1", txFormula: "'Data'!rngNameY", title: "SeriesY");
        customized = RewriteSeriesValToNamedRange(customized, seriesIdx: "1", valFormula: "'Data'!rngValY", valPoints: [("0", "80"), ("1", "20")]);

        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Data");
        var reloadedChart = reloadedSheet.Charts.Should().ContainSingle().Subject;

        // Sanity: confirm the reader's r110-r112 fallback fired the way the report claims -- the
        // synthetic 1x1 placeholder DataRange with a header row is exactly the shape that made
        // ChartLayoutRequestBuilder.TryBuild's dataStartRow > endRow guard return null pre-fix.
        reloadedChart.EmbeddedSeriesData.Should().NotBeNull().And.HaveCount(2);
        reloadedChart.DataRange.Start.Should().Be(reloadedChart.DataRange.End,
            "the fallback path sets a synthetic 1x1 placeholder DataRange -- the real data lives in EmbeddedSeriesData");
        reloadedChart.FirstRowIsHeader.Should().BeTrue();

        var page = BuildPdfPage(reloadedWorkbook, reloadedSheet);

        var bars = ChartBarFills(page);

        // THE BUG, exact pre-fix behavior (captured via the cp-backup technique -- see report): with
        // DataRange.Start == DataRange.End (row/col span both zero) and FirstRowIsHeader true,
        // dataStartRow (startRow + 1) > endRow (startRow), so TryBuild returned null and
        // AddChartPlotOps emitted ZERO ops for the chart's plot area -- an empty box in the PDF.
        bars.Should().HaveCount(4,
            "2 series x 2 cached points = 4 bar fills must be drawn from EmbeddedSeriesData -- " +
            "THE BUG rendered zero bars because TryBuild returned null for the synthetic 1x1 placeholder range");
    }

    // ---------------------------------------------------------------------------------------------
    // Sibling no-regression: an ordinary cell-range-backed Column chart (no EmbeddedSeriesData) must
    // still export identically -- same bar count, unaffected by the embedded-data fallback path.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void BuildWithPageSetup_OrdinaryCellRangeColumnChart_UnaffectedByEmbeddedFallbackFix()
    {
        var saved = SaveColumnHeaderedTwoSeriesChart();
        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(saved, writable: false));
        var reloadedSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Data");
        var reloadedChart = reloadedSheet.Charts.Should().ContainSingle().Subject;

        reloadedChart.EmbeddedSeriesData.Should().BeNull("no series formula is a named range");
        reloadedChart.DataRange.Start.Should().NotBe(reloadedChart.DataRange.End,
            "an ordinary chart keeps its real, non-degenerate DataRange");

        var page = BuildPdfPage(reloadedWorkbook, reloadedSheet);
        var bars = ChartBarFills(page);

        bars.Should().HaveCount(4, "2 series x 2 data rows = 4 bar fills, exactly as before this fix");
    }

    /// <summary>
    /// The chart's own background (AddFillRect in AddVectorDrawingOps, always solid white here since
    /// the fixture chart has no explicit fill) is emitted as a <see cref="PdfFillRect"/> before the
    /// bar fills from <c>AddChartBarOps</c> -- excluding pure white isolates the actual bar ops.
    /// </summary>
    private static List<PdfFillRect> ChartBarFills(PdfContentPage page) =>
        page.Ops.OfType<PdfFillRect>()
            .Where(r => r.Width > 0 && r.Height > 0 && r.Color is not { R: 0xFF, G: 0xFF, B: 0xFF })
            .ToList();

    private static PdfContentPage BuildPdfPage(Workbook workbook, Sheet sheet)
    {
        var sheetIndex = workbook.Sheets.ToList().IndexOf(sheet);
        var intent = new WorkbookExportPrintIntent(
            WorkbookExportPrintScope.ActiveSheet,
            WorkbookExportPrintOutputKind.Pdf,
            ActiveSheetIndex: sheetIndex);

        var exportPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(workbook, intent);
        exportPlan.IsReady.Should().BeTrue(exportPlan.StatusText);

        var pdfPlan = PortablePdfExportPlanner.CreatePlan(exportPlan);
        pdfPlan.IsReady.Should().BeTrue(pdfPlan.StatusText);

        var doc = WorkbookPdfContentBuilder.BuildWithPageSetup(workbook, pdfPlan);
        doc.Pages.Should().NotBeEmpty();
        return doc.Pages[0];
    }

    /// <summary>Header row present (so FirstRowIsHeader is true on reload), two value series, no category column.</summary>
    private static byte[] SaveColumnHeaderedTwoSeriesChart()
    {
        var workbook = new Workbook("ColumnHeaderedTwoSeries");
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
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = false,
            SeriesColumnMappings = [new ChartSeriesColumnMapping(0, 1), new ChartSeriesColumnMapping(1, 2)],
        });

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
