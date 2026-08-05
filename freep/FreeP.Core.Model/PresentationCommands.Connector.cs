using Free.Shared.Drawing;

namespace FreeP.Core.Model;

// ════════════════════════════════════════════════════════════════════════════════
// CONNECTOR ATTACHMENT / ROUTING  (Wave 23 + Wave 26 elbow auto-routing)
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Sets the absolute bounds of a connector shape so that its endpoints line up with
/// the resolved connection-site points of its attached shapes.
/// Wave 26: also stores the computed <see cref="SlideShape.ElbowRoute"/> for
/// ElbowConnector shapes between two attached shapes.
///
/// This command is NOT issued directly by user actions; it is embedded inline inside
/// <see cref="MoveShapeCommand"/>, <see cref="ResizeShapeCommand"/>, and
/// <see cref="RotateShapeCommand"/>'s Apply/Revert so the entire operation (shape
/// move + connector follow) is a single undoable step.
/// </summary>
public sealed class UpdateConnectorBoundsCommand : IPresentationCommand
{
    private readonly int  _slideIndex;
    private readonly uint _connectorId;
    private readonly long _newX;
    private readonly long _newY;
    private readonly long _newCx;
    private readonly long _newCy;

    // Wave 26: optional Manhattan route for elbow connectors.
    private readonly List<(long X, long Y)>? _newRoute;

    // Captured on first Apply for Revert.
    private long _oldX;
    private long _oldY;
    private long _oldCx;
    private long _oldCy;
    private List<(long X, long Y)>? _oldRoute;

    // Internal read-only accessors used by the parent command's capture logic.
    internal uint ConnectorId => _connectorId;
    internal long NewX  => _newX;
    internal long NewY  => _newY;
    internal long NewCx => _newCx;
    internal long NewCy => _newCy;

    public UpdateConnectorBoundsCommand(
        int slideIndex, uint connectorId,
        long newX, long newY, long newCx, long newCy,
        List<(long X, long Y)>? newRoute = null)
    {
        _slideIndex  = slideIndex;
        _connectorId = connectorId;
        _newX        = newX;
        _newY        = newY;
        _newCx       = newCx;
        _newCy       = newCy;
        _newRoute    = newRoute;
    }

    public string Label => "Reroute Connector";

    public void Apply(Presentation p)
    {
        var c = FindConnector(p);
        if (c is null) return;
        _oldX     = c.OffsetXEmu;
        _oldY     = c.OffsetYEmu;
        _oldCx    = c.ExtentCxEmu;
        _oldCy    = c.ExtentCyEmu;
        _oldRoute = c.ElbowRoute;
        ApplyBounds(c, _newX, _newY, _newCx, _newCy, _newRoute);
    }

    public void Revert(Presentation p)
    {
        var c = FindConnector(p);
        if (c is null) return;
        ApplyBounds(c, _oldX, _oldY, _oldCx, _oldCy, _oldRoute);
    }

    private SlideShape? FindConnector(Presentation p)
    {
        if (_slideIndex < 0 || _slideIndex >= p.Slides.Count) return null;
        return ShapeHelper.Find(p, _slideIndex, _connectorId);
    }

    private static void ApplyBounds(SlideShape c, long x, long y, long cx, long cy,
        List<(long X, long Y)>? route)
    {
        c.OffsetXEmu  = x;
        c.OffsetYEmu  = y;
        c.ExtentCxEmu = cx;
        c.ExtentCyEmu = cy;
        c.ElbowRoute  = route;
    }
}

