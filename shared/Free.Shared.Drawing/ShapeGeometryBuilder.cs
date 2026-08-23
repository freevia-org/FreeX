namespace Free.Shared.Drawing;

/// <summary>
/// Portable, framework-free builder for drawing-shape outlines. Given a <see cref="DrawingShapeKind"/>
/// and a bounds rectangle, it returns pure vertex/segment geometry (see <see cref="ShapeGeometry"/>)
/// faithful to the source factory's math: polygons are listed as fractional vertices of the bounds,
/// rounded forms use elliptical arcs, and crossing bars are emitted as rotated-rectangle vertices.
/// The renderers convert the result into their own paths.
///
/// Bounds with negative width/height are normalized to a positive-size rectangle before layout
/// (the equivalent of a horizontal/vertical flip about the rectangle's center), matching how the
/// hosts hand flipped shapes to this math.
///
/// Ported from FreeX.App.Presentation.Shapes.ShapeGeometryBuilder (45 presets).
/// </summary>
public static class ShapeGeometryBuilder
{
    /// <summary>Builds the outline of <paramref name="kind"/> within <paramref name="bounds"/>.</summary>
    public static ShapeGeometry Build(DrawingShapeKind kind, LayoutRect bounds) =>
        Build(kind, bounds, adjustments: null);

    /// <summary>
    /// Builds a preset shape while honoring DrawingML adjustment guides when supplied.
    /// Values are the raw guide values stored by OOXML (for chord, 1/60000 degree).
    /// </summary>
    public static ShapeGeometry Build(
        DrawingShapeKind kind,
        LayoutRect bounds,
        IReadOnlyDictionary<string, double>? adjustments)
    {
        var rect = Normalize(bounds);

        // For line-like shapes allow one dimension to be zero (e.g. a perfectly horizontal or
        // vertical line has zero height or zero width in its bounding box).
        if (DrawingShapeKindSupport.IsLineLike(kind))
        {
            if (rect.Width <= 0 && rect.Height <= 0)
                return ShapeGeometry.Empty;

            // Flat horizontal line (cy=0): draw left→right across the full width.
            if (rect.Height <= 0)
                return Single(new ShapeContour(
                    new LayoutPoint(rect.Left, rect.Top),
                    [ShapeSegment.LineTo(new LayoutPoint(rect.Right, rect.Top))],
                    Closed: false, Filled: false));

            // Flat vertical line (cx=0): draw top→bottom across the full height.
            if (rect.Width <= 0)
                return Single(new ShapeContour(
                    new LayoutPoint(rect.Left, rect.Top),
                    [ShapeSegment.LineTo(new LayoutPoint(rect.Left, rect.Bottom))],
                    Closed: false, Filled: false));
        }
        else if (rect.Width <= 0 || rect.Height <= 0)
        {
            return ShapeGeometry.Empty;
        }

        return kind switch
        {
            DrawingShapeKind.RoundedRectangle => RoundedRectangle(rect, CornerRadius(rect, adjustments)),
            DrawingShapeKind.Ellipse => Ellipse(rect),
            DrawingShapeKind.Line => LinePath(rect),
            DrawingShapeKind.ElbowConnector => ElbowPath(rect),
            DrawingShapeKind.CurvedConnector => CurvedConnector(rect),
            DrawingShapeKind.Triangle => Triangle(rect, adjustments),
            DrawingShapeKind.RightTriangle => Polygon(rect, [(0, 0), (1, 1), (0, 1)]),
            DrawingShapeKind.Diamond => Polygon(rect, [(0.5, 0), (1, 0.5), (0.5, 1), (0, 0.5)]),
            DrawingShapeKind.Parallelogram => Parallelogram(rect, adjustments),
            DrawingShapeKind.Trapezoid => Trapezoid(rect, adjustments),
            DrawingShapeKind.Pentagon => Polygon(rect, [(0.5, 0), (1, 0.38), (0.82, 1), (0.18, 1), (0, 0.38)]),
            DrawingShapeKind.Hexagon => Polygon(rect, [(0.25, 0), (0.75, 0), (1, 0.5), (0.75, 1), (0.25, 1), (0, 0.5)]),
            DrawingShapeKind.Octagon => Polygon(rect, [(0.3, 0), (0.7, 0), (1, 0.3), (1, 0.7), (0.7, 1), (0.3, 1), (0, 0.7), (0, 0.3)]),
            DrawingShapeKind.Cross or DrawingShapeKind.PlusSign => Plus(rect, adjustments),
            DrawingShapeKind.RightArrow => Arrow(rect, adjustments, DrawingShapeKind.RightArrow),
            DrawingShapeKind.LeftArrow => Arrow(rect, adjustments, DrawingShapeKind.LeftArrow),
            DrawingShapeKind.UpArrow => Arrow(rect, adjustments, DrawingShapeKind.UpArrow),
            DrawingShapeKind.DownArrow => Arrow(rect, adjustments, DrawingShapeKind.DownArrow),
            DrawingShapeKind.LeftRightArrow => CompoundArrow(rect, adjustments, vertical: false),
            DrawingShapeKind.UpDownArrow => CompoundArrow(rect, adjustments, vertical: true),
            DrawingShapeKind.MinusSign => Minus(rect),
            DrawingShapeKind.MultiplySign => Multiply(rect),
            DrawingShapeKind.DivideSign => Divide(rect),
            DrawingShapeKind.EqualSign => Equal(rect),
            DrawingShapeKind.NotEqualSign => NotEqual(rect),
            DrawingShapeKind.FlowchartDecision => Polygon(rect, [(0.5, 0), (1, 0.5), (0.5, 1), (0, 0.5)]),
            DrawingShapeKind.FlowchartData => Polygon(rect, [(0.22, 0), (1, 0), (0.78, 1), (0, 1)]),
            DrawingShapeKind.FlowchartPredefinedProcess => FlowchartPredefinedProcess(rect),
            DrawingShapeKind.FlowchartDocument => FlowchartDocument(rect),
            DrawingShapeKind.FlowchartTerminator => RoundedRectangle(rect, Math.Max(1, rect.Height / 2)),
            DrawingShapeKind.Star5 => Star(rect, 5, StarInnerRadius(adjustments, 0.42)),
            DrawingShapeKind.Star8 => Star(rect, 8, StarInnerRadius(adjustments, 0.46)),
            DrawingShapeKind.Explosion => Star(
                rect,
                12,
                StarInnerRadius(adjustments, 0.62),
                startAngle: (-Math.PI / 2) + 0.08),
            DrawingShapeKind.Ribbon => Ribbon(rect, adjustments),
            DrawingShapeKind.Wave => Wave(rect, adjustments),
            DrawingShapeKind.RectangularCallout => RectangularCallout(rect),
            DrawingShapeKind.RoundedRectangularCallout => RoundedCallout(rect),
            DrawingShapeKind.OvalCallout => OvalCallout(rect),
            DrawingShapeKind.LineCallout => LineCallout(rect),
            DrawingShapeKind.Chevron => Chevron(rect, adjustments),
            DrawingShapeKind.HomePlate => HomePlate(rect, adjustments),
            DrawingShapeKind.Cylinder => CylinderShape(rect, adjustments),
            DrawingShapeKind.Chord => Chord(rect, adjustments),
            DrawingShapeKind.Heart => Heart(rect),
            DrawingShapeKind.QuadArrow => QuadArrow(rect, adjustments),
            _ => Rectangle(rect)
        };
    }

