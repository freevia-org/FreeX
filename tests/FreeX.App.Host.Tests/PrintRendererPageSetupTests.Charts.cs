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
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Month"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Jan"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Feb"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(14));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Mar"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(11));
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
