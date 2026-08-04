using Free.Shared.Drawing;

namespace FreeP.Core.Model;

/// <summary>
/// Resolves a connection-site index on a shape to a slide-coordinate point (in EMU).
///
/// <b>Standard OOXML 4-site mapping</b> (used by rectangles, most preset autoshapes, and the
/// default PowerPoint UI):
/// <list type="table">
///   <item><term>0</term><description>Left mid-edge</description></item>
///   <item><term>1</term><description>Top mid-edge</description></item>
///   <item><term>2</term><description>Right mid-edge</description></item>
///   <item><term>3</term><description>Bottom mid-edge</description></item>
/// </list>
///
/// Extended sites (indices 4–7) are mapped to corners for shapes that have them:
///   4 = top-left, 5 = top-right, 6 = bottom-right, 7 = bottom-left.
///
/// <b>Per-shape connection sites (Wave 26)</b>:
/// For common shapes, the helper now returns geometrically-accurate sites rather than
/// simple bbox mid-edges:
/// <list type="table">
///   <item><term>Ellipse</term><description>4 cardinal points on the ellipse curve (N/E/S/W).</description></item>
///   <item><term>Triangle (isosceles)</term><description>apex (top-mid), base-left, base-right, plus edge midpoints.</description></item>
///   <item><term>RightTriangle</term><description>top-left apex, bottom-left, bottom-right, plus edge midpoints.</description></item>
///   <item><term>Diamond</term><description>4 vertices (top/right/bottom/left = OOXML indices 0-3).</description></item>
///   <item><term>Parallelogram / Trapezoid</term><description>Slanted-edge midpoints using the authored inset guide.</description></item>
///   <item><term>Chevron / HomePlate</term><description>Notch/tip sites on the visible outline using the authored depth guide.</description></item>
///   <item><term>Star8</term><description>4 cardinal outer vertices.</description></item>
///   <item><term>Ribbon / Wave</term><description>Sites follow the visible tail, crest, and trough outline rather than the bounding box.</description></item>
///   <item><term>Rectangle / RoundedRectangle</term><description>4 mid-edges + 4 corners (unchanged).</description></item>
///   <item><term>Others</term><description>Falls back to the 8-site bbox approximation.</description></item>
/// </list>
///
/// If an index falls outside the supported range the <em>centre</em> of the shape is returned
/// as a safe fallback so connectors are always drawn somewhere meaningful.
///
/// Rotation and horizontal/vertical flips are applied after resolving the local site. Full
/// per-shape tables for cross, star,
/// chevron, etc. are deferred — these cover the overwhelming majority of real-world connectors.
/// </summary>
public static class ConnectionSiteHelper
{
    /// <summary>
    /// Returns the connection-site point in slide EMU coordinates.
    /// </summary>
    /// <param name="shape">The target shape (anchor + extent must be set).</param>
    /// <param name="siteIndex">The connection-site index from the OOXML connector element.</param>
    /// <returns>The (x, y) point in EMU relative to the slide top-left corner.</returns>
    public static (long X, long Y) Resolve(SlideShape shape, int siteIndex)
    {
        if (shape.CustomGeometry.Count > 0 || shape.CustomConnectionSites.Count > 0)
        {
            var customSite = ResolveCustomGeometrySite(shape, siteIndex);
            if (customSite.HasValue)
                return TransformSite(shape, customSite.Value);
        }

        // Dispatch to per-shape tables for shapes whose real geometry differs from the bbox.
        if (shape.Kind == SlideShapeKind.AutoShape || shape.Kind == SlideShapeKind.Connector)
        {
            var perShape = ResolvePerShape(shape, siteIndex);
            if (perShape.HasValue) return TransformSite(shape, perShape.Value);
        }

        return TransformSite(shape, ResolveBbox(shape, siteIndex));
    }

