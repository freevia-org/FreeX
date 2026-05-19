using System.Xml.Linq;
using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public sealed class XlsxChartPartReaderTests
{
    [Theory]
    [InlineData("surfaceChart", ChartType.Surface)]
    [InlineData("treemapChart", ChartType.Treemap)]
    [InlineData("sunburstChart", ChartType.Sunburst)]
    [InlineData("histogramChart", ChartType.Histogram)]
    [InlineData("boxWhiskerChart", ChartType.BoxAndWhisker)]
    [InlineData("waterfallChart", ChartType.Waterfall)]
    [InlineData("funnelChart", ChartType.Funnel)]
    public void TryReadSupportedChart_RecognizesDeferredAdvancedChartFamilies(string chartElementName, ChartType expectedType)
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse(BuildSingleSeriesChartXml(chartElementName));

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(expectedType);
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeFalse();
        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2)));
        chart.FirstRowIsHeader.Should().BeTrue();
        chart.FirstColIsCategories.Should().BeTrue();
    }

    [Fact]
    public void TryReadSupportedChart_RecognizesParetoHistogramAsDeferred()
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse(BuildSingleSeriesChartXml("histogramChart", """<c:paretoLine val="1"/>"""));

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Pareto);
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeFalse();
    }

    [Fact]
    public void TryReadSupportedChart_RecognizesMapChartExtensionAsDeferred()
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                          xmlns:c16="http://schemas.microsoft.com/office/drawing/2014/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:extLst>
                    <c:ext uri="{797F5495-87CF-4200-8BC9-4A52B092F858}">
                      <c16:geoChart>
                        <c16:ser>
                          <c16:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c16:tx>
                          <c16:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c16:cat>
                          <c16:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c16:val>
                        </c16:ser>
                      </c16:geoChart>
                    </c:ext>
                  </c:extLst>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Map);
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeFalse();
    }

    [Theory]
    [InlineData("cx:treemapChart", ChartType.Treemap)]
    [InlineData("cx:sunburstChart", ChartType.Sunburst)]
    [InlineData("cx:histogramChart", ChartType.Histogram)]
    [InlineData("cx:boxWhiskerChart", ChartType.BoxAndWhisker)]
    [InlineData("cx:waterfallChart", ChartType.Waterfall)]
    [InlineData("cx:funnelChart", ChartType.Funnel)]
    public void TryReadSupportedChart_RecognizesDeferredAdvancedChartFamiliesInsideExtensions(
        string chartElementName,
        ChartType expectedType)
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse($$"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:cx="http://schemas.microsoft.com/office/drawing/2014/chartex"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:extLst>
                    <c:ext uri="{C3380CC4-5D6E-409C-BE32-E72D297353CC}">
                      <{{chartElementName}}>
                        <cx:ser>
                          <cx:tx><cx:strRef><cx:f>Sheet1!$B$1</cx:f></cx:strRef></cx:tx>
                          <cx:cat><cx:strRef><cx:f>Sheet1!$A$2:$A$4</cx:f></cx:strRef></cx:cat>
                          <cx:val><cx:numRef><cx:f>Sheet1!$B$2:$B$4</cx:f></cx:numRef></cx:val>
                        </cx:ser>
                      </{{chartElementName}}>
                    </c:ext>
                  </c:extLst>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(expectedType);
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeFalse();
        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2)));
    }

    [Fact]
    public void TryReadSupportedChart_DoesNotLetAdvancedExtensionOverrideDirectSupportedChart()
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:cx="http://schemas.microsoft.com/office/drawing/2014/chartex">
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
                  <c:extLst>
                    <c:ext uri="{C3380CC4-5D6E-409C-BE32-E72D297353CC}">
                      <cx:treemapChart>
                        <cx:ser>
                          <cx:val><cx:numRef><cx:f>Sheet1!$D$2:$D$4</cx:f></cx:numRef></cx:val>
                        </cx:ser>
                      </cx:treemapChart>
                    </c:ext>
                  </c:extLst>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Column);
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeTrue();
    }

    [Theory]
    [InlineData("radarChart", ChartType.Radar)]
    [InlineData("stockChart", ChartType.Stock)]
    public void TryReadSupportedChart_ReadsRadarAndStockChartFamilies(string chartElementName, ChartType expectedType)
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse($$"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
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

    private static string BuildSingleSeriesChartXml(string chartElementName, string chartBody = "") =>
        $$"""
          <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                        xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
            <c:chart>
              <c:title><c:tx><c:rich><a:p><a:r><a:t>Advanced</a:t></a:r></a:p></c:rich></c:tx></c:title>
              <c:plotArea>
                <c:{{chartElementName}}>
                  {{chartBody}}
                  <c:ser>
                    <c:idx val="0"/>
                    <c:order val="0"/>
                    <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                    <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                    <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                  </c:ser>
                </c:{{chartElementName}}>
              </c:plotArea>
            </c:chart>
          </c:chartSpace>
          """;

    [Fact]
    public void TryReadSupportedChart_ReadsColumnChartRangeTitleAndThemeSeriesFill()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:title><c:tx><c:rich><a:p><a:r><a:t>Sales</a:t></a:r></a:p></c:rich></c:tx></c:title>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:spPr>
                        <a:solidFill><a:schemeClr val="accent2"/></a:solidFill>
                      </c:spPr>
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

        chart.Type.Should().Be(ChartType.Column);
        chart.Title.Should().Be("Sales");
        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2)));
        chart.FirstRowIsHeader.Should().BeTrue();
        chart.FirstColIsCategories.Should().BeTrue();
        chart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2)));
    }

    [Fact]
    public void TryReadSupportedChart_ReadsPivotSourceBinding()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:style val="42"/>
              <c:pivotSource>
                <c:name>Data!PivotTable1</c:name>
                <c:fmtId val="0"/>
              </c:pivotSource>
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:tx><c:strRef><c:f>Data!$E$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Data!$D$2:$D$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Data!$E$2:$E$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.IsPivotChart.Should().BeTrue();
        chart.PivotTableName.Should().Be("PivotTable1");
        chart.ChartStyleId.Should().Be(42);
    }

    [Fact]
    public void TryReadSupportedChart_ReadsBarDirection()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
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
    public void TryReadSupportedChart_ClearsUnsupportedPercentageDataLabels()
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
                    <c:dLbls>
                      <c:showVal val="1"/>
                      <c:showPercent val="1"/>
                    </c:dLbls>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Column);
        chart.ShowDataLabels.Should().BeTrue();
        chart.ShowDataLabelPercentage.Should().BeFalse();
    }

    [Fact]
    public void TryReadSupportedChart_ClearsUnsupportedSecondaryAxisState()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:grouping val="stacked"/>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="10"/>
                    <c:axId val="20"/>
                  </c:barChart>
                  <c:catAx>
                    <c:axId val="10"/>
                  </c:catAx>
                  <c:valAx>
                    <c:axId val="20"/>
                    <c:axPos val="r"/>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.StackedColumn);
        chart.ShowSecondaryAxis.Should().BeFalse();
        chart.SecondaryAxisSeriesIndexes.Should().BeEmpty();
    }

    [Fact]
    public void TryReadSupportedChart_ClearsUnsupportedComboLineOverlayState()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="bar"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                  <c:lineChart>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:lineChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Bar);
        chart.UseComboLineForSecondarySeries.Should().BeFalse();
        chart.ComboLineSeriesIndexes.Should().BeEmpty();
    }

    [Fact]
    public void TryReadSupportedChart_ClearsUnsupportedTrendlineState()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:pieChart>
                    <c:ser>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                      <c:trendline>
                        <c:trendlineType val="poly"/>
                        <c:order val="4"/>
                        <c:dispEq val="1"/>
                        <c:dispRSqr val="1"/>
                        <c:spPr>
                          <a:ln w="31750">
                            <a:solidFill><a:srgbClr val="D95319"/></a:solidFill>
                            <a:prstDash val="dot"/>
                          </a:ln>
                        </c:spPr>
                      </c:trendline>
                    </c:ser>
                  </c:pieChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Pie);
        chart.ShowLinearTrendline.Should().BeFalse();
        chart.TrendlineType.Should().Be(ChartTrendlineType.Linear);
        chart.TrendlineOrder.Should().Be(2);
        chart.ShowTrendlineEquation.Should().BeFalse();
        chart.ShowTrendlineRSquared.Should().BeFalse();
        chart.TrendlineColor.Should().BeNull();
        chart.TrendlineThickness.Should().Be(1.5);
        chart.TrendlineDashStyle.Should().Be(ChartLineDashStyle.Dash);
    }

    [Fact]
    public void TryReadSupportedChart_ClearsUnsupportedAxisStateForNoAxisCharts()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:pieChart>
                    <c:ser>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:pieChart>
                  <c:catAx>
                    <c:title>
                      <c:tx><c:rich><a:p><a:r><a:rPr sz="1800"><a:solidFill><a:srgbClr val="C00000"/></a:solidFill></a:rPr><a:t>Category Axis</a:t></a:r></a:p></c:rich></c:tx>
                    </c:title>
                    <c:majorGridlines><c:spPr><a:ln w="25400"><a:solidFill><a:srgbClr val="70AD47"/></a:solidFill></a:ln></c:spPr></c:majorGridlines>
                    <c:minorGridlines/>
                    <c:majorTickMark val="in"/>
                    <c:minorTickMark val="out"/>
                    <c:tickLblPos val="none"/>
                    <c:spPr><a:ln w="38100"><a:solidFill><a:srgbClr val="4472C4"/></a:solidFill></a:ln></c:spPr>
                  </c:catAx>
                  <c:valAx>
                    <c:title><c:tx><c:rich><a:p><a:r><a:t>Value Axis</a:t></a:r></a:p></c:rich></c:tx></c:title>
                    <c:scaling>
                      <c:logBase val="10"/>
                      <c:min val="1"/>
                      <c:max val="100"/>
                    </c:scaling>
                    <c:majorUnit val="10"/>
                    <c:minorUnit val="2"/>
                    <c:numFmt formatCode="0.00"/>
                    <c:majorGridlines/>
                    <c:minorGridlines/>
                    <c:majorTickMark val="in"/>
                    <c:minorTickMark val="out"/>
                    <c:tickLblPos val="none"/>
                    <c:spPr><a:ln w="38100"><a:solidFill><a:srgbClr val="ED7D31"/></a:solidFill></a:ln></c:spPr>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Pie);
        chart.XAxisTitle.Should().BeNull();
        chart.YAxisTitle.Should().BeNull();
        chart.AxisTitleTextColor.Should().BeNull();
        chart.AxisTitleFontSize.Should().Be(12);
        chart.XAxisMinimum.Should().BeNull();
        chart.XAxisMaximum.Should().BeNull();
        chart.XAxisMajorUnit.Should().BeNull();
        chart.XAxisMinorUnit.Should().BeNull();
        chart.XAxisLogScale.Should().BeFalse();
        chart.ShowXAxisMajorGridlines.Should().BeFalse();
        chart.ShowXAxisMinorGridlines.Should().BeFalse();
        chart.XAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        chart.XAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        chart.ShowXAxisLabels.Should().BeTrue();
        chart.XAxisLineColor.Should().BeNull();
        chart.YAxisMinimum.Should().BeNull();
        chart.YAxisMaximum.Should().BeNull();
        chart.YAxisMajorUnit.Should().BeNull();
        chart.YAxisMinorUnit.Should().BeNull();
        chart.YAxisLogScale.Should().BeFalse();
        chart.YAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        chart.ShowYAxisMajorGridlines.Should().BeFalse();
        chart.ShowYAxisMinorGridlines.Should().BeFalse();
        chart.YAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        chart.YAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        chart.ShowYAxisLabels.Should().BeTrue();
        chart.YAxisLineColor.Should().BeNull();
    }

    [Fact]
    public void TryReadSupportedChart_ClearsOutOfRangeExplodedSliceState()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:pieChart>
                    <c:ser>
                      <c:dPt>
                        <c:idx val="99"/>
                        <c:explosion val="25"/>
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
        chart.ExplodedSliceIndex.Should().Be(-1);
        chart.ExplodedSliceDistance.Should().Be(0.1);
    }

    [Fact]
    public void TryReadSupportedChart_DropsOutOfRangePointDataLabelFormatting()
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
                      <c:idx val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                      <c:dLbls>
                        <c:dLbl>
                          <c:idx val="99"/>
                          <c:spPr><a:solidFill><a:srgbClr val="70AD47"/></a:solidFill></c:spPr>
                        </c:dLbl>
                      </c:dLbls>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Column);
        chart.ShowDataLabels.Should().BeFalse();
        chart.PointDataLabelFormats.Should().BeEmpty();
    }

    [Fact]
    public void TryReadSupportedChart_DropsNegativeSeriesFormattingIndexes()
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
                      <c:idx val="-1"/>
                      <c:spPr><a:solidFill><a:srgbClr val="70AD47"/></a:solidFill></c:spPr>
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

        chart.Type.Should().Be(ChartType.Column);
        chart.SeriesFormats.Should().BeEmpty();
    }

    [Fact]
    public void TryReadSupportedChart_DropsPointDataLabelFormattingForNegativeSeriesIndexes()
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
                      <c:idx val="-1"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                      <c:dLbls>
                        <c:dLbl>
                          <c:idx val="0"/>
                          <c:spPr><a:solidFill><a:srgbClr val="70AD47"/></a:solidFill></c:spPr>
                        </c:dLbl>
                      </c:dLbls>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Column);
        chart.PointDataLabelFormats.Should().BeEmpty();
    }

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
}
