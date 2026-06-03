using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxChartPartReaderTests
{
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
                <c:fmtId val="7"/>
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
                <c:view3D>
                  <c:rotX val="20"/>
                  <c:hPercent val="150"/>
                  <c:rotY val="30"/>
                  <c:depthPercent val="200"/>
                  <c:rAngAx val="0"/>
                  <c:perspective val="45"/>
                </c:view3D>
                <c:floor>
                  <c:spPr><a:solidFill><a:srgbClr val="D9EAD3"/></a:solidFill><a:ln w="12700"><a:solidFill><a:schemeClr val="accent6"/></a:solidFill></a:ln></c:spPr>
                </c:floor>
                <c:sideWall>
                  <c:spPr><a:solidFill><a:schemeClr val="accent2"/></a:solidFill><a:ln w="25400"><a:solidFill><a:srgbClr val="C00000"/></a:solidFill></a:ln></c:spPr>
                </c:sideWall>
                <c:backWall>
                  <c:spPr><a:solidFill><a:srgbClr val="D9E1F2"/></a:solidFill><a:ln w="38100"><a:solidFill><a:srgbClr val="4472C4"/></a:solidFill></a:ln></c:spPr>
                </c:backWall>
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
        chart.PivotSourceFormatId.Should().Be(7);
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
        chart.ThreeDView.Should().BeEquivalentTo(new Chart3DViewModel
        {
            RotationX = 20,
            HeightPercent = 150,
            RotationY = 30,
            DepthPercent = 200,
            RightAngleAxes = false,
            Perspective = 45
        });
        chart.FloorFormat.Should().BeEquivalentTo(new ChartSurfaceFormatModel
        {
            FillColor = new CellColor(217, 234, 211),
            BorderThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent6),
            BorderThickness = 1
        });
        chart.SideWallFormat.Should().BeEquivalentTo(new ChartSurfaceFormatModel
        {
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
            BorderColor = new CellColor(192, 0, 0),
            BorderThickness = 2
        });
        chart.BackWallFormat.Should().BeEquivalentTo(new ChartSurfaceFormatModel
        {
            FillColor = new CellColor(217, 225, 242),
            BorderColor = new CellColor(68, 114, 196),
            BorderThickness = 3
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
}
