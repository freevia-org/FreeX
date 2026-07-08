using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R14-accessibility-automation-2: GridView must announce the selection's true active/anchor
/// cell to UI Automation, not <see cref="GridView.SelectedRange"/>'s normalized top-left corner.
/// Reproduces the WPF host scenario from the finding: a screen-reader user clicks C3 (making it
/// the active/anchor cell), then Shift+Up twice to select C1:C3. GridRange always normalizes
/// Start to the top-left corner (C1), but the real active cell — where F2/typing would edit —
/// stays at the anchor, C3. Before the fix, GridView.cs:58 fed
/// <c>peer.NotifySelectionChanged(SelectedRange?.Start)</c>, so the automation peer announced C1
/// (and C1's value) even though C3 is what a screen-reader user would actually be editing.
/// </summary>
public sealed class FreeXR14T12Tests
{
    [Fact]
    public void ExtendingSelectionUpward_AnnouncesTheAnchorCell_NotSelectedRangeTopLeftCorner()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var grid = new GridView
            {
                Viewport = new ViewportModel(
                    [
                        new DisplayCell(1, 3, new NumberValue(11), "11", null, StyleId.Default, null),
                        new DisplayCell(2, 3, new NumberValue(22), "22", null, StyleId.Default, null),
                        new DisplayCell(3, 3, new NumberValue(100), "100", null, StyleId.Default, null)
                    ],
                    [
                        new RowMetric(1, 20, 0),
                        new RowMetric(2, 20, 20),
                        new RowMetric(3, 20, 40)
                    ],
                    [
                        new ColMetric(3, 64, 0)
                    ])
            };

            // Click C3: it becomes both the selection and the active/anchor cell.
            grid.SelectedRange = new GridRange(
                new CellAddress(sheetId, 3, 3), new CellAddress(sheetId, 3, 3));
            grid.ActiveCell = new CellAddress(sheetId, 3, 3);

            var peer = UIElementAutomationPeer.CreatePeerForElement(grid);

            // Shift+Up twice extends the selection to C1:C3. The host (MainWindow.Selection.cs
            // ExtendSelection) keeps the anchor at C3 and normalizes SelectedRange.Start to the
            // top-left corner C1 — but ActiveCell must stay at the real anchor, C3.
            grid.SelectedRange = new GridRange(
                new CellAddress(sheetId, 1, 3), new CellAddress(sheetId, 3, 3));

            var children = peer.GetChildren();
            var c1Peer = children.Single(c =>
                c.GetPattern(PatternInterface.GridItem) is IGridItemProvider { Row: 0, Column: 0 });
            var c3Peer = children.Single(c =>
                c.GetPattern(PatternInterface.GridItem) is IGridItemProvider { Row: 2, Column: 0 });

            c3Peer.HasKeyboardFocus().Should().BeTrue(
                because: "C3 is the anchor a screen-reader user actually clicked — and where F2/typing edits");
            c1Peer.HasKeyboardFocus().Should().BeFalse(
                because: "C1 is only SelectedRange's normalized top-left corner, not the active cell");
            c3Peer.GetName().Should().Be("C3: 100",
                because: "the announced address and value must be the anchor cell's, not C1's");
        });
    }
}
