using System.Collections;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Regression coverage for cleanup batch MED4 (round-10 findings P34, P38, P40, P59).
/// </summary>
public sealed class FreeXCleanupMED4Tests
{
    // P34: split-pane cell automation peers must resolve bounds via the pinned pane's own
    // metrics (not just the main Viewport.RowMetrics/ColMetrics) and must offset the main-pane
    // origin by the pinned pane extent, matching GridView.SplitPanes.cs's rendering/hit-testing.
    [Fact]
    public void GetCellBoundingRectangle_AccountsForSplitPanePinnedExtentAndOwnMetrics()
    {
        WpfTestThread.Run(() =>
        {
            // Main viewport: row 20 (bottom pane), col 10 (right pane).
            // Split panes: TopRows = rows 1-2 (total height 40), LeftColumns = cols 1-2 (total width 144).
            var viewport = new ViewportModel(
                [],
                [new RowMetric(20, 18, 0), new RowMetric(21, 18, 18)],
                [new ColMetric(10, 64, 0), new ColMetric(11, 64, 64)],
                SplitPanes: new SplitPaneState(
                    4,
                    4,
                    [new RowMetric(1, 18, 0), new RowMetric(2, 22, 18)],
                    [new ColMetric(1, 64, 0), new ColMetric(2, 80, 64)]));

            var grid = new GridView { Viewport = viewport, SelectedRange = null };
            var peer = UIElementAutomationPeer.CreatePeerForElement(grid);
            var children = peer.GetChildren();

            // Top-left pinned pane cell (row 1, col 1): before the fix, TryGetMetric only searched
            // Viewport.RowMetrics/ColMetrics, so a cell that exists solely in SplitPanes.TopRows/
            // LeftColumns came back as Rect.Empty (offscreen/unclickable to a screen reader).
            var topLeftCell = children.Single(c => c.GetAutomationId() == "Cell_A1");
            var topLeftBounds = topLeftCell.GetBoundingRectangle();

            topLeftBounds.Should().NotBe(Rect.Empty);
            topLeftBounds.Width.Should().Be(64);
            topLeftBounds.Height.Should().Be(18);
            // X = RowHeaderWidth (30) + LeftOffset (0); Y = ColHeaderHeight (18) + TopOffset (0).
            topLeftBounds.X.Should().Be(30);
            topLeftBounds.Y.Should().Be(18);

            // Main-pane (bottom-right) cell (row 20, col 10): before the fix, its bounds were
            // computed as ActualRowHeaderWidth + LeftOffset / EffectiveColHeaderHeight + TopOffset,
            // silently ignoring the pinned top/left pane extent, so the reported position was
            // shifted up-and-left of where the cell is actually rendered.
            var mainCell = children.Single(c => c.GetAutomationId() == "Cell_J20");
            var mainBounds = mainCell.GetBoundingRectangle();

            mainBounds.Should().NotBe(Rect.Empty);
            // X = verticalX (RowHeaderWidth 30 + leftWidth 144 = 174) + LeftOffset (0).
            mainBounds.X.Should().Be(174);
            // Y = horizontalY (ColHeaderHeight 18 + topHeight 40 = 58) + TopOffset (0).
            mainBounds.Y.Should().Be(58);
        });
    }

    // P38: GridViewAutomationPeer._cellPeers must not grow without bound as a UIA client walks a
    // large workbook; peers for cells that scroll out of the visible viewport must be evicted.
    [Fact]
    public void CellPeerCache_EvictsPeersForCellsNoLongerInTheVisibleViewport()
    {
        WpfTestThread.Run(() =>
        {
            var grid = new GridView
            {
                Viewport = GridViewTestHelpers.CreateTwoByTwoViewport(startRow: 1, startColumn: 1)
            };

            var peer = UIElementAutomationPeer.CreatePeerForElement(grid);

            // Force peer creation for the initial 2x2 viewport (rows 1-2, cols 1-2).
            _ = peer.GetChildren();
            GetCellPeerCacheCount(peer).Should().Be(4);

            // Simulate scrolling through many disjoint viewports, as happens when a UIA client
            // (or the user) pages through a large workbook.
            for (uint page = 2; page <= 50; page++)
            {
                grid.Viewport = GridViewTestHelpers.CreateTwoByTwoViewport(startRow: page * 1000, startColumn: page * 100);
                _ = peer.GetChildren();
            }

            // Before the fix, every visited (row, col) pair stayed cached forever: 50 pages x 4
            // cells/page would leave 200 entries. After the fix, only cells still reachable from
            // the current viewport (plus the still-tracked active cell, here none) remain cached.
            GetCellPeerCacheCount(peer).Should().Be(4);
        });
    }

