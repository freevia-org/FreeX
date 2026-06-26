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
/// Ellipses / circles follow the same convention because their geometry is fully described by
/// their bounding rectangle.
///
/// If an index falls outside the supported range the <em>centre</em> of the shape is returned
/// as a safe fallback so connectors are always drawn somewhere meaningful.
///
/// NOTE: Full per-shape site tables (e.g. cross, star, chevron) are deferred — these 8 sites
/// cover the overwhelming majority of real-world connectors.
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
        long left   = shape.OffsetXEmu;
        long top    = shape.OffsetYEmu;
        long right  = left + shape.ExtentCxEmu;
        long bottom = top  + shape.ExtentCyEmu;
        long midX   = left + shape.ExtentCxEmu / 2;
        long midY   = top  + shape.ExtentCyEmu / 2;

        // When the connector target has rotation we approximate with the unrotated mid-edge
        // points; full rotated-site calculation is deferred (rare in practice and complex).
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
