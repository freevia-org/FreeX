using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R44-io-chart-datapoint-3-1: per-data-point &lt;c:dPt&gt; fill overrides (e.g. Excel's
/// "Format Data Point > Fill" used to highlight a single column/marker with a distinct color) were
/// previously read only for the pie/doughnut family (XlsxChartSeriesFormatReader.ApplyPiePointFills,
/// wired solely into XlsxChartPartReader.PieBubble.cs). The bar/column, line, and scatter series
/// loops in XlsxChartPartReader.Bar.cs / .Scatter.cs never called it, so the override was silently
/// dropped on load for those chart types. Fixed by calling the (already chart-family-agnostic)
/// ApplyPiePointFills from every bar/line/scatter series loop.
/// </summary>
public sealed partial class XlsxChartPartReaderTests
{
    [Fact]
    public void TryReadSupportedChart_BarChart_ReadsPerPointFillColor()
    {
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
                      <c:dPt>
                        <c:idx val="2"/>
                        <c:spPr><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill></c:spPr>
                      </c:dPt>
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

        chart.Type.Should().Be(ChartType.Column);
        chart.PointFillColors.Should().ContainSingle(p => p.SeriesIndex == 0 && p.PointIndex == 2)
            .Which.FillColor.Should().Be(new CellColor(0xFF, 0x00, 0x00));
    }

    [Fact]
    public void TryReadSupportedChart_BarChart_NoDataPointOverride_LeavesPointFillColorsEmpty()
    {
        // Sibling no-regression case: a bar series without any <c:dPt> element must not spuriously
        // populate PointFillColors (the loop must remain a no-op when nothing is present).
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
                      <c:spPr><a:solidFill><a:srgbClr val="4472C4"/></a:solidFill></c:spPr>
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

        chart.Type.Should().Be(ChartType.Column);
        chart.PointFillColors.Should().BeEmpty();
        // The series-level fill must still round-trip normally (unaffected by the dPt fix).
        chart.SeriesFormats.Should().ContainSingle(f => f.SeriesIndex == 0)
            .Which.FillColor.Should().Be(new CellColor(0x44, 0x72, 0xC4));
    }

    [Fact]
    public void TryReadSupportedChart_ScatterChart_ReadsPerPointFillColor()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:scatterChart>
                    <c:scatterStyle val="lineMarker"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:dPt>
                        <c:idx val="1"/>
                        <c:spPr><a:solidFill><a:schemeClr val="accent3"/></a:solidFill></c:spPr>
                      </c:dPt>
                      <c:xVal><c:numRef><c:f>Sheet1!$A$2:$A$4</c:f></c:numRef></c:xVal>
                      <c:yVal><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:yVal>
                    </c:ser>
                  </c:scatterChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Scatter);
        var point = chart.PointFillColors.Should()
            .ContainSingle(p => p.SeriesIndex == 0 && p.PointIndex == 1).Subject;
        point.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3));
        point.FillColor.Should().BeNull();
    }

    [Fact]
    public void TryReadSupportedChart_ScatterChart_MultiSeriesDataPoints_AreKeptIndependentByIndex()
    {
        // Sibling no-regression case: two scatter series each with their own per-point override must
        // not cross-contaminate — each (SeriesIndex, PointIndex) pair stays distinct.
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:scatterChart>
                    <c:scatterStyle val="lineMarker"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:dPt>
                        <c:idx val="0"/>
                        <c:spPr><a:solidFill><a:srgbClr val="00FF00"/></a:solidFill></c:spPr>
                      </c:dPt>
                      <c:xVal><c:numRef><c:f>Sheet1!$A$2:$A$4</c:f></c:numRef></c:xVal>
                      <c:yVal><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:yVal>
                    </c:ser>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:dPt>
                        <c:idx val="0"/>
                        <c:spPr><a:solidFill><a:srgbClr val="0000FF"/></a:solidFill></c:spPr>
                      </c:dPt>
                      <c:xVal><c:numRef><c:f>Sheet1!$A$2:$A$4</c:f></c:numRef></c:xVal>
                      <c:yVal><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:yVal>
                    </c:ser>
                  </c:scatterChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.PointFillColors.Should().HaveCount(2);
        chart.PointFillColors.Should().ContainSingle(p => p.SeriesIndex == 0 && p.PointIndex == 0)
            .Which.FillColor.Should().Be(new CellColor(0x00, 0xFF, 0x00));
        chart.PointFillColors.Should().ContainSingle(p => p.SeriesIndex == 1 && p.PointIndex == 0)
            .Which.FillColor.Should().Be(new CellColor(0x00, 0x00, 0xFF));
    }
}
