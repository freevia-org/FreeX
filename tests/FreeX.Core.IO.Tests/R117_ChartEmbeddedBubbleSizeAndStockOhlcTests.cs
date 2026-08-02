using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R117-io-chart-embedded-bubble-size-1/R117-presentation-chart-stock-ohlc-1: r110-r112 taught the
/// readers to fall back to a series' embedded &lt;c:numCache&gt;/&lt;c:strCache&gt; when its formula is
/// an unresolvable named range (the OFFSET-based "auto-expanding chart" pattern), but
/// <see cref="ChartEmbeddedSeriesData"/> only ever carried <c>Categories</c>/<c>Values</c> -- no
/// per-point bubble radius, no explicit High/Low/Open/Close. This file proves:
/// <list type="bullet">
///   <item>Bubble: the reader now captures the series' &lt;c:bubbleSize&gt; numCache into the new
///   <see cref="ChartEmbeddedSeriesData.SizeValues"/> field (a real reader/model gap; the
///   fail-before/pass-after evidence is below).</item>
///   <item>Stock: the reader ALREADY captured each of Open/High/Low/Close correctly -- they are
///   separate classic &lt;c:ser&gt; elements, so <c>TryReadEmbeddedSeriesData</c> already returns one
///   list entry per dimension, in that fixed document order. There is no reader-level bug here (this
///   test documents/confirms that, it is not a regression test); the actual Stock bug -- and its
///   fix -- lives entirely in the two renderer-side consumers, which is where the fail-before/
///   pass-after Stock tests live (<c>ChartRendererTests</c> in FreeX.App.UI.Tests and
///   <c>ChartLayoutRequestBuilderTests</c>/<c>StockLayoutTests</c> in FreeX.App.Presentation.Tests).</item>
/// </list>
/// ROUND-TRIP FIXTURE RULE: every fixture below is produced by the product's OWN writer (via
/// <see cref="XlsxFileAdapter"/>) and then surgically rewritten to swap the writer's plain cell-range
/// series formulas for named-range formulas plus real embedded caches -- mirroring exactly what real
/// Excel emits for a workbook using OFFSET-based dynamic named ranges (see
/// R112_ChartTypeSupportEmbeddedFallbackCountTests for the established pattern).
/// </summary>
public sealed class R117_ChartEmbeddedBubbleSizeAndStockOhlcTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    // ---------------------------------------------------------------------------------------------
    // THE FIX: a Bubble chart whose xVal/yVal/bubbleSize formulas are all unresolvable named ranges
    // must still recover its per-point bubble sizes from the embedded bubbleSize numCache.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void BubbleChart_NamedRangeXYAndSizeFormulas_CapturesSizeValuesFromEmbeddedCache()
    {
        var saved = SaveBubbleChartWorkbook();

        var customized = RewriteSeriesNumRefToNamedRange(
            saved, seriesIdx: "0", containerName: "xVal", formula: "'Sheet1'!rngRevenue",
            points: [("0", "100"), ("1", "180"), ("2", "260")]);
        customized = RewriteSeriesNumRefToNamedRange(
            customized, seriesIdx: "0", containerName: "yVal", formula: "'Sheet1'!rngMargin",
            points: [("0", "12"), ("1", "18"), ("2", "24")]);
        customized = RewriteSeriesNumRefToNamedRange(
            customized, seriesIdx: "0", containerName: "bubbleSize", formula: "'Sheet1'!rngMarketSize",
            points: [("0", "40"), ("1", "65"), ("2", "90")]);

        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Sheet1");
        var reloadedChart = reloadedSheet.Charts.Should().ContainSingle().Subject;

        reloadedChart.Type.Should().Be(ChartType.Bubble);
        reloadedChart.EmbeddedSeriesData.Should().NotBeNull().And.HaveCount(1,
            "the single bubble series' xVal/yVal named-range formulas must trigger the embedded-cache fallback");
        var series = reloadedChart.EmbeddedSeriesData!.Single();
        series.Values.Should().Equal(
            new double?[] { 12.0, 18.0, 24.0 },
            "yVal's numCache must still populate Values as before this fix");

        // THE FIX: SizeValues must carry the bubbleSize numCache -- pre-fix this was always null
        // (ChartEmbeddedSeriesData had no field to carry it, and PieBubble.cs never asked the reader
        // for one), so BuildEmbeddedCellLookup/BuildFromEmbeddedData had no size data to recover and
        // every fallback-loaded bubble rendered at the uniform default/minimum radius.
        series.SizeValues.Should().NotBeNull(
            "THE BUG: SizeValues was always null pre-fix (ChartEmbeddedSeriesData had no field for it)");
        series.SizeValues.Should().Equal(
            new double?[] { 40.0, 65.0, 90.0 },
            "the bubbleSize numCache's real cached values must be recovered exactly");
    }

    // ---------------------------------------------------------------------------------------------
    // Sibling no-regression: an ordinary (non-named-range) bubble chart -- no EmbeddedSeriesData at
    // all -- is unaffected.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void BubbleChart_OrdinaryCellRangeChart_UnaffectedByEmbeddedFallbackFix()
    {
        var saved = SaveBubbleChartWorkbook();
        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(saved, writable: false));
        var reloadedSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Sheet1");
        var reloadedChart = reloadedSheet.Charts.Should().ContainSingle().Subject;

        reloadedChart.EmbeddedSeriesData.Should().BeNull("no series formula is a named range");
        reloadedChart.DataRange.Start.Should().NotBe(reloadedChart.DataRange.End,
            "an ordinary chart keeps its real, non-degenerate DataRange");
    }

    // ---------------------------------------------------------------------------------------------
    // Documents (does not "fix") that the reader already captures a Stock chart's Open/High/Low/
    // Close dimensions correctly as separate embedded-series-list entries, in document order, when
    // every series' val/cat formula is an unresolvable named range -- the bug for Stock is entirely
    // in the two renderer-side consumers (see ChartRendererTests/ChartLayoutRequestBuilderTests).
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void StockChart_NamedRangeOhlcFormulas_ReaderAlreadyCapturesEachDimensionAsSeparateSeries()
    {
        var saved = SaveStockChartWorkbook();

        // Every series' <c:cat> must ALSO become an unresolvable named range -- TryReadLineLikeChart
        // (which the Stock reader reuses) only enters the embedded-cache fallback when ranges.Count
        // stays 0, i.e. when NEITHER the val NOR the cat formula of any series resolves to a direct
        // cell range. Leaving <c:cat> as the writer's ordinary "Data!$A$2:$A$4" would let it resolve
        // and short-circuit straight to the live-cell path -- exactly what real Excel's OFFSET-based
        // "auto-expanding chart" pattern does NOT do (both axes are normally bound to the same
        // dynamic named range).
        string[] seriesIdxs = ["0", "1", "2", "3"];
        var customized = saved;
        foreach (var idx in seriesIdxs)
        {
            customized = RewriteSeriesStrRefToNamedRange(
                customized, seriesIdx: idx, containerName: "cat", formula: "'Data'!rngDate",
                points: [("0", "2026-01-02"), ("1", "2026-01-05"), ("2", "2026-01-06")]);
        }

        customized = RewriteSeriesNumRefToNamedRange(
            customized, seriesIdx: "0", containerName: "val", formula: "'Data'!rngOpen",
            points: [("0", "101"), ("1", "121"), ("2", "139")]);
        customized = RewriteSeriesNumRefToNamedRange(
            customized, seriesIdx: "1", containerName: "val", formula: "'Data'!rngHigh",
            points: [("0", "108"), ("1", "128"), ("2", "145")]);
        customized = RewriteSeriesNumRefToNamedRange(
            customized, seriesIdx: "2", containerName: "val", formula: "'Data'!rngLow",
            points: [("0", "98"), ("1", "118"), ("2", "135")]);
        customized = RewriteSeriesNumRefToNamedRange(
            customized, seriesIdx: "3", containerName: "val", formula: "'Data'!rngClose",
            points: [("0", "106"), ("1", "126"), ("2", "142")]);

        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Data");
        var reloadedChart = reloadedSheet.Charts.Should().ContainSingle().Subject;

        reloadedChart.Type.Should().Be(ChartType.Stock);
        reloadedChart.EmbeddedSeriesData.Should().NotBeNull().And.HaveCount(4,
            "all four OHLC named-range series formulas must trigger the embedded-cache fallback");
        var series = reloadedChart.EmbeddedSeriesData!;
        series[0].Values.Should().Equal(new double?[] { 101.0, 121.0, 139.0 }, "Open is the first <c:ser> in document order");
        series[1].Values.Should().Equal(new double?[] { 108.0, 128.0, 145.0 }, "High is the second <c:ser> in document order");
        series[2].Values.Should().Equal(new double?[] { 98.0, 118.0, 135.0 }, "Low is the third <c:ser> in document order");
        series[3].Values.Should().Equal(new double?[] { 106.0, 126.0, 142.0 }, "Close is the fourth <c:ser> in document order");
    }

    private static byte[] SaveBubbleChartWorkbook()
    {
        var workbook = new Workbook("BubbleEmbeddedFallback");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Margin"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Market Size"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(180));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(260));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(12));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(18));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(24));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(40));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(65));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(90));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Bubble,
            Title = "Bubble",
            FirstColIsCategories = false,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3)),
        });

        return SaveToBytes(workbook);
    }

    private static byte[] SaveStockChartWorkbook()
    {
        var workbook = new Workbook("StockEmbeddedFallback");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Date"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("High"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Low"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("Close"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("2026-01-02"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("2026-01-05"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("2026-01-06"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(101));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(121));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(139));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(108));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(128));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(145));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(98));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new NumberValue(118));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(135));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new NumberValue(106));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 5), new NumberValue(126));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 5), new NumberValue(142));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.OpenHighLowClose,
            Title = "Stock",
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 5)),
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
    /// Rewrites the given classic &lt;c:ser&gt;'s named numeric-reference container (e.g.
    /// "val"/"xVal"/"yVal"/"bubbleSize") to a named-range formula with a real numCache, mirroring
    /// what real Excel writes for a chart bound to an OFFSET-based dynamic named range. Generalizes
    /// R112_ChartTypeSupportEmbeddedFallbackCountTests's RewriteSeriesValToNamedRange to an arbitrary
    /// container name so it also covers Bubble's xVal/yVal/bubbleSize and Stock's per-dimension val.
    /// </summary>
    private static byte[] RewriteSeriesNumRefToNamedRange(
        byte[] package,
        string seriesIdx,
        string containerName,
        string formula,
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

            var numRef = series.Element(ChartNs + containerName)!.Element(ChartNs + "numRef");
            numRef.Should().NotBeNull($"the fixture chart must already emit <c:{containerName}><c:numRef> to rewrite");
            numRef!.RemoveNodes();
            numRef.Add(new XElement(ChartNs + "f", formula));
            var numCache = new XElement(ChartNs + "numCache",
                new XElement(ChartNs + "formatCode", "General"),
                new XElement(ChartNs + "ptCount", new XAttribute("val", points.Length)));
            foreach (var (idx, value) in points)
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
    /// Same as <see cref="RewriteSeriesNumRefToNamedRange"/> but for a string-reference container
    /// (e.g. "cat"), mirroring R110_ChartEmbeddedFastPathLineAreaPieScatterTests's cat rewrite.
    /// </summary>
    private static byte[] RewriteSeriesStrRefToNamedRange(
        byte[] package,
        string seriesIdx,
        string containerName,
        string formula,
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

            var strRef = series.Element(ChartNs + containerName)!.Element(ChartNs + "strRef");
            strRef.Should().NotBeNull($"the fixture chart must already emit <c:{containerName}><c:strRef> to rewrite");
            strRef!.RemoveNodes();
            strRef.Add(new XElement(ChartNs + "f", formula));
            var strCache = new XElement(ChartNs + "strCache",
                new XElement(ChartNs + "ptCount", new XAttribute("val", points.Length)));
            foreach (var (idx, value) in points)
                strCache.Add(new XElement(ChartNs + "pt", new XAttribute("idx", idx), new XElement(ChartNs + "v", value)));
            strRef.Add(strCache);

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/charts/chart1.xml", CompressionLevel.Optimal);
            using var newEntryStream = newEntry.Open();
            chartDoc.Save(newEntryStream);
        }

        return stream.ToArray();
    }
}
