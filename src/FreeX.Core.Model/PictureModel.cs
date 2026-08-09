namespace FreeX.Core.Model;

public enum PictureKind
{
    CellRangeSnapshot,
    Image
}

public sealed class PictureModel
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string? Name { get; set; }
    public CellAddress Anchor { get; set; }

    /// <summary>Horizontal sub-cell offset (in DIP pixels, EMU/9525) from the left edge of the
    /// <see cref="Anchor"/> cell to the picture's left edge, preserved from the authored anchor's
    /// <c>from/colOff</c> so the render reflects the true sub-cell position.</summary>
    public double AnchorOffsetX { get; set; }

    /// <summary>Vertical sub-cell offset (in DIP pixels, EMU/9525) from the top edge of the
    /// <see cref="Anchor"/> cell to the picture's top edge, preserved from the authored anchor's
    /// <c>from/rowOff</c>.</summary>
    public double AnchorOffsetY { get; set; }

    public PictureKind Kind { get; set; } = PictureKind.CellRangeSnapshot;
    public uint SourceRowCount { get; set; }
    public uint SourceColumnCount { get; set; }
    public bool IsLinkedToSourceRange { get; set; }
    public GridRange? LinkedSourceRange { get; set; }
    public string? LinkedSourceSheetName { get; set; }
    public List<PictureCellSnapshot> Cells { get; } = [];
    public byte[]? ImageBytes { get; set; }
    public string? ContentType { get; set; }
    public string? Title { get; set; }
    public string? AltText { get; set; }

    /// <summary>
    /// R90-app-accessibility-checker-5-2: true when the user explicitly marked this picture
    /// "decorative" (Excel's Alt Text pane "Mark as decorative" checkbox), from the
    /// <c>&lt;xdr:cNvPr&gt;&lt;a:extLst&gt;&lt;a:ext uri="{C183D7F6-B498-43B3-948B-1728B52AA6E4}"&gt;
    /// &lt;adec:decorative val="1"/&gt;</c> extension. A decorative picture is intentionally
    /// content-free (e.g. a divider graphic) and is exempt from the Accessibility Checker's
    /// "Missing alternative text" rule even when <see cref="AltText"/>/<see cref="Title"/>/
    /// <see cref="Name"/> are all blank -- matching real Excel's own Accessibility Checker.
    /// </summary>
    public bool IsDecorative { get; set; }
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 140;
    public bool LockAspectRatio { get; set; } = true;
    public double RotationDegrees { get; set; }
    public bool FlipHorizontal { get; set; }
    public bool FlipVertical { get; set; }
    public bool IsVisible { get; set; } = true;

    /// <summary>
    /// R111-model-drawing-object-lock-1-1: whether this picture is locked against move/resize while
    /// its sheet is protected with the "Edit objects" permission blocked -- mirrors
    /// <see cref="DrawingShapeModel.Locked"/> (matching OOXML <c>&lt;a:picLocks noMove="1" noResize="1".../&gt;</c>).
    /// Defaults to <see langword="true"/>, matching Excel's default of a locked picture. When an author
    /// explicitly unlocks a picture (unchecks Format Picture &gt; Properties &gt; Locked) that one
    /// picture stays movable/resizable even while the sheet protection has "Edit objects" turned off,
    /// while other (default-locked) pictures on the same protected sheet remain immovable.
    /// </summary>
    /// <remarks>
    /// Reading/writing the OOXML per-picture lock attribute (<c>a:picLocks</c>) on load/save is
    /// deferred follow-up work, exactly like <see cref="DrawingShapeModel.Locked"/> -- this field is
    /// currently in-memory/session-only and defaults to locked, matching Excel's authored default when
    /// no lock override is present.
    /// </remarks>
    public bool Locked { get; set; } = true;

    public double CropLeft { get; set; }
    public double CropTop { get; set; }
    public double CropRight { get; set; }
    public double CropBottom { get; set; }
    public bool IsSourceLoaded { get; set; }

    /// <summary>
    /// R127-editas-shift-gate: mirrors <see cref="ChartModel.DrawingAnchorKind"/> -- captures the
    /// source anchor's <c>editAs</c> semantics (<c>xdr:twoCellAnchor</c> "move and size with cells",
    /// <c>xdr:oneCellAnchor</c> "move but don't size with cells", or <c>xdr:absoluteAnchor</c> "don't
    /// move or size with cells") so <c>RowColumnShiftHelpers.ShiftPictures</c> can gate row/column
    /// insert-delete's move+resize on it instead of unconditionally applying twoCellAnchor semantics
    /// to every picture. Populated from <c>XlsxDrawingAnchor.Kind</c> by
    /// <see cref="FreeX.Core.IO.XlsxDrawingAnchorApplier"/>'s (internal) <c>ApplyToPicture</c>. Defaults
    /// to <see cref="ChartDrawingAnchorKind.TwoCell"/> -- Excel's own default for a freshly inserted
    /// picture (Insert &gt; Pictures) and the class's pre-existing move+resize behavior for any
    /// non-source-loaded picture built without setting this explicitly.
    /// </summary>
    public ChartDrawingAnchorKind DrawingAnchorKind { get; set; } = ChartDrawingAnchorKind.TwoCell;

    /// <summary>
    /// R97-model-drawing-hyperlink-2-2: this picture's object-level hyperlink (an
    /// <c>&lt;a:hlinkClick&gt;</c> on its <c>cNvPr</c>), populated on load and carried through
    /// clone/paste (<c>DuplicateSheetDrawingCloner</c>, <c>PastePicturesCommand</c>) so a copy of a
    /// hyperlinked picture keeps its hyperlink even when the copy is not itself source-loaded (and
    /// so has nothing for <c>XlsxWorksheetDrawingObjectWriter</c> to re-read from the source
    /// package). <see langword="null"/> means "no hyperlink".
    /// </summary>
    public DrawingObjectHyperlink? Hyperlink { get; set; }

    /// <summary>
    /// R94 fix: this picture's <see cref="Width"/>/<see cref="Height"/> as they stood immediately after
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
    /// layout as of load. Always non-null once the picture has been through <c>ApplyToPicture</c>; null
    /// only when the picture was never source-loaded (e.g. freshly inserted).
    /// </summary>
    public double? SourceLoadedWidthPixels { get; set; }

    /// <summary>See <see cref="SourceLoadedWidthPixels"/>; the same baseline for <see cref="Height"/>.</summary>
    public double? SourceLoadedHeightPixels { get; set; }

    /// <summary>
    /// The external relationship target (verbatim, e.g. an absolute path/URI such as
    /// <c>"file:///C:/Images/photo.png"</c>) for a picture inserted via Excel's "Link to File" — an
    /// <c>&lt;xdr:pic&gt;</c> whose <c>&lt;a:blip&gt;</c> carries <c>r:link</c> instead of <c>r:embed</c>,
    /// with no corresponding image part embedded in the package. Null for a normal embedded picture
    /// (the common case). A non-null value means <see cref="ImageBytes"/> is empty/null — there is
    /// nothing embedded to load — and this picture must round-trip its <c>r:link</c> + external
    /// relationship on save instead of being written (or silently dropped) as an embedded picture.
    /// See R65-io-image-drawing-6-1.
    /// </summary>
    public string? LinkedImageTarget { get; set; }

    /// <summary>
    /// The raw bytes of the vector <c>.svg</c> media part backing this picture, when it was inserted
    /// via Excel's Insert &gt; Icons/SVG -- Excel always keeps a PNG rasterization in
    /// <see cref="ImageBytes"/>/<see cref="ContentType"/> as the universal-compatibility fallback, but
    /// carries this vector original alongside it (via the <c>a:blip/a:extLst</c> <c>asvg:svgBlip</c>
    /// extension) so the picture stays editable as a vector in Excel's "Graphics Format" ribbon tab
    /// (recolor, "Convert to Shape"). Null for every ordinary raster picture. Must be re-emitted
    /// alongside the PNG fallback on save or the picture permanently downgrades to a flat raster the
    /// first time it is edited. See R80-io-drawing-image-5-3.
    /// </summary>
    public byte[]? SvgImageBytes { get; set; }
}

public sealed record PictureCellSnapshot(
    uint RowOffset,
    uint ColumnOffset,
    string Text,
    CellStyle? Style = null,
    bool IsNumericOrDate = false);
