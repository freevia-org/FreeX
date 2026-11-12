using System.Globalization;

using Free.Shared.Drawing;

namespace FreeW.Core.Model;

/// <summary>
/// Shared DrawingML preset-geometry engine for FreeW: turns an <c>a:prstGeom/@prst</c> token plus its
/// adjust values into the freeform outline (<see cref="CustomGeometry"/>) both shells already render,
/// export and hit-test. The outline is built in the shape's own EMU box so the adjust formulas, which
/// are expressed against the shorter side, keep their proportions.
/// <para>
/// The outline is only a drawing cache. A shape's identity stays its <see cref="Shape.PresetName"/>
/// (see <see cref="Shape.HasIntactPresetGeometry"/>), so presets this engine merely approximates, or
/// does not draw at all, still save back as the preset they were.
/// </para>
/// </summary>
public static partial class PresetShapeGeometry
{
    private const double EmuPerPoint = 12700.0;

    /// <summary>The preset tokens the three named <see cref="ShapeKind"/> values serialise as.</summary>
    public static bool IsNamedKindPreset(string? preset) =>
        preset is "rect" or "roundRect" or "ellipse";

    /// <summary>True for line and connector presets, which are strokes rather than filled outlines.</summary>
    public static bool IsLinePreset(string? preset) =>
        preset is "line" or "straightConnector1"
            or "bentConnector2" or "bentConnector3" or "bentConnector4" or "bentConnector5"
            or "curvedConnector2" or "curvedConnector3" or "curvedConnector4" or "curvedConnector5";

    /// <summary>True when this engine draws a dedicated outline for <paramref name="preset"/>.</summary>
    public static bool IsSupported(string? preset) =>
        !string.IsNullOrEmpty(preset)
        && (Builders.ContainsKey(preset) || DrawingMlPresetGeometryMap.TryGetShapeKind(preset, out _));

    /// <summary>
    /// Builds the outline of <paramref name="preset"/> for a shape of the given size, or null when the
    /// preset has no dedicated outline (the shape then draws as its <see cref="ShapeKind"/>).
    /// </summary>
    public static CustomGeometry? Build(
        string? preset,
        double widthPt,
        double heightPt,
        IReadOnlyList<ShapeAdjustment>? adjustments = null,
        ShapeLineEnd? headEnd = null,
        ShapeLineEnd? tailEnd = null,
        double outlineWidthPt = 0)
    {
        if (string.IsNullOrEmpty(preset) || IsNamedKindPreset(preset))
            return null;
        // Accept-range screens: a NaN / infinite / absurd size never reaches the outline math.
        if (!(widthPt >= 0 && widthPt <= 100000) || !(heightPt >= 0 && heightPt <= 100000))
            return null;

        var isLine = IsLinePreset(preset);
        var w = widthPt * EmuPerPoint;
        var h = heightPt * EmuPerPoint;
        if (isLine ? (w <= 0 && h <= 0) : (w <= 0 || h <= 0))
            return null;

        if (Builders.TryGetValue(preset, out var build))
        {
            var path = new PathBuilder(w, h, adjustments);
            build(path);
            if (isLine)
                path.AddLineEnds(headEnd, tailEnd, Math.Max(outlineWidthPt, 1.5) * EmuPerPoint);
            return path.ToGeometry();
        }

        return BuildFromSharedTier(preset, w, h, adjustments);
    }

    /// <summary>
    /// Synthesizes the outline for <see cref="Shape.PresetName"/> and records its signature, making the
    /// shape an intact preset. A preset without a dedicated outline clears the cache instead.
    /// </summary>
    public static void ApplyPresetOutline(Shape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        var geometry = Build(
            shape.PresetName,
            shape.WidthPt,
            shape.HeightPt,
            shape.PresetAdjustments,
            shape.HeadEnd,
            shape.TailEnd,
            shape.OutlineWidthPt);
        shape.CustomGeometry = geometry;
        shape.PresetOutlineSignature = geometry is null ? null : Signature(geometry);
    }

