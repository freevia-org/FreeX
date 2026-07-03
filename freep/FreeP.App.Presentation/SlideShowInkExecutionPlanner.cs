using System.Collections.Generic;

namespace FreeP.App.Compositor;

public sealed record SlideShowInkPoint(double X, double Y);

public sealed record SlideShowInkStroke(
    string StrokeId,
    int SlideIndex,
    SlideShowPresenterPointerMode PointerMode,
    SlideShowInkState InkState,
    IReadOnlyList<SlideShowInkPoint> Points);

public sealed record SlideShowInkExecutionState(
    int SlideIndex,
    SlideShowPresenterPointerMode ActivePointerMode,
    SlideShowInkState ActiveInkState,
    SlideShowInkRetentionDecision InkRetentionDecision,
    SlideShowInkStroke? ActiveStroke,
    IReadOnlyList<SlideShowInkStroke> CommittedStrokes,
    SlideShowInkPoint? LaserOverlayPoint);

public sealed record SlideShowInkOverlayPlan(
    int SlideIndex,
    IReadOnlyList<SlideShowInkStroke> CommittedStrokes,
    SlideShowInkStroke? ActiveStroke,
    SlideShowInkPoint? LaserOverlayPoint)
{
    public bool HasVisibleInk =>
        CommittedStrokes.Count > 0 || ActiveStroke is not null || LaserOverlayPoint is not null;
}

public enum SlideShowInkExecutionMutationKind
{
    None,
    BeginStroke,
    AppendStrokePoint,
    CommitStroke,
    MoveLaserOverlay,
    ClearLaserOverlay,
    EraseStroke,
    ClearInk
}

public sealed record SlideShowInkExecutionMutation(
    SlideShowInkExecutionMutationKind Kind,
    string? StrokeId,
    SlideShowInkPoint? Point,
    int AffectedStrokeCount,
    string StatusText);

public sealed record SlideShowInkExecutionResult(
    SlideShowInkExecutionState State,
    IReadOnlyList<SlideShowInkExecutionMutation> Mutations,
    bool IsHandled);

public static class SlideShowInkExecutionPlanner
{
    public const double MinimumEraseRadiusDip = 8;

    public static SlideShowInkExecutionState CreateState(
        int slideIndex = 0,
        SlideShowPointerInkPlan? pointerInk = null,
        IReadOnlyList<SlideShowInkStroke>? committedStrokes = null)
    {
        var plan = pointerInk ?? SlideShowPresenterToolPlanner.BuildPlan().PointerInk;
        return new SlideShowInkExecutionState(
            slideIndex,
            plan.PointerMode,
            plan.InkState,
            plan.InkRetentionDecision,
            ActiveStroke: null,
            committedStrokes ?? Array.Empty<SlideShowInkStroke>(),
            LaserOverlayPoint: null);
    }

    public static SlideShowInkExecutionState MoveToSlide(
        SlideShowInkExecutionState state,
        int slideIndex)
    {
        ArgumentNullException.ThrowIfNull(state);

        var committedStrokes = state.CommittedStrokes;
        if (state.ActiveStroke is not null)
        {
            var stroke = state.ActiveStroke with { Points = state.ActiveStroke.Points.ToArray() };
            committedStrokes = committedStrokes.Concat(new[] { stroke }).ToArray();
        }

        return state with
        {
            SlideIndex = slideIndex,
            ActiveStroke = null,
            CommittedStrokes = committedStrokes,
            LaserOverlayPoint = null,
        };
    }

    public static SlideShowInkExecutionState SelectPointerInk(
        SlideShowInkExecutionState state,
        SlideShowPointerInkPlan pointerInk)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(pointerInk);

