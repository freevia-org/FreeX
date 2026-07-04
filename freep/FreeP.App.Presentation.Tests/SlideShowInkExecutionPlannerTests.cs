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
    public void UndoLastStroke_RemovesOnlyLatestCurrentSlideStroke()
    {
        var penState = CreateState(SlideShowPresenterPointerMode.Pen, "#AA0000", 4);
        var first = CommitStroke(penState, new SlideShowInkPoint(10, 10), new SlideShowInkPoint(60, 10));
        var second = CommitStroke(first, new SlideShowInkPoint(200, 200), new SlideShowInkPoint(220, 220));
        var otherSlideStroke = second.CommittedStrokes[0] with { SlideIndex = 1, StrokeId = "other-slide" };
        var state = second with
        {
            CommittedStrokes = second.CommittedStrokes.Concat(new[] { otherSlideStroke }).ToArray(),
            LaserOverlayPoint = new SlideShowInkPoint(42, 42)
        };

        var result = SlideShowInkExecutionPlanner.UndoLastStroke(state);

        result.IsHandled.Should().BeTrue();
        result.Mutations.Single().Kind.Should().Be(SlideShowInkExecutionMutationKind.UndoLastStroke);
        result.Mutations.Single().AffectedStrokeCount.Should().Be(1);
        result.Mutations.Single().StrokeId.Should().Be(second.CommittedStrokes[1].StrokeId);
        result.State.LaserOverlayPoint.Should().BeNull();
        result.State.CommittedStrokes.Should().HaveCount(2);
        result.State.CommittedStrokes.Should().Contain(stroke => stroke.StrokeId == second.CommittedStrokes[0].StrokeId);
        result.State.CommittedStrokes.Should().Contain(stroke => stroke.StrokeId == "other-slide");
    }

    [Fact]
    public void UndoLastStroke_CancelsActiveStrokeBeforeCommittedInk()
    {
        var committed = CommitStroke(
            CreateState(SlideShowPresenterPointerMode.Pen, "#AA0000", 4),
            new SlideShowInkPoint(10, 10),
            new SlideShowInkPoint(60, 10));
        var active = SlideShowInkExecutionPlanner.Begin(committed, new SlideShowInkPoint(70, 20)).State;

        var result = SlideShowInkExecutionPlanner.UndoLastStroke(active);

        result.Mutations.Single().Kind.Should().Be(SlideShowInkExecutionMutationKind.UndoLastStroke);
        result.Mutations.Single().AffectedStrokeCount.Should().Be(1);
        result.Mutations.Single().StrokeId.Should().Be(active.ActiveStroke!.StrokeId);
        result.State.ActiveStroke.Should().BeNull();
        result.State.CommittedStrokes.Should().ContainSingle();
    }

    [Fact]
    public void BuildOverlayPlan_ReturnsCurrentSlideCommittedActiveAndLaserInk()
    {
        var penState = CreateState(SlideShowPresenterPointerMode.Pen, "#AA0000", 4);
        var committedCurrent = CommitStroke(penState, new SlideShowInkPoint(10, 10), new SlideShowInkPoint(60, 10));
        var otherSlideStroke = committedCurrent.CommittedStrokes[0] with
        {
            SlideIndex = 1,
            StrokeId = "other-slide"
        };
        var active = SlideShowInkExecutionPlanner.Begin(
            committedCurrent with
            {
                CommittedStrokes = committedCurrent.CommittedStrokes.Concat(new[] { otherSlideStroke }).ToArray()
            },
            new SlideShowInkPoint(70, 20));
        var overlay = SlideShowInkExecutionPlanner.BuildOverlayPlan(active.State);
        var laserState = SlideShowInkExecutionPlanner.SelectPointerInk(
            active.State,
            SlideShowPresenterToolPlanner.PlanPointerInk(
                SlideShowPresenterPointerMode.LaserPointer,
                "#00FF00",
                6,
                SlideShowInkRetentionDecision.KeepInk));
        var laser = SlideShowInkExecutionPlanner.BuildOverlayPlan(
            SlideShowInkExecutionPlanner.Begin(laserState, new SlideShowInkPoint(90, 40)).State);

        overlay.HasVisibleInk.Should().BeTrue();
        overlay.CommittedStrokes.Should().ContainSingle();
        overlay.CommittedStrokes.Single().StrokeId.Should().NotBe("other-slide");
        overlay.ActiveStroke.Should().NotBeNull();
        overlay.LaserOverlayPoint.Should().BeNull();
        laser.CommittedStrokes.Should().ContainSingle();
        laser.ActiveStroke.Should().BeNull();
        laser.LaserOverlayPoint.Should().Be(new SlideShowInkPoint(90, 40));
    }

    [Fact]
    public void BuildOverlayRenderPlan_ProjectsPowerPointStyleInkPrimitivesForHosts()
    {
        var penState = CreateState(SlideShowPresenterPointerMode.Highlighter, "#FFEE00", 10);
        var committed = CommitStroke(penState, new SlideShowInkPoint(10, 20), new SlideShowInkPoint(110, 220));
        var laserState = SlideShowInkExecutionPlanner.SelectPointerInk(
            committed,
            SlideShowPresenterToolPlanner.PlanPointerInk(
                SlideShowPresenterPointerMode.LaserPointer,
                "#00FF00",
                6,
                SlideShowInkRetentionDecision.KeepInk));
        var laser = SlideShowInkExecutionPlanner.Begin(laserState, new SlideShowInkPoint(50, 100)).State;

        var plan = SlideShowInkExecutionPlanner.BuildOverlayRenderPlan(
            laser,
            canvasWidthDip: 1920,
            canvasHeightDip: 1080,
            new SlideShowSlideMetrics(960, 540));

        plan.HasVisibleInk.Should().BeTrue();
        plan.CanvasWidthDip.Should().Be(1920);
        plan.CanvasHeightDip.Should().Be(1080);
        plan.Primitives.Should().HaveCount(2);

        var stroke = plan.Primitives[0];
        stroke.Kind.Should().Be(SlideShowInkOverlayPrimitiveKind.StrokePath);
        stroke.PointerMode.Should().Be(SlideShowPresenterPointerMode.Highlighter);
        stroke.InkState.ColorHex.Should().Be("#FFEE00");
        stroke.InkState.Opacity.Should().Be(0.45);
        stroke.StrokeThicknessDip.Should().Be(20);
        stroke.UseRoundLineCaps.Should().BeTrue();
        stroke.UseRoundLineJoin.Should().BeTrue();
        stroke.Points.Should().Equal(
            new SlideShowPoint(20, 40),
            new SlideShowPoint(220, 440));

        var dot = plan.Primitives[1];
        dot.Kind.Should().Be(SlideShowInkOverlayPrimitiveKind.LaserDot);
        dot.PointerMode.Should().Be(SlideShowPresenterPointerMode.LaserPointer);
        dot.CenterPoint.Should().Be(new SlideShowPoint(100, 200));
        dot.RadiusDip.Should().Be(12);
        dot.OutlineColorHex.Should().Be("#FFFFFF");
        dot.OutlineThicknessDip.Should().Be(1);
    }

    [Fact]
    public void MoveToSlide_CommitsActiveStrokeAndClearsTransientLaserOverlay()
    {
        var drawing = SlideShowInkExecutionPlanner.Append(
            SlideShowInkExecutionPlanner.Begin(
                CreateState(SlideShowPresenterPointerMode.Pen, "#AA0000", 4),
                new SlideShowInkPoint(10, 10)).State,
            new SlideShowInkPoint(60, 10)).State;

        var moved = SlideShowInkExecutionPlanner.MoveToSlide(drawing, slideIndex: 1);

        moved.SlideIndex.Should().Be(1);
        moved.ActiveStroke.Should().BeNull();
        moved.CommittedStrokes.Should().ContainSingle();
        moved.CommittedStrokes.Single().SlideIndex.Should().Be(0);
        moved.CommittedStrokes.Single().Points.Should().Equal(
            new SlideShowInkPoint(10, 10),
            new SlideShowInkPoint(60, 10));
        SlideShowInkExecutionPlanner.BuildOverlayPlan(moved).HasVisibleInk.Should().BeFalse();

        var laser = SlideShowInkExecutionPlanner.Begin(
            SlideShowInkExecutionPlanner.SelectPointerInk(
                moved,
                SlideShowPresenterToolPlanner.PlanPointerInk(
                    SlideShowPresenterPointerMode.LaserPointer,
                    "#FF0000",
                    6,
                    SlideShowInkRetentionDecision.KeepInk)),
            new SlideShowInkPoint(20, 20)).State;

        var laserMoved = SlideShowInkExecutionPlanner.MoveToSlide(laser, slideIndex: 0);

        laserMoved.LaserOverlayPoint.Should().BeNull();
        laserMoved.CommittedStrokes.Should().ContainSingle();
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