    // P40: the active cell's peer must re-announce Name/Value when the displayed content changes
    // without a selection move (Ctrl+Enter commit that leaves the selection in place, or an F9
    // recalc that updates the focused formula cell's displayed value).
    [Fact]
    public void ActiveCellPeer_ReAnnouncesValueWhenContentChangesWithoutSelectionMove()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var grid = new GridView
            {
                Viewport = new ViewportModel(
                    [new DisplayCell(1, 1, new NumberValue(2), "2", null, StyleId.Default, null)],
                    [new RowMetric(1, 20, 0)],
                    [new ColMetric(1, 64, 0)]),
                SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 1),
                    new CellAddress(sheetId, 1, 1))
            };

            var peer = UIElementAutomationPeer.CreatePeerForElement(grid);
            var activeCellPeer = peer.GetChildren().Single();
            activeCellPeer.GetName().Should().Be("A1: 2");

            // Simulate a Ctrl+Enter commit: the selection does not move, but the cell's displayed
            // value changes (new Viewport with the same SelectedRange).
            grid.Viewport = new ViewportModel(
                [new DisplayCell(1, 1, new NumberValue(5), "5", null, StyleId.Default, null)],
                [new RowMetric(1, 20, 0)],
                [new ColMetric(1, 64, 0)]);

            // The live query already reflects the fresh value regardless of the fix...
            activeCellPeer.GetName().Should().Be("A1: 5");

            // ...but before the fix, nothing tracked/propagated that the active cell's displayed
            // text had changed: the peer's internally-tracked baseline display text was never
            // updated outside of NotifySelectionChanged, so no Name-changed notification would
            // ever be raised for this edit. Verify the peer's internal tracking was refreshed by
            // the viewport-change hook (GridView.Properties.cs OnViewportChanged ->
            // NotifyViewportAutomationChanged -> NotifyActiveCellValueIfChanged).
            GetLastNotifiedActiveCellDisplayText(peer).Should().Be("5");
        });
    }

    [Fact]
    public void ActiveCellPeer_DoesNotReAnnounceWhenDisplayedValueIsUnchanged()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var cells = new DisplayCell[] { new(1, 1, new NumberValue(2), "2", null, StyleId.Default, null) };
            var grid = new GridView
            {
                Viewport = new ViewportModel(cells, [new RowMetric(1, 20, 0)], [new ColMetric(1, 64, 0)]),
                SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 1),
                    new CellAddress(sheetId, 1, 1))
            };

            var peer = UIElementAutomationPeer.CreatePeerForElement(grid);
            _ = peer.GetChildren().Single();
            GetLastNotifiedActiveCellDisplayText(peer).Should().Be("2");

            // Re-assigning an equal Viewport (e.g. an unrelated render-cache refresh) must not
            // desync the tracked baseline away from the still-current display text.
            grid.Viewport = new ViewportModel(cells, [new RowMetric(1, 20, 0)], [new ColMetric(1, 64, 0)]);

            GetLastNotifiedActiveCellDisplayText(peer).Should().Be("2");
        });
    }

    private static int GetCellPeerCacheCount(AutomationPeer peer)
    {
        var field = peer.GetType().GetField("_cellPeers", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        var dictionary = field!.GetValue(peer).Should().BeAssignableTo<IDictionary>().Subject;
        return dictionary.Count;
    }

    private static string? GetLastNotifiedActiveCellDisplayText(AutomationPeer peer)
    {
        var field = peer.GetType().GetField("_lastNotifiedActiveCellDisplayText", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return (string?)field!.GetValue(peer);
    }

    // P59: Increase Decimal must append the new placeholder at the END of the decimal-placeholder
    // run (matching the run's own '#'/'?' style), not immediately after the literal digits; and an
    // Increase+Decrease round-trip on a '#'-style format must be an identity, not a corrupting mask.
    [Theory]
    [InlineData("0.##", "0.###")]
    [InlineData("0.0#", "0.0##")]
    [InlineData("#.##", "#.###")]
    public void AddDecimalPlace_AppendsPlaceholderAtEndOfRunMatchingItsOwnStyle(string format, string expected)
    {
        NumberFormatDecimalAdjuster.AddDecimalPlace(format).Should().Be(expected);
    }

    [Fact]
    public void IncreaseThenDecreaseDecimal_OnHashStyleFormat_RoundTripsToOriginal()
    {
        const string original = "0.##";

        var increased = NumberFormatDecimalAdjuster.AddDecimalPlace(original);
        var roundTripped = NumberFormatDecimalAdjuster.RemoveDecimalPlace(increased);

        // Before the fix: AddDecimalPlace("0.##") inserted the new '0' immediately after the
        // literal digit run instead of at the end of the whole placeholder run, producing the
        // mis-shaped "0.0##" (a toolbar round-trip that should be an identity instead permanently
        // reshapes the format). After the fix, Increase Decimal appends a same-style placeholder
        // ("0.###"), and Decrease Decimal correctly trims it back to the original.
        increased.Should().Be("0.###");
        roundTripped.Should().Be(original);
    }
}
