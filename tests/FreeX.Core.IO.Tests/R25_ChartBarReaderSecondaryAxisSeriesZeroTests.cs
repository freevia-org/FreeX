using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R25-chart-axis-series-deep-1: the bar/line/scatter combo reader (XlsxChartPartReader.Bar.cs)
/// used to unconditionally skip adding series index 0 to <see cref="ChartModel.SecondaryAxisSeriesIndexes"/>
/// even when that series' own plot element (&lt;c:barChart&gt;/&lt;c:lineChart&gt;/&lt;c:scatterChart&gt;)
/// declared the secondary axId — real Excel allows "Format Data Series &gt; Secondary Axis" on any
/// series regardless of position, exactly like it already does for <see cref="ChartModel.ComboLineSeriesIndexes"/>
/// (see the "Do NOT drop index 0" comment already in XlsxChartPartReader.Bar.cs).
/// </summary>
public sealed class R25_ChartBarReaderSecondaryAxisSeriesZeroTests
{
    private static XDocument ParseChartXml(string xml) => XDocument.Parse(xml);

    [Fact]
    public void TryReadSupportedChart_BarLineCombo_SecondaryAxisAtNonZeroIndex_StillWorks()
    {
        // Representative already-working case (sibling of the bug): the LINE series is idx 1 (not 0)
        // and lives on the secondary value axis; the BAR series is idx 0 on the primary axis. This is
        // the common/legacy layout the reader already handled correctly and must keep handling after
        // the index-0 fix below.
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
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="111"/>
                    <c:axId val="222"/>
                  </c:barChart>
                  <c:lineChart>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:tx><c:strRef><c:f>Sheet1!$C$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="111"/>
                    <c:axId val="333"/>
                  </c:lineChart>
                  <c:catAx>
                    <c:axId val="111"/>
                    <c:axPos val="b"/>
                    <c:crossAx val="222"/>
                  </c:catAx>
                  <c:valAx>
                    <c:axId val="222"/>
                    <c:axPos val="l"/>
                    <c:crossAx val="111"/>
                  </c:valAx>
                  <c:valAx>
                    <c:axId val="333"/>
                    <c:axPos val="r"/>
                    <c:crossAx val="111"/>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var result = XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart);

