using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R103-io-chart-series-tx-1: Excel's Chart &gt; Select Data &gt; Edit Series &gt; "Series name" box
/// accepts ANY cell reference, not just the header cell directly above that series' data column
/// (e.g. Series name = 'Sheet1'!$F$1 while the series' values come from column B). FreeX's reader
/// never captured this per-series &lt;c:tx&gt; formula unless it was unparsable as a rectangular
/// range (named range / multi-area / external link) — a plain reference to a non-header cell parses
/// fine, so it silently fell through the pre-existing verbatim-tx bypass. On save,
/// <c>XlsxChartXmlWriter.ToSeriesTitleXml</c> always recomputed the series name as the strip's own
/// header cell (or emitted nothing when <see cref="ChartModel.FirstRowIsHeader"/> is false), so the
/// user's custom reference and its cached string were discarded. Fixed by capturing the series' own
/// &lt;c:tx&gt; formula verbatim on read into <see cref="ChartModel.SeriesNameOverrides"/> and having
/// the writer prefer it over the recomputed header-cell guess.
/// </summary>
public sealed class R103_ChartSeriesTxNameOverrideTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    private static XDocument ParseChartXml(string xml) => XDocument.Parse(xml);

    // ---------------------------------------------------------------------------------------
    // Reader-level capture
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void TryReadSupportedChart_BarChart_TxPointsAtNonHeaderCell_IsCapturedOnSeriesNameOverrides()
    {
        // The series' values come from column B (Sheet1!$B$2:$B$5) but the user pointed "Series name"
        // at an unrelated cell, F1 -- not the header cell (B1) directly above the data.
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$F$1</c:f><c:strCache><c:ptCount val="1"/><c:pt idx="0"><c:v>Custom Title</c:v></c:pt></c:strCache></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$5</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$5</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.SeriesNameOverrides.Should().ContainSingle(o => o.SeriesIndex == 0 && o.Formula == "Sheet1!$F$1");
    }

    [Fact]
    public void TryReadSupportedChart_BarChart_NoTxElement_LeavesSeriesNameOverridesEmpty()
    {
        // Sibling no-regression case: a series with no <c:tx> at all must not spuriously populate
        // SeriesNameOverrides.
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$5</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$5</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.SeriesNameOverrides.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------
    // Full read -> write round trip through the real product entry point (XlsxFileAdapter)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void BarChart_CustomSeriesNameCellReference_SurvivesLoadAndResave()
    {
        // Build a normal FreeX-authored workbook+chart first (so the package has a well-formed
        // chart1.xml, worksheet, styles, etc.), then hand-edit ONLY the <c:tx><c:f> text inside the
        // saved package to simulate what real Excel would have written for a custom "Series name" —
        // exactly the shape XlsxFileAdapter.Load must read back through XlsxChartPartReader.
        var workbook = new Workbook("SeriesTxOverrideRoundTrip");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Series A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 6), new TextValue("Custom Title"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
        });

        var saved = SaveToBytes(workbook);
        var customized = InjectCustomSeriesNameFormula(saved, "'Data'!$F$1");

        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(customized, writable: false));
        var reloadedChart = reloadedWorkbook.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        reloadedChart.SeriesNameOverrides.Should().ContainSingle(o => o.SeriesIndex == 0 && o.Formula == "'Data'!$F$1",
            "the custom Series-name cell reference must be captured on load, not silently discarded");

        var resaved = SaveToBytes(reloadedWorkbook);
        var chartDoc = LoadChartXml(resaved);
        var series = chartDoc.Descendants(ChartNs + "ser").Single();
        var txFormula = series.Element(ChartNs + "tx")?.Element(ChartNs + "strRef")?.Element(ChartNs + "f")?.Value;
        txFormula.Should().Be("'Data'!$F$1",
            "the writer must re-emit the captured custom series-name reference instead of recomputing " +
            "the strip's own header cell (Data!$B$1)");
    }

    [Fact]
    public void BarChart_NoManualSeriesNameEdit_EmitsRecomputedHeaderCellAndIsStableAcrossReSave()
    {
        // Sibling no-regression case: an ordinary chart whose <c:tx> already happens to equal the
        // strip's own header cell (the common case — no manual "Series name" edit was ever made)
        // must keep emitting that same header-cell <c:tx>, and must not drift or duplicate anything
        // across a second load/save cycle. By design the reader captures whatever <c:tx> formula is
        // present (see TryReadSupportedChart_BarChart_TxPointsAtNonHeaderCell_IsCapturedOnSeriesNameOverrides)
        // even when it happens to match the recomputed default, so SeriesNameOverrides is NOT
        // expected to be empty here — what matters is that the round-tripped text is unchanged and
        // idempotent, not that no capture occurred.
        var workbook = new Workbook("SeriesTxDefault");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Series A"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);
        var series = chartDoc.Descendants(ChartNs + "ser").Single();
        var txFormula = series.Element(ChartNs + "tx")?.Element(ChartNs + "strRef")?.Element(ChartNs + "f")?.Value;
        txFormula.Should().Be("Data!$B$1");

        var reloadedWorkbook = new XlsxFileAdapter().Load(new MemoryStream(saved, writable: false));
        var reloadedChart = reloadedWorkbook.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        reloadedChart.SeriesNameOverrides.Should().ContainSingle(o => o.SeriesIndex == 0 && o.Formula == "Data!$B$1");

        // Idempotency guard: a second save of the reloaded workbook (which now carries a
        // SeriesNameOverrides entry equal to the recomputed default) must emit the identical <c:tx>
        // text, not drift or duplicate the element.
        var resaved = SaveToBytes(reloadedWorkbook);
        var resavedDoc = LoadChartXml(resaved);
        var resavedSeries = resavedDoc.Descendants(ChartNs + "ser").Single();
        resavedSeries.Elements(ChartNs + "tx").Should().ContainSingle();
        resavedSeries.Element(ChartNs + "tx")!.Element(ChartNs + "strRef")!.Element(ChartNs + "f")!.Value
            .Should().Be("Data!$B$1");
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
    /// Rewrites the sole series' &lt;c:tx&gt;&lt;c:f&gt; formula text in xl/charts/chart1.xml to
    /// <paramref name="formula"/>, mimicking what Excel would write for a "Series name" the user
    /// pointed at an arbitrary cell. FreeX's own writer already emits a &lt;c:tx&gt; for this fixture
    /// (FirstRowIsHeader is true), so this only needs to replace its &lt;c:f&gt; text — the shape of
    /// the element is unchanged, only which cell it references.
    /// </summary>
    private static byte[] InjectCustomSeriesNameFormula(byte[] package, string formula)
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
            var txFormulaElement = series.Element(ChartNs + "tx")?.Element(ChartNs + "strRef")?.Element(ChartNs + "f");
            txFormulaElement.Should().NotBeNull("the fixture chart is built with FirstRowIsHeader so it must already emit a <c:tx> to rewrite");
            txFormulaElement!.Value = formula;

            entry.Delete();
            var newEntry = archive.CreateEntry("xl/charts/chart1.xml", CompressionLevel.Optimal);
            using var newEntryStream = newEntry.Open();
            chartDoc.Save(newEntryStream);
        }

        return stream.ToArray();
    }
}
