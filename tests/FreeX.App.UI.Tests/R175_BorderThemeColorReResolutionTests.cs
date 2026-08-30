using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R175-render-border-theme-color-reresolution: <see cref="CellBorder"/> stores a theme-bound
/// color the same way <see cref="CellStyle"/> does for fonts/fills (see R114) -- a live
/// <see cref="CellBorder.ThemeColor"/> reference re-resolved via <see cref="CellBorder.ResolveColor"/>,
/// plus a baked <see cref="CellBorder.Color"/> fallback that is only correct at load time. Before
/// this fix, GridView.Rendering.CellStyles.cs's <c>DrawBorderEdge</c> read <c>border.Color</c>
/// directly with no theme parameter at all, so a border set via the ribbon's Theme Colors picker
/// kept showing its stale baked RGB after a Theme Colors swap -- in both the main grid pass
/// (RenderCells) and the split-pane pass (RenderSplitPaneCell), which share the exact same
/// <c>DrawBorderEdge</c> call. A second, independent bug compounded this: <c>_borderPenCache</c> is
/// keyed by the <see cref="CellBorder"/> struct alone (which does not change when the theme swaps),
/// so even after teaching <c>DrawBorderEdge</c> to resolve against the theme, a Pen built under the
/// OLD theme would keep being served from cache on every subsequent repaint of the SAME GridView
/// instance unless <c>OnWorkbookThemeChanged</c> also clears that cache.
/// </summary>
public sealed class R175_BorderThemeColorReResolutionTests
{
    private static readonly CellColor StaleBakedRed = new(200, 0, 0);
    private static readonly CellColor NewThemeBlue = new(10, 20, 230);
    private static readonly CellColor SecondThemeGreen = new(20, 200, 40);
    private static readonly CellColor PlainExplicitPurple = new(120, 10, 140);

