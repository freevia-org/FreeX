using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Regression coverage for cleanup batch B7 finding P21: the WPF picture renderer must not crash
/// when a PictureModel's Cells snapshot list contains duplicate (RowOffset, ColumnOffset) pairs
/// (e.g. from a hand-edited or adversarial .fxl file). Mirrors the Avalonia shell's N52 fix so both
/// platforms tolerate the same malformed input instead of only one of them surviving the render.
/// </summary>
public sealed class FreeXCleanupB7Tests
{
    [Fact]
    public void PictureRenderer_ToleratesDuplicateCellOffsets_WithoutCrashing()
    {
        WpfTestThread.Run(() =>
        {
            var picture = new PictureModel
            {
                Anchor = new CellAddress(SheetId.New(), 1, 1),
                SourceRowCount = 1,
                SourceColumnCount = 1,
                Width = 80,
                Height = 30,
                Cells =
                {
                    // Two snapshot cells sharing the same (RowOffset, ColumnOffset). A naive
                    // .ToDictionary(...) over this list throws ArgumentException on the second
                    // entry; the fixed renderer must instead render successfully, taking the
                    // later ("last wins") entry.
                    new PictureCellSnapshot(0, 0, "first", IsNumericOrDate: false),
                    new PictureCellSnapshot(0, 0, "second", IsNumericOrDate: false)
                }
            };
            var grid = new GridView
            {
                Width = 100,
                Height = 45,
                ShowHeaders = false,
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(1, 30, 0)],
                    [new ColMetric(1, 80, 0)]),
                Pictures = [picture]
            };

            grid.Measure(new Size(100, 45));
            grid.Arrange(new Rect(0, 0, 100, 45));
            grid.UpdateLayout();

            var bitmap = new RenderTargetBitmap(
                100,
                45,
                96,
                96,
                PixelFormats.Pbgra32);

            // Pre-fix, this render throws ArgumentException("An item with the same key has
            // already been added") from inside OnRender via the plain ToDictionary call.
            var render = () => bitmap.Render(grid);
            render.Should().NotThrow();
        });
    }
}
