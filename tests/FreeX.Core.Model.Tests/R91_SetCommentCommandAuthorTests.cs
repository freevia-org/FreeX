using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R91-render-comment-ui-5-3: creating a brand-new legacy note via <see cref="SetCommentCommand"/>
/// (the sole production path for WorkbookSession.SetActiveCellNote, i.e. the GridView inline note
/// editor) must auto-attribute an author, matching Excel's own auto-filled <c>&lt;authors&gt;</c>
/// entry for a freshly inserted note. Before the fix, sheet.CommentAuthors was never written by
/// this command, so the Notes list and the printed "at end of sheet" comment summary both showed
/// a blank author for every note the user created inside FreeX.
/// </summary>
public sealed class R91_SetCommentCommandAuthorTests
{
    [Fact]
    public void SetComment_NewNoteIsAutoAttributedToDefaultAuthor()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = new SetCommentCommand(sheet.Id, address, "Follow up with finance");

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Comments[address].Should().Be("Follow up with finance");
        sheet.CommentAuthors.Should().ContainKey(address);
        sheet.CommentAuthors[address].Should().Be("FreeX");

        // Undo must remove both the note text and the auto-attributed author it created.
        command.Revert(ctx);
        sheet.Comments.ContainsKey(address).Should().BeFalse();
        sheet.CommentAuthors.ContainsKey(address).Should().BeFalse();
    }

    [Fact]
    public void SetComment_EditingExistingNoteDoesNotOverwriteItsRecordedAuthor()
    {
        // No-regression sibling: replacing an EXISTING note's text (e.g. re-editing a note that
        // round-tripped in with its own author, or that a prior SetComment call already
        // attributed) must leave the already-recorded author untouched -- only a genuinely NEW
        // note gets auto-attributed.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[address] = "original text";
        sheet.CommentAuthors[address] = "Original Author";

        var command = new SetCommentCommand(sheet.Id, address, "edited text");

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Comments[address].Should().Be("edited text");
        sheet.CommentAuthors[address].Should().Be("Original Author", "editing an existing note must not reassign its author");

        command.Revert(ctx);
        sheet.Comments[address].Should().Be("original text");
        sheet.CommentAuthors[address].Should().Be("Original Author");
    }
}
