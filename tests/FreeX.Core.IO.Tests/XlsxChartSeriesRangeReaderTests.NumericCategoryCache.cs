using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Tests that a numeric/date category axis backed by a &lt;c:numCache&gt; (rather than the more
/// common &lt;c:strCache&gt;) is still surfaced as embedded category labels. Excel writes
/// &lt;c:cat&gt;&lt;c:numRef&gt;...&lt;c:numCache&gt; whenever the referenced cells hold numbers or
/// dates (e.g. a dynamic-range chart whose category formula is a defined name resolving to a
/// column of dates). Before the fix, ReadEmbeddedStringCacheValues only looked for strCache and
/// returned an empty list for numCache-backed categories, leaving the chart's x-axis blank.
/// </summary>
public sealed class XlsxChartSeriesRangeReaderTests_NumericCategoryCache
{
    private static System.Xml.Linq.XElement ParseSeries(string xml) =>
        System.Xml.Linq.XElement.Parse(xml);

    [Fact]
    public void TryReadEmbeddedSeriesData_NumericCategoryCache_ReturnsCategoryLabels()
    {
        // val/cat formulas are both named ranges (not direct cell addresses), so the
        // named-range embedded-cache fallback path is used. The category axis is bound to a
        // defined name resolving to a column of dates, so Excel emits numRef/numCache (not
        // strRef/strCache) for the "cat" container.
        var sheetId = new SheetId(Guid.NewGuid());

        var seriesXml = """
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:tx>
                <c:strRef>
                  <c:f>rngHeader</c:f>
                  <c:strCache>
                    <c:ptCount val="1"/>
                    <c:pt idx="0"><c:v>Sales</c:v></c:pt>
                  </c:strCache>
                </c:strRef>
              </c:tx>
              <c:cat>
                <c:numRef>
                  <c:f>rngDates</c:f>
                  <c:numCache>
                    <c:formatCode>m/d/yyyy</c:formatCode>
                    <c:ptCount val="2"/>
                    <c:pt idx="0"><c:v>44197</c:v></c:pt>
                    <c:pt idx="1"><c:v>44228</c:v></c:pt>
                  </c:numCache>
                </c:numRef>
              </c:cat>
              <c:val>
                <c:numRef>
                  <c:f>rngSales</c:f>
                  <c:numCache>
                    <c:ptCount val="2"/>
                    <c:pt idx="0"><c:v>10</c:v></c:pt>
                    <c:pt idx="1"><c:v>20</c:v></c:pt>
                  </c:numCache>
                </c:numRef>
              </c:val>
            </c:ser>
            """;

        var seriesElements = new[] { ParseSeries(seriesXml) };

        var result = XlsxChartSeriesRangeReader.TryReadEmbeddedSeriesData(seriesElements, sheetId);

        result.Should().NotBeNull("named-range val/cat formulas with a numCache should produce embedded data");
        result!.Should().HaveCount(1);
        result[0].SeriesName.Should().Be("Sales");

        // Before the fix, Categories was always [] for a numCache-backed "cat" container.
        result[0].Categories.Should().HaveCount(2, "numCache category values should not be dropped");
        result[0].Categories[0].Should().Be("44197");
        result[0].Categories[1].Should().Be("44228");

        result[0].Values.Should().HaveCount(2);
        result[0].Values[0].Should().Be(10.0);
        result[0].Values[1].Should().Be(20.0);
    }
}
