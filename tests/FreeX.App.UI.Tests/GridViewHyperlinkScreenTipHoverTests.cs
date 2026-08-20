using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// freex-hyperlinks F1: the WPF GridView never surfaced a hyperlinked cell's ScreenTip/target as a
/// hover tooltip -- the only feedback was a Ctrl+hover hand cursor (TryHitTestHyperlinkCell), unlike
/// FreeX's own Avalonia shell (MainWindow.cs's ToolTip.SetTip/FormatHyperlinkTooltip, landed under
/// R88-app-hyperlink-navigation-5-4). GridView.Input.cs's UpdateHyperlinkScreenTip fixes this by
/// showing a Border dropped into <see cref="GridView.CommentOverlayHost"/> on plain hover (no Ctrl
/// needed) using the host-supplied <see cref="GridView.HyperlinkTooltips"/> text (the cell's custom
/// ScreenTip, or the raw target when none was set -- that fallback selection itself happens in
/// MainWindow.Viewport.cs, mirroring Avalonia's FormatHyperlinkTooltip). A native WPF
/// ToolTip/Popup+PlacementMode was deliberately avoided, matching
/// GridCommentPreviewPlacementPlannerTests's pinned source contract that GridView.Input.cs never
/// references System.Windows.Controls.Primitives -- the same reason GridView.CommentPreview.cs
/// positions its own hover preview by hand instead.
/// </summary>
public sealed class GridViewHyperlinkScreenTipHoverTests
{
    private static GridView CreateGridWithCell()
    {
        var cells = new DisplayCell[]
        {
            new(1, 1, null, "Link", null, default, null)
        };

        var grid = new GridView
        {
            Width = 160,
            Height = 40,
            ShowHeaders = false,
            ShowGridLines = false,
            CommentOverlayHost = new Canvas(),
            Viewport = new ViewportModel(
                cells,
                [new RowMetric(1, 40, 0)],
                [new ColMetric(1, 80, 0)])
        };

        grid.Measure(new Size(160, 40));
        grid.Arrange(new Rect(0, 0, 160, 40));
        grid.UpdateLayout();

        // A render pass is required before invoking the hover helper below, matching
        // GridViewPinnedNoteHoverPreviewTests -- the private lookups the hit-test path consults
        // are only rebuilt from OnRender.
        var bitmap = new RenderTargetBitmap(160, 40, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        return grid;
    }

    private static void InvokeUpdateHyperlinkScreenTip(GridView grid, Point pos)
    {
        var method = typeof(GridView).GetMethod("UpdateHyperlinkScreenTip", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("GridView.Input.cs must implement the hover ScreenTip lookup");
        method!.Invoke(grid, [pos]);
    }

    private static void InvokeDismissHyperlinkScreenTip(GridView grid)
    {
        var method = typeof(GridView).GetMethod("DismissHyperlinkScreenTip", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        method!.Invoke(grid, null);
    }

    private static Border? GetHyperlinkScreenTipBorder(GridView grid)
    {
        var field = typeof(GridView).GetField("_hyperlinkScreenTipBorder", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        return (Border?)field!.GetValue(grid);
    }

    private static string? GetHyperlinkScreenTipText(GridView grid)
    {
        var field = typeof(GridView).GetField("_hyperlinkScreenTipTextBlock", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        return ((TextBlock?)field!.GetValue(grid))?.Text;
    }

    [Fact]
    public void HoverOverHyperlinkCell_ShowsScreenTipText()
    {
        WpfTestThread.Run(() =>
        {
            var grid = CreateGridWithCell();
            var address = new CellAddress(default, 1, 1);
            grid.HyperlinkCells = new HashSet<CellAddress> { address };
            grid.HyperlinkTooltips = new Dictionary<CellAddress, string> { [address] = "Q3 report (internal)" };

            // HitTestViewportCell always assumes the standard 30px row-header / 18px column-header
            // hit-test origin regardless of ShowHeaders (matching
            // GridViewPinnedNoteHoverPreviewTests), so (50, 30) is the pointer position that
            // resolves to cell (1,1) -- no Ctrl modifier needed, unlike the hand-cursor check.
            InvokeUpdateHyperlinkScreenTip(grid, new Point(50, 30));

            var border = GetHyperlinkScreenTipBorder(grid);
            border.Should().NotBeNull("hovering a hyperlinked cell must surface its ScreenTip, matching Excel and FreeX's Avalonia shell");
            border!.Visibility.Should().Be(Visibility.Visible);
            GetHyperlinkScreenTipText(grid).Should().Be("Q3 report (internal)");
        });
    }

    [Fact]
    public void MovingOffHyperlinkCell_DismissesScreenTip()
    {
        WpfTestThread.Run(() =>
        {
            var grid = CreateGridWithCell();
            var address = new CellAddress(default, 1, 1);
            grid.HyperlinkCells = new HashSet<CellAddress> { address };
            grid.HyperlinkTooltips = new Dictionary<CellAddress, string> { [address] = "Q3 report (internal)" };

            InvokeUpdateHyperlinkScreenTip(grid, new Point(50, 30));
            GetHyperlinkScreenTipBorder(grid)!.Visibility.Should().Be(Visibility.Visible);

            InvokeDismissHyperlinkScreenTip(grid);

            GetHyperlinkScreenTipBorder(grid)!.Visibility.Should().Be(
                Visibility.Collapsed, "leaving the hyperlinked cell must close the ScreenTip");
        });
    }

    [Fact]
    public void HoverOverPlainCell_NoRegression_NeverShowsHyperlinkScreenTip()
    {
        WpfTestThread.Run(() =>
        {
            var grid = CreateGridWithCell();
            // No HyperlinkCells/HyperlinkTooltips supplied at all -- an ordinary cell must never
            // pick up a hyperlink hover ScreenTip.
            InvokeUpdateHyperlinkScreenTip(grid, new Point(50, 30));

            var border = GetHyperlinkScreenTipBorder(grid);
            (border is null || border.Visibility != Visibility.Visible).Should().BeTrue(
                "a cell with no hyperlink must never show the hyperlink hover ScreenTip");
        });
    }
}
