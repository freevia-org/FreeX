using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R114-render-theme-color-reresolution: <see cref="CellStyle"/> stores a theme-bound color two
/// ways -- a live reference (FontThemeColor/FillThemeColor/FillPatternThemeColor, a
/// WorkbookThemeColorSlot+tint) meant to be re-resolved against the CURRENT WorkbookTheme at paint
/// time via CellStyle.ResolveFontColor/ResolveFillColor/ResolveFillPatternColor, plus a concrete
/// baked FontColor/FillColor/FillPatternColor field that is only correct at the moment the style
/// was created/loaded (e.g. XlsxClosedXmlCellMapper.MapStyle bakes FontColor using the theme in
/// effect at load time). Before this fix, GridView's WPF renderer (GridView.Rendering.cs,
/// GridView.Rendering.CellStyles.cs, GridView.TextLayoutCache.cs, GridView.DrawingObjects.Pictures.cs)
/// read the baked fields directly with no re-resolution, so a Theme Colors swap left every themed
/// cell showing its stale baked RGB forever -- in the main grid pass, the split-pane pass, the
/// "default black text" fast-path cache-eligibility check, and (for fill) even on FIRST paint when
/// only the theme reference was ever set with no baked fallback color at all (StyleDiff.Apply sets
/// FillThemeColor without ever baking FillColor -- see CellStyle.cs's diff-apply logic).
/// </summary>
public sealed class R114_ThemeColorReResolutionTests
{
    private static readonly CellColor StaleBakedRed = new(200, 0, 0);
    private static readonly CellColor NewThemeBlue = new(10, 20, 230);
    private static readonly CellColor StaleBakedGreen = new(0, 180, 0);
    private static readonly CellColor NewThemeOrange = new(230, 120, 10);
    private static readonly CellColor PlainExplicitPurple = new(120, 10, 140);

