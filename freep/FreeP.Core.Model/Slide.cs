using Free.Shared.Drawing;

namespace FreeP.Core.Model;

/// <summary>
/// One segment in a custom geometry path. Uses normalized path-space coordinates (not yet
/// scaled to shape bounds) so the path can be stored independently of the shape's size.
/// </summary>
public enum CustomSegmentKind { MoveTo, LineTo, CubicBezTo, QuadBezTo, ArcTo, Close }

public sealed record CustomSegment(
    CustomSegmentKind Kind,
    double X = 0, double Y = 0,
    double X1 = 0, double Y1 = 0,
    double X2 = 0, double Y2 = 0,
    double X3 = 0, double Y3 = 0,
    // ArcTo params (angles in degrees, radii in path-space units)
    double WR = 0, double HR = 0,
    double StAng = 0, double SwAng = 0);

/// <summary>
/// One path within a custom geometry's path list. Coordinates are in the path's own w×h space.
/// Multiple paths = multiple contours.
/// </summary>
public sealed class CustomGeometryPath
{
    /// <summary>Path-space width (from a:path w= attribute). 0 means use shape extent.</summary>
    public long PathW { get; set; }
    /// <summary>Path-space height (from a:path h= attribute). 0 means use shape extent.</summary>
    public long PathH { get; set; }
    /// <summary>Whether this path should be filled.</summary>
    public bool Fill { get; set; } = true;
    /// <summary>Whether this path should be stroked.</summary>
    public bool Stroke { get; set; } = true;
    /// <summary>Segments for this path.</summary>
    public List<CustomSegment> Segments { get; } = new();
}

/// <summary>
/// Bevel descriptor (a:bevelT / a:bevelB inside a:sp3d). All sizes in EMU.
/// </summary>
public sealed class BevelInfo
{
    /// <summary>Bevel width in EMU (w= attribute). Default 76200 = 0.6 pt.</summary>
    public long WidthEmu { get; set; } = 76200;

    /// <summary>Bevel height in EMU (h= attribute). Default 76200.</summary>
    public long HeightEmu { get; set; } = 76200;

    /// <summary>Preset name (prst= attribute), e.g. "circle", "relaxedInset", "cross", "angle", etc. Empty = circle.</summary>
    public string PresetName { get; set; } = string.Empty;
}

/// <summary>
/// 3-D scene data from a:scene3d. Stored for round-trip; rendering is approximated.
/// </summary>
public sealed class Scene3dInfo
{
    /// <summary>Camera preset name (a:camera prst=), e.g. "orthographicFront", "perspectiveRelaxed".</summary>
    public string CameraPreset { get; set; } = string.Empty;

    /// <summary>Light rig preset name (a:lightRig rig=), e.g. "threePt", "flat", "balanced".</summary>
    public string LightRig { get; set; } = string.Empty;

    /// <summary>Light rig direction (a:lightRig dir=), e.g. "t", "tl", "r".</summary>
    public string LightRigDir { get; set; } = string.Empty;
}

/// <summary>
/// Shape effects carried on a SlideShape. All distances/radii are in EMU.
/// </summary>
public sealed class ShapeEffects
{
    // ── Outer shadow ──────────────────────────────────────────────────────────
    public bool HasOuterShadow { get; set; }
    public SrgbColor OuterShadowColor { get; set; }
    public byte OuterShadowAlpha { get; set; } = 0x80;      // 0-255
    public long OuterShadowBlurRadEmu { get; set; }
    public long OuterShadowDistEmu { get; set; }
    public double OuterShadowDirDeg { get; set; }

    // ── Inner shadow ──────────────────────────────────────────────────────────
    public bool HasInnerShadow { get; set; }
    public SrgbColor InnerShadowColor { get; set; }
    public byte InnerShadowAlpha { get; set; } = 0x80;
    public long InnerShadowBlurRadEmu { get; set; }
    public long InnerShadowDistEmu { get; set; }
    public double InnerShadowDirDeg { get; set; }