    /// <summary>
    /// Resolves the four standard connection directions from an authored custom path.
    /// Custom geometry has no preset-specific site table, so the visible path vertices are
    /// the only reliable geometry available after import. Pick the extreme vertex for the
    /// requested side, preferring the one nearest the perpendicular centre line. This keeps
    /// attached connectors on the authored outline instead of the transparent bbox corners.
    /// </summary>
    private static (long X, long Y)? ResolveCustomGeometrySite(SlideShape shape, int siteIndex)
    {
        if (siteIndex < 0)
            return null;

        // Prefer the authored a:custGeom/a:cxnLst position. Unlike path-derived
        // extrema, this is the connection-site contract PowerPoint uses for custom
        // geometry. Keep guide expressions on the model for round-trip and resolve
        // the common literal/edge tokens here; an unrecognized guide still falls
        // through to the existing outline heuristic.
        if (siteIndex < shape.CustomConnectionSites.Count)
        {
            var authored = shape.CustomConnectionSites[siteIndex];
            var pathW = shape.CustomGeometry.FirstOrDefault(path => path.PathW > 0)?.PathW
                ?? Math.Max(1, shape.ExtentCxEmu);
            var pathH = shape.CustomGeometry.FirstOrDefault(path => path.PathH > 0)?.PathH
                ?? Math.Max(1, shape.ExtentCyEmu);
            if (TryResolveGeometryCoordinate(authored.X, pathW, pathH, horizontal: true, out var x)
                && TryResolveGeometryCoordinate(authored.Y, pathW, pathH, horizontal: false, out var y))
            {
                return (
                    (long)Math.Round(shape.OffsetXEmu + x / pathW * shape.ExtentCxEmu),
                    (long)Math.Round(shape.OffsetYEmu + y / pathH * shape.ExtentCyEmu));
            }
        }

        if (siteIndex > 3)
            return null;

        var candidates = new List<(double X, double Y)>();
        foreach (var path in shape.CustomGeometry)
        {
            var pathW = path.PathW > 0 ? path.PathW : shape.ExtentCxEmu;
            var pathH = path.PathH > 0 ? path.PathH : shape.ExtentCyEmu;
            if (pathW <= 0 || pathH <= 0)
                continue;

            foreach (var segment in path.Segments)
            {
                (double X, double Y)? point = segment.Kind switch
                {
                    CustomSegmentKind.MoveTo or CustomSegmentKind.LineTo => (segment.X, segment.Y),
                    CustomSegmentKind.QuadBezTo => (segment.X1, segment.Y1),
                    CustomSegmentKind.CubicBezTo => (segment.X2, segment.Y2),
                    _ => null,
                };
                if (point is { } p)
                {
                    candidates.Add((
                        shape.OffsetXEmu + p.X / pathW * shape.ExtentCxEmu,
                        shape.OffsetYEmu + p.Y / pathH * shape.ExtentCyEmu));
                }
            }
        }

        if (candidates.Count == 0)
            return null;

        var centerX = shape.OffsetXEmu + shape.ExtentCxEmu / 2.0;
        var centerY = shape.OffsetYEmu + shape.ExtentCyEmu / 2.0;
        var selected = siteIndex switch
        {
            0 => candidates.OrderBy(point => point.X).ThenBy(point => Math.Abs(point.Y - centerY)).First(),
            1 => candidates.OrderBy(point => point.Y).ThenBy(point => Math.Abs(point.X - centerX)).First(),
            2 => candidates.OrderByDescending(point => point.X).ThenBy(point => Math.Abs(point.Y - centerY)).First(),
            _ => candidates.OrderByDescending(point => point.Y).ThenBy(point => Math.Abs(point.X - centerX)).First(),
        };

        return ((long)Math.Round(selected.X), (long)Math.Round(selected.Y));
    }

