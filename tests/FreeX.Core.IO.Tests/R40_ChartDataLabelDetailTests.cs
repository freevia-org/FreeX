using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-40 fixes for four chart data-label round-trip bugs (R40-io-chart-datalabel-numfmt-3-1..4):
///   1. c:dLblPos="inBase" (Inside Base) was read as BestFit then degraded to "ctr" on save.
///   2. Per-point data-label manual layout (a dragged label) was never read/written.
///   3. Per-point custom label text (c:dLbl/c:tx rich-text override) was never read/written.
///   4. A non-recognized literal data-label separator (e.g. "Period") coerced to Comma on load
///      and could not be re-emitted.
/// </summary>
public sealed class R40_ChartDataLabelDetailTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    private static MemoryStream SaveWorkbookWithColumnChart()
    {
        var workbook = new Workbook("R40ChartDataLabelDetail");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            Title = "Sales",
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        return saved;
    }

    /// <summary>
    /// Replaces xl/charts/chart1.xml in an already-saved package with custom raw chart XML, so the
    /// reader can be exercised against hand-authored OOXML that FreeX's own writer would not yet
    /// produce (the scenario a real Excel-authored file would present).
    /// </summary>
    private static MemoryStream ReplaceChartXml(MemoryStream package, string chartXml)
    {
        using (var archive = new ZipArchive(package, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry("xl/charts/chart1.xml")!.Delete();
            var entry = archive.CreateEntry("xl/charts/chart1.xml");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(chartXml);
        }

        package.Position = 0;
        return package;
    }

    private static XDocument LoadChartXml(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        using var stream = archive.GetEntry("xl/charts/chart1.xml")!.Open();
        return XDocument.Load(stream);
    }

    // ---------------------------------------------------------------------
    // Finding 1: c:dLblPos="inBase" (Inside Base)
    // ---------------------------------------------------------------------

    [Fact]
    public void XlsxAdapter_Load_ReadsInsideBaseDataLabelPositionAsDistinctValue()
    {
        var package = SaveWorkbookWithColumnChart();
        ReplaceChartXml(package, """
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
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
                    <c:dLbls>
                      <c:dLblPos val="inBase"/>
                      <c:showVal val="1"/>
                    </c:dLbls>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var loaded = new XlsxFileAdapter().Load(package);
        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        // Prior bug: this fell through to the default case and became BestFit.
        chart.DataLabelPosition.Should().Be(ChartDataLabelPosition.InsideBase);
    }

    [Fact]
    public void XlsxAdapter_SaveLoadedWorkbook_RoundTripsInsideBaseDataLabelPositionForClusteredColumn()
    {
        var package = SaveWorkbookWithColumnChart();
        ReplaceChartXml(package, """
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
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
                    <c:dLbls>
                      <c:dLblPos val="inBase"/>
                      <c:showVal val="1"/>
                    </c:dLbls>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        loaded.GetSheetAt(0).Charts.Single().Title = "Sales (edited)"; // force a full chart-XML rebuild

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;
        var chartXml = LoadChartXml(saved);

        // Prior bug: GateDataLabelPosition's final fallback rewrote the resulting "bestFit" down to
        // "ctr" for clustered column, silently moving the label from the base of the bar to its center.
        chartXml.Descendants(ChartNs + "dLblPos").Should().ContainSingle()
            .Which.Attribute("val")!.Value.Should().Be("inBase");
    }

    [Fact]
    public void XlsxAdapter_Save_GatesInsideBaseDataLabelPositionToCtrForStackedColumn()
    {
        // Sibling/no-regression case: inBase is only valid for clustered/3-D bar or column; a
        // stacked column must still gate every position down to "ctr".
        var workbook = new Workbook("R40StackedInsideBaseGate");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.StackedColumn,
            ShowDataLabels = true,
            DataLabelPosition = ChartDataLabelPosition.InsideBase,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2))
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        var chartXml = LoadChartXml(saved);

        chartXml.Descendants(ChartNs + "dLblPos").Should().ContainSingle()
            .Which.Attribute("val")!.Value.Should().Be("ctr");
    }

    // ---------------------------------------------------------------------
    // Finding 2: per-point manual layout (dragged label)
    // ---------------------------------------------------------------------

    [Fact]
    public void XlsxAdapter_SaveLoadedWorkbook_RoundTripsPerPointDataLabelManualLayout()
    {
        var package = SaveWorkbookWithColumnChart();
        ReplaceChartXml(package, """
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:dLbls>
                        <c:dLbl>
                          <c:idx val="1"/>
                          <c:layout>
                            <c:manualLayout>
                              <c:x val="0.05"/>
                              <c:y val="-0.08"/>
                            </c:manualLayout>
                          </c:layout>
                        </c:dLbl>
                        <c:showVal val="1"/>
                      </c:dLbls>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        var pointFormat = loadedChart.PointDataLabelFormats.Should().ContainSingle().Subject;

        // Prior bug: no field existed to hold this at all.
        pointFormat.Layout.Should().NotBeNull();
        pointFormat.Layout!.X.Should().Be(0.05);
        pointFormat.Layout!.Y.Should().Be(-0.08);

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;
        var chartXml = LoadChartXml(saved);

        var label = chartXml.Descendants(ChartNs + "dLbl").Should().ContainSingle().Subject;
        var manualLayout = label.Element(ChartNs + "layout")?.Element(ChartNs + "manualLayout");
        manualLayout.Should().NotBeNull();
        manualLayout!.Element(ChartNs + "x")!.Attribute("val")!.Value.Should().Be("0.05");
        manualLayout!.Element(ChartNs + "y")!.Attribute("val")!.Value.Should().Be("-0.08");
    }

    [Fact]
    public void XlsxAdapter_Save_OmitsPerPointLayoutWhenNoneWasCaptured()
    {
        // Sibling/no-regression case: a point format with no Layout must not spuriously grow one.
        var workbook = new Workbook("R40NoPointLayout");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            ShowDataLabels = true,
            PointDataLabelFormats = [new ChartPointDataLabelFormat(0, 0, ShowValue: true)],
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2))
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        var chartXml = LoadChartXml(saved);

        chartXml.Descendants(ChartNs + "dLbl").Should().ContainSingle()
            .Which.Element(ChartNs + "layout").Should().BeNull();
    }

    // ---------------------------------------------------------------------
    // Finding 3: per-point custom label text (c:dLbl/c:tx override)
    // ---------------------------------------------------------------------

    [Fact]
    public void XlsxAdapter_SaveLoadedWorkbook_RoundTripsPerPointCustomLabelText()
    {
        var package = SaveWorkbookWithColumnChart();
        ReplaceChartXml(package, """
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:dLbls>
                        <c:dLbl>
                          <c:idx val="1"/>
                          <c:tx>
                            <c:rich>
                              <a:bodyPr/>
                              <a:p><a:r><a:t>Record High</a:t></a:r></a:p>
                            </c:rich>
                          </c:tx>
                        </c:dLbl>
                        <c:showVal val="1"/>
                      </c:dLbls>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        // Prior bug: HasPointDataLabelMetadata had no field to detect, so the whole <c:dLbl> (idx +
        // tx only, no other overrides) was silently dropped before ever reaching the model.
        var pointFormat = loadedChart.PointDataLabelFormats.Should().ContainSingle().Subject;
        pointFormat.CustomTextXml.Should().NotBeNullOrEmpty();
        XElement.Parse(pointFormat.CustomTextXml!).Descendants(DrawingNs + "t")
            .Should().ContainSingle().Which.Value.Should().Be("Record High");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;
        var chartXml = LoadChartXml(saved);

        var label = chartXml.Descendants(ChartNs + "dLbl").Should().ContainSingle().Subject;
        var tx = label.Element(ChartNs + "tx");
        tx.Should().NotBeNull();
        tx!.Descendants(DrawingNs + "t").Should().ContainSingle().Which.Value.Should().Be("Record High");
    }

    [Fact]
    public void XlsxAdapter_Save_OmitsPerPointCustomTextWhenNoneWasCaptured()
    {
        // Sibling/no-regression case: a point format with only a show-flag override (no custom
        // text) must not spuriously grow a <c:tx> element.
        var workbook = new Workbook("R40NoPointCustomText");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            ShowDataLabels = true,
            PointDataLabelFormats = [new ChartPointDataLabelFormat(0, 0, ShowValue: false)],
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2))
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        var chartXml = LoadChartXml(saved);

        chartXml.Descendants(ChartNs + "dLbl").Should().ContainSingle()
            .Which.Element(ChartNs + "tx").Should().BeNull();
    }

    // ---------------------------------------------------------------------
    // Finding 4: non-recognized literal data-label separator (e.g. "Period")
    // ---------------------------------------------------------------------

    [Fact]
    public void XlsxAdapter_SaveLoadedWorkbook_RoundTripsPeriodDataLabelSeparatorLiterally()
    {
        var package = SaveWorkbookWithColumnChart();
        ReplaceChartXml(package, """
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
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
                    <c:dLbls>
                      <c:showVal val="1"/>
                      <c:showCatName val="1"/>
                      <c:separator>. </c:separator>
                    </c:dLbls>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        // Prior bug: fell through to the default case and silently became Comma, with no way to
        // recover the literal "Period" text.
        loadedChart.DataLabelSeparator.Should().Be(ChartDataLabelSeparator.Custom);
        loadedChart.DataLabelSeparatorText.Should().Be(". ");

        loadedChart.Title = "Sales (edited)"; // force a full chart-XML rebuild
        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;
        var chartXml = LoadChartXml(saved);

        chartXml.Descendants(ChartNs + "separator").Should().ContainSingle()
            .Which.Value.Should().Be(". ");
    }

    [Fact]
    public void XlsxAdapter_Save_StillWritesCommaSeparatorForDefaultChart()
    {
        // Sibling/no-regression case: an ordinary (never-round-tripped) chart with the default
        // Comma separator must still write ", " with no DataLabelSeparatorText set.
        var workbook = new Workbook("R40DefaultCommaSeparator");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            ShowDataLabels = true,
            ShowDataLabelCategoryName = true,
            DataLabelSeparator = ChartDataLabelSeparator.Comma,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2))
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;
        var chartXml = LoadChartXml(saved);

        chartXml.Descendants(ChartNs + "separator").Should().ContainSingle()
            .Which.Value.Should().Be(", ");
    }
}
