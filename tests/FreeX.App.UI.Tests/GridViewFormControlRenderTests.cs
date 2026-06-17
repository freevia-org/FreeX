using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed class GridViewFormControlRenderTests
{
    private static GridView CreateGrid(params FormControlModel[] controls)
    {
        var grid = new GridView
        {
            Width = 240,
            Height = 120,
            ShowHeaders = false,
            ShowGridLines = false,
            Viewport = new ViewportModel(
                [],
                [
                    new RowMetric(1, 24, 0),
                    new RowMetric(2, 24, 24),
                    new RowMetric(3, 24, 48),
                    new RowMetric(4, 24, 72),
                    new RowMetric(5, 24, 96)
                ],
                [
                    new ColMetric(1, 80, 0),
                    new ColMetric(2, 80, 80),
                    new ColMetric(3, 80, 160),
                    new ColMetric(4, 80, 240)
                ]),
            FormControls = controls
        };

        grid.Measure(new Size(240, 120));
        grid.Arrange(new Rect(0, 0, 240, 120));
        grid.UpdateLayout();
        return grid;
    }

    private static GridRange Anchor(uint startRow, uint startCol, uint endRow, uint endCol)
    {
        var sheet = SheetId.New();
        return new GridRange(
            new CellAddress(sheet, startRow, startCol),
            new CellAddress(sheet, endRow, endCol));
    }

    private static int CountNonWhitePixels(BitmapSource bitmap, Int32Rect rect)
    {
        var stride = rect.Width * 4;
        var pixels = new byte[stride * rect.Height];
        bitmap.CopyPixels(rect, pixels, stride, 0);

        var count = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var blue = pixels[i];
            var green = pixels[i + 1];
            var red = pixels[i + 2];
            var alpha = pixels[i + 3];
            if (alpha > 10 && (red < 245 || green < 245 || blue < 245))
                count++;
        }

        return count;
    }

    private static RenderTargetBitmap RenderToBitmap(GridView grid)
    {
        var bitmap = new RenderTargetBitmap(240, 120, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        return bitmap;
    }

    [Fact]
    public void CheckBoxControl_RendersNonEmptyChrome()
    {
        WpfTestThread.Run(() =>
        {
            var grid = CreateGrid(new FormControlModel
            {
                Kind = FormControlKind.CheckBox,
                Name = "Include weekends",
                IsChecked = true,
                Anchor = Anchor(1, 1, 1, 3)
            });

            var bitmap = RenderToBitmap(grid);

            CountNonWhitePixels(bitmap, new Int32Rect(0, 0, 240, 24))
                .Should().BeGreaterThan(50, "a checkbox control should draw a box, check glyph, and caption");
        });
    }

    [Fact]
    public void OptionButtonControl_RendersNonEmptyChrome()
    {
        WpfTestThread.Run(() =>
        {
            var grid = CreateGrid(new FormControlModel
            {
                Kind = FormControlKind.OptionButton,
                Name = "Monthly",
                IsChecked = true,
                Anchor = Anchor(2, 1, 2, 3)
            });

            var bitmap = RenderToBitmap(grid);

            CountNonWhitePixels(bitmap, new Int32Rect(0, 24, 240, 24))
                .Should().BeGreaterThan(50, "an option button should draw a radio circle, dot, and caption");
        });
    }

    [Fact]
    public void SpinnerControl_RendersNonEmptyChrome()
    {
        WpfTestThread.Run(() =>
        {
            var grid = CreateGrid(new FormControlModel
            {
                Kind = FormControlKind.Spinner,
                Anchor = Anchor(3, 1, 4, 1)
            });

            var bitmap = RenderToBitmap(grid);

            CountNonWhitePixels(bitmap, new Int32Rect(0, 48, 80, 48))
                .Should().BeGreaterThan(30, "a spinner should draw up/down arrow chrome");
        });
    }

    [Fact]
    public void NonRenderableControl_DrawsNothing()
    {
        WpfTestThread.Run(() =>
        {
            var grid = CreateGrid(new FormControlModel
            {
                Kind = FormControlKind.Button,
                Name = "Click me",
                Anchor = Anchor(1, 1, 1, 3)
            });

            var bitmap = RenderToBitmap(grid);

            CountNonWhitePixels(bitmap, new Int32Rect(0, 0, 240, 24))
                .Should().Be(0, "buttons are out of scope for static chrome and should not be drawn");
        });
    }
}
