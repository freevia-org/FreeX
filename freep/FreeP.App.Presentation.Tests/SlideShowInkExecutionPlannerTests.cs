using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowInkExecutionPlannerTests
{
    [Theory]
    [InlineData(SlideShowPresenterPointerMode.Pen, "#123456", 5, 1.0)]
    [InlineData(SlideShowPresenterPointerMode.Highlighter, "#FFEE00", 10, 0.45)]
    public void BeginAppendEnd_CommitsPenAndHighlighterStrokes(
        SlideShowPresenterPointerMode mode,
        string colorHex,
        double thicknessDip,
        double expectedOpacity)
    {
        var state = CreateState(mode, colorHex, thicknessDip);

        var begin = SlideShowInkExecutionPlanner.Begin(state, new SlideShowInkPoint(10, 20));
        var append = SlideShowInkExecutionPlanner.Append(begin.State, new SlideShowInkPoint(30, 40));
        var end = SlideShowInkExecutionPlanner.End(append.State, new SlideShowInkPoint(50, 60));

        begin.IsHandled.Should().BeTrue();
        begin.Mutations.Single().Kind.Should().Be(SlideShowInkExecutionMutationKind.BeginStroke);
        append.Mutations.Single().Kind.Should().Be(SlideShowInkExecutionMutationKind.AppendStrokePoint);
        end.Mutations.Single().Kind.Should().Be(SlideShowInkExecutionMutationKind.CommitStroke);
        end.State.ActiveStroke.Should().BeNull();
        end.State.CommittedStrokes.Should().ContainSingle();
        var stroke = end.State.CommittedStrokes.Single();
        stroke.PointerMode.Should().Be(mode);
        stroke.InkState.ColorHex.Should().Be(colorHex);
        stroke.InkState.ThicknessDip.Should().Be(thicknessDip);
        stroke.InkState.Opacity.Should().Be(expectedOpacity);
        stroke.Points.Should().Equal(
            new SlideShowInkPoint(10, 20),
            new SlideShowInkPoint(30, 40),
            new SlideShowInkPoint(50, 60));
    }

    [Fact]
    public void Append_ManyPoints_ProducesStrokeWithAllPointsInOrder()
    {
        // Regression test for a perf bug where Append rebuilt the active stroke's point array
        // via Concat().ToArray() on every call (O(n) copy per pointer-move => O(n^2) per
        // stroke). A large point count here is a correctness check that doubles as a guard
        // against that behavior reappearing (the run must complete quickly, not just produce
        // the right answer).
        const int pointCount = 20_000;
        var state = CreateState(SlideShowPresenterPointerMode.Pen, "#123456", 5);

        var begin = SlideShowInkExecutionPlanner.Begin(state, new SlideShowInkPoint(0, 0));
        var current = begin.State;
        var expectedPoints = new List<SlideShowInkPoint> { new(0, 0) };

        for (var i = 1; i < pointCount; i++)
        {
            var point = new SlideShowInkPoint(i, i * 2);
            expectedPoints.Add(point);
            current = SlideShowInkExecutionPlanner.Append(current, point).State;
        }

        var end = SlideShowInkExecutionPlanner.End(current);

        end.State.CommittedStrokes.Should().ContainSingle();
        end.State.CommittedStrokes.Single().Points.Should().Equal(expectedPoints);
    }

    [Fact]
    public void LaserPointer_ProducesTransientOverlayWithoutCommittedInk()
    {
        var state = CreateState(SlideShowPresenterPointerMode.LaserPointer, "#FF0000", 6);

        var begin = SlideShowInkExecutionPlanner.Begin(state, new SlideShowInkPoint(100, 120));
        var move = SlideShowInkExecutionPlanner.Append(begin.State, new SlideShowInkPoint(130, 140));
        var end = SlideShowInkExecutionPlanner.End(move.State);

        begin.Mutations.Single().Kind.Should().Be(SlideShowInkExecutionMutationKind.MoveLaserOverlay);
        move.State.LaserOverlayPoint.Should().Be(new SlideShowInkPoint(130, 140));
        move.State.CommittedStrokes.Should().BeEmpty();
        end.Mutations.Single().Kind.Should().Be(SlideShowInkExecutionMutationKind.ClearLaserOverlay);
        end.State.LaserOverlayPoint.Should().BeNull();
        end.State.CommittedStrokes.Should().BeEmpty();
    }

    [Fact]
    public void Eraser_RemovesOnlyCurrentSlideStrokesNearPoint()
    {
        var penState = CreateState(SlideShowPresenterPointerMode.Pen, "#AA0000", 4);
        var first = CommitStroke(penState, new SlideShowInkPoint(10, 10), new SlideShowInkPoint(60, 10));
        var second = CommitStroke(first, new SlideShowInkPoint(200, 200), new SlideShowInkPoint(220, 220));
        var otherSlideStroke = second.CommittedStrokes[0] with { SlideIndex = 1, StrokeId = "other-slide" };
        var eraser = SlideShowInkExecutionPlanner.SelectPointerInk(
            second with { CommittedStrokes = second.CommittedStrokes.Concat(new[] { otherSlideStroke }).ToArray() },
            SlideShowPresenterToolPlanner.PlanPointerInk(
                SlideShowPresenterPointerMode.Eraser,
                inkColorHex: null,
                inkThicknessDip: 12,
                SlideShowInkRetentionDecision.KeepInk));

        var result = SlideShowInkExecutionPlanner.Begin(eraser, new SlideShowInkPoint(32, 12));

        result.IsHandled.Should().BeTrue();
        result.Mutations.Single().Kind.Should().Be(SlideShowInkExecutionMutationKind.EraseStroke);
        result.Mutations.Single().AffectedStrokeCount.Should().Be(1);
        result.State.CommittedStrokes.Should().HaveCount(2);
        result.State.CommittedStrokes.Should().Contain(stroke => stroke.StrokeId == second.CommittedStrokes[1].StrokeId);
        result.State.CommittedStrokes.Should().Contain(stroke => stroke.StrokeId == "other-slide");
    }

    [Fact]
    public void ClearAndRetentionClear_RemoveCommittedInk()
    {
        var state = CreateState(
            SlideShowPresenterPointerMode.Pen,
            "#00AA00",
            4,
            SlideShowInkRetentionDecision.ClearInk);
        state = CommitStroke(state, new SlideShowInkPoint(5, 5), new SlideShowInkPoint(15, 15));

        var clearCurrent = SlideShowInkExecutionPlanner.ClearCurrentSlide(state);
        clearCurrent.Mutations.Single().Kind.Should().Be(SlideShowInkExecutionMutationKind.ClearInk);
        clearCurrent.Mutations.Single().AffectedStrokeCount.Should().Be(1);
        clearCurrent.State.CommittedStrokes.Should().BeEmpty();

        state = CommitStroke(state, new SlideShowInkPoint(5, 5), new SlideShowInkPoint(15, 15));
        var retention = SlideShowInkExecutionPlanner.ApplyRetentionOnExit(state);
        retention.State.CommittedStrokes.Should().BeEmpty();
        retention.Mutations.Single().StatusText.Should().Be("Clear all slideshow ink");
    }

    [Fact]
    public void Arrow_DoesNotStartInkOrConsumeNavigation()
    {
        var state = CreateState(SlideShowPresenterPointerMode.Arrow, "#FF0000", 4);

        var result = SlideShowInkExecutionPlanner.Begin(state, new SlideShowInkPoint(10, 20));

        result.IsHandled.Should().BeFalse();
        result.Mutations.Single().Kind.Should().Be(SlideShowInkExecutionMutationKind.None);
        result.State.Should().BeSameAs(state);
    }

    private static SlideShowInkExecutionState CreateState(
        SlideShowPresenterPointerMode mode,
        string colorHex,
        double thicknessDip,
        SlideShowInkRetentionDecision retention = SlideShowInkRetentionDecision.KeepInk) =>
        SlideShowInkExecutionPlanner.CreateState(
            slideIndex: 0,
            SlideShowPresenterToolPlanner.PlanPointerInk(
                mode,
                colorHex,
                thicknessDip,
                retention));

    private static SlideShowInkExecutionState CommitStroke(
        SlideShowInkExecutionState state,
        SlideShowInkPoint start,
        SlideShowInkPoint end) =>
        SlideShowInkExecutionPlanner.End(
            SlideShowInkExecutionPlanner.Begin(state, start).State,
            end).State;
}
