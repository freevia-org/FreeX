namespace FreeW.Core.Model;

/// <summary>
/// A floating drawing-object group: holds two or more <see cref="Children"/> (each an
/// <see cref="InlineImage"/>, <see cref="Shape"/>, <see cref="Chart"/>, <see cref="SmartArt"/> or
/// <see cref="WordArt"/>), a shared <see cref="Placement"/> (group-level anchor / z-order), and an
/// explicit <see cref="WidthPt"/> / <see cref="HeightPt"/> that bounds all children.
///
/// Serialised as <c>wp:anchor / a:graphic / a:graphicData[uri=wpg] / wpg:wgp</c>; each child is
/// emitted inside <c>wpg:wgp</c> as its natural DrawingML element
/// (<c>wpg:pic</c> / <c>wps:wsp</c> / etc.) positioned by a child-local <c>a:xfrm</c> offset
/// relative to the group origin. <see cref="ChildOffset"/> stores that (x, y) in points.
/// </summary>
public sealed class DrawingGroup
{
    /// <summary>
    /// Group-level floating placement (anchor mode, offsets from page/margin/column, z-order).
    /// Always non-null for a group (groups are always floating).
    /// </summary>
    public FloatingPlacement Placement { get; set; } = new FloatingPlacement
    {
        Wrapping = ImageWrapping.Square
    };

    /// <summary>Overall bounding-box width of the group, in points.</summary>
    public double WidthPt { get; set; } = 144;

    /// <summary>Overall bounding-box height of the group, in points.</summary>
    public double HeightPt { get; set; } = 72;

    /// <summary>
    /// Group-level DrawingML rotation in degrees. This is stored on the group's
    /// <c>wpg:grpSpPr/a:xfrm</c>, not on its individual children.
    /// </summary>
    public double RotationAngle { get; set; }

    /// <summary>Whether the complete group is mirrored horizontally about its centre.</summary>
    public bool FlipH { get; set; }

    /// <summary>Whether the complete group is mirrored vertically about its centre.</summary>
    public bool FlipV { get; set; }

    /// <summary>
    /// The grouped drawing objects.  Each element is one of:
    /// <see cref="InlineImage"/>, <see cref="Shape"/>, <see cref="Chart"/>,
    /// <see cref="SmartArt"/>, or <see cref="WordArt"/>.
    /// </summary>
    public List<object> Children { get; } = [];

    /// <summary>
    /// Per-child offsets (x, y) in points from the group's top-left origin.
    /// Parallel to <see cref="Children"/>: index i gives the offset of Children[i].
    /// </summary>
    public List<(double X, double Y)> ChildOffsets { get; } = [];

    /// <summary>True when the group has at least two children (the minimum for a valid group).</summary>
    public bool IsValid => Children.Count >= 2;

    /// <summary>Always true — groups are always floating objects on the overlay canvas.</summary>
    public bool IsFloating => true;

    /// <summary>
    /// Returns the width of child <paramref name="index"/> by inspecting its runtime type.
    /// </summary>
    public double ChildWidthPt(int index) => Children[index] switch
    {
        InlineImage img => img.WidthPt,
        Shape s => s.WidthPt,
        Chart c => c.WidthPt,
        SmartArt sa => sa.WidthPt,
        WordArt wa => wa.FontSizePt * Math.Max(1, wa.Text.Length) * 0.62,
        _ => 36
    };

    /// <summary>Returns the height of child <paramref name="index"/> by inspecting its runtime type.</summary>
    public double ChildHeightPt(int index) => Children[index] switch
    {
        InlineImage img => img.HeightPt,
        Shape s => s.HeightPt,
        Chart c => c.HeightPt,
        SmartArt sa => sa.HeightPt,
        WordArt wa => wa.FontSizePt * 1.6,
        _ => 36
    };
}
