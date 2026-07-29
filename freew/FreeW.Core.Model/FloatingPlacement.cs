namespace FreeW.Core.Model;

/// <summary>
/// Shared floating-position state carried by any floating drawing object
/// (<see cref="Shape"/>, <see cref="Chart"/>, <see cref="SmartArt"/>, <see cref="WordArt"/>).
/// Mirrors the equivalent fields on <see cref="InlineImage"/> so the IO and rendering layers
/// share a single helper rather than per-type duplicates.
/// </summary>
public sealed class FloatingPlacement
{
    /// <summary>
    /// How the object relates to the surrounding text. Defaults to
    /// <see cref="ImageWrapping.Inline"/> so existing objects are unaffected.
    /// </summary>
    public ImageWrapping Wrapping { get; set; } = ImageWrapping.Inline;

    /// <summary>
    /// The Word wrapping side policy for square or tight objects. Defaults to both sides.
    /// </summary>
    public FloatingWrapTextSide WrapTextSide { get; set; } = FloatingWrapTextSide.BothSides;

    /// <summary>True when the object is floating (Wrapping != Inline).</summary>
    public bool IsFloating => Wrapping != ImageWrapping.Inline;

    /// <summary>Horizontal offset in points from <see cref="HorizontalAnchor"/>.</summary>
    public double HorizontalOffsetPt { get; set; }

    /// <summary>Vertical offset in points from <see cref="VerticalAnchor"/>.</summary>
    public double VerticalOffsetPt { get; set; }

    /// <summary>The frame the horizontal offset is measured from.</summary>
    public HorizontalAnchor HorizontalAnchor { get; set; } = HorizontalAnchor.Column;

    /// <summary>The frame the vertical offset is measured from.</summary>
    public VerticalAnchor VerticalAnchor { get; set; } = VerticalAnchor.Paragraph;

    /// <summary>
    /// Z-order index (<c>wp:anchor/@relativeHeight</c>). Higher values render in front.
    /// Defaults to 0. Ignored when <see cref="IsFloating"/> is false.
    /// </summary>
    public int ZOrderIndex { get; set; }

    /// <summary>Creates an independent copy for document merge and undo snapshots.</summary>
    public FloatingPlacement Clone() => new()
    {
        Wrapping = Wrapping,
        WrapTextSide = WrapTextSide,
        HorizontalOffsetPt = HorizontalOffsetPt,
        VerticalOffsetPt = VerticalOffsetPt,
        HorizontalAnchor = HorizontalAnchor,
        VerticalAnchor = VerticalAnchor,
        ZOrderIndex = ZOrderIndex
    };
}
