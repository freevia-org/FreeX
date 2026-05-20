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
    public void OrderedComments_IncludesThreadedComments()
    {
        var sheetId = SheetId.New();
        var comments = new Dictionary<CellAddress, string>
        {
            [new(sheetId, 4, 1)] = "Note"
        };
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [new(sheetId, 2, 1)] = new("Thread"),
            [new(sheetId, 3, 2)] = new("Discussion")
        };

        CommentNavigationPlanner.OrderedCommentAddresses(comments, threadedComments)
            .Should()
            .Equal(new CellAddress(sheetId, 2, 1), new CellAddress(sheetId, 3, 2), new CellAddress(sheetId, 4, 1));
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

    [Fact]
    public void FormatCommentList_IncludesThreadedComments()
    {
        var sheetId = SheetId.New();
        var comments = new Dictionary<CellAddress, string>
        {
            [new(sheetId, 3, 2)] = "Later note"
        };
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [new(sheetId, 1, 1)] = new("First thread")
        };

        CommentNavigationPlanner.FormatCommentList(comments, threadedComments)
            .Should()
            .Be(string.Join(Environment.NewLine, "A1: First thread", "B3: Later note"));
    }

    [Fact]
    public void FormatCommentList_ShowsNoteAndThreadWhenCellHasBoth()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 2, 2);
        var comments = new Dictionary<CellAddress, string>
        {
            [address] = "Local note"
        };
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [address] = new("Threaded reply")
        };

        CommentNavigationPlanner.FormatCommentList(comments, threadedComments)
            .Should()
            .Be(string.Join(Environment.NewLine, "B2: Note: Local note", "B2: Threaded: Threaded reply"));
    }

    [Fact]
    public void GetDefaultCommentText_ReturnsExistingCommentForSelectedCell()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 2, 2);
        var comments = new Dictionary<CellAddress, string>
        {
            [address] = "Existing note"
        };

        CommentNavigationPlanner.GetDefaultCommentText(comments, address)
            .Should()
            .Be("Existing note");
        CommentNavigationPlanner.GetDefaultCommentText(comments, new CellAddress(sheetId, 3, 3))
            .Should()
            .BeEmpty();
    }
}
