namespace FreeP.App.Compositor;

public sealed record SlideShowMaskPoint(double X, double Y);

public sealed record SlideShowMaskRect(double X, double Y, double Width, double Height);

public sealed record SlideShowMaskRectPair(SlideShowMaskRect Closed, SlideShowMaskRect Open);

public sealed record SlideShowMaskRandomBarPlan(
    SlideShowMaskRectPair Geometry,
    int Order);

public sealed record SlideShowMaskEllipse(
    SlideShowMaskPoint Center,
    double RadiusX,
    double RadiusY);

public sealed record SlideShowMaskPolygon(IReadOnlyList<SlideShowMaskPoint> Points);

public sealed record SlideShowMaskStripPlan(
    bool IsFullyOpen,
    IReadOnlyList<SlideShowMaskPolygon> Polygons);

public sealed record SlideShowMaskArc(
    SlideShowMaskPoint Center,
    double Radius,
    SlideShowMaskPoint Start,
    SlideShowMaskPoint End,
    double SweepDegrees,
    bool IsLargeArc,
    bool IsClockwise);

public sealed record SlideShowMaskSweepPlan(
    bool IsFullyOpen,
    bool IsCollapsed,
    IReadOnlyList<SlideShowMaskArc> Arcs);

/// <summary>
/// Neutral geometry calculations used by both slideshow renderers.
/// Hosts convert these values into WPF or Avalonia geometry objects.
/// </summary>
public static class SlideShowMaskGeometryPlanner
{
    /// <summary>
    /// Returns the stable bar order used by both slideshow hosts. PowerPoint's
    /// Random Bars effect chooses a non-sequential order; keeping the
    /// permutation in the shared planner prevents WPF and Avalonia from
    /// diverging while retaining deterministic playback evidence.
    /// </summary>
    public static IReadOnlyList<int> BuildRandomBarsOrder(int bandCount)
    {
        var count = Math.Max(1, bandCount);
        var order = Enumerable.Range(0, count).ToArray();
        uint state = 0x9E3779B9u;
        for (var i = order.Length - 1; i > 0; i--)
        {
            state = state * 1664525u + 1013904223u;
            var swapIndex = (int)(state % (uint)(i + 1));
            (order[i], order[swapIndex]) = (order[swapIndex], order[i]);
        }

        return order;
    }

    public static IReadOnlyList<SlideShowMaskRandomBarPlan> BuildRandomBars(
        double width,
        double height,
        int bandCount,
        bool horizontal)
    {
        var count = Math.Max(1, bandCount);
        var order = BuildRandomBarsOrder(count);
        var rank = new int[count];
        for (var position = 0; position < order.Count; position++)
            rank[order[position]] = position;

        var bars = new SlideShowMaskRandomBarPlan[count];
        for (var index = 0; index < count; index++)
        {
            bars[index] = new(
                BuildBlindsBand(width, height, count, index, horizontal),
                rank[index]);
        }

        return bars;
    }

    public static SlideShowMaskRectPair BuildBlindsBand(
        double width,
        double height,
        int bandCount,
        int index,
        bool horizontal)
    {
        if (horizontal)
        {
            var y = height * index / bandCount;
            var nextY = height * (index + 1) / bandCount;
            return new(
                new SlideShowMaskRect(0, y, width, 0),
                new SlideShowMaskRect(0, y, width, Math.Max(0, nextY - y)));
        }

        var x = width * index / bandCount;
        var nextX = width * (index + 1) / bandCount;
        return new(
            new SlideShowMaskRect(x, 0, 0, height),
            new SlideShowMaskRect(x, 0, Math.Max(0, nextX - x), height));
    }

    /// <summary>Builds the open portion of each band at a normalized progress.</summary>
    public static IReadOnlyList<SlideShowMaskRect> BuildBlindsTransitionRects(
        double width,
        double height,
        int bandCount,
        double progress,
        bool horizontal)
    {
        progress = Math.Clamp(progress, 0, 1);
        var count = Math.Max(1, bandCount);
        var rects = new SlideShowMaskRect[count];
        for (var index = 0; index < count; index++)
        {
            var band = BuildBlindsBand(width, height, count, index, horizontal);
            rects[index] = new(
                band.Closed.X + (band.Open.X - band.Closed.X) * progress,
                band.Closed.Y + (band.Open.Y - band.Closed.Y) * progress,
                band.Closed.Width + (band.Open.Width - band.Closed.Width) * progress,
                band.Closed.Height + (band.Open.Height - band.Closed.Height) * progress);
        }
        return rects;
    }

