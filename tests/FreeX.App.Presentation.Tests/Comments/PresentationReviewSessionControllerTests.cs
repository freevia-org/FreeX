using FluentAssertions;
using FreeX.App.Presentation.Comments;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Comments;

public sealed class PresentationReviewSessionControllerTests
{
    [Fact]
    public void NavigateThreadedComment_UsesSharedOrderingAndCreatesViewportRefreshPlan()
    {
        var workbook = CreateWorkbook(out var sheet);
        var earlier = new CellAddress(sheet.Id, 2, 2);
        var later = new CellAddress(sheet.Id, 4, 1);
        sheet.ThreadedComments[later] = new ThreadedComment("later");
        sheet.ThreadedComments[earlier] = new ThreadedComment("earlier");
        var adapter = new FakeAdapter(workbook, sheet.Id, new GridRange(earlier, earlier));
        var controller = new PresentationReviewSessionController(adapter);

        var result = controller.NavigateThreadedComment(previous: false);

        result.Success.Should().BeTrue();
        result.Target.Should().Be(later);
        adapter.SelectedRange.Should().Be(new GridRange(later, later));
        result.RefreshPlan.Should().Be(new PresentationReviewRefreshPlan(true, false, false, true));
    }

    [Fact]
    public void ApplyThreadedComment_DelegatesReplyAndResolutionPlanningToMutationService()
    {
        var workbook = CreateWorkbook(out var sheet);
        var address = new CellAddress(sheet.Id, 3, 3);
        sheet.ThreadedComments[address] = new ThreadedComment("root")
        {
            Replies = [new CommentReply("reply")]
        };
        var adapter = new FakeAdapter(workbook, sheet.Id, new GridRange(address, address));
        var controller = new PresentationReviewSessionController(adapter);

        var result = controller.ApplyThreadedComment(
            new ThreadedCommentDialogResult(
                RootText: "root",
                ReplyText: null,
                IsResolved: true,
                Action: ThreadedCommentDialogAction.EditReply,
                ReplyIndex: 0,
                ReplyEditText: "updated reply"));

        result.Success.Should().BeTrue();
        adapter.LastPlan.Should().NotBeNull();
        adapter.LastPlan!.Label.Should().Be("Edit Comment Reply");
        adapter.LastPlan.CreateCommand(adapter.SelectedRange!.Value)
            .Should().BeOfType<UpdateThreadedCommentReplyCommand>();
        result.RefreshPlan.Should().Be(new PresentationReviewRefreshPlan(true, true, true, false));
    }

    [Fact]
    public void DeleteNote_WhenSelectionHasNoNote_DoesNotAskAdapterToMutate()
    {
        var workbook = CreateWorkbook(out var sheet);
        var address = new CellAddress(sheet.Id, 1, 1);
        var adapter = new FakeAdapter(workbook, sheet.Id, new GridRange(address, address));
        var controller = new PresentationReviewSessionController(adapter);

        var result = controller.DeleteNote();

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("No note is selected.");
        adapter.LastPlan.Should().BeNull();
        result.RefreshPlan.Should().Be(PresentationReviewRefreshPlan.None);
    }

    [Fact]
    public void ToggleNoteVisibility_UsesExplicitContextCellAndSharedCommandPlan()
    {
        var workbook = CreateWorkbook(out var sheet);
        var selected = new CellAddress(sheet.Id, 1, 1);
        var contextCell = new CellAddress(sheet.Id, 5, 3);
        var adapter = new FakeAdapter(workbook, sheet.Id, new GridRange(selected, selected));
        var controller = new PresentationReviewSessionController(adapter);

        var result = controller.ToggleNoteVisibility(contextCell);

        result.Success.Should().BeTrue();
        adapter.LastFallbackRange.Should().Be(new GridRange(contextCell, contextCell));
        adapter.LastPlan!.CreateCommand(adapter.LastFallbackRange!.Value)
            .Should().BeOfType<ShowHideCommentCommand>();
        result.RefreshPlan.Should().Be(new PresentationReviewRefreshPlan(true, true, true, false));
    }

    [Fact]
    public void ToggleAllNotesVisibility_UsesSharedCommandPlan()
    {
        var workbook = CreateWorkbook(out var sheet);
        var address = new CellAddress(sheet.Id, 2, 2);
        var adapter = new FakeAdapter(workbook, sheet.Id, new GridRange(address, address));
        var controller = new PresentationReviewSessionController(adapter);

        var result = controller.ToggleAllNotesVisibility();

        result.Success.Should().BeTrue();
        adapter.LastPlan!.CreateCommand(adapter.LastFallbackRange!.Value)
            .Should().BeOfType<ShowAllNotesCommand>();
    }

    private static Workbook CreateWorkbook(out Sheet sheet)
    {
        var workbook = new Workbook("Review");
        sheet = workbook.AddSheet("Sheet1");
        return workbook;
    }

    private sealed class FakeAdapter : IPresentationReviewSessionAdapter
    {
        public FakeAdapter(Workbook workbook, SheetId activeSheetId, GridRange? selectedRange)
        {
            Workbook = workbook;
            ActiveSheetId = activeSheetId;
            SelectedRange = selectedRange;
        }

        public Workbook Workbook { get; }
        public SheetId ActiveSheetId { get; }
        public GridRange? SelectedRange { get; private set; }
        public string AuthorName => "Reviewer";
        public PresentationCommentMutationPlan? LastPlan { get; private set; }
        public GridRange? LastFallbackRange { get; private set; }

        public PresentationCommentMutationExecutionResult ApplyMutation(
            PresentationCommentMutationPlan plan,
            GridRange fallbackRange)
        {
            LastPlan = plan;
            LastFallbackRange = fallbackRange;
            return new(true);
        }

        public void SelectCell(CellAddress address) => SelectedRange = new GridRange(address, address);
    }
}