    /// <summary>
    /// The outline to draw for <paramref name="shape"/>: an intact preset is re-synthesized for the
    /// shape's current size and outline so a resize keeps true proportions; anything else is the stored
    /// freeform geometry. Never mutates the shape.
    /// </summary>
    public static CustomGeometry? ResolveRenderGeometry(Shape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        if (!shape.HasCustomGeometry)
            return null;
        if (!shape.HasIntactPresetGeometry)
            return shape.CustomGeometry;
        return Build(
                   shape.PresetName,
                   shape.WidthPt,
                   shape.HeightPt,
                   shape.PresetAdjustments,
                   shape.HeadEnd,
                   shape.TailEnd,
                   shape.OutlineWidthPt)
               ?? shape.CustomGeometry;
    }

    /// <summary>A stable content signature of an outline (grid size plus every segment).</summary>
    public static string Signature(CustomGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        unchecked
        {
            ulong hash = 1469598103934665603UL;
            void Mix(long value)
            {
                hash ^= (ulong)value;
                hash *= 1099511628211UL;
            }

            void MixPoint(CustomPoint? point)
            {
                if (point is null)
                {
                    Mix(long.MinValue);
                    return;
                }

                Mix(point.X);
                Mix(point.Y);
            }

            Mix(geometry.Width);
            Mix(geometry.Height);
            foreach (var segment in geometry.Segments)
            {
                Mix((long)segment.Kind);
                MixPoint(segment.Point);
                MixPoint(segment.ControlPoint1);
                MixPoint(segment.ControlPoint2);
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{geometry.Width}x{geometry.Height}:{geometry.Segments.Count}:{hash:X16}");
        }
    }

    /// <summary>
    /// Fallback for presets without a dedicated builder here: the cross-app shared tier
    /// (<see cref="ShapeGeometryBuilder"/>), with its elliptical arcs flattened to cubic Béziers.
    /// </summary>
    private static CustomGeometry? BuildFromSharedTier(
        string preset,
        double w,
        double h,
        IReadOnlyList<ShapeAdjustment>? adjustments)
    {
        if (!DrawingMlPresetGeometryMap.TryGetShapeKind(preset, out var kind)
            || kind is DrawingShapeKind.Rectangle or DrawingShapeKind.RoundedRectangle or DrawingShapeKind.Ellipse)
        {
            return null;
        }

        Dictionary<string, double>? guides = null;
        if (adjustments is not null)
        {
            foreach (var adjustment in adjustments)
            {
                if (TryParseAdjustment(adjustment.Formula, out var value))
                    (guides ??= new Dictionary<string, double>(StringComparer.Ordinal))[adjustment.Name] = value;
            }
        }

        var geometry = ShapeGeometryBuilder.Build(kind, new LayoutRect(0, 0, w, h), guides);
        if (geometry.Contours.Count == 0)
            return null;

        var path = new PathBuilder(w, h, adjustments);
        foreach (var contour in geometry.Contours)
        {
            void Draw()
            {
                path.M(contour.Start.X, contour.Start.Y);
                foreach (var segment in contour.Segments)
                {
                    switch (segment.Kind)
                    {
                        case ShapeSegmentKind.CubicBezier:
                            path.C(segment.Control1.X, segment.Control1.Y, segment.Control2.X, segment.Control2.Y, segment.End.X, segment.End.Y);
                            break;
                        case ShapeSegmentKind.Arc:
                            path.SvgArc(segment.RadiusX, segment.RadiusY, segment.LargeArc, segment.SweepClockwise, segment.End.X, segment.End.Y);
                            break;
                        default:
                            path.L(segment.End.X, segment.End.Y);
                            break;
                    }
                }

                if (contour.Closed)
                    path.Z();
            }

            if (contour.Closed)
                Draw();
            else
                path.Stroke(Draw);
        }

        return path.ToGeometry();
    }

    internal static bool TryParseAdjustment(string? formula, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(formula))
            return false;
        var text = formula.Trim();
        if (text.StartsWith("val", StringComparison.Ordinal))
            text = text[3..].Trim();
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return false;
        // Accept range: DrawingML guides are bounded integers (angles top out at 21 600 000).
        if (!(parsed >= -100000000 && parsed <= 100000000))
            return false;
        value = parsed;
        return true;
    }

