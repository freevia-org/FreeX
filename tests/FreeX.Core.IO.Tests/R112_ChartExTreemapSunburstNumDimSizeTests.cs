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
/// R112-io-chartex-numdim-size-1: r111's <c>TryReadChartExEmbeddedSeriesData</c> (the chartEx
/// "eighth reader" fallback for named-range series formulas -- see
/// <see cref="R111_ChartDeferredAdvancedEmbeddedFastPathTests"/>) located each series' cache via
/// <c>FindChartExDimension(data, "numDim", "val")</c>, i.e. it only ever accepted a
/// &lt;cx:numDim&gt; whose @type is exactly "val". But this codebase's own writer
/// (<c>ToChartExNumericDimensionType</c> in XlsxChartXmlWriter.ChartEx.cs) emits @type="size" for
/// Treemap and Sunburst -- the chartEx area-size dimension for hierarchical charts -- and @type="val"
/// for every other chartEx family member (Histogram, Pareto, BoxAndWhisker, Waterfall, Funnel).
/// Since the reader never looked for "size", a Treemap/Sunburst chart whose series formula is an
/// unresolvable named range (the exact scenario the r111 fallback exists for) always got an empty
/// numeric cache, so <c>result.Any(s => s.Values.Count > 0)</c> was false and the whole chart was
/// silently dropped on load -- exactly the defect r111's commit message claimed was fixed for
/// Treemap and Sunburst among others.
///
/// Every fixture below is produced by the product's OWN writer (<see cref="XlsxChartXmlWriter"/>
/// via <see cref="XlsxFileAdapter.Save"/>) and then surgically rewritten to swap the writer's plain
/// cell-range formula for a named-range formula plus an embedded cx:lvl/cx:pt cache -- mirroring
/// exactly what real Excel emits for an OFFSET-based dynamic named range. The @type attribute the
/// writer itself chose (e.g. "size" for Treemap/Sunburst) is left untouched, so this proves the
/// reader must accept the type the writer (and real Excel) actually produces, never a hand-picked
/// one. This is deliberate: r111's own new test hand-authored &lt;cx:numDim type="val"&gt; for a
/// Treemap series, which the product's writer would never emit, and that mismatch is exactly why
/// the bug this test targets went undetected.
/// </summary>
public sealed class R112_ChartExTreemapSunburstNumDimSizeTests
{
    private static readonly XNamespace ChartExNs = "http://schemas.microsoft.com/office/drawing/2014/chartex";

    // ---------------------------------------------------------------------------------------------
    // THE FIX: Treemap and Sunburst both use the "size" branch of ToChartExNumericDimensionType.
    // ---------------------------------------------------------------------------------------------
    [Theory]
    [InlineData(ChartType.Treemap)]
    [InlineData(ChartType.Sunburst)]
    public void TreemapOrSunburst_NamedRangeNumDimSizeFormula_SeriesSurvivesLoadAndResave(ChartType chartType)
    {
        var saved = SaveHierarchicalChart(chartType);

        // Sanity: confirm our OWN writer really does emit type="size" for this chart type (i.e.
        // this fixture is representative of a real FreeX/Excel file, not a hand-picked shape).
        var writtenDoc = LoadChartXml(saved);
        var writtenNumDim = writtenDoc.Descendants(ChartExNs + "numDim").Single();
        writtenNumDim.Attribute("type")!.Value.Should().Be("size",
            "the writer's own ToChartExNumericDimensionType must choose \"size\" for Treemap/Sunburst -- " +
            "if this fails, the fixture no longer represents what the product actually writes");

        var customized = RewriteChartExDimensionsToNamedRanges(
            saved,
            catFormula: "'Data'!rngGroups",
            catPoints: [("0", "G2"), ("1", "G3")],
            numFormula: "'Data'!rngSizes",
            numPoints: [("0", "10"), ("1", "20")]);

        // --- Real Load entry point ------------------------------------------------------------
        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Data");

        // THE BUG: pre-fix, FindChartExDimension(data, "numDim", "val") never matched this
        // type="size" numDim, so ReadChartExNumericCacheValues returned an empty list, every
        // series ended up with Values.Count == 0, the `result.Any(s => s.Values.Count > 0)` guard
        // returned null, and TryReadDeferredAdvancedChart fell through to the "_xlchart." fallback
        // (which also does not apply here) and dropped the chart entirely.
        var reloadedChart = reloadedSheet.Charts.Should().ContainSingle(
            $"a named-range-sourced {chartType} chart must survive load -- its numDim uses " +
            "@type=\"size\", which the pre-fix reader could never find"
        ).Subject;

        reloadedChart.Type.Should().Be(chartType);
        reloadedChart.EmbeddedSeriesData.Should().NotBeNull().And.HaveCount(1);
        var series0 = reloadedChart.EmbeddedSeriesData![0];
        series0.Categories.Should().Equal("G2", "G3");
        series0.Values.Should().Equal(10.0, 20.0);

        // --- Real Save entry point -------------------------------------------------------------
        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
        chartDoc.Descendants(ChartExNs + "chartSpace").Should().ContainSingle(
            "the chart object must still be present after a save round trip");
    }

