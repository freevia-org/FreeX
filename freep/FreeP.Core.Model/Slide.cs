using Free.Shared.Drawing;

namespace FreeP.Core.Model;

/// <summary>
/// Describes one end of a connector's attachment to a shape's connection site.
/// </summary>
public sealed class ConnectorAttachment
{
    /// <summary>
    /// Id of the shape this end is attached to (matches <see cref="SlideShape.Id"/>).
    /// Null when the end is free (dangling in space).
    /// </summary>
    public uint ShapeId { get; set; }

    /// <summary>
    /// Index of the connection-site on the target shape.
    /// OOXML <c>a:stCxn</c>/<c>a:endCxn</c> <c>idx=</c> attribute.
    ///
    /// Standard PowerPoint 4-site mapping for rectangles and most presets:
    ///   0 = left-mid, 1 = top-mid, 2 = right-mid, 3 = bottom-mid.
    /// Additional per-shape sites follow the shape-specific numbering.
    /// </summary>
    public int SiteIndex { get; set; }
}

/// <summary>
/// One segment in a custom geometry path. Uses normalized path-space coordinates (not yet
/// scaled to shape bounds) so the path can be stored independently of the shape's size.
/// </summary>
public enum CustomSegmentKind { MoveTo, LineTo, CubicBezTo, QuadBezTo, ArcTo, Close }

/// <summary>Which authored point within a custom-geometry segment an Edit Points handle moves.</summary>
public enum CustomGeometryPointSlot { Endpoint, Control1, Control2 }

/// <summary>Which authored ArcTo parameter an Edit Points handle changes.</summary>
public enum CustomGeometryArcPointSlot { StartAngle, EndAngle, RadiusX, RadiusY }

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
/// One authored connection site from <c>a:custGeom/a:cxnLst</c>.
///
/// DrawingML permits the position and angle to be geometry-guide expressions rather
/// than literal numbers, so the raw attribute tokens are retained for round-trip. The
/// connector resolver evaluates the common literal and edge-guide tokens it can resolve
/// against the shape's geometry coordinate space and keeps its existing fallback for
/// expressions that require an unmodeled guide list.
/// </summary>
public sealed class CustomGeometryConnectionSite
{
    public string X { get; set; } = "0";
    public string Y { get; set; } = "0";
    public string Angle { get; set; } = string.Empty;
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

/// <summary>How a media object begins playback during a slide show.</summary>
public enum MediaPlaybackStartMode
{
    /// <summary>Start from the slide's click sequence or a direct media click.</summary>
    InClickSequence,

    /// <summary>Start as soon as the slide begins.</summary>
    Automatically,

}

/// <summary>
/// Payload for an audio or video media object embedded in a slide.
/// The poster image bytes (shown while not playing) are stored in the parent
/// shape's <see cref="SlideShape.Picture"/> field. The media asset itself
/// (audio/video bytes) is stored here together with its content-type for
/// round-trip preservation.
/// </summary>
public sealed class MediaInfo
{
    /// <summary>True = video, false = audio.</summary>
    public bool IsVideo { get; set; }

    /// <summary>Authored playback volume as a percentage from 0 (muted) to 100.</summary>
    public int VolumePercent { get; set; } = 80;

    /// <summary>PowerPoint's authored start behavior; click sequence is the default.</summary>
    public MediaPlaybackStartMode PlaybackStartMode { get; set; } = MediaPlaybackStartMode.InClickSequence;

    /// <summary>Whether playback restarts when the media reaches its end.</summary>
    public bool Loop { get; set; }

    /// <summary>Whether playback returns to the trim start after reaching its end.</summary>
    public bool RewindAfterPlaying { get; set; }

    /// <summary>Whether an authored video expands to the slideshow viewport while playing.</summary>
    public bool PlayFullScreen { get; set; }

    /// <summary>Whether the media poster remains visible while playback is stopped or paused.</summary>
    public bool ShowWhenStopped { get; set; } = true;

    /// <summary>Milliseconds trimmed from the beginning of playback.</summary>
    public double TrimStartMilliseconds { get; set; }

    /// <summary>Milliseconds trimmed from the end of playback.</summary>
    public double TrimEndMilliseconds { get; set; }

    /// <summary>Milliseconds used to fade the media in at playback start.</summary>
    public double FadeInMilliseconds { get; set; }

    /// <summary>Milliseconds used to fade the media out at playback end.</summary>
    public double FadeOutMilliseconds { get; set; }

