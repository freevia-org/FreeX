using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxChartPartReaderTests
{
    [Theory]
    [InlineData("surfaceChart", ChartType.Surface)]
    [InlineData("surface3DChart", ChartType.ThreeDSurface)]
    [InlineData("treemapChart", ChartType.Treemap)]
    [InlineData("sunburstChart", ChartType.Sunburst)]
    [InlineData("histogramChart", ChartType.Histogram)]
    [InlineData("boxWhiskerChart", ChartType.BoxAndWhisker)]
    [InlineData("waterfallChart", ChartType.Waterfall)]
    [InlineData("funnelChart", ChartType.Funnel)]
    public void TryReadSupportedChart_RecognizesAdvancedChartFamilies(string chartElementName, ChartType expectedType)
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse(BuildSingleSeriesChartXml(chartElementName));

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(expectedType);
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeTrue();
        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2)));
        chart.FirstRowIsHeader.Should().BeTrue();
        chart.FirstColIsCategories.Should().BeTrue();
    }

    [Fact]
    public void TryReadSupportedChart_RecognizesParetoHistogramAsRenderable()
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse(BuildSingleSeriesChartXml("histogramChart", """<c:paretoLine val="1"/>"""));

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Pareto);
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeTrue();
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
        ChartTypeSupport.IsAuthorable(chart.Type).Should().BeFalse();
        ChartTypeSupport.IsDeferredAuthoringFamily(chart.Type).Should().BeTrue();
    }

    [Theory]
    [InlineData("cx:treemapChart", ChartType.Treemap)]
    [InlineData("cx:sunburstChart", ChartType.Sunburst)]
    [InlineData("cx:histogramChart", ChartType.Histogram)]
    [InlineData("cx:boxWhiskerChart", ChartType.BoxAndWhisker)]
    [InlineData("cx:waterfallChart", ChartType.Waterfall)]
    [InlineData("cx:funnelChart", ChartType.Funnel)]
    public void TryReadSupportedChart_RecognizesAdvancedChartFamiliesInsideExtensions(
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
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeTrue();
        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2)));
    }

    [Fact]
    public void TryReadSupportedChart_RecognizesStandaloneChartExChartSpace()
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse("""
            <cx:chartSpace xmlns:cx="http://schemas.microsoft.com/office/drawing/2014/chartex"
                           xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <cx:chart>
                <cx:plotArea>
                  <cx:treemapChart>
                    <cx:ser>
                      <cx:tx><cx:strRef><cx:f>Sheet1!$B$1</cx:f></cx:strRef></cx:tx>
                      <cx:cat><cx:strRef><cx:f>Sheet1!$A$2:$A$4</cx:f></cx:strRef></cx:cat>
                      <cx:val><cx:numRef><cx:f>Sheet1!$B$2:$B$4</cx:f></cx:numRef></cx:val>
                    </cx:ser>
                  </cx:treemapChart>
                </cx:plotArea>
              </cx:chart>
            </cx:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Treemap);
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeTrue();
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
}
