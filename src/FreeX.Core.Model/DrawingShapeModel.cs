namespace FreeX.Core.Model;

public enum DrawingShapeEffectPreset
{
    None = 0,
    Shadow = 1,
    Glow = 2,
    SoftEdges = 3,
    InnerShadow = 4,
    Reflection = 5,
    Bevel = 6,
    ThreeDRotation = 7
}

public enum DrawingShapeGradientDirection
{
    DiagonalDown,
    Horizontal,
    Vertical,
    DiagonalUp
}

/// <summary>
/// Arrowhead type for the start (<c>&lt;a:headEnd&gt;</c>) or end (<c>&lt;a:tailEnd&gt;</c>) of a line/connector.
/// Matches OOXML <c>type</c> attribute values.
/// </summary>
public enum DrawingArrowheadType
{
    None = 0,
    Triangle = 1,
    Arrow = 2,
    Stealth = 3,
    Diamond = 4,
    Oval = 5,
}

/// <summary>
/// Size dimension (width or length) of a line/connector arrowhead.
/// Matches OOXML <c>w</c>/<c>len</c> attribute values on <c>&lt;a:headEnd&gt;</c> / <c>&lt;a:tailEnd&gt;</c>.
/// </summary>
public enum DrawingArrowheadSize
{
    Small = 0,
    Medium = 1,
    Large = 2,
}

/// <summary>
/// Arrowhead descriptor for one end of a line/connector shape.
/// </summary>
public sealed record DrawingArrowhead(
    DrawingArrowheadType Type,
    DrawingArrowheadSize Width = DrawingArrowheadSize.Medium,
    DrawingArrowheadSize Length = DrawingArrowheadSize.Medium)
{
    public static readonly DrawingArrowhead None = new(DrawingArrowheadType.None);
    public bool IsPresent => Type != DrawingArrowheadType.None;
}

/// <summary>
/// One adjust-handle value for a preset geometry shape, from <c>&lt;a:gd name="..." fmla="val N"/&gt;</c>
/// inside <c>&lt;a:avLst&gt;</c> (e.g. a rounded rectangle's corner-radius handle or a block arrow's
/// head-size handle). <see cref="Name"/> is the <c>gd</c> guide name (typically "adj", "adj1", "adj2", ...)
/// and <see cref="Formula"/> is the raw <c>fmla</c> attribute value (typically "val N").
/// </summary>
public sealed record DrawingShapeAdjustValue(string Name, string Formula);

/// <summary>
/// Dash style for a shape outline, matching OOXML <c>&lt;a:prstDash val="..."/&gt;</c> presets.
/// </summary>
public enum DrawingShapeOutlineDash
{
    Solid = 0,
    Dash = 1,
    Dot = 2,
    DashDot = 3,
    LongDash = 4,
    LongDashDot = 5,
    LongDashDotDot = 6,
    SystemDash = 7,
    SystemDot = 8,
    SystemDashDot = 9,
}

/// <summary>
/// Horizontal text alignment within a shape's text body, matching OOXML <c>&lt;a:pPr algn="..."/&gt;</c>.
/// </summary>
public enum DrawingShapeTextHAlign
{
    Left = 0,
    Center = 1,
    Right = 2,
}

/// <summary>
/// Vertical anchor for text within a shape's text body, matching OOXML <c>&lt;a:bodyPr anchor="..."/&gt;</c>.
/// </summary>
public enum DrawingShapeTextVAnchor
{
    Top = 0,
    Middle = 1,
    Bottom = 2,
}

public sealed class DrawingShapeModel
{
    public static readonly CellColor DefaultFillColor = new(0x5B, 0x9B, 0xD5);
    public static readonly CellColor DefaultOutlineColor = new(0x2F, 0x55, 0x97);

    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Name { get; set; }
    public CellAddress Anchor { get; set; }