    private static WorkbookTheme ThemeWithAccent2As(CellColor color) =>
        WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent2, color);

    private static WorkbookTheme ThemeWithAccent1As(CellColor color) =>
        WorkbookTheme.Office.WithColor(WorkbookThemeColorSlot.Accent1, color);

    private static RenderTargetBitmap RenderGridToBitmap(GridView grid)
    {
        var bitmap = new RenderTargetBitmap((int)grid.Width, (int)grid.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        return bitmap;
    }

    private static (byte R, byte G, byte B, byte A) SamplePixel(BitmapSource bitmap, int x, int y)
    {
        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        return (pixels[2], pixels[1], pixels[0], pixels[3]);
    }

    /// <summary>True when any pixel in the small box around (x, y) is close to <paramref name="expected"/>.</summary>
    private static bool AnyPixelNear(BitmapSource bitmap, int x, int y, int radius, CellColor expected, int tolerance = 20)
    {
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

    private static bool IsCloseTo(CellColor expected, (byte R, byte G, byte B, byte A) actual, int tolerance = 12) =>
        actual.A > 200 &&
        Math.Abs(actual.R - expected.R) <= tolerance &&
        Math.Abs(actual.G - expected.G) <= tolerance &&
        Math.Abs(actual.B - expected.B) <= tolerance;

    // ------------------------------------------------------------------
    // T1: main-pass fill re-resolution (GridView.Rendering.cs DrawCellSurface, line ~1160).
    // ------------------------------------------------------------------
    [Fact]
    public void MainGridPass_FillThemeColor_ReResolvesAgainstCurrentTheme_NotStaleBakedFillColor()
    {
        WpfTestThread.Run(() =>
        {
            var cells = new[]
            {
                new DisplayCell(1, 1, null, "", null, default, null, new CellStyle
                {
                    FillColor = StaleBakedRed,
                    FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2)
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
            var pixel = SamplePixel(bitmap, 45, 15);

            IsCloseTo(NewThemeBlue, pixel).Should().BeTrue(
                $"the cell's FillThemeColor must be re-resolved against the CURRENT WorkbookTheme " +
                $"(Accent2 -> NewThemeBlue), not the stale baked FillColor from load time; sampled rgb=({pixel.R},{pixel.G},{pixel.B})");
            IsCloseTo(StaleBakedRed, pixel).Should().BeFalse(
                "the stale baked FillColor must never win over a live FillThemeColor reference");
        });
    }

    // ------------------------------------------------------------------
    // T2 (sibling path): split-pane fill re-resolution (GridView.Rendering.cs RenderSplitPaneCell,
    // line ~265).
    // ------------------------------------------------------------------
    [Fact]
    public void SplitPanePass_FillThemeColor_ReResolvesAgainstCurrentTheme_NotStaleBakedFillColor()
    {
        WpfTestThread.Run(() =>
        {
            // Mirrors GridViewSplitPaneMainViewportRenderTests' known-good TopLeft-quadrant (fully
            // pinned both axes) geometry exactly, swapping only the marker cell's style: a
            // split-pane cell at (3, 1) -- inside both TopRows (rows 1-9) and LeftColumns
            // (cols 1-2) -- renders at the plain pinned-band origin
            // (RowHeaderWidth, ColHeaderHeight + 40) regardless of the divider position.
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
                    FillColor = StaleBakedRed,
                    FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2)
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
            var y = (int)GridView.ColHeaderHeight + 40 + 10;
            var pixel = SamplePixel(bitmap, x, y);

            IsCloseTo(NewThemeBlue, pixel).Should().BeTrue(
                $"the split-pane path must ALSO re-resolve FillThemeColor against the current theme, not just the main grid pass; sampled rgb=({pixel.R},{pixel.G},{pixel.B})");
            IsCloseTo(StaleBakedRed, pixel).Should().BeFalse(
                "the split-pane path must not fall back to the stale baked FillColor either");
        });
    }

    // ------------------------------------------------------------------
    // T3: main-pass font re-resolution, slow (non-cached) text path (GridView.Rendering.cs
    // RenderCells' non-default text branch, line ~946).
    // ------------------------------------------------------------------
    [Fact]
    public void MainGridPass_FontThemeColor_ReResolvesAgainstCurrentTheme_NotStaleBakedFontColor()
    {
        WpfTestThread.Run(() =>
        {
            // Bold forces the "slow" (non-default-cached) text layout branch regardless of color,
            // isolating this test to the RenderCells non-default branch fix (line ~946).
            var cells = new[]
            {
                new DisplayCell(1, 1, new TextValue("MMMMMM"), "MMMMMM", null, default, null, new CellStyle
                {
                    FontColor = StaleBakedGreen,
                    FontThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1),
                    Bold = true,
                    FontSize = 22,
                })
            };

            var grid = new GridView
            {
                Width = 160,
                Height = 60,
                ShowHeaders = false,
                ShowGridLines = false,
                WorkbookTheme = ThemeWithAccent1As(NewThemeOrange),
                Viewport = new ViewportModel(cells, [new RowMetric(1, 50, 0)], [new ColMetric(1, 150, 0)]),
            };
            grid.Measure(new Size(grid.Width, grid.Height));
            grid.Arrange(new Rect(0, 0, grid.Width, grid.Height));
            grid.UpdateLayout();

            var bitmap = RenderGridToBitmap(grid);

            AnyPixelNear(bitmap, 40, 30, 25, NewThemeOrange).Should().BeTrue(
                "the cell's FontThemeColor must be re-resolved against the CURRENT WorkbookTheme (Accent1 -> NewThemeOrange), not the stale baked FontColor");
            AnyPixelNear(bitmap, 40, 30, 25, StaleBakedGreen).Should().BeFalse(
                "the stale baked FontColor must never win over a live FontThemeColor reference");
        });
    }

    // ------------------------------------------------------------------
    // T4: the "assume default black text" fast-path cache-eligibility check
    // (GridView.TextLayoutCache.cs UsesDefaultTextLayoutStyleCore) must also re-resolve, not just
    // read the baked FontColor -- a cell whose FontThemeColor happened to bake to BLACK under the
    // theme in effect at load time must not stay eligible for the "assume black" fast path forever.
    // ------------------------------------------------------------------
    [Fact]
    public void FastPathCacheEligibility_FontThemeColor_BakedBlack_StillRepaintsAfterThemeResolvesNonBlack()
    {
        WpfTestThread.Run(() =>
        {
            // Every OTHER eligibility condition for the "default" fast path is satisfied (Calibri,
            // 11pt, no bold/italic/underline/etc) and the BAKED FontColor is literally Black --
            // exactly the case the old style.FontColor.IsBlack check would have waved through.
            var cells = new[]
            {
                new DisplayCell(1, 1, new TextValue("MMMMMM"), "MMMMMM", null, default, null, new CellStyle
                {
                    FontColor = CellColor.Black,
                    FontThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1),
                    FontName = "Calibri",
                })
            };

            var grid = new GridView
            {
                Width = 160,
                Height = 60,
                ShowHeaders = false,
                ShowGridLines = false,
                WorkbookTheme = ThemeWithAccent1As(NewThemeOrange),
                Viewport = new ViewportModel(cells, [new RowMetric(1, 50, 0)], [new ColMetric(1, 150, 0)]),
            };
            grid.Measure(new Size(grid.Width, grid.Height));
            grid.Arrange(new Rect(0, 0, grid.Width, grid.Height));
            grid.UpdateLayout();

            var bitmap = RenderGridToBitmap(grid);

            AnyPixelNear(bitmap, 40, 30, 25, NewThemeOrange).Should().BeTrue(
                "even though the baked FontColor is literally Black, the FontThemeColor reference resolves to NewThemeOrange under the current theme, so the fast-path eligibility check must not short-circuit to plain black text");
        });
    }

    // ------------------------------------------------------------------
    // T5: a cell whose fill was set PURELY via a Theme Color picker (FillThemeColor with no baked
    // FillColor fallback -- reachable via StyleDiff.Apply, see CellStyle.cs) must still render its
    // fill on the very first paint, not just after a later theme swap. This is the
    // HasVisibleCellSurface presence-check fix (GridView.Rendering.CellStyles.cs).
    // ------------------------------------------------------------------
    [Fact]
    public void MainGridPass_ThemeOnlyFill_WithNoBakedFillColor_StillRendersOnFirstPaint()
    {
        WpfTestThread.Run(() =>
        {
            var cells = new[]
            {
                new DisplayCell(1, 1, null, "", null, default, null, new CellStyle
                {
                    // FillColor deliberately left null/default: only the theme reference is set,
                    // exactly as StyleDiff.Apply leaves it when a user picks a Theme Fill Color.
                    FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2)
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
            var pixel = SamplePixel(bitmap, 45, 15);

            IsCloseTo(NewThemeBlue, pixel).Should().BeTrue(
                $"a cell whose fill was set purely via FillThemeColor (no baked FillColor) must still paint its resolved theme color, not be silently skipped by the surface-presence check; sampled rgb=({pixel.R},{pixel.G},{pixel.B})");
        });
    }

    // ------------------------------------------------------------------
    // No-regression sibling: a cell with a PLAIN explicit color (no theme reference at all) must
    // keep rendering exactly that color regardless of the active theme -- the fix must not force
    // theme resolution onto cells that never opted into it.
    // ------------------------------------------------------------------
    [Fact]
    public void MainGridPass_PlainExplicitFillColor_WithNoThemeReference_StillRendersExactColor_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var cells = new[]
            {
                new DisplayCell(1, 1, null, "", null, default, null, new CellStyle
                {
                    FillColor = PlainExplicitPurple
                    // FillThemeColor intentionally left null: a plain, non-themed fill color.
                })
            };

            var grid = new GridView
            {
                Width = 100,
                Height = 40,
                ShowHeaders = false,
                ShowGridLines = false,
                // Deliberately swap the active theme to something with wildly different accents --
                // must have ZERO effect on a cell with no FillThemeColor reference.
                WorkbookTheme = ThemeWithAccent2As(NewThemeBlue),
                Viewport = new ViewportModel(cells, [new RowMetric(1, 30, 0)], [new ColMetric(1, 90, 0)]),
            };
            grid.Measure(new Size(grid.Width, grid.Height));
            grid.Arrange(new Rect(0, 0, grid.Width, grid.Height));
            grid.UpdateLayout();

            var bitmap = RenderGridToBitmap(grid);
            var pixel = SamplePixel(bitmap, 45, 15);

            IsCloseTo(PlainExplicitPurple, pixel).Should().BeTrue(
                $"a cell with no FillThemeColor reference must keep rendering its plain explicit FillColor unchanged, regardless of the active theme; sampled rgb=({pixel.R},{pixel.G},{pixel.B})");
        });
    }
}
