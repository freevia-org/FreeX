using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round 45 findings R45-io-chart-datatable-legend-3-1/-2/-3:
/// <list type="bullet">
///   <item>3-1: a &lt;c:legendEntry&gt; carrying ONLY a &lt;c:txPr&gt; (per-entry text formatting,
///   e.g. a single legend key made bold+red) with no &lt;c:delete&gt; child was entirely discarded
///   by ReadLegendEntries because it filtered on <c>IsDeleted is not null</c>.</item>
///   <item>3-2: the writer's classic-stacked-chart "legend defaults to bottom" heuristic
///   (ToEffectiveLegendPosition) fired even when the source file had genuinely, explicitly set the
///   legend position to Right, silently rewriting it to Bottom on every save.</item>
///   <item>3-3: legend-wide Bold/Italic (&lt;c:legend&gt;'s &lt;c:txPr&gt;&lt;a:defRPr b="1"/&gt;)
///   was never read into the model or re-emitted by the writer.</item>
/// </list>
/// </summary>
public sealed class R45_ChartDataTableLegendTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static XDocument ParseChartXml(string xml) => XDocument.Parse(xml);

    private static Workbook CreateWorkbookWithChart(ChartModel chart)
    {
        var workbook = new Workbook("R45ChartDataTableLegend");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Q3"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(30));
        chart.DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        sheet.Charts.Add(chart);
        return workbook;
    }

    private static ChartModel RoundTrip(ChartModel chart)
    {
        var workbook = CreateWorkbookWithChart(chart);
        var adapter = new XlsxFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);
        return loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
    }

    private static XDocument SaveChartXml(ChartModel chart)
    {
        var workbook = CreateWorkbookWithChart(chart);
        using var stream = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        return XlsxPackageTestFixtures.LoadPackageXml(
            archive, "xl/charts/chart1.xml", "the XLSX package should contain xl/charts/chart1.xml");
    }

    // --- R45-io-chart-datatable-legend-3-1 -----------------------------------------------------

    [Fact]
    public void TryReadSupportedChart_LegendEntry_WithTxPrOnlyAndNoDelete_IsNotDiscarded()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
                <c:legend>
                  <c:legendPos val="r"/>
                  <c:legendEntry>
                    <c:idx val="2"/>
                    <c:txPr>
                      <a:bodyPr/>
                      <a:p>
                        <a:pPr>
                          <a:defRPr sz="1400" b="1">
                            <a:solidFill><a:srgbClr val="FF0000"/></a:solidFill>
                          </a:defRPr>
                        </a:pPr>
                      </a:p>
                    </c:txPr>
                  </c:legendEntry>
                  <c:overlay val="0"/>
                </c:legend>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart).Should().BeTrue();

        var entry = chart.LegendEntries.Should().ContainSingle().Subject;
        entry.Index.Should().Be(2);
        entry.IsDeleted.Should().BeNull("the source entry never had a <c:delete> child");
        entry.TextBold.Should().BeTrue();
        entry.TextFontSize.Should().Be(14);
        entry.TextColor.Should().Be(new CellColor(0xFF, 0x00, 0x00));
    }

    [Fact]
    public void TryReadSupportedChart_LegendEntry_DeleteOnlyNoTxPr_StillReadsAsBefore()
    {
        // Sibling/no-regression case: a plain hidden-legend-key entry (delete=1, no txPr) must keep
        // round-tripping exactly as it did before this fix.
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
                <c:legend>
                  <c:legendPos val="r"/>
                  <c:legendEntry>
                    <c:idx val="1"/>
                    <c:delete val="1"/>
                  </c:legendEntry>
                  <c:overlay val="0"/>
                </c:legend>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart).Should().BeTrue();

        var entry = chart.LegendEntries.Should().ContainSingle().Subject;
        entry.Index.Should().Be(1);
        entry.IsDeleted.Should().BeTrue();
        entry.HasTextFormatting.Should().BeFalse();
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_LegendEntryTextFormattingOnly_SurvivesSaveAndLoad()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            LegendEntries =
            [
                new ChartLegendEntryModel(1, null, TextBold: true, TextItalic: true, TextFontSize: 16,
                    TextColor: new CellColor(0x00, 0x80, 0x00))
            ]
        };

        var loaded = RoundTrip(chart);

        var entry = loaded.LegendEntries.Should().ContainSingle().Subject;
        entry.Index.Should().Be(1);
        entry.IsDeleted.Should().BeNull();
        entry.TextBold.Should().BeTrue();
        entry.TextItalic.Should().BeTrue();
        entry.TextFontSize.Should().Be(16);
        entry.TextColor.Should().Be(new CellColor(0x00, 0x80, 0x00));
    }

    // --- R45-io-chart-datatable-legend-3-2 -----------------------------------------------------

    [Fact]
    public void TryReadSupportedChart_ExplicitLegendPosRight_MarksLegendPositionExplicit()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = ParseChartXml("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:grouping val="stacked"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
                <c:legend>
                  <c:legendPos val="r"/>
                  <c:overlay val="0"/>
                </c:legend>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart).Should().BeTrue();

        chart.LegendPosition.Should().Be(ChartLegendPosition.Right);
        chart.LegendPositionExplicit.Should().BeTrue();
    }

    [Fact]
    public void XlsxAdapter_Save_ExplicitLoadedRightLegendOnStackedColumn_IsNotRewrittenToBottom()
    {
        // A chart whose LegendPositionExplicit was set true (as the reader now does when the
        // source file genuinely declared "r") must keep its explicit Right position on save, even
        // though it is a classic stacked-column chart -- the "mimic Excel's own default" heuristic
        // must not resurrect a real user choice.
        var chart = new ChartModel
        {
            Type = ChartType.StackedColumn,
            LegendPosition = ChartLegendPosition.Right,
            LegendPositionExplicit = true
        };

        var chartXml = SaveChartXml(chart);

        chartXml.Descendants(ChartNs + "legend").Single()
            .Element(ChartNs + "legendPos")!.Attribute("val")!.Value.Should().Be("r");
    }

    [Fact]
    public void XlsxAdapter_Save_FreshStackedColumnChart_StillDefaultsLegendToBottom()
    {
        // Sibling/no-regression case: a chart that was never loaded from a file (LegendPositionExplicit
        // stays null/unset) must still get Excel's own default bottom-legend placement for a freshly
        // authored stacked column chart -- pins the pre-existing XlsxClassicChartDefaultTests behavior.
        var chart = new ChartModel
        {
            Type = ChartType.StackedColumn
        };

        var chartXml = SaveChartXml(chart);

        chartXml.Descendants(ChartNs + "legend").Single()
            .Element(ChartNs + "legendPos")!.Attribute("val")!.Value.Should().Be("b");
    }

    // --- R45-io-chart-datatable-legend-3-3 -----------------------------------------------------

    [Fact]
    public void XlsxAdapter_RoundTrip_LegendBoldAndItalic_SurviveSaveAndLoad()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            LegendBold = true,
            LegendItalic = true
        };

        var loaded = RoundTrip(chart);

        loaded.LegendBold.Should().BeTrue();
        loaded.LegendItalic.Should().BeTrue();
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_LegendBoldItalicUnset_StaysNullAndOmitsAttributes()
    {
        // Sibling/no-regression case: a chart whose legend never had Bold/Italic set must not
        // spuriously gain b/i attributes (and must not force a <c:txPr> to be written at all, since
        // no other legend text override is set either).
        var chart = new ChartModel
        {
            Type = ChartType.Column
        };

        var chartXml = SaveChartXml(chart);
        var legend = chartXml.Descendants(ChartNs + "legend").Single();
        legend.Element(ChartNs + "txPr").Should().BeNull();

        var loaded = RoundTrip(chart);
        loaded.LegendBold.Should().BeNull();
        loaded.LegendItalic.Should().BeNull();
    }
}
