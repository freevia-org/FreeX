using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxChartPartReaderTests
{
    [Fact]
    public void TryReadSupportedChart_ReadsBarDirection()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="bar"/>
                    <c:ser>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Bar);
    }

    [Fact]
    public void TryReadSupportedChart_ReadsBarSpacingAndVaryColors()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:grouping val="clustered"/>
                    <c:varyColors val="1"/>
                    <c:ser>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:overlap val="-20"/>
                    <c:gapWidth val="75"/>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Column);
        chart.VaryColorsByPoint.Should().BeTrue();
        chart.BarOverlap.Should().Be(-20);
        chart.BarGapWidth.Should().Be(75);
    }

    [Fact]
    public void TryReadSupportedChart_ReadsChartDataTableMetadata()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                  <c:dTable>
                    <c:showHorzBorder val="1"/>
                    <c:showVertBorder val="0"/>
                    <c:showOutline val="1"/>
                    <c:showKeys val="1"/>
                  </c:dTable>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.DataTable.Should().BeEquivalentTo(new ChartDataTableModel
        {
            ShowHorizontalBorder = true,
            ShowVerticalBorder = false,
            ShowOutline = true,
            ShowLegendKeys = true
        });
    }

    [Fact]
    public void TryReadSupportedChart_ReadsErrorBarMetadata()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:errBars>
                        <c:errBarType val="plus"/>
                        <c:errValType val="percentage"/>
                        <c:noEndCap val="1"/>
                        <c:val val="12.5"/>
                        <c:spPr>
                          <a:ln w="25400" xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                            <a:solidFill><a:schemeClr val="accent3"/></a:solidFill>
                            <a:prstDash val="dash"/>
                          </a:ln>
                        </c:spPr>
                      </c:errBars>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.ShowErrorBars.Should().BeTrue();
        chart.ErrorBarKind.Should().Be(ChartErrorBarKind.Percentage);
        chart.ErrorBarDirection.Should().Be(ChartErrorBarDirection.Plus);
        chart.ErrorBarValue.Should().Be(12.5);
        chart.ErrorBarEndCaps.Should().BeFalse();
        chart.ErrorBarThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3));
        chart.ErrorBarColor.Should().BeNull();
        chart.ErrorBarThickness.Should().Be(2);
        chart.ErrorBarDashStyle.Should().Be(ChartLineDashStyle.Dash);
    }
}
