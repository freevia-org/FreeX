using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxChartSchemaOrderingTests
{
    private static readonly XNamespace ChartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace DrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

    [Fact]
    public void LineChart_WithFormattedLabelsAxesAndSmoothSeries_ProducesSchemaValidOrder()
    {
        var saved = XlsxPackageTestHelper.SaveToBytes(CreateLineChartWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        var reloadedChart = AssertReloadedSingleChart(saved, ChartType.Line, "Revenue trend", "A1", "C6");
        reloadedChart.ShowDataLabels.Should().BeTrue();
        reloadedChart.ShowLinearTrendline.Should().BeTrue();
        reloadedChart.YAxisDisplayUnit.Should().Be(ChartAxisDisplayUnit.Thousands);

        var chartXml = LoadChartXml(saved);
        AssertAllChartTextPropertiesStartWithBodyProperties(chartXml);

        var lineChart = chartXml.Descendants(ChartNs + "lineChart").Single();
        var series = lineChart.Elements(ChartNs + "ser").First();
        AssertChildOrder(series, "idx", "order", "tx", "spPr", "marker", "dLbls", "trendline", "cat", "val", "smooth");

        var seriesLabels = series.Element(ChartNs + "dLbls")!;
        AssertChildOrder(seriesLabels, "dLbl", "numFmt", "spPr", "txPr", "dLblPos", "showVal");
        AssertChildOrder(seriesLabels.Element(ChartNs + "dLbl")!, "idx", "numFmt", "spPr", "txPr", "dLblPos", "showVal");

        var chartLabels = lineChart.Element(ChartNs + "dLbls")!;
        AssertChildOrder(chartLabels, "numFmt", "spPr", "txPr", "dLblPos", "showLegendKey", "showVal");

        var valueAxis = chartXml.Descendants(ChartNs + "valAx").Single();
        AssertChildOrder(valueAxis, "numFmt", "majorTickMark", "spPr", "txPr", "crossAx", "crosses", "crossBetween", "majorUnit", "minorUnit", "dispUnits");
    }

    [Fact]
    public void PieChart_WithLabelsAndFirstSliceAngle_ProducesSchemaValidOrder()
    {
        var saved = XlsxPackageTestHelper.SaveToBytes(CreatePieChartWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        var reloadedChart = AssertReloadedSingleChart(saved, ChartType.Pie, "Share", "A1", "B4");
        reloadedChart.FirstSliceAngle.Should().Be(45);
        reloadedChart.ShowDataLabelPercentage.Should().BeTrue();

        var chartXml = LoadChartXml(saved);
        AssertAllChartTextPropertiesStartWithBodyProperties(chartXml);

        var pieChart = chartXml.Descendants(ChartNs + "pieChart").Single();
        AssertChildOrder(pieChart, "ser", "dLbls", "firstSliceAng");
        AssertChildOrder(pieChart.Element(ChartNs + "dLbls")!, "numFmt", "spPr", "txPr", "dLblPos", "showVal", "showPercent");
    }

    [Fact]
    public void RadarChart_WithStaleSmoothFlagFromPriorLineType_DoesNotWriteInvalidSmoothElement()
    {
        // R133-io-chart-radar-stale-smooth: simulates a chart that was Line (Smooth=true captured
        // on its SeriesFormats) and was then switched to Radar without the model ever clearing the
        // stale Smooth flag (e.g. a model constructed/loaded outside SetChartLayoutCommand's own
        // ClampSeriesFormat, such as a foreign file or a direct API caller). CT_RadarSer has no
        // <c:smooth> child at all -- only CT_LineSer (line/line3D/stock) does -- so the writer must
        // gate on the chart's OWN type, not on the "draw like a line series" forceLineShapeProperties
        // flag it shares with Radar for marker/shape-property purposes.
        var saved = XlsxPackageTestHelper.SaveToBytes(CreateRadarChartWithStaleSmoothFlagWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        var chartXml = LoadChartXml(saved);
        var radarChart = chartXml.Descendants(ChartNs + "radarChart").Should().ContainSingle().Subject;
        radarChart.Descendants(ChartNs + "smooth").Should().BeEmpty();
    }

    [Fact]
    public void ThreeDPieChart_WithFirstSliceAngle_DoesNotWriteInvalidFirstSliceElement()
    {
        var workbook = CreatePieChartWorkbook(ChartType.ThreeDPie);
        workbook.GetSheetAt(0).Charts.Single().FirstSliceAngle = 45;
        var saved = XlsxPackageTestHelper.SaveToBytes(workbook);

        SchemaErrors(saved).Should().BeEmpty();
        var chartXml = LoadChartXml(saved);
        var pieChart = chartXml.Descendants(ChartNs + "pie3DChart").Single();
        pieChart.Element(ChartNs + "firstSliceAng").Should().BeNull();
    }

    [Fact]
    public void LineChart_WithGuideLinesAndUpDownBars_ProducesSchemaValidOrder()
    {
        var saved = XlsxPackageTestHelper.SaveToBytes(CreateLineGuideChartWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        var reloadedChart = AssertReloadedSingleChart(saved, ChartType.Line, null, "A1", "C4");
        reloadedChart.ShowDropLines.Should().BeTrue();
        reloadedChart.ShowHighLowLines.Should().BeTrue();
        reloadedChart.ShowUpDownBars.Should().BeTrue();
        reloadedChart.UpDownBarGapWidth.Should().Be(180);

        var chartXml = LoadChartXml(saved);
        var lineChart = chartXml.Descendants(ChartNs + "lineChart").Should().ContainSingle().Subject;
        AssertChildOrder(lineChart, "grouping", "ser", "dropLines", "hiLowLines", "upDownBars", "axId");

        var dropLines = lineChart.Element(ChartNs + "dropLines");
        dropLines.Should().NotBeNull();
        AssertChildOrder(dropLines!, "spPr");

        var highLowLines = lineChart.Element(ChartNs + "hiLowLines");
        highLowLines.Should().NotBeNull();
        AssertChildOrder(highLowLines!, "spPr");

        var upDownBars = lineChart.Element(ChartNs + "upDownBars");
        upDownBars.Should().NotBeNull();
        AssertChildOrder(upDownBars!, "gapWidth", "upBars", "downBars");
    }

    [Fact]
    public void ColumnChart_WithErrorBarsAndComboSecondaryLines_ProducesSchemaValidOrder()
    {
        var saved = XlsxPackageTestHelper.SaveToBytes(CreateColumnComboChartWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        var reloadedChart = AssertReloadedSingleChart(saved, ChartType.Column, "Sales, units, and margin", "A1", "D5");
        reloadedChart.ShowErrorBars.Should().BeTrue();
        reloadedChart.ErrorBarKind.Should().Be(ChartErrorBarKind.Percentage);
        reloadedChart.ErrorBarDirection.Should().Be(ChartErrorBarDirection.Plus);
        reloadedChart.ErrorBarValue.Should().Be(12.5);
        reloadedChart.ShowSecondaryAxis.Should().BeTrue();
        reloadedChart.SecondaryAxisSeriesIndexes.Should().Equal(2);
        reloadedChart.ComboLineSeriesIndexes.Should().Equal(1, 2);

        var chartXml = LoadChartXml(saved);
        var barChart = chartXml.Descendants(ChartNs + "barChart").Should().ContainSingle().Subject;
        var lineCharts = chartXml.Descendants(ChartNs + "lineChart").ToList();
        lineCharts.Should().HaveCount(2);

        AssertChildOrder(barChart, "barDir", "grouping", "ser", "gapWidth", "overlap", "axId");

        var barSeries = barChart.Elements(ChartNs + "ser").Should().ContainSingle().Subject;
        AssertChildOrder(barSeries, "idx", "order", "tx", "spPr", "errBars", "cat", "val");

        var errorBars = barSeries.Element(ChartNs + "errBars");
        errorBars.Should().NotBeNull();
        AssertChildOrder(errorBars!, "errDir", "errBarType", "errValType", "noEndCap", "val", "spPr");
        errorBars.Element(ChartNs + "errBarType")!.Attribute("val")!.Value.Should().Be("plus");
        errorBars.Element(ChartNs + "errValType")!.Attribute("val")!.Value.Should().Be("percentage");
        errorBars.Element(ChartNs + "val")!.Attribute("val")!.Value.Should().Be("12.5");

        lineCharts.SelectMany(chart => chart.Elements(ChartNs + "ser")).Should().HaveCount(2);
        lineCharts[0].Elements(ChartNs + "axId").Select(element => element.Attribute("val")!.Value)
            .Should().Equal("48650112", "48672768");
        lineCharts[1].Elements(ChartNs + "axId").Select(element => element.Attribute("val")!.Value)
            .Should().Equal("48650112", "48672769");

        chartXml.Descendants(ChartNs + "catAx").Should().ContainSingle();
        chartXml.Descendants(ChartNs + "valAx").Should().HaveCount(2);
    }

    [Fact]
    public void ThreeDColumnChart_WithMetadataAndPrintSettings_ProducesSchemaValidOrder()
    {
        var saved = XlsxPackageTestHelper.SaveToBytes(CreateRichMetadataChartWorkbook());

        SchemaErrors(saved).Should().BeEmpty();
        var reloadedChart = AssertReloadedSingleChart(saved, ChartType.ThreeDColumn, "Sales", "A1", "B4");
        reloadedChart.Uses1904DateSystem.Should().BeTrue();
        reloadedChart.ChartStyleId.Should().Be(42);
        reloadedChart.PrintSettings.Should().NotBeNull();
        reloadedChart.ThreeDView.Should().NotBeNull();

        var chartXml = LoadChartXml(saved);
        AssertAllChartTextPropertiesStartWithBodyProperties(chartXml);

        var chartSpace = chartXml.Root!;
        AssertChildOrder(chartSpace, "date1904", "lang", "roundedCorners", "style", "clrMapOvr", "protection", "chart", "spPr", "txPr", "externalData", "printSettings");
        AssertChildOrder(chartSpace.Element(ChartNs + "protection")!, "chartObject", "data", "formatting", "selection", "userInterface");

        var chart = chartSpace.Element(ChartNs + "chart")!;
        AssertChildOrder(chart, "title", "autoTitleDeleted", "view3D", "floor", "sideWall", "backWall", "plotArea", "legend", "plotVisOnly", "dispBlanksAs", "showDLblsOverMax");

        var view3D = chart.Element(ChartNs + "view3D");
        view3D.Should().NotBeNull();
        AssertChildOrder(view3D!, "rotX", "hPercent", "rotY", "depthPercent", "rAngAx", "perspective");

        var printSettings = chartSpace.Element(ChartNs + "printSettings");
        printSettings.Should().NotBeNull();
        AssertChildOrder(printSettings!, "headerFooter", "pageMargins", "pageSetup");
    }

    private static Workbook CreateLineChartWorkbook()
    {
        var workbook = new Workbook("ChartSchemaOrderingLine");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Margin"));

        for (uint row = 2; row <= 6; row++)
        {
            var offset = row - 1;
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"M{offset}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(offset * 1000));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(offset * 240));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            Title = "Revenue trend",
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 6, 3)),
            ChartDefaultTextColor = new CellColor(31, 31, 31),
            ChartDefaultFontSize = 10,
            ChartTitleTextColor = new CellColor(31, 78, 121),
            XAxisTitle = "Month",
            YAxisTitle = "Revenue",
            AxisTitleTextColor = new CellColor(68, 68, 68),
            XAxisLabelTextColor = new CellColor(68, 68, 68),
            XAxisLineColor = new CellColor(127, 127, 127),
            YAxisMajorUnit = 1000,
            YAxisMinorUnit = 500,
            YAxisDisplayUnit = ChartAxisDisplayUnit.Thousands,
            YAxisCrossBetween = ChartAxisCrossBetween.Between,
            YAxisLabelTextColor = new CellColor(68, 68, 68),
            YAxisLineColor = new CellColor(127, 127, 127),
            ShowLegend = true,
            LegendPosition = ChartLegendPosition.Bottom,
            LegendTextColor = new CellColor(89, 89, 89),
            ShowDataLabels = true,
            DataLabelNumberFormat = ChartDataLabelNumberFormat.Number,
            DataLabelNumberFormatSourceLinked = false,
            DataLabelFillColor = new CellColor(255, 255, 255),
            DataLabelBorderColor = new CellColor(180, 180, 180),
            DataLabelTextColor = new CellColor(31, 31, 31),
            DataLabelPosition = ChartDataLabelPosition.OutsideEnd,
            DataLabelFontSize = 9,
            DataTable = new ChartDataTableModel
            {
                ShowHorizontalBorder = true,
                ShowVerticalBorder = true,
                ShowOutline = true,
                ShowLegendKeys = true,
                TextColor = new CellColor(89, 89, 89),
                FontSize = 9
            },
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    StrokeColor: new CellColor(68, 114, 196),
                    StrokeThickness: 1.5,
                    MarkerStyle: ChartMarkerStyle.Circle,
                    MarkerSize: 6,
                    Smooth: true)
            ],
            SeriesDataLabelFormats =
            [
                new ChartSeriesDataLabelFormat(
                    0,
                    FillColor: new CellColor(255, 255, 255),
                    BorderColor: new CellColor(180, 180, 180),
                    TextColor: new CellColor(31, 31, 31),
                    FontSize: 9,
                    Position: ChartDataLabelPosition.Center,
                    ShowValue: true,
                    NumberFormatCode: "0.0",
                    NumberFormatSourceLinked: false)
            ],
            PointDataLabelFormats =
            [
                new ChartPointDataLabelFormat(
                    0,
                    0,
                    FillColor: new CellColor(242, 242, 242),
                    BorderColor: new CellColor(160, 160, 160),
                    TextColor: new CellColor(31, 31, 31),
                    FontSize: 8,
                    Position: ChartDataLabelPosition.InsideEnd,
                    ShowValue: true,
                    NumberFormatCode: "0",
                    NumberFormatSourceLinked: false)
            ],
            ShowLinearTrendline = true,
            TrendlineLabelTextColor = new CellColor(31, 31, 31),
            TrendlineLabelFontSize = 9,
            TrendlineLabelNumberFormatCode = "0.00",
            TrendlineLabelNumberFormatSourceLinked = false
        });

        return workbook;
    }

    private static Workbook CreateRadarChartWithStaleSmoothFlagWorkbook()
    {
        var workbook = new Workbook("ChartSchemaOrderingRadarStaleSmooth");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Axis"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Score"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Speed"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Power"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Control"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(5));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Radar,
            Title = "Ratings",
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            SeriesFormats =
            [
                // Stale flag: only Line/ThreeDLine/Scatter ever legitimately set Smooth=true; this
                // series carries it over from a chart-type change that never cleared it.
                new ChartSeriesFormat(0, StrokeColor: new CellColor(68, 114, 196), Smooth: true)
            ]
        });

        return workbook;
    }

    private static Workbook CreateLineGuideChartWorkbook()
    {
        var workbook = new Workbook("ChartSchemaOrderingLineGuides");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Target"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(12));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(22));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(28));

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3)),
            ShowDropLines = true,
            ShowHighLowLines = true,
            ShowUpDownBars = true,
            DropLineColor = new CellColor(91, 155, 213),
            DropLineThickness = 1.5,
            DropLineDashStyle = ChartLineDashStyle.Dot,
            HighLowLineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4),
            HighLowLineThickness = 2,
            HighLowLineDashStyle = ChartLineDashStyle.Dash,
            UpDownBarGapWidth = 180,
            UpBarFillColor = new CellColor(112, 173, 71),
            UpBarBorderColor = new CellColor(84, 130, 53),
            UpBarBorderThickness = 1,
            DownBarFillColor = new CellColor(192, 0, 0),
            DownBarBorderThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
            DownBarBorderThickness = 2
        });

        return workbook;
    }

    private static Workbook CreateColumnComboChartWorkbook()
    {
        var workbook = new Workbook("ChartSchemaOrderingCombo");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Units"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Margin"));

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
            Title = "Sales, units, and margin",
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 4)),
            BarGapWidth = 150,
            BarOverlap = 0,
            ShowErrorBars = true,
            ErrorBarKind = ChartErrorBarKind.Percentage,
            ErrorBarDirection = ChartErrorBarDirection.Plus,
            ErrorBarValue = 12.5,
            ErrorBarEndCaps = false,
            ErrorBarColor = new CellColor(192, 0, 0),
            ErrorBarThickness = 2.25,
            ErrorBarDashStyle = ChartLineDashStyle.Dot,
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [2],
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [1, 2],
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    FillColor: new CellColor(68, 114, 196),
                    StrokeColor: new CellColor(47, 85, 151),
                    StrokeThickness: 1),
                new ChartSeriesFormat(
                    1,
                    StrokeColor: new CellColor(112, 173, 71),
                    StrokeThickness: 1.75,
                    DashStyle: ChartLineDashStyle.Dash),
                new ChartSeriesFormat(
                    2,
                    StrokeColor: new CellColor(192, 0, 0),
                    StrokeThickness: 1.75,
                    DashStyle: ChartLineDashStyle.Dot)
            ]
        });

        return workbook;
    }

    private static Workbook CreateRichMetadataChartWorkbook()
    {
        var workbook = new Workbook("ChartSchemaOrderingMetadata");
        var sheet = workbook.AddSheet("Data");
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
            Type = ChartType.ThreeDColumn,
            Title = "Sales",
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            Uses1904DateSystem = true,
            Language = "en-US",
            RoundedCorners = true,
            ChartStyleId = 42,
            ColorMapOverride = new ChartColorMapOverrideModel
            {
                OverrideMappings =
                {
                    ["accent1"] = "accent2"
                }
            },
            Protection = new ChartProtectionModel
            {
                ChartObject = true,
                Data = true,
                Formatting = false,
                Selection = true,
                UserInterface = true
            },
            AutoTitleDeleted = true,
            ThreeDView = new Chart3DViewModel
            {
                RotationX = 20,
                HeightPercent = 150,
                RotationY = 30,
                DepthPercent = 200,
                RightAngleAxes = false,
                Perspective = 45
            },
            FloorFormat = new ChartSurfaceFormatModel
            {
                FillColor = new CellColor(217, 234, 211),
                BorderThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent6),
                BorderThickness = 1
            },
            SideWallFormat = new ChartSurfaceFormatModel
            {
                FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
                BorderColor = new CellColor(192, 0, 0),
                BorderThickness = 2
            },
            BackWallFormat = new ChartSurfaceFormatModel
            {
                FillColor = new CellColor(217, 225, 242),
                BorderColor = new CellColor(68, 114, 196),
                BorderThickness = 3
            },
            ChartAreaFillColor = new CellColor(255, 255, 255),
            ChartDefaultTextColor = new CellColor(25, 35, 45),
            ChartDefaultFontSize = 13,
            ExternalData = new ChartExternalDataModel
            {
                RelationshipId = "rIdExternalData1",
                RelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package",
                Target = "linked-source.xlsx",
                TargetMode = "External",
                AutoUpdate = true
            },
            PrintSettings = new ChartPrintSettingsModel
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
                    FirstPageNumber = 5,
                    HorizontalDpi = 600,
                    VerticalDpi = 600,
                    BlackAndWhite = true,
                    Draft = false
                },
                HeaderFooter = new ChartHeaderFooterModel
                {
                    DifferentOddEven = true,
                    DifferentFirst = true,
                    AlignWithMargins = false,
                    OddHeader = "&CSales chart",
                    OddFooter = "&P of &N",
                    EvenHeader = "&LConfidential",
                    EvenFooter = "&RPrepared",
                    FirstHeader = "&CFirst page",
                    FirstFooter = "&D"
                }
            },
            ShowLegend = true,
            LegendPosition = ChartLegendPosition.Bottom,
            ShowDataInHiddenRowsAndColumns = true,
            BlankDisplayMode = ChartBlankDisplayMode.Zero,
            ShowDataLabelsOverMaximum = true
        });

        return workbook;
    }

    private static Workbook CreatePieChartWorkbook(ChartType chartType = ChartType.Pie)
    {
        var workbook = new Workbook("ChartSchemaOrderingPie");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Segment"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Share"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(45));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(25));

        sheet.Charts.Add(new ChartModel
        {
            Type = chartType,
            Title = "Share",
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            FirstSliceAngle = 45,
            ShowDataLabels = true,
            DataLabelNumberFormat = ChartDataLabelNumberFormat.Percent,
            DataLabelNumberFormatSourceLinked = false,
            DataLabelFillColor = new CellColor(255, 255, 255),
            DataLabelBorderColor = new CellColor(180, 180, 180),
            DataLabelTextColor = new CellColor(31, 31, 31),
            DataLabelPosition = ChartDataLabelPosition.OutsideEnd,
            ShowDataLabelPercentage = true
        });

        return workbook;
    }

    private static List<string> SchemaErrors(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var document = SpreadsheetDocument.Open(stream, false);
        var validator = new OpenXmlValidator(FileFormatVersions.Microsoft365);
        return validator.Validate(document)
            .Where(error => error.ErrorType == ValidationErrorType.Schema)
            .Select(error => $"{error.Description} @ {error.Path?.XPath}")
            .ToList();
    }

    private static XDocument LoadChartXml(byte[] package)
    {
        using var stream = new MemoryStream(package, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        return XlsxPackageTestFixtures.LoadPackageXml(archive, "xl/charts/chart1.xml", "xl/charts/chart1.xml");
    }

    private static ChartModel AssertReloadedSingleChart(
        byte[] package,
        ChartType expectedType,
        string? expectedTitle,
        string expectedDataRangeStart,
        string expectedDataRangeEnd)
    {
        using var stream = new MemoryStream(package, writable: false);
        var loaded = new XlsxFileAdapter().Load(stream);
        var sheet = loaded.GetSheetAt(0);
        var chart = sheet.Charts.Should().ContainSingle().Subject;

        chart.Type.Should().Be(expectedType);
        chart.Title.Should().Be(expectedTitle);
        chart.DataRange.Start.ToA1().Should().Be(expectedDataRangeStart);
        chart.DataRange.End.ToA1().Should().Be(expectedDataRangeEnd);
        sheet.GetValue(1, 1).Should().NotBeNull();

        return chart;
    }

    private static void AssertAllChartTextPropertiesStartWithBodyProperties(XDocument chartXml)
    {
        foreach (var textProperties in chartXml.Descendants(ChartNs + "txPr"))
        {
            textProperties.Elements().FirstOrDefault()?.Name
                .Should().Be(DrawingNs + "bodyPr");
        }
    }

    private static void AssertChildOrder(XElement parent, params string[] expectedNames)
    {
        var childNames = parent.Elements().Select(element => element.Name.LocalName).ToList();
        var startIndex = 0;
        foreach (var expectedName in expectedNames)
        {
            var foundIndex = childNames.FindIndex(startIndex, name => name == expectedName);
            foundIndex.Should().BeGreaterThanOrEqualTo(0, $"{expectedName} should appear after the previous checked child");
            startIndex = foundIndex + 1;
        }
    }
}