// ════════════════════════════════════════════════════════════════════════════════
// ELBOW ROUTER  (Wave 26)
// ════════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Computes a clean 1-2 bend Manhattan (orthogonal) route between two connector sites.
///
/// <b>Algorithm</b>:
/// Given start site S on shape A and end site E on shape B:
/// 1. Determine the exit direction from S: perpendicular to the shape edge the site is on
///    (e.g. site on right edge → exit right = +X).
/// 2. Determine the entry direction into E: perpendicular from E's edge side.
/// 3. Prefer a 2-segment L-route (horizontal + vertical) when S and E are horizontally
///    or vertically aligned within a gap. Otherwise use a 3-segment Z-route (H/V/H or V/H/V)
///    with the midpoint chosen as the midspan between the two shapes.
/// 4. When non-endpoint obstacle rectangles are supplied, a Manhattan visibility graph
///    detours around them while preserving the endpoint exit/entry directions. With no
///    obstacles, the compact endpoint-only route remains unchanged.
///
/// Returns a list of waypoints in EMU including the start and end sites:
///   [start, ... bend points ..., end]
/// The renderer converts this polyline to an elbow-path geometry.
///
/// This class is framework-free so it can be unit-tested without the host.
/// </summary>
public static class ElbowRouter
{
    // ── Public API ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes a Manhattan route between <paramref name="start"/> and <paramref name="end"/>.
    /// The bounding rects of the two attached shapes are used to choose the exit/midpoint gap.
    /// When either shape rect is unknown (zero-size), falls back to a simple 2-point line.
    /// </summary>
    public static List<(long X, long Y)> Route(
        (long X, long Y) start,
        (long X, long Y) end,
        (long L, long T, long R, long B)? startShapeRect,
        (long L, long T, long R, long B)? endShapeRect,
        IReadOnlyList<(long L, long T, long R, long B)>? obstacles = null)
    {
        // Trivial same-point
        if (start.X == end.X && start.Y == end.Y)
            return new List<(long, long)> { start, end };

        // Determine exit direction from start
        var exitDir  = InferExitDirection(start, startShapeRect);
        // Determine entry direction into end (we want the opposite of the natural exit from end)
        var entryDir = InferExitDirection(end, endShapeRect);

        // Gap midpoint for Z-routing: pick the midspan between the two shapes.
        long gapMidX = (start.X + end.X) / 2;
        long gapMidY = (start.Y + end.Y) / 2;

        // When shapes are available, prefer the gap between them
        if (startShapeRect.HasValue && endShapeRect.HasValue)
        {
            var sr = startShapeRect.Value;
            var er = endShapeRect.Value;
            // Horizontal gap: midpoint between the right edge of the left shape and
            // the left edge of the right shape.
            long leftShapeRight  = Math.Min(sr.R, er.R);
            long rightShapeLeft  = Math.Max(sr.L, er.L);
            if (leftShapeRight < rightShapeLeft)
                gapMidX = (leftShapeRight + rightShapeLeft) / 2;

            // Vertical gap: midpoint between the bottom of the top shape and top of the bottom.
            long topShapeBottom  = Math.Min(sr.B, er.B);
            long bottomShapeTop  = Math.Max(sr.T, er.T);
            if (topShapeBottom < bottomShapeTop)
                gapMidY = (topShapeBottom + bottomShapeTop) / 2;
        }

        var route = BuildRoute(start, end, exitDir, entryDir, gapMidX, gapMidY);
        return TryRouteAroundObstacles(route, start, end, exitDir, entryDir, obstacles)
            ?? route;
    }

    // ── Exit direction inference ───────────────────────────────────────────────────

    /// <summary>
    /// Returns the predominant exit direction from a site based on where the site sits
    /// relative to its shape's bounding rect.
    /// </summary>
    private static Direction InferExitDirection((long X, long Y) site, (long L, long T, long R, long B)? rect)
    {
        if (!rect.HasValue) return Direction.Right; // default

        var r = rect.Value;
        long distLeft   = Math.Abs(site.X - r.L);
        long distRight  = Math.Abs(site.X - r.R);
        long distTop    = Math.Abs(site.Y - r.T);
        long distBottom = Math.Abs(site.Y - r.B);

        // Find which edge the site is closest to
        long minDist = Math.Min(Math.Min(distLeft, distRight), Math.Min(distTop, distBottom));

        if (minDist == distLeft)   return Direction.Left;
        if (minDist == distRight)  return Direction.Right;
        if (minDist == distTop)    return Direction.Up;
        return Direction.Down;
    }

    private enum Direction { Left, Right, Up, Down }

    // ── Route builder ─────────────────────────────────────────────────────────────

