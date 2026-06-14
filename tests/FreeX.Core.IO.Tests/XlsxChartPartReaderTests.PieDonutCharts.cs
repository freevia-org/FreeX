using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxChartPartReaderTests
{
    [Fact]
    public void TryReadSupportedChart_ReadsDoughnutPerPointFillColors()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:doughnutChart>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:dPt>
                        <c:idx val="0"/>
                        <c:spPr><a:solidFill><a:srgbClr val="92D050"/></a:solidFill></c:spPr>
                      </c:dPt>
                      <c:dPt>
                        <c:idx val="1"/>
                        <c:spPr><a:solidFill><a:schemeClr val="accent2"/></a:solidFill></c:spPr>
                      </c:dPt>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$3</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$3</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:doughnutChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Doughnut);
        chart.PointFillColors.Should().HaveCount(2);

        var point0 = chart.PointFillColors.Should()
            .ContainSingle(p => p.SeriesIndex == 0 && p.PointIndex == 0).Subject;
        point0.FillColor.Should().Be(new CellColor(0x92, 0xD0, 0x50));
        point0.FillThemeColor.Should().BeNull();

        var point1 = chart.PointFillColors.Should()
            .ContainSingle(p => p.SeriesIndex == 0 && p.PointIndex == 1).Subject;
        point1.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2));
        point1.FillColor.Should().BeNull();
    }

    [Fact]
    public void TryReadSupportedChart_ReadsPiePerPointFillColors()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:pieChart>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:dPt>
                        <c:idx val="2"/>
                        <c:spPr><a:solidFill><a:srgbClr val="FF0000"/></a:solidFill></c:spPr>
                      </c:dPt>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:pieChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Pie);
        chart.PointFillColors.Should().ContainSingle(p => p.SeriesIndex == 0 && p.PointIndex == 2)
            .Which.FillColor.Should().Be(new CellColor(0xFF, 0x00, 0x00));
    }

    [Fact]
    public void TryReadSupportedChart_DoughnutWithAllShowFlagsZero_DoesNotSetShowDataLabels()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:doughnutChart>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$3</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$3</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:dLbls>
                      <c:showLegendKey val="0"/>
                      <c:showVal val="0"/>
                      <c:showCatName val="0"/>
                      <c:showSerName val="0"/>
                      <c:showPercent val="0"/>
                      <c:showBubbleSize val="0"/>
                      <c:showLeaderLines val="1"/>
                    </c:dLbls>
                  </c:doughnutChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        // All show flags are 0 — labels element is present but effectively disabled
        chart.ShowDataLabels.Should().BeFalse();
    }

    [Fact]
    public void TryReadSupportedChart_DoughnutWithShowValTrue_SetsShowDataLabels()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:doughnutChart>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$3</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$3</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:dLbls>
                      <c:showLegendKey val="0"/>
                      <c:showVal val="1"/>
                      <c:showCatName val="0"/>
                      <c:showSerName val="0"/>
                      <c:showPercent val="0"/>
                      <c:showBubbleSize val="0"/>
                    </c:dLbls>
                  </c:doughnutChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.ShowDataLabels.Should().BeTrue();
        chart.ShowDataLabelValue.Should().BeTrue();
    }
}
