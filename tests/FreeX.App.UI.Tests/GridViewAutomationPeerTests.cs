using System.Threading;
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
        RunOnStaThread(() =>
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

    private static void RunOnStaThread(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            throw failure;
    }
}