    /// <summary>
    /// Builds randomized bars at a shared normalized timeline. Each bar uses
    /// its deterministic shuffled order, so both hosts reveal the same bands.
    /// </summary>
    public static IReadOnlyList<SlideShowMaskRect> BuildRandomBarsTransitionRects(
        double width,
        double height,
        int bandCount,
        double progress,
        bool horizontal)
    {
        progress = Math.Clamp(progress, 0, 1);
        var bars = BuildRandomBars(width, height, bandCount, horizontal);
        var count = Math.Max(1, bars.Count);
        var rects = new SlideShowMaskRect[count];
        for (var index = 0; index < bars.Count; index++)
        {
            var bar = bars[index];
            var localProgress = Math.Clamp(progress * (count + 1) - bar.Order, 0, 1);
            var closed = bar.Geometry.Closed;
            var open = bar.Geometry.Open;
            rects[index] = new(
                closed.X + (open.X - closed.X) * localProgress,
                closed.Y + (open.Y - closed.Y) * localProgress,
                closed.Width + (open.Width - closed.Width) * localProgress,
                closed.Height + (open.Height - closed.Height) * localProgress);
        }
        return rects;
    }

    /// <summary>
    /// Returns the deterministic tile reveal used by a slide-level Dissolve.
    /// The shared order keeps WPF and Avalonia on the same visible tile at each
    /// playback checkpoint while retaining a genuinely discrete dissolve mask.
    /// </summary>
    public static IReadOnlyList<SlideShowMaskRect> BuildDissolveTransitionRects(
        double width,
        double height,
        int rowCount,
        int columnCount,
        double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        var rows = Math.Max(1, rowCount);
        var columns = Math.Max(1, columnCount);
        var tileCount = rows * columns;
        var revealCount = progress <= 0
            ? 0
            : progress >= 0.999
                ? tileCount
                : Math.Clamp((int)Math.Ceiling(progress * tileCount), 0, tileCount);
        if (revealCount == 0)
            return Array.Empty<SlideShowMaskRect>();

        var tileWidth = width / columns;
        var tileHeight = height / rows;
        var order = BuildRandomBarsOrder(tileCount);
        var rects = new SlideShowMaskRect[revealCount];
        for (var position = 0; position < revealCount; position++)
        {
            var tile = order[position];
            var row = tile / columns;
            var column = tile % columns;
            rects[position] = new SlideShowMaskRect(
                column * tileWidth,
                row * tileHeight,
                column == columns - 1 ? width - column * tileWidth : tileWidth,
                row == rows - 1 ? height - row * tileHeight : tileHeight);
        }

        return rects;
    }

    /// <summary>Builds the centered rectangular reveal used by a slide Box transition.</summary>
    public static SlideShowMaskRect BuildBoxTransitionRect(
        double width,
        double height,
        double progress,
        bool expandsFromCenter)
    {
        progress = Math.Clamp(progress, 0, 1);
        var scale = expandsFromCenter ? progress : 1 - progress;
        return new(
            width * (1 - scale) / 2,
            height * (1 - scale) / 2,
            width * scale,
            height * scale);
    }

    /// <summary>Builds the edge clip used by a directional slide Reveal transition.</summary>
    public static SlideShowMaskRect BuildRevealTransitionRect(
        double width,
        double height,
        double progress,
        double incomingOffsetX,
        double incomingOffsetY)
    {
        progress = Math.Clamp(progress, 0, 1);
        if (Math.Abs(incomingOffsetX) >= Math.Abs(incomingOffsetY))
        {
            var x = incomingOffsetX < 0 ? width * (1 - progress) : 0;
            return new(x, 0, width * progress, height);
        }

        var y = incomingOffsetY < 0 ? height * (1 - progress) : 0;
        return new(0, y, width, height * progress);
    }

