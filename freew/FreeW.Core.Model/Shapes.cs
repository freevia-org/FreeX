using System.Collections.Generic;

using Free.Shared.Drawing;

namespace FreeW.Core.Model;

// ── Custom Geometry (Edit Points / a:custGeom) ────────────────────────────────────────────────────

/// <summary>
/// A single edit-point on a freeform custom geometry path, in the shape's local coordinate space
/// (0–21600 units, matching DrawingML's default 21600×21600 grid). Serialised as an <c>a:pt</c>
/// element inside <c>a:custGeom/a:pathLst/a:path/a:lnTo</c> (or <c>a:moveTo</c> for the first point).
/// </summary>
public sealed record CustomPoint(long X, long Y);

/// <summary>
/// A freeform custom geometry path segment. The first point starts a move-to; subsequent points are
/// line-to segments, and cubic Bezier segments carry two control points plus their endpoint.
/// </summary>
public enum CustomSegmentKind
{
    /// <summary>Move to the point without drawing (used for the first point of each sub-path).</summary>
    MoveTo,
    /// <summary>Draw a straight line from the current position to the point.</summary>
    LineTo,
    /// <summary>Draw a cubic Bezier curve using two control points and an endpoint.</summary>
    CubicBezierTo,
    /// <summary>Close the sub-path back to the most recent move-to.</summary>
    Close,
}

/// <summary>
/// A single path segment in a <see cref="CustomGeometry"/> path list. Cubic Bezier segments use
/// <see cref="ControlPoint1"/> and <see cref="ControlPoint2"/> before their <see cref="Point"/> endpoint.
/// <see cref="Close"/> segments carry no points.
/// </summary>
public sealed record CustomSegment(
    CustomSegmentKind Kind,
    CustomPoint? Point = null,
    CustomPoint? ControlPoint1 = null,
    CustomPoint? ControlPoint2 = null);

/// <summary>
/// Freeform custom geometry for a <see cref="Shape"/>, replacing its preset geometry (<c>a:prstGeom</c>)
/// with an explicit polygon defined by user-draggable edit points (<c>a:custGeom</c>). The geometry is
/// expressed in a 21600×21600 local grid and serialised as a single <c>a:path</c> sub-path.
///
/// DOCX round-trip: writer emits <c>wps:spPr/a:custGeom/a:pathLst/a:path</c> with moveTo, lnTo,
/// cubicBezTo, and close segments; reader recovers them from the same structure and populates
/// <see cref="Segments"/>.
///
/// When this property is set on a <see cref="Shape"/>, the writer emits <c>a:custGeom</c> instead of
/// <c>a:prstGeom</c>; the renderer draws the polygon using WPF StreamGeometry.
/// </summary>
public sealed class CustomGeometry
{
    /// <summary>Local-coordinate bounding-box width (typically 21600). Used for a:path @w.</summary>
    public long Width { get; set; } = 21600;

    /// <summary>Local-coordinate bounding-box height (typically 21600). Used for a:path @h.</summary>
    public long Height { get; set; } = 21600;

    /// <summary>
    /// Path segments forming the freeform geometry. The first segment should be a <see cref="CustomSegmentKind.MoveTo"/>
    /// followed by line or cubic Bezier segments and an optional <see cref="CustomSegmentKind.Close"/>.
    /// </summary>
    public List<CustomSegment> Segments { get; } = [];

    /// <summary>All editable endpoints (MoveTo, LineTo, and cubic Bezier endpoints; Close has no point).</summary>
    public IEnumerable<CustomPoint> EditPoints =>
        Segments.Where(s => s.Point is not null).Select(s => s.Point!);

    /// <summary>
    /// Convenience factory: builds a closed rectangular polygon from the four corners of a
    /// given width×height bounding box in the 21600×21600 grid. Used by "Convert to Freeform".
    /// </summary>
    public static CustomGeometry RectanglePoly(long gridW = 21600, long gridH = 21600) => new()
    {
        Width = gridW,
        Height = gridH,
        Segments =
        {
            new CustomSegment(CustomSegmentKind.MoveTo, new CustomPoint(0,    0)),
            new CustomSegment(CustomSegmentKind.LineTo, new CustomPoint(gridW, 0)),
            new CustomSegment(CustomSegmentKind.LineTo, new CustomPoint(gridW, gridH)),
            new CustomSegment(CustomSegmentKind.LineTo, new CustomPoint(0,    gridH)),
            new CustomSegment(CustomSegmentKind.Close),
        }
    };