    /// <summary>Named playback bookmarks authored for this media object.</summary>
    public List<MediaBookmarkInfo> Bookmarks { get; } = new();

    /// <summary>Raw media bytes. Empty when the media is link-only (no embed).</summary>
    public byte[] Bytes { get; set; } = Array.Empty<byte>();

    /// <summary>MIME content type, e.g. "video/mp4", "audio/mpeg".</summary>
    public string ContentType { get; set; } = "video/mp4";

    /// <summary>
    /// Original embedded media package path, when loaded from a PPTX package.
    /// Empty for newly-authored or link-only media.
    /// </summary>
    public string SourcePackagePath { get; set; } = string.Empty;

    /// <summary>
    /// For link-only media: the external URI from r:link on the videoFile/audioFile element.
    /// Empty when the media is embedded.
    /// </summary>
    public string LinkUrl { get; set; } = string.Empty;

    /// <summary>Closed-caption or subtitle tracks associated with this media object.</summary>
    public List<MediaCaptionTrackInfo> CaptionTracks { get; } = new();
}

/// <summary>A named PowerPoint media bookmark measured from the media start in milliseconds.</summary>
public sealed class MediaBookmarkInfo
{
    public string Name { get; set; } = string.Empty;

    public double TimeMilliseconds { get; set; }
}

/// <summary>Metadata for a PowerPoint media caption/subtitle track.</summary>
public sealed class MediaCaptionTrackInfo
{
    /// <summary>Relationship id from the slide part, when the track is relationship-backed.</summary>
    public string RelationshipId { get; set; } = string.Empty;

    /// <summary>Original target path or URI for the caption/subtitle resource.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Caption/subtitle resource bytes for authored or read internal tracks.</summary>
    public byte[] Bytes { get; set; } = Array.Empty<byte>();

    /// <summary>MIME content type for the caption resource, when known.</summary>
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Language tag such as "en-US", when present in the package metadata.</summary>
    public string Language { get; set; } = string.Empty;

    /// <summary>Human-readable track label or name, when present in the package metadata.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>True when the track points to an external URI rather than an embedded package part.</summary>
    public bool IsExternal { get; set; }
}

/// <summary>
/// Visibility flags for footer/date/slide-number placeholders.
/// Corresponds to <c>p:hf</c> in slide/layout/master XML.
/// </summary>
public sealed class HfFlags
{
    /// <summary>Show footer placeholder.</summary>
    public bool ShowFooter { get; set; } = true;
    /// <summary>Show date/time placeholder.</summary>
    public bool ShowDate { get; set; } = true;
    /// <summary>Show slide number placeholder.</summary>
    public bool ShowSlideNum { get; set; } = true;
    /// <summary>Show header placeholder.</summary>
    public bool ShowHeader { get; set; } = false;
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
/// Crop and color-effect metadata for a picture shape (<see cref="SlideShapeKind.Picture"/>).
/// Stored on <see cref="SlideShape.PictureFormat"/>; null means no crop and no effects.
/// All crop fractions are in the range 0..1 and represent the fraction of the source image
/// to remove from each edge (matching the PresentationML a:srcRect l/t/r/b * 0.001% convention).
/// </summary>
public sealed class PictureFormat
{
    // ── Crop (a:srcRect) ──────────────────────────────────────────────────────────

    /// <summary>Fraction of image width to crop from the left edge (0..1). Default 0.</summary>
    public double CropLeft   { get; set; }
    /// <summary>Fraction of image height to crop from the top edge (0..1). Default 0.</summary>
    public double CropTop    { get; set; }
    /// <summary>Fraction of image width to crop from the right edge (0..1). Default 0.</summary>
    public double CropRight  { get; set; }
    /// <summary>Fraction of image height to crop from the bottom edge (0..1). Default 0.</summary>
    public double CropBottom { get; set; }

    /// <summary>True when any crop fraction is non-zero.</summary>
    public bool HasCrop =>
        CropLeft != 0 || CropTop != 0 || CropRight != 0 || CropBottom != 0;

    // ── Color effects (a:blip child elements) ─────────────────────────────────────

    /// <summary>a:grayscl — convert image to grayscale.</summary>
    public bool Grayscale { get; set; }

    /// <summary>
    /// a:biLevel thresh= — threshold to black/white.
    /// Expressed as a fraction 0..1 (OOXML stores it in 1/1000 of a %, i.e. 50000 = 50%).
    /// Null means the effect is not present.
    /// </summary>
    public double? BiLevelThreshold { get; set; }

