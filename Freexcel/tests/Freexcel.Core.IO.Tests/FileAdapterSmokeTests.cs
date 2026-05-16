using System.Text;
using System.IO.Compression;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public class FileAdapterSmokeTests
{
    // ── Native JSON ───────────────────────────────────────────────────────────

    [Fact]
    public void NativeJsonAdapter_RoundTrip()
    {
        var workbook = new Workbook("Test");
        var s1 = workbook.AddSheet("Alpha");
        var s2 = workbook.AddSheet("Beta");
        s2.IsHidden = true;
        s2.TabColor = new CellColor(0, 176, 80);

        var a1 = new CellAddress(s1.Id, 1, 1);
        var a2 = new CellAddress(s1.Id, 2, 3);
        s1.SetCell(a1, new TextValue("foo"));
        s1.SetCell(a2, new TextValue("hello"));

        var b1 = new CellAddress(s2.Id, 1, 1);
        s2.SetFormula(b1, "A1+1");

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        loaded.SheetCount.Should().Be(2);
        loaded.GetSheetAt(0).Name.Should().Be("Alpha");
        loaded.GetSheetAt(1).Name.Should().Be("Beta");

        // NativeJsonAdapter stores values via record.ToString() so cells survive as non-blank.
        var ls1 = loaded.GetSheetAt(0);
        ls1.GetValue(1, 1).Should().NotBeOfType<BlankValue>();
        ls1.GetValue(2, 3).Should().NotBeOfType<BlankValue>();

        var ls2 = loaded.GetSheetAt(1);
        ls2.IsHidden.Should().BeTrue();
        ls2.TabColor.Should().Be(new CellColor(0, 176, 80));
        ls2.GetCell(1, 1)!.FormulaText.Should().Be("A1+1");
    }

    // ── XLSX ──────────────────────────────────────────────────────────────────

    [Fact]
    public void NativeJsonAdapter_RoundTrip_IgnoredFormulaErrors()
    {
        var workbook = new Workbook("IgnoredErrors");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var cell = Cell.FromFormula("1/0");
        cell.Value = ErrorValue.DivByZero;
        cell.IgnoreFormulaError = true;
        sheet.SetCell(address, cell);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).GetCell(1, 1)!.IgnoreFormulaError.Should().BeTrue();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_ErrorCheckingOptions()
    {
        var workbook = new Workbook("ErrorCheckingOptions");
        workbook.DisabledFormulaErrorCodes.Add(ErrorValue.DivByZero.Code);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.DisabledFormulaErrorCodes.Should().ContainSingle(ErrorValue.DivByZero.Code);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WatchedCells()
    {
        var workbook = new Workbook("WatchTest");
        var sheet = workbook.AddSheet("Sheet1");
        var watched = new CellAddress(sheet.Id, 2, 3);
        sheet.SetFormula(watched, "A1+1");
        workbook.WatchedCells.Add(watched);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loaded.WatchedCells.Should().ContainSingle()
            .Which.Should().Be(new CellAddress(loadedSheet.Id, 2, 3));
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_Scenarios()
    {
        var workbook = new Workbook("ScenarioTest");
        var sheet = workbook.AddSheet("Sheet1");
        workbook.Scenarios.Add(new WorkbookScenario(
            "Best Case",
            [
                new ScenarioCellValue(new CellAddress(sheet.Id, 1, 1), new NumberValue(42)),
                new ScenarioCellValue(new CellAddress(sheet.Id, 2, 1), new TextValue("manual"))
            ]));

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        var scenario = loaded.Scenarios.Should().ContainSingle().Subject;
        scenario.Name.Should().Be("Best Case");
        scenario.ChangingCells.Should().Contain(new ScenarioCellValue(
            new CellAddress(loadedSheet.Id, 1, 1),
            new NumberValue(42)));
        scenario.ChangingCells.Should().Contain(new ScenarioCellValue(
            new CellAddress(loadedSheet.Id, 2, 1),
            new TextValue("manual")));
    }

    [Fact]
    public void FileSavePlanner_TryResolveExistingPath_UsesAdapterForCurrentPath()
    {
        var adapter = new XlsxFileAdapter();

        var resolved = FileSavePlanner.TryResolveExistingPath(
            @"C:\work\book.xlsx",
            [adapter],
            out var target);

        resolved.Should().BeTrue();
        target.Should().NotBeNull();
        target!.Path.Should().Be(@"C:\work\book.xlsx");
        target.Adapter.Should().BeSameAs(adapter);
    }

    [Fact]
    public void FileSavePlanner_TryResolveExistingPath_ReturnsFalseForUnknownExtension()
    {
        var resolved = FileSavePlanner.TryResolveExistingPath(
            @"C:\work\book.unsupported",
            [new XlsxFileAdapter()],
            out var target);

        resolved.Should().BeFalse();
        target.Should().BeNull();
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
        chart.DoughnutHoleSize.Should().Be(0.72);
        chart.FirstSliceAngle.Should().Be(135);
        chart.ExplodedSliceIndex.Should().Be(0);
        chart.ExplodedSliceDistance.Should().Be(0.18);
        chart.XAxisMinimum.Should().Be(0);
        chart.XAxisMaximum.Should().Be(10);
        chart.XAxisMajorUnit.Should().Be(2);
        chart.XAxisMinorUnit.Should().Be(1);
        chart.XAxisLogScale.Should().BeTrue();
        chart.XAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.Number);
        chart.ShowXAxisMajorGridlines.Should().BeTrue();
        chart.ShowXAxisMinorGridlines.Should().BeTrue();
        chart.XAxisMajorGridlineColor.Should().Be(new CellColor(200, 200, 200));
        chart.XAxisMinorGridlineColor.Should().Be(new CellColor(230, 230, 230));
        chart.XAxisGridlineThickness.Should().Be(1.5);
        chart.XAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        chart.XAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.Inside);
        chart.ShowXAxisLabels.Should().BeFalse();
        chart.XAxisLabelTextColor.Should().Be(new CellColor(70, 70, 70));
        chart.XAxisLabelFontSize.Should().Be(10);
        chart.XAxisLabelAngle.Should().Be(-45);
        chart.XAxisLineColor.Should().Be(new CellColor(10, 20, 30));
        chart.XAxisLineThickness.Should().Be(2.5);
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
        chart.ShowDataLabelPercentage.Should().BeTrue();
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
                MarkerStyle: ChartMarkerStyle.Diamond,
                MarkerSize: 7,
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
    public void NativeJsonAdapter_Load_SanitizesChartIndexesAgainstPersistedDataRange()
    {
        var workbook = new Workbook("ChartIndexSanitizeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            ExplodedSliceIndex = 5,
            SecondaryAxisSeriesIndexes = [-1, 0, 1, 1, 2],
            ComboLineSeriesIndexes = [-1, 0, 1, 1, 2],
            SeriesFormats =
            [
                new ChartSeriesFormat(-1, FillColor: new CellColor(255, 0, 0)),
                new ChartSeriesFormat(0, FillColor: new CellColor(0, 114, 178), StrokeThickness: -1, MarkerSize: 99),
                new ChartSeriesFormat(2, FillColor: new CellColor(255, 192, 0))
            ],
            PointDataLabelFormats =
            [
                new ChartPointDataLabelFormat(-1, 0, FillColor: new CellColor(255, 0, 0)),
                new ChartPointDataLabelFormat(0, -1, FillColor: new CellColor(255, 0, 0)),
                new ChartPointDataLabelFormat(0, 0, FillColor: new CellColor(0, 114, 178)),
                new ChartPointDataLabelFormat(0, 0, FillColor: new CellColor(112, 48, 160), BorderThickness: 25, FontSize: 2),
                new ChartPointDataLabelFormat(1, 2, FillColor: new CellColor(255, 192, 0)),
                new ChartPointDataLabelFormat(2, 0, FillColor: new CellColor(255, 0, 0))
            ]
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.ExplodedSliceIndex.Should().Be(-1);
        chart.SecondaryAxisSeriesIndexes.Should().Equal(1);
        chart.ComboLineSeriesIndexes.Should().Equal(1);
        chart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(0, FillColor: new CellColor(0, 114, 178), StrokeThickness: 0.5, MarkerSize: 30));
        chart.PointDataLabelFormats.Should().ContainSingle().Which.Should().Be(
            new ChartPointDataLabelFormat(0, 0, FillColor: new CellColor(112, 48, 160), BorderThickness: 10, FontSize: 6));
    }

    [Fact]
    public void NativeJsonAdapter_Load_ClearsUnsupportedComboLineOverlayState()
    {
        var workbook = new Workbook("ChartComboSanitizeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [1]
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.UseComboLineForSecondarySeries.Should().BeFalse();
        chart.ComboLineSeriesIndexes.Should().BeEmpty();
    }

    [Fact]
    public void NativeJsonAdapter_Load_ClampsChartLabelAngles()
    {
        var workbook = new Workbook("ChartAngleSanitizeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            XAxisLabelAngle = -120,
            YAxisLabelAngle = 135,
            DataLabelAngle = 180
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.XAxisLabelAngle.Should().Be(-90);
        chart.YAxisLabelAngle.Should().Be(90);
        chart.DataLabelAngle.Should().Be(90);
    }

    [Fact]
    public void NativeJsonAdapter_Load_ClampsChartLayoutNumbers()
    {
        var workbook = new Workbook("ChartLayoutSanitizeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            ChartTitleFontSize = 2,
            AxisTitleFontSize = 100,
            PlotAreaBorderThickness = -1,
            LegendBorderThickness = 25,
            LegendFontSize = 2,
            DoughnutHoleSize = 2,
            FirstSliceAngle = 725,
            ExplodedSliceDistance = 2,
            XAxisMajorUnit = -5,
            XAxisMinorUnit = -1,
            XAxisGridlineThickness = 0,
            XAxisLabelFontSize = 100,
            XAxisLineThickness = 0,
            YAxisMajorUnit = -10,
            YAxisMinorUnit = -2,
            YAxisGridlineThickness = 25,
            YAxisLabelFontSize = 1,
            YAxisLineThickness = 25,
            DataLabelBorderThickness = 25,
            DataLabelFontSize = 1,
            TrendlinePeriod = 0,
            TrendlineOrder = 99,
            TrendlineThickness = 0
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.ChartTitleFontSize.Should().Be(6);
        chart.AxisTitleFontSize.Should().Be(72);
        chart.PlotAreaBorderThickness.Should().Be(0);
        chart.LegendBorderThickness.Should().Be(10);
        chart.LegendFontSize.Should().Be(6);
        chart.DoughnutHoleSize.Should().Be(0.9);
        chart.FirstSliceAngle.Should().Be(5);
        chart.ExplodedSliceDistance.Should().Be(0.5);
        chart.XAxisMajorUnit.Should().Be(double.Epsilon);
        chart.XAxisMinorUnit.Should().Be(double.Epsilon);
        chart.XAxisGridlineThickness.Should().Be(0.25);
        chart.XAxisLabelFontSize.Should().Be(72);
        chart.XAxisLineThickness.Should().Be(0.5);
        chart.YAxisMajorUnit.Should().Be(double.Epsilon);
        chart.YAxisMinorUnit.Should().Be(double.Epsilon);
        chart.YAxisGridlineThickness.Should().Be(10);
        chart.YAxisLabelFontSize.Should().Be(6);
        chart.YAxisLineThickness.Should().Be(10);
        chart.DataLabelBorderThickness.Should().Be(10);
        chart.DataLabelFontSize.Should().Be(6);
        chart.TrendlinePeriod.Should().Be(2);
        chart.TrendlineOrder.Should().Be(6);
        chart.TrendlineThickness.Should().Be(0.5);
    }

    [Fact]
    public void NativeJsonAdapter_Load_ReplacesInvalidChartDimensionsWithDefaults()
    {
        var workbook = new Workbook("ChartDimensionSanitizeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            Width = -1,
            Height = 0
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.Width.Should().Be(400);
        chart.Height.Should().Be(300);
    }

    [Fact]
    public void NativeJsonAdapter_Load_ReplacesInvalidChartChoicesWithDefaults()
    {
        var workbook = new Workbook("ChartChoiceSanitizeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = (ChartType)99,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            XAxisNumberFormat = (ChartDataLabelNumberFormat)99,
            XAxisMajorTickStyle = (ChartAxisTickStyle)99,
            XAxisMinorTickStyle = (ChartAxisTickStyle)99,
            YAxisNumberFormat = (ChartDataLabelNumberFormat)99,
            YAxisMajorTickStyle = (ChartAxisTickStyle)99,
            YAxisMinorTickStyle = (ChartAxisTickStyle)99,
            LegendPosition = (ChartLegendPosition)99,
            DataLabelPosition = (ChartDataLabelPosition)99,
            DataLabelSeparator = (ChartDataLabelSeparator)99,
            DataLabelNumberFormat = (ChartDataLabelNumberFormat)99,
            TrendlineType = (ChartTrendlineType)99,
            TrendlineDashStyle = (ChartLineDashStyle)99,
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    DashStyle: (ChartLineDashStyle)99,
                    MarkerStyle: (ChartMarkerStyle)99)
            ]
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.Type.Should().Be(ChartType.Column);
        chart.XAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        chart.XAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        chart.XAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        chart.YAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        chart.YAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        chart.YAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        chart.LegendPosition.Should().Be(ChartLegendPosition.Right);
        chart.DataLabelPosition.Should().Be(ChartDataLabelPosition.BestFit);
        chart.DataLabelSeparator.Should().Be(ChartDataLabelSeparator.Comma);
        chart.DataLabelNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        chart.TrendlineType.Should().Be(ChartTrendlineType.Linear);
        chart.TrendlineDashStyle.Should().Be(ChartLineDashStyle.Dash);
        chart.SeriesFormats.Should().ContainSingle().Which.Should().Be(new ChartSeriesFormat(0));
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorksheetViewMode()
    {
        var workbook = new Workbook("ViewModeTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ViewMode = WorksheetViewMode.PageBreakPreview;

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).ViewMode.Should().Be(WorksheetViewMode.PageBreakPreview);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorksheetViewOptions()
    {
        var workbook = new Workbook("ViewOptionsTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ShowGridlines = false;
        sheet.ShowHeadings = false;
        sheet.ShowRulers = false;
        sheet.ZoomPercent = 125;
        sheet.ShowFormulas = true;

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).ShowGridlines.Should().BeFalse();
        loaded.GetSheetAt(0).ShowHeadings.Should().BeFalse();
        loaded.GetSheetAt(0).ShowRulers.Should().BeFalse();
        loaded.GetSheetAt(0).ZoomPercent.Should().Be(125);
        loaded.GetSheetAt(0).ShowFormulas.Should().BeTrue();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorkbookWindowArrangement()
    {
        var workbook = new Workbook("WindowArrangementTest");
        workbook.AddSheet("Sheet1");
        workbook.WindowArrangement = WorkbookWindowArrangement.Cascade;

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.WindowArrangement.Should().Be(WorkbookWindowArrangement.Cascade);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorkbookTheme()
    {
        var workbook = new Workbook("ThemeTest");
        workbook.AddSheet("Sheet1");
        workbook.Theme = WorkbookTheme.Office
            .WithName("Freexcel Test Theme")
            .WithFonts("Aptos Display", "Aptos")
            .WithEffects("FreexcelEffects")
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(12, 34, 56))
            .WithColor(WorkbookThemeColorSlot.Hyperlink, new CellColor(1, 99, 193));

        var adapter = new NativeJsonAdapter();
        using var ms = new MemoryStream();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.Theme.Name.Should().Be("Freexcel Test Theme");
        loaded.Theme.MajorFontName.Should().Be("Aptos Display");
        loaded.Theme.MinorFontName.Should().Be("Aptos");
        loaded.Theme.EffectsName.Should().Be("FreexcelEffects");
        loaded.Theme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(new CellColor(12, 34, 56));
        loaded.Theme.GetColor(WorkbookThemeColorSlot.Hyperlink).Should().Be(new CellColor(1, 99, 193));
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_SplitPanes()
    {
        var workbook = new Workbook("SplitPaneTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SplitRow = 8;
        sheet.SplitColumn = 4;

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).SplitRow.Should().Be(8);
        loaded.GetSheetAt(0).SplitColumn.Should().Be(4);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_CustomViews()
    {
        var workbook = new Workbook("CustomViewTest");
        workbook.AddSheet("Sheet1");
        workbook.CustomViews.Add(new WorkbookCustomView(
            "Review",
            [new WorksheetCustomViewState(
                "Sheet1",
                WorksheetViewMode.PageLayout,
                0,
                0,
                null,
                3,
                ShowGridlines: false,
                ShowHeadings: false,
                ShowRulers: false,
                ZoomPercent: 125,
                ShowFormulas: true)]));

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var view = loaded.CustomViews.Should().ContainSingle().Subject;
        view.Name.Should().Be("Review");
        var state = view.Sheets.Should().ContainSingle().Subject;
        state.SheetName.Should().Be("Sheet1");
        state.ViewMode.Should().Be(WorksheetViewMode.PageLayout);
        state.ShowGridlines.Should().BeFalse();
        state.ShowHeadings.Should().BeFalse();
        state.ShowRulers.Should().BeFalse();
        state.ZoomPercent.Should().Be(125);
        state.ShowFormulas.Should().BeTrue();
        state.FrozenRows.Should().Be(0);
        state.SplitColumn.Should().Be(3);
    }

    [Fact]
    public void NativeJsonAdapter_Load_SanitizesInvalidViewPaneState()
    {
        var workbook = new Workbook("ViewPaneSanitizeTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SplitRow = 0;
        sheet.SplitColumn = CellAddress.MaxCol + 1;
        workbook.CustomViews.Add(new WorkbookCustomView(
            "Bad panes",
            [new WorksheetCustomViewState(
                "Sheet1",
                (WorksheetViewMode)99,
                CellAddress.MaxRow + 1,
                CellAddress.MaxCol + 1,
                0,
                CellAddress.MaxCol + 1)]));

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SplitRow.Should().BeNull();
        loadedSheet.SplitColumn.Should().BeNull();
        var state = loaded.CustomViews.Should().ContainSingle().Subject.Sheets.Should().ContainSingle().Subject;
        state.ViewMode.Should().Be(WorksheetViewMode.Normal);
        state.FrozenRows.Should().Be(0);
        state.FrozenCols.Should().Be(0);
        state.SplitRow.Should().BeNull();
        state.SplitColumn.Should().BeNull();
    }

    [Fact]
    public void NativeJsonAdapter_Load_DropsSplitPaneStateWhenFrozenPanesArePresent()
    {
        var workbook = new Workbook("ViewPaneMutualExclusionTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 2;
        sheet.SplitRow = 12;
        sheet.SplitColumn = 4;
        workbook.CustomViews.Add(new WorkbookCustomView(
            "Both pane modes",
            [new WorksheetCustomViewState(
                "Sheet1",
                WorksheetViewMode.Normal,
                1,
                2,
                12,
                4)]));

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.FrozenRows.Should().Be(1);
        loadedSheet.FrozenCols.Should().Be(2);
        loadedSheet.SplitRow.Should().BeNull();
        loadedSheet.SplitColumn.Should().BeNull();
        var state = loaded.CustomViews.Should().ContainSingle().Subject.Sheets.Should().ContainSingle().Subject;
        state.FrozenRows.Should().Be(1);
        state.FrozenCols.Should().Be(2);
        state.SplitRow.Should().BeNull();
        state.SplitColumn.Should().BeNull();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_PageLayoutSettings()
    {
        var workbook = new Workbook("PageLayoutTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 8, 4));
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PaperSize = WorksheetPaperSize.Legal;
        sheet.PageMargins = new WorksheetPageMargins(0.7, 0.8, 0.9, 1.1);
        sheet.HeaderMargin = 0.35;
        sheet.FooterMargin = 0.45;
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 2);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        sheet.PageHeader = new WorksheetHeaderFooter("Left header", "Center header", "Right header");
        sheet.PageFooter = new WorksheetHeaderFooter("Left footer", "Page &[Page]", "Right footer");
        sheet.FirstPageHeader = new WorksheetHeaderFooter("First header left", "First header center", "First header right");
        sheet.FirstPageFooter = new WorksheetHeaderFooter("First footer left", "First footer center", "First footer right");
        sheet.EvenPageHeader = new WorksheetHeaderFooter("Even header left", "Even header center", "Even header right");
        sheet.EvenPageFooter = new WorksheetHeaderFooter("Even footer left", "Even footer center", "Even footer right");
        sheet.DifferentFirstPageHeaderFooter = true;
        sheet.DifferentOddEvenHeaderFooter = true;
        sheet.HeaderFooterScaleWithDocument = false;
        sheet.HeaderFooterAlignWithMargins = false;
        sheet.CenterHorizontallyOnPage = true;
        sheet.CenterVerticallyOnPage = true;
        sheet.PageOrder = WorksheetPageOrder.OverThenDown;
        sheet.FirstPageNumber = 5;
        sheet.PrintBlackAndWhite = true;
        sheet.PrintDraftQuality = true;
        sheet.PrintQualityDpi = 600;
        sheet.PrintErrorValue = WorksheetPrintErrorValue.Blank;
        sheet.PrintComments = WorksheetPrintComments.AtEnd;
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 1, 2);
        sheet.RowPageBreaks.Add(20);
        sheet.ColumnPageBreaks.Add(5);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.PrintArea.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 2, 2),
            new CellAddress(loadedSheet.Id, 8, 4)));
        loadedSheet.PageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
        loadedSheet.PaperSize.Should().Be(WorksheetPaperSize.Legal);
        loadedSheet.PageMargins.Should().Be(new WorksheetPageMargins(0.7, 0.8, 0.9, 1.1));
        loadedSheet.HeaderMargin.Should().Be(0.35);
        loadedSheet.FooterMargin.Should().Be(0.45);
        loadedSheet.PrintGridlines.Should().BeTrue();
        loadedSheet.PrintHeadings.Should().BeTrue();
        loadedSheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(1, 2));
        loadedSheet.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(1, 1));
        loadedSheet.PageHeader.Should().Be(new WorksheetHeaderFooter("Left header", "Center header", "Right header"));
        loadedSheet.PageFooter.Should().Be(new WorksheetHeaderFooter("Left footer", "Page &[Page]", "Right footer"));
        loadedSheet.FirstPageHeader.Should().Be(new WorksheetHeaderFooter("First header left", "First header center", "First header right"));
        loadedSheet.FirstPageFooter.Should().Be(new WorksheetHeaderFooter("First footer left", "First footer center", "First footer right"));
        loadedSheet.EvenPageHeader.Should().Be(new WorksheetHeaderFooter("Even header left", "Even header center", "Even header right"));
        loadedSheet.EvenPageFooter.Should().Be(new WorksheetHeaderFooter("Even footer left", "Even footer center", "Even footer right"));
        loadedSheet.DifferentFirstPageHeaderFooter.Should().BeTrue();
        loadedSheet.DifferentOddEvenHeaderFooter.Should().BeTrue();
        loadedSheet.HeaderFooterScaleWithDocument.Should().BeFalse();
        loadedSheet.HeaderFooterAlignWithMargins.Should().BeFalse();
        loadedSheet.CenterHorizontallyOnPage.Should().BeTrue();
        loadedSheet.CenterVerticallyOnPage.Should().BeTrue();
        loadedSheet.PageOrder.Should().Be(WorksheetPageOrder.OverThenDown);
        loadedSheet.FirstPageNumber.Should().Be(5);
        loadedSheet.PrintBlackAndWhite.Should().BeTrue();
        loadedSheet.PrintDraftQuality.Should().BeTrue();
        loadedSheet.PrintQualityDpi.Should().Be(600);
        loadedSheet.PrintErrorValue.Should().Be(WorksheetPrintErrorValue.Blank);
        loadedSheet.PrintComments.Should().Be(WorksheetPrintComments.AtEnd);
        loadedSheet.ScaleToFit.Should().Be(new WorksheetScaleToFit(null, 1, 2));
        loadedSheet.RowPageBreaks.Should().Contain(20u);
        loadedSheet.ColumnPageBreaks.Should().Contain(5u);
    }

    [Fact]
    public void NativeJsonAdapter_Load_SanitizesInvalidPageSetupNumbers()
    {
        var workbook = new Workbook("PageSetupSanitizeTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.PageMargins = new WorksheetPageMargins(-1, 0.8, -2, 1.1);
        sheet.HeaderMargin = -0.35;
        sheet.FooterMargin = -0.45;
        sheet.FirstPageNumber = 0;
        sheet.PrintQualityDpi = 0;
        sheet.ScaleToFit = new WorksheetScaleToFit(5, 0, -1);
        sheet.RowPageBreaks.Add(1);
        sheet.RowPageBreaks.Add(CellAddress.MaxRow + 1);
        sheet.ColumnPageBreaks.Add(1);
        sheet.ColumnPageBreaks.Add(CellAddress.MaxCol + 1);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.PageMargins.Should().Be(WorksheetPageMargins.Narrow);
        loadedSheet.HeaderMargin.Should().Be(0.3);
        loadedSheet.FooterMargin.Should().Be(0.3);
        loadedSheet.FirstPageNumber.Should().BeNull();
        loadedSheet.PrintQualityDpi.Should().BeNull();
        loadedSheet.ScaleToFit.Should().Be(WorksheetScaleToFit.Default);
        loadedSheet.RowPageBreaks.Should().BeEmpty();
        loadedSheet.ColumnPageBreaks.Should().BeEmpty();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_Pictures()
    {
        var workbook = new Workbook("PictureTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 4),
            SourceRowCount = 2,
            SourceColumnCount = 2,
            Width = 160,
            Height = 40,
            AltText = "Copied range snapshot",
            Cells =
            {
                new PictureCellSnapshot(0, 0, "A"),
                new PictureCellSnapshot(1, 1, "D")
            }
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var picture = loaded.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;
        picture.Anchor.Row.Should().Be(3);
        picture.Anchor.Col.Should().Be(4);
        picture.SourceRowCount.Should().Be(2);
        picture.AltText.Should().Be("Copied range snapshot");
        picture.Cells.Should().Contain(cell => cell.RowOffset == 1 && cell.ColumnOffset == 1 && cell.Text == "D");
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_ImagePicture()
    {
        var workbook = new Workbook("ImagePictureTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = PictureKind.Image,
            ImageBytes = [9, 8, 7],
            ContentType = "image/png",
            Width = 90,
            Height = 60,
            RotationDegrees = 30,
            AltText = "Product photo"
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var picture = loaded.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;
        picture.Kind.Should().Be(PictureKind.Image);
        picture.ImageBytes.Should().Equal(9, 8, 7);
        picture.ContentType.Should().Be("image/png");
        picture.Width.Should().Be(90);
        picture.Height.Should().Be(60);
        picture.RotationDegrees.Should().Be(30);
        picture.AltText.Should().Be("Product photo");
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorksheetBackgroundImage()
    {
        var workbook = new Workbook("BackgroundTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.BackgroundImage = new WorksheetBackgroundImage([4, 5, 6], "image/png", "grid-bg.png");

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var background = loaded.GetSheetAt(0).BackgroundImage;
        background.Should().NotBeNull();
        background!.ImageBytes.Should().Equal(4, 5, 6);
        background.ContentType.Should().Be("image/png");
        background.FileName.Should().Be("grid-bg.png");
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_WorksheetBackgroundImage()
    {
        var workbook = new Workbook("BackgroundXlsxTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.BackgroundImage = new WorksheetBackgroundImage([4, 5, 6], "image/png", "grid-bg.png");

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var background = loaded.GetSheetAt(0).BackgroundImage;
        background.Should().NotBeNull();
        background!.ImageBytes.Should().Equal(4, 5, 6);
        background.ContentType.Should().Be("image/png");
        background.FileName.Should().Be("grid-bg.png");
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_TextBoxesAndDrawingShapes()
    {
        var workbook = new Workbook("DrawingObjectTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Text = "Review note",
            Width = 220,
            Height = 120,
            RotationDegrees = 20,
            FillColor = new CellColor(240, 250, 255),
            OutlineColor = new CellColor(70, 80, 90),
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.25),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25),
            AltText = "Review note callout"
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 4, 3),
            Kind = DrawingShapeKind.Ellipse,
            Width = 140,
            Height = 90,
            RotationDegrees = 45,
            FillColor = new CellColor(200, 210, 220),
            OutlineColor = new CellColor(30, 40, 50),
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0.5),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, -0.5),
            AltText = "Approval marker"
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var textBox = loaded.GetSheetAt(0).TextBoxes.Should().ContainSingle().Subject;
        textBox.Anchor.Row.Should().Be(2);
        textBox.Anchor.Col.Should().Be(2);
        textBox.Text.Should().Be("Review note");
        textBox.Width.Should().Be(220);
        textBox.Height.Should().Be(120);
        textBox.RotationDegrees.Should().Be(20);
        textBox.FillColor.Should().Be(new CellColor(240, 250, 255));
        textBox.OutlineColor.Should().Be(new CellColor(70, 80, 90));
        textBox.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.25));
        textBox.OutlineThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25));
        textBox.AltText.Should().Be("Review note callout");
        var shape = loaded.GetSheetAt(0).DrawingShapes.Should().ContainSingle().Subject;
        shape.Anchor.Row.Should().Be(4);
        shape.Anchor.Col.Should().Be(3);
        shape.Kind.Should().Be(DrawingShapeKind.Ellipse);
        shape.Width.Should().Be(140);
        shape.Height.Should().Be(90);
        shape.RotationDegrees.Should().Be(45);
        shape.FillColor.Should().Be(new CellColor(200, 210, 220));
        shape.OutlineColor.Should().Be(new CellColor(30, 40, 50));
        shape.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0.5));
        shape.OutlineThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, -0.5));
        shape.AltText.Should().Be("Approval marker");
    }

    [Fact]
    public void NativeJsonAdapter_Load_ReplacesInvalidObjectKindsWithDefaults()
    {
        var workbook = new Workbook("ObjectKindSanitizeTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = (PictureKind)99,
            Width = 90,
            Height = 60
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 4, 3),
            Kind = (DrawingShapeKind)99,
            Width = 140,
            Height = 90
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).Pictures.Should().ContainSingle()
            .Which.Kind.Should().Be(PictureKind.CellRangeSnapshot);
        loaded.GetSheetAt(0).DrawingShapes.Should().ContainSingle()
            .Which.Kind.Should().Be(DrawingShapeKind.Rectangle);
    }

    [Fact]
    public void NativeJsonAdapter_Load_ReplacesInvalidObjectDimensionsWithDefaults()
    {
        var workbook = new Workbook("ObjectDimensionSanitizeTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Width = -1,
            Height = 0
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 2),
            Text = "Note",
            Width = -5,
            Height = 0
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 4, 3),
            Width = 0,
            Height = -10
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        var picture = loadedSheet.Pictures.Should().ContainSingle().Subject;
        picture.Width.Should().Be(240);
        picture.Height.Should().Be(140);
        var textBox = loadedSheet.TextBoxes.Should().ContainSingle().Subject;
        textBox.Width.Should().Be(180);
        textBox.Height.Should().Be(80);
        var shape = loadedSheet.DrawingShapes.Should().ContainSingle().Subject;
        shape.Width.Should().Be(120);
        shape.Height.Should().Be(70);
    }

    [Fact]
    public void NativeJsonAdapter_Load_NormalizesObjectRotation()
    {
        var workbook = new Workbook("ObjectRotationSanitizeTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            RotationDegrees = -90
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 2),
            Text = "Note",
            RotationDegrees = 450
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 4, 3),
            RotationDegrees = 725
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.Pictures.Should().ContainSingle().Which.RotationDegrees.Should().Be(270);
        loadedSheet.TextBoxes.Should().ContainSingle().Which.RotationDegrees.Should().Be(90);
        loadedSheet.DrawingShapes.Should().ContainSingle().Which.RotationDegrees.Should().Be(5);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_ValuesAndFormulas()
    {
        var workbook = new Workbook("XlsxTest");
        var sheet = workbook.AddSheet("Data");

        var addr1 = new CellAddress(sheet.Id, 1, 1);
        var addr2 = new CellAddress(sheet.Id, 1, 2);
        var addr3 = new CellAddress(sheet.Id, 2, 1);
        sheet.SetCell(addr1, new NumberValue(3.14));
        sheet.SetCell(addr2, new TextValue("world"));
        var formulaCell = Cell.FromFormula("A1*2");
        formulaCell.Value = new NumberValue(6.28);
        sheet.SetCell(addr3, formulaCell);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        loaded.SheetCount.Should().Be(1);
        var ls = loaded.GetSheetAt(0);
        ((NumberValue)ls.GetValue(1, 1)).Value.Should().BeApproximately(3.14, 1e-9);
        ((TextValue)ls.GetValue(1, 2)).Value.Should().Be("world");
        var formulaAddr = new CellAddress(ls.Id, 2, 1);
        ls.GetCell(2, 1)!.FormulaText.Should().NotBeNullOrEmpty();
        // ClosedXML preserves cached formula values on round-trip
        var reloadedFormulaCell = loaded.GetSheet(sheet.Name)!.GetCell(formulaAddr);
        reloadedFormulaCell!.Value.Should().NotBeOfType<BlankValue>();
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_NamedRange_OnSheetWithApostrophe()
    {
        var workbook = new Workbook("NamedRangeTest");
        var sheet = workbook.AddSheet("Bob's Sheet");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        workbook.DefineNamedRange("SalesData", range);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.TryGetNamedRange("SalesData", out var loadedRange).Should().BeTrue();
        var loadedSheet = loaded.GetSheet("Bob's Sheet");
        loadedSheet.Should().NotBeNull();
        loadedRange.Start.Sheet.Should().Be(loadedSheet!.Id);
        loadedRange.Start.ToA1().Should().Be("A1");
        loadedRange.End.ToA1().Should().Be("A2");
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_Styles()
    {
        var workbook = new Workbook("StyleTest");
        var sheet = workbook.AddSheet("S1");

        var style = new CellStyle
        {
            Bold = true,
            FontColor = new CellColor(200, 0, 0),
        };
        var styleId = workbook.RegisterStyle(style);

        var addr = new CellAddress(sheet.Id, 1, 1);
        var cell = Cell.FromValue(new TextValue("styled"));
        cell.StyleId = styleId;
        sheet.SetCell(addr, cell);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        var loadedCell = loaded.GetSheetAt(0).GetCell(1, 1);
        loadedCell.Should().NotBeNull();
        var loadedStyle = loaded.GetStyle(loadedCell!.StyleId);
        loadedStyle.Bold.Should().BeTrue();
        loadedStyle.FontColor.R.Should().Be(200);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_ColumnWidths()
    {
        var workbook = new Workbook("WidthTest");
        var sheet = workbook.AddSheet("S1");
        sheet.ColumnWidths[2] = 25.0;

        // Put cells in both col 1 and col 2 so ColumnsUsed() sees col 2 and reads back its width.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("y"));

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).ColumnWidths.Should().ContainKey(2u);
        loaded.GetSheetAt(0).ColumnWidths[2u].Should().BeApproximately(25.0, 1.0);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_HiddenRowsAndColumns()
    {
        var workbook = new Workbook("HiddenLayoutTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("visible"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new TextValue("hidden markers"));
        sheet.HiddenRows.Add(3);
        sheet.HiddenCols.Add(4);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.HiddenRows.Should().Contain(3u);
        loadedSheet.HiddenCols.Should().Contain(4u);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_SplitPanes_PreservesSplitInsteadOfFreeze()
    {
        var workbook = new Workbook("SplitPaneXlsxTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("visible"));
        sheet.SplitRow = 8;
        sheet.SplitColumn = 4;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SplitRow.Should().Be(8);
        loadedSheet.SplitColumn.Should().Be(4);
        loadedSheet.FrozenRows.Should().Be(0);
        loadedSheet.FrozenCols.Should().Be(0);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_FreezePanes_PreservesFreezeInsteadOfSplit()
    {
        var workbook = new Workbook("FreezePaneXlsxTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("visible"));
        sheet.FrozenRows = 2;
        sheet.FrozenCols = 1;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.FrozenRows.Should().Be(2);
        loadedSheet.FrozenCols.Should().Be(1);
        loadedSheet.SplitRow.Should().BeNull();
        loadedSheet.SplitColumn.Should().BeNull();
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_HiddenSheetAndTabColor()
    {
        var workbook = new Workbook("HiddenSheetTest");
        var visible = workbook.AddSheet("Visible");
        visible.SetCell(new CellAddress(visible.Id, 1, 1), new TextValue("visible"));
        var hidden = workbook.AddSheet("Hidden");
        hidden.SetCell(new CellAddress(hidden.Id, 1, 1), new TextValue("hidden"));
        hidden.IsHidden = true;
        hidden.TabColor = new CellColor(255, 192, 0);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedHidden = loaded.GetSheet("Hidden");
        loadedHidden.Should().NotBeNull();
        loadedHidden!.IsHidden.Should().BeTrue();
        loadedHidden.TabColor.Should().Be(new CellColor(255, 192, 0));
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_HiddenEmptyRowsAndColumns()
    {
        var workbook = new Workbook("HiddenEmptyLayoutTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("anchor"));
        sheet.HiddenRows.Add(10);
        sheet.HiddenCols.Add(7);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.HiddenRows.Should().Contain(10u);
        loadedSheet.HiddenCols.Should().Contain(7u);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_PrintArea()
    {
        var workbook = new Workbook("PrintAreaTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("outside"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("inside"));
        sheet.PrintArea = new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 4, 3));

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.PrintArea.Should().NotBeNull();
        loadedSheet.PrintArea!.Value.Start.Row.Should().Be(2);
        loadedSheet.PrintArea.Value.Start.Col.Should().Be(2);
        loadedSheet.PrintArea.Value.End.Row.Should().Be(4);
        loadedSheet.PrintArea.Value.End.Col.Should().Be(3);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_PageOrientation()
    {
        var workbook = new Workbook("PageOrientationTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).PageOrientation.Should().Be(WorksheetPageOrientation.Landscape);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_PaperSize()
    {
        var workbook = new Workbook("PaperSizeTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.PaperSize = WorksheetPaperSize.Legal;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).PaperSize.Should().Be(WorksheetPaperSize.Legal);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_PageMargins()
    {
        var workbook = new Workbook("PageMarginsTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.PageMargins = WorksheetPageMargins.Wide;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).PageMargins.Left.Should().BeApproximately(1.25, 0.001);
        loaded.GetSheetAt(0).PageMargins.Right.Should().BeApproximately(1.25, 0.001);
        loaded.GetSheetAt(0).PageMargins.Top.Should().BeApproximately(1.0, 0.001);
        loaded.GetSheetAt(0).PageMargins.Bottom.Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_PrintGridlinesAndHeadings()
    {
        var workbook = new Workbook("PrintOptionsTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).PrintGridlines.Should().BeTrue();
        loaded.GetSheetAt(0).PrintHeadings.Should().BeTrue();
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_WorksheetViewOptions()
    {
        var workbook = new Workbook("ViewOptionsTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.ShowGridlines = false;
        sheet.ShowHeadings = false;
        sheet.ShowRulers = false;
        sheet.ZoomPercent = 125;
        sheet.ShowFormulas = true;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).ShowGridlines.Should().BeFalse();
        loaded.GetSheetAt(0).ShowHeadings.Should().BeFalse();
        loaded.GetSheetAt(0).ShowRulers.Should().BeFalse();
        loaded.GetSheetAt(0).ZoomPercent.Should().Be(125);
        loaded.GetSheetAt(0).ShowFormulas.Should().BeTrue();
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_PrintTitlesPageBreaksAndScaleToFit()
    {
        var workbook = new Workbook("PageSetupTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.PrintTitleRows = new WorksheetRepeatRange(1, 2);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(1, 1);
        sheet.HeaderMargin = 0.35;
        sheet.FooterMargin = 0.45;
        sheet.PageHeader = new WorksheetHeaderFooter("Left header", "Center header", "Right header");
        sheet.PageFooter = new WorksheetHeaderFooter("Left footer", "Page &[Page] of &[Pages]", "Right footer");
        sheet.FirstPageHeader = new WorksheetHeaderFooter("First header left", "First header center", "First header right");
        sheet.FirstPageFooter = new WorksheetHeaderFooter("First footer left", "First footer center", "First footer right");
        sheet.EvenPageHeader = new WorksheetHeaderFooter("Even header left", "Even header center", "Even header right");
        sheet.EvenPageFooter = new WorksheetHeaderFooter("Even footer left", "Even footer center", "Even footer right");
        sheet.DifferentFirstPageHeaderFooter = true;
        sheet.DifferentOddEvenHeaderFooter = true;
        sheet.HeaderFooterScaleWithDocument = false;
        sheet.HeaderFooterAlignWithMargins = false;
        sheet.CenterHorizontallyOnPage = true;
        sheet.CenterVerticallyOnPage = true;
        sheet.PageOrder = WorksheetPageOrder.OverThenDown;
        sheet.FirstPageNumber = 5;
        sheet.PrintBlackAndWhite = true;
        sheet.PrintDraftQuality = true;
        sheet.PrintQualityDpi = 600;
        sheet.PrintErrorValue = WorksheetPrintErrorValue.Dash;
        sheet.PrintComments = WorksheetPrintComments.AsDisplayed;
        sheet.RowPageBreaks.Add(20);
        sheet.ColumnPageBreaks.Add(4);
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 1, 1);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(1, 2));
        loadedSheet.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(1, 1));
        loadedSheet.HeaderMargin.Should().Be(0.35);
        loadedSheet.FooterMargin.Should().Be(0.45);
        loadedSheet.PageHeader.Should().Be(new WorksheetHeaderFooter("Left header", "Center header", "Right header"));
        loadedSheet.PageFooter.Should().Be(new WorksheetHeaderFooter("Left footer", "Page &[Page] of &[Pages]", "Right footer"));
        loadedSheet.FirstPageHeader.Should().Be(new WorksheetHeaderFooter("First header left", "First header center", "First header right"));
        loadedSheet.FirstPageFooter.Should().Be(new WorksheetHeaderFooter("First footer left", "First footer center", "First footer right"));
        loadedSheet.EvenPageHeader.Should().Be(new WorksheetHeaderFooter("Even header left", "Even header center", "Even header right"));
        loadedSheet.EvenPageFooter.Should().Be(new WorksheetHeaderFooter("Even footer left", "Even footer center", "Even footer right"));
        loadedSheet.DifferentFirstPageHeaderFooter.Should().BeTrue();
        loadedSheet.DifferentOddEvenHeaderFooter.Should().BeTrue();
        loadedSheet.HeaderFooterScaleWithDocument.Should().BeFalse();
        loadedSheet.HeaderFooterAlignWithMargins.Should().BeFalse();
        loadedSheet.CenterHorizontallyOnPage.Should().BeTrue();
        loadedSheet.CenterVerticallyOnPage.Should().BeTrue();
        loadedSheet.PageOrder.Should().Be(WorksheetPageOrder.OverThenDown);
        loadedSheet.FirstPageNumber.Should().Be(5);
        loadedSheet.PrintBlackAndWhite.Should().BeTrue();
        loadedSheet.PrintDraftQuality.Should().BeTrue();
        loadedSheet.PrintQualityDpi.Should().Be(600);
        loadedSheet.PrintErrorValue.Should().Be(WorksheetPrintErrorValue.Dash);
        loadedSheet.PrintComments.Should().Be(WorksheetPrintComments.AsDisplayed);
        loadedSheet.RowPageBreaks.Should().Contain(20u);
        loadedSheet.ColumnPageBreaks.Should().Contain(4u);
        loadedSheet.ScaleToFit.Should().Be(new WorksheetScaleToFit(null, 1, 1));
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_CalculationMode()
    {
        var workbook = new Workbook("CalculationModeTest");
        workbook.CalculationMode = WorkbookCalculationMode.Manual;
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.CalculationMode.Should().Be(WorkbookCalculationMode.Manual);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_Hyperlinks()
    {
        var workbook = new Workbook("HyperlinkTest");
        var sheet = workbook.AddSheet("S1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new TextValue("Example"));
        sheet.Hyperlinks[addr] = "https://example.com/docs";

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        var loadedAddr = new CellAddress(loadedSheet.Id, 1, 1);
        loadedSheet.Hyperlinks[loadedAddr].Should().Be("https://example.com/docs");
        loadedSheet.GetValue(loadedAddr).Should().Be(new TextValue("Example"));
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_SheetProtection()
    {
        var workbook = new Workbook("ProtectionTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));
        sheet.IsProtected = true;
        sheet.ProtectionPassword = "secret";

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.IsProtected.Should().BeTrue();
        loadedSheet.ProtectionPassword.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_UnlockedCellStyle()
    {
        var workbook = new Workbook("UnlockedStyleTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var styleId = workbook.RegisterStyle(new CellStyle { Locked = false });
        var cell = Cell.FromValue(new TextValue("editable"));
        cell.StyleId = styleId;
        sheet.SetCell(address, cell);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedCell = loaded.GetSheetAt(0).GetCell(1, 1);
        loadedCell.Should().NotBeNull();
        loaded.GetStyle(loadedCell!.StyleId).Locked.Should().BeFalse();
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_CellComments()
    {
        var workbook = new Workbook("CommentTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue("review"));
        sheet.Comments[address] = "Check this input";

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        var loadedAddress = new CellAddress(loadedSheet.Id, 2, 3);
        loadedSheet.Comments.Should().ContainKey(loadedAddress);
        loadedSheet.Comments[loadedAddress].Should().Be("Check this input");
    }

    [Theory]
    [InlineData("#DIV/0!")]
    [InlineData("#VALUE!")]
    [InlineData("#REF!")]
    [InlineData("#NAME?")]
    [InlineData("#NULL!")]
    [InlineData("#N/A")]
    [InlineData("#NUM!")]
    public void XlsxAdapter_RoundTrip_ErrorValues_PreservesErrorCode(string errorCode)
    {
        var workbook = new Workbook("ErrorRoundTrip");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new ErrorValue(errorCode));

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).GetValue(1, 1).Should().Be(new ErrorValue(errorCode));
    }

    // ── XLSX — conditional formatting round-trip ──────────────────────────────

    [Fact]
    public void XlsxAdapter_RoundTrip_ConditionalFormat_CellValueRule_Survives()
    {
        var workbook = new Workbook("CfTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 1)),
            RuleType    = CfRuleType.CellValue,
            Operator    = CfOperator.GreaterThan,
            Value1      = "5",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        };
        sheet.ConditionalFormats.Add(cf);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.ConditionalFormats.Should().NotBeEmpty("CF rule must survive XLSX round-trip");
        var rule = loadedSheet.ConditionalFormats[0];
        rule.RuleType.Should().Be(CfRuleType.CellValue);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_DataValidation_ListRule_Survives()
    {
        var workbook = new Workbook("DvTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Apple"));

        var dv = new DataValidation
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 10, 1)),
            Type     = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            AlertStyle = DvAlertStyle.Information,
            ShowInputMessage = false,
            ShowErrorMessage = false
        };
        sheet.DataValidations.Add(dv);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.DataValidations.Should().NotBeEmpty("DV rule must survive XLSX round-trip");
        var rule = loadedSheet.DataValidations[0];
        rule.Type.Should().Be(DvType.List);
        rule.AlertStyle.Should().Be(DvAlertStyle.Information);
        rule.ShowInputMessage.Should().BeFalse();
        rule.ShowErrorMessage.Should().BeFalse();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_DataValidationRule_Survives()
    {
        var workbook = new Workbook("DvNativeTest");
        var sheet = workbook.AddSheet("S1");
        var dv = new DataValidation
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 1)),
            Type = DvType.Time,
            Operator = DvOperator.Between,
            Formula1 = "09:00",
            Formula2 = "17:30",
            AllowBlank = false,
            ShowDropdown = false,
            AlertStyle = DvAlertStyle.Warning,
            ShowInputMessage = false,
            ShowErrorMessage = false,
            ErrorTitle = "Invalid time",
            ErrorMessage = "Enter a time during business hours.",
            PromptTitle = "Business hours",
            PromptMessage = "Use 09:00 through 17:30."
        };
        sheet.DataValidations.Add(dv);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.DataValidations.Should().ContainSingle();
        var rule = loadedSheet.DataValidations[0];
        rule.AppliesTo.Start.ToA1().Should().Be("A1");
        rule.AppliesTo.End.ToA1().Should().Be("A5");
        rule.Type.Should().Be(DvType.Time);
        rule.Operator.Should().Be(DvOperator.Between);
        rule.Formula1.Should().Be("09:00");
        rule.Formula2.Should().Be("17:30");
        rule.AllowBlank.Should().BeFalse();
        rule.ShowDropdown.Should().BeFalse();
        rule.AlertStyle.Should().Be(DvAlertStyle.Warning);
        rule.ShowInputMessage.Should().BeFalse();
        rule.ShowErrorMessage.Should().BeFalse();
        rule.ErrorTitle.Should().Be("Invalid time");
        rule.ErrorMessage.Should().Be("Enter a time during business hours.");
        rule.PromptTitle.Should().Be("Business hours");
        rule.PromptMessage.Should().Be("Use 09:00 through 17:30.");
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_MergedRegions_Survive()
    {
        var workbook = new Workbook("MergeTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("merged"));
        sheet.MergedRegions.Add(new GridRange(
            new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 3)));

        var ms = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, ms);
        ms.Position = 0;
        var loaded = new XlsxFileAdapter().Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.MergedRegions.Should().HaveCount(1);
        loadedSheet.MergedRegions[0].Start.Row.Should().Be(1);
        loadedSheet.MergedRegions[0].End.Row.Should().Be(2);
        loadedSheet.MergedRegions[0].End.Col.Should().Be(3);
    }

    [Fact]
    public void XlsxAdapter_Load_ReadsWorkbookThemePart()
    {
        var workbook = new Workbook("ThemeLoadTest");
        workbook.AddSheet("S1");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddUnknownPackagePart(source, "xl/theme/theme1.xml", TestThemeXml);

        source.Position = 0;
        var loaded = adapter.Load(source);

        loaded.Theme.Name.Should().Be("Freexcel Test Theme");
        loaded.Theme.MajorFontName.Should().Be("Major Test");
        loaded.Theme.MinorFontName.Should().Be("Minor Test");
        loaded.Theme.EffectsName.Should().Be("Effects Test");
        loaded.Theme.GetColor(WorkbookThemeColorSlot.Dark1).Should().Be(new CellColor(1, 2, 3));
        loaded.Theme.GetColor(WorkbookThemeColorSlot.Light1).Should().Be(new CellColor(250, 251, 252));
        loaded.Theme.GetColor(WorkbookThemeColorSlot.Accent1).Should().Be(new CellColor(12, 34, 56));
        loaded.Theme.GetColor(WorkbookThemeColorSlot.Hyperlink).Should().Be(new CellColor(5, 99, 193));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesWorkbookThemePart()
    {
        var workbook = new Workbook("ThemeSaveTest");
        workbook.AddSheet("S1");
        workbook.Theme = WorkbookTheme.Office
            .WithName("Saved Freexcel Theme")
            .WithFonts("Saved Major", "Saved Minor")
            .WithEffects("Saved Effects")
            .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(12, 34, 56));

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var themeEntry = archive.GetEntry("xl/theme/theme1.xml");
        themeEntry.Should().NotBeNull();
        using var reader = new StreamReader(themeEntry!.Open(), Encoding.UTF8);
        var themeXml = reader.ReadToEnd();

        themeXml.Should().Contain("Saved Freexcel Theme");
        themeXml.Should().Contain("Saved Major");
        themeXml.Should().Contain("Saved Minor");
        themeXml.Should().Contain("Saved Effects");
        themeXml.Should().Contain("0C2238");
    }

    [Fact]
    public void XlsxAdapter_Load_ResolvesThemeFontColorAgainstWorkbookTheme()
    {
        var source = new MemoryStream();
        using (var xlWorkbook = new XLWorkbook())
        {
            var sheet = xlWorkbook.Worksheets.Add("S1");
            sheet.Cell("A1").Value = "themed";
            sheet.Cell("A1").Style.Font.FontColor = XLColor.FromTheme(XLThemeColor.Accent1);
            xlWorkbook.SaveAs(source);
        }

        source.Position = 0;
        AddUnknownPackagePart(source, "xl/theme/theme1.xml", TestThemeXml);

        source.Position = 0;
        var loaded = new XlsxFileAdapter().Load(source);

        var cell = loaded.GetSheetAt(0).GetCell(1, 1);
        cell.Should().NotBeNull();
        loaded.GetStyle(cell!.StyleId).FontColor.Should().Be(new CellColor(12, 34, 56));
    }

    [Fact]
    public void XlsxAdapter_Load_ReadsEmbeddedColumnChartPackagePart()
    {
        var workbook = new Workbook("ChartPackageLoad");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalColumnChartPackage(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);

        var loadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Column);
        loadedChart.Title.Should().Be("Sales");
        loadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 4, 2)));
        loadedChart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2)));
        loadedChart.Left.Should().BeApproximately(3 * loadedSheet.DefaultColumnWidth * 8 + 10, 0.01);
        loadedChart.Top.Should().BeApproximately(loadedSheet.DefaultRowHeight + 20, 0.01);
        loadedChart.Width.Should().BeApproximately(5 * loadedSheet.DefaultColumnWidth * 8 - 5, 0.01);
        loadedChart.Height.Should().BeApproximately(15 * loadedSheet.DefaultRowHeight - 10, 0.01);
    }

    [Fact]
    public void XlsxAdapter_Load_ReadsEmbeddedColumnChartOneCellAnchorPackagePart()
    {
        var workbook = new Workbook("ChartPackageLoad");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalColumnChartPackage(source, useOneCellAnchor: true);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);

        var loadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Column);
        loadedChart.Left.Should().BeApproximately(4 * loadedSheet.DefaultColumnWidth * 8 + 10, 0.01);
        loadedChart.Top.Should().BeApproximately(2 * loadedSheet.DefaultRowHeight + 20, 0.01);
        loadedChart.Width.Should().BeApproximately(200, 0.01);
        loadedChart.Height.Should().BeApproximately(120, 0.01);
    }

    [Fact]
    public void XlsxAdapter_Load_ReadsEmbeddedColumnChartAbsoluteAnchorPackagePart()
    {
        var workbook = new Workbook("ChartPackageLoad");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalColumnChartPackage(source, useAbsoluteAnchor: true);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);

        var loadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Column);
        loadedChart.Left.Should().BeApproximately(100, 0.01);
        loadedChart.Top.Should().BeApproximately(80, 0.01);
        loadedChart.Width.Should().BeApproximately(220, 0.01);
        loadedChart.Height.Should().BeApproximately(140, 0.01);
    }

    // ── CSV ───────────────────────────────────────────────────────────────────

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedColumnChartPackagePart()
    {
        var workbook = new Workbook("ChartPackageSave");
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
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2)),
            Left = 100,
            Top = 80,
            Width = 220,
            Height = 140,
            SeriesFormats =
            [
                new ChartSeriesFormat(0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2))
            ]
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            archive.GetEntry("xl/drawings/drawing1.xml").Should().NotBeNull();
            archive.GetEntry("xl/drawings/_rels/drawing1.xml.rels").Should().NotBeNull();
            archive.GetEntry("xl/charts/chart1.xml").Should().NotBeNull();
            archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels").Should().NotBeNull();
        }

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Column);
        loadedChart.Title.Should().Be("Sales");
        loadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 4, 2)));
        loadedChart.Left.Should().BeApproximately(100, 0.01);
        loadedChart.Top.Should().BeApproximately(80, 0.01);
        loadedChart.Width.Should().BeApproximately(220, 0.01);
        loadedChart.Height.Should().BeApproximately(140, 0.01);
        loadedChart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2)));
    }

    [Theory]
    [InlineData(ChartType.StackedColumn)]
    [InlineData(ChartType.PercentStackedColumn)]
    public void XlsxAdapter_Save_WritesEmbeddedStackedColumnChartPackagePart(ChartType chartType)
    {
        var workbook = new Workbook("StackedChartPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Expenses"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(11));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(14));
        sheet.Charts.Add(new ChartModel
        {
            Type = chartType,
            Title = chartType.ToString(),
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3)),
            Left = 120,
            Top = 90,
            Width = 260,
            Height = 160,
            SeriesFormats =
            [
                new ChartSeriesFormat(0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2)),
                new ChartSeriesFormat(1, FillColor: new CellColor(12, 34, 56))
            ]
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            archive.GetEntry("xl/drawings/drawing1.xml").Should().NotBeNull();
            archive.GetEntry("xl/charts/chart1.xml").Should().NotBeNull();
        }

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(chartType);
        loadedChart.Title.Should().Be(chartType.ToString());
        loadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 4, 3)));
        loadedChart.Left.Should().BeApproximately(120, 0.01);
        loadedChart.Top.Should().BeApproximately(90, 0.01);
        loadedChart.Width.Should().BeApproximately(260, 0.01);
        loadedChart.Height.Should().BeApproximately(160, 0.01);
        loadedChart.SeriesFormats.Should().Contain(
            new ChartSeriesFormat(0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2)));
        loadedChart.SeriesFormats.Should().Contain(
            new ChartSeriesFormat(1, FillColor: new CellColor(12, 34, 56)));
    }

    [Theory]
    [InlineData(ChartType.Bar)]
    [InlineData(ChartType.StackedBar)]
    [InlineData(ChartType.PercentStackedBar)]
    public void XlsxAdapter_Save_WritesEmbeddedBarChartPackagePart(ChartType chartType)
    {
        var workbook = new Workbook("BarChartPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Expenses"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(11));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(14));
        sheet.Charts.Add(new ChartModel
        {
            Type = chartType,
            Title = chartType.ToString(),
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3)),
            Left = 130,
            Top = 95,
            Width = 280,
            Height = 170,
            SeriesFormats =
            [
                new ChartSeriesFormat(0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3)),
                new ChartSeriesFormat(1, FillColor: new CellColor(56, 34, 12))
            ]
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            archive.GetEntry("xl/drawings/drawing1.xml").Should().NotBeNull();
            archive.GetEntry("xl/charts/chart1.xml").Should().NotBeNull();
        }

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(chartType);
        loadedChart.Title.Should().Be(chartType.ToString());
        loadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 4, 3)));
        loadedChart.Left.Should().BeApproximately(130, 0.01);
        loadedChart.Top.Should().BeApproximately(95, 0.01);
        loadedChart.Width.Should().BeApproximately(280, 0.01);
        loadedChart.Height.Should().BeApproximately(170, 0.01);
        loadedChart.SeriesFormats.Should().Contain(
            new ChartSeriesFormat(0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3)));
        loadedChart.SeriesFormats.Should().Contain(
            new ChartSeriesFormat(1, FillColor: new CellColor(56, 34, 12)));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedLineChartPackagePart()
    {
        var workbook = new Workbook("LineChartPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Expenses"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(11));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(14));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            Title = "Line",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3)),
            Left = 140,
            Top = 100,
            Width = 300,
            Height = 180,
            SeriesFormats =
            [
                new ChartSeriesFormat(0, StrokeThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4)),
                new ChartSeriesFormat(1, StrokeColor: new CellColor(90, 80, 70))
            ]
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            archive.GetEntry("xl/drawings/drawing1.xml").Should().NotBeNull();
            archive.GetEntry("xl/charts/chart1.xml").Should().NotBeNull();
        }

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Line);
        loadedChart.Title.Should().Be("Line");
        loadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 4, 3)));
        loadedChart.Left.Should().BeApproximately(140, 0.01);
        loadedChart.Top.Should().BeApproximately(100, 0.01);
        loadedChart.Width.Should().BeApproximately(300, 0.01);
        loadedChart.Height.Should().BeApproximately(180, 0.01);
        loadedChart.SeriesFormats.Should().Contain(
            new ChartSeriesFormat(0, StrokeThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4)));
        loadedChart.SeriesFormats.Should().Contain(
            new ChartSeriesFormat(1, StrokeColor: new CellColor(90, 80, 70)));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedScatterChartPackagePart()
    {
        var workbook = new Workbook("ScatterChartPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Dose"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Response A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Response B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(18));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(31));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(29));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Scatter,
            Title = "Dose Response",
            FirstColIsCategories = false,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3)),
            Left = 150,
            Top = 110,
            Width = 320,
            Height = 190,
            SeriesFormats =
            [
                new ChartSeriesFormat(0, StrokeThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent5)),
                new ChartSeriesFormat(1, StrokeColor: new CellColor(20, 110, 180))
            ]
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            archive.GetEntry("xl/drawings/drawing1.xml").Should().NotBeNull();
            archive.GetEntry("xl/charts/chart1.xml").Should().NotBeNull();
        }

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Scatter);
        loadedChart.Title.Should().Be("Dose Response");
        loadedChart.FirstColIsCategories.Should().BeFalse();
        loadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 4, 3)));
        loadedChart.Left.Should().BeApproximately(150, 0.01);
        loadedChart.Top.Should().BeApproximately(110, 0.01);
        loadedChart.Width.Should().BeApproximately(320, 0.01);
        loadedChart.Height.Should().BeApproximately(190, 0.01);
        loadedChart.SeriesFormats.Should().Contain(
            new ChartSeriesFormat(0, StrokeThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent5)));
        loadedChart.SeriesFormats.Should().Contain(
            new ChartSeriesFormat(1, StrokeColor: new CellColor(20, 110, 180)));
    }

    [Fact]
    public void XlsxAdapter_SaveFromModel_DoesNotPreserveUnknownPackageParts()
    {
        var workbook = new Workbook("UnknownPartTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kept"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddUnknownPackagePart(source, "customXml/item1.xml", "<freexcel-test />");

        source.Position = 0;
        var loaded = adapter.Load(source);
        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("customXml/item1.xml").Should().BeNull(
            "v1 XLSX save writes from the Freexcel model and does not preserve unsupported OOXML package parts");
    }

    private static void AddUnknownPackagePart(MemoryStream packageStream, string entryName, string content)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            archive.GetEntry(entryName)?.Delete();
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalColumnChartPackage(
        MemoryStream packageStream,
        bool useOneCellAnchor = false,
        bool useAbsoluteAnchor = false)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
            XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(contentTypesXml, contentTypeNs, "/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml");
            AddContentTypeOverride(contentTypesXml, contentTypeNs, "/xl/charts/chart1.xml", "application/vnd.openxmlformats-officedocument.drawingml.chart+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            var worksheetXml = LoadPackageXml(worksheetEntry);
            worksheetXml.Root!.Elements(worksheetNs + "drawing").Remove();
            worksheetXml.Root!.Add(new XElement(worksheetNs + "drawing", new XAttribute(relNs + "id", "rIdFreexcelChartDrawing")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadPackageXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreexcelChartDrawing"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
                new XAttribute("Target", "../drawings/drawing1.xml")));
            ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            var anchorXml = useAbsoluteAnchor
                ? new XElement(spreadsheetDrawingNs + "absoluteAnchor",
                    new XElement(spreadsheetDrawingNs + "pos",
                        new XAttribute("x", "952500"),
                        new XAttribute("y", "762000")),
                    new XElement(spreadsheetDrawingNs + "ext",
                        new XAttribute("cx", "2095500"),
                        new XAttribute("cy", "1333500")),
                    CreateMinimalChartGraphicFrame(spreadsheetDrawingNs, drawingNs, chartNs, relNs),
                    new XElement(spreadsheetDrawingNs + "clientData"))
                : useOneCellAnchor
                ? new XElement(spreadsheetDrawingNs + "oneCellAnchor",
                    new XElement(spreadsheetDrawingNs + "from",
                        new XElement(spreadsheetDrawingNs + "col", "4"),
                        new XElement(spreadsheetDrawingNs + "colOff", "95250"),
                        new XElement(spreadsheetDrawingNs + "row", "2"),
                        new XElement(spreadsheetDrawingNs + "rowOff", "190500")),
                    new XElement(spreadsheetDrawingNs + "ext",
                        new XAttribute("cx", "1905000"),
                        new XAttribute("cy", "1143000")),
                    CreateMinimalChartGraphicFrame(spreadsheetDrawingNs, drawingNs, chartNs, relNs),
                    new XElement(spreadsheetDrawingNs + "clientData"))
                : new XElement(spreadsheetDrawingNs + "twoCellAnchor",
                    new XElement(spreadsheetDrawingNs + "from",
                        new XElement(spreadsheetDrawingNs + "col", "3"),
                        new XElement(spreadsheetDrawingNs + "colOff", "95250"),
                        new XElement(spreadsheetDrawingNs + "row", "1"),
                        new XElement(spreadsheetDrawingNs + "rowOff", "190500")),
                    new XElement(spreadsheetDrawingNs + "to",
                        new XElement(spreadsheetDrawingNs + "col", "8"),
                        new XElement(spreadsheetDrawingNs + "colOff", "47625"),
                        new XElement(spreadsheetDrawingNs + "row", "16"),
                        new XElement(spreadsheetDrawingNs + "rowOff", "95250")),
                    CreateMinimalChartGraphicFrame(spreadsheetDrawingNs, drawingNs, chartNs, relNs),
                    new XElement(spreadsheetDrawingNs + "clientData"));

            var drawingXml = new XDocument(
                new XElement(spreadsheetDrawingNs + "wsDr",
                    new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                    new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                    new XAttribute(XNamespace.Xmlns + "c", chartNs),
                    new XAttribute(XNamespace.Xmlns + "r", relNs),
                    anchorXml));
            ReplacePackageXml(archive, "xl/drawings/drawing1.xml", drawingXml);

            var drawingRelsXml = new XDocument(
                new XElement(packageRelNs + "Relationships",
                    new XElement(packageRelNs + "Relationship",
                        new XAttribute("Id", "rIdFreexcelChart"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chart"),
                        new XAttribute("Target", "../charts/chart1.xml"))));
            ReplacePackageXml(archive, "xl/drawings/_rels/drawing1.xml.rels", drawingRelsXml);
            ReplacePackageXml(archive, "xl/charts/chart1.xml", XDocument.Parse(MinimalColumnChartXml));
        }

        packageStream.Position = 0;
    }

    private static XElement CreateMinimalChartGraphicFrame(
        XNamespace spreadsheetDrawingNs,
        XNamespace drawingNs,
        XNamespace chartNs,
        XNamespace relNs) =>
        new(spreadsheetDrawingNs + "graphicFrame",
            new XElement(spreadsheetDrawingNs + "nvGraphicFramePr",
                new XElement(spreadsheetDrawingNs + "cNvPr",
                    new XAttribute("id", "2"),
                    new XAttribute("name", "Chart 1")),
                new XElement(spreadsheetDrawingNs + "cNvGraphicFramePr")),
            new XElement(spreadsheetDrawingNs + "xfrm"),
            new XElement(drawingNs + "graphic",
                new XElement(drawingNs + "graphicData",
                    new XAttribute("uri", "http://schemas.openxmlformats.org/drawingml/2006/chart"),
                    new XElement(chartNs + "chart", new XAttribute(relNs + "id", "rIdFreexcelChart")))));

    private static XDocument LoadPackageXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static void ReplacePackageXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        document.Save(stream);
    }

    private static void AddContentTypeOverride(XDocument contentTypesXml, XNamespace contentTypeNs, string partName, string contentType)
    {
        contentTypesXml.Root!
            .Elements(contentTypeNs + "Override")
            .Where(element => string.Equals(element.Attribute("PartName")?.Value, partName, StringComparison.OrdinalIgnoreCase))
            .Remove();
        contentTypesXml.Root!.Add(new XElement(
            contentTypeNs + "Override",
            new XAttribute("PartName", partName),
            new XAttribute("ContentType", contentType)));
    }

    private const string TestThemeXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Freexcel Test Theme">
          <a:themeElements>
            <a:clrScheme name="Freexcel Colors">
              <a:dk1><a:srgbClr val="010203"/></a:dk1>
              <a:lt1><a:srgbClr val="FAFBFC"/></a:lt1>
              <a:dk2><a:srgbClr val="44546A"/></a:dk2>
              <a:lt2><a:srgbClr val="E7E6E6"/></a:lt2>
              <a:accent1><a:srgbClr val="0C2238"/></a:accent1>
              <a:accent2><a:srgbClr val="E97132"/></a:accent2>
              <a:accent3><a:srgbClr val="196B24"/></a:accent3>
              <a:accent4><a:srgbClr val="0F9ED5"/></a:accent4>
              <a:accent5><a:srgbClr val="A02B93"/></a:accent5>
              <a:accent6><a:srgbClr val="4EA72E"/></a:accent6>
              <a:hlink><a:srgbClr val="0563C1"/></a:hlink>
              <a:folHlink><a:srgbClr val="954F72"/></a:folHlink>
            </a:clrScheme>
            <a:fontScheme name="Freexcel Fonts">
              <a:majorFont><a:latin typeface="Major Test"/></a:majorFont>
              <a:minorFont><a:latin typeface="Minor Test"/></a:minorFont>
            </a:fontScheme>
            <a:fmtScheme name="Effects Test"/>
          </a:themeElements>
        </a:theme>
        """;

    private const string MinimalColumnChartXml = """
        <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <c:chart>
            <c:title><c:tx><c:rich><a:p><a:r><a:t>Sales</a:t></a:r></a:p></c:rich></c:tx></c:title>
            <c:plotArea>
              <c:barChart>
                <c:barDir val="col"/>
                <c:ser>
                  <c:tx><c:strRef><c:f>Sheet1!$B$1</c:f></c:strRef></c:tx>
                  <c:spPr><a:solidFill><a:schemeClr val="accent2"/></a:solidFill></c:spPr>
                  <c:cat><c:strRef><c:f>Sheet1!$A$2:$A$4</c:f></c:strRef></c:cat>
                  <c:val><c:numRef><c:f>Sheet1!$B$2:$B$4</c:f></c:numRef></c:val>
                </c:ser>
              </c:barChart>
            </c:plotArea>
          </c:chart>
        </c:chartSpace>
        """;

    [Fact]
    public void CsvAdapter_RoundTrip()
    {
        var workbook = new Workbook("CsvTest");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("world"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(9));

        var ms = new MemoryStream();
        var adapter = new CsvFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        var ls = loaded.GetSheetAt(0);
        ((NumberValue)ls.GetValue(1, 1)).Value.Should().Be(1);
        ((NumberValue)ls.GetValue(1, 2)).Value.Should().Be(2);
        ((TextValue)ls.GetValue(1, 3)).Value.Should().Be("hello");
        ((NumberValue)ls.GetValue(3, 3)).Value.Should().Be(9);
    }

    [Fact]
    public void CsvAdapter_Load_AutoDetectsNumbers()
    {
        const string csv = "1,2,hello\r\n";
        var ms = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var adapter = new CsvFileAdapter();
        var loaded = adapter.Load(ms);

        var sheet = loaded.GetSheetAt(0);
        sheet.GetValue(1, 1).Should().BeOfType<NumberValue>().Which.Value.Should().Be(1);
        sheet.GetValue(1, 2).Should().BeOfType<NumberValue>().Which.Value.Should().Be(2);
        sheet.GetValue(1, 3).Should().BeOfType<TextValue>().Which.Value.Should().Be("hello");
    }

    [Fact]
    public void CsvLoad_MultilineQuotedField_IsReadAsOneCell()
    {
        var csv = "\"line1\nline2\",second\r\nrow2a,row2b\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var adapter  = new CsvFileAdapter();
        var workbook = adapter.Load(stream);
        var sheet    = workbook.Sheets[0];

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1))
            .Should().Be(new TextValue("line1\nline2"),
                "RFC 4180 allows newlines inside quoted fields");
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2))
            .Should().Be(new TextValue("second"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1))
            .Should().Be(new TextValue("row2a"));
    }

    [Fact]
    public void CsvRoundTrip_MultilineField_PreservesContent()
    {
        var wb    = new Workbook("T");
        var sheet = wb.AddSheet("S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("a\nb"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("c"));

        var adapter = new CsvFileAdapter();
        using var ms = new MemoryStream();
        adapter.Save(wb, ms);
        ms.Position = 0;
        var wb2    = adapter.Load(ms);
        var sheet2 = wb2.Sheets[0];
        sheet2.GetValue(new CellAddress(sheet2.Id, 1, 1)).Should().Be(new TextValue("a\nb"));
        sheet2.GetValue(new CellAddress(sheet2.Id, 1, 2)).Should().Be(new TextValue("c"));
    }
}
