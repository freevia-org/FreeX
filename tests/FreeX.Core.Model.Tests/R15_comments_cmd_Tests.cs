using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R15-comments-threading-ui-1: DeleteCommentCommand and ClearCommentsCommand must treat
/// Comments, CommentAuthors, and ShownComments as companion collections (matching
/// ConvertNotesToCommentsCommand and the other sibling commands), so a deleted/cleared note's
/// address does not leave stale author attribution or a stale pinned-open state behind for a
/// later note added at the same address.
/// </summary>
public partial class CommentCommandTests
{
    [Fact]
    public void DeleteCommentCommand_RemovesAuthorAndShownState_AndUndoRestoresAll()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);

        // Add a note authored by Alice and pin it open (ShownComments).
        sheet.Comments[addr] = "Please review";
        sheet.CommentAuthors[addr] = "Alice";
        sheet.ShownComments.Add(addr);

        var cmd = new DeleteCommentCommand(sheet.Id, addr);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Comments.Should().NotContainKey(addr);
        sheet.CommentAuthors.Should().NotContainKey(addr);
        sheet.ShownComments.Should().NotContain(addr);

        // Re-adding a note at the same address must not resurrect the stale author or pinned state.
        sheet.Comments[addr] = "New note";
        sheet.CommentAuthors.Should().NotContainKey(addr);
        sheet.ShownComments.Should().NotContain(addr);
        sheet.Comments.Remove(addr);

        cmd.Revert(ctx);

        sheet.Comments[addr].Should().Be("Please review");
        sheet.CommentAuthors[addr].Should().Be("Alice");
        sheet.ShownComments.Should().Contain(addr);
    }

    [Fact]
    public void ClearCommentsCommand_RemovesAuthorsAndShownState_AndUndoRestoresAll()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        var other = new CellAddress(sheet.Id, 2, 1);

        sheet.Comments[addr] = "Please review";
        sheet.CommentAuthors[addr] = "Alice";
        sheet.ShownComments.Add(addr);

        // A second cell outside the cleared range should be unaffected.
        sheet.Comments[other] = "Untouched";
        sheet.CommentAuthors[other] = "Bob";
        sheet.ShownComments.Add(other);

        var range = new GridRange(addr, addr);
        var cmd = new ClearCommentsCommand(sheet.Id, range);
        var outcome = cmd.Apply(ctx);

        outcome.Success.Should().BeTrue();
        sheet.Comments.Should().NotContainKey(addr);
        sheet.CommentAuthors.Should().NotContainKey(addr);
        sheet.ShownComments.Should().NotContain(addr);

        // Unrelated cell outside the range is untouched.
        sheet.CommentAuthors[other].Should().Be("Bob");
        sheet.ShownComments.Should().Contain(other);

        // Re-adding a note at the cleared address must not resurrect stale author/pinned state.
        sheet.Comments[addr] = "New note";
        sheet.CommentAuthors.Should().NotContainKey(addr);
        sheet.ShownComments.Should().NotContain(addr);
        sheet.Comments.Remove(addr);

        cmd.Revert(ctx);

        sheet.Comments[addr].Should().Be("Please review");
        sheet.CommentAuthors[addr].Should().Be("Alice");
        sheet.ShownComments.Should().Contain(addr);
    }
}
