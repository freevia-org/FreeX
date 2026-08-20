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

    private static void InvokeUpdateCommentPreviewForPointer(GridView grid, Point pos)
    {
        var method = typeof(GridView).GetMethod("UpdateCommentPreviewForPointer", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        method!.Invoke(grid, [pos]);
    }

    /// <summary>
    /// Mirrors the bookkeeping OnMouseMove performs (see GridView.Input.cs) for the F1 (round 159)
    /// fix -- <c>_lastGridPointerPosition</c>/<c>_isPointerInsideGridView</c> -- without needing a
    /// real <see cref="System.Windows.Input.MouseEventArgs"/> (this test project has no
    /// PresentationSource/MouseDevice to construct one from, matching every other helper in this
    /// class that drives GridView's hover handlers directly instead of through a real mouse event).
    /// </summary>
    private static void SetPointerInsideGridView(GridView grid, Point pos)
    {
        var posField = typeof(GridView).GetField("_lastGridPointerPosition", BindingFlags.NonPublic | BindingFlags.Instance);
        posField.Should().NotBeNull("GridView.Input.cs must track the last pointer position for the F1 refresh-on-data-change fix");
        posField!.SetValue(grid, pos);

        var insideField = typeof(GridView).GetField("_isPointerInsideGridView", BindingFlags.NonPublic | BindingFlags.Instance);
        insideField.Should().NotBeNull("GridView.Input.cs must track whether the pointer is inside the grid for the F1 refresh-on-data-change fix");
        insideField!.SetValue(grid, true);
    }

    private static Border? GetCommentPreviewBorder(GridView grid)
    {
        var field = typeof(GridView).GetField("_commentPreviewBorder", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        return (Border?)field!.GetValue(grid);
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

    /// <summary>
    /// Round 158 remediation of a round-152 regression: Excel allows one cell to carry both a
    /// hyperlink and a comment, and OnMouseMove calls UpdateCommentPreviewForPointer followed
    /// unconditionally by UpdateHyperlinkScreenTip, so hovering such a cell used to raise two
    /// overlapping hover boxes for the same pointer position. FreeX.App.Avalonia's MainWindow.cs
    /// cell-build (~line 8843) already gets this right -- comment ToolTip.SetTip wins, hyperlink
    /// ToolTip.SetTip only runs in the "else" branch. This mirrors that: the comment preview must
    /// still show, and the hyperlink ScreenTip must be suppressed for the identical cell.
    /// </summary>
    [Fact]
    public void HoverOverCellWithHyperlinkAndComment_ShowsCommentPreview_SuppressesHyperlinkScreenTip()
    {
        WpfTestThread.Run(() =>
        {
            var comment = new CellCommentDisplay(CellCommentDisplayKind.Note, "Note", "Body");
            var cells = new DisplayCell[]
            {
                new(1, 1, null, "Link", null, default, null, HasComment: true, CommentDisplay: comment)
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
            var bitmap = new RenderTargetBitmap(160, 40, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(grid);

            var address = new CellAddress(default, 1, 1);
            grid.HyperlinkCells = new HashSet<CellAddress> { address };
            grid.HyperlinkTooltips = new Dictionary<CellAddress, string> { [address] = "Q3 report (internal)" };

            // Mirrors OnMouseMove's exact call order in GridView.Input.cs: the comment preview is
            // updated first, then the hyperlink ScreenTip, both for the same pointer position.
            InvokeUpdateCommentPreviewForPointer(grid, new Point(50, 30));
            InvokeUpdateHyperlinkScreenTip(grid, new Point(50, 30));

            var commentBorder = GetCommentPreviewBorder(grid);
            commentBorder.Should().NotBeNull();
            commentBorder!.Visibility.Should().Be(
                Visibility.Visible,
                "Excel prioritizes the comment popup when a cell carries both a comment and a hyperlink");

            var hyperlinkBorder = GetHyperlinkScreenTipBorder(grid);
            (hyperlinkBorder is null || hyperlinkBorder.Visibility != Visibility.Visible).Should().BeTrue(
                "the hyperlink ScreenTip must not stack on top of the comment preview for the same cell");
        });
    }

    /// <summary>
    /// Sibling no-regression case for the fix above: the comment-vs-hyperlink exclusion must be
    /// scoped to the individual cell under the pointer, not to "some comment preview is active
    /// anywhere". A comment preview raised for one cell must not suppress an unrelated hyperlink
    /// cell's ScreenTip once the pointer has moved on to hover it.
    /// </summary>
    [Fact]
    public void HoverOverHyperlinkCell_AfterCommentPreviewElsewhere_NoRegression_StillShowsScreenTip()
    {
        WpfTestThread.Run(() =>
        {
            var comment = new CellCommentDisplay(CellCommentDisplayKind.Note, "Note", "Body");
            var cells = new DisplayCell[]
            {
                new(1, 1, null, "", null, default, null, HasComment: true, CommentDisplay: comment),
                new(2, 1, null, "Link", null, default, null)
            };

            var grid = new GridView
            {
                Width = 160,
                Height = 80,
                ShowHeaders = false,
                ShowGridLines = false,
                CommentOverlayHost = new Canvas(),
                Viewport = new ViewportModel(
                    cells,
                    [new RowMetric(1, 40, 0), new RowMetric(2, 40, 40)],
                    [new ColMetric(1, 80, 0)])
            };

            grid.Measure(new Size(160, 80));
            grid.Arrange(new Rect(0, 0, 160, 80));
            grid.UpdateLayout();
            var bitmap = new RenderTargetBitmap(160, 80, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(grid);

            var linkAddress = new CellAddress(default, 2, 1);
            grid.HyperlinkCells = new HashSet<CellAddress> { linkAddress };
            grid.HyperlinkTooltips = new Dictionary<CellAddress, string> { [linkAddress] = "Q3 report (internal)" };

            // First hover the comment-only cell (row 1) so its preview becomes active...
            InvokeUpdateCommentPreviewForPointer(grid, new Point(50, 30));
            GetCommentPreviewBorder(grid)!.Visibility.Should().Be(Visibility.Visible);

            // ...then move to the unrelated hyperlink-only cell (row 2), mirroring the same
            // per-position pairing OnMouseMove performs.
            InvokeUpdateCommentPreviewForPointer(grid, new Point(50, 70));
            InvokeUpdateHyperlinkScreenTip(grid, new Point(50, 70));

            var hyperlinkBorder = GetHyperlinkScreenTipBorder(grid);
            hyperlinkBorder.Should().NotBeNull();
            hyperlinkBorder!.Visibility.Should().Be(
                Visibility.Visible,
                "a hyperlink cell with no comment of its own must still show its ScreenTip, even if a " +
                "different cell's comment preview was active moments before");
        });
    }

    /// <summary>
    /// F1 (round 159): UpdateHyperlinkScreenTip's comment-suppression guard above is a pure
    /// per-position query, only ever re-evaluated from OnMouseMove/OnMouseLeave. Deleting the
    /// hovered cell's comment rebuilds the Viewport (as any edit does, via
    /// GridView.Properties.cs's OnViewportChanged) without the mouse moving, so nothing used to
    /// re-run that guard and the ScreenTip stayed wrongly hidden. This proves the fix by driving
    /// the exact same trigger production code uses -- assigning a new GridView.Viewport -- and
    /// nothing else: no direct call to UpdateHyperlinkScreenTip/UpdateCommentPreviewForPointer is
    /// made after the comment is removed.
    /// </summary>
    [Fact]
    public void CommentDeletedMidHover_ViewportChangeAloneRevivesHyperlinkScreenTip_NoMouseMoveNeeded()
    {
        WpfTestThread.Run(() =>
        {
            var comment = new CellCommentDisplay(CellCommentDisplayKind.Note, "Note", "Body");
            var cellWithComment =
                new DisplayCell(1, 1, null, "Link", null, default, null, HasComment: true, CommentDisplay: comment);

            var grid = new GridView
            {
                Width = 160,
                Height = 40,
                ShowHeaders = false,
                ShowGridLines = false,
                CommentOverlayHost = new Canvas(),
                Viewport = new ViewportModel(
                    [cellWithComment],
                    [new RowMetric(1, 40, 0)],
                    [new ColMetric(1, 80, 0)])
            };

            grid.Measure(new Size(160, 40));
            grid.Arrange(new Rect(0, 0, 160, 40));
            grid.UpdateLayout();
            var bitmap = new RenderTargetBitmap(160, 40, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(grid);

            var address = new CellAddress(default, 1, 1);
            grid.HyperlinkCells = new HashSet<CellAddress> { address };
            grid.HyperlinkTooltips = new Dictionary<CellAddress, string> { [address] = "Q3 report (internal)" };

            // Hover the cell (mirrors OnMouseMove's own bookkeeping and call order).
            SetPointerInsideGridView(grid, new Point(50, 30));
            InvokeUpdateCommentPreviewForPointer(grid, new Point(50, 30));
            InvokeUpdateHyperlinkScreenTip(grid, new Point(50, 30));

            GetCommentPreviewBorder(grid)!.Visibility.Should().Be(
                Visibility.Visible, "baseline: the comment preview must show while hovering the cell");
            var suppressedBorder = GetHyperlinkScreenTipBorder(grid);
            (suppressedBorder is null || suppressedBorder.Visibility != Visibility.Visible).Should().BeTrue(
                "baseline: the hyperlink ScreenTip must be suppressed while the cell still has a comment");

            // Delete the comment "mid-hover": rebuild the Viewport with CommentDisplay/HasComment
            // cleared for the same cell -- exactly what Review > Delete Comment does (MainWindow
            // rebuilds Viewport from a fresh command) -- without moving the mouse and without
            // calling any Update*ForPointer/ScreenTip method directly.
            var cellWithoutComment = cellWithComment with { HasComment = false, CommentDisplay = null };
            grid.Viewport = new ViewportModel(
                [cellWithoutComment],
                [new RowMetric(1, 40, 0)],
                [new ColMetric(1, 80, 0)]);

            var refreshedBorder = GetHyperlinkScreenTipBorder(grid);
            refreshedBorder.Should().NotBeNull();
            refreshedBorder!.Visibility.Should().Be(
                Visibility.Visible,
                "the comment is gone and the mouse never left the cell, so the Viewport rebuild alone must " +
                "revive the hyperlink ScreenTip without requiring the mouse to move even one pixel");
        });
    }

    /// <summary>
    /// Sibling no-regression case for the fix above: a Viewport rebuild must not spawn a hyperlink
    /// ScreenTip out of nowhere when the pointer was never inside the grid to begin with (e.g. an
    /// edit made entirely via keyboard/menu while the mouse sits elsewhere on screen). Guards
    /// against a fix that dropped the <c>_isPointerInsideGridView</c> check and refreshed
    /// unconditionally at a stale/default position.
    /// </summary>
    [Fact]
    public void ViewportChange_WithoutPriorHover_NoRegression_NeverSpawnsHyperlinkScreenTip()
    {
        WpfTestThread.Run(() =>
        {
            var cells = new DisplayCell[] { new(1, 1, null, "Link", null, default, null) };
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
            var bitmap = new RenderTargetBitmap(160, 40, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(grid);

            var address = new CellAddress(default, 1, 1);
            grid.HyperlinkCells = new HashSet<CellAddress> { address };
            grid.HyperlinkTooltips = new Dictionary<CellAddress, string> { [address] = "Q3 report (internal)" };

            // The pointer never entered the grid in this test -- _isPointerInsideGridView stays
            // false, matching a real session where no OnMouseMove has ever fired for this control.
            grid.Viewport = new ViewportModel(
                cells,
                [new RowMetric(1, 40, 0)],
                [new ColMetric(1, 80, 0)]);

            var border = GetHyperlinkScreenTipBorder(grid);
            (border is null || border.Visibility != Visibility.Visible).Should().BeTrue(
                "a Viewport rebuild must never show the hyperlink ScreenTip when the pointer was never inside the grid");
        });
    }
}
