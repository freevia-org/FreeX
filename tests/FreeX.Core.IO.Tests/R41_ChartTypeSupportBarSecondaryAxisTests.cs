using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R41-twin-incomplete-fix-sweep-1: <see cref="ChartTypeSupport.SupportsSecondaryAxis"/> listed
/// Column/Line/Area/Scatter but omitted the horizontal Bar siblings (Bar/StackedBar/PercentStackedBar).
/// Real Excel DOES support "Format Data Series &gt; Secondary Axis" on horizontal bar charts, so loading
/// a real Excel horizontal-bar chart with a secondary-axis series had
/// <see cref="ChartModel.ShowSecondaryAxis"/> and <see cref="ChartModel.SecondaryAxisSeriesIndexes"/>
/// wiped by <see cref="ChartSeriesIndexSanitizer.SanitizeSecondaryAxisAndComboLineIndexes"/>
/// (called from <c>XlsxChartSanitizer.SanitizeLoadedChart</c> at the end of every
/// <c>XlsxChartPartReader.Bar.cs</c> read path), since that sanitizer clears both fields whenever
/// <c>SupportsSecondaryAxis</c> returns false for the chart's type — a load-time data-loss + wrong-render
/// bug for horizontal-bar twin/secondary-axis charts.
/// </summary>
public sealed class R41_ChartTypeSupportBarSecondaryAxisTests
{
    private static XDocument ParseChartXml(string xml) => XDocument.Parse(xml);

    [Theory]
    [InlineData(ChartType.Bar)]
    [InlineData(ChartType.StackedBar)]
    [InlineData(ChartType.PercentStackedBar)]
    public void SupportsSecondaryAxis_HorizontalBarFamily_ReturnsTrue(ChartType type)
    {
        ChartTypeSupport.SupportsSecondaryAxis(type).Should().BeTrue(
            $"Excel allows Format Data Series > Secondary Axis on horizontal bar charts ({type})");
    }

    [Theory]
    [InlineData(ChartType.Pie)]
    [InlineData(ChartType.ThreeDPie)]
    [InlineData(ChartType.Doughnut)]
    public void SupportsSecondaryAxis_PieFamily_StillReturnsFalse(ChartType type)
    {
        // No-regression sibling: pie/doughnut charts have no value axis at all and must never claim
        // secondary-axis support.
        ChartTypeSupport.SupportsSecondaryAxis(type).Should().BeFalse(
            $"{type} has no value axis and cannot support a secondary axis");
    }

    [Fact]
    public void TryReadSupportedChart_PlainBarChart_SecondaryAxisSeries_PreservesShowSecondaryAxis()
    {
        // Real-Excel-shaped horizontal bar chart (barDir="bar"): series 0 on the primary value axis
        // (222), series 1 on the secondary value axis (333, axPos="r") — Excel expresses this as two
        // <c:barChart> plot groups since a single group's axId set applies to ALL its series. Before
        // the fix, ChartSeriesIndexSanitizer.SanitizeSecondaryAxisAndComboLineIndexes wiped
        // ShowSecondaryAxis + SecondaryAxisSeriesIndexes right after the reader recorded them, because
        // SupportsSecondaryAxis(ChartType.Bar) returned false.
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="bar"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="111"/>
                    <c:axId val="222"/>
                  </c:barChart>
                  <c:barChart>
                    <c:barDir val="bar"/>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:tx><c:strRef><c:f>Sheet1!$C$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="111"/>
                    <c:axId val="333"/>
                  </c:barChart>
                  <c:catAx>
                    <c:axId val="111"/>
                    <c:axPos val="l"/>
                    <c:crossAx val="222"/>
                  </c:catAx>
                  <c:valAx>
                    <c:axId val="222"/>
                    <c:axPos val="b"/>
                    <c:crossAx val="111"/>
                  </c:valAx>
                  <c:valAx>
                    <c:axId val="333"/>
                    <c:axPos val="r"/>
                    <c:crossAx val="111"/>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var result = XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart);

        result.Should().BeTrue();
        chart.Type.Should().Be(ChartType.Bar);
        chart.ShowSecondaryAxis.Should().BeTrue(
            "a horizontal Bar chart with a secondary-axis series must keep ShowSecondaryAxis after load");
        chart.SecondaryAxisSeriesIndexes.Should().Contain(1,
            "series 1's secondary-axis assignment must survive the reader's sanitizer instead of being wiped out");
    }

    [Fact]
    public void TryReadSupportedChart_PlainPieChart_SecondaryAxisNeverSet()
    {
        // No-regression sibling: pie charts have no axes at all, so ShowSecondaryAxis must stay false
        // regardless of the SupportsSecondaryAxis change made for the Bar family.
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:pieChart>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:pieChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var result = XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart);

        result.Should().BeTrue();
        chart.Type.Should().Be(ChartType.Pie);
        chart.ShowSecondaryAxis.Should().BeFalse();
        chart.SecondaryAxisSeriesIndexes.Should().BeEmpty();
    }
}