    /// <summary>
    /// Convenience factory: builds a closed ellipse approximated by 8 straight-line segments in the
    /// 21600×21600 grid. Used by "Convert to Freeform" for ellipse shapes.
    /// </summary>
    public static CustomGeometry EllipsePoly(long gridW = 21600, long gridH = 21600)
    {
        var geo = new CustomGeometry { Width = gridW, Height = gridH };
        long cx = gridW / 2, cy = gridH / 2;
        int segments = 8;
        for (int i = 0; i < segments; i++)
        {
            double angle = 2 * System.Math.PI * i / segments;
            long x = cx + (long)(cx * System.Math.Cos(angle));
            long y = cy + (long)(cy * System.Math.Sin(angle));
            var kind = i == 0 ? CustomSegmentKind.MoveTo : CustomSegmentKind.LineTo;
            geo.Segments.Add(new CustomSegment(kind, new CustomPoint(x, y)));
        }
        geo.Segments.Add(new CustomSegment(CustomSegmentKind.Close));
        return geo;
    }

    /// <summary>
    /// Convenience factory: builds a rounded-rectangle approximation (rectangle with slightly cut
    /// corners) in the 21600×21600 grid. Used by "Convert to Freeform" for rounded-rectangle shapes.
    /// </summary>
    public static CustomGeometry RoundedRectPoly(long gridW = 21600, long gridH = 21600)
    {
        long r = System.Math.Min(gridW, gridH) / 6; // corner inset
        var geo = new CustomGeometry { Width = gridW, Height = gridH };
        geo.Segments.AddRange([
            new CustomSegment(CustomSegmentKind.MoveTo, new CustomPoint(r,         0)),
            new CustomSegment(CustomSegmentKind.LineTo, new CustomPoint(gridW - r, 0)),
            new CustomSegment(CustomSegmentKind.LineTo, new CustomPoint(gridW,     r)),
            new CustomSegment(CustomSegmentKind.LineTo, new CustomPoint(gridW,     gridH - r)),
            new CustomSegment(CustomSegmentKind.LineTo, new CustomPoint(gridW - r, gridH)),
            new CustomSegment(CustomSegmentKind.LineTo, new CustomPoint(r,         gridH)),
            new CustomSegment(CustomSegmentKind.LineTo, new CustomPoint(0,         gridH - r)),
            new CustomSegment(CustomSegmentKind.LineTo, new CustomPoint(0,         r)),
            new CustomSegment(CustomSegmentKind.Close),
        ]);
        return geo;
    }
}

// ── Shape Fill union ─────────────────────────────────────────────────────────────────────────────

/// <summary>Discriminated kind of a <see cref="ShapeFill"/>.</summary>
public enum ShapeFillKind { Solid, Gradient, Pattern, NoFill }

/// <summary>A single gradient colour stop (0–100 000 position, RRGGBB hex colour).</summary>
public sealed record GradientStop(int Position, string ColorHex);

/// <summary>
/// Fill descriptor for a <see cref="Shape"/>, replacing the previous <c>FillColorHex</c> string for
/// gradient and pattern fills. Solid fills keep <c>FillColorHex</c> on the shape for backwards compat;
/// this class is used when the fill is anything more complex than a solid colour.
/// </summary>
public sealed class ShapeFill
{
    public ShapeFillKind Kind { get; set; } = ShapeFillKind.Solid;

    // Solid: reuses Shape.FillColorHex (not duplicated here)

    // Gradient
    /// <summary>Gradient stops (position 0–100 000, hex colour). Non-empty only for <see cref="ShapeFillKind.Gradient"/>.</summary>
    public List<GradientStop> GradientStops { get; } = [];
    /// <summary>Linear gradient angle in 60 000ths of a degree (5 400 000 = 90°). 0 = left→right.</summary>
    public int GradientAngle { get; set; } = 0;

    // Pattern
    /// <summary>DrawingML preset pattern token (e.g. "pct5", "diagCross", "horzBrick"). Non-null only for <see cref="ShapeFillKind.Pattern"/>.</summary>
    public string? PatternPreset { get; set; }
    /// <summary>Pattern foreground (fgClr) RRGGBB hex. Null = theme default.</summary>
    public string? PatternFgColorHex { get; set; }
    /// <summary>Pattern background (bgClr) RRGGBB hex. Null = theme default.</summary>
    public string? PatternBgColorHex { get; set; }

