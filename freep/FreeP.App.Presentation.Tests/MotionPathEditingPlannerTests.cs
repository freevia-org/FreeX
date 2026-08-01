using FluentAssertions;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class MotionPathEditingPlannerTests
{
    [Fact]
    public void BuildPlan_SnapshotsSegmentsAndMetadata()
    {
        var editor = CreateEditor(out var animation);
        animation.Motion!.Segments.Add(MotionPathSegment.MoveTo(0.1, 0.2));
        animation.Motion.Segments.Add(MotionPathSegment.CubicTo(0.2, 0.3, 0.4, 0.5, 0.6, 0.7));
        animation.Motion.Origin = "layout";
        animation.Motion.PtsTypes = "spline";

        var plan = MotionPathEditingPlanner.BuildPlan(editor.CurrentSlideAnimations, 0);

        plan.CanEdit.Should().BeTrue();
        plan.Origin.Should().Be("layout");
        plan.PtsTypes.Should().Be("spline");
        plan.Segments.Should().HaveCount(2);
        plan.Segments[1].X2.Should().BeApproximately(0.4, 1e-9);
    }

    [Fact]
    public void TryApply_ReplacesGeometryOnceAndPreservesAnimationMetadata()
    {
        var editor = CreateEditor(out var animation);
        animation.Trigger = AnimationTrigger.AfterPrevious;
        animation.DurationMs = 1200;
        animation.Motion!.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        animation.Motion.Segments.Add(MotionPathSegment.LineTo(0.25, 0.1));
        var edits = new[]
        {
            new MotionPathSegmentEdit(MotionPathSegmentKind.Move, 0.2, 0.3, 0, 0, 0, 0),
            new MotionPathSegmentEdit(MotionPathSegmentKind.Cubic, 0.8, 0.9, 0.35, 0.4, 0.65, 0.7),
        };

        MotionPathEditingPlanner.TryApply(editor, 0, edits, "parent", "spline", out var error)
            .Should().BeTrue(error);
        var updated = editor.CurrentSlideAnimations[0];
        updated.Trigger.Should().Be(AnimationTrigger.AfterPrevious);
        updated.DurationMs.Should().Be(1200);
        updated.Motion!.PtsTypes.Should().Be("spline");
        updated.Motion.Segments[1].X.Should().BeApproximately(0.8, 1e-9);

        editor.Undo();
        editor.CurrentSlideAnimations[0].Motion!.Segments[1].X.Should().BeApproximately(0.25, 1e-9);
        editor.Redo();
        editor.CurrentSlideAnimations[0].Motion!.Segments[1].X.Should().BeApproximately(0.8, 1e-9);
    }

    [Theory]
    [InlineData("first path segment")]
    [InlineData("line or curve")]
    [InlineData("finite numbers")]
    public void TryApply_RejectsInvalidGeometryWithoutUndoStep(string expectedMessage)
    {
        var editor = CreateEditor(out var animation);
        animation.Motion!.Segments.Add(MotionPathSegment.MoveTo(0, 0));
        animation.Motion.Segments.Add(MotionPathSegment.LineTo(0.2, 0.2));
        var edits = expectedMessage switch
        {
            "first path segment" => new[]
            {
                new MotionPathSegmentEdit(MotionPathSegmentKind.Line, 0, 0, 0, 0, 0, 0),
                new MotionPathSegmentEdit(MotionPathSegmentKind.Line, 1, 1, 0, 0, 0, 0),
            },
            "line or curve" => new[]
            {
                new MotionPathSegmentEdit(MotionPathSegmentKind.Move, 0, 0, 0, 0, 0, 0),
                new MotionPathSegmentEdit(MotionPathSegmentKind.Close, 0, 0, 0, 0, 0, 0),
            },
            _ => new[]
            {
                new MotionPathSegmentEdit(MotionPathSegmentKind.Move, double.NaN, 0, 0, 0, 0, 0),
                new MotionPathSegmentEdit(MotionPathSegmentKind.Line, 1, 1, 0, 0, 0, 0),
            },
        };

        MotionPathEditingPlanner.TryApply(editor, 0, edits, "parent", null, out var error)
            .Should().BeFalse();
        error.Should().ContainEquivalentOf(expectedMessage);
        editor.Bus.CanUndo.Should().BeFalse();
    }

    private static EditingSession CreateEditor(out ShapeAnimation animation)
    {
        var presentation = Presentation.CreateEmpty();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        animation = new ShapeAnimation
        {
            ShapeId = presentation.Slides[0].Shapes[0].Id,
            Kind = AnimationKind.Motion,
            Motion = new MotionPath(),
        };
        presentation.Slides[0].Animations.Add(animation);
        return editor;
    }
}