    private static WorkbookTheme ThemeWithAccent2As(CellColor color) =>
        WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent2, color);

    // GridView is never attached to a real PresentationSource in this headless test, so its own
    // DPI-aware border-thickness/position snapping (GetBorderEffectivePixelsPerDip) always resolves
    // to a flat 1.0 pixelsPerDip regardless of this bitmap's own target DPI -- rendering at a much
    // higher DPI here does not change WHERE GridView paints the line, only how many output device
    // pixels each of its (still 1.0-DIP-scale) drawing coordinates spans. At the plain 96 DPI a
    // Thick (2.5 DIP) border line straddles exactly one output pixel row with partial coverage on
    // both sides (confirmed by direct sampling: no row is ever fully opaque), so every sampled pixel
    // is an antialiased blend and never matches the resolved color under a tight tolerance. Scaling
    // the OUTPUT bitmap resolution by Scale spreads that same line over Scale output pixels, which
    // reliably includes fully-opaque, unblended rows in the middle of the line -- mirrors
    // BorderRenderTests.cs's DoubleBorder test, which renders at 8x DPI for the identical reason.
    private const int Scale = 5;

    private static RenderTargetBitmap RenderGridToBitmap(GridView grid)
    {
        var bitmap = new RenderTargetBitmap(
            (int)grid.Width * Scale, (int)grid.Height * Scale, 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        return bitmap;
    }

    private static (byte R, byte G, byte B, byte A) SamplePixel(BitmapSource bitmap, int x, int y)
    {
        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        return (pixels[2], pixels[1], pixels[0], pixels[3]);
    }

    /// <summary>
    /// True when any pixel in the box around DIP point (<paramref name="dipX"/>, <paramref name="dipY"/>)
    /// -- widened by <paramref name="dipRadius"/> DIPs, scaled up to this bitmap's device pixels --
    /// is close to <paramref name="expected"/>.
    /// </summary>
    private static bool AnyPixelNear(BitmapSource bitmap, int dipX, int dipY, int dipRadius, CellColor expected, int tolerance = 20)
    {
        var x = dipX * Scale;
        var y = dipY * Scale;
        var radius = dipRadius * Scale;
        var minX = Math.Max(0, x - radius);
        var maxXExclusive = Math.Min(bitmap.PixelWidth, x + radius + 1);
        var minY = Math.Max(0, y - radius);
        var maxYExclusive = Math.Min(bitmap.PixelHeight, y + radius + 1);

        for (var py = minY; py < maxYExclusive; py++)
        for (var px = minX; px < maxXExclusive; px++)
        {
            var pixel = SamplePixel(bitmap, px, py);
            if (IsCloseTo(expected, pixel, tolerance))
                return true;
        }

        return false;
    }

    private static bool IsCloseTo(CellColor expected, (byte R, byte G, byte B, byte A) actual, int tolerance) =>
        actual.A > 200 &&
        Math.Abs(actual.R - expected.R) <= tolerance &&
        Math.Abs(actual.G - expected.G) <= tolerance &&
        Math.Abs(actual.B - expected.B) <= tolerance;

    // ------------------------------------------------------------------
    // T1: main-pass border re-resolution (GridView.Rendering.cs RenderCells' Pass 2, via the shared
    // GridView.Rendering.CellStyles.cs DrawBorderEdge).
    // ------------------------------------------------------------------
    [Fact]
    public void MainGridPass_BorderThemeColor_ReResolvesAgainstCurrentTheme_NotStaleBakedColor()
    {
        WpfTestThread.Run(() =>
        {
            var cells = new[]
            {
                new DisplayCell(1, 1, null, "", null, default, null, new CellStyle
                {
                    BorderBottom = new CellBorder(
                        BorderStyle.Thick,
                        StaleBakedRed,
                        new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2))
                })
            };

            var grid = new GridView
            {
                Width = 100,
                Height = 40,
                ShowHeaders = false,
                ShowGridLines = false,
                WorkbookTheme = ThemeWithAccent2As(NewThemeBlue),
                Viewport = new ViewportModel(cells, [new RowMetric(1, 30, 0)], [new ColMetric(1, 90, 0)]),
            };
            grid.Measure(new Size(grid.Width, grid.Height));
            grid.Arrange(new Rect(0, 0, grid.Width, grid.Height));
            grid.UpdateLayout();

            var bitmap = RenderGridToBitmap(grid);

            AnyPixelNear(bitmap, 45, 29, 3, NewThemeBlue).Should().BeTrue(
                "the cell's BorderBottom.ThemeColor must be re-resolved against the CURRENT WorkbookTheme (Accent2 -> NewThemeBlue), not the stale baked Color");
            AnyPixelNear(bitmap, 45, 29, 3, StaleBakedRed).Should().BeFalse(
                "the stale baked border Color must never win over a live ThemeColor reference");
        });
    }

    // ------------------------------------------------------------------
    // T2 (sibling path): split-pane border re-resolution (GridView.Rendering.cs
    // RenderSplitPaneCell), which shares the same DrawBorderEdge call as the main pass.
    // ------------------------------------------------------------------
    [Fact]
    public void SplitPanePass_BorderThemeColor_ReResolvesAgainstCurrentTheme_NotStaleBakedColor()
    {
        WpfTestThread.Run(() =>
        {
            var topRows = new RowMetric[9];
            for (var i = 0; i < 9; i++)
                topRows[i] = new RowMetric((uint)(i + 1), 20, i * 20);
            var leftColumns = new[] { new ColMetric(1, 64, 0), new ColMetric(2, 64, 64) };

            var mainRows = new RowMetric[20];
            for (var i = 0; i < 20; i++)
                mainRows[i] = new RowMetric((uint)(10 + i), 20, i * 20);
            var mainCols = new ColMetric[6];
            for (var i = 0; i < 6; i++)
                mainCols[i] = new ColMetric((uint)(3 + i), 64, i * 64);

            var splitCells = new[]
            {
                new DisplayCell(3, 1, null, "", null, default, null, new CellStyle
                {
                    BorderBottom = new CellBorder(
                        BorderStyle.Thick,
                        StaleBakedRed,
                        new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2))
                })
            };

            var grid = new GridView
            {
                Width = 320,
                Height = 320,
                ShowHeaders = true,
                ShowGridLines = false,
                WorkbookTheme = ThemeWithAccent2As(NewThemeBlue),
                Viewport = new ViewportModel(
                    [],
                    mainRows,
                    mainCols,
                    SplitPanes: new SplitPaneState(
                        10, 3, topRows, leftColumns, splitCells,
                        [new ColMetric(52, 64, 0), new ColMetric(53, 64, 64)],
                        [new RowMetric(500, 20, 0), new RowMetric(501, 20, 20)])),
            };
            grid.Measure(new Size(grid.Width, grid.Height));
            grid.Arrange(new Rect(0, 0, grid.Width, grid.Height));
            grid.UpdateLayout();

            var bitmap = RenderGridToBitmap(grid);
            var x = (int)GridView.RowHeaderWidth + 10;
            var y = (int)GridView.ColHeaderHeight + 40 + 20 - 1;

            AnyPixelNear(bitmap, x, y, 3, NewThemeBlue).Should().BeTrue(
                "the split-pane path must ALSO re-resolve CellBorder.ThemeColor against the current theme, not just the main grid pass");
            AnyPixelNear(bitmap, x, y, 3, StaleBakedRed).Should().BeFalse(
                "the split-pane path must not fall back to the stale baked border Color either");
        });
    }

    // ------------------------------------------------------------------
    // T3: the border-pen cache (_borderPenCache, keyed by the CellBorder struct alone) must be
    // invalidated when the theme changes on the SAME GridView instance -- otherwise a Pen built
    // under the OLD theme keeps being served from cache forever, since the CellBorder key itself
    // does not encode which theme it was last resolved against.
    // ------------------------------------------------------------------
    [Fact]
    public void MainGridPass_BorderThemeColor_FollowsSecondThemeSwap_OnSameGridViewInstance()
    {
        WpfTestThread.Run(() =>
        {
            var cells = new[]
            {
                new DisplayCell(1, 1, null, "", null, default, null, new CellStyle
                {
                    BorderBottom = new CellBorder(
                        BorderStyle.Thick,
                        StaleBakedRed,
                        new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2))
                })
            };

            var grid = new GridView
            {
                Width = 100,
                Height = 40,
                ShowHeaders = false,
                ShowGridLines = false,
                WorkbookTheme = ThemeWithAccent2As(NewThemeBlue),
                Viewport = new ViewportModel(cells, [new RowMetric(1, 30, 0)], [new ColMetric(1, 90, 0)]),
            };
            grid.Measure(new Size(grid.Width, grid.Height));
            grid.Arrange(new Rect(0, 0, grid.Width, grid.Height));
            grid.UpdateLayout();

            // First paint under theme #1 -- populates _borderPenCache keyed by this CellBorder value.
            var firstBitmap = RenderGridToBitmap(grid);
            AnyPixelNear(firstBitmap, 45, 29, 3, NewThemeBlue).Should().BeTrue(
                "sanity check: the first paint must show the first theme's resolved color");

            // Swap the theme on the SAME instance (no new GridView, no new CellBorder struct value)
            // and repaint. If _borderPenCache were not cleared by OnWorkbookThemeChanged, the stale
            // Pen built for theme #1 would still be returned for the identical CellBorder key.
            grid.WorkbookTheme = ThemeWithAccent2As(SecondThemeGreen);
            grid.InvalidateVisual();
            grid.UpdateLayout();
            var secondBitmap = RenderGridToBitmap(grid);

            AnyPixelNear(secondBitmap, 45, 29, 3, SecondThemeGreen).Should().BeTrue(
                "after swapping the theme on the SAME GridView instance, the border must repaint with the NEW theme's resolved color -- _borderPenCache must not serve a stale Pen for the same CellBorder key");
            AnyPixelNear(secondBitmap, 45, 29, 3, NewThemeBlue).Should().BeFalse(
                "the border must not keep showing the FIRST theme's color after the swap");
        });
    }

    // ------------------------------------------------------------------
    // No-regression sibling: a border with a PLAIN explicit color (no theme reference at all) must
    // keep rendering exactly that color regardless of the active theme.
    // ------------------------------------------------------------------
    [Fact]
    public void MainGridPass_PlainExplicitBorderColor_WithNoThemeReference_StillRendersExactColor_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var cells = new[]
            {
                new DisplayCell(1, 1, null, "", null, default, null, new CellStyle
                {
                    BorderBottom = new CellBorder(BorderStyle.Thick, PlainExplicitPurple)
                    // ThemeColor intentionally left null: a plain, non-themed border color.
                })
            };

            var grid = new GridView
            {
                Width = 100,
                Height = 40,
                ShowHeaders = false,
                ShowGridLines = false,
                // Deliberately swap the active theme to something with wildly different accents --
                // must have ZERO effect on a border with no ThemeColor reference.
                WorkbookTheme = ThemeWithAccent2As(NewThemeBlue),
                Viewport = new ViewportModel(cells, [new RowMetric(1, 30, 0)], [new ColMetric(1, 90, 0)]),
            };
            grid.Measure(new Size(grid.Width, grid.Height));
            grid.Arrange(new Rect(0, 0, grid.Width, grid.Height));
            grid.UpdateLayout();

            var bitmap = RenderGridToBitmap(grid);

            AnyPixelNear(bitmap, 45, 29, 3, PlainExplicitPurple).Should().BeTrue(
                "a border with no ThemeColor reference must keep rendering its plain explicit Color unchanged, regardless of the active theme");
        });
    }
}