    public static ShapeFill NoFill() => new ShapeFill { Kind = ShapeFillKind.NoFill };

    public static ShapeFill LinearGradient(int angleDegree60k, params GradientStop[] stops)
    {
        var fill = new ShapeFill { Kind = ShapeFillKind.Gradient, GradientAngle = angleDegree60k };
        foreach (var s in stops) fill.GradientStops.Add(s);
        return fill;
    }

    public static ShapeFill Patterned(string preset, string? fg = null, string? bg = null) =>
        new ShapeFill
        {
            Kind = ShapeFillKind.Pattern,
            PatternPreset = preset,
            PatternFgColorHex = fg,
            PatternBgColorHex = bg,
        };
}

// ── Shape Effects ─────────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Optional effects bundle applied to a <see cref="Shape"/> via <c>a:effectLst</c> in
/// <c>wps:spPr</c>. Only fields that are non-default are emitted; all are optional.
/// </summary>
public sealed class ShapeEffectLst
{
    // Shadow (a:outerShdw)
    public bool HasShadow { get; set; }
    public int ShadowBlurRad { get; set; } = 50800;       // EMU (4 pt)
    public int ShadowDist   { get; set; } = 38100;        // EMU (3 pt)
    public int ShadowDir    { get; set; } = 2700000;      // 60k-degree (45°)
    public string ShadowColorHex { get; set; } = "000000";
    public int ShadowAlpha { get; set; } = 40000;         // out of 100 000

    // Glow (a:glow)
    public bool HasGlow { get; set; }
    public int GlowRad { get; set; } = 50800;
    public string GlowColorHex { get; set; } = "4472C4";
    public int GlowAlpha { get; set; } = 60000;

    // Soft Edges (a:softEdge)
    public bool HasSoftEdge { get; set; }
    public int SoftEdgeRad { get; set; } = 50800;

    // Reflection (a:reflection)
    public bool HasReflection { get; set; }
    public int ReflectionBlurRad { get; set; } = 6350;
    public int ReflectionStartAlpha { get; set; } = 50000;
    public int ReflectionStartPosition { get; set; }
    public int ReflectionEndAlpha { get; set; }
    public int ReflectionEndPosition { get; set; } = 100000;
    public int ReflectionDir    { get; set; } = 5400000; // 90° = flip below
    public int ReflectionFadeDir { get; set; } = 5400000;
    public int ReflectionScaleX { get; set; } = 100000;
    public int ReflectionScaleY { get; set; } = -100000;
    public int ReflectionSkewX { get; set; }
    public int ReflectionSkewY { get; set; }
    public string ReflectionAlignment { get; set; } = "bl";
    public bool ReflectionRotWithShape { get; set; }
    public int ReflectionDist   { get; set; } = 23000;

    /// <summary>Compatibility alias for the reflection start alpha.</summary>
    public int ReflectionAlpha
    {
        get => ReflectionStartAlpha;
        set => ReflectionStartAlpha = value;
    }

    // Bevel / 3-D (a:sp3d — best-effort; carried through round-trip, rendered as border highlight)
    public bool HasBevel { get; set; }
    public int BevelW { get; set; } = 63500;              // EMU (5 pt)
    public int BevelH { get; set; } = 63500;
    public string BevelPresetType { get; set; } = "circle"; // circle / relaxedInset / angle / cross / divot

    public bool HasAny => HasShadow || HasGlow || HasSoftEdge || HasReflection || HasBevel;

