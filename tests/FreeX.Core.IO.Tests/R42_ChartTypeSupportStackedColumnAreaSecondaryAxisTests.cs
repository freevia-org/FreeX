using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R42-meta-2 (r41-incomplete-twin): the R41 <see cref="ChartTypeSupport.SupportsSecondaryAxis"/> fix
/// added the horizontal Bar family (Bar/StackedBar/PercentStackedBar) but left the Column's own
/// Stacked/PercentStacked siblings (StackedColumn, PercentStackedColumn) and the Area siblings
/// (StackedArea, PercentStackedArea) absent. Real Excel allows "Format Data Series &gt; Secondary Axis"
/// on any bar/column/area grouping, so loading a real Excel Stacked-Column (or 100%-Stacked-Column,
/// Stacked-Area) chart with a secondary-axis series had <see cref="ChartModel.ShowSecondaryAxis"/> and
/// <see cref="ChartModel.SecondaryAxisSeriesIndexes"/> wiped by
/// <see cref="ChartSeriesIndexSanitizer.SanitizeSecondaryAxisAndComboLineIndexes"/> on load — a
/// load-time data-loss + wrong-render bug for those chart types' twin/secondary-axis charts.
/// </summary>
public sealed class R42_ChartTypeSupportStackedColumnAreaSecondaryAxisTests
{
    private static XDocument ParseChartXml(string xml) => XDocument.Parse(xml);

    [Theory]
    [InlineData(ChartType.StackedColumn)]
    [InlineData(ChartType.PercentStackedColumn)]
    [InlineData(ChartType.StackedArea)]
    [InlineData(ChartType.PercentStackedArea)]
    public void SupportsSecondaryAxis_StackedColumnAndAreaFamily_ReturnsTrue(ChartType type)
    {
        ChartTypeSupport.SupportsSecondaryAxis(type).Should().BeTrue(
            $"Excel allows Format Data Series > Secondary Axis on stacked column/area charts ({type})");
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
    public void TryReadSupportedChart_StackedColumnChart_SecondaryAxisSeries_PreservesShowSecondaryAxis()
    {
        // Real-Excel-shaped stacked column chart (barDir="col", grouping="stacked"): series 0 on the
        // primary value axis (222), series 1 on the secondary value axis (333, axPos="r"). Excel
        // expresses this as two <c:barChart> plot groups since a single group's axId set applies to
        // ALL its series. Before the fix, ChartSeriesIndexSanitizer.SanitizeSecondaryAxisAndComboLineIndexes
        // wiped ShowSecondaryAxis + SecondaryAxisSeriesIndexes right after the reader recorded them,
        // because SupportsSecondaryAxis(ChartType.StackedColumn) returned false.
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:grouping val="stacked"/>
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
                    <c:barDir val="col"/>
                    <c:grouping val="stacked"/>
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
                    <c:axPos val="b"/>
                    <c:crossAx val="222"/>
                  </c:catAx>
                  <c:valAx>
                    <c:axId val="222"/>
                    <c:axPos val="l"/>
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
        chart.Type.Should().Be(ChartType.StackedColumn);
        chart.ShowSecondaryAxis.Should().BeTrue(
            "a StackedColumn chart with a secondary-axis series must keep ShowSecondaryAxis after load");
        chart.SecondaryAxisSeriesIndexes.Should().Contain(1,
            "series 1's secondary-axis assignment must survive the reader's sanitizer instead of being wiped out");
    }

    [Fact]
    public void TryReadSupportedChart_PlainPieChart_SecondaryAxisNeverSet()
    {
        // No-regression sibling: pie charts have no axes at all, so ShowSecondaryAxis must stay false
        // regardless of the SupportsSecondaryAxis change made for the StackedColumn/Area families.
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
