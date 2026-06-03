using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class CommentNavigationPlannerTests
{
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
            .Be(string.Join(Environment.NewLine, "A1: FreeX: First thread", "B3: Later note"));
    }

    [Fact]
    public void FormatThreadedCommentList_ExcludesNotes()
    {
        var sheetId = SheetId.New();
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [new(sheetId, 1, 1)] = new("First thread"),
            [new(sheetId, 3, 2)] = new("Later thread", "Quinn")
        };

        CommentNavigationPlanner.FormatThreadedCommentList(threadedComments)
            .Should()
            .Be(string.Join(Environment.NewLine, "A1: FreeX: First thread", "B3: Quinn: Later thread"));
    }

    [Fact]
    public void FormatNoteList_ExcludesThreadedComments()
    {
        var sheetId = SheetId.New();
        var notes = new Dictionary<CellAddress, string>
        {
            [new(sheetId, 3, 2)] = "Later note",
            [new(sheetId, 1, 1)] = "First note"
        };

        CommentNavigationPlanner.FormatNoteList(notes)
            .Should()
            .Be(string.Join(Environment.NewLine, "A1: First note", "B3: Later note"));
    }

    [Fact]
    public void FormatCommentList_IncludesThreadedAuthorsRepliesAndResolvedState()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 1, 1);
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [address] = new("Please review total", "Quinn")
            {
                Replies =
                [
                    new CommentReply("Updated", "Codex"),
                    new CommentReply("Looks good", "Quinn")
                ],
                IsResolved = true
            }
        };

        CommentNavigationPlanner.FormatCommentList(new Dictionary<CellAddress, string>(), threadedComments)
            .Should()
            .Be("A1: Quinn: Please review total | Codex: Updated | Quinn: Looks good | Resolved");
    }

    [Fact]
    public void FormatCommentList_IncludesThreadedCreatedTimestampsWhenAvailable()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 1, 1);
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [address] = new("Please review total", "Quinn")
            {
                CreatedAtUtc = new DateTimeOffset(2026, 5, 31, 8, 0, 0, TimeSpan.Zero),
                Replies =
                [
                    new CommentReply("Updated", "Codex")
                    {
                        CreatedAtUtc = new DateTimeOffset(2026, 5, 31, 8, 5, 0, TimeSpan.Zero)
                    }
                ]
            }
        };

        CommentNavigationPlanner.FormatCommentList(new Dictionary<CellAddress, string>(), threadedComments)
            .Should()
            .Be("A1: Quinn (2026-05-31 08:00 UTC): Please review total | Codex (2026-05-31 08:05 UTC): Updated");
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
            [address] = new("Threaded reply", "Codex")
        };

        CommentNavigationPlanner.FormatCommentList(comments, threadedComments)
            .Should()
            .Be(string.Join(Environment.NewLine, "B2: Note: Local note", "B2: Threaded: Codex: Threaded reply"));
    }
}
