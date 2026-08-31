using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed class GridViewObjectTransformPreviewTests
{
    [Fact]
    public void ActivePictureMovePreview_DrawsPictureAtLiveRectEvenWhenLayerCacheIsWarm()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var anchor = new CellAddress(sheetId, 1, 1);
            var picture = CreateRedSnapshotPicture(anchor, width: 32, height: 20);
            var grid = CreateGrid(picture, width: 160, height: 90);

            RenderGrid(grid, 160, 90);
            RenderGrid(grid, 160, 90);

            GridViewTestHelpers.SetObjectTransformPreview(
                grid,
                picture.Id,
                ObjectKind.Picture,
                ObjectDragKind.Move,
                new Rect(0, 0, 32, 20),
                new Rect(80, 24, 32, 20));

            var bitmap = RenderGrid(grid, 160, 90);

            CountRedPixels(bitmap, new Int32Rect(86, 29, 12, 8))
                .Should().BeGreaterThan(60, "the selected picture should render at the live move rect");
            CountRedPixels(bitmap, new Int32Rect(8, 6, 12, 8))
                .Should().Be(0, "the warmed cached picture layer should not be reused during live transform preview");
            picture.Anchor.Should().Be(anchor);
            picture.Width.Should().Be(32);
            picture.Height.Should().Be(20);
            picture.RotationDegrees.Should().Be(0);
        });
    }

    [Fact]
    public void ActiveChartMovePreview_DrawsChartAtLiveRectEvenWhenLayerCacheIsWarm()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var chart = CreateRedChart(sheetId, left: 4, top: 4, width: 48, height: 32);
            var grid = CreateGrid(chart, width: 160, height: 90);

            RenderGrid(grid, 160, 90);
            RenderGrid(grid, 160, 90);

            GridViewTestHelpers.SetObjectTransformPreview(
                grid,
                chart.Id,
                ObjectKind.Chart,
                ObjectDragKind.Move,
                new Rect(4, 4, 48, 32),
                new Rect(80, 24, 48, 32));

            var bitmap = RenderGrid(grid, 160, 90);

            CountRedPixels(bitmap, new Int32Rect(88, 32, 16, 12))
                .Should().BeGreaterThan(120, "the selected chart should render at the live move rect");
            CountRedPixels(bitmap, new Int32Rect(12, 12, 16, 12))
                .Should().Be(0, "the warmed cached chart layer should not be reused during live transform preview");
            chart.Left.Should().Be(4);
            chart.Top.Should().Be(4);
            chart.Width.Should().Be(48);
            chart.Height.Should().Be(32);
        });
    }

    [Fact]
    public void TwoCellAnchoredChart_UsesVisibleAnchorEdgesInsteadOfImportedAnchorSpaceExtent()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var chart = CreateRedChart(sheetId, left: 0, top: 0, width: 500, height: 500);
            chart.Anchor = new CellAddress(sheetId, 1, 1);
            chart.AnchorOffsetX = 5;
            chart.AnchorOffsetY = 6;
            chart.AnchorEnd = new CellAddress(sheetId, 3, 3);
            chart.AnchorEndOffsetX = 35;
            chart.AnchorEndOffsetY = 24;

            var grid = new GridView
            {
                Width = 320,
                Height = 260,
                ShowHeaders = false,
                Viewport = new ViewportModel(
                    [],
                    [
                        new RowMetric(1, 100, 0),
                        new RowMetric(2, 100, 100),
                        new RowMetric(3, 100, 200)
                    ],
                    [
                        new ColMetric(1, 100, 0),
                        new ColMetric(2, 100, 100),
                        new ColMetric(3, 100, 200)
                    ]),
                Charts = [chart],
                SelectedObjectId = chart.Id,
                SelectedObjectKind = ObjectKind.Chart,
                SheetColumnWidths = new Dictionary<uint, double>
                {
                    [1] = 95d / 7d,
                    [2] = 95d / 7d,
                    [3] = 95d / 7d
                },
                SheetRowHeights = new Dictionary<uint, double>
                {
                    [1] = 100,
                    [2] = 100,
                    [3] = 100
                }
            };

            GridViewTestHelpers.GetSelectedObjectRect(grid)
                .Should().Be(new Rect(5, 6, 230, 218),
                    "the two visible source anchor markers define the chart's exact grid-space bounds");
        });
    }

    [Fact]
    public void TwoCellAnchoredChart_WithStartMarkerScrolledAway_UsesGridSpaceExtentFromEndMarker()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var chart = CreateRedChart(sheetId, left: 0, top: 0, width: 500, height: 500);
            chart.Anchor = new CellAddress(sheetId, 1, 1);
            chart.AnchorOffsetX = 5;
            chart.AnchorOffsetY = 6;
            chart.AnchorEnd = new CellAddress(sheetId, 3, 3);
            chart.AnchorEndOffsetX = 35;
            chart.AnchorEndOffsetY = 24;

            var grid = new GridView
            {
                Width = 320,
                Height = 260,
                ShowHeaders = false,
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(3, 100, 0)],
                    [new ColMetric(3, 100, 0)]),
                Charts = [chart],
                SelectedObjectId = chart.Id,
                SelectedObjectKind = ObjectKind.Chart,
                SheetColumnWidths = new Dictionary<uint, double>
                {
                    [1] = 95d / 7d,
                    [2] = 95d / 7d,
                    [3] = 95d / 7d
                },
                SheetRowHeights = new Dictionary<uint, double>
                {
                    [1] = 100,
                    [2] = 100,
                    [3] = 100
                }
            };

            GridViewTestHelpers.GetSelectedObjectRect(grid)
                .Should().Be(new Rect(-195, -194, 230, 218),
                    "the visible end marker must recover the same chart bounds after the start marker scrolls offscreen");
        });
    }

    [Fact]
    public void ChartMoveCommit_RaisesFinalBoundsWithoutMutatingModel()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var chart = CreateRedChart(sheetId, left: 4, top: 4, width: 48, height: 32);
            var grid = CreateGrid(chart, width: 160, height: 90);
            (Guid Id, double Left, double Top, double Width, double Height)? committed = null;
            grid.ChartBoundsChanged += (id, left, top, width, height) =>
                committed = (id, left, top, width, height);

            GridViewTestHelpers.CommitChartObjectBoundsChange(
                grid,
                chart.Id,
                new Rect(4, 4, 48, 32),
                new Rect(80, 24, 48, 32));

            committed.Should().Be((chart.Id, 80d, 24d, 48d, 32d));
            chart.Left.Should().Be(4);
            chart.Top.Should().Be(4);
            chart.Width.Should().Be(48);
            chart.Height.Should().Be(32);
        });
    }

    [Fact]
    public void ChartResizeCommit_ClampsToWpfMinimumBounds()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var chart = CreateRedChart(sheetId, left: 4, top: 4, width: 48, height: 32);
            var grid = CreateGrid(chart, width: 160, height: 90);
            (Guid Id, double Left, double Top, double Width, double Height)? committed = null;
            grid.ChartBoundsChanged += (id, left, top, width, height) =>
                committed = (id, left, top, width, height);

            GridViewTestHelpers.CommitChartObjectBoundsChange(
                grid,
                chart.Id,
                new Rect(4, 4, 48, 32),
                new Rect(4, 4, 3, 2));

            committed.Should().Be((chart.Id, 4d, 4d, 24d, 18d));
        });
    }

    [Fact]
    public void ActivePictureResizePreview_DrawsPictureAtLiveSizeWithoutMutatingModel()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var anchor = new CellAddress(sheetId, 1, 1);
            var picture = CreateRedSnapshotPicture(anchor, width: 32, height: 20);
            var grid = CreateGrid(picture, width: 140, height: 80);

            GridViewTestHelpers.SetObjectTransformPreview(
                grid,
                picture.Id,
                ObjectKind.Picture,
                ObjectDragKind.ResizeSE,
                new Rect(0, 0, 32, 20),
                new Rect(0, 0, 68, 34));

            var bitmap = RenderGrid(grid, 140, 80);

            CountRedPixels(bitmap, new Int32Rect(50, 22, 12, 8))
                .Should().BeGreaterThan(60, "the selected picture should render into the live resize area");
            picture.Width.Should().Be(32);
            picture.Height.Should().Be(20);
        });
    }

    [Fact]
    public void ActivePictureFlipPreview_DrawsMirroredContentWithoutMutatingModel()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var anchor = new CellAddress(sheetId, 1, 1);
            var picture = CreateTwoColorSnapshotPicture(anchor, width: 64, height: 20);
            var grid = CreateGrid(picture, width: 100, height: 50);
            var rect = new Rect(0, 0, 64, 20);

            GridViewTestHelpers.SetObjectTransformPreview(
                grid,
                picture.Id,
                ObjectKind.Picture,
                ObjectDragKind.ResizeE,
                rect,
                rect,
                currentFlipHorizontal: true);

            var bitmap = RenderGrid(grid, 100, 50);

            CountRedPixels(bitmap, new Int32Rect(42, 6, 14, 8))
                .Should().BeGreaterThan(70, "a horizontal flip should mirror the left snapshot cell into the right half");
            CountRedPixels(bitmap, new Int32Rect(8, 6, 14, 8))
                .Should().Be(0, "the left half should no longer contain the red snapshot cell while the preview is flipped");
            picture.FlipHorizontal.Should().BeFalse();
            picture.FlipVertical.Should().BeFalse();
        });
    }

    [Fact]
    public void ActivePictureRotatePreview_DrawsPictureAtLiveRotationWithoutMutatingModel()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var anchor = new CellAddress(sheetId, 1, 1);
            var picture = CreateRedSnapshotPicture(anchor, width: 60, height: 20);
            var grid = CreateGrid(picture, width: 150, height: 110);
            var rect = new Rect(60, 40, 60, 20);

            GridViewTestHelpers.SetObjectTransformPreview(
                grid,
                picture.Id,
                ObjectKind.Picture,
                ObjectDragKind.Rotate,
                rect,
                rect,
                rotationDegrees: 90);

            var bitmap = RenderGrid(grid, 150, 110);

            CountRedPixels(bitmap, new Int32Rect(85, 24, 10, 12))
                .Should().BeGreaterThan(60, "the selected picture should render at the live rotation angle");
            picture.RotationDegrees.Should().Be(0);
        });
    }

    private static GridView CreateGrid(PictureModel picture, double width, double height) =>
        new()
        {
            Width = width,
            Height = height,
            ShowHeaders = false,
            Viewport = new ViewportModel(
                [],
                [new RowMetric(1, height, 0)],
                [new ColMetric(1, width, 0)]),
            Pictures = [picture],
            SelectedObjectId = picture.Id,
            SelectedObjectKind = ObjectKind.Picture
        };

    private static GridView CreateGrid(ChartModel chart, double width, double height) =>
        new()
        {
            Width = width,
            Height = height,
            ShowHeaders = false,
            Viewport = new ViewportModel(
                [],
                [new RowMetric(1, height, 0)],
                [new ColMetric(1, width, 0)]),
            Charts = [chart],
            SelectedObjectId = chart.Id,
            SelectedObjectKind = ObjectKind.Chart
        };

    private static PictureModel CreateRedSnapshotPicture(CellAddress anchor, double width, double height) =>
        new()
        {
            Anchor = anchor,
            SourceRowCount = 1,
            SourceColumnCount = 1,
            Width = width,
            Height = height,
            Cells =
            {
                new PictureCellSnapshot(
                    0,
                    0,
                    "",
                    new CellStyle { FillColor = new CellColor(220, 40, 40) })
            }
        };

    private static PictureModel CreateTwoColorSnapshotPicture(CellAddress anchor, double width, double height) =>
        new()
        {
            Anchor = anchor,
            SourceRowCount = 1,
            SourceColumnCount = 2,
            Width = width,
            Height = height,
            Cells =
            {
                new PictureCellSnapshot(
                    0,
                    0,
                    "",
                    new CellStyle { FillColor = new CellColor(220, 40, 40) }),
                new PictureCellSnapshot(
                    0,
                    1,
                    "",
                    new CellStyle { FillColor = new CellColor(40, 80, 220) })
            }
        };

    private static ChartModel CreateRedChart(SheetId sheetId, double left, double top, double width, double height) =>
        new()
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            Left = left,
            Top = top,
            Width = width,
            Height = height,
            ChartAreaFillColor = new CellColor(220, 40, 40),
            ChartAreaBorderColor = new CellColor(220, 40, 40)
        };

    private static RenderTargetBitmap RenderGrid(GridView grid, int width, int height)
    {
        grid.InvalidateVisual();
        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        return bitmap;
    }

    private static int CountRedPixels(BitmapSource bitmap, Int32Rect rect)
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
            if (alpha > 20 && red > 150 && green < 110 && blue < 110)
                count++;
        }

        return count;
    }
}
