using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class ChartInsertionRenderPipelineTests
{
    [Fact]
    public void InsertedColumnChart_RendersVisibleContentFromWorkbookViewport()
    {
        StaTestRunner.Run(() =>
        {
            var workbook = new Workbook("Inserted chart");
            var sheet = workbook.AddSheet("Sheet1");
            PopulateSampleChartData(sheet);
            var range = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 5, 2));
            var command = new AddChartCommand(
                sheet.Id,
                range,
                ChartType.Column,
                "Chart",
                left: 120,
                top: 24,
                width: 400,
                height: 300);

            command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
            var chart = sheet.Charts.Should().ContainSingle().Subject;

            var viewport = new ViewportService().GetViewport(
                workbook,
                sheet.Id,
                new ViewportRequest(1, 1, 420, 720));

            viewport.ChartDataCells.Any(cell =>
                cell.Row == 5 &&
                cell.Col == 2 &&
                cell.RawValue is NumberValue { Value: 26 })
                .Should().BeTrue();

            var grid = new GridView
            {
                Width = 640,
                Height = 380,
                ShowHeaders = false,
                Viewport = viewport,
                Charts = sheet.Charts,
                SelectedObjectId = chart.Id,
                SelectedObjectKind = ObjectKind.Chart
            };
            grid.Measure(new Size(640, 380));
            grid.Arrange(new Rect(0, 0, 640, 380));
            grid.UpdateLayout();

            var warmupBitmap = new RenderTargetBitmap(
                640,
                380,
                96,
                96,
                PixelFormats.Pbgra32);
            warmupBitmap.Render(grid);

            var bitmap = new RenderTargetBitmap(
                640,
                380,
                96,
                96,
                PixelFormats.Pbgra32);
            bitmap.Render(grid);

            CountNonWhitePixels(bitmap, new Int32Rect(150, 54, 330, 230))
                .Should().BeGreaterThan(500, "inserted workbook charts should paint the generated chart image, not just the object frame");
        });
    }

    private static void PopulateSampleChartData(Sheet sheet)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Quarter"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Revenue"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Q1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Q2"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(18));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Q3"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(14));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new TextValue("Q4"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 2), new NumberValue(26));
    }

    private static int CountNonWhitePixels(BitmapSource bitmap, Int32Rect rect)
    {
        var source = bitmap.Format == PixelFormats.Bgra32
            ? bitmap
            : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = source.PixelWidth * 4;
        var pixels = new byte[stride * source.PixelHeight];
        source.CopyPixels(pixels, stride, 0);

        var count = 0;
        for (var y = rect.Y; y < rect.Y + rect.Height; y++)
        {
            for (var x = rect.X; x < rect.X + rect.Width; x++)
            {
                var offset = (y * source.PixelWidth + x) * 4;
                var blue = pixels[offset];
                var green = pixels[offset + 1];
                var red = pixels[offset + 2];
                var alpha = pixels[offset + 3];
                if (alpha > 10 && (red < 245 || green < 245 || blue < 245))
                    count++;
            }
        }

        return count;
    }
}
