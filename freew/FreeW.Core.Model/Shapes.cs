namespace FreeW.Core.Model;

// MODEL-DESIGN CHOICE (roadmap item W2, basic DrawingML shapes & text boxes):
// A shape is modelled as an OPTIONAL INLINE RUN MARK (Run.Shape) exactly like Run.Equation / Run.Image.
// This is the established FreeW pattern for every inline feature, so shapes flow through the existing run
// sequence, hyperlink/comment/revision wrapping, table cells, headers and footers with zero new plumbing,
// and they round-trip through docx as an inline w:drawing emitted in place of the run's w:t. A Shape is a
// single DrawingML preset geometry (rect / roundRect / ellipse / a plain text-box rect) plus a size, an
// optional solid fill, and â€” for text boxes â€” text content held as a list of paragraphs (the same Paragraph
// model used everywhere else, so the txbx body round-trips through the ordinary paragraph reader/writer).
// We deliberately stop at preset geometries + simple text: no connectors, grouping or freeform geometry.

/// <summary>
/// The preset DrawingML geometry of a <see cref="Shape"/>. Maps onto <c>a:prstGeom/@prst</c>:
/// <see cref="Rectangle"/> â†’ <c>rect</c>, <see cref="RoundedRectangle"/> â†’ <c>roundRect</c>,
/// <see cref="Ellipse"/> â†’ <c>ellipse</c>. <see cref="TextBox"/> is a rectangle whose purpose is to hold
/// text (it also serialises as <c>rect</c>, but the model distinguishes it so a caller's intent survives).
/// </summary>
public enum ShapeKind
{
    Rectangle,
    RoundedRectangle,
    Ellipse,
    TextBox
}

/// <summary>
/// The text direction inside a text-box shape. Maps onto <c>wps:bodyPr/@vert</c> / <c>wps:bodyPr/@rot</c>:
/// <see cref="Horizontal"/> â†’ default (no attribute), <see cref="Rotate90"/> â†’ <c>vert="eaVert"</c> +
/// <c>rot="5400000"</c> (text rotated 90Â°), <see cref="Rotate270"/> â†’ <c>vert="eaVert"</c> +
/// <c>rot="-5400000"</c> (text rotated 270Â°).
/// </summary>
public enum ShapeTextDirection
{
    Horizontal,
    Rotate90,
    Rotate270
}

/// <summary>
/// A basic DrawingML shape or text box carried inline by a <see cref="Run"/> (via <see cref="Run.Shape"/>),
/// mirroring <see cref="InlineImage"/> and <see cref="Equation"/>. It serialises as an inline
/// <c>w:drawing</c> wrapping a <c>wps:wsp</c> (a preset-geometry shape) and, when it carries
/// <see cref="TextParagraphs"/>, a <c>wps:txbx/w:txbxContent</c> holding the text. Size is in points to
/// match the rest of the FreeW unit model; the fill is an optional hex colour.
/// </summary>
public sealed class Shape
{
    /// <summary>The preset geometry kind (rectangle, rounded rectangle, ellipse, or text box).</summary>
    public ShapeKind Kind { get; set; } = ShapeKind.Rectangle;

    /// <summary>Shape width in points (maps to the drawing's EMU extent on save).</summary>
    public double WidthPt { get; set; }

    /// <summary>Shape height in points (maps to the drawing's EMU extent on save).</summary>
    public double HeightPt { get; set; }

    /// <summary>
    /// Optional solid fill colour as an RRGGBB hex string (a leading '#' is tolerated and normalised away).
    /// Null means no explicit fill (the shape is emitted without an <c>a:solidFill</c>).
    /// </summary>
    public string? FillColorHex { get; set; }

    /// <summary>
    /// The text-box body: the paragraphs rendered inside the shape (serialised as <c>w:txbxContent</c>).
    /// Empty for a plain (text-less) shape. Re-uses the ordinary <see cref="Paragraph"/> model so the body
    /// round-trips through the existing paragraph reader/writer.
    /// </summary>
    public List<Paragraph> TextParagraphs { get; } = [];

    /// <summary>
    /// Optional outline color as an RRGGBB hex string. Null means no explicit outline.
    /// Maps onto <c>a:ln/a:solidFill/a:srgbClr/@val</c> in the shape properties.
    /// </summary>
    public string? OutlineColorHex { get; set; }

    /// <summary>
    /// Outline stroke width in points (default 0 = hairline / inherited). Only meaningful when
    /// <see cref="OutlineColorHex"/> is set. Maps onto <c>a:ln/@w</c> in EMU (1 pt = 12700 EMU).
    /// </summary>
    public double OutlineWidthPt { get; set; }

    /// <summary>
    /// Optional outline dash token (e.g. <c>"dash"</c>, <c>"sysDot"</c>, <c>"dashDot"</c>).
    /// Null means solid. Maps onto <c>a:ln/a:prstDash/@val</c>.
    /// </summary>
    public string? OutlineDash { get; set; }

    /// <summary>
    /// Accessibility description (maps onto <c>wp:docPr/@descr</c>). Null means no alt text.
    /// Mirrors <see cref="InlineImage.AltText"/>.
    /// </summary>
    public string? AltText { get; set; }

    /// <summary>
    /// Text direction for text-box shapes. Ignored for non-text-box shapes.
    /// Maps onto <c>wps:bodyPr/@vert</c> / <c>wps:bodyPr/@rot</c>.
    /// </summary>
    public ShapeTextDirection TextDirection { get; set; } = ShapeTextDirection.Horizontal;

    /// <summary>
    /// Floating-position state. Null (the default) means the shape is inline.
    /// Set <see cref="FloatingPlacement.Wrapping"/> to any non-Inline value to make it float.
    /// </summary>
    public FloatingPlacement? Placement { get; set; }

    /// <summary>True when this shape is floating (non-null Placement with Wrapping != Inline).</summary>
    public bool IsFloating => Placement?.IsFloating ?? false;
    public Shape() { }

    public Shape(ShapeKind kind, double widthPt, double heightPt, string? fillColorHex = null)
    {
        Kind = kind;
        WidthPt = widthPt;
        HeightPt = heightPt;
        FillColorHex = fillColorHex;
    }

    /// <summary>True when this shape carries text-box content (one or more paragraphs).</summary>
    public bool HasText => TextParagraphs.Count > 0;

    /// <summary>A best-effort plain-text rendering of the text-box content (paragraph texts joined by newlines).</summary>
    public string PlainText => string.Join('\n', TextParagraphs.Select(p => p.PlainText));

    /// <summary>Creates a preset-geometry shape (no text) of the given kind and size.</summary>
    public static Shape Preset(ShapeKind kind, double widthPt, double heightPt, string? fillColorHex = null) =>
        new(kind, widthPt, heightPt, fillColorHex);

    /// <summary>
    /// Creates a text box of the given size whose body is a single paragraph carrying <paramref name="text"/>.
    /// </summary>
    public static Shape TextBoxWith(string text, double widthPt, double heightPt, string? fillColorHex = null)
    {
        var shape = new Shape(ShapeKind.TextBox, widthPt, heightPt, fillColorHex);
        var paragraph = new Paragraph();
        paragraph.Runs.Add(new Run(text));
        shape.TextParagraphs.Add(paragraph);
        return shape;
    }
}