        result.Should().BeTrue();
        chart.Type.Should().Be(ChartType.Column);
        chart.ShowSecondaryAxis.Should().BeTrue();
        chart.SecondaryAxisSeriesIndexes.Should().Equal(1);
    }

    [Fact]
    public void TryReadSupportedChart_PlainBarChart_SecondaryAxisAtNonZeroIndex_StillWorks()
    {
        // Same "already-working" proof for the non-combo TryReadBarChart path. Series 0 stays on the
        // PRIMARY value axis and series 1 is moved to the SECONDARY — which OOXML expresses as two
        // <c:barChart> plot groups (a single group's axId set applies to ALL its series, so same-group
        // series cannot straddle two value axes). Group 0 references the primary valAx (222); group 1
        // references the secondary valAx (333, axPos="r").
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
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="111"/>
                    <c:axId val="222"/>
                  </c:barChart>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:tx><c:strRef><c:f>Sheet1!$C$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="111"/>
                    <c:axId val="333"/>
                  </c:barChart>
                  <c:catAx>
                    <c:axId val="111"/>
                    <c:axPos val="b"/>
                    <c:crossAx val="222"/>
                  </c:catAx>
                  <c:valAx>
                    <c:axId val="222"/>
                    <c:axPos val="l"/>
                    <c:crossAx val="111"/>
                  </c:valAx>
                  <c:valAx>
                    <c:axId val="333"/>
                    <c:axPos val="r"/>
                    <c:crossAx val="111"/>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var result = XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart);

        result.Should().BeTrue();
        chart.Type.Should().Be(ChartType.Column);
        chart.ShowSecondaryAxis.Should().BeTrue();
        chart.SecondaryAxisSeriesIndexes.Should().Equal(1);
    }

    [Fact]
    public void TryReadSupportedChart_BarLineCombo_LineAtIndexZeroOnSecondaryAxis_ShouldPreserveSecondaryAxis()
    {
        // Mirrors the finding's failure scenario: a Line series at idx 0 ("Utilization %") plotted on
        // the SECONDARY value axis, and a Column series at idx 1 ("Headcount") on the primary axis —
        // a legal Excel layout (Format Data Series > Secondary Axis works on any series regardless of
        // position).
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:tx><c:strRef><c:f>Sheet1!$C$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="111"/>
                    <c:axId val="222"/>
                  </c:barChart>
                  <c:lineChart>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="111"/>
                    <c:axId val="333"/>
                  </c:lineChart>
                  <c:catAx>
                    <c:axId val="111"/>
                    <c:axPos val="b"/>
                    <c:crossAx val="222"/>
                  </c:catAx>
                  <c:valAx>
                    <c:axId val="222"/>
                    <c:axPos val="l"/>
                    <c:crossAx val="111"/>
                  </c:valAx>
                  <c:valAx>
                    <c:axId val="333"/>
                    <c:axPos val="r"/>
                    <c:crossAx val="111"/>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var result = XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart);

        result.Should().BeTrue();
        chart.Type.Should().Be(ChartType.Column);
        chart.ShowSecondaryAxis.Should().BeTrue();
        chart.SecondaryAxisSeriesIndexes.Should().Contain(0);
    }

    [Fact]
    public void TryReadSupportedChart_AreaChart_SeriesZeroOnSecondaryAxis_ShouldPreserveSecondaryAxis()
    {
        // Same bug pattern as the bar reader, in XlsxChartPartReader.Area.cs: two <c:areaChart> plot
        // groups where the idx-0 series lives on the SECONDARY value axis (axPos="r") and the idx-1
        // series stays on the primary. The reader used to strip index 0 (`&& seriesIndex > 0`).
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:areaChart>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="111"/>
                    <c:axId val="333"/>
                  </c:areaChart>
                  <c:areaChart>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:tx><c:strRef><c:f>Sheet1!$C$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="111"/>
                    <c:axId val="222"/>
                  </c:areaChart>
                  <c:catAx>
                    <c:axId val="111"/>
                    <c:axPos val="b"/>
                    <c:crossAx val="222"/>
                  </c:catAx>
                  <c:valAx>
                    <c:axId val="222"/>
                    <c:axPos val="l"/>
                    <c:crossAx val="111"/>
                  </c:valAx>
                  <c:valAx>
                    <c:axId val="333"/>
                    <c:axPos val="r"/>
                    <c:crossAx val="111"/>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var result = XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart);

        result.Should().BeTrue();
        chart.Type.Should().Be(ChartType.Area);
        chart.ShowSecondaryAxis.Should().BeTrue();
        chart.SecondaryAxisSeriesIndexes.Should().Contain(0);
    }

    [Fact]
    public void TryReadSupportedChart_LineChart_SeriesZeroOnSecondaryAxis_ShouldPreserveSecondaryAxis()
    {
        // XlsxChartPartReader.Line.cs (TryReadLineChart): two <c:lineChart> plot groups, the idx-0
        // line on the secondary value axis, the idx-1 line on the primary. Route: two lineCharts and
        // no bar/area/scatter -> TryReadLineChart.
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:lineChart>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="111"/>
                    <c:axId val="333"/>
                  </c:lineChart>
                  <c:lineChart>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:tx><c:strRef><c:f>Sheet1!$C$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="111"/>
                    <c:axId val="222"/>
                  </c:lineChart>
                  <c:catAx>
                    <c:axId val="111"/>
                    <c:axPos val="b"/>
                    <c:crossAx val="222"/>
                  </c:catAx>
                  <c:valAx>
                    <c:axId val="222"/>
                    <c:axPos val="l"/>
                    <c:crossAx val="111"/>
                  </c:valAx>
                  <c:valAx>
                    <c:axId val="333"/>
                    <c:axPos val="r"/>
                    <c:crossAx val="111"/>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var result = XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart);

        result.Should().BeTrue();
        chart.Type.Should().Be(ChartType.Line);
        chart.ShowSecondaryAxis.Should().BeTrue();
        chart.SecondaryAxisSeriesIndexes.Should().Contain(0);
    }

    [Fact]
    public void TryReadSupportedChart_ScatterChart_SeriesZeroOnSecondaryAxis_ShouldPreserveSecondaryAxis()
    {
        // XlsxChartPartReader.Scatter.cs: two <c:scatterChart> plot groups, the idx-0 series on the
        // secondary value axis (axPos="r") and the idx-1 series on the primary. The reader used to
        // strip index 0 (`&& modelSeriesIndex > 0`).
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:scatterChart>
                    <c:scatterStyle val="lineMarker"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:xVal><c:numRef><c:f>Sheet1!$A$2:$A$4</c:f></c:numRef></c:xVal>
                      <c:yVal><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:yVal>
                    </c:ser>
                    <c:axId val="111"/>
                    <c:axId val="333"/>
                  </c:scatterChart>
                  <c:scatterChart>
                    <c:scatterStyle val="lineMarker"/>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:tx><c:strRef><c:f>Sheet1!$C$1</c:f></c:strRef></c:tx>
                      <c:xVal><c:numRef><c:f>Sheet1!$A$2:$A$4</c:f></c:numRef></c:xVal>
                      <c:yVal><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:yVal>
                    </c:ser>
                    <c:axId val="111"/>
                    <c:axId val="222"/>
                  </c:scatterChart>
                  <c:valAx>
                    <c:axId val="111"/>
                    <c:axPos val="b"/>
                    <c:crossAx val="222"/>
                  </c:valAx>
                  <c:valAx>
                    <c:axId val="222"/>
                    <c:axPos val="l"/>
                    <c:crossAx val="111"/>
                  </c:valAx>
                  <c:valAx>
                    <c:axId val="333"/>
                    <c:axPos val="r"/>
                    <c:crossAx val="111"/>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var result = XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart);

        result.Should().BeTrue();
        chart.Type.Should().Be(ChartType.Scatter);
        chart.ShowSecondaryAxis.Should().BeTrue();
        chart.SecondaryAxisSeriesIndexes.Should().Contain(0);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_ColumnChart_SecondaryAxisSeriesZero_SurvivesSave()
    {
        // read -> write -> read: a Column chart whose FIRST series (index 0) is on the secondary axis
        // must round-trip through the XLSX writer (XlsxChartXmlWriter.GetSecondaryAxisSeriesIndexes)
        // instead of being silently dropped on save.
        var workbook = new Workbook("R25SecondaryAxisZero");
        var sheet = workbook.AddSheet("Sheet1");
        // A = categories, B = series 0 (secondary axis), C = series 1 (primary axis).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Cat"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Util%"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Headcount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(0.8));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(12));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(0.9));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(15));

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            ShowSecondaryAxis = true,
            // The FIRST series is the one on the secondary axis — the exact case the finding says was
            // dropped on save (GetSecondaryAxisSeriesIndexes filtered `index > 0`).
            SecondaryAxisSeriesIndexes = [0],
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 3)),
        };
        sheet.Charts.Add(chart);

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var reloaded = adapter.Load(ms).GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        reloaded.ShowSecondaryAxis.Should().BeTrue(
            "a chart whose only secondary-axis series is series 0 must still report a secondary axis after save+load");
        reloaded.SecondaryAxisSeriesIndexes.Should().Contain(0,
            "series 0's secondary-axis assignment must survive the XLSX writer instead of being filtered out");
    }
}
