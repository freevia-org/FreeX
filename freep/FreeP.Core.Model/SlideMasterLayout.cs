namespace FreeP.Core.Model;

/// <summary>
/// A slide master: the root of the layout/theme inheritance hierarchy. Holds placeholder shapes
/// (with default geometry and text styles) that slide layouts and slides inherit from.
/// Corresponds to <c>slideMaster*.xml</c> in the .pptx package.
/// </summary>
public sealed class SlideMaster
{
    /// <summary>Stable identifier (from the relationship id, e.g. "rId1").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Reference to the theme used by this master (by theme name).</summary>
    public string? ThemeId { get; set; }

    /// <summary>
    /// Placeholder shapes on this master, in z-order. These define default geometry and
    /// text properties for all placeholders on descendant layouts and slides.
    /// </summary>
    public List<SlideShape> Placeholders { get; } = new();

    /// <summary>Optional background fill for this master (inherited by layouts/slides).</summary>
    public ShapeFill? Background { get; set; }
}

/// <summary>Slide layout type identifiers from OOXML <c>p:sld type="..."</c>.</summary>
public enum SlideLayoutType
{
    Title = 0,
    TitleContent = 1,
    TitleOnly = 2,
    Blank = 3,
    TwoContent = 4,
    Comparison = 5,
    ContentCaption = 6,
    PictureCaption = 7,
    Custom = 8
}

/// <summary>
/// A slide layout: defines the default placeholder positions and styles for a class of slides.
/// Corresponds to <c>slideLayout*.xml</c> in the .pptx package.
/// </summary>
public sealed class SlideLayout
{
    /// <summary>Stable identifier (from the relationship id, e.g. "rId1").</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable layout name (from <c>p:cSld name="..."</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Layout type (see OOXML §19.3.1.16 p:sldLayout type).</summary>
    public SlideLayoutType LayoutType { get; set; } = SlideLayoutType.Custom;

    /// <summary>Reference to the parent slide master (by master Id).</summary>
    public string? MasterId { get; set; }

    /// <summary>
    /// Placeholder shapes on this layout, in z-order. These override master defaults and
    /// provide default geometry/style for slide placeholders.
    /// </summary>
    public List<SlideShape> Placeholders { get; } = new();

    /// <summary>Optional background fill for this layout (overrides master, inherited by slides).</summary>
    public ShapeFill? Background { get; set; }
}