    /// <summary>
    /// Builds the shrinking outgoing-slide clip used by an Uncover transition.
    /// The incoming slide is already underneath the outgoing snapshot; the
    /// snapshot contracts toward the configured travel edge until it disappears.
    /// </summary>
    public static SlideShowMaskRect BuildUncoverTransitionRect(
        double width,
        double height,
        double progress,
        double incomingOffsetX,
        double incomingOffsetY)
    {
        progress = Math.Clamp(progress, 0, 1);
        if (Math.Abs(incomingOffsetX) >= Math.Abs(incomingOffsetY))
        {
            var x = incomingOffsetX > 0 ? width * progress : 0;
            return new(x, 0, width * (1 - progress), height);
        }

        var y = incomingOffsetY > 0 ? height * progress : 0;
        return new(0, y, width, height * (1 - progress));
    }

    /// <summary>
    /// Returns the two panels used by a slide split transition. For an
    /// outgoing split the panels open from the center; for an incoming split
    /// they open inward from the two outside edges.
    /// </summary>
    public static IReadOnlyList<SlideShowMaskRect> BuildSplitRects(
        double width,
        double height,
        double progress,
        bool horizontal,
        bool fromCenter)
    {
        progress = Math.Clamp(progress, 0, 1);
        if (horizontal)
        {
            var half = width / 2;
            var extent = half * progress;
            return fromCenter
                ? [
                    new SlideShowMaskRect(half - extent, 0, extent, height),
                    new SlideShowMaskRect(half, 0, extent, height)
                ]
                : [
                    new SlideShowMaskRect(0, 0, extent, height),
                    new SlideShowMaskRect(width - extent, 0, extent, height)
                ];
        }

        var halfHeight = height / 2;
        var verticalExtent = halfHeight * progress;
        return fromCenter
            ? [
                new SlideShowMaskRect(0, halfHeight - verticalExtent, width, verticalExtent),
                new SlideShowMaskRect(0, halfHeight, width, verticalExtent)
            ]
            : [
                new SlideShowMaskRect(0, 0, width, verticalExtent),
                new SlideShowMaskRect(0, height - verticalExtent, width, verticalExtent)
            ];
    }

    public static SlideShowMaskRectPair BuildCheckerboardCell(
        double width,
        double height,
        int rowCount,
        int columnCount,
        int row,
        int column,
        bool horizontal)
    {
        var x = width * column / columnCount;
        var nextX = width * (column + 1) / columnCount;
        var y = height * row / rowCount;
        var nextY = height * (row + 1) / rowCount;
        var cellWidth = Math.Max(0, nextX - x);
        var cellHeight = Math.Max(0, nextY - y);

        return horizontal
            ? new(
                new SlideShowMaskRect(x, y, 0, cellHeight),
                new SlideShowMaskRect(x, y, cellWidth, cellHeight))
            : new(
                new SlideShowMaskRect(x, y, cellWidth, 0),
                new SlideShowMaskRect(x, y, cellWidth, cellHeight));
    }

    public static bool IsSecondCheckerboardPhase(int row, int column) =>
        ((row + column) & 1) == 1;

    public static SlideShowMaskEllipse BuildCircle(
        double width,
        double height,
        double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        return new(
            new SlideShowMaskPoint(width / 2, height / 2),
            width / 2 * progress,
            height / 2 * progress);
    }

    public static IReadOnlyList<SlideShowMaskPoint> BuildDiamond(
        double width,
        double height,
        double progress)
    {
        return Enumerable.Range(0, 4)
            .Select(vertexIndex => BuildDiamondPoint(width, height, vertexIndex, progress))
            .ToArray();
    }

    public static SlideShowMaskPoint BuildDiamondPoint(
        double width,
        double height,
        int vertexIndex,
        double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        var center = new SlideShowMaskPoint(width / 2, height / 2);
        var full = vertexIndex switch
        {
            0 => new SlideShowMaskPoint(width / 2, 0),
            1 => new SlideShowMaskPoint(width, height / 2),
            2 => new SlideShowMaskPoint(width / 2, height),
            _ => new SlideShowMaskPoint(0, height / 2)
        };

        return new(
            center.X + (full.X - center.X) * progress,
            center.Y + (full.Y - center.Y) * progress);
    }

    public static SlideShowMaskRectPair BuildPlusRects(
        double width,
        double height,
        double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        var verticalWidth = width * progress;
        var horizontalHeight = height * progress;
        return new(
            new SlideShowMaskRect((width - verticalWidth) / 2, 0, verticalWidth, height),
            new SlideShowMaskRect(0, (height - horizontalHeight) / 2, width, horizontalHeight));
    }

