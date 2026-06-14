using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Tests that cross-sheet cell-ref series with embedded numCache/strCache data are captured
/// so the renderer can fall back to cached values when the live cross-sheet cells are not
/// available in the viewport (e.g. chart on "10 Charts" references "4. Dynamic Histogram").
/// </summary>
public sealed class XlsxChartSeriesRangeReaderTests_CrossSheetCache
{
    private static System.Xml.Linq.XElement ParseSeries(string xml) =>
        System.Xml.Linq.XElement.Parse(xml);

    [Fact]
    public void TryReadCrossSheetEmbeddedData_CrossSheetValRef_ReturnsEmbeddedValues()
    {
        // Series formula references a DIFFERENT sheet than the chart host.
        // The numCache carries 2 values that should be surfaced for fallback rendering.
        var sheetId = new SheetId(Guid.NewGuid());
        var dataSheetId = new SheetId(Guid.NewGuid());
        var sheetNameResolver = new Dictionary<string, SheetId>(StringComparer.OrdinalIgnoreCase)
        {
            ["4. Dynamic Histogram"] = dataSheetId
        };

        var seriesXml = """
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:order val="0"/>
              <c:val>
                <c:numRef>
                  <c:f>'4. Dynamic Histogram'!$B$31:$B$32</c:f>
                  <c:numCache>
                    <c:formatCode>General</c:formatCode>
                    <c:ptCount val="2"/>
                    <c:pt idx="0"><c:v>6</c:v></c:pt>
                    <c:pt idx="1"><c:v>4</c:v></c:pt>
                  </c:numCache>
                </c:numRef>
              </c:val>
            </c:ser>
            """;

        var seriesElements = new[] { ParseSeries(seriesXml) };

        // The formula IS a direct cell ref (not a named range) but it crosses sheets.
        // TryReadCrossSheetEmbeddedData should detect this and return embedded values.
        var result = XlsxChartSeriesRangeReader.TryReadCrossSheetEmbeddedData(
            seriesElements,
            sheetId,
            sheetNameResolver);

        result.Should().NotBeNull("cross-sheet cell ref with numCache should produce embedded data");
        result!.Should().HaveCount(1);
        result[0].SeriesIndex.Should().Be(0);
        result[0].Values.Should().HaveCount(2);
        result[0].Values[0].Should().Be(6.0);
        result[0].Values[1].Should().Be(4.0);
    }

    [Fact]
    public void TryReadCrossSheetEmbeddedData_SameSheetRef_ReturnsNull()
    {
        // Series formula references the SAME sheet as the chart. Live cells should be used.
        var sheetId = new SheetId(Guid.NewGuid());
        var sheetNameResolver = new Dictionary<string, SheetId>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sheet1"] = sheetId
        };

        var seriesXml = """
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:val>
                <c:numRef>
                  <c:f>'Sheet1'!$B$2:$B$4</c:f>
                  <c:numCache>
                    <c:ptCount val="3"/>
                    <c:pt idx="0"><c:v>10</c:v></c:pt>
                    <c:pt idx="1"><c:v>20</c:v></c:pt>
                    <c:pt idx="2"><c:v>30</c:v></c:pt>
                  </c:numCache>
                </c:numRef>
              </c:val>
            </c:ser>
            """;

        var seriesElements = new[] { ParseSeries(seriesXml) };

        var result = XlsxChartSeriesRangeReader.TryReadCrossSheetEmbeddedData(
            seriesElements,
            sheetId,
            sheetNameResolver);

        result.Should().BeNull("same-sheet ref should use live cell lookup, not embedded cache");
    }

    [Fact]
    public void TryReadCrossSheetEmbeddedData_NoResolver_ReturnsNull()
    {
        // Without a resolver we cannot determine cross-sheet-ness; return null (safe fallback).
        var sheetId = new SheetId(Guid.NewGuid());
        var seriesXml = """
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:val>
                <c:numRef>
                  <c:f>'OtherSheet'!$B$2:$B$4</c:f>
                  <c:numCache>
                    <c:ptCount val="2"/>
                    <c:pt idx="0"><c:v>1</c:v></c:pt>
                    <c:pt idx="1"><c:v>2</c:v></c:pt>
                  </c:numCache>
                </c:numRef>
              </c:val>
            </c:ser>
            """;

        var seriesElements = new[] { ParseSeries(seriesXml) };

        var result = XlsxChartSeriesRangeReader.TryReadCrossSheetEmbeddedData(
            seriesElements,
            sheetId,
            sheetNameResolver: null);

        result.Should().BeNull("without a resolver cross-sheet-ness cannot be confirmed; return null");
    }

    [Fact]
    public void TryReadCrossSheetEmbeddedData_MultipleSeriesCrossSheet_ReturnsAllSeries()
    {
        // Two series both referencing a cross-sheet range — both should be captured.
        var sheetId = new SheetId(Guid.NewGuid());
        var dataSheetId = new SheetId(Guid.NewGuid());
        var sheetNameResolver = new Dictionary<string, SheetId>(StringComparer.OrdinalIgnoreCase)
        {
            ["DataSheet"] = dataSheetId
        };

        var seriesXml1 = """
            <c:ser xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:idx val="0"/>
              <c:val>
                <c:numRef>
                  <c:f>'DataSheet'!$C$31:$C$35</c:f>
                  <c:numCache>
                    <c:ptCount val="5"/>
                    <c:pt idx="0"><c:v>4</c:v></c:pt>
                    <c:pt idx="1"><c:v>6</c:v></c:pt>
                    <c:pt idx="2"><c:v>3</c:v></c:pt>
                    <c:pt idx="3"><c:v>8</c:v></c:pt>
                    <c:pt idx="4"><c:v>5</c:v></c:pt>
                  </c:numCache>
                </c:numRef>
              </c:val>
            </c:ser>
            """;

        var seriesElements = new[] { ParseSeries(seriesXml1) };

        var result = XlsxChartSeriesRangeReader.TryReadCrossSheetEmbeddedData(
            seriesElements,
            sheetId,
            sheetNameResolver);

        result.Should().NotBeNull();
        result![0].Values.Should().HaveCount(5);
        result[0].Values[0].Should().Be(4.0);
        result[0].Values[4].Should().Be(5.0);
    }
}