        return state with
        {
            ActivePointerMode = pointerInk.PointerMode,
            ActiveInkState = pointerInk.InkState,
            InkRetentionDecision = pointerInk.InkRetentionDecision,
            ActiveStroke = null,
            LaserOverlayPoint = null,
        };
    }

    public static SlideShowInkOverlayPlan BuildOverlayPlan(SlideShowInkExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new SlideShowInkOverlayPlan(
            state.SlideIndex,
            state.CommittedStrokes
                .Where(stroke => stroke.SlideIndex == state.SlideIndex)
                .ToArray(),
            state.ActiveStroke?.SlideIndex == state.SlideIndex ? state.ActiveStroke : null,
            state.LaserOverlayPoint);
    }

    public static SlideShowInkExecutionResult Begin(
        SlideShowInkExecutionState state,
        SlideShowInkPoint point)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(point);

        return state.ActivePointerMode switch
        {
            SlideShowPresenterPointerMode.Pen or SlideShowPresenterPointerMode.Highlighter =>
                BeginInkStroke(state, point),
            SlideShowPresenterPointerMode.LaserPointer =>
                MoveLaser(state, point),
            SlideShowPresenterPointerMode.Eraser =>
                EraseNearPoint(state, point),
            _ => NoOp(state)
        };
    }

    public static SlideShowInkExecutionResult Append(
        SlideShowInkExecutionState state,
        SlideShowInkPoint point)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(point);

        if (state.ActiveStroke is not null)
        {
            // The active stroke's Points list is a growable List<SlideShowInkPoint> while the
            // stroke is being drawn (see BeginInkStroke), so appending here is amortized O(1)
            // instead of copying the whole array on every pointer-move (see End for where the
            // list becomes an immutable, materialized array on commit).
            if (state.ActiveStroke.Points is List<SlideShowInkPoint> growableActivePoints)
            {
                growableActivePoints.Add(point);

                return Handled(
                    state,
                    new(
                        SlideShowInkExecutionMutationKind.AppendStrokePoint,
                        state.ActiveStroke.StrokeId,
                        point,
                        AffectedStrokeCount: 1,
                        "Append ink stroke point"));
            }

            var updated = state.ActiveStroke with
            {
                Points = state.ActiveStroke.Points.Concat(new[] { point }).ToArray()
            };

            return Handled(
                state with { ActiveStroke = updated },
                new(
                    SlideShowInkExecutionMutationKind.AppendStrokePoint,
                    updated.StrokeId,
                    point,
                    AffectedStrokeCount: 1,
                    "Append ink stroke point"));
        }

        return state.ActivePointerMode switch
        {
            SlideShowPresenterPointerMode.LaserPointer =>
                MoveLaser(state, point),
            SlideShowPresenterPointerMode.Eraser =>
                EraseNearPoint(state, point),
            _ => NoOp(state)
        };
    }

    public static SlideShowInkExecutionResult End(
        SlideShowInkExecutionState state,
        SlideShowInkPoint? point = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (state.ActiveStroke is not null)
        {
            if (point is not null)
            {
                if (state.ActiveStroke.Points is List<SlideShowInkPoint> growableActivePoints)
                {
                    growableActivePoints.Add(point);
                }
                else
                {
                    state = state with
                    {
                        ActiveStroke = state.ActiveStroke with
                        {
                            Points = state.ActiveStroke.Points.Concat(new[] { point }).ToArray()
                        }
                    };
                }
            }

            // Materialize the (possibly still-growable) points into an immutable array once, at
            // commit time, so committed strokes never carry a mutable backing list.
            var stroke = state.ActiveStroke with { Points = state.ActiveStroke.Points.ToArray() };

            var committed = state.CommittedStrokes.Concat(new[] { stroke }).ToArray();
            return Handled(
                state with
                {
                    ActiveStroke = null,
                    CommittedStrokes = committed,
                },
                new(
                    SlideShowInkExecutionMutationKind.CommitStroke,
                    stroke.StrokeId,
                    point,
                    AffectedStrokeCount: 1,
                    "Commit ink stroke"));
        }

        if (state.LaserOverlayPoint is not null)
        {
            return Handled(
                state with { LaserOverlayPoint = null },
                new(
                    SlideShowInkExecutionMutationKind.ClearLaserOverlay,
                    StrokeId: null,
                    Point: null,
                    AffectedStrokeCount: 0,
                    "Clear laser overlay"));
        }

        return NoOp(state);
    }

    public static SlideShowInkExecutionResult EraseNearPoint(
        SlideShowInkExecutionState state,
        SlideShowInkPoint point)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(point);

        var radius = Math.Max(MinimumEraseRadiusDip, state.ActiveInkState.ThicknessDip);
        var kept = new List<SlideShowInkStroke>();
        var removed = 0;

        foreach (var stroke in state.CommittedStrokes)
        {
            if (stroke.SlideIndex == state.SlideIndex && StrokeIntersectsPoint(stroke, point, radius))
            {
                removed++;
                continue;
            }

            kept.Add(stroke);
        }

        return Handled(
            state with
            {
                ActiveStroke = null,
                CommittedStrokes = kept,
                LaserOverlayPoint = null,
            },
            new(
                SlideShowInkExecutionMutationKind.EraseStroke,
                StrokeId: null,
                point,
                removed,
                removed == 0 ? "Erase ink stroke hit no strokes" : $"Erase {removed} ink stroke(s)"));
    }

    public static SlideShowInkExecutionResult ClearCurrentSlide(SlideShowInkExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var kept = state.CommittedStrokes
            .Where(stroke => stroke.SlideIndex != state.SlideIndex)
            .ToArray();
        var removed = state.CommittedStrokes.Count - kept.Length;

        return Handled(
            state with
            {
                ActiveStroke = null,
                CommittedStrokes = kept,
                LaserOverlayPoint = null,
            },
            new(
                SlideShowInkExecutionMutationKind.ClearInk,
                StrokeId: null,
                Point: null,
                removed,
                "Clear current slide ink"));
    }

    public static SlideShowInkExecutionResult ClearAllInk(SlideShowInkExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return Handled(
            state with
            {
                ActiveStroke = null,
                CommittedStrokes = Array.Empty<SlideShowInkStroke>(),
                LaserOverlayPoint = null,
            },
            new(
                SlideShowInkExecutionMutationKind.ClearInk,
                StrokeId: null,
                Point: null,
                state.CommittedStrokes.Count,
                "Clear all slideshow ink"));
    }

    public static SlideShowInkExecutionResult ApplyRetentionOnExit(SlideShowInkExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.InkRetentionDecision == SlideShowInkRetentionDecision.ClearInk
            ? ClearAllInk(state)
            : End(state);
    }

    private static SlideShowInkExecutionResult BeginInkStroke(
        SlideShowInkExecutionState state,
        SlideShowInkPoint point)
    {
        // Use a growable List<SlideShowInkPoint> for the active stroke's points so Append can
        // add points in amortized O(1) instead of copying the array on every pointer-move.
        var stroke = new SlideShowInkStroke(
            Guid.NewGuid().ToString("N"),
            state.SlideIndex,
            state.ActivePointerMode,
            state.ActiveInkState,
            new List<SlideShowInkPoint> { point });

        return Handled(
            state with
            {
                ActiveStroke = stroke,
                LaserOverlayPoint = null,
            },
            new(
                SlideShowInkExecutionMutationKind.BeginStroke,
                stroke.StrokeId,
                point,
                AffectedStrokeCount: 1,
                "Begin ink stroke"));
    }

    private static SlideShowInkExecutionResult MoveLaser(
        SlideShowInkExecutionState state,
        SlideShowInkPoint point) =>
        Handled(
            state with
            {
                ActiveStroke = null,
                LaserOverlayPoint = point,
            },
            new(
                SlideShowInkExecutionMutationKind.MoveLaserOverlay,
                StrokeId: null,
                point,
                AffectedStrokeCount: 0,
                "Move laser overlay"));

    private static SlideShowInkExecutionResult Handled(
        SlideShowInkExecutionState state,
        SlideShowInkExecutionMutation mutation) =>
        new(state, new[] { mutation }, IsHandled: true);

    private static SlideShowInkExecutionResult NoOp(SlideShowInkExecutionState state) =>
        new(
            state,
            new[]
            {
                new SlideShowInkExecutionMutation(
                    SlideShowInkExecutionMutationKind.None,
                    StrokeId: null,
                    Point: null,
                    AffectedStrokeCount: 0,
                    "No presenter ink execution")
            },
            IsHandled: false);

    private static bool StrokeIntersectsPoint(
        SlideShowInkStroke stroke,
        SlideShowInkPoint point,
        double radius)
    {
        var effectiveRadius = radius + (stroke.InkState.ThicknessDip / 2);

        for (var i = 0; i < stroke.Points.Count; i++)
        {
            if (Distance(stroke.Points[i], point) <= effectiveRadius)
            {
                return true;
            }

            if (i > 0 && DistanceToSegment(point, stroke.Points[i - 1], stroke.Points[i]) <= effectiveRadius)
            {
                return true;
            }
        }

        return false;
    }

    private static double Distance(SlideShowInkPoint first, SlideShowInkPoint second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double DistanceToSegment(
        SlideShowInkPoint point,
        SlideShowInkPoint start,
        SlideShowInkPoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (dx == 0 && dy == 0)
        {
            return Distance(point, start);
        }

        var t = (((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / ((dx * dx) + (dy * dy));
        t = Math.Clamp(t, 0, 1);

        return Distance(point, new SlideShowInkPoint(start.X + (t * dx), start.Y + (t * dy)));
    }
}
