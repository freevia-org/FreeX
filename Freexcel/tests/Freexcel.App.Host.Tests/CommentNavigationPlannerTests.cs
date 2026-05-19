using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class CommentNavigationPlannerTests
{
    [Fact]
    public void OrderedComments_SortsByRowThenColumn()
    {
        var sheetId = SheetId.New();
        var comments = new Dictionary<CellAddress, string>
        {
            [new(sheetId, 4, 1)] = "C",
            [new(sheetId, 2, 3)] = "B",
            [new(sheetId, 2, 1)] = "A"
        };

        CommentNavigationPlanner.OrderedCommentAddresses(comments)
            .Should()
            .Equal(new CellAddress(sheetId, 2, 1), new CellAddress(sheetId, 2, 3), new CellAddress(sheetId, 4, 1));
    }

    [Fact]
    public void NextComment_WrapsForwardAndBackward()
    {
        var sheetId = SheetId.New();
        var comments = new[]
        {
            new CellAddress(sheetId, 1, 1),
            new CellAddress(sheetId, 2, 1),
            new CellAddress(sheetId, 4, 1)
        };

        CommentNavigationPlanner.FindNext(comments, new CellAddress(sheetId, 2, 1), previous: false)
            .Should()
            .Be(new CellAddress(sheetId, 4, 1));
        CommentNavigationPlanner.FindNext(comments, new CellAddress(sheetId, 4, 1), previous: false)
            .Should()
            .Be(new CellAddress(sheetId, 1, 1));
        CommentNavigationPlanner.FindNext(comments, new CellAddress(sheetId, 1, 1), previous: true)
            .Should()
            .Be(new CellAddress(sheetId, 4, 1));
    }

    [Fact]
    public void FormatCommentList_UsesA1AddressesInSortedOrder()
    {
        var sheetId = SheetId.New();
        var comments = new Dictionary<CellAddress, string>
        {
            [new(sheetId, 3, 2)] = "Later",
            [new(sheetId, 1, 1)] = "First"
        };

        CommentNavigationPlanner.FormatCommentList(comments)
            .Should()
            .Be(string.Join(Environment.NewLine, "A1: First", "B3: Later"));
    }
}
