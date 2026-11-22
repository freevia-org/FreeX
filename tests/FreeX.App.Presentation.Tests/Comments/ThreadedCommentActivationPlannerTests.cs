using FluentAssertions;
using FreeX.App.Presentation.Comments;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Comments;

public sealed class ThreadedCommentActivationPlannerTests
{
    [Theory]
    [InlineData(CommentActivationSource.Selection, CellCommentDisplayKind.ThreadedComment, CommentActivationAction.OpenCard)]
    [InlineData(CommentActivationSource.IndicatorClick, CellCommentDisplayKind.ThreadedComment, CommentActivationAction.OpenCard)]
    [InlineData(CommentActivationSource.Selection, CellCommentDisplayKind.Mixed, CommentActivationAction.OpenCard)]
    [InlineData(CommentActivationSource.Hover, CellCommentDisplayKind.ThreadedComment, CommentActivationAction.ShowPreview)]
    [InlineData(CommentActivationSource.Hover, CellCommentDisplayKind.Note, CommentActivationAction.ShowPreview)]
    [InlineData(CommentActivationSource.Selection, CellCommentDisplayKind.Note, CommentActivationAction.ShowPreview)]
    [InlineData(CommentActivationSource.IndicatorClick, CellCommentDisplayKind.Note, CommentActivationAction.ShowPreview)]
    public void Decide_routes_thread_activation_to_the_editable_card(
        CommentActivationSource source,
        CellCommentDisplayKind kind,
        CommentActivationAction expected) =>
        ThreadedCommentActivationPlanner.Decide(source, kind).Should().Be(expected);

    [Fact]
    public void Decide_returns_none_without_comment_or_while_editor_open()
    {
        ThreadedCommentActivationPlanner.Decide(CommentActivationSource.Selection, null)
            .Should().Be(CommentActivationAction.None);
        ThreadedCommentActivationPlanner.Decide(
                CommentActivationSource.Selection, CellCommentDisplayKind.ThreadedComment, inlineEditorOpen: true)
            .Should().Be(CommentActivationAction.None);
    }

    [Theory]
    [InlineData(95, 2, true)]
    [InlineData(99, 0.5, true)]
    [InlineData(50, 5, false)]
    [InlineData(95, 7, false)]
    [InlineData(100, 9, false)]
    public void IsIndicatorHit_tests_top_right_triangle(double x, double y, bool expected) =>
        ThreadedCommentActivationPlanner.IsIndicatorHit(x, y, 100, 20).Should().Be(expected);

    [Fact]
    public void PlanPaneDock_docks_to_the_right_of_the_grid()
    {
        var plan = ThreadedCommentActivationPlanner.PlanPaneDock(100, 50, 1400, 900, contentTopInset: 200);

        plan.IsDocked.Should().BeTrue();
        plan.Width.Should().BeInRange(ThreadedCommentActivationPlanner.MinPaneWidth, ThreadedCommentActivationPlanner.MaxPaneWidth);
        plan.Left.Should().Be(100 + 1400 - plan.Width);
        plan.Top.Should().Be(250);
        plan.Height.Should().Be(700);
    }

    [Fact]
    public void PlanPaneDock_declines_when_grid_would_be_squeezed()
    {
        ThreadedCommentActivationPlanner.PlanPaneDock(0, 0, 560, 900, 200).IsDocked.Should().BeFalse();
        ThreadedCommentActivationPlanner.PlanPaneDock(0, 0, 1400, 300, 200).IsDocked.Should().BeFalse();
    }

    [Fact]
    public void ShowComments_toggles_pane()
    {
        ThreadedCommentActivationPlanner.ShouldClosePaneOnToggle(true).Should().BeTrue();
        ThreadedCommentActivationPlanner.ShouldClosePaneOnToggle(false).Should().BeFalse();
    }
}
