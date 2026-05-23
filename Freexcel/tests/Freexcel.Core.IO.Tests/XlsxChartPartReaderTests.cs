using System.Xml.Linq;
using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public sealed class XlsxChartPartReaderTests
{
    [Theory]
    [InlineData("surfaceChart", ChartType.Surface)]
    [InlineData("treemapChart", ChartType.Treemap)]
    [InlineData("sunburstChart", ChartType.Sunburst)]
    [InlineData("histogramChart", ChartType.Histogram)]
    [InlineData("boxWhiskerChart", ChartType.BoxAndWhisker)]
    [InlineData("waterfallChart", ChartType.Waterfall)]
    [InlineData("funnelChart", ChartType.Funnel)]
    public void TryReadSupportedChart_RecognizesDeferredAdvancedChartFamilies(string chartElementName, ChartType expectedType)
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse(BuildSingleSeriesChartXml(chartElementName));

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(expectedType);
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeFalse();
        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2)));
        chart.FirstRowIsHeader.Should().BeTrue();
        chart.FirstColIsCategories.Should().BeTrue();
    }

    [Fact]
    public void TryReadSupportedChart_RecognizesParetoHistogramAsDeferred()
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse(BuildSingleSeriesChartXml("histogramChart", """<c:paretoLine val="1"/>"""));

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Pareto);
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeFalse();
    }

    [Fact]
    public void TryReadSupportedChart_RecognizesMapChartExtensionAsDeferred()
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
                          xmlns:c16="http://schemas.microsoft.com/office/drawing/2014/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:extLst>
                    <c:ext uri="{797F5495-87CF-4200-8BC9-4A52B092F858}">
                      <c16:geoChart>
                        <c16:ser>
                          <c16:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c16:tx>
                          <c16:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c16:cat>
                          <c16:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c16:val>
                        </c16:ser>
                      </c16:geoChart>
                    </c:ext>
                  </c:extLst>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Map);
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeFalse();
    }

    [Theory]
    [InlineData("cx:treemapChart", ChartType.Treemap)]
    [InlineData("cx:sunburstChart", ChartType.Sunburst)]
    [InlineData("cx:histogramChart", ChartType.Histogram)]
    [InlineData("cx:boxWhiskerChart", ChartType.BoxAndWhisker)]
    [InlineData("cx:waterfallChart", ChartType.Waterfall)]
    [InlineData("cx:funnelChart", ChartType.Funnel)]
    public void TryReadSupportedChart_RecognizesDeferredAdvancedChartFamiliesInsideExtensions(
        string chartElementName,
        ChartType expectedType)
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse($$"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:cx="http://schemas.microsoft.com/office/drawing/2014/chartex"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:extLst>
                    <c:ext uri="{C3380CC4-5D6E-409C-BE32-E72D297353CC}">
                      <{{chartElementName}}>
                        <cx:ser>
                          <cx:tx><cx:strRef><cx:f>Sheet1!$B$1</cx:f></cx:strRef></cx:tx>
                          <cx:cat><cx:strRef><cx:f>Sheet1!$A$2:$A$4</cx:f></cx:strRef></cx:cat>
                          <cx:val><cx:numRef><cx:f>Sheet1!$B$2:$B$4</cx:f></cx:numRef></cx:val>
                        </cx:ser>
                      </{{chartElementName}}>
                    </c:ext>
                  </c:extLst>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(expectedType);
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeFalse();
        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2)));
    }

    [Fact]
    public void TryReadSupportedChart_DoesNotLetAdvancedExtensionOverrideDirectSupportedChart()
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:cx="http://schemas.microsoft.com/office/drawing/2014/chartex">
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
                  <c:extLst>
                    <c:ext uri="{C3380CC4-5D6E-409C-BE32-E72D297353CC}">
                      <cx:treemapChart>
                        <cx:ser>
                          <cx:val><cx:numRef><cx:f>Sheet1!$D$2:$D$4</cx:f></cx:numRef></cx:val>
                        </cx:ser>
                      </cx:treemapChart>
                    </c:ext>
                  </c:extLst>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Column);
        ChartTypeSupport.IsRenderable(chart.Type).Should().BeTrue();
    }

    [Theory]
    [InlineData("radarChart", ChartType.Radar)]
    [InlineData("stockChart", ChartType.Stock)]
    public void TryReadSupportedChart_ReadsRadarAndStockChartFamilies(string chartElementName, ChartType expectedType)
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse($$"""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <c:chart>
                <c:title><c:tx><c:rich><a:p><a:r><a:t>Market View</a:t></a:r></a:p></c:rich></c:tx></c:title>
                <c:plotArea>
                  <c:{{chartElementName}}>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:{{chartElementName}}>
                  <c:catAx><c:axId val="1"/><c:crossAx val="2"/></c:catAx>
                  <c:valAx><c:axId val="2"/><c:crossAx val="1"/></c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(expectedType);
        chart.Title.Should().Be("Market View");
        chart.DataRange.Start.Row.Should().Be(1);
        chart.DataRange.Start.Col.Should().Be(1);
        chart.DataRange.End.Row.Should().Be(4);
        chart.DataRange.End.Col.Should().Be(2);
        chart.FirstRowIsHeader.Should().BeTrue();
        chart.FirstColIsCategories.Should().BeTrue();
    }

    [Fact]
    public void TryReadSupportedChart_ReadsVolumeOpenHighLowCloseStockChart()
    {
        var sheetId = SheetId.New();
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:title><c:tx><c:rich><a:p><a:r><a:t>OHLCV</a:t></a:r></a:p></c:rich></c:tx></c:title>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:grouping val="clustered"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                  <c:stockChart>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:tx><c:strRef><c:f>Sheet1!$C$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:ser>
                      <c:idx val="2"/>
                      <c:order val="2"/>
                      <c:tx><c:strRef><c:f>Sheet1!$D$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$D$2:$D$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:ser>
                      <c:idx val="3"/>
                      <c:order val="3"/>
                      <c:tx><c:strRef><c:f>Sheet1!$E$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$E$2:$E$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:ser>
                      <c:idx val="4"/>
                      <c:order val="4"/>
                      <c:tx><c:strRef><c:f>Sheet1!$F$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$F$2:$F$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:hiLowLines/>
                    <c:upDownBars/>
                  </c:stockChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Stock);
        chart.StockSubtype.Should().Be(StockChartSubtype.VolumeOpenHighLowClose);
        chart.ShowHighLowLines.Should().BeTrue();
        chart.ShowUpDownBars.Should().BeTrue();
        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 6)));
    }

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

    [Fact]
    public void TryReadSupportedChart_ReadsColumnChartRangeTitleAndThemeSeriesFill()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <c:chart>
                <c:title><c:tx><c:rich><a:p><a:r><a:t>Sales</a:t></a:r></a:p></c:rich></c:tx></c:title>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:spPr>
                        <a:solidFill><a:schemeClr val="accent2"/></a:solidFill>
                      </c:spPr>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Column);
        chart.Title.Should().Be("Sales");
        chart.DataRange.Should().Be(new GridRange(
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 4, 2)));
        chart.FirstRowIsHeader.Should().BeTrue();
        chart.FirstColIsCategories.Should().BeTrue();
        chart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2)));
    }

    [Fact]
    public void TryReadSupportedChart_ReadsPivotSourceBinding()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <c:date1904 val="1"/>
              <c:lang val="en-US"/>
              <c:style val="42"/>
              <c:clrMapOvr>
                <a:overrideClrMapping bg1="lt1" tx1="dk1" accent1="accent2"/>
              </c:clrMapOvr>
              <c:protection chartObject="1" data="1" formatting="0" selection="1" userInterface="1"/>
              <c:printSettings>
                <c:pageMargins l="0.7" r="0.7" t="0.75" b="0.75" header="0.3" footer="0.3"/>
                <c:pageSetup paperSize="9" orientation="landscape" copies="2" blackAndWhite="1" draft="0"/>
              </c:printSettings>
              <c:pivotSource>
                <c:name>Data!PivotTable1</c:name>
                <c:fmtId val="0"/>
              </c:pivotSource>
              <c:roundedCorners val="1"/>
              <c:chart>
                <c:autoTitleDeleted val="1"/>
                <c:pivotFmts>
                  <c:pivotFmt>
                    <c:idx val="0"/>
                    <c:spPr><a:solidFill><a:srgbClr val="4472C4"/></a:solidFill></c:spPr>
                  </c:pivotFmt>
                </c:pivotFmts>
                <c:plotArea>
                  <c:layout>
                    <c:manualLayout>
                      <c:layoutTarget val="outer"/>
                      <c:xMode val="factor"/>
                      <c:yMode val="edge"/>
                      <c:wMode val="factor"/>
                      <c:hMode val="factor"/>
                      <c:x val="0.1"/>
                      <c:y val="0.2"/>
                      <c:w val="0.8"/>
                      <c:h val="0.6"/>
                    </c:manualLayout>
                  </c:layout>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:tx><c:strRef><c:f>Data!$E$1</c:f></c:strRef></c:tx>
                      <c:cat><c:strRef><c:f>Data!$D$2:$D$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Data!$E$2:$E$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
                <c:legend>
                  <c:legendPos val="r"/>
                  <c:layout>
                    <c:manualLayout>
                      <c:layoutTarget val="inner"/>
                      <c:xMode val="edge"/>
                      <c:yMode val="edge"/>
                      <c:wMode val="factor"/>
                      <c:hMode val="factor"/>
                      <c:x val="0.76"/>
                      <c:y val="0.15"/>
                      <c:w val="0.2"/>
                      <c:h val="0.7"/>
                    </c:manualLayout>
                  </c:layout>
                  <c:overlay val="1"/>
                </c:legend>
                <c:plotVisOnly val="0"/>
                <c:dispBlanksAs val="span"/>
                <c:showDLblsOverMax val="1"/>
              </c:chart>
              <c:externalData r:id="rIdExternalData1">
                <c:autoUpdate val="1"/>
              </c:externalData>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.IsPivotChart.Should().BeTrue();
        chart.PivotTableName.Should().Be("PivotTable1");
        chart.PivotFormatsXml.Should().Contain("pivotFmt");
        chart.PivotFormatsXml.Should().Contain("4472C4");
        chart.ChartStyleId.Should().Be(42);
        chart.RoundedCorners.Should().BeTrue();
        chart.BlankDisplayMode.Should().Be(ChartBlankDisplayMode.Span);
        chart.ShowDataLabelsOverMaximum.Should().BeTrue();
        chart.AutoTitleDeleted.Should().BeTrue();
        chart.ShowDataInHiddenRowsAndColumns.Should().BeTrue();
        chart.Uses1904DateSystem.Should().BeTrue();
        chart.Language.Should().Be("en-US");
        chart.ColorMapOverride.Should().BeEquivalentTo(new ChartColorMapOverrideModel
        {
            OverrideMappings =
            {
                ["bg1"] = "lt1",
                ["tx1"] = "dk1",
                ["accent1"] = "accent2"
            }
        });
        chart.ExternalData.Should().BeEquivalentTo(new ChartExternalDataModel
        {
            RelationshipId = "rIdExternalData1",
            AutoUpdate = true
        });
        chart.PlotAreaLayout.Should().BeEquivalentTo(new ChartManualLayoutModel
        {
            LayoutTarget = "outer",
            XMode = "factor",
            YMode = "edge",
            WidthMode = "factor",
            HeightMode = "factor",
            X = 0.1,
            Y = 0.2,
            Width = 0.8,
            Height = 0.6
        });
        chart.LegendLayout.Should().BeEquivalentTo(new ChartManualLayoutModel
        {
            LayoutTarget = "inner",
            XMode = "edge",
            YMode = "edge",
            WidthMode = "factor",
            HeightMode = "factor",
            X = 0.76,
            Y = 0.15,
            Width = 0.2,
            Height = 0.7
        });
        chart.PrintSettings.Should().BeEquivalentTo(new ChartPrintSettingsModel
        {
            PageMargins = new ChartPageMarginsModel
            {
                Left = 0.7,
                Right = 0.7,
                Top = 0.75,
                Bottom = 0.75,
                Header = 0.3,
                Footer = 0.3
            },
            PageSetup = new ChartPageSetupModel
            {
                PaperSize = "9",
                Orientation = "landscape",
                Copies = 2,
                BlackAndWhite = true,
                Draft = false
            }
        });
        chart.Protection.Should().BeEquivalentTo(new ChartProtectionModel
        {
            ChartObject = true,
            Data = true,
            Formatting = false,
            Selection = true,
            UserInterface = true
        });
    }

    [Fact]
    public void TryReadSupportedChart_ReadsBarDirection()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="bar"/>
                    <c:ser>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Bar);
    }

    [Fact]
    public void TryReadSupportedChart_ReadsBarSpacingAndVaryColors()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:grouping val="clustered"/>
                    <c:varyColors val="1"/>
                    <c:ser>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:overlap val="-20"/>
                    <c:gapWidth val="75"/>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Column);
        chart.VaryColorsByPoint.Should().BeTrue();
        chart.BarOverlap.Should().Be(-20);
        chart.BarGapWidth.Should().Be(75);
    }

    [Fact]
    public void TryReadSupportedChart_ReadsChartDataTableMetadata()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                  <c:dTable>
                    <c:showHorzBorder val="1"/>
                    <c:showVertBorder val="0"/>
                    <c:showOutline val="1"/>
                    <c:showKeys val="1"/>
                  </c:dTable>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.DataTable.Should().BeEquivalentTo(new ChartDataTableModel
        {
            ShowHorizontalBorder = true,
            ShowVerticalBorder = false,
            ShowOutline = true,
            ShowLegendKeys = true
        });
    }

    [Fact]
    public void TryReadSupportedChart_ReadsErrorBarMetadata()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:errBars>
                        <c:errBarType val="plus"/>
                        <c:errValType val="percentage"/>
                        <c:noEndCap val="1"/>
                        <c:val val="12.5"/>
                      </c:errBars>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.ShowErrorBars.Should().BeTrue();
        chart.ErrorBarKind.Should().Be(ChartErrorBarKind.Percentage);
        chart.ErrorBarDirection.Should().Be(ChartErrorBarDirection.Plus);
        chart.ErrorBarValue.Should().Be(12.5);
        chart.ErrorBarEndCaps.Should().BeFalse();
    }

    [Fact]
    public void TryReadSupportedChart_ReadsLineGuideMetadata()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:lineChart>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:order val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:order val="1"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:dropLines/>
                    <c:hiLowLines/>
                    <c:upDownBars/>
                  </c:lineChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.ShowDropLines.Should().BeTrue();
        chart.ShowHighLowLines.Should().BeTrue();
        chart.ShowUpDownBars.Should().BeTrue();
    }

    [Fact]
    public void TryReadSupportedChart_ReadsConcreteSeriesFill()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:spPr><a:solidFill><a:srgbClr val="0C2238"/></a:solidFill></c:spPr>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(0, FillColor: new CellColor(12, 34, 56)));
    }

    [Fact]
    public void TryReadSupportedChart_ClearsUnsupportedPercentageDataLabels()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:dLbls>
                      <c:showVal val="1"/>
                      <c:showPercent val="1"/>
                    </c:dLbls>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Column);
        chart.ShowDataLabels.Should().BeTrue();
        chart.ShowDataLabelPercentage.Should().BeFalse();
    }

    [Fact]
    public void TryReadSupportedChart_ClearsUnsupportedSecondaryAxisState()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:grouping val="stacked"/>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                    <c:axId val="10"/>
                    <c:axId val="20"/>
                  </c:barChart>
                  <c:catAx>
                    <c:axId val="10"/>
                  </c:catAx>
                  <c:valAx>
                    <c:axId val="20"/>
                    <c:axPos val="r"/>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.StackedColumn);
        chart.ShowSecondaryAxis.Should().BeFalse();
        chart.SecondaryAxisSeriesIndexes.Should().BeEmpty();
    }

    [Fact]
    public void TryReadSupportedChart_ClearsUnsupportedComboLineOverlayState()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="bar"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                  <c:lineChart>
                    <c:ser>
                      <c:idx val="1"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:lineChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Bar);
        chart.UseComboLineForSecondarySeries.Should().BeFalse();
        chart.ComboLineSeriesIndexes.Should().BeEmpty();
    }

    [Fact]
    public void TryReadSupportedChart_ClearsUnsupportedTrendlineState()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:pieChart>
                    <c:ser>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                      <c:trendline>
                        <c:trendlineType val="poly"/>
                        <c:order val="4"/>
                        <c:dispEq val="1"/>
                        <c:dispRSqr val="1"/>
                        <c:spPr>
                          <a:ln w="31750">
                            <a:solidFill><a:srgbClr val="D95319"/></a:solidFill>
                            <a:prstDash val="dot"/>
                          </a:ln>
                        </c:spPr>
                      </c:trendline>
                    </c:ser>
                  </c:pieChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Pie);
        chart.ShowLinearTrendline.Should().BeFalse();
        chart.TrendlineType.Should().Be(ChartTrendlineType.Linear);
        chart.TrendlineOrder.Should().Be(2);
        chart.ShowTrendlineEquation.Should().BeFalse();
        chart.ShowTrendlineRSquared.Should().BeFalse();
        chart.TrendlineColor.Should().BeNull();
        chart.TrendlineThickness.Should().Be(1.5);
        chart.TrendlineDashStyle.Should().Be(ChartLineDashStyle.Dash);
    }

    [Fact]
    public void TryReadSupportedChart_ClearsUnsupportedAxisStateForNoAxisCharts()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:pieChart>
                    <c:ser>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:pieChart>
                  <c:catAx>
                    <c:title>
                      <c:tx><c:rich><a:p><a:r><a:rPr sz="1800"><a:solidFill><a:srgbClr val="C00000"/></a:solidFill></a:rPr><a:t>Category Axis</a:t></a:r></a:p></c:rich></c:tx>
                    </c:title>
                    <c:majorGridlines><c:spPr><a:ln w="25400"><a:solidFill><a:srgbClr val="70AD47"/></a:solidFill></a:ln></c:spPr></c:majorGridlines>
                    <c:minorGridlines/>
                    <c:majorTickMark val="in"/>
                    <c:minorTickMark val="out"/>
                    <c:tickLblPos val="none"/>
                    <c:spPr><a:ln w="38100"><a:solidFill><a:srgbClr val="4472C4"/></a:solidFill></a:ln></c:spPr>
                  </c:catAx>
                  <c:valAx>
                    <c:title><c:tx><c:rich><a:p><a:r><a:t>Value Axis</a:t></a:r></a:p></c:rich></c:tx></c:title>
                    <c:scaling>
                      <c:logBase val="10"/>
                      <c:min val="1"/>
                      <c:max val="100"/>
                    </c:scaling>
                    <c:majorUnit val="10"/>
                    <c:minorUnit val="2"/>
                    <c:numFmt formatCode="0.00"/>
                    <c:majorGridlines/>
                    <c:minorGridlines/>
                    <c:majorTickMark val="in"/>
                    <c:minorTickMark val="out"/>
                    <c:tickLblPos val="none"/>
                    <c:spPr><a:ln w="38100"><a:solidFill><a:srgbClr val="ED7D31"/></a:solidFill></a:ln></c:spPr>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Pie);
        chart.XAxisTitle.Should().BeNull();
        chart.YAxisTitle.Should().BeNull();
        chart.AxisTitleTextColor.Should().BeNull();
        chart.AxisTitleFontSize.Should().Be(12);
        chart.XAxisMinimum.Should().BeNull();
        chart.XAxisMaximum.Should().BeNull();
        chart.XAxisMajorUnit.Should().BeNull();
        chart.XAxisMinorUnit.Should().BeNull();
        chart.XAxisLogScale.Should().BeFalse();
        chart.ShowXAxisMajorGridlines.Should().BeFalse();
        chart.ShowXAxisMinorGridlines.Should().BeFalse();
        chart.XAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        chart.XAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        chart.ShowXAxisLabels.Should().BeTrue();
        chart.XAxisLineColor.Should().BeNull();
        chart.YAxisMinimum.Should().BeNull();
        chart.YAxisMaximum.Should().BeNull();
        chart.YAxisMajorUnit.Should().BeNull();
        chart.YAxisMinorUnit.Should().BeNull();
        chart.YAxisLogScale.Should().BeFalse();
        chart.YAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        chart.ShowYAxisMajorGridlines.Should().BeFalse();
        chart.ShowYAxisMinorGridlines.Should().BeFalse();
        chart.YAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        chart.YAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        chart.ShowYAxisLabels.Should().BeTrue();
        chart.YAxisLineColor.Should().BeNull();
    }

    [Fact]
    public void TryReadSupportedChart_ClearsOutOfRangeExplodedSliceState()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart">
              <c:chart>
                <c:plotArea>
                  <c:pieChart>
                    <c:ser>
                      <c:dPt>
                        <c:idx val="99"/>
                        <c:explosion val="25"/>
                      </c:dPt>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:pieChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Pie);
        chart.ExplodedSliceIndex.Should().Be(-1);
        chart.ExplodedSliceDistance.Should().Be(0.1);
    }

    [Fact]
    public void TryReadSupportedChart_DropsOutOfRangePointDataLabelFormatting()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="0"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                      <c:dLbls>
                        <c:dLbl>
                          <c:idx val="99"/>
                          <c:spPr><a:solidFill><a:srgbClr val="70AD47"/></a:solidFill></c:spPr>
                        </c:dLbl>
                      </c:dLbls>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Column);
        chart.ShowDataLabels.Should().BeFalse();
        chart.PointDataLabelFormats.Should().BeEmpty();
    }

    [Fact]
    public void TryReadSupportedChart_DropsNegativeSeriesFormattingIndexes()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="-1"/>
                      <c:spPr><a:solidFill><a:srgbClr val="70AD47"/></a:solidFill></c:spPr>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Column);
        chart.SeriesFormats.Should().BeEmpty();
    }

    [Fact]
    public void TryReadSupportedChart_DropsPointDataLabelFormattingForNegativeSeriesIndexes()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:barChart>
                    <c:barDir val="col"/>
                    <c:ser>
                      <c:idx val="-1"/>
                      <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                      <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                      <c:dLbls>
                        <c:dLbl>
                          <c:idx val="0"/>
                          <c:spPr><a:solidFill><a:srgbClr val="70AD47"/></a:solidFill></c:spPr>
                        </c:dLbl>
                      </c:dLbls>
                    </c:ser>
                  </c:barChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Column);
        chart.PointDataLabelFormats.Should().BeEmpty();
    }

    [Fact]
    public void TryReadSupportedChart_ReadsScatterAxisTitlesByChartAxisIdOrder()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:scatterChart>
                    <c:scatterStyle val="lineMarker"/>
                    <c:ser>
                      <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                      <c:xVal><c:numRef><c:f>Sheet1!$A$2:$A$4</c:f></c:numRef></c:xVal>
                      <c:yVal><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:yVal>
                    </c:ser>
                    <c:axId val="10"/>
                    <c:axId val="20"/>
                  </c:scatterChart>
                  <c:valAx>
                    <c:axId val="20"/>
                    <c:title><c:tx><c:rich><a:p><a:r><a:t>Y Axis</a:t></a:r></a:p></c:rich></c:tx></c:title>
                  </c:valAx>
                  <c:valAx>
                    <c:axId val="10"/>
                    <c:title><c:tx><c:rich><a:p><a:r><a:t>X Axis</a:t></a:r></a:p></c:rich></c:tx></c:title>
                  </c:valAx>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Scatter);
        chart.XAxisTitle.Should().Be("X Axis");
        chart.YAxisTitle.Should().Be("Y Axis");
    }

    [Fact]
    public void TryReadSupportedChart_UsesBubbleSeriesIndexForSeriesFormatting()
    {
        var sheetId = new SheetId(Guid.NewGuid());
        var chartXml = XDocument.Parse("""
            <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                          xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
              <c:chart>
                <c:plotArea>
                  <c:bubbleChart>
                    <c:ser>
                      <c:idx val="3"/>
                      <c:spPr><a:solidFill><a:srgbClr val="70AD47"/></a:solidFill></c:spPr>
                      <c:xVal><c:numRef><c:f>Sheet1!$A$2:$A$4</c:f></c:numRef></c:xVal>
                      <c:yVal><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:yVal>
                      <c:bubbleSize><c:numRef><c:f>Sheet1!$C$2:$C$4</c:f></c:numRef></c:bubbleSize>
                    </c:ser>
                  </c:bubbleChart>
                </c:plotArea>
              </c:chart>
            </c:chartSpace>
            """);

        XlsxChartPartReader.TryReadSupportedChart(chartXml, sheetId, out var chart)
            .Should().BeTrue();

        chart.Type.Should().Be(ChartType.Bubble);
        chart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(3, FillColor: new CellColor(112, 173, 71)));
    }
}
