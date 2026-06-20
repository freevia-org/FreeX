namespace FreeP.Core.Model;

/// <summary>
/// A single slide: a stable id, a title string, and a list of minimal <see cref="SlideShape"/> stubs.
/// Kept intentionally minimal — enough to round-trip and to populate the placeholder canvas. There is no
/// geometry, styling, layout, or master-slide concept yet; those belong to the presentation-domain session.
/// </summary>
public sealed class Slide
{
    /// <summary>A stable identifier for the slide (generated unless explicitly supplied on load).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The slide title (shown in the slide list and as the placeholder canvas label).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>The slide's shape stubs, in z-order.</summary>
    public List<SlideShape> Shapes { get; } = new();
}

/// <summary>
/// A minimal shape stub. FreeP only models a kind + a text payload today; real shapes (position, size,
/// fills, lines, images, tables, charts) are deferred to the presentation-domain session.
/// </summary>
public sealed class SlideShape
{
    /// <summary>The shape kind (e.g. "text", "rectangle"). Free-form for now — no enum until the domain lands.</summary>
    public string Kind { get; set; } = "text";

    /// <summary>The shape's text content, if any.</summary>
    public string Text { get; set; } = string.Empty;
}
