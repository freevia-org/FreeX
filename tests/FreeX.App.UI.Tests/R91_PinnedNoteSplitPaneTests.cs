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
/// R91-render-comment-ui-5-1: a pinned ("Show Comment") note box's screen position, built by
/// RefreshPinnedNoteBoxes, used to be computed purely from the FLAT ViewportModel.ColMetrics/RowMetrics
/// via the single-pane <c>TryGetCellRect</c> overload -- it never consulted <see
/// cref="ViewportModel.SplitPanes"/>, so in a split window the pinned box landed at the flat position
/// instead of wherever its cell actually sits in its own (independently-scrolled) pane. The transient
/// hover preview (TryGetCommentPreviewAt/TryGetCommentPreviewForCell) already walked
/// <c>CalculateSplitPaneCellLayouts</c> first for exactly this reason.
/// </summary>
public sealed class R91_PinnedNoteSplitPaneTests
{
    private static (GridView Grid, ViewportModel Viewport, CellCommentDisplay Comment) BuildSplitGrid()
    {
        var comment = new CellCommentDisplay(CellCommentDisplayKind.Note, "Note", "Body");
        var cells = new DisplayCell[]
        {
            new(1, 3, null, "", null, default, null, HasComment: true, CommentDisplay: comment)
        };

        // Vertical-only split after column 2: LeftColumns covers cols 1-2, TopRightColumns covers
        // cols 3-4 but rebased to its OWN pane-local origin (col 3 starts at local offset 0) --
        // mirroring the independently-scrolled right pane, exactly like
        // GridViewSplitPaneLayoutTests.MergeCommentQuadrant's fixture.
        var viewport = new ViewportModel(
            cells,
            [new RowMetric(1, 20, 0)],
            [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144), new ColMetric(4, 80, 208)],
            SplitPanes: new SplitPaneState(
                null,
                3,
                [],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64)],
                cells,
                [new ColMetric(3, 64, 0), new ColMetric(4, 80, 64)]));

        var grid = new GridView
        {
            Width = 400,
            Height = 200,
            ShowHeaders = false,
            ShowGridLines = false,
            CommentOverlayHost = new Canvas(),
            PinnedNoteAddresses = new HashSet<(uint Row, uint Col)> { (1u, 3u) },
            Viewport = viewport
        };

        grid.Measure(new Size(400, 200));
        grid.Arrange(new Rect(0, 0, 400, 200));
        grid.UpdateLayout();
        var bitmap = new RenderTargetBitmap(400, 200, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(grid);
        // PinnedNoteAddresses/Viewport were set before layout ran (ActualWidth/Height still 0 at that
        // point), so force a fresh rebuild now that real dimensions are known -- exactly what the real
        // app does when a resize/render invalidates the pinned overlay.
        grid.RefreshPinnedNoteBoxes();

        return (grid, viewport, comment);
    }

    private static Border GetPinnedNoteBorder(GridView grid, uint row, uint col)
    {
        var field = typeof(GridView).GetField("_pinnedNoteBorders", BindingFlags.NonPublic | BindingFlags.Instance);
        field.Should().NotBeNull();
        var dict = (System.Collections.IDictionary)field!.GetValue(grid)!;
        dict.Contains((row, col)).Should().BeTrue("RefreshPinnedNoteBoxes must have built a border for the pinned cell");
        return (Border)dict[(row, col)]!;
    }

    private static Rect InvokeTryGetPinnedNoteCellRect(GridView grid, ViewportModel viewport, uint row, uint col)
    {
        var method = typeof(GridView).GetMethod("TryGetPinnedNoteCellRect", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        var args = new object?[] { viewport, row, col, null };
        ((bool)method!.Invoke(grid, args)!).Should().BeTrue();
        return (Rect)args[3]!;
    }

    [Fact]
    public void RefreshPinnedNoteBoxes_SplitWindow_UsesPaneAdjustedPosition()
    {
        WpfTestThread.Run(() =>
        {
            var (grid, viewport, comment) = BuildSplitGrid();

            // The authoritative split-pane rect for (1,3): the ONLY other place in this file that
            // already gets this right (the hover preview) resolves it the same way.
            var layouts = GridView.CalculateSplitPaneCellLayouts(viewport, null);
            var splitRect = layouts.Should().ContainSingle(l => l.Cell.Row == 1 && l.Cell.Col == 3).Subject.Rect;

            // Sanity: the flat (pre-fix) rect really is different here -- otherwise this fixture
            // wouldn't actually exercise the bug.
            var flatRectMethod = typeof(GridView).GetMethod(
                "TryGetCellRect",
                BindingFlags.NonPublic | BindingFlags.Instance,
                [typeof(ViewportModel), typeof(uint), typeof(uint), typeof(Rect).MakeByRefType()]);
            flatRectMethod.Should().NotBeNull();
            var flatArgs = new object?[] { viewport, 1u, 3u, null };
            ((bool)flatRectMethod!.Invoke(grid, flatArgs)!).Should().BeTrue();
            var flatRect = (Rect)flatArgs[3]!;
            flatRect.Should().NotBe(splitRect, "the fixture must place the cell differently in its pane than in the flat viewport for this test to be meaningful");

            // The actual fixed lookup GridView.CommentPreview.cs now calls must agree with the
            // split-pane geometry, not the flat one.
            var actualRect = InvokeTryGetPinnedNoteCellRect(grid, viewport, 1u, 3u);
            actualRect.Should().Be(splitRect);

            // And the real consumer -- RefreshPinnedNoteBoxes, which actually positions the ink drawn
            // on screen via Canvas.SetLeft/SetTop -- must have used that same split-aware rect.
            var expectedPlacement = GridCommentPreviewPlacementPlanner.Calculate(
                splitRect, new Size(grid.ActualWidth, grid.ActualHeight), comment);
            var border = GetPinnedNoteBorder(grid, 1, 3);
            border.Visibility.Should().Be(Visibility.Visible);
            Canvas.GetLeft(border).Should().Be(expectedPlacement.HorizontalOffset);
            Canvas.GetTop(border).Should().Be(expectedPlacement.VerticalOffset);
        });
    }

    [Fact]
    public void RefreshPinnedNoteBoxes_NoSplit_UsesFlatPosition_NoRegression()
    {
        WpfTestThread.Run(() =>
        {
            var comment = new CellCommentDisplay(CellCommentDisplayKind.Note, "Note", "Body");
            var cells = new DisplayCell[]
            {
                new(1, 3, null, "", null, default, null, HasComment: true, CommentDisplay: comment)
            };
            var viewport = new ViewportModel(
                cells,
                [new RowMetric(1, 20, 0)],
                [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64), new ColMetric(3, 64, 144), new ColMetric(4, 80, 208)]);

            var grid = new GridView
            {
                Width = 400,
                Height = 200,
                ShowHeaders = false,
                ShowGridLines = false,
                CommentOverlayHost = new Canvas(),
                PinnedNoteAddresses = new HashSet<(uint Row, uint Col)> { (1u, 3u) },
                Viewport = viewport
            };
            grid.Measure(new Size(400, 200));
            grid.Arrange(new Rect(0, 0, 400, 200));
            grid.UpdateLayout();
            var bitmap = new RenderTargetBitmap(400, 200, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(grid);
            grid.RefreshPinnedNoteBoxes();

            var expectedPlacement = GridCommentPreviewPlacementPlanner.Calculate(
                new Rect(144, 0, 64, 20), new Size(grid.ActualWidth, grid.ActualHeight), comment);

            var border = GetPinnedNoteBorder(grid, 1, 3);
            border.Visibility.Should().Be(Visibility.Visible);
            Canvas.GetLeft(border).Should().Be(expectedPlacement.HorizontalOffset,
                "without a split window the pinned note box must keep using the flat viewport position, same as before this fix");
            Canvas.GetTop(border).Should().Be(expectedPlacement.VerticalOffset);
        });
    }
}