    private static List<(long X, long Y)> BuildRoute(
        (long X, long Y) s,
        (long X, long Y) e,
        Direction exitDir,
        Direction entryDir,
        long gapMidX,
        long gapMidY)
    {
        var pts = new List<(long X, long Y)>();
        pts.Add(s);

        bool sameX = s.X == e.X;
        bool sameY = s.Y == e.Y;

        // Simple collinear cases — one bend already
        if (sameX)
        {
            // Vertical straight shot — no bend needed
            pts.Add(e);
            return pts;
        }
        if (sameY)
        {
            // Horizontal straight shot — no bend needed
            pts.Add(e);
            return pts;
        }

        // Choose routing strategy based on exit/entry directions.
        // Case A: horizontal exits on both sides (left↔right) → Z-route H-V-H
        bool bothHorizontal = (exitDir == Direction.Right || exitDir == Direction.Left)
                           && (entryDir == Direction.Right || entryDir == Direction.Left);
        // Case B: vertical exits on both sides (up↔down) → Z-route V-H-V
        bool bothVertical   = (exitDir == Direction.Up || exitDir == Direction.Down)
                           && (entryDir == Direction.Up || entryDir == Direction.Down);

        if (bothHorizontal)
        {
            // H-V-H: go horizontal from s to gapMidX, then vertical to e.Y, then horizontal to e
            pts.Add((gapMidX, s.Y));
            pts.Add((gapMidX, e.Y));
        }
        else if (bothVertical)
        {
            // V-H-V: go vertical from s to gapMidY, then horizontal to e.X, then vertical to e
            pts.Add((s.X, gapMidY));
            pts.Add((e.X, gapMidY));
        }
        else
        {
            // Mixed: one horizontal exit, one vertical exit → simple L-route with one bend
            // Determine bend point: prefer the corner that aligns with the exit direction.
            bool exitIsHorizontal = exitDir == Direction.Right || exitDir == Direction.Left;
            if (exitIsHorizontal)
                pts.Add((e.X, s.Y));  // go horizontal first, then vertical
            else
                pts.Add((s.X, e.Y));  // go vertical first, then horizontal
        }

        pts.Add(e);
        return pts;
    }

    private readonly record struct RouteState(int Node, int Direction);

    /// <summary>
    /// Re-routes a base Manhattan path through a small orthogonal visibility graph when
    /// the base path intersects an intervening shape. Grid coordinates are the endpoints
    /// plus the outside edges of every obstacle, so the result stays deterministic and
    /// does not introduce arbitrary sampling resolution into the document model.
    /// </summary>
    private static List<(long X, long Y)>? TryRouteAroundObstacles(
        IReadOnlyList<(long X, long Y)> baseRoute,
        (long X, long Y) start,
        (long X, long Y) end,
        Direction exitDirection,
        Direction entryDirection,
        IReadOnlyList<(long L, long T, long R, long B)>? obstacles)
    {
        var rects = obstacles?
            .Where(rect => rect.R > rect.L && rect.B > rect.T)
            .Select(InflateObstacle)
            .Distinct()
            .ToArray();
        if (rects is not { Length: > 0 }
            || IsRouteClear(baseRoute, rects))
            return baseRoute.ToList();

        var xs = new SortedSet<long> { start.X, end.X };
        var ys = new SortedSet<long> { start.Y, end.Y };
        foreach (var rect in rects)
        {
            xs.Add(rect.L);
            xs.Add(rect.R);
            ys.Add(rect.T);
            ys.Add(rect.B);
        }

        var points = new List<(long X, long Y)>();
        var nodeByPoint = new Dictionary<(long X, long Y), int>();
        foreach (var y in ys)
        foreach (var x in xs)
        {
            var point = (x, y);
            if (point != start && point != end && IsPointInsideObstacle(point, rects))
                continue;

            nodeByPoint[point] = points.Count;
            points.Add(point);
        }

        if (!nodeByPoint.TryGetValue(start, out var startNode)
            || !nodeByPoint.TryGetValue(end, out var endNode))
            return null;

        var edges = Enumerable.Range(0, points.Count)
            .Select(_ => new List<int>())
            .ToArray();
        for (var left = 0; left < points.Count; left++)
        for (var right = left + 1; right < points.Count; right++)
        {
            var first = points[left];
            var second = points[right];
            if ((first.X != second.X && first.Y != second.Y)
                || !IsSegmentClear(first, second, rects))
                continue;

            edges[left].Add(right);
            edges[right].Add(left);
        }

        var requiredFirstDirection = DirectionCode(exitDirection);
        var requiredLastDirection = DirectionCode(Opposite(entryDirection));
        var initial = new RouteState(startNode, 0);
        var distances = new Dictionary<RouteState, double> { [initial] = 0 };
        var previous = new Dictionary<RouteState, RouteState>();
        var queue = new PriorityQueue<RouteState, double>();
        queue.Enqueue(initial, 0);

        RouteState? completed = null;
        while (queue.TryDequeue(out var current, out var priority))
        {
            if (!distances.TryGetValue(current, out var currentDistance)
                || priority > currentDistance)
                continue;
            if (current.Node == endNode)
            {
                completed = current;
                break;
            }

            foreach (var nextNode in edges[current.Node])
            {
                var direction = SegmentDirection(points[current.Node], points[nextNode]);
                if (direction == 0)
                    continue;
                if (current.Node == startNode
                    && requiredFirstDirection != 0
                    && direction != requiredFirstDirection)
                    continue;
                if (nextNode == endNode
                    && requiredLastDirection != 0
                    && direction != requiredLastDirection)
                    continue;

                var next = new RouteState(
                    nextNode,
                    direction is 1 or 2 ? 1 : 2);
                var segmentLength = Math.Abs(points[current.Node].X - points[nextNode].X)
                    + Math.Abs(points[current.Node].Y - points[nextNode].Y);
                var bendPenalty = current.Direction != 0 && current.Direction != next.Direction
                    ? 1_000_000d
                    : 0d;
                var candidateDistance = currentDistance + segmentLength + bendPenalty;
                if (distances.TryGetValue(next, out var knownDistance)
                    && knownDistance <= candidateDistance)
                    continue;

                distances[next] = candidateDistance;
                previous[next] = current;
                queue.Enqueue(next, candidateDistance);
            }
        }

        if (completed is not { } final)
            return null;

        var reversed = new List<(long X, long Y)> { points[final.Node] };
        var cursor = final;
        while (previous.TryGetValue(cursor, out var prior))
        {
            reversed.Add(points[prior.Node]);
            cursor = prior;
        }
        reversed.Reverse();
        return SimplifyRoute(reversed);
    }

