using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public partial class CommentCommandTests
{
    // -----------------------------------------------------------------------
    // ConvertNotesToCommentsCommand.Revert must restore a genuine author that
    // happens to equal the literal sentinel "FreeX" used as the default
    // fallback author when no author was recorded (H4).
    // -----------------------------------------------------------------------

    [Fact]
    public void ConvertNotesToCommentsCommand_Undo_RestoresGenuineAuthorNamedFreeX()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[addr] = "Legacy note with a real author literally named FreeX";
        sheet.CommentAuthors[addr] = "FreeX";

        var cmd = new ConvertNotesToCommentsCommand(sheet.Id, timestampUtc: CreatedAtUtc);
        var outcome = cmd.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.ThreadedComments[addr].Author.Should().Be("FreeX");

        cmd.Revert(ctx);

        // The genuine author must be restored, not dropped.
        sheet.CommentAuthors.Should().ContainKey(addr);
        sheet.CommentAuthors[addr].Should().Be("FreeX");
        sheet.Comments.Should().ContainKey(addr);
        sheet.Comments[addr].Should().Be("Legacy note with a real author literally named FreeX");
        sheet.ThreadedComments.Should().NotContainKey(addr);
    }

    [Fact]
    public void ConvertNotesToCommentsCommand_Undo_DoesNotFabricateAuthorWhenNoneExistedOriginally()
    {
        var (_, sheet, ctx) = Setup();
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[addr] = "Note without any recorded author";
        // Deliberately do NOT set CommentAuthors[addr] — Apply will default it to "FreeX".

        var cmd = new ConvertNotesToCommentsCommand(sheet.Id, timestampUtc: CreatedAtUtc);
        var outcome = cmd.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);

        sheet.ThreadedComments[addr].Author.Should().Be("FreeX");

        cmd.Revert(ctx);

        // No author was ever recorded pre-Apply, so Revert must not invent one.
        sheet.CommentAuthors.Should().NotContainKey(addr);
        sheet.Comments.Should().ContainKey(addr);
        sheet.Comments[addr].Should().Be("Note without any recorded author");
    }
}
