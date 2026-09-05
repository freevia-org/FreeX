using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r455: showing or hiding a note must honour "Edit objects", like every other command that touches
/// a note.
///
/// <para>Found by driving every constructible command against a fully protected sheet. Of 66 commands
/// that change an unprotected sheet, 27 still changed a protected one -- and 25 of those are correct:
/// Excel's sheet protection governs cell content and objects, not workbook structure, view settings,
/// page setup or protection management itself. FreeX's <c>SheetProtectionPermission</c> enum is
/// exactly Excel's fifteen Protect Sheet permissions, so anything outside it is outside the model by
/// design.</para>
///
/// <para>The two that were wrong are these. A note's pinned state is PERSISTED -- <c>ShownComments</c>
/// is written to the xlsx VML and to the native JSON and read back on load -- so showing or hiding
/// one durably changes the document. <see cref="SetCommentCommand"/> and
/// <see cref="DeleteCommentCommand"/>, in the same file and touching the same objects, already
/// refused when "Edit objects" was withheld. These two did not, so a protected sheet could still be
/// permanently altered through the one comment command that skipped the guard.</para>
/// </summary>
public sealed class R455_CommentVisibilityHonoursSheetProtectionTests
{
    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Context) Setup(
        bool isProtected,
        bool grantEditObjects = false)
    {
        var workbook = new Workbook("protection");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("value"));
        sheet.Comments[new CellAddress(sheet.Id, 1, 1)] = "a note";
        sheet.Comments[new CellAddress(sheet.Id, 2, 1)] = "another note";

        if (isProtected)
        {
            sheet.IsProtected = true;
            sheet.ProtectionPermissions.Clear();
            if (grantEditObjects)
                sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);
        }

        return (workbook, sheet, new TestCommandContext(workbook));
    }

    [Fact]
    public void ShowHideIsRefusedOnAProtectedSheet()
    {
        var (_, sheet, context) = Setup(isProtected: true);
        var address = new CellAddress(sheet.Id, 1, 1);

        var outcome = new ShowHideCommentCommand(sheet.Id, address).Apply(context);

        outcome.Success.Should().BeFalse(
            "pinning a note is a persisted change to the document, so a protected sheet must refuse it");
        sheet.ShownComments.Should().NotContain(address, "and nothing may have changed");
    }

    [Fact]
    public void ShowAllNotesIsRefusedOnAProtectedSheet()
    {
        var (_, sheet, context) = Setup(isProtected: true);

        var outcome = new ShowAllNotesCommand(sheet.Id).Apply(context);

        outcome.Success.Should().BeFalse();
        sheet.ShownComments.Should().BeEmpty();
    }

    [Fact]
    public void ARefusedShowAllLeavesNoUndoStateBehind()
    {
        // The guard sits BEFORE the undo snapshot deliberately. A command refused after snapshotting
        // would leave a Revert that could later restore state the user never reached.
        var (_, sheet, context) = Setup(isProtected: true);
        var command = new ShowAllNotesCommand(sheet.Id);

        command.Apply(context).Success.Should().BeFalse();
        command.Revert(context);

        sheet.ShownComments.Should().BeEmpty("undoing a command that never applied must change nothing");
    }

    [Fact]
    public void ShowHideIsAllowedWhenEditObjectsIsGranted()
    {
        // Narrowness: protection is a permission model, not a blanket ban. An author who granted
        // "Edit objects" must still be able to pin a note.
        var (_, sheet, context) = Setup(isProtected: true, grantEditObjects: true);
        var address = new CellAddress(sheet.Id, 1, 1);

        var outcome = new ShowHideCommentCommand(sheet.Id, address).Apply(context);

        outcome.Success.Should().BeTrue("the permission the author granted must be honoured");
        sheet.ShownComments.Should().Contain(address);
    }

    [Fact]
    public void ShowHideIsUnaffectedOnAnUnprotectedSheet()
    {
        var (_, sheet, context) = Setup(isProtected: false);
        var address = new CellAddress(sheet.Id, 1, 1);

        new ShowHideCommentCommand(sheet.Id, address).Apply(context).Success.Should().BeTrue();

        sheet.ShownComments.Should().Contain(address, "the ordinary path must not change");
    }

    [Fact]
    public void ItMatchesHowItsSiblingsAlreadyBehaved()
    {
        // The finding was an inconsistency, so this pins the consistency rather than each command
        // separately: every comment command refuses the same protected sheet.
        var (_, sheet, context) = Setup(isProtected: true);
        var address = new CellAddress(sheet.Id, 1, 1);

        new SetCommentCommand(sheet.Id, address, "edited").Apply(context).Success.Should().BeFalse();
        new DeleteCommentCommand(sheet.Id, address).Apply(context).Success.Should().BeFalse();
        new ShowHideCommentCommand(sheet.Id, address).Apply(context).Success.Should().BeFalse();
        new ShowAllNotesCommand(sheet.Id).Apply(context).Success.Should().BeFalse();
    }
}
