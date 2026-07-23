using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;

namespace FreeX.App.UI.Tests;

public sealed class GridViewAutomationPeerTests
{
    [Fact]
    public void GridViewAutomationPeer_ExposesVisibleCellsAsGridItemsWithValuesAndSelection()
    {
        WpfTestThread.Run(() =>
        {
            var sheetId = SheetId.New();
            var grid = new GridView
            {
                Viewport = new ViewportModel(
                    [
                        new DisplayCell(1, 1, new NumberValue(42), "42", null, StyleId.Default, null),
                        new DisplayCell(1, 2, new TextValue("East"), "East", null, StyleId.Default, null),
                    ],
                    [
                        new RowMetric(1, 20, 0),
                        new RowMetric(2, 20, 20)
                    ],
                    [
                        new ColMetric(1, 64, 0),
                        new ColMetric(2, 64, 64)
                    ]),
                SelectedRange = new GridRange(
                    new CellAddress(sheetId, 1, 1),
                    new CellAddress(sheetId, 1, 2))
            };

            var peer = UIElementAutomationPeer.CreatePeerForElement(grid);
            var gridProvider = peer.GetPattern(PatternInterface.Grid).Should().BeAssignableTo<IGridProvider>().Subject;
            var selectionProvider = peer.GetPattern(PatternInterface.Selection).Should().BeAssignableTo<ISelectionProvider>().Subject;

            gridProvider.RowCount.Should().Be(2);
            gridProvider.ColumnCount.Should().Be(2);
            selectionProvider.GetSelection().Should().HaveCount(2);

            var children = peer.GetChildren();
            children.Should().HaveCount(4);

            var firstCell = children[0];
            var gridItem = firstCell.GetPattern(PatternInterface.GridItem).Should().BeAssignableTo<IGridItemProvider>().Subject;
            var value = firstCell.GetPattern(PatternInterface.Value).Should().BeAssignableTo<IValueProvider>().Subject;
            var selectionItem = firstCell.GetPattern(PatternInterface.SelectionItem).Should().BeAssignableTo<ISelectionItemProvider>().Subject;

            gridItem.Row.Should().Be(0);
            gridItem.Column.Should().Be(0);
            value.Value.Should().Be("42");
            selectionItem.IsSelected.Should().BeTrue();
        });
    }

    [Fact]
    public void GridViewCellAutomationPeer_AnnouncesCommentPresenceInNameAndHelpText()
    {
        // R80-app-accessibility-a11y-5-3: a screen reader must be told a cell carries a
        // note/comment (sighted users see a corner-triangle indicator) via the cell's
        // UIA Name/HelpText, not just its address and value.
        WpfTestThread.Run(() =>
        {
            var grid = new GridView
            {
                Viewport = new ViewportModel(
                    [
                        new DisplayCell(
                            1, 1, new NumberValue(42), "42", null, StyleId.Default, null,
                            HasComment: true,
                            CommentDisplay: new CellCommentDisplay(
                                CellCommentDisplayKind.Note, "Note", "Double-check this figure.")),
                    ],
                    [new RowMetric(1, 20, 0)],
                    [new ColMetric(1, 64, 0)])
            };

            var peer = UIElementAutomationPeer.CreatePeerForElement(grid);
            var cellPeer = peer.GetChildren()[0];

            cellPeer.GetName().Should().Be("A1: 42, has note");
            cellPeer.GetHelpText().Should().Be("Note: Double-check this figure.");
        });
    }

    [Fact]
    public void GridViewCellAutomationPeer_OmitsCommentCueWhenCellHasNoComment()
    {
        // No-regression sibling: a plain cell (no note/comment) keeps the original
        // address/address:value Name and an empty HelpText.
        WpfTestThread.Run(() =>
        {
            var grid = new GridView
            {
                Viewport = new ViewportModel(
                    [
                        new DisplayCell(1, 1, new NumberValue(42), "42", null, StyleId.Default, null),
                    ],
                    [new RowMetric(1, 20, 0)],
                    [new ColMetric(1, 64, 0)])
            };

            var peer = UIElementAutomationPeer.CreatePeerForElement(grid);
            var cellPeer = peer.GetChildren()[0];

            cellPeer.GetName().Should().Be("A1: 42");
            cellPeer.GetHelpText().Should().Be(string.Empty);
        });
    }
}
