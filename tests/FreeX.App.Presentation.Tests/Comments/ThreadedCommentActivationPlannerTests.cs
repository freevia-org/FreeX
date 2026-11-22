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

    [Theory]
    [InlineData(1400, 392)]
    [InlineData(2000, 400)]
    [InlineData(800, 280)]
    public void DefaultPaneWidth_is_a_fraction_of_the_work_area_within_limits(double hostWidth, double expected) =>
        ThreadedCommentActivationPlanner.DefaultPaneWidth(hostWidth).Should().BeApproximately(expected, 0.001);

    [Theory]
    [InlineData(100, 1400, 280)]
    [InlineData(450, 1400, 450)]
    [InlineData(2000, 1400, 640)]
    [InlineData(2000, 800, 480)]
    [InlineData(450, 500, 280)]
    [InlineData(double.NaN, 1400, 280)]
    public void ClampPaneWidth_keeps_pane_minimum_and_grid_minimum(double requested, double hostWidth, double expected) =>
        ThreadedCommentActivationPlanner.ClampPaneWidth(requested, hostWidth).Should().Be(expected);

    [Fact]
    public void ShowComments_toggles_pane()
    {
        ThreadedCommentActivationPlanner.ShouldClosePaneOnToggle(true).Should().BeTrue();
        ThreadedCommentActivationPlanner.ShouldClosePaneOnToggle(false).Should().BeFalse();
    }
}