    /// <summary>
    /// a:lum bright= — brightness adjustment in the range -1..1.
    /// (OOXML stores -100000..100000, we normalise to -1..1.)
    /// Null means the effect is not present.
    /// </summary>
    public double? Brightness { get; set; }

    /// <summary>
    /// a:lum contrast= — contrast adjustment in the range -1..1.
    /// Null means the effect is not present (same element as Brightness; both present together).
    /// </summary>
    public double? Contrast { get; set; }

    /// <summary>
    /// a:alphaModFix amt= — opacity multiplier in the range 0..1.
    /// (OOXML stores 0..100000; 100000 = fully opaque = 1.0.)
    /// Null means the effect is not present (treated as fully opaque).
    /// </summary>
    public double? AlphaModPct { get; set; }

    /// <summary>True when any colour effect is active.</summary>
    public bool HasColorEffect =>
        Grayscale ||
        BiLevelThreshold.HasValue ||
        Brightness.HasValue ||
        Contrast.HasValue ||
        AlphaModPct.HasValue;
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

    /// <summary>Persistent alternative text title (from cNvPr title="...").</summary>
    public string AlternativeTextTitle { get; set; } = string.Empty;

    /// <summary>Persistent alternative text description (from cNvPr descr="...").</summary>
    public string AlternativeText { get; set; } = string.Empty;

    /// <summary>True when the object is marked decorative and should not require alt text.</summary>
    public bool IsDecorative { get; set; }

    /// <summary>True when the object is hidden in the slide editing view (p:cNvPr/@hidden).</summary>
    public bool IsHidden { get; set; }

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

