using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.Core.IO.Tests;

public partial class XlsxFeatureInspectorTests
{
    [Fact]
    public void Inspect_BarLineScatterComboChart_DoesNotReportUnsupportedChart()
    {
        using var package = CreatePackageWithContent(("xl/charts/chart1.xml", """
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
            """));

        var report = XlsxFeatureInspector.Inspect(package);

        report.Features.Should().NotContain(f => f.Kind == XlsxUnsupportedFeatureKind.Charts);
    }
}
