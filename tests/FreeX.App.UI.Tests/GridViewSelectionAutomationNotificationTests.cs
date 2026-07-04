using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// Regression coverage for J7: the spreadsheet grid must raise UI Automation selection/focus
/// notifications when the active cell changes, so screen readers announce keyboard navigation.
/// Before the fix, SelectedRange/SelectedRanges changes only triggered render-cache/comment-preview
/// side effects (GridView.Properties.cs OnSelectionVisualPropertyChanged) with zero automation
/// notification, and the cell automation peer unconditionally reported no keyboard focus.
/// </summary>
public sealed class GridViewSelectionAutomationNotificationTests
{
    [Fact]
    public void ActiveCellPeer_ReportsKeyboardFocusOnlyForTheActiveCell()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var grid = new GridView
            {
                Viewport = GridViewTestHelpers.CreateTwoByTwoViewport(),
                SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 1),
                    new CellAddress(sheetId, 1, 1))
            };

            var peer = UIElementAutomationPeer.CreatePeerForElement(grid);
            var children = peer.GetChildren();
            var activeCellPeer = children.Single(c =>
                c.GetPattern(PatternInterface.GridItem) is IGridItemProvider { Row: 0, Column: 0 });
            var otherCellPeer = children.Single(c =>
                c.GetPattern(PatternInterface.GridItem) is IGridItemProvider { Row: 0, Column: 1 });

            // Before any navigation, the active cell (top-left of SelectedRange) already reports
            // keyboard focus/focusability, since GridView seeds this from the initial SelectedRange.
            activeCellPeer.IsKeyboardFocusable().Should().BeTrue();
            activeCellPeer.HasKeyboardFocus().Should().BeTrue();
            otherCellPeer.HasKeyboardFocus().Should().BeFalse();
        });
    }

    [Fact]
    public void MovingSelectedRange_MovesReportedKeyboardFocusToTheNewActiveCell()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var grid = new GridView
            {
                Viewport = GridViewTestHelpers.CreateTwoByTwoViewport(),
                SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 1),
                    new CellAddress(sheetId, 1, 1))
            };

            var peer = UIElementAutomationPeer.CreatePeerForElement(grid);

            // Simulate arrow-key navigation moving the active cell from A1 to B1 (row 1, col 2).
            grid.SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 2),
                new CellAddress(sheetId, 1, 2));

            var children = peer.GetChildren();
            var previousActiveCellPeer = children.Single(c =>
                c.GetPattern(PatternInterface.GridItem) is IGridItemProvider { Row: 0, Column: 0 });
            var newActiveCellPeer = children.Single(c =>
                c.GetPattern(PatternInterface.GridItem) is IGridItemProvider { Row: 0, Column: 1 });

            newActiveCellPeer.HasKeyboardFocus().Should().BeTrue();
            previousActiveCellPeer.HasKeyboardFocus().Should().BeFalse();
        });
    }

    [Fact]
    public void ActiveCellPeer_NameIncludesAddressAndCurrentValue()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var grid = new GridView
            {
                Viewport = new ViewportModel(
                    [new DisplayCell(1, 1, new NumberValue(7), "7", null, StyleId.Default, null)],
                    [new RowMetric(1, 20, 0)],
                    [new ColMetric(1, 64, 0)]),
                SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 1),
                    new CellAddress(sheetId, 1, 1))
            };

            var peer = UIElementAutomationPeer.CreatePeerForElement(grid);
            var activeCellPeer = peer.GetChildren().Single();

            activeCellPeer.GetName().Should().Be("A1: 7");
        });
    }
}