    private static ShapeGeometry Heart(LayoutRect rect)
    {
        var start = P(rect, 0.5, 0.22);
        ShapeSegment[] segments =
        [
            ShapeSegment.BezierTo(P(rect, 0.58, 0.04), P(rect, 0.80, 0.02), P(rect, 1.0, 0.20)),
            ShapeSegment.BezierTo(P(rect, 1.0, 0.55), P(rect, 0.78, 0.72), P(rect, 0.64, 0.85)),
            ShapeSegment.BezierTo(P(rect, 0.58, 0.91), P(rect, 0.53, 0.97), P(rect, 0.5, 1.0)),
            ShapeSegment.BezierTo(P(rect, 0.47, 0.97), P(rect, 0.42, 0.91), P(rect, 0.36, 0.85)),
            ShapeSegment.BezierTo(P(rect, 0.22, 0.72), P(rect, 0.0, 0.55), P(rect, 0.0, 0.20)),
            ShapeSegment.BezierTo(P(rect, 0.20, 0.02), P(rect, 0.42, 0.04), start)
        ];
        return Single(new ShapeContour(start, segments, Closed: true, Filled: true));
    }

    private static ShapeGeometry QuadArrow(
        LayoutRect rect,
        IReadOnlyDictionary<string, double>? adjustments)
    {
        // The cached SmartArt matrix2 axis is the native quadArrow preset. Its
        // authored guides are intentionally bounded to the standard axis grammar;
        // preserve the same four-way silhouette for both desktop renderers.
        // quadArrow's native guides are angular proportions, not the direct
        // shaft/head fractions used by the directional-arrow presets.
        const double shaft = 0.12;
        const double head = 0.22;
        var center = 0.5;
        var shaftStart = center - shaft / 2;
        var shaftEnd = center + shaft / 2;
        var headStart = center - head;
        var headEnd = center + head;

        return Polygon(rect,
        [
            (center, 0), (headEnd, headStart), (shaftEnd, headStart),
            (shaftEnd, shaftStart), (headEnd, shaftStart), (1, center),
            (headEnd, shaftEnd), (shaftEnd, shaftEnd), (shaftEnd, headEnd),
            (center, 1), (headStart, headEnd), (shaftStart, headEnd),
            (shaftStart, shaftEnd), (headStart, shaftEnd), (0, center),
            (headStart, shaftStart), (shaftStart, shaftStart), (shaftStart, headStart)
        ]);
    }

    private static LayoutRect Normalize(LayoutRect bounds) =>
        LayoutRect.FromCorners(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);

    private static double CornerRadius(
        LayoutRect rect,
        IReadOnlyDictionary<string, double>? adjustments = null)
    {
        // Keep the established fallback for newly-created and legacy shapes. An authored
        // DrawingML roundRect guide is a 0..50000 fraction of the smaller dimension.
        if (adjustments is null || !adjustments.TryGetValue("adj", out var adjustment))
            return Math.Clamp(Math.Min(rect.Width, rect.Height) * 0.18, 2, 18);

        return Math.Min(rect.Width, rect.Height) * Math.Clamp(adjustment, 0, 50000) / 100000.0;
    }

    private static LayoutPoint P(LayoutRect rect, double x, double y) =>
        new(rect.Left + (rect.Width * x), rect.Top + (rect.Height * y));

    private static ShapeGeometry Parallelogram(
        LayoutRect rect,
        IReadOnlyDictionary<string, double>? adjustments)
    {
        var inset = SlantInset(rect, adjustments);
        return Polygon(rect,
        [
            (inset / rect.Width, 0), (1, 0),
            (1 - inset / rect.Width, 1), (0, 1),
        ]);
    }

    private static ShapeGeometry Trapezoid(
        LayoutRect rect,
        IReadOnlyDictionary<string, double>? adjustments)
    {
        var inset = SlantInset(rect, adjustments);
        return Polygon(rect,
        [
            (inset / rect.Width, 0), (1 - inset / rect.Width, 0),
            (1, 1), (0, 1),
        ]);
    }

