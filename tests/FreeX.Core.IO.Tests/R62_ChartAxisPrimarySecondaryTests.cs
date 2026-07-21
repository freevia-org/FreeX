using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-62 io-chart-axis-a bucket:
///  - R62-io-chart-axis-6-1: a scatter/bubble chart's axis min/max/title/log/units were read from the
///    FIRST &lt;c:scatterChart&gt;/&lt;c:bubbleChart&gt; plot group's axId pair only -- when that first
///    group happens to be the SECONDARY-axis group, chart.YAxis* was silently populated from the
///    secondary axis's scale instead of the primary's, and the secondary axis's own settings were never
///    captured at all (ApplySecondaryAxisProperties was never invoked for scatter/bubble).
///  - R62-io-chart-axis-6-2: the secondary value axis's own majorUnit/minorUnit were never read, so
///    save always wrote the PRIMARY axis's majorUnit/minorUnit onto the secondary axis.
///  - R62-io-chart-axis-6-3: for horizontal Bar-family charts, the category axis's own tick-mark
///    styles and spPr line were written unconditionally to X* (not routed by physical axis position
///    like the neighboring gridline/tickLblPos/crosses properties), so ApplyValueAxisProperties (which
///    also targets X* for a horizontal Bar chart's value axis) immediately clobbered them.
/// </summary>
public sealed class R62_ChartAxisPrimarySecondaryTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    private static XDocument ParseChartXml(string xml) => XDocument.Parse(xml);

    [Fact]
    public void TryReadSupportedChart_ScatterChart_SecondaryAxisGroupListedFirst_PrimaryAxisScaleNotOverwrittenBySecondary()
    {
        // The FIRST <c:scatterChart> plot group (idx-0 series) is the SECONDARY-axis group (axId 111,
        // 333; valAx 333 has axPos="r"). The SECOND group (idx-1 series) is the PRIMARY-axis group
        // (axId 111, 222; valAx 222 has axPos="l"). Before the fix, ApplyAxisMetadata blindly read
        // axisIds from the first group only, so chart.YAxisMinimum/Maximum ended up populated from the
        // secondary axis's 0..1 range instead of the primary's 10..100 range, and the secondary axis's
        // own scale was never captured into chart.SecondaryAxis* at all.
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
                    <c:scaling><c:min val="10"/><c:max val="100"/></c:scaling>
                    <c:majorUnit val="20"/>
                    <c:crossAx val="111"/>
                  </c:valAx>
                  <c:valAx>
                    <c:axId val="333"/>
                    <c:axPos val="r"/>
                    <c:scaling><c:min val="0"/><c:max val="1"/></c:scaling>
                    <c:majorUnit val="0.1"/>
                    <c:crossAx val="111"/>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var result = XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart);

        result.Should().BeTrue();
        chart.Type.Should().Be(ChartType.Scatter);

        // The PRIMARY (left) axis's own 10..100 scale must land on chart.YAxis*, not the secondary
        // (right) axis's 0..1 scale.
        chart.YAxisMinimum.Should().Be(10, "the primary value axis's own minimum must be captured, not the secondary axis's");
        chart.YAxisMaximum.Should().Be(100, "the primary value axis's own maximum must be captured, not the secondary axis's");

        // The SECONDARY (right) axis's own 0..1 scale must be captured separately.
        chart.SecondaryAxisMinimum.Should().Be(0, "the secondary value axis's own minimum must be captured independently");
        chart.SecondaryAxisMaximum.Should().Be(1, "the secondary value axis's own maximum must be captured independently");
    }

    /// <summary>
    /// Sibling no-regression: the common/already-working layout where the PRIMARY group is declared
    /// FIRST (as in most real-world files and the existing R25 test's second group) must keep resolving
    /// to the same primary axis scale as before this fix.
    /// </summary>
    [Fact]
    public void TryReadSupportedChart_ScatterChart_PrimaryAxisGroupListedFirst_StillReadsPrimaryScaleCorrectly()
    {
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
                    <c:axId val="222"/>
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
                    <c:axId val="333"/>
                  </c:scatterChart>
                  <c:valAx>
                    <c:axId val="111"/>
                    <c:axPos val="b"/>
                    <c:crossAx val="222"/>
                  </c:valAx>
                  <c:valAx>
                    <c:axId val="222"/>
                    <c:axPos val="l"/>
                    <c:scaling><c:min val="10"/><c:max val="100"/></c:scaling>
                    <c:majorUnit val="20"/>
                    <c:crossAx val="111"/>
                  </c:valAx>
                  <c:valAx>
                    <c:axId val="333"/>
                    <c:axPos val="r"/>
                    <c:scaling><c:min val="0"/><c:max val="1"/></c:scaling>
                    <c:majorUnit val="0.1"/>
                    <c:crossAx val="111"/>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var result = XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart);

        result.Should().BeTrue();
        chart.YAxisMinimum.Should().Be(10);
        chart.YAxisMaximum.Should().Be(100);
        chart.SecondaryAxisMinimum.Should().Be(0);
        chart.SecondaryAxisMaximum.Should().Be(1);
    }

    [Fact]
    public void ComboChart_SecondaryAxisOwnMajorMinorUnit_RoundTripsIndependentlyOfPrimary()
    {
        var workbook = CreateColumnLineComboWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.YAxisMajorUnit = 20;
        chart.YAxisMinorUnit = 4;
        // Secondary (percentage) axis: its own much smaller unit -- must NOT be overwritten with the
        // primary axis's 20/4 on save.
        chart.SecondaryAxisMajorUnit = 0.1;
        chart.SecondaryAxisMinorUnit = 0.02;

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var valueAxes = chartXml.Descendants(ChartNs + "valAx").ToList();
        valueAxes.Should().HaveCount(2);
        var primaryAxis = valueAxes[0];
        var secondaryAxis = valueAxes[1];

        primaryAxis.Element(ChartNs + "majorUnit")!.Attribute("val")!.Value.Should().Be("20");
        secondaryAxis.Element(ChartNs + "majorUnit")!.Attribute("val")!.Value.Should().Be("0.1",
            "the secondary axis's own majorUnit must survive the save, not the primary axis's");
        secondaryAxis.Element(ChartNs + "minorUnit")!.Attribute("val")!.Value.Should().Be("0.02",
            "the secondary axis's own minorUnit must survive the save, not the primary axis's");

        var reloaded = ReloadSingleChart(saved);
        reloaded.YAxisMajorUnit.Should().Be(20);
        reloaded.SecondaryAxisMajorUnit.Should().Be(0.1);
        reloaded.SecondaryAxisMinorUnit.Should().Be(0.02);
    }

    /// <summary>
    /// Sibling no-regression: a secondary axis with no explicit unit of its own (the only shape the
    /// writer supported before the fix) must keep falling back to cloning the primary axis's
    /// majorUnit/minorUnit, matching prior behavior for a chart never round-tripped through the reader.
    /// </summary>
    [Fact]
    public void ComboChart_SecondaryAxisWithoutOwnUnit_StillClonesPrimaryUnitAsBefore()
    {
        var workbook = CreateColumnLineComboWorkbook();
        var chart = workbook.GetSheetAt(0).Charts.Single();
        chart.YAxisMajorUnit = 20;
        chart.YAxisMinorUnit = 4;

        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);
        var chartXml = LoadChartXml(saved);

        var valueAxes = chartXml.Descendants(ChartNs + "valAx").ToList();
        valueAxes.Should().HaveCount(2);
        var secondaryAxis = valueAxes[1];
        secondaryAxis.Element(ChartNs + "majorUnit")!.Attribute("val")!.Value.Should().Be("20");
        secondaryAxis.Element(ChartNs + "minorUnit")!.Attribute("val")!.Value.Should().Be("4");

        var reloaded = ReloadSingleChart(saved);
        reloaded.SecondaryAxisMajorUnit.Should().Be(20);
        reloaded.SecondaryAxisMinorUnit.Should().Be(4);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_HorizontalBarChart_CategoryAndValueAxisKeepOwnTickStylesAndLine()
    {
        var workbook = new Workbook("R62ChartAxisBarTickLine");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));

        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 2)),

            // Category axis is physically on the LEFT (Y) for a Bar chart -- its own tick styles and
            // line color must land here and stay here.
            YAxisMajorTickStyle = ChartAxisTickStyle.Cross,
            YAxisMinorTickStyle = ChartAxisTickStyle.Inside,
            YAxisLineColor = new CellColor(0, 0, 255),
            YAxisLineThickness = 2,

            // Value axis is physically on the BOTTOM (X) for a Bar chart -- its own (different) tick
            // styles and line color must land here and stay here, not bleed onto (or get overwritten
            // by) the category axis's Y* fields above.
            XAxisMajorTickStyle = ChartAxisTickStyle.None,
            XAxisMinorTickStyle = ChartAxisTickStyle.None,
            XAxisLineColor = new CellColor(0, 128, 0),
            XAxisLineThickness = 1,
        };
        sheet.Charts.Add(chart);

        var adapter = new XlsxFileAdapter();
        using var ms1 = new MemoryStream();
        adapter.Save(workbook, ms1);
        ms1.Position = 0;
        var loaded1 = adapter.Load(ms1);
        var afterFirst = loaded1.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        AssertAxesKeepOwnTickStylesAndLine(afterFirst, "first");

        // Re-save/reload -- an idempotent round-trip must not swap or drop these on a SECOND pass.
        using var ms2 = new MemoryStream();
        adapter.Save(loaded1, ms2);
        ms2.Position = 0;
        var loaded2 = adapter.Load(ms2);
        var afterSecond = loaded2.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        AssertAxesKeepOwnTickStylesAndLine(afterSecond, "second");
    }

    private static void AssertAxesKeepOwnTickStylesAndLine(ChartModel chart, string pass)
    {
        chart.YAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Cross,
            $"the category axis's own major tick style must survive the {pass} round-trip");
        chart.YAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.Inside,
            $"the category axis's own minor tick style must survive the {pass} round-trip");
        chart.YAxisLineColor.Should().Be(new CellColor(0, 0, 255),
            $"the category axis's own line color must survive the {pass} round-trip");

        chart.XAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.None,
            $"the value axis's own (hidden) major tick style must survive the {pass} round-trip");
        chart.XAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None,
            $"the value axis's own (hidden) minor tick style must survive the {pass} round-trip");
        chart.XAxisLineColor.Should().Be(new CellColor(0, 128, 0),
            $"the value axis's own line color must survive the {pass} round-trip");
    }

    /// <summary>
    /// Sibling no-regression: for a NON-bar chart (Column), the category axis is physically X and the
    /// value axis is physically Y -- the routing this fix introduces must be a no-op here, exactly like
    /// it already was before the fix (categoryAxisOnY/valueAxisOnX both false).
    /// </summary>
    [Fact]
    public void XlsxAdapter_RoundTrip_ColumnChart_CategoryAndValueAxisKeepOwnTickStylesAndLine()
    {
        var workbook = new Workbook("R62ChartAxisColumnNoRegression");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));

        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 2)),

            // Category axis is physically on the BOTTOM (X) for a Column chart.
            XAxisMajorTickStyle = ChartAxisTickStyle.Cross,
            XAxisMinorTickStyle = ChartAxisTickStyle.Inside,
            XAxisLineColor = new CellColor(0, 0, 255),

            // Value axis is physically on the LEFT (Y) for a Column chart.
            YAxisMajorTickStyle = ChartAxisTickStyle.None,
            YAxisMinorTickStyle = ChartAxisTickStyle.None,
            YAxisLineColor = new CellColor(0, 128, 0),
        };
        sheet.Charts.Add(chart);

        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);
        var afterRoundTrip = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        afterRoundTrip.XAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Cross);
        afterRoundTrip.XAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.Inside);
        afterRoundTrip.XAxisLineColor.Should().Be(new CellColor(0, 0, 255));
        afterRoundTrip.YAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.None);
        afterRoundTrip.YAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        afterRoundTrip.YAxisLineColor.Should().Be(new CellColor(0, 128, 0));
    }

    private static Workbook CreateColumnLineComboWorkbook()
    {
        var workbook = new Workbook("ChartAxisMajorMinorUnitSecondaryDeep");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Units"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Growth"));
        for (uint row = 2; row <= 5; row++)
        {
            var offset = row - 1;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"M{offset}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(offset * 100));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(70 + (offset * 8)));
            sheet.SetCell(new CellAddress(sheet.Id, row, 4), new NumberValue(0.15 + (offset * 0.02)));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            Title = "Sales, units, and growth",
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4)),
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [2],
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [1, 2]
        });

        return workbook;
    }

    private static ChartModel ReloadSingleChart(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        return new XlsxFileAdapter().Load(stream).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
    }

    private static XDocument LoadChartXml(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var entry = archive.Entries.Single(e => e.FullName == "xl/charts/chart1.xml");
        using var entryStream = entry.Open();
        return XDocument.Load(entryStream);
    }
}
