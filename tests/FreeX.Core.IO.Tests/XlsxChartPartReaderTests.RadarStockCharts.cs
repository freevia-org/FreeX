using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxChartPartReaderTests
{
    [Theory]
    [InlineData("radarChart", ChartType.Radar)]
    [InlineData("stockChart", ChartType.Stock)]
    public void TryReadSupportedChart_ReadsRadarAndStockChartFamilies(string chartElementName, ChartType expectedType)
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse($$"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <c:chart>
                <c:title><c:tx><c:rich><a:p><a:r><a:t>Market View</a:t></a:r></a:p></c:rich></c:tx></c:title>
                <c:plotArea>
                  <c:{{chartElementName}}>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:{{chartElementName}}>
                  <c:catAx><c:axId val="1"/><c:crossAx val="2"/></c:catAx>
                  <c:valAx><c:axId val="2"/><c:crossAx val="1"/></c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(expectedType);
        chart.Title.Should().Be("Market View");
        chart.DataRange.Start.Row.Should().Be(1);
        chart.DataRange.Start.Col.Should().Be(1);
        chart.DataRange.End.Row.Should().Be(4);
        chart.DataRange.End.Col.Should().Be(2);
        chart.FirstRowIsHeader.Should().BeTrue();
        chart.FirstColIsCategories.Should().BeTrue();
    }

    [Fact]
    public void TryReadSupportedChart_ReadsVolumeOpenHighLowCloseStockChart()
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:title><c:tx><c:rich><a:p><a:r><a:t>OHLCV</a:t></a:r></a:p></c:rich></c:tx></c:title>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:grouping val="clustered"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                  <c:stockChart>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:tx><c:strRef><c:f>Sheet1!$C$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:ser>
                      <c:idx val="2"/>
                      <c:order val="2"/>
                      <c:tx><c:strRef><c:f>Sheet1!$D$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$D$2:$D$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:ser>
                      <c:idx val="3"/>
                      <c:order val="3"/>
                      <c:tx><c:strRef><c:f>Sheet1!$E$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$E$2:$E$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:ser>
                      <c:idx val="4"/>
                      <c:order val="4"/>
                      <c:tx><c:strRef><c:f>Sheet1!$F$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$F$2:$F$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:hiLowLines/>
                    <c:upDownBars/>
                  </c:stockChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Stock);
        chart.StockSubtype.Should().Be(StockChartSubtype.VolumeOpenHighLowClose);
        chart.ShowHighLowLines.Should().BeTrue();
        chart.ShowUpDownBars.Should().BeTrue();
        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 6)));
    }
}
