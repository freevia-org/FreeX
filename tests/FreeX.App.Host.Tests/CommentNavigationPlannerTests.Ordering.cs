using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class CommentNavigationPlannerTests
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
    public void OrderedThreadedComments_SortsByRowThenColumnWithoutNotes()
    {
        var sheetId = SheetId.New();
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [new(sheetId, 4, 1)] = new("Later"),
            [new(sheetId, 2, 3)] = new("Middle"),
            [new(sheetId, 2, 1)] = new("First")
        };

        CommentNavigationPlanner.OrderedThreadedCommentAddresses(threadedComments)
            .Should()
            .Equal(new CellAddress(sheetId, 2, 1), new CellAddress(sheetId, 2, 3), new CellAddress(sheetId, 4, 1));
    }

    [Fact]
    public void OrderedNotes_SortsByRowThenColumnWithoutThreadedComments()
    {
        var sheetId = SheetId.New();
        var notes = new Dictionary<CellAddress, string>
        {
            [new(sheetId, 4, 1)] = "Later",
            [new(sheetId, 2, 3)] = "Middle",
            [new(sheetId, 2, 1)] = "First"
        };

        CommentNavigationPlanner.OrderedNoteAddresses(notes)
            .Should()
            .Equal(new CellAddress(sheetId, 2, 1), new CellAddress(sheetId, 2, 3), new CellAddress(sheetId, 4, 1));
    }
}
