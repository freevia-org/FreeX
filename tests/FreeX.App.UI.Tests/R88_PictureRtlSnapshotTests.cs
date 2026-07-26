using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R88-render-rtl-bidi-5-2: a Copy/Paste-as-Picture cell-range snapshot must mirror General
/// alignment and text flow direction for a right-to-left sheet the same way the live grid does
/// (<see cref="FreeX.Core.Calc.CellTextOrientationLayoutPlanner.ResolveIsEffectivelyRightToLeft"/> /
/// <c>ResolveEffectiveHorizontalAlignment</c>, GridView.Rendering.cs) instead of always rendering as
/// if the sheet were left-to-right. Before the fix, <c>DrawPictureCellText</c> never consulted
/// <c>GridView.IsSheetRightToLeft</c> at all, so a snapshot rendered exactly the same regardless of
/// the sheet's reading order.
/// </summary>
public sealed class R88_PictureRtlSnapshotTests
{
    // A single source cell (SourceRowCount = SourceColumnCount = 1) so the snapshot's one cell
    // spans the whole picture rect -- simplifies reasoning about where the text lands.
    private static byte[] RenderNumericGeneralCellSnapshot(bool isSheetRightToLeft)
    {
        var pixels = new byte[140 * 100 * 4];

        WpfTestThread.Run(() =>
        {
            var picture = new PictureModel
            {
                Anchor = new CellAddress(SheetId.New(), 1, 1),
                Kind = PictureKind.CellRangeSnapshot,
                SourceRowCount = 1,
                SourceColumnCount = 1,
                Width = 100,
                Height = 60
            };
            picture.Cells.Add(new PictureCellSnapshot(
                RowOffset: 0,
                ColumnOffset: 0,
                Text: "12345",
                Style: new CellStyle { FontSize = 16 },
                IsNumericOrDate: true));

            var grid = new GridView
            {
                Width = 140,
                Height = 100,
                ShowHeaders = false,
                ShowGridLines = false,
                IsSheetRightToLeft = isSheetRightToLeft,
                Viewport = new ViewportModel(
                    [],
                    [new RowMetric(1, 100, 0)],
                    [new ColMetric(1, 140, 0)]),
                Pictures = [picture]
            };

            grid.Measure(new Size(140, 100));
            grid.Arrange(new Rect(0, 0, 140, 100));
            grid.UpdateLayout();

            var bitmap = new RenderTargetBitmap(140, 100, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(grid);
            bitmap.CopyPixels(pixels, stride: 140 * 4, offset: 0);
        });

        return pixels;
    }

    private static (int LeftDarkPixels, int RightDarkPixels) CountDarkPixelsBySide(byte[] pixels)
    {
        int leftCount = 0, rightCount = 0;

        // The snapshot's single cell fills Rect(0, 0, 100, 60) (row/col header hidden). Scan an
        // interior band that avoids the 1px border/gridline pen so only text ink is counted, and
        // split it at the cell's horizontal midpoint (x = 50).
        for (var y = 8; y < 52; y++)
        {
            for (var x = 4; x < 96; x++)
            {
                var offset = (y * 140 + x) * 4;
                // Pbgra32 byte order is B, G, R, A. Text is drawn in near-black; the white cell
                // fill and the mid-gray (120,120,120) border/gridline pens are both much lighter.
                var isDark = pixels[offset + 2] < 100 && pixels[offset + 1] < 100 && pixels[offset + 0] < 100;
                if (!isDark)
                    continue;

                if (x < 50)
                    leftCount++;
                else
                    rightCount++;
            }
        }

        return (leftCount, rightCount);
    }

    [Fact]
    public void PictureSnapshot_NumericGeneralCell_OnRightToLeftSheet_RendersDifferentlyThanOnLeftToRightSheet()
    {
        // Pre-fix bug: DrawPictureCellText never read GridView.IsSheetRightToLeft at all, so the two
        // renders below were byte-for-byte IDENTICAL no matter what the sheet's reading order was --
        // the snapshot silently ignored the RTL flag. After the fix, the resolved reading order feeds
        // into the same hAlign/FlowDirection resolution the live grid uses, so an RTL sheet must
        // actually change how the numeric General cell renders.
        var ltrPixels = RenderNumericGeneralCellSnapshot(isSheetRightToLeft: false);
        var rtlPixels = RenderNumericGeneralCellSnapshot(isSheetRightToLeft: true);

        rtlPixels.Should().NotEqual(ltrPixels,
            "IsSheetRightToLeft must actually affect how a Copy/Paste-as-Picture cell-range snapshot renders " +
            "its text, instead of being silently ignored");
    }

    [Fact]
    public void PictureSnapshot_NumericGeneralCell_OnLeftToRightSheet_KeepsTextOnTheRight()
    {
        // Sibling/no-regression: the ordinary left-to-right sheet (by far the common case) must keep
        // the pre-existing right-flush placement for numeric General cells.
        var (leftDark, rightDark) = CountDarkPixelsBySide(RenderNumericGeneralCellSnapshot(isSheetRightToLeft: false));

        rightDark.Should().BeGreaterThan(0, "the numeric text must actually render somewhere in the snapshot");
        rightDark.Should().BeGreaterThan(leftDark,
            "General alignment on a numeric cell flushes to the RIGHT on a left-to-right sheet, unchanged from " +
            "before the fix");
    }
}
