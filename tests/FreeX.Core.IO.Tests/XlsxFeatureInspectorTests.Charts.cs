using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public partial class XlsxFeatureInspectorTests
{
    [Fact]
    public void Inspect_SupportedNativeChartPackage_DoesNotReportUnsupportedChart()
    {
        using var package = CreatePackageWithContent(("xl/charts/chart1.xml", """
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:title><c:tx><c:rich><a:p><a:r><a:t>Sales</a:t></a:r></a:p></c:rich></c:tx></c:title>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().NotContain(f => f.Kind == XlsxUnsupportedFeatureKind.Charts);
    }


    [Fact]
    public void Inspect_ChartStyleAndColorPartsAlone_DoNotReportUnsupportedChart()
    {
        using var package = CreatePackageWithContent(
            ("xl/charts/style1.xml", """
                <c:chartStyle xmlns:c="http://schemas.microsoft.com/office/drawing/2012/chartStyle"/>
                """),
            ("xl/charts/colors1.xml", """
                <c:colorStyle xmlns:c="http://schemas.microsoft.com/office/drawing/2012/chartStyle"/>
                """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().NotContain(f => f.Kind == XlsxUnsupportedFeatureKind.Charts);
    }


    [Theory]
    [InlineData("histogramChart")]
    [InlineData("waterfallChart")]
    [InlineData("treemapChart")]
    [InlineData("sunburstChart")]
    [InlineData("boxWhiskerChart")]
    [InlineData("funnelChart")]
    [InlineData("mapChart")]
    public void Inspect_AdvancedUnmodeledChartFamilies_ReportUnsupportedChart(string chartElementName)
    {
        using var package = CreatePackageWithContent(("xl/charts/chart1.xml", $$"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:{{chartElementName}}>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                    </c:ser>
                  </c:{{chartElementName}}>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().Contain(f => f.Kind == XlsxUnsupportedFeatureKind.Charts);
    }


    [Theory]
    [InlineData("surfaceChart")]
    [InlineData("surface3DChart")]
    public void Inspect_SurfaceChartsWithSourceRanges_DoesNotReportUnsupportedChart(string chartElementName)
    {
        using var package = CreatePackageWithContent(("xl/charts/chart1.xml", $$"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:{{chartElementName}}>
                    <c:ser>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:{{chartElementName}}>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().NotContain(f => f.Kind == XlsxUnsupportedFeatureKind.Charts);
    }


    [Theory]
    [InlineData("treemap")]
    [InlineData("sunburst")]
    [InlineData("clusteredColumn")]
    [InlineData("boxWhisker")]
    [InlineData("waterfall")]
    [InlineData("funnel")]
    public void Inspect_ChartExAdvancedFamiliesWithSourceRanges_DoesNotReportUnsupportedChart(string layoutId)
    {
        using var package = CreatePackageWithContent(("xl/charts/chart1.xml", BuildChartExPackageXml(layoutId)));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().NotContain(f => f.Kind == XlsxUnsupportedFeatureKind.Charts);
    }


    [Fact]
    public void Inspect_ChartExParetoWithSourceRanges_DoesNotReportUnsupportedChart()
    {
        using var package = CreatePackageWithContent(("xl/charts/chart1.xml", BuildChartExPackageXml("clusteredColumn", includeParetoLine: true)));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().NotContain(f => f.Kind == XlsxUnsupportedFeatureKind.Charts);
    }

}
