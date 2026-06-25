using Free.Shared.Drawing;
using FreeP.Core.Model;

namespace FreeP.App.Compositor;

/// <summary>
/// Builds <see cref="ShapeGeometry"/> from custom geometry path data (<see cref="CustomGeometryPath"/>).
/// Lives in FreeP.App.Presentation because it depends on both Free.Shared.Drawing and FreeP.Core.Model,
/// and Free.Shared.Drawing must not depend on FreeP.Core.Model.
/// </summary>
public static class CustomGeometryBuilder
{
    /// <summary>
    /// Builds a <see cref="ShapeGeometry"/> from a list of custom geometry paths.
    /// Each path is mapped from its own w×h coordinate space to <paramref name="bounds"/>.
    /// </summary>
    public static ShapeGeometry BuildCustom(
        IReadOnlyList<CustomGeometryPath> paths,
        LayoutRect bounds)
    {
        if (paths.Count == 0) return ShapeGeometry.Empty;

        var contours = new List<ShapeContour>();
        foreach (var path in paths)
        {
            double pw = path.PathW > 0 ? path.PathW : Math.Max(1, bounds.Width);
            double ph = path.PathH > 0 ? path.PathH : Math.Max(1, bounds.Height);

            // Scale factors: path-space → DIP bounds
            double scaleX = bounds.Width  / pw;
            double scaleY = bounds.Height / ph;

            LayoutPoint Map(double x, double y) =>
                new(bounds.Left + x * scaleX, bounds.Top + y * scaleY);

            LayoutPoint currentPos = new(bounds.Left, bounds.Top);
            LayoutPoint? figureStart = null;
            var segments = new List<ShapeSegment>();
            bool inFigure = false;

            foreach (var seg in path.Segments)
            {
                switch (seg.Kind)
                {
                    case CustomSegmentKind.MoveTo:
                    {
                        if (inFigure && figureStart.HasValue && segments.Count > 0)
                        {
                            contours.Add(new ShapeContour(figureStart.Value, segments.ToList(),
                                Closed: false, Filled: path.Fill));
                            segments.Clear();
                        }
                        currentPos = Map(seg.X, seg.Y);
                        figureStart = currentPos;
                        inFigure = true;
                        break;
                    }
                    case CustomSegmentKind.LineTo:
                    {
                        var end = Map(seg.X, seg.Y);
                        segments.Add(ShapeSegment.LineTo(end));
                        currentPos = end;
                        break;
                    }
                    case CustomSegmentKind.CubicBezTo:
                    {
                        var c1 = Map(seg.X,  seg.Y);
                        var c2 = Map(seg.X1, seg.Y1);
                        var ep = Map(seg.X2, seg.Y2);
                        segments.Add(ShapeSegment.BezierTo(c1, c2, ep));
                        currentPos = ep;
                        break;
                    }
                    case CustomSegmentKind.QuadBezTo:
                    {
                        // Elevate quadratic to cubic: P1 = cp + 2/3*(qcp-cp); P2 = ep + 2/3*(qcp-ep)
                        var qcp = Map(seg.X, seg.Y);
                        var ep  = Map(seg.X1, seg.Y1);
                        var c1 = new LayoutPoint(
                            currentPos.X + 2.0/3.0 * (qcp.X - currentPos.X),
                            currentPos.Y + 2.0/3.0 * (qcp.Y - currentPos.Y));
                        var c2 = new LayoutPoint(
                            ep.X + 2.0/3.0 * (qcp.X - ep.X),
                            ep.Y + 2.0/3.0 * (qcp.Y - ep.Y));
                        segments.Add(ShapeSegment.BezierTo(c1, c2, ep));
                        currentPos = ep;
                        break;
                    }
                    case CustomSegmentKind.ArcTo:
                    {
                        // OOXML arcTo: from currentPos, with radii wR/hR (in path-space),
                        // start angle stAng (degrees), sweep swAng (degrees).
                        // Convert to one arc segment (SVG-style end-point parameterization).
                        double wR = seg.WR * scaleX;
                        double hR = seg.HR * scaleY;
                        double stAng = seg.StAng * Math.PI / 180.0;
                        double swAng = seg.SwAng * Math.PI / 180.0;
                        double endAng = stAng + swAng;

                        // Center of the ellipse: back-compute from currentPos on the arc.
                        // currentPos = center + (wR*cos(stAng), hR*sin(stAng))
                        double cx = currentPos.X - wR * Math.Cos(stAng);
                        double cy = currentPos.Y - hR * Math.Sin(stAng);
                        var endPt = new LayoutPoint(cx + wR * Math.Cos(endAng), cy + hR * Math.Sin(endAng));

                        bool largeArc = Math.Abs(swAng) > Math.PI;
                        bool sweepCw  = swAng >= 0;
                        segments.Add(ShapeSegment.ArcTo(endPt, Math.Abs(wR), Math.Abs(hR), sweepCw, largeArc));
                        currentPos = endPt;
                        break;
                    }
                    case CustomSegmentKind.Close:
                    {
                        if (inFigure && figureStart.HasValue)
                        {
                            contours.Add(new ShapeContour(figureStart.Value, segments.ToList(),
                                Closed: true, Filled: path.Fill));
                            segments.Clear();
                            inFigure = false;
                            figureStart = null;
                        }
                        break;
                    }
                }
            }

            // Flush any open figure
            if (inFigure && figureStart.HasValue && segments.Count > 0)
            {
                contours.Add(new ShapeContour(figureStart.Value, segments.ToList(),
                    Closed: false, Filled: path.Fill));
            }
        }

        return contours.Count > 0 ? new ShapeGeometry(contours) : ShapeGeometry.Empty;
    }
}
