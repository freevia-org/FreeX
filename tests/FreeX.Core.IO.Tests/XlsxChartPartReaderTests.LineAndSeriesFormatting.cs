using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxChartPartReaderTests
{
    [Fact]
    public void TryReadSupportedChart_ReadsLineGuideMetadata()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:lineChart>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:dropLines>
                      <c:spPr><a:ln w="19050"><a:solidFill><a:srgbClr val="5B9BD5"/></a:solidFill><a:prstDash val="dot"/></a:ln></c:spPr>
                    </c:dropLines>
                    <c:hiLowLines>
                      <c:spPr><a:ln w="25400"><a:solidFill><a:schemeClr val="accent4"/></a:solidFill><a:prstDash val="dash"/></a:ln></c:spPr>
                    </c:hiLowLines>
                    <c:upDownBars>
                      <c:gapWidth val="180"/>
                      <c:upBars><c:spPr><a:solidFill><a:srgbClr val="70AD47"/></a:solidFill><a:ln w="12700"><a:solidFill><a:srgbClr val="548235"/></a:solidFill></a:ln></c:spPr></c:upBars>
                      <c:downBars><c:spPr><a:solidFill><a:srgbClr val="C00000"/></a:solidFill><a:ln w="25400"><a:solidFill><a:schemeClr val="accent2"/></a:solidFill></a:ln></c:spPr></c:downBars>
                    </c:upDownBars>
                  </c:lineChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.ShowDropLines.Should().BeTrue();
        chart.ShowHighLowLines.Should().BeTrue();
        chart.ShowUpDownBars.Should().BeTrue();
        chart.DropLineColor.Should().Be(new CellColor(91, 155, 213));
        chart.DropLineThemeColor.Should().BeNull();
        chart.DropLineThickness.Should().Be(1.5);
        chart.DropLineDashStyle.Should().Be(ChartLineDashStyle.Dot);
        chart.HighLowLineThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4));
        chart.HighLowLineColor.Should().BeNull();
        chart.HighLowLineThickness.Should().Be(2);
        chart.HighLowLineDashStyle.Should().Be(ChartLineDashStyle.Dash);
        chart.UpDownBarGapWidth.Should().Be(180);
        chart.UpBarFillColor.Should().Be(new CellColor(112, 173, 71));
        chart.UpBarBorderColor.Should().Be(new CellColor(84, 130, 53));
        chart.UpBarBorderThickness.Should().Be(1);
        chart.DownBarFillColor.Should().Be(new CellColor(192, 0, 0));
        chart.DownBarBorderThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2));
        chart.DownBarBorderColor.Should().BeNull();
        chart.DownBarBorderThickness.Should().Be(2);
    }

    [Fact]
    public void TryReadSupportedChart_ReadsBarSeriesLineMetadata()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:grouping val="stacked"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:serLines>
                      <c:spPr><a:ln w="19050"><a:solidFill><a:schemeClr val="accent5"/></a:solidFill><a:prstDash val="dash"/></a:ln></c:spPr>
                    </c:serLines>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.ShowSeriesLines.Should().BeTrue();
        chart.SeriesLineThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent5));
        chart.SeriesLineColor.Should().BeNull();
        chart.SeriesLineThickness.Should().Be(1.5);
        chart.SeriesLineDashStyle.Should().Be(ChartLineDashStyle.Dash);
    }

    [Fact]
    public void TryReadSupportedChart_ReadsConcreteSeriesFill()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:spPr><a:solidFill><a:srgbClr val="0C2238"/></a:solidFill></c:spPr>
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

        chart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(0, FillColor: new CellColor(12, 34, 56)));
    }

    [Fact]
    public void TryReadSupportedChart_ReadsSeriesInvertIfNegativeFormatting()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:invertIfNegative val="1"/>
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

        chart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(0, InvertIfNegative: true));
    }

    [Fact]
    public void TryReadSupportedChart_ReadsLineSeriesSmoothFormatting()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:lineChart>
                    <c:ser>
                      <c:smooth val="1"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:lineChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(0, Smooth: true));
    }

    [Fact]
    public void TryReadSupportedChart_ReadsLineMarkerBorderFormatting()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:lineChart>
                    <c:ser>
                      <c:marker>
                        <c:symbol val="circle"/>
                        <c:spPr><a:ln w="19050"><a:solidFill><a:schemeClr val="accent3"/></a:solidFill></a:ln></c:spPr>
                      </c:marker>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:lineChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(
                0,
                MarkerStyle: ChartMarkerStyle.Circle,
                MarkerBorderThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3),
                MarkerBorderThickness: 1.5));
    }
}
