using Free.Shared.Drawing;

namespace FreeP.Core.Model;

/// <summary>
/// An image part referenced by a <see cref="SlideShape"/> with <see cref="SlideShapeKind.Picture"/>.
/// Stores the raw bytes and MIME content type so the IO layer can embed it into a .pptx package.
/// </summary>
public sealed class ImagePart
{
    /// <summary>Raw image bytes (JPEG, PNG, GIF, SVG, WMF, EMF, …).</summary>
    public byte[] Bytes { get; set; } = Array.Empty<byte>();

    /// <summary>MIME content type (e.g. "image/png", "image/jpeg").</summary>
    public string ContentType { get; set; } = "image/png";
}

/// <summary>
/// A shape on a slide. Covers autoshapes, textboxes, pictures, connectors, and group shapes.
/// The <see cref="Kind"/> discriminator determines which optional properties are populated.
/// </summary>
public sealed class SlideShape
{
    // ── Identity ─────────────────────────────────────────────────────────────────

    /// <summary>Stable shape identifier within the presentation (from p:sp/nvSpPr/cNvPr id="...").</summary>
    public uint Id { get; set; }

    /// <summary>Display name for the shape (from p:sp/nvSpPr/cNvPr name="...").</summary>
    public string Name { get; set; } = string.Empty;

    // ── Kind discriminator ───────────────────────────────────────────────────────

    /// <summary>
    /// High-level shape kind. When <see cref="SlideShapeKind.AutoShape"/>, <see cref="AutoShapeKind"/>
    /// specifies the exact geometry preset.
    /// </summary>
    public SlideShapeKind Kind { get; set; } = SlideShapeKind.AutoShape;

    /// <summary>
    /// The preset geometry, used when Kind == AutoShape or Connector.
    /// </summary>
    public DrawingShapeKind AutoShapeKind { get; set; } = DrawingShapeKind.Rectangle;

    // ── Anchor (absolute EMU positions) ─────────────────────────────────────────

    /// <summary>Horizontal offset from the slide left edge, in EMU.</summary>
    public long OffsetXEmu { get; set; }

    /// <summary>Vertical offset from the slide top edge, in EMU.</summary>
    public long OffsetYEmu { get; set; }

    /// <summary>Shape width in EMU.</summary>
    public long ExtentCxEmu { get; set; }

    /// <summary>Shape height in EMU.</summary>
    public long ExtentCyEmu { get; set; }

    /// <summary>Rotation in degrees, clockwise (from spPr/xfrm rot="..."; OOXML stores 1/60000 degree).</summary>
    public double RotationDeg { get; set; }

    /// <summary>Horizontal flip.</summary>
    public bool FlipH { get; set; }

    /// <summary>Vertical flip.</summary>
    public bool FlipV { get; set; }

    // ── Styling ──────────────────────────────────────────────────────────────────

    /// <summary>Shape fill. Null means inherit from layout/master/theme defaults.</summary>
    public ShapeFill? Fill { get; set; }

    /// <summary>Shape outline (border/stroke). Null means inherit.</summary>
    public ShapeOutline? Outline { get; set; }

    // ── Text ─────────────────────────────────────────────────────────────────────

    /// <summary>Text body, or null if the shape has no text.</summary>
    public TextBody? TextBody { get; set; }

    // ── Placeholder (for layout/master inheritance) ───────────────────────────────

    /// <summary>If non-null, this shape is a placeholder and inherits geometry/style from the matching layout/master placeholder.</summary>
    public Placeholder? Placeholder { get; set; }

    // ── Picture ───────────────────────────────────────────────────────────────────

    /// <summary>Image data when Kind == Picture.</summary>
    public ImagePart? Picture { get; set; }

    // ── Table ─────────────────────────────────────────────────────────────────────

    /// <summary>Table data when Kind == Table.</summary>
    public TableShape? Table { get; set; }

    // ── Group children ────────────────────────────────────────────────────────────

    /// <summary>Child shapes when Kind == Group.</summary>
    public List<SlideShape> Children { get; } = new();

    // ── Legacy FXP round-trip support ────────────────────────────────────────────

    /// <summary>
    /// Stores the original Kind string from .fxp JSON so byte-stable round-trips work without
    /// the IO layer re-deriving it from the enum. Set by FxpFormat on load; null for new shapes.
    /// Not serialized by the model layer — FxpFormat uses it directly.
    /// </summary>
    public string? LegacyFxpKind { get; set; }

    // ── Convenience helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the concatenated plain text of all runs (newline-separated paragraphs).
    /// Used by the PDF exporter and title-placeholder lookup.
    /// </summary>
    public string PlainText =>
        TextBody is null
            ? string.Empty
            : string.Join("\n", TextBody.Paragraphs.Select(p => string.Concat(p.Runs.Select(r => r.Text))));

    /// <summary>
    /// Text content accessor. Getting returns PlainText; setting replaces TextBody with a single paragraph+run.
    /// Preserved for FxpFormat and legacy consumers.
    /// </summary>
    public string Text
    {
        get => PlainText;
        set
        {
            if (TextBody is null)
                TextBody = new TextBody();
            TextBody.Paragraphs.Clear();
            if (!string.IsNullOrEmpty(value))
            {
                var para = new Paragraph();
                para.Runs.Add(new Run { Text = value });
                TextBody.Paragraphs.Add(para);
            }
        }
    }
}

/// <summary>
/// A slide in the presentation.
/// </summary>
public sealed class Slide
{
    /// <summary>
    /// Stable identifier for the slide (integer from the slide list; stored as string for
    /// round-trip stability with the legacy .fxp format).
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Reference to the SlideLayout this slide uses (by layout name or index).
    /// Null if the layout is unknown or not yet resolved.
    /// </summary>
    public string? LayoutId { get; set; }

    /// <summary>Shapes on the slide, in z-order (back to front).</summary>
    public List<SlideShape> Shapes { get; } = new();

    /// <summary>
    /// Optional slide-level background fill override. Null = inherit from layout/master.
    /// </summary>
    public ShapeFill? Background { get; set; }

    // ── Legacy title accessor ─────────────────────────────────────────────────────

    /// <summary>
    /// The title of the slide, derived from the title placeholder shape's plain text.
    /// Setting this updates (or creates) the title placeholder shape.
    /// </summary>
    public string Title
    {
        get
        {
            var titleShape = Shapes.FirstOrDefault(s =>
                s.Placeholder?.Type is PlaceholderType.Title or PlaceholderType.CenteredTitle);
            return titleShape?.PlainText ?? string.Empty;
        }
        set
        {
            var titleShape = Shapes.FirstOrDefault(s =>
                s.Placeholder?.Type is PlaceholderType.Title or PlaceholderType.CenteredTitle);
            if (titleShape is null)
            {
                titleShape = new SlideShape
                {
                    Id = (uint)(Shapes.Count + 1),
                    Name = "Title 1",
                    Kind = SlideShapeKind.AutoShape,
                    AutoShapeKind = DrawingShapeKind.Rectangle,
                    Placeholder = new Placeholder { Type = PlaceholderType.Title, Idx = 0 }
                };
                Shapes.Insert(0, titleShape);
            }
            titleShape.Text = value;
        }
    }
}
