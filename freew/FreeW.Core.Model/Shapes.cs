namespace FreeW.Core.Model;

// MODEL-DESIGN CHOICE (roadmap item W2, basic DrawingML shapes & text boxes):
// A shape is modelled as an OPTIONAL INLINE RUN MARK (Run.Shape) exactly like Run.Equation / Run.Image.
// This is the established FreeW pattern for every inline feature, so shapes flow through the existing run
// sequence, hyperlink/comment/revision wrapping, table cells, headers and footers with zero new plumbing,
// and they round-trip through docx as an inline w:drawing emitted in place of the run's w:t. A Shape is a
// single DrawingML preset geometry (rect / roundRect / ellipse / a plain text-box rect) plus a size, an
// optional solid fill, and — for text boxes — text content held as a list of paragraphs (the same Paragraph
// model used everywhere else, so the txbx body round-trips through the ordinary paragraph reader/writer).
// We deliberately stop at preset geometries + simple text: no connectors, grouping or freeform geometry.

/// <summary>
/// The preset DrawingML geometry of a <see cref="Shape"/>. Maps onto <c>a:prstGeom/@prst</c>:
/// <see cref="Rectangle"/> → <c>rect</c>, <see cref="RoundedRectangle"/> → <c>roundRect</c>,
/// <see cref="Ellipse"/> → <c>ellipse</c>. <see cref="TextBox"/> is a rectangle whose purpose is to hold
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