    // ── Path builder ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A tiny DrawingML-flavoured path recorder working in the shape's EMU box. Angles are degrees,
    /// clockwise from the positive x axis (y grows downwards), matching <c>a:arcTo</c>.
    /// </summary>
    internal sealed class PathBuilder
    {
        private readonly record struct Op(char Kind, double X, double Y, double X1, double Y1, double X2, double Y2, bool StrokeOnly);

        private readonly List<Op> _ops = [];
        private readonly IReadOnlyList<ShapeAdjustment>? _adjustments;
        private double _x;
        private double _y;
        private bool _strokeOnly;
        private Func<double, double, (double X, double Y)>? _transform;

        public PathBuilder(double w, double h, IReadOnlyList<ShapeAdjustment>? adjustments)
        {
            BoxW = w;
            BoxH = h;
            W = w;
            H = h;
            _adjustments = adjustments;
        }

        /// <summary>The real box size; <see cref="W"/>/<see cref="H"/> may be a transposed working frame.</summary>
        public double BoxW { get; }
        public double BoxH { get; }
        public double W { get; private set; }
        public double H { get; private set; }
        public double Ss => Math.Min(W, H);
        public double Hc => W / 2;
        public double Vc => H / 2;

        /// <summary>An adjust value as a 0..1 style fraction of 100000, pinned to [min, max].</summary>
        public double Adj(string name, double fallback, double min = 0, double max = 100000) =>
            Math.Clamp(Raw(name, fallback), min, Math.Max(min, max)) / 100000.0;

        /// <summary>An adjust value in its raw units (e.g. 60000ths of a degree).</summary>
        public double Raw(string name, double fallback)
        {
            if (_adjustments is not null)
            {
                foreach (var adjustment in _adjustments)
                {
                    if (string.Equals(adjustment.Name, name, StringComparison.Ordinal)
                        && TryParseAdjustment(adjustment.Formula, out var value))
                    {
                        return value;
                    }
                }
            }

            return fallback;
        }

        /// <summary>Draws in a mirrored and/or transposed frame (used to derive left/up/down variants).</summary>
        public void InFrame(bool transpose, bool mirrorX, bool mirrorY, Action draw)
        {
            var previous = _transform;
            var (w, h) = (W, H);
            if (transpose)
                (W, H) = (H, W);
            var (fw, fh) = (W, H);
            _transform = (x, y) =>
            {
                if (mirrorX) x = fw - x;
                if (mirrorY) y = fh - y;
                if (transpose) (x, y) = (y, x);
                return previous is null ? (x, y) : previous(x, y);
            };
            draw();
            _transform = previous;
            (W, H) = (w, h);
        }

        private (double X, double Y) Map(double x, double y) => _transform is null ? (x, y) : _transform(x, y);

        public void M(double x, double y)
        {
            var (mx, my) = Map(x, y);
            _ops.Add(new Op('M', mx, my, 0, 0, 0, 0, _strokeOnly));
            (_x, _y) = (x, y);
        }

        public void L(double x, double y)
        {
            var (mx, my) = Map(x, y);
            _ops.Add(new Op('L', mx, my, 0, 0, 0, 0, _strokeOnly));
            (_x, _y) = (x, y);
        }

        public void C(double x1, double y1, double x2, double y2, double x, double y)
        {
            var (ax, ay) = Map(x1, y1);
            var (bx, by) = Map(x2, y2);
            var (mx, my) = Map(x, y);
            _ops.Add(new Op('C', mx, my, ax, ay, bx, by, _strokeOnly));
            (_x, _y) = (x, y);
        }

