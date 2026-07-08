using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Round-14 bucket T7 fix verification: column/win-loss sparkline negative bars must only use the
/// negative color when "Negative Points" is enabled; otherwise they render in the series color,
/// matching Excel (R14-sparklines-2).
/// </summary>
public class FreeXR14T7Tests
{
    private static readonly CellColor SeriesColor = new(0, 0, 255);   // pure blue
    private static readonly CellColor NegativeColor = new(255, 0, 0); // pure red

    private static GridView CreateColumnSparklineGrid(Guid sparklineId, bool showNegativePoints)
    {
        var sheetId = SheetId.New();
        var grid = new GridView
        {
            Width = 100,
            Height = 60,
            ShowHeaders = false,
            ShowGridLines = false,
            Viewport = new ViewportModel(
                [],
                [new RowMetric(1, 60, 0)],
                [new ColMetric(1, 100, 0)]),
            Sparklines =
            [
                new SparklineModel
                {
                    Id = sparklineId,
                    Kind = SparklineKind.Column,
                    Location = new CellAddress(sheetId, 1, 1),
                    SeriesColor = SeriesColor,
                    NegativeColor = NegativeColor,
                    ShowNegativePoints = showNegativePoints,
                }
            ],
            SparklineValues = new Dictionary<Guid, IReadOnlyList<double>>
            {
                [sparklineId] = new List<double> { 3, -2, 5, -1 },
            },
        };

        grid.Measure(new Size(100, 60));
        grid.Arrange(new Rect(0, 0, 100, 60));
        grid.UpdateLayout();
        return grid;
    }

    private static RenderTargetBitmap RenderToBitmap(GridView grid)
    {
        var bitmap = new RenderTargetBitmap(100, 60, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        return bitmap;
    }

    private static (byte R, byte G, byte B) SamplePixel(BitmapSource bitmap, int x, int y)
    {
        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        // Pbgra32: byte order is B, G, R, A.
        return (pixels[2], pixels[1], pixels[0]);
    }

    [Fact]
    public void ColumnSparkline_NegativeBar_UsesSeriesColorUnlessNegativePointsEnabled()
    {
        WpfTestThread.Run(() =>
        {
            // With values [3, -2, 5, -1] laid out inside the sparkline cell rect, the bar for the
            // second value (-2) sits below the axis in the horizontal band x:[30.6, 45.9],
            // y:[30, 40.8]; (38, 35) is well inside that bar's interior.
            const int sampleX = 38;
            const int sampleY = 35;

            var idOff = Guid.NewGuid();
            var gridOff = CreateColumnSparklineGrid(idOff, showNegativePoints: false);
            var colorOff = SamplePixel(RenderToBitmap(gridOff), sampleX, sampleY);
            colorOff.Should().Be((SeriesColor.R, SeriesColor.G, SeriesColor.B),
                "Excel paints negative column/win-loss bars in the series color when 'Negative Points' is off");

            var idOn = Guid.NewGuid();
            var gridOn = CreateColumnSparklineGrid(idOn, showNegativePoints: true);
            var colorOn = SamplePixel(RenderToBitmap(gridOn), sampleX, sampleY);
            colorOn.Should().Be((NegativeColor.R, NegativeColor.G, NegativeColor.B),
                "enabling 'Negative Points' should still emphasize negative bars in the negative color");
        });
    }
}
