using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxChartPartReaderTests
{
    [Fact]
    public void TryReadSupportedChart_ReadsScatterAxisTitlesByChartAxisIdOrder()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:scatterChart>
                    <c:scatterStyle val="lineMarker"/>
                    <c:ser>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:xVal><c:numRef><c:f>Sheet1!$A$2:$A$4</c:f></c:numRef></c:xVal>
                      <c:yVal><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:yVal>
                    </c:ser>
                    <c:axId val="10"/>
                    <c:axId val="20"/>
                  </c:scatterChart>
                  <c:valAx>
                    <c:axId val="20"/>
                    <c:title><c:tx><c:rich><a:p><a:r><a:t>Y Axis</a:t></a:r></a:p></c:rich></c:tx></c:title>
                  </c:valAx>
                  <c:valAx>
                    <c:axId val="10"/>
                    <c:title><c:tx><c:rich><a:p><a:r><a:t>X Axis</a:t></a:r></a:p></c:rich></c:tx></c:title>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Scatter);
        chart.XAxisTitle.Should().Be("X Axis");
        chart.YAxisTitle.Should().Be("Y Axis");
    }

    [Fact]
    public void TryReadSupportedChart_UsesBubbleSeriesIndexForSeriesFormatting()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:bubbleChart>
                    <c:ser>
                      <c:idx val="3"/>
                      <c:spPr><a:solidFill><a:srgbClr val="70AD47"/></a:solidFill></c:spPr>
                      <c:xVal><c:numRef><c:f>Sheet1!$A$2:$A$4</c:f></c:numRef></c:xVal>
                      <c:yVal><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:yVal>
                      <c:bubbleSize><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:bubbleSize>
                    </c:ser>
                  </c:bubbleChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Bubble);
        chart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(3, FillColor: new CellColor(112, 173, 71)));
    }

    [Fact]
    public void TryReadSupportedChart_ReadsBubbleChartOptions()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:bubbleChart>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:xVal><c:numRef><c:f>Sheet1!$A$2:$A$4</c:f></c:numRef></c:xVal>
                      <c:yVal><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:yVal>
                      <c:bubbleSize><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:bubbleSize>
                    </c:ser>
                    <c:bubbleScale val="150"/>
                    <c:showNegBubbles val="1"/>
                    <c:sizeRepresents val="w"/>
                  </c:bubbleChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Bubble);
        chart.BubbleScale.Should().Be(150);
        chart.ShowNegativeBubbles.Should().BeTrue();
        chart.BubbleSizeRepresents.Should().Be(ChartBubbleSizeRepresents.Width);
    }
}