        /// <summary>Quadratic Bézier, raised to a cubic.</summary>
        public void Q(double x1, double y1, double x, double y) =>
            C(_x + (2.0 / 3.0) * (x1 - _x), _y + (2.0 / 3.0) * (y1 - _y),
              x + (2.0 / 3.0) * (x1 - x), y + (2.0 / 3.0) * (y1 - y),
              x, y);

        public void Z() => _ops.Add(new Op('Z', 0, 0, 0, 0, 0, 0, _strokeOnly));

        /// <summary>
        /// <c>a:arcTo</c>: the current point sits at <paramref name="startDeg"/> on an ellipse of the
        /// given radii; draws <paramref name="sweepDeg"/> degrees (positive = clockwise).
        /// </summary>
        public void Arc(double wR, double hR, double startDeg, double sweepDeg)
        {
            if (!(wR > 0) || !(hR > 0) || !double.IsFinite(startDeg) || !double.IsFinite(sweepDeg) || sweepDeg == 0)
                return;
            sweepDeg = Math.Clamp(sweepDeg, -360, 360);
            var start = startDeg * Math.PI / 180;
            var cx = _x - wR * Math.Cos(start);
            var cy = _y - hR * Math.Sin(start);
            var pieces = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweepDeg) / 90.0));
            var step = sweepDeg * Math.PI / 180 / pieces;
            var k = 4.0 / 3.0 * Math.Tan(step / 4);
            var a0 = start;
            for (var i = 0; i < pieces; i++)
            {
                var a1 = a0 + step;
                var p0x = cx + wR * Math.Cos(a0);
                var p0y = cy + hR * Math.Sin(a0);
                var p1x = cx + wR * Math.Cos(a1);
                var p1y = cy + hR * Math.Sin(a1);
                C(p0x - k * wR * Math.Sin(a0), p0y + k * hR * Math.Cos(a0),
                  p1x + k * wR * Math.Sin(a1), p1y - k * hR * Math.Cos(a1),
                  p1x, p1y);
                a0 = a1;
            }
        }

        /// <summary>SVG-style endpoint arc (no axis rotation), used for the shared-tier fallback.</summary>
        public void SvgArc(double rx, double ry, bool largeArc, bool sweepClockwise, double x, double y)
        {
            if (!(rx > 0) || !(ry > 0) || (Math.Abs(x - _x) < 1e-9 && Math.Abs(y - _y) < 1e-9))
            {
                L(x, y);
                return;
            }

            // Endpoint → centre parameterisation (SVG implementation notes F.6.5), phi = 0.
            var dx = (_x - x) / 2;
            var dy = (_y - y) / 2;
            var lambda = dx * dx / (rx * rx) + dy * dy / (ry * ry);
            if (lambda > 1)
            {
                var scale = Math.Sqrt(lambda);
                rx *= scale;
                ry *= scale;
            }

            var numerator = rx * rx * ry * ry - rx * rx * dy * dy - ry * ry * dx * dx;
            var denominator = rx * rx * dy * dy + ry * ry * dx * dx;
            var factor = denominator <= 0 ? 0 : Math.Sqrt(Math.Max(0, numerator / denominator));
            if (largeArc == sweepClockwise)
                factor = -factor;
            var cxp = factor * rx * dy / ry;
            var cyp = -factor * ry * dx / rx;
            var cx = cxp + (_x + x) / 2;
            var cy = cyp + (_y + y) / 2;
            var startAngle = Math.Atan2((_y - cy) / ry, (_x - cx) / rx) * 180 / Math.PI;
            var endAngle = Math.Atan2((y - cy) / ry, (x - cx) / rx) * 180 / Math.PI;
            var sweep = endAngle - startAngle;
            if (sweepClockwise && sweep < 0) sweep += 360;
            if (!sweepClockwise && sweep > 0) sweep -= 360;
            Arc(rx, ry, startAngle, sweep);
        }

        public void Poly(params (double X, double Y)[] points)
        {
            if (points.Length == 0)
                return;
            M(points[0].X, points[0].Y);
            for (var i = 1; i < points.Length; i++)
                L(points[i].X, points[i].Y);
            Z();
        }

        /// <summary>A closed polygon given on a fixed design grid (e.g. 21600) scaled to the box.</summary>
        public void GridPoly(double grid, params (double X, double Y)[] points)
        {
            var scaled = new (double X, double Y)[points.Length];
            for (var i = 0; i < points.Length; i++)
                scaled[i] = (points[i].X * W / grid, points[i].Y * H / grid);
            Poly(scaled);
        }

        public void Rect(double l, double t, double r, double b) => Poly((l, t), (r, t), (r, b), (l, b));

        public void Ellipse(double cx, double cy, double rx, double ry)
        {
            if (!(rx > 0) || !(ry > 0))
                return;
            M(cx - rx, cy);
            Arc(rx, ry, 180, 360);
            Z();
        }

        /// <summary>
        /// Records unfilled detail strokes (connector lines, fold lines, an arc). Each sub-path is
        /// retraced back to its start so it encloses no area and never punches a hole in the fill.
        /// </summary>
        public void Stroke(Action draw)
        {
            var previous = _strokeOnly;
            _strokeOnly = true;
            draw();
            _strokeOnly = previous;
        }

        public void StrokeLine(double x1, double y1, double x2, double y2) =>
            Stroke(() =>
            {
                M(x1, y1);
                L(x2, y2);
            });

        /// <summary>Adds line-end decorations to the first sub-path (the connector itself).</summary>
        public void AddLineEnds(ShapeLineEnd? head, ShapeLineEnd? tail, double lineWidth)
        {
            var end = -1;
            for (var i = 1; i < _ops.Count; i++)
            {
                if (_ops[i].Kind == 'M')
                    break;
                end = i;
            }

            if (_ops.Count < 2 || _ops[0].Kind != 'M' || end < 1)
                return;

            var first = _ops[0];
            var second = _ops[1];
            var last = _ops[end];
            var beforeLast = _ops[end - 1];

            // Direction pointing out of the line at each end.
            var (hx, hy) = second.Kind == 'C' && (second.X1 != first.X || second.Y1 != first.Y)
                ? (first.X - second.X1, first.Y - second.Y1)
                : (first.X - second.X, first.Y - second.Y);
            var (tx, ty) = last.Kind == 'C' && (last.X2 != last.X || last.Y2 != last.Y)
                ? (last.X - last.X2, last.Y - last.Y2)
                : (last.X - beforeLast.X, last.Y - beforeLast.Y);

            var saved = _transform;
            _transform = null; // recorded ops are already in box space
            AddLineEnd(head, first.X, first.Y, hx, hy, lineWidth);
            AddLineEnd(tail, last.X, last.Y, tx, ty, lineWidth);
            _transform = saved;
        }

        private void AddLineEnd(ShapeLineEnd? lineEnd, double tipX, double tipY, double dx, double dy, double lineWidth)
        {
            if (lineEnd is null || lineEnd.Type is "none" or "" || !(lineWidth > 0))
                return;
            var length = Math.Sqrt(dx * dx + dy * dy);
            if (!(length > 0))
                return;
            var (ux, uy) = (dx / length, dy / length);
            var (nx, ny) = (-uy, ux);
            var along = lineWidth * SizeFactor(lineEnd.Length);
            var across = lineWidth * SizeFactor(lineEnd.Width) / 2;
            var (bx, by) = (tipX - ux * along, tipY - uy * along);

            Stroke(() =>
            {
                switch (lineEnd.Type)
                {
                    case "arrow":
                        M(bx + nx * across, by + ny * across);
                        L(tipX, tipY);
                        L(bx - nx * across, by - ny * across);
                        break;
                    case "oval":
                    {
                        // A disc centred on the line end, filled by spokes no further apart than the pen.
                        const int Spokes = 16;
                        var (px, py) = (tipX + across, tipY);
                        for (var i = 1; i <= Spokes; i++)
                        {
                            var angle = 2 * Math.PI * i / Spokes;
                            var (qx, qy) = (tipX + across * Math.Cos(angle), tipY + across * Math.Sin(angle));
                            M(tipX, tipY);
                            L(px, py);
                            L(qx, qy);
                            (px, py) = (qx, qy);
                        }

                        break;
                    }
                    case "diamond":
                    {
                        var (cx, cy) = (tipX, tipY);
                        Fan(cx + ux * along / 2, cy + uy * along / 2, cx, cy, nx, ny, across, lineWidth);
                        Fan(cx - ux * along / 2, cy - uy * along / 2, cx, cy, nx, ny, across, lineWidth);
                        break;
                    }
                    default: // triangle, stealth
                        Fan(tipX, tipY, bx, by, nx, ny, across, lineWidth);
                        break;
                }
            });
        }

        /// <summary>A solid triangle drawn as a fan of pen strokes from the tip to its base.</summary>
        private void Fan(double tipX, double tipY, double baseX, double baseY, double nx, double ny, double across, double lineWidth)
        {
            var count = Math.Clamp((int)Math.Ceiling(4 * across / lineWidth), 4, 24);
            M(baseX + nx * across, baseY + ny * across);
            L(baseX - nx * across, baseY - ny * across);
            for (var i = 0; i <= count; i++)
            {
                var t = -1 + 2.0 * i / count;
                M(tipX, tipY);
                L(baseX + nx * across * t, baseY + ny * across * t);
            }
        }

        private static double SizeFactor(string? token) => token switch
        {
            "sm" => 2,
            "lg" => 5,
            _ => 3,
        };

        public CustomGeometry? ToGeometry()
        {
            if (_ops.Count == 0)
                return null;

            var gridW = Math.Max(1, (long)Math.Round(BoxW));
            var gridH = Math.Max(1, (long)Math.Round(BoxH));
            var geometry = new CustomGeometry { Width = gridW, Height = gridH };

            static CustomPoint Point(double x, double y) =>
                new((long)Math.Round(double.IsFinite(x) ? Math.Clamp(x, -1e12, 1e12) : 0),
                    (long)Math.Round(double.IsFinite(y) ? Math.Clamp(y, -1e12, 1e12) : 0));

            var index = 0;
            while (index < _ops.Count)
            {
                var start = index;
                if (_ops[start].Kind != 'M')
                {
                    index++;
                    continue;
                }

                index++;
                while (index < _ops.Count && _ops[index].Kind != 'M')
                    index++;

                var move = _ops[start];
                geometry.Segments.Add(new CustomSegment(CustomSegmentKind.MoveTo, Point(move.X, move.Y)));
                var closed = false;
                for (var i = start + 1; i < index; i++)
                {
                    var op = _ops[i];
                    switch (op.Kind)
                    {
                        case 'L':
                            geometry.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, Point(op.X, op.Y)));
                            break;
                        case 'C':
                            geometry.Segments.Add(new CustomSegment(
                                CustomSegmentKind.CubicBezierTo, Point(op.X, op.Y), Point(op.X1, op.Y1), Point(op.X2, op.Y2)));
                            break;
                        case 'Z':
                            closed = true;
                            break;
                    }
                }

                if (move.StrokeOnly && !closed)
                {
                    // Retrace back to the start: zero enclosed area, so the fill is untouched.
                    for (var i = index - 1; i > start; i--)
                    {
                        var op = _ops[i];
                        var previous = _ops[i - 1];
                        if (op.Kind == 'L')
                            geometry.Segments.Add(new CustomSegment(CustomSegmentKind.LineTo, Point(previous.X, previous.Y)));
                        else if (op.Kind == 'C')
                            geometry.Segments.Add(new CustomSegment(
                                CustomSegmentKind.CubicBezierTo, Point(previous.X, previous.Y), Point(op.X2, op.Y2), Point(op.X1, op.Y1)));
                    }
                }
                else if (closed)
                {
                    geometry.Segments.Add(new CustomSegment(CustomSegmentKind.Close));
                }
            }

            return geometry.Segments.Count > 0 ? geometry : null;
        }
    }
}