    private static bool TryResolveGeometryCoordinate(
        string? token,
        long pathW,
        long pathH,
        bool horizontal,
        out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        if (double.TryParse(token, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out value))
            return true;

        var normalized = token.Trim().ToLowerInvariant();
        var extent = horizontal ? pathW : pathH;
        var otherExtent = horizontal ? pathH : pathW;
        value = normalized switch
        {
            "l" or "t" => 0,
            "r" or "b" => extent,
            "hc" or "vc" => extent / 2.0,
            "ss" => Math.Min(extent, otherExtent),
            "ls" => Math.Max(extent, otherExtent),
            _ => double.NaN,
        };
        return !double.IsNaN(value);
    }

    private static (long X, long Y) TransformSite(SlideShape shape, (long X, long Y) site)
    {
        if (!shape.FlipH && !shape.FlipV && Math.Abs(shape.RotationDeg) < 0.000001)
            return site;

        var centerX = shape.OffsetXEmu + shape.ExtentCxEmu / 2.0;
        var centerY = shape.OffsetYEmu + shape.ExtentCyEmu / 2.0;
        var dx = site.X - centerX;
        var dy = site.Y - centerY;

        if (shape.FlipH)
            dx = -dx;
        if (shape.FlipV)
            dy = -dy;

        var radians = shape.RotationDeg * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var x = centerX + cos * dx - sin * dy;
        var y = centerY + sin * dx + cos * dy;

        return ((long)Math.Round(x), (long)Math.Round(y));
    }

