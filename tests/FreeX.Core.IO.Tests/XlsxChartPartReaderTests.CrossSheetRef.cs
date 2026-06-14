using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Tests that chart series formulas referencing a sheet other than the chart's own sheet
/// (cross-sheet references) resolve the DataRange to the correct sheet's SheetId.
/// Regression guard for the bug where 'DataSheet'!$B$2:$B$6 on a chart hosted on
/// 'ChartSheet' produced a DataRange pointing at ChartSheet (which has no data), causing
/// empty column/bar charts when the referenced sheet had valid cell data.
/// </summary>
public sealed partial class XlsxChartPartReaderTests
{
    private static readonly SheetId ChartSheetId = SheetId.New();
    private static readonly SheetId DataSheetId = SheetId.New();

    private static IReadOnlyDictionary<string, SheetId> BuildTwoSheetResolver() =>
        new Dictionary<string, SheetId>(StringComparer.OrdinalIgnoreCase)
        {
            ["ChartSheet"] = ChartSheetId,
            ["DataSheet"] = DataSheetId,
        };

    [Fact]
    public void TryReadSupportedChart_ColumnChart_CrossSheetValRef_ResolvesDataRangeToDataSheet()
    {
        // chart hosted on ChartSheet; val references DataSheet rows 2-6
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
                      <c:val>
                        <c:numRef>
                          <c:f>'DataSheet'!$B$2:$B$6</c:f>
                          <c:numCache>
                            <c:formatCode>General</c:formatCode>
                            <c:ptCount val="5"/>
                            <c:pt idx="0"><c:v>10</c:v></c:pt>
                            <c:pt idx="1"><c:v>20</c:v></c:pt>
                            <c:pt idx="2"><c:v>30</c:v></c:pt>
                            <c:pt idx="3"><c:v>40</c:v></c:pt>
                            <c:pt idx="4"><c:v>50</c:v></c:pt>
                          </c:numCache>
                        </c:numRef>
                      </c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var resolver = BuildTwoSheetResolver();
        var read = XlsxChartPartReader.TryReadSupportedChart(chartXml, ChartSheetId, fallbackDataRange: null, sheetNameResolver: resolver, out var chart);

        read.Should().BeTrue();
        chart.Type.Should().Be(ChartType.Column);
        // DataRange must point at the DATA sheet, not the chart's own sheet
        chart.DataRange.Start.Sheet.Should().Be(DataSheetId,
            "the val formula references 'DataSheet', so the DataRange must use DataSheet's SheetId");
        chart.DataRange.End.Sheet.Should().Be(DataSheetId);
        chart.DataRange.Start.Row.Should().Be(2);
        chart.DataRange.End.Row.Should().Be(6);
    }

    [Fact]
    public void TryReadSupportedChart_ColumnChart_CrossSheetValRef_WithoutResolver_UsesChartSheetId()
    {
        // Without a resolver, existing behaviour: sheetId fallback is ChartSheet
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:val><c:numRef><c:f>'DataSheet'!$B$2:$B$6</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        // Without resolver — falls back to chart-sheet Id (old behaviour, DataRange wrong sheet)
        var read = XlsxChartPartReader.TryReadSupportedChart(chartXml, ChartSheetId, out var chart);

        read.Should().BeTrue();
        // Without a resolver the chart still parses (formula is syntactically valid cell range)
        // but the sheet in the DataRange is the fallback chart-sheet id.
        chart.DataRange.Start.Sheet.Should().Be(ChartSheetId,
            "without a sheet-name resolver the fallback sheetId (chart host sheet) is used");
    }

    [Fact]
    public void TryReadSupportedChart_ColumnChart_SameSheetRef_ResolverHasNoEffect()
    {
        // When the formula references the SAME sheet as the chart, the resolver should still
        // return the same sheetId (no change in behaviour).
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:cat><c:strRef><c:f>'ChartSheet'!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>'ChartSheet'!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var resolver = BuildTwoSheetResolver();
        var read = XlsxChartPartReader.TryReadSupportedChart(chartXml, ChartSheetId, fallbackDataRange: null, sheetNameResolver: resolver, out var chart);

        read.Should().BeTrue();
        chart.DataRange.Start.Sheet.Should().Be(ChartSheetId,
            "formula references 'ChartSheet' which resolves to ChartSheetId — same as the fallback");
    }
}