    /// <summary>DrawingML preset geometry guides, retained in their raw OOXML units.</summary>
    public Dictionary<string, double> PresetGeometryAdjustments { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    // ── Anchor (absolute EMU positions) ─────────────────────────────────────────

    /// <summary>Horizontal offset from the slide left edge, in EMU.</summary>
    public long OffsetXEmu { get; set; }

    /// <summary>Vertical offset from the slide top edge, in EMU.</summary>
    public long OffsetYEmu { get; set; }

    /// <summary>Shape width in EMU.</summary>
    public long ExtentCxEmu { get; set; }

    /// <summary>Shape height in EMU.</summary>
    public long ExtentCyEmu { get; set; }

    /// <summary>
    /// True when the source shape explicitly carried an <c>a:xfrm</c> with zero width and
    /// height. On a slide placeholder, PowerPoint treats that as hidden rather than inheriting
    /// the layout placeholder's visible geometry.
    /// </summary>
    public bool HasExplicitZeroExtentTransform { get; set; }

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

    /// <summary>
    /// Authored custom connection sites from <c>a:custGeom/a:cxnLst</c>.
    /// Site order is the OOXML connection-site index order.
    /// </summary>
    public List<CustomGeometryConnectionSite> CustomConnectionSites { get; } = new();

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

    /// <summary>
    /// Crop rectangle and colour-effect overrides for the picture. Null means no crop and no
    /// colour effects (render the full image at natural colours).
    /// Only populated when Kind == Picture (or Media, for the poster image).
    /// </summary>
    public PictureFormat? PictureFormat { get; set; }

    /// <summary>
    /// Wave 26: picture frame clip geometry preset name from the picture's <c>p:spPr/a:prstGeom prst=</c>.
    /// Common values:
    ///   <c>rect</c>      = plain rectangle (default, no rounding).
    ///   <c>roundRect</c> = rounded-rectangle clip (most common picture style frame).
    ///   <c>ellipse</c>   = elliptical clip (oval frame).
    /// Null or "rect" = no special clipping (draw a rectangle).
    /// Stored as the raw OOXML prst string so unknown shapes pass through unchanged.
    /// Only populated for Kind == Picture.
    /// </summary>
    public string? PictureFrameGeometry { get; set; }

    // ── Media (audio/video) ───────────────────────────────────────────────────────────

    /// <summary>
    /// Media payload when Kind == Media. The poster image (shown when not playing)
    /// is stored in <see cref="Picture"/>. Audio/video bytes live here.
    /// </summary>
    public MediaInfo? Media { get; set; }

    // ── Table ─────────────────────────────────────────────────────────────────────

    /// <summary>Table data when Kind == Table.</summary>
    public TableShape? Table { get; set; }

    // ── Chart ─────────────────────────────────────────────────────────────────────

    /// <summary>Chart data when Kind == Chart.</summary>
    public ChartShape? Chart { get; set; }

    // ── SmartArt ───────────────────────────────────────────────────────────────────

    /// <summary>SmartArt data when Kind == SmartArt.</summary>
    public SmartArtShape? SmartArt { get; set; }

    // ── OLE embedded object ───────────────────────────────────────────────────────

    /// <summary>
    /// OLE embedded object data when Kind == Ole.
    /// The fallback preview image is stored in <see cref="Picture"/> for rendering.
    /// </summary>
    public OleObjectInfo? OleObject { get; set; }

    // ── Preserved modern objects (zoom / ink / 3D / unknown) ────────────────────────

    /// <summary>
    /// Preserved modern object payload when Kind is Zoom, Ink, Model3d, or PreservedObject.
    /// The fallback/preview image is in <see cref="Picture"/> for rendering.
    /// </summary>
    public PreservedObjectInfo? PreservedObject { get; set; }

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

    // ── Connector attachment (Kind == Connector only) ─────────────────────────────

    /// <summary>
    /// Start-point attachment for a connector shape. Corresponds to <c>a:stCxn</c> inside
    /// <c>p:cNvCxnSpPr</c>. Null = free (not attached to any shape).
    /// Only meaningful when <see cref="Kind"/> == <see cref="SlideShapeKind.Connector"/>.
    /// </summary>
    public ConnectorAttachment? ConnectionStart { get; set; }

    /// <summary>
    /// End-point attachment for a connector shape. Corresponds to <c>a:endCxn</c> inside
    /// <c>p:cNvCxnSpPr</c>. Null = free.
    /// Only meaningful when <see cref="Kind"/> == <see cref="SlideShapeKind.Connector"/>.
    /// </summary>
    public ConnectorAttachment? ConnectionEnd { get; set; }

    /// <summary>
    /// Wave 26: computed Manhattan (orthogonal) route for an elbow/bent connector.
    /// A list of waypoints (in slide EMU) that the connector polyline passes through,
    /// starting at the start-site and ending at the end-site.
    /// Null = not computed (use the bbox-only fallback path drawn by the compositor).
    /// Only meaningful for <see cref="DrawingShapeKind.ElbowConnector"/> connectors when
    /// both endpoints are attached to shapes.
    /// </summary>
    public List<(long X, long Y)>? ElbowRoute { get; set; }

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
    /// Whether the slide is hidden from slideshow presentation. The default is visible,
    /// matching PresentationML omission semantics for <c>p:sld/@show</c>.
    /// </summary>
    public bool IsHidden { get; set; }

    /// <summary>
    /// Stable identifier for the slide (integer from the slide list; stored as string for
    /// round-trip stability with the legacy .fxp format).
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Numeric id from the presentation's p:sldId element. Null for newly authored slides
    /// until the package writer assigns one.
    /// </summary>
    public uint? NumericId { get; set; }

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

    /// <summary>
    /// Preserved PowerPoint paragraph-build list from <c>p:timing/p:bldLst</c>.
    /// The shared playback model does not yet expose fragment-level text builds, so this
    /// payload is retained verbatim for edit/save round-trips instead of being discarded.
    /// </summary>
    public string? AnimationBuildListXml { get; set; }

    // ── Speaker notes ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The speaker-notes text body for this slide, or null if no notes have been set.
    /// Corresponds to the body placeholder (p:ph type="body") in the ppt/notesSlides/notesSlideN.xml part.
    /// </summary>
    public TextBody? Notes { get; set; }

    // ── Header/footer visibility ──────────────────────────────────────────────────────

    /// <summary>
    /// Slide-level header/footer visibility flags (from <c>p:hf</c>).
    /// Null = not present on this slide (inherit from layout/master).
    /// </summary>
    public HfFlags? HfVisibility { get; set; }

    // ── Color map override (p:clrMapOvr) ─────────────────────────────────────────

    /// <summary>
    /// Per-slide color-role override from <c>p:clrMapOvr/a:overrideClrMapping</c>.
    /// When non-null, this map takes precedence over the master's <c>p:clrMap</c> when
    /// resolving scheme-color role names (tx1, bg1, …) to theme slots for shapes on this slide.
    /// Null means "use master mapping" (<c>&lt;a:masterClrMapping/&gt;</c>).
    /// </summary>
    public Dictionary<string, string>? ColorMapOverride { get; set; }

    // ── Comments ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Comments attached to this slide (legacy p:cm schema, ppt/comments/commentN.xml).
    /// Empty when the slide has no comments. Author identity is de-duplicated on write
    /// into a shared commentAuthors.xml part.
    /// </summary>
    public List<SlideComment> Comments { get; } = new();

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