    private static (long L, long T, long R, long B) InflateObstacle(
        (long L, long T, long R, long B) rect) =>
        (rect.L == long.MinValue ? rect.L : rect.L - 1,
         rect.T == long.MinValue ? rect.T : rect.T - 1,
         rect.R == long.MaxValue ? rect.R : rect.R + 1,
         rect.B == long.MaxValue ? rect.B : rect.B + 1);

    private static bool IsRouteClear(
        IReadOnlyList<(long X, long Y)> route,
        IReadOnlyList<(long L, long T, long R, long B)> obstacles)
    {
        for (var index = 1; index < route.Count; index++)
        {
            if (!IsSegmentClear(route[index - 1], route[index], obstacles))
                return false;
        }

        return true;
    }

    private static bool IsPointInsideObstacle(
        (long X, long Y) point,
        IReadOnlyList<(long L, long T, long R, long B)> obstacles) =>
        obstacles.Any(rect => point.X > rect.L && point.X < rect.R
            && point.Y > rect.T && point.Y < rect.B);

    private static bool IsSegmentClear(
        (long X, long Y) first,
        (long X, long Y) second,
        IReadOnlyList<(long L, long T, long R, long B)> obstacles)
    {
        if (first.X != second.X && first.Y != second.Y)
            return false;
        if (first == second)
            return true;

        foreach (var rect in obstacles)
        {
            if (first.Y == second.Y
                && first.Y > rect.T && first.Y < rect.B
                && Math.Max(first.X, second.X) > rect.L
                && Math.Min(first.X, second.X) < rect.R)
                return false;
            if (first.X == second.X
                && first.X > rect.L && first.X < rect.R
                && Math.Max(first.Y, second.Y) > rect.T
                && Math.Min(first.Y, second.Y) < rect.B)
                return false;
        }

        return true;
    }

    private static int SegmentDirection((long X, long Y) first, (long X, long Y) second) =>
        first.Y == second.Y
            ? second.X >= first.X ? 1 : 2
            : second.Y >= first.Y ? 3 : 4;

    private static int DirectionCode(Direction direction) => direction switch
    {
        Direction.Right => 1,
        Direction.Left => 2,
        Direction.Down => 3,
        Direction.Up => 4,
        _ => 0,
    };

    private static Direction Opposite(Direction direction) => direction switch
    {
        Direction.Right => Direction.Left,
        Direction.Left => Direction.Right,
        Direction.Down => Direction.Up,
        Direction.Up => Direction.Down,
        _ => direction,
    };

    private static List<(long X, long Y)> SimplifyRoute(
        IReadOnlyList<(long X, long Y)> route)
    {
        var simplified = new List<(long X, long Y)>();
        foreach (var point in route)
        {
            if (simplified.Count >= 2)
            {
                var previous = simplified[^1];
                var beforePrevious = simplified[^2];
                if ((beforePrevious.X == previous.X && previous.X == point.X)
                    || (beforePrevious.Y == previous.Y && previous.Y == point.Y))
                {
                    simplified[^1] = point;
                    continue;
                }
            }

            simplified.Add(point);
        }

        return simplified;
    }

    // ── Helper: shape rect from SlideShape ───────────────────────────────────────

