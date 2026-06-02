using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public partial class FileAdapterSmokeTests
{
    [Fact]
    public void NativeJsonAdapter_RoundTrip_HistogramBinning()
    {
        var workbook = new Workbook("HistogramTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Histogram,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            HistogramBinning = new HistogramBinningModel(
                HistogramBinningMode.BinCount,
                BinCount: 5,
                OverflowThreshold: 90,
                UnderflowThreshold: 10),
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.HistogramBinning.Should().Be(new HistogramBinningModel(
            HistogramBinningMode.BinCount,
            BinCount: 5,
            OverflowThreshold: 90,
            UnderflowThreshold: 10));
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WaterfallTotalPointIndices()
    {
        var workbook = new Workbook("WaterfallTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Waterfall,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1)),
            WaterfallTotalPointIndices = [0, 3],
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.WaterfallTotalPointIndices.Should().Equal(0, 3);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_ChartLayout()
    {
        var workbook = new Workbook("ChartLayoutTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Quarter"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Cost"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(4));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Area,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)),
            Title = "Revenue",
            XAxisTitle = "Amount",
            YAxisTitle = "Quarter",
            ChartTitleTextColor = new CellColor(31, 78, 121),
            ChartTitleFontSize = 18,
            AxisTitleTextColor = new CellColor(89, 89, 89),
            AxisTitleFontSize = 12,
            ChartAreaFillColor = new CellColor(245, 245, 245),
            ChartAreaFillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.2),
            PlotAreaFillColor = new CellColor(250, 252, 255),
            PlotAreaFillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Light1),
            PlotAreaBorderColor = new CellColor(120, 120, 120),
            PlotAreaBorderThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25),
            PlotAreaBorderThickness = 2.25,
            LegendTextColor = new CellColor(40, 40, 40),
            LegendTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1),
            LegendFillColor = new CellColor(248, 248, 248),
            LegendFillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Light2),
            LegendBorderColor = new CellColor(180, 180, 180),
            LegendBorderThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3),
            LegendBorderThickness = 1.25,
            LegendFontSize = 11,
            DoughnutHoleSize = 0.72,
            FirstSliceAngle = 135,
            ExplodedSliceIndex = 0,
            ExplodedSliceDistance = 0.18,
            XAxisMinimum = 0,
            XAxisMaximum = 10,
            XAxisMajorUnit = 2,
            XAxisMinorUnit = 1,
            XAxisLogScale = true,
            XAxisNumberFormat = ChartDataLabelNumberFormat.Number,
            ShowXAxisMajorGridlines = true,
            ShowXAxisMinorGridlines = true,
            XAxisMajorGridlineColor = new CellColor(200, 200, 200),
            XAxisMinorGridlineColor = new CellColor(230, 230, 230),
            XAxisGridlineThickness = 1.5,
            XAxisMajorTickStyle = ChartAxisTickStyle.Outside,
            XAxisMinorTickStyle = ChartAxisTickStyle.Inside,
            ShowXAxisLabels = false,
            XAxisLabelTextColor = new CellColor(70, 70, 70),
            XAxisLabelFontSize = 10,
            XAxisLabelAngle = -45,
            XAxisLineColor = new CellColor(10, 20, 30),
            XAxisLineThickness = 2.5,
            YAxisMinimum = -5,
            YAxisMaximum = 25,
            YAxisMajorUnit = 5,
            YAxisMinorUnit = 2.5,
            YAxisLogScale = true,
            YAxisNumberFormat = ChartDataLabelNumberFormat.Currency,
            ShowYAxisMajorGridlines = true,
            ShowYAxisMinorGridlines = true,
            YAxisMajorGridlineColor = new CellColor(190, 190, 190),
            YAxisMinorGridlineColor = new CellColor(225, 225, 225),
            YAxisGridlineThickness = 2,
            YAxisMajorTickStyle = ChartAxisTickStyle.Cross,
            YAxisMinorTickStyle = ChartAxisTickStyle.None,
            ShowYAxisLabels = false,
            YAxisLabelTextColor = new CellColor(80, 80, 80),
            YAxisLabelFontSize = 11,
            YAxisLabelAngle = 90,
            YAxisLineColor = new CellColor(40, 50, 60),
            YAxisLineThickness = 3.5,
            LegendPosition = ChartLegendPosition.Bottom,
            LegendOverlay = true,
            ShowLegend = false,
            ShowDataLabels = true,
            DataLabelPosition = ChartDataLabelPosition.OutsideEnd,
            ShowDataLabelValue = false,
            ShowDataLabelLegendKey = true,
            ShowDataLabelBubbleSize = true,
            ShowDataLabelCategoryName = true,
            ShowDataLabelSeriesName = true,
            ShowDataLabelPercentage = true,
            DataLabelSeparator = ChartDataLabelSeparator.NewLine,
            DataLabelNumberFormat = ChartDataLabelNumberFormat.Currency,
            ShowDataLabelCallouts = true,
            DataLabelFillColor = new CellColor(255, 255, 225),
            DataLabelFillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, 0.4),
            DataLabelBorderColor = new CellColor(128, 128, 128),
            DataLabelBorderThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent5),
            DataLabelTextColor = new CellColor(30, 30, 30),
            DataLabelTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark2),
            DataLabelBorderThickness = 1.5,
            DataLabelFontSize = 13,
            DataLabelAngle = -35,
            ShowLinearTrendline = true,
            TrendlineType = ChartTrendlineType.Power,
            TrendlinePeriod = 3,
            TrendlineOrder = 4,
            ShowTrendlineEquation = true,
            ShowTrendlineRSquared = true,
            TrendlineColor = new CellColor(217, 83, 25),
            TrendlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent6),
            TrendlineThickness = 2.5,
            TrendlineDashStyle = ChartLineDashStyle.Solid,
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1],
            ComboLineSeriesIndexes = [1],
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    FillColor: new CellColor(0, 114, 178),
                    StrokeColor: new CellColor(0, 0, 0),
                    StrokeThickness: 2.5,
                    DashStyle: ChartLineDashStyle.Dot,
                    MarkerStyle: ChartMarkerStyle.Diamond,
                    MarkerSize: 7,
                    FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1),
                    StrokeThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2))
            ],
            PointDataLabelFormats =
            [
                new ChartPointDataLabelFormat(
                    1,
                    0,
                    FillColor: new CellColor(226, 239, 218),
                    BorderColor: new CellColor(112, 173, 71),
                    BorderThickness: 2,
                    TextColor: new CellColor(0, 97, 0),
                    FontSize: 14,
                    FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0.5),
                    BorderThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4),
                    TextThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1))
            ],
            UseComboLineForSecondarySeries = true,
            Left = 12,
            Top = 34,
            Width = 500,
            Height = 240
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.Type.Should().Be(ChartType.Area);
        chart.DataRange.Start.ToA1().Should().Be("A1");
        chart.DataRange.End.ToA1().Should().Be("C2");
        chart.Title.Should().Be("Revenue");
        chart.XAxisTitle.Should().Be("Amount");
        chart.YAxisTitle.Should().Be("Quarter");
        chart.ChartTitleTextColor.Should().Be(new CellColor(31, 78, 121));
        chart.ChartTitleFontSize.Should().Be(18);
        chart.AxisTitleTextColor.Should().Be(new CellColor(89, 89, 89));
        chart.AxisTitleFontSize.Should().Be(12);
        chart.ChartAreaFillColor.Should().Be(new CellColor(245, 245, 245));
        chart.ChartAreaFillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.2));
        chart.PlotAreaFillColor.Should().Be(new CellColor(250, 252, 255));
        chart.PlotAreaFillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Light1));
        chart.PlotAreaBorderColor.Should().Be(new CellColor(120, 120, 120));
        chart.PlotAreaBorderThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25));
        chart.PlotAreaBorderThickness.Should().Be(2.25);
        chart.LegendTextColor.Should().Be(new CellColor(40, 40, 40));
        chart.LegendTextThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1));
        chart.LegendFillColor.Should().Be(new CellColor(248, 248, 248));
        chart.LegendFillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Light2));
        chart.LegendBorderColor.Should().Be(new CellColor(180, 180, 180));
        chart.LegendBorderThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3));
        chart.LegendBorderThickness.Should().Be(1.25);
        chart.LegendFontSize.Should().Be(11);
        chart.DoughnutHoleSize.Should().Be(0.55);
        chart.FirstSliceAngle.Should().Be(0);
        chart.ExplodedSliceIndex.Should().Be(-1);
        chart.ExplodedSliceDistance.Should().Be(0.1);
        chart.XAxisMinimum.Should().BeNull();
        chart.XAxisMaximum.Should().BeNull();
        chart.XAxisMajorUnit.Should().BeNull();
        chart.XAxisMinorUnit.Should().BeNull();
        chart.XAxisLogScale.Should().BeFalse();
        chart.XAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        chart.ShowXAxisMajorGridlines.Should().BeFalse();
        chart.ShowXAxisMinorGridlines.Should().BeFalse();
        chart.XAxisMajorGridlineColor.Should().BeNull();
        chart.XAxisMinorGridlineColor.Should().BeNull();
        chart.XAxisGridlineThickness.Should().Be(1);
        chart.XAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        chart.XAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        chart.ShowXAxisLabels.Should().BeTrue();
        chart.XAxisLabelTextColor.Should().BeNull();
        chart.XAxisLabelFontSize.Should().Be(11);
        chart.XAxisLabelAngle.Should().Be(0);
        chart.XAxisLineColor.Should().BeNull();
        chart.XAxisLineThickness.Should().Be(1);
        chart.YAxisMinimum.Should().Be(-5);
        chart.YAxisMaximum.Should().Be(25);
        chart.YAxisMajorUnit.Should().Be(5);
        chart.YAxisMinorUnit.Should().Be(2.5);
        chart.YAxisLogScale.Should().BeTrue();
        chart.YAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.Currency);
        chart.ShowYAxisMajorGridlines.Should().BeTrue();
        chart.ShowYAxisMinorGridlines.Should().BeTrue();
        chart.YAxisMajorGridlineColor.Should().Be(new CellColor(190, 190, 190));
        chart.YAxisMinorGridlineColor.Should().Be(new CellColor(225, 225, 225));
        chart.YAxisGridlineThickness.Should().Be(2);
        chart.YAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Cross);
        chart.YAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        chart.ShowYAxisLabels.Should().BeFalse();
        chart.YAxisLabelTextColor.Should().Be(new CellColor(80, 80, 80));
        chart.YAxisLabelFontSize.Should().Be(11);
        chart.YAxisLabelAngle.Should().Be(90);
        chart.YAxisLineColor.Should().Be(new CellColor(40, 50, 60));
        chart.YAxisLineThickness.Should().Be(3.5);
        chart.LegendPosition.Should().Be(ChartLegendPosition.Bottom);
        chart.LegendOverlay.Should().BeTrue();
        chart.ShowLegend.Should().BeFalse();
        chart.ShowDataLabels.Should().BeTrue();
        chart.DataLabelPosition.Should().Be(ChartDataLabelPosition.OutsideEnd);
        chart.ShowDataLabelCategoryName.Should().BeTrue();
        chart.ShowDataLabelSeriesName.Should().BeTrue();
        chart.ShowDataLabelPercentage.Should().BeFalse();
        chart.DataLabelSeparator.Should().Be(ChartDataLabelSeparator.NewLine);
        chart.DataLabelNumberFormat.Should().Be(ChartDataLabelNumberFormat.Currency);
        chart.ShowDataLabelCallouts.Should().BeTrue();
        chart.DataLabelFillColor.Should().Be(new CellColor(255, 255, 225));
        chart.DataLabelFillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, 0.4));
        chart.DataLabelBorderColor.Should().Be(new CellColor(128, 128, 128));
        chart.DataLabelBorderThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent5));
        chart.DataLabelTextColor.Should().Be(new CellColor(30, 30, 30));
        chart.DataLabelTextThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark2));
        chart.DataLabelBorderThickness.Should().Be(1.5);
        chart.DataLabelFontSize.Should().Be(13);
        chart.DataLabelAngle.Should().Be(-35);
        chart.ShowLinearTrendline.Should().BeTrue();
        chart.TrendlineType.Should().Be(ChartTrendlineType.Power);
        chart.TrendlinePeriod.Should().Be(3);
        chart.TrendlineOrder.Should().Be(4);
        chart.ShowTrendlineEquation.Should().BeTrue();
        chart.ShowTrendlineRSquared.Should().BeTrue();
        chart.TrendlineColor.Should().Be(new CellColor(217, 83, 25));
        chart.TrendlineThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent6));
        chart.TrendlineThickness.Should().Be(2.5);
        chart.TrendlineDashStyle.Should().Be(ChartLineDashStyle.Solid);
        chart.ShowSecondaryAxis.Should().BeTrue();
        chart.SecondaryAxisSeriesIndexes.Should().Equal(1);
        chart.ComboLineSeriesIndexes.Should().Equal(1);
        chart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(
                0,
                FillColor: new CellColor(0, 114, 178),
                StrokeColor: new CellColor(0, 0, 0),
                StrokeThickness: 2.5,
                DashStyle: ChartLineDashStyle.Dot,
                FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1),
                StrokeThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2)));
        chart.PointDataLabelFormats.Should().ContainSingle().Which.Should().Be(
            new ChartPointDataLabelFormat(
                1,
                0,
                FillColor: new CellColor(226, 239, 218),
                BorderColor: new CellColor(112, 173, 71),
                BorderThickness: 2,
                TextColor: new CellColor(0, 97, 0),
                FontSize: 14,
                FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0.5),
                BorderThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4),
                TextThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1)));
        chart.UseComboLineForSecondarySeries.Should().BeTrue();
        chart.Left.Should().Be(12);
        chart.Top.Should().Be(34);
        chart.Width.Should().Be(500);
        chart.Height.Should().Be(240);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_PivotChartOptions()
    {
        var workbook = new Workbook("PivotChartNativeJsonTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(100));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            IsPivotChart = true,
            PivotSourceSheetName = "Pivot",
            PivotTableName = "PivotTable1",
            PivotCacheId = 7,
            ChartStyleId = 48,
            ShowPivotChartFieldButtons = false,
            ShowPivotChartReportFilterButtons = false,
            ShowPivotChartAxisFieldButtons = true,
            ShowPivotChartValueFieldButtons = false
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loadedChart = adapter.Load(ms).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.IsPivotChart.Should().BeTrue();
        loadedChart.PivotSourceSheetName.Should().Be("Pivot");
        loadedChart.PivotTableName.Should().Be("PivotTable1");
        loadedChart.PivotCacheId.Should().Be(7);
        loadedChart.ChartStyleId.Should().Be(48);
        loadedChart.ShowPivotChartFieldButtons.Should().BeFalse();
        loadedChart.ShowPivotChartReportFilterButtons.Should().BeFalse();
        loadedChart.ShowPivotChartAxisFieldButtons.Should().BeTrue();
        loadedChart.ShowPivotChartValueFieldButtons.Should().BeFalse();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_ChartDesignMetadata()
    {
        var workbook = new Workbook("ChartDesignNativeJsonTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            PivotFormatsXml = "<c:pivotFmts><c:pivotFmt /></c:pivotFmts>",
            Uses1904DateSystem = true,
            Language = "en-US",
            RoundedCorners = true,
            BlankDisplayMode = ChartBlankDisplayMode.Zero,
            ShowDataLabelsOverMaximum = true,
            AutoTitleDeleted = true,
            ShowDataInHiddenRowsAndColumns = true,
            ColorMapOverride = new ChartColorMapOverrideModel
            {
                UseMasterColorMapping = true,
                OverrideMappings = { ["accent1"] = "accent2" }
            },
            ExternalData = new ChartExternalDataModel
            {
                RelationshipId = "rIdExternal",
                RelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package",
                Target = "../externalLinks/externalLink1.xml",
                TargetMode = "External",
                AutoUpdate = true
            },
            PlotAreaLayout = new ChartManualLayoutModel { LayoutTarget = "inner", XMode = "factor", X = 0.1, Y = 0.2, Width = 0.8, Height = 0.6 },
            LegendLayout = new ChartManualLayoutModel { LayoutTarget = "inner", X = 0.76, Height = 0.7 },
            ThreeDView = new Chart3DViewModel
            {
                RotationX = 20,
                HeightPercent = 150,
                RotationY = 30,
                DepthPercent = 200,
                RightAngleAxes = false,
                Perspective = 45
            },
            Protection = new ChartProtectionModel { ChartObject = true, Data = true, Formatting = false, Selection = true, UserInterface = true },
            PrintSettings = new ChartPrintSettingsModel
            {
                PageMargins = new ChartPageMarginsModel { Left = 0.7, Right = 0.7, Top = 0.75, Bottom = 0.75, Header = 0.3, Footer = 0.3 },
                PageSetup = new ChartPageSetupModel { PaperSize = "9", Orientation = "landscape", Copies = 2, BlackAndWhite = true, Draft = false }
            }
        };
        sheet.Charts.Add(chart);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loadedChart = adapter.Load(ms).GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.PivotFormatsXml.Should().Be(chart.PivotFormatsXml);
        loadedChart.Uses1904DateSystem.Should().BeTrue();
        loadedChart.Language.Should().Be("en-US");
        loadedChart.RoundedCorners.Should().BeTrue();
        loadedChart.BlankDisplayMode.Should().Be(ChartBlankDisplayMode.Zero);
        loadedChart.ShowDataLabelsOverMaximum.Should().BeTrue();
        loadedChart.AutoTitleDeleted.Should().BeTrue();
        loadedChart.ShowDataInHiddenRowsAndColumns.Should().BeTrue();
        loadedChart.ColorMapOverride.Should().BeEquivalentTo(chart.ColorMapOverride);
        loadedChart.ExternalData.Should().BeEquivalentTo(chart.ExternalData);
        loadedChart.PlotAreaLayout.Should().BeEquivalentTo(chart.PlotAreaLayout);
        loadedChart.LegendLayout.Should().BeEquivalentTo(chart.LegendLayout);
        loadedChart.ThreeDView.Should().BeEquivalentTo(chart.ThreeDView);
        loadedChart.Protection.Should().BeEquivalentTo(chart.Protection);
        loadedChart.PrintSettings.Should().BeEquivalentTo(chart.PrintSettings);
    }
}
