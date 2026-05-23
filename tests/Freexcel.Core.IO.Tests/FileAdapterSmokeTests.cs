using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Globalization;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using Freexcel.Core.IO;
using Freexcel.Core.Model;

namespace Freexcel.Core.IO.Tests;

public partial class FileAdapterSmokeTests
{
    private const string NumberStoredAsTextCode = "NumberStoredAsText";
    private const string FormulaRefersToBlankCellsCode = "FormulaRefersToBlankCells";

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
    public void NativeJsonAdapter_RoundTrip_HeaderFooterPictures()
    {
        var workbook = new Workbook("HeaderPicture");
        var sheet = workbook.AddSheet("Sheet1");
        var picture = new WorksheetHeaderFooterPicture([1, 2, 3, 4], "image/png", "logo.png", 120, 48);
        sheet.PageHeader = new WorksheetHeaderFooter("&[Picture]", "", "");
        sheet.PageHeaderPictures = new WorksheetHeaderFooterPictureSet(picture, null, null);

        var adapter = new NativeJsonAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var loaded = adapter.Load(stream).GetSheetAt(0);

        loaded.PageHeader.Left.Should().Be("&[Picture]");
        loaded.PageHeaderPictures.Left.Should().NotBeNull();
        loaded.PageHeaderPictures.Left!.ImageBytes.Should().Equal([1, 2, 3, 4]);
        loaded.PageHeaderPictures.Left.ContentType.Should().Be("image/png");
        loaded.PageHeaderPictures.Left.FileName.Should().Be("logo.png");
        loaded.PageHeaderPictures.Left.Width.Should().Be(120);
        loaded.PageHeaderPictures.Left.Height.Should().Be(48);
    }

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
        workbook.DisabledFormulaErrorCodes.Add(NumberStoredAsTextCode);
        workbook.DisabledFormulaErrorCodes.Add(FormulaRefersToBlankCellsCode);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.DisabledFormulaErrorCodes.Should().BeEquivalentTo(
            ErrorValue.DivByZero.Code,
            NumberStoredAsTextCode,
            FormulaRefersToBlankCellsCode);
    }

    [Fact]
    public void NativeJsonAdapter_Load_SkipsUnsupportedErrorCheckingOptions()
    {
        const string json = """
        {
          "Name": "ErrorCheckingOptions",
          "DisabledFormulaErrorCodes": [ "#DIV/0!", "NumberStoredAsText", "FormulaRefersToBlankCells", "#NOT-AN-EXCEL-RULE!" ],
          "Sheets": [ { "Name": "Sheet1" } ]
        }
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var loaded = new NativeJsonAdapter().Load(ms);

        loaded.DisabledFormulaErrorCodes.Should().BeEquivalentTo(
            ErrorValue.DivByZero.Code,
            NumberStoredAsTextCode,
            FormulaRefersToBlankCellsCode);
    }

    [Fact]
    public void NativeJsonAdapter_Save_SkipsUnsupportedErrorCheckingOptions()
    {
        var workbook = new Workbook("ErrorCheckingSaveSanitize");
        workbook.AddSheet("Sheet1");
        workbook.DisabledFormulaErrorCodes.Add(ErrorValue.Ref.Code);
        workbook.DisabledFormulaErrorCodes.Add(NumberStoredAsTextCode);
        workbook.DisabledFormulaErrorCodes.Add(FormulaRefersToBlankCellsCode);
        workbook.DisabledFormulaErrorCodes.Add("#NOT-AN-EXCEL-RULE!");

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using var document = JsonDocument.Parse(ms);
        var codes = document.RootElement.GetProperty("DisabledFormulaErrorCodes")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToList();
        codes.Should().BeEquivalentTo(
            ErrorValue.Ref.Code,
            NumberStoredAsTextCode,
            FormulaRefersToBlankCellsCode);
    }

    [Fact]
    public void XlsxFileAdapter_Load_InvalidDateSerialFallsBackToNumber()
    {
        using var xl = new XLWorkbook();
        var ws = xl.Worksheets.Add("Sheet1");
        ws.Cell("A1").Value = 9_999_999d;
        ws.Cell("A1").Style.DateFormat.Format = "m/d/yyyy";

        using var stream = new MemoryStream();
        xl.SaveAs(stream);
        stream.Position = 0;

        var loaded = new XlsxFileAdapter().Load(stream);

        loaded.GetSheetAt(0).GetValue(1, 1)
            .Should().Be(new NumberValue(9_999_999d));
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
            new ChartSeriesFormat(0, FillColor: new CellColor(0, 114, 178), StrokeThickness: 0.5));
        chart.PointDataLabelFormats.Should().ContainSingle().Which.Should().Be(
            new ChartPointDataLabelFormat(0, 0, FillColor: new CellColor(112, 48, 160), BorderThickness: 10, FontSize: 6));
    }

    [Fact]
    public void NativeJsonAdapter_Load_ClearsSecondaryAxisWhenNoSeriesTargetsRemain()
    {
        var workbook = new Workbook("ChartSecondaryAxisSanitizeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [-1, 0, 2]
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.ShowSecondaryAxis.Should().BeFalse();
        chart.SecondaryAxisSeriesIndexes.Should().BeEmpty();
    }

    [Fact]
    public void NativeJsonAdapter_Load_ClearsUnsupportedSecondaryAxisState()
    {
        var workbook = new Workbook("ChartSecondaryAxisSanitizeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.StackedColumn,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1]
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.ShowSecondaryAxis.Should().BeFalse();
        chart.SecondaryAxisSeriesIndexes.Should().BeEmpty();
    }

    [Fact]
    public void NativeJsonAdapter_Load_ClearsUnsupportedTrendlineState()
    {
        var workbook = new Workbook("ChartTrendlineSanitizeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            ShowLinearTrendline = true,
            TrendlineType = ChartTrendlineType.Polynomial,
            TrendlinePeriod = 5,
            TrendlineOrder = 4,
            ShowTrendlineEquation = true,
            ShowTrendlineRSquared = true,
            TrendlineColor = new CellColor(217, 83, 25),
            TrendlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
            TrendlineThickness = 2.5,
            TrendlineDashStyle = ChartLineDashStyle.Dot
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.ShowLinearTrendline.Should().BeFalse();
        chart.TrendlineType.Should().Be(ChartTrendlineType.Linear);
        chart.TrendlinePeriod.Should().Be(2);
        chart.TrendlineOrder.Should().Be(2);
        chart.ShowTrendlineEquation.Should().BeFalse();
        chart.ShowTrendlineRSquared.Should().BeFalse();
        chart.TrendlineColor.Should().BeNull();
        chart.TrendlineThemeColor.Should().BeNull();
        chart.TrendlineThickness.Should().Be(1.5);
        chart.TrendlineDashStyle.Should().Be(ChartLineDashStyle.Dash);
    }

    [Fact]
    public void NativeJsonAdapter_Load_ClearsUnsupportedPercentageDataLabelState()
    {
        var workbook = new Workbook("ChartDataLabelSanitizeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            ShowDataLabels = true,
            ShowDataLabelCategoryName = true,
            ShowDataLabelPercentage = true
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.ShowDataLabels.Should().BeTrue();
        chart.ShowDataLabelCategoryName.Should().BeTrue();
        chart.ShowDataLabelPercentage.Should().BeFalse();
    }

    [Fact]
    public void NativeJsonAdapter_Load_ClearsAxisTitlesWhenChartHasNoAxes()
    {
        var workbook = new Workbook("ChartAxisTitleSanitizeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            XAxisTitle = "Quarter",
            YAxisTitle = "Amount",
            AxisTitleTextColor = new CellColor(89, 89, 89),
            AxisTitleFontSize = 18
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.XAxisTitle.Should().BeNull();
        chart.YAxisTitle.Should().BeNull();
        chart.AxisTitleTextColor.Should().BeNull();
        chart.AxisTitleFontSize.Should().Be(12);
    }

    [Fact]
    public void NativeJsonAdapter_Load_ClearsUnsupportedAxisBoundsAndStyles()
    {
        var workbook = new Workbook("ChartAxisSanitizeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
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
            XAxisMajorTickStyle = ChartAxisTickStyle.Cross,
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
            YAxisMinorTickStyle = ChartAxisTickStyle.Inside,
            ShowYAxisLabels = false,
            YAxisLabelTextColor = new CellColor(80, 80, 80),
            YAxisLabelFontSize = 12,
            YAxisLabelAngle = 90,
            YAxisLineColor = new CellColor(40, 50, 60),
            YAxisLineThickness = 3.5
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
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
        chart.YAxisMinimum.Should().BeNull();
        chart.YAxisMaximum.Should().BeNull();
        chart.YAxisMajorUnit.Should().BeNull();
        chart.YAxisMinorUnit.Should().BeNull();
        chart.YAxisLogScale.Should().BeFalse();
        chart.YAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        chart.ShowYAxisMajorGridlines.Should().BeFalse();
        chart.ShowYAxisMinorGridlines.Should().BeFalse();
        chart.YAxisMajorGridlineColor.Should().BeNull();
        chart.YAxisMinorGridlineColor.Should().BeNull();
        chart.YAxisGridlineThickness.Should().Be(1);
        chart.YAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        chart.YAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        chart.ShowYAxisLabels.Should().BeTrue();
        chart.YAxisLabelTextColor.Should().BeNull();
        chart.YAxisLabelFontSize.Should().Be(11);
        chart.YAxisLabelAngle.Should().Be(0);
        chart.YAxisLineColor.Should().BeNull();
        chart.YAxisLineThickness.Should().Be(1);
    }

    [Fact]
    public void NativeJsonAdapter_Load_ClearsPieAndDoughnutStateWhenUnsupported()
    {
        var workbook = new Workbook("ChartPieStateSanitizeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            DoughnutHoleSize = 0.72,
            FirstSliceAngle = 135,
            ExplodedSliceIndex = 1,
            ExplodedSliceDistance = 0.18
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.DoughnutHoleSize.Should().Be(0.55);
        chart.FirstSliceAngle.Should().Be(0);
        chart.ExplodedSliceIndex.Should().Be(-1);
        chart.ExplodedSliceDistance.Should().Be(0.1);
    }

    [Fact]
    public void NativeJsonAdapter_Load_ClearsSeriesMarkerFormattingWhenUnsupported()
    {
        var workbook = new Workbook("ChartMarkerSanitizeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    FillColor: new CellColor(68, 114, 196),
                    StrokeColor: new CellColor(47, 82, 143),
                    MarkerStyle: ChartMarkerStyle.Diamond,
                    MarkerSize: 8)
            ]
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.SeriesFormats.Should().Equal(
            new ChartSeriesFormat(
                0,
                FillColor: new CellColor(68, 114, 196),
                StrokeColor: new CellColor(47, 82, 143)));
    }

    [Fact]
    public void NativeJsonAdapter_Load_DropsEmptySeriesFormats()
    {
        var workbook = new Workbook("ChartSeriesFormatSanitizeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    MarkerStyle: ChartMarkerStyle.Diamond,
                    MarkerSize: 8)
            ]
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.SeriesFormats.Should().BeEmpty();
    }

    [Fact]
    public void NativeJsonAdapter_Load_DropsEmptyPointDataLabelFormats()
    {
        var workbook = new Workbook("ChartPointLabelFormatSanitizeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            PointDataLabelFormats =
            [
                new ChartPointDataLabelFormat(0, 0)
            ]
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var chart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        chart.PointDataLabelFormats.Should().BeEmpty();
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
    public void NativeJsonAdapter_Load_ClearsComboLineOverlayWhenNoSeriesTargetsRemain()
    {
        var workbook = new Workbook("ChartComboSanitizeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 3)),
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [-1, 0, 2]
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
        chart.XAxisLabelAngle.Should().Be(0);
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
        chart.DoughnutHoleSize.Should().Be(0.55);
        chart.FirstSliceAngle.Should().Be(0);
        chart.ExplodedSliceDistance.Should().Be(0.1);
        chart.XAxisMajorUnit.Should().BeNull();
        chart.XAxisMinorUnit.Should().BeNull();
        chart.XAxisGridlineThickness.Should().Be(1);
        chart.XAxisLabelFontSize.Should().Be(11);
        chart.XAxisLineThickness.Should().Be(1);
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
        chart.SeriesFormats.Should().BeEmpty();
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
    public void NativeJsonAdapter_Save_SanitizesInvalidWorkbookWindowArrangement()
    {
        var workbook = new Workbook("WindowArrangementSanitizeTest");
        workbook.AddSheet("Sheet1");
        workbook.WindowArrangement = (WorkbookWindowArrangement)99;

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using var document = JsonDocument.Parse(ms);
        document.RootElement.GetProperty("WindowArrangement").GetInt32()
            .Should().Be((int)WorkbookWindowArrangement.Tiled);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_CalculationMode()
    {
        var workbook = new Workbook("CalculationNativeTest");
        workbook.AddSheet("Sheet1");
        workbook.CalculationMode = WorkbookCalculationMode.Manual;
        workbook.FullCalculationOnLoad = true;
        workbook.ForceFullCalculation = true;
        workbook.IterativeCalculation = true;
        workbook.MaxCalculationIterations = 50;
        workbook.MaxCalculationChange = 0.01;

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.CalculationMode.Should().Be(WorkbookCalculationMode.Manual);
        loaded.FullCalculationOnLoad.Should().BeTrue();
        loaded.ForceFullCalculation.Should().BeTrue();
        loaded.IterativeCalculation.Should().BeTrue();
        loaded.MaxCalculationIterations.Should().Be(50);
        loaded.MaxCalculationChange.Should().Be(0.01);
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
                ShowFormulas: true)],
            "{11111111-1111-1111-1111-111111111111}"));

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var view = loaded.CustomViews.Should().ContainSingle().Subject;
        view.Name.Should().Be("Review");
        view.Id.Should().Be("{11111111-1111-1111-1111-111111111111}");
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
    public void NativeJsonAdapter_RoundTrip_WorksheetCustomProperties()
    {
        var workbook = new Workbook("WorksheetCustomProperties");
        var sheet = workbook.AddSheet("Data");
        sheet.CustomProperties.Add(new WorksheetCustomProperty("FreexcelModeledProperty", 7));

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).CustomProperties.Should()
            .ContainSingle()
            .Which.Should().Be(new WorksheetCustomProperty("FreexcelModeledProperty", 7));
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorksheetCalculationProperties()
    {
        var workbook = new Workbook("WorksheetCalculationProperties");
        workbook.AddSheet("Data").FullCalculationOnLoad = true;

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).FullCalculationOnLoad.Should().BeTrue();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_WorksheetPhoneticProperties()
    {
        var workbook = new Workbook("WorksheetPhoneticProperties");
        workbook.AddSheet("Data").PhoneticProperties = new WorksheetPhoneticProperties(
            "1",
            "fullwidthKatakana",
            "center");

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).PhoneticProperties.Should().Be(new WorksheetPhoneticProperties(
            "1",
            "fullwidthKatakana",
            "center"));
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
    public void NativeJsonAdapter_Save_SanitizesInvalidCustomViewPaneState()
    {
        var workbook = new Workbook("CustomViewSaveSanitizeTest");
        workbook.AddSheet("Sheet1");
        workbook.CustomViews.Add(new WorkbookCustomView(
            "Bad panes",
            [new WorksheetCustomViewState(
                "Sheet1",
                (WorksheetViewMode)99,
                CellAddress.MaxRow + 1,
                CellAddress.MaxCol + 1,
                0,
                CellAddress.MaxCol + 1,
                ZoomPercent: 5)]));

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using var document = JsonDocument.Parse(ms);
        var state = document.RootElement.GetProperty("CustomViews")[0].GetProperty("Sheets")[0];
        state.GetProperty("ViewMode").GetInt32().Should().Be((int)WorksheetViewMode.Normal);
        state.GetProperty("FrozenRows").GetUInt32().Should().Be(0);
        state.GetProperty("FrozenCols").GetUInt32().Should().Be(0);
        state.GetProperty("SplitRow").ValueKind.Should().Be(JsonValueKind.Null);
        state.GetProperty("SplitColumn").ValueKind.Should().Be(JsonValueKind.Null);
        state.GetProperty("ZoomPercent").GetInt32().Should().Be(100);
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
    public void NativeJsonAdapter_Save_SanitizesInvalidViewAndPageSetupValues()
    {
        var workbook = new Workbook("PageSetupSaveSanitizeTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ViewMode = (WorksheetViewMode)99;
        sheet.ZoomPercent = 5;
        sheet.FrozenRows = CellAddress.MaxRow + 1;
        sheet.FrozenCols = CellAddress.MaxCol + 1;
        sheet.SplitRow = 0;
        sheet.SplitColumn = CellAddress.MaxCol + 1;
        sheet.PageOrientation = (WorksheetPageOrientation)99;
        sheet.PaperSize = (WorksheetPaperSize)99;
        sheet.PageMargins = new WorksheetPageMargins(-1, double.NaN, 0.5, 0.5);
        sheet.HeaderMargin = -0.35;
        sheet.FooterMargin = double.PositiveInfinity;
        sheet.ScaleToFit = new WorksheetScaleToFit(5, 0, -1);
        sheet.PrintTitleRows = new WorksheetRepeatRange(0, 2);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(0, 1);
        sheet.PageOrder = (WorksheetPageOrder)99;
        sheet.FirstPageNumber = 0;
        sheet.PrintQualityDpi = 0;
        sheet.PrintErrorValue = (WorksheetPrintErrorValue)99;
        sheet.PrintComments = (WorksheetPrintComments)99;
        sheet.RowPageBreaks.Add(1);
        sheet.RowPageBreaks.Add(CellAddress.MaxRow + 1);
        sheet.ColumnPageBreaks.Add(1);
        sheet.ColumnPageBreaks.Add(CellAddress.MaxCol + 1);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using var document = JsonDocument.Parse(ms);
        var sheetJson = document.RootElement.GetProperty("Sheets")[0];
        sheetJson.GetProperty("ViewMode").GetInt32().Should().Be((int)WorksheetViewMode.Normal);
        sheetJson.GetProperty("ZoomPercent").GetInt32().Should().Be(100);
        sheetJson.GetProperty("FrozenRows").GetUInt32().Should().Be(0);
        sheetJson.GetProperty("FrozenCols").GetUInt32().Should().Be(0);
        sheetJson.GetProperty("SplitRow").ValueKind.Should().Be(JsonValueKind.Null);
        sheetJson.GetProperty("SplitColumn").ValueKind.Should().Be(JsonValueKind.Null);
        sheetJson.GetProperty("PageOrientation").GetInt32().Should().Be((int)WorksheetPageOrientation.Portrait);
        sheetJson.GetProperty("PaperSize").GetInt32().Should().Be((int)WorksheetPaperSize.A4);
        var margins = sheetJson.GetProperty("PageMargins");
        margins.GetProperty("Left").GetDouble().Should().Be(WorksheetPageMargins.Narrow.Left);
        margins.GetProperty("Right").GetDouble().Should().Be(WorksheetPageMargins.Narrow.Right);
        sheetJson.GetProperty("HeaderMargin").GetDouble().Should().Be(0.3);
        sheetJson.GetProperty("FooterMargin").GetDouble().Should().Be(0.3);
        sheetJson.GetProperty("ScaleToFit").GetProperty("ScalePercent").GetInt32().Should().Be(100);
        sheetJson.GetProperty("PrintTitleRows").ValueKind.Should().Be(JsonValueKind.Null);
        sheetJson.GetProperty("PrintTitleColumns").ValueKind.Should().Be(JsonValueKind.Null);
        sheetJson.GetProperty("PageOrder").GetInt32().Should().Be((int)WorksheetPageOrder.DownThenOver);
        sheetJson.GetProperty("FirstPageNumber").ValueKind.Should().Be(JsonValueKind.Null);
        sheetJson.GetProperty("PrintQualityDpi").ValueKind.Should().Be(JsonValueKind.Null);
        sheetJson.GetProperty("PrintErrorValue").GetInt32().Should().Be((int)WorksheetPrintErrorValue.Displayed);
        sheetJson.GetProperty("PrintComments").GetInt32().Should().Be((int)WorksheetPrintComments.None);
        sheetJson.GetProperty("RowPageBreaks").GetArrayLength().Should().Be(0);
        sheetJson.GetProperty("ColumnPageBreaks").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void XlsxAdapter_Save_SanitizesInvalidPageSetupValues()
    {
        var workbook = new Workbook("XlsxPageSetupSanitizeTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.PageOrientation = (WorksheetPageOrientation)99;
        sheet.PaperSize = (WorksheetPaperSize)99;
        sheet.PageMargins = new WorksheetPageMargins(-1, double.NaN, 0.5, 0.5);
        sheet.HeaderMargin = -0.35;
        sheet.FooterMargin = double.PositiveInfinity;
        sheet.ScaleToFit = new WorksheetScaleToFit(5, 0, -1);
        sheet.PrintTitleRows = new WorksheetRepeatRange(0, 2);
        sheet.PrintTitleColumns = new WorksheetRepeatRange(0, 1);
        sheet.PageOrder = (WorksheetPageOrder)99;
        sheet.FirstPageNumber = 0;
        sheet.PrintQualityDpi = 0;
        sheet.PrintErrorValue = (WorksheetPrintErrorValue)99;
        sheet.PrintComments = (WorksheetPrintComments)99;
        sheet.RowPageBreaks.Add(0);
        sheet.RowPageBreaks.Add(CellAddress.MaxRow + 1);
        sheet.ColumnPageBreaks.Add(0);
        sheet.ColumnPageBreaks.Add(CellAddress.MaxCol + 1);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.PageOrientation.Should().Be(WorksheetPageOrientation.Portrait);
        loadedSheet.PaperSize.Should().Be(WorksheetPaperSize.A4);
        loadedSheet.PageMargins.Should().Be(WorksheetPageMargins.Narrow);
        loadedSheet.HeaderMargin.Should().Be(0.3);
        loadedSheet.FooterMargin.Should().Be(0.3);
        loadedSheet.ScaleToFit.Should().Be(WorksheetScaleToFit.Default);
        loadedSheet.PrintTitleRows.Should().BeNull();
        loadedSheet.PrintTitleColumns.Should().BeNull();
        loadedSheet.PageOrder.Should().Be(WorksheetPageOrder.DownThenOver);
        loadedSheet.FirstPageNumber.Should().BeNull();
        loadedSheet.PrintQualityDpi.Should().BeNull();
        loadedSheet.PrintErrorValue.Should().Be(WorksheetPrintErrorValue.Displayed);
        loadedSheet.PrintComments.Should().Be(WorksheetPrintComments.None);
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
            IsLinkedToSourceRange = true,
            LinkedSourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            LinkedSourceSheetName = "Sheet1",
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
        picture.IsLinkedToSourceRange.Should().BeTrue();
        picture.LinkedSourceRange.Should().Be(new GridRange(new CellAddress(loaded.GetSheetAt(0).Id, 1, 1), new CellAddress(loaded.GetSheetAt(0).Id, 2, 2)));
        picture.LinkedSourceSheetName.Should().Be("Sheet1");
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
            CropLeft = 0.1,
            CropTop = 0.2,
            CropRight = 0.15,
            CropBottom = 0.05,
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
        picture.CropLeft.Should().Be(0.1);
        picture.CropTop.Should().Be(0.2);
        picture.CropRight.Should().Be(0.15);
        picture.CropBottom.Should().Be(0.05);
        picture.AltText.Should().Be("Product photo");
    }

    [Fact]
    public void XlsxAdapter_LoadsPictureMetadataAndBytes()
    {
        var workbook = new Workbook("PictureXlsxLoadTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Has picture"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        var imageBytes = MinimalPngBytes();
        AddMinimalPicturePackage(source, imageBytes);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var picture = loaded.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;
        picture.Kind.Should().Be(PictureKind.Image);
        picture.Anchor.Should().Be(new CellAddress(loaded.GetSheetAt(0).Id, 2, 3));
        picture.ImageBytes.Should().Equal(imageBytes);
        picture.ContentType.Should().Be("image/png");
        picture.Width.Should().Be(120);
        picture.Height.Should().Be(80);
        picture.AltText.Should().Be("Native picture");
    }

    [Fact]
    public void XlsxAdapter_LoadsPictureCropFromSourceRectangle()
    {
        var workbook = new Workbook("PictureXlsxCropLoadTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Has picture"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalPicturePackage(
            source,
            MinimalPngBytes(),
            cropLeft: 0.1,
            cropTop: 0.2,
            cropRight: 0.15,
            cropBottom: 0.05);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var picture = loaded.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;
        picture.CropLeft.Should().Be(0.1);
        picture.CropTop.Should().Be(0.2);
        picture.CropRight.Should().Be(0.15);
        picture.CropBottom.Should().Be(0.05);
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesPictureDrawingAndMediaReferencesAlongsideModelEdits()
    {
        var workbook = new Workbook("PictureXlsxRetentionTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Has picture"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalPicturePackage(source, MinimalPngBytes());

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("xl/drawings/drawing1.xml").Should().NotBeNull();
        archive.GetEntry("xl/drawings/_rels/drawing1.xml.rels").Should().NotBeNull();
        archive.GetEntry("xl/media/image1.png").Should().NotBeNull();

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        worksheetXml.ToString().Should().Contain("drawing");

        var drawingXml = LoadPackageXml(archive.GetEntry("xl/drawings/drawing1.xml")!);
        drawingXml.ToString().Should().Contain("pic");
        drawingXml.ToString().Should().Contain("Native picture");
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_ImagePicture_SavesAsDrawing()
    {
        var workbook = new Workbook("PictureXlsxSaveTest");
        var sheet = workbook.AddSheet("Sheet1");
        var imageBytes = MinimalPngBytes();
        sheet.Pictures.Add(new PictureModel
        {
            Name = "Product Photo",
            Anchor = new CellAddress(sheet.Id, 2, 3),
            Kind = PictureKind.Image,
            ImageBytes = imageBytes,
            ContentType = "image/png",
            Width = 120,
            Height = 80,
            AltText = "Authored picture"
        });

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using (var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            archive.GetEntry("xl/drawings/drawing1.xml").Should().NotBeNull();
            archive.GetEntry("xl/drawings/_rels/drawing1.xml.rels").Should().NotBeNull();
            archive.GetEntry("xl/media/freexcelPicture1.png").Should().NotBeNull();
        }

        ms.Position = 0;
        var loaded = adapter.Load(ms);

        var picture = loaded.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;
        picture.Name.Should().Be("Product Photo");
        picture.Kind.Should().Be(PictureKind.Image);
        picture.ImageBytes.Should().Equal(imageBytes);
        picture.ContentType.Should().Be("image/png");
        picture.Width.Should().Be(120);
        picture.Height.Should().Be(80);
        picture.AltText.Should().Be("Authored picture");
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_ImagePicture_SavesCropAsSourceRectangle()
    {
        var workbook = new Workbook("PictureXlsxCropSaveTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 3),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            Width = 120,
            Height = 80,
            CropLeft = 0.1,
            CropTop = 0.2,
            CropRight = 0.15,
            CropBottom = 0.05,
            AltText = "Cropped picture"
        });

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using (var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            var drawingXml = LoadPackageXml(archive.GetEntry("xl/drawings/drawing1.xml")!);
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            var srcRect = drawingXml.Descendants(drawingNs + "srcRect").Should().ContainSingle().Subject;
            srcRect.Attribute("l")?.Value.Should().Be("10000");
            srcRect.Attribute("t")?.Value.Should().Be("20000");
            srcRect.Attribute("r")?.Value.Should().Be("15000");
            srcRect.Attribute("b")?.Value.Should().Be("5000");
        }

        ms.Position = 0;
        var loaded = adapter.Load(ms);

        var picture = loaded.GetSheetAt(0).Pictures.Should().ContainSingle().Subject;
        picture.CropLeft.Should().Be(0.1);
        picture.CropTop.Should().Be(0.2);
        picture.CropRight.Should().Be(0.15);
        picture.CropBottom.Should().Be(0.05);
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
            Name = "Review Callout",
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
            Name = "Approval Shape",
            Anchor = new CellAddress(sheet.Id, 4, 3),
            Kind = DrawingShapeKind.Ellipse,
            Width = 140,
            Height = 90,
            RotationDegrees = 45,
            FillColor = new CellColor(200, 210, 220),
            OutlineColor = new CellColor(30, 40, 50),
            GradientFillEndColor = new CellColor(240, 245, 250),
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0.5),
            OutlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, -0.5),
            HasShadowEffect = true,
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
        shape.GradientFillEndColor.Should().Be(new CellColor(240, 245, 250));
        shape.FillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0.5));
        shape.OutlineThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, -0.5));
        shape.HasShadowEffect.Should().BeTrue();
        shape.AltText.Should().Be("Approval marker");
    }

    [Fact]
    public void XlsxAdapter_LoadsTextBoxesAndDrawingShapes()
    {
        var workbook = new Workbook("XlsxDrawingLoadTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        var source = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, source);
        source.Position = 0;
        AddMinimalShapePackage(source);

        var loaded = new XlsxFileAdapter().Load(source);

        var loadedSheet = loaded.GetSheetAt(0);
        var textBox = loadedSheet.TextBoxes.Should().ContainSingle().Subject;
        textBox.Anchor.Row.Should().Be(2);
        textBox.Anchor.Col.Should().Be(2);
        textBox.Text.Should().Be("Native note");
        textBox.AltText.Should().Be("Native text box");
        textBox.Width.Should().BeApproximately(160, 0.1);
        textBox.Height.Should().BeApproximately(70, 0.1);
        textBox.FillColor.Should().Be(new CellColor(255, 238, 204));
        textBox.OutlineColor.Should().Be(new CellColor(102, 119, 136));

        var shape = loadedSheet.DrawingShapes.Should().ContainSingle().Subject;
        shape.Anchor.Row.Should().Be(5);
        shape.Anchor.Col.Should().Be(4);
        shape.Kind.Should().Be(DrawingShapeKind.Ellipse);
        shape.AltText.Should().Be("Native ellipse");
        shape.Width.Should().BeApproximately(120, 0.1);
        shape.Height.Should().BeApproximately(80, 0.1);
        shape.FillColor.Should().Be(new CellColor(221, 238, 255));
        shape.GradientFillEndColor.Should().Be(new CellColor(238, 248, 255));
        shape.OutlineColor.Should().Be(new CellColor(17, 34, 51));
        shape.HasShadowEffect.Should().BeTrue();
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_TextBoxesAndDrawingShapes()
    {
        var workbook = new Workbook("XlsxDrawingSaveTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Name = "Review Callout",
            Text = "Review note",
            Width = 220,
            Height = 120,
            FillColor = new CellColor(240, 250, 255),
            OutlineColor = new CellColor(70, 80, 90),
            AltText = "Review note callout"
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 4, 3),
            Name = "Approval Shape",
            Kind = DrawingShapeKind.Ellipse,
            Width = 140,
            Height = 90,
            FillColor = new CellColor(200, 210, 220),
            OutlineColor = new CellColor(30, 40, 50),
            GradientFillEndColor = new CellColor(240, 245, 250),
            HasShadowEffect = true,
            AltText = "Approval marker"
        });

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using (var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            var drawingXml = LoadPackageXml(archive.GetEntry("xl/drawings/drawing1.xml")!);
            XNamespace xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
            XNamespace a = "http://schemas.openxmlformats.org/drawingml/2006/main";
            drawingXml.Descendants(xdr + "sp").Should().HaveCount(2);
            drawingXml.Descendants(xdr + "cNvPr").Select(e => e.Attribute("name")?.Value)
                .Should()
                .Contain(["Review Callout", "Approval Shape"]);
            drawingXml.Descendants(a + "t").Select(e => e.Value).Should().Contain("Review note");
            drawingXml.Descendants(a + "prstGeom").Select(e => e.Attribute("prst")?.Value).Should().Contain("ellipse");
            drawingXml.Descendants(a + "gradFill").Should().ContainSingle();
            drawingXml.Descendants(a + "outerShdw").Should().ContainSingle();
        }

        ms.Position = 0;
        var loaded = adapter.Load(ms);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedTextBox = loadedSheet.TextBoxes.Should().ContainSingle().Subject;
        loadedTextBox.Name.Should().Be("Review Callout");
        loadedTextBox.Text.Should().Be("Review note");
        var loadedShape = loadedSheet.DrawingShapes.Should().ContainSingle().Subject;
        loadedShape.Name.Should().Be("Approval Shape");
        loadedShape.Kind.Should().Be(DrawingShapeKind.Ellipse);
        loadedShape.GradientFillEndColor.Should().Be(new CellColor(240, 245, 250));
        loadedShape.HasShadowEffect.Should().BeTrue();
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
    public void NativeJsonAdapter_Save_SanitizesInvalidObjectState()
    {
        var workbook = new Workbook("ObjectSaveSanitizeTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            Kind = (PictureKind)99,
            Width = -1,
            Height = double.NaN,
            RotationDegrees = -90
        });
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Anchor = new CellAddress(sheet.Id, 3, 2),
            Text = "Note",
            Width = 0,
            Height = double.PositiveInfinity,
            RotationDegrees = 450
        });
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(sheet.Id, 4, 3),
            Kind = (DrawingShapeKind)99,
            Width = double.NegativeInfinity,
            Height = -10,
            RotationDegrees = 725
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using var document = JsonDocument.Parse(ms);
        var sheetJson = document.RootElement.GetProperty("Sheets")[0];
        var picture = sheetJson.GetProperty("Pictures")[0];
        picture.GetProperty("Kind").GetInt32().Should().Be((int)PictureKind.CellRangeSnapshot);
        picture.GetProperty("Width").GetDouble().Should().Be(240);
        picture.GetProperty("Height").GetDouble().Should().Be(140);
        picture.GetProperty("RotationDegrees").GetDouble().Should().Be(270);

        var textBox = sheetJson.GetProperty("TextBoxes")[0];
        textBox.GetProperty("Width").GetDouble().Should().Be(180);
        textBox.GetProperty("Height").GetDouble().Should().Be(80);
        textBox.GetProperty("RotationDegrees").GetDouble().Should().Be(90);

        var shape = sheetJson.GetProperty("DrawingShapes")[0];
        shape.GetProperty("Kind").GetInt32().Should().Be((int)DrawingShapeKind.Rectangle);
        shape.GetProperty("Width").GetDouble().Should().Be(120);
        shape.GetProperty("Height").GetDouble().Should().Be(70);
        shape.GetProperty("RotationDegrees").GetDouble().Should().Be(5);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_Sparklines()
    {
        var workbook = new Workbook("SparklineNativeTest");
        var sheet = workbook.AddSheet("Sheet1");
        var sparkline = new SparklineModel
        {
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 3)),
            Location = new CellAddress(sheet.Id, 1, 4),
            Kind = SparklineKind.Column
        };
        sheet.Sparklines.Add(sparkline);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSparkline = loaded.GetSheetAt(0).Sparklines.Should().ContainSingle().Subject;
        loadedSparkline.DataRange.Start.ToA1().Should().Be("A1");
        loadedSparkline.DataRange.End.ToA1().Should().Be("C1");
        loadedSparkline.Location.ToA1().Should().Be("D1");
        loadedSparkline.Kind.Should().Be(SparklineKind.Column);
    }

    [Fact]
    public void XlsxAdapter_LoadsSparklineGroups()
    {
        var workbook = new Workbook("SparklineXlsxLoadTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(3));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalSparklineWorksheetExtension(source);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var loadedSheet = loaded.GetSheetAt(0);
        var sparkline = loadedSheet.Sparklines.Should().ContainSingle().Subject;
        sparkline.Kind.Should().Be(SparklineKind.Column);
        sparkline.DataRange.Start.ToA1().Should().Be("A1");
        sparkline.DataRange.End.ToA1().Should().Be("C1");
        sparkline.Location.ToA1().Should().Be("D1");
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_Sparklines()
    {
        var workbook = new Workbook("SparklineXlsxSaveTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(3));
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 3)),
            Location = new CellAddress(sheet.Id, 1, 4),
            Kind = SparklineKind.WinLoss
        });

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using (var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.ToString().Should().Contain("sparklineGroups");
            worksheetXml.ToString().Should().Contain("stacked");
        }

        ms.Position = 0;
        var loaded = adapter.Load(ms);

        var sparkline = loaded.GetSheetAt(0).Sparklines.Should().ContainSingle().Subject;
        sparkline.Kind.Should().Be(SparklineKind.WinLoss);
        sparkline.DataRange.Start.ToA1().Should().Be("A1");
        sparkline.DataRange.End.ToA1().Should().Be("C1");
        sparkline.Location.ToA1().Should().Be("D1");
    }

    [Fact]
    public void NativeJsonAdapter_Save_SkipsInvalidSparklines()
    {
        var workbook = new Workbook("SparklineSaveSanitizeTest");
        var sheet = workbook.AddSheet("Sheet1");
        var otherSheet = workbook.AddSheet("Other");
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 3)),
            Location = new CellAddress(sheet.Id, 1, 4),
            Kind = SparklineKind.Line
        });
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 2, 1),
                new CellAddress(sheet.Id, 2, 3)),
            Location = new CellAddress(sheet.Id, 2, 4),
            Kind = (SparklineKind)99
        });
        sheet.Sparklines.Add(new SparklineModel
        {
            DataRange = new GridRange(
                new CellAddress(otherSheet.Id, 1, 1),
                new CellAddress(otherSheet.Id, 1, 3)),
            Location = new CellAddress(sheet.Id, 3, 4),
            Kind = SparklineKind.Column
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using var document = JsonDocument.Parse(ms);
        var sparklines = document.RootElement.GetProperty("Sheets")[0].GetProperty("Sparklines");
        sparklines.GetArrayLength().Should().Be(1);
        sparklines[0].GetProperty("Kind").GetInt32().Should().Be((int)SparklineKind.Line);
        sparklines[0].GetProperty("DataRange").GetString().Should().Be("A1:C1");
        sparklines[0].GetProperty("Location").GetString().Should().Be("D1");
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
    public void XlsxAdapter_LoadedWorkbookSave_PreservesFormulaNativeMetadata()
    {
        var workbook = new Workbook("FormulaNativeMetadata");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(3.14));
        var formulaCell = Cell.FromFormula("A1*2");
        formulaCell.Value = new NumberValue(6.28);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), formulaCell);

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetFormulaNativeMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 3, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var formula = worksheetXml.Root!
            .Element(worksheetNs + "sheetData")!
            .Descendants(worksheetNs + "c")
            .Single(element => element.Attribute("r")?.Value == "A2")
            .Element(worksheetNs + "f");
        formula.Should().NotBeNull();
        formula!.Attribute("t").Should().NotBeNull();
        formula.Attribute("t")!.Value.Should().Be("array");
        formula.Attribute("ref").Should().NotBeNull();
        formula.Attribute("ref")!.Value.Should().Be("A2:A2");
        formula.Attribute("ca").Should().NotBeNull();
        formula.Attribute("ca")!.Value.Should().Be("1");
        formula.Attribute("customAttr").Should().NotBeNull();
        formula.Attribute("customAttr")!.Value.Should().Be("formula-native");
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
            FillColor = new CellColor(255, 242, 204),
            FillPatternStyle = CellFillPatternStyle.DarkGrid,
            FillPatternColor = new CellColor(112, 48, 160)
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
        loadedStyle.FillColor.Should().Be(new CellColor(255, 242, 204));
        loadedStyle.FillPatternStyle.Should().Be(CellFillPatternStyle.DarkGrid);
        loadedStyle.FillPatternColor.Should().Be(new CellColor(112, 48, 160));
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_TextRotation()
    {
        var workbook = new Workbook("TextRotationTest");
        var sheet = workbook.AddSheet("S1");

        var style = new CellStyle
        {
            TextRotation = 45
        };
        var styleId = workbook.RegisterStyle(style);

        var addr = new CellAddress(sheet.Id, 1, 1);
        var cell = Cell.FromValue(new TextValue("rotated"));
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
        loadedStyle.TextRotation.Should().Be(45);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_JustifyDistributedAndShrinkToFit()
    {
        var workbook = new Workbook("AlignmentTest");
        var sheet = workbook.AddSheet("S1");

        var style = new CellStyle
        {
            HorizontalAlignment = HorizontalAlignment.Distributed,
            VerticalAlignment = VerticalAlignment.Justify,
            ShrinkToFit = true
        };
        var styleId = workbook.RegisterStyle(style);

        var addr = new CellAddress(sheet.Id, 1, 1);
        var cell = Cell.FromValue(new TextValue("aligned"));
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
        loadedStyle.HorizontalAlignment.Should().Be(HorizontalAlignment.Distributed);
        loadedStyle.VerticalAlignment.Should().Be(VerticalAlignment.Justify);
        loadedStyle.ShrinkToFit.Should().BeTrue();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_ShrinkToFit()
    {
        var workbook = new Workbook("NativeShrinkTest");
        var sheet = workbook.AddSheet("S1");
        var styleId = workbook.RegisterStyle(new CellStyle { ShrinkToFit = true });
        var cell = Cell.FromValue(new TextValue("fit"));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        var loadedCell = loaded.GetSheetAt(0).GetCell(1, 1);
        loadedCell.Should().NotBeNull();
        loaded.GetStyle(loadedCell!.StyleId).ShrinkToFit.Should().BeTrue();
    }

    [Fact]
    public void XlsxAdapter_Save_SanitizesInvalidAlignmentAndCellStyleValues()
    {
        var workbook = new Workbook("InvalidStyleSaveTest");
        var sheet = workbook.AddSheet("S1");

        var style = new CellStyle
        {
            Bold = true,
            FontSize = 0,
            HorizontalAlignment = (HorizontalAlignment)99,
            VerticalAlignment = (VerticalAlignment)99,
            BorderTop = new CellBorder((BorderStyle)99, new CellColor(255, 0, 0)),
            TextRotation = 999
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
        loadedStyle.FontSize.Should().Be(11);
        loadedStyle.HorizontalAlignment.Should().Be(HorizontalAlignment.General);
        loadedStyle.VerticalAlignment.Should().Be(VerticalAlignment.Bottom);
        loadedStyle.BorderTop.Style.Should().Be(BorderStyle.None);
        loadedStyle.TextRotation.Should().Be(0);
        loadedStyle.ShrinkToFit.Should().BeFalse();
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
    public void XlsxAdapter_Save_SkipsInvalidRowHeightsAndColumnWidths()
    {
        var workbook = new Workbook("InvalidLayoutSaveTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Layout"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Width"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new TextValue("Ignored"));
        sheet.RowHeights[1] = 28;
        sheet.RowHeights[2] = 0;
        sheet.RowHeights[3] = double.NaN;
        sheet.ColumnWidths[1] = 18;
        sheet.ColumnWidths[2] = -5;
        sheet.ColumnWidths[3] = double.PositiveInfinity;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        var sheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        sheetXml.Descendants(sheetNs + "row")
            .Single(row => row.Attribute("r")!.Value == "1")
            .Attribute("ht")!.Value.Should().Be("21");
        sheetXml.Descendants(sheetNs + "row")
            .Where(row => row.Attribute("r")!.Value == "2" || row.Attribute("r")!.Value == "3")
            .Should().OnlyContain(row => row.Attribute("ht") == null);
        sheetXml.Descendants(sheetNs + "col")
            .Single(col => col.Attribute("min")!.Value == "1" && col.Attribute("max")!.Value == "1")
            .Attribute("width")!.Value.Should().NotBeNullOrWhiteSpace();
        sheetXml.Descendants(sheetNs + "col")
            .Where(col => col.Attribute("min")!.Value == "2" ||
                col.Attribute("min")!.Value == "3" ||
                col.Attribute("max")!.Value == "2" ||
                col.Attribute("max")!.Value == "3")
            .Should().BeEmpty();
    }

    [Fact]
    public void XlsxAdapter_Save_SkipsOutOfBoundsRowAndColumnLayoutIndexes()
    {
        var workbook = new Workbook("OutOfBoundsLayoutSaveTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Layout"));
        sheet.RowHeights[1] = 28;
        sheet.RowHeights[CellAddress.MaxRow + 1] = 32;
        sheet.ColumnWidths[1] = 18;
        sheet.ColumnWidths[CellAddress.MaxCol + 1] = 24;
        sheet.HiddenRows.Add(CellAddress.MaxRow + 1);
        sheet.HiddenCols.Add(CellAddress.MaxCol + 1);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        var sheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace sheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        sheetXml.Descendants(sheetNs + "row")
            .Should().NotContain(row => row.Attribute("r")!.Value == (CellAddress.MaxRow + 1).ToString(CultureInfo.InvariantCulture));
        sheetXml.Descendants(sheetNs + "col")
            .Should().NotContain(col =>
                col.Attribute("min")!.Value == (CellAddress.MaxCol + 1).ToString(CultureInfo.InvariantCulture) ||
                col.Attribute("max")!.Value == (CellAddress.MaxCol + 1).ToString(CultureInfo.InvariantCulture));
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
    public void XlsxAdapter_Load_FreezePaneTopLeftDoesNotBecomeWorksheetViewport()
    {
        var workbook = new Workbook("FreezePaneViewStateTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("visible"));
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 1;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using (var archive = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument document;
            using (var entryStream = entry.Open())
                document = XDocument.Load(entryStream);

            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var sheetView = document.Root!
                .Element(worksheetNs + "sheetViews")!
                .Element(worksheetNs + "sheetView")!;
            sheetView.SetAttributeValue("topLeftCell", "C7");
            sheetView.Element(worksheetNs + "pane")!.SetAttributeValue("topLeftCell", "B2");
            sheetView.Elements(worksheetNs + "selection").Remove();
            sheetView.Add(new XElement(
                worksheetNs + "selection",
                new XAttribute("pane", "bottomRight"),
                new XAttribute("activeCell", "D9"),
                new XAttribute("sqref", "D9")));

            entry.Delete();
            var updated = archive.CreateEntry("xl/worksheets/sheet1.xml");
            using var updatedStream = updated.Open();
            document.Save(updatedStream);
        }
        ms.Position = 0;

        var loadedSheet = adapter.Load(ms).GetSheetAt(0);

        loadedSheet.FrozenRows.Should().Be(1);
        loadedSheet.FrozenCols.Should().Be(1);
        loadedSheet.ViewTopRow.Should().Be(7);
        loadedSheet.ViewLeftCol.Should().Be(3);
        loadedSheet.ActiveRow.Should().Be(9);
        loadedSheet.ActiveCol.Should().Be(4);
    }

    [Fact]
    public void XlsxAdapter_Save_SanitizesInvalidFrozenPaneState()
    {
        var workbook = new Workbook("InvalidFreezePaneXlsxTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("visible"));
        sheet.FrozenRows = CellAddress.MaxRow + 1;
        sheet.FrozenCols = CellAddress.MaxCol + 1;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.FrozenRows.Should().Be(0);
        loadedSheet.FrozenCols.Should().Be(0);
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
    public void XlsxAdapter_RoundTrip_VeryHiddenSheetAndCodeName()
    {
        var workbook = new Workbook("VeryHiddenSheetMetadataTest");
        var visible = workbook.AddSheet("Visible");
        visible.SetCell(new CellAddress(visible.Id, 1, 1), new TextValue("visible"));
        var hidden = workbook.AddSheet("Internal");
        hidden.SetCell(new CellAddress(hidden.Id, 1, 1), new TextValue("hidden"));
        hidden.IsHidden = true;
        hidden.IsVeryHidden = true;
        hidden.CodeName = "SheetInternal";

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedHidden = loaded.Sheets.Single(s => s.Name == "Internal");
        loadedHidden.IsHidden.Should().BeTrue();
        loadedHidden.IsVeryHidden.Should().BeTrue();
        loadedHidden.CodeName.Should().Be("SheetInternal");
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
    public void XlsxAdapter_RoundTrip_WorksheetViewMode()
    {
        var workbook = new Workbook("XlsxViewModeTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        sheet.ViewMode = WorksheetViewMode.PageBreakPreview;

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).ViewMode.Should().Be(WorksheetViewMode.PageBreakPreview);
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
        const string friendlyTokens = "&[Date] &[Time] &[File] &[Path] &[Tab] &[Page]/&[Pages] &[Picture]";
        sheet.PageHeader = new WorksheetHeaderFooter("Left header", friendlyTokens, "Right header");
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

        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            var sheetXml = zip.GetEntry("xl/worksheets/sheet1.xml");
            sheetXml.Should().NotBeNull();
            using var reader = new StreamReader(sheetXml.Open());
            var xml = XDocument.Parse(reader.ReadToEnd());
            XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            xml.Descendants(main + "oddHeader")
                .Single()
                .Value
                .Should()
                .Contain("&C&D &T &F &Z &A &P/&N &G");
        }

        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.PrintTitleRows.Should().Be(new WorksheetRepeatRange(1, 2));
        loadedSheet.PrintTitleColumns.Should().Be(new WorksheetRepeatRange(1, 1));
        loadedSheet.HeaderMargin.Should().Be(0.35);
        loadedSheet.FooterMargin.Should().Be(0.45);
        loadedSheet.PageHeader.Should().Be(new WorksheetHeaderFooter("Left header", friendlyTokens, "Right header"));
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
    public void XlsxAdapter_Load_ConvertsExcelHeaderFooterShortTokens()
    {
        using var xl = new XLWorkbook();
        var xlSheet = xl.AddWorksheet("Summary");
        xlSheet.Cell(1, 1).Value = "x";
        xlSheet.PageSetup.Header.Center.AddText("&D &T &F &Z &A &P/&N &G", XLHFOccurrence.AllPages);

        var ms = new MemoryStream();
        xl.SaveAs(ms);
        ms.Position = 0;

        var loaded = new XlsxFileAdapter().Load(ms);

        loaded.GetSheetAt(0).PageHeader.Center
            .Should()
            .Be("&[Date] &[Time] &[File] &[Path] &[Tab] &[Page]/&[Pages] &[Picture]");
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
    public void XlsxAdapter_RoundTrip_WorkbookCalculationProperties()
    {
        var workbook = new Workbook("CalculationPropertiesTest")
        {
            CalculationMode = WorkbookCalculationMode.Manual,
            FullCalculationOnLoad = true,
            ForceFullCalculation = true,
            IterativeCalculation = true,
            MaxCalculationIterations = 123,
            MaxCalculationChange = 0.001
        };
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "1+1");

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using (var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!).ToString();
            workbookXml.Should().Contain("calcMode=\"manual\"");
            workbookXml.Should().Contain("fullCalcOnLoad=\"1\"");
            workbookXml.Should().Contain("forceFullCalc=\"1\"");
            workbookXml.Should().Contain("iterate=\"1\"");
            workbookXml.Should().Contain("iterateCount=\"123\"");
            workbookXml.Should().Contain("iterateDelta=\"0.001\"");
        }

        ms.Position = 0;
        var loaded = adapter.Load(ms);

        loaded.CalculationMode.Should().Be(WorkbookCalculationMode.Manual);
        loaded.FullCalculationOnLoad.Should().BeTrue();
        loaded.ForceFullCalculation.Should().BeTrue();
        loaded.IterativeCalculation.Should().BeTrue();
        loaded.MaxCalculationIterations.Should().Be(123);
        loaded.MaxCalculationChange.Should().Be(0.001);
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorkbookCalculationNativeMetadata()
    {
        var workbook = new Workbook("CalculationNativeMetadataTest")
        {
            CalculationMode = WorkbookCalculationMode.Manual,
            IterativeCalculation = true,
            MaxCalculationIterations = 50
        };
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "1+1");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorkbookCalculationNativeMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var calcPr = workbookXml.Root!.Element(workbookNs + "calcPr");
        calcPr.Should().NotBeNull();
        calcPr!.Attribute("calcMode")!.Value.Should().Be("manual");
        calcPr.Attribute("iterate")!.Value.Should().Be("1");
        calcPr.Attribute("iterateCount")!.Value.Should().Be("50");
        calcPr.Attribute("calcId").Should().NotBeNull();
        calcPr.Attribute("calcId")!.Value.Should().Be("191029");
        calcPr.Attribute("refMode").Should().NotBeNull();
        calcPr.Attribute("refMode")!.Value.Should().Be("A1");
        calcPr.Attribute("fullPrecision").Should().NotBeNull();
        calcPr.Attribute("fullPrecision")!.Value.Should().Be("0");
        calcPr.Attribute("concurrentCalc").Should().NotBeNull();
        calcPr.Attribute("concurrentCalc")!.Value.Should().Be("1");
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_Hyperlinks()
    {
        var workbook = new Workbook("HyperlinkTest");
        var sheet = workbook.AddSheet("S1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new TextValue("Example"));
        sheet.Hyperlinks[addr] = "https://example.com/docs";
        sheet.HyperlinkMetadata[addr] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open documentation",
            "");

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        var loadedAddr = new CellAddress(loadedSheet.Id, 1, 1);
        loadedSheet.Hyperlinks[loadedAddr].Should().Be("https://example.com/docs");
        loadedSheet.HyperlinkMetadata[loadedAddr].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open documentation",
            ""));
        loadedSheet.GetValue(loadedAddr).Should().Be(new TextValue("Example"));
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_InternalHyperlinkMetadata()
    {
        var workbook = new Workbook("HyperlinkInternalTest");
        var sheet = workbook.AddSheet("S1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new TextValue("Jump"));
        sheet.Hyperlinks[addr] = "S1!B2";
        sheet.HyperlinkMetadata[addr] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump to details",
            "S1!B2");

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loadedSheet = adapter.Load(ms).GetSheetAt(0);
        var loadedAddr = new CellAddress(loadedSheet.Id, 1, 1);

        loadedSheet.Hyperlinks[loadedAddr].Should().Be("S1!B2");
        loadedSheet.HyperlinkMetadata[loadedAddr].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Jump to details",
            "S1!B2"));
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesHyperlinkNativeMetadata()
    {
        var workbook = new Workbook("HyperlinkNativeMetadata");
        var sheet = workbook.AddSheet("S1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new TextValue("Example"));
        sheet.Hyperlinks[addr] = "https://example.com/docs";

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetHyperlinkNativeMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var hyperlink = worksheetXml.Root!
            .Element(worksheetNs + "hyperlinks")!
            .Elements(worksheetNs + "hyperlink")
            .Single(element => element.Attribute("ref")?.Value == "A1");
        worksheetXml.Root!
            .Element(worksheetNs + "hyperlinks")!
            .Attribute("nativeHyperlinksAttr")
            .Should()
            .NotBeNull();
        worksheetXml.Root!
            .Element(worksheetNs + "hyperlinks")!
            .Attribute("nativeHyperlinksAttr")!
            .Value
            .Should()
            .Be("kept");
        hyperlink.Attribute("tooltip").Should().NotBeNull();
        hyperlink.Attribute("tooltip")!.Value.Should().Be("Open documentation");
        hyperlink.Attribute("display").Should().NotBeNull();
        hyperlink.Attribute("display")!.Value.Should().Be("Freexcel docs");
        hyperlink.Attribute("customAttr").Should().NotBeNull();
        hyperlink.Attribute("customAttr")!.Value.Should().Be("hyperlink-native");
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
    public void XlsxAdapter_LoadedWorkbookSave_PreservesAdvancedSheetProtectionMetadata()
    {
        var workbook = new Workbook("AdvancedSheetProtectionRetentionTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddAdvancedSheetProtectionMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var protection = worksheetXml.Root!.Element(worksheetNs + "sheetProtection");
        protection.Should().NotBeNull();
        (protection!.Attribute("algorithmName")?.Value).Should().Be("SHA-512");
        (protection.Attribute("hashValue")?.Value).Should().Be("abc123");
        (protection.Attribute("saltValue")?.Value).Should().Be("salt123");
        (protection.Attribute("spinCount")?.Value).Should().Be("100000");
        (protection.Attribute("objects")?.Value).Should().Be("1");
        (protection.Attribute("scenarios")?.Value).Should().Be("1");
        protection.Elements(XName.Get("sheetProtectionNativeChild", "urn:freexcel:test"))
            .Select(element => element.Attribute("id")?.Value)
            .Should()
            .BeEquivalentTo("first", "second");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesProtectedRangeMetadata()
    {
        var workbook = new Workbook("ProtectedRangeRetentionTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddProtectedRangeMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).AllowEditRanges.Should().ContainSingle();
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 4, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var protectedRange = worksheetXml.Root!
            .Element(worksheetNs + "protectedRanges")!
            .Element(worksheetNs + "protectedRange");
        protectedRange.Should().NotBeNull();
        protectedRange!.Attribute("sqref")!.Value.Should().Be("B2:C3");
        protectedRange.Attribute("name")!.Value.Should().Be("NativeEditableRange");
        protectedRange.Attribute("password")!.Value.Should().Be("ABCD");
        protectedRange.Attribute("securityDescriptor")!.Value.Should().Be("D:PAI");
        protectedRange.Element(worksheetNs + "extLst").Should().NotBeNull();
        protectedRange.Elements(XName.Get("protectedRangeNativeChild", "urn:freexcel:test"))
            .Select(element => element.Attribute("id")?.Value)
            .Should()
            .BeEquivalentTo("first", "second");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_DoesNotResurrectRemovedProtectedRange()
    {
        var workbook = new Workbook("ProtectedRangeRemovalTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddProtectedRangeMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.AllowEditRanges.Should().ContainSingle();
        loadedSheet.AllowEditRanges.Clear();

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        worksheetXml.Root!
            .Element(worksheetNs + "protectedRanges")?
            .Elements(worksheetNs + "protectedRange")
            .Should()
            .BeNullOrEmpty();
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_RetainsNativeOnlyMultiAreaProtectedRange()
    {
        var workbook = new Workbook("ProtectedRangeNativeOnlyTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMultiAreaProtectedRangeMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).AllowEditRanges.Should().BeEmpty();

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var protectedRange = worksheetXml.Root!
            .Element(worksheetNs + "protectedRanges")!
            .Element(worksheetNs + "protectedRange");
        protectedRange.Should().NotBeNull();
        protectedRange!.Attribute("sqref")!.Value.Should().Be("B2 C3");
        protectedRange.Attribute("name")!.Value.Should().Be("NativeMultiAreaRange");
        protectedRange.Attribute("password")!.Value.Should().Be("1234");
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_WorkbookStructureProtection()
    {
        var workbook = new Workbook("WorkbookProtectionTest");
        workbook.IsStructureProtected = true;
        workbook.StructureProtectionPassword = "password";
        workbook.AddSheet("S1");

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using (var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var protection = workbookXml.Root!.Element(ns + "workbookProtection");
            protection.Should().NotBeNull();
            protection!.Attribute("lockStructure")!.Value.Should().Be("1");
            protection.Attribute("workbookPassword")!.Value.Should().Be("83AF");
            protection.Attribute("workbookPassword")!.Value.Should().NotBe("password");
        }

        ms.Position = 0;
        var loaded = adapter.Load(ms);

        loaded.IsStructureProtected.Should().BeTrue();
        loaded.StructureProtectionPassword.Should().Be("83AF");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesAdvancedWorkbookProtectionMetadata()
    {
        var workbook = new Workbook("AdvancedWorkbookProtectionRetentionTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("locked"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddAdvancedWorkbookProtectionMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var protection = workbookXml.Root!.Element(workbookNs + "workbookProtection");
        protection.Should().NotBeNull();
        (protection!.Attribute("algorithmName")?.Value).Should().Be("SHA-512");
        (protection.Attribute("hashValue")?.Value).Should().Be("def456");
        (protection.Attribute("saltValue")?.Value).Should().Be("salt456");
        (protection.Attribute("spinCount")?.Value).Should().Be("100000");
        (protection.Attribute("lockWindows")?.Value).Should().Be("1");
        protection.Elements(XName.Get("workbookProtectionNativeChild", "urn:freexcel:test"))
            .Select(element => element.Attribute("id")?.Value)
            .Should()
            .BeEquivalentTo("first", "second");
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_UnlockedCellStyle()
    {
        var workbook = new Workbook("UnlockedStyleTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 1, 1);
        var styleId = workbook.RegisterStyle(new CellStyle { Locked = false, Hidden = true });
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
        loaded.GetStyle(loadedCell.StyleId).Hidden.Should().BeTrue();
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

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesSharedStringRichTextAndPhonetics()
    {
        var workbook = new Workbook("SharedStringNativeMetadata");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Rich phonetic"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddSharedStringRichTextAndPhonetics(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var sharedStringsXml = LoadPackageXml(archive.GetEntry("xl/sharedStrings.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var richString = sharedStringsXml.Root!
            .Elements(worksheetNs + "si")
            .Single(element => element.Elements(worksheetNs + "r").Any(run =>
                run.Element(worksheetNs + "rPr")?.Element(worksheetNs + "rFont")?.Attribute("val")?.Value == "FreexcelRich"));
        richString.Elements(worksheetNs + "r").Should().HaveCount(2);
        richString.Element(worksheetNs + "rPh").Should().NotBeNull();
        richString.Element(worksheetNs + "rPh")!
            .Element(worksheetNs + "t")!
            .Value.Should().Be("ri-chi");
        richString.Element(worksheetNs + "phoneticPr").Should().NotBeNull();
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesInlineStringRichTextAndPhonetics()
    {
        var workbook = new Workbook("InlineStringNativeMetadata");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Inline phonetic"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddInlineStringRichTextAndPhonetics(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cell = worksheetXml.Root!
            .Element(worksheetNs + "sheetData")!
            .Descendants(worksheetNs + "c")
            .Single(element => element.Attribute("r")?.Value == "A1");
        cell.Attribute("t")!.Value.Should().Be("inlineStr");
        var inlineString = cell.Element(worksheetNs + "is");
        inlineString.Should().NotBeNull();
        inlineString!.Elements(worksheetNs + "r").Should().HaveCount(2);
        inlineString.Element(worksheetNs + "rPh").Should().NotBeNull();
        inlineString.Element(worksheetNs + "rPh")!
            .Element(worksheetNs + "t")!
            .Value.Should().Be("in-line");
        inlineString.Element(worksheetNs + "phoneticPr").Should().NotBeNull();
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesLegacyCommentAuthorsAndRichText()
    {
        var workbook = new Workbook("CommentNativeMetadata");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue("review"));
        sheet.Comments[address] = "Check this input";

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddLegacyCommentRichTextMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 3, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var commentsEntry = archive.Entries.Single(entry =>
            entry.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase) &&
            entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
        var commentsXml = LoadPackageXml(commentsEntry);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        commentsXml.Root!
            .Element(worksheetNs + "authors")!
            .Elements(worksheetNs + "author")
            .Should().ContainSingle(author => author.Value == "Excel Reviewer");

        var comment = commentsXml.Root
            .Element(worksheetNs + "commentList")!
            .Elements(worksheetNs + "comment")
            .Single(element => element.Attribute("ref")?.Value == "C2");
        comment.Attribute("authorId")!.Value.Should().Be("0");
        comment.Element(worksheetNs + "text")!
            .Elements(worksheetNs + "r")
            .Should().HaveCount(2);
        comment.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("FreexcelBold");
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
    public void XlsxAdapter_Save_SkipsInvalidConditionalFormatRules()
    {
        var workbook = new Workbook("CfInvalidSaveTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(10));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));
        var validStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "5",
            FormatIfTrue = validStyle
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = (CfRuleType)999,
            Operator = CfOperator.GreaterThan,
            Value1 = "5",
            FormatIfTrue = validStyle
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.CellValue,
            Operator = (CfOperator)999,
            Value1 = "5",
            FormatIfTrue = validStyle
        });

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle();
        var rule = loaded.GetSheetAt(0).ConditionalFormats[0];
        rule.RuleType.Should().Be(CfRuleType.CellValue);
        rule.Operator.Should().Be(CfOperator.GreaterThan);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_ConditionalFormat_ColorScaleAndDataBarMetadata_Survives()
    {
        var workbook = new Workbook("CfAdvancedMetadata");
        var sheet = workbook.AddSheet("S1");
        for (uint row = 1; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 10));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row * 20));
        }

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.ColorScale,
            Priority = 1,
            UseThreeColorScale = true,
            MinThresholdType = CfThresholdType.Number,
            MinThresholdValue = "0",
            MidThresholdType = CfThresholdType.Percentile,
            MidThresholdValue = "50",
            MaxThresholdType = CfThresholdType.Number,
            MaxThresholdValue = "100",
            MinColor = new RgbColor(99, 190, 123),
            MidColor = new RgbColor(255, 235, 132),
            MaxColor = new RgbColor(248, 105, 107)
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 5, 2)),
            RuleType = CfRuleType.DataBar,
            Priority = 2,
            DataBarColor = new RgbColor(91, 155, 213),
            DataBarMinThresholdType = CfThresholdType.Number,
            DataBarMinThresholdValue = "0",
            DataBarMaxThresholdType = CfThresholdType.Number,
            DataBarMaxThresholdValue = "100",
            DataBarShowValue = false,
            DataBarMinLength = 5,
            DataBarMaxLength = 95
        });

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        var colorScale = loaded.GetSheetAt(0).ConditionalFormats.Should()
            .Contain(rule => rule.RuleType == CfRuleType.ColorScale).Subject;
        colorScale.UseThreeColorScale.Should().BeTrue();
        colorScale.MinThresholdType.Should().Be(CfThresholdType.Number);
        colorScale.MinThresholdValue.Should().Be("0");
        colorScale.MidThresholdType.Should().Be(CfThresholdType.Percentile);
        colorScale.MidThresholdValue.Should().Be("50");
        colorScale.MaxThresholdType.Should().Be(CfThresholdType.Number);
        colorScale.MaxThresholdValue.Should().Be("100");
        colorScale.MinColor.Should().Be(new RgbColor(99, 190, 123));
        colorScale.MidColor.Should().Be(new RgbColor(255, 235, 132));
        colorScale.MaxColor.Should().Be(new RgbColor(248, 105, 107));

        var dataBar = loaded.GetSheetAt(0).ConditionalFormats.Should()
            .Contain(rule => rule.RuleType == CfRuleType.DataBar).Subject;
        dataBar.DataBarColor.Should().Be(new RgbColor(91, 155, 213));
        dataBar.DataBarMinThresholdType.Should().Be(CfThresholdType.Number);
        dataBar.DataBarMinThresholdValue.Should().Be("0");
        dataBar.DataBarMaxThresholdType.Should().Be(CfThresholdType.Number);
        dataBar.DataBarMaxThresholdValue.Should().Be("100");
        dataBar.DataBarShowValue.Should().BeFalse();
        dataBar.DataBarMinLength.Should().Be(5);
        dataBar.DataBarMaxLength.Should().Be(95);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_ConditionalFormat_IconSetMetadata_Survives()
    {
        var workbook = new Workbook("CfIconSetMetadata");
        var sheet = workbook.AddSheet("S1");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            RuleType = CfRuleType.IconSet,
            Priority = 1,
            IconSetStyle = "3TrafficLights1",
            IconSetShowValue = false,
            IconSetReverse = true
        });
        sheet.ConditionalFormats.Single().IconSetThresholds.AddRange(
        [
            new CfThresholdModel(CfThresholdType.Number, "0"),
            new CfThresholdModel(CfThresholdType.Percentile, "50"),
            new CfThresholdModel(CfThresholdType.Formula, "AVERAGE($A$1:$A$5)")
        ]);

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: true))
        {
            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var xml = worksheetXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
            xml.Should().Contain("type=\"num\" val=\"0\"");
            xml.Should().Contain("type=\"percentile\" val=\"50\"");
            xml.Should().Contain("type=\"formula\" val=\"AVERAGE($A$1:$A$5)\"");
        }

        ms.Position = 0;
        var loaded = adapter.Load(ms);

        var iconSet = loaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        iconSet.RuleType.Should().Be(CfRuleType.IconSet);
        iconSet.IconSetStyle.Should().Be("3TrafficLights1");
        iconSet.IconSetShowValue.Should().BeFalse();
        iconSet.IconSetReverse.Should().BeTrue();
        iconSet.IconSetThresholds.Should().Equal(
            new CfThresholdModel(CfThresholdType.Number, "0"),
            new CfThresholdModel(CfThresholdType.Percentile, "50"),
            new CfThresholdModel(CfThresholdType.Formula, "AVERAGE($A$1:$A$5)"));
    }

    [Fact]
    public void XlsxAdapter_LoadSave_RoundTripsAdvancedConditionalFormatNativeMetadata()
    {
        var workbook = new Workbook("CfNativeMetadata");
        var sheet = workbook.AddSheet("S1");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 10));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            RuleType = CfRuleType.ColorScale,
            Priority = 1,
            MinThresholdType = CfThresholdType.Number,
            MinThresholdValue = "0",
            MaxThresholdType = CfThresholdType.Number,
            MaxThresholdValue = "50",
            MinColor = new RgbColor(99, 190, 123),
            MaxColor = new RgbColor(248, 105, 107)
        });

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddAdvancedConditionalFormatNativeMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedRule = loaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        loadedRule.NativeContainerAttributes.Should().ContainKey("customBlockAttr").WhoseValue.Should().Be("cf-container");
        loadedRule.NativeContainerChildXmls.Should().ContainSingle()
            .Which.Should().Contain("{FREEXCEL-CF-CONTAINER-EXT}");
        loadedRule.NativeAttributes.Should().ContainKey("customAttr").WhoseValue.Should().Be("cf-native");
        loadedRule.NativeChildXmls.Should().ContainSingle()
            .Which.Should().Contain("{FREEXCEL-CF-EXT}");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        worksheetXml.Should().Contain("customBlockAttr=\"cf-container\"");
        worksheetXml.Should().Contain("customAttr=\"cf-native\"");
        worksheetXml.Should().Contain("extLst");
        worksheetXml.Should().Contain("{FREEXCEL-CF-CONTAINER-EXT}");
        worksheetXml.Should().Contain("{FREEXCEL-CF-EXT}");
        worksheetXml.Should().Contain("colorScale");
    }

    [Fact]
    public void XlsxAdapter_LoadSave_RoundTripsAdvancedConditionalFormatPayloadNativeMetadata()
    {
        var workbook = new Workbook("CfPayloadNativeMetadata");
        var sheet = workbook.AddSheet("S1");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 10));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            RuleType = CfRuleType.DataBar,
            Priority = 1,
            DataBarShowValue = true,
            DataBarColor = new RgbColor(99, 142, 198)
        });

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddAdvancedConditionalFormatPayloadNativeMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedRule = loaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        loadedRule.NativePayloadAttributes.Should().ContainKey("border").WhoseValue.Should().Be("1");
        loadedRule.NativePayloadAttributes.Should().ContainKey("axisPosition").WhoseValue.Should().Be("middle");
        loadedRule.NativePayloadChildXmls.Should().Contain(xml => xml.Contains("negativeFillColor", StringComparison.Ordinal));
        loadedRule.NativePayloadChildXmls.Should().Contain(xml => xml.Contains("axisColor", StringComparison.Ordinal));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        worksheetXml.Should().Contain("dataBar");
        worksheetXml.Should().Contain("border=\"1\"");
        worksheetXml.Should().Contain("axisPosition=\"middle\"");
        worksheetXml.Should().Contain("negativeFillColor");
        worksheetXml.Should().Contain("axisColor");
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_ConditionalFormat_LongTailMetadata_Survives()
    {
        var workbook = new Workbook("LongTailCfXlsxTest");
        var sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            RuleType = CfRuleType.Top10,
            Priority = 1,
            TopBottomRank = 3,
            AboveAverage = false,
            TopBottomPercent = true
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 5, 2)),
            RuleType = CfRuleType.ContainsText,
            Priority = 2,
            TextRuleText = "urgent",
            FormulaText = "NOT(ISERROR(SEARCH(\"urgent\",B1)))"
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 5, 3)),
            RuleType = CfRuleType.DateOccurring,
            Priority = 3,
            DateOccurringPeriod = "last7Days"
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 5, 4)),
            RuleType = CfRuleType.DuplicateValues,
            Priority = 4
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 5, 5)),
            RuleType = CfRuleType.NoErrors,
            Priority = 5
        });

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedFormats = loaded.GetSheetAt(0).ConditionalFormats;
        loadedFormats.Should().Contain(format => format.RuleType == CfRuleType.Top10 && format.TopBottomRank == 3 && !format.AboveAverage && format.TopBottomPercent);
        loadedFormats.Should().Contain(format => format.RuleType == CfRuleType.ContainsText && format.TextRuleText == "urgent");
        loadedFormats.Should().Contain(format => format.RuleType == CfRuleType.DateOccurring && format.DateOccurringPeriod == "last7Days");
        loadedFormats.Should().Contain(format => format.RuleType == CfRuleType.DuplicateValues);
        loadedFormats.Should().Contain(format => format.RuleType == CfRuleType.NoErrors);
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_ConditionalFormat_LongTailDifferentialStyle_Survives()
    {
        var workbook = new Workbook("LongTailCfDxfXlsxTest");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("urgent"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            RuleType = CfRuleType.ContainsText,
            Priority = 1,
            TextRuleText = "urgent",
            FormulaText = "NOT(ISERROR(SEARCH(\"urgent\",A1)))",
            FormatIfTrue = new CellStyle
            {
                Bold = true,
                FontColor = new CellColor(255, 255, 255),
                FillColor = new CellColor(192, 0, 0),
                BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(0, 0, 0)),
                NumberFormat = "0.00"
            }
        });

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var style = loaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject.FormatIfTrue;
        style.Should().NotBeNull();
        style!.Bold.Should().BeTrue();
        style.FontColor.Should().Be(new CellColor(255, 255, 255));
        style.FillColor.Should().Be(new CellColor(192, 0, 0));
        style.BorderBottom.Should().Be(new CellBorder(BorderStyle.Thin, new CellColor(0, 0, 0)));
        style.NumberFormat.Should().Be("0.00");
    }

    [Fact]
    public void XlsxAdapter_LoadSave_RoundTripsConditionalFormatDifferentialStyleNativeMetadata()
    {
        var workbook = new Workbook("CfDxfNativeMetadata");
        var sheet = workbook.AddSheet("S1");
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row * 10));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            RuleType = CfRuleType.Top10,
            Priority = 1,
            TopBottomRank = 3,
            FormatIfTrue = new CellStyle
            {
                Bold = true,
                FillColor = new CellColor(255, 242, 204),
                NumberFormat = "0.00"
            }
        });

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddConditionalFormatDifferentialStyleNativeMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedStyle = loaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject.FormatIfTrue;
        loadedStyle.Should().NotBeNull();
        loadedStyle!.NativeDifferentialAttributes.Should().ContainKey("customAttr").WhoseValue.Should().Be("dxf-native");
        loadedStyle.NativeDifferentialChildXmls.Should().ContainSingle()
            .Which.Should().Contain("{FREEXCEL-DXF-NATIVE}");
        loadedStyle.NativeDifferentialElementXmls.Should().ContainKey("font")
            .WhoseValue.Should().Contain("customFontAttr=\"font-native\"");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var stylesXml = LoadPackageXml(archive.GetEntry("xl/styles.xml")!).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        stylesXml.Should().Contain("customAttr=\"dxf-native\"");
        stylesXml.Should().Contain("customFontAttr=\"font-native\"");
        stylesXml.Should().Contain("scheme val=\"minor\"");
        stylesXml.Should().Contain("{FREEXCEL-DXF-NATIVE}");
        stylesXml.Should().Contain("formatCode=\"0.00\"");
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
    public void XlsxAdapter_LoadSave_RoundTripsDataValidationNativeMetadata()
    {
        var workbook = new Workbook("DvNativeMetadataTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Apple"));
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 1)),
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry"
        });

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using (var archive = new ZipArchive(ms, ZipArchiveMode.Update, leaveOpen: true))
        {
            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var dataValidations = worksheetXml.Root!.Element(worksheetNs + "dataValidations")!;
            dataValidations.SetAttributeValue("disablePrompts", "1");
            dataValidations.SetAttributeValue("xWindow", "25");
            var validation = dataValidations.Element(worksheetNs + "dataValidation")!;
            validation.SetAttributeValue("imeMode", "noControl");
            validation.Add(new XElement(
                worksheetNs + "extLst",
                new XElement(worksheetNs + "ext", new XAttribute("uri", "{FREEXCEL-DV-NATIVE}"))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        ms.Position = 0;
        var loaded = adapter.Load(ms);
        var loadedRule = loaded.GetSheetAt(0).DataValidations.Should().ContainSingle().Subject;
        loadedRule.NativeAttributes.Should().Contain("imeMode", "noControl");
        loadedRule.NativeChildXmls.Should().ContainSingle(xml => xml.Contains("{FREEXCEL-DV-NATIVE}", StringComparison.Ordinal));
        loadedRule.NativeContainerAttributes.Should().Contain("disablePrompts", "1");
        loadedRule.NativeContainerAttributes.Should().Contain("xWindow", "25");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var savedArchive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var savedWorksheetXml = LoadPackageXml(savedArchive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace savedWorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var savedDataValidations = savedWorksheetXml.Root!.Element(savedWorksheetNs + "dataValidations")!;
        savedDataValidations.Attribute("disablePrompts")!.Value.Should().Be("1");
        savedDataValidations.Attribute("xWindow")!.Value.Should().Be("25");
        var savedValidation = savedDataValidations.Element(savedWorksheetNs + "dataValidation")!;
        savedValidation.Attribute("imeMode")!.Value.Should().Be("noControl");
        savedValidation.Element(savedWorksheetNs + "extLst")!
            .Element(savedWorksheetNs + "ext")!
            .Attribute("uri")!.Value.Should().Be("{FREEXCEL-DV-NATIVE}");
    }

    [Fact]
    public void XlsxAdapter_Save_SkipsInvalidDataValidationRules()
    {
        var workbook = new Workbook("DvInvalidSaveTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Apple"));

        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 10, 1));
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = range,
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            AlertStyle = DvAlertStyle.Information
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = range,
            Type = (DvType)999,
            Formula1 = "Apple,Banana,Cherry",
            AlertStyle = DvAlertStyle.Information
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = range,
            Type = DvType.WholeNumber,
            Operator = (DvOperator)999,
            Formula1 = "1",
            Formula2 = "10",
            AlertStyle = DvAlertStyle.Information
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = range,
            Type = DvType.List,
            Formula1 = "Apple,Banana,Cherry",
            AlertStyle = (DvAlertStyle)999
        });

        var ms = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;
        var loaded = adapter.Load(ms);

        loaded.GetSheetAt(0).DataValidations.Should().ContainSingle();
        var rule = loaded.GetSheetAt(0).DataValidations[0];
        rule.Type.Should().Be(DvType.List);
        rule.AlertStyle.Should().Be(DvAlertStyle.Information);
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
            PromptMessage = "Use 09:00 through 17:30.",
            NativeAttributes = new Dictionary<string, string> { ["imeMode"] = "noControl" },
            NativeChildXmls = ["<extLst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><ext uri=\"{FREEXCEL-DV-NATIVE}\" /></extLst>"],
            NativeContainerAttributes = new Dictionary<string, string> { ["disablePrompts"] = "1", ["xWindow"] = "25" },
            NativeContainerChildXmls = ["<extLst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><ext uri=\"{FREEXCEL-DV-CONTAINER}\" /></extLst>"]
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
        rule.NativeAttributes.Should().Contain("imeMode", "noControl");
        rule.NativeChildXmls.Should().ContainSingle(xml => xml.Contains("{FREEXCEL-DV-NATIVE}", StringComparison.Ordinal));
        rule.NativeContainerAttributes.Should().Contain("disablePrompts", "1");
        rule.NativeContainerAttributes.Should().Contain("xWindow", "25");
        rule.NativeContainerChildXmls.Should().ContainSingle(xml => xml.Contains("{FREEXCEL-DV-CONTAINER}", StringComparison.Ordinal));
    }

    [Fact]
    public void NativeJsonAdapter_Load_SkipsInvalidDataValidationRules()
    {
        const string json = """
        {
          "Name": "DvNativeInvalidLoad",
          "Sheets": [
            {
              "Name": "S1",
              "DataValidations": [
                {
                  "AppliesTo": "A1:A5",
                  "Type": 5,
                  "Operator": 0,
                  "AlertStyle": 1,
                  "Formula1": "09:00",
                  "Formula2": "17:30"
                },
                {
                  "AppliesTo": "A1:A5",
                  "Type": 999,
                  "Operator": 0,
                  "AlertStyle": 1
                },
                {
                  "AppliesTo": "A1:A5",
                  "Type": 5,
                  "Operator": 999,
                  "AlertStyle": 1
                },
                {
                  "AppliesTo": "A1:A5",
                  "Type": 5,
                  "Operator": 0,
                  "AlertStyle": 999
                }
              ]
            }
          ]
        }
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var loaded = new NativeJsonAdapter().Load(ms);

        loaded.GetSheetAt(0).DataValidations.Should().ContainSingle();
        loaded.GetSheetAt(0).DataValidations[0].Type.Should().Be(DvType.Time);
    }

    [Fact]
    public void NativeJsonAdapter_Save_SkipsInvalidDataValidationRules()
    {
        var workbook = new Workbook("DvNativeInvalidSave");
        var sheet = workbook.AddSheet("S1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));

        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = range,
            Type = DvType.Time,
            Operator = DvOperator.Between,
            AlertStyle = DvAlertStyle.Warning,
            Formula1 = "09:00",
            Formula2 = "17:30"
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = range,
            Type = (DvType)999,
            Operator = DvOperator.Between,
            AlertStyle = DvAlertStyle.Warning
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = range,
            Type = DvType.Time,
            Operator = (DvOperator)999,
            AlertStyle = DvAlertStyle.Warning
        });
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = range,
            Type = DvType.Time,
            Operator = DvOperator.Between,
            AlertStyle = (DvAlertStyle)999
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using var document = JsonDocument.Parse(ms);
        var validations = document.RootElement
            .GetProperty("Sheets")[0]
            .GetProperty("DataValidations")
            .EnumerateArray()
            .ToList();

        validations.Should().ContainSingle();
        validations[0].GetProperty("Type").GetInt32().Should().Be((int)DvType.Time);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_ConditionalFormatRule_Survives()
    {
        var workbook = new Workbook("CfNativeTest");
        var sheet = workbook.AddSheet("S1");
        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 1)),
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "5",
            Priority = 2,
            StopIfTrue = true,
            NativeAttributes = new Dictionary<string, string> { ["customAttr"] = "cf-native" },
            NativeChildXmls = ["<extLst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><ext uri=\"{FREEXCEL-CF-EXT}\" /></extLst>"],
            NativePayloadAttributes = new Dictionary<string, string> { ["border"] = "1" },
            NativePayloadChildXmls = ["<axisColor xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" rgb=\"FF000000\" />"],
            NativeContainerAttributes = new Dictionary<string, string> { ["customBlockAttr"] = "cf-container" },
            NativeContainerChildXmls = ["<extLst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><ext uri=\"{FREEXCEL-CF-CONTAINER-EXT}\" /></extLst>"],
            FormatIfTrue = new CellStyle
            {
                Bold = true,
                FillColor = new CellColor(255, 0, 0),
                FontColor = new CellColor(255, 255, 255),
                NativeDifferentialAttributes = new Dictionary<string, string> { ["customAttr"] = "dxf-native" },
                NativeDifferentialChildXmls = ["<extLst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><ext uri=\"{FREEXCEL-DXF-NATIVE}\" /></extLst>"],
                NativeDifferentialElementXmls = new Dictionary<string, string>
                {
                    ["font"] = "<font xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" customFontAttr=\"font-native\"><scheme val=\"minor\" /></font>"
                }
            }
        };
        sheet.ConditionalFormats.Add(cf);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var rule = loaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.AppliesTo.Start.ToA1().Should().Be("A1");
        rule.AppliesTo.End.ToA1().Should().Be("A5");
        rule.RuleType.Should().Be(CfRuleType.CellValue);
        rule.Operator.Should().Be(CfOperator.GreaterThan);
        rule.Value1.Should().Be("5");
        rule.Priority.Should().Be(2);
        rule.StopIfTrue.Should().BeTrue();
        rule.NativeContainerAttributes.Should().ContainKey("customBlockAttr").WhoseValue.Should().Be("cf-container");
        rule.NativeContainerChildXmls.Should().ContainSingle().Which.Should().Contain("{FREEXCEL-CF-CONTAINER-EXT}");
        rule.NativeAttributes.Should().ContainKey("customAttr").WhoseValue.Should().Be("cf-native");
        rule.NativeChildXmls.Should().ContainSingle().Which.Should().Contain("{FREEXCEL-CF-EXT}");
        rule.NativePayloadAttributes.Should().ContainKey("border").WhoseValue.Should().Be("1");
        rule.NativePayloadChildXmls.Should().ContainSingle().Which.Should().Contain("axisColor");
        rule.FormatIfTrue.Should().NotBeNull();
        rule.FormatIfTrue!.Bold.Should().BeTrue();
        rule.FormatIfTrue.FillColor.Should().Be(new CellColor(255, 0, 0));
        rule.FormatIfTrue.FontColor.Should().Be(new CellColor(255, 255, 255));
        rule.FormatIfTrue.NativeDifferentialAttributes.Should().ContainKey("customAttr").WhoseValue.Should().Be("dxf-native");
        rule.FormatIfTrue.NativeDifferentialChildXmls.Should().ContainSingle().Which.Should().Contain("{FREEXCEL-DXF-NATIVE}");
        rule.FormatIfTrue.NativeDifferentialElementXmls.Should().ContainKey("font").WhoseValue.Should().Contain("customFontAttr");
    }

    [Fact]
    public void NativeJsonAdapter_Load_SkipsInvalidConditionalFormatRules()
    {
        const string json = """
        {
          "Name": "CfNativeInvalidLoad",
          "Sheets": [
            {
              "Name": "S1",
              "ConditionalFormats": [
                {
                  "AppliesTo": "A1:A5",
                  "RuleType": 0,
                  "Operator": 2,
                  "Value1": "5"
                },
                {
                  "AppliesTo": "A1:A5",
                  "RuleType": 999,
                  "Operator": 2,
                  "Value1": "5"
                },
                {
                  "AppliesTo": "A1:A5",
                  "RuleType": 0,
                  "Operator": 999,
                  "Value1": "5"
                }
              ]
            }
          ]
        }
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var loaded = new NativeJsonAdapter().Load(ms);

        var rule = loaded.GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        rule.RuleType.Should().Be(CfRuleType.CellValue);
        rule.Operator.Should().Be(CfOperator.GreaterThan);
    }

    [Fact]
    public void NativeJsonAdapter_Save_SkipsInvalidConditionalFormatRules()
    {
        var workbook = new Workbook("CfNativeInvalidSave");
        var sheet = workbook.AddSheet("S1");
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 5, 1));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "5"
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = (CfRuleType)999,
            Operator = CfOperator.GreaterThan,
            Value1 = "5"
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            RuleType = CfRuleType.CellValue,
            Operator = (CfOperator)999,
            Value1 = "5"
        });

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        using var document = JsonDocument.Parse(ms);
        var formats = document.RootElement
            .GetProperty("Sheets")[0]
            .GetProperty("ConditionalFormats")
            .EnumerateArray()
            .ToList();

        formats.Should().ContainSingle();
        formats[0].GetProperty("RuleType").GetInt32().Should().Be((int)CfRuleType.CellValue);
        formats[0].GetProperty("Operator").GetInt32().Should().Be((int)CfOperator.GreaterThan);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_MergedRegions_Survive()
    {
        var workbook = new Workbook("MergeNativeTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("merged"));
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 3)));

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var merged = loaded.GetSheetAt(0).MergedRegions.Should().ContainSingle().Subject;
        merged.Start.Row.Should().Be(1);
        merged.Start.Col.Should().Be(1);
        merged.End.Row.Should().Be(2);
        merged.End.Col.Should().Be(3);
    }

    [Fact]
    public void NativeJsonAdapter_Load_SkipsInvalidMergedRegions()
    {
        const string json = """
        {
          "Name": "MergeNativeInvalidLoad",
          "Sheets": [
            {
              "Name": "S1",
              "MergedRegions": [ "A1:C2", "not-a-range" ]
            }
          ]
        }
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var loaded = new NativeJsonAdapter().Load(ms);

        var merged = loaded.GetSheetAt(0).MergedRegions.Should().ContainSingle().Subject;
        merged.Start.ToA1().Should().Be("A1");
        merged.End.ToA1().Should().Be("C2");
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_CellComments()
    {
        var workbook = new Workbook("CommentNativeTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.Comments[address] = "Check this input";

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedAddress = new CellAddress(loaded.GetSheetAt(0).Id, 2, 3);
        loaded.GetSheetAt(0).Comments.Should().ContainKey(loadedAddress);
        loaded.GetSheetAt(0).Comments[loadedAddress].Should().Be("Check this input");
    }

    [Fact]
    public void NativeJsonAdapter_Load_SkipsInvalidCellComments()
    {
        const string json = """
        {
          "Name": "CommentNativeInvalidLoad",
          "Sheets": [
            {
              "Name": "S1",
              "Comments": [
                { "Address": "C2", "Text": "Check this input" },
                { "Address": "not-a-cell", "Text": "Skip me" },
                { "Address": "D4", "Text": null }
              ]
            }
          ]
        }
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var loaded = new NativeJsonAdapter().Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.Comments.Should().ContainSingle();
        loadedSheet.Comments[new CellAddress(loadedSheet.Id, 2, 3)].Should().Be("Check this input");
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_Hyperlinks()
    {
        var workbook = new Workbook("HyperlinkNativeTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.SetCell(address, new TextValue("Docs"));
        sheet.Hyperlinks[address] = "https://example.com/docs";
        sheet.HyperlinkMetadata[address] = new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Open docs section",
            "Docs!A1");

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedAddress = new CellAddress(loaded.GetSheetAt(0).Id, 2, 3);
        loaded.GetSheetAt(0).Hyperlinks.Should().ContainKey(loadedAddress);
        loaded.GetSheetAt(0).Hyperlinks[loadedAddress].Should().Be("https://example.com/docs");
        loaded.GetSheetAt(0).HyperlinkMetadata[loadedAddress].Should().Be(new HyperlinkMetadata(
            HyperlinkTargetKind.PlaceInThisDocument,
            "Open docs section",
            "Docs!A1"));
    }

    [Fact]
    public void NativeJsonAdapter_Load_SkipsInvalidHyperlinks()
    {
        const string json = """
        {
          "Name": "HyperlinkNativeInvalidLoad",
          "Sheets": [
            {
              "Name": "S1",
              "Hyperlinks": [
                { "Address": "C2", "Target": "https://example.com/docs" },
                { "Address": "not-a-cell", "Target": "https://example.com/skip" },
                { "Address": "D4", "Target": null }
              ]
            }
          ]
        }
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var loaded = new NativeJsonAdapter().Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.Hyperlinks.Should().ContainSingle();
        loadedSheet.Hyperlinks[new CellAddress(loadedSheet.Id, 2, 3)].Should().Be("https://example.com/docs");
        loadedSheet.HyperlinkMetadata[new CellAddress(loadedSheet.Id, 2, 3)].Should().Be(new HyperlinkMetadata());
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_ProtectionState()
    {
        var workbook = new Workbook("ProtectionNativeTest");
        workbook.IsStructureProtected = true;
        workbook.StructureProtectionPassword = "workbook-secret";
        var sheet = workbook.AddSheet("S1");
        sheet.IsProtected = true;
        sheet.ProtectionPassword = "sheet-secret";
        sheet.AllowEditRanges.Add(new GridRange(
            new CellAddress(sheet.Id, 2, 2),
            new CellAddress(sheet.Id, 3, 3)));

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.IsStructureProtected.Should().BeTrue();
        loaded.StructureProtectionPassword.Should().Be("workbook-secret");
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.IsProtected.Should().BeTrue();
        loadedSheet.ProtectionPassword.Should().Be("sheet-secret");
        var allowEditRange = loadedSheet.AllowEditRanges.Should().ContainSingle().Subject;
        allowEditRange.Start.ToA1().Should().Be("B2");
        allowEditRange.End.ToA1().Should().Be("C3");
    }

    [Fact]
    public void NativeJsonAdapter_Load_SkipsInvalidAllowEditRanges()
    {
        const string json = """
        {
          "Name": "ProtectionNativeInvalidLoad",
          "IsStructureProtected": true,
          "StructureProtectionPassword": "workbook-secret",
          "Sheets": [
            {
              "Name": "S1",
              "IsProtected": true,
              "ProtectionPassword": "sheet-secret",
              "AllowEditRanges": [ "B2:C3", "not-a-range" ]
            }
          ]
        }
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var loaded = new NativeJsonAdapter().Load(ms);

        loaded.IsStructureProtected.Should().BeTrue();
        loaded.StructureProtectionPassword.Should().Be("workbook-secret");
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.IsProtected.Should().BeTrue();
        loadedSheet.ProtectionPassword.Should().Be("sheet-secret");
        var allowEditRange = loadedSheet.AllowEditRanges.Should().ContainSingle().Subject;
        allowEditRange.Start.ToA1().Should().Be("B2");
        allowEditRange.End.ToA1().Should().Be("C3");
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_RowColumnLayoutState()
    {
        var workbook = new Workbook("LayoutNativeTest");
        var sheet = workbook.AddSheet("S1");
        sheet.RowHeights[2] = 28;
        sheet.ColumnWidths[3] = 18;
        sheet.HiddenRows.Add(4);
        sheet.FilterHiddenRows.Add(12);
        sheet.HiddenCols.Add(5);
        sheet.RowOutlineLevels[6] = 2;
        sheet.ColOutlineLevels[7] = 3;
        sheet.GroupHiddenRows.Add(8);
        sheet.GroupHiddenCols.Add(9);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms).GetSheetAt(0);

        loaded.RowHeights[2].Should().Be(28);
        loaded.ColumnWidths[3].Should().Be(18);
        loaded.HiddenRows.Should().Contain(4u);
        loaded.FilterHiddenRows.Should().Contain(12u);
        loaded.HiddenCols.Should().Contain(5u);
        loaded.RowOutlineLevels[6].Should().Be(2);
        loaded.ColOutlineLevels[7].Should().Be(3);
        loaded.GroupHiddenRows.Should().Contain(8u);
        loaded.GroupHiddenCols.Should().Contain(9u);
    }

    [Fact]
    public void NativeJsonAdapter_Load_SkipsInvalidRowColumnLayoutState()
    {
        const string json = """
        {
          "Name": "LayoutNativeInvalidLoad",
          "Sheets": [
            {
              "Name": "S1",
              "RowHeights": [
                { "Index": 2, "Value": 28 },
                { "Index": 1048577, "Value": 30 },
                { "Index": 3, "Value": 0 }
              ],
              "ColumnWidths": [
                { "Index": 3, "Value": 18 },
                { "Index": 16385, "Value": 20 },
                { "Index": 4, "Value": -1 }
              ],
              "HiddenRows": [ 4, 1048577 ],
              "FilterHiddenRows": [ 12, 1048577 ],
              "HiddenCols": [ 5, 16385 ],
              "RowOutlineLevels": [
                { "Index": 6, "Value": 2 },
                { "Index": 7, "Value": 9 }
              ],
              "ColOutlineLevels": [
                { "Index": 8, "Value": 3 },
                { "Index": 9, "Value": 0 }
              ],
              "GroupHiddenRows": [ 10, 1048577 ],
              "GroupHiddenCols": [ 11, 16385 ]
            }
          ]
        }
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var loaded = new NativeJsonAdapter().Load(ms).GetSheetAt(0);

        loaded.RowHeights.Should().ContainSingle().Which.Should().Be(new KeyValuePair<uint, double>(2, 28));
        loaded.ColumnWidths.Should().ContainSingle().Which.Should().Be(new KeyValuePair<uint, double>(3, 18));
        loaded.HiddenRows.Should().Equal(4u);
        loaded.FilterHiddenRows.Should().Equal(12u);
        loaded.HiddenCols.Should().Equal(5u);
        loaded.RowOutlineLevels.Should().ContainSingle().Which.Should().Be(new KeyValuePair<uint, int>(6, 2));
        loaded.ColOutlineLevels.Should().ContainSingle().Which.Should().Be(new KeyValuePair<uint, int>(8, 3));
        loaded.GroupHiddenRows.Should().Equal(10u);
        loaded.GroupHiddenCols.Should().Equal(11u);
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_CellStyles()
    {
        var workbook = new Workbook("StyleNativeTest");
        var sheet = workbook.AddSheet("S1");
        var address = new CellAddress(sheet.Id, 2, 3);
        var cell = Cell.FromValue(new TextValue("styled"));
        cell.StyleId = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FontColor = new CellColor(12, 34, 56),
            FillColor = new CellColor(200, 210, 220),
            FillPatternStyle = CellFillPatternStyle.DarkGrid,
            FillPatternColor = new CellColor(90, 80, 70),
            Locked = false,
            Hidden = true
        });
        sheet.SetCell(address, cell);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedCell = loaded.GetSheetAt(0).GetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 3));
        loadedCell.Should().NotBeNull();
        var loadedStyle = loaded.GetStyle(loadedCell!.StyleId);
        loadedStyle.Bold.Should().BeTrue();
        loadedStyle.FontColor.Should().Be(new CellColor(12, 34, 56));
        loadedStyle.FillColor.Should().Be(new CellColor(200, 210, 220));
        loadedStyle.FillPatternStyle.Should().Be(CellFillPatternStyle.DarkGrid);
        loadedStyle.FillPatternColor.Should().Be(new CellColor(90, 80, 70));
        loadedStyle.Locked.Should().BeFalse();
        loadedStyle.Hidden.Should().BeTrue();
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_StyleOnlyCells()
    {
        var workbook = new Workbook("StyleOnlyNativeTest");
        var sheet = workbook.AddSheet("S1");
        var styleId = workbook.RegisterStyle(new CellStyle
        {
            Italic = true,
            FillColor = new CellColor(1, 2, 3)
        });
        sheet.SetStyleOnly(4, 5, styleId);

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.GetCell(4, 5).Should().BeNull();
        var loadedStyleId = loadedSheet.GetStyleOnly(4, 5);
        loadedStyleId.Should().NotBeNull();
        var loadedStyle = loaded.GetStyle(loadedStyleId!.Value);
        loadedStyle.Italic.Should().BeTrue();
        loadedStyle.FillColor.Should().Be(new CellColor(1, 2, 3));
    }

    [Fact]
    public void NativeJsonAdapter_RoundTrip_NamedRanges()
    {
        var workbook = new Workbook("NamedRangeNativeTest");
        var sheet = workbook.AddSheet("Data");
        workbook.DefineNamedRange(
            "SalesData",
            new GridRange(
                new CellAddress(sheet.Id, 2, 2),
                new CellAddress(sheet.Id, 5, 4)),
            new NamedRangeMetadata("Data", "Quarterly sales"));

        var ms = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, ms);
        ms.Position = 0;

        var loaded = adapter.Load(ms);

        loaded.NamedRanges.Should().ContainKey("SalesData");
        var loadedSheet = loaded.GetSheet("Data")!;
        loaded.NamedRanges["SalesData"].Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 2, 2),
            new CellAddress(loadedSheet.Id, 5, 4)));
        loaded.NamedRangeMetadataByName["SalesData"].Should().Be(new NamedRangeMetadata("Data", "Quarterly sales"));
    }

    [Fact]
    public void NativeJsonAdapter_Load_SkipsInvalidNamedRanges()
    {
        const string json = """
        {
          "Name": "NamedRangeNativeInvalidLoad",
          "NamedRanges": [
            { "Name": "SalesData", "SheetName": "Data", "Range": "B2:D5" },
            { "Name": "Bad Name", "SheetName": "Data", "Range": "B2:D5" },
            { "Name": "MissingSheet", "SheetName": "Missing", "Range": "B2:D5" },
            { "Name": "BadRange", "SheetName": "Data", "Range": "not-a-range" }
          ],
          "Sheets": [ { "Name": "Data" } ]
        }
        """;

        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var loaded = new NativeJsonAdapter().Load(ms);

        loaded.NamedRanges.Should().ContainSingle();
        loaded.NamedRanges.Should().ContainKey("SalesData");
    }

    [Fact]
    public void XlsxAdapter_RoundTrip_MergedRegions_Survive()
    {
        var workbook = new Workbook("MergeTest");
        var sheet = workbook.AddSheet("S1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("merged"));
        sheet.AddMergedRegion(new GridRange(
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
    public void XlsxAdapter_LoadedWorkbookSave_PreservesMergedCellNativeMetadata()
    {
        var workbook = new Workbook("MergedCellNativeMetadata");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("merged"));
        sheet.AddMergedRegion(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2)));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMergedCellNativeMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 3, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var mergeCells = worksheetXml.Root!.Element(worksheetNs + "mergeCells");
        mergeCells.Should().NotBeNull();
        mergeCells!.Attribute("nativeMergeContainerAttr").Should().NotBeNull();
        mergeCells.Attribute("nativeMergeContainerAttr")!.Value.Should().Be("kept");
        var mergeCell = mergeCells.Elements(worksheetNs + "mergeCell")
            .Single(element => element.Attribute("ref")?.Value == "A1:B2");
        mergeCell.Attribute("nativeMergeCellAttr").Should().NotBeNull();
        mergeCell.Attribute("nativeMergeCellAttr")!.Value.Should().Be("kept");
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

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedColumnChartWithoutHeaderPackagePart()
    {
        var workbook = new Workbook("ColumnChartNoHeaderPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(30));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            Title = "Sales",
            FirstRowIsHeader = false,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        loadedChart.FirstRowIsHeader.Should().BeFalse();
        loadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 3, 2)));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedSingleRowColumnChartWithoutHeaderPackagePart()
    {
        var workbook = new Workbook("ColumnChartSingleRowNoHeaderPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(10));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            Title = "Sales",
            FirstRowIsHeader = false,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        loadedChart.FirstRowIsHeader.Should().BeFalse();
        loadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 1, 2)));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedColumnChartWithoutCategoryColumnPackagePart()
    {
        var workbook = new Workbook("ColumnChartNoCategoryPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Expenses"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(11));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(14));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            Title = "Values",
            FirstColIsCategories = false,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        loadedChart.FirstColIsCategories.Should().BeFalse();
        loadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 4, 2)));
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
    public void XlsxAdapter_Save_WritesEmbeddedLineChartWithoutCategoryColumnPackagePart()
    {
        var workbook = new Workbook("LineChartNoCategoryPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Expenses"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(11));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(14));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            Title = "Values",
            FirstColIsCategories = false,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Line);
        loadedChart.FirstColIsCategories.Should().BeFalse();
        loadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 4, 2)));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedLineChartSeriesFormattingPackagePart()
    {
        var workbook = new Workbook("LineChartSeriesFormattingPackageSave");
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
            Type = ChartType.Line,
            Title = "Sales",
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    StrokeColor: new CellColor(68, 114, 196),
                    StrokeThickness: 2.75,
                    DashStyle: ChartLineDashStyle.Dash,
                    MarkerStyle: ChartMarkerStyle.Diamond,
                    MarkerSize: 7)
            ],
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(
                0,
                StrokeColor: new CellColor(68, 114, 196),
                StrokeThickness: 2.75,
                DashStyle: ChartLineDashStyle.Dash,
                MarkerStyle: ChartMarkerStyle.Diamond,
                MarkerSize: 7));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedLineChartMarkerFillPackagePart()
    {
        var workbook = new Workbook("LineChartMarkerFillPackageSave");
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
            Type = ChartType.Line,
            Title = "Sales",
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    FillColor: new CellColor(255, 192, 0),
                    StrokeColor: new CellColor(68, 114, 196),
                    MarkerStyle: ChartMarkerStyle.Circle,
                    MarkerSize: 8)
            ],
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(
                0,
                FillColor: new CellColor(255, 192, 0),
                StrokeColor: new CellColor(68, 114, 196),
                MarkerStyle: ChartMarkerStyle.Circle,
                MarkerSize: 8));
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
    public void XlsxAdapter_Save_WritesEmbeddedScatterChartSecondaryAxisPackagePart()
    {
        var workbook = new Workbook("ScatterChartSecondaryPackageSave");
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
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(0.2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(0.25));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(0.3));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Scatter,
            Title = "Dose Response",
            FirstColIsCategories = false,
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1],
            SeriesFormats =
            [
                new ChartSeriesFormat(1, StrokeColor: new CellColor(192, 0, 0))
            ],
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Scatter);
        loadedChart.FirstColIsCategories.Should().BeFalse();
        loadedChart.ShowSecondaryAxis.Should().BeTrue();
        loadedChart.SecondaryAxisSeriesIndexes.Should().Equal(1);
        loadedChart.SeriesFormats.Should().Contain(format =>
            format.SeriesIndex == 1 &&
            format.StrokeColor == new CellColor(192, 0, 0));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedAreaChartPackagePart()
    {
        var workbook = new Workbook("AreaChartPackageSave");
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
            Type = ChartType.Area,
            Title = "Area",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3)),
            Left = 160,
            Top = 120,
            Width = 340,
            Height = 200,
            SeriesFormats =
            [
                new ChartSeriesFormat(0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent6)),
                new ChartSeriesFormat(1, FillColor: new CellColor(110, 160, 90))
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
        loadedChart.Type.Should().Be(ChartType.Area);
        loadedChart.Title.Should().Be("Area");
        loadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 4, 3)));
        loadedChart.Left.Should().BeApproximately(160, 0.01);
        loadedChart.Top.Should().BeApproximately(120, 0.01);
        loadedChart.Width.Should().BeApproximately(340, 0.01);
        loadedChart.Height.Should().BeApproximately(200, 0.01);
        loadedChart.SeriesFormats.Should().Contain(
            new ChartSeriesFormat(0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent6)));
        loadedChart.SeriesFormats.Should().Contain(
            new ChartSeriesFormat(1, FillColor: new CellColor(110, 160, 90)));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedAreaChartWithoutCategoryColumnPackagePart()
    {
        var workbook = new Workbook("AreaChartNoCategoryPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Expenses"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(11));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(14));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Area,
            Title = "Values",
            FirstColIsCategories = false,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Area);
        loadedChart.FirstColIsCategories.Should().BeFalse();
        loadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 4, 2)));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedAreaChartSecondaryAxisPackagePart()
    {
        var workbook = new Workbook("AreaChartSecondaryPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Margin"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(120));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(140));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(0.2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(0.25));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(0.3));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Area,
            Title = "Revenue and Margin",
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1],
            SeriesFormats =
            [
                new ChartSeriesFormat(1, FillColor: new CellColor(192, 0, 0))
            ],
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Area);
        loadedChart.ShowSecondaryAxis.Should().BeTrue();
        loadedChart.SecondaryAxisSeriesIndexes.Should().Equal(1);
        loadedChart.SeriesFormats.Should().Contain(format =>
            format.SeriesIndex == 1 &&
            format.FillColor == new CellColor(192, 0, 0));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedAreaChartSeriesOutlineFormattingPackagePart()
    {
        var workbook = new Workbook("AreaChartSeriesOutlinePackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(120));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(140));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Area,
            Title = "Revenue",
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    FillColor: new CellColor(221, 235, 247),
                    StrokeColor: new CellColor(47, 117, 181),
                    StrokeThickness: 2.25,
                    DashStyle: ChartLineDashStyle.Dot)
            ],
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(
                0,
                FillColor: new CellColor(221, 235, 247),
                StrokeColor: new CellColor(47, 117, 181),
                StrokeThickness: 2.25,
                DashStyle: ChartLineDashStyle.Dot));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedAreaChartComboSecondaryAxisPackagePart()
    {
        var workbook = new Workbook("AreaChartComboSecondaryPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Margin"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(120));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(140));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(0.2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(0.25));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(0.3));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Area,
            Title = "Revenue and Margin",
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1],
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [1],
            SeriesFormats =
            [
                new ChartSeriesFormat(1, StrokeColor: new CellColor(192, 0, 0))
            ],
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Area);
        loadedChart.ShowSecondaryAxis.Should().BeTrue();
        loadedChart.SecondaryAxisSeriesIndexes.Should().Equal(1);
        loadedChart.UseComboLineForSecondarySeries.Should().BeTrue();
        loadedChart.ComboLineSeriesIndexes.Should().Equal(1);
        loadedChart.SeriesFormats.Should().Contain(format =>
            format.SeriesIndex == 1 &&
            format.StrokeColor == new CellColor(192, 0, 0));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedAreaChartComboWithoutCategoryColumnPackagePart()
    {
        var workbook = new Workbook("AreaChartComboNoCategoryPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Margin"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(120));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(140));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(0.2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(0.25));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(0.3));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Area,
            Title = "Revenue and Margin",
            FirstColIsCategories = false,
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1],
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [1],
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Area);
        loadedChart.FirstColIsCategories.Should().BeFalse();
        loadedChart.ShowSecondaryAxis.Should().BeTrue();
        loadedChart.UseComboLineForSecondarySeries.Should().BeTrue();
        loadedChart.ComboLineSeriesIndexes.Should().Equal(1);
        loadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 4, 2)));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedBubbleChartPackagePart()
    {
        var workbook = new Workbook("BubbleChartPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Margin"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Market Size"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(180));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(260));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(12));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(18));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(24));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(40));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(65));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(90));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Bubble,
            Title = "Bubble",
            FirstColIsCategories = false,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3)),
            Left = 170,
            Top = 130,
            Width = 360,
            Height = 210,
            SeriesFormats =
            [
                new ChartSeriesFormat(0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1))
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
        loadedChart.Type.Should().Be(ChartType.Bubble);
        loadedChart.Title.Should().Be("Bubble");
        loadedChart.FirstColIsCategories.Should().BeFalse();
        loadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 4, 3)));
        loadedChart.Left.Should().BeApproximately(170, 0.01);
        loadedChart.Top.Should().BeApproximately(130, 0.01);
        loadedChart.Width.Should().BeApproximately(360, 0.01);
        loadedChart.Height.Should().BeApproximately(210, 0.01);
        loadedChart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1)));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedBubbleChartMultipleSeriesPackagePart()
    {
        var workbook = new Workbook("BubbleChartMultipleSeriesPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Margin A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Size A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Margin B"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("Size B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(180));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(260));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(12));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(18));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(25));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(13));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(9));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new NumberValue(14));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 5), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 5), new NumberValue(10));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Bubble,
            Title = "Bubble",
            FirstColIsCategories = false,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 5)),
            SeriesFormats =
            [
                new ChartSeriesFormat(0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1)),
                new ChartSeriesFormat(1, FillColor: new CellColor(110, 160, 90))
            ]
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Bubble);
        loadedChart.FirstColIsCategories.Should().BeFalse();
        loadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 4, 5)));
        loadedChart.SeriesFormats.Should().Contain(
            new ChartSeriesFormat(0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1)));
        loadedChart.SeriesFormats.Should().Contain(
            new ChartSeriesFormat(1, FillColor: new CellColor(110, 160, 90)));
    }

    [Theory]
    [InlineData(ChartType.Radar, "radarChart", null)]
    [InlineData(ChartType.Stock, "stockChart", null)]
    [InlineData(ChartType.ThreeDLine, "line3DChart", null)]
    [InlineData(ChartType.ThreeDArea, "area3DChart", null)]
    [InlineData(ChartType.ThreeDColumn, "bar3DChart", "col")]
    [InlineData(ChartType.ThreeDBar, "bar3DChart", "bar")]
    public void XlsxAdapter_Save_WritesEmbeddedRadarStockAnd3DChartPackagePart(
        ChartType chartType,
        string expectedElementName,
        string? expectedBarDirection)
    {
        var workbook = new Workbook("RadarStockChartPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Series A"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Series B"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(18));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(27));
        sheet.Charts.Add(new ChartModel
        {
            Type = chartType,
            Title = chartType.ToString(),
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
            XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
            var plotChart = chartXml.Descendants(chartNs + expectedElementName).Should().ContainSingle().Subject;
            if (expectedBarDirection is not null)
                plotChart.Element(chartNs + "barDir")?.Attribute("val")?.Value.Should().Be(expectedBarDirection);
            chartXml.Descendants(chartNs + "ser").Should().HaveCount(2);
        }

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(chartType);
        loadedChart.Title.Should().Be(chartType.ToString());
    }

    [Fact]
    public void XlsxAdapter_Save_WritesVolumeOpenHighLowCloseStockChartPackagePart()
    {
        var workbook = new Workbook("StockVolumeChartPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        string[] headers = ["Date", "Volume", "Open", "High", "Low", "Close"];
        for (var i = 0; i < headers.Length; i++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)i + 1), new TextValue(headers[i]));

        for (uint row = 2; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue($"Day {row - 1}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(1000 + row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(10 + row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 4), new NumberValue(15 + row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 5), new NumberValue(9 + row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 6), new NumberValue(13 + row));
        }

        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Stock,
            StockSubtype = StockChartSubtype.VolumeOpenHighLowClose,
            Title = "OHLCV",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 6)),
            ShowHighLowLines = true,
            ShowUpDownBars = true
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
            XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
            chartXml.Descendants(chartNs + "barChart").Should().ContainSingle();
            chartXml.Descendants(chartNs + "barChart").Descendants(chartNs + "ser").Should().ContainSingle();
            chartXml.Descendants(chartNs + "stockChart").Should().ContainSingle();
            chartXml.Descendants(chartNs + "stockChart").Descendants(chartNs + "ser").Should().HaveCount(4);
            chartXml.Descendants(chartNs + "hiLowLines").Should().ContainSingle();
            chartXml.Descendants(chartNs + "upDownBars").Should().ContainSingle();
        }

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Stock);
        loadedChart.StockSubtype.Should().Be(StockChartSubtype.VolumeOpenHighLowClose);
        loadedChart.DataRange.End.Col.Should().Be(6);
        loadedChart.ShowHighLowLines.Should().BeTrue();
        loadedChart.ShowUpDownBars.Should().BeTrue();
    }

    [Theory]
    [InlineData(ChartType.Pie, "pieChart")]
    [InlineData(ChartType.ThreeDPie, "pie3DChart")]
    [InlineData(ChartType.Doughnut, "doughnutChart")]
    public void XlsxAdapter_Save_WritesEmbeddedPieFamilyChartPackagePart(ChartType chartType, string expectedElementName)
    {
        var workbook = new Workbook("PieChartPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        sheet.Charts.Add(new ChartModel
        {
            Type = chartType,
            Title = chartType.ToString(),
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2)),
            Left = 180,
            Top = 140,
            Width = 300,
            Height = 220,
            DoughnutHoleSize = 0.6,
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
            var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
            XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
            chartXml.Descendants(chartNs + expectedElementName).Should().ContainSingle();
        }

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(chartType);
        loadedChart.Title.Should().Be(chartType.ToString());
        loadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 4, 2)));
        loadedChart.Left.Should().BeApproximately(180, 0.01);
        loadedChart.Top.Should().BeApproximately(140, 0.01);
        loadedChart.Width.Should().BeApproximately(300, 0.01);
        loadedChart.Height.Should().BeApproximately(220, 0.01);
        if (chartType == ChartType.Doughnut)
            loadedChart.DoughnutHoleSize.Should().BeApproximately(0.6, 0.01);
        loadedChart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2)));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedPieChartMultipleSeriesPackagePart()
    {
        var workbook = new Workbook("PieChartMultipleSeriesPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Expenses"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(11));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(14));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Pie,
            Title = "Pie",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3)),
            SeriesFormats =
            [
                new ChartSeriesFormat(0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2)),
                new ChartSeriesFormat(1, FillColor: new CellColor(110, 160, 90))
            ]
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Pie);
        loadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 4, 3)));
        loadedChart.SeriesFormats.Should().Contain(
            new ChartSeriesFormat(0, FillThemeColor: new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2)));
        loadedChart.SeriesFormats.Should().Contain(
            new ChartSeriesFormat(1, FillColor: new CellColor(110, 160, 90)));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedPieChartWithoutCategoryColumnPackagePart()
    {
        var workbook = new Workbook("PieChartNoCategoryPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(30));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Pie,
            Title = "Sales",
            FirstColIsCategories = false,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 1))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedSheet = loaded.GetSheetAt(0);
        var loadedChart = loadedSheet.Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Pie);
        loadedChart.FirstColIsCategories.Should().BeFalse();
        loadedChart.DataRange.Should().Be(new GridRange(
            new CellAddress(loadedSheet.Id, 1, 1),
            new CellAddress(loadedSheet.Id, 4, 1)));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedPieChartRotationAndExplosionPackagePart()
    {
        var workbook = new Workbook("PieChartRotationExplosionPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Share"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(40));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(35));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(25));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Pie,
            Title = "Share",
            FirstSliceAngle = 135,
            ExplodedSliceIndex = 1,
            ExplodedSliceDistance = 0.25,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Pie);
        loadedChart.FirstSliceAngle.Should().Be(135);
        loadedChart.ExplodedSliceIndex.Should().Be(1);
        loadedChart.ExplodedSliceDistance.Should().BeApproximately(0.25, 0.001);
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedChartLegendPositionPackagePart()
    {
        var workbook = new Workbook("ChartLegendPackageSave");
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
            ShowLegend = true,
            LegendPosition = ChartLegendPosition.Top
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.ShowLegend.Should().BeTrue();
        loadedChart.LegendPosition.Should().Be(ChartLegendPosition.Top);
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedChartAreaAndPlotAreaPackageFormatting()
    {
        var workbook = new Workbook("ChartAreaPackageFormatting");
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
            ChartAreaFillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.2),
            PlotAreaFillColor = new CellColor(245, 250, 255),
            PlotAreaBorderThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25),
            PlotAreaBorderThickness = 2.25
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.ChartAreaFillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1, 0.2));
        loadedChart.PlotAreaFillColor.Should().Be(new CellColor(245, 250, 255));
        loadedChart.PlotAreaBorderThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2, -0.25));
        loadedChart.PlotAreaBorderThickness.Should().BeApproximately(2.25, 0.01);
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedChartAxisTitlesPackagePart()
    {
        var workbook = new Workbook("ChartAxisTitlesPackageSave");
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
            XAxisTitle = "Month",
            YAxisTitle = "Amount",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.XAxisTitle.Should().Be("Month");
        loadedChart.YAxisTitle.Should().Be("Amount");
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedScatterChartValueAxesPackagePart()
    {
        var workbook = new Workbook("ScatterChartAxisPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Margin"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(300));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(12));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(28));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Scatter,
            Title = "Scatter",
            XAxisTitle = "Revenue",
            YAxisTitle = "Margin",
            FirstColIsCategories = false,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var chartEntry = archive.GetEntry("xl/charts/chart1.xml");
            chartEntry.Should().NotBeNull();
            using var chartStream = chartEntry.Open();
            var chartXml = XDocument.Load(chartStream);
            XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
            var plotArea = chartXml.Root!
                .Element(chartNs + "chart")!
                .Element(chartNs + "plotArea")!;

            plotArea.Elements(chartNs + "catAx").Should().BeEmpty();
            plotArea.Elements(chartNs + "valAx").Should().HaveCount(2);
            plotArea.Element(chartNs + "scatterChart")!
                .Elements(chartNs + "axId")
                .Should()
                .HaveCount(2);
        }

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.XAxisTitle.Should().Be("Revenue");
        loadedChart.YAxisTitle.Should().Be("Margin");
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedChartValueAxisScalePackagePart()
    {
        var workbook = new Workbook("ChartValueAxisScalePackageSave");
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
            YAxisMinimum = 1,
            YAxisMaximum = 100,
            YAxisMajorUnit = 10,
            YAxisMinorUnit = 5,
            YAxisLogScale = true,
            YAxisNumberFormat = ChartDataLabelNumberFormat.Currency,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.YAxisMinimum.Should().Be(1);
        loadedChart.YAxisMaximum.Should().Be(100);
        loadedChart.YAxisMajorUnit.Should().Be(10);
        loadedChart.YAxisMinorUnit.Should().Be(5);
        loadedChart.YAxisLogScale.Should().BeTrue();
        loadedChart.YAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.Currency);
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedChartValueAxisGridlinesPackagePart()
    {
        var workbook = new Workbook("ChartValueAxisGridlinesPackageSave");
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
            ShowYAxisMajorGridlines = true,
            ShowYAxisMinorGridlines = true,
            YAxisMajorGridlineColor = new CellColor(190, 190, 190),
            YAxisMinorGridlineColor = new CellColor(225, 225, 225),
            YAxisGridlineThickness = 2,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.ShowYAxisMajorGridlines.Should().BeTrue();
        loadedChart.ShowYAxisMinorGridlines.Should().BeTrue();
        loadedChart.YAxisMajorGridlineColor.Should().Be(new CellColor(190, 190, 190));
        loadedChart.YAxisMinorGridlineColor.Should().Be(new CellColor(225, 225, 225));
        loadedChart.YAxisGridlineThickness.Should().Be(2);
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedScatterChartXAxisGridlinesPackagePart()
    {
        var workbook = new Workbook("ScatterChartXAxisGridlinesPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Margin"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(200));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new NumberValue(300));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(12));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(28));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Scatter,
            Title = "Scatter",
            FirstColIsCategories = false,
            ShowXAxisMajorGridlines = true,
            ShowXAxisMinorGridlines = true,
            XAxisMajorGridlineColor = new CellColor(200, 200, 200),
            XAxisMinorGridlineColor = new CellColor(230, 230, 230),
            XAxisGridlineThickness = 1.5,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.ShowXAxisMajorGridlines.Should().BeTrue();
        loadedChart.ShowXAxisMinorGridlines.Should().BeTrue();
        loadedChart.XAxisMajorGridlineColor.Should().Be(new CellColor(200, 200, 200));
        loadedChart.XAxisMinorGridlineColor.Should().Be(new CellColor(230, 230, 230));
        loadedChart.XAxisGridlineThickness.Should().Be(1.5);
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedChartAxisTickMarksAndLinePackagePart()
    {
        var workbook = new Workbook("ChartAxisLinePackageSave");
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
            XAxisMajorTickStyle = ChartAxisTickStyle.Inside,
            XAxisMinorTickStyle = ChartAxisTickStyle.Cross,
            XAxisLineColor = new CellColor(10, 20, 30),
            XAxisLineThickness = 2.5,
            YAxisMajorTickStyle = ChartAxisTickStyle.Cross,
            YAxisMinorTickStyle = ChartAxisTickStyle.None,
            YAxisLineColor = new CellColor(40, 50, 60),
            YAxisLineThickness = 3.5,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.XAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Inside);
        loadedChart.XAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.Cross);
        loadedChart.XAxisLineColor.Should().Be(new CellColor(10, 20, 30));
        loadedChart.XAxisLineThickness.Should().Be(2.5);
        loadedChart.YAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Cross);
        loadedChart.YAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        loadedChart.YAxisLineColor.Should().Be(new CellColor(40, 50, 60));
        loadedChart.YAxisLineThickness.Should().Be(3.5);
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedChartAxisLabelVisibilityPackagePart()
    {
        var workbook = new Workbook("ChartAxisLabelVisibilityPackageSave");
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
            ShowXAxisLabels = false,
            ShowYAxisLabels = false,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.ShowXAxisLabels.Should().BeFalse();
        loadedChart.ShowYAxisLabels.Should().BeFalse();
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedChartAxisTitleFormattingPackagePart()
    {
        var workbook = new Workbook("ChartAxisTitleFormattingPackageSave");
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
            XAxisTitle = "Month",
            YAxisTitle = "Amount",
            AxisTitleTextColor = new CellColor(89, 89, 89),
            AxisTitleFontSize = 14,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.XAxisTitle.Should().Be("Month");
        loadedChart.YAxisTitle.Should().Be("Amount");
        loadedChart.AxisTitleTextColor.Should().Be(new CellColor(89, 89, 89));
        loadedChart.AxisTitleFontSize.Should().Be(14);
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedChartTitleFormattingPackagePart()
    {
        var workbook = new Workbook("ChartTitleFormattingPackageSave");
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
            ChartTitleTextColor = new CellColor(31, 78, 121),
            ChartTitleFontSize = 18,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.Title.Should().Be("Sales");
        loadedChart.ChartTitleTextColor.Should().Be(new CellColor(31, 78, 121));
        loadedChart.ChartTitleFontSize.Should().Be(18);
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedChartLegendFormattingPackagePart()
    {
        var workbook = new Workbook("ChartLegendFormattingPackageSave");
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
            ShowLegend = true,
            LegendPosition = ChartLegendPosition.Bottom,
            LegendOverlay = true,
            LegendTextColor = new CellColor(40, 40, 40),
            LegendFillColor = new CellColor(248, 248, 248),
            LegendBorderColor = new CellColor(180, 180, 180),
            LegendBorderThickness = 1.25,
            LegendFontSize = 11,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.ShowLegend.Should().BeTrue();
        loadedChart.LegendPosition.Should().Be(ChartLegendPosition.Bottom);
        loadedChart.LegendOverlay.Should().BeTrue();
        loadedChart.LegendTextColor.Should().Be(new CellColor(40, 40, 40));
        loadedChart.LegendFillColor.Should().Be(new CellColor(248, 248, 248));
        loadedChart.LegendBorderColor.Should().Be(new CellColor(180, 180, 180));
        loadedChart.LegendBorderThickness.Should().Be(1.25);
        loadedChart.LegendFontSize.Should().Be(11);
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedChartLegendThemeTextPackagePart()
    {
        var workbook = new Workbook("ChartLegendThemeTextPackageSave");
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
            ShowLegend = true,
            LegendTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1),
            LegendFontSize = 10,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.LegendTextThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1));
        loadedChart.LegendTextColor.Should().BeNull();
        loadedChart.LegendFontSize.Should().Be(10);
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedChartDataLabelPackagePart()
    {
        var workbook = new Workbook("ChartDataLabelPackageSave");
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
            ShowDataLabels = true,
            DataLabelPosition = ChartDataLabelPosition.OutsideEnd,
            ShowDataLabelCategoryName = true,
            ShowDataLabelSeriesName = true,
            ShowDataLabelPercentage = true,
            DataLabelSeparator = ChartDataLabelSeparator.NewLine,
            DataLabelNumberFormat = ChartDataLabelNumberFormat.Currency,
            ShowDataLabelCallouts = true,
            DataLabelFillColor = new CellColor(255, 255, 225),
            DataLabelBorderColor = new CellColor(128, 128, 128),
            DataLabelBorderThickness = 1.5,
            DataLabelTextColor = new CellColor(30, 30, 30),
            DataLabelFontSize = 13,
            DataLabelAngle = -35,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var chartEntry = archive.GetEntry("xl/charts/chart1.xml");
            chartEntry.Should().NotBeNull();
            var chartXml = LoadPackageXml(chartEntry!);
            XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
            chartXml.Descendants(chartNs + "showPercent")
                .Should()
                .ContainSingle()
                .Which.Attribute("val")?.Value.Should().Be("0");
        }

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.ShowDataLabels.Should().BeTrue();
        loadedChart.DataLabelPosition.Should().Be(ChartDataLabelPosition.OutsideEnd);
        loadedChart.ShowDataLabelCategoryName.Should().BeTrue();
        loadedChart.ShowDataLabelSeriesName.Should().BeTrue();
        loadedChart.ShowDataLabelPercentage.Should().BeFalse();
        loadedChart.DataLabelSeparator.Should().Be(ChartDataLabelSeparator.NewLine);
        loadedChart.DataLabelNumberFormat.Should().Be(ChartDataLabelNumberFormat.Currency);
        loadedChart.ShowDataLabelCallouts.Should().BeTrue();
        loadedChart.DataLabelFillColor.Should().Be(new CellColor(255, 255, 225));
        loadedChart.DataLabelBorderColor.Should().Be(new CellColor(128, 128, 128));
        loadedChart.DataLabelBorderThickness.Should().Be(1.5);
        loadedChart.DataLabelTextColor.Should().Be(new CellColor(30, 30, 30));
        loadedChart.DataLabelFontSize.Should().Be(13);
        loadedChart.DataLabelAngle.Should().Be(-35);
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedChartPointDataLabelPackagePart()
    {
        var workbook = new Workbook("ChartPointDataLabelPackageSave");
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
            ShowDataLabels = true,
            DataLabelFillColor = new CellColor(255, 255, 255),
            PointDataLabelFormats =
            [
                new ChartPointDataLabelFormat(
                    0,
                    1,
                    FillColor: new CellColor(226, 239, 218),
                    BorderColor: new CellColor(112, 173, 71),
                    BorderThickness: 2,
                    TextColor: new CellColor(0, 97, 0),
                    FontSize: 14)
            ],
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.ShowDataLabels.Should().BeTrue();
        loadedChart.PointDataLabelFormats.Should().ContainSingle().Which.Should().Be(
            new ChartPointDataLabelFormat(
                0,
                1,
                FillColor: new CellColor(226, 239, 218),
                BorderColor: new CellColor(112, 173, 71),
                BorderThickness: 2,
                TextColor: new CellColor(0, 97, 0),
                FontSize: 14));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedChartTrendlinePackagePart()
    {
        var workbook = new Workbook("ChartTrendlinePackageSave");
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
            ShowLinearTrendline = true,
            TrendlineType = ChartTrendlineType.Polynomial,
            TrendlineOrder = 4,
            ShowTrendlineEquation = true,
            ShowTrendlineRSquared = true,
            TrendlineColor = new CellColor(217, 83, 25),
            TrendlineThickness = 2.5,
            TrendlineDashStyle = ChartLineDashStyle.Dot,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.ShowLinearTrendline.Should().BeTrue();
        loadedChart.TrendlineType.Should().Be(ChartTrendlineType.Polynomial);
        loadedChart.TrendlineOrder.Should().Be(4);
        loadedChart.ShowTrendlineEquation.Should().BeTrue();
        loadedChart.ShowTrendlineRSquared.Should().BeTrue();
        loadedChart.TrendlineColor.Should().Be(new CellColor(217, 83, 25));
        loadedChart.TrendlineThickness.Should().Be(2.5);
        loadedChart.TrendlineDashStyle.Should().Be(ChartLineDashStyle.Dot);
    }

    [Fact]
    public void XlsxAdapter_Save_ClampsEmbeddedChartTrendlineThicknessPackagePart()
    {
        var workbook = new Workbook("ChartTrendlineThicknessPackageSave");
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
            ShowLinearTrendline = true,
            TrendlineThickness = 25,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        chartXml.Descendants(drawingNs + "ln")
            .Single(line => line.Parent?.Name.LocalName == "spPr" && line.Parent.Parent?.Name.LocalName == "trendline")
            .Attribute("w")!
            .Value.Should().Be("127000");
    }

    [Fact]
    public void XlsxAdapter_Save_ClampsEmbeddedChartDataLabelBorderThicknessPackagePart()
    {
        var workbook = new Workbook("ChartDataLabelBorderThicknessPackageSave");
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
            ShowDataLabels = true,
            DataLabelBorderThickness = 25,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        chartXml.Descendants(drawingNs + "ln")
            .Single(line => line.Parent?.Name.LocalName == "spPr" && line.Parent.Parent?.Name.LocalName == "dLbls")
            .Attribute("w")!
            .Value.Should().Be("127000");
    }

    [Fact]
    public void XlsxAdapter_Save_ClampsEmbeddedChartAxisLineThicknessPackagePart()
    {
        var workbook = new Workbook("ChartAxisLineThicknessPackageSave");
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
            XAxisLineColor = new CellColor(10, 20, 30),
            XAxisLineThickness = 0,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        chartXml.Descendants(chartNs + "catAx")
            .Single()
            .Element(chartNs + "spPr")!
            .Element(drawingNs + "ln")!
            .Attribute("w")!
            .Value.Should().Be("6350");
    }

    [Fact]
    public void XlsxAdapter_Save_ClampsEmbeddedChartGridlineThicknessPackagePart()
    {
        var workbook = new Workbook("ChartGridlineThicknessPackageSave");
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
            ShowYAxisMajorGridlines = true,
            YAxisMajorGridlineColor = new CellColor(10, 20, 30),
            YAxisGridlineThickness = 0,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        chartXml.Descendants(chartNs + "majorGridlines")
            .Single()
            .Element(chartNs + "spPr")!
            .Element(drawingNs + "ln")!
            .Attribute("w")!
            .Value.Should().Be("3175");
    }

    [Fact]
    public void XlsxAdapter_Save_SanitizesEmbeddedChartAxisNumericStatePackagePart()
    {
        var workbook = new Workbook("ChartAxisNumericPackageSave");
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
            YAxisMinimum = double.NaN,
            YAxisMaximum = double.PositiveInfinity,
            YAxisMajorUnit = -5,
            YAxisMinorUnit = double.NaN,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

        var valueAxis = chartXml.Descendants(chartNs + "valAx").Single();
        valueAxis.Descendants(chartNs + "min").Should().BeEmpty();
        valueAxis.Descendants(chartNs + "max").Should().BeEmpty();
        valueAxis.Element(chartNs + "majorUnit")!.Attribute("val")!.Value.Should().Be(double.Epsilon.ToString(CultureInfo.InvariantCulture));
        valueAxis.Element(chartNs + "minorUnit").Should().BeNull();
    }

    [Fact]
    public void XlsxAdapter_Save_DropsOutOfRangePointDataLabelFormattingPackagePart()
    {
        var workbook = new Workbook("ChartPointDataLabelPackageSave");
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
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2)),
            PointDataLabelFormats =
            [
                new ChartPointDataLabelFormat(0, -1, FillColor: new CellColor(10, 20, 30)),
                new ChartPointDataLabelFormat(0, 1, FillColor: new CellColor(40, 50, 60)),
                new ChartPointDataLabelFormat(0, 99, FillColor: new CellColor(70, 80, 90))
            ]
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

        chartXml.Descendants(chartNs + "dLbl")
            .Select(label => label.Element(chartNs + "idx")!.Attribute("val")!.Value)
            .Should().Equal("1");
    }

    [Fact]
    public void XlsxAdapter_Save_UsesLastDuplicateSeriesFormattingPackagePart()
    {
        var workbook = new Workbook("ChartDuplicateSeriesFormatPackageSave");
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
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2)),
            SeriesFormats =
            [
                new ChartSeriesFormat(0, FillColor: new CellColor(255, 0, 0)),
                new ChartSeriesFormat(0, FillColor: new CellColor(0, 176, 80))
            ]
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";

        chartXml.Descendants(chartNs + "ser")
            .Single()
            .Element(chartNs + "spPr")!
            .Descendants(drawingNs + "srgbClr")
            .Single()
            .Attribute("val")!
            .Value.Should().Be("00B050");
    }

    [Fact]
    public void XlsxAdapter_Save_DefaultsInvalidEmbeddedChartTypeToColumnPackagePart()
    {
        var workbook = new Workbook("ChartInvalidTypePackageSave");
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
            Type = (ChartType)999,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2))
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartEntry = archive.GetEntry("xl/charts/chart1.xml");
        chartEntry.Should().NotBeNull();
        var chartXml = LoadPackageXml(chartEntry!);
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

        var barChart = chartXml.Descendants(chartNs + "barChart").Single();
        barChart.Element(chartNs + "barDir")!.Attribute("val")!.Value.Should().Be("col");
        barChart.Element(chartNs + "grouping")!.Attribute("val")!.Value.Should().Be("clustered");
    }

    [Fact]
    public void XlsxAdapter_Save_DropsInvalidSeriesFormattingChoicesPackagePart()
    {
        var workbook = new Workbook("ChartInvalidSeriesChoicesPackageSave");
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
            Type = ChartType.Line,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2)),
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    DashStyle: (ChartLineDashStyle)999,
                    MarkerStyle: (ChartMarkerStyle)999)
            ]
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

        chartXml.Descendants(drawingNs + "prstDash").Should().BeEmpty();
        chartXml.Descendants(chartNs + "marker")
            .SelectMany(marker => marker.Elements(chartNs + "symbol"))
            .Should().BeEmpty();
    }

    [Fact]
    public void XlsxAdapter_Save_DropsEmptyPointDataLabelFormattingPackagePart()
    {
        var workbook = new Workbook("ChartEmptyPointDataLabelPackageSave");
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
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2)),
            PointDataLabelFormats =
            [
                new ChartPointDataLabelFormat(0, 1)
            ]
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

        chartXml.Descendants(chartNs + "dLbl").Should().BeEmpty();
    }

    [Fact]
    public void XlsxAdapter_Save_WritesBarChartSpacingAndVaryColors()
    {
        var workbook = new Workbook("ChartBarSpacingPackageSave");
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
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2)),
            VaryColorsByPoint = true,
            BarOverlap = -20,
            BarGapWidth = 75
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        var barChart = chartXml.Descendants(chartNs + "barChart").Single();

        barChart.Element(chartNs + "varyColors")!.Attribute("val")!.Value.Should().Be("1");
        barChart.Element(chartNs + "overlap")!.Attribute("val")!.Value.Should().Be("-20");
        barChart.Element(chartNs + "gapWidth")!.Attribute("val")!.Value.Should().Be("75");
    }

    [Fact]
    public void XlsxAdapter_Save_WritesChartDataTableMetadata()
    {
        var workbook = new Workbook("ChartDataTablePackageSave");
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
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2)),
            DataTable = new ChartDataTableModel
            {
                ShowHorizontalBorder = true,
                ShowVerticalBorder = false,
                ShowOutline = true,
                ShowLegendKeys = true
            }
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        var dataTable = chartXml.Descendants(chartNs + "dTable").Single();

        dataTable.Element(chartNs + "showHorzBorder")!.Attribute("val")!.Value.Should().Be("1");
        dataTable.Element(chartNs + "showVertBorder")!.Attribute("val")!.Value.Should().Be("0");
        dataTable.Element(chartNs + "showOutline")!.Attribute("val")!.Value.Should().Be("1");
        dataTable.Element(chartNs + "showKeys")!.Attribute("val")!.Value.Should().Be("1");
    }

    [Fact]
    public void XlsxAdapter_Save_WritesChartErrorBarMetadata()
    {
        var workbook = new Workbook("ChartErrorBarsPackageSave");
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
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2)),
            ShowErrorBars = true,
            ErrorBarKind = ChartErrorBarKind.Percentage,
            ErrorBarDirection = ChartErrorBarDirection.Plus,
            ErrorBarValue = 12.5,
            ErrorBarEndCaps = false
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        var errorBars = chartXml.Descendants(chartNs + "errBars").Single();

        errorBars.Element(chartNs + "errBarType")!.Attribute("val")!.Value.Should().Be("plus");
        errorBars.Element(chartNs + "errValType")!.Attribute("val")!.Value.Should().Be("percentage");
        errorBars.Element(chartNs + "noEndCap")!.Attribute("val")!.Value.Should().Be("1");
        errorBars.Element(chartNs + "val")!.Attribute("val")!.Value.Should().Be("12.5");
    }

    [Fact]
    public void XlsxAdapter_Save_WritesLineChartGuideMetadata()
    {
        var workbook = new Workbook("ChartLineGuidesPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
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
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3)),
            ShowDropLines = true,
            ShowHighLowLines = true,
            ShowUpDownBars = true
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        var lineChart = chartXml.Descendants(chartNs + "lineChart").Single();

        lineChart.Element(chartNs + "dropLines").Should().NotBeNull();
        lineChart.Element(chartNs + "hiLowLines").Should().NotBeNull();
        lineChart.Element(chartNs + "upDownBars").Should().NotBeNull();
    }

    [Fact]
    public void XlsxAdapter_Save_DropsUnsupportedComboSeriesMarkersPackagePart()
    {
        var workbook = new Workbook("ChartUnsupportedComboMarkerPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Margin"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(120));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(140));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(0.2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(0.25));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(0.3));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [1],
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3)),
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    1,
                    StrokeColor: new CellColor(192, 0, 0),
                    MarkerStyle: ChartMarkerStyle.Circle,
                    MarkerSize: 7)
            ]
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";

        chartXml.Descendants(chartNs + "marker").Should().BeEmpty();
    }

    [Fact]
    public void XlsxAdapter_Save_SkipsEmbeddedChartWithoutDataSeriesPackagePart()
    {
        var workbook = new Workbook("ChartNoSeriesPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            FirstColIsCategories = true,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 1))
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("xl/charts/chart1.xml").Should().BeNull();
        archive.GetEntry("xl/drawings/drawing1.xml").Should().BeNull();
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedColumnChartSecondaryAxisPackagePart()
    {
        var workbook = new Workbook("ChartSecondaryPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Margin"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(120));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(140));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(0.2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(0.25));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(0.3));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            Title = "Sales and Margin",
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1],
            SeriesFormats =
            [
                new ChartSeriesFormat(1, FillColor: new CellColor(192, 0, 0))
            ],
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Column);
        loadedChart.ShowSecondaryAxis.Should().BeTrue();
        loadedChart.SecondaryAxisSeriesIndexes.Should().Equal(1);
        loadedChart.SeriesFormats.Should().Contain(format =>
            format.SeriesIndex == 1 &&
            format.FillColor == new CellColor(192, 0, 0));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedColumnChartComboSecondaryAxisPackagePart()
    {
        var workbook = new Workbook("ChartComboSecondaryPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Margin"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(120));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(140));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(0.2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(0.25));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(0.3));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            Title = "Sales and Margin",
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1],
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [1],
            SeriesFormats =
            [
                new ChartSeriesFormat(1, StrokeColor: new CellColor(192, 0, 0))
            ],
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Column);
        loadedChart.ShowSecondaryAxis.Should().BeTrue();
        loadedChart.SecondaryAxisSeriesIndexes.Should().Equal(1);
        loadedChart.UseComboLineForSecondarySeries.Should().BeTrue();
        loadedChart.ComboLineSeriesIndexes.Should().Equal(1);
        loadedChart.SeriesFormats.Should().Contain(format =>
            format.SeriesIndex == 1 &&
            format.StrokeColor == new CellColor(192, 0, 0));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedColumnChartPrimaryAndSecondaryComboLinePackageParts()
    {
        var workbook = new Workbook("ChartMultiComboLinePackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Units"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Margin"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(120));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(140));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(80));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(90));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(95));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new NumberValue(0.2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new NumberValue(0.25));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new NumberValue(0.3));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            Title = "Sales, Units, and Margin",
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [2],
            UseComboLineForSecondarySeries = true,
            ComboLineSeriesIndexes = [1, 2],
            SeriesFormats =
            [
                new ChartSeriesFormat(1, StrokeColor: new CellColor(68, 114, 196)),
                new ChartSeriesFormat(2, StrokeColor: new CellColor(192, 0, 0))
            ],
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 4))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Column);
        loadedChart.ShowSecondaryAxis.Should().BeTrue();
        loadedChart.SecondaryAxisSeriesIndexes.Should().Equal(2);
        loadedChart.UseComboLineForSecondarySeries.Should().BeTrue();
        loadedChart.ComboLineSeriesIndexes.Should().Equal(1, 2);
        loadedChart.SeriesFormats.Should().Contain(format =>
            format.SeriesIndex == 1 &&
            format.StrokeColor == new CellColor(68, 114, 196));
        loadedChart.SeriesFormats.Should().Contain(format =>
            format.SeriesIndex == 2 &&
            format.StrokeColor == new CellColor(192, 0, 0));
    }

    [Fact]
    public void XlsxAdapter_Save_WritesEmbeddedLineChartSecondaryAxisPackagePart()
    {
        var workbook = new Workbook("LineChartSecondaryPackageSave");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Margin"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(120));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(140));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new NumberValue(0.2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new NumberValue(0.25));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new NumberValue(0.3));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Line,
            Title = "Revenue and Margin",
            ShowSecondaryAxis = true,
            SecondaryAxisSeriesIndexes = [1],
            SeriesFormats =
            [
                new ChartSeriesFormat(1, StrokeColor: new CellColor(192, 0, 0))
            ],
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3))
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var loaded = adapter.Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Subject;
        loadedChart.Type.Should().Be(ChartType.Line);
        loadedChart.ShowSecondaryAxis.Should().BeTrue();
        loadedChart.SecondaryAxisSeriesIndexes.Should().Equal(1);
        loadedChart.SeriesFormats.Should().Contain(format =>
            format.SeriesIndex == 1 &&
            format.StrokeColor == new CellColor(192, 0, 0));
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesUnknownPackagePartsAlongsideModelEdits()
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
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 1, 1), new TextValue("edited"));
        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("customXml/item1.xml").Should().NotBeNull(
            "XLSX files opened from Excel should retain unsupported package parts when Freexcel saves modeled edits");

        saved.Position = 0;
        var roundTripped = adapter.Load(saved);
        roundTripped.GetSheetAt(0).GetValue(1, 1).Should().Be(new TextValue("edited"));
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesUnsupportedChartsheetReferenceAlongsideModelEdits()
    {
        var workbook = new Workbook("UnsupportedSheetTypeRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kept"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalChartsheetPackage(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 1, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("xl/chartsheets/sheet1.xml").Should().NotBeNull();

        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        workbookXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("ChartSheet1");

        var workbookRelsXml = LoadPackageXml(archive.GetEntry("xl/_rels/workbook.xml.rels")!);
        workbookRelsXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("chartsheets/sheet1.xml");

        var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
        contentTypesXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("/xl/chartsheets/sheet1.xml");

        saved.Position = 0;
        var roundTripped = adapter.Load(saved);
        roundTripped.GetSheetAt(0).GetValue(1, 1).Should().Be(new TextValue("edited"));
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesUnsupportedDialogsheetReferenceAlongsideModelEdits()
    {
        var workbook = new Workbook("UnsupportedDialogSheetRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kept"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalDialogsheetPackage(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 1, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("xl/dialogSheets/sheet1.xml").Should().NotBeNull();

        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        workbookXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("DialogSheet1");

        var workbookRelsXml = LoadPackageXml(archive.GetEntry("xl/_rels/workbook.xml.rels")!);
        workbookRelsXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("dialogSheets/sheet1.xml");

        var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
        contentTypesXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("/xl/dialogSheets/sheet1.xml");

        saved.Position = 0;
        var roundTripped = adapter.Load(saved);
        roundTripped.GetSheetAt(0).GetValue(1, 1).Should().Be(new TextValue("edited"));
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetQueryTableReferencesAlongsideModelEdits()
    {
        var workbook = new Workbook("QueryTableRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kept"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalQueryTablePackage(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 1, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("xl/queryTables/queryTable1.xml").Should().NotBeNull();

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        worksheetXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("queryTableParts");

        var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        worksheetRelsXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("../queryTables/queryTable1.xml");

        var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
        contentTypesXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("/xl/queryTables/queryTable1.xml");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetWebPublishItemsAlongsideModelEdits()
    {
        var workbook = new Workbook("WebPublishItemsRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kept"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalWorksheetWebPublishItemsPackage(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 1, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("xl/webPublishItems.xml").Should().NotBeNull();

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        worksheetXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("webPublishItems");
        worksheetXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("rIdFreexcelWebPublishItems");

        var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        worksheetRelsXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("../webPublishItems.xml");

        var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
        contentTypesXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("/xl/webPublishItems.xml");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetOleObjectsAlongsideModelEdits()
    {
        var workbook = new Workbook("OleObjectRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kept"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalWorksheetOleObjectPackage(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 1, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("xl/embeddings/oleObject1.bin").Should().NotBeNull();

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        worksheetXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("oleObjects");
        worksheetXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("rIdFreexcelOleObject");

        var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        worksheetRelsXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("../embeddings/oleObject1.bin");

        var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
        contentTypesXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("/xl/embeddings/oleObject1.bin");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetControlsAlongsideModelEdits()
    {
        var workbook = new Workbook("ControlRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kept"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalWorksheetControlPackage(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 1, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("xl/ctrlProps/ctrlProp1.xml").Should().NotBeNull();

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        worksheetXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("controls");
        worksheetXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("rIdFreexcelControl");

        var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        worksheetRelsXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("../ctrlProps/ctrlProp1.xml");

        var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
        contentTypesXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("/xl/ctrlProps/ctrlProp1.xml");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetLegacyDrawingAlongsideModelEdits()
    {
        var workbook = new Workbook("LegacyDrawingRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kept"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalWorksheetLegacyDrawingPackage(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 1, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("xl/drawings/vmlDrawing1.vml").Should().NotBeNull();

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        worksheetXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("legacyDrawing");
        worksheetXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("rIdFreexcelLegacyDrawing");

        var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        worksheetRelsXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("../drawings/vmlDrawing1.vml");

        var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
        contentTypesXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("/xl/drawings/vmlDrawing1.vml");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesExternalWorksheetPictureReferenceAlongsideModelEdits()
    {
        var workbook = new Workbook("ExternalPictureRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kept"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddExternalWorksheetPictureReference(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 1, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        worksheetXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("picture");
        worksheetXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("rIdFreexcelExternalPicture");

        var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        var worksheetRelsText = worksheetRelsXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        worksheetRelsText.Should().Contain("https://example.invalid/background.png");
        worksheetRelsText.Should().Contain("TargetMode=\"External\"");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetSingleXmlCellsAlongsideModelEdits()
    {
        var workbook = new Workbook("SingleXmlCellsRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kept"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetSingleXmlCells(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 1, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        worksheetXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("singleXmlCells");
        worksheetXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("xmlCellPrId=\"1\"");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesUnsupportedChartDrawingReferencesAlongsideModelEdits()
    {
        var workbook = new Workbook("UnsupportedChartRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalColumnChartPackage(source, chartXml: MinimalUnsupportedRadarChartXml);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 3, 1), new TextValue("B"));
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 3, 2), new NumberValue(20));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("xl/drawings/drawing1.xml").Should().NotBeNull();
        archive.GetEntry("xl/drawings/_rels/drawing1.xml.rels").Should().NotBeNull();
        archive.GetEntry("xl/charts/chart1.xml").Should().NotBeNull();

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        worksheetXml.ToString().Should().Contain("drawing");

        var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        worksheetRelsXml.ToString().Should().Contain("../drawings/drawing1.xml");

        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        chartXml.ToString().Should().Contain("radarChart");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesUnknownConditionalFormattingAlongsideModelEdits()
    {
        var workbook = new Workbook("UnknownCfRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("urgent"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddUnknownConditionalFormatting(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 3, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        worksheetXml.ToString().Should().Contain("freexcelFutureRule");
        worksheetXml.ToString().Should().Contain("UNKNOWN_CF_SENTINEL");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesUnsupportedDrawingAnchorsAlongsideAuthoredShapes()
    {
        var workbook = new Workbook("UnsupportedDrawingRetention");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Native drawing"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddUnsupportedDrawingPackage(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Anchor = new CellAddress(loadedSheet.Id, 4, 2),
            Kind = DrawingShapeKind.Rectangle,
            Width = 120,
            Height = 60,
            FillColor = new CellColor(220, 235, 247),
            OutlineColor = new CellColor(45, 90, 135)
        });

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var drawingXml = LoadPackageXml(archive.GetEntry("xl/drawings/drawing1.xml")!);
        var drawingText = drawingXml.ToString();
        drawingText.Should().Contain("cxnSp");
        drawingText.Should().Contain("grpSp");
        drawingText.Should().Contain("Native connector");
        drawingText.Should().Contain("Native group");
        drawingText.Should().Contain("Shape 1");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_LoadsAndPreservesExternalLinkMetadata()
    {
        var workbook = new Workbook("ExternalLinksRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Linked"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalExternalLinkPackage(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.ExternalLinks.Should().ContainSingle(link =>
            link.PackagePart == "xl/externalLinks/externalLink1.xml" &&
            link.TargetUri == "linked-workbook.xlsx");
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("xl/externalLinks/externalLink1.xml").Should().NotBeNull();
        archive.GetEntry("xl/externalLinks/_rels/externalLink1.xml.rels").Should().NotBeNull();
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        workbookXml.ToString().Should().Contain("externalReferences");
        var workbookRelsXml = LoadPackageXml(archive.GetEntry("xl/_rels/workbook.xml.rels")!);
        workbookRelsXml.ToString().Should().Contain("externalLink1.xml");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesCalcChainPackagePartAlongsideModelEdits()
    {
        var workbook = new Workbook("CalcChainRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "1+1");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalCalcChainPackage(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("xl/calcChain.xml").Should().NotBeNull();
        var workbookRelsXml = LoadPackageXml(archive.GetEntry("xl/_rels/workbook.xml.rels")!);
        workbookRelsXml.ToString().Should().Contain("calcChain.xml");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesUnknownWorkbookExtensionListEntries()
    {
        var workbook = new Workbook("WorkbookExtensionRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("workbook metadata"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalWorkbookExtensionList(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        workbookXml.ToString().Should().Contain("{00112233-4455-6677-8899-AABBCCDDEEFF}");
        workbookXml.ToString().Should().Contain("FreexcelUnknownWorkbookExtension");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorkbookWebPublishObjects()
    {
        var workbook = new Workbook("WorkbookWebPublishRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("web publish metadata"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalWorkbookWebPublishObjects(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        workbookXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("webPublishObjects");
        workbookXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("FreexcelWebPublish");
        workbookXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("destinationFile=\"https://example.invalid/report.htm\"");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorkbookWebPublishingSettings()
    {
        var workbook = new Workbook("WorkbookWebPublishingSettingsRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("web publishing settings"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalWorkbookWebPublishingSettings(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        workbookXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("webPublishing");
        workbookXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("css=\"1\"");
        workbookXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("targetScreenSize=\"800x600\"");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorkbookRevisionPointer()
    {
        var workbook = new Workbook("WorkbookRevisionPointerRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("revision pointer"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalWorkbookRevisionPointer(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("xl/revisionHeaders/revisionHeader1.xml").Should().NotBeNull();
        archive.GetEntry("xl/revisions/revisionLog1.xml").Should().NotBeNull();

        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        workbookXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("revisionPtr");
        workbookXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("documentId=\"FreexcelRevisionDoc\"");

        var workbookRelsXml = LoadPackageXml(archive.GetEntry("xl/_rels/workbook.xml.rels")!);
        workbookRelsXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("revisionHeaders/revisionHeader1.xml");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorkbookOleSize()
    {
        var workbook = new Workbook("WorkbookOleSizeRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("ole size"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalWorkbookOleSize(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        workbookXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("oleSize");
        workbookXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("ref=\"A1:D12\"");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesUnsupportedDefinedNames()
    {
        var workbook = new Workbook("DefinedNameRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddUnsupportedDefinedName(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        workbookXml.ToString().Should().Contain("DynamicSalesRange");
        workbookXml.ToString().Should().Contain("hidden=\"1\"");
        workbookXml.ToString().Should().Contain("1+1");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesAdditionalWorkbookViews()
    {
        var workbook = new Workbook("WorkbookViewsRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("view"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddAdditionalWorkbookView(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var views = workbookXml.Root!.Element(workbookNs + "bookViews")!.Elements(workbookNs + "workbookView").ToList();
        views.Should().HaveCount(2);
        views.Any(view =>
            string.Equals(view.Attribute("visibility")?.Value, "hidden", StringComparison.Ordinal) &&
            string.Equals(view.Attribute("tabRatio")?.Value, "700", StringComparison.Ordinal))
            .Should().BeTrue();
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesPrimaryWorkbookViewNativeMetadata()
    {
        var workbook = new Workbook("PrimaryWorkbookViewRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("primary view"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddPrimaryWorkbookViewNativeMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var views = workbookXml.Root!.Element(workbookNs + "bookViews")!.Elements(workbookNs + "workbookView").ToList();
        views.Should().ContainSingle();
        var primaryView = views.Single();
        primaryView.Attribute("visibility").Should().NotBeNull();
        primaryView.Attribute("visibility")!.Value.Should().Be("visible");
        primaryView.Attribute("showSheetTabs").Should().NotBeNull();
        primaryView.Attribute("showSheetTabs")!.Value.Should().Be("0");
        primaryView.Attribute("tabRatio").Should().NotBeNull();
        primaryView.Attribute("tabRatio")!.Value.Should().Be("700");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_MergesPrimaryWorkbookViewMetadataAlongsideAdditionalViews()
    {
        var workbook = new Workbook("PrimaryWorkbookViewWithAdditionalViewsTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("primary plus additional view"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddPrimaryWorkbookViewNativeMetadata(source);
        AddAdditionalWorkbookView(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var views = workbookXml.Root!.Element(workbookNs + "bookViews")!.Elements(workbookNs + "workbookView").ToList();
        views.Should().HaveCount(2);
        var primaryViews = views
            .Where(view =>
                string.Equals(view.Attribute("firstSheet")?.Value, "0", StringComparison.Ordinal) &&
                string.Equals(view.Attribute("activeTab")?.Value, "0", StringComparison.Ordinal) &&
                !string.Equals(view.Attribute("visibility")?.Value, "hidden", StringComparison.Ordinal))
            .ToList();
        primaryViews.Should().ContainSingle();
        var primaryView = primaryViews.Single();
        primaryView.Attribute("showSheetTabs").Should().NotBeNull();
        primaryView.Attribute("showSheetTabs")!.Value.Should().Be("0");
        primaryView.Attribute("tabRatio").Should().NotBeNull();
        primaryView.Attribute("tabRatio")!.Value.Should().Be("700");
        views.Any(view => string.Equals(view.Attribute("visibility")?.Value, "hidden", StringComparison.Ordinal))
            .Should().BeTrue();
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesCustomWorkbookViews()
    {
        var workbook = new Workbook("CustomWorkbookViewsRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("custom view"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddCustomWorkbookViews(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var customViews = workbookXml.Root!.Element(workbookNs + "customWorkbookViews");
        customViews.Should().NotBeNull();
        customViews!.ToString().Should().Contain("FreexcelView");
        customViews.ToString().Should().Contain("guid=\"{22222222-2222-2222-2222-222222222222}\"");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorkbookFileVersion()
    {
        var workbook = new Workbook("WorkbookFileVersionRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("version"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorkbookFileVersion(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var fileVersion = workbookXml.Root!.Element(workbookNs + "fileVersion");
        fileVersion.Should().NotBeNull();
        fileVersion!.Attribute("appName")!.Value.Should().Be("xl");
        fileVersion.Attribute("lastEdited")!.Value.Should().Be("7");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorkbookFileSharing()
    {
        var workbook = new Workbook("WorkbookFileSharingRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("sharing"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorkbookFileSharing(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var fileSharing = workbookXml.Root!.Element(workbookNs + "fileSharing");
        fileSharing.Should().NotBeNull();
        fileSharing!.Attribute("readOnlyRecommended")!.Value.Should().Be("1");
        fileSharing.Attribute("userName")!.Value.Should().Be("FreexcelTest");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorkbookFileRecoveryProperties()
    {
        var workbook = new Workbook("WorkbookFileRecoveryRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("recovery"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorkbookFileRecoveryProperties(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var recovery = workbookXml.Root!.Element(workbookNs + "fileRecoveryPr");
        recovery.Should().NotBeNull();
        recovery!.Attribute("autoRecover")!.Value.Should().Be("1");
        recovery.Attribute("crashSave")!.Value.Should().Be("1");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorkbookSmartTagMetadata()
    {
        var workbook = new Workbook("WorkbookSmartTagRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("smart tags"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorkbookSmartTagMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var smartTagProperties = workbookXml.Root!.Element(workbookNs + "smartTagPr");
        var smartTagTypes = workbookXml.Root.Element(workbookNs + "smartTagTypes");
        smartTagProperties.Should().NotBeNull();
        smartTagProperties!.Attribute("embed")!.Value.Should().Be("1");
        smartTagProperties.Attribute("show")!.Value.Should().Be("all");
        smartTagTypes.Should().NotBeNull();
        smartTagTypes!.Element(workbookNs + "smartTagType")!.Attribute("namespaceUri")!.Value.Should().Be("urn:schemas-microsoft-com:office:smarttags");
        smartTagTypes.Element(workbookNs + "smartTagType")!.Attribute("name")!.Value.Should().Be("place");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorkbookFunctionGroups()
    {
        var workbook = new Workbook("WorkbookFunctionGroupsRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("function group"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorkbookFunctionGroups(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var functionGroups = workbookXml.Root!.Element(workbookNs + "functionGroups");
        functionGroups.Should().NotBeNull();
        functionGroups!.ToString().Should().Contain("builtInGroupCount=\"16\"");
        functionGroups.ToString().Should().Contain("name=\"FreexcelNativeFunctions\"");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesStylesheetNativeMetadata()
    {
        var workbook = new Workbook("StylesheetNativeMetadataRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("style metadata"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddStylesheetNativeMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var stylesXml = LoadPackageXml(archive.GetEntry("xl/styles.xml")!);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var colors = stylesXml.Root!.Element(workbookNs + "colors");
        colors.Should().NotBeNull();
        colors!.ToString().Should().Contain("rgb=\"FF010203\"");

        var tableStyles = stylesXml.Root!.Element(workbookNs + "tableStyles");
        tableStyles.Should().NotBeNull();
        tableStyles!.Attribute("defaultPivotStyle").Should().NotBeNull();
        tableStyles.Attribute("defaultPivotStyle")!.Value.Should().Be("PivotStyleMedium9");
        tableStyles.Elements(workbookNs + "tableStyle")
            .Any(element => element.Attribute("name")?.Value == "FreexcelNativeTableStyle")
            .Should().BeTrue();
        tableStyles.Element(XName.Get("tableStylesNativeChild", "urn:freexcel:test"))!
            .Attribute("value")!
            .Value
            .Should()
            .Be("kept");
        loaded.PivotTableStyles.Should().ContainSingle(style =>
            style.Name == "FreexcelNativePivotStyle" &&
            style.AppliesToPivotTables &&
            !style.AppliesToTables &&
            style.Elements.Any(element =>
                element.Type == "wholeTable" &&
                element.DifferentialFormatId == 0));
        tableStyles.Elements(workbookNs + "tableStyle")
            .Any(element => element.Attribute("name")?.Value == "FreexcelNativePivotStyle" &&
                            element.Attribute("pivot")?.Value == "1")
            .Should().BeTrue();

        var extensionList = stylesXml.Root!.Element(workbookNs + "extLst");
        extensionList.Should().NotBeNull();
        extensionList!.ToString().Should().Contain("{FFEEDDCC-7788-6655-4433-22110099AABB}");
        extensionList.ToString().Should().Contain("FreexcelNativeStylesExtension");
    }

    [Fact]
    public void XlsxAdapter_Save_WritesAuthoredPivotTableStyleMetadata()
    {
        var workbook = new Workbook("AuthoredPivotStyleMetadataTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("pivot style"));
        var style = new PivotTableStyleModel
        {
            Name = "FreexcelAuthoredPivotStyle",
            AppliesToPivotTables = true,
            AppliesToTables = false
        };
        style.Elements.Add(new PivotTableStyleElementModel("wholeTable", 0));
        style.Elements.Add(new PivotTableStyleElementModel("firstRowStripe", 1, 1));
        workbook.PivotTableStyles.Add(style);

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var stylesXml = LoadPackageXml(archive.GetEntry("xl/styles.xml")!);
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            var tableStyle = stylesXml.Root!
                .Element(workbookNs + "tableStyles")!
                .Elements(workbookNs + "tableStyle")
                .Where(element => element.Attribute("name")?.Value == "FreexcelAuthoredPivotStyle")
                .Should().ContainSingle()
                .Subject;
            tableStyle.Attribute("pivot")!.Value.Should().Be("1");
            tableStyle.Attribute("table")!.Value.Should().Be("0");
            tableStyle.Attribute("count")!.Value.Should().Be("2");
            tableStyle.ToString().Should().Contain("type=\"firstRowStripe\"");
            tableStyle.ToString().Should().Contain("size=\"1\"");
        }

        saved.Position = 0;
        var loaded = new XlsxFileAdapter().Load(saved);
        loaded.PivotTableStyles.Should().ContainSingle(style =>
            style.Name == "FreexcelAuthoredPivotStyle" &&
            style.Elements.Count == 2);
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesStableDocumentProperties()
    {
        var workbook = new Workbook("DocumentPropertiesRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("document properties"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddStableDocumentProperties(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var coreProperties = LoadPackageXml(archive.GetEntry("docProps/core.xml")!);
        XNamespace dcNs = "http://purl.org/dc/elements/1.1/";
        XNamespace cpNs = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        coreProperties.Root!.Element(dcNs + "subject").Should().NotBeNull();
        coreProperties.Root.Element(dcNs + "subject")!.Value.Should().Be("Freexcel parity subject");
        coreProperties.Root.Element(cpNs + "keywords").Should().NotBeNull();
        coreProperties.Root.Element(cpNs + "keywords")!.Value.Should().Be("freexcel,xlsx,parity");
        coreProperties.Root.Element(cpNs + "category").Should().NotBeNull();
        coreProperties.Root.Element(cpNs + "category")!.Value.Should().Be("Native Metadata");
        coreProperties.Root.Element(cpNs + "contentStatus").Should().NotBeNull();
        coreProperties.Root.Element(cpNs + "contentStatus")!.Value.Should().Be("Reviewed");

        var appProperties = LoadPackageXml(archive.GetEntry("docProps/app.xml")!);
        XNamespace appNs = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";
        appProperties.Root!.Element(appNs + "Company").Should().NotBeNull();
        appProperties.Root.Element(appNs + "Company")!.Value.Should().Be("Freexcel Test Lab");
        appProperties.Root.Element(appNs + "Manager").Should().NotBeNull();
        appProperties.Root.Element(appNs + "Manager")!.Value.Should().Be("XLSX Fidelity");
        appProperties.Root.Element(appNs + "Application").Should().NotBeNull();
        appProperties.Root.Element(appNs + "Application")!.Value.Should().Be("Microsoft Excel");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesUnsupportedWorkbookProperties()
    {
        var workbook = new Workbook("WorkbookPropertiesRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("properties"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddUnsupportedWorkbookProperties(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookPr = workbookXml.Root!.Element(workbookNs + "workbookPr");
        workbookPr.Should().NotBeNull();
        workbookPr!.Attribute("date1904").Should().NotBeNull();
        workbookPr.Attribute("date1904")!.Value.Should().Be("1");
        workbookPr.Attribute("defaultThemeVersion")!.Value.Should().Be("166925");
        workbookPr.Elements(XName.Get("workbookPrNativeChild", "urn:freexcel:test"))
            .Select(element => element.Attribute("id")?.Value)
            .Should()
            .BeEquivalentTo("first", "second");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesPrinterSettingsPackageAndWorksheetReference()
    {
        var workbook = new Workbook("PrinterSettingsRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("print me"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalPrinterSettingsPackage(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("xl/printerSettings/printerSettings1.bin").Should().NotBeNull();
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        worksheetXml.Root!.Element(worksheetNs + "pageSetup")!
            .Attribute(relNs + "id")!
            .Value.Should().Be("rIdPrinterSettings1");
        var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        worksheetRelsXml.ToString().Should().Contain("../printerSettings/printerSettings1.bin");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesHeaderFooterLegacyDrawingReference()
    {
        var workbook = new Workbook("HeaderFooterDrawingRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("header image"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddHeaderFooterLegacyDrawingPackage(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("xl/drawings/vmlDrawing1.vml").Should().NotBeNull();
        archive.GetEntry("xl/media/headerFooterImage1.png").Should().NotBeNull();
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        var legacyDrawing = worksheetXml.Root!.Element(worksheetNs + "legacyDrawingHF");
        legacyDrawing.Should().NotBeNull();
        var relId = legacyDrawing!.Attribute(relNs + "id")!.Value;
        relId.Should().NotBeNullOrWhiteSpace();

        var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
        var hasLegacyDrawingRelationship = worksheetRelsXml.Root!.Elements(packageRelNs + "Relationship")
            .Any(rel =>
                string.Equals(rel.Attribute("Id")?.Value, relId, StringComparison.Ordinal) &&
                string.Equals(rel.Attribute("Target")?.Value, "../drawings/vmlDrawing1.vml", StringComparison.Ordinal));
        hasLegacyDrawingRelationship.Should().BeTrue();
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetCustomSheetViews()
    {
        var workbook = new Workbook("CustomSheetViewsRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("view state"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalCustomSheetViews(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var customSheetViews = worksheetXml.Root!.Element(worksheetNs + "customSheetViews");
        customSheetViews.Should().NotBeNull();
        customSheetViews!.ToString().Should().Contain("{11111111-1111-1111-1111-111111111111}");
        customSheetViews.ToString().Should().Contain("topLeftCell=\"B2\"");
        customSheetViews.ToString().Should().Contain("showGridLines=\"0\"");
    }

    [Fact]
    public void XlsxAdapter_LoadsMatchedCustomViewsIntoWorkbookModel()
    {
        var workbook = new Workbook("CustomViewLoadTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("view state"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMatchedCustomViews(source);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var view = loaded.CustomViews.Should().ContainSingle().Subject;
        view.Name.Should().Be("FreexcelView");
        view.Id.Should().Be("{11111111-1111-1111-1111-111111111111}");
        var state = view.Sheets.Should().ContainSingle().Subject;
        state.SheetName.Should().Be("Data");
        state.ZoomPercent.Should().Be(120);
        state.ShowGridlines.Should().BeFalse();
        state.ShowHeadings.Should().BeFalse();
        state.SplitRow.Should().Be(1);
        state.SplitColumn.Should().Be(1);
    }

    [Fact]
    public void XlsxAdapter_FreshSave_WritesModeledCustomViewsToWorkbookAndWorksheets()
    {
        var workbook = new Workbook("CustomViewSaveTest");
        workbook.AddSheet("Data");
        workbook.CustomViews.Add(new WorkbookCustomView(
            "Review",
            [new WorksheetCustomViewState(
                "Data",
                WorksheetViewMode.PageLayout,
                0,
                0,
                2,
                3,
                ShowGridlines: false,
                ShowHeadings: false,
                ShowRulers: false,
                ZoomPercent: 125,
                ShowFormulas: true)],
            "{33333333-3333-3333-3333-333333333333}"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorkbookFileRecoveryProperties(source);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var workbookView = workbookXml.Root!
            .Element(workbookNs + "customWorkbookViews")!
            .Element(workbookNs + "customWorkbookView")!;
        workbookView.Attribute("name")!.Value.Should().Be("Review");
        workbookView.Attribute("guid")!.Value.Should().Be("{33333333-3333-3333-3333-333333333333}");
        workbookXml.Root!.Elements().Select(element => element.Name.LocalName)
            .Should().ContainInOrder("customWorkbookViews", "fileRecoveryPr");

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var customSheetView = worksheetXml.Root!
            .Element(workbookNs + "customSheetViews")!
            .Element(workbookNs + "customSheetView")!;
        customSheetView.Attribute("guid")!.Value.Should().Be("{33333333-3333-3333-3333-333333333333}");
        customSheetView.Attribute("view")!.Value.Should().Be("pageLayout");
        customSheetView.Attribute("scale")!.Value.Should().Be("125");
        customSheetView.Attribute("showGridLines")!.Value.Should().Be("0");
        customSheetView.Attribute("showRowCol")!.Value.Should().Be("0");
        customSheetView.Attribute("showRuler")!.Value.Should().Be("0");
        customSheetView.Attribute("showFormulas")!.Value.Should().Be("1");
        customSheetView.Element(workbookNs + "pane")!.Attribute("xSplit")!.Value.Should().Be("3");
        customSheetView.Element(workbookNs + "pane")!.Attribute("ySplit")!.Value.Should().Be("2");
        worksheetXml.Root!.Elements().Select(element => element.Name.LocalName)
            .Should().ContainInOrder("customSheetViews", "pageMargins");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_DoesNotResurrectRemovedCustomViewSheetState()
    {
        var workbook = new Workbook("CustomViewSheetStateRemovalTest");
        var data = workbook.AddSheet("Data");
        var assumptions = workbook.AddSheet("Assumptions");
        data.SetCell(new CellAddress(data.Id, 1, 1), new TextValue("view state"));
        assumptions.SetCell(new CellAddress(assumptions.Id, 1, 1), new TextValue("other view state"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMatchedCustomViewsOnTwoSheets(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.CustomViews.Should().ContainSingle();
        loaded.CustomViews[0] = loaded.CustomViews[0] with
        {
            Sheets = loaded.CustomViews[0].Sheets
                .Where(state => state.SheetName == "Data")
                .ToList()
        };

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var firstWorksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        firstWorksheetXml.Root!
            .Element(workbookNs + "customSheetViews")!
            .Elements(workbookNs + "customSheetView")
            .Should().ContainSingle();

        var secondWorksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet2.xml")!);
        var secondSheetViews = secondWorksheetXml.Root!
            .Element(workbookNs + "customSheetViews")?
            .Elements(workbookNs + "customSheetView") ?? [];
        secondSheetViews.Any(view =>
            view.Attribute("guid")?.Value == "{11111111-1111-1111-1111-111111111111}").Should().BeFalse();
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_MergesCustomViewNativeMetadataAndRetainsNativeOnlyViews()
    {
        var workbook = new Workbook("CustomViewMergeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("view state"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMatchedCustomViews(source, includeNativeOnlyView: true);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.CustomViews.Should().ContainSingle(view => view.Name == "FreexcelView");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
        var workbookViews = workbookXml.Root!
            .Element(workbookNs + "customWorkbookViews")!
            .Elements(workbookNs + "customWorkbookView")
            .ToList();
        workbookViews.Any(view =>
            view.Attribute("guid")?.Value == "{11111111-1111-1111-1111-111111111111}" &&
            view.Attribute("includePrintSettings")?.Value == "1").Should().BeTrue();
        workbookViews.Any(view => view.Attribute("name")?.Value == "NativeOnlyView").Should().BeTrue();

        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var sheetViews = worksheetXml.Root!
            .Element(workbookNs + "customSheetViews")!
            .Elements(workbookNs + "customSheetView")
            .ToList();
        sheetViews.Any(view =>
            view.Attribute("guid")?.Value == "{11111111-1111-1111-1111-111111111111}" &&
            view.Element(workbookNs + "pane")?.Attribute("topLeftCell")?.Value == "B2").Should().BeTrue();
        sheetViews.Any(view => view.Attribute("guid")?.Value == "{22222222-2222-2222-2222-222222222222}").Should().BeTrue();
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesAdditionalWorksheetSheetViews()
    {
        var workbook = new Workbook("AdditionalSheetViewsRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("view state"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddAdditionalWorksheetSheetView(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetViews = worksheetXml.Root!.Element(worksheetNs + "sheetViews");
        sheetViews.Should().NotBeNull();
        var hasAdditionalSheetView = sheetViews!.Elements(worksheetNs + "sheetView").Any(view =>
            string.Equals(view.Attribute("workbookViewId")?.Value, "1", StringComparison.Ordinal) &&
            string.Equals(view.Attribute("view")?.Value, "pageBreakPreview", StringComparison.Ordinal) &&
            string.Equals(view.Attribute("topLeftCell")?.Value, "C3", StringComparison.Ordinal));
        hasAdditionalSheetView.Should().BeTrue();
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesExistingWorksheetSheetViewNativeMetadata()
    {
        var workbook = new Workbook("ExistingWorksheetSheetViewMetadata");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Sheet view"));
        sheet.FrozenRows = 1;

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddExistingWorksheetSheetViewNativeMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetView = worksheetXml.Root!
            .Element(worksheetNs + "sheetViews")!
            .Elements(worksheetNs + "sheetView")
            .Single(element => element.Attribute("workbookViewId")?.Value == "0");
        sheetView.Attribute("showZeros").Should().NotBeNull();
        sheetView.Attribute("showZeros")!.Value.Should().Be("0");
        sheetView.Attribute("rightToLeft").Should().NotBeNull();
        sheetView.Attribute("rightToLeft")!.Value.Should().Be("1");
        sheetView.Element(worksheetNs + "pivotSelection").Should().NotBeNull();
        sheetView.Element(worksheetNs + "pivotSelection")!.Attribute("pane")!.Value.Should().Be("topRight");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetSheetViewsNativeAttributes()
    {
        var workbook = new Workbook("WorksheetSheetViewsNativeMetadata");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("View state"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetSheetViewsNativeAttributes(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetViews = worksheetXml.Root!.Element(worksheetNs + "sheetViews");
        sheetViews.Should().NotBeNull();
        sheetViews!.Attribute("nativeSheetViewsAttr").Should().NotBeNull();
        sheetViews.Attribute("nativeSheetViewsAttr")!.Value.Should().Be("kept");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_MergesExistingWorksheetSheetViewChildNativeMetadata()
    {
        var workbook = new Workbook("ExistingWorksheetSheetViewChildMetadata");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Sheet view child"));
        sheet.FrozenRows = 1;
        sheet.FrozenCols = 1;
        sheet.ActiveRow = 1;
        sheet.ActiveCol = 1;

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddExistingWorksheetSheetViewChildNativeMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetView = worksheetXml.Root!
            .Element(worksheetNs + "sheetViews")!
            .Elements(worksheetNs + "sheetView")
            .Single(element => element.Attribute("workbookViewId")?.Value == "0");
        var panes = sheetView.Elements(worksheetNs + "pane").ToList();
        panes.Should().ContainSingle();
        panes.Single().Attribute("customPaneAttr").Should().NotBeNull();
        panes.Single().Attribute("customPaneAttr")!.Value.Should().Be("pane-native");

        var selections = sheetView.Elements(worksheetNs + "selection")
            .Where(element => element.Attribute("pane")?.Value == "bottomRight")
            .ToList();
        selections.Should().ContainSingle();
        selections.Single().Attribute("customSelectionAttr").Should().NotBeNull();
        selections.Single().Attribute("customSelectionAttr")!.Value.Should().Be("selection-native");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetSheetFormatMetadata()
    {
        var workbook = new Workbook("WorksheetSheetFormatMetadata");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Sheet format"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetSheetFormatMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetFormat = worksheetXml.Root!.Element(worksheetNs + "sheetFormatPr");
        sheetFormat.Should().NotBeNull();
        sheetFormat!.Attribute("baseColWidth")!.Value.Should().Be("12");
        sheetFormat.Attribute("zeroHeight")!.Value.Should().Be("1");
        sheetFormat.Attribute("thickTop")!.Value.Should().Be("1");
        sheetFormat.Attribute("outlineLevelRow")!.Value.Should().Be("3");
        sheetFormat.Element(worksheetNs + "nativeSheetFormatChild").Should().NotBeNull();
        sheetFormat.Element(worksheetNs + "nativeSheetFormatChild")!
            .Attribute("value")!
            .Value
            .Should()
            .Be("kept");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetPageBreakMetadata()
    {
        var workbook = new Workbook("WorksheetPageBreakMetadata");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Page breaks"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetPageBreakMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).RowPageBreaks.Should().Contain(20u);
        loaded.GetSheetAt(0).ColumnPageBreaks.Should().Contain(5u);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rowBreak = worksheetXml.Root!.Element(worksheetNs + "rowBreaks")!
            .Elements(worksheetNs + "brk")
            .Single(element => element.Attribute("id")?.Value == "20");
        rowBreak.Attribute("pt")!.Value.Should().Be("1");
        rowBreak.Attribute("customAttr")!.Value.Should().Be("row-native");

        var columnBreak = worksheetXml.Root!.Element(worksheetNs + "colBreaks")!
            .Elements(worksheetNs + "brk")
            .Single(element => element.Attribute("id")?.Value == "5");
        columnBreak.Attribute("pt")!.Value.Should().Be("1");
        columnBreak.Attribute("customAttr")!.Value.Should().Be("col-native");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_DoesNotResurrectRemovedWorksheetPageBreaks()
    {
        var workbook = new Workbook("WorksheetPageBreakRemoval");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Page breaks"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetPageBreakMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.RowPageBreaks.Remove(20u).Should().BeTrue();
        loadedSheet.ColumnPageBreaks.Remove(5u).Should().BeTrue();
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        worksheetXml.Root!.Element(worksheetNs + "rowBreaks")?.Elements(worksheetNs + "brk")
            .Select(element => (string?)element.Attribute("id"))
            .Should().NotContain("20");
        worksheetXml.Root!.Element(worksheetNs + "colBreaks")?.Elements(worksheetNs + "brk")
            .Select(element => (string?)element.Attribute("id"))
            .Should().NotContain("5");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_RetainsMalformedNativeOnlyWorksheetPageBreaks()
    {
        var workbook = new Workbook("WorksheetNativeOnlyPageBreaks");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Page breaks"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetPageBreakMetadata(source);
        AddMalformedWorksheetPageBreakMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.RowPageBreaks.Remove(20u).Should().BeTrue();
        loadedSheet.ColumnPageBreaks.Remove(5u).Should().BeTrue();
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rowBreaks = worksheetXml.Root!.Element(worksheetNs + "rowBreaks")!
            .Elements(worksheetNs + "brk")
            .ToList();
        rowBreaks.Select(element => (string?)element.Attribute("id")).Should().NotContain("20");
        rowBreaks.Select(element => (string?)element.Attribute("customAttr")).Should().ContainSingle("row-native-only");

        var columnBreaks = worksheetXml.Root!.Element(worksheetNs + "colBreaks")!
            .Elements(worksheetNs + "brk")
            .ToList();
        columnBreaks.Select(element => (string?)element.Attribute("id")).Should().NotContain("5");
        columnBreaks.Select(element => (string?)element.Attribute("customAttr")).Should().ContainSingle("col-native-only");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetPrintOptionsMetadata()
    {
        var workbook = new Workbook("WorksheetPrintOptionsMetadata");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Print options"));
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;
        sheet.CenterHorizontallyOnPage = true;

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetPrintOptionsMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var printOptions = worksheetXml.Root!.Element(worksheetNs + "printOptions");
        printOptions.Should().NotBeNull();
        printOptions!.Attribute("gridLinesSet")!.Value.Should().Be("1");
        printOptions.Attribute("customAttr")!.Value.Should().Be("print-native");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetPageSetupNativeAttributes()
    {
        var workbook = new Workbook("WorksheetPageSetupNativeMetadata");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Page setup"));
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PrintQualityDpi = 600;

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetPageSetupNativeAttributes(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var pageSetup = worksheetXml.Root!.Element(worksheetNs + "pageSetup");
        pageSetup.Should().NotBeNull();
        pageSetup!.Attribute("usePrinterDefaults")!.Value.Should().Be("1");
        pageSetup.Attribute("copies")!.Value.Should().Be("3");
        pageSetup.Attribute("customAttr")!.Value.Should().Be("page-setup-native");
        pageSetup.Element(worksheetNs + "nativePageSetupChild").Should().NotBeNull();
        pageSetup.Element(worksheetNs + "nativePageSetupChild")!
            .Attribute("value")!
            .Value
            .Should()
            .Be("kept");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetPageMarginsHeaderFooterAttributes()
    {
        var workbook = new Workbook("WorksheetPageMarginsNativeMetadata");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Page margins"));
        sheet.PageMargins = new WorksheetPageMargins(0.7, 0.8, 0.9, 1.1);

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetPageMarginsHeaderFooterAttributes(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var pageMargins = worksheetXml.Root!.Element(worksheetNs + "pageMargins");
        pageMargins.Should().NotBeNull();
        pageMargins!.Attribute("header").Should().NotBeNull();
        pageMargins.Attribute("header")!.Value.Should().Be("0.35");
        pageMargins.Attribute("footer").Should().NotBeNull();
        pageMargins.Attribute("footer")!.Value.Should().Be("0.45");
        pageMargins.Attribute("customAttr").Should().NotBeNull();
        pageMargins.Attribute("customAttr")!.Value.Should().Be("page-margins-native");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetHeaderFooterNativeAttributes()
    {
        var workbook = new Workbook("HeaderFooterNativeMetadata");
        var sheet = workbook.AddSheet("Data");
        sheet.PageHeader = new WorksheetHeaderFooter("Left", "Center", "Right");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("keep"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetHeaderFooterNativeAttributes(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var headerFooter = worksheetXml.Root!.Element(worksheetNs + "headerFooter");

        headerFooter.Should().NotBeNull();
        headerFooter!.Attribute("nativeHeaderFooterAttr").Should().NotBeNull();
        headerFooter.Attribute("nativeHeaderFooterAttr")!.Value.Should().Be("kept");
        headerFooter.Element(worksheetNs + "oddHeader")!.Value.Should().Contain("Center");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetDimensionNativeAttributes()
    {
        var workbook = new Workbook("DimensionNativeMetadata");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("keep"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetDimensionNativeAttributes(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var dimension = worksheetXml.Root!.Element(worksheetNs + "dimension");
        dimension.Should().NotBeNull();
        dimension!.Attribute("nativeDimensionAttr").Should().NotBeNull();
        dimension.Attribute("nativeDimensionAttr")!.Value.Should().Be("kept");
        dimension.Attribute("ref")!.Value.Should().Be("A1:A2");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_DoesNotResurrectModeledPrintOptionsAttributes()
    {
        var workbook = new Workbook("WorksheetPrintOptionsAuthority");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Print options"));
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;
        sheet.CenterHorizontallyOnPage = true;
        sheet.CenterVerticallyOnPage = true;

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetPrintOptionsModeledAndNativeAttributes(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.PrintGridlines = false;
        loadedSheet.PrintHeadings = false;
        loadedSheet.CenterHorizontallyOnPage = false;
        loadedSheet.CenterVerticallyOnPage = false;

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var printOptions = worksheetXml.Root!.Element(worksheetNs + "printOptions");
        printOptions.Should().NotBeNull();
        printOptions!.Attribute("gridLinesSet")!.Value.Should().Be("1");
        printOptions.Attribute("customAttr")!.Value.Should().Be("print-native");
        printOptions.Attribute("gridLines")?.Value.Should().NotBe("1");
        printOptions.Attribute("headings")?.Value.Should().NotBe("1");
        printOptions.Attribute("horizontalCentered")?.Value.Should().NotBe("1");
        printOptions.Attribute("verticalCentered")?.Value.Should().NotBe("1");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_DoesNotReAddPrintOptionsWhenSourceOnlyHadModeledAttributes()
    {
        var workbook = new Workbook("WorksheetPrintOptionsModeledOnly");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Print options"));
        sheet.PrintGridlines = true;
        sheet.PrintHeadings = true;
        sheet.CenterHorizontallyOnPage = true;
        sheet.CenterVerticallyOnPage = true;

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetPrintOptionsModeledOnlyAttributes(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.PrintGridlines = false;
        loadedSheet.PrintHeadings = false;
        loadedSheet.CenterHorizontallyOnPage = false;
        loadedSheet.CenterVerticallyOnPage = false;

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var printOptions = worksheetXml.Root!.Element(worksheetNs + "printOptions");
        printOptions.Should().NotBeNull();
        printOptions!.Attribute("gridLines")?.Value.Should().NotBe("1");
        printOptions.Attribute("headings")?.Value.Should().NotBe("1");
        printOptions.Attribute("horizontalCentered")?.Value.Should().NotBe("1");
        printOptions.Attribute("verticalCentered")?.Value.Should().NotBe("1");
        printOptions.Attribute("customAttr").Should().BeNull();
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_DoesNotResurrectModeledPageSetupAttributes()
    {
        var workbook = new Workbook("WorksheetPageSetupAuthority");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Page setup"));
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PaperSize = WorksheetPaperSize.Legal;
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 2, 3);
        sheet.PageOrder = WorksheetPageOrder.OverThenDown;
        sheet.FirstPageNumber = 7;
        sheet.PrintBlackAndWhite = true;
        sheet.PrintDraftQuality = true;
        sheet.PrintQualityDpi = 600;
        sheet.PrintErrorValue = WorksheetPrintErrorValue.Dash;
        sheet.PrintComments = WorksheetPrintComments.AtEnd;

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetPageSetupModeledAndNativeAttributes(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.PageOrientation = WorksheetPageOrientation.Portrait;
        loadedSheet.PaperSize = WorksheetPaperSize.A4;
        loadedSheet.ScaleToFit = WorksheetScaleToFit.Default;
        loadedSheet.PageOrder = WorksheetPageOrder.DownThenOver;
        loadedSheet.FirstPageNumber = null;
        loadedSheet.PrintBlackAndWhite = false;
        loadedSheet.PrintDraftQuality = false;
        loadedSheet.PrintQualityDpi = null;
        loadedSheet.PrintErrorValue = WorksheetPrintErrorValue.Displayed;
        loadedSheet.PrintComments = WorksheetPrintComments.None;

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var pageSetup = worksheetXml.Root!.Element(worksheetNs + "pageSetup");
        pageSetup.Should().NotBeNull();
        pageSetup!.Attribute("usePrinterDefaults")!.Value.Should().Be("1");
        pageSetup.Attribute("copies")!.Value.Should().Be("3");
        pageSetup.Attribute("customAttr")!.Value.Should().Be("page-setup-native");
        pageSetup.Attribute("orientation")?.Value.Should().NotBe("landscape");
        pageSetup.Attribute("paperSize")?.Value.Should().NotBe("5");
        pageSetup.Attribute("fitToWidth")?.Value.Should().NotBe("2");
        pageSetup.Attribute("fitToHeight")?.Value.Should().NotBe("3");
        pageSetup.Attribute("pageOrder")?.Value.Should().NotBe("overThenDown");
        pageSetup.Attribute("firstPageNumber")?.Value.Should().NotBe("7");
        pageSetup.Attribute("blackAndWhite")?.Value.Should().NotBe("1");
        pageSetup.Attribute("draft")?.Value.Should().NotBe("1");
        pageSetup.Attribute("horizontalDpi")?.Value.Should().NotBe("600");
        pageSetup.Attribute("verticalDpi")?.Value.Should().NotBe("600");
        pageSetup.Attribute("errors")?.Value.Should().NotBe("dash");
        pageSetup.Attribute("cellComments")?.Value.Should().NotBe("atEnd");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_DoesNotReAddPageSetupWhenSourceOnlyHadModeledAttributes()
    {
        var workbook = new Workbook("WorksheetPageSetupModeledOnly");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Page setup"));
        sheet.PageOrientation = WorksheetPageOrientation.Landscape;
        sheet.PaperSize = WorksheetPaperSize.Legal;
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 2, 3);
        sheet.PageOrder = WorksheetPageOrder.OverThenDown;
        sheet.FirstPageNumber = 7;
        sheet.PrintBlackAndWhite = true;
        sheet.PrintDraftQuality = true;
        sheet.PrintQualityDpi = 600;
        sheet.PrintErrorValue = WorksheetPrintErrorValue.Dash;
        sheet.PrintComments = WorksheetPrintComments.AtEnd;

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetPageSetupModeledOnlyAttributes(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.PageOrientation = WorksheetPageOrientation.Portrait;
        loadedSheet.PaperSize = WorksheetPaperSize.A4;
        loadedSheet.ScaleToFit = WorksheetScaleToFit.Default;
        loadedSheet.PageOrder = WorksheetPageOrder.DownThenOver;
        loadedSheet.FirstPageNumber = null;
        loadedSheet.PrintBlackAndWhite = false;
        loadedSheet.PrintDraftQuality = false;
        loadedSheet.PrintQualityDpi = null;
        loadedSheet.PrintErrorValue = WorksheetPrintErrorValue.Displayed;
        loadedSheet.PrintComments = WorksheetPrintComments.None;

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var pageSetup = worksheetXml.Root!.Element(worksheetNs + "pageSetup");
        pageSetup.Should().NotBeNull();
        pageSetup!.Attribute("orientation")?.Value.Should().NotBe("landscape");
        pageSetup.Attribute("paperSize")?.Value.Should().NotBe("5");
        pageSetup.Attribute("fitToWidth")?.Value.Should().NotBe("2");
        pageSetup.Attribute("fitToHeight")?.Value.Should().NotBe("3");
        pageSetup.Attribute("pageOrder")?.Value.Should().NotBe("overThenDown");
        pageSetup.Attribute("firstPageNumber")?.Value.Should().NotBe("7");
        pageSetup.Attribute("blackAndWhite")?.Value.Should().NotBe("1");
        pageSetup.Attribute("draft")?.Value.Should().NotBe("1");
        pageSetup.Attribute("horizontalDpi")?.Value.Should().NotBe("600");
        pageSetup.Attribute("verticalDpi")?.Value.Should().NotBe("600");
        pageSetup.Attribute("errors")?.Value.Should().NotBe("dash");
        pageSetup.Attribute("cellComments")?.Value.Should().NotBe("atEnd");
        pageSetup.Attribute("customAttr").Should().BeNull();
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetRowNativeAttributes()
    {
        var workbook = new Workbook("WorksheetRowNativeMetadata");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Row metadata"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetRowNativeAttributes(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 3, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var row = worksheetXml.Root!
            .Element(worksheetNs + "sheetData")!
            .Elements(worksheetNs + "row")
            .Single(element => element.Attribute("r")?.Value == "2");
        worksheetXml.Root!
            .Element(worksheetNs + "sheetData")!
            .Attribute("nativeSheetDataAttr")
            .Should()
            .NotBeNull();
        worksheetXml.Root!
            .Element(worksheetNs + "sheetData")!
            .Attribute("nativeSheetDataAttr")!
            .Value
            .Should()
            .Be("kept");
        row.Attribute("thickTop")!.Value.Should().Be("1");
        row.Attribute("ph")!.Value.Should().Be("1");
        row.Attribute("customAttr")!.Value.Should().Be("row-native");
        row.Element(XName.Get("rowNativeChild", "urn:freexcel:test"))!
            .Attribute("value")!
            .Value
            .Should()
            .Be("kept");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetCellNativeAttributes()
    {
        var workbook = new Workbook("WorksheetCellNativeMetadata");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Cell metadata"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetCellNativeAttributes(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 3, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cell = worksheetXml.Root!
            .Element(worksheetNs + "sheetData")!
            .Descendants(worksheetNs + "c")
            .Single(element => element.Attribute("r")?.Value == "A2");
        cell.Attribute("cm")!.Value.Should().Be("2");
        cell.Attribute("vm")!.Value.Should().Be("1");
        cell.Attribute("ph")!.Value.Should().Be("1");
        cell.Attribute("customAttr")!.Value.Should().Be("cell-native");
        cell.Element(XName.Get("cellNativeChild", "urn:freexcel:test"))!
            .Attribute("value")!
            .Value
            .Should()
            .Be("kept");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetRowAndCellExtensionLists()
    {
        var workbook = new Workbook("WorksheetSheetDataExtensionMetadata");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Extension metadata"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetRowAndCellExtensionLists(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 3, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace freexcelNs = "urn:freexcel:test";
        var row = worksheetXml.Root!
            .Element(worksheetNs + "sheetData")!
            .Elements(worksheetNs + "row")
            .Single(element => element.Attribute("r")?.Value == "2");
        row.Element(worksheetNs + "extLst").Should().NotBeNull();
        row.Element(worksheetNs + "extLst")!
            .Element(worksheetNs + "ext")!
            .Element(freexcelNs + "rowExt")!
            .Attribute("value")!.Value.Should().Be("row-extension");

        var cell = row.Elements(worksheetNs + "c")
            .Single(element => element.Attribute("r")?.Value == "A2");
        cell.Element(worksheetNs + "extLst").Should().NotBeNull();
        cell.Element(worksheetNs + "extLst")!
            .Element(worksheetNs + "ext")!
            .Element(freexcelNs + "cellExt")!
            .Attribute("value")!.Value.Should().Be("cell-extension");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetColumnNativeAttributes()
    {
        var workbook = new Workbook("WorksheetColumnNativeMetadata");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Column metadata"));
        sheet.ColumnWidths[2] = 14.0;

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetColumnNativeAttributes(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 2), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var column = worksheetXml.Root!
            .Element(worksheetNs + "cols")!
            .Elements(worksheetNs + "col")
            .Single(element => element.Attribute("min")?.Value == "2" && element.Attribute("max")?.Value == "2");
        worksheetXml.Root!
            .Element(worksheetNs + "cols")!
            .Attribute("nativeColsAttr")
            .Should()
            .NotBeNull();
        worksheetXml.Root!
            .Element(worksheetNs + "cols")!
            .Attribute("nativeColsAttr")!
            .Value
            .Should()
            .Be("kept");
        column.Attribute("bestFit")!.Value.Should().Be("1");
        column.Attribute("phonetic")!.Value.Should().Be("1");
        column.Attribute("customAttr")!.Value.Should().Be("column-native");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetScenarios()
    {
        var workbook = new Workbook("ScenariosRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("input"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalWorksheetScenarios(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var scenarios = worksheetXml.Root!.Element(worksheetNs + "scenarios");
        scenarios.Should().NotBeNull();
        scenarios!.ToString().Should().Contain("BestCase");
        scenarios.ToString().Should().Contain("inputCells");
        scenarios.ToString().Should().Contain("val=\"42\"");
    }

    [Fact]
    public void XlsxAdapter_LoadsSupportedWorksheetScenariosIntoWorkbookModel()
    {
        var workbook = new Workbook("ScenarioLoadTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("input"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalWorksheetScenarios(source);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var loadedSheet = loaded.GetSheetAt(0);
        var scenario = loaded.Scenarios.Should().ContainSingle().Subject;
        scenario.Name.Should().Be("BestCase");
        scenario.ChangingCells.Should().ContainSingle()
            .Which.Should().Be(new ScenarioCellValue(
                new CellAddress(loadedSheet.Id, 1, 1),
                new NumberValue(42)));
    }

    [Fact]
    public void XlsxAdapter_FreshSave_WritesModelScenariosToWorksheetXmlGroupedBySheet()
    {
        var workbook = new Workbook("ScenarioSaveTest");
        var data = workbook.AddSheet("Data");
        var assumptions = workbook.AddSheet("Assumptions");
        workbook.Scenarios.Add(new WorkbookScenario(
            "BestCase",
            [
                new ScenarioCellValue(new CellAddress(data.Id, 1, 1), new NumberValue(42)),
                new ScenarioCellValue(new CellAddress(assumptions.Id, 2, 2), new TextValue("manual"))
            ]));

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var firstWorksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var secondWorksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet2.xml")!);

        var firstScenario = firstWorksheetXml.Root!
            .Element(worksheetNs + "scenarios")!
            .Elements(worksheetNs + "scenario")
            .Should().ContainSingle().Subject;
        firstScenario.Attribute("name")!.Value.Should().Be("BestCase");
        firstScenario.Element(worksheetNs + "inputCells")!.Attribute("r")!.Value.Should().Be("A1");
        firstScenario.Element(worksheetNs + "inputCells")!.Attribute("val")!.Value.Should().Be("42");
        firstScenario.Elements(worksheetNs + "inputCells").Should().ContainSingle();
        firstWorksheetXml.Root!.Elements().Select(element => element.Name.LocalName)
            .Should().ContainInOrder("scenarios", "pageMargins");

        var secondScenario = secondWorksheetXml.Root!
            .Element(worksheetNs + "scenarios")!
            .Elements(worksheetNs + "scenario")
            .Should().ContainSingle().Subject;
        secondScenario.Attribute("name")!.Value.Should().Be("BestCase");
        secondScenario.Element(worksheetNs + "inputCells")!.Attribute("r")!.Value.Should().Be("B2");
        secondScenario.Element(worksheetNs + "inputCells")!.Attribute("val")!.Value.Should().Be("manual");
    }

    [Fact]
    public void XlsxAdapter_FreshSave_DeduplicatesScenarioInputCellsAndSkipsBlankValues()
    {
        var workbook = new Workbook("ScenarioDedupeTest");
        var sheet = workbook.AddSheet("Data");
        workbook.Scenarios.Add(new WorkbookScenario(
            "BestCase",
            [
                new ScenarioCellValue(new CellAddress(sheet.Id, 1, 1), new NumberValue(10)),
                new ScenarioCellValue(new CellAddress(sheet.Id, 1, 1), new NumberValue(20)),
                new ScenarioCellValue(new CellAddress(sheet.Id, 1, 2), BlankValue.Instance)
            ]));

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var inputCells = worksheetXml.Root!
            .Element(worksheetNs + "scenarios")!
            .Element(worksheetNs + "scenario")!
            .Elements(worksheetNs + "inputCells")
            .ToList();

        inputCells.Should().ContainSingle();
        inputCells[0].Attribute("r")!.Value.Should().Be("A1");
        inputCells[0].Attribute("val")!.Value.Should().Be("20");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesModeledScenarioNativeMetadataWithoutDuplicates()
    {
        var workbook = new Workbook("ScenarioMetadataMergeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("input"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalWorksheetScenarios(source);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var scenarios = worksheetXml.Root!.Element(worksheetNs + "scenarios")!;
        scenarios.Attribute("current").Should().BeNull();
        scenarios.Attribute("show").Should().BeNull();
        var scenario = scenarios.Elements(worksheetNs + "scenario")
            .Single(element => element.Attribute("name")?.Value == "BestCase");
        scenario.Attribute("locked")!.Value.Should().Be("1");
        scenario.Attribute("user")!.Value.Should().Be("FreexcelTest");
        scenario.Elements(worksheetNs + "inputCells").Should().ContainSingle();
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_DoesNotPreserveScenarioCurrentShowWhenListChanges()
    {
        var workbook = new Workbook("ScenarioIndexMetadataTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("input"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalWorksheetScenarios(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loaded.Scenarios.Clear();
        loaded.Scenarios.Add(new WorkbookScenario(
            "OtherCase",
            [new ScenarioCellValue(new CellAddress(loadedSheet.Id, 1, 1), new NumberValue(12))]));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var scenarios = worksheetXml.Root!.Element(worksheetNs + "scenarios")!;

        scenarios.Attribute("current").Should().BeNull();
        scenarios.Attribute("show").Should().BeNull();
        var scenario = scenarios.Elements(worksheetNs + "scenario").Should().ContainSingle().Subject;
        scenario.Attribute("name")!.Value.Should().Be("OtherCase");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_DoesNotResurrectRemovedModeledScenario()
    {
        var workbook = new Workbook("ScenarioRemovalTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("input"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalWorksheetScenarios(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.Scenarios.Clear();

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var scenarioEntries = worksheetXml.Root!
            .Element(worksheetNs + "scenarios")?
            .Elements(worksheetNs + "scenario") ?? [];
        scenarioEntries.Any(element => element.Attribute("name")?.Value == "BestCase").Should().BeFalse();
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_RetainsUnsupportedWorksheetScenarioEntryBestEffort()
    {
        var workbook = new Workbook("ScenarioUnsupportedRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("input"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddUnsupportedWorksheetScenario(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.Scenarios.Should().BeEmpty();

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var scenario = worksheetXml.Root!
            .Element(worksheetNs + "scenarios")!
            .Elements(worksheetNs + "scenario")
            .Single(element => element.Attribute("name")?.Value == "NativeOnly");
        scenario.Element(worksheetNs + "inputCells")!.Attribute("r")!.Value.Should().Be("A1:B1");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_MergesUnknownWorksheetExtensionListEntries()
    {
        var workbook = new Workbook("WorksheetExtensionRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(3));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalSparklineWorksheetExtension(source, includeUnknownExtension: true);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).Sparklines.Should().ContainSingle();
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        worksheetXml.ToString().Should().Contain("sparklineGroups");
        worksheetXml.ToString().Should().Contain("{FFEEDDCC-BBAA-9988-7766-554433221100}");
        worksheetXml.ToString().Should().Contain("FreexcelUnknownWorksheetExtension");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesUnsupportedWorksheetSheetProperties()
    {
        var workbook = new Workbook("SheetPropertiesRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("sheet properties"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddUnsupportedWorksheetSheetProperties(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetPr = worksheetXml.Root!.Element(worksheetNs + "sheetPr");
        sheetPr.Should().NotBeNull();
        sheetPr!.Attribute("filterMode").Should().NotBeNull();
        sheetPr.Attribute("filterMode")!.Value.Should().Be("1");
        sheetPr.Element(worksheetNs + "pageSetUpPr").Should().NotBeNull();
        sheetPr.Element(worksheetNs + "pageSetUpPr")!.Attribute("autoPageBreaks")!.Value.Should().Be("0");
        sheetPr.Elements(XName.Get("sheetPrNativeChild", "urn:freexcel:test"))
            .Select(element => element.Attribute("id")?.Value)
            .Should()
            .BeEquivalentTo("first", "second");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetIgnoredErrors()
    {
        var workbook = new Workbook("IgnoredErrorsRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("00123"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetIgnoredErrors(source, "A1", ("numberStoredAsText", "1"), ("twoDigitTextYear", "1"));

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var ignoredErrors = worksheetXml.Root!.Element(worksheetNs + "ignoredErrors");
        ignoredErrors.Should().NotBeNull();
        ignoredErrors!.ToString().Should().Contain("numberStoredAsText=\"1\"");
        ignoredErrors.ToString().Should().Contain("twoDigitTextYear=\"1\"");
        ignoredErrors.ToString().Should().Contain("sqref=\"A1\"");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_MergesRangeIgnoredErrorsWithoutDuplicateCells()
    {
        var workbook = new Workbook("IgnoredErrorsRangeRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("00123"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("00456"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetIgnoredErrors(source, "A1:B1", ("numberStoredAsText", "1"), ("twoDigitTextYear", "1"));

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var ignoredErrors = worksheetXml.Root!.Element(worksheetNs + "ignoredErrors");
        ignoredErrors.Should().NotBeNull();
        var entries = ignoredErrors!.Elements(worksheetNs + "ignoredError").ToList();

        entries.Select(entry => entry.Attribute("sqref")?.Value).Should().BeEquivalentTo(["A1", "B1"]);
        entries.Select(entry => entry.Attribute("numberStoredAsText")?.Value).Should().OnlyContain(value => value == "1");
        entries.Select(entry => entry.Attribute("twoDigitTextYear")?.Value).Should().OnlyContain(value => value == "1");
    }

    [Fact]
    public void XlsxAdapter_Load_IgnoredErrors_SetIgnoreFormulaErrorForSingleRefsAndRanges()
    {
        var workbook = new Workbook("IgnoredErrorsLoadTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("00123"));
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "1/0");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetIgnoredErrors(source, "A1 B2:C3", ("numberStoredAsText", "1"));

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);

        loadedSheet.GetCell(1, 1)!.IgnoreFormulaError.Should().BeTrue();
        loadedSheet.GetCell(2, 2)!.IgnoreFormulaError.Should().BeTrue();
        loadedSheet.GetCell(2, 3)!.IgnoreFormulaError.Should().BeTrue();
        loadedSheet.GetCell(3, 2)!.IgnoreFormulaError.Should().BeTrue();
        loadedSheet.GetCell(3, 3)!.IgnoreFormulaError.Should().BeTrue();
    }

    [Fact]
    public void XlsxAdapter_Load_IgnoredErrors_LargeRangeMarksOnlyExistingCells()
    {
        var workbook = new Workbook("IgnoredErrorsLargeRangeLoadTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("00123"));
        sheet.SetFormula(new CellAddress(sheet.Id, 1_048_576, 16_384), "1/0");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetIgnoredErrors(source, "A1:XFD1048576", ("numberStoredAsText", "1"));

        source.Position = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var loaded = adapter.Load(source);
        stopwatch.Stop();
        var loadedSheet = loaded.GetSheetAt(0);

        loadedSheet.CellCount.Should().Be(2);
        loadedSheet.GetCell(1, 1)!.IgnoreFormulaError.Should().BeTrue();
        loadedSheet.GetCell(1_048_576, 16_384)!.IgnoreFormulaError.Should().BeTrue();
        loadedSheet.GetCell(2, 2).Should().BeNull();
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void XlsxAdapter_Load_IgnoredErrors_SkipsMalformedReferences()
    {
        var workbook = new Workbook("IgnoredErrorsMalformedRefsTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("00123"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetIgnoredErrors(source, "A0 NotARef XFE1 B2:Bogus", ("numberStoredAsText", "1"));

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);

        loadedSheet.GetCell(1, 1)!.IgnoreFormulaError.Should().BeFalse();
        loadedSheet.GetCell(2, 2).Should().BeNull();
    }

    [Fact]
    public void XlsxAdapter_Save_FreshWorkbook_WritesIgnoredErrorsForIgnoreFormulaErrorCells()
    {
        var workbook = new Workbook("IgnoredErrorsSaveTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("00123"));
        sheet.GetCell(2, 2)!.IgnoreFormulaError = true;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("plain"));
        sheet.GetCell(1, 1)!.IgnoreFormulaError = true;

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var ignoredErrors = worksheetXml.Root!.Element(worksheetNs + "ignoredErrors");
        ignoredErrors.Should().NotBeNull();
        var entries = ignoredErrors!.Elements(worksheetNs + "ignoredError").ToList();
        entries.Select(entry => entry.Attribute("sqref")?.Value).Should().Equal("A1", "B2");
        foreach (var entry in entries)
        {
            entry.Attribute("numberStoredAsText")!.Value.Should().Be("1");
            entry.Attribute("evalError")!.Value.Should().Be("1");
            entry.Attribute("formula")!.Value.Should().Be("1");
            entry.Attribute("emptyCellReference")!.Value.Should().Be("1");
        }
    }

    [Fact]
    public void XlsxAdapter_Save_FreshWorkbook_InsertsIgnoredErrorsBeforeSparklineExtensionList()
    {
        var workbook = new Workbook("IgnoredErrorsWithSparklineTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("2"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("3"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("00123"));
        sheet.GetCell(1, 4)!.IgnoreFormulaError = true;
        sheet.Sparklines.Add(new SparklineModel
        {
            Kind = SparklineKind.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 3)),
            Location = new CellAddress(sheet.Id, 1, 5)
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var orderedChildren = worksheetXml.Root!.Elements().ToList();
        var ignoredErrorsIndex = orderedChildren.FindIndex(element => element.Name == worksheetNs + "ignoredErrors");
        var extensionListIndex = orderedChildren.FindIndex(element => element.Name == worksheetNs + "extLst");

        ignoredErrorsIndex.Should().BeGreaterThanOrEqualTo(0);
        extensionListIndex.Should().BeGreaterThanOrEqualTo(0);
        ignoredErrorsIndex.Should().BeLessThan(extensionListIndex);
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetCellWatches()
    {
        var workbook = new Workbook("CellWatchesRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "1+1");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetCellWatches(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var cellWatches = worksheetXml.Root!.Element(worksheetNs + "cellWatches");
        cellWatches.Should().NotBeNull();
        cellWatches!.ToString().Should().Contain("r=\"A1\"");
    }

    [Fact]
    public void XlsxAdapter_Load_CellWatches_MapsNativeRefsToWorkbookWatchedCells()
    {
        var workbook = new Workbook("CellWatchesLoadTest");
        var firstSheet = workbook.AddSheet("Data");
        var secondSheet = workbook.AddSheet("Summary");
        firstSheet.SetFormula(new CellAddress(firstSheet.Id, 1, 1), "1+1");
        secondSheet.SetFormula(new CellAddress(secondSheet.Id, 3, 3), "2+2");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetCellWatches(
            source,
            "xl/worksheets/sheet1.xml",
            ("A1", null, null),
            ("B2", null, null),
            ("A1", null, null),
            ("A0", null, null),
            ("XFE1", null, null),
            ("NotARef", null, null));
        AddWorksheetCellWatches(
            source,
            "xl/worksheets/sheet2.xml",
            ("C3", null, null));

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedFirstSheet = loaded.GetSheetAt(0);
        var loadedSecondSheet = loaded.GetSheetAt(1);

        loaded.WatchedCells.Should().Equal(
            new CellAddress(loadedFirstSheet.Id, 1, 1),
            new CellAddress(loadedFirstSheet.Id, 2, 2),
            new CellAddress(loadedSecondSheet.Id, 3, 3));
        loadedFirstSheet.GetCell(2, 2).Should().BeNull();
    }

    [Fact]
    public void XlsxAdapter_Save_FreshWorkbook_WritesCellWatchesFromModel()
    {
        var workbook = new Workbook("CellWatchesSaveTest");
        var firstSheet = workbook.AddSheet("Data");
        var secondSheet = workbook.AddSheet("Summary");
        workbook.WatchedCells.Add(new CellAddress(firstSheet.Id, 2, 2));
        workbook.WatchedCells.Add(new CellAddress(firstSheet.Id, 1, 1));
        workbook.WatchedCells.Add(new CellAddress(secondSheet.Id, 3, 3));

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var firstWorksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var secondWorksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet2.xml")!);

        firstWorksheetXml.Root!
            .Element(worksheetNs + "cellWatches")!
            .Elements(worksheetNs + "cellWatch")
            .Select(element => element.Attribute("r")?.Value)
            .Should()
            .Equal("A1", "B2");
        secondWorksheetXml.Root!
            .Element(worksheetNs + "cellWatches")!
            .Elements(worksheetNs + "cellWatch")
            .Select(element => element.Attribute("r")?.Value)
            .Should()
            .Equal("C3");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_MergesNativeCellWatchesWithoutDuplicateRefs()
    {
        var workbook = new Workbook("CellWatchesMergeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "1+1");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetCellWatches(
            source,
            "xl/worksheets/sheet1.xml",
            ("A1", "unsupportedAttr", "kept"),
            ("C3", null, null));

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loaded.WatchedCells.Add(new CellAddress(loadedSheet.Id, 2, 2));
        loaded.GetSheetAt(0).SetCell(new CellAddress(loadedSheet.Id, 4, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var entries = worksheetXml.Root!
            .Element(worksheetNs + "cellWatches")!
            .Elements(worksheetNs + "cellWatch")
            .ToList();

        entries.Select(entry => entry.Attribute("r")?.Value).Should().BeEquivalentTo(["A1", "B2", "C3"]);
        entries.Select(entry => entry.Attribute("r")?.Value).Should().OnlyHaveUniqueItems();
        entries.Single(entry => entry.Attribute("r")?.Value == "A1")
            .Attribute("unsupportedAttr")!
            .Value
            .Should()
            .Be("kept");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_RemovesModeledCellWatches()
    {
        var workbook = new Workbook("CellWatchesRemoveTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "1+1");
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "2+2");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetCellWatches(
            source,
            "xl/worksheets/sheet1.xml",
            ("A1", null, null),
            ("B2", null, null),
            ("NotARef", "unsupportedAttr", "kept"));

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loaded.WatchedCells.Remove(new CellAddress(loadedSheet.Id, 1, 1)).Should().BeTrue();
        loaded.GetSheetAt(0).SetCell(new CellAddress(loadedSheet.Id, 4, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var entries = worksheetXml.Root!
            .Element(worksheetNs + "cellWatches")!
            .Elements(worksheetNs + "cellWatch")
            .ToList();

        var references = entries.Select(entry => entry.Attribute("r")?.Value).ToList();
        references.Should().BeEquivalentTo(["B2", "NotARef"]);
        references.Should().NotContain("A1");
        entries.Single(entry => entry.Attribute("r")?.Value == "NotARef")
            .Attribute("unsupportedAttr")!
            .Value
            .Should()
            .Be("kept");
    }

    [Fact]
    public void XlsxAdapter_Save_FreshWorkbook_InsertsCellWatchesBeforeExtensionList()
    {
        var workbook = new Workbook("CellWatchesWithSparklineTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("1"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("2"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("3"));
        workbook.WatchedCells.Add(new CellAddress(sheet.Id, 5, 5));
        sheet.Sparklines.Add(new SparklineModel
        {
            Kind = SparklineKind.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 3)),
            Location = new CellAddress(sheet.Id, 1, 5)
        });

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var orderedChildren = worksheetXml.Root!.Elements().ToList();
        var cellWatchesIndex = orderedChildren.FindIndex(element => element.Name == worksheetNs + "cellWatches");
        var extensionListIndex = orderedChildren.FindIndex(element => element.Name == worksheetNs + "extLst");

        cellWatchesIndex.Should().BeGreaterThanOrEqualTo(0);
        extensionListIndex.Should().BeGreaterThanOrEqualTo(0);
        cellWatchesIndex.Should().BeLessThan(extensionListIndex);
    }

    [Fact]
    public void XlsxAdapter_Save_FreshWorkbook_InsertsCellWatchesBeforeIgnoredErrors()
    {
        var workbook = new Workbook("CellWatchesWithIgnoredErrorsTest");
        var sheet = workbook.AddSheet("Data");
        var ignoredAddress = new CellAddress(sheet.Id, 1, 1);
        var watchedAddress = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(ignoredAddress, new TextValue("00123"));
        sheet.GetCell(ignoredAddress)!.IgnoreFormulaError = true;
        workbook.WatchedCells.Add(watchedAddress);

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var orderedChildren = worksheetXml.Root!.Elements().ToList();
        var cellWatchesIndex = orderedChildren.FindIndex(element => element.Name == worksheetNs + "cellWatches");
        var ignoredErrorsIndex = orderedChildren.FindIndex(element => element.Name == worksheetNs + "ignoredErrors");

        cellWatchesIndex.Should().BeGreaterThanOrEqualTo(0);
        ignoredErrorsIndex.Should().BeGreaterThanOrEqualTo(0);
        cellWatchesIndex.Should().BeLessThan(ignoredErrorsIndex);
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetCalculationProperties()
    {
        var workbook = new Workbook("WorksheetCalculationPropertiesRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "1+1");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetCalculationProperties(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetCalcPr = worksheetXml.Root!.Element(worksheetNs + "sheetCalcPr");
        sheetCalcPr.Should().NotBeNull();
        sheetCalcPr!.Attribute("fullCalcOnLoad")!.Value.Should().Be("1");
        sheetCalcPr.Attribute("calcId")!.Value.Should().Be("999");
    }

    [Fact]
    public void XlsxAdapter_LoadsWorksheetCalculationPropertiesIntoSheetModel()
    {
        var workbook = new Workbook("CalculationPropertiesLoadTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("calc"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetCalculationProperties(source);

        source.Position = 0;
        var loaded = adapter.Load(source);

        loaded.GetSheetAt(0).FullCalculationOnLoad.Should().BeTrue();
    }

    [Fact]
    public void XlsxAdapter_FreshSave_WritesModeledWorksheetCalculationProperties()
    {
        var workbook = new Workbook("CalculationPropertiesSaveTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("calc"));
        sheet.FullCalculationOnLoad = true;

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetCalcPr = worksheetXml.Root!.Element(worksheetNs + "sheetCalcPr");
        sheetCalcPr.Should().NotBeNull();
        sheetCalcPr!.Attribute("fullCalcOnLoad")!.Value.Should().Be("1");
    }

    [Fact]
    public void XlsxAdapter_FreshSave_WritesWorksheetCalculationPropertiesBeforeSheetProtection()
    {
        var workbook = new Workbook("CalculationPropertiesProtectedSheetOrderTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("calc"));
        sheet.FullCalculationOnLoad = true;
        sheet.IsProtected = true;

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var childNames = worksheetXml.Root!
            .Elements()
            .Select(element => element.Name.LocalName)
            .ToList();

        childNames.IndexOf("sheetCalcPr").Should().BeLessThan(childNames.IndexOf("sheetProtection"));
        worksheetXml.Root!.Element(worksheetNs + "sheetCalcPr")!.Attribute("fullCalcOnLoad")!.Value.Should().Be("1");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_DoesNotResurrectRemovedWorksheetCalculationProperty()
    {
        var workbook = new Workbook("CalculationPropertiesRemovalTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("calc"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetCalculationProperties(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).FullCalculationOnLoad = false;

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheetCalcPr = worksheetXml.Root!.Element(worksheetNs + "sheetCalcPr");
        sheetCalcPr.Should().NotBeNull();
        sheetCalcPr!.Attribute("fullCalcOnLoad").Should().BeNull();
        sheetCalcPr.Attribute("calcId")!.Value.Should().Be("999");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetPhoneticProperties()
    {
        var workbook = new Workbook("PhoneticPropertiesRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kana"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetPhoneticProperties(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var phoneticProperties = worksheetXml.Root!.Element(worksheetNs + "phoneticPr");
        phoneticProperties.Should().NotBeNull();
        phoneticProperties!.Attribute("fontId")!.Value.Should().Be("1");
        phoneticProperties.Attribute("type")!.Value.Should().Be("fullwidthKatakana");
        phoneticProperties.Attribute("alignment")!.Value.Should().Be("center");
        phoneticProperties.Attribute("nativeOnly")!.Value.Should().Be("kept");
    }

    [Fact]
    public void XlsxAdapter_LoadsWorksheetPhoneticPropertiesIntoSheetModel()
    {
        var workbook = new Workbook("PhoneticPropertiesLoadTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kana"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetPhoneticProperties(source);

        source.Position = 0;
        var loaded = adapter.Load(source);

        loaded.GetSheetAt(0).PhoneticProperties.Should().Be(new WorksheetPhoneticProperties(
            "1",
            "fullwidthKatakana",
            "center"));
    }

    [Fact]
    public void XlsxAdapter_FreshSave_WritesModeledWorksheetPhoneticProperties()
    {
        var workbook = new Workbook("PhoneticPropertiesSaveTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kana"));
        sheet.PhoneticProperties = new WorksheetPhoneticProperties("1", "fullwidthKatakana", "center");

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var phoneticProperties = worksheetXml.Root!.Element(worksheetNs + "phoneticPr");

        phoneticProperties.Should().NotBeNull();
        phoneticProperties!.Attribute("fontId")!.Value.Should().Be("1");
        phoneticProperties.Attribute("type")!.Value.Should().Be("fullwidthKatakana");
        phoneticProperties.Attribute("alignment")!.Value.Should().Be("center");
    }

    [Fact]
    public void XlsxAdapter_FreshSave_WritesProtectedRangesBeforeWorksheetPhoneticProperties()
    {
        var workbook = new Workbook("PhoneticPropertiesProtectedRangeOrderTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kana"));
        sheet.PhoneticProperties = new WorksheetPhoneticProperties("1", "fullwidthKatakana", "center");
        sheet.AllowEditRanges.Add(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 1)));

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        var childNames = worksheetXml.Root!
            .Elements()
            .Select(element => element.Name.LocalName)
            .ToList();

        childNames.IndexOf("protectedRanges").Should().BeLessThan(childNames.IndexOf("phoneticPr"));
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_DoesNotDuplicateWorksheetPhoneticProperties()
    {
        var workbook = new Workbook("PhoneticPropertiesMergeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kana"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetPhoneticProperties(source);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var phoneticProperties = worksheetXml.Root!.Elements(worksheetNs + "phoneticPr").ToList();

        phoneticProperties.Should().ContainSingle();
        phoneticProperties[0].Attribute("fontId")!.Value.Should().Be("1");
        phoneticProperties[0].Attribute("nativeOnly")!.Value.Should().Be("kept");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_DoesNotResurrectRemovedWorksheetPhoneticProperties()
    {
        var workbook = new Workbook("PhoneticPropertiesRemovalTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("kana"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetPhoneticProperties(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).PhoneticProperties = null;

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var phoneticProperties = worksheetXml.Root!.Element(worksheetNs + "phoneticPr");

        phoneticProperties.Should().NotBeNull();
        phoneticProperties!.Attribute("fontId").Should().BeNull();
        phoneticProperties.Attribute("type").Should().BeNull();
        phoneticProperties.Attribute("alignment").Should().BeNull();
        phoneticProperties.Attribute("nativeOnly")!.Value.Should().Be("kept");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetSortState()
    {
        var workbook = new Workbook("SortStateRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Name"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("A"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetSortState(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 4, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sortState = worksheetXml.Root!.Element(worksheetNs + "sortState");
        sortState.Should().NotBeNull();
        sortState!.ToString().Should().Contain("ref=\"A1:A3\"");
        sortState.ToString().Should().Contain("descending=\"1\"");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetDataConsolidation()
    {
        var workbook = new Workbook("DataConsolidationRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetDataConsolidation(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 3, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var dataConsolidate = worksheetXml.Root!.Element(worksheetNs + "dataConsolidate");
        dataConsolidate.Should().NotBeNull();
        dataConsolidate!.Attribute("function")!.Value.Should().Be("sum");
        dataConsolidate.Attribute("leftLabels")!.Value.Should().Be("1");
        dataConsolidate.ToString().Should().Contain("ref=\"A1:B2\"");
        dataConsolidate.ToString().Should().Contain("sheet=\"Data\"");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetCustomProperties()
    {
        var workbook = new Workbook("WorksheetCustomPropertiesRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("custom property"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetCustomProperties(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var customProperties = worksheetXml.Root!.Element(worksheetNs + "customProperties");
        customProperties.Should().NotBeNull();
        customProperties!.ToString().Should().Contain("name=\"FreexcelNativeProperty\"");
        customProperties.ToString().Should().Contain("id=\"1\"");
        customProperties.ToString().Should().Contain("unsupportedAttr=\"kept\"");
    }

    [Fact]
    public void XlsxAdapter_LoadsWorksheetCustomPropertiesIntoSheetModel()
    {
        var workbook = new Workbook("WorksheetCustomPropertiesLoadTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("custom property"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetCustomProperties(source);

        source.Position = 0;
        var loaded = adapter.Load(source);

        loaded.GetSheetAt(0).CustomProperties.Should()
            .ContainSingle()
            .Which.Should().Be(new WorksheetCustomProperty("FreexcelNativeProperty", 1));
    }

    [Fact]
    public void XlsxAdapter_FreshSave_WritesModeledWorksheetCustomProperties()
    {
        var workbook = new Workbook("WorksheetCustomPropertiesSaveTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("custom property"));
        sheet.CustomProperties.Add(new WorksheetCustomProperty("FreexcelModeledProperty", 7));

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var customProperty = worksheetXml.Root!
            .Element(worksheetNs + "customProperties")!
            .Elements(worksheetNs + "customPr")
            .Single();

        customProperty.Attribute("name")!.Value.Should().Be("FreexcelModeledProperty");
        customProperty.Attribute("id")!.Value.Should().Be("7");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_DoesNotDuplicateWorksheetCustomProperties()
    {
        var workbook = new Workbook("WorksheetCustomPropertiesMergeTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("custom property"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetCustomProperties(source);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var customProperties = worksheetXml.Root!
            .Element(worksheetNs + "customProperties")!
            .Elements(worksheetNs + "customPr")
            .ToList();

        customProperties.Should().ContainSingle();
        customProperties[0].Attribute("name")!.Value.Should().Be("FreexcelNativeProperty");
        customProperties[0].Attribute("unsupportedAttr")!.Value.Should().Be("kept");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_DoesNotResurrectRemovedWorksheetCustomProperty()
    {
        var workbook = new Workbook("WorksheetCustomPropertiesRemovalTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("custom property"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetCustomProperties(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).CustomProperties.Clear();

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        worksheetXml.Root!
            .Element(worksheetNs + "customProperties")?
            .Elements(worksheetNs + "customPr")
            .Any(property => property.Attribute("name")?.Value == "FreexcelNativeProperty")
            .Should().BeFalse();
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetSmartTags()
    {
        var workbook = new Workbook("WorksheetSmartTagsRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Seattle"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetSmartTags(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var smartTags = worksheetXml.Root!.Element(worksheetNs + "smartTags");
        smartTags.Should().NotBeNull();
        smartTags!.ToString().Should().Contain("r=\"A1\"");
        smartTags.ToString().Should().Contain("type=\"0\"");
        smartTags.ToString().Should().Contain("key=\"place\"");
        smartTags.ToString().Should().Contain("val=\"Seattle\"");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesWorksheetAutoFilterMetadata()
    {
        var workbook = new Workbook("WorksheetAutoFilterRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddWorksheetAutoFilterMetadata(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 4, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
        XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var autoFilter = worksheetXml.Root!.Element(worksheetNs + "autoFilter");
        autoFilter.Should().NotBeNull();
        autoFilter!.Attribute("ref")!.Value.Should().Be("A1:B3");
        autoFilter.ToString().Should().Contain("colId=\"0\"");
        autoFilter.ToString().Should().Contain("blank=\"1\"");
        autoFilter.ToString().Should().Contain("val=\"A\"");
    }

    [Fact]
    public void XlsxAdapter_LoadsPivotTableMetadata()
    {
        var workbook = new Workbook("PivotMetadataTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalPivotTablePackage(source);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var pivotCache = loaded.PivotCaches.Should().ContainSingle().Subject;
        pivotCache.CacheId.Should().Be(1);
        pivotCache.SourceType.Should().Be(PivotCacheSourceType.WorksheetRange);
        pivotCache.SourceSheetName.Should().Be("Data");
        pivotCache.SourceReference.Should().Be("A1:B3");
        pivotCache.PackagePart.Should().Be("xl/pivotCache/pivotCacheDefinition1.xml");
        pivotCache.Fields.Select(field => field.Name).Should().Equal("Category", "Amount");

        var loadedSheet = loaded.GetSheetAt(0);
        var pivotTable = loadedSheet.PivotTables.Should().ContainSingle().Subject;
        pivotTable.Name.Should().Be("PivotTable1");
        pivotTable.CacheId.Should().Be(1);
        pivotTable.TargetRange.Start.ToA1().Should().Be("D3");
        pivotTable.TargetRange.End.ToA1().Should().Be("E5");
        pivotTable.PackagePart.Should().Be("xl/pivotTables/pivotTable1.xml");
        pivotTable.RowFields.Should().ContainSingle().Which.SourceFieldIndex.Should().Be(0);
        pivotTable.DataFields.Should().ContainSingle().Which.Name.Should().Be("Sum of Amount");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesPivotTablePackageReferencesAlongsideModelEdits()
    {
        var workbook = new Workbook("PivotPackageRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalPivotTablePackage(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 4, 1), new TextValue("C"));
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 4, 2), new NumberValue(30));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            archive.GetEntry("xl/pivotCache/pivotCacheDefinition1.xml").Should().NotBeNull();
            archive.GetEntry("xl/pivotTables/pivotTable1.xml").Should().NotBeNull();
            archive.GetEntry("xl/pivotTables/_rels/pivotTable1.xml.rels").Should().NotBeNull();

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            contentTypesXml.ToString().Should().Contain("/xl/pivotCache/pivotCacheDefinition1.xml");
            contentTypesXml.ToString().Should().Contain("/xl/pivotTables/pivotTable1.xml");

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.ToString().Should().Contain("pivotCaches");
            workbookXml.ToString().Should().Contain("rIdFreexcelPivotCache");

            var workbookRelsXml = LoadPackageXml(archive.GetEntry("xl/_rels/workbook.xml.rels")!);
            workbookRelsXml.ToString().Should().Contain("pivotCache/pivotCacheDefinition1.xml");

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.ToString().Should().Contain("pivotTableDefinition");
            worksheetXml.ToString().Should().Contain("rIdFreexcelPivotTable");

            var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
            worksheetRelsXml.ToString().Should().Contain("../pivotTables/pivotTable1.xml");
        }

        saved.Position = 0;
        var roundTripped = adapter.Load(saved);
        roundTripped.GetSheetAt(0).GetValue(4, 1).Should().Be(new TextValue("C"));
        roundTripped.GetSheetAt(0).PivotTables.Should().ContainSingle()
            .Which.Name.Should().Be("PivotTable1");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesNativePivotChartGraphAndCacheBinding()
    {
        var workbook = new Workbook("PivotChartPackageRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalPivotTablePackage(source, pivotTableDefinitionXml: StyledMinimalPivotTableDefinitionXml);
        AddMinimalColumnChartPackage(source, chartXml: MinimalPivotChartXml);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.PivotTables.Should().ContainSingle()
            .Which.CacheId.Should().Be(1);
        loadedSheet.Charts.Should().ContainSingle().Which.Should().Match<ChartModel>(
            chart => chart.IsPivotChart &&
                     chart.PivotTableName == "PivotTable1" &&
                     chart.PivotCacheId == 1);

        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 4, 1), new TextValue("C"));
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 4, 2), new NumberValue(30));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        archive.GetEntry("xl/pivotCache/pivotCacheDefinition1.xml").Should().NotBeNull();
        archive.GetEntry("xl/pivotTables/pivotTable1.xml").Should().NotBeNull();
        archive.GetEntry("xl/drawings/drawing1.xml").Should().NotBeNull();
        archive.GetEntry("xl/drawings/_rels/drawing1.xml.rels").Should().NotBeNull();
        archive.GetEntry("xl/charts/chart1.xml").Should().NotBeNull();

        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        var pivotSource = chartXml.Root!.Element(chartNs + "pivotSource");
        pivotSource.Should().NotBeNull();
        pivotSource!.Element(chartNs + "name")!.Value.Should().Be("Data!PivotTable1");
        pivotSource.Element(chartNs + "fmtId")!.Attribute("val")!.Value.Should().Be("0");

        var pivotTableXml = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!);
        pivotTableXml.ToString().Should().Contain("pivotTableStyleInfo");
        pivotTableXml.ToString().Should().Contain("PivotStyleMedium9");

        var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
        contentTypesXml.ToString().Should().Contain("/xl/pivotCache/pivotCacheDefinition1.xml");
        contentTypesXml.ToString().Should().Contain("/xl/pivotTables/pivotTable1.xml");
        contentTypesXml.ToString().Should().Contain("/xl/drawings/drawing1.xml");
        contentTypesXml.ToString().Should().Contain("/xl/charts/chart1.xml");

        var workbookRelsXml = LoadPackageXml(archive.GetEntry("xl/_rels/workbook.xml.rels")!);
        workbookRelsXml.ToString().Should().Contain("pivotCache/pivotCacheDefinition1.xml");

        var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
        worksheetRelsXml.ToString().Should().Contain("../pivotTables/pivotTable1.xml");
        worksheetRelsXml.ToString().Should().Contain("../drawings/drawing1.xml");

        var drawingRelsXml = LoadPackageXml(archive.GetEntry("xl/drawings/_rels/drawing1.xml.rels")!);
        drawingRelsXml.ToString().Should().Contain("../charts/chart1.xml");

        var pivotTableRelsXml = LoadPackageXml(archive.GetEntry("xl/pivotTables/_rels/pivotTable1.xml.rels")!);
        pivotTableRelsXml.ToString().Should().Contain("../pivotCache/pivotCacheDefinition1.xml");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_ResolvesCrossSheetNativePivotChartCacheBinding()
    {
        var workbook = new Workbook("CrossSheetPivotChartPackageRetentionTest");
        var dataSheet = workbook.AddSheet("Data");
        var dashboardSheet = workbook.AddSheet("Dashboard");
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 1), new TextValue("Category"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 1, 2), new TextValue("Amount"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 2, 1), new TextValue("A"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 2, 2), new NumberValue(10));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 3, 1), new TextValue("B"));
        dataSheet.SetCell(new CellAddress(dataSheet.Id, 3, 2), new NumberValue(20));
        dashboardSheet.SetCell(new CellAddress(dashboardSheet.Id, 1, 1), new TextValue("Dashboard"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalPivotTablePackage(source);
        AddMinimalColumnChartPackage(source, worksheetPath: "xl/worksheets/sheet2.xml", chartXml: MinimalPivotChartXml);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var loadedDataSheet = loaded.GetSheetAt(0);
        loadedDataSheet.PivotTables.Should().ContainSingle()
            .Which.CacheId.Should().Be(1);
        var loadedDashboardSheet = loaded.GetSheetAt(1);
        loadedDashboardSheet.Charts.Should().ContainSingle().Which.Should().Match<ChartModel>(
            chart => chart.IsPivotChart &&
                     chart.PivotSourceSheetName == "Data" &&
                     chart.PivotTableName == "PivotTable1" &&
                     chart.PivotCacheId == 1);

        loadedDashboardSheet.SetCell(new CellAddress(loadedDashboardSheet.Id, 2, 1), new TextValue("edited"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: false);
        var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
        XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        chartXml.Root!
            .Element(chartNs + "pivotSource")!
            .Element(chartNs + "name")!
            .Value.Should().Be("Data!PivotTable1");
    }

    [Fact]
    public void XlsxAdapter_Save_WritesAuthoredPivotChartStyleMetadata()
    {
        var workbook = new Workbook("AuthoredPivotChartStyleTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            PivotFormatsXml = """
                <c:pivotFmts xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                             xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
                  <c:pivotFmt>
                    <c:idx val="0"/>
                    <c:spPr><a:solidFill><a:srgbClr val="4472C4"/></a:solidFill></c:spPr>
                  </c:pivotFmt>
                </c:pivotFmts>
                """,
            ChartStyleId = 42,
            Uses1904DateSystem = true,
            Language = "en-US",
            ColorMapOverride = new ChartColorMapOverrideModel
            {
                OverrideMappings =
                {
                    ["bg1"] = "lt1",
                    ["tx1"] = "dk1",
                    ["accent1"] = "accent2"
                }
            },
            ExternalData = new ChartExternalDataModel
            {
                RelationshipId = "rIdExternalData1",
                RelationshipType = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/package",
                Target = "linked-pivot-source.xlsx",
                TargetMode = "External",
                AutoUpdate = true
            },
            PlotAreaLayout = new ChartManualLayoutModel
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
            },
            LegendLayout = new ChartManualLayoutModel
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
            },
            ThreeDView = new Chart3DViewModel
            {
                RotationX = 20,
                HeightPercent = 150,
                RotationY = 30,
                DepthPercent = 200,
                RightAngleAxes = false,
                Perspective = 45
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
                    BlackAndWhite = true,
                    Draft = false
                }
            },
            RoundedCorners = true,
            BlankDisplayMode = ChartBlankDisplayMode.Zero,
            ShowDataLabelsOverMaximum = true,
            AutoTitleDeleted = true,
            ShowDataInHiddenRowsAndColumns = true,
            Protection = new ChartProtectionModel
            {
                ChartObject = true,
                Data = true,
                Formatting = false,
                Selection = true,
                UserInterface = true
            }
        };
        sheet.Charts.Add(chart);

        var saved = new MemoryStream();
        new XlsxFileAdapter().Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
            XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            chartXml.Root!.Element(chartNs + "date1904")!.Attribute("val")!.Value.Should().Be("1");
            chartXml.Root.Element(chartNs + "lang")!.Attribute("val")!.Value.Should().Be("en-US");
            var colorMap = chartXml.Root.Element(chartNs + "clrMapOvr")!
                .Element(drawingNs + "overrideClrMapping")!;
            colorMap.Attribute("bg1")!.Value.Should().Be("lt1");
            colorMap.Attribute("tx1")!.Value.Should().Be("dk1");
            colorMap.Attribute("accent1")!.Value.Should().Be("accent2");
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var externalData = chartXml.Root.Element(chartNs + "externalData")!;
            externalData.Attribute(relNs + "id")!.Value.Should().Be("rIdExternalData1");
            externalData.Element(chartNs + "autoUpdate")!.Attribute("val")!.Value.Should().Be("1");
            var chartRelsXml = LoadPackageXml(archive.GetEntry("xl/charts/_rels/chart1.xml.rels")!);
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            var externalRelationship = chartRelsXml.Root!
                .Elements(packageRelNs + "Relationship")
                .Where(relationship => relationship.Attribute("Id")?.Value == "rIdExternalData1")
                .Should().ContainSingle()
                .Which;
            externalRelationship.Attribute("Type")!.Value.Should().Be("http://schemas.openxmlformats.org/officeDocument/2006/relationships/package");
            externalRelationship.Attribute("Target")!.Value.Should().Be("linked-pivot-source.xlsx");
            externalRelationship.Attribute("TargetMode")!.Value.Should().Be("External");
            var plotAreaManualLayout = chartXml.Root.Element(chartNs + "chart")!
                .Element(chartNs + "plotArea")!
                .Element(chartNs + "layout")!
                .Element(chartNs + "manualLayout")!;
            plotAreaManualLayout.Element(chartNs + "layoutTarget")!.Attribute("val")!.Value.Should().Be("outer");
            plotAreaManualLayout.Element(chartNs + "xMode")!.Attribute("val")!.Value.Should().Be("factor");
            plotAreaManualLayout.Element(chartNs + "yMode")!.Attribute("val")!.Value.Should().Be("edge");
            plotAreaManualLayout.Element(chartNs + "wMode")!.Attribute("val")!.Value.Should().Be("factor");
            plotAreaManualLayout.Element(chartNs + "hMode")!.Attribute("val")!.Value.Should().Be("factor");
            plotAreaManualLayout.Element(chartNs + "x")!.Attribute("val")!.Value.Should().Be("0.1");
            plotAreaManualLayout.Element(chartNs + "y")!.Attribute("val")!.Value.Should().Be("0.2");
            plotAreaManualLayout.Element(chartNs + "w")!.Attribute("val")!.Value.Should().Be("0.8");
            plotAreaManualLayout.Element(chartNs + "h")!.Attribute("val")!.Value.Should().Be("0.6");
            var legendManualLayout = chartXml.Root.Element(chartNs + "chart")!
                .Element(chartNs + "legend")!
                .Element(chartNs + "layout")!
                .Element(chartNs + "manualLayout")!;
            legendManualLayout.Element(chartNs + "layoutTarget")!.Attribute("val")!.Value.Should().Be("inner");
            legendManualLayout.Element(chartNs + "x")!.Attribute("val")!.Value.Should().Be("0.76");
            legendManualLayout.Element(chartNs + "h")!.Attribute("val")!.Value.Should().Be("0.7");
            var view3D = chartXml.Root.Element(chartNs + "chart")!.Element(chartNs + "view3D")!;
            view3D.Element(chartNs + "rotX")!.Attribute("val")!.Value.Should().Be("20");
            view3D.Element(chartNs + "hPercent")!.Attribute("val")!.Value.Should().Be("150");
            view3D.Element(chartNs + "rotY")!.Attribute("val")!.Value.Should().Be("30");
            view3D.Element(chartNs + "depthPercent")!.Attribute("val")!.Value.Should().Be("200");
            view3D.Element(chartNs + "rAngAx")!.Attribute("val")!.Value.Should().Be("0");
            view3D.Element(chartNs + "perspective")!.Attribute("val")!.Value.Should().Be("45");
            var printSettings = chartXml.Root.Element(chartNs + "printSettings")!;
            var pageMargins = printSettings.Element(chartNs + "pageMargins")!;
            pageMargins.Attribute("l")!.Value.Should().Be("0.7");
            pageMargins.Attribute("r")!.Value.Should().Be("0.7");
            pageMargins.Attribute("t")!.Value.Should().Be("0.75");
            pageMargins.Attribute("b")!.Value.Should().Be("0.75");
            pageMargins.Attribute("header")!.Value.Should().Be("0.3");
            pageMargins.Attribute("footer")!.Value.Should().Be("0.3");
            var pageSetup = printSettings.Element(chartNs + "pageSetup")!;
            pageSetup.Attribute("paperSize")!.Value.Should().Be("9");
            pageSetup.Attribute("orientation")!.Value.Should().Be("landscape");
            pageSetup.Attribute("copies")!.Value.Should().Be("2");
            pageSetup.Attribute("blackAndWhite")!.Value.Should().Be("1");
            pageSetup.Attribute("draft")!.Value.Should().Be("0");
            chartXml.Root!.Element(chartNs + "style")!.Attribute("val")!.Value.Should().Be("42");
            var pivotFormats = chartXml.Root.Element(chartNs + "chart")!.Element(chartNs + "pivotFmts")!;
            pivotFormats.Element(chartNs + "pivotFmt")!.Element(chartNs + "idx")!.Attribute("val")!.Value.Should().Be("0");
            pivotFormats.ToString().Should().Contain("4472C4");
            chartXml.Root.Element(chartNs + "roundedCorners")!.Attribute("val")!.Value.Should().Be("1");
            var protection = chartXml.Root.Element(chartNs + "protection")!;
            protection.Attribute("chartObject")!.Value.Should().Be("1");
            protection.Attribute("data")!.Value.Should().Be("1");
            protection.Attribute("formatting")!.Value.Should().Be("0");
            protection.Attribute("selection")!.Value.Should().Be("1");
            protection.Attribute("userInterface")!.Value.Should().Be("1");
            chartXml.Root.Element(chartNs + "chart")!.Element(chartNs + "autoTitleDeleted")!.Attribute("val")!.Value.Should().Be("1");
            chartXml.Root.Element(chartNs + "chart")!.Element(chartNs + "plotVisOnly")!.Attribute("val")!.Value.Should().Be("0");
            chartXml.Root.Element(chartNs + "chart")!.Element(chartNs + "dispBlanksAs")!.Attribute("val")!.Value.Should().Be("zero");
            chartXml.Root.Element(chartNs + "chart")!.Element(chartNs + "showDLblsOverMax")!.Attribute("val")!.Value.Should().Be("1");
            chartXml.Root.Element(chartNs + "pivotSource").Should().NotBeNull();
        }

        saved.Position = 0;
        var loaded = new XlsxFileAdapter().Load(saved);
        var loadedChart = loaded.GetSheetAt(0).Charts.Should().ContainSingle().Which;
        loadedChart.ChartStyleId.Should().Be(42);
        loadedChart.PivotFormatsXml.Should().Contain("4472C4");
        loadedChart.Uses1904DateSystem.Should().BeTrue();
        loadedChart.Language.Should().Be("en-US");
        loadedChart.ColorMapOverride.Should().BeEquivalentTo(chart.ColorMapOverride);
        loadedChart.ExternalData.Should().BeEquivalentTo(chart.ExternalData);
        loadedChart.PlotAreaLayout.Should().BeEquivalentTo(chart.PlotAreaLayout);
        loadedChart.LegendLayout.Should().BeEquivalentTo(chart.LegendLayout);
        loadedChart.ThreeDView.Should().BeEquivalentTo(chart.ThreeDView);
        loadedChart.PrintSettings.Should().BeEquivalentTo(chart.PrintSettings);
        loadedChart.RoundedCorners.Should().BeTrue();
        loadedChart.BlankDisplayMode.Should().Be(ChartBlankDisplayMode.Zero);
        loadedChart.ShowDataLabelsOverMaximum.Should().BeTrue();
        loadedChart.AutoTitleDeleted.Should().BeTrue();
        loadedChart.ShowDataInHiddenRowsAndColumns.Should().BeTrue();
        loadedChart.Protection.Should().BeEquivalentTo(new ChartProtectionModel
        {
            ChartObject = true,
            Data = true,
            Formatting = false,
            Selection = true,
            UserInterface = true
        });
    }

    [Fact]
    public void XlsxAdapter_Save_WritesAuthoredPivotTablePackageParts()
    {
        var workbook = new Workbook("AuthoredPivotXlsxTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B3"
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Category"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount"));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 7, 2)),
            CompactRowLabelIndent = 4,
            ShowExpandCollapseButtons = false,
            AutofitColumnsOnUpdate = false,
            PreserveFormattingOnUpdate = false,
            PrintTitles = true,
            PrintExpandCollapseButtons = true,
            AltTextTitle = "Sales pivot",
            AltTextDescription = "Pivot summary for sales"
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum", 4));
        sheet.PivotTables.Add(pivot);

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            archive.GetEntry("xl/pivotCache/pivotCacheDefinition1.xml").Should().NotBeNull();
            archive.GetEntry("xl/pivotTables/pivotTable1.xml").Should().NotBeNull();
            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.ToString().Should().Contain("pivotCaches");
            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.ToString().Should().Contain("pivotTableDefinition");
            var pivotXml = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!);
            pivotXml.ToString().Should().Contain("rowFields");
            pivotXml.ToString().Should().Contain("dataFields");
            pivotXml.Root!.Attribute("itemPrintTitles")!.Value.Should().Be("1");
            pivotXml.Root!.Attribute("fieldPrintTitles")!.Value.Should().Be("1");
            pivotXml.Root!.Attribute("showDrill")!.Value.Should().Be("0");
            pivotXml.Root!.Attribute("applyWidthHeightFormats")!.Value.Should().Be("0");
            pivotXml.Root!.Attribute("preserveFormatting")!.Value.Should().Be("0");
            pivotXml.Root!.Attribute("printDrill")!.Value.Should().Be("1");
            pivotXml.Root!.Attribute("indent")!.Value.Should().Be("4");
            pivotXml.Root!.Attribute("altText")!.Value.Should().Be("Sales pivot");
            pivotXml.Root!.Attribute("altTextSummary")!.Value.Should().Be("Pivot summary for sales");
        }

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        loaded.PivotCaches.Should().ContainSingle().Which.Fields.Select(field => field.Name)
            .Should().Equal("Category", "Amount");
        var loadedPivot = loaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject;
        loadedPivot.DataFields.Should().ContainSingle().Which.NumberFormatId.Should().Be(4);
        loadedPivot.CompactRowLabelIndent.Should().Be(4);
        loadedPivot.ShowExpandCollapseButtons.Should().BeFalse();
        loadedPivot.AutofitColumnsOnUpdate.Should().BeFalse();
        loadedPivot.PreserveFormattingOnUpdate.Should().BeFalse();
        loadedPivot.PrintTitles.Should().BeTrue();
        loadedPivot.PrintExpandCollapseButtons.Should().BeTrue();
        loadedPivot.AltTextTitle.Should().Be("Sales pivot");
        loadedPivot.AltTextDescription.Should().Be("Pivot summary for sales");
    }

    [Fact]
    public void XlsxAdapter_SaveLoad_RoundTripsPivotCustomValueNumberFormatCatalog()
    {
        var workbook = new Workbook("AuthoredPivotCustomNumberFormatXlsxTest");
        workbook.NumberFormatCatalog[165] = "#,##0.0 \"kg\"";
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B2"
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Category"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount"));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 7, 2))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(
            1,
            "Sum of Amount",
            "sum",
            NumberFormatId: 165,
            NumberFormatCode: "#,##0.0 \"kg\""));
        sheet.PivotTables.Add(pivot);

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var stylesXml = LoadPackageXml(archive.GetEntry("xl/styles.xml")!);
            stylesXml.ToString().Should().Contain("numFmtId=\"165\"");
            stylesXml.ToString().Should().Contain("formatCode=\"#,##0.0 &quot;kg&quot;\"");
            var pivotXml = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!);
            pivotXml.ToString().Should().Contain("numFmtId=\"165\"");
        }

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        loaded.NumberFormatCatalog.Should().Contain(165, "#,##0.0 \"kg\"");
        var loadedField = loaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject.DataFields
            .Should().ContainSingle().Subject;
        loadedField.NumberFormatId.Should().Be(165);
        loadedField.NumberFormatCode.Should().Be("#,##0.0 \"kg\"");
    }

    [Fact]
    public void XlsxAdapter_SaveLoad_RemapPivotCustomNumberFormatWhenCellStylesUseSameId()
    {
        var workbook = new Workbook("PivotCustomNumberFormatCollisionXlsxTest");
        workbook.NumberFormatCatalog[164] = "#,##0.0 \"kg\"";
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        var conflictingStyle = CellStyle.Default.Clone();
        conflictingStyle.NumberFormat = "0.0000";
        var styledCell = Cell.FromValue(new NumberValue(10));
        styledCell.StyleId = workbook.RegisterStyle(conflictingStyle);
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), styledCell);
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B2"
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Category"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount"));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 7, 2))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(
            1,
            "Sum of Amount",
            "sum",
            NumberFormatId: 164,
            NumberFormatCode: "#,##0.0 \"kg\""));
        sheet.PivotTables.Add(pivot);

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var stylesText = LoadPackageXml(archive.GetEntry("xl/styles.xml")!).ToString();
            stylesText.Should().Contain("formatCode=\"0.0000\"");
            stylesText.Should().Contain("formatCode=\"#,##0.0 &quot;kg&quot;\"");
            var pivotText = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!).ToString();
            pivotText.Should().NotContain("numFmtId=\"164\"");
        }

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        var loadedField = loaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject.DataFields
            .Should().ContainSingle().Subject;
        loadedField.NumberFormatId.Should().NotBe(164);
        loadedField.NumberFormatCode.Should().Be("#,##0.0 \"kg\"");
        loaded.NumberFormatCatalog[loadedField.NumberFormatId!.Value].Should().Be("#,##0.0 \"kg\"");
    }

    [Fact]
    public void XlsxAdapter_SaveLoadedWorkbook_RemapPreservedPivotCustomNumberFormatWhenCellStylesUseSameId()
    {
        var source = new Workbook("LoadedPivotCustomNumberFormatCollisionXlsxTest");
        source.NumberFormatCatalog[164] = "#,##0.0 \"kg\"";
        var sourceSheet = source.AddSheet("Data");
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 1), new TextValue("Category"));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 1, 2), new TextValue("Amount"));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 2, 1), new TextValue("A"));
        sourceSheet.SetCell(new CellAddress(sourceSheet.Id, 2, 2), new NumberValue(10));
        source.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B2"
        });
        source.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Category"));
        source.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount"));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sourceSheet.Id, 1, 1), new CellAddress(sourceSheet.Id, 2, 2)),
            TargetRange = new GridRange(new CellAddress(sourceSheet.Id, 5, 1), new CellAddress(sourceSheet.Id, 7, 2))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(
            1,
            "Sum of Amount",
            "sum",
            NumberFormatId: 164,
            NumberFormatCode: "#,##0.0 \"kg\""));
        sourceSheet.PivotTables.Add(pivot);

        var adapter = new XlsxFileAdapter();
        var sourceBytes = new MemoryStream();
        adapter.Save(source, sourceBytes);
        sourceBytes.Position = 0;

        var loaded = adapter.Load(sourceBytes);
        var loadedSheet = loaded.GetSheetAt(0);
        var conflictingStyle = CellStyle.Default.Clone();
        conflictingStyle.NumberFormat = "0.0000";
        var styledCell = Cell.FromValue(new NumberValue(10));
        styledCell.StyleId = loaded.RegisterStyle(conflictingStyle);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 2, 2), styledCell);

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var stylesText = LoadPackageXml(archive.GetEntry("xl/styles.xml")!).ToString();
            stylesText.Should().Contain("formatCode=\"0.0000\"");
            stylesText.Should().Contain("formatCode=\"#,##0.0 &quot;kg&quot;\"");
            var pivotText = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!).ToString();
            pivotText.Should().NotContain("numFmtId=\"164\"");
        }

        saved.Position = 0;
        var reloaded = adapter.Load(saved);
        var reloadedField = reloaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject.DataFields
            .Should().ContainSingle().Subject;
        reloadedField.NumberFormatId.Should().NotBe(164);
        reloadedField.NumberFormatCode.Should().Be("#,##0.0 \"kg\"");
        reloaded.NumberFormatCatalog[reloadedField.NumberFormatId!.Value].Should().Be("#,##0.0 \"kg\"");
    }

    [Fact]
    public void XlsxAdapter_SaveLoad_RoundTripsPivotCacheRefreshFlags()
    {
        var workbook = new Workbook("PivotCacheFlagsRoundTrip");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:B2",
            RefreshOnLoad = false,
            SaveData = false,
            EnableRefresh = false,
            MissingItemsLimit = 0,
            RefreshedVersion = 7,
            RefreshedBy = "Freexcel Tests"
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount"));
        workbook.PivotCaches.Add(cache);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 2)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 4, 1), new CellAddress(sheet.Id, 7, 3))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var cacheXml = LoadPackageXml(archive.GetEntry("xl/pivotCache/pivotCacheDefinition1.xml")!).ToString();
            cacheXml.Should().Contain("refreshOnLoad=\"0\"");
            cacheXml.Should().Contain("saveData=\"0\"");
            cacheXml.Should().Contain("enableRefresh=\"0\"");
            cacheXml.Should().Contain("missingItemsLimit=\"0\"");
            cacheXml.Should().Contain("refreshedVersion=\"7\"");
            cacheXml.Should().Contain("refreshedBy=\"Freexcel Tests\"");
        }

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        var loadedCache = loaded.PivotCaches.Should().ContainSingle().Subject;
        loadedCache.RefreshOnLoad.Should().BeFalse();
        loadedCache.SaveData.Should().BeFalse();
        loadedCache.EnableRefresh.Should().BeFalse();
        loadedCache.MissingItemsLimit.Should().Be(0);
        loadedCache.RefreshedVersion.Should().Be(7);
        loadedCache.RefreshedBy.Should().Be("Freexcel Tests");
    }

    [Fact]
    public void XlsxAdapter_SaveLoad_RoundTripsExternalOlapPivotCacheMetadata()
    {
        var workbook = new Workbook("ExternalOlapPivotCacheTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalPivotTablePackage(source, pivotCacheDefinitionXml: ExternalOlapPivotCacheDefinitionXml);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedCache = loaded.PivotCaches.Should().ContainSingle().Subject;
        loadedCache.SourceType.Should().Be(PivotCacheSourceType.External);
        loadedCache.IsOlap.Should().BeTrue();
        loadedCache.ConnectionId.Should().Be(2);
        loadedCache.SourceSheetName.Should().BeNull();
        loadedCache.SourceReference.Should().BeNull();

        var authored = new Workbook("AuthoredExternalOlapPivotCacheTest");
        var authoredSheet = authored.AddSheet("Data");
        authoredSheet.SetCell(new CellAddress(authoredSheet.Id, 1, 1), new TextValue("Category"));
        authoredSheet.SetCell(new CellAddress(authoredSheet.Id, 1, 2), new TextValue("Amount"));
        var cache = new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.External,
            IsOlap = true,
            ConnectionId = 2,
            RefreshOnLoad = false,
            SaveData = true,
            EnableRefresh = true
        };
        cache.Fields.Add(new PivotCacheFieldModel("Category"));
        cache.Fields.Add(new PivotCacheFieldModel("Amount"));
        authored.PivotCaches.Add(cache);
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(authoredSheet.Id, 1, 1), new CellAddress(authoredSheet.Id, 2, 2)),
            TargetRange = new GridRange(new CellAddress(authoredSheet.Id, 4, 1), new CellAddress(authoredSheet.Id, 6, 2))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        authoredSheet.PivotTables.Add(pivot);

        var saved = new MemoryStream();
        adapter.Save(authored, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var cacheXml = LoadPackageXml(archive.GetEntry("xl/pivotCache/pivotCacheDefinition1.xml")!).ToString();
            cacheXml.Should().Contain("olap=\"1\"");
            cacheXml.Should().Contain("type=\"external\"");
            cacheXml.Should().Contain("connectionId=\"2\"");
            cacheXml.Should().NotContain("worksheetSource");
        }

        saved.Position = 0;
        var roundTripped = adapter.Load(saved);
        var roundTrippedCache = roundTripped.PivotCaches.Should().ContainSingle().Subject;
        roundTrippedCache.SourceType.Should().Be(PivotCacheSourceType.External);
        roundTrippedCache.IsOlap.Should().BeTrue();
        roundTrippedCache.ConnectionId.Should().Be(2);
    }

    [Fact]
    public void XlsxAdapter_LoadSave_RoundTripsPivotCacheSharedItemMetadata()
    {
        var workbook = new Workbook("PivotSharedItemsMetadataTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalPivotTablePackage(source);

        var loaded = adapter.Load(source);

        var fields = loaded.PivotCaches.Should().ContainSingle().Subject.Fields;
        fields[0].SharedItemCount.Should().Be(2);
        fields[0].ContainsString.Should().BeTrue();
        fields[0].SharedItems.Should().Equal("A", "B");
        fields[1].SharedItemCount.Should().Be(2);
        fields[1].ContainsNumber.Should().BeTrue();
        fields[1].SharedItems.Should().Equal("10", "20");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var cacheXml = LoadPackageXml(archive.GetEntry("xl/pivotCache/pivotCacheDefinition1.xml")!).ToString();
        cacheXml.Should().Contain("<s v=\"A\"");
        cacheXml.Should().Contain("<s v=\"B\"");
        cacheXml.Should().Contain("<n v=\"10\"");
        cacheXml.Should().Contain("<n v=\"20\"");
        cacheXml.Should().Contain("containsNumber=\"1\"");
        cacheXml.Should().Contain("count=\"2\"");
    }

    [Fact]
    public void XlsxAdapter_LoadSave_RoundTripsPivotCacheSharedItemEdgeMetadata()
    {
        var workbook = new Workbook("PivotSharedItemEdgeMetadataTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalPivotTablePackage(source, pivotCacheDefinitionXml: PivotCacheDefinitionWithSharedItemEdgeMetadataXml);

        var loaded = adapter.Load(source);

        var fields = loaded.PivotCaches.Should().ContainSingle().Subject.Fields;
        var mixed = fields[0];
        mixed.ContainsMixedTypes.Should().BeTrue();
        mixed.ContainsSemiMixedTypes.Should().BeTrue();
        mixed.ContainsLongText.Should().BeTrue();
        mixed.SharedItems.Should().Equal("Short", "Long text sample");
        var numeric = fields[1];
        numeric.ContainsInteger.Should().BeTrue();
        numeric.ContainsNonDate.Should().BeTrue();
        numeric.MinValue.Should().Be(10);
        numeric.MaxValue.Should().Be(20);
        numeric.SharedItems.Should().Equal("10", "20");
        var date = fields[2];
        date.ContainsDate.Should().BeTrue();
        date.MinDate.Should().Be("2026-01-01T00:00:00");
        date.MaxDate.Should().Be("2026-03-31T00:00:00");
        date.SharedItems.Should().Equal("2026-01-01T00:00:00", "2026-03-31T00:00:00");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var cacheXml = LoadPackageXml(archive.GetEntry("xl/pivotCache/pivotCacheDefinition1.xml")!).ToString();
        cacheXml.Should().Contain("containsMixedTypes=\"1\"");
        cacheXml.Should().Contain("containsSemiMixedTypes=\"1\"");
        cacheXml.Should().Contain("longText=\"1\"");
        cacheXml.Should().Contain("containsInteger=\"1\"");
        cacheXml.Should().Contain("containsNonDate=\"1\"");
        cacheXml.Should().Contain("minValue=\"10\"");
        cacheXml.Should().Contain("maxValue=\"20\"");
        cacheXml.Should().Contain("minDate=\"2026-01-01T00:00:00\"");
        cacheXml.Should().Contain("maxDate=\"2026-03-31T00:00:00\"");
        cacheXml.Should().Contain("<s v=\"Short\"");
        cacheXml.Should().Contain("<n v=\"20\"");
        cacheXml.Should().Contain("<d v=\"2026-03-31T00:00:00\"");
    }

    [Fact]
    public void XlsxAdapter_LoadSave_ImportsNativePivotHiddenItemSelections()
    {
        var workbook = new Workbook("PivotHiddenItemSelectionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalPivotTablePackage(source, pivotTableDefinitionXml: PivotTableDefinitionWithHiddenItemsXml);

        var loaded = adapter.Load(source);

        var pivotTable = loaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject;
        pivotTable.RowFields.Should().ContainSingle()
            .Which.SelectedItems.Should().Equal("A");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        var roundTripped = adapter.Load(saved);
        roundTripped.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject.RowFields
            .Should().ContainSingle()
            .Which.SelectedItems.Should().Equal("A");
    }

    [Fact]
    public void XlsxAdapter_LoadSave_ImportsNativePivotFilters()
    {
        var workbook = new Workbook("PivotNativeFilterTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalPivotTablePackage(source, pivotTableDefinitionXml: PivotTableDefinitionWithNativeFiltersXml);

        var loaded = adapter.Load(source);

        var pivotTable = loaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject;
        pivotTable.LabelFilters.Should().ContainSingle()
            .Which.Should().Be(new PivotLabelFilterModel(0, PivotLabelFilterKind.Contains, "A"));
        pivotTable.ValueFilters.Should().ContainSingle()
            .Which.Should().Be(new PivotValueFilterModel(0, PivotValueFilterKind.GreaterThan, ComparisonValue: 15, SourceFieldIndex: 0));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        var roundTripped = adapter.Load(saved);
        var roundTrippedPivot = roundTripped.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject;
        roundTrippedPivot.LabelFilters.Should().ContainSingle()
            .Which.Should().Be(new PivotLabelFilterModel(0, PivotLabelFilterKind.Contains, "A"));
        roundTrippedPivot.ValueFilters.Should().ContainSingle()
            .Which.Should().Be(new PivotValueFilterModel(0, PivotValueFilterKind.GreaterThan, ComparisonValue: 15, SourceFieldIndex: 0));
    }

    [Fact]
    public void XlsxAdapter_LoadSave_ImportsNativePivotFieldSorts()
    {
        var workbook = new Workbook("PivotNativeSortTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalPivotTablePackage(source, pivotTableDefinitionXml: PivotTableDefinitionWithNativeSortXml);

        var loaded = adapter.Load(source);

        loaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject.Sorts
            .Should().ContainSingle()
            .Which.Should().Be(new PivotSortModel(PivotSortTarget.Label, PivotSortDirection.Descending, FieldIndex: 0));
    }

    [Fact]
    public void XlsxAdapter_LoadSave_ImportsNativePivotFieldGrouping()
    {
        var workbook = new Workbook("PivotNativeGroupingTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalPivotTablePackage(source, pivotTableDefinitionXml: PivotTableDefinitionWithNativeGroupingXml);

        var loaded = adapter.Load(source);

        var rowField = loaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject.RowFields
            .Should().ContainSingle()
            .Subject;
        rowField.Grouping.Should().Be(PivotFieldGrouping.NumberRange);
        rowField.GroupStart.Should().Be(0);
        rowField.GroupEnd.Should().Be(100);
        rowField.GroupInterval.Should().Be(10);
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesNativePivotCacheRecordsRelationship()
    {
        var workbook = new Workbook("PivotCacheRecordsRetentionTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalPivotTablePackage(source, includeCacheRecords: true);

        var loaded = adapter.Load(source);
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 10, 1), new TextValue("ordinary edit"));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        archive.GetEntry("xl/pivotCache/pivotCacheRecords1.xml").Should().NotBeNull();
        var contentTypes = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!).ToString();
        contentTypes.Should().Contain("/xl/pivotCache/pivotCacheRecords1.xml");
        var cacheRels = LoadPackageXml(archive.GetEntry("xl/pivotCache/_rels/pivotCacheDefinition1.xml.rels")!).ToString();
        cacheRels.Should().Contain("pivotCacheRecords1.xml");
        var recordsXml = LoadPackageXml(archive.GetEntry("xl/pivotCache/pivotCacheRecords1.xml")!).ToString();
        recordsXml.Should().Contain("<r>");
        recordsXml.Should().Contain("<x v=\"0\"");
    }

    [Fact]
    public void XlsxAdapter_Save_RoundTripsExpandedPivotTableFields()
    {
        var workbook = new Workbook("ExpandedPivotXlsxTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Quarter"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Channel"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Market"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue("Retail"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new TextValue("Domestic"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new NumberValue(10));
        workbook.PivotCaches.Add(new PivotCacheModel
        {
            CacheId = 1,
            SourceType = PivotCacheSourceType.WorksheetRange,
            SourceSheetName = "Data",
            SourceReference = "A1:E2"
        });
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Region"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Quarter"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Channel"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Market"));
        workbook.PivotCaches[0].Fields.Add(new PivotCacheFieldModel("Amount"));
        var pivot = new PivotTableModel
        {
            Name = "PivotTable1",
            CacheId = 1,
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 5)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 9, 5))
        };
        pivot.ShowRowGrandTotals = false;
        pivot.ShowColumnGrandTotals = true;
        pivot.RepeatItemLabels = false;
        pivot.BlankLineAfterItems = true;
        pivot.ReportLayout = PivotReportLayout.Compact;
        pivot.StyleName = "PivotStyleMedium9";
        pivot.ShowRowHeaders = false;
        pivot.ShowColumnHeaders = true;
        pivot.ShowRowStripes = true;
        pivot.ShowColumnStripes = true;
        pivot.RowFields.Add(new PivotFieldModel(0, SelectedItems: ["East"], Grouping: PivotFieldGrouping.NumberRange, GroupStart: 0, GroupInterval: 10));
        pivot.ColumnFields.Add(new PivotFieldModel(1, SelectedItems: ["Q1"]));
        pivot.ColumnFields.Add(new PivotFieldModel(2));
        pivot.PageFields.Add(new PivotFieldModel(3, SelectedItem: "Domestic", SelectedItems: ["Domestic", "Export"]));
        pivot.ShowSubtotals = true;
        pivot.SubtotalPlacement = PivotSubtotalPlacement.Top;
        pivot.CalculatedFields.Add(new PivotCalculatedFieldModel("Revenue", "Amount*2"));
        pivot.CalculatedItems.Add(new PivotCalculatedItemModel(0, "East + West", "East+West"));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.Top, 3));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.GreaterThan, ComparisonValue: 25));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.LessThanOrEqual, ComparisonValue: 100, SourceFieldIndex: 1));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.Between, ComparisonValue: 25, ComparisonValue2: 100, SourceFieldIndex: 0));
        pivot.ValueFilters.Add(new PivotValueFilterModel(0, PivotValueFilterKind.AboveAverage, SourceFieldIndex: 0));
        pivot.LabelFilters.Add(new PivotLabelFilterModel(0, PivotLabelFilterKind.Contains, "East"));
        pivot.LabelFilters.Add(new PivotLabelFilterModel(0, PivotLabelFilterKind.Between, "East", "West"));
        pivot.Sorts.Add(new PivotSortModel(PivotSortTarget.Value, PivotSortDirection.Descending, DataFieldIndex: 0));
        pivot.DataFields.Add(new PivotDataFieldModel(4, "Average Amount", "average", 4, ShowValuesAs: PivotShowValuesAs.PercentOfGrandTotal));
        pivot.DataFields.Add(new PivotDataFieldModel(4, "Max Amount", "max", 4, ShowValuesAs: PivotShowValuesAs.PercentOfRowTotal));
        pivot.DataFields.Add(new PivotDataFieldModel(-1, "Sum of Revenue", "sum", 4, "Revenue", PivotShowValuesAs.PercentOfColumnTotal));
        pivot.DataFields.Add(new PivotDataFieldModel(4, "Running Amount", "sum", 4, ShowValuesAs: PivotShowValuesAs.RunningTotalIn, BaseFieldIndex: 1));
        pivot.DataFields.Add(new PivotDataFieldModel(4, "Difference From Q1", "sum", 4, ShowValuesAs: PivotShowValuesAs.DifferenceFrom, BaseFieldIndex: 1, BaseItem: "Q1"));
        sheet.PivotTables.Add(pivot);

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            var pivotXml = LoadPackageXml(archive.GetEntry("xl/pivotTables/pivotTable1.xml")!);
            pivotXml.ToString().Should().Contain("pageField");
            pivotXml.ToString().Should().Contain("colFields");
            pivotXml.ToString().Should().Contain("name=\"Domestic\"");
            pivotXml.ToString().Should().Contain("selectedItems=\"Domestic,Export\"");
            pivotXml.ToString().Should().Contain("selectedItems=\"East\"");
            pivotXml.ToString().Should().Contain("selectedItems=\"Q1\"");
            pivotXml.ToString().Should().Contain("groupBy=\"numberRange\"");
            pivotXml.ToString().Should().Contain("groupStart=\"0\"");
            pivotXml.ToString().Should().Contain("groupInterval=\"10\"");
            pivotXml.ToString().Should().Contain("valueFilters");
            pivotXml.ToString().Should().Contain("type=\"top\"");
            pivotXml.ToString().Should().Contain("type=\"greaterThan\"");
            pivotXml.ToString().Should().Contain("type=\"lessThanOrEqual\"");
            pivotXml.ToString().Should().Contain("type=\"between\"");
            pivotXml.ToString().Should().Contain("type=\"aboveAverage\"");
            pivotXml.ToString().Should().Contain("field=\"1\"");
            pivotXml.ToString().Should().Contain("comparisonValue=\"25\"");
            pivotXml.ToString().Should().Contain("comparisonValue2=\"100\"");
            pivotXml.ToString().Should().Contain("labelFilters");
            pivotXml.ToString().Should().Contain("contains");
            pivotXml.ToString().Should().Contain("between");
            pivotXml.ToString().Should().Contain("value2=\"West\"");
            pivotXml.ToString().Should().Contain("pivotSorts");
            pivotXml.ToString().Should().Contain("direction=\"descending\"");
            pivotXml.ToString().Should().Contain("showGrandTotals=\"1\"");
            pivotXml.ToString().Should().Contain("showRowGrandTotals=\"0\"");
            pivotXml.ToString().Should().Contain("showColumnGrandTotals=\"1\"");
            pivotXml.ToString().Should().Contain("repeatItemLabels=\"0\"");
            pivotXml.ToString().Should().Contain("blankLineAfterItems=\"1\"");
            pivotXml.ToString().Should().Contain("reportLayout=\"compact\"");
            pivotXml.ToString().Should().Contain("name=\"PivotStyleMedium9\"");
            pivotXml.ToString().Should().Contain("showRowHeaders=\"0\"");
            pivotXml.ToString().Should().Contain("showColHeaders=\"1\"");
            pivotXml.ToString().Should().Contain("showRowStripes=\"1\"");
            pivotXml.ToString().Should().Contain("showColStripes=\"1\"");
            pivotXml.ToString().Should().Contain("defaultSubtotal=\"1\"");
            pivotXml.ToString().Should().Contain("subtotalTop=\"1\"");
            pivotXml.ToString().Should().Contain("calculatedField");
            pivotXml.ToString().Should().Contain("calculatedItems");
            pivotXml.ToString().Should().Contain("formula=\"East+West\"");
            pivotXml.ToString().Should().Contain("formula=\"Amount*2\"");
            pivotXml.ToString().Should().Contain("subtotal=\"average\"");
            pivotXml.ToString().Should().Contain("showValuesAs=\"percentOfGrandTotal\"");
            pivotXml.ToString().Should().Contain("subtotal=\"max\"");
            pivotXml.ToString().Should().Contain("showValuesAs=\"percentOfRowTotal\"");
            pivotXml.ToString().Should().Contain("showValuesAs=\"percentOfColumnTotal\"");
            pivotXml.ToString().Should().Contain("showValuesAs=\"runningTotalIn\"");
            pivotXml.ToString().Should().Contain("showValuesAs=\"differenceFrom\"");
            pivotXml.ToString().Should().Contain("baseField=\"1\"");
            pivotXml.ToString().Should().Contain("baseItem=\"Q1\"");
        }

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        var loadedPivot = loaded.GetSheetAt(0).PivotTables.Should().ContainSingle().Subject;
        var loadedRowField = loadedPivot.RowFields.Should().ContainSingle().Subject;
        loadedRowField.Grouping.Should().Be(PivotFieldGrouping.NumberRange);
        loadedRowField.GroupStart.Should().Be(0);
        loadedRowField.GroupInterval.Should().Be(10);
        loadedRowField.SelectedItems.Should().Equal("East");
        loadedPivot.ColumnFields.Select(field => field.SourceFieldIndex).Should().Equal(1, 2);
        loadedPivot.ColumnFields[0].SelectedItems.Should().Equal("Q1");
        var loadedPageField = loadedPivot.PageFields.Should().ContainSingle().Subject;
        loadedPageField.SourceFieldIndex.Should().Be(3);
        loadedPageField.SelectedItem.Should().Be("Domestic");
        loadedPageField.SelectedItems.Should().Equal("Domestic", "Export");
        loadedPivot.ShowSubtotals.Should().BeTrue();
        loadedPivot.SubtotalPlacement.Should().Be(PivotSubtotalPlacement.Top);
        loadedPivot.ShowGrandTotals.Should().BeTrue();
        loadedPivot.ShowRowGrandTotals.Should().BeFalse();
        loadedPivot.ShowColumnGrandTotals.Should().BeTrue();
        loadedPivot.RepeatItemLabels.Should().BeFalse();
        loadedPivot.BlankLineAfterItems.Should().BeTrue();
        loadedPivot.ReportLayout.Should().Be(PivotReportLayout.Compact);
        loadedPivot.StyleName.Should().Be("PivotStyleMedium9");
        loadedPivot.ShowRowHeaders.Should().BeFalse();
        loadedPivot.ShowColumnHeaders.Should().BeTrue();
        loadedPivot.ShowRowStripes.Should().BeTrue();
        loadedPivot.ShowColumnStripes.Should().BeTrue();
        loadedPivot.CalculatedFields.Should().ContainSingle().Which.Should().Be(new PivotCalculatedFieldModel("Revenue", "Amount*2"));
        loadedPivot.CalculatedItems.Should().ContainSingle().Which.Should().Be(new PivotCalculatedItemModel(0, "East + West", "East+West"));
        loadedPivot.ValueFilters.Should().HaveCount(5);
        loadedPivot.ValueFilters[0].Should().Be(new PivotValueFilterModel(0, PivotValueFilterKind.Top, 3));
        loadedPivot.ValueFilters[1].Should().Be(new PivotValueFilterModel(0, PivotValueFilterKind.GreaterThan, ComparisonValue: 25));
        loadedPivot.ValueFilters[2].Should().Be(new PivotValueFilterModel(0, PivotValueFilterKind.LessThanOrEqual, ComparisonValue: 100, SourceFieldIndex: 1));
        loadedPivot.ValueFilters[3].Should().Be(new PivotValueFilterModel(0, PivotValueFilterKind.Between, ComparisonValue: 25, ComparisonValue2: 100, SourceFieldIndex: 0));
        loadedPivot.ValueFilters[4].Should().Be(new PivotValueFilterModel(0, PivotValueFilterKind.AboveAverage, SourceFieldIndex: 0));
        loadedPivot.LabelFilters.Should().Equal(
            new PivotLabelFilterModel(0, PivotLabelFilterKind.Contains, "East"),
            new PivotLabelFilterModel(0, PivotLabelFilterKind.Between, "East", "West"));
        loadedPivot.Sorts.Should().ContainSingle().Which.Should().Be(new PivotSortModel(PivotSortTarget.Value, PivotSortDirection.Descending, DataFieldIndex: 0));
        loadedPivot.DataFields.Select(field => field.SummaryFunction).Should().Equal("average", "max", "sum", "sum", "sum");
        loadedPivot.DataFields[0].ShowValuesAs.Should().Be(PivotShowValuesAs.PercentOfGrandTotal);
        loadedPivot.DataFields[1].ShowValuesAs.Should().Be(PivotShowValuesAs.PercentOfRowTotal);
        loadedPivot.DataFields[2].ShowValuesAs.Should().Be(PivotShowValuesAs.PercentOfColumnTotal);
        loadedPivot.DataFields[2].CalculatedFieldName.Should().Be("Revenue");
        loadedPivot.DataFields[3].ShowValuesAs.Should().Be(PivotShowValuesAs.RunningTotalIn);
        loadedPivot.DataFields[3].BaseFieldIndex.Should().Be(1);
        loadedPivot.DataFields[4].ShowValuesAs.Should().Be(PivotShowValuesAs.DifferenceFrom);
        loadedPivot.DataFields[4].BaseFieldIndex.Should().Be(1);
        loadedPivot.DataFields[4].BaseItem.Should().Be("Q1");
    }

    [Fact]
    public void XlsxAdapter_Save_WritesPivotChartSourceAndRoundTripsBinding()
    {
        var workbook = new Workbook("AuthoredPivotChartXlsxTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new TextValue("Sum of Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 7, 2), new NumberValue(20));
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 5, 1), new CellAddress(sheet.Id, 7, 2)),
            Title = "Pivot Chart",
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            PivotCacheId = 1,
            Width = 360,
            Height = 240
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            XNamespace chartNs = "http://schemas.openxmlformats.org/drawingml/2006/chart";
            var chartXml = LoadPackageXml(archive.GetEntry("xl/charts/chart1.xml")!);
            var pivotSource = chartXml.Root!.Element(chartNs + "pivotSource");
            pivotSource.Should().NotBeNull();
            pivotSource!.Element(chartNs + "name")!.Value.Should().Be("Data!PivotTable1");
            pivotSource.Element(chartNs + "fmtId")!.Attribute("val")!.Value.Should().Be("0");
        }

        saved.Position = 0;
        var loaded = adapter.Load(saved);
        loaded.GetSheetAt(0).Charts.Should().ContainSingle().Which.Should().Match<ChartModel>(
            chart => chart.IsPivotChart &&
                     chart.PivotTableName == "PivotTable1" &&
                     chart.PivotCacheId == null);
    }

    [Fact]
    public void XlsxAdapter_LoadsSlicerAndTimelineMetadataAndPreservesPackageParts()
    {
        var workbook = new Workbook("SlicerTimelineMetadataTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalSlicerTimelinePackage(source);

        var loaded = adapter.Load(source);

        loaded.Slicers.Should().ContainSingle().Which.Should().BeEquivalentTo(new SlicerModel
        {
            Name = "Region Slicer",
            Caption = "Region",
            CacheName = "Slicer_Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region",
            StyleName = "SlicerStyleLight2",
            PackagePart = "xl/slicers/slicer1.xml"
        });
        loaded.Timelines.Should().ContainSingle().Which.Should().BeEquivalentTo(new TimelineModel
        {
            Name = "Date Timeline",
            Caption = "Order Date",
            CacheName = "Timeline_Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date",
            StartDate = "2026-01-01",
            EndDate = "2026-03-31",
            StyleName = "TimeSlicerStyleLight1",
            PackagePart = "xl/timelines/timeline1.xml"
        });

        var saved = new MemoryStream();
        loaded.GetSheetAt(0).SetCell(new CellAddress(loaded.GetSheetAt(0).Id, 2, 1), new TextValue("edit"));
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        archive.GetEntry("xl/slicers/slicer1.xml").Should().NotBeNull();
        archive.GetEntry("xl/slicerCaches/slicerCache1.xml").Should().NotBeNull();
        archive.GetEntry("xl/timelines/timeline1.xml").Should().NotBeNull();
        archive.GetEntry("xl/timelineCaches/timelineCache1.xml").Should().NotBeNull();
        var slicerXml = LoadPackageXml(archive.GetEntry("xl/slicers/slicer1.xml")!);
        slicerXml.Root!.Attribute("caption")!.Value.Should().Be("Region");
        slicerXml.Root.Attribute("style")!.Value.Should().Be("SlicerStyleLight2");
        var timelineXml = LoadPackageXml(archive.GetEntry("xl/timelines/timeline1.xml")!);
        timelineXml.Root!.Attribute("caption")!.Value.Should().Be("Order Date");
        timelineXml.Root.Attribute("style")!.Value.Should().Be("TimeSlicerStyleLight1");
    }

    [Fact]
    public void XlsxAdapter_PreservesNativeSlicerTimelineDrawingAnchorsWhenWritingFreexcelDrawings()
    {
        var workbook = new Workbook("SlicerTimelineDrawingTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalSlicerTimelinePackage(source, includeDrawing: true);

        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(loadedSheet.Id, 4, 6),
            Kind = PictureKind.Image,
            ImageBytes = MinimalPngBytes(),
            ContentType = "image/png",
            AltText = "Freexcel picture",
            Width = 96,
            Height = 64
        });

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var drawingXml = LoadPackageXml(archive.GetEntry("xl/drawings/drawing1.xml")!);
        drawingXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("Native Slicer Shape");
        drawingXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("Freexcel picture");

        var drawingRelsXml = LoadPackageXml(archive.GetEntry("xl/drawings/_rels/drawing1.xml.rels")!);
        drawingRelsXml.ToString(System.Xml.Linq.SaveOptions.DisableFormatting).Should().Contain("media/freexcelPicture");
    }

    [Fact]
    public void XlsxAdapter_RoundTripsAuthoredSlicerAndTimelineSelectionState()
    {
        var workbook = new Workbook("AuthoredSlicerTimelineStateTest");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("x"));
        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            CacheName = "Slicer_Region",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Region"
        };
        slicer.SelectedItems.AddRange(["East", "West"]);
        workbook.Slicers.Add(slicer);
        workbook.Timelines.Add(new TimelineModel
        {
            Name = "Date Timeline",
            CacheName = "Timeline_Date",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Date",
            StartDate = "2026-01-01",
            EndDate = "2026-12-31",
            SelectedStartDate = "2026-03-01",
            SelectedEndDate = "2026-06-30"
        });

        var saved = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            archive.GetEntry("xl/slicers/slicer1.xml").Should().NotBeNull();
            archive.GetEntry("xl/slicerCaches/slicerCache1.xml").Should().NotBeNull();
            archive.GetEntry("xl/timelines/timeline1.xml").Should().NotBeNull();
            archive.GetEntry("xl/timelineCaches/timelineCache1.xml").Should().NotBeNull();
            var slicerRels = LoadPackageXml(archive.GetEntry("xl/slicers/_rels/slicer1.xml.rels")!).ToString();
            slicerRels.Should().Contain("../slicerCaches/slicerCache1.xml");
            slicerRels.Should().Contain("slicerCache");
            var timelineRels = LoadPackageXml(archive.GetEntry("xl/timelines/_rels/timeline1.xml.rels")!).ToString();
            timelineRels.Should().Contain("../timelineCaches/timelineCache1.xml");
            timelineRels.Should().Contain("timelineCache");
        }

        saved.Position = 0;
        var loaded = adapter.Load(saved);

        loaded.Slicers.Should().ContainSingle().Which.SelectedItems.Should().Equal("East", "West");
        var timeline = loaded.Timelines.Should().ContainSingle().Subject;
        timeline.SelectedStartDate.Should().Be("2026-03-01");
        timeline.SelectedEndDate.Should().Be("2026-06-30");
    }

    [Fact]
    public void XlsxAdapter_LoadsStructuredTableMetadata()
    {
        var workbook = CreateStructuredTableWorkbook("StructuredTableMetadataTest");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalStructuredTablePackage(source);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var table = loaded.GetSheetAt(0).StructuredTables.Should().ContainSingle().Subject;
        table.Id.Should().Be(1);
        table.Name.Should().Be("Table1");
        table.DisplayName.Should().Be("Table1");
        table.Range.Start.ToA1().Should().Be("A1");
        table.Range.End.ToA1().Should().Be("B3");
        table.HasAutoFilter.Should().BeTrue();
        table.TotalsRowShown.Should().BeFalse();
        table.StyleName.Should().Be("TableStyleMedium2");
        table.ShowRowStripes.Should().BeTrue();
        table.Columns.Select(column => column.Name).Should().Equal("Category", "Amount");
        table.PackagePart.Should().Be("xl/tables/table1.xml");
    }

    [Fact]
    public void XlsxAdapter_LoadedWorkbookSave_PreservesStructuredTablePackageReferencesAlongsideModelEdits()
    {
        var workbook = CreateStructuredTableWorkbook("StructuredTableRetentionTest");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalStructuredTablePackage(source);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 4, 1), new TextValue("C"));
        loadedSheet.SetCell(new CellAddress(loadedSheet.Id, 4, 2), new NumberValue(30));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using (var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true))
        {
            archive.GetEntry("xl/tables/table1.xml").Should().NotBeNull();

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            contentTypesXml.ToString().Should().Contain("/xl/tables/table1.xml");

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.ToString().Should().Contain("tableParts");

            var worksheetRelsXml = LoadPackageXml(archive.GetEntry("xl/worksheets/_rels/sheet1.xml.rels")!);
            worksheetRelsXml.ToString().Should().Contain("../tables/table1.xml");
        }

        saved.Position = 0;
        var roundTripped = adapter.Load(saved);
        roundTripped.GetSheetAt(0).GetValue(4, 1).Should().Be(new TextValue("C"));
        roundTripped.GetSheetAt(0).StructuredTables.Should().ContainSingle()
            .Which.Name.Should().Be("Table1");
    }

    [Fact]
    public void XlsxAdapter_LoadSave_RoundTripsStructuredTableTotalsRowMetadata()
    {
        var workbook = CreateStructuredTableWorkbook("StructuredTableTotalsTest");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalStructuredTablePackage(source, includeTotalsRow: true);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var table = loaded.GetSheetAt(0).StructuredTables.Should().ContainSingle().Subject;
        table.TotalsRowShown.Should().BeTrue();
        table.Columns.Should().Contain(column =>
            column.Name == "Category" &&
            column.TotalsRowLabel == "Total");
        table.Columns.Should().Contain(column =>
            column.Name == "Amount" &&
            column.TotalsRowFunction == "sum");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var tableXml = LoadPackageXml(archive.GetEntry("xl/tables/table1.xml")!).ToString();
        tableXml.Should().Contain("totalsRowShown=\"1\"");
        tableXml.Should().Contain("totalsRowLabel=\"Total\"");
        tableXml.Should().Contain("totalsRowFunction=\"sum\"");
    }

    [Fact]
    public void XlsxAdapter_LoadSave_RoundTripsStructuredTableColumnFormulas()
    {
        var workbook = CreateStructuredTableWorkbook("StructuredTableFormulaTest");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalStructuredTablePackage(source, includeColumnFormulas: true);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var table = loaded.GetSheetAt(0).StructuredTables.Should().ContainSingle().Subject;
        table.Columns[1].CalculatedColumnFormula.Should().Be("SUM(Table1[@[Q1]:[Q2]])");
        table.Columns[1].TotalsRowFormula.Should().Be("SUBTOTAL(109,[Amount])");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var tableXml = LoadPackageXml(archive.GetEntry("xl/tables/table1.xml")!).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        tableXml.Should().Contain("<calculatedColumnFormula>SUM(Table1[@[Q1]:[Q2]])</calculatedColumnFormula>");
        tableXml.Should().Contain("<totalsRowFormula>SUBTOTAL(109,[Amount])</totalsRowFormula>");
    }

    [Fact]
    public void XlsxAdapter_LoadSave_RoundTripsStructuredTableAutoFilterValues()
    {
        var workbook = CreateStructuredTableWorkbook("StructuredTableFilterTest");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalStructuredTablePackage(source, includeFilterValues: true);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var table = loaded.GetSheetAt(0).StructuredTables.Should().ContainSingle().Subject;
        table.FilterColumns.Should().ContainSingle().Which.Should().BeEquivalentTo(new StructuredTableFilterColumnModel(
            ColumnId: 0,
            Values: ["A", "B"],
            IncludeBlank: true));

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var tableXml = LoadPackageXml(archive.GetEntry("xl/tables/table1.xml")!).ToString();
        tableXml.Should().Contain("filterColumn");
        tableXml.Should().Contain("colId=\"0\"");
        tableXml.Should().Contain("blank=\"1\"");
        tableXml.Should().Contain("val=\"A\"");
        tableXml.Should().Contain("val=\"B\"");
    }

    [Fact]
    public void XlsxAdapter_LoadSave_RoundTripsStructuredTableNativeCustomFilter()
    {
        var workbook = CreateStructuredTableWorkbook("StructuredTableCustomFilterTest");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalStructuredTablePackage(source, includeCustomFilter: true);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var table = loaded.GetSheetAt(0).StructuredTables.Should().ContainSingle().Subject;
        var filter = table.FilterColumns.Should().ContainSingle().Subject;
        filter.ColumnId.Should().Be(1);
        filter.Values.Should().BeEmpty();
        filter.NativeFilterXml.Should().Contain("customFilters");
        filter.NativeFilterXml.Should().Contain("greaterThan");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var tableXml = LoadPackageXml(archive.GetEntry("xl/tables/table1.xml")!).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        tableXml.Should().Contain("customFilters");
        tableXml.Should().Contain("customFilter operator=\"greaterThan\" val=\"10\"");
    }

    [Fact]
    public void XlsxAdapter_LoadSave_RoundTripsStructuredTableNativeFilterExtensionSiblings()
    {
        var workbook = CreateStructuredTableWorkbook("StructuredTableCustomFilterExtensionTest");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalStructuredTablePackage(source, includeCustomFilterWithExtension: true);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var table = loaded.GetSheetAt(0).StructuredTables.Should().ContainSingle().Subject;
        var filter = table.FilterColumns.Should().ContainSingle().Subject;
        filter.ColumnId.Should().Be(1);
        filter.Values.Should().BeEmpty();
        filter.NativeFilterXmls.Should().HaveCount(2);
        filter.NativeFilterXmls[0].Should().Contain("customFilters");
        filter.NativeFilterXmls[1].Should().Contain("extLst");
        filter.NativeFilterXmls[1].Should().Contain("{FREEXCEL-TABLE-FILTER-EXT}");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var tableXml = LoadPackageXml(archive.GetEntry("xl/tables/table1.xml")!).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        tableXml.Should().Contain("customFilters");
        tableXml.Should().Contain("customFilter operator=\"greaterThan\" val=\"10\"");
        tableXml.Should().Contain("extLst");
        tableXml.Should().Contain("{FREEXCEL-TABLE-FILTER-EXT}");
    }

    [Fact]
    public void XlsxAdapter_LoadSave_RoundTripsStructuredTableFilterColumnNativeAttributes()
    {
        var workbook = CreateStructuredTableWorkbook("StructuredTableFilterColumnAttributeTest");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalStructuredTablePackage(source, includeFilterColumnAttributes: true);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var table = loaded.GetSheetAt(0).StructuredTables.Should().ContainSingle().Subject;
        var filter = table.FilterColumns.Should().ContainSingle().Subject;
        filter.ColumnId.Should().Be(0);
        filter.NativeAttributes.Should().ContainKey("hiddenButton").WhoseValue.Should().Be("1");
        filter.NativeAttributes.Should().ContainKey("showButton").WhoseValue.Should().Be("0");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var tableXml = LoadPackageXml(archive.GetEntry("xl/tables/table1.xml")!).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        tableXml.Should().Contain("hiddenButton=\"1\"");
        tableXml.Should().Contain("showButton=\"0\"");
    }

    [Fact]
    public void XlsxAdapter_LoadSave_RoundTripsStructuredTableAutoFilterNativeMetadata()
    {
        var workbook = CreateStructuredTableWorkbook("StructuredTableAutoFilterMetadataTest");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalStructuredTablePackage(source, includeAutoFilterMetadata: true);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var table = loaded.GetSheetAt(0).StructuredTables.Should().ContainSingle().Subject;
        table.NativeAutoFilterAttributes.Should().ContainKey("customAttr").WhoseValue.Should().Be("auto-filter-native");
        table.NativeAutoFilterChildXmls.Should().ContainSingle()
            .Which.Should().Contain("{FREEXCEL-TABLE-AUTOFILTER-EXT}");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var tableXml = LoadPackageXml(archive.GetEntry("xl/tables/table1.xml")!).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        tableXml.Should().Contain("customAttr=\"auto-filter-native\"");
        tableXml.Should().Contain("extLst");
        tableXml.Should().Contain("{FREEXCEL-TABLE-AUTOFILTER-EXT}");
    }

    [Fact]
    public void XlsxAdapter_LoadSave_RoundTripsStructuredTableNativeSortState()
    {
        var workbook = CreateStructuredTableWorkbook("StructuredTableSortStateTest");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalStructuredTablePackage(source, includeSortState: true);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var table = loaded.GetSheetAt(0).StructuredTables.Should().ContainSingle().Subject;
        table.NativeSortStateXml.Should().Contain("sortState");
        table.NativeSortStateXml.Should().Contain("descending=\"1\"");
        table.NativeSortStateXml.Should().Contain("ref=\"B2:B3\"");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var tableXml = LoadPackageXml(archive.GetEntry("xl/tables/table1.xml")!).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        tableXml.Should().Contain("sortState");
        tableXml.Should().Contain("descending=\"1\"");
        tableXml.Should().Contain("ref=\"B2:B3\"");
        tableXml.IndexOf("autoFilter", StringComparison.Ordinal).Should().BeLessThan(tableXml.IndexOf("sortState", StringComparison.Ordinal));
        tableXml.IndexOf("sortState", StringComparison.Ordinal).Should().BeLessThan(tableXml.IndexOf("tableColumns", StringComparison.Ordinal));
    }

    [Fact]
    public void XlsxAdapter_LoadSave_RoundTripsStructuredTableColumnNativeChildren()
    {
        var workbook = CreateStructuredTableWorkbook("StructuredTableColumnExtensionTest");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalStructuredTablePackage(source, includeColumnExtension: true);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var table = loaded.GetSheetAt(0).StructuredTables.Should().ContainSingle().Subject;
        var amountColumn = table.Columns.Should().Contain(column => column.Name == "Amount").Subject;
        amountColumn.NativeChildXmls.Should().ContainSingle()
            .Which.Should().Contain("{FREEXCEL-TABLE-COLUMN-EXT}");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var tableXml = LoadPackageXml(archive.GetEntry("xl/tables/table1.xml")!).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        tableXml.Should().Contain("extLst");
        tableXml.Should().Contain("{FREEXCEL-TABLE-COLUMN-EXT}");
    }

    [Fact]
    public void XlsxAdapter_LoadSave_RoundTripsStructuredTableColumnNativeAttributes()
    {
        var workbook = CreateStructuredTableWorkbook("StructuredTableColumnAttributeTest");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalStructuredTablePackage(source, includeColumnAttributes: true);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var table = loaded.GetSheetAt(0).StructuredTables.Should().ContainSingle().Subject;
        var amountColumn = table.Columns.Should().Contain(column => column.Name == "Amount").Subject;
        amountColumn.NativeAttributes.Should().ContainKey("uniqueName").WhoseValue.Should().Be("AmountNative");
        amountColumn.NativeAttributes.Should().ContainKey("dataDxfId").WhoseValue.Should().Be("4");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var tableXml = LoadPackageXml(archive.GetEntry("xl/tables/table1.xml")!).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        tableXml.Should().Contain("uniqueName=\"AmountNative\"");
        tableXml.Should().Contain("dataDxfId=\"4\"");
    }

    [Fact]
    public void XlsxAdapter_LoadSave_RoundTripsStructuredTableStyleInfoNativeMetadata()
    {
        var workbook = CreateStructuredTableWorkbook("StructuredTableStyleInfoMetadataTest");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalStructuredTablePackage(source, includeStyleInfoExtension: true);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var table = loaded.GetSheetAt(0).StructuredTables.Should().ContainSingle().Subject;
        table.NativeStyleInfoAttributes.Should().ContainKey("pivot").WhoseValue.Should().Be("0");
        table.NativeStyleInfoChildXmls.Should().ContainSingle()
            .Which.Should().Contain("{FREEXCEL-TABLE-STYLE-EXT}");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var tableXml = LoadPackageXml(archive.GetEntry("xl/tables/table1.xml")!).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        tableXml.Should().Contain("pivot=\"0\"");
        tableXml.Should().Contain("showRowStripes=\"1\"");
        tableXml.Should().Contain("extLst");
        tableXml.Should().Contain("{FREEXCEL-TABLE-STYLE-EXT}");
    }

    [Fact]
    public void XlsxAdapter_LoadSave_RoundTripsStructuredTableRootNativeMetadata()
    {
        var workbook = CreateStructuredTableWorkbook("StructuredTableRootMetadataTest");

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalStructuredTablePackage(source, includeRootMetadata: true);

        source.Position = 0;
        var loaded = adapter.Load(source);

        var table = loaded.GetSheetAt(0).StructuredTables.Should().ContainSingle().Subject;
        table.NativeAttributes.Should().ContainKey("published").WhoseValue.Should().Be("1");
        table.NativeAttributes.Should().ContainKey("headerRowDxfId").WhoseValue.Should().Be("2");
        table.NativeChildXmls.Should().ContainSingle()
            .Which.Should().Contain("{FREEXCEL-TABLE-ROOT-EXT}");

        var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        using var archive = new ZipArchive(saved, ZipArchiveMode.Read);
        var tableXml = LoadPackageXml(archive.GetEntry("xl/tables/table1.xml")!).ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
        tableXml.Should().Contain("published=\"1\"");
        tableXml.Should().Contain("headerRowDxfId=\"2\"");
        tableXml.Should().Contain("extLst");
        tableXml.Should().Contain("{FREEXCEL-TABLE-ROOT-EXT}");
    }

    [Fact]
    public void XlsxAdapter_Load_MaterializesStructuredTableAutoFilterValuesIntoHiddenRows()
    {
        var workbook = CreateStructuredTableWorkbook("StructuredTableFilterVisibilityTest");
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("C"));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalStructuredTablePackage(source, includeFilterValues: true);

        source.Position = 0;
        var loaded = adapter.Load(source);
        var loadedSheet = loaded.GetSheetAt(0);

        loadedSheet.StructuredTables.Should().ContainSingle();
        loadedSheet.FilterHiddenRows.Should().BeEquivalentTo([3u]);
        loadedSheet.HiddenRows.Should().BeEmpty();
    }

    [Fact]
    public void XlsxAdapter_Load_StructuredTableAutoFilterDoesNotHideTotalsRow()
    {
        var workbook = CreateStructuredTableWorkbook("StructuredTableTotalsFilterVisibilityTest");
        var sheet = workbook.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("C"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Grand Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        var source = new MemoryStream();
        var adapter = new XlsxFileAdapter();
        adapter.Save(workbook, source);
        source.Position = 0;
        AddMinimalStructuredTablePackage(source, includeTotalsRow: true, includeFilterValues: true);

        source.Position = 0;
        var loadedSheet = adapter.Load(source).GetSheetAt(0);

        loadedSheet.FilterHiddenRows.Should().Contain(3u);
        loadedSheet.FilterHiddenRows.Should().NotContain(4u);
    }

    private static Workbook CreateStructuredTableWorkbook(string name)
    {
        var workbook = new Workbook(name);
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("A"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("B"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        return workbook;
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

    private static void AddMinimalChartsheetPackage(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace freexcelNs = "urn:freexcel:test";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(
                contentTypesXml,
                contentTypeNs,
                "/xl/chartsheets/sheet1.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.chartsheet+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var workbookRelsPath = "xl/_rels/workbook.xml.rels";
            var workbookRelsXml = LoadPackageXml(archive.GetEntry(workbookRelsPath)!);
            workbookRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreexcelUnsupportedChartsheet"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/chartsheet"),
                new XAttribute("Target", "chartsheets/sheet1.xml")));
            ReplacePackageXml(archive, workbookRelsPath, workbookRelsXml);

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!
                .Element(workbookNs + "sheets")!
                .Add(new XElement(
                    workbookNs + "sheet",
                    new XAttribute("name", "ChartSheet1"),
                    new XAttribute("sheetId", "99"),
                    new XAttribute(relNs + "id", "rIdFreexcelUnsupportedChartsheet")));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);

            ReplacePackageXml(archive, "xl/chartsheets/sheet1.xml", new XDocument(
                new XElement(
                    workbookNs + "chartsheet",
                    new XElement(workbookNs + "sheetPr"),
                    new XElement(
                        workbookNs + "sheetViews",
                        new XElement(workbookNs + "sheetView", new XAttribute("workbookViewId", "0"))),
                    new XElement(
                        workbookNs + "pageMargins",
                        new XAttribute("left", "0.7"),
                        new XAttribute("right", "0.7"),
                        new XAttribute("top", "0.75"),
                        new XAttribute("bottom", "0.75"),
                        new XAttribute("header", "0.3"),
                        new XAttribute("footer", "0.3")))));
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalDialogsheetPackage(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(
                contentTypesXml,
                contentTypeNs,
                "/xl/dialogSheets/sheet1.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.dialogsheet+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var workbookRelsPath = "xl/_rels/workbook.xml.rels";
            var workbookRelsXml = LoadPackageXml(archive.GetEntry(workbookRelsPath)!);
            workbookRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreexcelUnsupportedDialogsheet"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/dialogsheet"),
                new XAttribute("Target", "dialogSheets/sheet1.xml")));
            ReplacePackageXml(archive, workbookRelsPath, workbookRelsXml);

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!
                .Element(workbookNs + "sheets")!
                .Add(new XElement(
                    workbookNs + "sheet",
                    new XAttribute("name", "DialogSheet1"),
                    new XAttribute("sheetId", "100"),
                    new XAttribute(relNs + "id", "rIdFreexcelUnsupportedDialogsheet")));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);

            ReplacePackageXml(archive, "xl/dialogSheets/sheet1.xml", new XDocument(
                new XElement(
                    workbookNs + "dialogsheet",
                    new XElement(workbookNs + "sheetPr"),
                    new XElement(
                        workbookNs + "sheetViews",
                        new XElement(workbookNs + "sheetView", new XAttribute("workbookViewId", "0"))),
                    new XElement(
                        workbookNs + "pageMargins",
                        new XAttribute("left", "0.7"),
                        new XAttribute("right", "0.7"),
                        new XAttribute("top", "0.75"),
                        new XAttribute("bottom", "0.75"),
                        new XAttribute("header", "0.3"),
                        new XAttribute("footer", "0.3")))));
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalQueryTablePackage(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(
                contentTypesXml,
                contentTypeNs,
                "/xl/queryTables/queryTable1.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.queryTable+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadPackageXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreexcelQueryTable"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/queryTable"),
                new XAttribute("Target", "../queryTables/queryTable1.xml")));
            ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "queryTableParts",
                new XAttribute("count", "1"),
                new XElement(
                    worksheetNs + "queryTablePart",
                    new XAttribute(relNs + "id", "rIdFreexcelQueryTable"))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            ReplacePackageXml(archive, "xl/queryTables/queryTable1.xml", new XDocument(
                new XElement(
                    worksheetNs + "queryTable",
                    new XAttribute("name", "FreexcelQueryTable"),
                    new XAttribute("connectionId", "1"),
                    new XAttribute("autoFormatId", "16"),
                    new XAttribute("applyNumberFormats", "0"),
                    new XAttribute("applyBorderFormats", "0"),
                    new XAttribute("applyFontFormats", "0"),
                    new XAttribute("applyPatternFormats", "0"),
                    new XAttribute("applyAlignmentFormats", "0"),
                    new XAttribute("applyWidthHeightFormats", "0"))));
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalWorksheetWebPublishItemsPackage(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(
                contentTypesXml,
                contentTypeNs,
                "/xl/webPublishItems.xml",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.webPublishItems+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadPackageXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreexcelWebPublishItems"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/webPublishItems"),
                new XAttribute("Target", "../webPublishItems.xml")));
            ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "webPublishItems",
                new XAttribute("count", "1"),
                new XElement(
                    worksheetNs + "webPublishItem",
                    new XAttribute(relNs + "id", "rIdFreexcelWebPublishItems"),
                    new XAttribute("divId", "FreexcelWebPublishItems"),
                    new XAttribute("sourceType", "sheet"),
                    new XAttribute("sourceRef", "A1:B2"),
                    new XAttribute("destinationFile", "https://example.invalid/sheet.htm"))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            ReplacePackageXml(archive, "xl/webPublishItems.xml", new XDocument(
                new XElement(
                    worksheetNs + "webPublishItems",
                    new XAttribute("count", "1"),
                    new XElement(
                        worksheetNs + "webPublishItem",
                        new XAttribute("divId", "FreexcelWebPublishItems"),
                        new XAttribute("sourceType", "sheet"),
                        new XAttribute("destinationFile", "https://example.invalid/sheet.htm")))));
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalWorksheetOleObjectPackage(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(
                contentTypesXml,
                contentTypeNs,
                "/xl/embeddings/oleObject1.bin",
                "application/vnd.openxmlformats-officedocument.oleObject");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadPackageXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreexcelOleObject"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/oleObject"),
                new XAttribute("Target", "../embeddings/oleObject1.bin")));
            ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "oleObjects",
                new XElement(
                    worksheetNs + "oleObject",
                    new XAttribute("progId", "Package"),
                    new XAttribute("shapeId", "1025"),
                    new XAttribute(relNs + "id", "rIdFreexcelOleObject"))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            archive.GetEntry("xl/embeddings/oleObject1.bin")?.Delete();
            var oleEntry = archive.CreateEntry("xl/embeddings/oleObject1.bin");
            using var writer = new StreamWriter(oleEntry.Open(), Encoding.UTF8);
            writer.Write("Freexcel generated OLE placeholder");
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalWorksheetControlPackage(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(
                contentTypesXml,
                contentTypeNs,
                "/xl/ctrlProps/ctrlProp1.xml",
                "application/vnd.ms-excel.controlproperties+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadPackageXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreexcelControl"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/ctrlProp"),
                new XAttribute("Target", "../ctrlProps/ctrlProp1.xml")));
            ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "controls",
                new XElement(
                    worksheetNs + "control",
                    new XAttribute("shapeId", "1026"),
                    new XAttribute("name", "Button 1"),
                    new XAttribute(relNs + "id", "rIdFreexcelControl"))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            ReplacePackageXml(archive, "xl/ctrlProps/ctrlProp1.xml", new XDocument(
                new XElement(
                    worksheetNs + "formControlPr",
                    new XAttribute("objectType", "Button"),
                    new XAttribute("checked", "Unchecked"))));
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalWorksheetLegacyDrawingPackage(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(
                contentTypesXml,
                contentTypeNs,
                "/xl/drawings/vmlDrawing1.vml",
                "application/vnd.openxmlformats-officedocument.vmlDrawing");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadPackageXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreexcelLegacyDrawing"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/vmlDrawing"),
                new XAttribute("Target", "../drawings/vmlDrawing1.vml")));
            ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "legacyDrawing",
                new XAttribute(relNs + "id", "rIdFreexcelLegacyDrawing")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            archive.GetEntry("xl/drawings/vmlDrawing1.vml")?.Delete();
            var vmlEntry = archive.CreateEntry("xl/drawings/vmlDrawing1.vml");
            using var writer = new StreamWriter(vmlEntry.Open(), Encoding.UTF8);
            writer.Write("""
                <xml xmlns:v="urn:schemas-microsoft-com:vml"
                     xmlns:o="urn:schemas-microsoft-com:office:office"
                     xmlns:x="urn:schemas-microsoft-com:office:excel">
                  <v:shape id="FreexcelLegacyDrawingShape" type="#_x0000_t201">
                    <x:ClientData ObjectType="Note"/>
                  </v:shape>
                </xml>
                """);
        }

        packageStream.Position = 0;
    }

    private static void AddExternalWorksheetPictureReference(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

            var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadPackageXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreexcelExternalPicture"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                new XAttribute("Target", "https://example.invalid/background.png"),
                new XAttribute("TargetMode", "External")));
            ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "picture",
                new XAttribute(relNs + "id", "rIdFreexcelExternalPicture")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetSingleXmlCells(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "singleXmlCells",
                new XElement(
                    worksheetNs + "singleXmlCell",
                    new XAttribute("id", "1"),
                    new XAttribute("r", "A1"),
                    new XAttribute("xmlCellPrId", "1"))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddStylesheetNativeMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace freexcelNs = "urn:freexcel:test";

            var stylesXml = LoadPackageXml(archive.GetEntry("xl/styles.xml")!);
            stylesXml.Root!.Elements(workbookNs + "colors").Remove();
            stylesXml.Root.Add(new XElement(
                workbookNs + "colors",
                new XElement(
                    workbookNs + "indexedColors",
                    new XElement(workbookNs + "rgbColor", new XAttribute("rgb", "FF010203")))));

            var tableStyles = stylesXml.Root.Element(workbookNs + "tableStyles");
            if (tableStyles is null)
            {
                tableStyles = new XElement(workbookNs + "tableStyles");
                stylesXml.Root.Add(tableStyles);
            }

            tableStyles.SetAttributeValue("defaultPivotStyle", "PivotStyleMedium9");
            tableStyles.Add(new XElement(freexcelNs + "tableStylesNativeChild", new XAttribute("value", "kept")));
            tableStyles.Add(new XElement(
                workbookNs + "tableStyle",
                new XAttribute("name", "FreexcelNativeTableStyle"),
                new XAttribute("pivot", "0"),
                new XAttribute("table", "1"),
                new XAttribute("count", "1"),
                new XElement(
                    workbookNs + "tableStyleElement",
                    new XAttribute("type", "wholeTable"),
                    new XAttribute("dxfId", "0"))));
            tableStyles.Add(new XElement(
                workbookNs + "tableStyle",
                new XAttribute("name", "FreexcelNativePivotStyle"),
                new XAttribute("pivot", "1"),
                new XAttribute("table", "0"),
                new XAttribute("count", "1"),
                new XElement(
                    workbookNs + "tableStyleElement",
                    new XAttribute("type", "wholeTable"),
                    new XAttribute("dxfId", "0"))));

            stylesXml.Root.Elements(workbookNs + "extLst").Remove();
            stylesXml.Root.Add(new XElement(
                workbookNs + "extLst",
                new XElement(
                    workbookNs + "ext",
                    new XAttribute("uri", "{FFEEDDCC-7788-6655-4433-22110099AABB}"),
                    new XElement(workbookNs + "FreexcelNativeStylesExtension"))));
            ReplacePackageXml(archive, "xl/styles.xml", stylesXml);
        }

        packageStream.Position = 0;
    }

    private static void AddStableDocumentProperties(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace dcNs = "http://purl.org/dc/elements/1.1/";
            XNamespace cpNs = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
            XNamespace appNs = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";

            var coreXml = archive.GetEntry("docProps/core.xml") is { } coreEntry
                ? LoadPackageXml(coreEntry)
                : new XDocument(new XElement(cpNs + "coreProperties"));
            SetElementValue(coreXml.Root!, dcNs + "subject", "Freexcel parity subject");
            SetElementValue(coreXml.Root!, cpNs + "keywords", "freexcel,xlsx,parity");
            SetElementValue(coreXml.Root!, cpNs + "category", "Native Metadata");
            SetElementValue(coreXml.Root!, cpNs + "contentStatus", "Reviewed");
            ReplacePackageXml(archive, "docProps/core.xml", coreXml);

            var appXml = archive.GetEntry("docProps/app.xml") is { } appEntry
                ? LoadPackageXml(appEntry)
                : new XDocument(new XElement(appNs + "Properties"));
            SetElementValue(appXml.Root!, appNs + "Company", "Freexcel Test Lab");
            SetElementValue(appXml.Root!, appNs + "Manager", "XLSX Fidelity");
            SetElementValue(appXml.Root!, appNs + "Application", "Microsoft Excel");
            ReplacePackageXml(archive, "docProps/app.xml", appXml);
            EnsureContentType(
                archive,
                "/docProps/core.xml",
                "application/vnd.openxmlformats-package.core-properties+xml");
            EnsureContentType(
                archive,
                "/docProps/app.xml",
                "application/vnd.openxmlformats-officedocument.extended-properties+xml");
        }

        packageStream.Position = 0;

        static void SetElementValue(XElement root, XName name, string value)
        {
            var element = root.Element(name);
            if (element is null)
            {
                element = new XElement(name);
                root.Add(element);
            }

            element.Value = value;
        }

        static void EnsureContentType(ZipArchive archive, string partName, string contentType)
        {
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            var contentTypes = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            var existing = contentTypes.Root!
                .Elements(contentTypeNs + "Override")
                .FirstOrDefault(element => element.Attribute("PartName")?.Value == partName);
            if (existing is null)
            {
                contentTypes.Root.Add(new XElement(
                    contentTypeNs + "Override",
                    new XAttribute("PartName", partName),
                    new XAttribute("ContentType", contentType)));
            }
            else
            {
                existing.SetAttributeValue("ContentType", contentType);
            }

            ReplacePackageXml(archive, "[Content_Types].xml", contentTypes);
        }
    }

    private static void AddMinimalCustomSheetViews(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "customSheetViews",
                new XElement(
                    worksheetNs + "customSheetView",
                    new XAttribute("guid", "{11111111-1111-1111-1111-111111111111}"),
                    new XAttribute("scale", "120"),
                    new XAttribute("showGridLines", "0"),
                    new XAttribute("showRowCol", "0"),
                    new XAttribute("state", "visible"),
                    new XElement(
                        worksheetNs + "pane",
                        new XAttribute("xSplit", "1"),
                        new XAttribute("ySplit", "1"),
                        new XAttribute("topLeftCell", "B2"),
                        new XAttribute("activePane", "bottomRight")))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddMatchedCustomViews(MemoryStream packageStream, bool includeNativeOnlyView = false)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.Add(new XElement(
                workbookNs + "customWorkbookViews",
                new XElement(
                    workbookNs + "customWorkbookView",
                    new XAttribute("name", "FreexcelView"),
                    new XAttribute("guid", "{11111111-1111-1111-1111-111111111111}"),
                    new XAttribute("autoUpdate", "0"),
                    new XAttribute("mergeInterval", "0"),
                    new XAttribute("personalView", "0"),
                    new XAttribute("includePrintSettings", "1")),
                includeNativeOnlyView
                    ? new XElement(
                        workbookNs + "customWorkbookView",
                        new XAttribute("name", "NativeOnlyView"),
                        new XAttribute("guid", "{22222222-2222-2222-2222-222222222222}"),
                        new XAttribute("autoUpdate", "0"),
                        new XAttribute("mergeInterval", "0"),
                        new XAttribute("personalView", "0"))
                    : null));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "customSheetViews",
                new XElement(
                    worksheetNs + "customSheetView",
                    new XAttribute("guid", "{11111111-1111-1111-1111-111111111111}"),
                    new XAttribute("scale", "120"),
                    new XAttribute("showGridLines", "0"),
                    new XAttribute("showRowCol", "0"),
                    new XAttribute("state", "visible"),
                    new XElement(
                        worksheetNs + "pane",
                        new XAttribute("xSplit", "1"),
                        new XAttribute("ySplit", "1"),
                        new XAttribute("topLeftCell", "B2"),
                        new XAttribute("activePane", "bottomRight"))),
                includeNativeOnlyView
                    ? new XElement(
                        worksheetNs + "customSheetView",
                        new XAttribute("guid", "{22222222-2222-2222-2222-222222222222}"),
                        new XAttribute("scale", "90"),
                        new XAttribute("state", "visible"))
                    : null));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddMatchedCustomViewsOnTwoSheets(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.Add(new XElement(
                workbookNs + "customWorkbookViews",
                new XElement(
                    workbookNs + "customWorkbookView",
                    new XAttribute("name", "FreexcelView"),
                    new XAttribute("guid", "{11111111-1111-1111-1111-111111111111}"),
                    new XAttribute("autoUpdate", "0"),
                    new XAttribute("mergeInterval", "0"),
                    new XAttribute("personalView", "0"))));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);

            for (var sheetIndex = 1; sheetIndex <= 2; sheetIndex++)
            {
                var worksheetPath = $"xl/worksheets/sheet{sheetIndex}.xml";
                var worksheetXml = LoadPackageXml(archive.GetEntry(worksheetPath)!);
                worksheetXml.Root!.Add(new XElement(
                    worksheetNs + "customSheetViews",
                    new XElement(
                        worksheetNs + "customSheetView",
                        new XAttribute("guid", "{11111111-1111-1111-1111-111111111111}"),
                        new XAttribute("scale", sheetIndex == 1 ? "120" : "90"),
                        new XAttribute("state", "visible"))));
                ReplacePackageXml(archive, worksheetPath, worksheetXml);
            }
        }

        packageStream.Position = 0;
    }

    private static void AddAdditionalWorksheetSheetView(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var sheetViews = worksheetXml.Root!.Element(worksheetNs + "sheetViews");
            if (sheetViews is null)
            {
                sheetViews = new XElement(worksheetNs + "sheetViews");
                worksheetXml.Root.AddFirst(sheetViews);
            }

            sheetViews.Add(new XElement(
                worksheetNs + "sheetView",
                new XAttribute("workbookViewId", "1"),
                new XAttribute("view", "pageBreakPreview"),
                new XAttribute("topLeftCell", "C3"),
                new XAttribute("zoomScale", "80")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddExistingWorksheetSheetViewNativeMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var sheetViews = worksheetXml.Root!.Element(worksheetNs + "sheetViews");
            sheetViews.Should().NotBeNull();
            var sheetView = sheetViews!.Elements(worksheetNs + "sheetView")
                .Single(element => element.Attribute("workbookViewId")?.Value == "0");
            sheetView.SetAttributeValue("showZeros", "0");
            sheetView.SetAttributeValue("rightToLeft", "1");
            sheetView.Add(new XElement(
                worksheetNs + "pivotSelection",
                new XAttribute("pane", "topRight"),
                new XAttribute("showHeader", "1"),
                new XAttribute("axis", "axisRow"),
                new XAttribute("dimension", "0"),
                new XAttribute("start", "0"),
                new XAttribute("min", "0"),
                new XAttribute("max", "0")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetSheetViewsNativeAttributes(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var sheetViews = worksheetXml.Root!.Element(worksheetNs + "sheetViews");
            sheetViews.Should().NotBeNull();
            sheetViews!.SetAttributeValue("nativeSheetViewsAttr", "kept");
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddExistingWorksheetSheetViewChildNativeMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var sheetViews = worksheetXml.Root!.Element(worksheetNs + "sheetViews");
            sheetViews.Should().NotBeNull();
            var sheetView = sheetViews!.Elements(worksheetNs + "sheetView")
                .Single(element => element.Attribute("workbookViewId")?.Value == "0");
            var pane = sheetView.Element(worksheetNs + "pane");
            pane.Should().NotBeNull();
            pane!.SetAttributeValue("customPaneAttr", "pane-native");
            var selection = sheetView.Elements(worksheetNs + "selection")
                .FirstOrDefault(element => element.Attribute("pane")?.Value == "bottomRight")
                ?? sheetView.Element(worksheetNs + "selection");
            selection.Should().NotBeNull();
            selection!.SetAttributeValue("pane", "bottomRight");
            selection.SetAttributeValue("activeCell", "B2");
            selection.SetAttributeValue("sqref", "B2");
            selection.SetAttributeValue("customSelectionAttr", "selection-native");
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetSheetFormatMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var sheetFormat = worksheetXml.Root!.Element(worksheetNs + "sheetFormatPr");
            if (sheetFormat is null)
            {
                sheetFormat = new XElement(worksheetNs + "sheetFormatPr");
                worksheetXml.Root!.AddFirst(sheetFormat);
            }

            sheetFormat.SetAttributeValue("baseColWidth", "12");
            sheetFormat.SetAttributeValue("zeroHeight", "1");
            sheetFormat.SetAttributeValue("thickTop", "1");
            sheetFormat.SetAttributeValue("outlineLevelRow", "3");
            sheetFormat.Add(new XElement(
                worksheetNs + "nativeSheetFormatChild",
                new XAttribute("value", "kept")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetPageBreakMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Elements(worksheetNs + "rowBreaks").Remove();
            worksheetXml.Root!.Elements(worksheetNs + "colBreaks").Remove();
            worksheetXml.Root!.Add(
                new XElement(
                    worksheetNs + "rowBreaks",
                    new XAttribute("count", "1"),
                    new XAttribute("manualBreakCount", "1"),
                    new XElement(
                        worksheetNs + "brk",
                        new XAttribute("id", "20"),
                        new XAttribute("max", "16383"),
                        new XAttribute("man", "1"),
                        new XAttribute("pt", "1"),
                        new XAttribute("customAttr", "row-native"))),
                new XElement(
                    worksheetNs + "colBreaks",
                    new XAttribute("count", "1"),
                    new XAttribute("manualBreakCount", "1"),
                    new XElement(
                        worksheetNs + "brk",
                        new XAttribute("id", "5"),
                        new XAttribute("max", "1048575"),
                        new XAttribute("man", "1"),
                        new XAttribute("pt", "1"),
                        new XAttribute("customAttr", "col-native"))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddMalformedWorksheetPageBreakMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Element(worksheetNs + "rowBreaks")!.Add(
                new XElement(
                    worksheetNs + "brk",
                    new XAttribute("id", "0"),
                    new XAttribute("man", "1"),
                    new XAttribute("customAttr", "row-native-only")));
            worksheetXml.Root!.Element(worksheetNs + "colBreaks")!.Add(
                new XElement(
                    worksheetNs + "brk",
                    new XAttribute("id", "0"),
                    new XAttribute("man", "1"),
                    new XAttribute("customAttr", "col-native-only")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetPrintOptionsMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var printOptions = worksheetXml.Root!.Element(worksheetNs + "printOptions");
            if (printOptions is null)
            {
                printOptions = new XElement(worksheetNs + "printOptions");
                worksheetXml.Root!.Add(printOptions);
            }

            printOptions.SetAttributeValue("gridLinesSet", "1");
            printOptions.SetAttributeValue("customAttr", "print-native");
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetPrintOptionsModeledAndNativeAttributes(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var printOptions = worksheetXml.Root!.Element(worksheetNs + "printOptions");
            if (printOptions is null)
            {
                printOptions = new XElement(worksheetNs + "printOptions");
                worksheetXml.Root!.Add(printOptions);
            }

            printOptions.SetAttributeValue("gridLines", "1");
            printOptions.SetAttributeValue("headings", "1");
            printOptions.SetAttributeValue("horizontalCentered", "1");
            printOptions.SetAttributeValue("verticalCentered", "1");
            printOptions.SetAttributeValue("gridLinesSet", "1");
            printOptions.SetAttributeValue("customAttr", "print-native");
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetPrintOptionsModeledOnlyAttributes(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Element(worksheetNs + "printOptions")?.Remove();
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "printOptions",
                new XAttribute("gridLines", "1"),
                new XAttribute("headings", "1"),
                new XAttribute("horizontalCentered", "1"),
                new XAttribute("verticalCentered", "1")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetPageSetupNativeAttributes(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var pageSetup = worksheetXml.Root!.Element(worksheetNs + "pageSetup");
            if (pageSetup is null)
            {
                pageSetup = new XElement(worksheetNs + "pageSetup");
                worksheetXml.Root!.Add(pageSetup);
            }

            pageSetup.SetAttributeValue("usePrinterDefaults", "1");
            pageSetup.SetAttributeValue("copies", "3");
            pageSetup.SetAttributeValue("customAttr", "page-setup-native");
            pageSetup.Add(new XElement(
                worksheetNs + "nativePageSetupChild",
                new XAttribute("value", "kept")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetPageMarginsHeaderFooterAttributes(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var pageMargins = worksheetXml.Root!.Element(worksheetNs + "pageMargins");
            if (pageMargins is null)
            {
                pageMargins = new XElement(worksheetNs + "pageMargins");
                worksheetXml.Root!.Add(pageMargins);
            }

            pageMargins.SetAttributeValue("header", "0.35");
            pageMargins.SetAttributeValue("footer", "0.45");
            pageMargins.SetAttributeValue("customAttr", "page-margins-native");
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddMergedCellNativeMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var mergeCells = worksheetXml.Root!.Element(worksheetNs + "mergeCells");
            mergeCells.Should().NotBeNull();
            mergeCells!.SetAttributeValue("nativeMergeContainerAttr", "kept");
            var mergeCell = mergeCells.Elements(worksheetNs + "mergeCell")
                .Single(element => element.Attribute("ref")?.Value == "A1:B2");
            mergeCell.SetAttributeValue("nativeMergeCellAttr", "kept");
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetHeaderFooterNativeAttributes(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var headerFooter = worksheetXml.Root!.Element(worksheetNs + "headerFooter");
            headerFooter.Should().NotBeNull();
            headerFooter!.SetAttributeValue("nativeHeaderFooterAttr", "kept");
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetDimensionNativeAttributes(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var dimension = worksheetXml.Root!.Element(worksheetNs + "dimension");
            dimension.Should().NotBeNull();
            dimension!.SetAttributeValue("nativeDimensionAttr", "kept");
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetHyperlinkNativeMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!
                .Element(worksheetNs + "hyperlinks")!
                .SetAttributeValue("nativeHyperlinksAttr", "kept");
            var hyperlink = worksheetXml.Root!
                .Element(worksheetNs + "hyperlinks")!
                .Elements(worksheetNs + "hyperlink")
                .Single(element => element.Attribute("ref")?.Value == "A1");
            hyperlink.SetAttributeValue("tooltip", "Open documentation");
            hyperlink.SetAttributeValue("display", "Freexcel docs");
            hyperlink.SetAttributeValue("customAttr", "hyperlink-native");
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddLegacyCommentRichTextMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var commentsEntry = archive.Entries.Single(entry =>
                entry.FullName.StartsWith("xl/comments", StringComparison.OrdinalIgnoreCase) &&
                entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
            ReplacePackageXml(archive, commentsEntry.FullName, new XDocument(
                new XElement(
                    worksheetNs + "comments",
                    new XElement(
                        worksheetNs + "authors",
                        new XElement(worksheetNs + "author", "Excel Reviewer")),
                    new XElement(
                        worksheetNs + "commentList",
                        new XElement(
                            worksheetNs + "comment",
                            new XAttribute("ref", "C2"),
                            new XAttribute("authorId", "0"),
                            new XElement(
                                worksheetNs + "text",
                                new XElement(
                                    worksheetNs + "r",
                                    new XElement(
                                        worksheetNs + "rPr",
                                        new XElement(worksheetNs + "b"),
                                        new XElement(worksheetNs + "rFont", new XAttribute("val", "FreexcelBold"))),
                                    new XElement(worksheetNs + "t", "Check ")),
                                new XElement(
                                    worksheetNs + "r",
                                    new XElement(worksheetNs + "t", "this input"))))))));
        }

        packageStream.Position = 0;
    }

    private static void AddSharedStringRichTextAndPhonetics(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var sharedStringsXml = LoadPackageXml(archive.GetEntry("xl/sharedStrings.xml")!);
            var sharedString = sharedStringsXml.Root!
                .Elements(worksheetNs + "si")
                .Single(element => element.Element(worksheetNs + "t")?.Value == "Rich phonetic");
            sharedString.ReplaceNodes(
                new XElement(
                    worksheetNs + "r",
                    new XElement(
                        worksheetNs + "rPr",
                        new XElement(worksheetNs + "b"),
                        new XElement(worksheetNs + "rFont", new XAttribute("val", "FreexcelRich"))),
                    new XElement(worksheetNs + "t", "Rich ")),
                new XElement(
                    worksheetNs + "r",
                    new XElement(worksheetNs + "t", "phonetic")),
                new XElement(
                    worksheetNs + "rPh",
                    new XAttribute("sb", "0"),
                    new XAttribute("eb", "4"),
                    new XElement(worksheetNs + "t", "ri-chi")),
                new XElement(
                    worksheetNs + "phoneticPr",
                    new XAttribute("fontId", "1"),
                    new XAttribute("type", "noConversion")));
            ReplacePackageXml(archive, "xl/sharedStrings.xml", sharedStringsXml);
        }

        packageStream.Position = 0;
    }

    private static void AddInlineStringRichTextAndPhonetics(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var cell = worksheetXml.Root!
                .Element(worksheetNs + "sheetData")!
                .Descendants(worksheetNs + "c")
                .Single(element => element.Attribute("r")?.Value == "A1");
            cell.SetAttributeValue("t", "inlineStr");
            cell.Elements(worksheetNs + "v").Remove();
            cell.Add(new XElement(
                worksheetNs + "is",
                new XElement(
                    worksheetNs + "r",
                    new XElement(
                        worksheetNs + "rPr",
                        new XElement(worksheetNs + "i"),
                        new XElement(worksheetNs + "rFont", new XAttribute("val", "FreexcelInline"))),
                    new XElement(worksheetNs + "t", "Inline ")),
                new XElement(
                    worksheetNs + "r",
                    new XElement(worksheetNs + "t", "phonetic")),
                new XElement(
                    worksheetNs + "rPh",
                    new XAttribute("sb", "0"),
                    new XAttribute("eb", "6"),
                    new XElement(worksheetNs + "t", "in-line")),
                new XElement(
                    worksheetNs + "phoneticPr",
                    new XAttribute("fontId", "1"),
                    new XAttribute("type", "noConversion"))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetFormulaNativeMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var formula = worksheetXml.Root!
                .Element(worksheetNs + "sheetData")!
                .Descendants(worksheetNs + "c")
                .Single(element => element.Attribute("r")?.Value == "A2")
                .Element(worksheetNs + "f");
            formula.Should().NotBeNull();
            formula!.SetAttributeValue("t", "array");
            formula.SetAttributeValue("ref", "A2:A2");
            formula.SetAttributeValue("ca", "1");
            formula.SetAttributeValue("customAttr", "formula-native");
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetPageSetupModeledAndNativeAttributes(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var pageSetup = worksheetXml.Root!.Element(worksheetNs + "pageSetup");
            if (pageSetup is null)
            {
                pageSetup = new XElement(worksheetNs + "pageSetup");
                worksheetXml.Root!.Add(pageSetup);
            }

            pageSetup.SetAttributeValue("paperSize", "5");
            pageSetup.SetAttributeValue("scale", null);
            pageSetup.SetAttributeValue("fitToWidth", "2");
            pageSetup.SetAttributeValue("fitToHeight", "3");
            pageSetup.SetAttributeValue("pageOrder", "overThenDown");
            pageSetup.SetAttributeValue("orientation", "landscape");
            pageSetup.SetAttributeValue("firstPageNumber", "7");
            pageSetup.SetAttributeValue("blackAndWhite", "1");
            pageSetup.SetAttributeValue("draft", "1");
            pageSetup.SetAttributeValue("horizontalDpi", "600");
            pageSetup.SetAttributeValue("verticalDpi", "600");
            pageSetup.SetAttributeValue("cellComments", "atEnd");
            pageSetup.SetAttributeValue("errors", "dash");
            pageSetup.SetAttributeValue("usePrinterDefaults", "1");
            pageSetup.SetAttributeValue("copies", "3");
            pageSetup.SetAttributeValue("customAttr", "page-setup-native");
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetPageSetupModeledOnlyAttributes(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Element(worksheetNs + "pageSetup")?.Remove();
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "pageSetup",
                new XAttribute("paperSize", "5"),
                new XAttribute("fitToWidth", "2"),
                new XAttribute("fitToHeight", "3"),
                new XAttribute("pageOrder", "overThenDown"),
                new XAttribute("orientation", "landscape"),
                new XAttribute("firstPageNumber", "7"),
                new XAttribute("blackAndWhite", "1"),
                new XAttribute("draft", "1"),
                new XAttribute("horizontalDpi", "600"),
                new XAttribute("verticalDpi", "600"),
                new XAttribute("cellComments", "atEnd"),
                new XAttribute("errors", "dash")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetRowNativeAttributes(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace freexcelNs = "urn:freexcel:test";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!
                .Element(worksheetNs + "sheetData")!
                .SetAttributeValue("nativeSheetDataAttr", "kept");
            var row = worksheetXml.Root!
                .Element(worksheetNs + "sheetData")!
                .Elements(worksheetNs + "row")
                .Single(element => element.Attribute("r")?.Value == "2");
            row.SetAttributeValue("thickTop", "1");
            row.SetAttributeValue("ph", "1");
            row.SetAttributeValue("customAttr", "row-native");
            row.Add(new XElement(freexcelNs + "rowNativeChild", new XAttribute("value", "kept")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetCellNativeAttributes(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace freexcelNs = "urn:freexcel:test";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var cell = worksheetXml.Root!
                .Element(worksheetNs + "sheetData")!
                .Descendants(worksheetNs + "c")
                .Single(element => element.Attribute("r")?.Value == "A2");
            cell.SetAttributeValue("cm", "2");
            cell.SetAttributeValue("vm", "1");
            cell.SetAttributeValue("ph", "1");
            cell.SetAttributeValue("customAttr", "cell-native");
            cell.Add(new XElement(freexcelNs + "cellNativeChild", new XAttribute("value", "kept")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetRowAndCellExtensionLists(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace freexcelNs = "urn:freexcel:test";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var row = worksheetXml.Root!
                .Element(worksheetNs + "sheetData")!
                .Elements(worksheetNs + "row")
                .Single(element => element.Attribute("r")?.Value == "2");
            row.Add(new XElement(
                worksheetNs + "extLst",
                new XElement(
                    worksheetNs + "ext",
                    new XAttribute("uri", "{FREEXCEL-ROW-EXT}"),
                    new XElement(freexcelNs + "rowExt", new XAttribute("value", "row-extension")))));

            var cell = row.Elements(worksheetNs + "c")
                .Single(element => element.Attribute("r")?.Value == "A2");
            cell.Add(new XElement(
                worksheetNs + "extLst",
                new XElement(
                    worksheetNs + "ext",
                    new XAttribute("uri", "{FREEXCEL-CELL-EXT}"),
                    new XElement(freexcelNs + "cellExt", new XAttribute("value", "cell-extension")))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetColumnNativeAttributes(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var columns = worksheetXml.Root!.Element(worksheetNs + "cols");
            columns.Should().NotBeNull();
            columns!.SetAttributeValue("nativeColsAttr", "kept");
            var column = columns!.Elements(worksheetNs + "col")
                .Single(element => element.Attribute("min")?.Value == "2" && element.Attribute("max")?.Value == "2");
            column.SetAttributeValue("bestFit", "1");
            column.SetAttributeValue("phonetic", "1");
            column.SetAttributeValue("customAttr", "column-native");
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalWorksheetScenarios(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "scenarios",
                new XAttribute("current", "0"),
                new XAttribute("show", "0"),
                new XElement(
                    worksheetNs + "scenario",
                    new XAttribute("name", "BestCase"),
                    new XAttribute("locked", "1"),
                    new XAttribute("count", "1"),
                    new XAttribute("user", "FreexcelTest"),
                    new XElement(
                        worksheetNs + "inputCells",
                        new XAttribute("r", "A1"),
                        new XAttribute("val", "42")))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddUnsupportedWorksheetScenario(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "scenarios",
                new XElement(
                    worksheetNs + "scenario",
                    new XAttribute("name", "NativeOnly"),
                    new XAttribute("count", "1"),
                    new XAttribute("comment", "range reference is outside Freexcel's supported subset"),
                    new XElement(
                        worksheetNs + "inputCells",
                        new XAttribute("r", "A1:B1"),
                        new XAttribute("val", "42")))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddUnsupportedWorksheetSheetProperties(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace freexcelNs = "urn:freexcel:test";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var sheetPr = worksheetXml.Root!.Element(worksheetNs + "sheetPr");
            if (sheetPr is null)
            {
                sheetPr = new XElement(worksheetNs + "sheetPr");
                worksheetXml.Root!.AddFirst(sheetPr);
            }

            sheetPr.SetAttributeValue("filterMode", "1");
            if (sheetPr.Element(worksheetNs + "pageSetUpPr") is null)
            {
                sheetPr.Add(new XElement(
                    worksheetNs + "pageSetUpPr",
                    new XAttribute("autoPageBreaks", "0")));
            }

            sheetPr.Add(
                new XElement(freexcelNs + "sheetPrNativeChild", new XAttribute("id", "first")),
                new XElement(freexcelNs + "sheetPrNativeChild", new XAttribute("id", "second")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetIgnoredErrors(
        MemoryStream packageStream,
        string sqref = "A1",
        params (string Name, string Value)[] flags)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            if (flags.Length == 0)
                flags = [("numberStoredAsText", "1")];

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            var ignoredError = new XElement(
                worksheetNs + "ignoredError",
                new XAttribute("sqref", sqref));
            foreach (var (name, value) in flags)
                ignoredError.SetAttributeValue(name, value);

            worksheetXml.Root!.Add(new XElement(worksheetNs + "ignoredErrors", ignoredError));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetCellWatches(MemoryStream packageStream) =>
        AddWorksheetCellWatches(packageStream, "xl/worksheets/sheet1.xml", ("A1", null, null));

    private static void AddWorksheetCellWatches(
        MemoryStream packageStream,
        string worksheetPath,
        params (string Reference, string? AttributeName, string? AttributeValue)[] watches)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry(worksheetPath)!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "cellWatches",
                watches.Select(watch =>
                {
                    var element = new XElement(
                        worksheetNs + "cellWatch",
                        new XAttribute("r", watch.Reference));
                    if (!string.IsNullOrWhiteSpace(watch.AttributeName))
                        element.SetAttributeValue(watch.AttributeName, watch.AttributeValue);

                    return element;
                })));
            ReplacePackageXml(archive, worksheetPath, worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetCalculationProperties(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "sheetCalcPr",
                new XAttribute("fullCalcOnLoad", "1"),
                new XAttribute("calcId", "999")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetPhoneticProperties(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "phoneticPr",
                new XAttribute("fontId", "1"),
                new XAttribute("type", "fullwidthKatakana"),
                new XAttribute("alignment", "center"),
                new XAttribute("nativeOnly", "kept")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetSortState(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "sortState",
                new XAttribute("ref", "A1:A3"),
                new XElement(
                    worksheetNs + "sortCondition",
                    new XAttribute("ref", "A2:A3"),
                    new XAttribute("descending", "1"))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetDataConsolidation(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "dataConsolidate",
                new XAttribute("function", "sum"),
                new XAttribute("leftLabels", "1"),
                new XAttribute("topLabels", "1"),
                new XAttribute("link", "1"),
                new XElement(
                    worksheetNs + "dataRefs",
                    new XAttribute("count", "1"),
                    new XElement(
                        worksheetNs + "dataRef",
                        new XAttribute("ref", "A1:B2"),
                        new XAttribute("sheet", "Data")))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetCustomProperties(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "customProperties",
                new XElement(
                    worksheetNs + "customPr",
                    new XAttribute("name", "FreexcelNativeProperty"),
                    new XAttribute("id", "1"),
                    new XAttribute("unsupportedAttr", "kept"))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetSmartTags(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "smartTags",
                new XElement(
                    worksheetNs + "cellSmartTags",
                    new XAttribute("r", "A1"),
                    new XElement(
                        worksheetNs + "cellSmartTag",
                        new XAttribute("type", "0"),
                        new XAttribute("deleted", "0"),
                        new XElement(
                            worksheetNs + "cellSmartTagPr",
                            new XAttribute("key", "place"),
                            new XAttribute("val", "Seattle"))))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddWorksheetAutoFilterMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "autoFilter",
                new XAttribute("ref", "A1:B3"),
                new XElement(
                    worksheetNs + "filterColumn",
                    new XAttribute("colId", "0"),
                    new XElement(
                        worksheetNs + "filters",
                        new XAttribute("blank", "1"),
                        new XElement(worksheetNs + "filter", new XAttribute("val", "A"))))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddAdvancedSheetProtectionMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace freexcelNs = "urn:freexcel:test";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Element(worksheetNs + "sheetProtection")?.Remove();
            worksheetXml.Root.Add(new XElement(
                worksheetNs + "sheetProtection",
                new XAttribute("sheet", "1"),
                new XAttribute("algorithmName", "SHA-512"),
                new XAttribute("hashValue", "abc123"),
                new XAttribute("saltValue", "salt123"),
                new XAttribute("spinCount", "100000"),
                new XAttribute("objects", "1"),
                new XAttribute("scenarios", "1"),
                new XElement(freexcelNs + "sheetProtectionNativeChild", new XAttribute("id", "first")),
                new XElement(freexcelNs + "sheetProtectionNativeChild", new XAttribute("id", "second"))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddProtectedRangeMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace freexcelNs = "urn:freexcel:test";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "protectedRanges",
                new XElement(
                    worksheetNs + "protectedRange",
                    new XAttribute("name", "NativeEditableRange"),
                    new XAttribute("sqref", "B2:C3"),
                    new XAttribute("password", "ABCD"),
                    new XAttribute("securityDescriptor", "D:PAI"),
                    new XElement(
                        worksheetNs + "extLst",
                        new XElement(
                            worksheetNs + "ext",
                            new XAttribute("uri", "{FREEXCEL-PROTECTED-RANGE-TEST}"))),
                    new XElement(freexcelNs + "protectedRangeNativeChild", new XAttribute("id", "first")),
                    new XElement(freexcelNs + "protectedRangeNativeChild", new XAttribute("id", "second")))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddMultiAreaProtectedRangeMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "protectedRanges",
                new XElement(
                    worksheetNs + "protectedRange",
                    new XAttribute("name", "NativeMultiAreaRange"),
                    new XAttribute("sqref", "B2 C3"),
                    new XAttribute("password", "1234"))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddAdvancedWorkbookProtectionMetadata(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace freexcelNs = "urn:freexcel:test";

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.Element(workbookNs + "workbookProtection")?.Remove();
            workbookXml.Root.AddFirst(new XElement(
                workbookNs + "workbookProtection",
                new XAttribute("lockStructure", "1"),
                new XAttribute("lockWindows", "1"),
                new XAttribute("workbookPassword", "83AF"),
                new XAttribute("algorithmName", "SHA-512"),
                new XAttribute("hashValue", "def456"),
                new XAttribute("saltValue", "salt456"),
                new XAttribute("spinCount", "100000"),
                new XElement(freexcelNs + "workbookProtectionNativeChild", new XAttribute("id", "first")),
                new XElement(freexcelNs + "workbookProtectionNativeChild", new XAttribute("id", "second"))));
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalStructuredTablePackage(
        MemoryStream packageStream,
        bool includeTotalsRow = false,
        bool includeFilterValues = false,
        bool includeColumnFormulas = false,
        bool includeCustomFilter = false,
        bool includeCustomFilterWithExtension = false,
        bool includeSortState = false,
        bool includeColumnExtension = false,
        bool includeColumnAttributes = false,
        bool includeStyleInfoExtension = false,
        bool includeRootMetadata = false,
        bool includeFilterColumnAttributes = false,
        bool includeAutoFilterMetadata = false)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(contentTypesXml, contentTypeNs, "/xl/tables/table1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.table+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            var worksheetXml = LoadPackageXml(worksheetEntry);
            worksheetXml.Root!.Elements(worksheetNs + "tableParts").Remove();
            worksheetXml.Root!.Add(new XElement(
                worksheetNs + "tableParts",
                new XAttribute("count", "1"),
                new XElement(worksheetNs + "tablePart", new XAttribute(relNs + "id", "rIdFreexcelTable"))));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadPackageXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreexcelTable"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/table"),
                new XAttribute("Target", "../tables/table1.xml")));
            ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            ReplacePackageXml(
                archive,
                "xl/tables/table1.xml",
                XDocument.Parse(includeTotalsRow
                    ? includeFilterValues
                        ? StructuredTableWithTotalsRowAndFilterValuesXml
                        : StructuredTableWithTotalsRowXml
                    : includeFilterValues
                        ? StructuredTableWithFilterValuesXml
                        : includeAutoFilterMetadata
                            ? StructuredTableWithAutoFilterMetadataXml
                        : includeFilterColumnAttributes
                            ? StructuredTableWithFilterColumnAttributesXml
                        : includeRootMetadata
                            ? StructuredTableWithRootMetadataXml
                        : includeStyleInfoExtension
                            ? StructuredTableWithStyleInfoExtensionXml
                        : includeColumnAttributes
                            ? StructuredTableWithColumnAttributesXml
                        : includeColumnExtension
                            ? StructuredTableWithColumnExtensionXml
                        : includeSortState
                            ? StructuredTableWithSortStateXml
                        : includeCustomFilterWithExtension
                            ? StructuredTableWithCustomFilterAndExtensionXml
                        : includeCustomFilter
                            ? StructuredTableWithCustomFilterXml
                        : includeColumnFormulas
                            ? StructuredTableWithColumnFormulasXml
                        : MinimalStructuredTableXml));
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalPicturePackage(
        MemoryStream packageStream,
        byte[] imageBytes,
        double cropLeft = 0,
        double cropTop = 0,
        double cropRight = 0,
        double cropBottom = 0)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            contentTypesXml.Root!
                .Elements(contentTypeNs + "Default")
                .Where(element => string.Equals(element.Attribute("Extension")?.Value, "png", StringComparison.OrdinalIgnoreCase))
                .Remove();
            contentTypesXml.Root!.Add(new XElement(
                contentTypeNs + "Default",
                new XAttribute("Extension", "png"),
                new XAttribute("ContentType", "image/png")));
            AddContentTypeOverride(contentTypesXml, contentTypeNs, "/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Elements(worksheetNs + "drawing").Remove();
            worksheetXml.Root!.Add(new XElement(worksheetNs + "drawing", new XAttribute(relNs + "id", "rIdFreexcelPictureDrawing")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadPackageXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreexcelPictureDrawing"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
                new XAttribute("Target", "../drawings/drawing1.xml")));
            ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            var drawingXml = new XDocument(
                new XElement(spreadsheetDrawingNs + "wsDr",
                    new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                    new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                    new XAttribute(XNamespace.Xmlns + "r", relNs),
                    new XElement(spreadsheetDrawingNs + "oneCellAnchor",
                        new XElement(spreadsheetDrawingNs + "from",
                            new XElement(spreadsheetDrawingNs + "col", "2"),
                            new XElement(spreadsheetDrawingNs + "colOff", "0"),
                            new XElement(spreadsheetDrawingNs + "row", "1"),
                            new XElement(spreadsheetDrawingNs + "rowOff", "0")),
                        new XElement(spreadsheetDrawingNs + "ext",
                            new XAttribute("cx", "1143000"),
                            new XAttribute("cy", "762000")),
                        new XElement(spreadsheetDrawingNs + "pic",
                            new XElement(spreadsheetDrawingNs + "nvPicPr",
                                new XElement(spreadsheetDrawingNs + "cNvPr",
                                    new XAttribute("id", "2"),
                                    new XAttribute("name", "Picture 1"),
                                    new XAttribute("descr", "Native picture")),
                                new XElement(spreadsheetDrawingNs + "cNvPicPr")),
                            new XElement(spreadsheetDrawingNs + "blipFill",
                                new XElement(drawingNs + "blip", new XAttribute(relNs + "embed", "rIdFreexcelPictureImage")),
                                HasPictureCrop(cropLeft, cropTop, cropRight, cropBottom)
                                    ? new XElement(drawingNs + "srcRect",
                                        new XAttribute("l", ToSourceRectanglePercent(cropLeft)),
                                        new XAttribute("t", ToSourceRectanglePercent(cropTop)),
                                        new XAttribute("r", ToSourceRectanglePercent(cropRight)),
                                        new XAttribute("b", ToSourceRectanglePercent(cropBottom)))
                                    : null,
                                new XElement(drawingNs + "stretch", new XElement(drawingNs + "fillRect"))),
                            new XElement(spreadsheetDrawingNs + "spPr",
                                new XElement(drawingNs + "xfrm"),
                                new XElement(drawingNs + "prstGeom", new XAttribute("prst", "rect"), new XElement(drawingNs + "avLst")))),
                        new XElement(spreadsheetDrawingNs + "clientData"))));
            ReplacePackageXml(archive, "xl/drawings/drawing1.xml", drawingXml);

            var drawingRelsXml = new XDocument(
                new XElement(packageRelNs + "Relationships",
                    new XElement(packageRelNs + "Relationship",
                        new XAttribute("Id", "rIdFreexcelPictureImage"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image"),
                        new XAttribute("Target", "../media/image1.png"))));
            ReplacePackageXml(archive, "xl/drawings/_rels/drawing1.xml.rels", drawingRelsXml);

            archive.GetEntry("xl/media/image1.png")?.Delete();
            var imageEntry = archive.CreateEntry("xl/media/image1.png");
            using var imageStream = imageEntry.Open();
            imageStream.Write(imageBytes);
        }

        packageStream.Position = 0;
    }

    private static bool HasPictureCrop(double left, double top, double right, double bottom) =>
        left > 0 || top > 0 || right > 0 || bottom > 0;

    private static string ToSourceRectanglePercent(double ratio) =>
        ((int)Math.Round(Math.Clamp(ratio, 0, 1) * 100000)).ToString(CultureInfo.InvariantCulture);

    private static void AddMinimalShapePackage(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(contentTypesXml, contentTypeNs, "/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Elements(worksheetNs + "drawing").Remove();
            worksheetXml.Root!.Add(new XElement(worksheetNs + "drawing", new XAttribute(relNs + "id", "rIdFreexcelShapeDrawing")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadPackageXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreexcelShapeDrawing"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
                new XAttribute("Target", "../drawings/drawing1.xml")));
            ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            var drawingXml = new XDocument(
                new XElement(spreadsheetDrawingNs + "wsDr",
                    new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                    new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                    new XElement(spreadsheetDrawingNs + "oneCellAnchor",
                        new XElement(spreadsheetDrawingNs + "from",
                            new XElement(spreadsheetDrawingNs + "col", "1"),
                            new XElement(spreadsheetDrawingNs + "colOff", "0"),
                            new XElement(spreadsheetDrawingNs + "row", "1"),
                            new XElement(spreadsheetDrawingNs + "rowOff", "0")),
                        new XElement(spreadsheetDrawingNs + "ext", new XAttribute("cx", "1524000"), new XAttribute("cy", "666750")),
                        new XElement(spreadsheetDrawingNs + "sp",
                            new XElement(spreadsheetDrawingNs + "nvSpPr",
                                new XElement(spreadsheetDrawingNs + "cNvPr",
                                    new XAttribute("id", "2"),
                                    new XAttribute("name", "TextBox 1"),
                                    new XAttribute("descr", "Native text box")),
                                new XElement(spreadsheetDrawingNs + "cNvSpPr", new XAttribute("txBox", "1"))),
                            new XElement(spreadsheetDrawingNs + "spPr",
                                new XElement(drawingNs + "xfrm"),
                                new XElement(drawingNs + "prstGeom", new XAttribute("prst", "rect"), new XElement(drawingNs + "avLst")),
                                new XElement(drawingNs + "solidFill", new XElement(drawingNs + "srgbClr", new XAttribute("val", "FFEECC"))),
                                new XElement(drawingNs + "ln", new XElement(drawingNs + "solidFill", new XElement(drawingNs + "srgbClr", new XAttribute("val", "667788"))))),
                            new XElement(spreadsheetDrawingNs + "txBody",
                                new XElement(drawingNs + "bodyPr"),
                                new XElement(drawingNs + "lstStyle"),
                                new XElement(drawingNs + "p", new XElement(drawingNs + "r", new XElement(drawingNs + "t", "Native note"))))),
                        new XElement(spreadsheetDrawingNs + "clientData")),
                    new XElement(spreadsheetDrawingNs + "oneCellAnchor",
                        new XElement(spreadsheetDrawingNs + "from",
                            new XElement(spreadsheetDrawingNs + "col", "3"),
                            new XElement(spreadsheetDrawingNs + "colOff", "0"),
                            new XElement(spreadsheetDrawingNs + "row", "4"),
                            new XElement(spreadsheetDrawingNs + "rowOff", "0")),
                        new XElement(spreadsheetDrawingNs + "ext", new XAttribute("cx", "1143000"), new XAttribute("cy", "762000")),
                        new XElement(spreadsheetDrawingNs + "sp",
                            new XElement(spreadsheetDrawingNs + "nvSpPr",
                                new XElement(spreadsheetDrawingNs + "cNvPr",
                                    new XAttribute("id", "3"),
                                    new XAttribute("name", "Ellipse 1"),
                                    new XAttribute("descr", "Native ellipse")),
                                new XElement(spreadsheetDrawingNs + "cNvSpPr")),
                            new XElement(spreadsheetDrawingNs + "spPr",
                                new XElement(drawingNs + "xfrm"),
                                new XElement(drawingNs + "prstGeom", new XAttribute("prst", "ellipse"), new XElement(drawingNs + "avLst")),
                                new XElement(drawingNs + "gradFill",
                                    new XElement(drawingNs + "gsLst",
                                        new XElement(drawingNs + "gs",
                                            new XAttribute("pos", "0"),
                                            new XElement(drawingNs + "srgbClr", new XAttribute("val", "DDEEFF"))),
                                        new XElement(drawingNs + "gs",
                                            new XAttribute("pos", "100000"),
                                            new XElement(drawingNs + "srgbClr", new XAttribute("val", "EEF8FF")))),
                                    new XElement(drawingNs + "lin", new XAttribute("ang", "5400000"), new XAttribute("scaled", "1"))),
                                new XElement(drawingNs + "ln", new XElement(drawingNs + "solidFill", new XElement(drawingNs + "srgbClr", new XAttribute("val", "112233")))),
                                new XElement(drawingNs + "effectLst",
                                    new XElement(drawingNs + "outerShdw",
                                        new XAttribute("blurRad", "40000"),
                                        new XAttribute("dist", "20000"),
                                        new XAttribute("dir", "5400000"),
                                        new XElement(drawingNs + "srgbClr", new XAttribute("val", "808080")))))),
                        new XElement(spreadsheetDrawingNs + "clientData"))));
            ReplacePackageXml(archive, "xl/drawings/drawing1.xml", drawingXml);
        }

        packageStream.Position = 0;
    }

    private static void AddUnsupportedDrawingPackage(MemoryStream packageStream)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(contentTypesXml, contentTypeNs, "/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Elements(worksheetNs + "drawing").Remove();
            worksheetXml.Root!.Add(new XElement(worksheetNs + "drawing", new XAttribute(relNs + "id", "rIdNativeUnsupportedDrawing")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadPackageXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdNativeUnsupportedDrawing"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
                new XAttribute("Target", "../drawings/drawing1.xml")));
            ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            var drawingXml = new XDocument(
                new XElement(spreadsheetDrawingNs + "wsDr",
                    new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                    new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                    new XElement(spreadsheetDrawingNs + "twoCellAnchor",
                        new XElement(spreadsheetDrawingNs + "from",
                            new XElement(spreadsheetDrawingNs + "col", "1"),
                            new XElement(spreadsheetDrawingNs + "colOff", "0"),
                            new XElement(spreadsheetDrawingNs + "row", "1"),
                            new XElement(spreadsheetDrawingNs + "rowOff", "0")),
                        new XElement(spreadsheetDrawingNs + "to",
                            new XElement(spreadsheetDrawingNs + "col", "3"),
                            new XElement(spreadsheetDrawingNs + "colOff", "0"),
                            new XElement(spreadsheetDrawingNs + "row", "3"),
                            new XElement(spreadsheetDrawingNs + "rowOff", "0")),
                        new XElement(spreadsheetDrawingNs + "cxnSp",
                            new XElement(spreadsheetDrawingNs + "nvCxnSpPr",
                                new XElement(spreadsheetDrawingNs + "cNvPr",
                                    new XAttribute("id", "2"),
                                    new XAttribute("name", "Native connector")),
                                new XElement(spreadsheetDrawingNs + "cNvCxnSpPr")),
                            new XElement(spreadsheetDrawingNs + "spPr",
                                new XElement(drawingNs + "prstGeom", new XAttribute("prst", "line"), new XElement(drawingNs + "avLst")))),
                        new XElement(spreadsheetDrawingNs + "clientData")),
                    new XElement(spreadsheetDrawingNs + "twoCellAnchor",
                        new XElement(spreadsheetDrawingNs + "from",
                            new XElement(spreadsheetDrawingNs + "col", "4"),
                            new XElement(spreadsheetDrawingNs + "colOff", "0"),
                            new XElement(spreadsheetDrawingNs + "row", "1"),
                            new XElement(spreadsheetDrawingNs + "rowOff", "0")),
                        new XElement(spreadsheetDrawingNs + "to",
                            new XElement(spreadsheetDrawingNs + "col", "6"),
                            new XElement(spreadsheetDrawingNs + "colOff", "0"),
                            new XElement(spreadsheetDrawingNs + "row", "4"),
                            new XElement(spreadsheetDrawingNs + "rowOff", "0")),
                        new XElement(spreadsheetDrawingNs + "grpSp",
                            new XElement(spreadsheetDrawingNs + "nvGrpSpPr",
                                new XElement(spreadsheetDrawingNs + "cNvPr",
                                    new XAttribute("id", "3"),
                                    new XAttribute("name", "Native group")),
                                new XElement(spreadsheetDrawingNs + "cNvGrpSpPr")),
                            new XElement(spreadsheetDrawingNs + "grpSpPr",
                                new XElement(drawingNs + "xfrm"))),
                        new XElement(spreadsheetDrawingNs + "clientData"))));
            ReplacePackageXml(archive, "xl/drawings/drawing1.xml", drawingXml);
        }

        packageStream.Position = 0;
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private static void AddMinimalSparklineWorksheetExtension(MemoryStream packageStream, bool includeUnknownExtension = false)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace x14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
            XNamespace x15Ns = "http://schemas.microsoft.com/office/spreadsheetml/2010/11/main";
            XNamespace xmNs = "http://schemas.microsoft.com/office/excel/2006/main";
            var worksheetXml = LoadPackageXml(archive.GetEntry("xl/worksheets/sheet1.xml")!);
            worksheetXml.Root!.Elements(worksheetNs + "extLst").Remove();
            var sparkline = new XElement(
                x14Ns + "sparkline",
                new XElement(xmNs + "f", "Sheet1!A1:C1"),
                new XElement(xmNs + "sqref", "D1"));
            var sparklineGroup = new XElement(
                x14Ns + "sparklineGroup",
                new XAttribute("type", "column"),
                new XElement(x14Ns + "sparklines", sparkline));
            var extLst = new XElement(
                worksheetNs + "extLst",
                new XElement(
                    worksheetNs + "ext",
                    new XAttribute("uri", "{05C60535-1F16-4fd2-B633-F4F36F0B64E0}"),
                    new XElement(
                        x14Ns + "sparklineGroups",
                        new XAttribute(XNamespace.Xmlns + "x14", x14Ns),
                        new XAttribute(XNamespace.Xmlns + "xm", xmNs),
                        sparklineGroup)));
            if (includeUnknownExtension)
            {
                extLst.Add(new XElement(
                    worksheetNs + "ext",
                    new XAttribute("uri", "{FFEEDDCC-BBAA-9988-7766-554433221100}"),
                    new XElement(
                        x15Ns + "futureMetadata",
                        new XAttribute(XNamespace.Xmlns + "x15", x15Ns),
                        new XAttribute("name", "FreexcelUnknownWorksheetExtension"))));
            }

            worksheetXml.Root!.Add(extLst);
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalPivotTablePackage(
        MemoryStream packageStream,
        bool includeCacheRecords = false,
        string? pivotCacheDefinitionXml = null,
        string? pivotTableDefinitionXml = null)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace workbookNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";

            var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
            AddContentTypeOverride(contentTypesXml, contentTypeNs, "/xl/pivotCache/pivotCacheDefinition1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheDefinition+xml");
            if (includeCacheRecords)
                AddContentTypeOverride(contentTypesXml, contentTypeNs, "/xl/pivotCache/pivotCacheRecords1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotCacheRecords+xml");
            AddContentTypeOverride(contentTypesXml, contentTypeNs, "/xl/pivotTables/pivotTable1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.pivotTable+xml");
            ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

            var workbookXml = LoadPackageXml(archive.GetEntry("xl/workbook.xml")!);
            workbookXml.Root!.Elements(workbookNs + "pivotCaches").Remove();
            var sheetsElement = workbookXml.Root.Element(workbookNs + "sheets");
            var pivotCachesElement = new XElement(workbookNs + "pivotCaches",
                new XElement(workbookNs + "pivotCache",
                    new XAttribute("cacheId", "1"),
                    new XAttribute(relNs + "id", "rIdFreexcelPivotCache")));
            if (sheetsElement is not null)
                sheetsElement.AddBeforeSelf(pivotCachesElement);
            else
                workbookXml.Root.Add(pivotCachesElement);
            ReplacePackageXml(archive, "xl/workbook.xml", workbookXml);

            var workbookRelsPath = "xl/_rels/workbook.xml.rels";
            var workbookRelsXml = LoadPackageXml(archive.GetEntry(workbookRelsPath)!);
            workbookRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreexcelPivotCache"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition"),
                new XAttribute("Target", "pivotCache/pivotCacheDefinition1.xml")));
            ReplacePackageXml(archive, workbookRelsPath, workbookRelsXml);

            var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            var worksheetXml = LoadPackageXml(worksheetEntry);
            worksheetXml.Root!.Elements(workbookNs + "pivotTableDefinition").Remove();
            worksheetXml.Root!.Add(new XElement(workbookNs + "pivotTableDefinition", new XAttribute(relNs + "id", "rIdFreexcelPivotTable")));
            ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

            var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
            var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                ? LoadPackageXml(worksheetRelsEntry)
                : new XDocument(new XElement(packageRelNs + "Relationships"));
            worksheetRelsXml.Root!.Add(new XElement(
                packageRelNs + "Relationship",
                new XAttribute("Id", "rIdFreexcelPivotTable"),
                new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotTable"),
                new XAttribute("Target", "../pivotTables/pivotTable1.xml")));
            ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

            var pivotTableRelsXml = new XDocument(
                new XElement(packageRelNs + "Relationships",
                    new XElement(packageRelNs + "Relationship",
                        new XAttribute("Id", "rIdFreexcelPivotCache"),
                        new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheDefinition"),
                        new XAttribute("Target", "../pivotCache/pivotCacheDefinition1.xml"))));
            ReplacePackageXml(archive, "xl/pivotTables/_rels/pivotTable1.xml.rels", pivotTableRelsXml);

            ReplacePackageXml(archive, "xl/pivotCache/pivotCacheDefinition1.xml", XDocument.Parse(pivotCacheDefinitionXml ?? MinimalPivotCacheDefinitionXml));
            if (includeCacheRecords)
            {
                var cacheRelsXml = new XDocument(
                    new XElement(packageRelNs + "Relationships",
                        new XElement(packageRelNs + "Relationship",
                            new XAttribute("Id", "rIdFreexcelPivotCacheRecords"),
                            new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/pivotCacheRecords"),
                            new XAttribute("Target", "pivotCacheRecords1.xml"))));
                ReplacePackageXml(archive, "xl/pivotCache/_rels/pivotCacheDefinition1.xml.rels", cacheRelsXml);
                ReplacePackageXml(archive, "xl/pivotCache/pivotCacheRecords1.xml", XDocument.Parse(MinimalPivotCacheRecordsXml));
            }
            ReplacePackageXml(archive, "xl/pivotTables/pivotTable1.xml", XDocument.Parse(pivotTableDefinitionXml ?? MinimalPivotTableDefinitionXml));
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalSlicerTimelinePackage(MemoryStream packageStream, bool includeDrawing = false)
    {
        using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            XNamespace worksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace relNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            XNamespace packageRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";
            XNamespace contentTypeNs = "http://schemas.openxmlformats.org/package/2006/content-types";
            XNamespace drawingNs = "http://schemas.openxmlformats.org/drawingml/2006/main";
            XNamespace spreadsheetDrawingNs = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

            ReplacePackageXml(archive, "xl/slicers/slicer1.xml", XDocument.Parse("""
                <slicer xmlns="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main"
                        name="Region Slicer"
                        cache="Slicer_Region"
                        caption="Region"
                        style="SlicerStyleLight2"/>
                """));
            ReplacePackageXml(archive, "xl/slicerCaches/slicerCache1.xml", XDocument.Parse("""
                <slicerCacheDefinition xmlns="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main"
                                       name="Slicer_Region"
                                       sourceName="Region">
                  <pivotTables>
                    <pivotTable name="PivotTable1"/>
                  </pivotTables>
                </slicerCacheDefinition>
                """));
            ReplacePackageXml(archive, "xl/timelines/timeline1.xml", XDocument.Parse("""
                <timeline xmlns="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main"
                          name="Date Timeline"
                          cache="Timeline_Date"
                          caption="Order Date"
                          style="TimeSlicerStyleLight1"/>
                """));
            ReplacePackageXml(archive, "xl/timelineCaches/timelineCache1.xml", XDocument.Parse("""
                <timelineCacheDefinition xmlns="http://schemas.microsoft.com/office/spreadsheetml/2010/11/main"
                                         name="Timeline_Date"
                                         sourceName="Date"
                                         startDate="2026-01-01"
                                         endDate="2026-03-31">
                  <pivotTables>
                    <pivotTable name="PivotTable1"/>
                  </pivotTables>
                </timelineCacheDefinition>
                """));

            if (includeDrawing)
            {
                var contentTypesXml = LoadPackageXml(archive.GetEntry("[Content_Types].xml")!);
                AddContentTypeOverride(contentTypesXml, contentTypeNs, "/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml");
                ReplacePackageXml(archive, "[Content_Types].xml", contentTypesXml);

                var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
                var worksheetXml = LoadPackageXml(worksheetEntry);
                worksheetXml.Root!.Elements(worksheetNs + "drawing").Remove();
                worksheetXml.Root!.Add(new XElement(worksheetNs + "drawing", new XAttribute(relNs + "id", "rIdNativeSlicerDrawing")));
                ReplacePackageXml(archive, "xl/worksheets/sheet1.xml", worksheetXml);

                var worksheetRelsPath = "xl/worksheets/_rels/sheet1.xml.rels";
                var worksheetRelsXml = archive.GetEntry(worksheetRelsPath) is { } worksheetRelsEntry
                    ? LoadPackageXml(worksheetRelsEntry)
                    : new XDocument(new XElement(packageRelNs + "Relationships"));
                worksheetRelsXml.Root!.Add(new XElement(
                    packageRelNs + "Relationship",
                    new XAttribute("Id", "rIdNativeSlicerDrawing"),
                    new XAttribute("Type", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing"),
                    new XAttribute("Target", "../drawings/drawing1.xml")));
                ReplacePackageXml(archive, worksheetRelsPath, worksheetRelsXml);

                var drawingXml = new XDocument(
                    new XElement(spreadsheetDrawingNs + "wsDr",
                        new XAttribute(XNamespace.Xmlns + "xdr", spreadsheetDrawingNs),
                        new XAttribute(XNamespace.Xmlns + "a", drawingNs),
                        new XAttribute(XNamespace.Xmlns + "r", relNs),
                        new XElement(spreadsheetDrawingNs + "twoCellAnchor",
                            new XElement(spreadsheetDrawingNs + "from",
                                new XElement(spreadsheetDrawingNs + "col", "2"),
                                new XElement(spreadsheetDrawingNs + "colOff", "0"),
                                new XElement(spreadsheetDrawingNs + "row", "2"),
                                new XElement(spreadsheetDrawingNs + "rowOff", "0")),
                            new XElement(spreadsheetDrawingNs + "to",
                                new XElement(spreadsheetDrawingNs + "col", "5"),
                                new XElement(spreadsheetDrawingNs + "colOff", "0"),
                                new XElement(spreadsheetDrawingNs + "row", "10"),
                                new XElement(spreadsheetDrawingNs + "rowOff", "0")),
                            new XElement(spreadsheetDrawingNs + "sp",
                                new XElement(spreadsheetDrawingNs + "nvSpPr",
                                    new XElement(spreadsheetDrawingNs + "cNvPr",
                                        new XAttribute("id", "100"),
                                        new XAttribute("name", "Native Slicer Shape")),
                                    new XElement(spreadsheetDrawingNs + "cNvSpPr")),
                                new XElement(spreadsheetDrawingNs + "spPr"),
                                new XElement(spreadsheetDrawingNs + "txBody",
                                    new XElement(drawingNs + "bodyPr"),
                                    new XElement(drawingNs + "lstStyle"),
                                    new XElement(drawingNs + "p"))),
                            new XElement(spreadsheetDrawingNs + "clientData"))));
                ReplacePackageXml(archive, "xl/drawings/drawing1.xml", drawingXml);
            }
        }

        packageStream.Position = 0;
    }

    private static void AddMinimalColumnChartPackage(
        MemoryStream packageStream,
        bool useOneCellAnchor = false,
        bool useAbsoluteAnchor = false,
        string? chartXml = null,
        string worksheetPath = "xl/worksheets/sheet1.xml")
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

            var worksheetEntry = archive.GetEntry(worksheetPath)!;
            var worksheetXml = LoadPackageXml(worksheetEntry);
            worksheetXml.Root!.Elements(worksheetNs + "drawing").Remove();
            worksheetXml.Root!.Add(new XElement(worksheetNs + "drawing", new XAttribute(relNs + "id", "rIdFreexcelChartDrawing")));
            ReplacePackageXml(archive, worksheetPath, worksheetXml);

            var worksheetFileName = Path.GetFileName(worksheetPath);
            var worksheetDirectory = Path.GetDirectoryName(worksheetPath)?.Replace('\\', '/') ?? "xl/worksheets";
            var worksheetRelsPath = $"{worksheetDirectory}/_rels/{worksheetFileName}.rels";
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
            ReplacePackageXml(archive, "xl/charts/chart1.xml", XDocument.Parse(chartXml ?? MinimalColumnChartXml));
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

    private const string MinimalPivotChartXml = """
        <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <c:pivotSource>
            <c:name>Data!PivotTable1</c:name>
            <c:fmtId val="0"/>
          </c:pivotSource>
          <c:chart>
            <c:title><c:tx><c:rich><a:p><a:r><a:t>Pivot Chart</a:t></a:r></a:p></c:rich></c:tx></c:title>
            <c:plotArea>
              <c:barChart>
                <c:barDir val="col"/>
                <c:ser>
                  <c:tx><c:strRef><c:f>Data!$B$1</c:f></c:strRef></c:tx>
                  <c:cat><c:strRef><c:f>Data!$A$2:$A$3</c:f></c:strRef></c:cat>
                  <c:val><c:numRef><c:f>Data!$B$2:$B$3</c:f></c:numRef></c:val>
                </c:ser>
              </c:barChart>
            </c:plotArea>
          </c:chart>
        </c:chartSpace>
        """;

    private const string MinimalUnsupportedRadarChartXml = """
        <c:chartSpace xmlns:c="http://schemas.openxmlformats.org/drawingml/2006/chart"
                      xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main">
          <c:chart>
            <c:plotArea>
              <c:radarChart>
                <c:radarStyle val="marker"/>
              </c:radarChart>
            </c:plotArea>
          </c:chart>
        </c:chartSpace>
        """;

    private const string MinimalPivotCacheDefinitionXml = """
        <pivotCacheDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                              refreshedBy="Freexcel Test"
                              refreshOnLoad="0"
                              recordCount="2">
          <cacheSource type="worksheet">
            <worksheetSource ref="A1:B3" sheet="Data"/>
          </cacheSource>
          <cacheFields count="2">
            <cacheField name="Category" numFmtId="0">
              <sharedItems count="2">
                <s v="A"/>
                <s v="B"/>
              </sharedItems>
            </cacheField>
            <cacheField name="Amount" numFmtId="0">
              <sharedItems containsNumber="1" count="2">
                <n v="10"/>
                <n v="20"/>
              </sharedItems>
            </cacheField>
          </cacheFields>
        </pivotCacheDefinition>
        """;

    private const string ExternalOlapPivotCacheDefinitionXml = """
        <pivotCacheDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                              refreshedBy="Freexcel Test"
                              refreshOnLoad="0"
                              saveData="1"
                              enableRefresh="1"
                              refreshedVersion="8"
                              olap="1"
                              recordCount="0">
          <cacheSource type="external" connectionId="2"/>
          <cacheFields count="2">
            <cacheField name="Category">
              <sharedItems count="0"/>
            </cacheField>
            <cacheField name="Amount">
              <sharedItems containsNumber="1" count="0"/>
            </cacheField>
          </cacheFields>
        </pivotCacheDefinition>
        """;

    private const string PivotCacheDefinitionWithSharedItemEdgeMetadataXml = """
        <pivotCacheDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                              refreshedBy="Freexcel Test"
                              refreshOnLoad="0"
                              recordCount="2">
          <cacheSource type="worksheet">
            <worksheetSource ref="A1:C3" sheet="Data"/>
          </cacheSource>
          <cacheFields count="3">
            <cacheField name="Mixed" numFmtId="0">
              <sharedItems containsString="1"
                           containsMixedTypes="1"
                           containsSemiMixedTypes="1"
                           longText="1"
                           count="2">
                <s v="Short"/>
                <s v="Long text sample"/>
              </sharedItems>
            </cacheField>
            <cacheField name="Amount" numFmtId="0">
              <sharedItems containsNumber="1"
                           containsNonDate="1"
                           containsInteger="1"
                           minValue="10"
                           maxValue="20"
                           count="2">
                <n v="10"/>
                <n v="20"/>
              </sharedItems>
            </cacheField>
            <cacheField name="Date" numFmtId="14">
              <sharedItems containsDate="1"
                           minDate="2026-01-01T00:00:00"
                           maxDate="2026-03-31T00:00:00"
                           count="2">
                <d v="2026-01-01T00:00:00"/>
                <d v="2026-03-31T00:00:00"/>
              </sharedItems>
            </cacheField>
          </cacheFields>
        </pivotCacheDefinition>
        """;

    private const string MinimalPivotCacheRecordsXml = """
        <pivotCacheRecords xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" count="2">
          <r>
            <x v="0"/>
            <n v="10"/>
          </r>
          <r>
            <x v="1"/>
            <n v="20"/>
          </r>
        </pivotCacheRecords>
        """;

    private const string MinimalStructuredTableXml = """
        <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
               id="1"
               name="Table1"
               displayName="Table1"
               ref="A1:B3"
               totalsRowShown="0">
          <autoFilter ref="A1:B3"/>
          <tableColumns count="2">
            <tableColumn id="1" name="Category"/>
            <tableColumn id="2" name="Amount"/>
          </tableColumns>
          <tableStyleInfo name="TableStyleMedium2"
                          showFirstColumn="0"
                          showLastColumn="0"
                          showRowStripes="1"
                          showColumnStripes="0"/>
        </table>
        """;

    private const string StructuredTableWithTotalsRowXml = """
        <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
               id="1"
               name="Table1"
               displayName="Table1"
               ref="A1:B4"
               totalsRowShown="1">
          <autoFilter ref="A1:B3"/>
          <tableColumns count="2">
            <tableColumn id="1" name="Category" totalsRowLabel="Total"/>
            <tableColumn id="2" name="Amount" totalsRowFunction="sum"/>
          </tableColumns>
          <tableStyleInfo name="TableStyleMedium2"
                          showFirstColumn="0"
                          showLastColumn="0"
                          showRowStripes="1"
                          showColumnStripes="0"/>
        </table>
        """;

    private const string StructuredTableWithFilterValuesXml = """
        <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
               id="1"
               name="Table1"
               displayName="Table1"
               ref="A1:B3"
               totalsRowShown="0">
          <autoFilter ref="A1:B3">
            <filterColumn colId="0">
              <filters blank="1">
                <filter val="A"/>
                <filter val="B"/>
              </filters>
            </filterColumn>
          </autoFilter>
          <tableColumns count="2">
            <tableColumn id="1" name="Category"/>
            <tableColumn id="2" name="Amount"/>
          </tableColumns>
          <tableStyleInfo name="TableStyleMedium2"
                          showFirstColumn="0"
                          showLastColumn="0"
                          showRowStripes="1"
                          showColumnStripes="0"/>
        </table>
        """;

    private const string StructuredTableWithColumnFormulasXml = """
        <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
               id="1"
               name="Table1"
               displayName="Table1"
               ref="A1:B4"
               totalsRowShown="1">
          <autoFilter ref="A1:B3"/>
          <tableColumns count="2">
            <tableColumn id="1" name="Category"/>
            <tableColumn id="2" name="Amount">
              <calculatedColumnFormula>SUM(Table1[@[Q1]:[Q2]])</calculatedColumnFormula>
              <totalsRowFormula>SUBTOTAL(109,[Amount])</totalsRowFormula>
            </tableColumn>
          </tableColumns>
          <tableStyleInfo name="TableStyleMedium2"
                          showFirstColumn="0"
                          showLastColumn="0"
                          showRowStripes="1"
                          showColumnStripes="0"/>
        </table>
        """;

    private const string StructuredTableWithCustomFilterXml = """
        <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
               id="1"
               name="Table1"
               displayName="Table1"
               ref="A1:B3"
               totalsRowShown="0">
          <autoFilter ref="A1:B3">
            <filterColumn colId="1">
              <customFilters>
                <customFilter operator="greaterThan" val="10"/>
              </customFilters>
            </filterColumn>
          </autoFilter>
          <tableColumns count="2">
            <tableColumn id="1" name="Category"/>
            <tableColumn id="2" name="Amount"/>
          </tableColumns>
          <tableStyleInfo name="TableStyleMedium2"
                          showFirstColumn="0"
                          showLastColumn="0"
                          showRowStripes="1"
                          showColumnStripes="0"/>
        </table>
        """;

    private const string StructuredTableWithCustomFilterAndExtensionXml = """
        <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
               id="1"
               name="Table1"
               displayName="Table1"
               ref="A1:B3"
               totalsRowShown="0">
          <autoFilter ref="A1:B3">
            <filterColumn colId="1">
              <customFilters>
                <customFilter operator="greaterThan" val="10"/>
              </customFilters>
              <extLst>
                <ext uri="{FREEXCEL-TABLE-FILTER-EXT}"/>
              </extLst>
            </filterColumn>
          </autoFilter>
          <tableColumns count="2">
            <tableColumn id="1" name="Category"/>
            <tableColumn id="2" name="Amount"/>
          </tableColumns>
          <tableStyleInfo name="TableStyleMedium2"
                          showFirstColumn="0"
                          showLastColumn="0"
                          showRowStripes="1"
                          showColumnStripes="0"/>
        </table>
        """;

    private const string StructuredTableWithFilterColumnAttributesXml = """
        <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
               id="1"
               name="Table1"
               displayName="Table1"
               ref="A1:B3"
               totalsRowShown="0">
          <autoFilter ref="A1:B3">
            <filterColumn colId="0" hiddenButton="1" showButton="0"/>
          </autoFilter>
          <tableColumns count="2">
            <tableColumn id="1" name="Category"/>
            <tableColumn id="2" name="Amount"/>
          </tableColumns>
          <tableStyleInfo name="TableStyleMedium2"
                          showFirstColumn="0"
                          showLastColumn="0"
                          showRowStripes="1"
                          showColumnStripes="0"/>
        </table>
        """;

    private const string StructuredTableWithAutoFilterMetadataXml = """
        <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
               id="1"
               name="Table1"
               displayName="Table1"
               ref="A1:B3"
               totalsRowShown="0">
          <autoFilter ref="A1:B3" customAttr="auto-filter-native">
            <filterColumn colId="0">
              <filters blank="1">
                <filter val="A"/>
              </filters>
            </filterColumn>
            <extLst>
              <ext uri="{FREEXCEL-TABLE-AUTOFILTER-EXT}"/>
            </extLst>
          </autoFilter>
          <tableColumns count="2">
            <tableColumn id="1" name="Category"/>
            <tableColumn id="2" name="Amount"/>
          </tableColumns>
          <tableStyleInfo name="TableStyleMedium2"
                          showFirstColumn="0"
                          showLastColumn="0"
                          showRowStripes="1"
                          showColumnStripes="0"/>
        </table>
        """;

    private const string StructuredTableWithSortStateXml = """
        <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
               id="1"
               name="Table1"
               displayName="Table1"
               ref="A1:B3"
               totalsRowShown="0">
          <autoFilter ref="A1:B3"/>
          <sortState ref="A1:B3">
            <sortCondition descending="1" ref="B2:B3"/>
          </sortState>
          <tableColumns count="2">
            <tableColumn id="1" name="Category"/>
            <tableColumn id="2" name="Amount"/>
          </tableColumns>
          <tableStyleInfo name="TableStyleMedium2"
                          showFirstColumn="0"
                          showLastColumn="0"
                          showRowStripes="1"
                          showColumnStripes="0"/>
        </table>
        """;

    private const string StructuredTableWithColumnExtensionXml = """
        <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
               id="1"
               name="Table1"
               displayName="Table1"
               ref="A1:B3"
               totalsRowShown="0">
          <autoFilter ref="A1:B3"/>
          <tableColumns count="2">
            <tableColumn id="1" name="Category"/>
            <tableColumn id="2" name="Amount">
              <extLst>
                <ext uri="{FREEXCEL-TABLE-COLUMN-EXT}"/>
              </extLst>
            </tableColumn>
          </tableColumns>
          <tableStyleInfo name="TableStyleMedium2"
                          showFirstColumn="0"
                          showLastColumn="0"
                          showRowStripes="1"
                          showColumnStripes="0"/>
        </table>
        """;

    private const string StructuredTableWithColumnAttributesXml = """
        <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
               id="1"
               name="Table1"
               displayName="Table1"
               ref="A1:B3"
               totalsRowShown="0">
          <autoFilter ref="A1:B3"/>
          <tableColumns count="2">
            <tableColumn id="1" name="Category"/>
            <tableColumn id="2" name="Amount" uniqueName="AmountNative" dataDxfId="4"/>
          </tableColumns>
          <tableStyleInfo name="TableStyleMedium2"
                          showFirstColumn="0"
                          showLastColumn="0"
                          showRowStripes="1"
                          showColumnStripes="0"/>
        </table>
        """;

    private const string StructuredTableWithStyleInfoExtensionXml = """
        <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
               id="1"
               name="Table1"
               displayName="Table1"
               ref="A1:B3"
               totalsRowShown="0">
          <autoFilter ref="A1:B3"/>
          <tableColumns count="2">
            <tableColumn id="1" name="Category"/>
            <tableColumn id="2" name="Amount"/>
          </tableColumns>
          <tableStyleInfo name="TableStyleMedium2"
                          showFirstColumn="0"
                          showLastColumn="0"
                          showRowStripes="1"
                          showColumnStripes="0"
                          pivot="0">
            <extLst>
              <ext uri="{FREEXCEL-TABLE-STYLE-EXT}"/>
            </extLst>
          </tableStyleInfo>
        </table>
        """;

    private const string StructuredTableWithRootMetadataXml = """
        <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
               id="1"
               name="Table1"
               displayName="Table1"
               ref="A1:B3"
               totalsRowShown="0"
               published="1"
               headerRowDxfId="2">
          <autoFilter ref="A1:B3"/>
          <tableColumns count="2">
            <tableColumn id="1" name="Category"/>
            <tableColumn id="2" name="Amount"/>
          </tableColumns>
          <tableStyleInfo name="TableStyleMedium2"
                          showFirstColumn="0"
                          showLastColumn="0"
                          showRowStripes="1"
                          showColumnStripes="0"/>
          <extLst>
            <ext uri="{FREEXCEL-TABLE-ROOT-EXT}"/>
          </extLst>
        </table>
        """;

    private const string StructuredTableWithTotalsRowAndFilterValuesXml = """
        <table xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
               id="1"
               name="Table1"
               displayName="Table1"
               ref="A1:B4"
               totalsRowShown="1">
          <autoFilter ref="A1:B3">
            <filterColumn colId="0">
              <filters>
                <filter val="A"/>
                <filter val="B"/>
              </filters>
            </filterColumn>
          </autoFilter>
          <tableColumns count="2">
            <tableColumn id="1" name="Category" totalsRowLabel="Total"/>
            <tableColumn id="2" name="Amount" totalsRowFunction="sum"/>
          </tableColumns>
          <tableStyleInfo name="TableStyleMedium2"
                          showFirstColumn="0"
                          showLastColumn="0"
                          showRowStripes="1"
                          showColumnStripes="0"/>
        </table>
        """;

    private const string MinimalPivotTableDefinitionXml = """
        <pivotTableDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                              name="PivotTable1"
                              cacheId="1"
                              dataOnRows="0"
                              applyNumberFormats="0"
                              applyBorderFormats="0"
                              applyFontFormats="0"
                              applyPatternFormats="0"
                              applyAlignmentFormats="0"
                              applyWidthHeightFormats="1">
          <location ref="D3:E5" firstHeaderRow="1" firstDataRow="2" firstDataCol="1"/>
          <pivotFields count="2">
            <pivotField axis="axisRow" showAll="0"/>
            <pivotField dataField="1" showAll="0"/>
          </pivotFields>
          <rowFields count="1">
            <field x="0"/>
          </rowFields>
          <dataFields count="1">
            <dataField name="Sum of Amount" fld="1" subtotal="sum" numFmtId="0"/>
          </dataFields>
        </pivotTableDefinition>
        """;

    private const string StyledMinimalPivotTableDefinitionXml = """
        <pivotTableDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                              name="PivotTable1"
                              cacheId="1"
                              dataOnRows="0"
                              applyNumberFormats="0"
                              applyBorderFormats="0"
                              applyFontFormats="0"
                              applyPatternFormats="0"
                              applyAlignmentFormats="0"
                              applyWidthHeightFormats="1">
          <location ref="D3:E5" firstHeaderRow="1" firstDataRow="2" firstDataCol="1"/>
          <pivotFields count="2">
            <pivotField axis="axisRow" showAll="0"/>
            <pivotField dataField="1" showAll="0"/>
          </pivotFields>
          <rowFields count="1">
            <field x="0"/>
          </rowFields>
          <dataFields count="1">
            <dataField name="Sum of Amount" fld="1" subtotal="sum" numFmtId="0"/>
          </dataFields>
          <pivotTableStyleInfo name="PivotStyleMedium9"
                               showRowHeaders="1"
                               showColHeaders="1"
                               showRowStripes="1"
                               showColStripes="0"/>
        </pivotTableDefinition>
        """;

    private const string PivotTableDefinitionWithHiddenItemsXml = """
        <pivotTableDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                              name="PivotTable1"
                              cacheId="1"
                              dataOnRows="0"
                              applyNumberFormats="0"
                              applyBorderFormats="0"
                              applyFontFormats="0"
                              applyPatternFormats="0"
                              applyAlignmentFormats="0"
                              applyWidthHeightFormats="1">
          <location ref="D3:E5" firstHeaderRow="1" firstDataRow="2" firstDataCol="1"/>
          <pivotFields count="2">
            <pivotField axis="axisRow" showAll="0">
              <items count="2">
                <item x="0"/>
                <item x="1" hidden="1"/>
              </items>
            </pivotField>
            <pivotField dataField="1" showAll="0"/>
          </pivotFields>
          <rowFields count="1">
            <field x="0"/>
          </rowFields>
          <dataFields count="1">
            <dataField name="Sum of Amount" fld="1" subtotal="sum" numFmtId="0"/>
          </dataFields>
        </pivotTableDefinition>
        """;

    private const string PivotTableDefinitionWithNativeFiltersXml = """
        <pivotTableDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                              name="PivotTable1"
                              cacheId="1"
                              dataOnRows="0"
                              applyNumberFormats="0"
                              applyBorderFormats="0"
                              applyFontFormats="0"
                              applyPatternFormats="0"
                              applyAlignmentFormats="0"
                              applyWidthHeightFormats="1">
          <location ref="D3:E5" firstHeaderRow="1" firstDataRow="2" firstDataCol="1"/>
          <pivotFields count="2">
            <pivotField axis="axisRow" showAll="0"/>
            <pivotField dataField="1" showAll="0"/>
          </pivotFields>
          <rowFields count="1">
            <field x="0"/>
          </rowFields>
          <dataFields count="1">
            <dataField name="Sum of Amount" fld="1" subtotal="sum" numFmtId="0"/>
          </dataFields>
          <filters count="2">
            <filter fld="0" type="captionContains" stringValue1="A"/>
            <filter fld="0" iMeasureFld="0" type="valueGreaterThan" stringValue1="15"/>
          </filters>
        </pivotTableDefinition>
        """;

    private const string PivotTableDefinitionWithNativeSortXml = """
        <pivotTableDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                              name="PivotTable1"
                              cacheId="1"
                              dataOnRows="0"
                              applyNumberFormats="0"
                              applyBorderFormats="0"
                              applyFontFormats="0"
                              applyPatternFormats="0"
                              applyAlignmentFormats="0"
                              applyWidthHeightFormats="1">
          <location ref="D3:E5" firstHeaderRow="1" firstDataRow="2" firstDataCol="1"/>
          <pivotFields count="2">
            <pivotField axis="axisRow" showAll="0" sortType="descending"/>
            <pivotField dataField="1" showAll="0"/>
          </pivotFields>
          <rowFields count="1">
            <field x="0"/>
          </rowFields>
          <dataFields count="1">
            <dataField name="Sum of Amount" fld="1" subtotal="sum" numFmtId="0"/>
          </dataFields>
        </pivotTableDefinition>
        """;

    private const string PivotTableDefinitionWithNativeGroupingXml = """
        <pivotTableDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                              name="PivotTable1"
                              cacheId="1"
                              dataOnRows="0"
                              applyNumberFormats="0"
                              applyBorderFormats="0"
                              applyFontFormats="0"
                              applyPatternFormats="0"
                              applyAlignmentFormats="0"
                              applyWidthHeightFormats="1">
          <location ref="D3:E5" firstHeaderRow="1" firstDataRow="2" firstDataCol="1"/>
          <pivotFields count="2">
            <pivotField axis="axisRow" showAll="0">
              <fieldGroup>
                <rangePr groupBy="range" startNum="0" endNum="100" groupInterval="10"/>
              </fieldGroup>
            </pivotField>
            <pivotField dataField="1" showAll="0"/>
          </pivotFields>
          <rowFields count="1">
            <field x="0"/>
          </rowFields>
          <dataFields count="1">
            <dataField name="Sum of Amount" fld="1" subtotal="sum" numFmtId="0"/>
          </dataFields>
        </pivotTableDefinition>
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
