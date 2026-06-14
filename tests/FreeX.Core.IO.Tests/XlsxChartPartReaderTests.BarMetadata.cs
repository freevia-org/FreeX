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
        var chartXml = ParseChartXml("""
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
        var chartXml = ParseChartXml("""
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
        var chartXml = ParseChartXml("""
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
        var chartXml = ParseChartXml("""
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

    /// <summary>
    /// Regression test: when a chart series uses named-range formulas (e.g. OFFSET-based
    /// dynamic ranges like <c>'Sheet1'!rngCount</c>) the reader must fall back to the
    /// embedded numCache/strCache values and expose them via
    /// <see cref="ChartModel.EmbeddedSeriesData"/> so the renderer can draw bars without recalc.
    /// </summary>
    [Fact]
    public void TryReadSupportedChart_NamedRangeValCat_PopulatesEmbeddedSeriesData()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        // Chart20-style: both cat and val formulas reference named ranges (non-cell-address)
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:grouping val="clustered"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx>
                        <c:strRef>
                          <c:f>'Sheet1'!$E$13</c:f>
                          <c:strCache>
                            <c:ptCount val="1"/>
                            <c:pt idx="0"><c:v>Frequency</c:v></c:pt>
                          </c:strCache>
                        </c:strRef>
                      </c:tx>
                      <c:cat>
                        <c:strRef>
                          <c:f>'Sheet1'!rngGroups</c:f>
                          <c:strCache>
                            <c:ptCount val="2"/>
                            <c:pt idx="0"><c:v>10-44</c:v></c:pt>
                            <c:pt idx="1"><c:v>45-80</c:v></c:pt>
                          </c:strCache>
                        </c:strRef>
                      </c:cat>
                      <c:val>
                        <c:numRef>
                          <c:f>'Sheet1'!rngCount</c:f>
                          <c:numCache>
                            <c:formatCode>General</c:formatCode>
                            <c:ptCount val="2"/>
                            <c:pt idx="0"><c:v>64</c:v></c:pt>
                            <c:pt idx="1"><c:v>36</c:v></c:pt>
                          </c:numCache>
                        </c:numRef>
                      </c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue("chart should load even when formulas reference named ranges");

        chart.Type.Should().Be(ChartType.Column);
        chart.EmbeddedSeriesData.Should().NotBeNull()
            .And.HaveCount(1);

        var series0 = chart.EmbeddedSeriesData![0];
        series0.SeriesIndex.Should().Be(0);
        series0.SeriesName.Should().Be("Frequency");
        series0.Categories.Should().Equal("10-44", "45-80");
        series0.Values.Should().HaveCount(2);
        series0.Values[0].Should().Be(64.0);
        series0.Values[1].Should().Be(36.0);
    }

    /// <summary>
    /// When a chart series uses normal cell-range formulas (not named ranges), the
    /// <see cref="ChartModel.EmbeddedSeriesData"/> must be null (normal cell-lookup path used).
    /// </summary>
    [Fact]
    public void TryReadSupportedChart_NormalCellRangeFormulas_EmbeddedSeriesDataIsNull()
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
                      <c:cat>
                        <c:strRef>
                          <c:f>Sheet1!$A$2:$A$4</c:f>
                          <c:strCache>
                            <c:ptCount val="3"/>
                            <c:pt idx="0"><c:v>A</c:v></c:pt>
                            <c:pt idx="1"><c:v>B</c:v></c:pt>
                            <c:pt idx="2"><c:v>C</c:v></c:pt>
                          </c:strCache>
                        </c:strRef>
                      </c:cat>
                      <c:val>
                        <c:numRef>
                          <c:f>Sheet1!$B$2:$B$4</c:f>
                          <c:numCache>
                            <c:ptCount val="3"/>
                            <c:pt idx="0"><c:v>10</c:v></c:pt>
                            <c:pt idx="1"><c:v>20</c:v></c:pt>
                            <c:pt idx="2"><c:v>30</c:v></c:pt>
                          </c:numCache>
                        </c:numRef>
                      </c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.EmbeddedSeriesData.Should().BeNull("normal cell-range formulas should use the cell-lookup path");
    }
}