    public ShapeEffectLst Clone() => new()
    {
        HasShadow = HasShadow,
        ShadowBlurRad = ShadowBlurRad,
        ShadowDist = ShadowDist,
        ShadowDir = ShadowDir,
        ShadowColorHex = ShadowColorHex,
        ShadowAlpha = ShadowAlpha,
        HasGlow = HasGlow,
        GlowRad = GlowRad,
        GlowColorHex = GlowColorHex,
        GlowAlpha = GlowAlpha,
        HasSoftEdge = HasSoftEdge,
        SoftEdgeRad = SoftEdgeRad,
        HasReflection = HasReflection,
        ReflectionBlurRad = ReflectionBlurRad,
        ReflectionStartAlpha = ReflectionStartAlpha,
        ReflectionStartPosition = ReflectionStartPosition,
        ReflectionEndAlpha = ReflectionEndAlpha,
        ReflectionEndPosition = ReflectionEndPosition,
        ReflectionDir = ReflectionDir,
        ReflectionFadeDir = ReflectionFadeDir,
        ReflectionScaleX = ReflectionScaleX,
        ReflectionScaleY = ReflectionScaleY,
        ReflectionSkewX = ReflectionSkewX,
        ReflectionSkewY = ReflectionSkewY,
        ReflectionAlignment = ReflectionAlignment,
        ReflectionRotWithShape = ReflectionRotWithShape,
        ReflectionDist = ReflectionDist,
        HasBevel = HasBevel,
        BevelW = BevelW,
        BevelH = BevelH,
        BevelPresetType = BevelPresetType
    };
}