    /// <summary>
    /// Builds the shape rect tuple from a <see cref="SlideShape"/>.
    /// Returns null when the shape has zero extent (degenerate).
    /// </summary>
    public static (long L, long T, long R, long B)? RectOf(SlideShape? shape)
    {
        if (shape is null) return null;
        if (shape.ExtentCxEmu <= 0 || shape.ExtentCyEmu <= 0) return null;
        return (shape.OffsetXEmu,
                shape.OffsetYEmu,
                shape.OffsetXEmu + shape.ExtentCxEmu,
                shape.OffsetYEmu + shape.ExtentCyEmu);
    }
}

/// <summary>
/// Helpers for building connector-reroute commands.
/// Called from shape-mutation commands after the moved shape's new position is known.
/// </summary>
internal static class ConnectorRouter
{
    /// <summary>
    /// Finds all connectors on slide <paramref name="slideIndex"/> whose start or end is
    /// attached to <paramref name="movedShapeId"/>, resolves both endpoints from the
    /// slide's current shape positions, and returns one <see cref="UpdateConnectorBoundsCommand"/>
    /// per affected connector.
    ///
    /// Wave 26: for ElbowConnector shapes that have both endpoints attached, this also computes
    /// a clean Manhattan route and stores it as <see cref="SlideShape.ElbowRoute"/> via
    /// the command's Apply path.
    ///
    /// Call this AFTER the moved shape's position has been updated in the model so
    /// <see cref="ConnectionSiteHelper.Resolve"/> sees the new coordinates.
    /// </summary>
    internal static IEnumerable<UpdateConnectorBoundsCommand> BuildRerouteCommands(
        Presentation p, int slideIndex, uint movedShapeId)
    {
        if (slideIndex < 0 || slideIndex >= p.Slides.Count)
            yield break;

        var slide = p.Slides[slideIndex];

        foreach (var shape in ShapeHelper.All(p, slideIndex))
        {
            if (shape.Kind != SlideShapeKind.Connector) continue;
            if (shape.ConnectionStart is null && shape.ConnectionEnd is null) continue;

            bool startAttached = shape.ConnectionStart?.ShapeId == movedShapeId;
            bool endAttached   = shape.ConnectionEnd  ?.ShapeId == movedShapeId;
            if (!startAttached && !endAttached) continue;

            // Resolve both endpoints (whichever is attached uses the live slide shape).
            (long sx, long sy) = shape.ConnectionStart is not null
                ? ConnectionSiteHelper.Resolve(shape.ConnectionStart, slide)
                : (shape.OffsetXEmu, shape.OffsetYEmu);

            (long ex, long ey) = shape.ConnectionEnd is not null
                ? ConnectionSiteHelper.Resolve(shape.ConnectionEnd, slide)
                : (shape.OffsetXEmu + shape.ExtentCxEmu, shape.OffsetYEmu + shape.ExtentCyEmu);

            // Connector bounding box = axis-aligned rect covering both endpoints.
            long newX  = Math.Min(sx, ex);
            long newY  = Math.Min(sy, ey);
            long newCx = Math.Max(Math.Abs(ex - sx), 1L); // minimum 1 EMU to keep valid
            long newCy = Math.Max(Math.Abs(ey - sy), 1L);

            // Wave 26: compute elbow route for ElbowConnector shapes with both ends attached.
            List<(long X, long Y)>? elbowRoute = null;
            if (shape.AutoShapeKind == DrawingShapeKind.ElbowConnector
                && shape.ConnectionStart is not null
                && shape.ConnectionEnd is not null)
            {
                var startShape = ShapeHelper.Find(p, slideIndex, shape.ConnectionStart.ShapeId);
                var endShape   = ShapeHelper.Find(p, slideIndex, shape.ConnectionEnd.ShapeId);
                var endpointIds = new HashSet<uint>
                {
                    shape.ConnectionStart.ShapeId,
                    shape.ConnectionEnd.ShapeId,
                };
                var obstacles = ShapeHelper.All(p, slideIndex)
                    .Where(candidate => candidate.Kind != SlideShapeKind.Connector
                        && !endpointIds.Contains(candidate.Id))
                    .Select(ElbowRouter.RectOf)
                    .Where(rect => rect.HasValue)
                    .Select(rect => rect!.Value)
                    .ToArray();
                elbowRoute = ElbowRouter.Route(
                    (sx, sy), (ex, ey),
                    ElbowRouter.RectOf(startShape),
                    ElbowRouter.RectOf(endShape),
                    obstacles);
            }

            yield return new UpdateConnectorBoundsCommand(slideIndex, shape.Id, newX, newY, newCx, newCy, elbowRoute);
        }
    }
}
