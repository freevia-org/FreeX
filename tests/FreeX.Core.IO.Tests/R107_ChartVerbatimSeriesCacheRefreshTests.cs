using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R107-io-chart-series-verbatim-refresh: a chart series whose &lt;c:val&gt;/&lt;c:cat&gt; formula
/// resolves to a DIFFERENT sheet than the chart's own host sheet (or, before this fix, a named
/// range / multi-area / external-link formula) gets its &lt;c:numCache&gt;/&lt;c:strCache&gt; frozen
/// verbatim at LOAD time into <see cref="ChartModel.VerbatimSeriesFormulas"/>
/// (<c>XlsxChartSeriesRangeReader.CaptureCacheXmlIfUnparsable</c>). Before this fix,
/// <c>XlsxChartXmlWriter.Series.cs</c> re-emitted that frozen snapshot verbatim on EVERY subsequent
/// save, no matter how many times the referenced cells were edited afterward — unlike the ordinary
/// same-sheet path, which always rebuilds the cache fresh from current worksheet data. Real Excel
/// has no notion of a chart writer "confined" to one sheet: a cross-sheet series always shows the
/// CURRENT cell values on save (assuming automatic calculation, the default).
/// <para>
/// Fixed by having the writer re-resolve each verbatim formula against the CURRENT workbook (via
/// <c>XlsxChartSeriesRangeReader.TryParseFormulaRange</c> with a sheet-name resolver built from
/// today's <see cref="Workbook.Sheets"/>) and rebuild the cache from live cell values whenever the
/// target sheet/range can still be located, falling back to the frozen cache only when the formula
/// truly cannot be resolved at all (a genuine named range, multi-area reference, or
/// external-workbook link).
/// </para>
/// </summary>
public sealed class R107_ChartVerbatimSeriesCacheRefreshTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    // ---------------------------------------------------------------------------------------------
    // THE BUG, through the real product entry points (XlsxFileAdapter.Load / .Save):
    // load a file with a cross-sheet series, edit the cell the series formula still points at
    // (mirroring what an ordinary user edit does to the workbook model), then save AGAIN. The
    // re-emitted numCache must reflect the EDIT, not the value that was on disk when the file was
    // first opened.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void CrossSheetSeries_CellEditedAfterLoad_ResaveEmitsUpdatedValues_NotFrozenSnapshot()
    {
        var workbook = new Workbook("CrossSheetRefresh");
        var dataSheet = workbook.AddSheet("Data");
        var paramsSheet = workbook.AddSheet("Params");

        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 1), new TextValue("Cat"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 2), new TextValue("Actual"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 3), new TextValue("PlaceholderTarget"));
        for (uint row = 2; row <= 4; row++)
        {
            dataSheet.SetCell(new CellAddress(dataSheet.Id, row, 1), new TextValue($"C{row}"));
            dataSheet.SetCell(new CellAddress(dataSheet.Id, row, 2), new NumberValue((row - 1) * 10));
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
        // Rebind series 1's <c:val> to a cross-sheet range with its own real <c:numCache> — mirrors
        // what real Excel writes for a series added via "Select Data > Add Series" on another sheet.
        var customized = RebindSeriesValueToOtherSheet(
            saved,
            seriesIdx: "1",
            crossSheetFormula: "'Params'!$B$2:$B$4",
            points: [("0", "100"), ("1", "200"), ("2", "300")]);

        // --- Real Load entry point ------------------------------------------------------------
        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedDataSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Data");
        var reloadedParamsSheet = reloadedWorkbook.Sheets.Single(s => s.Name == "Params");
        var reloadedChart = reloadedDataSheet.Charts.Should().ContainSingle().Subject;

        reloadedChart.VerbatimSeriesFormulas.Should().NotBeNull();
        var verbatim = reloadedChart.VerbatimSeriesFormulas!.Should().ContainSingle(v => v.SeriesIndex == 1).Subject;
        verbatim.ValFormula.Should().Be("'Params'!$B$2:$B$4");

        // --- Simulate an ordinary user edit to the cells the series formula still points at ----
        reloadedParamsSheet.SetCell(new CellAddress(reloadedParamsSheet.Id, 2, 2), new NumberValue(555));
        reloadedParamsSheet.SetCell(new CellAddress(reloadedParamsSheet.Id, 3, 2), new NumberValue(666));
        reloadedParamsSheet.SetCell(new CellAddress(reloadedParamsSheet.Id, 4, 2), new NumberValue(777));

        // --- Real Save entry point (no further "Select Data" round-trip happened) --------------
        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
        var series1 = chartDoc.Descendants(ChartNs + "ser")
            .Single(s => s.Element(ChartNs + "idx")!.Attribute("val")!.Value == "1");
        var numCache = series1.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!.Element(ChartNs + "numCache");

        numCache.Should().NotBeNull();
        var points = numCache!.Elements(ChartNs + "pt")
            .Select(pt => (Idx: pt.Attribute("idx")!.Value, Value: pt.Element(ChartNs + "v")!.Value))
            .OrderBy(p => p.Idx)
            .ToList();

        // THE BUG: pre-fix this was still [("0","100"), ("1","200"), ("2","300")] — the frozen
        // snapshot captured when the file was first opened, ignoring the edit entirely.
        points.Should().BeEquivalentTo(
            [("0", "555"), ("1", "666"), ("2", "777")],
            "the resaved cache must reflect the edited Params-sheet cells, not the stale snapshot " +
            "captured when the file was first opened");
    }

    // ---------------------------------------------------------------------------------------------
    // Sibling no-regression: a formula that STILL cannot be resolved at all (a genuine named range)
    // must keep re-emitting the frozen verbatim cache exactly as before — the fix must not force a
    // (wrong, chart's-own-sheet) recompute attempt for the case it explicitly cannot handle.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void NamedRangeSeries_StillUnresolvable_KeepsFrozenVerbatimCacheOnResave()
    {
        var workbook = new Workbook("NamedRangeUnresolvable");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Actual"));
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
            SeriesColumnMappings = [new ChartSeriesColumnMapping(0, 2)],
        });

        var saved = SaveToBytes(workbook);
        var customized = RebindSeriesValueToOtherSheet(
            saved,
            seriesIdx: "0",
            crossSheetFormula: "MyNamedRange",
            points: [("0", "42"), ("1", "43"), ("2", "44")]);

        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedChart = reloadedWorkbook.Sheets.Single(s => s.Name == "Data").Charts.Should().ContainSingle().Subject;

        reloadedChart.VerbatimSeriesFormulas.Should().NotBeNull();
        var verbatim = reloadedChart.VerbatimSeriesFormulas!.Should().ContainSingle(v => v.SeriesIndex == 0).Subject;
        verbatim.ValFormula.Should().Be("MyNamedRange");

        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
        var series0 = chartDoc.Descendants(ChartNs + "ser").Single();
        series0.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!.Element(ChartNs + "f")!.Value
            .Should().Be("MyNamedRange", "an unresolvable named-range formula must round-trip verbatim");

        var numCache = series0.Element(ChartNs + "val")!.Element(ChartNs + "numRef")!.Element(ChartNs + "numCache");
        numCache.Should().NotBeNull();
        var points = numCache!.Elements(ChartNs + "pt")
            .Select(pt => (Idx: pt.Attribute("idx")!.Value, Value: pt.Element(ChartNs + "v")!.Value))
            .OrderBy(p => p.Idx)
            .ToList();

        points.Should().BeEquivalentTo(
            [("0", "42"), ("1", "43"), ("2", "44")],
            "a formula that still cannot be resolved to any concrete cells must keep re-emitting the " +
            "frozen verbatim cache exactly as it did before this fix");
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