// ── Shape Style Preset ────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A preset bundle of fill + outline + effect that together define a named shape style (like Word's
/// Shape Styles gallery). Applying a preset sets all three fields on the target <see cref="Shape"/>.
/// </summary>
public sealed class ShapeStylePreset
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string? FillColorHex { get; init; }
    public ShapeFill? Fill { get; init; }                    // non-null overrides FillColorHex
    public string? OutlineColorHex { get; init; }
    public double OutlineWidthPt { get; init; }
    public string? OutlineDash { get; init; }
    public ShapeEffectLst? Effect { get; init; }

    // ── Catalog of 40 presets (theme-colour accent bands × effect tiers) ─────────────────────────
    public static readonly IReadOnlyList<ShapeStylePreset> Catalog = BuildCatalog();

    private static ShapeStylePreset[] BuildCatalog()
    {
        // Six accent colours × 6 tiers (no-fill/light/subtle/moderate/intense/dark) + 4 special = 40.
        var accents = new[]
        {
            ("Accent1", "4472C4"), ("Accent2", "ED7D31"), ("Accent3", "A9D18E"),
            ("Accent4", "FFC000"), ("Accent5", "5B9BD5"), ("Accent6", "70AD47"),
        };
        var list = new List<ShapeStylePreset>();
        int n = 1;
        foreach (var (label, hex) in accents)
        {
            // Tier 1: transparent fill, coloured outline
            list.Add(new ShapeStylePreset
            {
                Id = $"shape-style-{n++}", Name = $"Outlined – {label}",
                Fill = ShapeFill.NoFill(), OutlineColorHex = "#" + hex, OutlineWidthPt = 1.0
            });
            // Tier 2: light tint fill, thin outline
            var tint = LightenHex(hex, 0.7f);
            list.Add(new ShapeStylePreset
            {
                Id = $"shape-style-{n++}", Name = $"Light – {label}",
                FillColorHex = "#" + tint, OutlineColorHex = "#" + hex, OutlineWidthPt = 0.75
            });
            // Tier 3: moderate fill, no outline
            var mid = LightenHex(hex, 0.4f);
            list.Add(new ShapeStylePreset
            {
                Id = $"shape-style-{n++}", Name = $"Moderate – {label}",
                FillColorHex = "#" + mid, OutlineColorHex = null, OutlineWidthPt = 0
            });
            // Tier 4: intense (full accent fill, white outline)
            list.Add(new ShapeStylePreset
            {
                Id = $"shape-style-{n++}", Name = $"Intense – {label}",
                FillColorHex = "#" + hex, OutlineColorHex = "#FFFFFF", OutlineWidthPt = 0.75
            });
            // Tier 5: gradient fill
            var dark = DarkenHex(hex, 0.3f);
            list.Add(new ShapeStylePreset
            {
                Id = $"shape-style-{n++}", Name = $"Gradient – {label}",
                Fill = ShapeFill.LinearGradient(5400000,
                    new GradientStop(0, "#" + hex),
                    new GradientStop(100000, "#" + dark)),
                OutlineColorHex = null
            });
            // Tier 6: accent fill + shadow
            list.Add(new ShapeStylePreset
            {
                Id = $"shape-style-{n++}", Name = $"Shadow – {label}",
                FillColorHex = "#" + hex, OutlineColorHex = null,
                Effect = new ShapeEffectLst { HasShadow = true }
            });
        }
        // 4 special presets to reach 40
        list.Add(new ShapeStylePreset
        {
            Id = $"shape-style-{n++}", Name = "Dark 1 – No Fill",
            Fill = ShapeFill.NoFill(), OutlineColorHex = "#242424", OutlineWidthPt = 1.5
        });
        list.Add(new ShapeStylePreset
        {
            Id = $"shape-style-{n++}", Name = "Subtle Effect – Dark",
            FillColorHex = "#E7E6E6", OutlineColorHex = "#595959", OutlineWidthPt = 0.5
        });
        list.Add(new ShapeStylePreset
        {
            Id = $"shape-style-{n++}", Name = "Intense Effect – Dark",
            FillColorHex = "#404040", OutlineColorHex = null,
            Effect = new ShapeEffectLst { HasShadow = true }
        });
        list.Add(new ShapeStylePreset
        {
            Id = $"shape-style-{n}", Name = "Diagonal Hatch",
            Fill = ShapeFill.Patterned("diagCross", "#4472C4", "#FFFFFF"),
            OutlineColorHex = "#4472C4", OutlineWidthPt = 1.0
        });
        return [.. list];
    }

    private static string LightenHex(string hex, float amount)
    {
        var (r, g, b) = ParseHex(hex);
        r = (byte)(r + (255 - r) * amount);
        g = (byte)(g + (255 - g) * amount);
        b = (byte)(b + (255 - b) * amount);
        return $"{r:X2}{g:X2}{b:X2}";
    }

    private static string DarkenHex(string hex, float amount)
    {
        var (r, g, b) = ParseHex(hex);
        r = (byte)(r * (1 - amount));
        g = (byte)(g * (1 - amount));
        b = (byte)(b * (1 - amount));
        return $"{r:X2}{g:X2}{b:X2}";
    }

    private static (byte r, byte g, byte b) ParseHex(string hex)
    {
        if (!DrawingMlRgbColor.TryParseHexRgb(hex, out var color))
            throw new FormatException($"'{hex}' is not a six-digit RGB color.");

        return (color.R, color.G, color.B);
    }
}

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

    // ── New W24 fields ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extended fill (gradient / pattern / no-fill). When non-null this overrides
    /// <see cref="FillColorHex"/> for DOCX serialisation. Null means the simple solid-fill path.
    /// </summary>
    public ShapeFill? ExtendedFill { get; set; }

    /// <summary>
    /// Optional effects bundle (shadow / glow / soft-edge / reflection / bevel). Null means no effects.
    /// Maps onto <c>a:effectLst</c> (and <c>a:sp3d</c> for bevel) inside <c>wps:spPr</c>.
    /// </summary>
    public ShapeEffectLst? Effects { get; set; }

    // ── W25: Custom Geometry (Edit Points) ────────────────────────────────────────────────────────

    /// <summary>
    /// Optional freeform custom geometry replacing the preset shape outline. When non-null the writer
    /// emits <c>a:custGeom</c> instead of <c>a:prstGeom</c> in <c>wps:spPr</c>, and the renderer
    /// draws the polygon using WPF StreamGeometry. The "Convert to Freeform / Edit Points" command
    /// sets this property. Null means the shape continues to use its <see cref="Kind"/> preset.
    /// Round-trips via <c>a:custGeom/a:pathLst/a:path</c> in DOCX.
    /// </summary>
    public CustomGeometry? CustomGeometry { get; set; }

    /// <summary>True when this shape has been converted to a freeform custom geometry.</summary>
    public bool HasCustomGeometry => CustomGeometry is not null && CustomGeometry.Segments.Count > 0;

    // ── W26: Body rotation / flip ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Clockwise rotation of the entire shape body in degrees (0–359).
    /// Maps onto <c>a:xfrm/@rot</c> (degrees × 60 000 = DrawingML angle). Defaults to 0.
    /// </summary>
    public double RotationAngle { get; set; }

    /// <summary>
    /// Mirror the shape horizontally (<c>a:xfrm/@flipH="1"</c>). Defaults to false.
    /// </summary>
    public bool FlipH { get; set; }

    /// <summary>
    /// Mirror the shape vertically (<c>a:xfrm/@flipV="1"</c>). Defaults to false.
    /// </summary>
    public bool FlipV { get; set; }

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