    public static SlideShowMaskStripPlan BuildStrips(
        double width,
        double height,
        double progress,
        int stripCount,
        bool slopeDown)
    {
        progress = Math.Clamp(progress, 0, 1);
        if (progress >= 0.999)
        {
            return new(true, Array.Empty<SlideShowMaskPolygon>());
        }

        var bands = Math.Max(1, stripCount);
        var bandWidth = width / bands;
        var diagonalShift = height;
        var openWidth = bandWidth + diagonalShift;
        var polygons = new SlideShowMaskPolygon[bands];

        for (var band = 0; band < bands; band++)
        {
            var x0 = band * bandWidth - diagonalShift;
            var x1 = x0 + openWidth * progress;
            polygons[band] = new(BuildStripPoints(x0, x1, height, diagonalShift, slopeDown));
        }

        return new(false, polygons);
    }

    public static IReadOnlyList<SlideShowMaskPoint> BuildStripPoints(
        double x0,
        double x1,
        double height,
        double diagonalShift,
        bool slopeDown) =>
        slopeDown
            ? new[]
            {
                new SlideShowMaskPoint(x0, 0),
                new SlideShowMaskPoint(x1, 0),
                new SlideShowMaskPoint(x1 + diagonalShift, height),
                new SlideShowMaskPoint(x0 + diagonalShift, height)
            }
            : new[]
            {
                new SlideShowMaskPoint(x0 + diagonalShift, 0),
                new SlideShowMaskPoint(x1 + diagonalShift, 0),
                new SlideShowMaskPoint(x1, height),
                new SlideShowMaskPoint(x0, height)
            };

    public static SlideShowMaskSweepPlan BuildWedge(
        double width,
        double height,
        double progress)
    {
        progress = Math.Clamp(progress, 0, 1);
        var center = new SlideShowMaskPoint(width / 2, height / 2);
        if (progress >= 0.999)
        {
            return new(true, false, Array.Empty<SlideShowMaskArc>());
        }

        if (progress <= 0)
        {
            return new(false, true, Array.Empty<SlideShowMaskArc>());
        }

        var radius = Math.Sqrt(width * width + height * height) / 2;
        return new(false, false, new[] { BuildArc(center, radius, -90, 360 * progress, clockwise: true) });
    }

    public static SlideShowMaskSweepPlan BuildWheel(
        double width,
        double height,
        double progress,
        int spokeCount,
        bool clockwise = true)
    {
        progress = Math.Clamp(progress, 0, 1);
        var center = new SlideShowMaskPoint(width / 2, height / 2);
        if (progress >= 0.999)
        {
            return new(true, false, Array.Empty<SlideShowMaskArc>());
        }

        if (progress <= 0)
        {
            return new(false, true, Array.Empty<SlideShowMaskArc>());
        }

        var radius = Math.Sqrt(width * width + height * height) / 2;
        var spokes = Math.Max(1, spokeCount);
        var spokeSweep = 360.0 / spokes;
        var arcs = new SlideShowMaskArc[spokes];
        for (var spoke = 0; spoke < spokes; spoke++)
        {
            arcs[spoke] = BuildArc(
                center,
                radius,
                clockwise
                    ? -90 + spoke * spokeSweep
                    : -90 - spoke * spokeSweep,
                (clockwise ? 1 : -1) * spokeSweep * progress,
                clockwise);
        }

        return new(false, false, arcs);
    }

    private static SlideShowMaskArc BuildArc(
        SlideShowMaskPoint center,
        double radius,
        double startDegrees,
        double sweepDegrees,
        bool clockwise)
    {
        var start = PointOnRadius(center, radius, startDegrees);
        var end = PointOnRadius(center, radius, startDegrees + sweepDegrees);
        return new(
            center,
            radius,
            start,
            end,
            Math.Abs(sweepDegrees),
            Math.Abs(sweepDegrees) > 180,
            clockwise);
    }

    private static SlideShowMaskPoint PointOnRadius(
        SlideShowMaskPoint center,
        double radius,
        double degrees)
    {
        var radians = degrees * Math.PI / 180;
        return new(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }
}