    // ---------------------------------------------------------------------------------------------
    // Sibling no-regression: the OTHER branch of ToChartExNumericDimensionType ("val", covering
    // Histogram/Pareto/BoxAndWhisker/Waterfall/Funnel) must keep working exactly as r111 fixed it.
    // Waterfall stands in for the branch here.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void Waterfall_NamedRangeNumDimValFormula_SeriesSurvivesLoadAndResave()
    {
        var saved = SaveHierarchicalChart(ChartType.Waterfall);

        var writtenDoc = LoadChartXml(saved);
        var writtenNumDim = writtenDoc.Descendants(ChartExNs + "numDim").Single();
        writtenNumDim.Attribute("type")!.Value.Should().Be("val",
            "Waterfall is not Treemap/Sunburst, so the writer must still choose \"val\"");

        var customized = RewriteChartExDimensionsToNamedRanges(
            saved,
            catFormula: "'Data'!rngGroups",
            catPoints: [("0", "G2"), ("1", "G3")],
            numFormula: "'Data'!rngSizes",
            numPoints: [("0", "10"), ("1", "20")]);

        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedChart = reloadedWorkbook.Sheets.Single(s => s.Name == "Data").Charts.Should().ContainSingle().Subject;

        reloadedChart.Type.Should().Be(ChartType.Waterfall);
        reloadedChart.EmbeddedSeriesData.Should().NotBeNull().And.HaveCount(1);
        var series0 = reloadedChart.EmbeddedSeriesData![0];
        series0.Categories.Should().Equal("G2", "G3");
        series0.Values.Should().Equal(10.0, 20.0);
    }

    // ---------------------------------------------------------------------------------------------
    // Sibling no-regression: an ordinary Treemap chart with a direct cell-range series (the
    // overwhelming common case) must be completely unaffected by loosening the numDim @type match.
    // ---------------------------------------------------------------------------------------------
    [Fact]
    public void Treemap_OrdinaryCellRangeSeries_UnaffectedByLooserNumDimMatch()
    {
        var saved = SaveHierarchicalChart(ChartType.Treemap);

        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(saved, writable: false));
        var reloadedChart = reloadedWorkbook.Sheets.Single(s => s.Name == "Data").Charts.Should().ContainSingle().Subject;

        reloadedChart.Type.Should().Be(ChartType.Treemap);
        reloadedChart.EmbeddedSeriesData.Should().BeNull("no series formula is a named range");
    }

    private static byte[] SaveHierarchicalChart(ChartType chartType)
    {
        var workbook = new Workbook($"{chartType}Scratch");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Group"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Size"));
        for (uint row = 2; row <= 3; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"G{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue((row - 1) * 10));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = chartType,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            FirstRowIsHeader = true,
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
    /// Rewrites the sole &lt;cx:data&gt;'s &lt;cx:strDim type="cat"&gt; and &lt;cx:numDim&gt; (its
    /// @type is left exactly as the writer produced it -- "size" for Treemap/Sunburst, "val"
    /// otherwise) &lt;cx:f&gt; formulas to named-range formulas, and injects a real
    /// &lt;cx:lvl&gt;/&lt;cx:pt&gt; cache under each -- mimicking what real Excel writes for a chart
    /// bound to OFFSET-based dynamic named ranges.
    /// </summary>
    private static byte[] RewriteChartExDimensionsToNamedRanges(
        byte[] package,
        string catFormula,
        (string Idx, string Value)[] catPoints,
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

            var data = chartDoc.Descendants(ChartExNs + "data").Single();

            var strDim = data.Elements(ChartExNs + "strDim").Single(e => e.Attribute("type")?.Value == "cat");
            RewriteDimension(strDim, catFormula, catPoints);

            var numDim = data.Elements(ChartExNs + "numDim").Single();
            RewriteDimension(numDim, numFormula, numPoints);

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/charts/chart1.xml", CompressionLevel.Optimal);
            using var newEntryStream = newEntry.Open();
            chartDoc.Save(newEntryStream);
        }

        return stream.ToArray();
    }

    private static void RewriteDimension(XElement dimension, string formula, (string Idx, string Value)[] points)
    {
        var f = dimension.Element(ChartExNs + "f");
        f.Should().NotBeNull("the fixture chart must already emit a <cx:f> formula to rewrite");
        f!.Value = formula;

        // Drop any writer-emitted <cx:nf> (name-formula cache) -- irrelevant to this rewrite and
        // not valid alongside a <cx:lvl> cache in the same position per CT_NumDim/CT_StrDim.
        dimension.Elements(ChartExNs + "nf").Remove();

        dimension.Add(new XElement(ChartExNs + "lvl",
            new XAttribute("ptCount", points.Length.ToString()),
            points.Select(p => new XElement(ChartExNs + "pt",
                new XAttribute("idx", p.Idx), p.Value))));
    }
}
