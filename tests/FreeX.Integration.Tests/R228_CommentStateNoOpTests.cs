using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// r228: the comment-state family, plus the Selection pane's visibility setter. Four of these write
/// a fresh timestamp over a record whose user-visible fields are unchanged -- opening a comment and
/// pressing Save without typing, or resolving a thread that is already resolved.
/// <para>
/// The judgement is worth stating rather than assuming, because "nothing changed" is not quite true
/// of a timestamp. Two things settle it. The timestamp helper for the root edit is called
/// <c>TouchRootTextEdit</c>, so stamping it when no text was edited is wrong on the helper's own
/// terms rather than merely wasteful. And both Update commands were ALREADY computing the text
/// equality one line further down, to decide whether the preserved @mention metadata still points
/// at valid offsets -- so each knew whether the text had changed and wrote the new timestamp anyway.
/// </para>
/// <para>
/// The two commands in the same files that are NOT fixed are the genuine toggles: ShowHideComment
/// and ShowAllNotes read the current state and flip it, so they have no same-value path to guard.
/// The distinction is the point -- a toggle is self-guaranteeing, a setter takes the target state as
/// an argument and can be handed the one already in place.
/// </para>
/// </summary>
public sealed class R228_CommentStateNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx) Fixture()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        return (sheet, new TestCommandContext(workbook));
    }

    private static CellAddress Address(Sheet sheet) => new(sheet.Id, 2, 2);

    private static ThreadedComment Thread(Sheet sheet, string text, bool resolved = false)
    {
        var comment = new ThreadedComment(text, "A") { IsResolved = resolved };
        sheet.ThreadedComments[Address(sheet)] = comment;
        return comment;
    }

    [Fact]
    public void SavingACommentWithoutChangingItsText_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        Thread(sheet, "Please review");

        new UpdateThreadedCommentTextCommand(sheet.Id, Address(sheet), "Please review").Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void SavingACommentWithoutChangingItsText_LeavesTheRootTextEditTimestampAlone()
    {
        var (sheet, ctx) = Fixture();
        var before = Thread(sheet, "Please review");
        var stamp = before.RootTextEditedAtUtc;

        new UpdateThreadedCommentTextCommand(sheet.Id, Address(sheet), "Please review").Apply(ctx);

        sheet.ThreadedComments[Address(sheet)].RootTextEditedAtUtc.Should().Be(stamp);
        // RootTextEditedAtUtc is documented on the model as "the UTC time the ROOT comment.s own
        // text was last GENUINELY edited". Stamping it for a save that changed no text contradicts
        // the field.s own stated meaning, which is what settles this as a no-op rather than a
        // judgement call about whether a timestamp counts as a change.
    }

    [Fact]
    public void EditingACommentsText_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        Thread(sheet, "Please review");

        var outcome = new UpdateThreadedCommentTextCommand(sheet.Id, Address(sheet), "Reviewed")
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.ThreadedComments[Address(sheet)].Text.Should().Be("Reviewed");
    }

    [Fact]
    public void ResolvingAThreadThatIsAlreadyResolved_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        Thread(sheet, "Please review", resolved: true);

        new ResolveThreadedCommentCommand(sheet.Id, Address(sheet), resolved: true).Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ResolvingAnOpenThread_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        Thread(sheet, "Please review");

        var outcome = new ResolveThreadedCommentCommand(sheet.Id, Address(sheet), resolved: true)
            .Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        sheet.ThreadedComments[Address(sheet)].IsResolved.Should().BeTrue();
    }

    [Fact]
    public void HidingAnObjectThatIsAlreadyHidden_ReportsNoOp()
    {
        var (sheet, ctx) = Fixture();
        var picture = new PictureModel { Anchor = Address(sheet), IsVisible = false };
        sheet.Pictures.Add(picture);

        new SetSelectionPaneObjectVisibilityCommand(
                sheet.Id, SelectionPaneObjectKind.Picture, picture.Id, isVisible: false)
            .Apply(ctx)
            .IsNoOp.Should().BeTrue();
    }

    [Fact]
    public void ShowingAHiddenObject_DoesNotReportNoOp()
    {
        var (sheet, ctx) = Fixture();
        var picture = new PictureModel { Anchor = Address(sheet), IsVisible = false };
        sheet.Pictures.Add(picture);

        var outcome = new SetSelectionPaneObjectVisibilityCommand(
            sheet.Id, SelectionPaneObjectKind.Picture, picture.Id, isVisible: true).Apply(ctx);

        outcome.IsNoOp.Should().BeFalse();
        picture.IsVisible.Should().BeTrue();
    }

    [Fact]
    public void TheShowHideToggleIsNeverANoOp()
    {
        // Not a fix -- a contrast. This one reads the current state and flips it, so there is no
        // argument that could ask it to do what is already done.
        var (sheet, ctx) = Fixture();
        sheet.Comments[Address(sheet)] = "A note";

        new ShowHideCommentCommand(sheet.Id, Address(sheet)).Apply(ctx).IsNoOp.Should().BeFalse();
        sheet.ShownComments.Should().Contain(Address(sheet));

        new ShowHideCommentCommand(sheet.Id, Address(sheet)).Apply(ctx).IsNoOp.Should().BeFalse();
        sheet.ShownComments.Should().NotContain(Address(sheet));
    }
}
