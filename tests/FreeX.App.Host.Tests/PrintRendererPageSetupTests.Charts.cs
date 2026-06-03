using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PrintRendererPageSetupTests
{
    [Fact]
    public void RenderWorksheet_PrintsVisibleChartBitmap()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Chart print");
            var sheet = workbook.AddSheet("Sheet1");
            PopulateChartSource(sheet);
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 20, 8));
            sheet.Charts.Add(CreatePrintedChart(
                sheet,
                "Printable sales chart",
                left: 24,
                top: 24,
                new CellColor(23, 180, 90)));
            var hiddenChart = CreatePrintedChart(
                sheet,
                "Hidden sales chart",
                left: 24,
                top: 24,
                new CellColor(210, 20, 90));
            hiddenChart.IsVisible = false;
            sheet.Charts.Add(hiddenChart);
            sheet.Charts.Add(CreatePrintedChart(
                sheet,
                "Off-page sales chart",
                left: 10000,
                top: 10000,
                new CellColor(25, 40, 230)));

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;

            CountApproximateRgbPixels(page, 23, 180, 90).Should().BeGreaterThan(100);
            CountApproximateRgbPixels(page, 210, 20, 90).Should().Be(0);
            CountApproximateRgbPixels(page, 25, 40, 230).Should().Be(0);
        });
    }

    [Fact]
    public void RenderWorksheet_AttachesSelectableTextOverlaysToPrintedCharts()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Chart print text");
            var sheet = workbook.AddSheet("Sheet1");
            PopulateChartSource(sheet);
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 20, 8));
            var chart = CreatePrintedChart(
                sheet,
                "Printable chart title",
                left: 24,
                top: 24,
                new CellColor(23, 180, 90));
            chart.XAxisTitle = "Printable month axis";
            chart.YAxisTitle = "Printable sales axis";
            sheet.Charts.Add(chart);

            var hiddenChart = CreatePrintedChart(
                sheet,
                "Hidden chart title",
                left: 24,
                top: 24,
                new CellColor(210, 20, 90));
            hiddenChart.XAxisTitle = "Hidden month axis";
            hiddenChart.YAxisTitle = "Hidden sales axis";
            hiddenChart.IsVisible = false;
            sheet.Charts.Add(hiddenChart);

            var offPageChart = CreatePrintedChart(
                sheet,
                "Off-page chart title",
                left: 10000,
                top: 10000,
                new CellColor(25, 40, 230));
            offPageChart.XAxisTitle = "Off-page month axis";
            offPageChart.YAxisTitle = "Off-page sales axis";
            sheet.Charts.Add(offPageChart);

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlays = PdfTextOverlayExtractor.Extract(page);
            var overlayTexts = overlays.Select(overlay => overlay.Text).ToList();

            overlayTexts.Should().Contain("Printable chart title");
            overlayTexts.Should().Contain("Printable month axis");
            overlayTexts.Should().Contain("Printable sales axis");
            overlayTexts.Should().NotContain("Hidden chart title");
            overlayTexts.Should().NotContain("Hidden month axis");
            overlayTexts.Should().NotContain("Hidden sales axis");
            overlayTexts.Should().NotContain("Off-page chart title");
            overlayTexts.Should().NotContain("Off-page month axis");
            overlayTexts.Should().NotContain("Off-page sales axis");
            overlays.Single(overlay => overlay.Text == "Printable sales axis")
                .RotationDegrees.Should().Be(-90);
        });
    }

    [Fact]
    public void RenderWorksheet_AttachesSelectableLegendTickDataLabelAndValueAxisOverlaysToPrintedCharts()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Chart print label text");
            var sheet = workbook.AddSheet("Sheet1");
            PopulateChartSource(
                sheet,
                startRow: 30,
                startCol: 30,
                category1: "PDF tick Jan",
                category2: "PDF tick Feb",
                category3: "PDF tick Mar",
                seriesName: "PDF Rev");
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 20, 8));
            var chart = new ChartModel
            {
                Type = ChartType.Column,
                DataRange = new GridRange(
                    new CellAddress(sheet.Id, 30, 30),
                    new CellAddress(sheet.Id, 33, 31)),
                Title = "Printable chart label title",
                XAxisTitle = "Printable chart label axis",
                Left = 24,
                Top = 24,
                Width = 380,
                Height = 210,
                ShowLegend = true,
                LegendPosition = ChartLegendPosition.Right,
                YAxisMinimum = 0,
                YAxisMaximum = 20,
                YAxisMajorUnit = 10,
                YAxisNumberFormat = ChartDataLabelNumberFormat.Currency,
                ShowDataLabels = true,
                ShowDataLabelCategoryName = true,
                ShowDataLabelValue = true
            };
            sheet.Charts.Add(chart);

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlayTexts = PdfTextOverlayExtractor.Extract(page)
                .Select(overlay => overlay.Text)
                .ToList();

            overlayTexts.Should().Contain("PDF Rev");
            overlayTexts.Should().Contain("PDF tick Jan");
            overlayTexts.Should().Contain("$10.00");
            overlayTexts.Should().Contain("PDF tick Jan, 8");
        });
    }

    [Fact]
    public void RenderWorksheet_DoesNotAttachChartTextOverlaysForClippedCharts()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Clipped chart print text");
            var sheet = workbook.AddSheet("Sheet1");
            PopulateChartSource(sheet);
            sheet.PrintArea = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 20, 8));
            var chart = CreatePrintedChart(
                sheet,
                "Clipped chart title",
                left: 650,
                top: 24,
                new CellColor(23, 180, 90));
            chart.XAxisTitle = "Clipped month axis";
            chart.YAxisTitle = "Clipped sales axis";
            sheet.Charts.Add(chart);

            var document = PrintRenderer.RenderWorksheet(workbook, sheet.Id, new ViewportService());
            var page = document.Pages[0].GetPageRoot(forceReload: false)!;
            var overlayTexts = PdfTextOverlayExtractor.Extract(page)
                .Select(overlay => overlay.Text)
                .ToList();

            overlayTexts.Should().NotContain("Clipped chart title");
            overlayTexts.Should().NotContain("Clipped month axis");
            overlayTexts.Should().NotContain("Clipped sales axis");
        });
    }

    private static void PopulateChartSource(Sheet sheet)
    {
        PopulateChartSource(sheet, 1, 1, "Jan", "Feb", "Mar", "Sales");
    }

    private static void PopulateChartSource(
        Sheet sheet,
        uint startRow,
        uint startCol,
        string category1,
        string category2,
        string category3,
        string seriesName)
    {
        sheet.SetCell(new CellAddress(sheet.Id, startRow, startCol), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, startRow, startCol + 1), new TextValue(seriesName));
        sheet.SetCell(new CellAddress(sheet.Id, startRow + 1, startCol), new TextValue(category1));
        sheet.SetCell(new CellAddress(sheet.Id, startRow + 1, startCol + 1), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, startRow + 2, startCol), new TextValue(category2));
        sheet.SetCell(new CellAddress(sheet.Id, startRow + 2, startCol + 1), new NumberValue(14));
        sheet.SetCell(new CellAddress(sheet.Id, startRow + 3, startCol), new TextValue(category3));
        sheet.SetCell(new CellAddress(sheet.Id, startRow + 3, startCol + 1), new NumberValue(11));
    }

    private static ChartModel CreatePrintedChart(
        Sheet sheet,
        string title,
        double left,
        double top,
        CellColor fillColor)
    {
        return new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 2)),
            Title = title,
            Left = left,
            Top = top,
            Width = 260,
            Height = 180,
            SeriesFormats =
            [
                new ChartSeriesFormat(
                    0,
                    FillColor: fillColor,
                    StrokeColor: fillColor)
            ]
        };
    }
}
