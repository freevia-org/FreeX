using FluentAssertions;
using FreeX.App.Presentation.Comments;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Comments;

public sealed class ThreadedCommentCardPlannerTests
{
    private static readonly DateTimeOffset Created = new(2026, 6, 2, 10, 0, 0, TimeSpan.Zero);

    private static (Sheet Sheet, CellAddress Address) CreateSheetWithThread(bool resolved = false)
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 2, 3);
        sheet.ThreadedComments[address] = new ThreadedComment("Root text", "Quinn")
        {
            CreatedAtUtc = Created,
            IsResolved = resolved,
            Replies =
            [
                new CommentReply("First reply", "Bob") { CreatedAtUtc = Created.AddMinutes(5) },
                new CommentReply("Second reply", "Quinn") { CreatedAtUtc = Created.AddMinutes(9) }
            ]
        };
        return (sheet, address);
    }

    [Fact]
    public void BuildCard_ListsRootAndRepliesWithAuthorTimestampAndText()
    {
        var (sheet, address) = CreateSheetWithThread();

        var card = ThreadedCommentCardPlanner.BuildCard(sheet, address, Created.AddMinutes(30))!;

        card.CellReference.Should().Be("C2");
        card.Messages.Select(m => m.ReplyIndex).Should().Equal(-1, 0, 1);
        card.Messages[0].IsRoot.Should().BeTrue();
        card.Messages[0].Author.Should().Be("Quinn");
        card.Messages[0].Text.Should().Be("Root text");
        card.Messages[0].Heading.Should().StartWith("Quinn - ");
        card.Messages[1].Text.Should().Be("First reply");
        card.CanReply.Should().BeTrue();
        card.CanEditRoot.Should().BeTrue();
        card.CanDeleteThread.Should().BeTrue();
        card.ToggleResolvedAction.Should().Be(ThreadedCommentCardAction.Resolve);
        card.ToggleResolvedLabel.ResourceKey.Should().Be("ThreadedCommentCard_ResolveThread");
        card.StatusText.Should().BeNull();
        card.ReadOnlyNotice.Should().BeNull();
    }

    [Fact]
    public void BuildCard_ResolvedThreadOffersReopenAndBlocksReplies()
    {
        var (sheet, address) = CreateSheetWithThread(resolved: true);

        var card = ThreadedCommentCardPlanner.BuildCard(sheet, address)!;

        card.IsResolved.Should().BeTrue();
        card.CanReply.Should().BeFalse();
        card.ToggleResolvedAction.Should().Be(ThreadedCommentCardAction.Reopen);
        card.ToggleResolvedLabel.ResourceKey.Should().Be("ThreadedCommentCard_ReopenThread");
        card.StatusText!.ResourceKey.Should().Be("ThreadedCommentCard_ResolvedStatus");
    }

    [Fact]
    public void BuildCard_NoThread_ReturnsNull()
    {
        var (sheet, _) = CreateSheetWithThread();

        ThreadedCommentCardPlanner.BuildCard(sheet, new CellAddress(sheet.Id, 9, 9)).Should().BeNull();
    }

    [Fact]
    public void BuildCard_ProtectedSheetWithoutEditObjects_IsReadOnly()
    {
        var (sheet, address) = CreateSheetWithThread();
        sheet.IsProtected = true;

        var card = ThreadedCommentCardPlanner.BuildCard(sheet, address)!;

        card.CanReply.Should().BeFalse();
        card.CanEditRoot.Should().BeFalse();
        card.CanDeleteThread.Should().BeFalse();
        card.CanToggleResolved.Should().BeFalse();
        card.Messages.Should().OnlyContain(m => !m.CanEdit && !m.CanDelete);
        card.ReadOnlyNotice!.ResourceKey.Should().Be("ThreadedCommentCard_ProtectedNotice");
    }

    [Fact]
    public void BuildCard_ProtectedSheetWithEditObjects_StaysEditable()
    {
        var (sheet, address) = CreateSheetWithThread();
        sheet.IsProtected = true;
        sheet.ProtectionPermissions.Add(SheetProtectionPermission.EditObjects);

        var card = ThreadedCommentCardPlanner.BuildCard(sheet, address)!;

        card.CanReply.Should().BeTrue();
        card.CanDeleteThread.Should().BeTrue();
        card.ReadOnlyNotice.Should().BeNull();
    }

    [Fact]
    public void BuildPane_CountsAndFiltersByResolvedState()
    {
        var (sheet, address) = CreateSheetWithThread(resolved: true);
        var second = new CellAddress(sheet.Id, 1, 1);
        sheet.ThreadedComments[second] = new ThreadedComment("Open one", "Ann");

        var all = ThreadedCommentCardPlanner.BuildPane(sheet);
        var active = ThreadedCommentCardPlanner.BuildPane(sheet, ThreadedCommentPaneFilter.Active);
        var resolved = ThreadedCommentCardPlanner.BuildPane(sheet, ThreadedCommentPaneFilter.Resolved);

        all.Rows.Select(r => r.Address).Should().Equal(second, address);
        all.ActiveCount.Should().Be(1);
        all.ResolvedCount.Should().Be(1);
        all.TotalCount.Should().Be(2);
        active.Rows.Select(r => r.Address).Should().Equal(second);
        resolved.Rows.Select(r => r.Address).Should().Equal(address);
        resolved.Rows[0].ReplyCount.Should().Be(2);
        resolved.Rows[0].ToggleResolvedLabel.ResourceKey.Should().Be("ThreadedCommentCard_ReopenThread");
    }

    [Fact]
    public void PlanAction_ResolveReopenDeleteReply_ProduceUndoableCommands()
    {
        var (sheet, address) = CreateSheetWithThread();
        var service = new PresentationCommentMutationService();
        var thread = sheet.ThreadedComments[address];
        var range = new GridRange(address, address);

        var resolve = ThreadedCommentCardPlanner.PlanAction(service, sheet.Id, thread, ThreadedCommentCardAction.Resolve, "Me");
        resolve!.CreateCommand(range).Should().BeOfType<ResolveThreadedCommentCommand>();
        ThreadedCommentCardPlanner.PlanAction(service, sheet.Id, thread, ThreadedCommentCardAction.Reopen, "Me")
            .Should().BeNull("an open thread cannot be reopened");

        var reply = ThreadedCommentCardPlanner.PlanAction(
            service, sheet.Id, thread, ThreadedCommentCardAction.AddReply, "Me", "Thanks");
        reply!.CreateCommand(range).Should().BeOfType<ApplyThreadedCommentChangesCommand>();
        ThreadedCommentCardPlanner.PlanAction(
            service, sheet.Id, thread, ThreadedCommentCardAction.AddReply, "Me", "  ").Should().BeNull();

        ThreadedCommentCardPlanner.PlanAction(
                service, sheet.Id, thread, ThreadedCommentCardAction.DeleteReply, "Me", replyIndex: 1)!
            .CreateCommand(range).Should().BeOfType<DeleteThreadedCommentReplyCommand>();
        ThreadedCommentCardPlanner.PlanAction(
                service, sheet.Id, thread, ThreadedCommentCardAction.DeleteReply, "Me", replyIndex: 7)
            .Should().BeNull();
        ThreadedCommentCardPlanner.PlanAction(
                service, sheet.Id, thread, ThreadedCommentCardAction.EditReply, "Me", "Edited", 0)!
            .CreateCommand(range).Should().BeOfType<UpdateThreadedCommentReplyCommand>();
        ThreadedCommentCardPlanner.PlanAction(
                service, sheet.Id, thread, ThreadedCommentCardAction.EditRoot, "Me", "Root text")
            .Should().BeNull("unchanged root text is not a mutation");
        ThreadedCommentCardPlanner.PlanAction(
                service, sheet.Id, thread, ThreadedCommentCardAction.DeleteThread, "Me")!
            .CreateCommand(range).Should().BeOfType<DeleteThreadedCommentCommand>();
    }

    [Fact]
    public void DialogResultDeleteThread_MapsToDeleteThreadedCommentCommand()
    {
        var (sheet, address) = CreateSheetWithThread();
        var service = new PresentationCommentMutationService();

        var plan = service.PlanThreadedComment(
            sheet.Id,
            sheet.ThreadedComments[address],
            new ThreadedCommentDialogResult(null, null, false, ThreadedCommentDialogAction.DeleteThread),
            "Me");

        plan!.CreateCommand(new GridRange(address, address)).Should().BeOfType<DeleteThreadedCommentCommand>();
    }
}
