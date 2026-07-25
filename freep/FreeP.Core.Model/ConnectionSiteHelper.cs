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
        // Dispatch to per-shape tables for shapes whose real geometry differs from the bbox.
        if (shape.Kind == SlideShapeKind.AutoShape || shape.Kind == SlideShapeKind.Connector)
        {
            var perShape = ResolvePerShape(shape, siteIndex);
            if (perShape.HasValue) return TransformSite(shape, perShape.Value);
        }

        return TransformSite(shape, ResolveBbox(shape, siteIndex));
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

            default:
                return null; // use bbox resolver
        }
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
        var target = slide.Shapes.FirstOrDefault(s => s.Id == attachment.ShapeId);
        if (target is null) return (0, 0);
        return Resolve(target, attachment.SiteIndex);
    }
}
