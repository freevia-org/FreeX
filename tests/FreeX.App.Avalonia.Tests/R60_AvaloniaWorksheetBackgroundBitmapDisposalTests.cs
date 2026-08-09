using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FluentAssertions;

using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression guard for round-60 finding R60-missing-dispose-sweep-3: the Avalonia worksheet-
/// background <see cref="Bitmap"/> cache (<c>_worksheetBackgroundBrushCache</c> in MainWindow.cs)
/// silently overwrote or nulled the previously cached <see cref="ImageBrush"/>/<see cref="Bitmap"/>
/// whenever the Page Layout background picture changed or was removed, without ever disposing the
/// old native Skia-backed bitmap — a per-change leak of native memory for the life of the window.
/// Fixed by disposing the previous cache entry's <see cref="Bitmap"/> before replacing/clearing it.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R60_AvaloniaWorksheetBackgroundBitmapDisposalTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task BuildWorksheetBackgroundBrush_DisposesPreviousBitmap_WhenBackgroundImageChanges()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            sheet.BackgroundImage = new WorksheetBackgroundImage(MinimalPngBytes(), "image/png", "bg1.png");

            var firstGrid = FindInnerGrid(window.RebuildSheetGridForTest());
            firstGrid.Background.Should().BeOfType<ImageBrush>();
            var firstBrush = (ImageBrush)firstGrid.Background!;
            var firstBitmap = (Bitmap)firstBrush.Source!;

            // Sanity: the first bitmap is alive and usable before the background picture changes.
            var initialSize = firstBitmap.PixelSize;
            initialSize.Width.Should().Be(1);

            // Swap in a brand-new WorksheetBackgroundImage instance (a distinct reference, exactly
            // like the user picking a different Page Layout background picture) so
            // BuildWorksheetBackgroundBrush takes the decode-and-replace path instead of the
            // ReferenceEquals cache-hit.
            sheet.BackgroundImage = new WorksheetBackgroundImage(MinimalPngBytes(), "image/png", "bg2.png");

            var secondGrid = FindInnerGrid(window.RebuildSheetGridForTest());
            var secondBrush = (ImageBrush)secondGrid.Background!;
            secondBrush.Should().NotBeSameAs(firstBrush, "a new background image must produce a new cached ImageBrush");

            // The old cached bitmap must have been disposed rather than silently dropped/leaked.
            Action accessDisposedBitmap = () => _ = firstBitmap.PixelSize;
            accessDisposedBitmap.Should().Throw<ObjectDisposedException>(
                "the previous worksheet-background Bitmap must be disposed once it is replaced in the cache");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    // ── Sibling/no-regression: the still-current cached bitmap must stay usable ─────────────────

    [Fact]
    public async Task BuildWorksheetBackgroundBrush_KeepsCurrentBitmapUsable_WhenBackgroundImageIsUnchanged()
    {
        // Rebuilding the grid again with the SAME WorksheetBackgroundImage reference must hit the
        // ReferenceEquals cache path and return the exact same, still-usable ImageBrush/Bitmap - the
        // disposal fix must only affect the REPLACED entry, never the one still in active use.
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            var sheet = window.Session.Workbook.AddSheet("CleanFixture");
            window.Session.SelectSheet(sheet.Id);

            var background = new WorksheetBackgroundImage(MinimalPngBytes(), "image/png", "bg.png");
            sheet.BackgroundImage = background;

            var firstGrid = FindInnerGrid(window.RebuildSheetGridForTest());
            var firstBrush = (ImageBrush)firstGrid.Background!;
            var firstBitmap = (Bitmap)firstBrush.Source!;

            var secondGrid = FindInnerGrid(window.RebuildSheetGridForTest());
            var secondBrush = (ImageBrush)secondGrid.Background!;

            secondBrush.Should().BeSameAs(firstBrush, "the unchanged background image must hit the cache and reuse the same brush");
            Action accessStillCachedBitmap = () => _ = firstBitmap.PixelSize;
            accessStillCachedBitmap.Should().NotThrow("the still-current cached bitmap must remain usable");

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    /// <summary>
    /// BuildSheetGrid returns the sheet cell grid directly when there is no overlay/page-break
    /// content, or wraps it as the first child of a composite Grid when there is. The sheet's own
    /// cell grid is the only one of these that always sets an explicit Background.
    /// </summary>
    private static Grid FindInnerGrid(Control built)
    {
        if (built is Grid { Background: not null } ownGrid)
            return ownGrid;

        if (built is Grid composite)
            return composite.Children.OfType<Grid>().First(g => g.Background is not null);

        return (Grid)built;
    }
}
