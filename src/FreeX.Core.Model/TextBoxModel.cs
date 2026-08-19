namespace FreeX.Core.Model;

public sealed class TextBoxModel
{
    public const double DefaultWidth = 180d;
    public const double DefaultHeight = 80d;

    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Name { get; set; }
    public CellAddress Anchor { get; set; }

    /// <summary>Horizontal sub-cell offset (in DIP pixels, EMU/9525) from the left edge of the
    /// <see cref="Anchor"/> cell to the text box's left edge, preserved from the authored anchor's
    /// <c>from/colOff</c> so the render reflects the true sub-cell position.</summary>
    public double AnchorOffsetX { get; set; }

    /// <summary>Vertical sub-cell offset (in DIP pixels, EMU/9525) from the top edge of the
    /// <see cref="Anchor"/> cell to the text box's top edge, preserved from the authored anchor's
    /// <c>from/rowOff</c>.</summary>
    public double AnchorOffsetY { get; set; }

    public string Text { get; set; } = "";
    public string? Title { get; set; }
    public string? AltText { get; set; }

    /// <summary>
    /// R149-app-accessibility-checker-decorative-shapes: true when the user marked this text box
    /// "decorative" via Excel's Format Shape &gt; Alt Text pane -- mirrors
    /// <see cref="PictureModel.IsDecorative"/> and <see cref="DrawingShapeModel.IsDecorative"/>.
    /// A decorative text box is intentionally content-free and is exempt from the Accessibility
    /// Checker's Missing Alt Text rule even when it has no alt text/title/name at all, matching
    /// real Excel's own Accessibility Checker.
    /// </summary>
    public bool IsDecorative { get; set; }
    public double Width { get; set; } = DefaultWidth;
    public double Height { get; set; } = DefaultHeight;
    public double RotationDegrees { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// R111-model-drawing-object-lock-1-1: whether this text box is locked against move/resize while
    /// its sheet is protected with the "Edit objects" permission blocked -- mirrors
    /// <see cref="DrawingShapeModel.Locked"/> (matching OOXML <c>&lt;a:spLocks noMove="1" noResize="1".../&gt;</c>).
    /// Defaults to <see langword="true"/>, matching Excel's default of a locked text box. When an author
    /// explicitly unlocks a text box (unchecks Format Shape &gt; Properties &gt; Locked) that one text
    /// box stays movable/resizable even while the sheet protection has "Edit objects" turned off, while
    /// other (default-locked) text boxes on the same protected sheet remain immovable.
    /// </summary>
    /// <remarks>
    /// Reading/writing the OOXML per-text-box lock attribute (<c>a:spLocks</c>) on load/save is
    /// deferred follow-up work, exactly like <see cref="DrawingShapeModel.Locked"/> -- this field is
    /// currently in-memory/session-only and defaults to locked, matching Excel's authored default when
    /// no lock override is present.
    /// </remarks>
    public bool Locked { get; set; } = true;

    public bool HasFill { get; set; } = true;
    public CellColor? FillColor { get; set; }
    public CellColor? OutlineColor { get; set; }
    public WorkbookThemeColorReference? FillThemeColor { get; set; }
    public WorkbookThemeColorReference? OutlineThemeColor { get; set; }

    /// <summary>
    /// True when the text box's line is explicitly suppressed (OOXML <c>&lt;a:ln&gt;&lt;a:noFill/&gt;</c>),
    /// mirroring <c>DrawingShapeModel.OutlineHasNoFill</c>. Defaults to <see langword="false"/> (line shown)
    /// so every existing construction path -- the xlsx/legacy-xls/native-json loaders and any other code
    /// that doesn't set this explicitly -- keeps its prior always-bordered rendering unchanged. A freshly
    /// inserted text box is the one deliberate exception: Excel's Insert &gt; Text Box defaults to No Fill,
    /// No Line, so <c>AddTextBoxCommand</c> (FreeX.Core.Commands) explicitly sets both this and
    /// <see cref="HasFill"/> to match, instead of this field's own safe default.
    /// </summary>
    public bool OutlineHasNoFill { get; set; }

    public bool IsSourceLoaded { get; set; }

    /// <summary>
    /// R127-editas-shift-gate: mirrors <see cref="ChartModel.DrawingAnchorKind"/> -- captures the
    /// source anchor's <c>editAs</c> semantics (<c>xdr:twoCellAnchor</c> "move and size with cells",
    /// <c>xdr:oneCellAnchor</c> "move but don't size with cells", or <c>xdr:absoluteAnchor</c> "don't
    /// move or size with cells") so <c>RowColumnShiftHelpers.ShiftTextBoxes</c> can gate row/column
    /// insert-delete's move+resize on it instead of unconditionally applying twoCellAnchor semantics
    /// to every text box. Populated from <c>XlsxDrawingAnchor.Kind</c> by
    /// <see cref="FreeX.Core.IO.XlsxDrawingAnchorApplier"/>'s (internal) <c>ApplyToTextBox</c>. Defaults
    /// to <see cref="ChartDrawingAnchorKind.TwoCell"/> -- Excel's own default for a freshly inserted
    /// text box (Insert &gt; Text Box) and the class's pre-existing move+resize behavior for any
    /// non-source-loaded text box built without setting this explicitly.
    /// </summary>
    public ChartDrawingAnchorKind DrawingAnchorKind { get; set; } = ChartDrawingAnchorKind.TwoCell;

    /// <summary>
    /// R97-model-drawing-hyperlink-2-2: this text box's object-level hyperlink (an
    /// <c>&lt;a:hlinkClick&gt;</c> on its <c>cNvPr</c>), populated on load and carried through
    /// clone/paste (<c>DuplicateSheetDrawingCloner</c>, <c>PasteTextBoxesCommand</c>) so a copy of a
    /// hyperlinked text box keeps its hyperlink even when the copy is not itself source-loaded (and
    /// so has nothing for <c>XlsxWorksheetDrawingObjectWriter</c> to re-read from the source
    /// package). <see langword="null"/> means "no hyperlink".
    /// </summary>
    public DrawingObjectHyperlink? Hyperlink { get; set; }

    /// <summary>
    /// R94 fix: this text box's <see cref="Width"/>/<see cref="Height"/> as they stood immediately after
    /// LOAD -- either the size computed from the source anchor's original cell span
    /// (<c>XlsxDrawingAnchorApplier.GetAnchorSize</c>) or, when that computation yields 0 for an axis
    /// because the anchor's own span falls entirely within hidden rows/columns, the class-default
    /// <see cref="Width"/>/<see cref="Height"/> the model retains in that case (R94-hidden-span fix) --
    /// captured by that same applier call and never touched afterward except by a fresh reload. Used by
    /// <c>XlsxSourceDrawingGeometryRewriter</c> to tell a genuine user resize (<see cref="Width"/>/
    /// <see cref="Height"/> diverging from this baseline) apart from an incidental sheet layout change --
    /// a row/column elsewhere hidden or resized between load and save -- which would otherwise make the
    /// SAME never-touched anchor appear to need its <c>to</c> marker rewritten, because the marker's
    /// pixel-to-cell walk is evaluated against the CURRENT sheet layout while these fields freeze the
    /// layout as of load. Always non-null once the text box has been through <c>ApplyToTextBox</c>; null
    /// only when the text box was never source-loaded (e.g. freshly inserted).
    /// </summary>
    public double? SourceLoadedWidthPixels { get; set; }

    /// <summary>See <see cref="SourceLoadedWidthPixels"/>; the same baseline for <see cref="Height"/>.</summary>
    public double? SourceLoadedHeightPixels { get; set; }

    // ── Text formatting (txBody) ────────────────────────────────────────────
    // Mirrors DrawingShapeModel's ShapeText* fields (same flattened, first-run-only
    // simplification -- see XlsxWorksheetDrawingParts.ReadShapeTextFormatting) so a text box's
    // rich-text formatting survives a load -> Duplicate Sheet -> save round-trip instead of being
    // silently dropped (the model previously had no fields to carry it, so DuplicateSheetCommand
    // stripped it and a real xlsx load never populated it -- backlog textbox-6-2).

    /// <summary>
    /// Font family/typeface for the text box's text, from the first run's
    /// <c>&lt;a:rPr&gt;&lt;a:latin typeface="..."/&gt;</c>. <see langword="null"/> means "no
    /// explicit font family authored" -- the renderer/Excel falls back to the theme's minor font.
    /// </summary>
    public string? TextFontFamily { get; set; }

    /// <summary>
    /// Font size for the first run's <c>&lt;a:rPr sz&gt;</c>, in points (OOXML stores hundredths
    /// of a point; divide by 100 when reading). Zero or negative means "inherit default".
    /// </summary>
    public double TextFontSizePoints { get; set; }

    /// <summary>Bold (<c>&lt;a:rPr b="1"/&gt;</c>).</summary>
    public bool TextBold { get; set; }

    /// <summary>Italic (<c>&lt;a:rPr i="1"/&gt;</c>).</summary>
    public bool TextItalic { get; set; }

    /// <summary>
    /// Explicit font color from <c>&lt;a:rPr&gt;&lt;a:solidFill&gt;&lt;a:srgbClr&gt;</c>.
    /// <see langword="null"/> means "no explicit color" -- renderer uses a default (e.g. black).
    /// </summary>
    public CellColor? TextColor { get; set; }

    /// <summary>
    /// Theme-based font color (from <c>&lt;a:rPr&gt;&lt;a:solidFill&gt;&lt;a:schemeClr&gt;</c>).
    /// Takes precedence over <see cref="TextColor"/> when non-null.
    /// </summary>
    public WorkbookThemeColorReference? TextThemeColor { get; set; }

    /// <summary>Horizontal paragraph alignment from <c>&lt;a:pPr algn="l|ctr|r"/&gt;</c>.</summary>
    public DrawingShapeTextHAlign TextHAlign { get; set; } = DrawingShapeTextHAlign.Left;

    /// <summary>
    /// Vertical text anchor from <c>&lt;a:bodyPr anchor="t|ctr|b"/&gt;</c>. Defaults to
    /// <see cref="DrawingShapeTextVAnchor.Top"/> -- unlike <c>DrawingShapeModel.ShapeTextVAnchor</c>
    /// (which defaults to Middle), a plain Excel-authored text box's bodyPr genuinely defaults to
    /// top-anchored, and this is also the value a brand-new (never-loaded) FreeX text box needs so
    /// the writer's now-unconditional explicit anchor attribute reproduces the same rendered
    /// position a fresh text box always had before this field existed.
    /// </summary>
    public DrawingShapeTextVAnchor TextVAnchor { get; set; } = DrawingShapeTextVAnchor.Top;

    public CellColor GetEffectiveFillColor(WorkbookTheme theme, CellColor fallback) =>
        FillThemeColor?.Resolve(theme) ?? FillColor ?? fallback;

    public CellColor? ResolveFillColor(WorkbookTheme theme, CellColor fallback) =>
        HasFill ? GetEffectiveFillColor(theme, fallback) : null;

    public CellColor GetEffectiveOutlineColor(WorkbookTheme theme, CellColor fallback) =>
        OutlineThemeColor?.Resolve(theme) ?? OutlineColor ?? fallback;

    /// <summary>
    /// Resolves the effective text color, preferring the theme reference when present.
    /// Returns <see langword="null"/> when neither an explicit nor a theme color is set.
    /// </summary>
    public CellColor? ResolveTextColor(WorkbookTheme theme) =>
        TextThemeColor?.Resolve(theme) ?? TextColor;

    public static TextBoxModel? FindById(IEnumerable<TextBoxModel> textBoxes, Guid textBoxId)
    {
        ArgumentNullException.ThrowIfNull(textBoxes);

        foreach (var textBox in textBoxes)
        {
            if (textBox.Id == textBoxId)
                return textBox;
        }

        return null;
    }
}
