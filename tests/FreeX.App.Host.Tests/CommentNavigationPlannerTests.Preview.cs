using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class CommentNavigationPlannerTests
{
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

    [Fact]
    public void FormatCellCommentPreview_ShowsNotesAndThreadedCommentsForHoveredCell()
    {
        var sheetId = SheetId.New();
        var address = new CellAddress(sheetId, 2, 2);
        var comments = new Dictionary<CellAddress, string>
        {
            [address] = "Local note"
        };
        var threadedComments = new Dictionary<CellAddress, ThreadedComment>
        {
            [address] = new("Please review total", "Quinn")
            {
                Replies = [new CommentReply("Updated", "Codex")]
            }
        };

        CommentNavigationPlanner.FormatCellCommentPreview(comments, threadedComments, address)
            .Should()
            .Be(string.Join(Environment.NewLine, "Note: Local note", "Quinn: Please review total | Codex: Updated"));
        CommentNavigationPlanner.FormatCellCommentPreview(comments, threadedComments, new CellAddress(sheetId, 3, 3))
            .Should()
            .BeNull();
    }
}
