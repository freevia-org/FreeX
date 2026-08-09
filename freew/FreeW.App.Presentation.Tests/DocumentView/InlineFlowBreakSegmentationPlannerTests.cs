using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Tests.DocumentView;

public sealed class InlineFlowBreakSegmentationPlannerTests
{
    [Fact]
    public void Mixed_breaks_create_ordered_segments_and_stable_source_offsets()
    {
        var plan = InlineFlowBreakSegmentationPlanner.Build(
        [
            new InlineFlowRunInput(6),
            new InlineFlowRunInput(0, IsPageBreak: true),
            new InlineFlowRunInput(6),
            new InlineFlowRunInput(0, IsColumnBreak: true),
            new InlineFlowRunInput(5),
        ]);

        plan.SourceLength.Should().Be(17);
        plan.Runs.Select(run => run.SourceOffset).Should().Equal(0, 6, 6, 12, 12);
        plan.Breaks.Should().Equal(
            new InlineFlowBreakDescriptor(1, 6, InlineFlowBreakKind.Page),
            new InlineFlowBreakDescriptor(3, 12, InlineFlowBreakKind.Column));
        plan.Segments.Should().Equal(
            new InlineFlowSegmentPlan(0, 2, 0, 6, InlineFlowBreakKind.None),
            new InlineFlowSegmentPlan(2, 2, 6, 6, InlineFlowBreakKind.Page),
            new InlineFlowSegmentPlan(4, 1, 12, 5, InlineFlowBreakKind.Column));
        plan.SourceOffsetAtBoundary(0).Should().Be(0);
        plan.SourceOffsetAtBoundary(2).Should().Be(6);
        plan.SourceOffsetAtBoundary(4).Should().Be(12);
        plan.SourceOffsetAtBoundary(5).Should().Be(17);
    }

    [Fact]
    public void Page_break_wins_when_a_projected_run_carries_both_flags()
    {
        var plan = InlineFlowBreakSegmentationPlanner.Build(
        [
            new InlineFlowRunInput(3),
            new InlineFlowRunInput(4, IsPageBreak: true, IsColumnBreak: true),
            new InlineFlowRunInput(2),
        ]);

        plan.Runs[1].BreakKind.Should().Be(InlineFlowBreakKind.Page);
        plan.Runs[1].SourceLength.Should().Be(0, "break markers never consume a model text offset");
        plan.Runs[2].SourceOffset.Should().Be(3);
        plan.SourceLength.Should().Be(5);
        plan.Segments[1].BreakBefore.Should().Be(InlineFlowBreakKind.Page);
    }

    [Fact]
    public void Consecutive_and_trailing_breaks_preserve_empty_native_fragments()
    {
        var plan = InlineFlowBreakSegmentationPlanner.Build(
        [
            new InlineFlowRunInput(0, IsColumnBreak: true),
            new InlineFlowRunInput(0, IsPageBreak: true),
        ], pageBreakBefore: true);

        plan.Segments.Should().Equal(
            new InlineFlowSegmentPlan(0, 1, 0, 0, InlineFlowBreakKind.Page),
            new InlineFlowSegmentPlan(1, 1, 0, 0, InlineFlowBreakKind.Column),
            new InlineFlowSegmentPlan(2, 0, 0, 0, InlineFlowBreakKind.Page));
    }

    [Fact]
    public void Negative_source_length_is_rejected()
    {
        var act = () => InlineFlowBreakSegmentationPlanner.Build([new InlineFlowRunInput(-1)]);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
