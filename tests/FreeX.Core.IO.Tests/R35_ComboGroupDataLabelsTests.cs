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
/// Regression coverage for R35-deferred-combo-group-dlbls-1: a combo chart (e.g. bar-primary +
/// line-secondary) writes one native plot-chart-type group per series subset, and in real Excel
/// each group can carry its own &lt;c:dLbls&gt;. Before this fix, FreeX's reader
/// (XlsxChartDataLabelReader.FindPlotChartElement) only ever looked at the FIRST group for
/// &lt;c:dLbls&gt;, so a combo chart where only a LATER group (e.g. the secondary-axis line
/// series) had data labels silently lost them on open; the writer mirrored the bug by only ever
/// emitting the chart-wide dLbls onto the first yielded group. The fix preserves a later group's
/// &lt;c:dLbls&gt; verbatim (ChartModel.AdditionalPlotGroupDataLabels) and re-attaches it to the
/// same group on save.
/// </summary>
public sealed class R35_ComboGroupDataLabelsTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    private static XDocument ParseChartXml(string xml) => XDocument.Parse(xml);

    [Fact]
    public void TryReadSupportedChart_ComboLineGroupHasDataLabels_BarGroupDoesNot_PreservesLineGroupLabels()
    {
        // Mirrors real Excel output for: bar series on the primary axis (no data labels) + line
        // series with "Format Data Labels > Show Value" turned on. Excel writes <c:dLbls> as a
        // child of the SECOND group (c:lineChart), not the first (c:barChart).
        var sheetId = new SheetId(System.Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                  <c:lineChart>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:dLbls>
                      <c:showLegendKey val="0"/>
                      <c:showVal val="1"/>
                      <c:showCatName val="0"/>
                      <c:showSerName val="0"/>
                      <c:showPercent val="0"/>
                      <c:showBubbleSize val="0"/>
                    </c:dLbls>
                  </c:lineChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var result = XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart);

        result.Should().BeTrue();
        chart.ComboLineSeriesIndexes.Should().Equal([1]);

        // Not lost: the line group's own <c:dLbls> is captured verbatim, keyed by its group index (1).
        var preserved = chart.AdditionalPlotGroupDataLabels.Should().ContainSingle().Subject;
        preserved.GroupIndex.Should().Be(1);
        preserved.RawXml.Should().Contain("showVal");
        XElement.Parse(preserved.RawXml).Element(ChartNs + "showVal")!.Attribute("val")!.Value.Should().Be("1");
    }

    [Fact]
    public void TryReadSupportedChart_ComboBarGroupHasDataLabels_LineGroupDoesNot_NoSpuriousPreservedGroup()
    {
        // Sibling/no-regression case: the FIRST group (bar) has the data labels, matching the
        // already-working pre-fix scenario. It must still be modeled as chart-wide scalars, and no
        // group-index override should be recorded since the (label-less) second group has no dLbls.
        var sheetId = new SheetId(System.Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:dLbls>
                      <c:showLegendKey val="0"/>
                      <c:showVal val="1"/>
                      <c:showCatName val="0"/>
                      <c:showSerName val="0"/>
                      <c:showPercent val="0"/>
                      <c:showBubbleSize val="0"/>
                    </c:dLbls>
                  </c:barChart>
                  <c:lineChart>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:lineChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var result = XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart);

        result.Should().BeTrue();
        chart.ShowDataLabels.Should().BeTrue();
        chart.ShowDataLabelValue.Should().BeTrue();
        chart.AdditionalPlotGroupDataLabels.Should().BeEmpty();
    }

    [Fact]
    public void ComboChart_LineGroupDataLabelsOverride_SurvivesFullSaveReloadRoundTrip()
    {
        // End-to-end: build a bar(primary)+line(comboLine) chart whose line group carries a
        // preserved <c:dLbls> override (as XlsxChartDataLabelReader would have captured from a
        // real Excel file). Saving must re-attach it to the lineChart group (not the barChart
        // group), and reloading must recapture the same override.
        var workbook = new Workbook("ComboLineDataLabels");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Bar"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Line"));
        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"C{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 10));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(row));
        }

        const string preservedDLbls = """
            <c:dLbls xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:showLegendKey val="0"/>
              <c:showVal val="1"/>
              <c:showCatName val="0"/>
              <c:showSerName val="0"/>
              <c:showPercent val="0"/>
              <c:showBubbleSize val="0"/>
            </c:dLbls>
            """;

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [1],
            AdditionalPlotGroupDataLabels = [new ChartPlotGroupDataLabelsXml(1, preservedDLbls)],
        });

        var saved = SaveToBytes(workbook);
        var chartDoc = LoadChartXml(saved);
        var plotArea = chartDoc.Descendants(ChartNs + "plotArea").Single();

        var barChart = plotArea.Element(ChartNs + "barChart");
        barChart.Should().NotBeNull();
        barChart!.Element(ChartNs + "dLbls").Should().BeNull("the bar (first/primary) group had no preserved override");

        var lineChart = plotArea.Element(ChartNs + "lineChart");
        lineChart.Should().NotBeNull();
        var lineDLbls = lineChart!.Element(ChartNs + "dLbls");
        lineDLbls.Should().NotBeNull("the line (second/combo) group's preserved <c:dLbls> must be re-attached to it, not dropped");
        lineDLbls!.Element(ChartNs + "showVal")!.Attribute("val")!.Value.Should().Be("1");

        using var stream = new MemoryStream(saved, writable: false);
        var reloaded = new XlsxFileAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        var reloadedOverride = reloaded.AdditionalPlotGroupDataLabels.Should().ContainSingle().Subject;
        reloadedOverride.GroupIndex.Should().Be(1);
        reloadedOverride.RawXml.Should().Contain("showVal");
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