    // ── Glow ──────────────────────────────────────────────────────────────────
    public bool HasGlow { get; set; }
    public SrgbColor GlowColor { get; set; }
    public byte GlowAlpha { get; set; } = 0xA0;
    public long GlowRadiusEmu { get; set; }

    // ── Soft edge ─────────────────────────────────────────────────────────────
    public bool HasSoftEdge { get; set; }
    public long SoftEdgeRadEmu { get; set; }

    // ── Bevel / 3-D (a:sp3d) ─────────────────────────────────────────────────

    /// <summary>
    /// True when any sp3d data is present. Back-compat alias; callers should prefer
    /// checking BevelTop/BevelBottom/ExtrusionHeightEmu directly.
    /// </summary>
    public bool HasBevel => BevelTop is not null || BevelBottom is not null;

    /// <summary>Top-face bevel (a:bevelT). Null = none.</summary>
    public BevelInfo? BevelTop { get; set; }

    /// <summary>Bottom-face bevel (a:bevelB). Null = none.</summary>
    public BevelInfo? BevelBottom { get; set; }

    /// <summary>3-D extrusion depth in EMU (a:sp3d extrusionH= attribute). 0 = none.</summary>
    public long ExtrusionHeightEmu { get; set; }

    /// <summary>Contour width in EMU (a:sp3d contourW= attribute). 0 = none.</summary>
    public long ContourWidthEmu { get; set; }

    /// <summary>Preset material name (a:sp3d prstMaterial= attribute).</summary>
    public string PrstMaterial { get; set; } = string.Empty;

    /// <summary>Extrusion colour (a:extrusionClr). Null = no override.</summary>
    public SrgbColor? ExtrusionColor { get; set; }

    /// <summary>Contour colour (a:contourClr). Null = no override.</summary>
    public SrgbColor? ContourColor { get; set; }

    // ── Scene 3-D (a:scene3d) ─────────────────────────────────────────────────

    /// <summary>Scene 3-D camera/light data. Null = not present.</summary>
    public Scene3dInfo? Scene3d { get; set; }
}

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

    // ── Custom geometry ───────────────────────────────────────────────────────

    /// <summary>
    /// Custom geometry paths. When non-empty and Kind==AutoShape, these override the preset.
    /// Each entry corresponds to one a:path in a:custGeom/a:pathLst.
    /// </summary>
    public List<CustomGeometryPath> CustomGeometry { get; } = new();

    // ── Shape effects ─────────────────────────────────────────────────────────

    /// <summary>Shadow, glow, soft-edge, and bevel effects. Null if no effects are set.</summary>
    public ShapeEffects? Effects { get; set; }

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

    // ── Chart ─────────────────────────────────────────────────────────────────────

    /// <summary>Chart data when Kind == Chart.</summary>
    public ChartShape? Chart { get; set; }

    // ── SmartArt ───────────────────────────────────────────────────────────────────

    /// <summary>SmartArt data when Kind == SmartArt.</summary>
    public SmartArtShape? SmartArt { get; set; }

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

    // ── Hyperlink ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Shape-level hyperlink.  Corresponds to <c>a:hlinkClick</c> inside <c>p:cNvPr</c>.
    /// When set, a click anywhere on the shape navigates to the hyperlink target.
    /// </summary>
    public Hyperlink? Hyperlink { get; set; }

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

    // ── Transitions + Animations ─────────────────────────────────────────────────

    /// <summary>
    /// Slide transition played when this slide enters during a slideshow.
    /// Null means no transition (or inherit from template). Maps to p:transition in slide XML.
    /// </summary>
    public SlideTransition? Transition { get; set; }

    /// <summary>
    /// Ordered list of shape animation build steps for this slide.
    /// Playback order matches the list order. Maps to the main sequence in p:timing.
    /// </summary>
    public List<ShapeAnimation> Animations { get; } = new();

    // ── Speaker notes ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The speaker-notes text body for this slide, or null if no notes have been set.
    /// Corresponds to the body placeholder (p:ph type="body") in the ppt/notesSlides/notesSlideN.xml part.
    /// </summary>
    public TextBody? Notes { get; set; }

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
