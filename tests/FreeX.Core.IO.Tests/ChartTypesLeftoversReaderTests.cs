using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests pinned to what real Excel writes for a column + area combo and a waterfall (chartEx):
/// measured by building the charts through Excel COM and reading the saved chart parts.
/// </summary>
public sealed class ChartTypesLeftoversReaderTests
{
    [Fact]
    public void ColumnAreaCombo_ReadsTheAreaGroupAsPerSeriesComboAreaAndKeepsLegendOrder()
    {
        // Excel declares the area group first (idx 1) and the column group second (idx 0).
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:areaChart>
                    <c:grouping val="standard"/>
                    <c:ser>
                      <c:idx val="1"/><c:order val="1"/>
                      <c:tx><c:strRef><c:f>Sheet1!$C$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$6</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$6</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="111"/><c:axId val="222"/>
                  </c:areaChart>
                  <c:barChart>
                    <c:barDir val="col"/><c:grouping val="clustered"/>
                    <c:ser>
                      <c:idx val="0"/><c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$6</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$6</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="111"/><c:axId val="222"/>
                  </c:barChart>
                  <c:catAx><c:axId val="111"/><c:axPos val="b"/><c:crossAx val="222"/></c:catAx>
                  <c:valAx><c:axId val="222"/><c:axPos val="l"/><c:crossAx val="111"/></c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var read = XlsxChartPartReader.TryReadSupportedChart(chartXml, new SheetId(Guid.NewGuid()), out var chart);

        read.Should().BeTrue();
        chart.Type.Should().Be(ChartType.Column);
        chart.ComboAreaSeriesIndexes.Should().Equal(1);
        chart.ComboLineSeriesIndexes.Should().BeEmpty();
        chart.SeriesPlotOrder.Should().Equal(1, 0);
    }

    [Fact]
    public void ChartEx_Waterfall_WithoutSubtotalsHasNoTotalsAndShowsConnectors()
    {
        var chartXml = XDocument.Parse(WaterfallChartEx("<cx:layoutPr><cx:subtotals /></cx:layoutPr>"));

        var read = XlsxChartPartReader.TryReadSupportedChart(chartXml, new SheetId(Guid.NewGuid()), out var chart);

        read.Should().BeTrue();
        chart.Type.Should().Be(ChartType.Waterfall);
        chart.Title.Should().Be("Waterfall");
        // Excel never invents a total: no <cx:idx> means every column is a plain increase/decrease.
        chart.WaterfallTotalPointIndices.Should().BeEmpty();
        chart.ShowSeriesLines.Should().BeTrue("Excel draws waterfall connector lines by default");
    }

    [Fact]
    public void ChartEx_Waterfall_ConnectorLinesCanBeTurnedOff()
    {
        var chartXml = XDocument.Parse(WaterfallChartEx(
            """<cx:layoutPr><cx:visibility connectorLines="0"/><cx:subtotals><cx:idx val="4"/></cx:subtotals></cx:layoutPr>"""));

        XlsxChartPartReader.TryReadSupportedChart(chartXml, new SheetId(Guid.NewGuid()), out var chart).Should().BeTrue();

        chart.ShowSeriesLines.Should().BeFalse();
        chart.WaterfallTotalPointIndices.Should().Equal(4);
    }

    private static string WaterfallChartEx(string layoutPr) => $$"""
        <cx:chartSpace xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:cx="http://schemas.microsoft.com/office/drawing/2014/chartex">
          <cx:chartData>
            <cx:data id="0">
              <cx:strDim type="cat"><cx:f>Sheet1!$A$2:$A$6</cx:f></cx:strDim>
              <cx:numDim type="val"><cx:f>Sheet1!$B$2:$B$6</cx:f></cx:numDim>
            </cx:data>
          </cx:chartData>
          <cx:chart>
            <cx:title pos="t" align="ctr" overlay="0"><cx:tx><cx:txData><cx:v>Waterfall</cx:v></cx:txData></cx:tx></cx:title>
            <cx:plotArea>
              <cx:plotAreaRegion>
                <cx:series layoutId="waterfall" uniqueId="{00000001-E12F-4260-B882-33348C6BC983}">
                  <cx:tx><cx:txData><cx:f>Sheet1!$B$1</cx:f><cx:v>Amount</cx:v></cx:txData></cx:tx>
                  <cx:dataId val="0"/>
                  {{layoutPr}}
                </cx:series>
              </cx:plotAreaRegion>
              <cx:axis id="0"><cx:catScaling gapWidth="0.5"/><cx:tickLabels/></cx:axis>
              <cx:axis id="1"><cx:valScaling/><cx:majorGridlines/><cx:tickLabels/></cx:axis>
            </cx:plotArea>
            <cx:legend pos="t" align="ctr" overlay="0"/>
          </cx:chart>
        </cx:chartSpace>
        """;
}