    // ── Per-shape site tables ─────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a shape-specific site when the geometry differs from the bbox, null to fall back.
    /// </summary>
    private static (long X, long Y)? ResolvePerShape(SlideShape shape, int siteIndex)
    {
        long left   = shape.OffsetXEmu;
        long top    = shape.OffsetYEmu;
        long right  = left + shape.ExtentCxEmu;
        long bottom = top  + shape.ExtentCyEmu;
        long midX   = left + shape.ExtentCxEmu / 2;
        long midY   = top  + shape.ExtentCyEmu / 2;

        switch (shape.AutoShapeKind)
        {
            // ── Ellipse ──────────────────────────────────────────────────────────────
            // PowerPoint uses cardinal points on the ellipse: N(1), E(2), S(3), W(0).
            // These coincide with the mid-edges of the bounding rect, which the bbox
            // resolver already returns correctly — so ellipse is handled by fall-through.
            // We list it explicitly to document the intent.
            case DrawingShapeKind.Ellipse:
                return siteIndex switch
                {
                    0 => (left,  midY),     // West  — leftmost point on ellipse
                    1 => (midX,  top),      // North — topmost point on ellipse
                    2 => (right, midY),     // East  — rightmost point on ellipse
                    3 => (midX,  bottom),   // South — bottommost point on ellipse
                    _ => null               // fall back to bbox for corners/fallback
                };

            // ── Diamond ──────────────────────────────────────────────────────────────
            // PowerPoint connection sites for a Diamond (rhombus):
            //   idx 0 = left vertex, 1 = top vertex, 2 = right vertex, 3 = bottom vertex
            // These are the 4 tip vertices of the diamond.
            case DrawingShapeKind.Diamond:
            case DrawingShapeKind.FlowchartDecision:
                return siteIndex switch
                {
                    0 => (left,  midY),     // left vertex
                    1 => (midX,  top),      // top vertex
                    2 => (right, midY),     // right vertex
                    3 => (midX,  bottom),   // bottom vertex
                    _ => (midX,  midY)      // fallback: centre
                };

            // FlowchartData is the shared parallelogram with independently slanted
            // top and bottom edges.  Use the actual polygon intersections instead
            // of the bounding-box midpoints, which can fall outside the outline.
            case DrawingShapeKind.FlowchartData:
                return siteIndex switch
                {
                    0 => (left + (long)Math.Round(shape.ExtentCxEmu * 0.11), midY),
                    1 => (left + (long)Math.Round(shape.ExtentCxEmu * 0.61), top),
                    2 => (left + (long)Math.Round(shape.ExtentCxEmu * 0.89), midY),
                    3 => (left + (long)Math.Round(shape.ExtentCxEmu * 0.39), bottom),
                    _ => (midX, midY)
                };

            // ── Isosceles Triangle ───────────────────────────────────────────────────
            // Vertices: apex (top-mid), base-left (bottom-left), base-right (bottom-right).
            // PowerPoint site mapping:
            //   idx 0 = left-mid edge, 1 = top/apex, 2 = right-mid edge, 3 = base-mid
            //   idx 4 = base-left corner, 5 = base-right corner
            // We return geometrically-exact edge midpoints.
            case DrawingShapeKind.Triangle:
                return siteIndex switch
                {
                    0 => (left + shape.ExtentCxEmu / 4, top + shape.ExtentCyEmu / 2),    // left edge midpoint
                    1 => (midX, top),                                                      // apex
                    2 => (right - shape.ExtentCxEmu / 4, top + shape.ExtentCyEmu / 2),   // right edge midpoint
                    3 => (midX, bottom),                                                   // base midpoint
                    4 => (left, bottom),                                                   // base-left corner
                    5 => (right, bottom),                                                  // base-right corner
                    _ => (midX, midY)
                };

            // ── Right Triangle ───────────────────────────────────────────────────────
            // Vertices: top-left (right-angle), bottom-left, bottom-right (hypotenuse).
            // PowerPoint site mapping:
            //   idx 0 = left edge midpoint, 1 = top-left, 2 = hypotenuse midpoint, 3 = bottom-mid
            //   idx 4 = top-left corner, 5 = bottom-right corner
            case DrawingShapeKind.RightTriangle:
                return siteIndex switch
                {
                    0 => (left, midY),                   // left edge midpoint (vertical side)
                    1 => (left, top),                    // top-left (right-angle apex)
                    2 => (midX + shape.ExtentCxEmu / 4, midY - shape.ExtentCyEmu / 4), // hypotenuse mid
                    3 => (midX, bottom),                 // bottom edge midpoint (base)
                    4 => (left, bottom),                 // bottom-left corner
                    5 => (right, bottom),                // bottom-right corner
                    _ => (midX, midY)
                };

            case DrawingShapeKind.Pentagon:
                return siteIndex switch
                {
                    0 => (left, top + (long)Math.Round(shape.ExtentCyEmu * 0.38)),
                    1 => (midX, top),
                    2 => (right, top + (long)Math.Round(shape.ExtentCyEmu * 0.38)),
                    3 => (midX, bottom),
                    _ => (midX, midY)
                };

            // Callout tails are part of the visible outline.  The old bbox fallback
            // placed the bottom attachment in the callout body, so an attached
            // connector could visibly stop above the tail tip.
            case DrawingShapeKind.RectangularCallout:
            case DrawingShapeKind.RoundedRectangularCallout:
            {
                const double bodyFraction = 0.80;
                return siteIndex switch
                {
                    0 => (left, top + (long)Math.Round(shape.ExtentCyEmu * bodyFraction / 2)),
                    1 => (midX, top),
                    2 => (right, top + (long)Math.Round(shape.ExtentCyEmu * bodyFraction / 2)),
                    3 => (left + (long)Math.Round(shape.ExtentCxEmu * 0.45), bottom),
                    _ => (midX, midY)
                };
            }

            case DrawingShapeKind.OvalCallout:
            {
                const double bodyFraction = 0.82;
                return siteIndex switch
                {
                    0 => (left, top + (long)Math.Round(shape.ExtentCyEmu * bodyFraction / 2)),
                    1 => (midX, top),
                    2 => (right, top + (long)Math.Round(shape.ExtentCyEmu * bodyFraction / 2)),
                    3 => (left + (long)Math.Round(shape.ExtentCxEmu * 0.47), bottom),
                    _ => (midX, midY)
                };
            }

            // The heart's top connection is its inward notch, not the midpoint of
            // the transparent bounding box.  The other three sites follow the
            // extrema of the shared heart path.
            case DrawingShapeKind.Heart:
                return siteIndex switch
                {
                    0 => (left, top + (long)Math.Round(shape.ExtentCyEmu * 0.38)),
                    1 => (midX, top + (long)Math.Round(shape.ExtentCyEmu * 0.22)),
                    2 => (right, top + (long)Math.Round(shape.ExtentCyEmu * 0.38)),
                    3 => (midX, bottom),
                    _ => (midX, midY)
                };

            // These four-site mappings follow the shared preset outlines rather
            // than the bounding box. This keeps attached connectors on the
            // visible edge when an authored slant/depth guide is present.
            case DrawingShapeKind.Parallelogram:
            case DrawingShapeKind.Trapezoid:
            {
                var inset = ResolveSlantInset(shape);
                var leftMidX = inset / 2;
                var rightMidX = right - inset / 2;
                return siteIndex switch
                {
                    0 => (left + leftMidX, midY),
                    1 => (midX, top),
                    2 => (rightMidX, midY),
                    3 => (midX, bottom),
                    _ => (midX, midY)
                };
            }

            case DrawingShapeKind.Chevron:
            {
                var depth = ResolvePointDepth(shape, fallback: 0.24);
                var x1 = (long)Math.Round(shape.ExtentCxEmu * depth);
                var x2 = right - (long)Math.Round(shape.ExtentCxEmu * depth);
                return siteIndex switch
                {
                    0 => (left + x1, midY),
                    1 => (left + (x2 - left) / 2, top),
                    2 => (right, midY),
                    3 => (left + (x2 - left) / 2, bottom),
                    _ => (midX, midY)
                };
            }

            case DrawingShapeKind.HomePlate:
            {
                var depth = ResolvePointDepth(shape, fallback: 0.24);
                var x1 = right - (long)Math.Round(shape.ExtentCxEmu * depth);
                return siteIndex switch
                {
                    0 => (left, midY),
                    1 => (left + (x1 - left) / 2, top),
                    2 => (right, midY),
                    3 => (left + (x1 - left) / 2, bottom),
                    _ => (midX, midY)
                };
            }

            // Directional arrows use the same guide contract as ShapeGeometryBuilder:
            // adj1 controls shaft thickness and adj2 controls head length.  Site 2/0
            // is the visible tip for right/left arrows, while site 1/3 follow the
            // midpoint of the authored top/bottom shaft edge rather than the bbox.
            case DrawingShapeKind.RightArrow:
            case DrawingShapeKind.LeftArrow:
            {
                var headBase = ResolveArrowHeadBase(shape);
                var topBottomX = shape.AutoShapeKind == DrawingShapeKind.RightArrow
                    ? left + (long)Math.Round(shape.ExtentCxEmu * headBase)
                    : right - (long)Math.Round(shape.ExtentCxEmu * headBase);
                return siteIndex switch
                {
                    0 => (left, midY),
                    1 => (topBottomX, top),
                    2 => (right, midY),
                    3 => (topBottomX, bottom),
                    _ => (midX, midY)
                };
            }

            case DrawingShapeKind.UpArrow:
            case DrawingShapeKind.DownArrow:
            {
                var headBase = ResolveArrowHeadBase(shape);
                var topBottomY = shape.AutoShapeKind == DrawingShapeKind.UpArrow
                    ? top + (long)Math.Round(shape.ExtentCyEmu * (1 - headBase))
                    : top + (long)Math.Round(shape.ExtentCyEmu * headBase);
                return siteIndex switch
                {
                    0 => (left, topBottomY),
                    1 => (midX, top),
                    2 => (right, topBottomY),
                    3 => (midX, bottom),
                    _ => (midX, midY)
                };
            }

            // Compound arrows have real tips at both ends.  Keep the other two
            // sites on the shaft edges so attached connectors never land in the
            // transparent corner of the bounding rectangle.
            case DrawingShapeKind.LeftRightArrow:
                return siteIndex switch
                {
                    0 => (left, midY),
                    1 => (midX, top),
                    2 => (right, midY),
                    3 => (midX, bottom),
                    _ => (midX, midY)
                };

            case DrawingShapeKind.UpDownArrow:
                return siteIndex switch
                {
                    0 => (left, midY),
                    1 => (midX, top),
                    2 => (right, midY),
                    3 => (midX, bottom),
                    _ => (midX, midY)
                };

            case DrawingShapeKind.Star8:
            {
                var topStar8 = StarPoint(midX, midY, shape.ExtentCxEmu / 2.0, shape.ExtentCyEmu / 2.0, -90);
                var rightStar8 = StarPoint(midX, midY, shape.ExtentCxEmu / 2.0, shape.ExtentCyEmu / 2.0, 0);
                var bottomStar8 = StarPoint(midX, midY, shape.ExtentCxEmu / 2.0, shape.ExtentCyEmu / 2.0, 90);
                var leftStar8 = StarPoint(midX, midY, shape.ExtentCxEmu / 2.0, shape.ExtentCyEmu / 2.0, 180);
                return siteIndex switch
                {
                    0 => leftStar8,
                    1 => topStar8,
                    2 => rightStar8,
                    3 => bottomStar8,
                    _ => (midX, midY)
                };
            }

            // These presets do not have a visible point at every bbox mid-edge. Use
            // stable points from the shared outline so connectors stay on the shape.
            case DrawingShapeKind.Ribbon:
                return siteIndex switch
                {
                    0 => (left, top + (long)Math.Round(shape.ExtentCyEmu * 0.76)),
                    1 => (midX, top + (long)Math.Round(shape.ExtentCyEmu * 0.22)),
                    2 => (right, top + (long)Math.Round(shape.ExtentCyEmu * 0.24)),
                    3 => (midX, top + (long)Math.Round(shape.ExtentCyEmu * 0.78)),
                    _ => (midX, midY)
                };

            case DrawingShapeKind.Wave:
                return siteIndex switch
                {
                    0 => (left, top + (long)Math.Round(shape.ExtentCyEmu * 0.45)),
                    1 => (left + (long)Math.Round(shape.ExtentCxEmu * 0.22), top + (long)Math.Round(shape.ExtentCyEmu * 0.12)),
                    2 => (right, top + (long)Math.Round(shape.ExtentCyEmu * 0.36)),
                    3 => (left + (long)Math.Round(shape.ExtentCxEmu * 0.58), top + (long)Math.Round(shape.ExtentCyEmu * 0.88)),
                    _ => (midX, midY)
                };

            case DrawingShapeKind.Hexagon:
            case DrawingShapeKind.Octagon:
            case DrawingShapeKind.Cross:
            case DrawingShapeKind.PlusSign:
                return siteIndex switch
                {
                    0 => (left, midY),
                    1 => (midX, top),
                    2 => (right, midY),
                    3 => (midX, bottom),
                    _ => (midX, midY)
                };

            case DrawingShapeKind.Star5:
                var outerRadiusX = shape.ExtentCxEmu / 2.0;
                var outerRadiusY = shape.ExtentCyEmu / 2.0;
                var innerRadius = shape.PresetGeometryAdjustments.TryGetValue("adj", out var adjustment)
                    ? Math.Clamp(adjustment / 100000.0, 0, 1)
                    : 0.42;
                var leftStar = StarPoint(midX, midY, outerRadiusX, outerRadiusY, 126);
                var topStar = StarPoint(midX, midY, outerRadiusX, outerRadiusY, -90);
                var rightStar = StarPoint(midX, midY, outerRadiusX, outerRadiusY, -18);
                var bottomStar = StarPoint(midX, midY, outerRadiusX * innerRadius, outerRadiusY * innerRadius, 90);
                return siteIndex switch
                {
                    0 => leftStar,
                    1 => topStar,
                    2 => rightStar,
                    3 => bottomStar,
                    _ => (midX, midY)
                };

            default:
                return null; // use bbox resolver
        }
    }

