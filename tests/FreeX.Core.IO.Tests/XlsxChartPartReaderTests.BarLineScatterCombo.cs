using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxChartPartReaderTests
{
    [Fact]
    public void TryReadSupportedChart_BarLineScatterCombo_ReturnsTrueWithColumnType()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                  <c:lineChart>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:tx><c:strRef><c:f>Sheet1!$C$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:lineChart>
                  <c:scatterChart>
                    <c:ser>
                      <c:idx val="2"/>
                      <c:order val="2"/>
                      <c:tx><c:strRef><c:f>Sheet1!$D$1</c:f></c:strRef></c:tx>
                      <c:xVal><c:numRef><c:f>Sheet1!$A$2:$A$4</c:f></c:numRef></c:xVal>
                      <c:yVal><c:numRef><c:f>Sheet1!$D$2:$D$4</c:f></c:numRef></c:yVal>
                    </c:ser>
                  </c:scatterChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var result = XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart);

        result.Should().BeTrue();
        chart.Type.Should().Be(ChartType.Column);
        chart.ComboLineSeriesIndexes.Should().Equal([1]);
        chart.ComboScatterSeriesIndexes.Should().Equal([2]);
    }

    [Fact]
    public void TryReadSupportedChart_BarLineScatterCombo_MultipleBarSeriesWithComboIndexes()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$5</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$5</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$5</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$5</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                  <c:lineChart>
                    <c:ser>
                      <c:idx val="2"/>
                      <c:order val="2"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$5</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$D$2:$D$5</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:lineChart>
                  <c:scatterChart>
                    <c:ser>
                      <c:idx val="3"/>
                      <c:order val="3"/>
                      <c:xVal><c:numRef><c:f>Sheet1!$A$2:$A$5</c:f></c:numRef></c:xVal>
                      <c:yVal><c:numRef><c:f>Sheet1!$E$2:$E$5</c:f></c:numRef></c:yVal>
                    </c:ser>
                  </c:scatterChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var result = XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart);

        result.Should().BeTrue();
        chart.Type.Should().Be(ChartType.Column);
        chart.ComboLineSeriesIndexes.Should().Equal([2]);
        chart.ComboScatterSeriesIndexes.Should().Equal([3]);
    }

    [Fact]
    public void TryReadSupportedChart_ScatterOnly_NotMisroutedToCombo()
    {
        // A pure scatter chart (no barChart) must NOT be treated as a combo
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:scatterChart>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
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
        chart.ComboScatterSeriesIndexes.Should().BeEmpty();
    }
}