    private static double SlantInset(
        LayoutRect rect,
        IReadOnlyDictionary<string, double>? adjustments)
    {
        // Keep the legacy outline for new/old shapes without an authored guide. DrawingML
        // adjustment values use the smaller shape dimension as their reference length.
        if (adjustments is null || !adjustments.TryGetValue("adj", out var adjustment))
            return rect.Width * 0.2;

        var maximumInset = rect.Width / 2;
        var inset = Math.Min(rect.Width, rect.Height) * Math.Clamp(adjustment, 0, 100000) / 100000.0;
        return Math.Clamp(inset, 0, maximumInset);
    }

    private static ShapeGeometry Single(ShapeContour contour) => new([contour]);

    private static ShapeGeometry Polygon(LayoutRect rect, IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count == 0)
            return ShapeGeometry.Empty;

        var start = P(rect, points[0].X, points[0].Y);
        var segments = new ShapeSegment[points.Count - 1];
        for (var i = 1; i < points.Count; i++)
            segments[i - 1] = ShapeSegment.LineTo(P(rect, points[i].X, points[i].Y));

        return Single(new ShapeContour(start, segments, Closed: true, Filled: true));
    }

    private static ShapeContour PolygonContour(LayoutRect rect, IReadOnlyList<(double X, double Y)> points)
    {
        var start = P(rect, points[0].X, points[0].Y);
        var segments = new ShapeSegment[points.Count - 1];
        for (var i = 1; i < points.Count; i++)
            segments[i - 1] = ShapeSegment.LineTo(P(rect, points[i].X, points[i].Y));

        return new ShapeContour(start, segments, Closed: true, Filled: true);
    }

    private static ShapeGeometry OpenPath(LayoutRect rect, IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count == 0)
            return ShapeGeometry.Empty;

        return Single(OpenPathContour(rect, points));
    }

    private static ShapeContour OpenPathContour(LayoutRect rect, IReadOnlyList<(double X, double Y)> points)
    {
        var start = P(rect, points[0].X, points[0].Y);
        var segments = new ShapeSegment[points.Count - 1];
        for (var i = 1; i < points.Count; i++)
            segments[i - 1] = ShapeSegment.LineTo(P(rect, points[i].X, points[i].Y));

        return new ShapeContour(start, segments, Closed: false, Filled: false);
    }

    private static ShapeGeometry Rectangle(LayoutRect rect) => Single(RectangleContour(rect));

    private static ShapeGeometry Triangle(
        LayoutRect rect,
        IReadOnlyDictionary<string, double>? adjustments)
    {
        // DrawingML's triangle guide moves the apex along the top edge.  The
        // default 50000 value preserves the centered triangle used by newly
        // created shapes and legacy packages without the guide.
        var apexX = GetAdjustment(adjustments, "adj", 50000) / 100000.0;
        return Polygon(rect, [(apexX, 0), (1, 1), (0, 1)]);
    }

    private static ShapeGeometry Arrow(
        LayoutRect rect,
        IReadOnlyDictionary<string, double>? adjustments,
        DrawingShapeKind direction)
    {
        // Preserve established FreeP outlines for legacy shapes that carry no
        // authored guide. DrawingML directional arrows use adj1 for shaft
        // thickness and adj2 for head length; once either guide is present,
        // consume the native 0..100000 values instead of flattening them.
        if (adjustments is null ||
            (!adjustments.ContainsKey("adj1") && !adjustments.ContainsKey("adj2")))
        {
            return direction switch
            {
                DrawingShapeKind.RightArrow => Polygon(rect, [(0, 0.25), (0.62, 0.25), (0.62, 0), (1, 0.5), (0.62, 1), (0.62, 0.75), (0, 0.75)]),
                DrawingShapeKind.LeftArrow => Polygon(rect, [(1, 0.25), (0.38, 0.25), (0.38, 0), (0, 0.5), (0.38, 1), (0.38, 0.75), (1, 0.75)]),
                DrawingShapeKind.UpArrow => Polygon(rect, [(0.25, 1), (0.25, 0.38), (0, 0.38), (0.5, 0), (1, 0.38), (0.75, 0.38), (0.75, 1)]),
                _ => Polygon(rect, [(0.25, 0), (0.75, 0), (0.75, 0.62), (1, 0.62), (0.5, 1), (0, 0.62), (0.25, 0.62)]),
            };
        }

        var shaftAdjustment = GetAdjustment(adjustments, "adj1", 50000);
        var headAdjustment = GetAdjustment(adjustments, "adj2", 50000);
        var shaftHalf = Math.Clamp(shaftAdjustment, 0, 100000) / 200000.0;
        var headBase = 1 - Math.Clamp(headAdjustment, 0, 100000) / 100000.0;
        return direction switch
        {
            DrawingShapeKind.RightArrow => Polygon(rect,
            [
                (0, 0.5 - shaftHalf), (headBase, 0.5 - shaftHalf), (headBase, 0),
                (1, 0.5), (headBase, 1), (headBase, 0.5 + shaftHalf), (0, 0.5 + shaftHalf),
            ]),
            DrawingShapeKind.LeftArrow => Polygon(rect,
            [
                (1, 0.5 - shaftHalf), (1 - headBase, 0.5 - shaftHalf), (1 - headBase, 0),
                (0, 0.5), (1 - headBase, 1), (1 - headBase, 0.5 + shaftHalf), (1, 0.5 + shaftHalf),
            ]),
            DrawingShapeKind.UpArrow => Polygon(rect,
            [
                (0.5 - shaftHalf, 1), (0.5 - shaftHalf, 1 - headBase), (0, 1 - headBase),
                (0.5, 0), (1, 1 - headBase), (0.5 + shaftHalf, 1 - headBase), (0.5 + shaftHalf, 1),
            ]),
            _ => Polygon(rect,
            [
                (0.5 + shaftHalf, 0), (0.5 + shaftHalf, headBase), (1, headBase),
                (0.5, 1), (0, headBase), (0.5 - shaftHalf, headBase), (0.5 - shaftHalf, 0),
            ]),
        };
    }

    private static ShapeGeometry Chevron(
        LayoutRect rect,
        IReadOnlyDictionary<string, double>? adjustments)
    {
        if (adjustments is null || !adjustments.ContainsKey("adj"))
            return Polygon(rect, [(0, 0), (0.76, 0), (1, 0.5), (0.76, 1), (0, 1), (0.24, 0.5)]);

        var maximum = 100000.0 * rect.Width / Math.Min(rect.Width, rect.Height);
        var depth = Math.Clamp(GetAdjustment(adjustments, "adj", 50000), 0, maximum);
        var x1 = Math.Min(rect.Width, rect.Height) * depth / 100000.0;
        var x2 = rect.Width - x1;
        return Polygon(rect,
        [(0, 0), (x2 / rect.Width, 0), (1, 0.5), (x2 / rect.Width, 1), (0, 1), (x1 / rect.Width, 0.5)]);
    }

    private static ShapeGeometry HomePlate(
        LayoutRect rect,
        IReadOnlyDictionary<string, double>? adjustments)
    {
        if (adjustments is null || !adjustments.ContainsKey("adj"))
            return Polygon(rect, [(0, 0), (0.76, 0), (1, 0.5), (0.76, 1), (0, 1)]);

        var maximum = 100000.0 * rect.Width / Math.Min(rect.Width, rect.Height);
        var depth = Math.Clamp(GetAdjustment(adjustments, "adj", 50000), 0, maximum);
        var x1 = rect.Width - Math.Min(rect.Width, rect.Height) * depth / 100000.0;
        return Polygon(rect,
        [(0, 0), (x1 / rect.Width, 0), (1, 0.5), (x1 / rect.Width, 1), (0, 1)]);
    }

    private static ShapeGeometry CompoundArrow(
        LayoutRect rect,
        IReadOnlyDictionary<string, double>? adjustments,
        bool vertical)
    {
        if (adjustments is null ||
            (!adjustments.ContainsKey("adj1") && !adjustments.ContainsKey("adj2")))
        {
            return vertical
                ? Polygon(rect, [(0.5, 0), (1, 0.25), (0.75, 0.25), (0.75, 0.75), (1, 0.75), (0.5, 1), (0, 0.75), (0.25, 0.75), (0.25, 0.25), (0, 0.25)])
                : Polygon(rect, [(0, 0.5), (0.25, 0), (0.25, 0.25), (0.75, 0.25), (0.75, 0), (1, 0.5), (0.75, 1), (0.75, 0.75), (0.25, 0.75), (0.25, 1)]);
        }

        var minimumDimension = Math.Min(rect.Width, rect.Height);
        var shaftHalf = minimumDimension * Math.Clamp(GetAdjustment(adjustments, "adj1", 50000), 0, 100000) / 200000.0;
        var headDepth = minimumDimension * Math.Clamp(
            GetAdjustment(adjustments, "adj2", 50000),
            0,
            100000.0 * (vertical ? rect.Height : rect.Width) / minimumDimension) / 100000.0;

        if (vertical)
        {
            var shaftHalfRatio = shaftHalf / rect.Width;
            var topBase = headDepth / rect.Height;
            var bottomBase = 1 - topBase;
            return Polygon(rect,
            [
                (0.5, 0), (1, topBase), (0.5 + shaftHalfRatio, topBase),
                (0.5 + shaftHalfRatio, bottomBase), (1, bottomBase), (0.5, 1),
                (0, bottomBase), (0.5 - shaftHalfRatio, bottomBase),
                (0.5 - shaftHalfRatio, topBase), (0, topBase),
            ]);
        }

        var shaftHalfRatioHorizontal = shaftHalf / rect.Height;
        var leftBase = headDepth / rect.Width;
        var rightBase = 1 - leftBase;
        return Polygon(rect,
        [
            (0, 0.5), (leftBase, 0), (leftBase, 0.5 - shaftHalfRatioHorizontal),
            (rightBase, 0.5 - shaftHalfRatioHorizontal), (rightBase, 0), (1, 0.5),
            (rightBase, 1), (rightBase, 0.5 + shaftHalfRatioHorizontal),
            (leftBase, 0.5 + shaftHalfRatioHorizontal), (leftBase, 1),
        ]);
    }

    private static ShapeContour RectangleContour(LayoutRect rect) =>
        PolygonContour(rect, [(0, 0), (1, 0), (1, 1), (0, 1)]);

    private static ShapeContour RectangleContour(double left, double top, double width, double height)
    {
        var start = new LayoutPoint(left, top);
        ShapeSegment[] segments =
        [
            ShapeSegment.LineTo(new LayoutPoint(left + width, top)),
            ShapeSegment.LineTo(new LayoutPoint(left + width, top + height)),
            ShapeSegment.LineTo(new LayoutPoint(left, top + height))
        ];
        return new ShapeContour(start, segments, Closed: true, Filled: true);
    }

    private static ShapeGeometry RoundedRectangle(LayoutRect rect, double radius) =>
        Single(RoundedRectangleContour(rect, radius));

    private static ShapeContour RoundedRectangleContour(LayoutRect rect, double radius)
    {
        var rx = Math.Min(radius, rect.Width / 2);
        var ry = Math.Min(radius, rect.Height / 2);
        var l = rect.Left;
        var t = rect.Top;
        var r = rect.Right;
        var b = rect.Bottom;

        var start = new LayoutPoint(l + rx, t);
        ShapeSegment[] segments =
        [
            ShapeSegment.LineTo(new LayoutPoint(r - rx, t)),
            ShapeSegment.ArcTo(new LayoutPoint(r, t + ry), rx, ry, sweepClockwise: true),
            ShapeSegment.LineTo(new LayoutPoint(r, b - ry)),
            ShapeSegment.ArcTo(new LayoutPoint(r - rx, b), rx, ry, sweepClockwise: true),
            ShapeSegment.LineTo(new LayoutPoint(l + rx, b)),
            ShapeSegment.ArcTo(new LayoutPoint(l, b - ry), rx, ry, sweepClockwise: true),
            ShapeSegment.LineTo(new LayoutPoint(l, t + ry)),
            ShapeSegment.ArcTo(new LayoutPoint(l + rx, t), rx, ry, sweepClockwise: true)
        ];
        return new ShapeContour(start, segments, Closed: true, Filled: true);
    }

    private static ShapeGeometry Ellipse(LayoutRect rect) => Single(EllipseContour(rect));

    private static ShapeContour EllipseContour(LayoutRect rect) =>
        EllipseContour(rect.Left, rect.Top, rect.Width, rect.Height);

    private static ShapeContour EllipseContour(double left, double top, double width, double height)
    {
        var rx = width / 2;
        var ry = height / 2;
        var cx = left + rx;
        var cy = top + ry;

        // Two semicircular arcs from the left extreme, across the right extreme, back to the start.
        var start = new LayoutPoint(cx - rx, cy);
        ShapeSegment[] segments =
        [
            ShapeSegment.ArcTo(new LayoutPoint(cx + rx, cy), rx, ry, sweepClockwise: true),
            ShapeSegment.ArcTo(new LayoutPoint(cx - rx, cy), rx, ry, sweepClockwise: true)
        ];
        return new ShapeContour(start, segments, Closed: true, Filled: true);
    }

    private static ShapeGeometry Chord(LayoutRect rect, IReadOnlyDictionary<string, double>? adjustments)
    {
        const double AngleUnitsPerDegree = 60000.0;
        var startDegrees = GetAdjustment(adjustments, "adj1", 0) / AngleUnitsPerDegree;
        var endDegrees = GetAdjustment(adjustments, "adj2", 180 * AngleUnitsPerDegree) / AngleUnitsPerDegree;
        var sweepDegrees = endDegrees - startDegrees;
        while (sweepDegrees < 0)
            sweepDegrees += 360;
        while (sweepDegrees > 360)
            sweepDegrees -= 360;

        // Equal start/end guides are PowerPoint's full-circle case. An ArcTo with
        // identical endpoints would be treated as an empty arc by both host APIs.
        if (sweepDegrees <= 0.001 || sweepDegrees >= 359.999)
            return Ellipse(rect);

        var rx = rect.Width / 2;
        var ry = rect.Height / 2;
        var cx = rect.Left + rx;
        var cy = rect.Top + ry;
        var startRadians = startDegrees * Math.PI / 180;
        var endRadians = (startDegrees + sweepDegrees) * Math.PI / 180;
        var start = new LayoutPoint(cx + rx * Math.Cos(startRadians), cy + ry * Math.Sin(startRadians));

        // Sample non-full-circle chords so WPF and Avalonia do not reinterpret the
        // preset angles through different ArcTo sweep conventions.
        int arcSegments = Math.Max(8, (int)Math.Ceiling(sweepDegrees / 15.0));
        var segments = new List<ShapeSegment>(arcSegments + 1);
        for (int index = 1; index <= arcSegments; index++)
        {
            var angle = startRadians + (endRadians - startRadians) * index / arcSegments;
            segments.Add(ShapeSegment.LineTo(new LayoutPoint(
                cx + rx * Math.Cos(angle),
                cy + ry * Math.Sin(angle))));
        }

        segments.Add(ShapeSegment.LineTo(start));
        return Single(new ShapeContour(start, segments, Closed: true, Filled: true));
    }

    private static double GetAdjustment(
        IReadOnlyDictionary<string, double>? adjustments,
        string name,
        double fallback) =>
        adjustments is not null && adjustments.TryGetValue(name, out var value) ? value : fallback;

    /// <summary>
    /// Renders a <see cref="DrawingShapeKind.Line"/> shape. The path direction is inferred from
    /// the bounding-box aspect ratio:
    /// <list type="bullet">
    ///   <item>Very wide (width/height &gt; 4): horizontal — draw left-to-right at vertical centre.</item>
    ///   <item>Very tall (height/width &gt; 4): vertical — draw top-to-bottom at horizontal centre.</item>
    ///   <item>Otherwise: diagonal — draw from top-left corner to bottom-right corner.</item>
    /// </list>
    /// Flip/rotation applied by the host renderer handle the remaining orientations (e.g. a
    /// bottom-left to top-right diagonal is a flipV of the normal diagonal).
    /// </summary>
    private static ShapeGeometry LinePath(LayoutRect rect)
    {
        const double FlatRatioThreshold = 4.0;
        var cx = rect.Width;
        var cy = rect.Height;

        // Horizontal line (cy ≈ 0 or very flat bounding box)
        if (cy <= 0 || (cx > 0 && cx / cy >= FlatRatioThreshold))
            return Single(new ShapeContour(
                new LayoutPoint(rect.Left, rect.Top + cy / 2),
                [ShapeSegment.LineTo(new LayoutPoint(rect.Right, rect.Top + cy / 2))],
                Closed: false, Filled: false));

        // Vertical line (cx ≈ 0 or very tall bounding box)
        if (cx <= 0 || cy / cx >= FlatRatioThreshold)
            return Single(new ShapeContour(
                new LayoutPoint(rect.Left + cx / 2, rect.Top),
                [ShapeSegment.LineTo(new LayoutPoint(rect.Left + cx / 2, rect.Bottom))],
                Closed: false, Filled: false));

        // Diagonal (aspect ratio near 1:1) — flip attributes on the host handle direction.
        return OpenPath(rect, [(0, 0), (1, 1)]);
    }

    /// <summary>
    /// Renders an <see cref="DrawingShapeKind.ElbowConnector"/> as a 3-segment orthogonal path
    /// from the top-left corner of the bounding box to the bottom-right corner, bending at the
    /// horizontal midpoint. The host renderer applies flip/rotation for other orientations.
    /// </summary>
    private static ShapeGeometry ElbowPath(LayoutRect rect) =>
        OpenPath(rect, [(0, 0), (0.5, 0), (0.5, 1), (1, 1)]);

    /// <summary>
    /// Renders a <see cref="DrawingShapeKind.CurvedConnector"/> as a single smooth cubic Bézier
    /// from the top-left corner of the bounding box to the bottom-right corner. Control points
    /// keep both endpoint tangents horizontal and cross over midway, matching Excel's default
    /// curvedConnector3 S-curve rather than a vertical-start bow.
    /// The host renderer applies flip/rotation for other orientations.
    /// </summary>
    private static ShapeGeometry CurvedConnector(LayoutRect rect)
    {
        // S-curve: start at top-left (0,0), end at bottom-right (1,1).
        // Excel leaves the start heading right and enters the end horizontally. Keeping the
        // controls on the top and bottom edges produces that sideways S rather than a curve
        // which initially drops straight down.
        var start = P(rect, 0, 0);
        ShapeSegment[] segments =
        [
            ShapeSegment.BezierTo(P(rect, 0.67, 0), P(rect, 0.33, 1), P(rect, 1, 1))
        ];
        return Single(new ShapeContour(start, segments, Closed: false, Filled: false));
    }

    /// <summary>
    /// Renders a <see cref="DrawingShapeKind.Cylinder"/> (the OOXML "can" preset) as two contours:
    /// <list type="bullet">
    ///   <item>The body: a rectangle with an open-arc bottom representing the can's base.</item>
    ///   <item>The top ellipse cap: a full ellipse whose height is ~25 % of the shape height.</item>
    /// </list>
    /// This produces the classic database/storage "can" symbol.  Excel's default adjust ratio puts
    /// the top ellipse at roughly 25 % of the total shape height.
    /// </summary>
    private static ShapeGeometry CylinderShape(
        LayoutRect rect,
        IReadOnlyDictionary<string, double>? adjustments)
    {
        // The native "can" preset uses a single 0..50000 `adj` guide for the
        // top-cap height. Keep the established 25% fallback for new/legacy
        // shapes with no authored guide.
        const double DefaultAdjustment = 25000;
        var ellipseHeightFraction = Math.Clamp(
            GetAdjustment(adjustments, "adj", DefaultAdjustment),
            0,
            50000) / 100000.0;
        var ew = rect.Width;
        var eh = rect.Height * ellipseHeightFraction;
        var rx = ew / 2;
        var ry = eh / 2;
        var cx = rect.Left + rx;
        var bodyTop = rect.Top + ry;   // body starts at the ellipse's vertical center
        var bodyBottom = rect.Bottom;

        // ── Contour 1: body outline ─────────────────────────────────────────────
        // Draw the body: left side down, bottom half-ellipse arc (convex outward at bottom),
        // right side up, then the top half-ellipse arc (convex outward at top) to close.

        var leftAtTop = new LayoutPoint(cx - rx, bodyTop);
        var leftAtBottom = new LayoutPoint(cx - rx, bodyBottom);
        var rightAtBottom = new LayoutPoint(cx + rx, bodyBottom);
        var rightAtTop = new LayoutPoint(cx + rx, bodyTop);

        ShapeSegment[] bodySegments =
        [
            ShapeSegment.LineTo(leftAtBottom),
            ShapeSegment.ArcTo(rightAtBottom, rx, ry, sweepClockwise: true),
            ShapeSegment.LineTo(rightAtTop),
            ShapeSegment.ArcTo(leftAtTop, rx, ry, sweepClockwise: false)
        ];
        var bodyContour = new ShapeContour(leftAtTop, bodySegments, Closed: true, Filled: true);

        // ── Contour 2: full top ellipse cap ────────────────────────────────────
        // A complete ellipse at (left, top, ew, eh) drawn on top of the body to give the
        // cylinder a visible "lid".  EllipseContour draws two clockwise half-arcs.
        var topCapContour = EllipseContour(rect.Left, rect.Top, ew, eh);

        return new ShapeGeometry([bodyContour, topCapContour]);
    }

    private static ShapeGeometry Plus(
        LayoutRect rect,
        IReadOnlyDictionary<string, double>? adjustments = null)
    {
        // The preset cross/plus guide is the inset from each edge to the central bar.
        // Keep the existing 35% fallback for new and legacy shapes without an authored guide.
        var inset = Math.Clamp(GetAdjustment(adjustments, "adj", 35000), 0, 50000) / 100000.0;
        var opposite = 1 - inset;
        return Polygon(rect,
        [
            (inset, 0), (opposite, 0), (opposite, inset), (1, inset),
            (1, opposite), (opposite, opposite), (opposite, 1), (inset, 1),
            (inset, opposite), (0, opposite), (0, inset), (inset, inset)
        ]);
    }

    private static ShapeContour MinusContour(LayoutRect rect) =>
        RectangleContour(rect.Left, rect.Top + (rect.Height * 0.38), rect.Width, rect.Height * 0.24);

    private static ShapeGeometry Minus(LayoutRect rect) => Single(MinusContour(rect));

    private static ShapeGeometry Multiply(LayoutRect rect)
    {
        var thickness = Math.Min(rect.Width, rect.Height) * 0.16;
        return new ShapeGeometry([RotatedBar(rect, thickness, 45), RotatedBar(rect, thickness, -45)]);
    }

    private static ShapeGeometry Divide(LayoutRect rect)
    {
        var dotSize = Math.Min(rect.Width, rect.Height) * 0.16;
        return new ShapeGeometry(
        [
            MinusContour(rect),
            EllipseContour(rect.Left + (rect.Width * 0.5) - (dotSize / 2), rect.Top + (rect.Height * 0.12), dotSize, dotSize),
            EllipseContour(rect.Left + (rect.Width * 0.5) - (dotSize / 2), rect.Top + (rect.Height * 0.72), dotSize, dotSize)
        ]);
    }

    private static IReadOnlyList<ShapeContour> EqualContours(LayoutRect rect) =>
    [
        RectangleContour(rect.Left, rect.Top + (rect.Height * 0.28), rect.Width, rect.Height * 0.18),
        RectangleContour(rect.Left, rect.Top + (rect.Height * 0.56), rect.Width, rect.Height * 0.18)
    ];

    private static ShapeGeometry Equal(LayoutRect rect) => new(EqualContours(rect));

    private static ShapeGeometry NotEqual(LayoutRect rect)
    {
        var contours = new List<ShapeContour>(EqualContours(rect))
        {
            RotatedBar(rect, Math.Min(rect.Width, rect.Height) * 0.12, -63)
        };
        return new ShapeGeometry(contours);
    }

    /// <summary>
    /// A horizontal bar across the middle of <paramref name="rect"/>, rotated by
    /// <paramref name="degrees"/> about the rectangle center. Returns the four rotated corner
    /// vertices as a closed quad.
    /// </summary>
    private static ShapeContour RotatedBar(LayoutRect rect, double thickness, double degrees)
    {
        var left = rect.Left + (rect.Width * 0.08);
        var top = rect.Top + (rect.Height * 0.5) - (thickness / 2);
        var width = rect.Width * 0.84;
        var cx = rect.Left + (rect.Width / 2);
        var cy = rect.Top + (rect.Height / 2);
        var radians = degrees * Math.PI / 180;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        LayoutPoint Rotate(double x, double y)
        {
            var dx = x - cx;
            var dy = y - cy;
            return new LayoutPoint(cx + (dx * cos) - (dy * sin), cy + (dx * sin) + (dy * cos));
        }

        var p0 = Rotate(left, top);
        var p1 = Rotate(left + width, top);
        var p2 = Rotate(left + width, top + thickness);
        var p3 = Rotate(left, top + thickness);

        ShapeSegment[] segments =
        [
            ShapeSegment.LineTo(p1),
            ShapeSegment.LineTo(p2),
            ShapeSegment.LineTo(p3)
        ];
        return new ShapeContour(p0, segments, Closed: true, Filled: true);
    }

    private static ShapeGeometry FlowchartPredefinedProcess(LayoutRect rect) =>
        new(
        [
            RectangleContour(rect),
            OpenPathContour(rect, [(0.18, 0), (0.18, 1)]),
            OpenPathContour(rect, [(0.82, 0), (0.82, 1)])
        ]);

    private static ShapeGeometry FlowchartDocument(LayoutRect rect)
    {
        var start = P(rect, 0, 0);
        ShapeSegment[] segments =
        [
            ShapeSegment.LineTo(P(rect, 1, 0)),
            ShapeSegment.LineTo(P(rect, 1, 0.82)),
            ShapeSegment.BezierTo(P(rect, 0.72, 0.72), P(rect, 0.48, 0.98), P(rect, 0.22, 0.86)),
            ShapeSegment.BezierTo(P(rect, 0.12, 0.82), P(rect, 0.05, 0.80), P(rect, 0, 0.86))
        ];
        return Single(new ShapeContour(start, segments, Closed: true, Filled: true));
    }

    private static ShapeGeometry Star(LayoutRect rect, int points, double innerRadius, double startAngle = -Math.PI / 2)
    {
        var vertices = new (double X, double Y)[points * 2];
        for (var i = 0; i < vertices.Length; i++)
        {
            var radius = i % 2 == 0 ? 0.5 : 0.5 * innerRadius;
            var angle = startAngle + (i * Math.PI / points);
            vertices[i] = (0.5 + (Math.Cos(angle) * radius), 0.5 + (Math.Sin(angle) * radius));
        }

        return Polygon(rect, vertices);
    }

    private static double StarInnerRadius(
        IReadOnlyDictionary<string, double>? adjustments,
        double fallback)
    {
        // Star5 and Star8 expose one DrawingML `adj` guide controlling point depth. Keep
        // the established fallback for new/legacy shapes with no authored guide.
        return Math.Clamp(GetAdjustment(adjustments, "adj", fallback * 100000) / 100000.0, 0, 1);
    }

    private static ShapeGeometry Ribbon(
        LayoutRect rect,
        IReadOnlyDictionary<string, double>? adjustments)
    {
        // Keep the established outline for newly-created shapes. Authored DrawingML ribbons
        // expose fold depth (adj1) and fold width (adj2); these values must drive the outline
        // so edit-point changes survive the next render.
        if (adjustments is null ||
            (!adjustments.ContainsKey("adj1") && !adjustments.ContainsKey("adj2")))
        {
            return Polygon(rect, [(0.08, 0.22), (0.92, 0.22), (0.92, 0.06), (1, 0.24), (0.92, 0.42), (0.92, 0.78), (0.08, 0.78), (0.08, 0.94), (0, 0.76), (0.08, 0.58)]);
        }

        var fold = Math.Clamp(GetAdjustment(adjustments, "adj1", 16667), 0, 33333) / 100000.0;
        var width = Math.Clamp(GetAdjustment(adjustments, "adj2", 50000), 25000, 75000) / 200000.0;
        var bandTop = Math.Clamp(fold, 0.04, 0.45);
        var bandBottom = 1 - bandTop;
        var tailTop = Math.Max(0.01, bandTop / 2);
        var tailBottom = 1 - tailTop;
        var leftFold = 0.5 - width;
        var rightFold = 0.5 + width;
        var center = (bandTop + bandBottom) / 2;
        var foldLip = Math.Min(0.08, Math.Max(0.02, (bandBottom - bandTop) / 5));

        return Polygon(
            rect,
            [
                (0.08, bandTop), (leftFold, bandTop), (0.08, tailTop), (0, center),
                (0.08, tailBottom), (leftFold, bandBottom), (0.08, bandBottom),
                (0.08, bandBottom - foldLip), (0.92, bandBottom), (rightFold, bandBottom),
                (0.92, tailBottom), (1, center), (0.92, tailTop), (rightFold, bandTop),
                (0.92, bandTop), (0.92, bandTop - foldLip),
            ]);
    }

    private static ShapeGeometry Wave(
        LayoutRect rect,
        IReadOnlyDictionary<string, double>? adjustments)
    {
        if (adjustments is null ||
            (!adjustments.ContainsKey("adj1") && !adjustments.ContainsKey("adj2")))
        {
            return LegacyWave(rect);
        }

        // The standard wave guides use adj1 for amplitude and adj2 for horizontal phase. The
        // guide-derived extrema may extend beyond the box, as they do in DrawingML; the host
        // clips the resulting shape to the authored bounds when appropriate.
        var amplitude = Math.Clamp(GetAdjustment(adjustments, "adj1", 12500), 0, 20000) / 100000.0;
        var phase = Math.Clamp(GetAdjustment(adjustments, "adj2", 0), -10000, 10000) / 100000.0;
        var dy = amplitude * 10 / 3;
        var y1 = amplitude;
        var y2 = y1 - dy;
        var y3 = y1 + dy;
        var y4 = 1 - y1;
        var y5 = y4 - dy;
        var y6 = y4 + dy;
        var shift = phase * 0.5;
        var x = (double value) => Math.Clamp(value + shift, -0.15, 1.15);
        var start = P(rect, 0, y1);
        ShapeSegment[] authoredSegments =
        [
            ShapeSegment.BezierTo(P(rect, x(0.22), y2), P(rect, x(0.38), y3), P(rect, x(0.58), y1)),
            ShapeSegment.BezierTo(P(rect, x(0.74), y2), P(rect, x(0.88), y3), P(rect, 1, y4)),
            ShapeSegment.BezierTo(P(rect, x(0.78), y5), P(rect, x(0.58), y6), P(rect, x(0.36), y4)),
            ShapeSegment.BezierTo(P(rect, x(0.18), y5), P(rect, x(0.08), y6), P(rect, 0, y1))
        ];
        return Single(new ShapeContour(start, authoredSegments, Closed: true, Filled: true));
    }

    private static ShapeGeometry LegacyWave(LayoutRect rect)
    {
        var start = P(rect, 0, 0.45);
        ShapeSegment[] segments =
        [
            ShapeSegment.BezierTo(P(rect, 0.22, 0.12), P(rect, 0.38, 0.78), P(rect, 0.58, 0.45)),
            ShapeSegment.BezierTo(P(rect, 0.74, 0.18), P(rect, 0.88, 0.24), P(rect, 1, 0.36)),
            ShapeSegment.LineTo(P(rect, 1, 0.72)),
            ShapeSegment.BezierTo(P(rect, 0.78, 0.56), P(rect, 0.58, 1.02), P(rect, 0.36, 0.72)),
            ShapeSegment.BezierTo(P(rect, 0.18, 0.48), P(rect, 0.08, 0.62), P(rect, 0, 0.74))
        ];
        return Single(new ShapeContour(start, segments, Closed: true, Filled: true));
    }

    private static ShapeGeometry RectangularCallout(LayoutRect rect)
    {
        const double bodyFraction = 0.80;
        var body = new LayoutRect(rect.Left, rect.Top, rect.Width, rect.Height * bodyFraction);
        return new ShapeGeometry(
        [
            RectangleContour(body),
            PolygonContour(rect, [(0.38, bodyFraction), (0.52, bodyFraction), (0.45, 1)])
        ]);
    }

    private static ShapeGeometry RoundedCallout(LayoutRect rect)
    {
        const double bodyFraction = 0.80;
        var body = new LayoutRect(rect.Left, rect.Top, rect.Width, rect.Height * bodyFraction);
        return new ShapeGeometry(
        [
            RoundedRectangleContour(body, CornerRadius(body)),
            PolygonContour(rect, [(0.38, bodyFraction), (0.52, bodyFraction), (0.45, 1)])
        ]);
    }

    private static ShapeGeometry OvalCallout(LayoutRect rect)
    {
        const double bodyFraction = 0.82;
        var body = new LayoutRect(rect.Left, rect.Top, rect.Width, rect.Height * bodyFraction);
        return new ShapeGeometry(
        [
            EllipseContour(body),
            PolygonContour(rect, [(0.40, bodyFraction * 0.9), (0.54, bodyFraction * 0.9), (0.47, 1)])
        ]);
    }

    private static ShapeGeometry LineCallout(LayoutRect rect) =>
        new(
        [
            RectangleContour(rect.Left + (rect.Width * 0.24), rect.Top, rect.Width * 0.76, rect.Height * 0.58),
            OpenPathContour(rect, [(0.02, 1), (0.24, 0.58)])
        ]);
}