    /// <summary>
    /// Horizontal sub-cell offset (in DIP pixels, EMU/9525) from the left edge of the <see cref="Anchor"/>
    /// cell to the shape's left edge, preserved from the authored two-/one-cell anchor's <c>from/colOff</c>.
    /// Lets the render reflect the true sub-cell position rather than snapping to the whole-cell left edge.
    /// </summary>
    public double AnchorOffsetX { get; set; }

    /// <summary>
    /// Vertical sub-cell offset (in DIP pixels, EMU/9525) from the top edge of the <see cref="Anchor"/> cell
    /// to the shape's top edge, preserved from the authored anchor's <c>from/rowOff</c>.
    /// </summary>
    public double AnchorOffsetY { get; set; }

    public DrawingShapeKind Kind { get; set; } = DrawingShapeKind.Rectangle;
    public double Width { get; set; } = 120;
    public double Height { get; set; } = 70;
    public double RotationDegrees { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool HasFill { get; set; } = true;

    /// <summary>
    /// Whether this shape is locked against move/resize while its sheet is protected with
    /// the "Edit objects" permission blocked, matching OOXML <c>&lt;a:spLocks noMove="1" noResize="1".../&gt;</c>
    /// (the fLocksWithSheet-style per-object lock under the shape's spPr, or the analogous
    /// picLocks/graphicFrameLocks for pictures/charts). Defaults to <see langword="true"/>, matching
    /// Excel's default of a locked shape. When an author explicitly unlocks a shape (unchecks
    /// Format Shape &gt; Properties &gt; Locked) that one shape stays movable/resizable even while
    /// the sheet protection has "Edit objects" turned off, while other (default-locked) shapes on
    /// the same protected sheet remain immovable.
    /// </summary>
    /// <remarks>
    /// Reading/writing the OOXML per-shape lock attribute (<c>a:spLocks</c>) on load/save is
    /// deferred follow-up work — this field is currently in-memory/session-only and defaults to
    /// locked, matching Excel's authored default when no lock override is present.
    /// </remarks>
    public bool Locked { get; set; } = true;
    public string? Title { get; set; }
    public string? AltText { get; set; }
    public CellColor? FillColor { get; set; }
    public CellColor? OutlineColor { get; set; }
    public CellColor? GradientFillEndColor { get; set; }
    public DrawingShapeGradientDirection GradientFillDirection { get; set; } = DrawingShapeGradientDirection.DiagonalDown;
    public WorkbookThemeColorReference? FillThemeColor { get; set; }
    public WorkbookThemeColorReference? OutlineThemeColor { get; set; }
    public bool HasShadowEffect { get; set; }
    public DrawingShapeEffectPreset EffectPreset { get; set; }
    public bool UsesThemeEffects { get; set; }
    public bool IsSourceLoaded { get; set; }

    /// <summary>
    /// R127-editas-shift-gate: mirrors <see cref="ChartModel.DrawingAnchorKind"/> -- captures the
    /// source anchor's <c>editAs</c> semantics (<c>xdr:twoCellAnchor</c> "move and size with cells",
    /// <c>xdr:oneCellAnchor</c> "move but don't size with cells", or <c>xdr:absoluteAnchor</c> "don't
    /// move or size with cells") so <c>RowColumnShiftHelpers.ShiftDrawingShapes</c> can gate row/column
    /// insert-delete's move+resize on it instead of unconditionally applying twoCellAnchor semantics
    /// to every shape. Populated from <c>XlsxDrawingAnchor.Kind</c> by
    /// <see cref="FreeX.Core.IO.XlsxDrawingAnchorApplier"/>'s (internal) <c>ApplyToShape</c>. Defaults
    /// to <see cref="ChartDrawingAnchorKind.TwoCell"/> -- Excel's own default for a freshly inserted
    /// shape and the class's pre-existing move+resize behavior for any non-source-loaded shape built
    /// without setting this explicitly.
    /// </summary>
    public ChartDrawingAnchorKind DrawingAnchorKind { get; set; } = ChartDrawingAnchorKind.TwoCell;

    /// <summary>
    /// R97-model-drawing-hyperlink-2-2: this shape's object-level hyperlink (an
    /// <c>&lt;a:hlinkClick&gt;</c> on its <c>cNvPr</c>), populated on load and carried through
    /// clone/paste (<c>DuplicateSheetDrawingCloner</c>, <c>PasteShapesCommand</c>) so a copy of a
    /// hyperlinked shape keeps its hyperlink even when the copy is not itself source-loaded (and so
    /// has nothing for <c>XlsxWorksheetDrawingObjectWriter</c> to re-read from the source package).
    /// <see langword="null"/> means "no hyperlink".
    /// </summary>
    public DrawingObjectHyperlink? Hyperlink { get; set; }

    /// <summary>
    /// R94 fix: this shape's <see cref="Width"/>/<see cref="Height"/> as they stood immediately after
    /// LOAD -- either the size computed from the source anchor's original cell span
    /// (<c>XlsxDrawingAnchorApplier.GetAnchorSize</c>/its xfrm-extent preference) or, when that
    /// computation yields 0 for an axis because the anchor's own span falls entirely within hidden
    /// rows/columns, the class-default <see cref="Width"/>/<see cref="Height"/> the model retains in
    /// that case (R94-hidden-span fix) -- captured by that same applier call and never touched
    /// afterward except by a fresh reload. Used by <c>XlsxSourceDrawingGeometryRewriter</c> to tell a
    /// genuine user resize (<see cref="Width"/>/<see cref="Height"/> diverging from this baseline)
    /// apart from an incidental sheet layout change -- a row/column elsewhere hidden or resized between
    /// load and save -- which would otherwise make the SAME never-touched anchor appear to need its
    /// <c>to</c> marker rewritten, because the marker's pixel-to-cell walk is evaluated against the
    /// CURRENT sheet layout while these fields freeze the layout as of load. Always non-null once the
    /// shape has been through <c>ApplyToShape</c>; null only when the shape was never source-loaded
    /// (e.g. freshly inserted).
    /// </summary>
    public double? SourceLoadedWidthPixels { get; set; }

    /// <summary>See <see cref="SourceLoadedWidthPixels"/>; the same baseline for <see cref="Height"/>.</summary>
    public double? SourceLoadedHeightPixels { get; set; }

    /// <summary>
    /// Outline stroke width in points (1 pt = 12700 EMU).  Zero means "use the renderer default".
    /// Null/negative is treated as zero.  Set from <c>&lt;a:ln w="..."/&gt;</c>.
    /// </summary>
    public double OutlineWidthPoints { get; set; }

    /// <summary>
    /// When <see langword="true"/> the outline element was present with <c>&lt;a:noFill/&gt;</c>
    /// which means the shape explicitly has NO border, regardless of <see cref="OutlineColor"/>.
    /// </summary>
    public bool OutlineHasNoFill { get; set; }

    /// <summary>
    /// Dash style for the outline stroke, sourced from <c>&lt;a:prstDash val="..."/&gt;</c>.
    /// </summary>
    public DrawingShapeOutlineDash OutlineDash { get; set; } = DrawingShapeOutlineDash.Solid;

    /// <summary>
    /// Arrowhead at the start (head) of a line/connector, from <c>&lt;a:headEnd type="..." w="..." len="..."/&gt;</c>.
    /// <see langword="null"/> or <see cref="DrawingArrowhead.None"/> means no arrowhead at the start.
    /// Only meaningful when <see cref="DrawingShapeKindSupport.IsLineLike"/> is <see langword="true"/>.
    /// </summary>
    public DrawingArrowhead? HeadArrowhead { get; set; }

    /// <summary>
    /// Arrowhead at the end (tail) of a line/connector, from <c>&lt;a:tailEnd type="..." w="..." len="..."/&gt;</c>.
    /// <see langword="null"/> or <see cref="DrawingArrowhead.None"/> means no arrowhead at the end.
    /// Only meaningful when <see cref="DrawingShapeKindSupport.IsLineLike"/> is <see langword="true"/>.
    /// </summary>
    public DrawingArrowhead? TailArrowhead { get; set; }

    /// <summary>
    /// R90-shape-5-3: the <c>id</c> of the other drawing shape this connector's START point is glued
    /// to, from <c>&lt;xdr:cxnSp&gt;&lt;xdr:nvCxnSpPr&gt;&lt;xdr:cNvCxnSpPr&gt;&lt;a:stCxn id="..." idx="..."/&gt;</c>.
    /// <see langword="null"/> means the start point is a free-floating endpoint, not attached to any shape.
    /// Only meaningful when <see cref="DrawingShapeKindSupport.IsLineLike"/> is <see langword="true"/>. Preserved
    /// for round-trip fidelity; FreeX does not yet re-route a connector when its attached shape moves.
    /// </summary>
    public int? StartConnectedShapeId { get; set; }

    /// <summary>
    /// The connection-site index (<c>idx</c>) on the <see cref="StartConnectedShapeId"/> shape that this
    /// connector's start point is glued to. Meaningless when <see cref="StartConnectedShapeId"/> is null.
    /// </summary>
    public int? StartConnectedShapeConnectionIndex { get; set; }

    /// <summary>
    /// R90-shape-5-3: the <c>id</c> of the other drawing shape this connector's END point is glued to,
    /// from <c>&lt;xdr:cxnSp&gt;&lt;xdr:nvCxnSpPr&gt;&lt;xdr:cNvCxnSpPr&gt;&lt;a:endCxn id="..." idx="..."/&gt;</c>.
    /// <see langword="null"/> means the end point is a free-floating endpoint, not attached to any shape.
    /// Only meaningful when <see cref="DrawingShapeKindSupport.IsLineLike"/> is <see langword="true"/>. Preserved
    /// for round-trip fidelity; FreeX does not yet re-route a connector when its attached shape moves.
    /// </summary>
    public int? EndConnectedShapeId { get; set; }

    /// <summary>
    /// The connection-site index (<c>idx</c>) on the <see cref="EndConnectedShapeId"/> shape that this
    /// connector's end point is glued to. Meaningless when <see cref="EndConnectedShapeId"/> is null.
    /// </summary>
    public int? EndConnectedShapeConnectionIndex { get; set; }

    /// <summary>
    /// Adjust-handle values for the shape's preset geometry, from <c>&lt;a:avLst&gt;&lt;a:gd .../&gt;</c>
    /// (e.g. a rounded rectangle's dragged corner-radius handle, or a block arrow's head-size handle).
    /// <see langword="null"/> or empty means "use the geometry's built-in default handle positions".
    /// Populated when reading a source shape so a customized handle survives a save even after an
    /// edit (fill/outline/effect change) clears <see cref="IsSourceLoaded"/> and routes the shape
    /// through the generated-geometry writer path instead of verbatim passthrough.
    /// </summary>
    public IReadOnlyList<DrawingShapeAdjustValue>? AdjustValues { get; set; }

    // ── WordArt / text effects ──────────────────────────────────────────────

    /// <summary>
    /// When <see langword="true"/> the shape's txBody carries a WordArt-style text run:
    /// a text fill (gradient or solid) and/or text outline that should be rendered using
    /// styled text geometry rather than the plain body fill + white text fallback.
    /// Detected when a run contains a gradient text fill, a text outline, or a
    /// <c>&lt;a:prstTxWarp&gt;</c> element is present on the bodyPr.
    /// </summary>
    public bool IsWordArt { get; set; }

    /// <summary>
    /// The <c>prst</c> attribute value from <c>&lt;a:prstTxWarp prst="..."&gt;</c>, e.g. "textWave1".
    /// Preserved for round-trip; warp rendering is deferred — the text is rendered flat.
    /// <see langword="null"/> when no warp preset is authored.
    /// </summary>
    public string? WarpPreset { get; set; }

    /// <summary>
    /// Gradient end color for the shape's text fill (WordArt gradient text).
    /// When non-null the text fill is a gradient from <see cref="ShapeTextColor"/> (or
    /// <see cref="ShapeTextThemeColor"/>) to this color.
    /// <see langword="null"/> means no gradient — solid fill using the existing color fields.
    /// </summary>
    public CellColor? ShapeTextGradientEndColor { get; set; }

    /// <summary>
    /// Theme-color reference for the gradient end stop of a WordArt gradient text fill.
    /// </summary>
    public WorkbookThemeColorReference? ShapeTextGradientEndThemeColor { get; set; }

    /// <summary>
    /// Linear gradient direction angle in OOXML 60,000ths-of-a-degree (same unit as
    /// <c>&lt;a:lin ang="..."&gt;</c>).  5400000 = 90° = top-to-bottom (default).
    /// Only meaningful when a gradient text fill is present
    /// (<see cref="ShapeTextGradientEndColor"/> or <see cref="ShapeTextGradientEndThemeColor"/>
    /// is non-null).
    /// </summary>
    public long ShapeTextGradientAngle { get; set; } = 5400000;

    /// <summary>
    /// Outline color for WordArt text (from <c>&lt;a:rPr&gt;&lt;a:ln&gt;&lt;a:solidFill&gt;</c>).
    /// <see langword="null"/> means no text outline.
    /// </summary>
    public CellColor? ShapeTextOutlineColor { get; set; }

    /// <summary>
    /// Theme-color reference for the WordArt text outline.
    /// </summary>
    public WorkbookThemeColorReference? ShapeTextOutlineThemeColor { get; set; }

    /// <summary>
    /// Width in points of the WordArt text outline stroke.
    /// Zero means "use a thin default" (≈0.5 pt) when <see cref="ShapeTextOutlineColor"/> is set.
    /// </summary>
    public double ShapeTextOutlineWidthPoints { get; set; }

    // ── Shape text (txBody) properties ─────────────────────────────────────

    /// <summary>
    /// Concatenated plain text from all runs in the shape's <c>&lt;xdr:txBody&gt;</c>, or
    /// <see langword="null"/> / empty when the shape carries no text.
    /// </summary>
    public string? ShapeText { get; set; }

    /// <summary>
    /// Font size for the first run's <c>&lt;a:rPr sz&gt;</c>, in points (OOXML stores
    /// hundredths of a point; divide by 100 when reading).  Zero or negative means "inherit
    /// default" (renderer uses 11 pt).
    /// </summary>
    public double ShapeTextFontSizePoints { get; set; }

    /// <summary>Bold (<c>&lt;a:rPr b="1"/&gt;</c>).</summary>
    public bool ShapeTextBold { get; set; }

    /// <summary>Italic (<c>&lt;a:rPr i="1"/&gt;</c>).</summary>
    public bool ShapeTextItalic { get; set; }

    /// <summary>Underline (<c>&lt;a:rPr u="sng"/&gt;</c> or any non-"none" value).</summary>
    public bool ShapeTextUnderline { get; set; }

    /// <summary>
    /// Explicit font color from <c>&lt;a:rPr&gt;&lt;a:solidFill&gt;&lt;a:srgbClr&gt;</c>.
    /// <see langword="null"/> means "no explicit color" — renderer uses white or a theme default.
    /// </summary>
    public CellColor? ShapeTextColor { get; set; }

    /// <summary>
    /// Theme-based font color (from <c>&lt;a:rPr&gt;&lt;a:solidFill&gt;&lt;a:schemeClr&gt;</c>).
    /// Takes precedence over <see cref="ShapeTextColor"/> when non-null.
    /// </summary>
    public WorkbookThemeColorReference? ShapeTextThemeColor { get; set; }

    /// <summary>
    /// Horizontal paragraph alignment from <c>&lt;a:pPr algn="l|ctr|r"/&gt;</c>.
    /// </summary>
    public DrawingShapeTextHAlign ShapeTextHAlign { get; set; } = DrawingShapeTextHAlign.Left;

    /// <summary>
    /// Vertical text anchor from <c>&lt;a:bodyPr anchor="t|ctr|b"/&gt;</c>.
    /// </summary>
    public DrawingShapeTextVAnchor ShapeTextVAnchor { get; set; } = DrawingShapeTextVAnchor.Middle;

    /// <summary>
    /// Whether the text wraps within the shape bounds (<c>&lt;a:bodyPr wrap="square"/&gt;</c>
    /// vs <c>"none"</c>).
    /// </summary>
    public bool ShapeTextWrap { get; set; } = true;

    /// <summary>
    /// Returns <see langword="true"/> when this shape carries displayable text.
    /// </summary>
    public bool HasShapeText => !string.IsNullOrEmpty(ShapeText);

    /// <summary>
    /// Resolves the effective font color for shape text, using theme if available.
    /// Returns <see langword="null"/> when neither an explicit nor a theme color is set
    /// (caller should use a default such as white-on-dark / black-on-light).
    /// </summary>
    public CellColor? ResolveShapeTextColor(WorkbookTheme theme) =>
        ShapeTextThemeColor?.Resolve(theme) ?? ShapeTextColor;

    /// <summary>
    /// Resolves the effective gradient-end color for a WordArt text fill.
    /// Returns <see langword="null"/> when no gradient end color is authored.
    /// </summary>
    public CellColor? ResolveShapeTextGradientEndColor(WorkbookTheme theme) =>
        ShapeTextGradientEndThemeColor?.Resolve(theme) ?? ShapeTextGradientEndColor;

    /// <summary>
    /// Resolves the effective text outline color for WordArt.
    /// Returns <see langword="null"/> when no text outline is authored.
    /// </summary>
    public CellColor? ResolveShapeTextOutlineColor(WorkbookTheme theme) =>
        ShapeTextOutlineThemeColor?.Resolve(theme) ?? ShapeTextOutlineColor;

    public CellColor GetEffectiveFillColor(WorkbookTheme theme, CellColor fallback) =>
        FillThemeColor?.Resolve(theme) ?? FillColor ?? fallback;

    public CellColor? ResolveFillColor(WorkbookTheme theme, CellColor fallback) =>
        HasFill ? GetEffectiveFillColor(theme, fallback) : null;

    public CellColor GetEffectiveOutlineColor(WorkbookTheme theme, CellColor fallback) =>
        OutlineThemeColor?.Resolve(theme) ?? OutlineColor ?? fallback;

    public static CellColor ResolveDefaultFillColor(WorkbookTheme theme) =>
        theme.ObjectDefaults?.Shape?.FillThemeColor?.Resolve(theme) ??
        theme.ObjectDefaults?.Shape?.FillColor ??
        DefaultFillColor;

    public static CellColor ResolveDefaultOutlineColor(WorkbookTheme theme) =>
        theme.ObjectDefaults?.Shape?.OutlineThemeColor?.Resolve(theme) ??
        theme.ObjectDefaults?.Shape?.OutlineColor ??
        DefaultOutlineColor;

    public DrawingShapeGradientDirection GetEffectiveGradientFillDirection() =>
        Enum.IsDefined(GradientFillDirection)
            ? GradientFillDirection
            : DrawingShapeGradientDirection.DiagonalDown;

    public DrawingShapeEffectPreset GetEffectiveEffectPreset()
    {
        if (Enum.IsDefined(EffectPreset) && EffectPreset != DrawingShapeEffectPreset.None)
            return EffectPreset;

        return HasShadowEffect
            ? DrawingShapeEffectPreset.Shadow
            : DrawingShapeEffectPreset.None;
    }
}