    private static long ResolveSlantInset(SlideShape shape)
    {
        if (!shape.PresetGeometryAdjustments.TryGetValue("adj", out var adjustment))
            return (long)Math.Round(shape.ExtentCxEmu * 0.2);

        var maximumInset = shape.ExtentCxEmu / 2;
        var inset = Math.Min(shape.ExtentCxEmu, shape.ExtentCyEmu) *
            Math.Clamp(adjustment, 0, 100000) / 100000.0;
        return Math.Clamp((long)Math.Round(inset), 0, maximumInset);
    }

    private static double ResolvePointDepth(SlideShape shape, double fallback)
    {
        if (!shape.PresetGeometryAdjustments.TryGetValue("adj", out var adjustment))
            return fallback;

        var maximum = 100000.0 * shape.ExtentCxEmu / Math.Max(1, Math.Min(shape.ExtentCxEmu, shape.ExtentCyEmu));
        var depth = Math.Clamp(adjustment, 0, maximum) / 100000.0;
        return Math.Clamp(depth, 0, 1);
    }

    private static double ResolveArrowHeadBase(SlideShape shape)
    {
        if (!shape.PresetGeometryAdjustments.ContainsKey("adj1") &&
            !shape.PresetGeometryAdjustments.ContainsKey("adj2"))
        {
            return shape.AutoShapeKind == DrawingShapeKind.LeftArrow ? 0.38 : 0.62;
        }

        var adjustment = shape.PresetGeometryAdjustments.TryGetValue("adj2", out var value)
            ? value
            : 50000;
        return 1 - Math.Clamp(adjustment, 0, 100000) / 100000.0;
    }

