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
/// Round 57 io-chart-series-refs findings:
/// <list type="bullet">
///   <item>
///   R57-io-chart-series-refs-5-1: a chart's series ranges/values/categories must be keyed to the
///   sheet <see cref="ChartModel.DataRange"/> actually points at, not to the chart's own anchor
///   sheet — a cross-sheet chart (anchored on one tab, plotting data on another) was silently
///   rewritten to reference the anchor sheet's (usually blank) cells on every save. Fixed by
///   resolving <c>chart.DataRange.Start.Sheet</c>'s real <see cref="Sheet"/> via the
///   <see cref="Workbook"/> now threaded through <c>XlsxWorksheetChartWriter.Save</c> -&gt;
///   <c>XlsxChartXmlWriter.ToChartXml</c> -&gt; <c>BuildChartSeries</c> et al.
///   </item>
///   <item>
///   R57-io-chart-series-refs-5-2: a Scatter/Bubble series has no <c>cat</c>/<c>val</c> containers
///   (it uses <c>xVal</c>/<c>yVal</c>[/<c>bubbleSize</c>] instead), so an unparsable
///   external-link/multi-area formula on those containers was invisible to the verbatim-formula
///   detector and silently replaced by a fabricated local range on save. Fixed by detecting the
///   series' actual container set in <c>XlsxChartSeriesRangeReader</c>, and by giving
///   <see cref="ChartSeriesVerbatimFormulas"/> a dedicated <c>BubbleSizeFormula</c> field that
///   <c>BuildBubbleChartSeries</c> now consults.
///   </item>
///   <item>
///   R57-io-chart-series-refs-5-3: <c>BuildNumCacheXml</c> unconditionally wrote
///   <c>&lt;c:formatCode&gt;General&lt;/c:formatCode&gt;</c> regardless of the source cells' real
///   number format. Fixed by resolving the strip's first data cell's format via
///   <c>Workbook.GetStyle(cell.StyleId).NumberFormat</c>.
///   </item>
/// </list>
/// </summary>
public sealed class R57_ChartSeriesRefsTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    // --- R57-io-chart-series-refs-5-1 --------------------------------------------------------

    [Fact]
    public void ColumnChart_CrossSheetDataRange_SeriesRangeAndCacheUseDataSheetNotAnchorSheet()
    {
        var workbook = new Workbook("CrossSheetChart");
        var chartSheet = workbook.AddSheet("ChartSheet");
        var dataSheet = workbook.AddSheet("DataSheet");
        for (uint row = 2; row <= 6; row++)
            dataSheet.SetCell(new CellAddress(dataSheet.Id, row, 2), new NumberValue((row - 1) * 10));

        chartSheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            // The chart is anchored on ChartSheet but its series data lives on DataSheet — a
            // normal, Excel-supported "dashboard tab charts data on another tab" scenario.
            DataRange = new GridRange(new CellAddress(dataSheet.Id, 2, 2), new CellAddress(dataSheet.Id, 6, 2)),
            // Single-column data range: no separate category column, no header row.
            FirstColIsCategories = false,
            FirstRowIsHeader = false,
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var valFormula = chartDoc.Descendants(ChartNs + "val").Single()
            .Descendants(ChartNs + "f").Single().Value;
        valFormula.Should().Be("DataSheet!$B$2:$B$6",
            "the series must reference the sheet chart.DataRange actually points at, not the chart's anchor sheet");

        var cachedValues = chartDoc.Descendants(ChartNs + "val").Single()
            .Descendants(ChartNs + "v").Select(v => v.Value).ToList();
        cachedValues.Should().Equal(["10", "20", "30", "40", "50"],
            "the numCache must be built from the real DataSheet cells, not the (blank) anchor-sheet cells at the same row/col");
    }

    // Sibling no-regression: an ordinary same-sheet chart (the overwhelmingly common case) must
    // keep referencing its own sheet by name, unaffected by the cross-sheet resolution added above.
    [Fact]
    public void ColumnChart_SameSheetDataRange_SeriesRangeStillUsesAnchorSheet()
    {
        var workbook = new Workbook("SameSheetChart");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 2; row <= 4; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 4, 2)),
            FirstColIsCategories = false,
            FirstRowIsHeader = false,
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var valFormula = chartDoc.Descendants(ChartNs + "val").Single()
            .Descendants(ChartNs + "f").Single().Value;
        valFormula.Should().Be("Data!$B$2:$B$4");
    }

    // --- R57-io-chart-series-refs-5-2 --------------------------------------------------------

    [Fact]
    public void HasUnparsableFormula_ScatterSeriesWithNoCatValContainers_DetectsUnparsableYVal()
    {
        var sheetId = SheetId.New();
        var series = XElement.Parse("""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:xVal><c:numRef><c:f>Sheet1!$A$2:$A$6</c:f></c:numRef></c:xVal>
              <c:yVal><c:numRef><c:f>[1]Sheet1!$B$2:$B$6</c:f></c:numRef></c:yVal>
            </c:ser>
            """);

        // Pre-fix, HasUnparsableFormula only ever inspected the tx/cat/val containers — absent on
        // a scatter series — so it always returned false here regardless of the external-link yVal.
        XlsxChartSeriesRangeReader.HasUnparsableFormula(series, sheetId).Should().BeTrue(
            "the external-link yVal formula must be detected as unparsable even though this series has no cat/val containers");

        var verbatim = XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas([series], sheetId);
        verbatim.Should().NotBeNull();
        var entry = verbatim!.Single();
        entry.CatFormula.Should().Be("Sheet1!$A$2:$A$6", "xVal is repurposed into CatFormula for a scatter series (matches the writer's own reuse)");
        entry.ValFormula.Should().Be("[1]Sheet1!$B$2:$B$6", "yVal is repurposed into ValFormula for a scatter series");
    }

    // Sibling no-regression: a scatter series whose xVal/yVal are both ordinary, fully-parseable
    // same-workbook ranges must NOT trigger the verbatim bypass — the normal positional path stays
    // in effect exactly as before this fix.
    [Fact]
    public void HasUnparsableFormula_ScatterSeriesAllParseable_ReturnsFalse()
    {
        var sheetId = SheetId.New();
        var series = XElement.Parse("""
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:xVal><c:numRef><c:f>Sheet1!$A$2:$A$6</c:f></c:numRef></c:xVal>
              <c:yVal><c:numRef><c:f>Sheet1!$B$2:$B$6</c:f></c:numRef></c:yVal>
            </c:ser>
            """);

        XlsxChartSeriesRangeReader.HasUnparsableFormula(series, sheetId).Should().BeFalse();
        XlsxChartSeriesRangeReader.TryCollectVerbatimFormulas([series], sheetId).Should().BeNull();
    }

    [Fact]
    public void BubbleChart_VerbatimBubbleSizeFormula_WrittenVerbatimInsteadOfRecomputedRange()
    {
        var workbook = new Workbook("BubbleVerbatimSize");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(row * 100));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Bubble,
            DataRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 4, 3)),
            FirstColIsCategories = false,
            FirstRowIsHeader = false,
            VerbatimSeriesFormulas = [new ChartSeriesVerbatimFormulas(0, null, null, null, "[1]Sheet1!$C$2:$C$4")],
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var bubbleSize = chartDoc.Descendants(ChartNs + "bubbleSize").Single();
        bubbleSize.Descendants(ChartNs + "f").Single().Value.Should().Be("[1]Sheet1!$C$2:$C$4",
            "an unparsable (external-link) bubbleSize formula must round-trip verbatim, not be replaced by a fabricated local range");
        bubbleSize.Descendants(ChartNs + "numCache").Should().BeEmpty(
            "a verbatim bubbleSize formula has no known live strip, so no cache should be fabricated for it");
    }

    // Sibling no-regression: a bubble chart with NO verbatim entry must keep computing bubbleSize
    // positionally (with its cache), exactly as before this fix.
    [Fact]
    public void BubbleChart_NoVerbatimFormulas_BubbleSizeStillComputedPositionallyWithCache()
    {
        var workbook = new Workbook("BubbleNoVerbatim");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(row * 100));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Bubble,
            DataRange = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 4, 3)),
            FirstColIsCategories = false,
            FirstRowIsHeader = false,
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var bubbleSize = chartDoc.Descendants(ChartNs + "bubbleSize").Single();
        bubbleSize.Descendants(ChartNs + "f").Single().Value.Should().Be("Data!$C$2:$C$4");
        bubbleSize.Descendants(ChartNs + "numCache").Should().ContainSingle();
    }

    // --- R57-io-chart-series-refs-5-3 --------------------------------------------------------

    [Fact]
    public void ColumnChart_ValueSeriesFromPercentageFormattedCells_NumCacheFormatCodeMirrorsSourceFormat()
    {
        var workbook = new Workbook("ChartNumCacheFormat");
        var sheet = workbook.AddSheet("Data");
        var percentStyleId = workbook.RegisterStyle(new CellStyle { NumberFormat = "0%" });
        for (uint row = 2; row <= 4; row++)
        {
            var cell = Cell.FromValue(new NumberValue(0.1 * (row - 1)));
            cell.StyleId = percentStyleId;
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), cell);
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 4, 2)),
            FirstColIsCategories = false,
            FirstRowIsHeader = false,
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var formatCode = chartDoc.Descendants(ChartNs + "numCache").Single()
            .Element(ChartNs + "formatCode")!.Value;
        formatCode.Should().Be("0%",
            "the numCache must mirror the source cell's real number format instead of always hardcoding \"General\"");
    }

    // Sibling no-regression: a value series from cells with no explicit number format (Default
    // style, "General") must keep writing formatCode="General" — unaffected by this fix.
    [Fact]
    public void ColumnChart_ValueSeriesFromUnformattedCells_NumCacheFormatCodeIsGeneral()
    {
        var workbook = new Workbook("ChartNumCacheGeneral");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 2; row <= 4; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 4, 2)),
            FirstColIsCategories = false,
            FirstRowIsHeader = false,
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);

        var formatCode = chartDoc.Descendants(ChartNs + "numCache").Single()
            .Element(ChartNs + "formatCode")!.Value;
        formatCode.Should().Be("General");
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
}
