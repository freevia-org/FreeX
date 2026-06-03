namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxChartPartReaderTests
{
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
}