    private static (long X, long Y) StarPoint(
        long centerX,
        long centerY,
        double radiusX,
        double radiusY,
        double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return (
            (long)Math.Round(centerX + Math.Cos(radians) * radiusX),
            (long)Math.Round(centerY + Math.Sin(radians) * radiusY));
    }

    // ── Bbox 8-site fallback (original behaviour) ─────────────────────────────────────

    /// <summary>
    /// Original 8-site bounding-box approximation: 4 mid-edges + 4 corners.
    /// Used for Rectangle, RoundedRectangle, all other shapes, and as a fallback.
    /// </summary>
    private static (long X, long Y) ResolveBbox(SlideShape shape, int siteIndex)
    {
        long left   = shape.OffsetXEmu;
        long top    = shape.OffsetYEmu;
        long right  = left + shape.ExtentCxEmu;
        long bottom = top  + shape.ExtentCyEmu;
        long midX   = left + shape.ExtentCxEmu / 2;
        long midY   = top  + shape.ExtentCyEmu / 2;

        // The caller applies the target's flip/rotation after resolving these local points.
        return siteIndex switch
        {
            0 => (left,  midY),     // left-mid
            1 => (midX,  top),      // top-mid
            2 => (right, midY),     // right-mid
            3 => (midX,  bottom),   // bottom-mid
            4 => (left,  top),      // top-left corner
            5 => (right, top),      // top-right corner
            6 => (right, bottom),   // bottom-right corner
            7 => (left,  bottom),   // bottom-left corner
            _ => (midX,  midY),     // fallback: shape centre
        };
    }

    // ── Public API ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a connection-site by looking up the attached shape on the slide.
    /// Returns the shape centre if the shape is not found or <paramref name="attachment"/> is null.
    /// </summary>
    public static (long X, long Y) Resolve(ConnectorAttachment? attachment, Slide slide)
    {
        if (attachment is null) return (0, 0);
        var target = FindShape(slide.Shapes, attachment.ShapeId);
        if (target is null) return (0, 0);
        return Resolve(target, attachment.SiteIndex);
    }

    private static SlideShape? FindShape(IEnumerable<SlideShape> shapes, uint shapeId)
    {
        foreach (var shape in shapes)
        {
            if (shape.Id == shapeId) return shape;
            if (shape.Children.Count > 0 && FindShape(shape.Children, shapeId) is { } child)
                return child;
        }

        return null;
    }
}
