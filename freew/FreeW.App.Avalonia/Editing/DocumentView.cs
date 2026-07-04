using System.Globalization;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Links;
using FreeW.App.Presentation.Ribbon;
using FreeW.App.Presentation.Shell;
using FreeW.Core.Model;
using TextAlignment = FreeW.Core.Model.TextAlignment;

namespace FreeW.App.Avalonia.Editing;

/// <summary>Controls how <see cref="DocumentView"/> lays out and renders the document.</summary>
public enum DocumentViewMode
{
    /// <summary>
    /// Paginated white pages on a grey desk with margins, page breaks, and inter-page gaps.
    /// This is the default — matches Word's Print Layout view.
    /// </summary>
    PrintLayout,

    /// <summary>
    /// Single continuous column at the control's available width (capped at <c>WebMaxContentWidth</c>),
    /// plain white background, no page breaks, no grey desk, no page chrome. Matches Word's Web Layout.
    /// </summary>
    WebLayout,

    /// <summary>
    /// Plain white background, small fixed left margin, no chrome, continuous flow.
    /// Fastest/plainest reading/editing view. Matches Word's Draft view.
    /// </summary>
    Draft,
}

/// <summary>
/// AV-HANDLES: identifies which manipulation handle a pointer is over (or that a drag is operating on)
/// for a selected floating object. <see cref="None"/> = not on the object at all; <see cref="Body"/> =
/// inside the object but not on a resize handle (a press there starts a drag-move). The eight remaining
/// values are the resize handles: four corners (resize both dimensions) and four edge midpoints
/// (resize one dimension, anchoring the opposite edge).
/// </summary>
public enum FloatHandle
{
    None,
    Body,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
}

/// <summary>
/// FreeW's editing surface. The WPF host used a RichTextBox/FlowDocument; Avalonia has no
/// FlowDocument, so this is a custom <see cref="Control"/> that lays out the
/// <see cref="TextDocument"/> per character, renders runs with their formatting, and routes every
/// edit through the shared <see cref="DocumentCommandBus"/> (so undo/redo come for free). Caret
/// addressing is (block index, character offset within the block's text). Tables render as grids with
/// modal cell text editing; non-text runs render read-only for now; plain paragraphs are fully editable.
/// </summary>
public sealed class DocumentView : Control
{
    private const double PxPerPoint = 96.0 / 72.0;
    // Print-layout chrome: grey "desk" gap above (and below) the white page surface.
    private const double DeskPadding = 24;
    // AV-VIEW: spacing (DIP) between layout-gridlines (Word draws a ~quarter-inch grid; 18pt ≈ 0.25in).
    private const double GridlineStepDip = 18.0;
    // AV-VIEW: height/width of the ruler strip drawn at the page top / left edge in Print Layout.
    private const double RulerThicknessDip = 14.0;
    // Gap between consecutive page rectangles (grey desk visible between them).
    private const double PageGap = 20;
    private const double DefaultFontSizePt = 11;
    private const double FallbackWidth = 816; // 8.5in * 96dpi
    private const double ListIndentStep = 24;

    // Superscript / subscript rendering approximation (matches Word's ~58% size + ~33% raise/lower).
    // SuperSubScale: font shrinks to ~58% of the run's size (Word uses 58.3%).
    private const double SuperSubScale = 0.583;
    // SuperYRaiseFraction: superscript baseline sits at ~33% from the top of the line box.
    private const double SuperYRaiseFraction = 0.15;
    // SubYLowerFraction: subscript top sits at ~33% from the top of the line box so the shrunk
    // glyph (~58% of line height) finishes near the baseline (≈0.33 + 0.58*lineH ≈ 0.91*lineH)
    // instead of overflowing into the next line.  Matches Word's subscript baseline offset.
    private const double SubYLowerFraction = 0.33;

    private DocumentViewMode _viewMode = DocumentViewMode.PrintLayout;
    private DocumentViewDepthLayoutPlan _viewDepthLayout =
        DocumentViewDepthLayoutPlanner.Build(FreeWViewDepthMode.LiveEditor);
    private bool _showParagraphMarks;
    // AV-VIEW: layout-gridlines overlay (faint grid behind text) + ruler strip (top horizontal +
    // left vertical with tick marks and margin markers). Both are view-only chrome — they never
    // affect layout, only the Render pass — so toggling them just invalidates the visual.
    private bool _showGridlines;
    private bool _showTableGridlines;
    private bool _showRuler;

    // Standard Word font-size ladder (pt).
    private static readonly double[] FontSizeLadder = [8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 36, 48, 72];

    private readonly Dictionary<string, IBrush> _brushCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PlacedChar> _placed = new();
    private readonly List<(double X, double Y, string Text, RunFormatting Fmt)> _markers = new();
    // AV-TAB: leader spans emitted during body tab layout; drawn in Render before glyph text.
    // Each entry: (X1=tab start, X2=segment start, Y=page-space top, LineHeight, Leader kind, RunFmt for color/size).
    private readonly List<(double X1, double X2, double Y, double LineHeight, TabLeader Leader, RunFormatting Fmt)> _tabLeaderSpans = new();
    // AV-TBL4: extended to carry per-cell shading brush and shared per-edge border plans.
    // Fill: IBrush? combines table-style fills (header/band) with per-cell ShadingColorHex.
    // Border: bool = table-level outer border; CellBorderPlan: per-edge override planned from the model.
    private readonly List<(Rect Rect, IBrush? Fill, bool Border, TableCellBorderVisualPlan? CellBorderPlan)> _rects = new();
    private readonly List<(Rect Rect, string? ShadingHex, ParagraphBorder? Border)> _paragraphDecorations = new();
    private readonly List<(Rect Rect, Bitmap? Image)> _images = new();
    // Floating images collected during layout; rendered separately from inline images with z-order.
    // BehindText=true → drawn before body text (behind); BehindText=false → drawn after (in front).
    // AV-FLSEL: BlockIndex/RunIndex added so hit-test can locate the model object.
    private readonly List<(Rect Rect, Bitmap? Image, bool BehindText, int ZOrder, int BlockIndex, int RunIndex)> _floatingImages = new();
    // Floating shapes collected during layout; rendered in the same z-ordered passes as floating images.
    // ShapeData captures everything needed to draw the shape in Render() without re-touching the model.
    private readonly List<FloatingShapeData> _floatingShapes = new();
    // FO3: floating charts, WordArt, SmartArt, drawing groups — same z-ordered behind/in-front passes.
    private readonly List<FloatingChartData>    _floatingCharts    = new();
    private readonly List<FloatingWordArtData>  _floatingWordArts  = new();
    private readonly List<FloatingSmartArtData> _floatingSmartArts = new();
    private readonly List<FloatingGroupData>    _floatingGroups    = new();
    private readonly List<DocumentFloatingObjectSnapshot> _floatingSnapshots = new();
    // AV-WRAP: unified list of wrap-exclusion zones (Square/Tight/TopAndBottom only).
    // Populated during Collect* calls; consulted by EmitLinePaged and LayoutParagraphPaged.
    // Each entry is a page-space rect + the wrapping mode (Behind/InFront entries are never added).
    private readonly List<DocumentFloatingWrapExclusionZone> _wrapExclusions = new();
    // HF: pre-computed header/footer render items (rebuilt in Relayout when PrintLayout).
    private readonly List<HfRenderItem> _headerFooterItems = new();
    // AV-NOTERENDER: pre-computed footnote/endnote text render items (rebuilt in Relayout when PrintLayout).
    // Footnotes land in the bottom margin band of the page that hosts their reference; endnotes are
    // stacked in a synthetic section after the last body page. Separator rules are stored alongside.
    private readonly List<NoteRenderItem> _noteItems = new();
    private readonly List<(double X1, double X2, double Y)> _noteSeparators = new();
    // AV-NOTERENDER: extra page-space height reserved below the last body page for the endnotes section.
    private double _endnoteExtentDip;
    // DB1/DB2: per-page true footnote band height (0-based page index → band height in DIP).
    // Populated by BuildFootnoteItems after a first-pass body layout; ReserveContentY uses it to shrink
    // the effective text area on that page so body text reflows above the footnote band.
    private readonly Dictionary<int, double> _footnoteBandHeightByPage = new();
    // FO4: inline (non-floating) charts, WordArt, SmartArt — rendered in the text flow like inline images.
    private readonly List<FloatingChartData>    _inlineCharts    = new();
    private readonly List<FloatingWordArtData>  _inlineWordArts  = new();
    private readonly List<FloatingSmartArtData> _inlineSmartArts = new();
    private readonly Dictionary<InlineImage, Bitmap?> _bitmapCache = new();
    private byte[]? _watermarkBitmapCacheBytes;
    private Bitmap? _watermarkBitmapCache;
    private readonly List<(Rect Rect, int Block, int Row, int Col)> _cellHits = new();

    // ── AV-TBL: in-place table cell caret ─────────────────────────────────────────────────────────
    // Non-null when the caret is inside a table cell. Stores the fully-qualified cell address and the
    // character offset within that cell's paragraph. When _cellCaret is set, _caret.Block = the table
    // block index and _caret.Offset = the PlacedChar.Offset emitted for that glyph (for hit-test lookup).
    private (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? _cellCaret;
    // Non-null when there is a selection anchor inside a cell (same encoding as _cellCaret).
    private (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? _cellAnchor;

    // ── AV-TBL2: cross-cell rectangular selection ─────────────────────────────────────────────────
    // When a drag spans more than one cell, we switch from single-cell text selection to a rectangular
    // block selection. _cellBlockAnchor is the cell where the drag started; _cellBlockFocus is the cell
    // under the pointer. Non-null only while a multi-cell selection is active.
    private (int TableBlock, int Row, int Col)? _cellBlockAnchor;
    private (int TableBlock, int Row, int Col)? _cellBlockFocus;

    // ── AV-HFEDIT: in-region header/footer caret ──────────────────────────────────────────────────
    // Non-null when the caret is inside a rendered header or footer region. Stores which section
    // header/footer SLOT the caret is in (default/first/even header or footer), the paragraph index
    // within that slot, and the character offset within that paragraph's literal model text.
    // Mirrors the _cellCaret pattern but addresses the per-section HeaderFooter store instead of a
    // table cell. When set, the body caret/selection state is suppressed (drawn separately).
    private (HfTarget Target, int Offset)? _hfCaret;

    // ── AV-FLSEL: floating-object selection + placement edit ──────────────────────────────────────
    // Selected floating object (null = no selection). Kind = "Image"|"Shape"|"Chart"|"WordArt"|"SmartArt"|"Group".
    // Rect is the page-space bounding rect as laid out in the last layout pass.
    private (int BlockIndex, int RunIndex, string Kind, Rect Rect)? _selectedFloating;
    // Multi-select set for object arrange commands. DrawingGroup itself is single-select because the
    // shared grouping command currently consumes only concrete floating object runs.
    private readonly List<(int BlockIndex, int RunIndex, string Kind)> _selectedFloatingObjects = [];
    // AV-HANDLES: drag state — non-null while the user is dragging a selected float (move OR resize).
    // PointerDown : pointer page-space position when the drag started.
    // FloatRect   : the float's page-space Rect at drag start (used to revert on Esc + as the resize base).
    // Handle      : which manipulation is active (Body = move; any edge/corner = resize from that handle).
    private (Point PointerDown, Rect FloatRect, FloatHandle Handle)? _floatDragState;

    private TextDocument _doc = TextDocument.CreateEmpty();
    private DocumentCommandBus _bus;
    private DocPosition _caret;
    private DocPosition? _selectionAnchor;
    // BZ5: pending character formatting to be applied to the NEXT typed character when the caret
    // is collapsed (no selection). Set by the Font dialog on a collapsed-caret apply; consumed
    // and cleared by the next InsertText call.
    private RunFormatting? _pendingRunFmt;
    private FormatPainterClipboard? _formatPainter;
    private bool _formatPainterLocked;
    // GB2: backed by a persisted .lex store (mirrors the WPF host's CustomDictionaryStore — same file
    // location/format, so words added in either shell are available in the other) rather than a plain
    // in-memory CustomDictionary, so Add-to-Dictionary survives a restart. Loaded once at construction;
    // best-effort (a failed load/save never blocks editing — see CustomDictionaryStore).
    private readonly CustomDictionaryStore _customDictionary;
    private double _laidOutWidth = -1;
    private double _contentHeight;
    private double _pageLeft;
    private double _pageWidth;
    private double _contentLeft;
    private double _contentWidth;
    // Top/bottom margins in DIP (from PageSettings, recomputed on each Relayout).
    private double _marginTopDip;
    private double _marginBottomDip;
    // Page height in DIP (from PageSettings, recomputed on each Relayout).
    private double _pageHeightPx;
    // Number of discrete pages after the last layout pass.
    private int _pageCount = 1;
    private DocumentViewSurfacePlan _surfacePlan =
        DocumentViewLayoutPlanner.BuildSurfacePlan(new PageSettings(), DocumentViewLayoutKind.PrintLayout, FallbackWidth);

    // ── AV-COL: multi-column body text layout fields ────────────────────────────────────────────────
    // Number of body-text columns for the current layout (1 = single-column, the default).
    private int    _colCount     = 1;
    // Width of each equal column in DIP (0 when single-column).
    private double _colWidth     = 0;
    // Gap between adjacent columns in DIP (0 when single-column).
    private double _colGap       = 0;
    // Whether to draw a vertical rule line in each inter-column gap.
    private bool   _colLineBetween = false;

    public DocumentView()
        : this(null)
    {
    }

    internal DocumentView(CustomDictionaryStore? customDictionary)
    {
        _customDictionary = customDictionary ?? CustomDictionaryStore.Load();
        Focusable = true;
        _bus = new DocumentCommandBus(new ViewContext(this));
        _bus.Changed += OnModelChanged;
    }

    /// <summary>Raised after any change to the document (edit, undo/redo, load) so the shell can refresh chrome.</summary>
    public event Action? DocumentChanged;

    /// <summary>Raised when a Find result moves the caret, so the shell can scroll it into view.</summary>
    public event Action? ScrollToCaretRequested;

    /// <summary>Raised when the caret moves (key navigation, click, find) so the shell can update the page indicator.</summary>
    public event Action? CaretMoved;

    /// <summary>
    /// AV-PICTAB: Raised when the floating-object selection IDENTITY changes — i.e. a different
    /// float (block+run) is selected, or the selection is cleared. Does NOT fire on pure
    /// rect/geometry refreshes of the same object (drag-move, size update). The ribbon's
    /// <c>FloatingRibbonContextSource</c> subscribes to this to show/hide the Picture / Drawing
    /// Format contextual tabs.
    /// </summary>
    public event Action? FloatingSelectionChanged;

    /// <summary>
    /// AV-PICTAB: Last (block,run) identity for which <see cref="FloatingSelectionChanged"/> was raised.
    /// Used to suppress duplicate notifications when only the selection rect changes.
    /// </summary>
    private (int BlockIndex, int RunIndex)? _lastSignaledFloating;

    /// <summary>
    /// AV-PICTAB: Fire <see cref="FloatingSelectionChanged"/> iff the selected float's identity
    /// (block+run) differs from the last signalled value. Call after every assignment to
    /// <see cref="_selectedFloating"/>.
    /// </summary>
    private void RaiseFloatingSelectionChangedIfIdentityChanged()
    {
        var identity = _selectedFloating is { } sel ? (sel.BlockIndex, sel.RunIndex) : ((int, int)?)null;
        if (identity == _lastSignaledFloating)
            return;
        _lastSignaledFloating = identity;
        FloatingSelectionChanged?.Invoke();
    }

    /// <summary>
    /// Raised when a table cell double-click is received and no in-place caret placement is possible
    /// (e.g., the cell has no placed glyphs yet). Kept for shell compatibility; normal editing now
    /// routes the caret directly into the cell via <see cref="PlaceCaretInCell"/>.
    /// AV-TBL: this event is now only fired as a fallback; in-place editing supersedes it.
    /// </summary>
#pragma warning disable CS0067 // event may remain un-raised when in-place path is always taken
    public event Action<CellEditRequest>? CellEditRequested;
#pragma warning restore CS0067

    /// <summary>Raised when <see cref="ViewMode"/> changes so the shell can update the status bar / ribbon state.</summary>
    public event Action? ViewModeChanged;

    /// <summary>
    /// AV-LINK: Raised when the user follows an <em>external</em> hyperlink (a web/file URL) — by Ctrl+Click
    /// on the link, or via <see cref="FollowHyperlinkAtCaret"/>. The shell/MainWindow handles this to open the
    /// URL with the OS shell (the control deliberately does not hard-code a browser). Internal links (to a
    /// document bookmark) are not raised here — they are navigated in-place via <see cref="GoToBookmark"/>.
    /// </summary>
    public event Action<string>? HyperlinkActivated;

    /// <summary>
    /// Gets or sets the view mode. Switching modes triggers a full re-layout and visual invalidation.
    /// <list type="bullet">
    ///   <item><see cref="DocumentViewMode.PrintLayout"/> — paginated, grey desk (default).</item>
    ///   <item><see cref="DocumentViewMode.WebLayout"/> — continuous column, plain white, no page chrome.</item>
    ///   <item><see cref="DocumentViewMode.Draft"/> — continuous, minimal left margin, no chrome.</item>
    /// </list>
    /// Editing (caret, hit-test, selection, undo/redo, GetBlockTop, find) works in all modes because
    /// all operations read <c>_placed</c>, which is fully re-laid-out for the active mode.
    /// </summary>
    public DocumentViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (_viewMode == value)
                return;
            _viewMode = value;
            _viewDepthLayout = DocumentViewDepthLayoutPlanner.Build(FreeWViewDepthMode.LiveEditor);
            InvalidateLayoutAndVisual();
            ViewModeChanged?.Invoke();
        }
    }

    internal DocumentViewDepthLayoutPlan ViewDepthLayout => _viewDepthLayout;

    internal void ApplyViewDepthLayout(DocumentViewDepthLayoutPlan layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        _viewDepthLayout = layout;
        InvalidateVisual();
    }

    public sealed record CellEditRequest(int Block, int Row, int Col, string Text);

    /// <summary>
    /// Extended run+paragraph formatting snapshot for the current selection, produced by
    /// <see cref="GetSelectionFormatting"/>. The indeterminate flags indicate that the
    /// corresponding property is non-uniform across the selection (mixed). When a flag is true
    /// the dialog should show a blank/indeterminate state for that field and skip applying it
    /// on OK unless the user explicitly changed it.
    /// </summary>
    public sealed record SelectionFormatting(
        RunFormatting Run,
        ParagraphFormatting Paragraph,
        bool BoldIndeterminate          = false,
        bool ItalicIndeterminate        = false,
        bool UnderlineIndeterminate     = false,
        bool StrikethroughIndeterminate = false,
        bool FamilyIndeterminate        = false,
        bool SizeIndeterminate          = false);

    public string GetCellText(int block, int row, int col)
    {
        if (block >= 0 && block < _doc.Blocks.Count && _doc.Blocks[block] is Table table
            && row >= 0 && row < table.Rows.Count && col >= 0 && col < table.Rows[row].Cells.Count)
            return table.Rows[row].Cells[col].PlainText;
        return string.Empty;
    }

    public void SetCellText(int block, int row, int col, string text)
    {
        if (IsEditingLocked)
            return;

        _bus.Execute(new CellTextCommand(block, row, col, text));
    }

    public TextDocument Document => _doc;
    public string? CurrentParagraphStyleId => CurrentParagraph()?.StyleId;
    public bool CanUndo =>
        _bus.CanUndo && AllowsRestrictEditingHistoryOperation(
            RestrictEditingOperationKind.HistoryUndo,
            _bus.NextUndoMutationKind);

    public bool CanRedo =>
        _bus.CanRedo && AllowsRestrictEditingHistoryOperation(
            RestrictEditingOperationKind.HistoryRedo,
            _bus.NextRedoMutationKind);
    public bool SpellCheckEnabled { get; private set; } = true;
    public IReadOnlyList<string> CustomDictionaryWords => _customDictionary.Words;

    /// <summary>Raised whenever document protection or Mark-as-Final state changes.</summary>
    public event EventHandler? ProtectionStateChanged;

    /// <summary>True when restrict-editing protection is enforced.</summary>
    public bool IsProtected => _doc.Protection.IsProtected;

    /// <summary>The current restrict-editing mode stored in the document model.</summary>
    public ProtectionMode ProtectionMode => _doc.Protection.Mode;

    /// <summary>The full document protection settings stored in the document model.</summary>
    public ProtectionSettings Protection => _doc.Protection;

    /// <summary>True when the document is marked final, Word's advisory read-only state.</summary>
    public bool IsMarkedAsFinal => _doc.MarkedAsFinal;

    /// <summary>True when free typing/editing should be blocked by final/protection state.</summary>
    public bool IsEditingLocked => RestrictEditingPolicy.IsBodyEditingLocked;

    public RestrictEditingEnforcementPolicy RestrictEditingPolicy =>
        RestrictEditingEnforcementPolicy.From(_doc.Protection, _doc.MarkedAsFinal);

    public RestrictEditingEnforcementDecision GetRestrictEditingDecision(RestrictEditingOperationKind operation) =>
        RestrictEditingPolicy.DecisionFor(operation);

    private bool AllowsRestrictEditingOperation(RestrictEditingOperationKind operation) =>
        RestrictEditingPolicy.Allows(operation);

    private bool AllowsRestrictEditingHistoryOperation(
        RestrictEditingOperationKind operation,
        DocumentCommandMutationKind? mutationKind) =>
        RestrictEditingPolicy.AllowsHistory(operation, mutationKind);

    public void LoadDocument(TextDocument document)
    {
        _doc = document ?? throw new ArgumentNullException(nameof(document));
        if (_doc.Blocks.Count == 0)
            _doc.Blocks.Add(new Paragraph());
        _bus = new DocumentCommandBus(new ViewContext(this));
        _bus.Changed += OnModelChanged;
        _caret = new DocPosition(FirstEditableBlock(), 0);
        _selectionAnchor = null;
        _cellCaret = null; // AV-TBL: clear cell state on document load
        _cellAnchor = null;
        _hfCaret = null; // AV-HFEDIT: clear header/footer caret on document load
        _selectedFloating = null; // AV-PICTAB: clear float selection on document load
        _selectedFloatingObjects.Clear();
        _floatDragState   = null;
        if (RestrictEditingPolicy.ShouldForceTrackChanges)
            TrackChangesEnabled = true;
        RaiseFloatingSelectionChangedIfIdentityChanged();
        InvalidateLayoutAndVisual();
        DocumentChanged?.Invoke();
    }

    public void Undo()
    {
        if (!AllowsRestrictEditingHistoryOperation(
                RestrictEditingOperationKind.HistoryUndo,
                _bus.NextUndoMutationKind))
            return;

        if (_bus.Undo())
            ClampCaret();
    }

    public void Redo()
    {
        if (!AllowsRestrictEditingHistoryOperation(
                RestrictEditingOperationKind.HistoryRedo,
                _bus.NextRedoMutationKind))
            return;

        if (_bus.Redo())
            ClampCaret();
    }

    /// <summary>Set the document's Word-style Mark as Final advisory read-only state.</summary>
    public void SetMarkedAsFinal(bool markedAsFinal)
    {
        if (_doc.MarkedAsFinal == markedAsFinal)
            return;

        _doc.MarkedAsFinal = markedAsFinal;
        OnProtectionStateChanged();
    }

    /// <summary>Apply restrict-editing protection settings stored on the document model.</summary>
    public void SetProtection(ProtectionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (_doc.Protection == settings)
            return;

        _doc.Protection = settings;
        if (RestrictEditingPolicy.ShouldForceTrackChanges)
            TrackChangesEnabled = true;
        OnProtectionStateChanged();
    }

    /// <summary>Apply a restrict-editing mode without a password.</summary>
    public void SetProtection(ProtectionMode mode) => SetProtection(new ProtectionSettings(mode));

    /// <summary>Toggle the common no-changes/read-only protection mode.</summary>
    public ProtectionMode ToggleReadOnlyProtection()
    {
        var next = _doc.Protection.Mode == ProtectionMode.None
            ? ProtectionMode.ReadOnly
            : ProtectionMode.None;
        SetProtection(next);
        return next;
    }

    private void OnProtectionStateChanged()
    {
        InvalidateLayoutAndVisual();
        ProtectionStateChanged?.Invoke(this, EventArgs.Empty);
        DocumentChanged?.Invoke();
    }

    /// <summary>Select the next occurrence of <paramref name="query"/> after the caret (wraps around).</summary>
    public bool FindNext(string query)
    {
        if (string.IsNullOrEmpty(query))
            return false;
        if (DocumentSearch.FindNext(_doc, query, _caret.Block, _caret.Offset) is not { } hit)
            return false;

        _selectionAnchor = new DocPosition(hit.Block, hit.Start);
        _caret = new DocPosition(hit.Block, hit.Start + hit.Length);
        Focus();
        InvalidateVisual();
        ScrollToCaretRequested?.Invoke();
        return true;
    }

    /// <summary>
    /// Number of discrete pages in the current layout (at least 1).
    /// Always 1 in <see cref="DocumentViewMode.WebLayout"/> and <see cref="DocumentViewMode.Draft"/> modes
    /// because those modes render a single continuous column with no page boundaries.
    /// </summary>
    public int PageCount
    {
        get
        {
            if (_viewMode != DocumentViewMode.PrintLayout)
                return 1;
            if (_laidOutWidth < 0)
                Relayout(FallbackWidth);
            return _pageCount;
        }
    }

    /// <summary>Zero-based page index of the current caret position (for "Page X of Y" in the status bar).</summary>
    public int CaretPageIndex
    {
        get
        {
            if (_laidOutWidth < 0)
                Relayout(FallbackWidth);
            // Find the placed char at the caret and derive its page from its page-space Y.
            foreach (var pc in _placed)
            {
                if (pc.Block == _caret.Block && pc.Offset == _caret.Offset)
                    return PageIndexFromPageSpaceY(pc.Y);
            }
            return 0;
        }
    }

    /// <summary>Top of the current caret in control coordinates (0 when not resolvable).</summary>
    public double CaretTop => TryGetCaretRect(out var rect) ? rect.Y : 0;

    /// <summary>
    /// Returns the Y coordinate (top edge) of the first placed character in <paramref name="blockIndex"/>,
    /// in control coordinates. Returns -1 when the block is not found in the current layout (e.g. out of
    /// range, or not yet laid out). Used by the navigation pane to scroll a heading into view.
    /// </summary>
    public double GetBlockTop(int blockIndex)
    {
        // Ensure at least one layout pass has happened (headless / not-yet-rendered).
        if (_laidOutWidth < 0)
            Relayout(FallbackWidth);

        foreach (var pc in _placed)
        {
            if (pc.Block == blockIndex)
                return pc.Y;
        }

        return -1;
    }

    /// <summary>If the current selection equals <paramref name="query"/>, replace it; then select the next match.</summary>
    public bool ReplaceNext(string query, string replacement)
    {
        if (string.IsNullOrEmpty(query))
            return false;
        if (string.Equals(SelectedText, query, StringComparison.OrdinalIgnoreCase))
            ReplaceSelectionWith(replacement);
        return FindNext(query);
    }

    /// <summary>Replace every occurrence of <paramref name="query"/> from the document start. Returns the count.</summary>
    public int ReplaceAll(string query, string replacement)
    {
        if (string.IsNullOrEmpty(query))
            return 0;

        _caret = new DocPosition(FirstEditableBlock(), 0);
        _selectionAnchor = _caret;
        var count = 0;
        while (count < 10000 && FindNext(query))
        {
            ReplaceSelectionWith(replacement);
            count++;
        }

        InvalidateVisual();
        return count;
    }

    private void ReplaceSelectionWith(string replacement)
    {
        if (NormalizedSelection() is not { } sel || sel.Start.Block != sel.End.Block)
            return;
        var block = sel.Start.Block;
        if (_doc.Blocks[block] is not Paragraph p0 || !IsEditable(p0))
            return;

        var a = sel.Start.Offset;
        var b = sel.End.Offset;
        var existing = ParaCells(p0);
        var fmt = existing.Count == 0
            ? RunFormatting.Default
            : existing[Math.Clamp(a > 0 ? a - 1 : 0, 0, existing.Count - 1)].Fmt;

        _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
        {
            var cells = ParaCells(p);
            var lo = Math.Clamp(a, 0, cells.Count);
            var hi = Math.Clamp(b, 0, cells.Count);
            cells.RemoveRange(lo, Math.Max(0, hi - lo));
            for (var i = 0; i < replacement.Length; i++)
                cells.Insert(lo + i, new Cell(replacement[i], fmt));
            SetRuns(p, cells);
        }));

        _caret = new DocPosition(block, a + replacement.Length);
        _selectionAnchor = _caret;
    }

    // ---- Snapshot for the launch smoke ----------------------------------------------------------

    public int BlockCount => _doc.Blocks.Count;
    public int ParagraphCount => _doc.Blocks.Count(b => b is Paragraph);
    public int PlacedGlyphCount => _placed.Count(p => !p.Sentinel);
    public string PlainText => _doc.PlainText;

    /// <summary>
    /// AV-LINK: Introspect the <em>resolved</em> render styling of the first laid-out glyph in the body
    /// paragraph at <paramref name="block"/> whose paragraph-offset is <paramref name="offset"/> — the colour
    /// + underline the render loop actually draws, after the hyperlink style is layered on. Returns null when
    /// there is no such glyph. Exposed for tests so hyperlink styling can be verified without pixel capture.
    /// </summary>
    internal (string? ColorHex, bool Underline, bool IsHyperlink)? GetGlyphRenderStyle(int block, int offset)
    {
        foreach (var pc in _placed)
        {
            if (pc.Sentinel || pc.Block != block || pc.Offset != offset || pc.IsCell)
                continue;
            var colorHex = pc.Fmt.ColorHex;
            var underline = pc.Fmt.Underline;
            if (pc.IsHyperlink)
            {
                colorHex = string.IsNullOrWhiteSpace(colorHex) ? HyperlinkColorHex : colorHex;
                underline = true;
            }
            return (colorHex, underline, pc.IsHyperlink);
        }
        return null;
    }

    internal RunDecorationVisualPlan? GetGlyphRunDecorationStyle(int block, int offset)
    {
        foreach (var pc in _placed)
        {
            if (pc.Sentinel || pc.Block != block || pc.Offset != offset || pc.IsCell)
                continue;
            return RunDecorationVisualPlanner.Build(pc.Fmt, PxPerPoint);
        }
        return null;
    }

    /// <summary>AV-LINK: the caret's current (Block, Offset) — exposed for navigation tests.</summary>
    internal (int Block, int Offset) CaretPositionForTest => (_caret.Block, _caret.Offset);

    internal IReadOnlyList<ProofingDiagnostic> ProofingDiagnosticsForTest => BuildProofingDiagnostics();

    internal IReadOnlyList<(int Block, int Offset, char Ch, RevisionKind Revision, bool IsRevisionStyled, bool IsFormatRevisionHighlighted, Rect Rect)>
        ReviewGlyphsForTest
    {
        get
        {
            if (_laidOutWidth < 0)
                Relayout(FallbackWidth);

            var policy = CurrentReviewDisplayPolicy;
            return _placed
                .Where(pc => !pc.Sentinel && !pc.IsCell)
                .Select(pc => (Placed: pc, Decision: policy.RevisionDecision(pc.Revision)))
                .Where(item => item.Decision.IsTextVisible)
                .Select(item => (
                    item.Placed.Block,
                    item.Placed.Offset,
                    item.Placed.Ch,
                    item.Placed.Revision,
                    item.Decision.IsRevisionStylingApplied,
                    item.Placed.HasFormatRevision && policy.ShouldHighlightFormattingChanges,
                    new Rect(item.Placed.X, item.Placed.Y, Math.Max(1, item.Placed.W), item.Placed.LineHeight)))
                .ToList();
        }
    }

    internal IReadOnlyList<(int CommentId, Rect Rect)> CommentHighlightGlyphsForTest
    {
        get
        {
            if (_laidOutWidth < 0)
                Relayout(FallbackWidth);

            return CommentAnchorGlyphSnapshot(highlightedOnly: true);
        }
    }

    internal IReadOnlyList<(int Block, Rect Rect)> SimpleMarkupChangeBarsForTest
    {
        get
        {
            if (_laidOutWidth < 0)
                Relayout(FallbackWidth);

            return SimpleMarkupChangeBarSnapshot();
        }
    }

    internal IReadOnlyList<(int Block, int Offset, Rect Rect)> ProofingSquiggleGlyphsForTest
    {
        get
        {
            if (_laidOutWidth < 0)
                Relayout(FallbackWidth);

            var offsets = BuildProofingOffsetSet();
            return _placed
                .Where(pc => !pc.Sentinel && !pc.IsCell && offsets.Contains((pc.Block, pc.Offset)))
                .Select(pc => (pc.Block, pc.Offset, new Rect(pc.X, pc.Y, Math.Max(1, pc.W), pc.LineHeight)))
                .ToList();
        }
    }

    /// <summary>Fires <see cref="HyperlinkActivated"/> with <paramref name="url"/> so tests can
    /// verify that hosts have subscribed without hitting real hyperlinks or Process.Start.</summary>
    internal void SimulateHyperlinkActivatedForTest(string url) => HyperlinkActivated?.Invoke(url);

    // ── AV-TBL: cell editing public surface ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current cell caret address (TableBlock, Row, Col, ParaIdx, Offset), or null
    /// when the caret is in body text. Used by tests and the ribbon to check whether the caret
    /// is inside a table cell.
    /// </summary>
    public (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? CellCaretInfo => _cellCaret;

    /// <summary>
    /// Programmatically place the caret at (row, col, paraIdx, offset) in the table at
    /// <paramref name="tableBlockIndex"/>. Triggers a layout pass if needed, then sets
    /// <c>_cellCaret</c> and updates <c>_caret</c> for caret rendering.
    /// Used by tests and the host to drive cell editing without pointer events.
    /// </summary>
    public void PlaceCaretInCell(int tableBlockIndex, int row, int col, int paraIdx, int offset)
    {
        if (_laidOutWidth < 0)
            Relayout(FallbackWidth);
        var para = GetCellParagraph(tableBlockIndex, row, col, paraIdx);
        if (para == null)
            return;
        var maxOffset = ParaCells(para).Count;
        offset = Math.Clamp(offset, 0, maxOffset);
        _cellCaret = (tableBlockIndex, row, col, paraIdx, offset);
        _cellAnchor = _cellCaret;
        _caret = new DocPosition(tableBlockIndex, FindCellGlyphOffset(tableBlockIndex, row, col, paraIdx, offset));
        _selectionAnchor = _caret;
        _hfCaret = null; // AV-HFEDIT: entering a cell exits any header/footer caret
        InvalidateVisual();
        CaretMoved?.Invoke();
    }

    // ── AV-HFEDIT: header/footer editing public surface ──────────────────────────────────────────

    /// <summary>
    /// Returns the current header/footer caret address, or null when the caret is in body text / a cell.
    /// Reports which section the caret is in, whether it is a footer (vs header), the slot, the paragraph
    /// index within that slot, and the character offset within the paragraph's literal model text.
    /// Used by tests and the ribbon to detect in-region header/footer editing and to drive an
    /// "Edit Header"/"Edit Footer" command.
    /// </summary>
    public (int SectionIndex, bool IsFooter, string Slot, int ParaIdx, int Offset)? HeaderFooterCaretInfo =>
        _hfCaret is { } hc
            ? (hc.Target.SectionIndex, IsFooterSlot(hc.Target.Slot), hc.Target.Slot.ToString(), hc.Target.ParaIdx, hc.Offset)
            : null;

    /// <summary>True when the caret is currently inside an editable header or footer region.</summary>
    public bool IsHeaderFooterCaretActive => _hfCaret is not null;

    /// <summary>
    /// Test entry point: hit-test a page-space point against the rendered header/footer regions and, when it
    /// lands in one, place the H/F caret there. Returns true on a hit. Mirrors the pointer-press routing
    /// without requiring a focused control or a real pointer event.
    /// </summary>
    internal bool HitTestHeaderFooterForTest(Point point)
    {
        if (_laidOutWidth < 0)
            Relayout(FallbackWidth);
        return TryHitTestHeaderFooter(point);
    }

    /// <summary>Test entry point: the current header/footer caret rectangle (page-space), or null when none/not laid out.</summary>
    internal Rect? HfCaretRectForTest => TryGetHfCaretRect(out var r) ? r : null;

    /// <summary>Test shim: invoke Backspace (routes into the H/F region when the H/F caret is active).</summary>
    internal void BackspaceForTest() => Backspace();

    /// <summary>Test shim: invoke forward-delete (routes into the H/F region when the H/F caret is active).</summary>
    internal void DeleteForwardForTest() => DeleteForward();

    /// <summary>Test shim: invoke a paragraph break / Enter (routes into the H/F region when the H/F caret is active).</summary>
    internal void InsertParagraphBreakForTest() => InsertParagraphBreak();

    /// <summary>
    /// Test shim for DD1/DD2: dispatches a key through the header/footer caret switch and returns whether
    /// the key was handled by the H/F guard (i.e. did NOT fall through to the body switch).
    /// Only Tab, Up, and Down are meaningful here. Mirrors the guard logic in OnKeyDown.
    /// </summary>
    internal bool SimulateHfKeyForTest(Key key, bool shift = false)
    {
        if (_hfCaret is null)
            return false;
        switch (key)
        {
            case Key.Tab:
                if (!shift) HfInsertText("\t");
                return true; // consumed — body list path never reached
            case Key.Up:
            case Key.Down:
                return true; // consumed as no-op — body MoveCaretVertical never called
            default:
                return false;
        }
    }

    /// <summary>Test shim: place the body caret at (block, offset), exiting any H/F caret (mirrors MoveCaretToBlock + H/F exit).</summary>
    internal void MoveCaretToBlockForTest(int blockIdx, int offset)
    {
        _hfCaret = null;
        MoveCaretToBlock(blockIdx, offset);
    }

    /// <summary>
    /// Test shim: simulate a body click at the given page-space point — exits any H/F caret and routes the
    /// caret to the body via the same hit-test the pointer handler uses.
    /// </summary>
    internal void HandleBodyClickForTest(Point point)
    {
        if (_laidOutWidth < 0)
            Relayout(FallbackWidth);
        // Mirror the pointer-press order: an H/F hit wins; otherwise a body hit exits the H/F caret.
        if (_viewMode == DocumentViewMode.PrintLayout && TryHitTestHeaderFooter(point))
            return;
        _hfCaret = null;
        if (TryHitTest(point, out var pos))
        {
            _caret = pos;
            _selectionAnchor = pos;
        }
        InvalidateVisual();
    }

    private static bool IsFooterSlot(HfSlot slot) =>
        slot is HfSlot.Footer or HfSlot.FirstFooter or HfSlot.EvenFooter;

    /// <summary>The literal-model text length (sum of run text lengths) of a header/footer paragraph.</summary>
    private int HfParaLength(HfTarget target) => GetHfParagraph(target) is { } p ? HfModelPlainText(p).Length : 0;

    /// <summary>Concatenates a header/footer paragraph's run text (literal model text, including field runs' cached text).</summary>
    private static string HfModelPlainText(Paragraph para)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var run in para.Runs)
            sb.Append(run.Text);
        return sb.ToString();
    }

    /// <summary>
    /// Programmatically place the caret inside the DEFAULT header or footer of the active (final) section
    /// at <paramref name="paraIdx"/> + <paramref name="offset"/>. Triggers a layout pass if needed so the
    /// header/footer region exists, ensures the targeted slot/paragraph exists (creating an empty paragraph
    /// when the slot is currently null/empty), then sets <c>_hfCaret</c>. Exposed for tests and a future
    /// "Edit Header"/"Edit Footer" ribbon command. First-page/odd-even variants are addressed via the
    /// hit-test entry point (clicking the rendered region) rather than this default-slot helper.
    /// </summary>
    public void PlaceCaretInHeaderFooter(bool footer, int paraIdx = 0, int offset = 0)
    {
        if (_laidOutWidth < 0)
            Relayout(FallbackWidth);

        // Default slot on the document-level (final-section) store; create the slot + a paragraph if absent
        // so the caret has a region to land in (mirrors Word's "click into empty header to start typing").
        var store = _doc.FinalSectionHeadersFooters;
        var slot = footer ? HfSlot.Footer : HfSlot.Header;
        var hf = GetHfSlot(store, slot);
        if (hf is null)
        {
            hf = new HeaderFooter();
            if (footer) store.Footer = hf; else store.Header = hf;
        }
        if (hf.Paragraphs.Count == 0)
            hf.Paragraphs.Add(new Paragraph());

        var target = new HfTarget(_doc.Sections.Count - 1, UseFinalSectionStore: true, slot,
            Math.Clamp(paraIdx, 0, hf.Paragraphs.Count - 1));
        // Set the caret first so HfCaretTargets returns true, THEN re-layout at the current width so the
        // (possibly freshly-created/empty) band is built immediately and becomes hit-testable / drawable.
        var width = _laidOutWidth > 0 ? _laidOutWidth : FallbackWidth;
        PlaceCaretInHeaderFooter(target, offset);
        Relayout(width);
        InvalidateVisual();
        CaretMoved?.Invoke();
    }

    /// <summary>
    /// Core header/footer caret placement: clamps the offset to the paragraph's literal length, sets
    /// <c>_hfCaret</c>, and clears body/cell caret state. Used by the public helper and the hit-test path.
    /// </summary>
    private void PlaceCaretInHeaderFooter(HfTarget target, int offset)
    {
        var len = HfParaLength(target);
        offset = Math.Clamp(offset, 0, len);
        _hfCaret = (target, offset);
        // Suppress body + cell caret so only the H/F caret renders + receives edits.
        _cellCaret = null;
        _cellAnchor = null;
        _cellBlockAnchor = null;
        _cellBlockFocus = null;
        _selectionAnchor = null;
        _selectedFloating = null;
        _selectedFloatingObjects.Clear();
        InvalidateVisual();
        CaretMoved?.Invoke();
    }

    /// <summary>
    /// Exit any header/footer caret and return the caret to the body. No-op when not in a header/footer.
    /// Triggered by Esc or by clicking back into the document body.
    /// </summary>
    public void ExitHeaderFooterCaret()
    {
        if (_hfCaret is null)
            return;
        _hfCaret = null;
        ClampCaret();
        InvalidateVisual();
        CaretMoved?.Invoke();
    }

    /// <summary>
    /// Hit-test a page-space point against the rendered header/footer regions. When the point lands in an
    /// editable header/footer line, places the H/F caret at the nearest character offset and returns true.
    /// Mirrors the table-cell entry point: a click inside the region routes the caret into that region.
    /// </summary>
    private bool TryHitTestHeaderFooter(Point point)
    {
        // Find the closest editable region line whose vertical band contains the click, preferring an exact
        // Y-band hit. Each item's band is [Y, Y + LineHeight); X must be within the content area.
        HfRenderItem? best = null;
        var bestDist = double.MaxValue;
        foreach (var item in _headerFooterItems)
        {
            if (item.Target is null)
                continue;
            var top = item.Y;
            var bottom = item.Y + (item.LineHeight > 0 ? item.LineHeight : DefaultFontSizePt * PxPerPoint * 1.3);
            // Vertical band test with a small slop so clicks just above/below the text still land.
            var withinY = point.Y >= top - 2 && point.Y <= bottom + 2;
            if (!withinY)
                continue;
            // Horizontal acceptance: anywhere across the content width of the page that owns this line.
            var left = _contentLeft - 4;
            var right = _contentLeft + _contentWidth + 4;
            if (point.X < left || point.X > right)
                continue;
            // Prefer the item whose drawn text X-range is closest to the click (handles tab-split segments
            // sharing a line). Distance is 0 when inside the segment's own [X, X+width] range.
            var dist = HfItemHorizontalDistance(item, point.X);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = item;
            }
        }

        if (best?.Target is not { } target)
            return false;

        var modelOffset = HfOffsetFromPoint(best, point.X);
        PlaceCaretInHeaderFooter(target, modelOffset);
        return true;
    }

    /// <summary>Horizontal distance from <paramref name="x"/> to a rendered H/F item's drawn text range (0 when inside).</summary>
    private double HfItemHorizontalDistance(HfRenderItem item, double x)
    {
        var ft = Build(item.Text.Length == 0 ? " " : item.Text, item.Fmt);
        var alignOffset = AlignmentOffset(item.Alignment, item.AvailableWidth, ft.WidthIncludingTrailingWhitespace, isLast: true);
        var x0 = item.X + alignOffset;
        var x1 = x0 + ft.WidthIncludingTrailingWhitespace;
        if (x < x0) return x0 - x;
        if (x > x1) return x - x1;
        return 0;
    }

    /// <summary>
    /// Maps a click X to a model-text offset inside a rendered H/F item, via per-prefix width measurement.
    /// Returns ModelStartOffset + (chars before X). Field-resolved text is treated by displayed-char count,
    /// which is exact for literal segments and a good approximation when a field's resolved length differs.
    /// </summary>
    private int HfOffsetFromPoint(HfRenderItem item, double x)
    {
        var ft = Build(item.Text.Length == 0 ? " " : item.Text, item.Fmt);
        var alignOffset = AlignmentOffset(item.Alignment, item.AvailableWidth, ft.WidthIncludingTrailingWhitespace, isLast: true);
        var x0 = item.X + alignOffset;
        var local = x - x0;
        if (local <= 0 || item.Text.Length == 0)
            return item.ModelStartOffset;
        // Walk character prefixes; pick the boundary nearest the click (left-edge of the char it falls before).
        var best = item.Text.Length;
        for (var i = 1; i <= item.Text.Length; i++)
        {
            var prefixW = Build(item.Text[..i], item.Fmt).WidthIncludingTrailingWhitespace;
            var prevW = Build(item.Text[..(i - 1)], item.Fmt).WidthIncludingTrailingWhitespace;
            var mid = (prefixW + prevW) / 2;
            if (local < mid)
            {
                best = i - 1;
                break;
            }
        }
        return item.ModelStartOffset + best;
    }

    // ── AV-HFEDIT: header/footer in-region edit operations ────────────────────────────────────────

    /// <summary>
    /// One editable atom of a header/footer paragraph: a single literal character carrying its run
    /// formatting, OR a whole atomic field run (kept intact across edits so view-only fields like the
    /// page number are never split or corrupted). Literal atoms occupy one model offset each; a field
    /// atom occupies <see cref="FieldRun"/>.Text.Length model offsets (its cached resolved text length).
    /// </summary>
    private readonly record struct HfAtom(char Ch, RunFormatting Fmt, Run? FieldRun)
    {
        public bool IsField => FieldRun is not null;
        public int ModelLength => FieldRun?.Text.Length ?? 1;
    }

    /// <summary>Decomposes a header/footer paragraph into editable atoms (literal chars + atomic field runs).</summary>
    private static List<HfAtom> HfAtoms(Paragraph para)
    {
        var atoms = new List<HfAtom>();
        foreach (var run in para.Runs)
        {
            if (run.FieldKind != RunFieldKind.None || run.ComplexField is not null)
            {
                // Atomic field run — keep whole.
                atoms.Add(new HfAtom('\0', run.Formatting, run));
            }
            else
            {
                foreach (var ch in run.Text)
                    atoms.Add(new HfAtom(ch, run.Formatting, null));
            }
        }
        return atoms;
    }

    /// <summary>
    /// Rebuilds a paragraph's runs from an edited atom list, coalescing consecutive literal atoms that
    /// share formatting into a single run and re-emitting field atoms as their preserved runs.
    /// </summary>
    private static void HfSetAtoms(Paragraph para, List<HfAtom> atoms)
    {
        para.Runs.Clear();
        var i = 0;
        while (i < atoms.Count)
        {
            if (atoms[i].IsField)
            {
                para.Runs.Add(atoms[i].FieldRun!);
                i++;
                continue;
            }
            var fmt = atoms[i].Fmt;
            var start = i;
            while (i < atoms.Count && !atoms[i].IsField && atoms[i].Fmt.Equals(fmt))
                i++;
            var text = new string(atoms.Skip(start).Take(i - start).Select(a => a.Ch).ToArray());
            para.Runs.Add(new Run(text, fmt));
        }
    }

    /// <summary>
    /// Maps a model-text offset to an atom-list index and reports whether that index lands inside a field
    /// atom. Literal atoms advance the model offset by 1; field atoms by their cached text length. When the
    /// offset falls strictly inside a field atom it is snapped to the field's leading edge (atomBefore) so
    /// edits never split a field run.
    /// </summary>
    private static (int AtomIndex, bool InsideField) HfAtomIndexForOffset(List<HfAtom> atoms, int modelOffset)
    {
        var pos = 0;
        for (var i = 0; i < atoms.Count; i++)
        {
            if (modelOffset <= pos)
                return (i, false);
            var next = pos + atoms[i].ModelLength;
            if (modelOffset < next)
                return (i, atoms[i].IsField); // inside this atom; for a field this is "inside the field"
            pos = next;
        }
        return (atoms.Count, false);
    }

    /// <summary>The model-offset length of an atom list (sum of each atom's model length).</summary>
    private static int HfAtomsModelLength(List<HfAtom> atoms) => atoms.Sum(a => a.ModelLength);

    /// <summary>The active run formatting for a typed character at <paramref name="modelOffset"/> (inherits the char before, else after, else default).</summary>
    private static RunFormatting HfActiveFormatting(List<HfAtom> atoms, int modelOffset)
    {
        var (idx, _) = HfAtomIndexForOffset(atoms, modelOffset);
        if (idx > 0 && idx - 1 < atoms.Count && !atoms[idx - 1].IsField)
            return atoms[idx - 1].Fmt;
        if (idx < atoms.Count && !atoms[idx].IsField)
            return atoms[idx].Fmt;
        // Fall back to the first literal atom's formatting, else default.
        var firstLiteral = atoms.FirstOrDefault(a => !a.IsField);
        return atoms.Any(a => !a.IsField) ? firstLiteral.Fmt : RunFormatting.Default;
    }

    /// <summary>Runs an undoable edit on the H/F caret's paragraph, then refreshes layout + caret.</summary>
    private void HfEditParagraph(HfTarget target, Action<List<HfAtom>> mutate, int newOffset)
    {
        var slot = (int)target.Slot;
        _bus.Execute(new EditHeaderFooterParagraphCommand(
            target.SectionIndex, target.UseFinalSectionStore, slot, target.ParaIdx, p =>
            {
                var atoms = HfAtoms(p);
                mutate(atoms);
                HfSetAtoms(p, atoms);
            }));
        var len = HfParaLength(target);
        _hfCaret = (target, Math.Clamp(newOffset, 0, len));
        // Re-layout at the current width so the H/F band + caret reflect the edit immediately.
        var width = _laidOutWidth > 0 ? _laidOutWidth : FallbackWidth;
        Relayout(width);
        InvalidateVisual();
        CaretMoved?.Invoke();
    }

    /// <summary>Insert literal text at the H/F caret (field runs are never split).</summary>
    private void HfInsertText(string text)
    {
        if (_hfCaret is not { } hc)
            return;
        var target = hc.Target;
        var offset = hc.Offset;
        HfEditParagraph(target, atoms =>
        {
            var (idx, _) = HfAtomIndexForOffset(atoms, offset);
            var fmt = HfActiveFormatting(atoms, offset);
            var at = idx;
            foreach (var ch in text)
                atoms.Insert(at++, new HfAtom(ch, fmt, null));
        }, offset + text.Length);
    }

    /// <summary>Backspace at the H/F caret: delete the literal atom before the caret (skips/removes a whole field atom).</summary>
    private void HfBackspace()
    {
        if (_hfCaret is not { } hc)
            return;
        var target = hc.Target;
        var offset = hc.Offset;
        if (offset <= 0)
            return; // at start of paragraph — H/F single-paragraph editing does not merge across slots
        var atoms0 = GetHfParagraph(target) is { } p0 ? HfAtoms(p0) : new List<HfAtom>();
        var (idx, _) = HfAtomIndexForOffset(atoms0, offset);
        var removeIdx = idx - 1;
        if (removeIdx < 0 || removeIdx >= atoms0.Count)
            return;
        var removedModelLen = atoms0[removeIdx].ModelLength;
        HfEditParagraph(target, atoms =>
        {
            if (removeIdx >= 0 && removeIdx < atoms.Count)
                atoms.RemoveAt(removeIdx);
        }, offset - removedModelLen);
    }

    /// <summary>Forward-delete at the H/F caret: delete the atom at the caret (a whole field atom when on one).</summary>
    private void HfDeleteForward()
    {
        if (_hfCaret is not { } hc)
            return;
        var target = hc.Target;
        var offset = hc.Offset;
        var atoms0 = GetHfParagraph(target) is { } p0 ? HfAtoms(p0) : new List<HfAtom>();
        var (idx, _) = HfAtomIndexForOffset(atoms0, offset);
        if (idx < 0 || idx >= atoms0.Count)
            return;
        HfEditParagraph(target, atoms =>
        {
            if (idx >= 0 && idx < atoms.Count)
                atoms.RemoveAt(idx);
        }, offset);
    }

    /// <summary>
    /// Enter in a header/footer splits the current paragraph into two at the caret (a new H/F line),
    /// targeting the H/F slot's paragraph list. Mirrors the cell paragraph-split behaviour.
    /// </summary>
    private void HfInsertParagraphBreak()
    {
        if (_hfCaret is not { } hc)
            return;
        var target = hc.Target;
        var offset = hc.Offset;
        var store = ResolveHfStore(target);
        var hf = store is null ? null : GetHfSlot(store, target.Slot);
        if (hf is null || target.ParaIdx < 0 || target.ParaIdx >= hf.Paragraphs.Count)
            return;
        var para = hf.Paragraphs[target.ParaIdx];
        var atoms = HfAtoms(para);
        var (idx, _) = HfAtomIndexForOffset(atoms, offset);

        _bus.Execute(new SpliceHeaderFooterParagraphsCommand(
            target.SectionIndex, target.UseFinalSectionStore, (int)target.Slot, target.ParaIdx,
            () =>
            {
                var src = hf.Paragraphs[target.ParaIdx];
                var srcAtoms = HfAtoms(src);
                var first = new Paragraph { Formatting = src.Formatting, StyleId = src.StyleId };
                HfSetAtoms(first, srcAtoms.Take(idx).ToList());
                var second = new Paragraph { Formatting = src.Formatting };
                HfSetAtoms(second, srcAtoms.Skip(idx).ToList());
                return [first, second];
            }));

        _hfCaret = (target with { ParaIdx = target.ParaIdx + 1 }, 0);
        var width = _laidOutWidth > 0 ? _laidOutWidth : FallbackWidth;
        Relayout(width);
        InvalidateVisual();
        CaretMoved?.Invoke();
    }

    /// <summary>Move the H/F caret left/right by one model offset within the current paragraph (clamped to its bounds).</summary>
    private void HfMoveCaret(int delta)
    {
        if (_hfCaret is not { } hc)
            return;
        var len = HfParaLength(hc.Target);
        _hfCaret = (hc.Target, Math.Clamp(hc.Offset + delta, 0, len));
        InvalidateVisual();
        CaretMoved?.Invoke();
    }

    // ── AV-TBL2: row/column insert + delete ──────────────────────────────────────────────────────

    /// <summary>
    /// Insert a blank row above the caret's current row in the table.
    /// No-op when the caret is not inside a table cell. Undoable.
    /// </summary>
    public void InsertTableRowAbove() => MutateCaretTable((blockIdx, row, _) =>
        new InsertTableRowCommand(blockIdx, row));

    /// <summary>
    /// Insert a blank row below the caret's current row in the table.
    /// No-op when the caret is not inside a table cell. Undoable.
    /// </summary>
    public void InsertTableRowBelow() => MutateCaretTable((blockIdx, row, _) =>
        new InsertTableRowCommand(blockIdx, row + 1));

    /// <summary>
    /// Delete the caret's current row from the table. No-op when not in a table or only one row remains.
    /// Undoable.
    /// </summary>
    public void DeleteTableRow() => MutateCaretTable((blockIdx, row, _) =>
        new DeleteTableRowCommand(blockIdx, row));

    /// <summary>
    /// Insert a blank column to the left of the caret's current column. Undoable.
    /// </summary>
    public void InsertTableColumnLeft() => MutateCaretTable((blockIdx, _, col) =>
        new InsertTableColumnCommand(blockIdx, col));

    /// <summary>
    /// Insert a blank column to the right of the caret's current column. Undoable.
    /// </summary>
    public void InsertTableColumnRight() => MutateCaretTable((blockIdx, _, col) =>
        new InsertTableColumnCommand(blockIdx, col + 1));

    /// <summary>
    /// Delete the caret's current column from the table. No-op when only one column remains. Undoable.
    /// </summary>
    public void DeleteTableColumn() => MutateCaretTable((blockIdx, _, col) =>
        new DeleteTableColumnCommand(blockIdx, col));

    /// <summary>
    /// Executes a table mutation (insert/delete row or column) keyed on the caret's table location.
    /// Locates the (blockIndex, row, col) from <see cref="_cellCaret"/>, builds the command with the
    /// supplied factory, runs it through the command bus (undoable), clears the stale cell caret, and
    /// triggers a re-layout.
    /// </summary>
    private void MutateCaretTable(Func<int, int, int, IDocumentCommand> build)
    {
        if (IsEditingLocked)
            return;

        if (_cellCaret is not { } cc)
            return;
        var blockIdx = cc.TableBlock;
        if (blockIdx < 0 || blockIdx >= _doc.Blocks.Count || _doc.Blocks[blockIdx] is not Table)
            return;
        var cmd = build(blockIdx, cc.Row, cc.Col);
        _bus.Execute(cmd);
        // Clear the cell caret — row/col indices shift after mutations; ClampCaret handles safe reset.
        _cellCaret = null;
        _cellAnchor = null;
        _cellBlockAnchor = null;
        _cellBlockFocus = null;
        InvalidateLayoutAndVisual();
    }

    // ── AV-TBL3: cell merge / split ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Merge the rectangular block of cells in <see cref="SelectedCellRange"/> into a single cell.
    /// When the selection spans only a single row the model-level horizontal merge command is used
    /// (collapses GridSpan). When the selection spans only a single column a vertical merge is used
    /// (sets VerticalMerge Restart/Continue flags). When the selection spans both rows and columns a
    /// horizontal merge is applied to the first row as a best-effort approximation (same behaviour as
    /// the WPF host). Requires an active cross-cell selection; no-op when only a point caret is active.
    /// Undoable.
    /// </summary>
    public void MergeSelectedCells()
    {
        if (IsEditingLocked)
            return;

        if (SelectedCellRange is not { } sel)
            return;
        var blockIdx = sel.TableBlock;
        if (blockIdx < 0 || blockIdx >= _doc.Blocks.Count || _doc.Blocks[blockIdx] is not Table)
            return;

        var table = (Table)_doc.Blocks[blockIdx];
        if (sel.MinRow == sel.MaxRow)
        {
            // Same row — horizontal merge.
            // BH1: SelectedCellRange returns GRID columns; MergeCellsHorizontalCommand expects
            // CELL-LIST indices. Convert via GridColumnToCellIndex for the relevant row.
            var row = table.Rows[sel.MinRow];
            var firstCellIdx = GridColumnToCellIndex(row, sel.MinCol);
            var lastCellIdx  = GridColumnToCellIndex(row, sel.MaxCol);
            if (firstCellIdx < 0 || lastCellIdx < 0)
                return;
            _bus.Execute(new MergeCellsHorizontalCommand(blockIdx, sel.MinRow, firstCellIdx, lastCellIdx));
        }
        else if (sel.MinCol == sel.MaxCol)
        {
            // Same column — vertical merge.
            // MergeCellsVerticalCommand takes a GRID column and converts internally — pass as-is.
            _bus.Execute(new MergeCellsVerticalCommand(blockIdx, sel.MinCol, sel.MinRow, sel.MaxRow));
        }
        else
        {
            // Mixed — best-effort: horizontal merge on the first row only.
            // BH1: same grid→cell-list conversion for the best-effort path.
            var row = table.Rows[sel.MinRow];
            var firstCellIdx = GridColumnToCellIndex(row, sel.MinCol);
            var lastCellIdx  = GridColumnToCellIndex(row, sel.MaxCol);
            if (firstCellIdx < 0 || lastCellIdx < 0)
                return;
            _bus.Execute(new MergeCellsHorizontalCommand(blockIdx, sel.MinRow, firstCellIdx, lastCellIdx));
        }

        // Clear block selection and place caret in the surviving top-left cell.
        _cellBlockAnchor = null;
        _cellBlockFocus  = null;
        PlaceCaretInCell(blockIdx, sel.MinRow, sel.MinCol, 0, 0);
        InvalidateLayoutAndVisual();
    }

    /// <summary>
    /// Split the merged cell at the current caret position back into individual cells via
    /// <see cref="SplitCellCommand"/>. Handles both horizontal merges (GridSpan &gt; 1) and vertical
    /// merges (VerticalMerge = Restart). No-op when the caret is not in a table or the cell is not
    /// merged. Undoable.
    /// </summary>
    /// <param name="rows">Reserved for future subdivision; currently ignored (model splits to 1×N or N×1).</param>
    /// <param name="cols">Reserved for future subdivision; currently ignored.</param>
    public void SplitCurrentCell(int rows = 1, int cols = 1)
    {
        if (IsEditingLocked)
            return;

        if (_cellCaret is not { } cc)
            return;
        var blockIdx = cc.TableBlock;
        if (blockIdx < 0 || blockIdx >= _doc.Blocks.Count || _doc.Blocks[blockIdx] is not Table)
            return;

        // BH2: _cellCaret.Col is a GRID column; SplitCellCommand expects a CELL-LIST index.
        // Convert via GridColumnToCellIndex before issuing the command.
        var splitTable = (Table)_doc.Blocks[blockIdx];
        var splitCellIdx = GridColumnToCellIndex(splitTable.Rows[cc.Row], cc.Col);
        if (splitCellIdx < 0)
            return;
        _bus.Execute(new SplitCellCommand(blockIdx, cc.Row, splitCellIdx));
        // Re-place caret in the same cell (which is now split back to span=1).
        PlaceCaretInCell(blockIdx, cc.Row, cc.Col, 0, 0);
        InvalidateLayoutAndVisual();
    }

    // ── AV-TBL4: cell shading + per-edge border edit surface ─────────────────────────────────────

    /// <summary>
    /// Set the background shading of the caret cell, or of ALL cells in <see cref="SelectedCellRange"/>
    /// when a block selection is active.
    /// <para><paramref name="hexColor"/> is an RRGGBB hex string (e.g. <c>"#FFFF00"</c>) or null/empty
    /// to clear the fill.</para>
    /// Routed through <see cref="DocumentCommandBus"/> so it is undoable. No-op when the caret is not
    /// inside a table cell.
    /// </summary>
    public void SetCellShading(string? hexColor)
    {
        if (IsEditingLocked)
            return;

        if (SelectedCellRange is { } sel)
        {
            // Block selection: apply to every cell in the rectangle.
            if (sel.TableBlock < 0 || sel.TableBlock >= _doc.Blocks.Count
                || _doc.Blocks[sel.TableBlock] is not Table selTbl)
                return;
            for (var r = sel.MinRow; r <= sel.MaxRow; r++)
            {
                if (r >= selTbl.Rows.Count) break;
                var row = selTbl.Rows[r];
                // BL1/BL3: _cellCaret/SelectedCellRange use GRID columns; SetCellShadingCommand
                // expects CELL-LIST indices. Convert each grid column and dedupe so a merged cell
                // spanning multiple grid columns is only shaded once.
                var lastCellIdx = -1;
                for (var gridCol = sel.MinCol; gridCol <= sel.MaxCol; gridCol++)
                {
                    var cellIdx = GridColumnToCellIndex(row, gridCol);
                    if (cellIdx < 0) break; // beyond row's grid width
                    if (cellIdx == lastCellIdx) continue; // merged cell already processed
                    lastCellIdx = cellIdx;
                    _bus.Execute(new SetCellShadingCommand(sel.TableBlock, r, cellIdx, hexColor));
                }
            }
        }
        else if (_cellCaret is { } cc)
        {
            // Single caret cell.
            if (cc.TableBlock < 0 || cc.TableBlock >= _doc.Blocks.Count
                || _doc.Blocks[cc.TableBlock] is not Table ccTbl)
                return;
            // BL1: cc.Col is a GRID column; convert to cell-list index before issuing the command.
            var caretCellIdx = GridColumnToCellIndex(ccTbl.Rows[cc.Row], cc.Col);
            if (caretCellIdx < 0) return;
            _bus.Execute(new SetCellShadingCommand(cc.TableBlock, cc.Row, caretCellIdx, hexColor));
        }
        else
        {
            return; // Not in a table — no-op.
        }
        InvalidateLayoutAndVisual();
    }

    /// <summary>
    /// Set or clear border edge(s) on the caret cell or all cells in <see cref="SelectedCellRange"/>.
    /// <para>
    /// <paramref name="edges"/> selects which edges to apply:
    /// <list type="bullet">
    ///   <item><see cref="CellBorderEdges.All"/> — all four edges of every selected cell.</item>
    ///   <item><see cref="CellBorderEdges.Outside"/> — the outer boundary of the selected block
    ///     (top edge of top row, bottom edge of bottom row, left of left col, right of right col).</item>
    ///   <item><see cref="CellBorderEdges.Inside"/> — the shared inner edges of the selected block
    ///     (bottom edges of all but last row, right edges of all but last column).</item>
    ///   <item>Individual flags (<see cref="CellBorderEdges.Top"/> etc.) — applied to every selected cell.</item>
    /// </list>
    /// </para>
    /// Pass <paramref name="clearEdges"/> = true to remove the specified edges rather than set them.
    /// Routed through <see cref="DocumentCommandBus"/> — undoable. No-op outside a table.
    /// </summary>
    public void SetCellBorders(
        CellBorderEdges edges,
        string colorHex = "#000000",
        double widthPt = 0.5,
        BorderLineStyle style = BorderLineStyle.Single,
        bool clearEdges = false)
    {
        if (IsEditingLocked)
            return;

        int blockIdx;
        int minRow, maxRow, minCol, maxCol;

        if (SelectedCellRange is { } sel)
        {
            blockIdx = sel.TableBlock;
            minRow = sel.MinRow; maxRow = sel.MaxRow;
            minCol = sel.MinCol; maxCol = sel.MaxCol;
        }
        else if (_cellCaret is { } cc)
        {
            blockIdx = cc.TableBlock;
            minRow = maxRow = cc.Row;
            minCol = maxCol = cc.Col;
        }
        else
        {
            return; // Not in a table.
        }

        if (blockIdx < 0 || blockIdx >= _doc.Blocks.Count
            || _doc.Blocks[blockIdx] is not Table tbl)
            return;

        for (var r = minRow; r <= maxRow; r++)
        {
            if (r >= tbl.Rows.Count) break;
            var row = tbl.Rows[r];
            // BL2/BL3: minCol..maxCol are GRID columns; SetCellBordersCommand expects CELL-LIST
            // indices. Convert each grid column and dedupe merged cells.
            // Edge boundary resolution (Outside/Inside) stays in GRID space.
            // A merged cell spanning multiple grid columns must:
            //   - get Left  if its FIRST grid column == minCol (it touches the outer-left boundary)
            //   - get Right if its LAST  grid column == maxCol (it touches the outer-right boundary)
            //   - get Inside Right if its LAST grid column < maxCol (it has a shared right inner edge)
            // Track both firstGridCol and lastGridCol per merged cell for correct edge resolution.
            var lastCellIdx      = -1;
            int firstGridColForCell = -1;
            int lastGridColForCell  = -1;
            for (var gridCol = minCol; gridCol <= maxCol; gridCol++)
            {
                var cellIdx = GridColumnToCellIndex(row, gridCol);
                if (cellIdx < 0) break; // beyond row's grid width

                bool isNewCell = cellIdx != lastCellIdx;
                if (isNewCell)
                {
                    // Flush the previous merged cell using its first/last grid columns for edge checks.
                    if (lastCellIdx >= 0)
                    {
                        var flushedEdges = ResolveEdgesForMergedCell(
                            edges, r, firstGridColForCell, lastGridColForCell,
                            minRow, maxRow, minCol, maxCol);
                        if (flushedEdges != CellBorderEdges.None)
                            _bus.Execute(new SetCellBordersCommand(blockIdx, r, lastCellIdx, flushedEdges, style, colorHex, widthPt, clearEdges));
                    }
                    lastCellIdx         = cellIdx;
                    firstGridColForCell = gridCol;
                    lastGridColForCell  = gridCol;
                }
                else
                {
                    // Same merged cell — extend the last grid column it covers.
                    lastGridColForCell = gridCol;
                }
            }
            // Flush the final cell.
            if (lastCellIdx >= 0)
            {
                var finalEdges = ResolveEdgesForMergedCell(
                    edges, r, firstGridColForCell, lastGridColForCell,
                    minRow, maxRow, minCol, maxCol);
                if (finalEdges != CellBorderEdges.None)
                    _bus.Execute(new SetCellBordersCommand(blockIdx, r, lastCellIdx, finalEdges, style, colorHex, widthPt, clearEdges));
            }
        }
        InvalidateLayoutAndVisual();
    }

    // ── AV-TBL5: cell alignment (vertical + horizontal) ──────────────────────────────────────────

    /// <summary>
    /// Set the vertical alignment of the table cell(s) and the horizontal (paragraph) alignment
    /// of all paragraphs within those cells. Applies to:
    /// <list type="bullet">
    ///   <item>All cells in <see cref="SelectedCellRange"/> when a block selection is active.</item>
    ///   <item>The single caret cell otherwise.</item>
    /// </list>
    /// Routed through <see cref="DocumentCommandBus"/> as a grouped undo action. No-op when the
    /// caret is not inside a table cell.
    /// <para>
    /// NOTE — vertical render: the Avalonia table renderer currently top-anchors all cell content
    /// (ty = rowPageSpaceY + pad in <c>LayoutTablePaged</c>).  Horizontal alignment applies
    /// immediately through the existing paragraph-alignment render path.  Vertical centering/bottom
    /// positioning is a follow-up render task (AV-TBL5-VRENDER).
    /// </para>
    /// </summary>
    public void SetCaretCellAlignment(TableCellVerticalAlignment verticalAlignment, TextAlignment horizontalAlignment)
    {
        if (IsEditingLocked)
            return;

        if (SelectedCellRange is { } sel)
        {
            if (sel.TableBlock < 0 || sel.TableBlock >= _doc.Blocks.Count
                || _doc.Blocks[sel.TableBlock] is not Table selTbl)
                return;
            _bus.BeginUndoGroup();
            try
            {
                for (var r = sel.MinRow; r <= sel.MaxRow; r++)
                {
                    if (r >= selTbl.Rows.Count) break;
                    var row = selTbl.Rows[r];
                    // BL1/BL3: SelectedCellRange uses GRID columns; SetCellAlignmentCommand expects
                    // CELL-LIST indices. Convert and dedupe merged cells (same pattern as SetCellShading).
                    var lastCellIdx = -1;
                    for (var gridCol = sel.MinCol; gridCol <= sel.MaxCol; gridCol++)
                    {
                        var cellIdx = GridColumnToCellIndex(row, gridCol);
                        if (cellIdx < 0) break;
                        if (cellIdx == lastCellIdx) continue; // merged cell already processed
                        lastCellIdx = cellIdx;
                        _bus.Execute(new SetCellAlignmentCommand(sel.TableBlock, r, cellIdx, verticalAlignment, horizontalAlignment));
                    }
                }
            }
            finally
            {
                _bus.CommitUndoGroup("Set Cell Alignment");
            }
        }
        else if (_cellCaret is { } cc)
        {
            if (cc.TableBlock < 0 || cc.TableBlock >= _doc.Blocks.Count
                || _doc.Blocks[cc.TableBlock] is not Table ccTbl)
                return;
            // BL1: cc.Col is a GRID column; convert to cell-list index.
            var caretCellIdx = GridColumnToCellIndex(ccTbl.Rows[cc.Row], cc.Col);
            if (caretCellIdx < 0) return;
            _bus.Execute(new SetCellAlignmentCommand(cc.TableBlock, cc.Row, caretCellIdx, verticalAlignment, horizontalAlignment));
        }
        else
        {
            return; // Not in a table — no-op.
        }
        InvalidateLayoutAndVisual();
    }

    /// <summary>
    /// Translates an abstract edge selector (All/Outside/Inside/primitive flags) into the
    /// concrete set of primitive edge bits that apply to a specific cell at (row, col) within
    /// the selected block [minRow..maxRow] × [minCol..maxCol].
    /// </summary>
    private static CellBorderEdges ResolveEdgesForCell(
        CellBorderEdges edges,
        int row, int col,
        int minRow, int maxRow, int minCol, int maxCol)
    {
        // Expand composite selectors: All = all four primitive edges; treat Outside / Inside below.
        bool hasAll     = (edges & CellBorderEdges.All)     == CellBorderEdges.All;
        bool hasOutside = (edges & CellBorderEdges.Outside) == CellBorderEdges.Outside;
        bool hasInside  = (edges & CellBorderEdges.Inside)  != 0;
        bool hasTop     = (edges & CellBorderEdges.Top)     != 0;
        bool hasBottom  = (edges & CellBorderEdges.Bottom)  != 0;
        bool hasLeft    = (edges & CellBorderEdges.Left)    != 0;
        bool hasRight   = (edges & CellBorderEdges.Right)   != 0;

        // If All requested, set all four edge bits now.
        if (hasAll)
            return CellBorderEdges.Top | CellBorderEdges.Bottom | CellBorderEdges.Left | CellBorderEdges.Right;

        var result = CellBorderEdges.None;

        // Outside: the outer boundary of the selection block.
        if (hasOutside)
        {
            if (row == minRow) result |= CellBorderEdges.Top;
            if (row == maxRow) result |= CellBorderEdges.Bottom;
            if (col == minCol) result |= CellBorderEdges.Left;
            if (col == maxCol) result |= CellBorderEdges.Right;
        }

        // Inside: shared inner edges (bottom of each non-last row; right of each non-last col).
        if (hasInside)
        {
            if (row < maxRow) result |= CellBorderEdges.Bottom;
            if (col < maxCol) result |= CellBorderEdges.Right;
        }

        // Primitive edge bits applied to every cell in the selection.
        if (hasTop)    result |= CellBorderEdges.Top;
        if (hasBottom) result |= CellBorderEdges.Bottom;
        if (hasLeft)   result |= CellBorderEdges.Left;
        if (hasRight)  result |= CellBorderEdges.Right;

        return result;
    }

    /// <summary>
    /// Variant of <see cref="ResolveEdgesForCell"/> for cells that may span multiple grid columns
    /// (horizontally merged). Uses <paramref name="firstGridCol"/> for Left-boundary checks and
    /// <paramref name="lastGridCol"/> for Right-boundary / Inside-Right checks so that the outer
    /// left/right edges land on the correct boundary cell and the inside Right is suppressed for
    /// the rightmost physical cell in the selection.
    /// </summary>
    private static CellBorderEdges ResolveEdgesForMergedCell(
        CellBorderEdges edges,
        int row, int firstGridCol, int lastGridCol,
        int minRow, int maxRow, int minCol, int maxCol)
    {
        bool hasAll     = (edges & CellBorderEdges.All)     == CellBorderEdges.All;
        bool hasOutside = (edges & CellBorderEdges.Outside) == CellBorderEdges.Outside;
        bool hasInside  = (edges & CellBorderEdges.Inside)  != 0;
        bool hasTop     = (edges & CellBorderEdges.Top)     != 0;
        bool hasBottom  = (edges & CellBorderEdges.Bottom)  != 0;
        bool hasLeft    = (edges & CellBorderEdges.Left)    != 0;
        bool hasRight   = (edges & CellBorderEdges.Right)   != 0;

        if (hasAll)
            return CellBorderEdges.Top | CellBorderEdges.Bottom | CellBorderEdges.Left | CellBorderEdges.Right;

        var result = CellBorderEdges.None;

        if (hasOutside)
        {
            if (row == minRow)         result |= CellBorderEdges.Top;
            if (row == maxRow)         result |= CellBorderEdges.Bottom;
            if (firstGridCol == minCol) result |= CellBorderEdges.Left;   // leftmost grid col of this cell
            if (lastGridCol  == maxCol) result |= CellBorderEdges.Right;  // rightmost grid col of this cell
        }

        if (hasInside)
        {
            if (row < maxRow)          result |= CellBorderEdges.Bottom;
            if (lastGridCol < maxCol)  result |= CellBorderEdges.Right;   // has a shared right inner edge
        }

        if (hasTop)    result |= CellBorderEdges.Top;
        if (hasBottom) result |= CellBorderEdges.Bottom;
        if (hasLeft)   result |= CellBorderEdges.Left;
        if (hasRight)  result |= CellBorderEdges.Right;

        return result;
    }

    /// <summary>
    /// Removes the entire table block at <paramref name="blockIndex"/> from the document.
    /// No-op if the index is out of range or the block is not a <see cref="Table"/>.
    /// Undoable.
    /// </summary>
    public void DeleteTableBlock(int blockIndex)
    {
        if (blockIndex < 0 || blockIndex >= _doc.Blocks.Count) return;
        if (_doc.Blocks[blockIndex] is not Table) return;
        // Replace the single table block with an empty replacement list — effectively deleting it.
        _bus.Execute(new ReplaceBlocksCommand(blockIndex, 1, Array.Empty<Block>()));
        _cellCaret = null;
        _cellAnchor = null;
        _cellBlockAnchor = null;
        _cellBlockFocus = null;
        // BY3: re-anchor _caret to a valid block — if the deleted table was the last block (or
        // _caret.Block >= the new Blocks.Count), _caret now points past the document end and
        // subsequent body ops read the wrong block. ClampCaret() handles all edge cases
        // (empty doc, last-block deletion) exactly as Undo/Redo does.
        ClampCaret();
        InvalidateLayoutAndVisual();
    }

    // ── AV-TBLTAB: table-level formatting toggles ────────────────────────────────────────────────

    /// <summary>
    /// Toggles the <see cref="TableFormatting.HeaderRow"/> flag on the table containing the caret.
    /// No-op outside a table. Undoable via the document command bus.
    /// </summary>
    public void ToggleTableHeaderRow() =>
        UpdateCaretTableFormatting(formatting => formatting with { HeaderRow = !formatting.HeaderRow });

    /// <summary>
    /// Toggles the <see cref="TableFormatting.BandedRows"/> flag on the table containing the caret.
    /// No-op outside a table. Undoable via the document command bus.
    /// </summary>
    public void ToggleBandedRows() =>
        UpdateCaretTableFormatting(formatting => formatting with { BandedRows = !formatting.BandedRows });

    public void ToggleTableRepeatHeaderRow() =>
        UpdateCaretTableFormatting(formatting => formatting with { RepeatHeaderRow = !formatting.RepeatHeaderRow });

    public void ToggleTableLastRow() =>
        UpdateCaretTableFormatting(formatting => formatting with { LastRow = !formatting.LastRow });

    public void ToggleTableFirstColumn() =>
        UpdateCaretTableFormatting(formatting => formatting with { FirstColumn = !formatting.FirstColumn });

    public void ToggleTableLastColumn() =>
        UpdateCaretTableFormatting(formatting => formatting with { LastColumn = !formatting.LastColumn });

    public void ToggleTableBandedColumns() =>
        UpdateCaretTableFormatting(formatting => formatting with { BandedColumns = !formatting.BandedColumns });

    private void UpdateCaretTableFormatting(Func<TableFormatting, TableFormatting> update)
    {
        if (IsEditingLocked || _cellCaret is not { } cc)
            return;
        if (cc.TableBlock < 0 || cc.TableBlock >= _doc.Blocks.Count
            || _doc.Blocks[cc.TableBlock] is not Table tbl)
            return;

        var newFmt = update(tbl.Formatting);
        _bus.Execute(new SetTableFormattingCommand(cc.TableBlock, newFmt));
        InvalidateLayoutAndVisual();
    }

    public void SplitTable()
    {
        if (IsEditingLocked || _cellCaret is not { } cc)
            return;
        if (cc.TableBlock < 0 || cc.TableBlock >= _doc.Blocks.Count
            || _doc.Blocks[cc.TableBlock] is not Table table)
            return;

        if (TableLayoutOperations.TryBuildSplitReplacement(table, cc.Row, out var replacement))
        {
            _bus.Execute(new ReplaceBlocksCommand(cc.TableBlock, 1, replacement));
            _cellCaret = null;
            _cellAnchor = null;
            _cellBlockAnchor = null;
            _cellBlockFocus = null;
            ClampCaret();
            InvalidateLayoutAndVisual();
        }
    }

    public void DistributeTableRows()
    {
        if (IsEditingLocked || CaretTable() is not { } table)
            return;
        if (TableLayoutOperations.DistributeRows(table))
            InvalidateLayoutAndVisual();
    }

    public void DistributeTableColumns()
    {
        if (IsEditingLocked || CaretTable() is not { } table)
            return;
        if (TableLayoutOperations.DistributeColumns(table))
            InvalidateLayoutAndVisual();
    }

    public void SetTableAutoFit(AutoFitMode mode)
    {
        if (IsEditingLocked || CaretTable() is not { } table)
            return;
        if (TableLayoutOperations.SetAutoFit(table, mode))
            InvalidateLayoutAndVisual();
    }

    public void SetCaretCellTextDirection(CellTextDirection direction)
    {
        if (IsEditingLocked || _cellCaret is not { } cc)
            return;
        if (cc.TableBlock < 0 || cc.TableBlock >= _doc.Blocks.Count
            || _doc.Blocks[cc.TableBlock] is not Table table)
            return;

        var cellIndex = GridColumnToCellIndex(table.Rows[cc.Row], cc.Col);
        if (TableLayoutOperations.SetCellTextDirection(table, cc.Row, cellIndex, direction))
            InvalidateLayoutAndVisual();
    }

    public (Table Table, int RowIndex, int ColumnIndex)? CaretTableCell()
    {
        if (_cellCaret is not { } cc)
            return null;
        if (cc.TableBlock < 0 || cc.TableBlock >= _doc.Blocks.Count
            || _doc.Blocks[cc.TableBlock] is not Table table)
            return null;

        var cellIndex = GridColumnToCellIndex(table.Rows[cc.Row], cc.Col);
        return cellIndex < 0 ? null : (table, cc.Row, cellIndex);
    }

    public ModelTableContext? CaretTableContext()
    {
        if (CaretTableCell() is not { } caret)
            return null;

        var row = caret.RowIndex >= 0 && caret.RowIndex < caret.Table.Rows.Count
            ? caret.Table.Rows[caret.RowIndex]
            : null;
        var cell = row is not null && caret.ColumnIndex >= 0 && caret.ColumnIndex < row.Cells.Count
            ? row.Cells[caret.ColumnIndex]
            : null;
        return new ModelTableContext(caret.Table, row, cell);
    }

    public void ApplyTableProperties(TablePropertiesValues values)
    {
        if (IsEditingLocked || CaretTableContext() is not { } context)
            return;

        TablePropertiesDialogPlanner.ApplyValues(context, values);
        InvalidateLayoutAndVisual();
    }

    public void InsertTableFormula(TableFormulaField formula)
    {
        if (IsEditingLocked || _cellCaret is not { } cc)
            return;
        if (cc.TableBlock < 0 || cc.TableBlock >= _doc.Blocks.Count
            || _doc.Blocks[cc.TableBlock] is not Table table)
            return;

        var cellIndex = GridColumnToCellIndex(table.Rows[cc.Row], cc.Col);
        if (cellIndex < 0)
            return;

        var run = TableLayoutOperations.BuildFormulaRun(table, cc.Row, cellIndex, formula);
        var targetOffset = cc.Offset;
        _bus.Execute(new ReplaceCellParagraphRunsCommand(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx, paragraph =>
            InsertRunAtOffset(paragraph, targetOffset, run)));
        var newOffset = targetOffset + run.Text.Length;
        _cellCaret = cc with { Offset = newOffset };
        _cellAnchor = _cellCaret;
        _caret = new DocPosition(cc.TableBlock, FindCellGlyphOffset(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx, newOffset));
        _selectionAnchor = _caret;
        InvalidateLayoutAndVisual();
    }

    private Table? CaretTable()
    {
        if (_cellCaret is not { } cc)
            return null;
        if (cc.TableBlock < 0 || cc.TableBlock >= _doc.Blocks.Count)
            return null;
        return _doc.Blocks[cc.TableBlock] as Table;
    }

    // ── AV-TBL2: cross-cell rectangular selection ────────────────────────────────────────────────

    /// <summary>
    /// The rectangular cell range currently selected by a cross-cell drag, or null when only a
    /// single cell (or body text) is active. Returns (TableBlock, MinRow, MinCol, MaxRow, MaxCol)
    /// with rows and cols clamped to the inclusive bounds of the anchor → focus rectangle.
    /// Ribbon commands (delete/merge/format) should check this before falling back to
    /// <see cref="CellCaretInfo"/>.
    /// </summary>
    public (int TableBlock, int MinRow, int MinCol, int MaxRow, int MaxCol)? SelectedCellRange
    {
        get
        {
            if (_cellBlockAnchor is not { } a || _cellBlockFocus is not { } f)
                return null;
            if (a.TableBlock != f.TableBlock)
                return null;
            var raw = (a.TableBlock,
                Math.Min(a.Row, f.Row), Math.Min(a.Col, f.Col),
                Math.Max(a.Row, f.Row), Math.Max(a.Col, f.Col));
            // BF5: expand the rectangle to fully cover any merged cells that straddle the boundary.
            return ExpandForMergedCells(raw);
        }
    }

    /// <summary>
    /// BY1: Returns (lastRow, lastGridCol) — the inclusive zero-based bounds of the table at
    /// <paramref name="tableBlock"/>. Used by select-table/row/column to clamp the focus
    /// instead of passing int.MaxValue (which causes an overflow loop in ExpandForMergedCells).
    /// Returns (0, 0) when the table is empty or the block is not a table.
    /// </summary>
    internal (int LastRow, int LastGridCol) GetTableBounds(int tableBlock)
    {
        if (tableBlock < 0 || tableBlock >= _doc.Blocks.Count) return (0, 0);
        if (_doc.Blocks[tableBlock] is not Table tbl) return (0, 0);
        var lastRow = Math.Max(0, tbl.Rows.Count - 1);
        // Max grid width = widest row measured by summing cell GridSpans.
        var maxGridWidth = tbl.Rows.Count == 0 ? 1
            : tbl.Rows.Max(row => row.Cells.Sum(c => Math.Max(1, c.GridSpan)));
        return (lastRow, Math.Max(0, maxGridWidth - 1));
    }

    /// <summary>
    /// Programmatically set the cross-cell selection anchor and focus for tests and external callers.
    /// Both cells must be in the same table block.
    /// </summary>
    public void SetCellBlockSelection(int tableBlock, int anchorRow, int anchorCol, int focusRow, int focusCol)
    {
        // BY1 defensive clamp: guard against callers passing int.MaxValue (or any value beyond the
        // actual table bounds), which would cause ExpandForMergedCells to loop forever via integer
        // overflow (r++ on int.MaxValue → int.MinValue, so r <= maxRow is always true).
        if (tableBlock >= 0 && tableBlock < _doc.Blocks.Count
            && _doc.Blocks[tableBlock] is Table tblDefensive)
        {
            var (clampLastRow, clampLastCol) = GetTableBounds(tableBlock);
            anchorRow = Math.Clamp(anchorRow, 0, clampLastRow);
            anchorCol = Math.Clamp(anchorCol, 0, clampLastCol);
            focusRow  = Math.Clamp(focusRow,  0, clampLastRow);
            focusCol  = Math.Clamp(focusCol,  0, clampLastCol);
            _ = tblDefensive; // suppress unused-variable warning
        }
        _cellBlockAnchor = (tableBlock, anchorRow, anchorCol);
        _cellBlockFocus  = (tableBlock, focusRow,  focusCol);
        // Clear single-cell text selection state to avoid ambiguity.
        _cellCaret  = null;
        _cellAnchor = null;
        _selectionAnchor = null;
        InvalidateVisual();
    }

    /// <summary>
    /// Sets a cell selection anchor independently of the caret — used by tests to simulate
    /// a drag selection inside a cell without pointer events. The anchor stays at
    /// (anchorOffset) while the caret is separately at its current position.
    /// </summary>
    internal void SetCellSelectionAnchorForTest(int tableBlockIndex, int row, int col, int paraIdx, int anchorOffset)
    {
        if (_laidOutWidth < 0)
            Relayout(FallbackWidth);
        _cellAnchor = (tableBlockIndex, row, col, paraIdx, anchorOffset);
        // Keep _selectionAnchor non-equal to _caret so NormalizedSelection returns non-null.
        // Use an offset that differs from _caret so the body selection detection picks it up.
        _selectionAnchor = new DocPosition(tableBlockIndex, anchorOffset);
    }

    // ---- Test-only layout introspection (internal — visible to FreeW.App.Avalonia.Tests) ---------

    /// <summary>
    /// Returns a lightweight snapshot of placed glyphs for the given block suitable for layout
    /// tests.  Each tuple is (Ch, X, W, Y, LineHeight, IsSubscript) for non-sentinel chars.
    /// Only available to the test assembly via InternalsVisibleTo.
    /// </summary>
    internal IReadOnlyList<(char Ch, double X, double W, double Y, double LineHeight, bool IsSubscript)>
        GetPlacedForBlock(int blockIndex) =>
            _placed
                .Where(p => p.Block == blockIndex && !p.Sentinel)
                .Select(p => (p.Ch, p.X, p.W, p.Y, p.LineHeight,
                              p.Fmt.VerticalAlign == VerticalAlign.Subscript))
                .ToList();

    /// <summary>
    /// AV-TAB: Returns placed glyphs for block 0 including tab characters for test introspection.
    /// Each tuple: (Ch, X, W) — non-sentinels only.
    /// </summary>
    internal IReadOnlyList<(char Ch, double X, double W)> GetBodyTabPlaced(int blockIndex) =>
        _placed
            .Where(p => p.Block == blockIndex && !p.Sentinel)
            .Select(p => (p.Ch, p.X, p.W))
            .ToList();

    /// <summary>AV-TAB: Leader spans emitted during layout. For tests.</summary>
    internal IReadOnlyList<(double X1, double X2, double Y, double LineHeight, TabLeader Leader)> TabLeaderSpans
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _tabLeaderSpans.Select(s => (s.X1, s.X2, s.Y, s.LineHeight, s.Leader)).ToList();
        }
    }

    /// <summary>
    /// Returns placed glyphs for a specific table cell and paragraph — including sentinels.
    /// Suitable for BE1/BE2 layout tests. Only available to the test assembly.
    /// Tuple: (Ch, X, Y, LineHeight, Sentinel, CellParaOffset).
    /// </summary>
    internal IReadOnlyList<(char Ch, double X, double Y, double LineHeight, bool Sentinel, int ParaOffset)>
        GetCellPlaced(int blockIndex, int row, int col, int paraIdx) =>
            _placed
                .Where(p => p.Block == blockIndex && p.CellRow == row && p.CellCol == col && p.CellParaIdx == paraIdx)
                .Select(p => (p.Ch, p.X, p.Y, p.LineHeight, p.Sentinel, p.CellParaOffset))
                .ToList();

    // ── AV-COL: column layout introspection for tests ─────────────────────────────────────────────

    /// <summary>
    /// Number of body-text columns used in the current layout.
    /// 1 when single-column or in Web/Draft modes; matches PageSettings.ColumnCount for multi-column.
    /// </summary>
    internal int LayoutColumnCount
    {
        get { if (_laidOutWidth < 0) Relayout(FallbackWidth); return _colCount; }
    }

    /// <summary>
    /// Width of each equal column in the current layout, in DIP.
    /// Equal to _contentWidth when single-column.
    /// </summary>
    internal double LayoutColumnWidth
    {
        get { if (_laidOutWidth < 0) Relayout(FallbackWidth); return _colWidth; }
    }

    /// <summary>
    /// Gap between adjacent columns in the current layout, in DIP.
    /// Zero when single-column.
    /// </summary>
    internal double LayoutColumnGap
    {
        get { if (_laidOutWidth < 0) Relayout(FallbackWidth); return _colGap; }
    }

    /// <summary>
    /// Returns the X-band [left, left+width) for the given 0-based column index in the current layout.
    /// Used by tests to verify that each glyph's X coordinate falls within the correct column band.
    /// </summary>
    internal (double Left, double Width) LayoutColumnBand(int colIndex)
    {
        if (_laidOutWidth < 0) Relayout(FallbackWidth);
        var left = _contentLeft + colIndex * (_colWidth + _colGap);
        return (left, _colWidth);
    }

    /// <summary>
    /// Returns the current caret position as (Block, Offset).
    /// Exposed internally for navigation regression tests (ZZ1 and similar).
    /// </summary>
    internal (int Block, int Offset) CaretPosition => (_caret.Block, _caret.Offset);

    // ── AV-LIST: test helpers ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Place the body caret at the given block and character offset.
    /// Exposed for AV-LIST unit tests (simulates cursor positioning without pointer events).
    /// </summary>
    internal void MoveCaretToBlock(int blockIdx, int offset)
    {
        _cellCaret = null;
        _cellAnchor = null;
        _caret = new DocPosition(blockIdx, offset);
        _selectionAnchor = _caret;
    }

    /// <summary>
    /// Set a multi-paragraph selection for testing: anchor at (anchorBlock, anchorOffset),
    /// caret at (caretBlock, caretOffset). The selection direction follows Word convention
    /// (anchor is where the selection started, caret is where it ends).
    /// Exposed for BS4 / AV-LIST unit tests.
    /// </summary>
    internal void SetSelectionRangePublic(int anchorBlock, int anchorOffset, int caretBlock, int caretOffset)
    {
        _cellCaret = null;
        _cellAnchor = null;
        _selectionAnchor = new DocPosition(anchorBlock, anchorOffset);
        _caret = new DocPosition(caretBlock, caretOffset);
    }

    /// <summary>
    /// Trigger an Enter key (InsertParagraphBreak) programmatically.
    /// Exposed for AV-LIST unit tests.
    /// </summary>
    internal void InsertParagraphBreakPublic() => InsertParagraphBreak();

    /// <summary>Trigger a Backspace programmatically. Exposed for AV-TRACKEDIT unit tests.</summary>
    internal void BackspacePublic() => Backspace();

    /// <summary>Trigger a forward Delete programmatically. Exposed for AV-TRACKEDIT unit tests.</summary>
    internal void DeleteForwardPublic() => DeleteForward();

    /// <summary>
    /// Invoke the list Tab/Shift-Tab handler and return whether it consumed the key.
    /// Exposed for AV-LIST unit tests.
    /// </summary>
    internal bool ListTabAtItemStartPublic(bool shift) => ListTabAtItemStart(shift);

    /// <summary>
    /// Invoke the Backspace-outdent list handler and return whether it consumed the key.
    /// Exposed for AV-LIST unit tests.
    /// </summary>
    internal bool BackspaceOutdentListItemPublic() => BackspaceOutdentListItem();

    /// <summary>
    /// Return the sequential list number that would be rendered for block <paramref name="blockIdx"/>,
    /// by walking the document model the same way the layout loop does (render-time numbering).
    /// For Number lists returns the per-level counter at the paragraph's level.
    /// For MultiLevel lists returns the accumulated dotted level counter (e.g. 1 for "1.", 11 for "1.1.").
    /// Returns 0 for bullet or non-list paragraphs.
    /// Exposed for AV-LIST unit tests.
    /// </summary>
    internal int GetListNumberForBlockPublic(int blockIdx)
    {
        var marker = GetListMarkerForBlockPublic(blockIdx);
        if (marker is null) return 0;
        // Extract the last numeric segment before the trailing dot (e.g. "1.2." → 2, "3." → 3).
        var parts = marker.TrimEnd('.').Split('.');
        return parts.Length > 0 && int.TryParse(parts[^1], out var n) ? n : 0;
    }

    /// <summary>
    /// Return the full marker string that would be rendered for block <paramref name="blockIdx"/>,
    /// using the same per-level counter logic as the layout loop.
    /// Returns <c>null</c> for bullet or non-list paragraphs.
    /// Exposed for AV-LIST unit tests (BS1/BS2/BS3).
    /// </summary>
    internal string? GetListMarkerForBlockPublic(int blockIdx)
    {
        // Re-layout so _markers are fresh.
        if (_laidOutWidth < 0)
            Relayout(FallbackWidth);

        const int MaxListDepth = 9;
        var levelCounters = new int[MaxListDepth];
        for (int i = 0; i < _doc.Blocks.Count; i++)
        {
            if (_doc.Blocks[i] is not Paragraph p)
            {
                // BT1 fix: Table and other non-Paragraph blocks (read-only, etc.) do NOT reset
                // the numbered-list counters — the render loop passes them with levelCounters
                // untouched (see LayoutTablePaged / LayoutReadOnlyBlockPaged branches).
                // Word numbering continues across an intervening table; the helper must match.
                continue;
            }

            // BW1: mirror the render loop's inline-object detection (~1767-1789).
            // A paragraph that routes through LayoutImageParagraphPaged (has an inline image)
            // or LayoutInlineObjectParagraphPaged (has an inline chart/WordArt/SmartArt) resets
            // levelCounters and is treated as non-list — exactly what we must replicate here so
            // the helper and render agree for ALL paragraph kinds.
            var hasInlineImage   = p.Runs.Any(r => r.Image    is { IsFloating: false });
            var hasInlineChart   = p.Runs.Any(r => r.Chart    is { IsFloating: false });
            var hasInlineWordArt = p.Runs.Any(r => r.WordArt  is { IsFloating: false });
            var hasInlineSmArt   = p.Runs.Any(r => r.SmartArt is { IsFloating: false });
            if (hasInlineImage || hasInlineChart || hasInlineWordArt || hasInlineSmArt)
            {
                // Render loop resets all counters and skips list numbering for this paragraph.
                Array.Clear(levelCounters, 0, MaxListDepth);
                if (i == blockIdx) return null;
                continue;
            }

            var kind = p.Formatting.ListKind;
            if (kind is ListKind.Number or ListKind.MultiLevel)
            {
                var level = Math.Clamp(p.Formatting.ListLevel, 0, MaxListDepth - 1);
                levelCounters[level]++;
                for (var deeper = level + 1; deeper < MaxListDepth; deeper++)
                    levelCounters[deeper] = 0;

                if (i == blockIdx)
                {
                    if (kind is ListKind.MultiLevel)
                    {
                        var sb = new System.Text.StringBuilder();
                        for (var ancestor = 0; ancestor <= level; ancestor++)
                        {
                            var value = levelCounters[ancestor] == 0 ? 1 : levelCounters[ancestor];
                            sb.Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('.');
                        }
                        return sb.ToString();
                    }
                    else
                    {
                        return $"{levelCounters[level]}.";
                    }
                }
            }
            else if (kind is ListKind.None)
            {
                // Non-list paragraph: the numbered run has ended, reset all counters.
                Array.Clear(levelCounters, 0, MaxListDepth);
                if (i == blockIdx) return null;
            }
            else
            {
                // BS3: Bullet does NOT reset numbered level counters.
                if (i == blockIdx) return null; // bullet paragraphs have no number marker
            }
        }
        return null;
    }

    /// <summary>
    /// Simulates pressing Down (+1) or Up (-1) arrow from the current caret position.
    /// Exposed internally so regression tests can assert that vertical navigation reaches
    /// a tall inline object (ZZ1).
    /// </summary>
    internal void TestMoveCaretVertical(int direction) => MoveCaretVertical(direction, extend: false);

    /// <summary>
    /// Simulates a pointer click at <paramref name="point"/> and returns the resolved
    /// (Block, Offset) if TryHitTest finds a match, or null if not.
    /// Exposed internally for hit-test regression tests (ZZ1).
    /// </summary>
    internal (int Block, int Offset)? TestHitTest(Point point) =>
        TryHitTest(point, out var pos) ? (pos.Block, pos.Offset) : null;

    /// <summary>
    /// Number of floating images collected during the last layout pass.
    /// Tests use this to verify that floating images are tracked separately from inline images.
    /// </summary>
    public int FloatingImageCount
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _floatingImages.Count;
        }
    }

    /// <summary>
    /// Returns a snapshot of the floating-image rects (page-space, in draw order) collected during
    /// the last layout pass.  Tests use this to verify position resolution from FloatingPlacement.
    /// </summary>
    public IReadOnlyList<(Rect Rect, bool BehindText, int ZOrder)> FloatingImageRects
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _floatingImages.Select(fi => (fi.Rect, fi.BehindText, fi.ZOrder)).ToList();
        }
    }

    /// <summary>
    /// Number of floating shapes collected during the last layout pass.
    /// Tests use this to verify that floating shapes are tracked separately from inline content.
    /// </summary>
    public int FloatingShapeCount
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _floatingShapes.Count;
        }
    }

    /// <summary>
    /// Returns a snapshot of the floating-shape rects (page-space, in draw order) collected during
    /// the last layout pass. Tests use this to verify position resolution, z-order, fill and outline.
    /// </summary>
    public IReadOnlyList<(Rect Rect, bool BehindText, int ZOrder, ShapeKind Kind, bool HasFill, bool HasOutline, string? Text)>
        FloatingShapeRects
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _floatingShapes
                .Select(sd => (sd.Rect, sd.BehindText, sd.ZOrder, sd.Kind,
                               sd.FillBrush is not null,
                               sd.OutlinePen is not null,
                               sd.Text))
                .ToList();
        }
    }

    // ── FO3 introspection properties ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Test-facing snapshot of shared drawing-object effect intent carried by the Avalonia renderer.
    /// The renderer owns platform brush/pen conversion, but not the capability truth.
    /// </summary>
    public IReadOnlyList<string> FloatingShapeEffectSummaries
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _floatingShapes.Select(sd => sd.Effects.Summary).ToList();
        }
    }

    /// <summary>
    /// Test-facing snapshot of grouped child drawing-object effect intent carried by the Avalonia renderer.
    /// </summary>
    public IReadOnlyList<string> FloatingGroupChildEffectSummaries
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _floatingGroups
                .SelectMany(group => group.Children)
                .Where(child => child.Kind == FloatingGroupChildData.ChildKind.Shape
                    && child.Shape?.Effects.HasAny == true)
                .Select(child =>
                    "GroupChild"
                    + child.ChildIndex.ToString(CultureInfo.InvariantCulture)
                    + ":Shape:"
                    + child.Shape!.Effects.Summary.Replace(", ", "+", StringComparison.Ordinal))
                .ToList();
        }
    }

    /// <summary>Number of floating charts collected during the last layout pass.</summary>
    public int FloatingChartCount
    {
        get { if (_laidOutWidth < 0) Relayout(FallbackWidth); return _floatingCharts.Count; }
    }

    /// <summary>Snapshot of floating chart rects for tests (rect, behind-text, z-order, kind, title).</summary>
    public IReadOnlyList<(Rect Rect, bool BehindText, int ZOrder, ChartKind Kind, string? Title)> FloatingChartRects
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _floatingCharts.Select(c => (c.Rect, c.BehindText, c.ZOrder, c.Kind, c.Title)).ToList();
        }
    }

    /// <summary>
    /// Extended snapshot of floating chart data for tests — includes Categories and Series count.
    /// (Rect, BehindText, ZOrder, Kind, Title, Categories, SeriesCount)
    /// </summary>
    public IReadOnlyList<(Rect Rect, bool BehindText, int ZOrder, ChartKind Kind, string? Title,
        IReadOnlyList<string> Categories, int SeriesCount)> FloatingChartDataSnapshots
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _floatingCharts.Select(c =>
                (c.Rect, c.BehindText, c.ZOrder, c.Kind, c.Title,
                 (IReadOnlyList<string>)c.Categories.AsReadOnly(),
                 c.Series.Count)).ToList();
        }
    }

    /// <summary>Number of floating WordArt objects collected during the last layout pass.</summary>
    public int FloatingWordArtCount
    {
        get { if (_laidOutWidth < 0) Relayout(FallbackWidth); return _floatingWordArts.Count; }
    }

    /// <summary>Snapshot of floating WordArt rects for tests (rect, behind-text, z-order, text, style).</summary>
    public IReadOnlyList<(Rect Rect, bool BehindText, int ZOrder, string Text, WordArtStyle Style)> FloatingWordArtRects
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _floatingWordArts.Select(w => (w.Rect, w.BehindText, w.ZOrder, w.Text, w.Style)).ToList();
        }
    }

    /// <summary>Number of floating SmartArt diagrams collected during the last layout pass.</summary>
    public int FloatingSmartArtCount
    {
        get { if (_laidOutWidth < 0) Relayout(FallbackWidth); return _floatingSmartArts.Count; }
    }

    /// <summary>Snapshot of floating SmartArt rects for tests (rect, behind-text, z-order, kind, node count).</summary>
    public IReadOnlyList<(Rect Rect, bool BehindText, int ZOrder, SmartArtKind Kind, int NodeCount,
        string? FirstFillHex, string? FirstBorderHex, double BorderThickness, double CornerRadius,
        double ShadowOpacity, double ShadowBlur, double ShadowDepth, string? FirstConnectorHex)> FloatingSmartArtRects
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _floatingSmartArts.Select(s =>
            {
                var first = s.NodePlans.FirstOrDefault();
                return (
                    s.Rect,
                    s.BehindText,
                    s.ZOrder,
                    s.Kind,
                    s.NodeTexts.Count,
                    first?.FillHex,
                    first?.BorderHex,
                    first?.BorderThickness ?? 0,
                    first?.CornerRadius ?? 0,
                    first?.ShadowOpacity ?? 0,
                    first?.ShadowBlur ?? 0,
                    first?.ShadowDepth ?? 0,
                    first?.ConnectorHex);
            }).ToList();
        }
    }

    /// <summary>Number of floating drawing groups collected during the last layout pass.</summary>
    public int FloatingGroupCount
    {
        get { if (_laidOutWidth < 0) Relayout(FallbackWidth); return _floatingGroups.Count; }
    }

    /// <summary>Snapshot of floating group rects for tests (rect, behind-text, z-order, child count).</summary>
    public IReadOnlyList<(Rect Rect, bool BehindText, int ZOrder, int ChildCount)> FloatingGroupRects
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _floatingGroups.Select(g => (g.Rect, g.BehindText, g.ZOrder, g.Children.Count)).ToList();
        }
    }

    // ── FO4 introspection properties (inline objects) ────────────────────────────────────────────────

    /// <summary>Number of inline (non-floating) charts laid out in the last layout pass.</summary>
    public int InlineChartCount
    {
        get { if (_laidOutWidth < 0) Relayout(FallbackWidth); return _inlineCharts.Count; }
    }

    /// <summary>Snapshot of inline chart rects for tests (rect, kind, title).</summary>
    public IReadOnlyList<(Rect Rect, ChartKind Kind, string? Title)> InlineChartRects
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _inlineCharts.Select(c => (c.Rect, c.Kind, c.Title)).ToList();
        }
    }

    /// <summary>Number of inline (non-floating) WordArt objects laid out in the last layout pass.</summary>
    public int InlineWordArtCount
    {
        get { if (_laidOutWidth < 0) Relayout(FallbackWidth); return _inlineWordArts.Count; }
    }

    /// <summary>Snapshot of inline WordArt rects for tests (rect, text, style).</summary>
    public IReadOnlyList<(Rect Rect, string Text, WordArtStyle Style)> InlineWordArtRects
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _inlineWordArts.Select(w => (w.Rect, w.Text, w.Style)).ToList();
        }
    }

    /// <summary>Number of inline (non-floating) SmartArt diagrams laid out in the last layout pass.</summary>
    public int InlineSmartArtCount
    {
        get { if (_laidOutWidth < 0) Relayout(FallbackWidth); return _inlineSmartArts.Count; }
    }

    /// <summary>Snapshot of inline SmartArt rects for tests (rect, kind, node count).</summary>
    public IReadOnlyList<(Rect Rect, SmartArtKind Kind, int NodeCount,
        string? FirstFillHex, string? FirstBorderHex, double BorderThickness, double CornerRadius,
        double ShadowOpacity, double ShadowBlur, double ShadowDepth, string? FirstConnectorHex)> InlineSmartArtRects
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _inlineSmartArts.Select(s =>
            {
                var first = s.NodePlans.FirstOrDefault();
                return (
                    s.Rect,
                    s.Kind,
                    s.NodeTexts.Count,
                    first?.FillHex,
                    first?.BorderHex,
                    first?.BorderThickness ?? 0,
                    first?.CornerRadius ?? 0,
                    first?.ShadowOpacity ?? 0,
                    first?.ShadowBlur ?? 0,
                    first?.ShadowDepth ?? 0,
                    first?.ConnectorHex);
            }).ToList();
        }
    }

    // ── AV-WRAP: wrap-exclusion introspection for tests ──────────────────────────────────────────────

    /// <summary>
    /// Number of wrap-exclusion zones registered in the current layout pass.
    /// Only Square/Tight/TopAndBottom floats contribute; Behind/InFront are excluded.
    /// </summary>
    internal int WrapExclusionCount
    {
        get { if (_laidOutWidth < 0) Relayout(FallbackWidth); return _wrapExclusions.Count; }
    }

    /// <summary>Snapshot of wrap-exclusion zones (page-space rect + wrapping mode) for tests.</summary>
    internal IReadOnlyList<(Rect Rect, ImageWrapping Wrapping)> WrapExclusionZones
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _wrapExclusions
                .Select(zone => (ToAvaloniaRect(zone.Rect), zone.Wrapping))
                .ToList();
        }
    }

    // ── AV-COL-NONTXT: inline-image and table-cell rect introspection for column-layout tests ──────────

    /// <summary>Snapshot of inline (non-floating) image rects — multi-column X-band tests.</summary>
    internal IReadOnlyList<Rect> InlineImageRects
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _images.Select(i => i.Rect).ToList();
        }
    }

    /// <summary>Snapshot of table cell rects (Rect, Block, Row, Col) — multi-column X-band tests.</summary>
    internal IReadOnlyList<(Rect Rect, int Block, int Row, int Col)> TableCellRects
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _cellHits.ToList();
        }
    }

    // ── XX1 draw-order introspection (tests only) ────────────────────────────────────────────────────

    /// <summary>Merged BehindText floating-object draw order (ZOrder, type) — verifies XX1 interleave.</summary>
    public IReadOnlyList<(int ZOrder, string TypeTag)> MergedBehindDrawOrder
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return DocumentViewLayoutPlanner
                .BuildFloatingObjectDrawOrder(_floatingSnapshots, behindText: true)
                .Select(snapshot => (snapshot.ZOrderIndex, snapshot.TypeTag))
                .ToList();
        }
    }

    /// <summary>Merged in-front floating-object draw order (ZOrder, type).</summary>
    public IReadOnlyList<(int ZOrder, string TypeTag)> MergedFrontDrawOrder
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return DocumentViewLayoutPlanner
                .BuildFloatingObjectDrawOrder(_floatingSnapshots, behindText: false)
                .Select(snapshot => (snapshot.ZOrderIndex, snapshot.TypeTag))
                .ToList();
        }
    }

    // ── HF: header/footer render introspection for tests ─────────────────────────────────────────────

    /// <summary>
    /// Snapshot of pre-computed header/footer render items from the last layout pass.
    /// Each entry: (Text, PageSpaceY, Alignment). Tests use this to verify that items
    /// appear in the correct margin bands and carry the right field-resolved text.
    /// </summary>
    internal IReadOnlyList<(string Text, double Y, TextAlignment Alignment)> HeaderFooterItems
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _headerFooterItems
                .Where(i => !string.IsNullOrEmpty(i.Text)) // AV-HFEDIT: skip empty editable-region placeholders
                .Select(i => (i.Text, i.Y, i.Alignment))
                .ToList();
        }
    }

    /// <summary>
    /// Extended snapshot of pre-computed header/footer render items including the absolute page-space X
    /// coordinate. Tab-stop-positioned items have Alignment=Left and X = the resolved stop position;
    /// paragraph-aligned items have X = _contentLeft (the alignment offset is applied at draw time).
    /// Used by AV-POLISH tab-stop tests.
    /// </summary>
    internal IReadOnlyList<(string Text, double X, double Y, TextAlignment Alignment, double AvailableWidth)> HeaderFooterItemsFull
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _headerFooterItems
                .Where(i => !string.IsNullOrEmpty(i.Text)) // AV-HFEDIT: skip empty editable-region placeholders
                .Select(i => (i.Text, i.X, i.Y, i.Alignment, i.AvailableWidth))
                .ToList();
        }
    }

    // ── AV-NOTERENDER: footnote/endnote render introspection for tests ───────────────────────────────

    /// <summary>
    /// Snapshot of pre-computed footnote/endnote render items from the last layout pass.
    /// Each entry: (Text, PageSpaceX, PageSpaceY, IsNumberMarker). The number-marker items carry the
    /// note's number (a superscript-formatted prefix); the remaining items are the wrapped note text.
    /// Tests verify the numbered text appears at the right page-space position and matches the body
    /// reference numbers.
    /// </summary>
    internal IReadOnlyList<(string Text, double X, double Y, bool IsNumberMarker)> NoteRenderItems
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _noteItems
                .Select(i => (i.Text, i.X, i.Y, i.Fmt.VerticalAlign == VerticalAlign.Superscript))
                .ToList();
        }
    }

    /// <summary>
    /// Snapshot of the footnote-band / endnotes-heading separator rules: (X1, X2, PageSpaceY).
    /// </summary>
    internal IReadOnlyList<(double X1, double X2, double Y)> NoteSeparators
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _noteSeparators.ToList();
        }
    }

    // ── AV-POLISH: chart annotation introspection for tests ──────────────────────────────────────────

    /// <summary>
    /// Snapshot of floating chart annotation fields (ShowLegend, ShowDataLabels, CategoryAxisTitle,
    /// ValueAxisTitle) resolved by <see cref="BuildChartData"/>. Tests verify that the annotation
    /// flags are correctly derived from QuickLayout / StyleId / individual properties.
    /// </summary>
    internal IReadOnlyList<(bool ShowLegend, bool ShowDataLabels, string? CategoryAxisTitle, string? ValueAxisTitle)>
        FloatingChartAnnotations
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _floatingCharts.Select(c =>
                (c.ShowLegend, c.ShowDataLabels, c.CategoryAxisTitle, c.ValueAxisTitle)).ToList();
        }
    }

    /// <summary>
    /// Same as <see cref="FloatingChartAnnotations"/> but for inline charts.
    /// </summary>
    internal IReadOnlyList<(bool ShowLegend, bool ShowDataLabels, string? CategoryAxisTitle, string? ValueAxisTitle)>
        InlineChartAnnotations
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _inlineCharts.Select(c =>
                (c.ShowLegend, c.ShowDataLabels, c.CategoryAxisTitle, c.ValueAxisTitle)).ToList();
        }
    }

    internal IReadOnlyList<(ChartVisualGeometryKind GeometryKind, IReadOnlyList<string> PaletteHex)> InlineChartVisualPlans
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _inlineCharts.Select(c =>
                (c.GeometryKind, (IReadOnlyList<string>)c.Palette.Select(ToHex).ToList())).ToList();
        }
    }

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    // ---- PDF export ------------------------------------------------------------------------------

    /// <summary>
    /// Builds the app-agnostic shared <see cref="Free.Shared.Pdf.PdfContentDocument"/> draw-op model
    /// from the current layout (the same placed glyphs the editor renders). This is FreeW Avalonia's
    /// Document → shared-page adapter: <see cref="FreeWAvaloniaPdfExport"/> hands the result to the
    /// shared Skia or portable WinAnsi backend. The export reuses the laid-out glyph positions so it
    /// matches what is on screen; it paginates the single continuous column by the page height from
    /// <see cref="TextDocument.Page"/>.
    /// <para>
    /// NOTE: This is a first, faithful-enough text export. Tables/images/decorations and true
    /// print-pipeline pagination (headers/footers/footnotes) are not yet modeled here — see M4 notes.
    /// </para>
    /// </summary>
    public Free.Shared.Pdf.PdfContentDocument BuildPdfContent()
    {
        // Ensure a layout exists (tests/headless may export before a Render pass).
        if (_laidOutWidth < 0 || _placed.Count == 0)
            Relayout(FallbackWidth);

        var pageWidthPt = _doc.Page.WidthPt > 0 ? _doc.Page.WidthPt : 612;
        var pageHeightPt = _doc.Page.HeightPt > 0 ? _doc.Page.HeightPt : 792;
        var pageHeightPx = pageHeightPt * PxPerPoint;

        // Group consecutive same-line, same-format glyphs (excluding the page-left offset) into runs.
        var glyphs = _placed
            .Where(p => !p.Sentinel && p.Ch != '\0')
            .OrderBy(p => p.Y)
            .ThenBy(p => p.X)
            .ToList();

        // Bucket glyphs by page index (continuous column split at page height).
        var pagesOps = new List<List<Free.Shared.Pdf.PdfDrawOp>>();

        var runStartX = 0.0;
        var runY = 0.0;
        var runText = new StringBuilder();
        RunFormatting? runFmt = null;
        var runPageIndex = -1;

        void Flush()
        {
            if (runFmt is null || runText.Length == 0)
            {
                runText.Clear();
                runFmt = null;
                return;
            }

            while (pagesOps.Count <= runPageIndex)
                pagesOps.Add(new List<Free.Shared.Pdf.PdfDrawOp>());

            var fontSizePt = runFmt.FontSizePt ?? DefaultFontSizePt;
            var face = runFmt.Bold ? Free.Shared.Pdf.PdfFontFace.Bold : Free.Shared.Pdf.PdfFontFace.Regular;
            var color = ParseColor(runFmt.ColorHex);

            // Convert px -> pt and flip to PDF y-up. The glyph Y is the top of the line box; the text
            // baseline sits roughly at top + fontSize, so the PDF baseline (y-up) is page bottom minus that.
            // runY is in page-space Y (discrete multi-page layout); invert to get within-page offset.
            var xPt = (runStartX - _contentLeft) / PxPerPoint + _doc.Page.MarginLeftPt;
            // Offset within this page's page-space rectangle:
            //   pageTop(pageSpace) = DeskPadding + pageIndex*(pageHeightPx+PageGap)
            //   yWithinPagePx = runY - pageTop(pageSpace) = runY - DeskPadding - pageIndex*(pageHeightPx+PageGap)
            var yWithinPagePx = runY - _surfacePlan.PageTopDip(runPageIndex);
            var baselineFromTopPt = yWithinPagePx / PxPerPoint + fontSizePt;
            var yPt = pageHeightPt - baselineFromTopPt;

            pagesOps[runPageIndex].Add(new Free.Shared.Pdf.PdfText(
                Math.Max(0, xPt), yPt, fontSizePt, face, color, runText.ToString()));

            runText.Clear();
            runFmt = null;
        }

        // Glyphs are now in page-space Y (discrete multi-page layout).
        // Derive page index and within-page Y directly from the page-space Y.
        var pageStride = _surfacePlan.PageStrideDip; // distance between page tops in page space
        foreach (var g in glyphs)
        {
            // Invert ContentYToPageSpaceY:
            //   pageSpaceY = DeskPadding + pageIndex*(pageHeightPx+PageGap) + marginTopDip + offsetWithinPage
            var rel = g.Y - _surfacePlan.DeskPaddingDip;
            var pageIndex = Math.Max(0, (int)(rel / pageStride));
            var sameRun = runFmt is not null
                && runPageIndex == pageIndex
                && Math.Abs(g.Y - runY) < 0.5
                && FormatKey(g.Fmt) == FormatKey(runFmt)
                && g.X >= runStartX; // left-to-right on the line

            if (!sameRun)
            {
                Flush();
                runStartX = g.X;
                runY = g.Y;
                runFmt = g.Fmt;
                runPageIndex = pageIndex;
            }

            runText.Append(g.Ch);
        }

        Flush();

        if (pagesOps.Count == 0)
            pagesOps.Add(new List<Free.Shared.Pdf.PdfDrawOp>());

        var pages = pagesOps
            .Select(ops => new Free.Shared.Pdf.PdfContentPage(pageWidthPt, pageHeightPt, ops))
            .ToList();
        var properties = new Free.Shared.Pdf.PdfDocumentProperties(
            Title: string.IsNullOrWhiteSpace(_doc.Properties.Title) ? null : _doc.Properties.Title,
            Author: string.IsNullOrWhiteSpace(_doc.Properties.Author) ? null : _doc.Properties.Author,
            Creator: "FreeW");
        return new Free.Shared.Pdf.PdfContentDocument(pages, properties);
    }

    private static string FormatKey(RunFormatting fmt) =>
        $"{fmt.Bold}|{fmt.Italic}|{fmt.FontSizePt}|{fmt.ColorHex}";

    private static Free.Shared.Pdf.PdfColor ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Free.Shared.Pdf.PdfColor.Black;

        var s = hex.TrimStart('#');
        if (s.Length == 6 &&
            byte.TryParse(s.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(s.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(s.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            return new Free.Shared.Pdf.PdfColor(r, g, b);
        }

        return Free.Shared.Pdf.PdfColor.Black;
    }

    // ---- Layout ---------------------------------------------------------------------------------

    private static DocumentViewLayoutKind ToLayoutKind(DocumentViewMode mode) => mode switch
    {
        DocumentViewMode.PrintLayout => DocumentViewLayoutKind.PrintLayout,
        DocumentViewMode.WebLayout => DocumentViewLayoutKind.WebLayout,
        DocumentViewMode.Draft => DocumentViewLayoutKind.Draft,
        _ => DocumentViewLayoutKind.PrintLayout
    };

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) && availableSize.Width > 0
            ? availableSize.Width
            : FallbackWidth;
        Relayout(width);
        return new Size(width, _contentHeight);
    }

    private void Relayout(double width)
    {
        _placed.Clear();
        _markers.Clear();
        _rects.Clear();
        _paragraphDecorations.Clear();
        _images.Clear();
        _floatingImages.Clear();
        _floatingShapes.Clear();
        _floatingCharts.Clear();
        _floatingWordArts.Clear();
        _floatingSmartArts.Clear();
        _floatingGroups.Clear();
        _floatingSnapshots.Clear();
        _wrapExclusions.Clear();
        _inlineCharts.Clear();
        _inlineWordArts.Clear();
        _inlineSmartArts.Clear();
        _cellHits.Clear();
        _headerFooterItems.Clear();
        _noteItems.Clear();           // AV-NOTERENDER
        _noteSeparators.Clear();      // AV-NOTERENDER
        _endnoteExtentDip = 0;        // AV-NOTERENDER
        _footnoteBandHeightByPage.Clear(); // DB1/DB2
        _tabLeaderSpans.Clear(); // AV-TAB

        _surfacePlan = DocumentViewLayoutPlanner.BuildSurfacePlan(
            _doc.Page,
            ToLayoutKind(_viewMode),
            width);

        if (_viewMode == DocumentViewMode.PrintLayout)
        {
            // ---- Print Layout: paginated white pages on a grey desk ----
            // Page geometry from the document's PageSettings: a centred page with its own margins.
            _pageWidth = _surfacePlan.PageWidthDip;
            _pageHeightPx = _surfacePlan.PageHeightDip;
            _marginTopDip    = _surfacePlan.MarginTopDip;
            _marginBottomDip = _surfacePlan.MarginBottomDip;
            // Centre the page in the available width, never closer than the planner's minimum gutter.
            _pageLeft = _surfacePlan.PageLeftDip;
            _contentLeft = _surfacePlan.ContentLeftDip;
            _contentWidth = _surfacePlan.ContentWidthDip;
        }
        else if (_viewMode == DocumentViewMode.WebLayout)
        {
            // ---- Web Layout: continuous column, capped width, no page chrome ----
            // Responsive up to the planner's Web Layout cap; small fixed inset on each side.
            _pageWidth = _surfacePlan.PageWidthDip;
            _pageHeightPx = _surfacePlan.PageHeightDip; // effectively infinite — no pagination
            _marginTopDip    = _surfacePlan.MarginTopDip;
            _marginBottomDip = _surfacePlan.MarginBottomDip;
            _pageLeft = _surfacePlan.PageLeftDip;
            _contentLeft = _surfacePlan.ContentLeftDip;
            _contentWidth = _surfacePlan.ContentWidthDip;
        }
        else // Draft
        {
            // ---- Draft: plain left-margin continuous flow ----
            _pageWidth = _surfacePlan.PageWidthDip;
            _pageHeightPx = _surfacePlan.PageHeightDip;
            _marginTopDip    = _surfacePlan.MarginTopDip;
            _marginBottomDip = _surfacePlan.MarginBottomDip;
            _pageLeft = _surfacePlan.PageLeftDip;
            _contentLeft = _surfacePlan.ContentLeftDip;
            _contentWidth = _surfacePlan.ContentWidthDip;
        }

        // AV-COL: compute multi-column geometry from PageSettings.
        // Only active in PrintLayout mode — Web/Draft always use a single column.
        {
            var columnPlan = DocumentViewLayoutPlanner.BuildColumnPlan(
                _doc.Page,
                _contentWidth,
                usePageColumns: _surfacePlan.IsPrintLayout);
            _colCount       = columnPlan.Count;
            _colWidth       = columnPlan.WidthDip;
            _colGap         = columnPlan.GapDip;
            _colLineBetween = columnPlan.LineBetween;
        }

        // Body text layout uses _colWidth as the per-column wrap width.
        var textWidth = _colWidth;
        // Available text-area height per page (between top and bottom margin).
        // For Web/Draft this is effectively infinite so ReserveContentY never paginates.
        var textAreaHeight = _surfacePlan.TextAreaHeightDip;

        // _layoutContentY tracks the "content Y" — the offset within the flowing text area
        // (0 = start of the first text area). This gets converted to page-space Y via
        // ContentYToPageSpaceY() when placing glyphs.
        _layoutContentY = 0;
        _layoutTextAreaHeight = textAreaHeight;

        // DB1: first body-layout pass — lays out body text with _footnoteBandHeightByPage EMPTY
        // (no per-page reservation yet). After this pass we know which footnotes land on which page
        // and can measure true band heights. A second pass then re-flows the body with per-page
        // reservations so body text breaks before encroaching on the footnote band.
        RunBodyLayoutBlocks(textWidth);

        if (_viewMode == DocumentViewMode.PrintLayout)
        {
            // The number of column-slots used = floor(lastContentY / textAreaHeight) + 1.
            // Number of pages = ceil(slots / colCount).
            var lastSlot = _layoutContentY > 0 ? (int)(_layoutContentY / _layoutTextAreaHeight) : 0;
            var totalSlots = lastSlot + 1;
            _pageCount = Math.Max(1, (int)Math.Ceiling((double)totalSlots / _colCount));
            _contentHeight = _surfacePlan.ScrollableHeightForPages(_pageCount);

            // DB1: measure true footnote band heights (needs first-pass placed positions to resolve pages).
            if (_doc.Footnotes.Count > 0)
            {
                ComputeFootnoteBandHeights();

                // DB1 second pass: re-flow the body with per-page footnote reservations active.
                // ReserveContentY now consults _footnoteBandHeightByPage to shrink each page's
                // effective text area so body text breaks before the footnote band.
                _placed.Clear();
                _markers.Clear();
                _rects.Clear();
                _floatingImages.Clear();
                _floatingShapes.Clear();
                _floatingCharts.Clear();
                _floatingWordArts.Clear();
                _floatingSmartArts.Clear();
                _floatingGroups.Clear();
                _floatingSnapshots.Clear();
                _wrapExclusions.Clear();
                _inlineCharts.Clear();
                _inlineWordArts.Clear();
                _inlineSmartArts.Clear();
                _cellHits.Clear();
                _tabLeaderSpans.Clear();
                _layoutContentY = 0;
                RunBodyLayoutBlocks(textWidth);

                // Recompute page count from the second pass.
                lastSlot = _layoutContentY > 0 ? (int)(_layoutContentY / _layoutTextAreaHeight) : 0;
                totalSlots = lastSlot + 1;
                _pageCount = Math.Max(1, (int)Math.Ceiling((double)totalSlots / _colCount));
                _contentHeight = _surfacePlan.ScrollableHeightForPages(_pageCount);
            }
        }
        else
        {
            // Web/Draft: single continuous column — total height is just the content plus margins.
            _pageCount = 1;
            _contentHeight = _layoutContentY + _marginBottomDip;
        }

        _laidOutWidth = width;

        if (_viewMode == DocumentViewMode.PrintLayout)
        {
            BuildHeaderFooterItems();
            // AV-NOTERENDER: footnotes render in the bottom margin band of the page hosting their
            // reference; endnotes render in a synthetic section after the last body page. Endnotes
            // extend the scrollable content height, so add their measured extent afterwards.
            BuildFootnoteItems();
            BuildEndnoteItems();
            _contentHeight += _endnoteExtentDip;
        }
    }

    /// <summary>
    /// Iterates all body blocks and routes each to its layout path (paragraph/table/read-only).
    /// Extracted so <see cref="Relayout"/> can call it twice: a first pass to determine footnote-page
    /// assignment, and a second pass (DB1) with per-page band reservations active in
    /// <see cref="ReserveContentY"/> so body text reflows above the footnote band.
    /// </summary>
    private void RunBodyLayoutBlocks(double textWidth)
    {
        // BS1/BS2/BS3 fix: per-level counter array mirrors WPF MultiLevelMarkerSequence.
        // levelCounters[k] tracks the current count at list depth k (0-based, max 9 levels).
        // A Number/MultiLevel paragraph increments its level's counter and resets all deeper
        // levels.  A Bullet paragraph does NOT reset numbered levels (BS3: continuation across
        // interleaved sub-bullets).  Only a non-list (ListKind.None) paragraph resets all counters
        // (the numbered list has genuinely ended).
        const int MaxListDepth = 9;
        var levelCounters = new int[MaxListDepth];
        for (var blockIndex = 0; blockIndex < _doc.Blocks.Count; blockIndex++)
        {
            var block = _doc.Blocks[blockIndex];
            if (block is Paragraph paragraph)
            {
                // Route to the image-paragraph path only when the paragraph contains inline images.
                // Paragraphs whose images are ALL floating (anchored) are laid out as normal text
                // paragraphs so that the anchor content-Y is tracked; their images are collected
                // into _floatingImages by CollectFloatingObjects() called from within each layout method.
                var hasInlineImage   = paragraph.Runs.Any(r => r.Image    is { IsFloating: false });
                var hasInlineChart   = paragraph.Runs.Any(r => r.Chart    is { IsFloating: false });
                var hasInlineWordArt = paragraph.Runs.Any(r => r.WordArt  is { IsFloating: false });
                var hasInlineSmArt   = paragraph.Runs.Any(r => r.SmartArt is { IsFloating: false });
                var hasAnyImage    = paragraph.Runs.Any(r => r.Image is not null);
                if (hasAnyImage)
                {
                    // Always collect floating images from this paragraph (done inside each layout path).
                    if (hasInlineImage)
                    {
                        // Mixed paragraph: inline image(s) present — use the image layout path which
                        // also calls CollectFloatingObjects internally.
                        // Non-list paragraph: reset all counters (list run ended).
                        Array.Clear(levelCounters, 0, MaxListDepth);
                        LayoutImageParagraphPaged(blockIndex, paragraph, textWidth);
                        continue;
                    }
                    // Floating-only: fall through to normal paragraph layout below,
                    // which calls CollectFloatingObjects at the start of EmitLinePaged.
                }

                // FO4: route paragraphs with inline charts / SmartArt / WordArt to the dedicated path.
                if (hasInlineChart || hasInlineWordArt || hasInlineSmArt)
                {
                    // Non-list paragraph: reset all counters (list run ended).
                    Array.Clear(levelCounters, 0, MaxListDepth);
                    LayoutInlineObjectParagraphPaged(blockIndex, paragraph, textWidth);
                    continue;
                }

                var kind = paragraph.Formatting.ListKind;
                double inset = 0;
                string? marker = null;
                if (kind != ListKind.None)
                {
                    var level = Math.Clamp(paragraph.Formatting.ListLevel, 0, MaxListDepth - 1);
                    inset = ListIndentStep * (level + 1);
                    if (kind is ListKind.Number or ListKind.MultiLevel)
                    {
                        // BS1: increment this level's counter, reset all deeper levels.
                        levelCounters[level]++;
                        for (var deeper = level + 1; deeper < MaxListDepth; deeper++)
                            levelCounters[deeper] = 0;

                        // BS2: build the appropriate marker.
                        if (kind is ListKind.MultiLevel)
                        {
                            // Accumulated dotted form: counters[0].counters[1]...counters[level].
                            var sb = new System.Text.StringBuilder();
                            for (var ancestor = 0; ancestor <= level; ancestor++)
                            {
                                // Ancestors not yet entered in this run show 1 (matches Word/WPF).
                                var value = levelCounters[ancestor] == 0 ? 1 : levelCounters[ancestor];
                                sb.Append(value.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('.');
                            }
                            marker = sb.ToString();
                        }
                        else
                        {
                            // Number: plain decimal marker for this level's counter.
                            marker = $"{levelCounters[level]}.";
                        }
                    }
                    else
                    {
                        // BS3: Bullet does NOT reset numbered level counters.
                        // The numbered list continues its sequence across interleaved sub-bullets.
                        marker = "•"; // bullet
                    }
                }
                else
                {
                    // Non-list paragraph: the numbered list run has ended, reset all counters.
                    Array.Clear(levelCounters, 0, MaxListDepth);
                }

                LayoutParagraphPaged(blockIndex, paragraph, textWidth, inset, marker);
            }
            else if (block is Table table)
            {
                LayoutTablePaged(blockIndex, table, textWidth);
            }
            else
            {
                LayoutReadOnlyBlockPaged(blockIndex, block, textWidth);
            }
        }
    }

    // ── HF: header/footer pre-computation ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a mapping from 0-based page index to (SectionHeadersFooters, section-relative
    /// 1-based page number, PageSettings) for <paramref name="pageCount"/> pages.
    ///
    /// Mirrors WPF's PaginatedEditorPanel.ComputePageSectionMap: walks <see cref="_doc"/>.Blocks
    /// to assign each block a section index (incrementing at each SectionBreak paragraph), then
    /// uses <see cref="_placed"/> character positions to determine which page each block's content
    /// first appears on, giving a true block→page→section mapping.
    ///
    /// Fallback: when a section's HeadersFooters is entirely empty, substitutes
    /// <see cref="TextDocument.FinalSectionHeadersFooters"/> (AE3 fix).
    /// </summary>
    private IReadOnlyList<HeaderFooterPageSectionPlan> ComputePageSectionMap(int pageCount)
    {
        var blocks = _doc.Blocks;
        var blockPageAssignments = Enumerable
            .Repeat(HeaderFooterPagePlanner.UnassignedBlockPageIndex, blocks.Count)
            .ToArray();

        foreach (var pc in _placed)
        {
            if (pc.Sentinel) continue;
            var b = pc.Block;
            if (b < 0 || b >= blocks.Count) continue;
            if (blockPageAssignments[b] >= 0) continue;

            var pg = PageIndexFromPageSpaceY(pc.Y);
            blockPageAssignments[b] = Math.Clamp(pg, 0, pageCount - 1);
        }

        return HeaderFooterPagePlanner.MapPagesToSections(_doc, blockPageAssignments, pageCount);
    }

    /// <summary>Maps the shared planner slot kind to Avalonia's edit-target slot enum.</summary>
    private static HfSlot ToHfSlot(HeaderFooterSlotKind slot) => slot switch
    {
        HeaderFooterSlotKind.Header => HfSlot.Header,
        HeaderFooterSlotKind.Footer => HfSlot.Footer,
        HeaderFooterSlotKind.FirstHeader => HfSlot.FirstHeader,
        HeaderFooterSlotKind.FirstFooter => HfSlot.FirstFooter,
        HeaderFooterSlotKind.EvenHeader => HfSlot.EvenHeader,
        HeaderFooterSlotKind.EvenFooter => HfSlot.EvenFooter,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null)
    };

    /// <summary>
    /// AV-HFEDIT: builds a stable <see cref="HfTarget"/> for a resolved HF store + slot. Identifies the
    /// store either as the document-level final-section store (which the document-level Header/Footer views
    /// alias) or by reference-equality with a section's own store.
    /// </summary>
    private HfTarget MakeHfTarget(SectionHeadersFooters sectionHf, HfSlot slot, int paraIdx)
    {
        if (ReferenceEquals(sectionHf, _doc.FinalSectionHeadersFooters))
            return new HfTarget(_doc.Sections.Count - 1, UseFinalSectionStore: true, slot, paraIdx);
        for (var i = 0; i < _doc.Sections.Count; i++)
        {
            if (ReferenceEquals(_doc.Sections[i].HeadersFooters, sectionHf))
                return new HfTarget(i, UseFinalSectionStore: false, slot, paraIdx);
        }
        // Defensive fallback: treat as the final-section store.
        return new HfTarget(_doc.Sections.Count - 1, UseFinalSectionStore: true, slot, paraIdx);
    }

    /// <summary>
    /// AV-HFEDIT: true when the active header/footer caret targets the given resolved store + slot. Used so
    /// the layout still emits an (empty) editable band for a freshly-clicked or just-emptied slot.
    /// </summary>
    private bool HfCaretTargets(SectionHeadersFooters sectionHf, HfSlot slot)
    {
        if (_hfCaret is not { } hc || hc.Target.Slot != slot)
            return false;
        var caretStore = ResolveHfStore(hc.Target);
        return ReferenceEquals(caretStore, sectionHf);
    }

    /// <summary>
    /// Pre-computes the header/footer render items for each page in PrintLayout mode.
    /// Called once per Relayout pass after _pageCount is known. Each item carries a
    /// field-resolved text string, run formatting, page-space position, and alignment so
    /// Render() can draw them with zero model access.
    /// </summary>
    private void BuildHeaderFooterItems()
    {
        // Default fallback header/footer distance: Word default 0.5 in = 36 pt.
        const double DefaultHfDistancePt = 36.0;

        var diffOddEven = HeaderFooterPagePlanner.UsesDifferentOddEvenPages(_doc);

        // Build a true page-to-section map from Avalonia's placed block positions.
        var pageToSection = ComputePageSectionMap(_pageCount);

        for (var pi = 0; pi < _pageCount; pi++)
        {
            // Page-space top of this page.
            var pageTop = _surfacePlan.PageTopDip(pi);

            // AE1: use the true owning section for this page (not an even distribution).
            var pageSection = pageToSection[pi];
            var sectionHf = pageSection.HeadersFooters;
            var sectionPage = pageSection.PageSettings;

            // 1-based page number for this page (pi is 0-based).
            var pageNumber = pi + 1;

            // Resolve header/footer slots through the shared Presentation planner.
            var slots = HeaderFooterPagePlanner.ResolveSlots(
                sectionHf,
                pageSection.SectionRelativePageNumber,
                sectionPage,
                diffOddEven);
            var header = slots.Header;
            var footer = slots.Footer;
            var headerSlot = ToHfSlot(slots.HeaderSlot);
            var footerSlot = ToHfSlot(slots.FooterSlot);

            // Header distance from page top (in DIP).
            var headerDistPt = sectionPage.HeaderDistancePt > 0
                ? sectionPage.HeaderDistancePt
                : DefaultHfDistancePt;
            var headerDistDip = headerDistPt * PxPerPoint;

            // Footer distance from page bottom (in DIP).
            var footerDistPt = sectionPage.FooterDistancePt > 0
                ? sectionPage.FooterDistancePt
                : DefaultHfDistancePt;
            var footerDistDip = footerDistPt * PxPerPoint;

            // Render-width = page content width (use same as body text area).
            var hfWidth = _contentWidth;

            // AV-HFEDIT: emit an empty (but editable) band when the H/F caret is active in this slot even if
            // the slot has no visible content yet — so a freshly-clicked/empty header still renders + edits.
            var headerActive = header is not null && (!header.IsEmpty || HfCaretTargets(sectionHf, headerSlot));
            var footerActive = footer is not null && (!footer.IsEmpty || HfCaretTargets(sectionHf, footerSlot));

            // Emit header.
            if (headerActive)
            {
                var hfY = pageTop + headerDistDip;
                EmitHfParagraphs(header!, hfY, hfWidth, pageNumber, _pageCount,
                    pi => MakeHfTarget(sectionHf, headerSlot, pi));
            }

            // Emit footer.
            if (footerActive)
            {
                // Footer distance is from the BOTTOM of the page upward; the footer text
                // starts at: pageBottom - footerDistDip (+ a line-height offset per line).
                var pageBottom = pageTop + _pageHeightPx;
                var hfY = pageBottom - footerDistDip;
                EmitHfParagraphs(footer!, hfY, hfWidth, pageNumber, _pageCount,
                    pi => MakeHfTarget(sectionHf, footerSlot, pi));
            }
        }
    }

    /// <summary>
    /// Emits <see cref="HfRenderItem"/>s for each paragraph line of a header/footer slot.
    /// Field runs (PAGE, NUMPAGES, DATE, FILENAME) are resolved to display strings here.
    /// Tab characters are interpreted as Word's default centre-tab (midpoint) and right-tab (right edge),
    /// mirroring Word's "Title[TAB]Center[TAB]Page" HF pattern. Explicit paragraph TabStops override the
    /// defaults when present. Each tab-separated segment is emitted as a separate HfRenderItem at the
    /// computed X position so the draw loop does not need tab-aware logic.
    /// </summary>
    private void EmitHfParagraphs(HeaderFooter hf, double startY, double availWidth, int pageNumber, int pageCount,
        Func<int, HfTarget>? targetFactory = null)
    {
        var y = startY;
        for (var paraIdx = 0; paraIdx < hf.Paragraphs.Count; paraIdx++)
        {
            var para = hf.Paragraphs[paraIdx];
            var pf = ResolveParagraphFmt(para);
            // AV-HFEDIT: the editing target for this paragraph (which section/slot/para), or null in
            // legacy callers that do not pass a factory (kept for backward compatibility / tests).
            HfTarget? paraTarget = targetFactory?.Invoke(paraIdx);

            // Build segments split on TAB characters.
            // Each entry carries (tabStopIndex, Text, Fmt, ModelStart):
            //   tabStopIndex 0 → segment before the first tab (left-aligned at X=_contentLeft)
            //   tabStopIndex 1 → segment after the first tab (centre stop)
            //   tabStopIndex 2 → segment after the second tab (right stop)
            // Multiple consecutive tabs advance the index so the ordinal is always correct even
            // if some slots carry empty text (e.g. "Left\t\tRight" puts "Right" at stop 2).
            // AV-HFEDIT: ModelStart is the literal-model-text offset where the segment's first char
            // begins, so a click X maps to a model offset for the editing caret. Model offset advances
            // by run.Text.Length for each run (field runs are atomic — their resolved text may differ in
            // length, but the model span is run.Text.Length, including a single tab char per model tab).
            var segments = new List<(int StopIndex, string Text, RunFormatting Fmt, int ModelStart)>();
            var sb = new System.Text.StringBuilder();
            RunFormatting segFmt = para.Runs.Count > 0 ? para.Runs[0].Formatting : RunFormatting.Default;
            var stopIndex = 0;
            var modelOffset = 0;       // running literal-model offset across all runs
            var segModelStart = 0;     // model offset at the start of the current (buffered) segment

            foreach (var run in para.Runs)
            {
                var fieldText = ResolveHfField(run, pageNumber, pageCount);
                var isField = fieldText is not null;
                var text = fieldText ?? run.Text;
                if (run.Formatting.FontSizePt.HasValue)
                    segFmt = run.Formatting;

                if (isField)
                {
                    // Atomic field run: append its resolved text whole (no tab-splitting inside a field).
                    sb.Append(text);
                    modelOffset += run.Text.Length;
                    continue;
                }

                // Split the literal run text on tab characters; model offset advances per literal char.
                var parts = text.Split('\t');
                for (var pi = 0; pi < parts.Length; pi++)
                {
                    sb.Append(parts[pi]);
                    modelOffset += parts[pi].Length;
                    if (pi < parts.Length - 1)
                    {
                        // A TAB was consumed — flush the current buffer as a segment and advance the stop index.
                        segments.Add((stopIndex, sb.ToString(), segFmt, segModelStart));
                        sb.Clear();
                        stopIndex++;
                        modelOffset += 1;            // the consumed tab is one model char
                        segModelStart = modelOffset; // next segment starts after the tab
                    }
                }
            }
            // Flush the final (or only) segment.
            segments.Add((stopIndex, sb.ToString(), segFmt, segModelStart));

            // Whether the paragraph contains any tab characters at all.
            var hasAnyTab = segments.Count > 1 || (segments.Count == 1 && stopIndex > 0);

            // Compute the line height from the first non-empty segment (or use DefaultFontSizePt).
            var firstNonEmpty = segments.FirstOrDefault(s => s.Text.Length > 0);
            RunFormatting lineRefFmt = firstNonEmpty.Text?.Length > 0 ? firstNonEmpty.Fmt : RunFormatting.Default;
            var sampleText = firstNonEmpty.Text ?? string.Empty;
            var lineH = string.IsNullOrEmpty(sampleText)
                ? DefaultFontSizePt * PxPerPoint * 1.3
                : Build(sampleText.Length > 1 ? sampleText[..1] : sampleText, lineRefFmt).Height * 1.15;

            // Resolve explicit tab stops from the paragraph; sort by position.
            var explicitStops = pf.TabStops
                .OrderBy(t => t.PositionPt)
                .Select(t => (PosPx: t.PositionPt * PxPerPoint, t.Alignment))
                .ToList();

            // Default Word H/F tab stops: centre at midpoint, right at full width.
            var defaultCenterPx = availWidth / 2.0;
            var defaultRightPx  = availWidth;

            if (!hasAnyTab)
            {
                // No tabs — use paragraph alignment as before. Emit even an EMPTY paragraph so an empty
                // header/footer line is still clickable for editing (the caret needs a region to land in).
                var seg = segments[0];
                _headerFooterItems.Add(new HfRenderItem
                {
                    Text             = seg.Text,
                    Fmt              = seg.Fmt,
                    X                = _contentLeft,
                    Y                = y,
                    AvailableWidth   = availWidth,
                    Alignment        = pf.Alignment,
                    Target           = paraTarget,
                    LineHeight       = lineH,
                    ModelStartOffset = seg.ModelStart,
                });
            }
            else
            {
                // Tab-split: each segment is positioned by its tab-stop ordinal.
                // StopIndex 0 → left (at _contentLeft, no stop lookup needed).
                // StopIndex 1 → first tab stop  (default: centre).
                // StopIndex 2 → second tab stop (default: right).
                foreach (var (si, text, fmt, modelStart) in segments)
                {
                    if (string.IsNullOrEmpty(text)) continue;

                    double stopX;
                    TabStopAlignment stopAlign;

                    if (si == 0)
                    {
                        stopX     = 0; // relative to _contentLeft
                        stopAlign = TabStopAlignment.Left;
                    }
                    else
                    {
                        // Tab stop ordinal within the explicit list is (si - 1).
                        var stopIdx = si - 1;
                        if (stopIdx < explicitStops.Count)
                        {
                            (stopX, stopAlign) = explicitStops[stopIdx];
                        }
                        else
                        {
                            // Fall back to Word defaults for ordinal within the default set.
                            if (si == 1) { stopX = defaultCenterPx; stopAlign = TabStopAlignment.Center; }
                            else         { stopX = defaultRightPx;  stopAlign = TabStopAlignment.Right;  }
                        }
                    }

                    // Measure segment text to compute the draw X.
                    var segFt = Build(text, fmt);
                    var segW  = segFt.WidthIncludingTrailingWhitespace;

                    var itemX = stopAlign switch
                    {
                        TabStopAlignment.Center  => _contentLeft + stopX - segW / 2,
                        TabStopAlignment.Right   => _contentLeft + stopX - segW,
                        TabStopAlignment.Decimal => _contentLeft + stopX - segW, // approximation: treat as right
                        _                        => _contentLeft + stopX,
                    };
                    // Clamp to content area so text never overflows the page edge.
                    itemX = Math.Max(_contentLeft, Math.Min(_contentLeft + availWidth - segW, itemX));

                    _headerFooterItems.Add(new HfRenderItem
                    {
                        Text             = text,
                        Fmt              = fmt,
                        X                = itemX,
                        Y                = y,
                        AvailableWidth   = 0,                  // absolute X — skip alignment offset at draw time
                        Alignment        = TextAlignment.Left, // draw loop uses X as-is (offset=0 for Left)
                        Target           = paraTarget,
                        LineHeight       = lineH,
                        ModelStartOffset = modelStart,
                    });
                }
            }

            y += lineH;
        }
    }

    /// <summary>
    /// Resolves a field run to its display string, or returns null when the run is plain text.
    /// Handles both <see cref="RunFieldKind"/> simple fields and <see cref="ComplexField"/>
    /// instructions that contain PAGE / NUMPAGES / DATE / FILENAME / AUTHOR keywords.
    /// </summary>
    private string? ResolveHfField(Run run, int pageNumber, int pageCount)
    {
        // Simple RunFieldKind fields.
        switch (run.FieldKind)
        {
            case RunFieldKind.PageNumber:
                return pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case RunFieldKind.NumPages:
                return pageCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case RunFieldKind.Date:
            case RunFieldKind.Time:
                return DateTime.Now.ToString("M/d/yyyy", System.Globalization.CultureInfo.InvariantCulture);
            case RunFieldKind.FileName:
                return string.Empty; // DocumentProperties has no FileName property
            case RunFieldKind.Author:
                return _doc.Properties.Author ?? string.Empty;
            case RunFieldKind.Title:
                return _doc.Properties.Title ?? string.Empty;
            case RunFieldKind.Subject:
                return _doc.Properties.Subject ?? string.Empty;
            case RunFieldKind.Keywords:
                return _doc.Properties.Keywords ?? string.Empty;
            case RunFieldKind.DocComments:
                return _doc.Properties.Comments ?? string.Empty;
        }

        // Complex fields: inspect the instruction keyword.
        if (run.ComplexField is { } cf)
        {
            var instr = cf.Instruction?.Trim() ?? string.Empty;
            var keyword = instr.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            return keyword.ToUpperInvariant() switch
            {
                "PAGE"     => pageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "NUMPAGES" => pageCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "DATE"     => DateTime.Now.ToString("M/d/yyyy", System.Globalization.CultureInfo.InvariantCulture),
                "TIME"     => DateTime.Now.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture),
                "FILENAME" => string.Empty, // DocumentProperties has no FileName property
                "AUTHOR"   => _doc.Properties.Author ?? string.Empty,
                "TITLE"    => _doc.Properties.Title ?? string.Empty,
                _          => run.Text, // fall back to cached result text
            };
        }

        // Not a field run — caller should use run.Text.
        return null;
    }

    // ── AV-NOTERENDER: footnote / endnote content rendering ───────────────────────────────────────────

    /// <summary>Default font size (pt) for footnote / endnote body text. Word uses 10pt; we use 9pt.</summary>
    private const double NoteFontSizePt = 9.0;

    /// <summary>
    /// DB2: Measures the true wrapped height of a single note's content (number prefix + paragraph text)
    /// without emitting any render items. Mirrors the word-wrap logic of <see cref="LayoutNoteContent"/>
    /// but just returns the final Y extent (height from the starting Y).
    /// Used by <see cref="BuildFootnoteItems"/> to compute the accurate band height for reservation and
    /// clamping, instead of the previous 1-line-per-note estimate.
    /// </summary>
    private double MeasureNoteContentHeight(string number, IReadOnlyList<Paragraph> content, double x, double availWidth)
    {
        var noteFmt = RunFormatting.Default with { FontSizePt = NoteFontSizePt };
        var numFmt  = noteFmt with { VerticalAlign = VerticalAlign.Superscript };
        var lineH   = Math.Max(1, Build("Ag", noteFmt).Height);

        var numText  = number + " ";
        var numWidth = Build(numText, numFmt).WidthIncludingTrailingWhitespace;
        var textLeft = x + numWidth;
        var penX     = textLeft;
        var lineY    = 0.0; // relative to the note's start Y

        var first = true;
        foreach (var para in content)
        {
            if (!first)
            {
                lineY += lineH;
                penX = textLeft;
            }
            first = false;

            var words = para.PlainText.Split(' ');
            for (var wi = 0; wi < words.Length; wi++)
            {
                var word = wi == words.Length - 1 ? words[wi] : words[wi] + " ";
                if (word.Length == 0) continue;
                var w = Build(word, noteFmt).WidthIncludingTrailingWhitespace;
                if (penX + w > x + availWidth && penX > textLeft)
                {
                    lineY += lineH;
                    penX = textLeft;
                }
                penX += w;
            }
        }
        return lineY + lineH; // total height from the note's top to the last line's bottom
    }

    /// <summary>
    /// DB3: Computes the display number string for a note (footnote or endnote) given its
    /// 1-based sequence index and the <see cref="NoteNumberingOptions"/> that govern its format.
    /// <para>
    /// Examples: decimal 3 → "3"; lowerRoman 4 → "iv"; lowerLetter 2 → "b";
    /// Chicago 1 → "*", 2 → "†", 3 → "‡", 4 → "§", then repeats.
    /// </para>
    /// </summary>
    private static string ComputeNoteDisplayNumber(int sequenceIndex, NoteNumberingOptions opts)
    {
        // sequenceIndex is 1-based display position (after applying StartAt offset).
        var n = sequenceIndex;
        return opts.NumberFormat switch
        {
            NoteNumberFormat.LowerRoman => ToRoman(n, lower: true),
            NoteNumberFormat.UpperRoman => ToRoman(n, lower: false),
            NoteNumberFormat.LowerLetter => ToLetter(n, lower: true),
            NoteNumberFormat.UpperLetter => ToLetter(n, lower: false),
            NoteNumberFormat.Chicago     => ToChicago(n),
            _                            => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    private static string ToRoman(int n, bool lower)
    {
        if (n <= 0) return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var sb = new System.Text.StringBuilder();
        int[] vals = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        string[] syms = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
        for (var i = 0; i < vals.Length; i++)
            while (n >= vals[i]) { sb.Append(syms[i]); n -= vals[i]; }
        var result = sb.ToString();
        return lower ? result.ToLowerInvariant() : result;
    }

    private static string ToLetter(int n, bool lower)
    {
        if (n <= 0) return n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // 1→a, 26→z, 27→aa, 52→az, 53→ba … (Word uses this scheme for footnotes)
        var sb = new System.Text.StringBuilder();
        while (n > 0)
        {
            n--;
            sb.Insert(0, (char)((lower ? 'a' : 'A') + n % 26));
            n /= 26;
        }
        return sb.ToString();
    }

    private static string ToChicago(int n)
    {
        // Chicago style: *, †, ‡, §, **, ††, … (repeating symbol groups for overflow).
        string[] symbols = { "*", "†", "‡", "§", "¶", "#" };
        var group  = (n - 1) / symbols.Length;
        var sym    = symbols[(n - 1) % symbols.Length];
        return group == 0 ? sym : new string(sym[0], group + 1); // * ** *** …
    }

    /// <summary>
    /// Resolves the 0-based page index that hosts the body reference for the note with the given id.
    /// Locates the body run carrying <paramref name="footnote"/>'s matching <see cref="Run.FootnoteId"/>
    /// (or <see cref="Run.EndnoteId"/>), computes its first character's cell offset within the host
    /// paragraph, then reads the page of the matching <see cref="PlacedChar"/>. Returns the last body
    /// page when no placed glyph is found (an acceptable approximation when a marker glyph was not laid
    /// out, e.g. a zero-width run).
    /// </summary>
    private int ResolveNoteReferencePage(int id, bool footnote)
    {
        var blocks = _doc.Blocks;
        for (var b = 0; b < blocks.Count; b++)
        {
            if (blocks[b] is not Paragraph para) continue;
            var offset = 0;
            foreach (var run in para.Runs)
            {
                var isMatch = footnote ? run.FootnoteId == id : run.EndnoteId == id;
                if (isMatch && run.Text.Length > 0)
                {
                    // Find the placed glyph for this paragraph at the run's first char offset.
                    foreach (var pc in _placed)
                    {
                        if (pc.Sentinel || pc.Block != b) continue;
                        if (pc.Offset == offset)
                            return Math.Clamp(PageIndexFromPageSpaceY(pc.Y), 0, Math.Max(0, _pageCount - 1));
                    }
                    // Run matched but no placed glyph at that offset — fall back to any glyph on the block.
                    foreach (var pc in _placed)
                    {
                        if (pc.Sentinel || pc.Block != b) continue;
                        return Math.Clamp(PageIndexFromPageSpaceY(pc.Y), 0, Math.Max(0, _pageCount - 1));
                    }
                }
                offset += run.Text.Length;
            }
        }
        // No reference glyph found — group on the last body page (documented approximation).
        return Math.Max(0, _pageCount - 1);
    }

    /// <summary>
    /// Lays out a single note's content (number + paragraph text) into <see cref="_noteItems"/> starting
    /// at page-space (<paramref name="x"/>, <paramref name="y"/>), wrapping within <paramref name="availWidth"/>.
    /// Reuses the body <see cref="Build"/> glyph metrics at <see cref="NoteFontSizePt"/>. The number prefix
    /// ("<c>n </c>") is emitted as a superscript-styled lead item; the note text follows as wrapped lines.
    /// Returns the page-space Y just past the laid-out content (the next free line).
    /// </summary>
    private double LayoutNoteContent(string number, IReadOnlyList<Paragraph> content, double x, double y, double availWidth)
    {
        var noteFmt = RunFormatting.Default with { FontSizePt = NoteFontSizePt };
        var numFmt  = noteFmt with { VerticalAlign = VerticalAlign.Superscript };

        var lineH = Math.Max(1, Build("Ag", noteFmt).Height);

        // Emit the number marker first (superscript), then the text flows after it on the same line.
        var numText = number + " ";
        var numWidth = Build(numText, numFmt).WidthIncludingTrailingWhitespace;
        _noteItems.Add(new NoteRenderItem { Text = numText, Fmt = numFmt, X = x, Y = y });

        var textLeft = x + numWidth;
        var lineLeft = textLeft;
        var penX = textLeft;
        var lineY = y;

        // Flatten the note's paragraphs into a single wrapped flow (note content is usually one paragraph).
        var first = true;
        foreach (var para in content)
        {
            if (!first)
            {
                // New paragraph in the note: break to a fresh line at the text-left indent.
                lineY += lineH;
                penX = textLeft;
                lineLeft = textLeft;
            }
            first = false;

            var words = para.PlainText.Split(' ');
            for (var wi = 0; wi < words.Length; wi++)
            {
                var word = wi == words.Length - 1 ? words[wi] : words[wi] + " ";
                if (word.Length == 0) continue;
                var w = Build(word, noteFmt).WidthIncludingTrailingWhitespace;
                // Wrap when the word would overflow the available width (but always place at least one word).
                if (penX + w > x + availWidth && penX > lineLeft)
                {
                    lineY += lineH;
                    penX = textLeft;
                    lineLeft = textLeft;
                }
                _noteItems.Add(new NoteRenderItem { Text = word, Fmt = noteFmt, X = penX, Y = lineY });
                penX += w;
            }
        }

        return lineY + lineH;
    }

    /// <summary>
    /// DB1 first-pass helper: populates <see cref="_footnoteBandHeightByPage"/> with the TRUE height
    /// of each page's footnote band (separator + all notes' true wrapped heights), without emitting any
    /// render items. Called before the second body-layout pass so <see cref="ReserveContentY"/> can use
    /// per-page reservations to shrink the effective body text area on each page that has footnotes.
    /// </summary>
    private void ComputeFootnoteBandHeights()
    {
        _footnoteBandHeightByPage.Clear();
        if (_doc.Footnotes.Count == 0) return;

        const double SepHeight = 6.0; // separator rule + gap

        // Group footnote ids by the page hosting their reference (using first-pass placed positions).
        var byPage = new Dictionary<int, List<int>>();
        foreach (var id in _doc.Footnotes.Keys.OrderBy(k => k))
        {
            var pg = ResolveNoteReferencePage(id, footnote: true);
            if (!byPage.TryGetValue(pg, out var list))
                byPage[pg] = list = new List<int>();
            list.Add(id);
        }

        // Compute display sequence numbers respecting StartAt and sort order.
        var opts = _doc.FootnoteNumbering;
        var seqBase = Math.Max(1, opts.StartAt);

        foreach (var (pg, ids) in byPage)
        {
            var totalHeight = SepHeight + 4; // separator rule + top-pad
            var seq = seqBase;
            foreach (var id in ids)
            {
                var note = _doc.Footnotes[id];
                var displayNum = ComputeNoteDisplayNumber(seq, opts);
                seq++;
                var content = note.Content.Count > 0
                    ? note.Content
                    : (IReadOnlyList<Paragraph>)new List<Paragraph> { new Paragraph(string.Empty) };
                totalHeight += MeasureNoteContentHeight(displayNum, content, _contentLeft, _contentWidth);
                totalHeight += 2; // inter-note gap
            }
            _footnoteBandHeightByPage[pg] = totalHeight;
        }
    }

    /// <summary>
    /// AV-NOTERENDER (footnotes): for each page in PrintLayout, renders a short separator rule then the
    /// footnotes whose body reference lands on that page, stacked as "<c>n note text</c>" at
    /// <see cref="NoteFontSizePt"/>. The band occupies the bottom margin area, above the footer.
    /// <para>
    /// DB1: the band height was pre-computed by <see cref="ComputeFootnoteBandHeights"/> and stored in
    /// <see cref="_footnoteBandHeightByPage"/>; <see cref="ReserveContentY"/> already shrank the body
    /// text area so body text does not overlap the band.
    /// </para>
    /// <para>
    /// DB2: each note's TRUE wrapped height (from <see cref="MeasureNoteContentHeight"/>) determines
    /// the band top, replacing the old 1-line-per-note estimate. Content is clamped so it does not
    /// overflow past the footer. Overflow (band taller than available gap) is clipped with a note below.
    /// </para>
    /// <para>
    /// DB3: note numbers use <see cref="ComputeNoteDisplayNumber"/> with the document's
    /// <see cref="TextDocument.FootnoteNumbering"/> options (format + StartAt).
    /// </para>
    /// Page assignment uses <see cref="ResolveNoteReferencePage"/>; footnotes with no locatable reference
    /// glyph fall back to the last body page (documented approximation).
    /// </summary>
    private void BuildFootnoteItems()
    {
        if (_doc.Footnotes.Count == 0) return;

        const double DefaultHfDistancePt = 36.0;

        // Group footnote ids by the page hosting their reference, preserving id order.
        var byPage = new Dictionary<int, List<int>>();
        foreach (var id in _doc.Footnotes.Keys.OrderBy(k => k))
        {
            var pg = ResolveNoteReferencePage(id, footnote: true);
            if (!byPage.TryGetValue(pg, out var list))
                byPage[pg] = list = new List<int>();
            list.Add(id);
        }

        var footerDistPt = _doc.Page.FooterDistancePt > 0 ? _doc.Page.FooterDistancePt : DefaultHfDistancePt;
        var footerDistDip = footerDistPt * PxPerPoint;

        // DB3: footnote numbering options — format (Decimal/LowerRoman/…) + StartAt offset.
        var opts     = _doc.FootnoteNumbering;
        var seqIndex = Math.Max(1, opts.StartAt); // 1-based display sequence counter

        foreach (var (pg, ids) in byPage.OrderBy(kv => kv.Key))
        {
            var pageTop    = _surfacePlan.PageTopDip(pg);
            var pageBottom = pageTop + _pageHeightPx;
            // Body text area bottom on this page (page-space), using the reserved effective height.
            var bandReservation = _footnoteBandHeightByPage.TryGetValue(pg, out var bh) ? bh : 0.0;
            var bodyBottom = pageTop + _marginTopDip + (_layoutTextAreaHeight - bandReservation);
            // Footer top (where the footer line begins).
            var footerTop = pageBottom - footerDistDip;

            // DB2: true total band height = separator + true wrapped heights of all notes on this page.
            var trueHeight = 4.0 + 6.0; // top-pad + separator
            var localSeq = seqIndex;
            foreach (var id in ids)
            {
                var note2 = _doc.Footnotes[id];
                var dn = ComputeNoteDisplayNumber(localSeq++, opts);
                var content2 = note2.Content.Count > 0
                    ? note2.Content
                    : (IReadOnlyList<Paragraph>)new List<Paragraph> { new Paragraph(string.Empty) };
                trueHeight += MeasureNoteContentHeight(dn, content2, _contentLeft, _contentWidth);
                trueHeight += 2; // inter-note gap
            }

            // DB2: anchor the band so its bottom aligns with the footer top.
            // The band top is (footerTop - trueHeight), but must stay at/below the body bottom.
            var bandTop = Math.Max(bodyBottom + 2, footerTop - trueHeight);
            // Also clamp: band must not start above the mid-margin (guard for very tall bands on short pages).
            bandTop = Math.Min(bandTop, footerTop - 6);
            // DB2: available height within the band = from bandTop to footerTop. If overflow, content is
            // clipped at footerTop (split-to-next-page is a follow-up; for now we clip).
            var availBandHeight = Math.Max(6, footerTop - bandTop);

            // Separator rule: a short line (~1.5") at the left of the content column.
            var sepWidth = Math.Min(2 * 72 * PxPerPoint, _contentWidth * 0.4);
            _noteSeparators.Add((_contentLeft, _contentLeft + sepWidth, bandTop));

            var y = bandTop + 4;
            foreach (var id in ids)
            {
                var note = _doc.Footnotes[id];
                // DB3: compute display number from NoteNumberingOptions.
                var displayNum = ComputeNoteDisplayNumber(seqIndex, opts);
                seqIndex++;

                var content = note.Content.Count > 0
                    ? note.Content
                    : (IReadOnlyList<Paragraph>)new List<Paragraph> { new Paragraph(string.Empty) };

                // DB2: only emit if still within the available band (clip overflow at footerTop).
                if (y < bandTop + availBandHeight)
                {
                    y = LayoutNoteContent(displayNum, content, _contentLeft, y, _contentWidth);
                    // DB2: clamp y to band bottom so subsequent notes don't escape past the footer.
                    y = Math.Min(y, bandTop + availBandHeight);
                }
            }
        }
    }

    /// <summary>
    /// AV-NOTERENDER (endnotes): renders an "Endnotes" heading + separator, then the numbered endnote
    /// texts, in a synthetic section after the last body page. The section's vertical extent is recorded
    /// in <see cref="_endnoteExtentDip"/> so the scrollable content height reserves room for it.
    /// <para>
    /// DB3: endnote numbers use <see cref="ComputeNoteDisplayNumber"/> with the document's
    /// <see cref="TextDocument.EndnoteNumbering"/> options (Word defaults to LowerRoman for endnotes).
    /// </para>
    /// </summary>
    private void BuildEndnoteItems()
    {
        if (_doc.Endnotes.Count == 0) return;

        // Start just below the last body page (in page-space). The last page's bottom edge:
        var lastPageBottom = _surfacePlan.PageTopDip(_pageCount - 1) + _pageHeightPx;
        var startY = lastPageBottom + PageGap + _marginTopDip * 0.25;

        var headingFmt = RunFormatting.Default with { FontSizePt = NoteFontSizePt + 2, Bold = true };
        var headingH = Math.Max(1, Build("Endnotes", headingFmt).Height);

        // Heading.
        _noteItems.Add(new NoteRenderItem { Text = "Endnotes", Fmt = headingFmt, X = _contentLeft, Y = startY });
        var y = startY + headingH + 2;

        // Separator rule beneath the heading.
        _noteSeparators.Add((_contentLeft, _contentLeft + _contentWidth, y));
        y += 6;

        // DB3: endnote numbering options — Word defaults to LowerRoman; users may override.
        var opts     = _doc.EndnoteNumbering;
        var seqIndex = Math.Max(1, opts.StartAt); // 1-based display sequence counter

        foreach (var id in _doc.Endnotes.Keys.OrderBy(k => k))
        {
            var note = _doc.Endnotes[id];
            // DB3: compute display number from EndnoteNumbering options.
            var displayNum = ComputeNoteDisplayNumber(seqIndex, opts);
            seqIndex++;
            y = LayoutNoteContent(displayNum,
                note.Content.Count > 0 ? note.Content : new List<Paragraph> { new Paragraph(string.Empty) },
                _contentLeft, y, _contentWidth);
            y += 2; // small gap between endnotes
        }

        // Record how far past the last body page the endnotes section extends.
        _endnoteExtentDip = Math.Max(0, y - lastPageBottom) + DeskPadding;
    }

    // Layout-pass mutable state for paged layout (reset at start of Relayout).
    private double _layoutContentY;
    private double _layoutTextAreaHeight;

    /// <summary>
    /// Converts a content-space Y (0 = first line of text area, increasing downward) to a
    /// page-space Y (the actual pixel Y in the control's coordinate system).
    /// <para>In <see cref="DocumentViewMode.PrintLayout"/>:</para>
    /// Content wraps line-granularly: a line that starts past the current page's bottom
    /// margin is pushed to the top of the next page.
    /// Formula: pageIndex = floor(contentY / textAreaHeight)
    ///          pageSpaceY = DeskPadding + pageIndex*(pageHeightPx+gap) + marginTopDip + (contentY - pageIndex*textAreaHeight)
    /// <para>In <see cref="DocumentViewMode.WebLayout"/> / <see cref="DocumentViewMode.Draft"/>:</para>
    /// No pagination — pageSpaceY = marginTopDip + contentY (simple offset by the top inset).
    /// </summary>
    private double ContentYToPageSpaceY(double contentY)
    {
        return _surfacePlan.ContentYToPageSpaceY(contentY, _colCount);
    }

    /// <summary>
    /// Derives the zero-based page index from a page-space Y coordinate.
    /// Inverse of <see cref="ContentYToPageSpaceY"/>.
    /// In Web/Draft modes always returns 0 (single continuous page).
    /// </summary>
    private int PageIndexFromPageSpaceY(double pageSpaceY)
    {
        return _surfacePlan.PageIndexFromPageSpaceY(pageSpaceY);
    }

    /// <summary>
    /// Advances _layoutContentY to the next page boundary if the line of <paramref name="lineHeight"/>
    /// would overflow the current page's text area. Returns the content Y at which the line should start.
    /// <para>
    /// DB1: the effective text-area height on a page is reduced by the footnote band height for that page
    /// (stored in <see cref="_footnoteBandHeightByPage"/>), so body text reflows to the NEXT page before
    /// it would encroach on the footnote band. Computed per-page so pages without footnotes are unaffected.
    /// </para>
    /// </summary>
    private double ReserveContentY(double lineHeight)
    {
        if (_layoutTextAreaHeight <= 0) return _layoutContentY;

        // DB1: compute the 0-based page (slot) index and its per-page footnote reservation.
        var slot = (int)(_layoutContentY / _layoutTextAreaHeight);
        var pageIndex = slot / _colCount;
        var bandReservation = _footnoteBandHeightByPage.TryGetValue(pageIndex, out var bh) ? bh : 0.0;
        // effectiveHeight = page text area minus footnote band on that page.
        var effectiveHeight = Math.Max(lineHeight + 1, _layoutTextAreaHeight - bandReservation);

        var posInPage = _layoutContentY % _layoutTextAreaHeight;
        if (posInPage > 0 && posInPage + lineHeight > effectiveHeight)
        {
            // Push to the top of the next page.
            _layoutContentY += (_layoutTextAreaHeight - posInPage);
        }
        return _layoutContentY;
    }

    /// <summary>
    /// Returns the content Y at which the next content of <paramref name="lineHeight"/> would start,
    /// applying the same page-break logic as <see cref="ReserveContentY"/> but WITHOUT mutating
    /// <c>_layoutContentY</c>.  Used to compute the post-break paragraph-anchor Y for floating images
    /// before the layout loop has consumed any lines.
    /// </summary>
    private double PeekFirstLineContentY(double lineHeight = 1)
    {
        if (_layoutTextAreaHeight <= 0)
            return _layoutContentY;

        // DB1: mirror the per-page reservation used by ReserveContentY.
        var slot = (int)(_layoutContentY / _layoutTextAreaHeight);
        var pageIndex = slot / _colCount;
        var bandReservation = _footnoteBandHeightByPage.TryGetValue(pageIndex, out var bh) ? bh : 0.0;
        var effectiveHeight = Math.Max(lineHeight + 1, _layoutTextAreaHeight - bandReservation);

        var posInPage = _layoutContentY % _layoutTextAreaHeight;
        if (posInPage > 0 && posInPage + lineHeight > effectiveHeight)
            return _layoutContentY + (_layoutTextAreaHeight - posInPage);
        return _layoutContentY;
    }

    // ── AV-WRAP: wrap-exclusion helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Advances <c>_layoutContentY</c> past any TopAndBottom exclusion zones that would overlap
    /// a line of <paramref name="estimatedLineHeight"/> at the current position.
    /// Loops until no TopAndBottom zone overlaps, capping at 200 iterations to prevent infinite loops
    /// for pathological documents.
    /// Only active when there are TopAndBottom exclusions registered.
    /// BB2: In multi-column layout a TopAndBottom float blocks the entire page-width Y-band, so we
    /// advance to the LAST column on the affected page (making all columns skip past the float's Y-band).
    /// </summary>
    private void AdvancePastTopAndBottomExclusions(double estimatedLineHeight)
    {
        if (_wrapExclusions.Count == 0) return;
        for (var guard = 0; guard < 200; guard++)
        {
            var peekContentY  = PeekFirstLineContentY(estimatedLineHeight);
            var peekPageSpaceY = ContentYToPageSpaceY(peekContentY);
            var wrapColumnWidth = _colCount > 1 ? _colWidth : _contentWidth;
            var exclusionBottom = DocumentViewLayoutPlanner.BuildTopAndBottomWrapExclusionBottom(
                _wrapExclusions,
                peekPageSpaceY,
                estimatedLineHeight,
                _contentLeft,
                _colCount,
                wrapColumnWidth,
                _colGap);
            if (exclusionBottom is null) break; // no overlap: done

            var targetContentY = DocumentViewLayoutPlanner.BuildContentYAfterTopAndBottomWrapExclusion(
                _surfacePlan,
                _layoutContentY,
                peekContentY,
                exclusionBottom.Value,
                _colCount);

            if (targetContentY <= _layoutContentY)
                break; // safety: do not regress
            _layoutContentY = targetContentY;
        }
    }

    private void LayoutParagraphPaged(int blockIndex, Paragraph paragraph, double textWidth, double leftInset = 0, string? marker = null)
    {
        var rawCells = IsEditable(paragraph) ? ParaCells(paragraph) : FallbackCells(paragraph.PlainText);
        // Resolve named-style formatting for display only; editing re-derives raw cells from the model.
        var cells = paragraph.StyleId is null
            ? rawCells
            : rawCells.Select(c => c with { Fmt = ResolveRunFmt(c.Fmt, paragraph) }).ToList();
        var reviewPolicy = CurrentReviewDisplayPolicy;
        var pf = ResolveParagraphFmt(paragraph);
        var alignment = pf.Alignment;
        var spaceAfter = pf.SpaceAfterPt * PxPerPoint;

        // Paragraph indents: left/right reduce available width; first-line applies only to line 0.
        var indentLeft  = pf.IndentLeftPt  * PxPerPoint;
        var indentRight = pf.IndentRightPt * PxPerPoint;
        var indentFirst = pf.FirstLineIndentPt * PxPerPoint; // positive = first-line, negative = hanging

        // Total left offset = list inset (already in leftInset) + paragraph indent.
        var paraLeftInset = leftInset + indentLeft;
        // Available width shrinks by both left+right paragraph indents.
        var availableWidth = Math.Max(60, textWidth - leftInset - indentLeft - indentRight);

        _layoutContentY += pf.SpaceBeforePt * PxPerPoint;

        // Collect floating images AND shapes AFTER the SpaceBeforePt advance, using the post-break
        // first-line content Y so that VerticalAnchor.Paragraph floats are anchored to where the
        // paragraph's first line ACTUALLY lands (correct page after any page-break, correct Y after
        // SpaceBefore). PeekFirstLineContentY is non-mutating — it simulates ReserveContentY without
        // side effects.
        //
        // VV1 fix (extends TT1): compute the paragraph's first-line height by taking the MAX glyph
        // height over ALL cells in the paragraph (an over-estimate: at most includes later-line cells,
        // but that only makes Peek break EARLIER, never later — so it is safe).
        //
        // The prior TT1 code took only the FIRST character of the FIRST text run (double break), which
        // under-estimates when line 0's tallest run is not its first character (e.g. small "see " then
        // a 24pt word).  EmitLinePaged uses max(heights[from..to)) over the line's cells; mirroring
        // that over ALL cells (safe upper bound) ensures Peek breaks whenever the real first-line
        // ReserveContentY would.  Empty paragraphs (no cells) fall back to the default line height.
        var firstLineNaturalH = DefaultFontSizePt * PxPerPoint * 1.3; // default / fallback
        foreach (var run in paragraph.Runs)
        {
            if (run.Image is not null || run.Shape is not null) continue; // skip non-text
            foreach (var ch in run.Text)
            {
                var h = Build(ch.ToString(), ResolveRunFmt(run.Formatting, paragraph)).Height;
                if (h > firstLineNaturalH) firstLineNaturalH = h;
                // VV1: do NOT break — scan all chars across ALL text runs (max over all cells).
            }
            // VV1: do NOT break — scan all runs.
        }
        var firstLineHeight = ApplyLineSpacing(firstLineNaturalH, pf);
        var anchorContentY = PeekFirstLineContentY(firstLineHeight);
        CollectFloatingObjects(blockIndex, paragraph, anchorContentY);

        if (marker is not null)
        {
            var markerFmt = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Formatting : RunFormatting.Default;
            var markerWidth = Build(marker, markerFmt).WidthIncludingTrailingWhitespace;
            // Place the marker at the current content-space Y converted to page-space.
            var markerY = ContentYToPageSpaceY(_layoutContentY);
            // AV-COL: resolve column X for the list marker (same column as the paragraph's first line).
            var markerSlot     = _layoutTextAreaHeight > 0 ? (int)(_layoutContentY / _layoutTextAreaHeight) : 0;
            var markerColIndex = markerSlot % _colCount;
            var markerColLeft  = _contentLeft + markerColIndex * (_colWidth + _colGap);
            _markers.Add((markerColLeft + paraLeftInset - markerWidth - 6, markerY, marker, markerFmt));
        }

        // Break the cell stream into wrapped lines.
        var lineStart = 0;
        var i = 0;
        var lineWidth = 0.0;
        var lastBreak = -1; // index of a space cell we can wrap after
        var measured = new double[cells.Count];
        var heights = new double[cells.Count];
        // AV-TAB: default tab interval from page settings (points → DIP).
        var defaultTabStopPt = Math.Max(1, _doc.Page.DefaultTabStopPt);

        for (var c = 0; c < cells.Count; c++)
        {
            if (cells[c].Ch == '\t')
            {
                // AV-TAB: tab width is determined lazily in the wrapping loop (depends on pen pos).
                // Use 0 here; the wrapping loop fills in the real value via ComputeTabMeasuredWidth.
                measured[c] = 0;
                heights[c] = DefaultFontSizePt * PxPerPoint * 1.3; // fallback height
            }
            else
            {
                var decision = reviewPolicy.RevisionDecision(cells[c].Revision);
                if (decision.IsTextVisible)
                {
                    var ft = Build(cells[c].Ch.ToString(), cells[c].Fmt);
                    measured[c] = ft.WidthIncludingTrailingWhitespace;
                    heights[c] = ft.Height;
                }
            }
        }

        var lineIndex = 0;

        // AV-WRAP: pre-compute the column geometry for exclusion queries.
        // These are the same values used in EmitLinePaged to identify the column.
        var wrapColW = _colCount > 1 ? _colWidth : _contentWidth;

        // AV-WRAP: Helper that peeks the page-space Y of the CURRENT line-being-built, then
        // queries Square/Tight exclusions to get the adjusted wrap budget for that line.
        // This mirrors the values EmitLinePaged will compute when actually emitting the line.
        // estimatedH: estimated line height (max of heights[lineStart..i)).
        double PeekLineAvail(double estimatedH, int fromIdx, double baseAlignWidth)
        {
            if (_wrapExclusions.Count == 0) return baseAlignWidth;
            var peekContentY   = PeekFirstLineContentY(estimatedH);
            var peekPageSpaceY = ContentYToPageSpaceY(peekContentY);
            // Peek the column for the line (mirrors EmitLinePaged slot logic).
            var slot       = _layoutTextAreaHeight > 0 ? (int)(peekContentY / _layoutTextAreaHeight) : 0;
            var colIdx     = slot % _colCount;
            var cLeft      = _contentLeft + colIdx * (_colWidth + _colGap);
            var exclusion  = DocumentViewLayoutPlanner.BuildSquareTightWrapExclusion(
                _wrapExclusions,
                peekPageSpaceY,
                estimatedH,
                cLeft,
                wrapColW);
            return Math.Max(20, baseAlignWidth - exclusion.LeftDeltaDip - exclusion.RightShrinkDip);
        }

        while (i < cells.Count)
        {
            // OO2/OO3 fix: for each line compute how much of availableWidth is consumed by
            // the per-line left indent BEYOND paraLeftInset.  The wrapping budget (lineAvail)
            // is already reduced for the first-line positive indent; hanging-indent continuation
            // lines also need the same reduction so they do not overshoot the right margin.
            // lineExtraInset is the extra indent relative to paraLeftInset for this line:
            //   • line 0 + positive first-line indent  → indentFirst  (normal first-line indent)
            //   • line > 0 + hanging indent (negative) → -indentFirst (continuation shifted right)
            //   • everything else                      → 0
            // BP1 fix: moved BEFORE the tab check so the full margin-relative pen offset
            // (lineWidth + paraLeftInset + lineExtraInset) is available for ComputeTabMeasuredWidth.
            var lineExtraInset = (lineIndex == 0 && indentFirst > 0) ? indentFirst :
                                 (lineIndex  > 0 && indentFirst < 0) ? -indentFirst : 0.0;

            // AV-TAB: resolve tab advance lazily at the wrapping loop so measured[] reflects the
            // actual pen position at the time each tab is encountered on its line.
            // BP1 fix: pass pen position from the MARGIN (lineWidth + full indent) so tab stops
            // compare against OOXML positions (margin-relative), not the indented text origin.
            if (cells[i].Ch == '\t' && reviewPolicy.IsRevisionTextVisible(cells[i].Revision))
                measured[i] = ComputeTabMeasuredWidth(lineWidth + paraLeftInset + lineExtraInset, pf, defaultTabStopPt);

            if (cells[i].Ch == ' ')
                lastBreak = i;

            // Effective alignment / wrap width for this line so the right edge always lands at
            // the right margin regardless of indent variant.
            var lineAlignWidth = availableWidth - lineExtraInset;
            // AV-WRAP: reduce lineAvail further for any Square/Tight exclusion zones.
            var lineH2 = DefaultFontSizePt * PxPerPoint * 1.3;
            for (var c2 = lineStart; c2 <= i && c2 < heights.Length; c2++)
                if (heights[c2] > lineH2) lineH2 = heights[c2];
            var lineAvail = PeekLineAvail(lineH2, lineStart, lineAlignWidth);

            if (lineWidth + measured[i] > lineAvail && i > lineStart)
            {
                var breakAt = lastBreak >= lineStart ? lastBreak + 1 : i;
                // AV-WRAP: push past any TopAndBottom exclusion zones before emitting.
                if (_wrapExclusions.Count > 0)
                    AdvancePastTopAndBottomExclusions(lineH2);
                EmitLinePaged(blockIndex, cells, measured, heights, lineStart, breakAt, alignment,
                    lineAlignWidth, paraLeftInset + lineExtraInset, pf);
                lineIndex++;
                lineStart = breakAt;
                lineWidth = 0;
                lastBreak = -1;
                // AV-TAB: recompute tab widths in the partial accumulation so they use the
                // new line's pen position (tabs reset to a fresh pen at the new lineStart).
                // BP1 fix: the new lineIndex may change lineExtraInset — recompute it now.
                var newLineExtraInset = (lineIndex == 0 && indentFirst > 0) ? indentFirst :
                                       (lineIndex  > 0 && indentFirst < 0) ? -indentFirst : 0.0;
                for (var k = lineStart; k < i; k++)
                {
                    if (cells[k].Ch == '\t' && reviewPolicy.IsRevisionTextVisible(cells[k].Revision))
                        measured[k] = ComputeTabMeasuredWidth(lineWidth + paraLeftInset + newLineExtraInset, pf, defaultTabStopPt);
                    lineWidth += measured[k];
                }
            }

            lineWidth += measured[i];
            i++;
        }

        {
            // Last (or only) line of the paragraph.
            var lineExtraInset = (lineIndex == 0 && indentFirst > 0) ? indentFirst :
                                 (lineIndex  > 0 && indentFirst < 0) ? -indentFirst : 0.0;
            var lineAlignWidth = availableWidth - lineExtraInset;
            // AV-WRAP: push past any TopAndBottom exclusion zones before emitting the last line.
            if (_wrapExclusions.Count > 0)
            {
                var lineH = DefaultFontSizePt * PxPerPoint * 1.3;
                for (var c2 = lineStart; c2 < cells.Count; c2++)
                    if (heights[c2] > lineH) lineH = heights[c2];
                AdvancePastTopAndBottomExclusions(lineH);
            }
            EmitLinePaged(blockIndex, cells, measured, heights, lineStart, cells.Count, alignment,
                lineAlignWidth, paraLeftInset + lineExtraInset, pf, isLast: true);
        }
        _layoutContentY += spaceAfter;
    }

    private void EmitLinePaged(
        int blockIndex,
        IReadOnlyList<Cell> cells,
        double[] measured,
        double[] heights,
        int from,
        int to,
        TextAlignment alignment,
        double availableWidth,
        double leftInset,
        ParagraphFormatting pf,
        bool isLast = false)
    {
        double lineWidth = 0;
        // Natural line height: use the tallest glyph but also respect line-spacing rule.
        double naturalHeight = DefaultFontSizePt * PxPerPoint * 1.3;
        for (var c = from; c < to; c++)
        {
            lineWidth += measured[c];
            if (heights[c] > naturalHeight)
                naturalHeight = heights[c];
        }

        // Apply line-spacing rule from paragraph formatting.
        double lineHeight = ApplyLineSpacing(naturalHeight, pf);

        // Ensure the whole line fits on one page (push to next page if it overflows).
        var contentY = ReserveContentY(lineHeight);
        var pageSpaceY = ContentYToPageSpaceY(contentY);

        // Word-spacing expansion for justify (last line stays left).
        // OO1 fix: exclude the trailing space from BOTH the visible-width sum and the gap-add loop.
        // breakAt = lastBreak+1, so [from, to) includes the trailing space at index lastBreak.
        // That space is invisible at the right edge: it must not receive a wordGap, and its natural
        // width must not count against the slack we distribute.  We find the last non-space cell,
        // compute visible width = sum of measured[from..lastNonSpaceIdx] inclusive, and distribute
        // (availableWidth - visibleWidth) only among inter-word spaces strictly before lastNonSpaceIdx.
        double wordGap = 0;
        int lastNonSpaceIdx = from - 1; // sentinel: no non-space found
        if (alignment == TextAlignment.Justify && !isLast)
        {
            // Find the index of the last non-space cell in [from, to).
            for (var c = to - 1; c >= from; c--)
            {
                if (cells[c].Ch != ' ')
                {
                    lastNonSpaceIdx = c;
                    break;
                }
            }
            if (lastNonSpaceIdx >= from)
            {
                // Visible line width: chars from `from` up to and including `lastNonSpaceIdx`
                // (excludes trailing spaces that follow the last word).
                var visibleWidth = 0.0;
                for (var c = from; c <= lastNonSpaceIdx; c++)
                    visibleWidth += measured[c];
                // Count inter-word spaces strictly between the first and last non-space cell.
                var spaceCount = 0;
                for (var c = from; c < lastNonSpaceIdx; c++)
                    if (cells[c].Ch == ' ')
                        spaceCount++;
                if (spaceCount > 0)
                    wordGap = Math.Max(0, availableWidth - visibleWidth) / spaceCount;
            }
        }

        // AV-COL: compute the left edge of the column this line lands in.
        // slot = which column-slot (0-based across all pages); colIndex = slot % _colCount.
        var lineSlot     = _layoutTextAreaHeight > 0 ? (int)(contentY / _layoutTextAreaHeight) : 0;
        var lineColIndex = lineSlot % _colCount;
        var colLeft      = _contentLeft + lineColIndex * (_colWidth + _colGap);
        var colW         = _colCount > 1 ? _colWidth : _contentWidth;

        // AV-WRAP: apply Square/Tight exclusion zones for this line.
        // TopAndBottom is handled in LayoutParagraphPaged (advances _layoutContentY before we arrive here).
        var lineExclusion = _wrapExclusions.Count > 0
            ? DocumentViewLayoutPlanner.BuildSquareTightWrapExclusion(
                _wrapExclusions,
                pageSpaceY,
                lineHeight,
                colLeft,
                colW)
            : new DocumentFloatingLineExclusionPlan(0, 0);
        var wrapLeftDelta = lineExclusion.LeftDeltaDip;
        var wrapRightShrink = lineExclusion.RightShrinkDip;
        var effectiveLeftInset = leftInset + wrapLeftDelta;
        var effectiveWidth     = availableWidth - wrapLeftDelta - wrapRightShrink;
        if (effectiveWidth < 20) effectiveWidth = 20; // safety floor

        // AV-TAB: detect whether this line contains any tab characters.
        var lineHasTabs = false;
        for (var c = from; c < to; c++)
            if (cells[c].Ch == '\t') { lineHasTabs = true; break; }

        // Content origin: absolute left edge where pen-position 0 begins (before alignment offset).
        // Tab stops are measured from this origin, not from the alignment-shifted x.
        var contentOriginX = colLeft + effectiveLeftInset;

        // BP1 fix: OOXML w:tab/@w:pos is measured from the LEFT MARGIN (colLeft), not from the
        // indented paragraph origin.  Ruler.cs confirms: tab markers are placed at
        //   contentStart + PointsToDip(tab.PositionPt)
        // where contentStart = pageX + MarginLeft (excludes IndentLeftPt).
        // So the tab-stop coordinate origin is colLeft + wrapLeftDelta, NOT contentOriginX.
        // effectiveLeftInset = leftInset + wrapLeftDelta  →  tabOriginX = contentOriginX - leftInset.
        var tabOriginX = contentOriginX - leftInset;
        var alignOffset    = AlignmentOffset(alignment, effectiveWidth, lineWidth, isLast);

        // For lines without tabs, keep the existing simple path (no extra overhead).
        // For lines with tabs, alignment applies only to the pre-tab prefix segment; subsequent
        // segments are pinned absolutely to their stop positions.
        var x = contentOriginX + (lineHasTabs ? 0.0 : alignOffset);

        if (!string.IsNullOrWhiteSpace(pf.ShadingColorHex) || pf.Border is not null)
        {
            const double decorationPad = 2.0;
            _paragraphDecorations.Add((
                new Rect(
                    contentOriginX - decorationPad,
                    pageSpaceY,
                    Math.Max(1, effectiveWidth + decorationPad * 2),
                    lineHeight),
                pf.ShadingColorHex,
                pf.Border));
        }

        // AV-TAB: default tab interval for this line (from document page settings).
        var lineDefaultTabStopPt = Math.Max(1.0, _doc.Page.DefaultTabStopPt);

        // AV-TAB: pre-tab alignment offset for lines with tabs — applied to the pre-tab prefix.
        // We compute the pre-tab segment width and centre/right-align it, then the first tab snaps x.
        if (lineHasTabs && alignment != TextAlignment.Left && alignment != TextAlignment.Justify)
        {
            // Find the first tab in [from, to).
            var firstTabIdx = from;
            while (firstTabIdx < to && cells[firstTabIdx].Ch != '\t') firstTabIdx++;
            // Sum the pre-tab segment width.
            var preTabWidth = 0.0;
            for (var c = from; c < firstTabIdx; c++) preTabWidth += measured[c];
            // Apply alignment to pre-tab segment only.
            x += alignment switch
            {
                TextAlignment.Center => Math.Max(0, (effectiveWidth - preTabWidth) / 2),
                TextAlignment.Right  => Math.Max(0, effectiveWidth - preTabWidth),
                _                   => 0.0,
            };
        }

        var reviewPolicy = CurrentReviewDisplayPolicy;
        for (var c = from; c < to; c++)
        {
            if (!reviewPolicy.IsRevisionTextVisible(cells[c].Revision))
            {
                _placed.Add(new PlacedChar(blockIndex, c, x, pageSpaceY, 0, lineHeight, cells[c].Fmt, cells[c].Ch, Sentinel: false, CommentId: cells[c].CommentId, Revision: cells[c].Revision, Link: cells[c].Link, HasFormatRevision: cells[c].FormatRevision is not null));
                continue;
            }

            if (cells[c].Ch == '\t')
            {
                // AV-TAB / BP1 fix: pen position and tab stops are relative to the margin origin
                // (tabOriginX = colLeft + wrapLeftDelta), NOT the indented content origin.
                var penPosInLine = x - tabOriginX;

                // Compute the advance: for left tabs this is straightforward. For center/right tabs the
                // shared planner needs the following segment's measured width.
                var segmentWidth = 0.0;
                double? decimalAlignmentOffset = null;
                for (var k = c + 1; k < to; k++)
                {
                    if (cells[k].Ch == '\t') break;
                    if (decimalAlignmentOffset is null && IsDecimalTabSeparator(cells[k].Ch))
                        decimalAlignmentOffset = segmentWidth;
                    segmentWidth += measured[k];
                }

                // Target X in page space where the segment should land.
                // BP1 fix: use tabOriginX (margin-relative) not contentOriginX (indent-relative).
                var plan = ParagraphTabStopLayoutPlanner.BuildPlacementPlan(
                    penPosInLine,
                    segmentWidth,
                    pf.TabStops,
                    lineDefaultTabStopPt,
                    PxPerPoint,
                    decimalAlignmentOffset);
                var segmentStartX = tabOriginX + plan.SegmentStartDip;

                // Tab glyph occupies the gap from current x to the segment start.
                var tabAdvance = plan.AdvanceDip;
                var tabX = x; // where the '\t' PlacedChar starts

                // Emit leader span if the tab stop has one.
                if (plan.HasLeader)
                    _tabLeaderSpans.Add((tabX, segmentStartX, pageSpaceY, lineHeight, plan.Leader, cells[c].Fmt));

                // Place the tab character with its computed advance width (for caret hit-testing).
                _placed.Add(new PlacedChar(blockIndex, c, tabX, pageSpaceY, tabAdvance, lineHeight, cells[c].Fmt, '\t', Sentinel: false, CommentId: cells[c].CommentId, Revision: cells[c].Revision, Link: cells[c].Link, HasFormatRevision: cells[c].FormatRevision is not null));
                x = segmentStartX;
                continue;
            }

            _placed.Add(new PlacedChar(blockIndex, c, x, pageSpaceY, measured[c], lineHeight, cells[c].Fmt, cells[c].Ch, Sentinel: false, CommentId: cells[c].CommentId, Revision: cells[c].Revision, Link: cells[c].Link, HasFormatRevision: cells[c].FormatRevision is not null));
            x += measured[c];
            // Extra inter-word gap for justify alignment: only for spaces before the last non-space cell.
            if (wordGap > 0 && cells[c].Ch == ' ' && c < lastNonSpaceIdx)
                x += wordGap;
        }

        // End-of-line / end-of-paragraph sentinel carries the caret slot after the last char.
        if (isLast)
            _placed.Add(new PlacedChar(blockIndex, to, x, pageSpaceY, 0, lineHeight, RunFormatting.Default, '\0', Sentinel: true));

        _layoutContentY = contentY + lineHeight;
    }

    /// <summary>
    /// Applies the paragraph line-spacing rule to the natural line height and returns the final
    /// line height to use for layout. Matches Word's line-spacing semantics:
    /// <list type="bullet">
    ///   <item><see cref="LineSpacingRule.Multiple"/> — multiply natural height by <c>LineSpacing</c> (default 1.15).</item>
    ///   <item><see cref="LineSpacingRule.Exact"/> — always use <c>LineHeightPt * PxPerPoint</c> exactly.</item>
    ///   <item><see cref="LineSpacingRule.AtLeast"/> — use <c>LineHeightPt * PxPerPoint</c> as a floor, allow taller glyphs.</item>
    /// </list>
    /// Approximation: we use a 1.2× leading factor on the raw glyph height as the "natural" height
    /// baseline (Avalonia <see cref="FormattedText.Height"/> already includes leading). The
    /// multiplier is applied on top of that.
    /// </summary>
    private static double ApplyLineSpacing(double naturalHeight, ParagraphFormatting pf)
    {
        return pf.LineRule switch
        {
            LineSpacingRule.Exact   => Math.Max(1, pf.LineHeightPt  * PxPerPoint),
            LineSpacingRule.AtLeast => Math.Max(naturalHeight, pf.LineHeightPt * PxPerPoint),
            // Multiple (default): multiply by the line-spacing factor (1.15 Word default).
            _ => naturalHeight * (pf.LineSpacing > 0 ? pf.LineSpacing : 1.15),
        };
    }

    private static double AlignmentOffset(TextAlignment alignment, double textWidth, double lineWidth, bool isLast = false) => alignment switch
    {
        TextAlignment.Center  => Math.Max(0, (textWidth - lineWidth) / 2),
        TextAlignment.Right   => Math.Max(0, textWidth - lineWidth),
        // Justify: last line (or single-line paragraphs) fall back to left.
        TextAlignment.Justify => isLast ? 0 : 0, // x already adjusted by wordGap in caller
        _ => 0,
    };

    /// <summary>
    /// AV-COL-NONTXT: Returns the left edge of the document column that contains a piece of content
    /// at the given <paramref name="contentY"/> (the flowing content-space Y, not page-space Y).
    /// <para>
    /// The snaking column model works by dividing the continuous content-Y axis into slots of height
    /// <see cref="_layoutTextAreaHeight"/>. Slot <c>k</c> maps to column-index <c>k % _colCount</c>
    /// within a page. The left edge of that column is
    /// <c>_contentLeft + colIndex * (_colWidth + _colGap)</c>.
    /// </para>
    /// When <c>_colCount == 1</c> this returns <see cref="_contentLeft"/> unchanged (no regression).
    /// </summary>
    private double ColumnLeftFor(double contentY)
    {
        if (_colCount <= 1 || _layoutTextAreaHeight <= 0)
            return _contentLeft;
        var slot     = (int)(contentY / _layoutTextAreaHeight);
        var colIndex = slot % _colCount;
        return _contentLeft + colIndex * (_colWidth + _colGap);
    }

    // ── AV-TAB: tab-stop resolution helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Computes the measured width to assign to a <c>\t</c> cell in <see cref="LayoutParagraphPaged"/>'s
    /// wrapping loop.  <paramref name="penPosInLine"/> must be the pen distance from the
    /// <em>left margin / column left</em> (i.e. lineWidth + paraLeftInset + lineExtraInset),
    /// matching the coordinate system of <see cref="ParagraphTabStopLayoutPlanner.ResolveNextStop"/>.
    /// The advance is from the current pen to the resolved stop, clamped to at least 1 px.
    /// </summary>
    private static double ComputeTabMeasuredWidth(double penPosInLine, ParagraphFormatting pf, double defaultTabStopPt) =>
        ParagraphTabStopLayoutPlanner.ComputeTabAdvance(
            penPosInLine,
            pf.TabStops,
            defaultTabStopPt,
            PxPerPoint);

    private static bool IsDecimalTabSeparator(char ch) => ch is '.' or ',';

    private void LayoutReadOnlyBlockPaged(int blockIndex, Block block, double textWidth)
    {
        var text = block is Table table ? TablePlainText(table) : block.ToString() ?? "";
        var cells = FallbackCells(text);
        var measured = new double[cells.Count];
        var heights = new double[cells.Count];
        for (var c = 0; c < cells.Count; c++)
        {
            var ft = Build(cells[c].Ch.ToString(), cells[c].Fmt);
            measured[c] = ft.WidthIncludingTrailingWhitespace;
            heights[c] = ft.Height;
        }

        EmitLinePaged(blockIndex, cells, measured, heights, 0, cells.Count, TextAlignment.Left, textWidth, 0, ParagraphFormatting.Default, isLast: true);
    }

    private static string TablePlainText(Table table) =>
        string.Join("  |  ", table.Rows.SelectMany(r => r.Cells).Select(c => c.PlainText));

    // ---- Table rendering (grid + modal cell text editing) ----------------------------------------

    private void LayoutTablePaged(int blockIndex, Table table, double textWidth)
    {
        var cols = Math.Max(1, table.ColumnCount);
        var colWidths = ComputeColumnWidths(table, cols, textWidth);
        // AV-COL-NONTXT AG1: build columnLeft[] as offsets from _contentLeft (column-0 base).
        // Each row re-applies the per-row column shift after reserving its Y slot.
        var colOffsets = new double[cols + 1]; // relative to _contentLeft
        var running = 0.0;
        for (var c = 0; c < cols; c++)
        {
            colOffsets[c] = running;
            running += colWidths[c];
        }
        colOffsets[cols] = running;

        const double pad = 5;
        var borders = table.Formatting.Borders || _showTableGridlines;
        var headerOffset = table.Formatting.HeaderRow ? 1 : 0;
        // AV-TBL: glyphOffset is unique within this table block and is used as PlacedChar.Offset so
        // TryGetCaretRect can match (Block == tableBlockIndex && Offset == glyphOffset).
        var glyphOffset = 0;

        // AV-TBL5-VRENDER-VMERGE: pre-compute every row's height up front. A vertical-merge Restart
        // cell needs the TOTAL height of all rows it spans to vertically align its content within the
        // whole merged region rather than just its own (first) row — that total isn't known yet when
        // the row loop below reaches the Restart row, since later rows haven't been measured. This
        // pass mirrors the exact rowHeight computation the main loop performs (same cell wrapping),
        // so results are identical — just computed early enough to sum across rows.
        var rowHeights = new double[table.Rows.Count];
        for (var pr = 0; pr < table.Rows.Count; pr++)
        {
            var prRow = table.Rows[pr];
            var prIsHeader = table.Formatting.HeaderRow && pr == 0;
            var prRowHeight = Build("Ag", RunFormatting.Default).Height + 2 * pad;
            var prCol = 0;
            foreach (var cell in prRow.Cells)
            {
                if (prCol >= cols)
                    break;
                var prSpan = Math.Clamp(cell.GridSpan <= 0 ? 1 : cell.GridSpan, 1, cols - prCol);
                double prCellWidth = 0;
                for (var s = 0; s < prSpan; s++)
                    prCellWidth += colWidths[prCol + s];

                var prFmt = cell.Paragraphs.Count > 0 && cell.Paragraphs[0].Runs.Count > 0
                    ? cell.Paragraphs[0].Runs[0].Formatting
                    : RunFormatting.Default;
                if (prIsHeader)
                    prFmt = prFmt with { Bold = true };

                var prInnerW = Math.Max(10, prCellWidth - 2 * pad);
                var prLines = cell.Paragraphs.Count > 0
                    ? cell.Paragraphs.SelectMany(p => WrapCellLines(p.PlainText, prFmt, prInnerW)).ToList()
                    : WrapCellLines(string.Empty, prFmt, prInnerW);
                var prCellHeight = prLines.Sum(l => l.Height) + 2 * pad;
                if (prCellHeight > prRowHeight)
                    prRowHeight = prCellHeight;

                prCol += prSpan;
            }
            rowHeights[pr] = prRowHeight;
        }

        var plannedPagesByFirstSourceRow = _viewMode == DocumentViewMode.PrintLayout
            ? DocumentViewLayoutPlanner.BuildTablePaginationPlan(table, _doc.Page).Pages
                .Where(page => page.PageNumber > 1 && page.SourceRowIndexes.Count > 0)
                .ToDictionary(page => page.SourceRowIndexes[0])
            : new Dictionary<int, DocumentTablePaginationPagePlan>();

        double NextPhysicalPageStartContentY()
        {
            if (_layoutTextAreaHeight <= 0)
                return _layoutContentY;

            var slotsPerPage = Math.Max(1, _colCount);
            var currentSlot = Math.Max(0, (int)(_layoutContentY / _layoutTextAreaHeight));
            var currentPage = currentSlot / slotsPerPage;
            var currentPageStart = currentPage * slotsPerPage * _layoutTextAreaHeight;
            return _layoutContentY <= currentPageStart + 0.5
                ? _layoutContentY
                : (currentPage + 1) * slotsPerPage * _layoutTextAreaHeight;
        }

        void RenderTableRow(int r, double? reservedContentY = null)
        {
            var row = table.Rows[r];
            var isHeader = table.Formatting.HeaderRow && r == 0;
            var isBand = table.Formatting.BandedRows
                && !isHeader
                && TableBanding.IsBandedBodyRow(r, table.Formatting.HeaderRow);

            // AV-TBL: carry the TableCell model reference and actual column index so we can emit
            // per-paragraph, per-character cell-aware PlacedChars for caret routing.
            // BE2: CellParas holds wrapped lines per-paragraph (outer list = para, inner = wrapped lines).
            var measured = new List<(TableCell Cell, int StartCol, int Span, List<List<(double Height, List<(char Ch, double W)> Chars)>> CellParas, RunFormatting Fmt)>();
            var rowHeight = Build("Ag", RunFormatting.Default).Height + 2 * pad;
            var col = 0;
            foreach (var cell in row.Cells)
            {
                if (col >= cols)
                    break;
                var span = Math.Clamp(cell.GridSpan <= 0 ? 1 : cell.GridSpan, 1, cols - col);
                double cellWidth = 0;
                for (var s = 0; s < span; s++)
                    cellWidth += colWidths[col + s];

                var fmt = cell.Paragraphs.Count > 0 && cell.Paragraphs[0].Runs.Count > 0
                    ? cell.Paragraphs[0].Runs[0].Formatting
                    : RunFormatting.Default;
                if (isHeader)
                    fmt = fmt with { Bold = true };

                // BE2: wrap each cell paragraph independently so multi-paragraph cells render on
                // separate visual lines instead of collapsing onto one line via a '\n' glyph.
                var innerW = Math.Max(10, cellWidth - 2 * pad);
                var cellParas = cell.Paragraphs.Count > 0
                    ? cell.Paragraphs.Select(p => WrapCellLines(p.PlainText, fmt, innerW)).ToList()
                    : new List<List<(double Height, List<(char Ch, double W)> Chars)>> { WrapCellLines(string.Empty, fmt, innerW) };
                var lines = cellParas.SelectMany(pl => pl).ToList(); // flattened for height calc
                var cellHeight = lines.Sum(l => l.Height) + 2 * pad;
                if (cellHeight > rowHeight)
                    rowHeight = cellHeight;

                measured.Add((cell, col, span, cellParas, fmt));
                col += span;
            }

            // Treat the row as a unit: reserve space on the current page (or push to next).
            var rowContentY = reservedContentY ?? ReserveContentY(rowHeight);
            var rowPageSpaceY = ContentYToPageSpaceY(rowContentY);

            // AV-COL-NONTXT AG1: use the column band that this row's content-Y falls in.
            var rowColLeft = ColumnLeftFor(rowContentY);

            foreach (var (cellModel, startCol, span, cellParas, fmt) in measured)
            {
                double cellWidth = 0;
                for (var s = 0; s < span; s++)
                    cellWidth += colWidths[startCol + s];
                var cellX = rowColLeft + colOffsets[startCol];
                var rect = new Rect(cellX, rowPageSpaceY, cellWidth, rowHeight);
                // AV-TBL4: per-cell ShadingColorHex overrides table-style fills; header/band still apply as fallback.
                IBrush? fill = ResolveCellFill(cellModel, isHeader, isBand);
                var cellBorderPlan = TableCellBorderVisualPlanner.Build(cellModel.Borders, PxPerPoint);
                _rects.Add((rect, fill, borders, cellBorderPlan.HasVisibleEdges ? cellBorderPlan : null));
                _cellHits.Add((rect, blockIndex, r, startCol));

                // AV-TBL5-VRENDER: per-cell vertical alignment offset within the row.
                // cellAvailableHeight = row interior height (row height minus top+bottom padding).
                // contentHeight = sum of this cell's laid-out line heights.
                // For single-row cells (no rowspan), the available height is the full row interior.
                //
                // AV-TBL5-VRENDER-VMERGE: for a cell that STARTS a vertical merge (Restart), Word
                // aligns the content within the height of the WHOLE merged span, not just the first
                // row. Sum the pre-computed heights of every row this cell spans (this row plus each
                // consecutive VerticalMerge.Continue cell below it at the same grid column) and use
                // that as the available height instead of the single rowHeight. A non-merged cell
                // (span 1) is unaffected — cellAvailableHeight still reduces to rowHeight - 2*pad.
                //
                // Paginated cells (content split across pages): ReserveContentY treats the row as a
                // unit so the whole row lands on one page; no per-page split of a single row occurs,
                // so the vAlign offset is safe to apply without extra pagination logic. If a merged
                // span crosses a page break, the span-height sum still uses each row's full measured
                // height (best-effort — matches the existing per-row pagination behavior rather than
                // clipping the merged region further).
                var cellLines = cellParas.SelectMany(pl => pl).ToList();
                var contentHeight = cellLines.Sum(l => l.Height);
                var mergedSpanHeight = rowHeight;
                if (cellModel.VerticalMerge == VerticalMergeState.Restart)
                {
                    mergedSpanHeight = 0;
                    for (var mr = r; mr < table.Rows.Count; mr++)
                    {
                        if (mr > r && GetCellModelGridCol(table, mr, startCol)?.VerticalMerge != VerticalMergeState.Continue)
                            break;
                        mergedSpanHeight += mr == r ? rowHeight : rowHeights[mr];
                    }
                }
                var cellAvailableHeight = mergedSpanHeight - 2 * pad;
                var vAlignOffset = cellModel.VerticalAlignment switch
                {
                    TableCellVerticalAlignment.Center =>
                        Math.Max(0.0, (cellAvailableHeight - contentHeight) / 2.0),
                    TableCellVerticalAlignment.Bottom =>
                        Math.Max(0.0, cellAvailableHeight - contentHeight),
                    _ => 0.0  // Top (default)
                };
                var contentTopY = rowPageSpaceY + pad + vAlignOffset;

                var ty = contentTopY;
                // BE2+BE1: iterate paragraphs independently — each paragraph's wrapped lines render
                // on their own visual Y band, so multi-paragraph cells never collapse to one line.
                // BE1: emit one sentinel PlacedChar per paragraph so the caret is findable at the
                // end of every paragraph, not just the last one.
                for (var pIdx = 0; pIdx < cellParas.Count; pIdx++)
                {
                    var paraLines = cellParas[pIdx];
                    var paraCharOffset = 0;

                    foreach (var (lineHeight, chars) in paraLines)
                    {
                        var tx = cellX + pad;
                        foreach (var (ch, w) in chars)
                        {
                            _placed.Add(new PlacedChar(blockIndex, glyphOffset, tx, ty, w, lineHeight, fmt, ch,
                                Sentinel: false, CellRow: r, CellCol: startCol, CellParaIdx: pIdx, CellParaOffset: paraCharOffset));
                            glyphOffset++;
                            paraCharOffset++;
                            tx += w;
                        }

                        ty += lineHeight;
                    }

                    // BE1: sentinel at end of this paragraph (at the end of its last visual line).
                    (double Height, List<(char Ch, double W)> Chars)? lastParaLine = paraLines.Count > 0 ? paraLines[^1] : null;
                    var sentinelX = cellX + pad + (lastParaLine.HasValue ? lastParaLine.Value.Chars.Sum(c => c.W) : 0);
                    var sentinelY = lastParaLine.HasValue
                        ? ty - lastParaLine.Value.Height
                        : contentTopY;
                    var sentinelH = lastParaLine.HasValue ? lastParaLine.Value.Height : Build("A", fmt).Height;
                    var sentinelParaOffset = cellModel.Paragraphs.Count > pIdx
                        ? cellModel.Paragraphs[pIdx].PlainText.Length
                        : 0;
                    _placed.Add(new PlacedChar(blockIndex, glyphOffset, sentinelX, sentinelY, 0, sentinelH, fmt, '\0',
                        Sentinel: true, CellRow: r, CellCol: startCol, CellParaIdx: pIdx, CellParaOffset: sentinelParaOffset));
                    glyphOffset++;
                }
            }

            _layoutContentY = rowContentY + rowHeight;
        }

        for (var r = 0; r < table.Rows.Count; r++)
        {
            if (plannedPagesByFirstSourceRow.TryGetValue(r, out var plannedPage))
            {
                var nextPageStart = NextPhysicalPageStartContentY();
                if (nextPageStart > _layoutContentY)
                    _layoutContentY = nextPageStart;

                var openingRows = plannedPage.RenderRows
                    .TakeWhile(row => row.IsRepeatedHeader || row.SourceRowIndex == r)
                    .ToList();
                var openingRowsHeight = openingRows.Sum(row =>
                    row.SourceRowIndex >= 0 && row.SourceRowIndex < rowHeights.Length
                        ? rowHeights[row.SourceRowIndex]
                        : Math.Max(0, row.EstimatedHeightDip));
                _layoutContentY = ReserveContentY(openingRowsHeight);

                foreach (var repeatedHeaderRow in openingRows.Where(row => row.IsRepeatedHeader))
                {
                    if (repeatedHeaderRow.SourceRowIndex >= 0 && repeatedHeaderRow.SourceRowIndex < table.Rows.Count)
                        RenderTableRow(repeatedHeaderRow.SourceRowIndex, _layoutContentY);
                }
            }

            RenderTableRow(r);
        }

        _layoutContentY += 8;
    }

    private void LayoutImageParagraphPaged(int blockIndex, Paragraph paragraph, double textWidth)
    {
        const double gap = 6;
        var alignment = paragraph.Formatting.Alignment;

        // Collect floating images and shapes using the post-break first-line content Y so that
        // VerticalAnchor.Paragraph floats land on the same page as the first inline image.
        //
        // TT1 fix: compute the image paragraph's first-line height (the first inline image's pixel
        // height) and pass it to PeekFirstLineContentY so the page-break probe matches the actual
        // first ReserveContentY call for that image.
        double firstImgLineH = 1; // fallback: default probe height
        foreach (var run in paragraph.Runs)
        {
            if (run.Image is not { IsFloating: false } firstImg) continue;
            var imgH = firstImg.HeightPt > 0 ? firstImg.HeightPt * PxPerPoint : 80;
            firstImgLineH = imgH;
            break;
        }
        var anchorContentY = PeekFirstLineContentY(firstImgLineH);
        CollectFloatingObjects(blockIndex, paragraph, anchorContentY);

        foreach (var run in paragraph.Runs)
        {
            if (run.Image is not { IsFloating: false } image)
                continue; // Skip floating images — they are handled by CollectFloatingObjects.

            var width = image.WidthPt > 0 ? image.WidthPt * PxPerPoint : 120;
            var height = image.HeightPt > 0 ? image.HeightPt * PxPerPoint : 80;
            if (width > textWidth)
            {
                var scale = textWidth / width;
                width = textWidth;
                height *= scale;
            }

            var imgContentY = ReserveContentY(height);
            var imgPageSpaceY = ContentYToPageSpaceY(imgContentY);
            // AV-COL-NONTXT AG2: shift X to the column band that this image's content-Y falls in.
            var x = ColumnLeftFor(imgContentY) + AlignmentOffset(alignment, textWidth, width);
            _images.Add((new Rect(x, imgPageSpaceY, width, height), DecodeBitmap(image)));
            _layoutContentY = imgContentY + height + gap;
        }
    }

    /// <summary>
    /// FO4: lays out a paragraph that contains inline (non-floating) charts, WordArt, or SmartArt.
    /// Each inline object reserves a line-box in the flow (like an inline image) and is stored in
    /// <c>_inlineCharts</c> / <c>_inlineWordArts</c> / <c>_inlineSmartArts</c> for rendering.
    /// A zero-width sentinel <see cref="PlacedChar"/> is emitted so that caret navigation steps over
    /// each object as a single atomic position.  Floating objects anchored to this paragraph are also
    /// collected via the normal FO1-FO3 helpers.
    /// </summary>
    private void LayoutInlineObjectParagraphPaged(int blockIndex, Paragraph paragraph, double textWidth)
    {
        const double gap = 6;
        var alignment = paragraph.Formatting.Alignment;

        // YY1 fix: compute the first inline object's pixel height (chart/WordArt/SmartArt/image, in
        // document order, using the same size formulas as the layout loop below) and pass it to
        // PeekFirstLineContentY so that Peek's page-break probe matches the actual first
        // ReserveContentY call.  Before this fix, PeekFirstLineContentY() used the default lineHeight=1,
        // which failed to detect the page-break for a tall inline chart/SmartArt near a page bottom —
        // the inline object correctly broke to the next page but the floating object anchored to this
        // paragraph stayed on the prior page (wrong).  Mirrors the TT1/VV1 fix in LayoutImageParagraphPaged.
        double firstObjHeight = DefaultFontSizePt * PxPerPoint * 1.3; // fallback: default text line height
        foreach (var run in paragraph.Runs)
        {
            if (run.Chart is { IsFloating: false } firstChart)
            {
                var h = firstChart.HeightPt > 0 ? firstChart.HeightPt * PxPerPoint : 216 * PxPerPoint;
                // Apply width-constrained scale if needed (mirrors the layout loop).
                var w = firstChart.WidthPt > 0 ? firstChart.WidthPt * PxPerPoint : 360 * PxPerPoint;
                if (w > textWidth) h *= textWidth / w;
                firstObjHeight = h;
                break;
            }
            if (run.WordArt is { IsFloating: false } firstWa)
            {
                var w = Math.Max(72, firstWa.FontSizePt * Math.Max(1, firstWa.Text.Length) * 0.62) * PxPerPoint;
                var h = Math.Max(40, firstWa.FontSizePt * 1.6) * PxPerPoint;
                if (w > textWidth) h *= textWidth / w;
                firstObjHeight = h;
                break;
            }
            if (run.SmartArt is { IsFloating: false } firstSa)
            {
                var h = firstSa.HeightPt > 0 ? firstSa.HeightPt * PxPerPoint : 216 * PxPerPoint;
                var w = firstSa.WidthPt  > 0 ? firstSa.WidthPt  * PxPerPoint : 468 * PxPerPoint;
                if (w > textWidth) h *= textWidth / w;
                firstObjHeight = h;
                break;
            }
            if (run.Image is { IsFloating: false } firstImg)
            {
                firstObjHeight = firstImg.HeightPt > 0 ? firstImg.HeightPt * PxPerPoint : 80;
                break;
            }
            // Plain text run: use text-line height as the Peek estimate.
            if (!string.IsNullOrEmpty(run.Text))
            {
                firstObjHeight = Build("Ag", run.Formatting).Height;
                break;
            }
        }
        // Collect floating objects anchored to this paragraph (mirrors LayoutImageParagraphPaged).
        var anchorContentY = PeekFirstLineContentY(firstObjHeight);
        CollectFloatingObjects(blockIndex, paragraph, anchorContentY);

        // Track a virtual glyph offset so the caret can step over inline objects.
        var glyphOffset = _placed.Count > 0
            ? _placed.Max(p => p.Block == blockIndex ? p.Offset : -1) + 1
            : 0;

        foreach (var run in paragraph.Runs)
        {
            // ── Inline chart ─────────────────────────────────────────────────────────
            if (run.Chart is { IsFloating: false } chart)
            {
                var width  = chart.WidthPt  > 0 ? chart.WidthPt  * PxPerPoint : 360 * PxPerPoint;
                var height = chart.HeightPt > 0 ? chart.HeightPt * PxPerPoint : 216 * PxPerPoint;
                if (width > textWidth) { var s = textWidth / width; width = textWidth; height *= s; }

                var contentY   = ReserveContentY(height);
                var pageSpaceY = ContentYToPageSpaceY(contentY);
                // AV-COL-NONTXT AG3: shift X to the column band for this inline chart's content-Y.
                var x          = ColumnLeftFor(contentY) + AlignmentOffset(alignment, textWidth, width);
                var rect       = new Rect(x, pageSpaceY, width, height);

                // Build inline chart data (reuses the same struct as floating charts).
                var series = chart.Series.Select(s => (s.Name, new List<double>(s.Values))).ToList();
                _inlineCharts.Add(BuildChartData(chart, rect, behindText: false, zOrder: 0, series));

                // ZZ1 fix: use the FULL object box as the hit-test band so TryHitTest/MoveCaretVertical
                // can reach a tall inline chart from above (pressing Down) or via a click in the upper
                // portion.  PlacedChar has no separate caret-draw Y field, so navigation correctness
                // takes priority; the YY3 baseline-cosmetic is dropped (it was LOW value, caused a MED
                // regression).  Y = pageSpaceY and LineHeight = height → band is [top, bottom].
                _placed.Add(new PlacedChar(blockIndex, glyphOffset++, x, pageSpaceY, 0, height,
                    RunFormatting.Default, '\0', Sentinel: false));

                _layoutContentY = contentY + height + gap;
                continue;
            }

            // ── Inline WordArt ───────────────────────────────────────────────────────
            if (run.WordArt is { IsFloating: false } wa)
            {
                // Size: estimate from text + font size (same formula as floating WordArt collector).
                var width  = Math.Max(72, wa.FontSizePt * Math.Max(1, wa.Text.Length) * 0.62) * PxPerPoint;
                var height = Math.Max(40, wa.FontSizePt * 1.6) * PxPerPoint;
                if (width > textWidth) { var s = textWidth / width; width = textWidth; height *= s; }

                var contentY   = ReserveContentY(height);
                var pageSpaceY = ContentYToPageSpaceY(contentY);
                // AV-COL-NONTXT AG3: shift X to the column band for this inline WordArt's content-Y.
                var x          = ColumnLeftFor(contentY) + AlignmentOffset(alignment, textWidth, width);
                var rect       = new Rect(x, pageSpaceY, width, height);

                _inlineWordArts.Add(new FloatingWordArtData
                {
                    Rect       = rect,
                    BehindText = false,
                    ZOrder     = 0,
                    Text       = wa.Text,
                    Style      = wa.Style,
                    FontSizePt = wa.FontSizePt,
                    Warp       = wa.Warp,
                });

                // ZZ1 fix: full-height sentinel for correct hit-test reach (see chart site above).
                _placed.Add(new PlacedChar(blockIndex, glyphOffset++, x, pageSpaceY, 0, height,
                    RunFormatting.Default, '\0', Sentinel: false));

                _layoutContentY = contentY + height + gap;
                continue;
            }

            // ── Inline SmartArt ──────────────────────────────────────────────────────
            if (run.SmartArt is { IsFloating: false } sa)
            {
                var width  = sa.WidthPt  > 0 ? sa.WidthPt  * PxPerPoint : 468 * PxPerPoint;
                var height = sa.HeightPt > 0 ? sa.HeightPt * PxPerPoint : 216 * PxPerPoint;
                if (width > textWidth) { var s = textWidth / width; width = textWidth; height *= s; }

                var contentY   = ReserveContentY(height);
                var pageSpaceY = ContentYToPageSpaceY(contentY);
                // AV-COL-NONTXT AG3: shift X to the column band for this inline SmartArt's content-Y.
                var x          = ColumnLeftFor(contentY) + AlignmentOffset(alignment, textWidth, width);
                var rect       = new Rect(x, pageSpaceY, width, height);

                _inlineSmartArts.Add(BuildFloatingSmartArtData(
                    sa,
                    rect,
                    behindText: false,
                    zOrder: 0,
                    blockIndex,
                    runIndex: -1));

                // ZZ1 fix: full-height sentinel for correct hit-test reach (see chart site above).
                _placed.Add(new PlacedChar(blockIndex, glyphOffset++, x, pageSpaceY, 0, height,
                    RunFormatting.Default, '\0', Sentinel: false));

                _layoutContentY = contentY + height + gap;
                continue;
            }

            // ── Inline text runs in the same paragraph ───────────────────────────────
            // (Treat any plain text runs as a single-line text block so mixed paragraphs
            //  with text + inline objects still show the text.)
            if (!string.IsNullOrEmpty(run.Text))
            {
                var fmt = run.Formatting;
                var lineH = Build("Ag", fmt).Height;
                var contentY   = ReserveContentY(lineH);
                var pageSpaceY = ContentYToPageSpaceY(contentY);
                // AV-COL-NONTXT AG3: shift X to the column band for this inline text's content-Y.
                var tx = ColumnLeftFor(contentY);
                foreach (var ch in run.Text)
                {
                    var ft = Build(ch.ToString(), fmt);
                    _placed.Add(new PlacedChar(blockIndex, glyphOffset++, tx, pageSpaceY,
                        ft.WidthIncludingTrailingWhitespace, lineH, fmt, ch, Sentinel: false));
                    tx += ft.WidthIncludingTrailingWhitespace;
                }
                _layoutContentY = contentY + lineH;
            }
        }

        // End-of-paragraph sentinel so the caret can rest after the last inline object.
        // AV-COL-NONTXT AG3: use the column-shifted X for the sentinel as well.
        var sentinelY = _placed.Count > 0
            ? _placed.Last(p => p.Block == blockIndex).Y
            : ContentYToPageSpaceY(_layoutContentY);
        _placed.Add(new PlacedChar(blockIndex, glyphOffset, ColumnLeftFor(_layoutContentY), sentinelY,
            0, DefaultFontSizePt * PxPerPoint * 1.3, RunFormatting.Default, '\0', Sentinel: true));

        _layoutContentY += gap;
    }

    private Bitmap? DecodeBitmap(InlineImage image)
    {
        if (_bitmapCache.TryGetValue(image, out var cached))
            return cached;

        Bitmap? bitmap = null;
        try
        {
            if (image.PngBytes.Length > 0)
            {
                using var stream = new MemoryStream(image.PngBytes);
                bitmap = new Bitmap(stream);
            }
        }
        catch (Exception)
        {
            bitmap = null; // undecodable -> placeholder rendered instead
        }

        _bitmapCache[image] = bitmap;
        return bitmap;
    }

    private void CollectFloatingObjects(int blockIndex, Paragraph paragraph, double anchorContentY)
    {
        var snapshots = DocumentViewLayoutPlanner.BuildFloatingObjectSnapshots(
            paragraph,
            blockIndex,
            anchorContentY,
            _surfacePlan,
            _colCount);

        if (snapshots.Count == 0)
            return;

        _floatingSnapshots.AddRange(snapshots);
        _wrapExclusions.AddRange(DocumentViewLayoutPlanner.BuildFloatingWrapExclusionZones(snapshots));

        foreach (var snapshot in snapshots)
        {
            if (snapshot.RunIndex < 0 || snapshot.RunIndex >= paragraph.Runs.Count)
                continue;

            var run = paragraph.Runs[snapshot.RunIndex];
            var rect = ToAvaloniaRect(snapshot.Rect);

            switch (snapshot.Kind)
            {
                case DocumentFloatingObjectKind.Image when run.Image is { IsFloating: true } img:
                    _floatingImages.Add((
                        rect,
                        DecodeBitmap(img),
                        snapshot.BehindText,
                        snapshot.ZOrderIndex,
                        snapshot.BlockIndex,
                        snapshot.RunIndex));
                    break;

                case DocumentFloatingObjectKind.Shape when run.Shape is { IsFloating: true } shape:
                    _floatingShapes.Add(BuildFloatingShapeData(
                        DrawingObjectVisualPlanner.BuildVisualPlan(shape, snapshot),
                        snapshot.BlockIndex,
                        snapshot.RunIndex));
                    break;

                case DocumentFloatingObjectKind.Chart when run.Chart is { IsFloating: true } chart:
                    var chartData = BuildChartData(
                        chart,
                        rect,
                        snapshot.BehindText,
                        snapshot.ZOrderIndex,
                        chart.Series.Select(s => (s.Name, new List<double>(s.Values))).ToList());
                    chartData.BlockIndex = snapshot.BlockIndex;
                    chartData.RunIndex = snapshot.RunIndex;
                    _floatingCharts.Add(chartData);
                    break;

                case DocumentFloatingObjectKind.WordArt when run.WordArt is { IsFloating: true } wordArt:
                    _floatingWordArts.Add(BuildFloatingWordArtData(
                        DrawingObjectVisualPlanner.BuildVisualPlan(wordArt, snapshot),
                        snapshot.BlockIndex,
                        snapshot.RunIndex));
                    break;

                case DocumentFloatingObjectKind.SmartArt when run.SmartArt is { IsFloating: true } smartArt:
                    _floatingSmartArts.Add(BuildFloatingSmartArtData(
                        smartArt,
                        rect,
                        snapshot.BehindText,
                        snapshot.ZOrderIndex,
                        snapshot.BlockIndex,
                        snapshot.RunIndex));
                    break;

                case DocumentFloatingObjectKind.Group when run.DrawingGroup is { } group:
                    _floatingGroups.Add(BuildFloatingGroupData(group, snapshot));
                    break;
            }
        }
    }

    private static FloatingShapeData BuildFloatingShapeData(
        DrawingObjectVisualPlan plan,
        int blockIndex = -1,
        int runIndex = -1)
    {
        var rect = ToAvaloniaRect(plan.Rect);
        IBrush? fillBrush = plan.Fill.Kind switch
        {
            DrawingObjectFillKind.Solid => ParseSolidBrush(plan.Fill.ColorHex),
            DrawingObjectFillKind.Gradient => BuildAvaloniaGradientBrush(ToShapeFill(plan.Fill), rect),
            DrawingObjectFillKind.Pattern => BuildAvaloniaPatternBrush(ToShapeFill(plan.Fill)),
            _ => null
        };

        Pen? outlinePen = null;
        if (plan.Outline.IsVisible)
        {
            var strokeBrush = ParseSolidBrush(plan.Outline.ColorHex);
            var strokeW = plan.Outline.WidthDip > 0 ? plan.Outline.WidthDip : 1.0;
            DashStyle? dashStyle = plan.Outline.DashStyle?.ToLowerInvariant() switch
            {
                "dash" => new DashStyle([4, 3], 0),
                "sysdot" => new DashStyle([1, 2], 0),
                "dashdot" => new DashStyle([4, 2, 1, 2], 0),
                _ => null,
            };
            outlinePen = dashStyle is not null
                ? new Pen(strokeBrush, strokeW, dashStyle)
                : new Pen(strokeBrush, strokeW);
        }

        return new FloatingShapeData
        {
            Rect = rect,
            BehindText = plan.BehindText,
            ZOrder = plan.ZOrderIndex,
            BlockIndex = blockIndex,
            RunIndex = runIndex,
            Kind = ToShapeKind(plan.GeometryKind),
            CustomGeo = plan.CustomGeometry,
            FillBrush = fillBrush,
            OutlinePen = outlinePen,
            Text = plan.Text?.Text,
            RotationAngle = plan.RotationAngle,
            FlipH = plan.FlipH,
            FlipV = plan.FlipV,
            Effects = plan.Effects,
        };
    }

    private static ShapeKind ToShapeKind(DrawingObjectGeometryKind? geometryKind) =>
        geometryKind switch
        {
            DrawingObjectGeometryKind.Ellipse => ShapeKind.Ellipse,
            DrawingObjectGeometryKind.RoundedRectangle => ShapeKind.RoundedRectangle,
            DrawingObjectGeometryKind.TextBox => ShapeKind.TextBox,
            _ => ShapeKind.Rectangle
        };

    private static ShapeFill ToShapeFill(DrawingObjectFillPlan plan)
    {
        if (plan.Kind == DrawingObjectFillKind.Gradient)
        {
            return ShapeFill.LinearGradient(
                plan.GradientAngle,
                plan.GradientStops
                    .Select(stop => new FreeW.Core.Model.GradientStop(stop.Position, stop.ColorHex))
                    .ToArray());
        }

        if (plan.Kind == DrawingObjectFillKind.Pattern)
        {
            return ShapeFill.Patterned(
                plan.PatternPreset ?? "diagCross",
                plan.PatternForegroundColorHex,
                plan.PatternBackgroundColorHex);
        }

        return ShapeFill.NoFill();
    }

    /// <summary>Builds an Avalonia <see cref="LinearGradientBrush"/> from a <see cref="ShapeFill"/> gradient.</summary>
    private static LinearGradientBrush BuildAvaloniaGradientBrush(ShapeFill fill, Rect rect)
    {
        // GradientAngle in 60k-degree units; 0=left→right, 5400000=top→bottom.
        var angleDeg = fill.GradientAngle / 60000.0;
        var angleRad = angleDeg * Math.PI / 180.0;
        // Convert angle to relative start/end points on the unit bounding box.
        var cos = Math.Cos(angleRad);
        var sin = Math.Sin(angleRad);
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5 - cos * 0.5, 0.5 - sin * 0.5, RelativeUnit.Relative),
            EndPoint   = new RelativePoint(0.5 + cos * 0.5, 0.5 + sin * 0.5, RelativeUnit.Relative),
        };
        foreach (var stop in fill.GradientStops)
        {
            if (TryParseAvaloniaColor(stop.ColorHex, out var c))
                brush.GradientStops.Add(new global::Avalonia.Media.GradientStop(c, stop.Position / 100000.0));
        }
        return brush;
    }

    /// <summary>
    /// Builds an Avalonia tiled <see cref="DrawingBrush"/> approximating a DrawingML preset pattern fill.
    /// Uses foreground/background colours from <paramref name="fill"/>; approximates each pattern family
    /// with a distinct tile so different presets are visually distinguishable.
    /// </summary>
    private static TileBrush BuildAvaloniaPatternBrush(ShapeFill fill)
    {
        TryParseAvaloniaColor(fill.PatternFgColorHex ?? "#4472C4", out var fg);
        TryParseAvaloniaColor(fill.PatternBgColorHex ?? "#FFFFFF", out var bg);

        var preset = fill.PatternPreset ?? string.Empty;

        // Build a small DrawingBrush tile matching the WPF reference.
        // Avalonia DrawingBrush with TileMode=Tile mirrors WPF's DrawingBrush.
        var fgBrush = new SolidColorBrush(fg);
        var pen     = new Pen(fgBrush, 1.0);

        // Each family gets a distinct 8×8 tile.
        Drawing tile;

        // UU2 fix: preset→family bucketing aligned to WPF BuildPatternBrush groupings.
        // Previously: upDiag/ltUpDiag were NW→SE (wrong); pct50/60/70 were dot (wrong);
        //             dkDiag was grouped with down-diag (wrong); cross/smGrid/lgGrid were grouped
        //             together (wrong — WPF puts smGrid with dot family).
        // Now matches WPF exactly: down-diag vs up-diag are separate; pct50 → down-diag;
        // pct60/pct70 → up-diag; cross/ltGrid/dkGrid/pct75/pct80 → H+V grid;
        // dotGrid/dotDmnd/smGrid/pct90 → dot tile.

        if (preset is "horz" or "ltHorz" or "medGray" or "dkHorz" or "pct5" or "pct10" or "pct20")
        {
            // Horizontal line across the middle (matches WPF)
            var dg = new global::Avalonia.Media.DrawingGroup();
            dg.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(bg), Geometry = new RectangleGeometry(new Rect(0, 0, 8, 8)) });
            dg.Children.Add(new GeometryDrawing { Pen = pen,  Geometry = new LineGeometry(new Point(0, 4), new Point(8, 4)) });
            tile = dg;
        }
        else if (preset is "vert" or "ltVert" or "dkVert" or "pct25" or "pct30")
        {
            // Vertical line (matches WPF)
            var dg = new global::Avalonia.Media.DrawingGroup();
            dg.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(bg), Geometry = new RectangleGeometry(new Rect(0, 0, 8, 8)) });
            dg.Children.Add(new GeometryDrawing { Pen = pen,  Geometry = new LineGeometry(new Point(4, 0), new Point(4, 8)) });
            tile = dg;
        }
        else if (preset is "diagStripe" or "ltDnDiag" or "dkDnDiag" or "dnDiag" or "pct50")
        {
            // Down-diagonal: top-left to bottom-right (NW→SE). Matches WPF "diagStripe/ltDnDiag/dkDnDiag/dnDiag/pct50".
            var dg = new global::Avalonia.Media.DrawingGroup();
            dg.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(bg), Geometry = new RectangleGeometry(new Rect(0, 0, 8, 8)) });
            dg.Children.Add(new GeometryDrawing { Pen = pen,  Geometry = new LineGeometry(new Point(0, 0), new Point(8, 8)) });
            tile = dg;
        }
        else if (preset is "ltUpDiag" or "dkUpDiag" or "upDiag" or "pct60" or "pct70")
        {
            // Up-diagonal: bottom-left to top-right (SW→NE). Matches WPF "ltUpDiag/dkUpDiag/upDiag/pct60/pct70".
            var dg = new global::Avalonia.Media.DrawingGroup();
            dg.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(bg), Geometry = new RectangleGeometry(new Rect(0, 0, 8, 8)) });
            dg.Children.Add(new GeometryDrawing { Pen = pen,  Geometry = new LineGeometry(new Point(0, 8), new Point(8, 0)) });
            tile = dg;
        }
        else if (preset is "cross" or "ltGrid" or "dkGrid" or "pct75" or "pct80")
        {
            // Horizontal + vertical cross/grid. Matches WPF "cross/ltGrid/dkGrid/pct75/pct80".
            var dg = new global::Avalonia.Media.DrawingGroup();
            dg.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(bg), Geometry = new RectangleGeometry(new Rect(0, 0, 8, 8)) });
            dg.Children.Add(new GeometryDrawing { Pen = pen,  Geometry = new LineGeometry(new Point(0, 4), new Point(8, 4)) });
            dg.Children.Add(new GeometryDrawing { Pen = pen,  Geometry = new LineGeometry(new Point(4, 0), new Point(4, 8)) });
            tile = dg;
        }
        else if (preset is "dotGrid" or "dotDmnd" or "smGrid" or "smDot" or "pct40" or "pct90")
        {
            // Dot tile. Matches WPF "dotGrid/dotDmnd/smGrid/pct90".
            var dg = new global::Avalonia.Media.DrawingGroup();
            dg.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(bg), Geometry = new RectangleGeometry(new Rect(0, 0, 8, 8)) });
            dg.Children.Add(new GeometryDrawing { Brush = fgBrush, Geometry = new EllipseGeometry(new Rect(3, 3, 2, 2)) });
            tile = dg;
        }
        else
        {
            // Default / diagCross: covers "diagCross", "dkDiag", "lgGrid", "lgConfetti", "smConfetti", unknowns.
            // Matches WPF fallback (both diagonals).
            var dg = new global::Avalonia.Media.DrawingGroup();
            dg.Children.Add(new GeometryDrawing { Brush = new SolidColorBrush(bg), Geometry = new RectangleGeometry(new Rect(0, 0, 8, 8)) });
            dg.Children.Add(new GeometryDrawing { Pen = pen,  Geometry = new LineGeometry(new Point(0, 0), new Point(8, 8)) });
            dg.Children.Add(new GeometryDrawing { Pen = pen,  Geometry = new LineGeometry(new Point(8, 0), new Point(0, 8)) });
            tile = dg;
        }

        return new DrawingBrush(tile)
        {
            TileMode        = TileMode.Tile,
            DestinationRect = new RelativeRect(0, 0, 8, 8, RelativeUnit.Absolute),
        };
    }

    /// <summary>
    /// Parses an RRGGBB (or #RRGGBB) hex string to an Avalonia <see cref="Color"/>. Returns false on failure.
    /// </summary>
    private static bool TryParseAvaloniaColor(string? hex, out Color color)
    {
        color = Colors.Black;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.TrimStart('#');
        if (s.Length == 6 &&
            byte.TryParse(s.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r) &&
            byte.TryParse(s.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g) &&
            byte.TryParse(s.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
        {
            color = Color.FromRgb(r, g, b);
            return true;
        }
        return false;
    }

    /// <summary>Parses a hex colour string to a <see cref="SolidColorBrush"/>. Returns null on failure.</summary>
    private static IBrush? ParseSolidBrush(string? hex)
    {
        if (TryParseAvaloniaColor(hex, out var c))
            return new SolidColorBrush(c);
        return null;
    }

    private static double[] ComputeColumnWidths(Table table, int cols, double textWidth)
    {
        var widths = new double[cols];
        double declared = 0;
        var declaredCount = 0;
        for (var c = 0; c < cols; c++)
        {
            var cw = c < table.ColumnWidthsPt.Count ? table.ColumnWidthsPt[c] * PxPerPoint : 0;
            widths[c] = cw;
            if (cw > 0)
            {
                declared += cw;
                declaredCount++;
            }
        }

        var missing = cols - declaredCount;
        var even = missing > 0 ? Math.Max(40, (textWidth - declared) / missing) : 0;
        for (var c = 0; c < cols; c++)
            if (widths[c] <= 0)
                widths[c] = missing > 0 ? even : textWidth / cols;

        var total = widths.Sum();
        // AV-COL-NONTXT AG4: Scale declared widths to fit the available column width.
        // In multi-column layout, textWidth = _colWidth (the per-column width, not the full page
        // content width).  A table whose declared ColumnWidthsPt sums to the full page width would
        // overflow the column and bleed across the column rule.  The scale-down here ensures that
        // any table — declared or not — is clamped to the available column (or page) width.
        if (total > textWidth && total > 0)
        {
            var scale = textWidth / total;
            for (var c = 0; c < cols; c++)
                widths[c] *= scale;
        }

        return widths;
    }

    private List<(double Height, List<(char Ch, double W)> Chars)> WrapCellLines(string text, RunFormatting fmt, double maxInner)
    {
        var result = new List<(double, List<(char, double)>)>();
        var lineHeight = Build("Ag", fmt).Height;
        var current = new List<(char, double)>();
        double currentWidth = 0;
        var lastSpace = -1;

        foreach (var ch in text)
        {
            var w = Build(ch.ToString(), fmt).WidthIncludingTrailingWhitespace;
            if (ch == ' ')
                lastSpace = current.Count;
            if (currentWidth + w > maxInner && current.Count > 0)
            {
                var breakAt = lastSpace > 0 ? lastSpace : current.Count;
                result.Add((lineHeight, current.Take(breakAt).ToList()));
                current = current.Skip(breakAt).ToList();
                currentWidth = current.Sum(c => c.Item2);
                lastSpace = -1;
            }

            current.Add((ch, w));
            currentWidth += w;
        }

        if (current.Count > 0 || result.Count == 0)
            result.Add((lineHeight, current));
        return result;
    }

    // AV-TBL4: resolve the fill brush for a cell — per-cell ShadingColorHex wins; header/band are fallbacks.
    private IBrush? ResolveCellFill(TableCell cell, bool isHeader, bool isBand)
    {
        if (!string.IsNullOrEmpty(cell.ShadingColorHex))
            return BrushFor(cell.ShadingColorHex);
        if (isHeader) return HeaderFill;
        if (isBand)   return BandFill;
        return null;
    }

    // AV-TBL4: draw per-edge cell borders using the shared host-neutral border plan.
    private void DrawCellBorderEdges(DrawingContext context, Rect rect, TableCellBorderVisualPlan plan)
    {
        foreach (var edge in plan.Edges)
            DrawCellEdgeLine(context, edge, rect);
    }

    private void DrawParagraphDecoration(DrawingContext context, Rect rect, string? shadingHex, ParagraphBorder? border)
    {
        if (!string.IsNullOrWhiteSpace(shadingHex))
            context.FillRectangle(BrushFor(shadingHex), rect);

        if (border is null)
            return;

        var pen = ParagraphBorderPen(border);
        if (border.Top)
            context.DrawLine(pen, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top));
        if (border.Bottom || border.BottomOnly)
            context.DrawLine(pen, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom));
        if (border.Left && !border.BottomOnly)
            context.DrawLine(pen, new Point(rect.Left, rect.Top), new Point(rect.Left, rect.Bottom));
        if (border.Right && !border.BottomOnly)
            context.DrawLine(pen, new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Bottom));
    }

    private Pen ParagraphBorderPen(ParagraphBorder border)
    {
        DashStyle? dashStyle = border.LineStyle switch
        {
            BorderLineStyle.Dashed => new DashStyle([4, 3], 0),
            BorderLineStyle.Dotted => new DashStyle([1, 2], 0),
            _ => null,
        };
        var width = Math.Max(0.5, border.WidthPt * PxPerPoint);
        return dashStyle is null
            ? new Pen(BrushFor(border.ColorHex), width)
            : new Pen(BrushFor(border.ColorHex), width, dashStyle);
    }

    private void DrawCharacterBorder(
        DrawingContext context,
        int placedIndex,
        PlacedChar pc,
        RunDecorationVisualPlan plan)
    {
        if (!plan.HasBorder || plan.Border is null)
            return;

        var rect = new Rect(pc.X, pc.Y, Math.Max(1, pc.W), pc.LineHeight);
        var pen = RunBorderPen(plan);
        var drawLeft = plan.DrawLeftBorder && !AdjacentGlyphSharesBorder(placedIndex - 1, pc, plan);
        var drawRight = plan.DrawRightBorder && !AdjacentGlyphSharesBorder(placedIndex + 1, pc, plan);

        if (plan.DrawTopBorder)
            context.DrawLine(pen, new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top));
        if (plan.DrawBottomBorder)
            context.DrawLine(pen, new Point(rect.Left, rect.Bottom), new Point(rect.Right, rect.Bottom));
        if (drawLeft)
            context.DrawLine(pen, new Point(rect.Left, rect.Top), new Point(rect.Left, rect.Bottom));
        if (drawRight)
            context.DrawLine(pen, new Point(rect.Right, rect.Top), new Point(rect.Right, rect.Bottom));
    }

    private bool AdjacentGlyphSharesBorder(int index, PlacedChar current, RunDecorationVisualPlan currentPlan)
    {
        if (index < 0 || index >= _placed.Count)
            return false;

        var adjacent = _placed[index];
        if (adjacent.Sentinel
            || adjacent.Block != current.Block
            || adjacent.IsCell != current.IsCell
            || Math.Abs(adjacent.Y - current.Y) > 0.5)
            return false;

        var adjacentPlan = RunDecorationVisualPlanner.Build(adjacent.Fmt, PxPerPoint);
        return adjacentPlan.HasBorder
            && adjacentPlan.Border == currentPlan.Border
            && adjacentPlan.DrawTopBorder == currentPlan.DrawTopBorder
            && adjacentPlan.DrawLeftBorder == currentPlan.DrawLeftBorder
            && adjacentPlan.DrawBottomBorder == currentPlan.DrawBottomBorder
            && adjacentPlan.DrawRightBorder == currentPlan.DrawRightBorder;
    }

    private Pen RunBorderPen(RunDecorationVisualPlan plan)
    {
        var border = plan.Border!;
        DashStyle? dashStyle = border.LineStyle switch
        {
            BorderLineStyle.Dashed => new DashStyle([4, 3], 0),
            BorderLineStyle.Dotted => new DashStyle([1, 2], 0),
            _ => null,
        };

        return dashStyle is null
            ? new Pen(BrushFor(border.ColorHex), plan.BorderWidthDip)
            : new Pen(BrushFor(border.ColorHex), plan.BorderWidthDip, dashStyle);
    }

    private void DrawCellEdgeLine(DrawingContext context, TableCellBorderEdgeVisualPlan edge, Rect rect)
    {
        if (!edge.IsVisible)
            return;

        var (p1, p2) = CellBorderPoints(edge.Edge, rect, 0);
        DashStyle? dashStyle = edge.Style switch
        {
            BorderLineStyle.Dashed => new DashStyle([4, 3], 0),
            BorderLineStyle.Dotted => new DashStyle([1, 2], 0),
            _ => null,
        };
        var pen = dashStyle is not null
            ? new Pen(BrushFor(edge.ColorHex), edge.WidthDip, dashStyle)
            : new Pen(BrushFor(edge.ColorHex), edge.WidthDip);

        if (edge.Style == BorderLineStyle.Double)
        {
            var offset = Math.Max(1.0, edge.WidthDip * 1.5);
            var (outer1, outer2) = CellBorderPoints(edge.Edge, rect, -offset / 2);
            var (inner1, inner2) = CellBorderPoints(edge.Edge, rect, offset / 2);
            context.DrawLine(pen, outer1, outer2);
            context.DrawLine(pen, inner1, inner2);
            return;
        }

        context.DrawLine(pen, p1, p2);
    }

    private static (Point Start, Point End) CellBorderPoints(TableCellBorderVisualEdge edge, Rect rect, double inwardOffset) =>
        edge switch
        {
            TableCellBorderVisualEdge.Top => (
                new Point(rect.Left, rect.Top + inwardOffset),
                new Point(rect.Right, rect.Top + inwardOffset)),
            TableCellBorderVisualEdge.Bottom => (
                new Point(rect.Left, rect.Bottom - inwardOffset),
                new Point(rect.Right, rect.Bottom - inwardOffset)),
            TableCellBorderVisualEdge.Left => (
                new Point(rect.Left + inwardOffset, rect.Top),
                new Point(rect.Left + inwardOffset, rect.Bottom)),
            TableCellBorderVisualEdge.Right => (
                new Point(rect.Right - inwardOffset, rect.Top),
                new Point(rect.Right - inwardOffset, rect.Bottom)),
            _ => (new Point(rect.Left, rect.Top), new Point(rect.Right, rect.Top)),
        };

    /// <summary>
    /// AV-VIEW: Draw the ruler strips for the first page in Print Layout — a horizontal strip along the
    /// page top and a vertical strip along the page left edge, each with the page margins tinted darker
    /// (so the lighter span marks the editable body area) and inch tick marks. Pure render chrome.
    /// </summary>
    private void DrawRuler(DrawingContext context)
    {
        const double inchDip = 72.0;
        var pageTop = _surfacePlan.PageTopDip(0); // first page only
        // ── Horizontal ruler: sits just above the page's top edge. ──
        var hRect = new Rect(_pageLeft, pageTop - RulerThicknessDip, _pageWidth, RulerThicknessDip);
        context.FillRectangle(RulerFill, hRect);
        // Margin tint: left margin + right margin spans (darker); body area stays light.
        context.FillRectangle(RulerMarginFill, new Rect(_pageLeft, hRect.Y, _contentLeft - _pageLeft, RulerThicknessDip));
        var bodyRight = _contentLeft + _contentWidth;
        context.FillRectangle(RulerMarginFill, new Rect(bodyRight, hRect.Y, _pageLeft + _pageWidth - bodyRight, RulerThicknessDip));
        context.DrawRectangle(null, RulerBorderPen, hRect);
        var rulerTicks = DocumentViewLayoutPlanner.BuildRulerTicks(_surfacePlan, inchDip);
        foreach (var x in rulerTicks)
            context.DrawLine(RulerTickPen, new Point(x, hRect.Y + RulerThicknessDip - 4), new Point(x, hRect.Y + RulerThicknessDip));

        // ── Vertical ruler: sits just left of the page's left edge. ──
        var vRect = new Rect(_pageLeft - RulerThicknessDip, pageTop, RulerThicknessDip, _pageHeightPx);
        context.FillRectangle(RulerFill, vRect);
        var bodyTop    = pageTop + _marginTopDip;
        var bodyBottom = pageTop + _pageHeightPx - _marginBottomDip;
        context.FillRectangle(RulerMarginFill, new Rect(vRect.X, pageTop, RulerThicknessDip, _marginTopDip));
        context.FillRectangle(RulerMarginFill, new Rect(vRect.X, bodyBottom, RulerThicknessDip, pageTop + _pageHeightPx - bodyBottom));
        context.DrawRectangle(null, RulerBorderPen, vRect);
        foreach (var y in rulerTicks.Select(tick => pageTop + tick - _pageLeft))
            context.DrawLine(RulerTickPen, new Point(vRect.X + RulerThicknessDip - 4, y), new Point(vRect.X + RulerThicknessDip, y));
    }

    // AV-DESIGN: the model's page border (w:pgBorders) drawn inset just inside the page sheet. Word draws
    // page borders a fixed offset from the page edge (its "Measure from: Edge of page" default is 24pt); we
    // mirror that with a small inset so the border sits between the chrome edge and the body text. FreeW's
    // own DocxWriter.BuildPageBorders emits w:space="24" (POINTS — w:space on w:pgBorders is always measured
    // in points, never twips/DXA), so the inset here must be 24 points converted to DIP, not 24 raw DIP.
    private const double PageBorderInsetPt = 24.0;

    // Test-only: exposes the DIP inset so tests can assert it matches 24pt (the writer's w:space) rather
    // than the raw point value, catching any future re-introduction of a DIP/point mismatch.
    internal static double PageBorderInsetDip => PageBorderInsetPt * PxPerPoint;

    private void DrawPageBorder(DrawingContext context, Rect pageRect)
    {
        if (_doc.Page.PageBorder is not { } pb)
            return;

        var color = TryParseAvaloniaColor(pb.ColorHex, out var c) ? c : Colors.Black;
        var widthDip = Math.Max(0.5, pb.WidthPt * PxPerPoint);
        var pen = new Pen(new SolidColorBrush(color), widthDip)
        {
            DashStyle = pb.LineStyle switch
            {
                BorderLineStyle.Dotted => new DashStyle([1, 2], 0),
                BorderLineStyle.Dashed => new DashStyle([3, 2], 0),
                _ => null,
            },
        };

        var inset = Math.Min(PageBorderInsetDip, Math.Min(pageRect.Width, pageRect.Height) / 4);
        var rect = pageRect.Deflate(new Thickness(inset));
        context.DrawRectangle(null, pen, rect);
        // BorderLineStyle.Double: draw a second, inner stroke a couple of DIP inside the first.
        if (pb.LineStyle == BorderLineStyle.Double)
            context.DrawRectangle(null, pen, rect.Deflate(new Thickness(widthDip + 1.5)));
    }

    // AV-DESIGN: faint watermark drawn behind the body on each page. Mirrors Word's Design >
    // Watermark: a large, low-opacity, optionally diagonal label or picture centred on the page.
    private void DrawWatermark(DrawingContext context, Rect pageRect)
    {
        if (_doc.Page.EffectiveWatermark is not { } wm)
            return;

        if (wm.IsPicture)
        {
            DrawPictureWatermark(context, pageRect, wm);
            return;
        }

        if (string.IsNullOrWhiteSpace(wm.Text))
            return;

        var color = TryParseAvaloniaColor(wm.FontColorHex, out var c) ? c : Color.FromRgb(0x80, 0x80, 0x80);
        var opacity = Math.Clamp(wm.Opacity, 0.0, 1.0);
        var brush = new SolidColorBrush(color, opacity);

        // Size the text to span most of the page width (Word auto-scales). Cap the point size sensibly.
        var typeface = new Typeface(
            wm.FontFamily is { Length: > 0 } family ? new FontFamily(family) : FontFamily.Default,
            FontStyle.Normal, FontWeight.Bold);
        var fontSize = Math.Min(pageRect.Width, 480) / Math.Max(4, wm.Text.Length) * 1.6;
        fontSize = Math.Clamp(fontSize, 24, 130);

        var ft = new FormattedText(
            wm.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, fontSize, brush);

        // Clip to the page sheet: an un-clipped long watermark string (fontSize floors at 24pt) can extend
        // past the page rect onto the grey desk / an adjacent page. Word tiles+clips watermarks via a brush
        // so they never overflow the page — mirror that here with a hard clip to pageRect.
        using var clip = context.PushClip(pageRect);

        var center = pageRect.Center;
        using var _ = context.PushTransform(
            Matrix.CreateTranslation(-ft.Width / 2, -ft.Height / 2)
            * (wm.Layout == WatermarkLayout.Diagonal
                ? Matrix.CreateRotation(-Math.PI / 4)
                : Matrix.Identity)
            * Matrix.CreateTranslation(center.X, center.Y));
        context.DrawText(ft, new Point(0, 0));
    }

    private void DrawPictureWatermark(DrawingContext context, Rect pageRect, WatermarkOptions wm)
    {
        if (wm.ImageBytes is not { Length: > 0 })
            return;

        var bitmap = DecodeWatermarkBitmap(wm.ImageBytes);
        if (bitmap is null)
            return;

        var plan = WatermarkVisualPlanner.BuildPictureLayout(
            wm,
            pageRect.Width,
            pageRect.Height,
            bitmap.PixelSize.Width,
            bitmap.PixelSize.Height);
        if (plan is null)
            return;

        var rect = new Rect(
            pageRect.X + plan.XDip,
            pageRect.Y + plan.YDip,
            plan.WidthDip,
            plan.HeightDip);

        using var clip = context.PushClip(pageRect);
        using var opacity = context.PushOpacity(plan.Opacity);
        if (Math.Abs(plan.RotationDegrees) > 0.01)
        {
            var centerX = pageRect.X + plan.CenterXDip;
            var centerY = pageRect.Y + plan.CenterYDip;
            using var transform = context.PushTransform(
                Matrix.CreateTranslation(-centerX, -centerY)
                * Matrix.CreateRotation(plan.RotationDegrees * Math.PI / 180.0)
                * Matrix.CreateTranslation(centerX, centerY));
            context.DrawImage(bitmap, rect);
            return;
        }

        context.DrawImage(bitmap, rect);
    }

    private Bitmap? DecodeWatermarkBitmap(byte[] imageBytes)
    {
        if (ReferenceEquals(_watermarkBitmapCacheBytes, imageBytes))
            return _watermarkBitmapCache;

        Bitmap? bitmap = null;
        try
        {
            using var stream = new MemoryStream(imageBytes);
            bitmap = new Bitmap(stream);
        }
        catch (Exception)
        {
            bitmap = null;
        }

        _watermarkBitmapCacheBytes = imageBytes;
        _watermarkBitmapCache = bitmap;
        return bitmap;
    }

    private static IBrush HeaderFill { get; } = new SolidColorBrush(Color.FromRgb(0xDE, 0xE9, 0xF7));
    private static IBrush BandFill { get; } = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
    private static Pen TableBorderPen { get; } = new Pen(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)), 0.75);
    private static IBrush PageDeskBrush   { get; } = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
    private static IBrush PageShadowBrush { get; } = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00));
    private static Pen    PageBorderPen   { get; } = new Pen(new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)), 0.5);
    // AV-COL: thin gray rule drawn in each inter-column gap when ColumnsLineBetween is set.
    private static Pen    ColumnRulePen   { get; } = new Pen(new SolidColorBrush(Colors.Gray), 1.0);
    // AV-NOTERENDER: thin separator rule above the footnote band / under the Endnotes heading.
    private static Pen    NoteSeparatorPen { get; } = new Pen(new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)), 0.75);
    // AV-VIEW: faint layout-gridlines drawn behind body text when ShowGridlines is set.
    private static Pen    GridlinePen      { get; } = new Pen(new SolidColorBrush(Color.FromArgb(0x30, 0x60, 0x90, 0xC0)), 0.5);
    // AV-VIEW: ruler strip fill, border, and tick marks drawn at the page top/left when ShowRuler is set.
    private static IBrush RulerFill        { get; } = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xFA));
    private static Pen    RulerBorderPen   { get; } = new Pen(new SolidColorBrush(Color.FromRgb(0xC0, 0xC8, 0xD4)), 0.75);
    private static Pen    RulerTickPen     { get; } = new Pen(new SolidColorBrush(Color.FromRgb(0x70, 0x80, 0x98)), 0.75);
    // AV-VIEW: darker tint marking the page margins on the ruler (the body text area is the lighter span).
    private static IBrush RulerMarginFill  { get; } = new SolidColorBrush(Color.FromRgb(0xD8, 0xDE, 0xE8));

    // ---- Render ---------------------------------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        if (_laidOutWidth < 0 || Math.Abs(_laidOutWidth - Bounds.Width) > 0.5)
            Relayout(Bounds.Width > 0 ? Bounds.Width : FallbackWidth);

        // AV-DESIGN: the page sheet is filled with the document's Page Color (w:background) when set,
        // else white. The page border (w:pgBorders) and watermark draw on top of the sheet fill.
        var pageFill = ParseSolidBrush(_doc.Page.BackgroundColorHex) ?? Brushes.White;

        if (_viewMode == DocumentViewMode.PrintLayout)
        {
            // Grey desk fills the full control area.
            context.FillRectangle(PageDeskBrush, new Rect(Bounds.Size));

            // Draw each discrete page rectangle: page-coloured sheet with drop-shadow + chrome border.
            for (var pi = 0; pi < _pageCount; pi++)
            {
                var pageTop = _surfacePlan.PageTopDip(pi);
                var pageRect   = new Rect(_pageLeft, pageTop, _pageWidth, _pageHeightPx);
                var shadowRect = new Rect(_pageLeft + 3, pageTop + 3, _pageWidth, _pageHeightPx);
                context.FillRectangle(PageShadowBrush, shadowRect);
                context.FillRectangle(pageFill, pageRect);
                context.DrawRectangle(null, PageBorderPen, pageRect);
                // AV-DESIGN: layering matches Word — page color, then watermark, then the page border on
                // top (a solid pgBorders line must not be occluded by the faint watermark behind it).
                DrawWatermark(context, pageRect);
                DrawPageBorder(context, pageRect);
            }
        }
        else
        {
            // Web Layout / Draft: plain page-coloured background — no desk, no page chrome.
            context.FillRectangle(pageFill, new Rect(Bounds.Size));
        }

        // AV-VIEW: faint layout-gridlines behind the body text (Print Layout only). Drawn after the
        // white page fill so the grid shows through, before table fills / text so it sits underneath.
        if (_showGridlines && _viewMode == DocumentViewMode.PrintLayout)
        {
            foreach (var (x1, y1, x2, y2) in ComputeGridlines())
                context.DrawLine(GridlinePen, new Point(x1, y1), new Point(x2, y2));
        }

        // AV-VIEW: horizontal + vertical ruler strips on the first page (Print Layout only).
        if (_showRuler && _viewMode == DocumentViewMode.PrintLayout)
            DrawRuler(context);

        foreach (var (rect, shadingHex, border) in _paragraphDecorations)
            DrawParagraphDecoration(context, rect, shadingHex, border);

        // Table fills + borders sit beneath the text.
        foreach (var (rect, fill, border, cellBorderPlan) in _rects)
        {
            if (fill is not null)
                context.FillRectangle(fill, rect);
            if (border)
                context.DrawRectangle(null, TableBorderPen, rect);
            // AV-TBL4: per-edge cell borders drawn on top of the table-level border.
            if (cellBorderPlan is not null)
                DrawCellBorderEdges(context, rect, cellBorderPlan);
        }

        // AV-COL: draw column rules (vertical divider lines in each inter-column gap) when enabled.
        // One rule per gap, centred horizontally, running the full text-area height on each page.
        if (_viewMode == DocumentViewMode.PrintLayout && _colCount > 1 && _colLineBetween)
        {
            for (var pi = 0; pi < _pageCount; pi++)
            {
                var pageTop = _surfacePlan.PageTopDip(pi);
                var ruleTop    = pageTop + _marginTopDip;
                var ruleBottom = pageTop + _pageHeightPx - _marginBottomDip;
                for (var ci = 0; ci < _colCount - 1; ci++)
                {
                    // Gap centre X = left edge of next column minus half gap.
                    var gapCentreX = _contentLeft + (ci + 1) * (_colWidth + _colGap) - _colGap / 2;
                    context.DrawLine(ColumnRulePen, new Point(gapCentreX, ruleTop), new Point(gapCentreX, ruleBottom));
                }
            }
        }

        // Behind-text pass: planner snapshots merge all floating kinds into one z-ordered band.
        foreach (var snapshot in DocumentViewLayoutPlanner.BuildFloatingObjectDrawOrder(_floatingSnapshots, behindText: true))
            DrawFloatingObjectSnapshot(context, snapshot);

        // Inline images (non-floating).
        foreach (var (rect, bitmap) in _images)
        {
            if (bitmap is not null)
                context.DrawImage(bitmap, rect);
            else
            {
                context.FillRectangle(BandFill, rect);
                context.DrawRectangle(null, TableBorderPen, rect);
            }
        }

        // FO4: inline charts, WordArt, SmartArt — rendered in the text flow using the same FO3 helpers.
        foreach (var cd in _inlineCharts)
            DrawFloatingChart(context, cd);
        foreach (var wd in _inlineWordArts)
            DrawFloatingWordArt(context, wd);
        foreach (var sd in _inlineSmartArts)
            DrawFloatingSmartArt(context, sd);

        // AV-TBL2: cross-cell block-selection highlight. Draw a semi-transparent overlay over each
        // cell-hit rect that falls inside the selected row×col rectangle.
        // BF5: use span-overlap test so merged cells straddling the boundary are highlighted.
        if (SelectedCellRange is { } cellSel)
        {
            foreach (var (cellRect, cellBlock, cellRow, cellCol) in _cellHits)
            {
                if (cellBlock != cellSel.TableBlock) continue;
                if (cellRow < cellSel.MinRow || cellRow > cellSel.MaxRow) continue;
                // Overlap test: include cell if its column range [startCol, startCol+span-1] overlaps
                // the selection [MinCol, MaxCol]. For cells with no merged span, GridSpan is 1.
                var cellSpan = GetCellModel(cellBlock, cellRow, cellCol)?.GridSpan ?? 1;
                cellSpan = Math.Max(1, cellSpan);
                if (cellCol + cellSpan - 1 < cellSel.MinCol) continue; // cell ends before selection
                if (cellCol > cellSel.MaxCol) continue;                 // cell starts after selection
                context.FillRectangle(CellBlockSelectionBrush, cellRect);
            }
        }

        // AV-TAB: draw tab leader spans (dots/dashes/underline) before the glyph text.
        foreach (var (x1, x2, spanY, lineH, leader, spanFmt) in _tabLeaderSpans)
        {
            if (leader == TabLeader.None || x2 <= x1) continue;
            DrawTabLeader(context, x1, x2, spanY, lineH, leader, spanFmt);
        }

        var selection = NormalizedSelection();
        var proofingOffsets = BuildProofingOffsetSet();
        var reviewPolicy = CurrentReviewDisplayPolicy;
        for (var placedIndex = 0; placedIndex < _placed.Count; placedIndex++)
        {
            var pc = _placed[placedIndex];
            if (pc.Sentinel)
                continue;
            var revisionDecision = reviewPolicy.RevisionDecision(pc.Revision);
            if (!revisionDecision.IsTextVisible)
                continue;
            var decorationPlan = RunDecorationVisualPlanner.Build(pc.Fmt, PxPerPoint);

            if (selection is { } sel && IsWithin(sel, pc.Block, pc.Offset))
                context.FillRectangle(SelectionBrush, new Rect(pc.X, pc.Y, Math.Max(2, pc.W), pc.LineHeight));

            // Character shading takes precedence over highlight; both fill behind glyphs.
            if (decorationPlan.HasBackground)
                context.FillRectangle(
                    BrushFor(decorationPlan.BackgroundColorHex),
                    new Rect(pc.X, pc.Y, Math.Max(1, pc.W), pc.LineHeight));

            // AV-COMMENT: a subtle amber background tint behind glyphs anchored by a review comment, so
            // the commented range reads as one region (the underline + margin marker are drawn after the
            // glyph loop). Resolved threads tint muted/grey to match Word's de-emphasised resolved state.
            if (pc.CommentId is { } commentTintId && reviewPolicy.ShouldHighlightComments)
            {
                var tint = IsCommentResolved(commentTintId) ? ResolvedCommentTintBrush : CommentTintBrush;
                context.FillRectangle(tint, new Rect(pc.X, pc.Y, Math.Max(1, pc.W), pc.LineHeight));
            }

            // AV-LINK: a hyperlinked glyph renders in the hyperlink style — Word's default blue + underline —
            // unless the run already carries an explicit colour / its own underline (e.g. a "Hyperlink"
            // character style was applied), in which case those win. Layered before super/sub + revision.
            var linkFmt = pc.Fmt;
            if (pc.IsHyperlink)
            {
                linkFmt = linkFmt with
                {
                    ColorHex = string.IsNullOrWhiteSpace(linkFmt.ColorHex) ? HyperlinkColorHex : linkFmt.ColorHex,
                    Underline = true,
                };
            }

            // Superscript/subscript: draw at a smaller size + vertical offset.
            // Word approximation: ~58% of the font size, raised/lowered by ~33% of line height.
            var drawFmt = linkFmt;
            var drawY   = pc.Y;
            if (pc.Fmt.VerticalAlign == VerticalAlign.Superscript)
            {
                var sz = (drawFmt.FontSizePt ?? DefaultFontSizePt) * SuperSubScale;
                drawFmt = drawFmt with { FontSizePt = sz };
                drawY   = pc.Y + pc.LineHeight * SuperYRaiseFraction;
            }
            else if (pc.Fmt.VerticalAlign == VerticalAlign.Subscript)
            {
                var sz = (drawFmt.FontSizePt ?? DefaultFontSizePt) * SuperSubScale;
                drawFmt = drawFmt with { FontSizePt = sz };
                drawY   = pc.Y + pc.LineHeight * SubYLowerFraction;
            }

            // AV-TRACKEDIT: tracked insertions/deletions draw in the revision colour; insertions are also
            // underlined and deletions struck through (the marks layered on top of any run decorations below).
            if (revisionDecision.IsRevisionStylingApplied)
                drawFmt = drawFmt with { ColorHex = RevisionColorHex };
            var formatRevisionHighlighted = pc.HasFormatRevision && reviewPolicy.ShouldHighlightFormattingChanges;
            if (formatRevisionHighlighted && string.IsNullOrWhiteSpace(drawFmt.ColorHex))
                drawFmt = drawFmt with { ColorHex = RevisionColorHex };

            // AV-TAB: tab characters have no glyph — skip text drawing (leader was drawn separately).
            if (pc.Ch == '\t')
            {
                DrawCharacterBorder(context, placedIndex, pc, decorationPlan);
                // Still draw underline/strikethrough across the tab gap if the run has them.
                if (drawFmt.Underline)
                    DrawDecoration(context, pc, pc.Y + pc.LineHeight * 0.82, drawFmt);
                if (drawFmt.Strikethrough)
                    DrawDecoration(context, pc, pc.Y + pc.LineHeight * 0.5, drawFmt);
                DrawRevisionDecoration(context, pc, revisionDecision);
                if (formatRevisionHighlighted)
                    DrawFormatRevisionDecoration(context, pc);
                continue;
            }

            var ft = Build(pc.Ch.ToString(), drawFmt);
            context.DrawText(ft, new Point(pc.X, drawY));

            DrawCharacterBorder(context, placedIndex, pc, decorationPlan);
            if (drawFmt.Underline)
                DrawDecoration(context, pc, pc.Y + pc.LineHeight * 0.82, drawFmt);
            if (drawFmt.Strikethrough)
                DrawDecoration(context, pc, pc.Y + pc.LineHeight * 0.5, drawFmt);
            DrawRevisionDecoration(context, pc, revisionDecision);
            if (formatRevisionHighlighted)
                DrawFormatRevisionDecoration(context, pc);
            if (!pc.IsCell && proofingOffsets.Contains((pc.Block, pc.Offset)))
                DrawProofingSquiggle(context, pc);
        }

        foreach (var (mx, my, text, fmt) in _markers)
            context.DrawText(Build(text, fmt), new Point(mx, my));

        // AV-COMMENT: draw the comment-anchor decorations on top of the text — an amber underline under
        // every commented glyph (the in-text anchor mark) plus, for each anchor line, a minimal marker in
        // the right margin (a balloon glyph + the author's initial) aligned to that line.
        DrawCommentAnchors(context);
        DrawSimpleMarkupChangeBars(context);

        // Paragraph marks (¶) rendered faintly at the end-sentinel of each block when enabled.
        if (_showParagraphMarks)
        {
            var pilcrowFmt = new RunFormatting { FontSizePt = 8, ColorHex = "#999999" };
            foreach (var pc in _placed)
            {
                if (pc.Sentinel)
                {
                    var ft = Build("¶", pilcrowFmt);
                    context.DrawText(ft, new Point(pc.X + 1, pc.Y + pc.LineHeight - ft.Height));
                }
            }
        }

        // In-front pass: same merged ZOrder logic for all six types (UU1 + XX1 fix).
        foreach (var snapshot in DocumentViewLayoutPlanner.BuildFloatingObjectDrawOrder(_floatingSnapshots, behindText: false))
            DrawFloatingObjectSnapshot(context, snapshot);

        // HF: draw header/footer items (pre-computed in BuildHeaderFooterItems). The in-front floating
        // objects are already drawn by the planner-ordered front pass above.
        if (_viewMode == DocumentViewMode.PrintLayout)
        {
            foreach (var item in _headerFooterItems)
            {
                if (string.IsNullOrEmpty(item.Text))
                    continue;
                var ft = Build(item.Text, item.Fmt);
                var alignOffset = AlignmentOffset(item.Alignment, item.AvailableWidth,
                    ft.WidthIncludingTrailingWhitespace, isLast: true);
                context.DrawText(ft, new Point(item.X + alignOffset, item.Y));
            }

            // AV-NOTERENDER: footnote-band separators + footnote/endnote text (pre-computed in
            // BuildFootnoteItems / BuildEndnoteItems). Separators draw first so the text sits below them.
            foreach (var (x1, x2, sy) in _noteSeparators)
                context.DrawLine(NoteSeparatorPen, new Point(x1, sy), new Point(x2, sy));

            foreach (var note in _noteItems)
            {
                if (string.IsNullOrEmpty(note.Text)) continue;
                var drawFmt = note.Fmt;
                var drawY = note.Y;
                // Render the superscript number prefix smaller + raised, mirroring the body super/subscript draw.
                if (note.Fmt.VerticalAlign == VerticalAlign.Superscript)
                {
                    var sz = (drawFmt.FontSizePt ?? NoteFontSizePt) * SuperSubScale;
                    drawFmt = drawFmt with { FontSizePt = sz };
                    drawY = note.Y - (note.Fmt.FontSizePt ?? NoteFontSizePt) * PxPerPoint * 0.15;
                }
                context.DrawText(Build(note.Text, drawFmt), new Point(note.X, drawY));
            }

            // AV-HFEDIT: when the caret is inside a header/footer region, draw a subtle region
            // outline + a small "Header"/"Footer" label so the active edit zone is obvious.
            DrawHeaderFooterEditRegion(context);
        }

        // AV-FLSEL: draw selection outline + 8 resize handles over the selected floating object.
        if (_selectedFloating is { } selFl)
            DrawFloatingSelection(context, selFl.Rect, selFl.BlockIndex, selFl.RunIndex, selFl.Kind);

        // AV-HFEDIT: the header/footer caret renders independently of the body caret.
        if (IsFocused && _hfCaret is not null && TryGetHfCaretRect(out var hfRect))
            context.FillRectangle(Brushes.Black, hfRect);
        else if (IsFocused && NormalizedSelection() is null && _hfCaret is null && TryGetCaretRect(out var caretRect))
            context.FillRectangle(Brushes.Black, caretRect);
    }

    // ── AV-HFEDIT: render the active header/footer edit region outline + label + caret ──────────────

    private static readonly IPen HfRegionPen =
        new Pen(new SolidColorBrush(Color.FromArgb(160, 90, 120, 200)), 1, DashStyle.Dash);
    private static readonly IBrush HfRegionLabelBrush = new SolidColorBrush(Color.FromArgb(200, 90, 120, 200));

    /// <summary>
    /// Draws a dashed outline around the line band of the currently-edited header/footer paragraph plus a
    /// "Header"/"Footer" label at its left edge. No-op when no H/F caret is active.
    /// </summary>
    private void DrawHeaderFooterEditRegion(DrawingContext context)
    {
        if (_hfCaret is not { } hc)
            return;
        // Find the rendered item(s) for this target's paragraph to derive the band.
        double top = double.MaxValue, bottom = double.MinValue, lineH = 0;
        var found = false;
        foreach (var item in _headerFooterItems)
        {
            if (item.Target is not { } t || !t.Equals(hc.Target))
                continue;
            found = true;
            top = Math.Min(top, item.Y);
            var h = item.LineHeight > 0 ? item.LineHeight : DefaultFontSizePt * PxPerPoint * 1.3;
            bottom = Math.Max(bottom, item.Y + h);
            lineH = Math.Max(lineH, h);
        }
        if (!found)
            return;

        var pad = 3.0;
        var rect = new Rect(_contentLeft - pad, top - pad,
            _contentWidth + 2 * pad, (bottom - top) + 2 * pad);
        context.DrawRectangle(null, HfRegionPen, rect);

        // Label: "Header"/"Footer" above the band's top-left (clamped into the page).
        var label = IsFooterSlot(hc.Target.Slot) ? "Footer" : "Header";
        var labelFt = Build(label, RunFormatting.Default with { FontSizePt = 8 });
        var labelY = Math.Max(0, top - pad - labelFt.Height);
        context.DrawText(labelFt, new Point(_contentLeft - pad, labelY));
    }

    /// <summary>
    /// Computes the caret rectangle for the active header/footer caret by measuring the prefix width up to
    /// the caret offset within the target paragraph's first rendered line. Returns false when no item for
    /// the target is laid out.
    /// </summary>
    private bool TryGetHfCaretRect(out Rect rect)
    {
        rect = default;
        if (_hfCaret is not { } hc)
            return false;

        // Locate the rendered item whose model span contains the caret offset (or the line's last item).
        HfRenderItem? hostItem = null;
        HfRenderItem? firstForTarget = null;
        foreach (var item in _headerFooterItems)
        {
            if (item.Target is not { } t || !t.Equals(hc.Target))
                continue;
            firstForTarget ??= item;
            var start = item.ModelStartOffset;
            var end = start + item.Text.Length;
            if (hc.Offset >= start && hc.Offset <= end)
            {
                hostItem = item;
                break;
            }
        }
        hostItem ??= firstForTarget;
        if (hostItem is null)
        {
            // Empty paragraph with no text segment: place caret at content-left on the band (use first item).
            // firstForTarget is null here only when nothing was laid out → fail.
            return false;
        }

        var ft = Build(hostItem.Text.Length == 0 ? " " : hostItem.Text, hostItem.Fmt);
        var alignOffset = AlignmentOffset(hostItem.Alignment, hostItem.AvailableWidth,
            ft.WidthIncludingTrailingWhitespace, isLast: true);
        var localOffset = Math.Clamp(hc.Offset - hostItem.ModelStartOffset, 0, hostItem.Text.Length);
        var prefixW = hostItem.Text.Length == 0 || localOffset == 0
            ? 0
            : Build(hostItem.Text[..localOffset], hostItem.Fmt).WidthIncludingTrailingWhitespace;
        var caretX = hostItem.X + alignOffset + prefixW;
        var caretH = hostItem.LineHeight > 0 ? hostItem.LineHeight : ft.Height;
        rect = new Rect(caretX, hostItem.Y, 1.5, caretH);
        return true;
    }

    /// <summary>
    /// Renders a single floating image (or a placeholder rect if the bitmap could not be decoded).
    /// Shared by the behind-text and in-front passes in <see cref="Render"/>.
    /// </summary>
    private void DrawFloatingObjectSnapshot(DrawingContext context, DocumentFloatingObjectSnapshot snapshot)
    {
        switch (snapshot.Kind)
        {
            case DocumentFloatingObjectKind.Image:
                foreach (var image in _floatingImages)
                {
                    if (image.BlockIndex == snapshot.BlockIndex && image.RunIndex == snapshot.RunIndex)
                    {
                        DrawFloatingImage(context, image.Rect, image.Image);
                        return;
                    }
                }
                break;

            case DocumentFloatingObjectKind.Shape:
                if (_floatingShapes.FirstOrDefault(shape =>
                        shape.BlockIndex == snapshot.BlockIndex && shape.RunIndex == snapshot.RunIndex) is { } shape)
                    DrawFloatingShape(context, shape);
                break;

            case DocumentFloatingObjectKind.Chart:
                if (_floatingCharts.FirstOrDefault(chart =>
                        chart.BlockIndex == snapshot.BlockIndex && chart.RunIndex == snapshot.RunIndex) is { } chart)
                    DrawFloatingChart(context, chart);
                break;

            case DocumentFloatingObjectKind.WordArt:
                if (_floatingWordArts.FirstOrDefault(wordArt =>
                        wordArt.BlockIndex == snapshot.BlockIndex && wordArt.RunIndex == snapshot.RunIndex) is { } wordArt)
                    DrawFloatingWordArt(context, wordArt);
                break;

            case DocumentFloatingObjectKind.SmartArt:
                if (_floatingSmartArts.FirstOrDefault(smartArt =>
                        smartArt.BlockIndex == snapshot.BlockIndex && smartArt.RunIndex == snapshot.RunIndex) is { } smartArt)
                    DrawFloatingSmartArt(context, smartArt);
                break;

            case DocumentFloatingObjectKind.Group:
                if (_floatingGroups.FirstOrDefault(group =>
                        group.BlockIndex == snapshot.BlockIndex && group.RunIndex == snapshot.RunIndex) is { } group)
                    DrawFloatingGroup(context, group);
                break;
        }
    }

    private void DrawFloatingImage(DrawingContext context, Rect rect, Bitmap? bitmap)
    {
        if (bitmap is not null)
            context.DrawImage(bitmap, rect);
        else
        {
            // Placeholder: light-blue fill + dashed border so the position is visible even without bitmap data.
            context.FillRectangle(FloatPlaceholderFill, rect);
            context.DrawRectangle(null, FloatPlaceholderPen, rect);
        }
    }

    /// <summary>
    /// Renders a single floating shape from its pre-computed <see cref="FloatingShapeData"/>.
    /// Applies rotation/flip transforms around the shape centre when present, then draws
    /// the geometry (fill + outline) followed by any centred shape text.
    /// </summary>
    private void DrawFloatingShape(DrawingContext context, FloatingShapeData sd)
    {
        var rect = sd.Rect;
        var cx = rect.X + rect.Width  / 2;
        var cy = rect.Y + rect.Height / 2;

        // Push a transform when rotation or flip is needed.
        bool needTransform = sd.RotationAngle != 0 || sd.FlipH || sd.FlipV;
        IDisposable? xformState = null;

        if (needTransform)
        {
            // Avalonia ScaleTransform has no center-point overload.
            // Build a composite matrix: translate to origin, scale/rotate, translate back.
            var mat = Matrix.Identity;
            // Translate to shape centre.
            mat = mat * Matrix.CreateTranslation(-cx, -cy);
            // Apply flip(s).
            if (sd.FlipH) mat = mat * new Matrix(-1, 0, 0, 1, 0, 0);
            if (sd.FlipV) mat = mat * new Matrix(1, 0, 0, -1, 0, 0);
            // Apply rotation (Avalonia RotateTransform takes degrees; convert to radians for matrix).
            if (sd.RotationAngle != 0)
            {
                var rad = sd.RotationAngle * Math.PI / 180.0;
                mat = mat * Matrix.CreateRotation(rad);
            }
            // Translate back.
            mat = mat * Matrix.CreateTranslation(cx, cy);
            xformState = context.PushTransform(mat);
        }

        try
        {
            DrawFloatingShapeEffects(context, sd, rect);

            // ── Draw geometry ──────────────────────────────────────────────────────
            if (sd.CustomGeo is { } cg && cg.Segments.Count > 0)
            {
                // Freeform custom geometry: build a StreamGeometry from the 21600×21600 grid segments.
                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    bool inFigure   = false;
                    bool closeFig   = false;
                    Point startPt   = default;
                    var linePts     = new System.Collections.Generic.List<Point>();

                    void FlushFigure()
                    {
                        if (!inFigure) return;
                        ctx.BeginFigure(startPt, isFilled: sd.FillBrush is not null);
                        foreach (var lp in linePts) ctx.LineTo(lp);
                        if (closeFig) ctx.EndFigure(true);
                        linePts.Clear();
                        inFigure  = false;
                        closeFig  = false;
                    }

                    foreach (var seg in cg.Segments)
                    {
                        if (seg.Kind == CustomSegmentKind.MoveTo && seg.Point is not null)
                        {
                            FlushFigure();
                            startPt   = new Point(
                                rect.X + seg.Point.X / (double)cg.Width  * rect.Width,
                                rect.Y + seg.Point.Y / (double)cg.Height * rect.Height);
                            inFigure  = true;
                        }
                        else if (seg.Kind == CustomSegmentKind.LineTo && seg.Point is not null && inFigure)
                        {
                            linePts.Add(new Point(
                                rect.X + seg.Point.X / (double)cg.Width  * rect.Width,
                                rect.Y + seg.Point.Y / (double)cg.Height * rect.Height));
                        }
                        else if (seg.Kind == CustomSegmentKind.Close && inFigure)
                        {
                            closeFig = true;
                        }
                    }
                    FlushFigure();
                }
                context.DrawGeometry(sd.FillBrush, sd.OutlinePen, geo);
            }
            else
            {
                switch (sd.Kind)
                {
                    case ShapeKind.Ellipse:
                        context.DrawEllipse(sd.FillBrush, sd.OutlinePen,
                            new Point(cx, cy), rect.Width / 2, rect.Height / 2);
                        break;

                    case ShapeKind.RoundedRectangle:
                    {
                        // Build a rounded rectangle with a 6pt corner radius (matches WPF reference CornerRadius=6).
                        var cornerR = Math.Min(6 * PxPerPoint, Math.Min(rect.Width, rect.Height) / 4);
                        var geo = BuildRoundedRectGeometry(rect, cornerR);
                        context.DrawGeometry(sd.FillBrush, sd.OutlinePen, geo);
                        break;
                    }

                    default: // Rectangle, TextBox — plain rect
                        context.DrawRectangle(sd.FillBrush, sd.OutlinePen, rect);
                        break;
                }
            }

            // ── Draw shape text (UU3 fix) ──────────────────────────────────────────
            // WPF reference (BuildShapeRun ~8319-8324): TextBlock with Margin=4, TextWrapping.Wrap,
            // VerticalAlignment.Top. Avalonia previously centred a single non-wrapping line.
            // Fix: set MaxTextWidth to the inset shape width to enable wrapping; top-anchor with 4px inset.
            if (!string.IsNullOrEmpty(sd.Text))
            {
                const double TextInset = 4.0; // matches WPF Margin(4) on TextBlock
                var textFmt = new RunFormatting { FontSizePt = 9 };
                var insetWidth = Math.Max(1, rect.Width - 2 * TextInset);
                var ft = Build(sd.Text, textFmt);
                ft.MaxTextWidth = insetWidth; // enables word wrapping (FormattedText clips+wraps at this width)
                // Top-anchor: place text at the top of the shape with the inset offset.
                var tx = rect.X + TextInset;
                var ty = rect.Y + TextInset;
                using var _ = context.PushClip(rect);
                context.DrawText(ft, new Point(tx, ty));
            }
        }
        finally
        {
            xformState?.Dispose();
        }
    }

    private static void DrawFloatingShapeEffects(DrawingContext context, FloatingShapeData sd, Rect rect)
    {
        var effects = sd.Effects;
        if (!effects.HasAny)
            return;

        if (effects.HasShadow)
        {
            var shadowColor = TryParseAvaloniaColor(effects.ShadowColorHex, out var parsed)
                ? parsed
                : Colors.Black;
            var radians = effects.ShadowDirectionDegrees * Math.PI / 180.0;
            var distance = effects.ShadowDistanceDip > 0 ? effects.ShadowDistanceDip : 3.0;
            var offsetX = Math.Cos(radians) * distance;
            var offsetY = Math.Sin(radians) * distance;
            var spread = Math.Max(0, effects.ShadowBlurDip * 0.12);
            DrawFloatingShapeEffectGeometry(
                context,
                sd,
                OffsetAndInflate(rect, offsetX, offsetY, spread),
                EffectBrush(shadowColor, effects.ShadowOpacity));
        }

        if (effects.HasGlow)
        {
            var glowColor = TryParseAvaloniaColor(effects.GlowColorHex, out var parsed)
                ? parsed
                : Color.FromRgb(0x44, 0x72, 0xC4);
            var radius = Math.Max(2.0, effects.GlowRadiusDip);
            DrawFloatingShapeEffectGeometry(
                context,
                sd,
                OffsetAndInflate(rect, 0, 0, radius * 0.55),
                EffectBrush(glowColor, effects.GlowOpacity * 0.24));
            DrawFloatingShapeEffectGeometry(
                context,
                sd,
                OffsetAndInflate(rect, 0, 0, radius * 0.25),
                EffectBrush(glowColor, effects.GlowOpacity * 0.36));
        }
    }

    private static void DrawFloatingShapeEffectGeometry(
        DrawingContext context,
        FloatingShapeData sd,
        Rect rect,
        IBrush brush)
    {
        if (sd.CustomGeo is { } cg && cg.Segments.Count > 0)
        {
            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                var inFigure = false;
                var closeFigure = false;
                Point startPoint = default;
                var linePoints = new System.Collections.Generic.List<Point>();

                void FlushFigure()
                {
                    if (!inFigure)
                        return;

                    ctx.BeginFigure(startPoint, isFilled: true);
                    foreach (var point in linePoints)
                        ctx.LineTo(point);
                    if (closeFigure)
                        ctx.EndFigure(true);
                    linePoints.Clear();
                    inFigure = false;
                    closeFigure = false;
                }

                foreach (var segment in cg.Segments)
                {
                    if (segment.Kind == CustomSegmentKind.MoveTo && segment.Point is not null)
                    {
                        FlushFigure();
                        startPoint = new Point(
                            rect.X + segment.Point.X / (double)cg.Width * rect.Width,
                            rect.Y + segment.Point.Y / (double)cg.Height * rect.Height);
                        inFigure = true;
                    }
                    else if (segment.Kind == CustomSegmentKind.LineTo && segment.Point is not null && inFigure)
                    {
                        linePoints.Add(new Point(
                            rect.X + segment.Point.X / (double)cg.Width * rect.Width,
                            rect.Y + segment.Point.Y / (double)cg.Height * rect.Height));
                    }
                    else if (segment.Kind == CustomSegmentKind.Close && inFigure)
                    {
                        closeFigure = true;
                    }
                }

                FlushFigure();
            }

            context.DrawGeometry(brush, null, geo);
            return;
        }

        switch (sd.Kind)
        {
            case ShapeKind.Ellipse:
                context.DrawEllipse(brush, null, rect.Center, rect.Width / 2, rect.Height / 2);
                break;

            case ShapeKind.RoundedRectangle:
                var cornerRadius = Math.Min(6 * PxPerPoint, Math.Min(rect.Width, rect.Height) / 4);
                context.DrawGeometry(brush, null, BuildRoundedRectGeometry(rect, cornerRadius));
                break;

            default:
                context.DrawRectangle(brush, null, rect);
                break;
        }
    }

    private static Rect OffsetAndInflate(Rect rect, double offsetX, double offsetY, double inflate) =>
        new(
            rect.X + offsetX - inflate,
            rect.Y + offsetY - inflate,
            Math.Max(1, rect.Width + 2 * inflate),
            Math.Max(1, rect.Height + 2 * inflate));

    private static IBrush EffectBrush(Color color, double opacity)
    {
        var alpha = (byte)Math.Clamp((int)Math.Round(255 * Math.Clamp(opacity, 0, 1)), 0, 255);
        return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
    }

    /// <summary>
    /// Builds a rounded-rectangle <see cref="StreamGeometry"/> with a uniform corner radius.
    /// </summary>
    private static StreamGeometry BuildRoundedRectGeometry(Rect rect, double r)
    {
        var geo = new StreamGeometry();
        using var ctx = geo.Open();
        ctx.BeginFigure(new Point(rect.X + r, rect.Y), isFilled: true);
        ctx.LineTo(new Point(rect.Right - r, rect.Y));
        ctx.ArcTo(new Point(rect.Right, rect.Y + r),
            new Size(r, r), 0, false, SweepDirection.Clockwise);
        ctx.LineTo(new Point(rect.Right, rect.Bottom - r));
        ctx.ArcTo(new Point(rect.Right - r, rect.Bottom),
            new Size(r, r), 0, false, SweepDirection.Clockwise);
        ctx.LineTo(new Point(rect.X + r, rect.Bottom));
        ctx.ArcTo(new Point(rect.X, rect.Bottom - r),
            new Size(r, r), 0, false, SweepDirection.Clockwise);
        ctx.LineTo(new Point(rect.X, rect.Y + r));
        ctx.ArcTo(new Point(rect.X + r, rect.Y),
            new Size(r, r), 0, false, SweepDirection.Clockwise);
        ctx.EndFigure(true);
        return geo;
    }

    private static IBrush FloatPlaceholderFill { get; } =
        new SolidColorBrush(Color.FromArgb(0x44, 0x33, 0x99, 0xFF));
    private static Pen FloatPlaceholderPen { get; } =
        new Pen(new SolidColorBrush(Color.FromArgb(0xBB, 0x33, 0x99, 0xFF)), 1.0,
            new DashStyle([4, 3], 0));

    // ── AV-FLSEL: floating selection rendering + hit-test + edit methods ───────────────────────────

    // Selection outline pen: solid blue, 1.5px.
    private static readonly Pen FloatSelectionPen =
        new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)), 1.5);
    // Handle fill: white square with blue border.
    private static readonly IBrush FloatHandleFill   = Brushes.White;
    private static readonly Pen    FloatHandlePen    =
        new Pen(new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)), 1.0);
    private const double FloatHandleSize = 7; // handle square side length in px

    private static DocumentFloatRect ToPlannerRect(Rect rect) =>
        new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Rect ToAvaloniaRect(DocumentFloatRect rect) =>
        new(rect.XDip, rect.YDip, rect.WidthDip, rect.HeightDip);

    private static DocumentFloatPoint ToPlannerPoint(Point point) =>
        new(point.X, point.Y);

    /// <summary>
    /// Resolves the current RotationAngle/FlipH/FlipV for the floating object at (blockIndex, runIndex,
    /// kind), so handle geometry, resize, and hit-test can un-rotate/un-flip against the SAME transform
    /// DrawFloatingShape uses to render it (see ~line 5828: <c>needTransform = RotationAngle != 0 ||
    /// FlipH || FlipV</c>, flip-then-rotate about the rect centre). Only <see cref="InlineImage"/> and
    /// <see cref="Shape"/> carry rotation/flip today; every other floating kind (Chart/WordArt/SmartArt/
    /// Group) has none, so they fall back to the identity (0, false, false) — same as an unrotated image
    /// or shape.
    /// </summary>
    private (double Angle, bool FlipH, bool FlipV) GetFloatRotation(int blockIndex, int runIndex, string kind)
    {
        if (blockIndex < 0 || blockIndex >= _doc.Blocks.Count) return (0, false, false);
        if (_doc.Blocks[blockIndex] is not Paragraph para) return (0, false, false);
        if (runIndex < 0 || runIndex >= para.Runs.Count) return (0, false, false);
        var run = para.Runs[runIndex];

        return kind switch
        {
            "Image" when run.Image is { } img => (img.RotationAngle, img.FlipH, img.FlipV),
            "Shape" when run.Shape is { } shape => (shape.RotationAngle, shape.FlipH, shape.FlipV),
            _ => (0, false, false),
        };
    }

    private static DocumentFloatingHandle ToPlannerHandle(FloatHandle handle) => handle switch
    {
        FloatHandle.Body => DocumentFloatingHandle.Body,
        FloatHandle.TopLeft => DocumentFloatingHandle.TopLeft,
        FloatHandle.Top => DocumentFloatingHandle.Top,
        FloatHandle.TopRight => DocumentFloatingHandle.TopRight,
        FloatHandle.Left => DocumentFloatingHandle.Left,
        FloatHandle.Right => DocumentFloatingHandle.Right,
        FloatHandle.BottomLeft => DocumentFloatingHandle.BottomLeft,
        FloatHandle.Bottom => DocumentFloatingHandle.Bottom,
        FloatHandle.BottomRight => DocumentFloatingHandle.BottomRight,
        _ => DocumentFloatingHandle.None,
    };

    private static FloatHandle FromPlannerHandle(DocumentFloatingHandle handle) => handle switch
    {
        DocumentFloatingHandle.Body => FloatHandle.Body,
        DocumentFloatingHandle.TopLeft => FloatHandle.TopLeft,
        DocumentFloatingHandle.Top => FloatHandle.Top,
        DocumentFloatingHandle.TopRight => FloatHandle.TopRight,
        DocumentFloatingHandle.Left => FloatHandle.Left,
        DocumentFloatingHandle.Right => FloatHandle.Right,
        DocumentFloatingHandle.BottomLeft => FloatHandle.BottomLeft,
        DocumentFloatingHandle.Bottom => FloatHandle.Bottom,
        DocumentFloatingHandle.BottomRight => FloatHandle.BottomRight,
        _ => FloatHandle.None,
    };

    /// <summary>
    /// Draws the selection outline (dashed blue rectangle) and 8 resize handles around
    /// the selected floating object's page-space bounding rect.
    /// </summary>
    private void DrawFloatingSelection(DrawingContext context, Rect rect, int blockIndex, int runIndex, string kind)
    {
        context.DrawRectangle(null, FloatSelectionPen, rect);

        var (angle, flipH, flipV) = GetFloatRotation(blockIndex, runIndex, kind);
        foreach (var (_, hRect) in HandleRects(rect, angle, flipH, flipV))
        {
            context.FillRectangle(FloatHandleFill, hRect);
            context.DrawRectangle(null, FloatHandlePen, hRect);
        }
    }

    // ── AV-HANDLES: handle geometry, hit-test, cursors, resize-drag commit ──────────────────────────

    // Minimum floating-object size in points (Word clamps tiny drags so the object never collapses).
    private const double MinFloatSizePt = 9; // ~0.125in

    /// <summary>
    /// Returns the eight resize-handle squares (corners + edge midpoints) for a selection
    /// <paramref name="rect"/>, each tagged with the <see cref="FloatHandle"/> it represents. When
    /// <paramref name="rotationAngle"/>/<paramref name="flipH"/>/<paramref name="flipV"/> are set, the
    /// drawn handle positions are carried through the same transform DrawFloatingShape renders with, so
    /// they track the VISIBLE rotated/flipped corners instead of the plain axis-aligned box. Shared by
    /// the renderer and the pointer hit-test so the drawn squares and the clickable targets never drift
    /// apart.
    /// </summary>
    private static IEnumerable<(FloatHandle Handle, Rect Rect)> HandleRects(
        Rect rect, double rotationAngle = 0, bool flipH = false, bool flipV = false)
    {
        foreach (var handle in DocumentViewLayoutPlanner.BuildFloatingHandleRects(
                     ToPlannerRect(rect),
                     FloatHandleSize,
                     rotationAngle,
                     flipH,
                     flipV))
            yield return (FromPlannerHandle(handle.Handle), ToAvaloniaRect(handle.Rect));
    }

    /// <summary>
    /// AV-HANDLES test seam: the eight resize-handle rects for the CURRENT floating selection,
    /// keyed by <see cref="FloatHandle"/>. Empty when nothing is selected. Lets tests assert that
    /// selecting a float exposes exactly eight handles in the expected geometry.
    /// </summary>
    public IReadOnlyDictionary<FloatHandle, Rect> HandleRectsForSelection()
    {
        var dict = new Dictionary<FloatHandle, Rect>();
        if (_selectedFloating is { } sel)
        {
            var (angle, flipH, flipV) = GetFloatRotation(sel.BlockIndex, sel.RunIndex, sel.Kind);
            foreach (var (h, r) in HandleRects(sel.Rect, angle, flipH, flipV))
                dict[h] = r;
        }
        return dict;
    }

    /// <summary>
    /// Hit-tests <paramref name="point"/> against the current selection's handles + body. A handle
    /// hit (within the handle square, padded slightly for easier grabbing) wins; otherwise a point
    /// inside the selection rect is <see cref="FloatHandle.Body"/>; anything else is
    /// <see cref="FloatHandle.None"/>. No selection → <see cref="FloatHandle.None"/>. Accounts for the
    /// selected object's rotation/flip so a click on the visible (rotated/flipped) handle or body
    /// resolves correctly — see <see cref="DocumentViewLayoutPlanner.HitTestFloatingHandle"/>.
    /// </summary>
    private FloatHandle HitTestHandle(Point point)
    {
        if (_selectedFloating is not { } sel) return FloatHandle.None;
        var (angle, flipH, flipV) = GetFloatRotation(sel.BlockIndex, sel.RunIndex, sel.Kind);
        return FromPlannerHandle(DocumentViewLayoutPlanner.HitTestFloatingHandle(
            ToPlannerRect(sel.Rect),
            ToPlannerPoint(point),
            FloatHandleSize,
            hitPaddingDip: 2,
            angle,
            flipH,
            flipV));
    }

    /// <summary>The mouse cursor appropriate for hovering a given handle (or moving the body).</summary>
    private static Cursor CursorForHandle(FloatHandle handle) => handle switch
    {
        FloatHandle.TopLeft or FloatHandle.BottomRight => new Cursor(StandardCursorType.TopLeftCorner),
        FloatHandle.TopRight or FloatHandle.BottomLeft => new Cursor(StandardCursorType.TopRightCorner),
        FloatHandle.Left or FloatHandle.Right          => new Cursor(StandardCursorType.SizeWestEast),
        FloatHandle.Top or FloatHandle.Bottom          => new Cursor(StandardCursorType.SizeNorthSouth),
        FloatHandle.Body                               => new Cursor(StandardCursorType.SizeAll),
        _                                              => Cursor.Default,
    };

    /// <summary>
    /// Computes the new page-space rect while dragging a resize <paramref name="handle"/> from the
    /// drag-start <paramref name="baseRect"/> to the current pointer position. The opposite edge(s)
    /// stay anchored; corners move both dimensions, edges only one. <paramref name="aspect"/> (Shift on
    /// a corner) preserves the base aspect ratio. The result is clamped so width/height never fall below
    /// <see cref="MinFloatSizePt"/> (converted to px). When <paramref name="rotationAngle"/>/<paramref
    /// name="flipH"/>/<paramref name="flipV"/> are set, the pointer is resolved against the object's OWN
    /// (rotated/flipped) axes rather than the screen axes — see
    /// <see cref="DocumentViewLayoutPlanner.BuildFloatingResizeRect"/>.
    /// </summary>
    private static Rect ResizeRect(
        Rect baseRect, FloatHandle handle, Point pointer, bool aspect,
        double rotationAngle = 0, bool flipH = false, bool flipV = false)
    {
        return ToAvaloniaRect(DocumentViewLayoutPlanner.BuildFloatingResizeRect(
            ToPlannerRect(baseRect),
            ToPlannerHandle(handle),
            ToPlannerPoint(pointer),
            preserveAspect: aspect,
            minimumSizeDip: MinFloatSizePt * PxPerPoint,
            rotationAngle,
            flipH,
            flipV));
    }

    /// <summary>
    /// Commits a handle-resize: converts the dragged page-space <paramref name="newRect"/> to model
    /// width/height (and a position delta when the anchored edge actually moved) and issues the
    /// undoable command(s). When only the size changed, a single <see cref="SetFloatingSizeCommand"/>
    /// (or image-size command) is pushed; when the top/left edge moved too, the size + position commands
    /// are wrapped in one <see cref="CompositeDocumentCommand"/> so a single undo reverts the whole drag.
    /// </summary>
    private void CommitFloatResize(int blockIndex, int runIndex, string kind, Rect baseRect, Rect newRect)
    {
        if (blockIndex < 0 || blockIndex >= _doc.Blocks.Count) return;
        if (_doc.Blocks[blockIndex] is not Paragraph para) return;
        if (runIndex < 0 || runIndex >= para.Runs.Count) return;
        var run = para.Runs[runIndex];

        var newWidthPt  = newRect.Width  / PxPerPoint;
        var newHeightPt = newRect.Height / PxPerPoint;
        // Offset delta in points: how far the top-left corner moved (non-zero only for top/left handles).
        var dxPt = (newRect.Left - baseRect.Left) / PxPerPoint;
        var dyPt = (newRect.Top  - baseRect.Top)  / PxPerPoint;
        bool anchorMoved = Math.Abs(dxPt) > 0.01 || Math.Abs(dyPt) > 0.01;

        var sizeCmd = new SetFloatingSizeCommand(blockIndex, runIndex, newWidthPt, newHeightPt);

        if (!anchorMoved)
        {
            _bus.Execute(sizeCmd);
        }
        else if (kind == "Image" && run.Image is { IsFloating: true } img)
        {
            var posCmd = new NudgeImagePositionCommand(blockIndex, runIndex,
                img.HorizontalOffsetPt + dxPt, img.VerticalOffsetPt + dyPt);
            _bus.Execute(new CompositeDocumentCommand("Resize",
                new IDocumentCommand[] { sizeCmd, posCmd }));
        }
        else if (SetFloatingPositionCommand.GetFloatingPlacement(run) is { } pl)
        {
            var posCmd = new SetFloatingPositionCommand(blockIndex, runIndex,
                pl.HorizontalOffsetPt + dxPt, pl.VerticalOffsetPt + dyPt,
                pl.HorizontalAnchor, pl.VerticalAnchor);
            _bus.Execute(new CompositeDocumentCommand("Resize",
                new IDocumentCommand[] { sizeCmd, posCmd }));
        }
        else
        {
            // FB4 guard: the anchored top/left edge moved (a top/left handle was dragged), but this
            // float has no placement to carry the position delta on (non-Image kind with no
            // FloatingPlacement available). Committing the size-only command here would grow the object
            // from its ORIGINAL top-left, silently sliding the anchored edge the user dragged instead of
            // holding it fixed — so skip the commit entirely rather than apply a visually-wrong resize.
        }

        InvalidateLayoutAndVisual();
        Relayout(_laidOutWidth > 0 ? _laidOutWidth : FallbackWidth);
        RefreshSelectedFloatingRect(blockIndex, runIndex, kind);
    }

    // ── AV-HANDLES: pointer-driven drag test seams ──────────────────────────────────────────────────

    /// <summary>
    /// Test seam: begin a drag on the current floating selection at page-space <paramref name="start"/>.
    /// The handle under that point decides whether the drag moves or resizes the object. Returns the
    /// resolved handle (<see cref="FloatHandle.None"/> if the point is off the selection, in which case
    /// no drag starts). Mirrors what <see cref="OnPointerPressed"/> does for a real press.
    /// </summary>
    public FloatHandle BeginFloatDrag(Point start)
    {
        if (_selectedFloating is not { } sel) return FloatHandle.None;
        var handle = HitTestHandle(start);
        if (handle == FloatHandle.None) return FloatHandle.None;
        _floatDragState = (start, sel.Rect, handle);
        return handle;
    }

    /// <summary>
    /// Test seam: drive an in-flight drag (started via <see cref="BeginFloatDrag"/>) to page-space
    /// <paramref name="to"/>, optionally holding <paramref name="shift"/> for aspect-locked corner
    /// resizing. Updates the transient selection rect exactly as live pointer movement would. No-op
    /// when no drag is active.
    /// </summary>
    public void SimulateDragTo(Point to, bool shift = false)
    {
        UpdateFloatDrag(to, shift);
    }

    /// <summary>
    /// Test seam: release an in-flight drag at page-space <paramref name="to"/>, committing the move or
    /// resize through the undoable command bus (a single undo reverts it). No-op when no drag is active.
    /// </summary>
    public void EndFloatDrag(Point to, bool shift = false)
    {
        CommitFloatDrag(to, shift);
    }

    /// <summary>
    /// Test seam: cancel an in-flight drag, reverting the transient rect to the drag-start geometry
    /// without touching the model — exactly what pressing Esc mid-drag does.
    /// </summary>
    public bool CancelFloatDrag()
    {
        if (_floatDragState is not { } drag || _selectedFloating is not { } sel)
            return false;
        _selectedFloating = sel with { Rect = drag.FloatRect };
        _floatDragState = null;
        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// Updates the transient selection rect for an in-flight drag (move or resize). Shared by the live
    /// pointer handler and the <see cref="SimulateDragTo"/> test seam.
    /// </summary>
    private void UpdateFloatDrag(Point point, bool shift)
    {
        if (_floatDragState is not { } drag || _selectedFloating is not { } sel) return;
        Rect newRect;
        if (drag.Handle == FloatHandle.Body)
        {
            newRect = ToAvaloniaRect(DocumentViewLayoutPlanner.BuildFloatingMoveRect(
                ToPlannerRect(drag.FloatRect),
                ToPlannerPoint(drag.PointerDown),
                ToPlannerPoint(point)));
        }
        else
        {
            var (angle, flipH, flipV) = GetFloatRotation(sel.BlockIndex, sel.RunIndex, sel.Kind);
            newRect = ResizeRect(drag.FloatRect, drag.Handle, point, shift, angle, flipH, flipV);
        }
        _selectedFloating = sel with { Rect = newRect };
        InvalidateVisual();
    }

    /// <summary>
    /// Commits an in-flight drag at the release point: a move routes to
    /// <see cref="CommitFloatDragMove"/>; a resize routes to <see cref="CommitFloatResize"/>. Below a
    /// 1px threshold the drag is treated as a plain click (no model change). Clears the drag state.
    /// </summary>
    private void CommitFloatDrag(Point releasePoint, bool shift)
    {
        if (_floatDragState is not { } drag || _selectedFloating is not { } sel)
        {
            _floatDragState = null;
            return;
        }

        if (drag.Handle == FloatHandle.Body)
        {
            var dxPt = (releasePoint.X - drag.PointerDown.X) / PxPerPoint;
            var dyPt = (releasePoint.Y - drag.PointerDown.Y) / PxPerPoint;
            if (Math.Abs(dxPt) >= 1 || Math.Abs(dyPt) >= 1)
                CommitFloatDragMove(sel.BlockIndex, sel.RunIndex, dxPt, dyPt, sel.Kind);
        }
        else
        {
            var (angle, flipH, flipV) = GetFloatRotation(sel.BlockIndex, sel.RunIndex, sel.Kind);
            var newRect = ResizeRect(drag.FloatRect, drag.Handle, releasePoint, shift, angle, flipH, flipV);
            if (Math.Abs(newRect.Width - drag.FloatRect.Width) >= 1 ||
                Math.Abs(newRect.Height - drag.FloatRect.Height) >= 1)
                CommitFloatResize(sel.BlockIndex, sel.RunIndex, sel.Kind, drag.FloatRect, newRect);
        }
        _floatDragState = null;
    }

    /// <summary>
    /// Draws the AV-COMMENT anchor decorations over the laid-out text: an amber underline beneath every
    /// commented glyph (the in-text anchor mark), and one minimal marker in the right margin per
    /// (comment, line) — a small balloon bracket plus the author's initial — aligned to that line. The
    /// margin marker only renders when there is room to the right of the content column (PrintLayout has
    /// a page margin there); otherwise the in-text underline alone marks the anchor. Resolved threads draw
    /// muted/grey to mirror Word's de-emphasised resolved state.
    /// </summary>
    private void DrawCommentAnchors(DrawingContext context)
    {
        var anchors = CommentAnchorGlyphSnapshot(highlightedOnly: true);
        if (anchors.Count == 0)
            return;

        // Right-margin x: just right of the content column. Available when the page extends past content
        // (the right page margin). Fall back to the control's right edge in non-paged modes.
        var pageRight = _pageWidth > 0 ? _pageLeft + _pageWidth : _laidOutWidth;
        var marginX = _contentLeft + _contentWidth + 6;
        var hasMargin = pageRight - (_contentLeft + _contentWidth) > 14;

        // Track, per (comment id, line top Y), whether we've already drawn a margin marker for that line
        // (one marker per anchor line, not one per glyph).
        var markedLines = new HashSet<(int Id, long Y)>();

        foreach (var (id, rect) in anchors)
        {
            var resolved = IsCommentResolved(id);

            // In-text anchor mark: amber underline just below the glyph baseline band.
            var underlineY = rect.Y + rect.Height * 0.90;
            var pen = resolved ? ResolvedCommentUnderlinePen : CommentUnderlinePen;
            context.DrawLine(pen, new Point(rect.X, underlineY), new Point(rect.Right, underlineY));

            // Right-margin marker: one per anchor line.
            if (!hasMargin)
                continue;
            var lineKey = (id, (long)Math.Round(rect.Y));
            if (!markedLines.Add(lineKey))
                continue;

            DrawCommentMarginMarker(context, marginX, rect.Y, rect.Height, id, resolved);
        }
    }

    private void DrawSimpleMarkupChangeBars(DrawingContext context)
    {
        foreach (var (_, rect) in SimpleMarkupChangeBarSnapshot())
        {
            var x = rect.X + rect.Width / 2;
            context.DrawLine(SimpleMarkupChangeBarPen, new Point(x, rect.Y), new Point(x, rect.Bottom));
        }
    }

    /// <summary>
    /// Draws a single minimal comment marker in the right margin aligned to an anchor line: a small
    /// rounded balloon filled in the comment colour, with the author's initial drawn inside it.
    /// </summary>
    private void DrawCommentMarginMarker(DrawingContext context, double x, double lineY, double lineHeight, int id, bool resolved)
    {
        const double size = 14;
        var top = lineY + Math.Max(0, (lineHeight - size) / 2);
        var balloon = new Rect(x, top, size, size);
        var fill = resolved ? ResolvedCommentMarkerBrush : CommentMarkerBrush;
        context.DrawRectangle(fill, null, new RoundedRect(balloon, 3));

        var initial = CommentInitial(id);
        if (string.IsNullOrEmpty(initial))
            return;
        var ft = Build(initial, new RunFormatting { FontSizePt = 8, ColorHex = "#FFFFFF", Bold = true });
        context.DrawText(ft, new Point(x + (size - ft.Width) / 2, top + (size - ft.Height) / 2));
    }

    /// <summary>True when the top-level comment thread anchoring <paramref name="commentId"/> is resolved.</summary>
    private bool IsCommentResolved(int commentId)
    {
        var topId = DeleteCommentCommand.ResolveTopLevel(_doc, commentId);
        return _doc.Comments.TryGetValue(topId, out var comment) && comment.Resolved;
    }

    /// <summary>The author's initial (or 'C') for the comment thread anchoring <paramref name="commentId"/>.</summary>
    private string CommentInitial(int commentId)
    {
        var topId = DeleteCommentCommand.ResolveTopLevel(_doc, commentId);
        if (!_doc.Comments.TryGetValue(topId, out var comment))
            return string.Empty;
        if (!string.IsNullOrWhiteSpace(comment.Initials))
            return comment.Initials.Trim()[..1].ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(comment.Author))
            return comment.Author.Trim()[..1].ToUpperInvariant();
        return "C";
    }

    /// <summary>
    /// Hit-tests <paramref name="point"/> against all floating objects (images, shapes, charts,
    /// WordArts, SmartArts, groups). Returns the topmost (highest z-order; in-front preferred
    /// over behind-text) object that contains the point, or false if none.
    /// Requires at least one layout pass to have been completed.
    /// </summary>
    private bool TryHitTestFloat(Point point,
        out (int BlockIndex, int RunIndex, string Kind, Rect Rect) hit)
    {
        hit = default;
        if (_laidOutWidth < 0) Relayout(FallbackWidth);

        // Rotation/flip are carried on each snapshot (threaded from the model's Image/Shape
        // RotationAngle/FlipH/FlipV in BuildFloatingObjectSnapshots) and un-applied per-candidate
        // inside HitTestFloatingObject before the containment test, so a rotated/flipped float's visible
        // (not axis-aligned) bounds decide the hit and the z-order winner among overlapping floats.
        var winner = DocumentViewLayoutPlanner.HitTestFloatingObject(
            _floatingSnapshots,
            ToPlannerPoint(point));
        if (winner is null)
            return false;

        hit = (winner.BlockIndex, winner.RunIndex, winner.TypeTag, ToAvaloniaRect(winner.Rect));
        return true;
    }

    /// <summary>
    /// Commits a drag-move by computing the new HOffset/VOffset in points (delta from the layout-time
    /// position, converted back through PxPerPoint) and issuing the appropriate move command.
    /// For Images: uses <see cref="NudgeImagePositionCommand"/> (direct offset fields).
    /// For all others: uses <see cref="SetFloatingPositionCommand"/> (via FloatingPlacement).
    /// </summary>
    private void CommitFloatDragMove(int blockIndex, int runIndex, double dxPt, double dyPt, string kind)
    {
        if (blockIndex < 0 || blockIndex >= _doc.Blocks.Count) return;
        if (_doc.Blocks[blockIndex] is not Paragraph para) return;
        if (runIndex < 0 || runIndex >= para.Runs.Count) return;
        var run = para.Runs[runIndex];

        if (kind == "Image" && run.Image is { IsFloating: true } img)
        {
            var newH = img.HorizontalOffsetPt + dxPt;
            var newV = img.VerticalOffsetPt   + dyPt;
            _bus.Execute(new NudgeImagePositionCommand(blockIndex, runIndex, newH, newV));
        }
        else
        {
            var pl = SetFloatingPositionCommand.GetFloatingPlacement(run);
            if (pl is null) return;
            var newH = pl.HorizontalOffsetPt + dxPt;
            var newV = pl.VerticalOffsetPt   + dyPt;
            _bus.Execute(new SetFloatingPositionCommand(blockIndex, runIndex,
                newH, newV, pl.HorizontalAnchor, pl.VerticalAnchor));
        }
        InvalidateLayoutAndVisual();
        // Refresh selected state after re-layout (the Rect changes).
        Relayout(_laidOutWidth > 0 ? _laidOutWidth : FallbackWidth);
        RefreshSelectedFloatingRect(blockIndex, runIndex, kind);
    }

    /// <summary>
    /// Arrow-key nudge: shifts the selected floating object by (dxPt, dyPt) points, routing through
    /// the appropriate undoable command. No-op if nothing is selected or the block/run is stale.
    /// </summary>
    private void NudgeSelectedFloating(double dxPt, double dyPt)
    {
        if (_selectedFloating is not { } sel) return;
        CommitFloatDragMove(sel.BlockIndex, sel.RunIndex, dxPt, dyPt, sel.Kind);
    }

    /// <summary>
    /// Rescans the floating lists after a layout pass to re-anchor <see cref="_selectedFloating"/>'s
    /// Rect to the newly computed page-space position.
    /// </summary>
    private void RefreshSelectedFloatingRect(int blockIndex, int runIndex, string kind)
    {
        var found = _floatingSnapshots.FirstOrDefault(snapshot =>
            snapshot.BlockIndex == blockIndex
            && snapshot.RunIndex == runIndex
            && snapshot.TypeTag == kind);
        if (found is not null)
            _selectedFloating = (blockIndex, runIndex, kind, ToAvaloniaRect(found.Rect));
        else
        {
            _selectedFloating = null; // object was deleted / moved out of view
            _selectedFloatingObjects.RemoveAll(item =>
                item.BlockIndex == blockIndex && item.RunIndex == runIndex);
        }
        RaiseFloatingSelectionChangedIfIdentityChanged();
        InvalidateVisual();
    }

    // ── AV-FLSEL: public edit API ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Info about the currently selected floating object, or null if nothing is selected.
    /// Kind = "Image" | "Shape" | "Chart" | "WordArt" | "SmartArt" | "Group".
    /// Placement is the model's current FloatingPlacement (null for Image, which stores offsets inline).
    /// </summary>
    public (int BlockIndex, int RunIndex, string Kind, Rect Rect)? SelectedFloatingInfo
        => _selectedFloating;

    /// <summary>Current floating-object multi-selection as model coordinates.</summary>
    public IReadOnlyList<(int BlockIndex, int RunIndex, string Kind)> SelectedFloatingObjects =>
        _selectedFloatingObjects.AsReadOnly();

    /// <summary>True when the selection contains at least two groupable floating object runs.</summary>
    public bool HasMultipleFloatingObjectsSelected =>
        SelectedGroupMemberLocations().Count >= 2;

    /// <summary>True when exactly one valid drawing group run is selected.</summary>
    public bool IsGroupSelected =>
        _selectedFloatingObjects.Count == 1
        && _selectedFloatingObjects[0].Kind == "Group"
        && TryGetRun(_selectedFloatingObjects[0].BlockIndex, _selectedFloatingObjects[0].RunIndex, out var run)
        && run.DrawingGroup is { IsValid: true };

    /// <summary>
    /// Returns the selected floating shape or text box, or null when the current drawing selection is
    /// a non-shape object such as WordArt or a group.
    /// </summary>
    public Shape? SelectedFloatingShape() =>
        SelectedFloatingShapeLocation()?.Shape;

    /// <summary>Deselect any selected floating object. No-op when nothing is selected.</summary>
    public void DeselectFloating()
    {
        if (_selectedFloating is null && _selectedFloatingObjects.Count == 0) return;
        _selectedFloating = null;
        _selectedFloatingObjects.Clear();
        _floatDragState   = null;
        RaiseFloatingSelectionChangedIfIdentityChanged();
        InvalidateVisual();
    }

    /// <summary>
    /// Programmatically select the floating object at (blockIndex, runIndex). Triggers a layout pass
    /// if needed and refreshes the selection rect. Used by tests and the host shell.
    /// </summary>
    public void SelectFloating(int blockIndex, int runIndex, bool addToMultiSelect = false)
    {
        if (_laidOutWidth < 0) Relayout(FallbackWidth);
        // Determine kind.
        if (blockIndex < 0 || blockIndex >= _doc.Blocks.Count) return;
        if (_doc.Blocks[blockIndex] is not Paragraph para) return;
        if (runIndex < 0 || runIndex >= para.Runs.Count) return;
        var run = para.Runs[runIndex];
        string kind;
        if (run.Image is { IsFloating: true })         kind = "Image";
        else if (run.Shape is { IsFloating: true })    kind = "Shape";
        else if (run.Chart is { IsFloating: true })    kind = "Chart";
        else if (run.WordArt is { IsFloating: true })  kind = "WordArt";
        else if (run.SmartArt is { IsFloating: true }) kind = "SmartArt";
        else if (run.DrawingGroup is not null)         kind = "Group";
        else return;

        SelectFloatingCore(blockIndex, runIndex, kind, addToMultiSelect);
    }

    private void SelectFloatingCore(int blockIndex, int runIndex, string kind, bool addToMultiSelect)
    {
        var item = (blockIndex, runIndex, kind);
        if (addToMultiSelect && IsGroupableFloatingKind(kind))
        {
            var existing = _selectedFloatingObjects.FindIndex(existingItem =>
                existingItem.BlockIndex == blockIndex && existingItem.RunIndex == runIndex);
            if (existing >= 0)
            {
                _selectedFloatingObjects.RemoveAt(existing);
                if (_selectedFloatingObjects.Count == 0)
                {
                    _selectedFloating = null;
                    _floatDragState = null;
                    RaiseFloatingSelectionChangedIfIdentityChanged();
                    InvalidateVisual();
                    return;
                }

                var fallback = _selectedFloatingObjects[^1];
                _selectedFloating = (fallback.BlockIndex, fallback.RunIndex, fallback.Kind, default);
                RefreshSelectedFloatingRect(fallback.BlockIndex, fallback.RunIndex, fallback.Kind);
                return;
            }

            _selectedFloatingObjects.RemoveAll(existingItem => existingItem.Kind == "Group");
            _selectedFloatingObjects.Add(item);
        }
        else
        {
            _selectedFloatingObjects.Clear();
            _selectedFloatingObjects.Add(item);
        }

        // Dummy rect; RefreshSelectedFloatingRect will update.
        _selectedFloating = (blockIndex, runIndex, kind, default);
        RefreshSelectedFloatingRect(blockIndex, runIndex, kind);
    }

    private static bool IsGroupableFloatingKind(string kind) =>
        kind is "Image" or "Shape" or "Chart" or "WordArt" or "SmartArt";

    private bool TryGetRun(int blockIndex, int runIndex, out Run run)
    {
        run = null!;
        if (blockIndex < 0 || blockIndex >= _doc.Blocks.Count) return false;
        if (_doc.Blocks[blockIndex] is not Paragraph para) return false;
        if (runIndex < 0 || runIndex >= para.Runs.Count) return false;
        run = para.Runs[runIndex];
        return true;
    }

    private bool IsGroupableFloatingRun(int blockIndex, int runIndex)
    {
        if (!TryGetRun(blockIndex, runIndex, out var run)) return false;
        return run.Image is { IsFloating: true }
            || run.Shape is { IsFloating: true }
            || run.Chart is { IsFloating: true }
            || run.WordArt is { IsFloating: true }
            || run.SmartArt is { IsFloating: true };
    }

    private List<(int Bi, int Ri)> SelectedGroupMemberLocations()
    {
        var members = new List<(int Bi, int Ri)>();
        foreach (var selected in _selectedFloatingObjects)
        {
            if (!IsGroupableFloatingRun(selected.BlockIndex, selected.RunIndex))
                continue;

            var member = (selected.BlockIndex, selected.RunIndex);
            if (!members.Contains(member))
                members.Add(member);
        }

        return members;
    }

    private (int BlockIndex, int RunIndex, Shape Shape, string Kind)? SelectedFloatingShapeLocation()
    {
        if (_selectedFloating is not { Kind: "Shape" } sel) return null;
        if (_doc.Blocks[sel.BlockIndex] is not Paragraph para) return null;
        if (sel.RunIndex < 0 || sel.RunIndex >= para.Runs.Count) return null;
        if (para.Runs[sel.RunIndex].Shape is not { } shape) return null;
        return (sel.BlockIndex, sel.RunIndex, shape, sel.Kind);
    }

    /// <summary>
    /// Set the wrap mode on the selected floating object. Undoable.
    /// No-op when nothing is selected.
    /// </summary>
    public void SetFloatingWrap(ImageWrapping wrapping)
    {
        if (_selectedFloating is not { } sel) return;
        _bus.Execute(new SetFloatingWrapCommand(sel.BlockIndex, sel.RunIndex, wrapping));
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// Change the z-order of the selected floating object. Undoable.
    /// No-op when nothing is selected.
    /// </summary>
    public void ChangeFloatingZOrder(ZOrderOperation op)
    {
        if (_selectedFloating is not { } sel) return;
        _bus.Execute(new ChangeZOrderCommand(sel.BlockIndex, sel.RunIndex, op));
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// Set the floating position (offset + anchor) of the selected object. Undoable.
    /// For Image: maps to <see cref="SetImagePositionCommand"/>.
    /// For all others: maps to <see cref="SetFloatingPositionCommand"/>.
    /// No-op when nothing is selected.
    /// </summary>
    public void SetFloatingPosition(double hOffsetPt, double vOffsetPt,
        HorizontalAnchor hAnchor, VerticalAnchor vAnchor)
    {
        if (_selectedFloating is not { } sel) return;
        if (_doc.Blocks[sel.BlockIndex] is not Paragraph para) return;
        if (sel.RunIndex < 0 || sel.RunIndex >= para.Runs.Count) return;
        var run = para.Runs[sel.RunIndex];

        if (run.Image is { IsFloating: true })
            _bus.Execute(new SetImagePositionCommand(sel.BlockIndex, sel.RunIndex,
                hOffsetPt, vOffsetPt, hAnchor, vAnchor));
        else
            _bus.Execute(new SetFloatingPositionCommand(sel.BlockIndex, sel.RunIndex,
                hOffsetPt, vOffsetPt, hAnchor, vAnchor));

        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// Set the size (widthPt, heightPt) of the selected floating object. Undoable.
    /// No-op when nothing is selected.
    /// </summary>
    public void SetFloatingSize(double widthPt, double heightPt)
    {
        if (_selectedFloating is not { } sel) return;
        _bus.Execute(new SetFloatingSizeCommand(sel.BlockIndex, sel.RunIndex, widthPt, heightPt));
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// AV-PICTAB: Returns the selected floating object's model size in points (width, height),
    /// or null when nothing is selected (or the kind carries no editable size).
    /// </summary>
    public (double WidthPt, double HeightPt)? GetSelectedFloatingSize()
    {
        if (_selectedFloating is not { } sel) return null;
        if (_doc.Blocks[sel.BlockIndex] is not Paragraph para) return null;
        if (sel.RunIndex < 0 || sel.RunIndex >= para.Runs.Count) return null;
        var run = para.Runs[sel.RunIndex];
        if (run.Image is { IsFloating: true } img) return (img.WidthPt, img.HeightPt);
        if (run.Shape is { } shape)    return (shape.WidthPt, shape.HeightPt);
        if (run.Chart is { } chart)    return (chart.WidthPt, chart.HeightPt);
        if (run.SmartArt is { } sa)    return (sa.WidthPt, sa.HeightPt);
        if (run.DrawingGroup is { } g) return (g.WidthPt, g.HeightPt);
        return null;
    }

    /// <summary>
    /// AV-PICTAB: Set just the width of the selected floating object, preserving its current height.
    /// No-op when nothing is selected. Undoable.
    /// </summary>
    public void SetFloatingWidth(double widthPt)
    {
        if (GetSelectedFloatingSize() is not { } size || widthPt <= 0) return;
        SetFloatingSize(widthPt, size.HeightPt);
    }

    /// <summary>
    /// AV-PICTAB: Set just the height of the selected floating object, preserving its current width.
    /// No-op when nothing is selected. Undoable.
    /// </summary>
    public void SetFloatingHeight(double heightPt)
    {
        if (GetSelectedFloatingSize() is not { } size || heightPt <= 0) return;
        SetFloatingSize(size.WidthPt, heightPt);
    }

    /// <summary>
    /// Set or clear alt text on the selected floating image, shape, or WordArt.
    /// Undoable through the shared model command bus. No-op for non-accessibility-bearing objects.
    /// </summary>
    public void SetSelectedFloatingAltText(string? altText)
    {
        if (_selectedFloating is not { } sel) return;
        if (!TryGetRun(sel.BlockIndex, sel.RunIndex, out var run)) return;

        var normalized = string.IsNullOrWhiteSpace(altText) ? null : altText.Trim();
        IDocumentCommand? command = run switch
        {
            { Image: { IsFloating: true } } => new SetImageAltTextCommand(sel.BlockIndex, sel.RunIndex, normalized),
            { Shape: { } } => new SetShapeAltTextCommand(sel.BlockIndex, sel.RunIndex, normalized),
            { WordArt: { } } => new SetWordArtAltTextCommand(sel.BlockIndex, sel.RunIndex, normalized),
            _ => null
        };

        if (command is null) return;
        _bus.Execute(command);
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// Apply a Word-like Shape Styles gallery preset to the selected floating shape/text box.
    /// </summary>
    public void ApplySelectedShapeStyle(ShapeStylePreset preset)
    {
        if (SelectedFloatingShapeLocation() is not { } sel) return;
        _bus.Execute(new ApplyShapeStyleCommand(sel.BlockIndex, sel.RunIndex, preset));
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// Set the solid fill color of the selected floating shape/text box. Pass null to remove fill.
    /// Undoable. No-op when the selected drawing object is not a shape.
    /// </summary>
    public void SetSelectedShapeFill(string? colorHex)
    {
        if (SelectedFloatingShapeLocation() is not { } sel) return;
        _bus.Execute(new SetShapeFillCommand(sel.BlockIndex, sel.RunIndex, colorHex));
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// Set an extended fill (gradient/pattern/no-fill) on the selected floating shape/text box.
    /// Undoable. No-op when the selected drawing object is not a shape.
    /// </summary>
    public void SetSelectedShapeExtendedFill(ShapeFill? fill)
    {
        if (SelectedFloatingShapeLocation() is not { } sel) return;
        _bus.Execute(new SetShapeExtendedFillCommand(sel.BlockIndex, sel.RunIndex, fill));
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// Set the outline of the selected floating shape/text box. Pass null colorHex to remove outline.
    /// Undoable. No-op when the selected drawing object is not a shape.
    /// </summary>
    public void SetSelectedShapeOutline(string? colorHex, double widthPt, string? dash = null)
    {
        if (SelectedFloatingShapeLocation() is not { } sel) return;
        _bus.Execute(new SetShapeOutlineCommand(sel.BlockIndex, sel.RunIndex, colorHex, widthPt, dash));
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// Rotate/flip the selected floating object (Images and Shapes only; other kinds are no-ops).
    /// Undoable. No-op when nothing is selected.
    /// </summary>
    public void RotateSelectedFloating(double angleDeg)
    {
        if (_selectedFloating is not { } sel) return;
        if (_doc.Blocks[sel.BlockIndex] is not Paragraph para) return;
        if (sel.RunIndex < 0 || sel.RunIndex >= para.Runs.Count) return;
        var run = para.Runs[sel.RunIndex];
        if (run.Image is { IsFloating: true } img)
            _bus.Execute(new SetImageRotationCommand(sel.BlockIndex, sel.RunIndex,
                angleDeg, img.FlipH, img.FlipV));
        else if (run.Shape is { } shape)
            _bus.Execute(new SetShapeRotationCommand(sel.BlockIndex, sel.RunIndex,
                angleDeg, shape.FlipH, shape.FlipV));
        // Chart/SmartArt/WordArt/Group don't carry rotation — ignore.
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// Flip the selected floating object horizontally or vertically (Images and Shapes only).
    /// Undoable. No-op when nothing is selected or the type doesn't support flip.
    /// </summary>
    public void FlipSelectedFloating(bool horizontal)
    {
        if (_selectedFloating is not { } sel) return;
        if (_doc.Blocks[sel.BlockIndex] is not Paragraph para) return;
        if (sel.RunIndex < 0 || sel.RunIndex >= para.Runs.Count) return;
        var run = para.Runs[sel.RunIndex];
        if (run.Image is { IsFloating: true } img)
        {
            var newFH = horizontal ? !img.FlipH : img.FlipH;
            var newFV = horizontal ? img.FlipV : !img.FlipV;
            _bus.Execute(new SetImageRotationCommand(sel.BlockIndex, sel.RunIndex, img.RotationAngle, newFH, newFV));
        }
        else if (run.Shape is { } shape)
        {
            var newFH = horizontal ? !shape.FlipH : shape.FlipH;
            var newFV = horizontal ? shape.FlipV : !shape.FlipV;
            _bus.Execute(new SetShapeRotationCommand(sel.BlockIndex, sel.RunIndex, shape.RotationAngle, newFH, newFV));
        }
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    // ── AV-OBJGROUP: floating-object Group/Ungroup edit API ────────────────────────────────────────

    /// <summary>
    /// Groups the current multi-selected floating objects through the shared model command.
    /// </summary>
    public void GroupSelectedFloatingObjects()
    {
        var members = SelectedGroupMemberLocations();
        if (members.Count < 2) return;

        _bus.Execute(new GroupFloatingObjectsCommand(members));
        _selectedFloating = null;
        _selectedFloatingObjects.Clear();
        _floatDragState = null;
        RaiseFloatingSelectionChangedIfIdentityChanged();
        InvalidateLayoutAndVisual();
    }

    // ── AV-CHARTTAB: Chart + SmartArt contextual-tab edit API ──────────────────────────────────────

    /// <summary>
    /// Ungroups the selected drawing group through the shared model command.
    /// </summary>
    public void UngroupSelectedFloatingObject()
    {
        if (_selectedFloating is not { Kind: "Group" } sel) return;
        if (!IsGroupSelected) return;

        _bus.Execute(new UngroupFloatingObjectsCommand(sel.BlockIndex, sel.RunIndex));
        _selectedFloating = null;
        _selectedFloatingObjects.Clear();
        _floatDragState = null;
        RaiseFloatingSelectionChangedIfIdentityChanged();
        InvalidateLayoutAndVisual();
    }

    /// <summary>
    /// Aligns/distributes the selected floating objects through the shared model command.
    /// </summary>
    public bool ArrangeSelectedFloatingObjects(FloatingObjectArrangeKind kind)
    {
        var members = SelectedFloatingArrangeLocations();
        if (ArrangeFloatingObjectsCommand.CountApplicableObjects(_doc, members) < RequiredArrangeObjectCount(kind))
            return false;

        _bus.Execute(new ArrangeFloatingObjectsCommand(kind, members));
        _floatDragState = null;

        if (_selectedFloating is { } selected)
            RefreshSelectedFloatingRect(selected.BlockIndex, selected.RunIndex, selected.Kind);
        else
            RaiseFloatingSelectionChangedIfIdentityChanged();

        InvalidateLayoutAndVisual();
        return true;
    }

    public bool CanArrangeSelectedFloatingObjects(FloatingObjectArrangeKind kind) =>
        ArrangeFloatingObjectsCommand.CountApplicableObjects(_doc, SelectedFloatingArrangeLocations())
            >= RequiredArrangeObjectCount(kind);

    private List<(int BlockIndex, int RunIndex)> SelectedFloatingArrangeLocations()
    {
        var members = new List<(int BlockIndex, int RunIndex)>();
        foreach (var selected in _selectedFloatingObjects)
        {
            var member = (selected.BlockIndex, selected.RunIndex);
            if (!members.Contains(member))
                members.Add(member);
        }

        return members;
    }

    private static int RequiredArrangeObjectCount(FloatingObjectArrangeKind kind) =>
        kind is FloatingObjectArrangeKind.DistributeHorizontal or FloatingObjectArrangeKind.DistributeVertical
            ? 2
            : 1;

    /// <summary>
    /// AV-CHARTTAB: Change the chart kind (column/bar/line/pie/scatter/area/doughnut) of the selected
    /// floating chart. Undoable + re-renders. No-op when the selected float is not a chart.
    /// </summary>
    public void SetChartType(ChartKind kind)
    {
        if (_selectedFloating is not { Kind: "Chart" } sel) return;
        _bus.Execute(new SetChartKindCommand(sel.BlockIndex, sel.RunIndex, kind));
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// AV-CHARTTAB: Apply a chart style (1-based catalog id) to the selected floating chart.
    /// Undoable + re-renders. No-op when the selected float is not a chart.
    /// </summary>
    public void SetChartStyle(int styleId)
    {
        if (_selectedFloating is not { Kind: "Chart" } sel) return;
        _bus.Execute(new SetChartStyleCommand(sel.BlockIndex, sel.RunIndex, styleId));
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// AV-CHARTTAB: Apply a chart colour scheme (catalog id, e.g. "colorful1") to the selected floating
    /// chart. Undoable + re-renders. No-op when the selected float is not a chart.
    /// </summary>
    public void SetChartColorScheme(string? colorSchemeId)
    {
        if (_selectedFloating is not { Kind: "Chart" } sel) return;
        _bus.Execute(new SetChartColorSchemeCommand(sel.BlockIndex, sel.RunIndex, colorSchemeId));
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// AV-CHARTTAB: Toggle the legend on the selected floating chart.
    /// Undoable + re-renders. No-op when the selected float is not a chart.
    /// </summary>
    public void ToggleChartLegend()
    {
        if (_selectedFloating is not { Kind: "Chart" } sel) return;
        if (_doc.Blocks[sel.BlockIndex] is not Paragraph para) return;
        if (sel.RunIndex < 0 || sel.RunIndex >= para.Runs.Count) return;
        if (para.Runs[sel.RunIndex].Chart is not { } chart) return;

        var state = ChartSmartArtVisualPlanner.BuildChartElementCommandState(chart);
        if (!state.CanToggleLegend) return;

        _bus.Execute(new SetChartLegendCommand(sel.BlockIndex, sel.RunIndex, !state.IsLegendVisible));
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// AV-CHARTTAB: Toggle the selected floating chart title between Word's default title text and hidden.
    /// Undoable + re-renders. No-op when the selected float is not a chart.
    /// </summary>
    public void ToggleChartTitle()
    {
        if (_selectedFloating is not { Kind: "Chart" } sel) return;
        if (_doc.Blocks[sel.BlockIndex] is not Paragraph para) return;
        if (sel.RunIndex < 0 || sel.RunIndex >= para.Runs.Count) return;
        if (para.Runs[sel.RunIndex].Chart is not { } chart) return;

        var state = ChartSmartArtVisualPlanner.BuildChartElementCommandState(chart);
        var title = state.HasChartTitle ? null : "Chart Title";
        _bus.Execute(new SetChartTitleCommand(sel.BlockIndex, sel.RunIndex, title));
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// AV-CHARTTAB: Toggle default axis titles on the selected floating chart.
    /// Undoable + re-renders. No-op when the selected float is not an axis-capable chart.
    /// </summary>
    public void ToggleChartAxisTitles()
    {
        if (_selectedFloating is not { Kind: "Chart" } sel) return;
        if (_doc.Blocks[sel.BlockIndex] is not Paragraph para) return;
        if (sel.RunIndex < 0 || sel.RunIndex >= para.Runs.Count) return;
        if (para.Runs[sel.RunIndex].Chart is not { } chart) return;

        var state = ChartSmartArtVisualPlanner.BuildChartElementCommandState(chart);
        if (!state.CanEditAxisTitles) return;

        var hasStoredAxisTitles = !string.IsNullOrWhiteSpace(chart.CategoryAxisTitle)
                               || !string.IsNullOrWhiteSpace(chart.ValueAxisTitle);
        var categoryTitle = hasStoredAxisTitles ? null : "Category Axis";
        var valueTitle = hasStoredAxisTitles ? null : "Value Axis";
        _bus.Execute(new SetChartAxisTitlesCommand(sel.BlockIndex, sel.RunIndex, categoryTitle, valueTitle));
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// AV-CHARTTAB: Replace the selected floating chart's editable data through the shared command bus.
    /// Undoable + re-renders. No-op when the selected float is not a chart.
    /// </summary>
    public void ReplaceSelectedChartData(Chart replacement)
    {
        if (_selectedFloating is not { Kind: "Chart" } sel) return;
        _bus.Execute(new ReplaceChartDataCommand(sel.BlockIndex, sel.RunIndex, replacement));
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// AV-CHARTTAB: Resize the selected floating chart through the shared floating-object size command.
    /// Undoable + re-renders. No-op when the selected float is not a chart.
    /// </summary>
    public void SetSelectedChartSize(double widthPt, double heightPt)
    {
        if (_selectedFloating is not { Kind: "Chart" } sel || widthPt <= 0 || heightPt <= 0) return;
        SetFloatingSize(widthPt, heightPt);
    }

    /// <summary>
    /// AV-CHARTTAB: Change the SmartArt layout family (List/Process/Hierarchy - Cycle maps to Process)
    /// of the selected floating SmartArt. Undoable + re-renders. No-op when the float is not SmartArt.
    /// </summary>
    public void SetSmartArtLayout(SmartArtKind kind)
    {
        if (_selectedFloating is not { Kind: "SmartArt" } sel) return;
        _bus.Execute(new SetSmartArtLayoutCommand(sel.BlockIndex, sel.RunIndex, kind));
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// AV-CHARTTAB: Apply a SmartArt colour scheme (catalog id) to the selected floating SmartArt.
    /// Undoable + re-renders. No-op when the selected float is not SmartArt.
    /// </summary>
    public void SetSmartArtColor(string? colorSchemeId)
    {
        if (_selectedFloating is not { Kind: "SmartArt" } sel) return;
        _bus.Execute(new SetSmartArtColorCommand(sel.BlockIndex, sel.RunIndex, colorSchemeId));
        InvalidateLayoutAndVisual();
        RefreshSelectedFloatingRect(sel.BlockIndex, sel.RunIndex, sel.Kind);
    }

    /// <summary>
    /// AV-CHARTTAB: Read the selected chart's current kind/style/colour-scheme, or null when the
    /// selected float is not a chart. Used by tests and the contextual-tab live-state.
    /// </summary>
    public (ChartKind Kind, int StyleId, string? ColorSchemeId)? GetSelectedChartInfo()
    {
        if (_selectedFloating is not { Kind: "Chart" } sel) return null;
        if (_doc.Blocks[sel.BlockIndex] is not Paragraph para) return null;
        if (sel.RunIndex < 0 || sel.RunIndex >= para.Runs.Count) return null;
        if (para.Runs[sel.RunIndex].Chart is not { } chart) return null;
        return (chart.Kind, chart.StyleId, chart.ColorSchemeId);
    }

    /// <summary>
    /// AV-CHARTTAB: Read the selected SmartArt's current kind/colour-scheme, or null when the selected
    /// float is not SmartArt. Used by tests and the contextual-tab live-state.
    /// </summary>
    public (SmartArtKind Kind, string? ColorSchemeId)? GetSelectedSmartArtInfo()
    {
        if (_selectedFloating is not { Kind: "SmartArt" } sel) return null;
        if (_doc.Blocks[sel.BlockIndex] is not Paragraph para) return null;
        if (sel.RunIndex < 0 || sel.RunIndex >= para.Runs.Count) return null;
        if (para.Runs[sel.RunIndex].SmartArt is not { } sa) return null;
        return (sa.Kind, sa.ColorSchemeId);
    }

    /// <summary>
    /// Delete the currently selected floating object. Removes the run from its paragraph.
    /// Undoable via the command bus. No-op when nothing is selected.
    /// </summary>
    public void DeleteSelectedFloating()
    {
        if (_selectedFloating is not { } sel) return;
        if (_doc.Blocks[sel.BlockIndex] is not Paragraph para) return;
        if (sel.RunIndex < 0 || sel.RunIndex >= para.Runs.Count) return;

        // Remove the run in-place via a command (undoable).
        _bus.Execute(new RemoveFloatingRunCommand(sel.BlockIndex, sel.RunIndex));
        _selectedFloating = null;
        _selectedFloatingObjects.Clear();
        _floatDragState   = null;
        RaiseFloatingSelectionChangedIfIdentityChanged();
        InvalidateLayoutAndVisual();
    }

    private void DrawDecoration(DrawingContext context, PlacedChar pc, double yLine) =>
        DrawDecoration(context, pc, yLine, pc.Fmt);

    // AV-LINK: overload that draws the decoration (under-/strike-line) in an explicit format's colour, so a
    // hyperlink underline uses the resolved hyperlink colour rather than the run's raw (unstyled) colour.
    private void DrawDecoration(DrawingContext context, PlacedChar pc, double yLine, RunFormatting fmt)
    {
        var pen = new Pen(BrushFor(fmt.ColorHex), Math.Max(1, FontSizePx(fmt) / 14));
        context.DrawLine(pen, new Point(pc.X, yLine), new Point(pc.X + pc.W, yLine));
    }

    /// <summary>
    /// AV-TRACKEDIT: draw the tracked-change mark for a glyph — a revision-coloured underline under a tracked
    /// insertion (Word's w:ins decoration) or a revision-coloured strikethrough across a tracked deletion
    /// (w:del). A no-op for ordinary (un-tracked) glyphs.
    /// </summary>
    private static void DrawRevisionDecoration(
        DrawingContext context,
        PlacedChar pc,
        ReviewRevisionDisplayDecision decision)
    {
        if (pc.W <= 0 || !decision.IsRevisionStylingApplied)
            return;
        if (decision.IsInsertionDecorationApplied)
        {
            var y = pc.Y + pc.LineHeight * 0.86;
            context.DrawLine(RevisionInsertUnderlinePen, new Point(pc.X, y), new Point(pc.X + pc.W, y));
        }
        else if (decision.IsDeletionDecorationApplied)
        {
            var y = pc.Y + pc.LineHeight * 0.5;
            context.DrawLine(RevisionDeleteStrikePen, new Point(pc.X, y), new Point(pc.X + pc.W, y));
        }
    }

    private static void DrawFormatRevisionDecoration(DrawingContext context, PlacedChar pc)
    {
        if (pc.W <= 0)
            return;

        var y = pc.Y + pc.LineHeight * 0.92;
        context.DrawLine(FormatRevisionUnderlinePen, new Point(pc.X, y), new Point(pc.X + pc.W, y));
    }

    private static void DrawProofingSquiggle(DrawingContext context, PlacedChar pc)
    {
        if (pc.W <= 0)
            return;

        var y = pc.Y + pc.LineHeight * 0.92;
        var x = pc.X;
        var end = pc.X + pc.W;
        var step = 3.0;
        var amplitude = 1.4;
        var high = true;
        var current = new Point(x, y);
        while (x < end)
        {
            x = Math.Min(end, x + step);
            var next = new Point(x, y + (high ? -amplitude : amplitude));
            context.DrawLine(ProofingSquigglePen, current, next);
            current = next;
            high = !high;
        }
    }

    /// <summary>
    /// AV-TAB: Draws a tab leader (dots / dashes / underline) filling the gap between
    /// <paramref name="x1"/> (tab character start) and <paramref name="x2"/> (next segment start)
    /// for the given <paramref name="leader"/> kind and run formatting.
    /// </summary>
    private void DrawTabLeader(DrawingContext context, double x1, double x2, double y, double lineH,
        TabLeader leader, RunFormatting fmt)
    {
        if (x2 <= x1) return;
        var brush = BrushFor(fmt.ColorHex);
        var thickness = Math.Max(0.8, FontSizePx(fmt) / 18);

        switch (leader)
        {
            case TabLeader.Underline:
            {
                // Solid underline across the full gap.
                var pen = new Pen(brush, thickness);
                var yLine = y + lineH * 0.82;
                context.DrawLine(pen, new Point(x1, yLine), new Point(x2, yLine));
                break;
            }
            case TabLeader.Dots:
            {
                // Dots spaced ~4px apart, drawn as small filled circles at the baseline.
                var yLine = y + lineH * 0.82;
                var dotR  = Math.Max(0.7, thickness * 0.6);
                var step  = Math.Max(4, dotR * 5);
                for (var dotX = x1 + step / 2; dotX < x2 - dotR; dotX += step)
                    context.FillRectangle(brush, new Rect(dotX - dotR, yLine - dotR, dotR * 2, dotR * 2));
                break;
            }
            case TabLeader.Dashes:
            {
                // Dashes via a dash-style pen.
                var dashPen = new Pen(brush, thickness, new DashStyle([4, 3], 0));
                var yLine   = y + lineH * 0.82;
                context.DrawLine(dashPen, new Point(x1, yLine), new Point(x2, yLine));
                break;
            }
        }
    }

    private bool TryGetCaretRect(out Rect rect)
    {
        // AV-TBL: when the caret is inside a cell, search by cell address + para-offset instead of
        // block+glyph-offset. This is robust after model edits (which invalidate glyph offsets).
        if (_cellCaret is { } cc)
        {
            foreach (var pc in _placed)
            {
                if (pc.Block == cc.TableBlock && pc.CellRow == cc.Row && pc.CellCol == cc.Col
                    && pc.CellParaIdx == cc.ParaIdx && pc.CellParaOffset == cc.Offset)
                {
                    rect = new Rect(pc.X, pc.Y, 1.5, pc.LineHeight);
                    return true;
                }
            }
            // If no exact glyph found (e.g., caret is at end of text = sentinel position),
            // look for the sentinel at that cell + para.
            foreach (var pc in _placed)
            {
                if (pc.Block == cc.TableBlock && pc.CellRow == cc.Row && pc.CellCol == cc.Col
                    && pc.CellParaIdx == cc.ParaIdx && pc.Sentinel)
                {
                    rect = new Rect(pc.X, pc.Y, 1.5, pc.LineHeight);
                    return true;
                }
            }
            rect = default;
            return false;
        }

        // Body paragraph: search by block + glyph offset (original logic).
        foreach (var pc in _placed)
        {
            if (pc.Block == _caret.Block && pc.Offset == _caret.Offset)
            {
                rect = new Rect(pc.X, pc.Y, 1.5, pc.LineHeight);
                return true;
            }
        }

        rect = default;
        return false;
    }

    // ---- Input ----------------------------------------------------------------------------------

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Focus();
        var point = e.GetPosition(this);
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        var ctrlOrMeta = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

        // AV-LINK: Ctrl+Click follows a hyperlink (Word's convention) — open an external URL via the
        // HyperlinkActivated event, or jump the caret to an internal bookmark target. Checked before the
        // body hit-test so the click is consumed instead of just moving the caret.
        if (ctrlOrMeta && !shift && TryHitTestHyperlink(point, out var clickedLink))
        {
            FollowHyperlink(clickedLink);
            e.Handled = true;
            return;
        }

        // AV-HANDLES: when a float is already selected, a press on one of its 8 resize handles starts a
        // resize drag (checked BEFORE the float hit-test so the handle squares, which sit on/outside the
        // object's edge, win over whatever object lies under them).
        var extendFloatingSelection = shift || ctrlOrMeta;

        if (!extendFloatingSelection && _selectedFloating is { } curSel)
        {
            var handle = HitTestHandle(point);
            if (handle is not FloatHandle.None and not FloatHandle.Body)
            {
                _floatDragState = (point, curSel.Rect, handle);
                Cursor = CursorForHandle(handle);
                InvalidateVisual();
                e.Handled = true;
                return;
            }
        }

        // AV-FLSEL: check whether the click landed on a floating object BEFORE body text hit-test.
        // The topmost object (highest z-order, in-front preferred over behind) wins.
        if (TryHitTestFloat(point, out var floatHit))
        {
            SelectFloatingCore(floatHit.BlockIndex, floatHit.RunIndex, floatHit.Kind, extendFloatingSelection);
            if (!extendFloatingSelection && _selectedFloating is { } selected)
            {
                // AV-HANDLES: a press inside the selected float's body starts a drag-move. Whether it
                // becomes a real move or stays a plain selecting click is decided on release by the 1px
                // threshold in CommitFloatDrag.
                _floatDragState = (point, selected.Rect, FloatHandle.Body);
                Cursor = CursorForHandle(FloatHandle.Body);
            }
            else
            {
                _floatDragState = null;
                Cursor = Cursor.Default;
            }
            InvalidateVisual();
            e.Handled = true;
            return;
        }

        // Click outside any float → deselect.
        if (_selectedFloating is not null)
        {
            _selectedFloating = null;
            _selectedFloatingObjects.Clear();
            _floatDragState   = null;
            Cursor = Cursor.Default;
            RaiseFloatingSelectionChangedIfIdentityChanged();
            InvalidateVisual();
        }

        // AV-HFEDIT: a click inside a rendered header/footer region routes the caret into that region.
        // Only in PrintLayout (the only mode that draws H/F bands). Checked before the body hit-test
        // because H/F bands sit in the page margins, which the body hit-test does not own.
        if (!shift && _viewMode == DocumentViewMode.PrintLayout && TryHitTestHeaderFooter(point))
        {
            CaretMoved?.Invoke();
            e.Handled = true;
            return;
        }

        if (TryHitTest(point, out var pos))
        {
            // AV-HFEDIT: clicking back into the body exits any active header/footer caret.
            _hfCaret = null;

            // AV-TBL2: clear any prior cross-cell block selection on a fresh (non-shift) press.
            if (!shift)
            {
                _cellBlockAnchor = null;
                _cellBlockFocus  = null;
            }

            // AV-TBL: When entering a cell, _cellCaret was set by TryHitTest.
            // When leaving a cell (hitting body text), _cellCaret is cleared.
            if (!shift)
            {
                _selectionAnchor = pos;
                _cellAnchor = _cellCaret;
            }
            else
            {
                // Shift-click extends selection; keep existing anchor.
                _selectionAnchor ??= _caret;
                _cellAnchor ??= _cellCaret;
            }
            _caret = pos;
            InvalidateVisual();
            CaretMoved?.Invoke();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // AV-HANDLES: with the button up, update the hover cursor over the selection so handles
            // advertise their resize direction (and the body shows a move cursor).
            if (_selectedFloating is not null && _floatDragState is null)
            {
                var hover = HitTestHandle(point);
                Cursor = hover == FloatHandle.None ? Cursor.Default : CursorForHandle(hover);
            }
            return;
        }

        // AV-HANDLES: live drag (move or resize) of the selected floating object.
        if (_floatDragState is { })
        {
            var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            UpdateFloatDrag(point, shift);
            e.Handled = true;
            return;
        }
        if (!TryHitTest(point, out var pos))
            return;

        // AV-TBL2: when a drag starts inside a cell and moves to a DIFFERENT cell, switch to
        // rectangular cross-cell block selection instead of single-cell text selection.
        if (_cellBlockAnchor is { } blockAnchor && _cellCaret is { } movingFocus
            && blockAnchor.TableBlock == movingFocus.TableBlock)
        {
            // BF4: block selection is already active — just update focus to the current cell.
            // _cellCaret/_cellAnchor are already null (cleared when block selection was first activated).
            _cellBlockFocus = (movingFocus.TableBlock, movingFocus.Row, movingFocus.Col);
            _cellCaret  = null;
            _cellAnchor = null;
            _selectionAnchor = _caret;
        }
        else if (_cellAnchor is { } anchor && _cellCaret is { } focus
            && anchor.TableBlock == focus.TableBlock
            && (anchor.Row != focus.Row || anchor.Col != focus.Col))
        {
            // Different cell than where the drag started → activate block selection.
            _cellBlockAnchor = (anchor.TableBlock, anchor.Row, anchor.Col);
            _cellBlockFocus  = (focus.TableBlock,  focus.Row,  focus.Col);
            // BF4: clear single-cell caret/anchor so SelectedCellRange and CellCaretInfo are
            // never both non-null (mirrors SetCellBlockSelection invariant).
            _cellCaret  = null;
            _cellAnchor = null;
            // Suppress the single-cell text selection anchor so IsWithin() doesn't highlight glyphs.
            _selectionAnchor = _caret;
        }
        else if (_cellAnchor is { } anch && _cellCaret is { } foc
                 && anch.TableBlock == foc.TableBlock
                 && anch.Row == foc.Row && anch.Col == foc.Col)
        {
            // Still in the same cell — clear block selection if it was set.
            _cellBlockAnchor = null;
            _cellBlockFocus  = null;
        }

        _caret = pos;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        // AV-HANDLES: commit the in-flight drag (move or resize) when the left button is released.
        // _selectedFloating is refreshed via the commit path's relayout; the cursor is reset to the
        // hover cursor for wherever the pointer ended up.
        if (_floatDragState is not null)
        {
            var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            CommitFloatDrag(e.GetPosition(this), shift);
            Cursor = _selectedFloating is null
                ? Cursor.Default
                : CursorForHandle(HitTestHandle(e.GetPosition(this)));
        }

        if (ApplyFormatPainterToSelection())
            e.Handled = true;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (IsEditingLocked)
        {
            e.Handled = true;
            return;
        }

        if (string.IsNullOrEmpty(e.Text) || e.Text == "\r" || e.Text == "\n")
            return;
        InsertText(e.Text);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        var ctrl = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;

        if (IsEditingLocked && IsEditingKey(e.Key, ctrl))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && IsFormatPainterArmed)
        {
            CancelFormatPainter();
            e.Handled = true;
            return;
        }

        // AV-FLSEL: when a float is selected, intercept navigation/delete keys before body text.
        if (_selectedFloating is { } selFloat)
        {
            const double NudgePt = 6; // 6pt ≈ 8px nudge per arrow key press
            switch (e.Key)
            {
                case Key.Escape:
                    // AV-HANDLES: Esc mid-drag cancels the drag (reverts the transient rect) and KEEPS
                    // the selection; a second Esc (no drag in flight) then deselects.
                    if (CancelFloatDrag())
                    {
                        Cursor = Cursor.Default;
                        e.Handled = true;
                        return;
                    }
                    _selectedFloating = null;
                    _selectedFloatingObjects.Clear();
                    _floatDragState   = null;
                    Cursor = Cursor.Default;
                    RaiseFloatingSelectionChangedIfIdentityChanged();
                    InvalidateVisual();
                    e.Handled = true;
                    return;
                case Key.Delete:
                case Key.Back:
                    DeleteSelectedFloating();
                    e.Handled = true;
                    return;
                case Key.Left:
                    NudgeSelectedFloating(-NudgePt, 0);
                    e.Handled = true;
                    return;
                case Key.Right:
                    NudgeSelectedFloating(+NudgePt, 0);
                    e.Handled = true;
                    return;
                case Key.Up:
                    NudgeSelectedFloating(0, -NudgePt);
                    e.Handled = true;
                    return;
                case Key.Down:
                    NudgeSelectedFloating(0, +NudgePt);
                    e.Handled = true;
                    return;
                case Key.Z when ctrl:
                    Undo(); e.Handled = true; return;
                case Key.Y when ctrl:
                    Redo(); e.Handled = true; return;
            }
            // Any other key: pass through (don't consume).
        }

        // AV-HFEDIT: when the caret is in a header/footer region, intercept navigation/exit keys.
        // Editing keys (Back/Delete/Enter) route through the shared methods which already check _hfCaret.
        if (_hfCaret is not null)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    ExitHeaderFooterCaret();
                    e.Handled = true;
                    return;
                case Key.Left:
                    HfMoveCaret(-1); e.Handled = true; return;
                case Key.Right:
                    HfMoveCaret(+1); e.Handled = true; return;
                case Key.Home:
                    _hfCaret = (_hfCaret.Value.Target, 0); InvalidateVisual(); CaretMoved?.Invoke(); e.Handled = true; return;
                case Key.End:
                    _hfCaret = (_hfCaret.Value.Target, HfParaLength(_hfCaret.Value.Target)); InvalidateVisual(); CaretMoved?.Invoke(); e.Handled = true; return;
                case Key.Back: Backspace(); e.Handled = true; return;
                case Key.Delete: DeleteForward(); e.Handled = true; return;
                case Key.Enter: InsertParagraphBreak(); e.Handled = true; return;
                case Key.Z when ctrl: Undo(); e.Handled = true; return;
                case Key.Y when ctrl: Redo(); e.Handled = true; return;
                // DD1 (AV-HFEDIT): Tab/Shift+Tab while H/F caret is active — insert a literal tab into the
                // header/footer and mark handled. This prevents fall-through to the body Tab path which would
                // fire ListTabAtItemStart and mutate body list items (or navigate table cells) instead.
                // Shift+Tab is a no-op (no reverse-tab concept in H/F single-line context) but still consumed
                // so the body list is never touched while the H/F caret is active.
                case Key.Tab:
                    if (!shift) HfInsertText("\t");
                    e.Handled = true; return;
                // DD2 (AV-HFEDIT): Up/Down are consumed as no-ops inside a header/footer.
                // Previously the comment said "no-op for H/F" but both keys fell through to MoveCaretVertical
                // which moved the BODY caret while _hfCaret stayed non-null, leaving the body caret displaced
                // after ExitHeaderFooterCaret(). Intercept here so the body caret never moves during H/F editing.
                case Key.Up:
                case Key.Down:
                    e.Handled = true; return;
            }
            // All other keys fall through to default handling below.
        }

        switch (e.Key)
        {
            case Key.Z when ctrl: Undo(); e.Handled = true; break;
            case Key.Y when ctrl: Redo(); e.Handled = true; break;
            case Key.B when ctrl: ToggleBold(); e.Handled = true; break;
            case Key.I when ctrl: ToggleItalic(); e.Handled = true; break;
            case Key.U when ctrl: ToggleUnderline(); e.Handled = true; break;
            case Key.Back: Backspace(); e.Handled = true; break;
            case Key.Delete: DeleteForward(); e.Handled = true; break;
            case Key.Enter: InsertParagraphBreak(); e.Handled = true; break;
            case Key.Left: MoveCaret(-1, shift); e.Handled = true; break;
            case Key.Right: MoveCaret(+1, shift); e.Handled = true; break;
            case Key.Home: MoveToLineEdge(toStart: true, shift); e.Handled = true; break;
            case Key.End: MoveToLineEdge(toStart: false, shift); e.Handled = true; break;
            case Key.Up: MoveCaretVertical(-1, shift); e.Handled = true; break;
            case Key.Down: MoveCaretVertical(+1, shift); e.Handled = true; break;
            // AV-TBL3: Tab navigates between cells when the caret is in a table; outside a table
            // it handles list demote/promote at item start, or inserts a literal tab character
            // (body-paragraph behaviour, same as before).
            case Key.Tab:
                if (_cellCaret is not null)
                {
                    TabNavigateCell(forward: !shift);
                    e.Handled = true;
                }
                else if (ListTabAtItemStart(shift))
                {
                    // AV-LIST: Tab/Shift+Tab at the start of a list item demotes/promotes.
                    e.Handled = true;
                }
                else if (!shift)
                {
                    InsertText("\t");
                    e.Handled = true;
                }
                break;
        }
    }

    private static bool IsEditingKey(Key key, bool ctrl) =>
        key is Key.Back or Key.Delete or Key.Enter or Key.Tab ||
        (ctrl && key is (Key.B or Key.I or Key.U or Key.Z or Key.Y));

    // ---- Editing operations (all via the command bus) -------------------------------------------

    public void InsertText(string text)
    {
        if (IsEditingLocked)
            return;

        // AV-HFEDIT: route into a header/footer region when the caret is inside one.
        if (_hfCaret is not null)
        {
            HfInsertText(text);
            return;
        }

        // AV-TBL: route into table cell when the caret is inside a cell.
        if (_cellCaret is { } cc)
        {
            // BE3: replace active in-cell selection before inserting (mirrors body-text DeleteSelection path).
            DeleteCellSelection(cc);
            cc = _cellCaret!.Value; // re-read after potential selection delete
            var para = GetCellParagraph(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx);
            if (para == null || !IsEditable(para))
                return;
            var offset = cc.Offset;
            var fmt = ActiveFormatting(para, offset);
            // AV-TRACKEDIT: record cell typing as a tracked insertion too when Track Changes is on.
            var cellInsRevision = TrackChangesEnabled ? RevisionKind.Inserted : RevisionKind.None;
            var cellInsAuthor = TrackChangesEnabled ? RevisionAuthor : null;
            var cellInsDate = TrackChangesEnabled ? CurrentRevisionDateXml() : null;
            _bus.Execute(new ReplaceCellParagraphRunsCommand(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx, p =>
            {
                var chars = ParaCells(p);
                // BE4: insert at incrementing position so multi-char paste/IME inserts in order.
                var at = Math.Clamp(offset, 0, chars.Count);
                foreach (var ch in text)
                    chars.Insert(at++, new Cell(ch, fmt, null, cellInsRevision, cellInsAuthor, cellInsDate));
                SetRuns(p, chars);
            }));
            _cellCaret = cc with { Offset = offset + text.Length };
            _cellAnchor = _cellCaret;
            // Update _caret.Offset to match so TryGetCaretRect can find the sentinel.
            _caret = new DocPosition(cc.TableBlock, FindCellGlyphOffset(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx, cc.Offset + text.Length));
            _selectionAnchor = _caret;
            return;
        }

        if (NormalizedSelection() is not null)
            DeleteSelection();
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return;

        var block = _caret.Block;
        var bodyOffset = _caret.Offset;
        // BZ5: use pending format if set (from collapsed-caret Font dialog apply), otherwise
        // inherit from the character at the caret position.
        var pendingFmt = _pendingRunFmt;
        _pendingRunFmt = null; // consume immediately so only the next typed char gets it
        var bodyFmt = pendingFmt ?? ActiveFormatting(paragraph, bodyOffset);
        // AV-TRACKEDIT: when Track Changes is on, typed characters are recorded as a tracked insertion
        // (author + date) so they render underlined/coloured and round-trip as w:ins. OFF behaves as before.
        var insRevision = TrackChangesEnabled ? RevisionKind.Inserted : RevisionKind.None;
        var insAuthor = TrackChangesEnabled ? RevisionAuthor : null;
        var insDate = TrackChangesEnabled ? CurrentRevisionDateXml() : null;
        // AV-LINK: typing strictly inside a hyperlink span extends that link (Word's behaviour); typing at a
        // link's edge or outside a link inserts plain (un-linked) text.
        var insLink = ActiveLink(paragraph, bodyOffset);
        _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
        {
            var cells = ParaCells(p);
            // BE4 (body parity): insert at an incrementing position so multi-char text (paste / IME /
            // model inserts like a citation string) keeps its order — a fixed insert index would reverse it.
            // Cells carry the tracked-insertion revision tags when Track Changes is on (null otherwise).
            var at = Math.Clamp(bodyOffset, 0, cells.Count);
            foreach (var ch in text)
                cells.Insert(at++, new Cell(ch, bodyFmt, null, insRevision, insAuthor, insDate, insLink));
            SetRuns(p, cells);
        }));
        _caret = new DocPosition(block, bodyOffset + text.Length);
        _selectionAnchor = _caret;
    }

    /// <summary>
    /// Inserts a plain-text content control at the body caret. A selected body range becomes the control
    /// content; an empty caret gets Word's default prompt text.
    /// </summary>
    public void InsertPlainTextControl(string? tag = null, string? alias = null)
    {
        InsertBodyContentControlRun(Run.PlainTextControl(
            ContentControlInteractionPlanner.PromptText(SelectedText),
            tag,
            alias));
    }

    /// <summary>Inserts an unchecked checkbox content control at the body caret.</summary>
    public void InsertCheckBoxControl(string? tag = null, string? alias = null) =>
        InsertBodyContentControlRun(Run.CheckBoxControl(@checked: false, tag, alias));

    /// <summary>
    /// Inserts a rich-text content control at the body caret. A selected body range becomes the control
    /// content; an empty caret gets Word's default prompt text.
    /// </summary>
    public void InsertRichTextControl(string? tag = null, string? alias = null)
    {
        InsertBodyContentControlRun(Run.RichTextControl(
            ContentControlInteractionPlanner.PromptText(SelectedText),
            tag,
            alias));
    }

    /// <summary>Inserts a date-picker content control at the body caret.</summary>
    public void InsertDatePickerControl(string? tag = null, string? alias = null, string? dateFormat = null)
    {
        var format = ContentControlInteractionPlanner.DateFormatOrDefault(dateFormat);
        var today = ContentControlInteractionPlanner.FormatDate(format, DateTime.Today);
        InsertBodyContentControlRun(Run.DatePickerControl(today, tag, alias, format));
    }

    /// <summary>Inserts a drop-down-list content control at the body caret.</summary>
    public void InsertDropDownListControl(
        IReadOnlyList<ContentControlListItem>? items = null,
        string? tag = null,
        string? alias = null) =>
        InsertBodyContentControlRun(Run.DropDownListControl(
            ContentControlInteractionPlanner.ListItemsOrDefault(items),
            tag: tag,
            alias: alias));

    /// <summary>Inserts a combo-box content control at the body caret.</summary>
    public void InsertComboBoxControl(
        IReadOnlyList<ContentControlListItem>? items = null,
        string? tag = null,
        string? alias = null) =>
        InsertBodyContentControlRun(Run.ComboBoxControl(
            ContentControlInteractionPlanner.ListItemsOrDefault(items),
            tag: tag,
            alias: alias));

    public bool ToggleContentControl(int blockIndex, int runIndex) =>
        ApplyContentControlInteraction(blockIndex, runIndex, ContentControlInteractionPlanner.ToggleCheckBox);

    public bool SelectContentControlItem(int blockIndex, int runIndex, int itemIndex) =>
        ApplyContentControlInteraction(blockIndex, runIndex, run =>
            ContentControlInteractionPlanner.SelectItem(run, itemIndex));

    public bool SelectContentControlRelativeDate(int blockIndex, int runIndex, int choiceIndex) =>
        ApplyContentControlInteraction(blockIndex, runIndex, run =>
            ContentControlInteractionPlanner.SelectRelativeDate(run, choiceIndex));

    private bool ApplyContentControlInteraction(int blockIndex, int runIndex, Func<Run, Run?> planner)
    {
        if (IsEditingLocked
            || blockIndex < 0
            || blockIndex >= _doc.Blocks.Count
            || _doc.Blocks[blockIndex] is not Paragraph paragraph
            || runIndex < 0
            || runIndex >= paragraph.Runs.Count)
        {
            return false;
        }

        var updated = planner(paragraph.Runs[runIndex]);
        if (updated is null)
            return false;

        _bus.Execute(new ReplaceParagraphRunsCommand(blockIndex, p =>
        {
            if (runIndex >= 0 && runIndex < p.Runs.Count)
                p.Runs[runIndex] = updated;
        }));
        return true;
    }

    private void InsertBodyContentControlRun(Run run)
    {
        if (IsEditingLocked || _hfCaret is not null || _cellCaret is not null)
            return;

        if (NormalizedSelection() is not null)
            DeleteSelection();
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return;

        var block = _caret.Block;
        var offset = _caret.Offset;
        _bus.Execute(new ReplaceParagraphRunsCommand(block, p => InsertRunAtOffset(p, offset, run)));
        _caret = new DocPosition(block, offset + run.Text.Length);
        _selectionAnchor = _caret;
    }

    private void Backspace()
    {
        if (IsEditingLocked)
            return;

        // AV-HFEDIT: route into a header/footer region.
        if (_hfCaret is not null)
        {
            HfBackspace();
            return;
        }

        // AV-TBL: route into table cell.
        if (_cellCaret is { } cc)
        {
            // BE3: if there is an in-cell selection, delete it and return (mirrors body NormalizedSelection path).
            if (DeleteCellSelection(cc)) return;
            cc = _cellCaret!.Value; // re-read in case anchor was updated
            if (cc.Offset > 0)
            {
                var offset = cc.Offset;
                _bus.Execute(new ReplaceCellParagraphRunsCommand(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx, p =>
                {
                    if (TrackChangesEnabled)
                    {
                        var (marked, _) = MarkCellsDeleted(ParaCells(p), offset - 1, offset);
                        SetRuns(p, marked);
                        return;
                    }
                    var chars = ParaCells(p);
                    if (offset - 1 < chars.Count)
                        chars.RemoveAt(offset - 1);
                    SetRuns(p, chars);
                }));
                _cellCaret = cc with { Offset = offset - 1 };
                _cellAnchor = _cellCaret;
                _caret = new DocPosition(cc.TableBlock, FindCellGlyphOffset(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx, cc.Offset - 1));
                _selectionAnchor = _caret;
            }
            else if (cc.ParaIdx > 0)
            {
                // At start of a non-first paragraph in a cell → merge with previous paragraph.
                CellMergeWithPreviousParagraph(cc);
            }
            // else: at start of first paragraph in cell → do nothing (can't go back past cell boundary)
            return;
        }

        if (NormalizedSelection() is not null) { DeleteSelection(); return; }
        // AV-LIST: Backspace at start of a list item outdents / removes list formatting.
        if (BackspaceOutdentListItem()) return;
        if (_caret.Offset > 0)
        {
            var block = _caret.Block;
            var offset = _caret.Offset;
            if (TrackChangesEnabled)
            {
                // AV-TRACKEDIT: a tracked deletion keeps the character (struck) unless it is this author's own
                // still-pending insertion, which is removed outright. MarkCellsDeleted applies that rule.
                _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
                {
                    var (cells, _) = MarkCellsDeleted(ParaCells(p), offset - 1, offset);
                    SetRuns(p, cells);
                }));
            }
            else
            {
                _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
                {
                    var cells = ParaCells(p);
                    if (offset - 1 < cells.Count)
                        cells.RemoveAt(offset - 1);
                    SetRuns(p, cells);
                }));
            }
            _caret = new DocPosition(block, offset - 1);
            _selectionAnchor = _caret;
        }
        else
        {
            MergeWithPrevious();
        }
    }

    private void DeleteForward()
    {
        if (IsEditingLocked)
            return;

        // AV-HFEDIT: route into a header/footer region.
        if (_hfCaret is not null)
        {
            HfDeleteForward();
            return;
        }

        // AV-TBL: route into table cell.
        if (_cellCaret is { } cc)
        {
            // BE3: if there is an in-cell selection, delete it and return.
            if (DeleteCellSelection(cc)) return;
            cc = _cellCaret!.Value; // re-read after potential anchor update
            var para = GetCellParagraph(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx);
            if (para == null || !IsEditable(para))
                return;
            var len = ParaCells(para).Count;
            if (cc.Offset < len)
            {
                var offset = cc.Offset;
                if (TrackChangesEnabled)
                {
                    var before = ParaCells(para);
                    var ownInsertion = offset < before.Count
                        && before[offset].Revision == RevisionKind.Inserted
                        && string.Equals(before[offset].RevisionAuthor, RevisionAuthor, StringComparison.Ordinal);
                    _bus.Execute(new ReplaceCellParagraphRunsCommand(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx, p =>
                    {
                        var (marked, _) = MarkCellsDeleted(ParaCells(p), offset, offset + 1);
                        SetRuns(p, marked);
                    }));
                    // Advance past a kept-struck char so repeated Delete progresses; stay put if it collapsed.
                    if (!ownInsertion)
                    {
                        _cellCaret = cc with { Offset = offset + 1 };
                        _cellAnchor = _cellCaret;
                        _caret = new DocPosition(cc.TableBlock, FindCellGlyphOffset(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx, offset + 1));
                        _selectionAnchor = _caret;
                    }
                }
                else
                {
                    _bus.Execute(new ReplaceCellParagraphRunsCommand(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx, p =>
                    {
                        var chars = ParaCells(p);
                        if (offset < chars.Count)
                            chars.RemoveAt(offset);
                        SetRuns(p, chars);
                    }));
                    // Caret stays at same offset (now pointing at the next char).
                }
            }
            // else at end of paragraph → delete paragraph break (join with next paragraph in cell)
            else
            {
                var cellModel = GetCellModel(cc.TableBlock, cc.Row, cc.Col);
                if (cellModel != null && cc.ParaIdx < cellModel.Paragraphs.Count - 1)
                {
                    // Merge current para + next para in cell.
                    var curPara = cellModel.Paragraphs[cc.ParaIdx];
                    var nextPara = cellModel.Paragraphs[cc.ParaIdx + 1];
                    var merged = new Paragraph { Formatting = curPara.Formatting, StyleId = curPara.StyleId };
                    var mergedCells = ParaCells(curPara);
                    mergedCells.AddRange(ParaCells(nextPara));
                    SetRuns(merged, mergedCells);
                    _bus.Execute(new SpliceCellParagraphsCommand(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx, 2, [merged]));
                }
            }
            return;
        }

        if (NormalizedSelection() is not null) { DeleteSelection(); return; }
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return;
        var bodyLen = ParaCells(paragraph).Count;
        if (_caret.Offset < bodyLen)
        {
            var block = _caret.Block;
            var offset = _caret.Offset;
            if (TrackChangesEnabled)
            {
                // AV-TRACKEDIT: forward-delete records a tracked deletion (keeps the struck char) unless it is
                // this author's own pending insertion (removed outright). When kept-struck, advance the caret
                // past the struck character so a repeated Delete keeps progressing (Word behaviour); when the
                // char collapsed away, leave the caret in place (the next char shifted into its position).
                var before = ParaCells(paragraph);
                var ownInsertion = offset < before.Count
                    && before[offset].Revision == RevisionKind.Inserted
                    && string.Equals(before[offset].RevisionAuthor, RevisionAuthor, StringComparison.Ordinal);
                _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
                {
                    var (cells, _) = MarkCellsDeleted(ParaCells(p), offset, offset + 1);
                    SetRuns(p, cells);
                }));
                if (!ownInsertion)
                {
                    _caret = new DocPosition(block, offset + 1);
                    _selectionAnchor = _caret;
                }
            }
            else
            {
                _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
                {
                    var cells = ParaCells(p);
                    if (offset < cells.Count)
                        cells.RemoveAt(offset);
                    SetRuns(p, cells);
                }));
            }
        }
    }

    private void InsertParagraphBreak()
    {
        if (IsEditingLocked)
            return;

        // AV-HFEDIT: route into a header/footer region.
        if (_hfCaret is not null)
        {
            HfInsertParagraphBreak();
            return;
        }

        // AV-TBL: route into table cell.
        if (_cellCaret is { } cc)
        {
            // BE3: delete active selection before splitting paragraph.
            if (DeleteCellSelection(cc))
                cc = _cellCaret!.Value;
            var para = GetCellParagraph(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx);
            if (para == null || !IsEditable(para))
                return;
            var offset = cc.Offset;
            var chars = ParaCells(para);
            var first = new Paragraph { Formatting = para.Formatting, StyleId = para.StyleId };
            SetRuns(first, chars.Take(offset).ToList());
            var second = new Paragraph { Formatting = para.Formatting };
            SetRuns(second, chars.Skip(offset).ToList());
            _bus.Execute(new SpliceCellParagraphsCommand(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx, 1, [first, second]));
            // Move caret to start of the new second paragraph.
            _cellCaret = cc with { ParaIdx = cc.ParaIdx + 1, Offset = 0 };
            _cellAnchor = _cellCaret;
            _caret = new DocPosition(cc.TableBlock, FindCellGlyphOffset(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx + 1, 0));
            _selectionAnchor = _caret;
            return;
        }

        if (NormalizedSelection() is not null)
            DeleteSelection();
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return;

        var block = _caret.Block;
        var bodyOffset = _caret.Offset;
        var bodyCells = ParaCells(paragraph);

        // AV-LIST: list continuation / exit-list logic.
        var listFmt = paragraph.Formatting;
        if (listFmt.ListKind != ListKind.None)
        {
            if (bodyCells.Count == 0)
            {
                // Enter on an EMPTY list item → exit the list: turn the paragraph into a normal one.
                var exitFmt = listFmt with { ListKind = ListKind.None, ListLevel = 0 };
                _bus.Execute(new SetParagraphFormattingCommand(block, exitFmt));
                // Caret stays at block 0 (now a normal paragraph). No split.
                return;
            }
            // Enter on a NON-EMPTY list item → split and continue the list on the new paragraph.
            // The new paragraph inherits ListKind + ListLevel (not StyleId, same as Word).
            var firstPara = new Paragraph { Formatting = listFmt, StyleId = paragraph.StyleId };
            SetRuns(firstPara, bodyCells.Take(bodyOffset).ToList());
            var contFmt = listFmt with { };   // same list kind + level; renumbering is render-time
            var secondPara = new Paragraph { Formatting = contFmt };
            SetRuns(secondPara, bodyCells.Skip(bodyOffset).ToList());
            _bus.Execute(new ReplaceBlocksCommand(block, 1, new Block[] { firstPara, secondPara }));
            _caret = new DocPosition(block + 1, 0);
            _selectionAnchor = _caret;
            return;
        }

        var firstParaNL = new Paragraph { Formatting = paragraph.Formatting, StyleId = paragraph.StyleId };
        SetRuns(firstParaNL, bodyCells.Take(bodyOffset).ToList());
        var secondParaNL = new Paragraph { Formatting = paragraph.Formatting };
        SetRuns(secondParaNL, bodyCells.Skip(bodyOffset).ToList());
        _bus.Execute(new ReplaceBlocksCommand(block, 1, new Block[] { firstParaNL, secondParaNL }));
        _caret = new DocPosition(block + 1, 0);
        _selectionAnchor = _caret;
    }

    // AV-TBL: merge the current cell paragraph with the previous one (Backspace at start of para).
    private void CellMergeWithPreviousParagraph((int TableBlock, int Row, int Col, int ParaIdx, int Offset) cc)
    {
        var prevParaIdx = cc.ParaIdx - 1;
        var prevPara = GetCellParagraph(cc.TableBlock, cc.Row, cc.Col, prevParaIdx);
        var curPara = GetCellParagraph(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx);
        if (prevPara == null || curPara == null)
            return;
        var prevLen = ParaCells(prevPara).Count;
        var merged = new Paragraph { Formatting = prevPara.Formatting, StyleId = prevPara.StyleId };
        var mergedCells = ParaCells(prevPara);
        mergedCells.AddRange(ParaCells(curPara));
        SetRuns(merged, mergedCells);
        _bus.Execute(new SpliceCellParagraphsCommand(cc.TableBlock, cc.Row, cc.Col, prevParaIdx, 2, [merged]));
        _cellCaret = cc with { ParaIdx = prevParaIdx, Offset = prevLen };
        _cellAnchor = _cellCaret;
        _caret = new DocPosition(cc.TableBlock, FindCellGlyphOffset(cc.TableBlock, cc.Row, cc.Col, prevParaIdx, prevLen));
        _selectionAnchor = _caret;
    }

    private void MergeWithPrevious()
    {
        var block = _caret.Block;
        var prev = PreviousEditableBlock(block);
        if (prev < 0 || _doc.Blocks[prev] is not Paragraph prevPara || _doc.Blocks[block] is not Paragraph curPara)
            return;
        var prevCells = ParaCells(prevPara);
        var prevLen = prevCells.Count;
        var merged = new Paragraph { Formatting = prevPara.Formatting, StyleId = prevPara.StyleId };
        prevCells.AddRange(ParaCells(curPara));
        SetRuns(merged, prevCells);
        _bus.Execute(new ReplaceBlocksCommand(prev, block - prev + 1, new Block[] { merged }));
        _caret = new DocPosition(prev, prevLen);
        _selectionAnchor = _caret;
    }

    // BE3: Delete the active in-cell selection (same-paragraph only) and collapse the caret to the
    // selection start. Returns true if a selection was deleted, false if there was no selection or
    // the anchors span different paragraphs (caller must decide what to do in that case).
    private bool DeleteCellSelection((int TableBlock, int Row, int Col, int ParaIdx, int Offset) cc)
    {
        if (_cellAnchor is not { } anchor)
            return false;
        // Only handle same-paragraph cell selections (cross-paragraph cross-cell not supported here).
        if (anchor.TableBlock != cc.TableBlock || anchor.Row != cc.Row || anchor.Col != cc.Col
            || anchor.ParaIdx != cc.ParaIdx)
            return false;
        if (anchor.Offset == cc.Offset)
            return false; // collapsed — nothing to delete

        var lo = Math.Min(anchor.Offset, cc.Offset);
        var hi = Math.Max(anchor.Offset, cc.Offset);
        _bus.Execute(new ReplaceCellParagraphRunsCommand(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx, p =>
        {
            if (TrackChangesEnabled)
            {
                // AV-TRACKEDIT: mark the in-cell selection as a tracked deletion (keep struck) rather than
                // removing it; own pending insertions collapse away (handled inside MarkCellsDeleted).
                var (marked, _) = MarkCellsDeleted(ParaCells(p), lo, hi);
                SetRuns(p, marked);
                return;
            }
            var chars = ParaCells(p);
            var clo = Math.Clamp(lo, 0, chars.Count);
            var chi = Math.Clamp(hi, 0, chars.Count);
            chars.RemoveRange(clo, Math.Max(0, chi - clo));
            SetRuns(p, chars);
        }));
        _cellCaret = cc with { Offset = lo };
        _cellAnchor = _cellCaret;
        _caret = new DocPosition(cc.TableBlock, FindCellGlyphOffset(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx, lo));
        _selectionAnchor = _caret;
        return true;
    }

    private void DeleteSelection()
    {
        if (IsEditingLocked)
            return;

        if (NormalizedSelection() is not { } sel)
            return;

        if (sel.Start.Block == sel.End.Block)
        {
            var block = sel.Start.Block;
            var a = sel.Start.Offset;
            var b = sel.End.Offset;
            // BE5: guard against DeleteSelection being called when the block is a Table (not a Paragraph).
            // This can happen when _caret is positioned on a table block but _cellCaret is null (e.g.,
            // a glyph-offset body selection that spans into a table block). Silently no-op to avoid an
            // InvalidCastException in ReplaceParagraphRunsCommand's Apply.
            if (_doc.Blocks[block] is not Paragraph)
            {
                _selectionAnchor = _caret;
                return;
            }
            if (TrackChangesEnabled)
            {
                // AV-TRACKEDIT: a tracked deletion keeps the selected text (struck), except this author's own
                // pending insertions which are removed outright. Caret collapses to the selection start.
                _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
                {
                    var (cells, _) = MarkCellsDeleted(ParaCells(p), Math.Min(a, b), Math.Max(a, b));
                    SetRuns(p, cells);
                }));
            }
            else
            {
                _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
                {
                    var cells = ParaCells(p);
                    var lo = Math.Clamp(a, 0, cells.Count);
                    var hi = Math.Clamp(b, 0, cells.Count);
                    cells.RemoveRange(lo, Math.Max(0, hi - lo));
                    SetRuns(p, cells);
                }));
            }
            _caret = new DocPosition(block, Math.Min(a, b));
        }
        else if (_doc.Blocks[sel.Start.Block] is Paragraph startPara && _doc.Blocks[sel.End.Block] is Paragraph endPara)
        {
            var head = ParaCells(startPara).Take(sel.Start.Offset).ToList();
            head.AddRange(ParaCells(endPara).Skip(sel.End.Offset));
            var merged = new Paragraph { Formatting = startPara.Formatting, StyleId = startPara.StyleId };
            SetRuns(merged, head);
            _bus.Execute(new ReplaceBlocksCommand(sel.Start.Block, sel.End.Block - sel.Start.Block + 1, new Block[] { merged }));
            _caret = new DocPosition(sel.Start.Block, sel.Start.Offset);
        }

        _selectionAnchor = _caret;
    }

    // ---- Formatting -----------------------------------------------------------------------------

    public void ToggleBold() => ToggleRunFlag(f => f.Bold, (f, v) => f with { Bold = v });
    public void ToggleItalic() => ToggleRunFlag(f => f.Italic, (f, v) => f with { Italic = v });
    public void ToggleUnderline() => ToggleRunFlag(f => f.Underline, (f, v) => f with { Underline = v });
    public void ToggleStrikethrough() => ToggleRunFlag(f => f.Strikethrough, (f, v) => f with { Strikethrough = v });
    public void ToggleSmallCaps() => ToggleRunFlag(f => f.SmallCaps, (f, v) => f with { SmallCaps = v });
    public void ToggleAllCaps() => ToggleRunFlag(f => f.AllCaps, (f, v) => f with { AllCaps = v });

    /// <summary>
    /// Toggle superscript on the selection (clears subscript if set; clears superscript if already set).
    /// Word semantics: superscript and subscript are mutually exclusive.
    /// </summary>
    public void ToggleSuperscript()
    {
        var cells = SelectionOrParagraphCells();
        var allSuper = cells.Count > 0 && cells.All(c => c.Fmt.VerticalAlign == VerticalAlign.Superscript);
        ApplyRunFormatting(f => f with { VerticalAlign = allSuper ? VerticalAlign.Baseline : VerticalAlign.Superscript });
    }

    /// <summary>
    /// Toggle subscript on the selection (clears superscript if set; clears subscript if already set).
    /// </summary>
    public void ToggleSubscript()
    {
        var cells = SelectionOrParagraphCells();
        var allSub = cells.Count > 0 && cells.All(c => c.Fmt.VerticalAlign == VerticalAlign.Subscript);
        ApplyRunFormatting(f => f with { VerticalAlign = allSub ? VerticalAlign.Baseline : VerticalAlign.Subscript });
    }

    /// <summary>
    /// Set the highlight (background) colour of the selection. Pass null or empty to clear.
    /// </summary>
    public void SetHighlightColor(string? colorHex) =>
        ApplyRunFormatting(f => f with { HighlightColorHex = string.IsNullOrWhiteSpace(colorHex) ? null : colorHex });

    public void SetCharacterBorder(ParagraphBorder? border) =>
        ApplyRunFormatting(f => f with { CharacterBorder = border });

    public void SetCharacterShading(string? colorHex, ShadingPattern pattern = ShadingPattern.Clear) =>
        ApplyRunFormatting(f => f with
        {
            CharacterShadingHex = string.IsNullOrWhiteSpace(colorHex) ? null : colorHex,
            CharacterShadingPattern = string.IsNullOrWhiteSpace(colorHex) ? ShadingPattern.Clear : pattern,
        });

    /// <summary>
    /// Set the proofing (spelling/grammar) language on the selected text range, or on the current
    /// proofing word when the caret is collapsed inside one.
    /// </summary>
    public void SetProofingLanguage(string? languageTag)
    {
        var sel = NormalizedSelection();
        int[] selectedBlocks;
        int startOffset;
        int endOffset;
        ProofingLanguageCaretContext? caretContext = null;

        if (sel is { } s)
        {
            selectedBlocks = Enumerable.Range(s.Start.Block, s.End.Block - s.Start.Block + 1).ToArray();
            startOffset = s.Start.Offset;
            endOffset = s.End.Offset;
        }
        else
        {
            selectedBlocks = [_caret.Block];
            startOffset = _caret.Offset;
            endOffset = _caret.Offset;
            if (CurrentParagraph() is { } paragraph && IsEditable(paragraph))
            {
                caretContext = new ProofingLanguageCaretContext(
                    _caret.Block,
                    _caret.Offset,
                    paragraph.PlainText);
            }
        }

        var plan = ProofingLanguageApplyPlanner.BuildForSelectionOrCaretWord(
            languageTag,
            selectedBlocks,
            startOffset,
            endOffset,
            caretContext);

        ApplyProofingLanguagePlan(plan);
    }

    private void ApplyProofingLanguagePlan(ProofingLanguageApplyPlan plan)
    {
        var ranges = plan.Ranges
            .Where(range => range.BlockIndex >= 0
                && range.BlockIndex < _doc.Blocks.Count
                && _doc.Blocks[range.BlockIndex] is Paragraph paragraph
                && IsEditable(paragraph)
                && TextRangeCoversParagraphText(paragraph, range.StartOffset, range.EndOffset))
            .ToList();
        if (ranges.Count == 0)
            return;

        if (ranges.Count == 1)
        {
            ExecuteProofingLanguageRange(ranges[0], plan.LanguageTag);
            return;
        }

        _bus.BeginUndoGroup();
        foreach (var range in ranges)
            ExecuteProofingLanguageRange(range, plan.LanguageTag);
        _bus.CommitUndoGroup("Proofing Language");
    }

    private void ExecuteProofingLanguageRange(ProofingLanguageTextRange range, string? languageTag)
    {
        var capturedBlock = range.BlockIndex;
        var capturedA = range.StartOffset;
        var capturedB = range.EndOffset;

        _bus.Execute(new ReplaceParagraphRunsCommand(capturedBlock, p =>
        {
            var live = ParaCells(p);
            var lo = Math.Clamp(capturedA, 0, live.Count);
            var hi = Math.Clamp(capturedB, 0, live.Count);
            for (var i = lo; i < hi; i++)
                live[i] = live[i] with { Fmt = live[i].Fmt with { LanguageTag = languageTag } };
            SetRuns(p, live);
        }));
    }

    private static bool TextRangeCoversParagraphText(Paragraph paragraph, int startOffset, int endOffset)
    {
        var textLength = paragraph.PlainText.Length;
        var start = Math.Clamp(startOffset, 0, textLength);
        var end = Math.Clamp(endOffset, 0, textLength);
        return end > start;
    }

    public bool ToggleSpellCheck()
    {
        SpellCheckEnabled = !SpellCheckEnabled;
        InvalidateVisual();
        return SpellCheckEnabled;
    }

    public bool AddCurrentWordToDictionary()
    {
        if (CurrentProofingDiagnostic is not { } diagnostic)
            return false;

        if (!_customDictionary.Add(diagnostic.Word))
            return false;

        InvalidateVisual();
        return true;
    }

    public bool AddToDictionary(string? word)
    {
        var normalized = NormalizeProofingWord(word);
        if (normalized is null || !_customDictionary.Add(normalized))
            return false;

        InvalidateVisual();
        return true;
    }

    public bool IsInCustomDictionary(string? word) =>
        NormalizeProofingWord(word) is { } normalized && _customDictionary.Contains(normalized);

    public string? CurrentProofingWord =>
        NormalizeProofingWord(SelectedText) ?? WordAtCaret();

    public bool ReplaceCurrentProofingWord(string replacement)
    {
        if (string.IsNullOrWhiteSpace(replacement) || IsEditingLocked || _hfCaret is not null || _cellCaret is not null)
            return false;

        if (NormalizedSelection() is { } selection
            && selection.Start.Block == selection.End.Block
            && selection.Start.Offset != selection.End.Offset
            && NormalizeProofingWord(SelectedText) is not null)
        {
            InsertText(replacement);
            return true;
        }

        if (ProofingWordRangeAtCaret() is not { } range)
            return false;

        _selectionAnchor = new DocPosition(range.Block, range.Start);
        _caret = new DocPosition(range.Block, range.End);
        InsertText(replacement);
        return true;
    }

    public ProofingDiagnostic? CurrentProofingDiagnostic => CurrentProofingDiagnosticAtCaret();

    private IReadOnlyList<ProofingDiagnostic> BuildProofingDiagnostics() =>
        ProofingDiagnosticPlanner.Build(_doc, SpellCheckEnabled, _customDictionary.Words);

    private HashSet<(int Block, int Offset)> BuildProofingOffsetSet()
    {
        var offsets = new HashSet<(int Block, int Offset)>();
        foreach (var diagnostic in BuildProofingDiagnostics())
        {
            for (var offset = diagnostic.ParagraphOffset; offset < diagnostic.ParagraphOffset + diagnostic.Length; offset++)
                offsets.Add((diagnostic.BlockIndex, offset));
        }
        return offsets;
    }

    private ProofingDiagnostic? CurrentProofingDiagnosticAtCaret()
    {
        if (!SpellCheckEnabled || _cellCaret is not null || _hfCaret is not null)
            return null;

        var caretOffset = Math.Clamp(_caret.Offset, 0, BlockLength(_caret.Block));
        return BuildProofingDiagnostics()
            .FirstOrDefault(d =>
                d.BlockIndex == _caret.Block
                && caretOffset >= d.ParagraphOffset
                && caretOffset <= d.ParagraphOffset + d.Length);
    }

    // ── AV-COMMENT: review-comment insert / delete / resolve + introspection ──────────────────────
    // Model-backed (comments already round-trip through Core.IO). All mutations ride the shared
    // DocumentCommandBus so they are undoable, mirroring the WPF host's InsertComment / DeleteComment /
    // ToggleResolve behaviour but reusing the portable model directly (no ribbon wiring this wave).

    /// <summary>
    /// Anchors a new review comment over the current selection (or, when the selection is empty or spans
    /// multiple blocks, the whole caret paragraph). Allocates the next comment id, marks the covered body
    /// runs with it + appends a reference anchor, and stores the <see cref="Comment"/> (author/initials/
    /// text/date) in the model. Undoable; re-renders so the anchor highlight + margin marker appear.
    /// Returns the new comment id, or null when there was nothing textual to anchor to.
    /// </summary>
    public int? AddComment(string text, string author = "", string initials = "")
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.CommentInsert))
            return null;

        if (string.IsNullOrWhiteSpace(text))
            return null;

        // Resolve the anchor block + char range. A single-block non-empty selection anchors to that range;
        // an empty or cross-block selection anchors to the whole caret paragraph (mirrors WPF InsertComment).
        var sel = NormalizedSelection();
        int block, startOffset, endOffset;
        if (sel is { } s && s.Start.Block == s.End.Block && s.Start.Offset != s.End.Offset)
        {
            block = s.Start.Block;
            startOffset = s.Start.Offset;
            endOffset = s.End.Offset;
        }
        else
        {
            block = _caret.Block;
            startOffset = 0;
            endOffset = int.MaxValue;
        }

        if (block < 0 || block >= _doc.Blocks.Count || _doc.Blocks[block] is not Paragraph paragraph || !IsPlainTextEditable(paragraph))
            return null;

        // Nothing to anchor to (empty paragraph) → no comment.
        if (ParaCells(paragraph).Count == 0)
            return null;

        var id = _doc.NextCommentId();
        var comment = new Comment(id)
        {
            Author = author,
            Initials = initials,
            // W3CDTF (UTC, second precision) — matches the docx writer's w:date expectation.
            DateXml = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        };
        comment.Content.Add(new Paragraph(text));

        _bus.Execute(new AddCommentCommand(block, startOffset, endOffset, id, comment));
        // The command no-ops (and leaves Comments unchanged) when the range covers no text.
        return _doc.Comments.ContainsKey(id) ? id : (int?)null;
    }

    /// <summary>
    /// Deletes the comment thread with <paramref name="commentId"/> (and its replies), clearing the body
    /// anchor marks + reference run(s). Undoable. Returns true when a comment was removed.
    /// </summary>
    public bool DeleteComment(int commentId)
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.CommentDelete))
            return false;

        var topId = DeleteCommentCommand.ResolveTopLevel(_doc, commentId);
        if (!_doc.Comments.ContainsKey(topId))
            return false;
        _bus.Execute(new DeleteCommentCommand(topId));
        return true;
    }

    /// <summary>Deletes the comment thread covering the caret/selection. Returns true when one was removed.</summary>
    public bool DeleteCommentAtCaret() =>
        CommentIdAtCaret() is { } id && DeleteComment(id);

    /// <summary>
    /// Sets the resolved/done flag on the comment thread with <paramref name="commentId"/>. Undoable.
    /// Returns true when the comment exists (flag was set/cleared), false otherwise.
    /// </summary>
    public bool SetCommentResolved(int commentId, bool resolved)
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.CommentResolve))
            return false;

        var topId = DeleteCommentCommand.ResolveTopLevel(_doc, commentId);
        if (!_doc.Comments.ContainsKey(topId))
            return false;
        _bus.Execute(new SetCommentResolvedCommand(topId, resolved));
        return true;
    }

    /// <summary>
    /// Appends a reply to the specified top-level comment thread using the shared comment model.
    /// Undoable. Returns true when the reply was appended.
    /// </summary>
    public bool ReplyToComment(int commentId, string text, string author = "", string initials = "")
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.CommentReply))
            return false;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var topId = DeleteCommentCommand.ResolveTopLevel(_doc, commentId);
        if (!_doc.Comments.TryGetValue(topId, out var comment))
            return false;

        var replyId = _doc.NextCommentId();
        var reply = new Comment(replyId, text, author, initials)
        {
            DateXml = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        };

        _bus.Execute(new AddCommentReplyCommand(topId, reply));
        return comment.Replies.Any(candidate => candidate.Id == replyId);
    }

    /// <summary>Replies to the comment thread covering the caret. Returns true when a reply was appended.</summary>
    public bool ReplyToCommentAtCaret(string text = "Reply")
    {
        if (CommentIdAtCaret() is not { } id)
            return false;

        return ReplyToComment(id, text, RevisionAuthor, DeriveInitials(RevisionAuthor));
    }

    /// <summary>
    /// Toggles the resolved flag of the comment thread covering the caret/selection. Returns the new
    /// resolved state, or null when the caret is not inside a comment.
    /// </summary>
    public bool? ToggleResolveCommentAtCaret()
    {
        if (!AllowsRestrictEditingOperation(RestrictEditingOperationKind.CommentResolve))
            return null;

        if (CommentIdAtCaret() is not { } id || !_doc.Comments.TryGetValue(id, out var comment))
            return null;
        var newState = !comment.Resolved;
        SetCommentResolved(id, newState);
        return newState;
    }

    /// <summary>All top-level review comments in the document, in id order. Replies live on each thread.</summary>
    public IReadOnlyList<Comment> AllComments =>
        _doc.Comments.Values.OrderBy(c => c.Id).ToList();

    /// <summary>Shared planned comment list rows in document order, including anchor positions.</summary>
    public IReadOnlyList<CommentListItem> PlannedCommentList() =>
        CommentListPlanner.Build(_doc);

    /// <summary>Moves the caret to the previous comment in document order, wrapping at the start.</summary>
    public bool PreviousComment() => NavigateComment(direction: -1);

    /// <summary>Moves the caret to the next comment in document order, wrapping at the end.</summary>
    public bool NextComment() => NavigateComment(direction: 1);

    private bool NavigateComment(int direction)
    {
        var target = CommentListPlanner.SelectAdjacent(PlannedCommentList(), CommentIdAtCaret(), direction);
        return target is not null && MoveCaretToComment(target);
    }

    private bool MoveCaretToComment(CommentListItem item)
    {
        var anchor = item.Anchor;
        if (anchor.IsTableAnchor)
        {
            PlaceCaretInCell(
                anchor.BlockIndex,
                anchor.TableRowIndex!.Value,
                anchor.TableGridColumnIndex!.Value,
                anchor.TableParagraphIndex!.Value,
                anchor.Offset);
            ScrollToCaretRequested?.Invoke();
            return true;
        }

        if (anchor.BlockIndex < 0 || anchor.BlockIndex >= _doc.Blocks.Count || _doc.Blocks[anchor.BlockIndex] is not Paragraph)
            return false;

        _hfCaret = null;
        MoveCaretToBlock(anchor.BlockIndex, Math.Clamp(anchor.Offset, 0, BlockLength(anchor.BlockIndex)));
        InvalidateVisual();
        CaretMoved?.Invoke();
        ScrollToCaretRequested?.Invoke();
        return true;
    }

    /// <summary>
    /// The top-level comment threads whose anchored range covers the caret (or selection start), in id
    /// order. Empty when the caret is not inside any comment.
    /// </summary>
    public IReadOnlyList<Comment> CommentsAtCaret =>
        CommentIdAtCaret() is { } id && _doc.Comments.TryGetValue(id, out var comment)
            ? new[] { comment }
            : System.Array.Empty<Comment>();

    /// <summary>
    /// The id of the top-level comment whose anchored range covers the caret (or selection start), or
    /// null when the caret is not inside a comment. Resolved from the model run carrying CommentId at the
    /// caret offset; a reply's id is mapped up to its owning top-level comment.
    /// </summary>
    private int? CommentIdAtCaret()
    {
        if (_cellCaret is { } cellCaret && GetCellParagraph(
                cellCaret.TableBlock,
                cellCaret.Row,
                cellCaret.Col,
                cellCaret.ParaIdx) is { } cellParagraph)
        {
            return CommentIdInParagraphAtOffset(cellParagraph, cellCaret.Offset);
        }

        if (_caret.Block < 0 || _caret.Block >= _doc.Blocks.Count || _doc.Blocks[_caret.Block] is not Paragraph paragraph)
            return null;

        return CommentIdInParagraphAtOffset(paragraph, _caret.Offset);
    }

    private int? CommentIdInParagraphAtOffset(Paragraph paragraph, int offset)
    {
        var cells = ParaCells(paragraph);
        if (cells.Count == 0)
            return null;

        // Probe the cell just before the caret (the char the caret sits after), then the one at the caret.
        foreach (var probe in new[] { offset - 1, offset })
        {
            if (probe < 0 || probe >= cells.Count)
                continue;
            if (cells[probe].CommentId is { } cid)
                return DeleteCommentCommand.ResolveTopLevel(_doc, cid);
        }
        // Fallback: any commented run in the paragraph (caret placed loosely inside the range).
        foreach (var cell in cells)
            if (cell.CommentId is { } cid)
                return DeleteCommentCommand.ResolveTopLevel(_doc, cid);
        return null;
    }

    /// <summary>
    /// Test/introspection hook: the page-space rectangles of every laid-out glyph currently marked by a
    /// review comment, paired with the anchoring top-level comment id. Non-empty exactly when a comment's
    /// anchored range maps onto rendered glyphs. Reflects the last layout pass (call after Measure).
    /// </summary>
    internal IReadOnlyList<(int CommentId, Rect Rect)> CommentAnchorGlyphs()
    {
        // Force a fresh layout so the result always reflects the current model (introspection/test hook).
        Relayout(_laidOutWidth > 0 ? _laidOutWidth : FallbackWidth);
        return CommentAnchorGlyphSnapshot(highlightedOnly: false);
    }

    private IReadOnlyList<(int CommentId, Rect Rect)> CommentAnchorGlyphSnapshot(bool highlightedOnly)
    {
        var policy = CurrentReviewDisplayPolicy;
        if (highlightedOnly && !policy.ShouldHighlightComments)
            return [];

        return _placed
            .Where(p => !p.Sentinel
                && p.CommentId is not null
                && policy.IsRevisionTextVisible(p.Revision))
            .Select(p => (DeleteCommentCommand.ResolveTopLevel(_doc, p.CommentId!.Value),
                          new Rect(p.X, p.Y, Math.Max(1, p.W), p.LineHeight)))
            .ToList();
    }

    private IReadOnlyList<(int Block, Rect Rect)> SimpleMarkupChangeBarSnapshot()
    {
        var policy = CurrentReviewDisplayPolicy;
        if (!policy.ShouldShowSimpleMarkupChangeBar)
            return [];

        return _placed
            .Where(p => !p.Sentinel && p.Revision != RevisionKind.None)
            .GroupBy(p => p.Block)
            .Select(g =>
            {
                var top = g.Min(p => p.Y);
                var bottom = g.Max(p => p.Y + Math.Max(1, p.LineHeight));
                var x = Math.Max(0, _contentLeft - 9);
                return (g.Key, new Rect(x, top, 2, Math.Max(6, bottom - top)));
            })
            .ToList();
    }

    // ── AV-REVIEW: Review-tab tracked-changes + comments + word count wiring ──────────────────────────
    // Accept/reject ride the shared DocumentCommandBus (undoable), mirroring the WPF host's accept/reject
    // but reusing the portable TrackChanges/RevisionList model directly. Comments reuse the AV-COMMENT
    // infra (AddComment/DeleteComment). Word count reads DocumentStatistics from the model.

    /// <summary>
    /// When true, the editor is in Track Changes mode and the edit pipeline records edits as revisions
    /// (AV-TRACKEDIT): typing inserts text marked as a tracked insertion (current <see cref="RevisionAuthor"/>
    /// + date), and Backspace/Delete/selection-delete mark the affected text as a tracked deletion (the text
    /// is kept and struck, per Word) rather than removing it — except deleting one's own still-pending tracked
    /// insertion, which is removed outright. Accept/Reject of existing revisions work regardless of this flag.
    /// </summary>
    public bool TrackChangesEnabled { get; private set; }

    /// <summary>The default revision author stamped on tracked changes this editor records.</summary>
    public string RevisionAuthor { get; set; } = "FreeW User";

    public ReviewDisplayMode DisplayForReview { get; private set; } = ReviewDisplayMode.AllMarkup;

    public bool ShowMarkupInsertionsAndDeletions { get; private set; } = true;

    public bool ShowMarkupComments { get; private set; } = true;

    public bool ShowMarkupFormatting { get; private set; } = true;

    public bool ShowMarkupBalloons { get; private set; }

    public ReviewDisplayPolicy CurrentReviewDisplayPolicy =>
        new(DisplayForReview, ShowMarkupInsertionsAndDeletions, ShowMarkupComments, ShowMarkupFormatting);

    public ReviewWorkflowStatus CurrentReviewWorkflowStatus =>
        ReviewWorkflowStatusPlanner.Build(_doc, CurrentReviewDisplayPolicy, TrackChangesEnabled);

    public void ApplyDisplayForReview(ReviewDisplayMode mode)
    {
        DisplayForReview = mode;
        InvalidateLayoutAndVisual();
    }

    public void ApplyShowMarkupInsertionsAndDeletions(bool show)
    {
        ShowMarkupInsertionsAndDeletions = show;
        InvalidateLayoutAndVisual();
    }

    public void ApplyShowMarkupComments(bool show)
    {
        ShowMarkupComments = show;
        InvalidateLayoutAndVisual();
    }

    public void ApplyShowMarkupFormatting(bool show)
    {
        ShowMarkupFormatting = show;
        InvalidateLayoutAndVisual();
    }

    public void ApplyShowMarkupBalloons(bool show)
    {
        ShowMarkupBalloons = show;
        InvalidateLayoutAndVisual();
    }

    /// <summary>The W3CDTF (UTC) timestamp stamped on revisions recorded right now.</summary>
    private static string CurrentRevisionDateXml() =>
        DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>
    /// AV-TRACKEDIT: mark the cell range [lo, hi) of <paramref name="cells"/> as a tracked deletion (per Word:
    /// the characters are KEPT and struck, not removed) and return the resulting list together with the caret
    /// offset that should follow the operation. Characters that are an unaccepted tracked insertion <em>by the
    /// same author</em> are removed outright instead (Word behaviour: deleting your own pending insertion just
    /// takes it back). Characters already marked deleted are left as-is. The returned caret offset is the start
    /// of the range when anything was kept-struck, otherwise <paramref name="lo"/> (the run collapsed away).
    /// </summary>
    private (List<Cell> Cells, int Caret) MarkCellsDeleted(List<Cell> cells, int lo, int hi)
    {
        lo = Math.Clamp(lo, 0, cells.Count);
        hi = Math.Clamp(hi, 0, cells.Count);
        if (hi <= lo)
            return (cells, lo);

        var result = new List<Cell>(cells.Count);
        result.AddRange(cells.Take(lo));
        for (var k = lo; k < hi; k++)
        {
            var cell = cells[k];
            // Deleting one's own still-pending insertion removes it outright (Word: it never "existed").
            if (cell.Revision == RevisionKind.Inserted &&
                string.Equals(cell.RevisionAuthor, RevisionAuthor, StringComparison.Ordinal))
                continue;
            // Already a tracked deletion → keep as-is (deleting struck text is a no-op).
            if (cell.Revision == RevisionKind.Deleted)
            {
                result.Add(cell);
                continue;
            }
            // Otherwise mark the (ordinary, or other-author-inserted) character as a tracked deletion: keep it.
            result.Add(cell with
            {
                Revision = RevisionKind.Deleted,
                RevisionAuthor = RevisionAuthor,
                RevisionDateXml = CurrentRevisionDateXml(),
            });
        }
        result.AddRange(cells.Skip(hi));
        return (result, lo);
    }

    /// <summary>
    /// Toggles <see cref="TrackChangesEnabled"/> and returns the new state. Re-renders so any change-bar /
    /// markup adorners that depend on the mode update. While on, subsequent edits are recorded as tracked
    /// revisions — see <see cref="TrackChangesEnabled"/>.
    /// </summary>
    public bool ToggleTrackChanges()
    {
        TrackChangesEnabled = !TrackChangesEnabled;
        InvalidateVisual();
        DocumentChanged?.Invoke();
        return TrackChangesEnabled;
    }

    /// <summary>
    /// Marks the current selection (or, when empty, the whole caret paragraph) as a tracked change of
    /// <paramref name="kind"/> (insertion or deletion) by <see cref="RevisionAuthor"/>. Undoable; re-renders
    /// so the revision colour/decoration appears and the marks round-trip on save. Returns true when a run
    /// was marked. A no-op for <see cref="RevisionKind.None"/> or when there is nothing textual to mark.
    /// </summary>
    public bool MarkSelectionAsRevision(RevisionKind kind)
    {
        if (kind == RevisionKind.None)
            return false;

        var sel = NormalizedSelection();
        int block, startOffset, endOffset;
        if (sel is { } s && s.Start.Block == s.End.Block && s.Start.Offset != s.End.Offset)
        {
            block = s.Start.Block;
            startOffset = s.Start.Offset;
            endOffset = s.End.Offset;
        }
        else
        {
            block = _caret.Block;
            startOffset = 0;
            endOffset = int.MaxValue;
        }

        if (block < 0 || block >= _doc.Blocks.Count || _doc.Blocks[block] is not Paragraph paragraph || !IsEditable(paragraph))
            return false;
        if (ParaCells(paragraph).Count == 0)
            return false;

        var dateXml = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        _bus.Execute(new MarkRevisionRangeCommand(block, startOffset, endOffset, kind, RevisionAuthor, dateXml));
        return TrackChanges.HasRevisions(_doc);
    }

    /// <summary>Every tracked change in the committed document, in reading order — drives Previous/Next.</summary>
    public IReadOnlyList<RevisionEntry> Revisions => RevisionList.Enumerate(_doc);

    /// <summary>True when the document carries any tracked change (insertion/deletion/formatting).</summary>
    public bool HasRevisions => TrackChanges.HasRevisions(_doc);

    /// <summary>
    /// Accept exactly one tracked change — the one at or after the caret, falling back to the first revision
    /// in reading order. Undoable; re-renders. Returns true when a revision was resolved.
    /// </summary>
    public bool AcceptCurrentRevision() => ResolveCurrentRevision(accept: true);

    /// <summary>
    /// Reject exactly one tracked change — the one at or after the caret, falling back to the first revision
    /// in reading order. Undoable; re-renders. Returns true when a revision was resolved.
    /// </summary>
    public bool RejectCurrentRevision() => ResolveCurrentRevision(accept: false);

    private bool ResolveCurrentRevision(bool accept)
    {
        var entries = RevisionList.Enumerate(_doc);
        if (entries.Count == 0)
            return false;

        // Prefer the first revision whose owning block index is at/after the caret block; else the first.
        var caretBlock = _caret.Block;
        var index = -1;
        for (var i = 0; i < entries.Count; i++)
        {
            if (entries[i].BlockIndex >= caretBlock)
            {
                index = i;
                break;
            }
        }
        if (index < 0)
            index = 0;

        _bus.Execute(accept ? new AcceptOneRevisionCommand(index) : new RejectOneRevisionCommand(index));
        return true;
    }

    /// <summary>
    /// Accept every tracked change: insertions become ordinary text, deletions are removed. Undoable as a
    /// single step; re-renders. Returns true when there was anything to resolve.
    /// </summary>
    public bool AcceptAllRevisions()
    {
        if (!TrackChanges.HasRevisions(_doc))
            return false;
        _bus.Execute(new AcceptAllRevisionsCommand());
        return true;
    }

    /// <summary>
    /// Reject every tracked change: insertions are removed, deletions become ordinary text. Undoable as a
    /// single step; re-renders. Returns true when there was anything to resolve.
    /// </summary>
    public bool RejectAllRevisions()
    {
        if (!TrackChanges.HasRevisions(_doc))
            return false;
        _bus.Execute(new RejectAllRevisionsCommand());
        return true;
    }

    /// <summary>
    /// Adds a review comment over the current selection using <see cref="RevisionAuthor"/> as the author
    /// (initials derived from it). Reuses the AV-COMMENT <see cref="AddComment(string,string,string)"/>
    /// infra. Returns the new comment id, or null when there was nothing textual to anchor to.
    /// Wired to <c>freew.new-comment</c>.
    /// </summary>
    public int? NewComment(string text = "New comment")
    {
        var initials = DeriveInitials(RevisionAuthor);
        return AddComment(text, RevisionAuthor, initials);
    }

    /// <summary>Derives up-to-two-letter initials from an author name (e.g. "Ann Reviewer" → "AR").</summary>
    private static string DeriveInitials(string? author)
    {
        if (string.IsNullOrWhiteSpace(author))
            return "";
        var parts = author.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            0 => "",
            1 => parts[0][..1].ToUpperInvariant(),
            _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Full word/character/paragraph statistics for the document, computed from the model via
    /// <see cref="DocumentStatistics.Compute(TextDocument)"/>. Drives the Word Count dialog.
    /// </summary>
    public DocumentStatistics ComputeStatistics() => DocumentStatistics.Compute(_doc);

    /// <summary>
    /// Set the paragraph space-before (in points) for the current paragraph.
    /// </summary>
    public void SetSpaceBefore(double pt)
    {
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return;
        _bus.Execute(new SetParagraphFormattingCommand(_caret.Block,
            paragraph.Formatting with { SpaceBeforePt = Math.Max(0, pt), SpaceBeforeIsSet = true }));
    }

    /// <summary>
    /// Set the paragraph space-after (in points) for the current paragraph.
    /// </summary>
    public void SetSpaceAfter(double pt)
    {
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return;
        _bus.Execute(new SetParagraphFormattingCommand(_caret.Block,
            paragraph.Formatting with { SpaceAfterPt = Math.Max(0, pt), SpaceAfterIsSet = true }));
    }

    /// <summary>
    /// Set the paragraph line-spacing rule. For <see cref="LineSpacingRule.Multiple"/> pass
    /// <paramref name="value"/> as the multiplier (e.g. 1.5 or 2.0). For
    /// <see cref="LineSpacingRule.Exact"/> or <see cref="LineSpacingRule.AtLeast"/> pass the
    /// absolute line height in points.
    /// </summary>
    public void SetLineSpacing(LineSpacingRule rule, double value)
    {
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return;
        var fmt = paragraph.Formatting;
        fmt = rule == LineSpacingRule.Multiple
            ? fmt with { LineRule = rule, LineSpacing = Math.Max(0.5, value), LineSpacingIsSet = true }
            : fmt with { LineRule = rule, LineHeightPt = Math.Max(1, value), LineSpacingIsSet = true };
        _bus.Execute(new SetParagraphFormattingCommand(_caret.Block, fmt));
    }

    /// <summary>
    /// Set the left/right/first-line indents (in points) for the current paragraph.
    /// Pass null to leave a particular indent unchanged.
    /// </summary>
    public void SetIndents(double? leftPt = null, double? rightPt = null, double? firstLinePt = null)
    {
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return;
        var fmt = paragraph.Formatting;
        if (leftPt.HasValue)     fmt = fmt with { IndentLeftPt      = Math.Max(0, leftPt.Value) };
        if (rightPt.HasValue)    fmt = fmt with { IndentRightPt     = Math.Max(0, rightPt.Value) };
        if (firstLinePt.HasValue) fmt = fmt with { FirstLineIndentPt = firstLinePt.Value };
        _bus.Execute(new SetParagraphFormattingCommand(_caret.Block, fmt));
    }

    /// <summary>
    /// Returns the cells in the current selection (if any and single-block), or all cells in the
    /// current paragraph. Used for toggle-state queries (e.g. "are all selected chars superscript?").
    /// </summary>
    private IReadOnlyList<Cell> SelectionOrParagraphCells()
    {
        var sel = NormalizedSelection();
        if (sel is { } s && s.Start.Block == s.End.Block && _doc.Blocks[s.Start.Block] is Paragraph selPara && IsEditable(selPara))
        {
            var all = ParaCells(selPara);
            var a = Math.Clamp(s.Start.Offset, 0, all.Count);
            var b = Math.Clamp(s.End.Offset, 0, all.Count);
            return all.Skip(a).Take(b - a).ToList();
        }
        return CurrentParagraph() is { } p && IsEditable(p) ? ParaCells(p) : [];
    }

    /// <summary>
    /// Increase the font size of the selection (or whole paragraph when no selection) to the next
    /// standard size step: 8 9 10 11 12 14 16 18 20 24 28 36 48 72. Above 72 the step is 8.
    /// </summary>
    public void GrowFont() => ApplyRunFormatting(f => f with { FontSizePt = NextFontSize(f.FontSizePt ?? DefaultFontSizePt, +1) });

    /// <summary>
    /// Decrease the font size of the selection (or whole paragraph when no selection) to the
    /// previous standard size step.
    /// </summary>
    public void ShrinkFont() => ApplyRunFormatting(f => f with { FontSizePt = NextFontSize(f.FontSizePt ?? DefaultFontSizePt, -1) });

    /// <summary>
    /// Remove all character-level formatting from the selection (resets to <see cref="RunFormatting.Default"/>).
    /// Paragraph-level properties (alignment, list kind, style) are untouched.
    /// </summary>
    public void ClearFormatting() => ApplyRunFormatting(_ => RunFormatting.Default);

    /// <summary>
    /// Set the text (foreground) colour of the selection or current paragraph to the given RRGGBB
    /// hex (e.g. <c>"#FF0000"</c>). Pass null to clear the explicit colour (inherit from theme).
    /// </summary>
    public void SetFontColor(string? colorHex) => ApplyRunFormatting(f => f with { ColorHex = colorHex });

    /// <summary>
    /// Extend the selection to encompass every block in the document (mirrors Ctrl+A / Edit → Select All).
    /// </summary>
    public void SelectAll()
    {
        if (_doc.Blocks.Count == 0)
            return;
        _selectionAnchor = new DocPosition(0, 0);
        var lastBlock = _doc.Blocks.Count - 1;
        _caret = new DocPosition(lastBlock, BlockLength(lastBlock));
        InvalidateVisual();
    }

    /// <summary>
    /// Cycle the text case of the selection: lower → Title → UPPER → lower.
    /// When there is no selection the whole current paragraph is cycled.
    /// </summary>
    public void ChangeCase() => ApplyRunFormattingToText(CycleCase);

    /// <summary>
    /// Increase the list indent level of the current paragraph by one (up to a reasonable cap).
    /// For non-list paragraphs this increases the left indent.
    /// </summary>
    public void IncreaseIndent()
    {
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return;
        var fmt = paragraph.Formatting;
        if (fmt.ListKind != ListKind.None)
            _bus.Execute(new SetParagraphFormattingCommand(_caret.Block, fmt with { ListLevel = Math.Min(fmt.ListLevel + 1, 8) }));
        else
            _bus.Execute(new SetParagraphFormattingCommand(_caret.Block, fmt with { IndentLeftPt = fmt.IndentLeftPt + 36 }));
    }

    /// <summary>
    /// Decrease the list indent level of the current paragraph by one (floor at 0).
    /// For non-list paragraphs this decreases the left indent (floor at 0).
    /// </summary>
    public void DecreaseIndent()
    {
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return;
        var fmt = paragraph.Formatting;
        if (fmt.ListKind != ListKind.None)
            _bus.Execute(new SetParagraphFormattingCommand(_caret.Block, fmt with { ListLevel = Math.Max(fmt.ListLevel - 1, 0) }));
        else
            _bus.Execute(new SetParagraphFormattingCommand(_caret.Block, fmt with { IndentLeftPt = Math.Max(fmt.IndentLeftPt - 36, 0) }));
    }

    /// <summary>
    /// Toggle display of paragraph marks (¶) and other formatting symbols.
    /// The marks are drawn as faint decorations that do not affect layout.
    /// </summary>
    public bool ShowParagraphMarks
    {
        get => _showParagraphMarks;
        set
        {
            if (_showParagraphMarks == value)
                return;
            _showParagraphMarks = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// AV-VIEW: Toggle a faint layout-gridlines overlay drawn behind the body text on each page
    /// (View → Show → Gridlines in Word). The grid is purely visual chrome; it does not affect layout.
    /// Only meaningful in <see cref="DocumentViewMode.PrintLayout"/> (where discrete pages exist).
    /// </summary>
    public bool ShowGridlines
    {
        get => _showGridlines;
        set
        {
            if (_showGridlines == value)
                return;
            _showGridlines = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Display-only Table Layout > View Gridlines toggle. Draws table cell outlines for borderless
    /// tables without changing the document model.
    /// </summary>
    public bool ViewTableGridlines
    {
        get => _showTableGridlines;
        set
        {
            if (_showTableGridlines == value)
                return;
            _showTableGridlines = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// AV-VIEW: Toggle a horizontal (top) + vertical (left) ruler strip with tick marks and margin
    /// markers, drawn on the first page in <see cref="DocumentViewMode.PrintLayout"/> (View → Show →
    /// Ruler in Word). View-only chrome; does not affect layout.
    /// </summary>
    public bool ShowRuler
    {
        get => _showRuler;
        set
        {
            if (_showRuler == value)
                return;
            _showRuler = value;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// AV-VIEW: Compute the layout-gridlines for the current layout — one horizontal line every
    /// <see cref="GridlineStepDip"/> within each page's text area, plus one vertical line every
    /// step across the text width. Returns page-space line segments (X1,Y1)-(X2,Y2). Exposed for the
    /// Render pass and for tests; empty when gridlines are off or not in Print Layout.
    /// </summary>
    internal IReadOnlyList<(double X1, double Y1, double X2, double Y2)> ComputeGridlines()
    {
        if (!_showGridlines || _viewMode != DocumentViewMode.PrintLayout)
            return [];

        return DocumentViewLayoutPlanner
            .BuildGridlines(_surfacePlan, _pageCount, GridlineStepDip)
            .Select(line => (line.X1, line.Y1, line.X2, line.Y2))
            .ToList();
    }

    /// <summary>
    /// AV-VIEW: Compute the ruler tick marks for the first page's horizontal ruler — one tick every
    /// inch (72pt) across the page width, measured from the left page edge. Returns page-space tick X
    /// positions. Exposed for the Render pass and for tests; empty when the ruler is off or not in
    /// Print Layout.
    /// </summary>
    internal IReadOnlyList<double> ComputeRulerTicks()
    {
        if (!_showRuler || _viewMode != DocumentViewMode.PrintLayout)
            return [];
        const double inchDip = 72.0; // 1in = 72pt = 72 DIP at 96 DPI base
        return DocumentViewLayoutPlanner.BuildRulerTicks(_surfacePlan, inchDip);
    }

    public void SetAlignment(TextAlignment alignment)
    {
        if (CurrentParagraph() is not { } paragraph)
            return;
        _bus.Execute(new SetParagraphFormattingCommand(_caret.Block, paragraph.Formatting with { Alignment = alignment }));
    }

    public void ApplyMultiLevelListToSelection()
    {
        FormatSelectedParagraphs(formatting => formatting with
        {
            ListKind = ListKind.MultiLevel,
            ListLevel = formatting.ListLevel
        });
    }

    public void ApplyMultiLevelListStartOverrides(int? level0StartAt, int? level1StartAt)
    {
        FormatSelectedParagraphs(formatting =>
            formatting.ListKind != ListKind.MultiLevel ? formatting :
            formatting.ListLevel == 0 && level0StartAt.HasValue ? formatting with { ListStartOverride = level0StartAt } :
            formatting.ListLevel == 1 && level1StartAt.HasValue ? formatting with { ListStartOverride = level1StartAt } :
            formatting);
    }

    public void ApplyMultiLevelHeadingPreset()
    {
        ApplyMultiLevelListToSelection();

        var styleId = GetCaretFormatting().Paragraph.ListLevel switch
        {
            0 => "Heading1",
            1 => "Heading2",
            _ => "Heading3",
        };

        ApplyNamedStyle(styleId);
    }

    /// <summary>
    /// Enumerate every paragraph block index spanned by the current selection (start block to end
    /// block inclusive). When there is no selection the result is just the caret's block (if it is
    /// an editable paragraph). Tables in the range are skipped (only paragraphs are returned),
    /// mirroring the WPF FormatSelectedModelParagraphs behaviour.
    /// </summary>
    private IReadOnlyList<int> SelectedParagraphIndices()
    {
        var sel = NormalizedSelection();
        int startBlock, endBlock;
        if (sel is { } s)
        {
            startBlock = s.Start.Block;
            endBlock   = s.End.Block;
        }
        else
        {
            startBlock = _caret.Block;
            endBlock   = _caret.Block;
        }

        var result = new List<int>();
        for (var i = startBlock; i <= endBlock && i < _doc.Blocks.Count; i++)
        {
            if (_doc.Blocks[i] is Paragraph p && IsEditable(p))
                result.Add(i);
        }
        return result;
    }

    /// <summary>
    /// Apply <paramref name="transform"/> to the formatting of every paragraph spanned by the
    /// current selection (or just the caret paragraph when there is no selection). All mutations
    /// are wrapped in a single undo group so one Undo reverts them all atomically.
    /// Mirrors the WPF DocumentView.FormatSelectedModelParagraphs.
    /// </summary>
    private void FormatSelectedParagraphs(Func<ParagraphFormatting, ParagraphFormatting> transform)
    {
        var indices = SelectedParagraphIndices();
        if (indices.Count == 0)
            return;

        if (indices.Count == 1)
        {
            // Single paragraph: no group overhead needed.
            var paragraph = (Paragraph)_doc.Blocks[indices[0]];
            _bus.Execute(new SetParagraphFormattingCommand(indices[0], transform(paragraph.Formatting)));
            return;
        }

        // Multiple paragraphs: group into a single undoable action.
        _bus.BeginUndoGroup();
        foreach (var idx in indices)
        {
            var paragraph = (Paragraph)_doc.Blocks[idx];
            _bus.Execute(new SetParagraphFormattingCommand(idx, transform(paragraph.Formatting)));
        }
        _bus.CommitUndoGroup("Paragraph Formatting");
    }

    public void ToggleKeepWithNext()
    {
        var indices = SelectedParagraphIndices();
        var enable = indices
            .Select(i => (Paragraph)_doc.Blocks[i])
            .Any(p => !p.Formatting.KeepWithNext);
        FormatSelectedParagraphs(f => f with { KeepWithNext = enable });
    }

    public void ToggleKeepLinesTogether()
    {
        var indices = SelectedParagraphIndices();
        var enable = indices
            .Select(i => (Paragraph)_doc.Blocks[i])
            .Any(p => !p.Formatting.KeepLinesTogether);
        FormatSelectedParagraphs(f => f with { KeepLinesTogether = enable });
    }

    public void ToggleWidowControl()
    {
        var indices = SelectedParagraphIndices();
        var enable = indices
            .Select(i => (Paragraph)_doc.Blocks[i])
            .Any(p => !p.Formatting.WidowControl);
        FormatSelectedParagraphs(f => f with { WidowControl = enable });
    }

    public void SetParagraphBorder(ParagraphBorder? border) =>
        FormatSelectedParagraphs(f => f with { Border = border });

    public void ToggleParagraphBorder(string colorHex = "#000000", double widthPt = 0.5)
    {
        var indices = SelectedParagraphIndices();
        var enable = indices
            .Select(i => (Paragraph)_doc.Blocks[i])
            .Any(p => p.Formatting.Border is null);
        SetParagraphBorder(enable ? new ParagraphBorder(colorHex, widthPt) : null);
    }

    public void SetParagraphShading(string? colorHex, ShadingPattern pattern = ShadingPattern.Clear) =>
        FormatSelectedParagraphs(f => f with
        {
            ShadingColorHex = string.IsNullOrWhiteSpace(colorHex) ? null : colorHex,
            ShadingPattern = string.IsNullOrWhiteSpace(colorHex) ? ShadingPattern.Clear : pattern,
        });

    public void ToggleParagraphShading(string? colorHex = "#FFF2CC")
    {
        var indices = SelectedParagraphIndices();
        var clear = string.IsNullOrWhiteSpace(colorHex)
            || indices
                .Select(i => (Paragraph)_doc.Blocks[i])
                .All(p => string.Equals(p.Formatting.ShadingColorHex, colorHex, StringComparison.OrdinalIgnoreCase));
        SetParagraphShading(clear ? null : colorHex, ShadingPattern.Clear);
    }

    public void SetParagraphTabStops(IReadOnlyList<TabStop> tabStops)
    {
        ArgumentNullException.ThrowIfNull(tabStops);
        var normalized = tabStops.ToArray();
        FormatSelectedParagraphs(f => f with { TabStops = normalized });
    }

    public bool IsCaretInTable() => CaretTableCell() is not null;

    public void SortSelectedParagraphs(SortKind kind, bool ascending, bool caseSensitive, bool hasHeaderRow)
    {
        if (IsEditingLocked)
            return;

        var indices = SelectedParagraphIndices();
        if (indices.Count == 0)
            return;

        var first = indices[0];
        var last = indices[^1];
        if (first < 0 || last >= _doc.Blocks.Count)
            return;

        var paragraphs = new List<Paragraph>();
        for (var i = first; i <= last; i++)
            if (_doc.Blocks[i] is Paragraph paragraph)
                paragraphs.Add(paragraph);
        if (paragraphs.Count < 2)
            return;

        var sorted = ParagraphSort.Sort(paragraphs, kind, ascending, caseSensitive, hasHeaderRow);
        var replacement = new List<Block>(last - first + 1);
        var nextSorted = 0;
        for (var i = first; i <= last; i++)
            replacement.Add(_doc.Blocks[i] is Paragraph ? sorted[nextSorted++] : _doc.Blocks[i]);

        _bus.Execute(new ReplaceBlocksCommand(first, replacement.Count, replacement));
    }

    public void SortCaretTableRows(SortKind kind, bool ascending, bool caseSensitive, bool hasHeaderRow)
    {
        if (IsEditingLocked || _cellCaret is not { } cc)
            return;
        if (cc.TableBlock < 0 || cc.TableBlock >= _doc.Blocks.Count ||
            _doc.Blocks[cc.TableBlock] is not Table table ||
            table.Rows.Count < 2)
            return;

        var keyColumn = GridColumnToCellIndex(table.Rows[cc.Row], cc.Col);
        if (keyColumn < 0)
            keyColumn = 0;

        var sorted = ParagraphSort.SortRows(table.Rows, keyColumn, kind, ascending, caseSensitive, hasHeaderRow);
        var replacement = TableLayoutOperations.CopyTableWithRows(table, sorted);
        _bus.Execute(new ReplaceBlocksCommand(cc.TableBlock, 1, new Block[] { replacement }));
    }

    /// <summary>
    /// Apply the complete set of paragraph-dialog fields (alignment, indents, spacing, line spacing)
    /// to every paragraph spanned by the current selection. All changes are issued as one undoable
    /// action (a single Undo reverts all paragraphs). Mirrors WPF ApplyParagraphDialogFormatting.
    /// </summary>
    public void ApplyParagraphDialogFormatting(
        TextAlignment alignment,
        double indentLeftPt, double indentRightPt, double firstLineIndentPt,
        double spaceBeforePt, double spaceAfterPt,
        LineSpacingRule lineRule, double lineSpacingValue)
    {
        FormatSelectedParagraphs(f =>
        {
            var fmt = f with
            {
                Alignment         = alignment,
                IndentLeftPt      = Math.Max(0, indentLeftPt),
                IndentRightPt     = Math.Max(0, indentRightPt),
                FirstLineIndentPt = firstLineIndentPt,
                SpaceBeforePt     = Math.Max(0, spaceBeforePt),
                SpaceAfterPt      = Math.Max(0, spaceAfterPt),
                SpaceBeforeIsSet  = true,
                SpaceAfterIsSet   = true,
                LineSpacingIsSet  = true,
            };
            fmt = lineRule == LineSpacingRule.Multiple
                ? fmt with { LineRule = lineRule, LineSpacing  = Math.Max(0.5, lineSpacingValue) }
                : fmt with { LineRule = lineRule, LineHeightPt = Math.Max(1,   lineSpacingValue) };
            return fmt;
        });
    }

    public void SetSelectionFontSize(double points) => ApplyRunFormatting(f => f with { FontSizePt = points });

    /// <summary>
    /// Apply <paramref name="settings"/> to the document's page geometry in a single undoable step
    /// (AV-PAGE). The command snapshots the prior values and restores them on Undo. Triggers a
    /// relayout so the page chrome (size, margins) updates immediately in Print Layout mode.
    /// </summary>
    public void SetPageSettings(PageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _bus.Execute(new SetPageSettingsCommand(settings));
    }

    /// <summary>
    /// Clone the current page settings, apply a layout mutation, then commit it as one undoable page
    /// setup command. Used by Layout ribbon commands such as Columns.
    /// </summary>
    public void ApplyPageSettings(Action<PageSettings> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);

        var settings = _doc.Page.Clone();
        apply(settings);
        SetPageSettings(settings);
    }

    public void ToggleDifferentFirstPage() =>
        ApplyPageSettings(settings => settings.DifferentFirstPage = !settings.DifferentFirstPage);

    public void ToggleDifferentOddEvenPages() =>
        ApplyPageSettings(settings => settings.DifferentOddEvenPages = !settings.DifferentOddEvenPages);

    public void SetHeaderDistance(double valuePt) =>
        ApplyPageSettings(settings => settings.HeaderDistancePt = Math.Max(0, valuePt));

    public void SetFooterDistance(double valuePt) =>
        ApplyPageSettings(settings => settings.FooterDistancePt = Math.Max(0, valuePt));

    public void EditHeaderFooterSlot(string slotName)
    {
        var slot = HeaderFooterDialogPlanner.ParseSlot(slotName);
        var plan = HeaderFooterDialogPlanner.PlanSlotActivation(slotName, _doc.Page);
        if (plan.Kind != HeaderFooterSlotActivationKind.Active)
            return;

        var store = _doc.FinalSectionHeadersFooters;
        var current = HeaderFooterDialogPlanner.GetSlot(store, slot);
        if (current is null)
        {
            current = new HeaderFooter();
            current.Paragraphs.Add(new Paragraph());
            HeaderFooterDialogPlanner.SetSlot(store, slot, current);
        }
        else if (current.Paragraphs.Count == 0)
        {
            current.Paragraphs.Add(new Paragraph());
        }

        PlaceCaretInHeaderFooter(new HfTarget(SectionIndex: -1, UseFinalSectionStore: true, Slot: ToHfSlot(slot), ParaIdx: 0), 0);
        Focus();
    }

    public void CloseHeaderFooterEditing()
    {
        ExitHeaderFooterCaret();
        Focus();
    }

    public void InsertHeaderFooterPageNumber(bool footer) =>
        MutateDefaultHeaderFooterSlot(
            footer,
            current => HeaderFooterDialogPlanner.AddPageNumberToSlot(current));

    public void InsertHeaderFooterDateTime() =>
        MutateDefaultHeaderFooterSlot(
            footer: false,
            current => HeaderFooterDialogPlanner.AppendFieldDateTimeToSlot(current, "DATE"));

    public void InsertHeaderFooterDocumentInfo() =>
        MutateDefaultHeaderFooterSlot(
            footer: false,
            current => HeaderFooterDialogPlanner.AppendComplexFieldToSlot(current, "FILENAME"));

    public void CyclePageVerticalAlignment() =>
        ApplyPageSettings(settings => settings.VerticalAlignment = settings.VerticalAlignment switch
        {
            PageVerticalAlignment.Top => PageVerticalAlignment.Center,
            PageVerticalAlignment.Center => PageVerticalAlignment.Bottom,
            PageVerticalAlignment.Bottom => PageVerticalAlignment.Justified,
            _ => PageVerticalAlignment.Top,
        });

    private void MutateDefaultHeaderFooterSlot(bool footer, Func<HeaderFooter?, HeaderFooter> mutate)
    {
        var store = _doc.FinalSectionHeadersFooters;
        var slot = footer ? HeaderFooterSlotKind.Footer : HeaderFooterSlotKind.Header;
        var next = mutate(HeaderFooterDialogPlanner.GetSlot(store, slot));
        HeaderFooterDialogPlanner.SetSlot(store, slot, next);
        InvalidateVisual();
        Focus();
    }

    /// <summary>Insert a bordered table (with a header row) after the current block. Cells edit on double-click.</summary>
    public void InsertTable(int rows, int columns)
    {
        if (IsEditingLocked)
            return;

        var table = Table.Create(Math.Max(1, rows), Math.Max(1, columns));
        table.Formatting = TableFormatting.Default with { Borders = true, HeaderRow = true };
        var insertAt = Math.Clamp(_caret.Block + 1, 0, _doc.Blocks.Count);
        _bus.Execute(new InsertBlockCommand(insertAt, table));
    }

    /// <summary>
    /// Convert the current paragraph to a bordered table, splitting text on tabs when present and commas otherwise.
    /// </summary>
    public void ConvertCurrentParagraphToTable()
    {
        if (IsEditingLocked || _doc.Blocks.Count == 0)
            return;

        var block = Math.Clamp(_caret.Block, 0, _doc.Blocks.Count - 1);
        if (_doc.Blocks[block] is not Paragraph paragraph)
            return;

        var delimiter = paragraph.PlainText.Contains('\t', StringComparison.Ordinal) ? '\t' : ',';
        var table = TextTableConvert.TextToTable([paragraph], delimiter);
        table.Formatting = TableFormatting.Default with { Borders = true };
        _bus.Execute(new ReplaceBlocksCommand(block, 1, [table]));
        _cellCaret = (block, 0, 0, 0, 0);
        _hfCaret = null;
        _selectionAnchor = _caret = new DocPosition(block, 0);
        InvalidateLayoutAndVisual();
        CaretMoved?.Invoke();
    }

    // ── AV-INSERT: Insert-tab inserts (page break / picture / shape / text box / symbol) ──────────

    /// <summary>
    /// Insert a page break at the caret: an empty paragraph that forces a page break before it,
    /// placed after the caret's block. Routed through the undo/redo bus, so a single undo removes it.
    /// Mirrors the WPF host's <c>DocumentView.InsertPageBreak</c>.
    /// </summary>
    public void InsertPageBreak()
    {
        if (IsEditingLocked)
            return;

        var insertAt = Math.Clamp(_caret.Block + 1, 0, _doc.Blocks.Count);
        _bus.Execute(new InsertBlockCommand(insertAt, DocumentOps.CreatePageBreak()));
    }

    /// <summary>
    /// Insert a whole blank page after the caret block using the shared Word-compatible page-break pair.
    /// </summary>
    public void InsertBlankPage()
    {
        if (IsEditingLocked)
            return;

        var insertAt = Math.Clamp(_caret.Block + 1, 0, _doc.Blocks.Count);
        var blocks = DocumentOps.BuildBlankPage();
        _bus.BeginUndoGroup();
        for (var i = 0; i < blocks.Count; i++)
            _bus.Execute(new InsertBlockCommand(insertAt + i, blocks[i]));
        _bus.CommitUndoGroup("Insert Blank Page");
    }

    /// <summary>
    /// Insert a horizontal-rule paragraph after the caret block.
    /// </summary>
    public void InsertHorizontalRule()
    {
        if (IsEditingLocked)
            return;

        var insertAt = Math.Clamp(_caret.Block + 1, 0, _doc.Blocks.Count);
        _bus.Execute(new InsertBlockCommand(insertAt, DocumentOps.CreateHorizontalRule()));
    }

    /// <summary>
    /// Insert a column break after the caret block using the shared model command path.
    /// </summary>
    public void InsertColumnBreak()
    {
        if (IsEditingLocked)
            return;

        var insertAt = Math.Clamp(_caret.Block + 1, 0, _doc.Blocks.Count);
        _bus.Execute(new InsertBlockCommand(insertAt, DocumentOps.CreateColumnBreak()));
    }

    /// <summary>
    /// Insert a section break after the caret block, inheriting the current page settings.
    /// </summary>
    public void InsertSectionBreak(SectionBreakKind breakKind)
    {
        if (IsEditingLocked)
            return;

        var insertAt = Math.Clamp(_caret.Block + 1, 0, _doc.Blocks.Count);
        _bus.Execute(new InsertBlockCommand(insertAt, DocumentOps.CreateSectionBreak(breakKind, _doc.Page)));
    }

    /// <summary>
    /// Insert an inline image at the caret's paragraph (AV-INSERT). The image is appended as a textless
    /// object run to the caret's body paragraph (mirroring the WPF host, which adds the image container to
    /// the caret paragraph's inlines). Undoable. When the caret is not in a body paragraph the image is
    /// appended to the nearest editable paragraph (or a new one is created when the body has none).
    /// </summary>
    /// <param name="bytes">Raw image bytes (stored verbatim; never transcoded).</param>
    /// <param name="widthPt">Display width in points.</param>
    /// <param name="heightPt">Display height in points.</param>
    /// <param name="format">Binary format; auto-detected from <paramref name="bytes"/> when null.</param>
    public void InsertInlineImage(byte[] bytes, double widthPt, double heightPt, ImageFormat? format = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var fmt = format ?? InlineImage.DetectFormat(bytes);
        var image = new InlineImage(bytes, Math.Max(1, widthPt), Math.Max(1, heightPt), fmt)
        {
            Wrapping = ImageWrapping.Inline,
        };
        InsertObjectRun(new Run(string.Empty, RunFormatting.Default) { Image = image });
    }

    /// <summary>
    /// Insert a shape at the caret as a floating object (AV-INSERT). The shape is appended as a textless
    /// object run to the caret's body paragraph; its <see cref="Shape.Placement"/> is set so it floats
    /// (square wrap) over the text, matching the WPF host's drawing inserts. Undoable.
    /// </summary>
    public void InsertShape(Shape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        // Make the shape floating (square wrap) so it renders on the floating-object overlay lane.
        shape.Placement ??= new FloatingPlacement();
        if (shape.Placement.Wrapping == ImageWrapping.Inline)
            shape.Placement.Wrapping = ImageWrapping.Square;
        InsertObjectRun(new Run(string.Empty, RunFormatting.Default) { Shape = shape });
    }

    /// <summary>
    /// Insert a default rectangle shape at the caret (AV-INSERT). Convenience over
    /// <see cref="InsertShape(Shape)"/> wired to the <c>freew.shape</c> ribbon command.
    /// </summary>
    public void InsertShape() =>
        InsertShape(Shape.Preset(ShapeKind.Rectangle, widthPt: 120, heightPt: 80, fillColorHex: "#DCE6F1"));

    /// <summary>
    /// Insert a floating text box at the caret (AV-INSERT). Wired to the <c>freew.text-box</c> ribbon
    /// command. The text box carries a single placeholder paragraph and floats over the body text.
    /// </summary>
    public void InsertTextBox() =>
        InsertShape(Shape.TextBoxWith("Text Box", widthPt: 180, heightPt: 90, fillColorHex: "#DCE6F1"));

    /// <summary>
    /// Insert inline decorative WordArt at the caret (AV-INSERT-TEXT). The run uses the shared
    /// <see cref="WordArt"/> model so it round-trips through DOCX and renders via the existing inline
    /// WordArt layout path.
    /// </summary>
    public void InsertWordArt(WordArt? wordArt = null)
    {
        InsertObjectRun(Run.FromWordArt(wordArt ?? WordArt.Create("WordArt", WordArtStyle.GradientFill)));
        Focus();
    }

    /// <summary>
    /// Insert a default inline chart at the caret using the shared chart model/rendering path.
    /// </summary>
    public void InsertChart(Chart? chart = null)
    {
        InsertObjectRun(Run.FromChart(chart ?? Chart.Create(
            ChartKind.Column,
            ["Q1", "Q2", "Q3", "Q4"],
            [4d, 7d, 5d, 9d],
            "Series 1",
            "Chart")));
        Focus();
    }

    /// <summary>
    /// Insert a default inline SmartArt diagram at the caret using the shared SmartArt model/rendering path.
    /// </summary>
    public void InsertSmartArt(SmartArt? smartArt = null)
    {
        InsertObjectRun(Run.FromSmartArt(smartArt ?? SmartArt.Create(
            SmartArtKind.Process,
            ["Plan", "Build", "Review"])));
        Focus();
    }

    /// <summary>
    /// Insert a simple icon glyph through the text/symbol path.
    /// </summary>
    public void InsertIcon() => InsertSymbol("\u2605");

    /// <summary>
    /// Insert a generic embedded object placeholder at the caret (AV-INSERT-TEXT). FreeW preserves the
    /// embedded payload and ProgID in the shared model; live OLE activation is intentionally out of scope.
    /// </summary>
    public void InsertEmbeddedObject(EmbeddedObject? embeddedObject = null)
    {
        InsertObjectRun(Run.FromEmbeddedObject(embeddedObject ?? SampleEmbeddedObject()));
        Focus();
    }

    private static EmbeddedObject SampleEmbeddedObject() =>
        EmbeddedObject.Create(
            System.Text.Encoding.UTF8.GetBytes("FreeW embedded object placeholder."),
            progId: "Package");

    /// <summary>
    /// Insert a symbol / special character at the caret as ordinary text (AV-INSERT). Flows through the
    /// normal text-edit/undo path (<see cref="InsertText"/>), so it works inside table cells too.
    /// Wired to the <c>freew.symbol</c> ribbon command's per-glyph sub-commands.
    /// </summary>
    public void InsertSymbol(string symbol)
    {
        if (!string.IsNullOrEmpty(symbol))
            InsertText(symbol);
    }

    /// <summary>
    /// Enable (create) the document header region if missing/empty so it renders in the top page-margin
    /// region (AV-INSERT). Undoable. Wired to the <c>freew.header</c> ribbon command. In-region caret
    /// editing of the header is a separate UI surface (deferred); this readies the region.
    /// </summary>
    public void EnsureHeader() => _bus.Execute(new EnsureHeaderFooterCommand(isFooter: false));

    /// <summary>
    /// Enable (create) the document footer region if missing/empty so it renders in the bottom
    /// page-margin region (AV-INSERT). Undoable. Wired to the <c>freew.footer</c> ribbon command.
    /// </summary>
    public void EnsureFooter() => _bus.Execute(new EnsureHeaderFooterCommand(isFooter: true));

    // ── AV-REF: References-tab inserts (footnote / endnote / TOC / caption / cross-reference / citation) ──

    /// <summary>
    /// AV-REF: Insert a footnote at the caret. Allocates the next footnote id, stores
    /// <paramref name="text"/> as the note's content in <see cref="TextDocument.Footnotes"/>, and appends a
    /// superscript reference run (carrying <see cref="Run.FootnoteId"/>) to the caret's body paragraph.
    /// Both the note-store mutation and the marker insert run in a single undo group so one Ctrl+Z reverts
    /// the whole insert. Mirrors the WPF host's <c>DocumentView.InsertFootnote</c>.
    /// </summary>
    public void InsertFootnote(string text = "") => InsertNote(text, footnote: true);

    /// <summary>
    /// AV-REF: Insert an endnote at the caret. Mirrors <see cref="InsertFootnote"/> but stores the content
    /// in <see cref="TextDocument.Endnotes"/> (collected at the document end) and the marker carries
    /// <see cref="Run.EndnoteId"/>.
    /// </summary>
    public void InsertEndnote(string text = "") => InsertNote(text, footnote: false);

    // Shared footnote/endnote insert: create the note in the model store and append the matching
    // reference run to the caret's host paragraph, grouped for a single undo.
    private void InsertNote(string text, bool footnote)
    {
        var hostIndex = ResolveReferenceHostBlock();
        if (hostIndex < 0)
            return;

        _bus.BeginUndoGroup();
        var id = footnote ? _doc.NextFootnoteId() : _doc.NextEndnoteId();
        // Seed the note's content store (an empty note when no text is supplied, ready for the user to type).
        _bus.Execute(new AddNoteCommand(id, text ?? string.Empty, footnote));
        var marker = footnote ? Run.FootnoteReference(id) : Run.EndnoteReference(id);
        _bus.Execute(new InsertObjectRunCommand(hostIndex, marker));
        _bus.CommitUndoGroup(footnote ? "Insert Footnote" : "Insert Endnote");

        _cellCaret = null;
        _caret = new DocPosition(hostIndex, BlockLength(hostIndex));
        _selectionAnchor = _caret;
    }

    /// <summary>
    /// AV-REF: Insert a Table of Contents generated from the document's heading outline at (before) the
    /// caret's block — front-matter placement, matching Word. The TOC is built by
    /// <see cref="TableOfContents.Build"/> from Heading-styled paragraphs; each entry paragraph is inserted
    /// through the undo/redo bus (grouped) so the whole TOC reverts in one undo. Mirrors the WPF host's
    /// <c>DocumentView.InsertTableOfContents</c>.
    /// </summary>
    public void InsertTableOfContents()
    {
        TableOfContents.EnsureStyles(_doc);
        var at = Math.Clamp(_caret.Block, 0, _doc.Blocks.Count);
        InsertTocAt(at, "Insert Table of Contents");
    }

    /// <summary>
    /// AV-REF: Rebuild the Table of Contents — remove the previously inserted TOC region (paragraphs
    /// carrying a TOC style, see <see cref="TableOfContents.IsTocParagraph"/>) and re-insert a freshly
    /// generated TOC at the same position. With no existing TOC this behaves like
    /// <see cref="InsertTableOfContents"/>, inserting at the document start. Grouped into one undo.
    /// </summary>
    public void UpdateTableOfContents()
    {
        TableOfContents.EnsureStyles(_doc);

        // Collect the existing TOC paragraphs (the marker region). The first anchors the re-insert point.
        var tocIndices = new List<int>();
        for (var i = 0; i < _doc.Blocks.Count; i++)
            if (TableOfContents.IsTocParagraph(_doc.Blocks[i]))
                tocIndices.Add(i);

        var insertAt = tocIndices.Count > 0 ? tocIndices[0] : 0;

        _bus.BeginUndoGroup();
        // Remove from the end so earlier indices stay valid.
        for (var i = tocIndices.Count - 1; i >= 0; i--)
            _bus.Execute(new DeleteParagraphCommand(tocIndices[i]));
        var index = Math.Clamp(insertAt, 0, _doc.Blocks.Count);
        foreach (var paragraph in TableOfContents.Build(_doc))
            _bus.Execute(new InsertParagraphCommand(index++, paragraph));
        _bus.CommitUndoGroup("Update Table of Contents");
    }

    // Build + insert the TOC paragraphs starting at block `at`, grouped into one undo.
    private void InsertTocAt(int at, string label)
    {
        _bus.BeginUndoGroup();
        var index = Math.Clamp(at, 0, _doc.Blocks.Count);
        foreach (var paragraph in TableOfContents.Build(_doc))
            _bus.Execute(new InsertParagraphCommand(index++, paragraph));
        _bus.CommitUndoGroup(label);
    }

    /// <summary>
    /// AV-REF: Insert an auto-numbered caption paragraph (e.g. "Figure 1: My diagram") of
    /// <paramref name="label"/> with the given <paramref name="text"/> after the caret's block, so it reads
    /// under the selected image/table. The next ordinal is computed by
    /// <see cref="Captions.NextCaptionNumber"/>; the caption is a single <c>Caption</c>-styled paragraph
    /// routed through the undo/redo bus. Mirrors the WPF host's <c>DocumentView.InsertCaption</c>.
    /// </summary>
    public void InsertCaption(CaptionLabel label, string text = "")
    {
        Captions.EnsureStyles(_doc);
        var number = Captions.NextCaptionNumber(_doc, label);
        var caption = Captions.BuildCaption(label, number, text);
        var index = Math.Clamp(_caret.Block + 1, 0, _doc.Blocks.Count);
        _bus.Execute(new InsertParagraphCommand(index, caption));
        _caret = new DocPosition(index, BlockLength(index));
        _selectionAnchor = _caret;
    }

    /// <summary>
    /// AV-REF: Insert a cross-reference field at the caret pointing at <paramref name="target"/> of
    /// <paramref name="type"/>, showing <paramref name="insertAs"/> and optionally as a clickable
    /// hyperlink. For a body target lacking a bookmark anchor, a hidden <c>_Ref…</c> bookmark is added to
    /// the target paragraph so the resulting REF/PAGEREF field resolves (Word auto-bookmarks the same way).
    /// The inserted run carries a cached resolved value so it renders before the next update. Grouped into
    /// one undo. Mirrors the WPF host's <c>DocumentView.InsertCrossReference</c>.
    /// </summary>
    public void InsertCrossReference(CrossRefType type, CrossRefTarget target, CrossRefInsertAs insertAs, bool hyperlink)
    {
        var hostIndex = ResolveReferenceHostBlock();
        if (hostIndex < 0)
            return;

        var sourceBlock = _caret.Block;
        var resolved = target;
        var fieldKind = CrossReferences.FieldKindFor(type, insertAs);

        _bus.BeginUndoGroup();

        // Body targets (REF/PAGEREF) need a bookmark anchor to resolve; ensure one on the target paragraph.
        if (fieldKind != CrossRefFieldKind.NoteRef
            && string.IsNullOrEmpty(resolved.Anchor)
            && resolved.BlockIndex is { } targetBlock
            && targetBlock >= 0 && targetBlock < _doc.Blocks.Count
            && _doc.Blocks[targetBlock] is Paragraph)
        {
            var anchor = EnsureCrossReferenceAnchor(targetBlock);
            resolved = resolved with { Anchor = anchor };
        }

        var field = CrossReferences.BuildField(type, resolved, insertAs, hyperlink);
        var cached = CrossReferences.ResolveText(_doc, type, resolved, insertAs, sourceBlock);
        var run = Run.CrossReferenceFieldRun(field, cached);
        _bus.Execute(new InsertObjectRunCommand(hostIndex, run));
        _bus.CommitUndoGroup("Insert Cross-reference");

        _cellCaret = null;
        _caret = new DocPosition(hostIndex, BlockLength(hostIndex));
        _selectionAnchor = _caret;
    }

    // Returns the target paragraph's existing bookmark name, or assigns a fresh hidden "_Ref<n>" one (the
    // smallest unused index) so a cross-reference field can resolve to it — mirroring Word's auto-bookmarks.
    private string EnsureCrossReferenceAnchor(int blockIndex)
    {
        if (_doc.Blocks[blockIndex] is not Paragraph paragraph)
            return string.Empty;
        if (paragraph.BookmarkName is { Length: > 0 } existing)
            return existing;

        var used = new HashSet<string>(
            _doc.Blocks.OfType<Paragraph>()
                .Select(p => p.BookmarkName)
                .Where(n => n is { Length: > 0 })!,
            StringComparer.Ordinal);
        var index = 1;
        string name;
        do
        {
            name = "_Ref" + index.ToString(CultureInfo.InvariantCulture);
            index++;
        }
        while (used.Contains(name));

        _bus.Execute(new SetBookmarkNameCommand(blockIndex, name));
        return name;
    }

    // ── AV-LINK: hyperlinks + bookmarks (render handled in the glyph loop; follow/navigate here) ─────

    /// <summary>
    /// AV-LINK: Insert (or convert the selection into) a hyperlink. <paramref name="target"/> is either an
    /// absolute web/file URL (an external link, wrapped as <c>w:hyperlink</c> with a relationship on save) or
    /// — when it starts with <c>'#'</c> — the name of a document bookmark (an internal link, wrapped as
    /// <c>w:hyperlink w:anchor</c>). When there is a (single-paragraph) selection it is re-marked as the
    /// hyperlink span (its text is preserved); otherwise <paramref name="displayText"/> (falling back to the
    /// target) is inserted as a new hyperlinked run at the caret. Undoable as one step; re-renders so the link
    /// styling shows immediately. Mirrors the WPF host's Insert &gt; Hyperlink.
    /// </summary>
    public void InsertHyperlink(string displayText, string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return;

        if (!HyperlinkTarget.TryParse(target, out var parsedTarget))
            return;

        var link = new LinkInfo(parsedTarget.Url, parsedTarget.Anchor, null);

        var sel = NormalizedSelection();
        // Only a same-paragraph selection can be wrapped in place; a cross-paragraph (or no) selection
        // inserts fresh hyperlinked text at the caret instead.
        if (sel is { } s && s.Start.Block == s.End.Block
            && _doc.Blocks[s.Start.Block] is Paragraph selPara && IsEditable(selPara)
            && s.End.Offset > s.Start.Offset)
        {
            var block = s.Start.Block;
            var from = s.Start.Offset;
            var to = s.End.Offset;
            var newEnd = to;
            _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
            {
                var cells = ParaCells(p);
                var lo = Math.Clamp(from, 0, cells.Count);
                var hi = Math.Clamp(to, 0, cells.Count);
                var selectedText = string.Concat(cells.Skip(lo).Take(hi - lo).Select(c => c.Ch));
                // Word replaces the selected text with the dialog's Display field when the user changed it;
                // when it is empty or unchanged, only the Link is (re)tagged and the characters are untouched.
                if (!string.IsNullOrEmpty(displayText) && !string.Equals(displayText, selectedText, StringComparison.Ordinal))
                {
                    var fmt = lo < cells.Count ? cells[lo].Fmt : ActiveFormatting(p, lo);
                    var replacement = displayText.Select(ch => new Cell(ch, fmt, Link: link)).ToList();
                    cells.RemoveRange(lo, hi - lo);
                    cells.InsertRange(lo, replacement);
                    newEnd = lo + replacement.Count;
                }
                else
                {
                    for (var i = lo; i < hi; i++)
                        cells[i] = cells[i] with { Link = link };
                }
                SetRuns(p, cells);
            }));
            _caret = new DocPosition(block, newEnd);
            _selectionAnchor = _caret;
            Focus();
            return;
        }

        // No usable selection → insert the display text (or the target itself) as a new hyperlinked run.
        var text = string.IsNullOrEmpty(displayText) ? parsedTarget.DisplayFallback : displayText;
        if (_hfCaret is not null || _cellCaret is not null)
        {
            // Header/footer and table-cell carets do not carry the body-paragraph hyperlink round-trip;
            // fall back to inserting plain display text there (still better than dropping the call).
            InsertText(text);
            return;
        }
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return;

        var atBlock = _caret.Block;
        var atOffset = _caret.Offset;
        var fmt = ActiveFormatting(paragraph, atOffset);
        _bus.Execute(new ReplaceParagraphRunsCommand(atBlock, p =>
        {
            var cells = ParaCells(p);
            var at = Math.Clamp(atOffset, 0, cells.Count);
            foreach (var ch in text)
                cells.Insert(at++, new Cell(ch, fmt, null, RevisionKind.None, null, null, link));
            SetRuns(p, cells);
        }));
        _caret = new DocPosition(atBlock, atOffset + text.Length);
        _selectionAnchor = _caret;
        Focus();
    }

    /// <summary>
    /// AV-LINK: Mark the caret's body paragraph as a bookmark named <paramref name="name"/> (Word's
    /// Insert &gt; Bookmark). Reuses the AV-REF <see cref="SetBookmarkNameCommand"/> so it is undoable and
    /// round-trips. When a selection spans multiple paragraphs the bookmark is placed on the selection's
    /// first paragraph (Word anchors the bookmark at the range start). A no-op for a blank name or when the
    /// caret is not in an editable body paragraph.
    /// </summary>
    public void InsertBookmark(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return;

        var block = NormalizedSelection() is { } sel ? sel.Start.Block : _caret.Block;
        if (block < 0 || block >= _doc.Blocks.Count || _doc.Blocks[block] is not Paragraph)
            return;

        _bus.Execute(new SetBookmarkNameCommand(block, name.Trim()));
        Focus();
    }

    /// <summary>
    /// AV-LINK: Move the caret to the bookmark named <paramref name="name"/> and scroll it into view,
    /// returning true when the bookmark was found. The bookmark target is the body paragraph carrying that
    /// name in its <see cref="Paragraph.BookmarkNames"/> (matched ordinally, ignoring a leading <c>'#'</c>).
    /// Word's Go To / internal-link navigation.
    /// </summary>
    public bool GoToBookmark(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        var target = name.TrimStart('#').Trim();

        foreach (var location in Bookmarks.List(_doc))
        {
            if (!string.Equals(location.Name, target, StringComparison.Ordinal))
                continue;
            var block = location.BlockIndex;
            if (block < 0 || block >= _doc.Blocks.Count)
                return false;
            _cellCaret = null;
            _hfCaret = null;
            _caret = new DocPosition(block, 0);
            _selectionAnchor = _caret;
            Focus();
            InvalidateVisual();
            CaretMoved?.Invoke();
            ScrollToCaretRequested?.Invoke();
            return true;
        }
        return false;
    }

    /// <summary>
    /// AV-LINK: True when the caret sits on an external URL or internal bookmark link.
    /// </summary>
    public bool IsCaretOnHyperlink() => HyperlinksAtCaret().Count > 0;

    /// <summary>The current external URL under the caret, or null when the caret is not on an external link.</summary>
    public string? HyperlinkUrlAtCaret()
    {
        var links = HyperlinksAtCaret();
        return links.Count > 0 ? links[0].Url : null;
    }

    /// <summary>The current ScreenTip under the caret, or null when no linked ScreenTip exists.</summary>
    public string? HyperlinkTooltipAtCaret()
    {
        var links = HyperlinksAtCaret();
        return links.Count > 0 ? links[0].Tooltip : null;
    }

    /// <summary>The bookmark names defined in the document, in document order.</summary>
    public IReadOnlyList<string> BookmarkNames() =>
        Bookmarks.List(_doc).Select(b => b.Name).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>
    /// AV-LINK: Retarget the hyperlink span under the caret, preserving visible text and ScreenTip.
    /// </summary>
    public void EditHyperlink(string newTarget) => EditHyperlink(newTarget, newDisplayText: null);

    /// <summary>
    /// AV-LINK: Retarget the hyperlink span under the caret, preserving its ScreenTip. When
    /// <paramref name="newDisplayText"/> is non-null, non-empty, and differs from the span's current
    /// visible text, the span's characters are rewritten to it (Word's Edit Hyperlink dialog applies an
    /// edited Display-text field); otherwise the existing text is left untouched.
    /// </summary>
    public void EditHyperlink(string newTarget, string? newDisplayText)
    {
        if (!HyperlinkTarget.TryParse(newTarget, out var parsedTarget)
            || !TryFindHyperlinkSpanAtCaret(out var block, out var start, out var end, out var current))
        {
            return;
        }

        var next = new LinkInfo(parsedTarget.Url, parsedTarget.Anchor, current.Tooltip);

        if (!string.IsNullOrEmpty(newDisplayText)
            && _doc.Blocks[block] is Paragraph paragraph
            && !string.Equals(newDisplayText, SpanText(paragraph, start, end), StringComparison.Ordinal))
        {
            var newEnd = end;
            _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
            {
                var cells = ParaCells(p);
                var lo = Math.Clamp(start, 0, cells.Count);
                var hi = Math.Clamp(end, lo, cells.Count);
                var fmt = lo < cells.Count ? cells[lo].Fmt : ActiveFormatting(p, lo);
                var replacement = newDisplayText.Select(ch => new Cell(ch, fmt, Link: next)).ToList();
                cells.RemoveRange(lo, hi - lo);
                cells.InsertRange(lo, replacement);
                newEnd = lo + replacement.Count;
                SetRuns(p, cells);
            }));
            _caret = new DocPosition(block, newEnd);
            _selectionAnchor = _caret;
            Focus();
            return;
        }

        ApplyLinkSpan(block, start, end, _ => next);
    }

    /// <summary>The visible text of paragraph cells [start, end) — used to detect a Display-text edit.</summary>
    private static string SpanText(Paragraph paragraph, int start, int end)
    {
        var cells = ParaCells(paragraph);
        var lo = Math.Clamp(start, 0, cells.Count);
        var hi = Math.Clamp(end, lo, cells.Count);
        return string.Concat(cells.Skip(lo).Take(hi - lo).Select(c => c.Ch));
    }

    /// <summary>
    /// AV-LINK: Remove the hyperlink span under the caret while preserving visible text.
    /// </summary>
    public void RemoveHyperlink()
    {
        if (TryFindHyperlinkSpanAtCaret(out var block, out var start, out var end, out _))
            ApplyLinkSpan(block, start, end, _ => null);
    }

    /// <summary>
    /// AV-LINK: Set or clear the ScreenTip on the hyperlink span under the caret.
    /// </summary>
    public void SetHyperlinkTooltip(string? tip)
    {
        if (!TryFindHyperlinkSpanAtCaret(out var block, out var start, out var end, out var current))
            return;

        var tooltip = string.IsNullOrWhiteSpace(tip) ? null : tip.Trim();
        ApplyLinkSpan(block, start, end, _ => new LinkInfo(current.Url, current.Anchor, tooltip));
    }

    /// <summary>
    /// AV-LINK: Link the current selection, or insert the bookmark name at the caret, as an internal link.
    /// </summary>
    public void ApplyInternalLink(string anchor)
    {
        if (string.IsNullOrWhiteSpace(anchor))
            return;

        var normalized = anchor.Trim().TrimStart('#').Trim();
        if (!HyperlinkTarget.TryParse("#" + normalized, out var parsedTarget))
            return;

        // A non-empty selection keeps its own text (Word links the selection as-is); only fall back to the
        // bookmark name as display text when there is nothing selected to wrap (mirrors InsertHyperlink's
        // own no-selection path).
        var sel = NormalizedSelection();
        var hasWrappableSelection = sel is { } s && s.Start.Block == s.End.Block && s.End.Offset > s.Start.Offset;
        var displayText = hasWrappableSelection ? string.Empty : parsedTarget.DisplayFallback;
        InsertHyperlink(displayText, "#" + parsedTarget.Anchor);
    }

    /// <summary>
    /// AV-LINK: The hyperlink targets covering the caret (or the current selection), for ribbon-state /
    /// tests. Each entry is <c>(Url, Anchor, Tooltip)</c> with exactly one of Url/Anchor set. Empty when the
    /// caret is not on a hyperlink. Reads the live model so it reflects the latest edit.
    /// </summary>
    public IReadOnlyList<(string? Url, string? Anchor, string? Tooltip)> HyperlinksAtCaret()
    {
        if (CurrentParagraph() is not { } paragraph)
            return [];

        var cells = ParaCells(paragraph);
        if (cells.Count == 0)
            return [];

        var sel = NormalizedSelection();
        int lo, hi;
        if (sel is { } s && s.Start.Block == _caret.Block && s.End.Block == _caret.Block)
        {
            lo = Math.Clamp(s.Start.Offset, 0, cells.Count - 1);
            hi = Math.Clamp(s.End.Offset - 1, 0, cells.Count - 1);
        }
        else
        {
            // Collapsed caret: read the character just left of the caret (Word's "on the link" rule).
            lo = hi = Math.Clamp(_caret.Offset - 1, 0, cells.Count - 1);
        }

        var found = new List<(string?, string?, string?)>();
        var seen = new HashSet<LinkInfo>();
        for (var i = lo; i <= hi; i++)
            if (cells[i].Link is { HasTarget: true } link && seen.Add(link))
                found.Add((link.Url, link.Anchor, link.Tooltip));
        return found;
    }

    private bool TryFindHyperlinkSpanAtCaret(out int block, out int start, out int end, out LinkInfo link)
    {
        block = _caret.Block;
        start = 0;
        end = 0;
        link = default;

        if (_cellCaret is not null || _hfCaret is not null || CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return false;

        var cells = ParaCells(paragraph);
        if (cells.Count == 0)
            return false;

        var sel = NormalizedSelection();
        var index = sel is { } s && s.Start.Block == block && s.End.Block == block
            ? Math.Clamp(s.Start.Offset, 0, cells.Count - 1)
            : Math.Clamp(_caret.Offset - 1, 0, cells.Count - 1);

        if (cells[index].Link is not { HasTarget: true } current)
            return false;

        var lo = index;
        while (lo > 0 && cells[lo - 1].Link == current)
            lo--;

        var hi = index + 1;
        while (hi < cells.Count && cells[hi].Link == current)
            hi++;

        start = lo;
        end = hi;
        link = current;
        return true;
    }

    private void ApplyLinkSpan(int block, int start, int end, Func<LinkInfo, LinkInfo?> transform)
    {
        _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
        {
            var cells = ParaCells(p);
            var lo = Math.Clamp(start, 0, cells.Count);
            var hi = Math.Clamp(end, lo, cells.Count);
            for (var i = lo; i < hi; i++)
            {
                if (cells[i].Link is { HasTarget: true } link)
                    cells[i] = cells[i] with { Link = transform(link) };
            }
            SetRuns(p, cells);
        }));

        _caret = new DocPosition(block, Math.Clamp(_caret.Offset, start, end));
        _selectionAnchor = _caret;
        Focus();
    }

    /// <summary>
    /// AV-LINK: Follow the hyperlink at the caret, if any — opening an external URL via
    /// <see cref="HyperlinkActivated"/> or jumping to an internal bookmark via <see cref="GoToBookmark"/>.
    /// Returns true when a link was followed. The keyboard counterpart of Ctrl+Click (used by the ribbon's
    /// Open Hyperlink command / tests).
    /// </summary>
    public bool FollowHyperlinkAtCaret()
    {
        if (HyperlinksAtCaret() is { Count: > 0 } links)
        {
            var (url, anchor, tooltip) = links[0];
            return FollowHyperlink(new LinkInfo(url, anchor, tooltip));
        }
        return false;
    }

    // Follow a resolved link: raise HyperlinkActivated for an external URL, else GoToBookmark for an
    // internal anchor. Returns true when something was followed.
    private bool FollowHyperlink(LinkInfo link)
    {
        if (link.IsExternal)
        {
            HyperlinkActivated?.Invoke(link.Url!);
            return true;
        }
        if (link.IsInternal)
            return GoToBookmark(link.Anchor!);
        return false;
    }

    // Hit-test a point against the placed glyphs and, when the nearest glyph carries a hyperlink, return it.
    // Used by Ctrl+Click follow. Returns false when the point is not over a hyperlinked glyph.
    private bool TryHitTestHyperlink(Point point, out LinkInfo link)
    {
        link = default;
        if (_placed.Count == 0)
            return false;

        PlacedChar? best = null;
        var bestScore = double.MaxValue;
        foreach (var pc in _placed)
        {
            if (pc.Sentinel || !pc.IsHyperlink)
                continue;
            // Only count glyphs the point actually falls within horizontally (a link is a tight target),
            // and use the same vertical-band scoring as TryHitTest so the closest line wins.
            if (point.X < pc.X || point.X > pc.X + pc.W)
                continue;
            var dy = point.Y < pc.Y ? pc.Y - point.Y
                : point.Y > pc.Y + pc.LineHeight ? point.Y - (pc.Y + pc.LineHeight) : 0;
            if (dy > 0)
                continue; // require the point to be on the glyph's line
            var dx = Math.Abs(point.X - (pc.X + pc.W / 2));
            if (dx < bestScore)
            {
                bestScore = dx;
                best = pc;
            }
        }

        if (best is { Link: { HasTarget: true } found })
        {
            link = found;
            return true;
        }
        return false;
    }

    /// <summary>
    /// AV-REF: Insert an in-text citation for <paramref name="source"/> at the caret. Tagged sources become
    /// Word-like <c>CITATION</c> complex-field runs so Update Fields can recompute them; untagged sources
    /// keep the visible plain-text fallback.
    /// </summary>
    public void InsertCitation(Source source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!Citations.TryCreateCitationFieldRun(_doc, source, _doc.BibliographyStyle, out var citationRun))
        {
            InsertText(Citations.FormatInText(_doc, source, _doc.BibliographyStyle));
            return;
        }

        if (IsEditingLocked || _hfCaret is not null || _cellCaret is not null)
        {
            InsertText(citationRun.Text);
            return;
        }

        if (NormalizedSelection() is not null)
            DeleteSelection();
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return;

        var block = _caret.Block;
        var bodyOffset = _caret.Offset;
        var bodyFmt = _pendingRunFmt ?? ActiveFormatting(paragraph, bodyOffset);
        _pendingRunFmt = null;

        var formattedRun = new Run(citationRun.Text, bodyFmt)
        {
            ComplexField = citationRun.ComplexField
        };

        _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
            InsertRunAtOffset(p, bodyOffset, formattedRun)));
        _caret = new DocPosition(block, bodyOffset + formattedRun.Text.Length);
        _selectionAnchor = _caret;
    }

    /// <summary>
    /// AV-REF: Insert a bibliography generated from the document's <see cref="TextDocument.Sources"/> at
    /// (before) the caret's block, else at the document end. The paragraphs carry dedicated bibliography
    /// styles (registered via <see cref="Citations.EnsureStyles"/>). Grouped into one undo. Mirrors the
    /// WPF host's <c>DocumentView.InsertBibliography</c>.
    /// </summary>
    public void InsertBibliography()
    {
        var plan = BibliographyRegionPlanner.BuildInsertPlan(
            _doc,
            Math.Clamp(_caret.Block, 0, _doc.Blocks.Count),
            _doc.BibliographyStyle);
        ApplyGeneratedReferencePlan(plan, "Insert Bibliography", adjustCaretForInsert: true);
    }

    public void RefreshBibliography()
    {
        var plan = BibliographyRegionPlanner.BuildRefreshPlan(_doc, _doc.BibliographyStyle);
        ApplyGeneratedReferencePlan(plan, "Update Bibliography", adjustCaretForInsert: false);
    }

    public void ReplaceSources(IReadOnlyList<Source> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _bus.Execute(new ReplaceSourcesCommand(sources));
        Focus();
    }

    public void MarkIndexEntry(string? term = null)
    {
        var resolved = string.IsNullOrWhiteSpace(term)
            ? SelectedText.Trim()
            : term.Trim();
        if (string.IsNullOrWhiteSpace(resolved))
            resolved = CurrentParagraph()?.PlainText.Trim() ?? string.Empty;
        if (resolved.Length == 0)
            return;

        _bus.Execute(new AddIndexEntryCommand(resolved));
        Focus();
    }

    public void InsertIndex()
    {
        DocumentIndex.EnsureStyles(_doc);
        InsertGeneratedReferenceBlocks(DocumentIndex.Build(_doc), "Insert Index", Math.Clamp(_caret.Block, 0, _doc.Blocks.Count));
    }

    public void RefreshIndex()
    {
        DocumentIndex.EnsureStyles(_doc);
        RefreshGeneratedReferenceBlocks(DocumentIndex.IsIndexParagraph, () => DocumentIndex.Build(_doc), "Update Index");
    }

    public void InsertTableOfFigures(CaptionLabel label = CaptionLabel.Figure)
    {
        TableOfFigures.EnsureStyles(_doc);
        InsertGeneratedReferenceBlocks(TableOfFigures.Build(_doc, label), "Insert Table of Figures", Math.Clamp(_caret.Block, 0, _doc.Blocks.Count));
    }

    public void RefreshTableOfFigures(CaptionLabel label = CaptionLabel.Figure)
    {
        TableOfFigures.EnsureStyles(_doc);
        RefreshGeneratedReferenceBlocks(TableOfFigures.IsTableOfFiguresParagraph, () => TableOfFigures.Build(_doc, label), "Update Table of Figures");
    }

    public void MarkCitation(string? longCitation = null)
    {
        var resolved = string.IsNullOrWhiteSpace(longCitation)
            ? SelectedText.Trim()
            : longCitation.Trim();
        if (string.IsNullOrWhiteSpace(resolved))
            resolved = CurrentParagraph()?.PlainText.Trim() ?? string.Empty;
        MarkCitation(new Citation(resolved));
    }

    public void MarkCitation(Citation citation)
    {
        ArgumentNullException.ThrowIfNull(citation);
        if (citation.LongCitation.Length == 0)
            return;

        var hostIndex = ResolveReferenceHostBlock();
        if (hostIndex < 0)
            return;

        var offset = ReferenceInsertionOffset(hostIndex);
        _bus.Execute(new ReplaceParagraphRunsCommand(hostIndex, paragraph =>
            InsertRunAtOffset(paragraph, offset, Run.CitationMark(citation))));

        _cellCaret = null;
        _caret = new DocPosition(hostIndex, Math.Clamp(offset, 0, BlockLength(hostIndex)));
        _selectionAnchor = _caret;
        Focus();
    }

    public void InsertTableOfAuthorities() => InsertTableOfAuthorities(ToaOptions.Default);

    public void InsertTableOfAuthorities(ToaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var plan = TableOfAuthoritiesRegionPlanner.BuildInsertPlan(
            _doc,
            Math.Clamp(_caret.Block, 0, _doc.Blocks.Count),
            options);
        ApplyGeneratedReferencePlan(plan, "Insert Table of Authorities", adjustCaretForInsert: true);
    }

    public void RefreshTableOfAuthorities() => RefreshTableOfAuthorities(ToaOptions.Default);

    public void RefreshTableOfAuthorities(ToaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var plan = TableOfAuthoritiesRegionPlanner.BuildRefreshPlan(_doc, options);
        ApplyGeneratedReferencePlan(plan, "Update Table of Authorities", adjustCaretForInsert: false);
    }

    public void ShowNotes()
    {
        Focus();
        InvalidateVisual();
    }

    public void ApplyDefaultFootnoteEndnoteOptions()
    {
        Focus();
        InvalidateVisual();
    }

    private void InsertGeneratedReferenceBlocks(IReadOnlyList<Paragraph> paragraphs, string label, int insertAt)
    {
        ArgumentNullException.ThrowIfNull(paragraphs);

        var originalCaret = _caret;
        _bus.BeginUndoGroup();
        var index = Math.Clamp(insertAt, 0, _doc.Blocks.Count);
        var appliedIndex = index;
        foreach (var paragraph in paragraphs)
            _bus.Execute(new InsertParagraphCommand(index++, paragraph));
        _bus.CommitUndoGroup(label);

        if (paragraphs.Count > 0 && appliedIndex <= originalCaret.Block)
        {
            _caret = originalCaret with { Block = originalCaret.Block + paragraphs.Count };
            _selectionAnchor = _caret;
        }
    }

    private void ApplyGeneratedReferencePlan(
        TableOfAuthoritiesRegionPlan plan,
        string label,
        bool adjustCaretForInsert)
    {
        var originalCaret = _caret;
        _bus.BeginUndoGroup();
        foreach (var deleteIndex in plan.DeleteIndicesDescending)
            _bus.Execute(new DeleteParagraphCommand(deleteIndex));

        var index = Math.Clamp(plan.InsertIndex, 0, _doc.Blocks.Count);
        var appliedIndex = index;
        foreach (var paragraph in plan.Paragraphs)
            _bus.Execute(new InsertParagraphCommand(index++, paragraph));
        _bus.CommitUndoGroup(label);

        if (adjustCaretForInsert && plan.Paragraphs.Count > 0 && appliedIndex <= originalCaret.Block)
        {
            _caret = originalCaret with { Block = originalCaret.Block + plan.Paragraphs.Count };
            _selectionAnchor = _caret;
        }
    }

    private void ApplyGeneratedReferencePlan(
        BibliographyRegionPlan plan,
        string label,
        bool adjustCaretForInsert)
    {
        var originalCaret = _caret;
        _bus.BeginUndoGroup();
        foreach (var deleteIndex in plan.DeleteIndicesDescending)
            _bus.Execute(new DeleteParagraphCommand(deleteIndex));

        var index = Math.Clamp(plan.InsertIndex, 0, _doc.Blocks.Count);
        var appliedIndex = index;
        foreach (var paragraph in plan.Paragraphs)
            _bus.Execute(new InsertParagraphCommand(index++, paragraph));
        _bus.CommitUndoGroup(label);

        if (adjustCaretForInsert && plan.Paragraphs.Count > 0 && appliedIndex <= originalCaret.Block)
        {
            _caret = originalCaret with { Block = originalCaret.Block + plan.Paragraphs.Count };
            _selectionAnchor = _caret;
        }
    }

    private void RefreshGeneratedReferenceBlocks(Func<Block, bool> isGeneratedBlock, Func<IReadOnlyList<Paragraph>> build, string label)
    {
        var indices = new List<int>();
        for (var i = 0; i < _doc.Blocks.Count; i++)
            if (isGeneratedBlock(_doc.Blocks[i]))
                indices.Add(i);

        var insertAt = indices.Count > 0 ? indices[0] : 0;

        _bus.BeginUndoGroup();
        for (var i = indices.Count - 1; i >= 0; i--)
            _bus.Execute(new DeleteParagraphCommand(indices[i]));
        var index = Math.Clamp(insertAt, 0, _doc.Blocks.Count);
        foreach (var paragraph in build())
            _bus.Execute(new InsertParagraphCommand(index++, paragraph));
        _bus.CommitUndoGroup(label);
    }

    // ── AV-INSERT2: Insert depth 2 (cover page / drop cap / document-property field / equation / quick part) ──

    /// <summary>
    /// AV-INSERT2: Prepend a cover page using the given <paramref name="preset"/> at the start of the
    /// document — a Title-styled (and optionally Subtitle/date) block layout drawn from
    /// <see cref="TextDocument.Properties"/> (see <see cref="DocumentOps.BuildCoverPage(TextDocument, CoverPagePreset)"/>).
    /// Each block insert is grouped into a single undo so one Ctrl+Z removes the whole cover page. Mirrors
    /// the WPF host's Insert &gt; Cover Page. FreeW models the cover page as a few styled paragraphs (there is
    /// no dedicated cover-page block type); this is the documented approximation.
    /// </summary>
    public void InsertCoverPage(CoverPagePreset preset = CoverPagePreset.Default)
    {
        var blocks = DocumentOps.BuildCoverPage(_doc, preset);
        if (blocks.Count == 0)
            return;

        _bus.BeginUndoGroup();
        for (var i = 0; i < blocks.Count; i++)
            _bus.Execute(new InsertBlockCommand(i, blocks[i]));
        _bus.CommitUndoGroup("Insert Cover Page");

        // Park the caret on the first body block after the cover page so typing continues in the body.
        _cellCaret = null;
        _hfCaret = null;
        var bodyIndex = Math.Clamp(blocks.Count, 0, Math.Max(0, _doc.Blocks.Count - 1));
        _caret = new DocPosition(bodyIndex, 0);
        _selectionAnchor = _caret;
    }

    /// <summary>
    /// AV-INSERT2: Apply a drop cap to the caret's body paragraph — the leading letter is split into its own
    /// enlarged, bold run (see <see cref="DropCap.ApplyDropCap"/>), the remainder keeping its formatting.
    /// Routed through the undo/redo bus (reversible) and re-renders so the enlarged letter shows immediately.
    /// No-op outside an editable body paragraph or on a paragraph with no leading text run. Mirrors the WPF
    /// host's Insert &gt; Drop Cap. The enlarged leading run already renders via the normal run path (font
    /// size is honoured); Word's true margin-float drop-cap geometry is an approximation here.
    /// </summary>
    public void ApplyDropCap(double sizePt = DropCap.DefaultSizePt)
    {
        var index = _caret.Block;
        if (index < 0 || index >= _doc.Blocks.Count || _doc.Blocks[index] is not Paragraph p || !IsEditable(p))
            return;
        _bus.Execute(new ReplaceParagraphRunsCommand(index, para => DropCap.ApplyDropCap(para, sizePt)));
        Focus();
    }

    /// <summary>
    /// AV-INSERT2: Remove a drop cap from the caret's body paragraph: every run's formatting is reset to the
    /// document default (see <see cref="DropCap.ClearFormatting"/>) while its text is preserved. The "None"
    /// option of Word's Drop Cap menu. Undoable; re-renders. No-op outside an editable body paragraph.
    /// </summary>
    public void ClearDropCap()
    {
        var index = _caret.Block;
        if (index < 0 || index >= _doc.Blocks.Count || _doc.Blocks[index] is not Paragraph p || !IsEditable(p))
            return;
        _bus.Execute(new ReplaceParagraphRunsCommand(index, DropCap.ClearFormatting));
        Focus();
    }

    /// <summary>
    /// AV-INSERT2: Insert a document-property / date field at the caret (Word's Insert &gt; Quick Parts &gt;
    /// Document Property / Field). The field run carries <paramref name="kind"/> (e.g.
    /// <see cref="RunFieldKind.Title"/>, <see cref="RunFieldKind.Author"/>, <see cref="RunFieldKind.Date"/>)
    /// and a cached display value resolved from <see cref="TextDocument.Properties"/> so it renders
    /// immediately and round-trips as a <c>w:fldSimple</c>. Appended as an object run to the caret's host
    /// paragraph, undoable. Mirrors the WPF host's <c>DocumentView.InsertField</c>.
    /// </summary>
    public void InsertField(RunFieldKind kind)
    {
        if (kind == RunFieldKind.None)
            return;
        var run = new Run(ResolveDocumentField(kind), RunFormatting.Default) { FieldKind = kind };
        InsertObjectRun(run);
        Focus();
    }

    /// <summary>
    /// Toggle complex field-code display across the document, matching Word's Alt+F9 surface.
    /// </summary>
    public void ToggleFieldCodes()
    {
        var fields = _doc.Blocks
            .OfType<Paragraph>()
            .SelectMany(p => p.Runs)
            .Where(r => r.ComplexField is not null)
            .ToList();
        if (fields.Count == 0)
            return;

        var show = fields.Count(r => r.ComplexField!.ShowCode) * 2 <= fields.Count;
        foreach (var run in fields)
            run.ComplexField = run.ComplexField! with { ShowCode = show };

        InvalidateVisual();
        Focus();
    }

    /// <summary>
    /// Refresh the cached display text for simple and recomputable complex fields.
    /// </summary>
    public void UpdateFields()
    {
        for (var b = 0; b < _doc.Blocks.Count; b++)
        {
            if (_doc.Blocks[b] is not Paragraph paragraph)
                continue;

            for (var r = 0; r < paragraph.Runs.Count; r++)
            {
                var run = paragraph.Runs[r];
                if (run.CrossReference is { } crossReference)
                {
                    var resolved = CrossReferences.ResolveField(_doc, crossReference, run.Text, b);
                    if (!string.IsNullOrEmpty(resolved))
                        run.Text = resolved;
                }
                else if (run.ComplexField is { } complexField)
                {
                    var resolved = ComplexFieldEngine.CanRecompute(complexField)
                        ? ComplexFieldEngine.Recompute(_doc, b, r)
                        : ResolveComplexField(complexField, run.Text);
                    if (!string.IsNullOrEmpty(resolved))
                        run.Text = resolved;
                }
                else if (run.FieldKind != RunFieldKind.None)
                {
                    var resolved = ResolveDocumentField(run.FieldKind);
                    if (!string.IsNullOrEmpty(resolved))
                        run.Text = resolved;
                }
            }
        }

        var refreshedGeneratedRegion = false;
        if (_doc.Blocks.Any(TableOfContents.IsTocParagraph))
        {
            UpdateTableOfContents();
            refreshedGeneratedRegion = true;
        }

        if (_doc.Blocks.Any(Citations.IsBibliographyParagraph))
        {
            RefreshBibliography();
            refreshedGeneratedRegion = true;
        }

        if (TableOfAuthoritiesRegionPlanner.ContainsRegion(_doc))
        {
            var plan = TableOfAuthoritiesRegionPlanner.BuildRefreshPlan(_doc);
            ApplyGeneratedReferencePlan(plan, "Update Table of Authorities", adjustCaretForInsert: false);
            refreshedGeneratedRegion = true;
        }

        if (refreshedGeneratedRegion)
        {
            InvalidateVisual();
            Focus();
            return;
        }

        InvalidateVisual();
        Focus();
    }

    private string ResolveComplexField(ComplexField field, string fallback) =>
        field.Keyword switch
        {
            "DATE" or "TIME" => DateTime.Now.ToString("M/d/yyyy", CultureInfo.InvariantCulture),
            "AUTHOR" => _doc.Properties.Author ?? string.Empty,
            "TITLE" => _doc.Properties.Title ?? string.Empty,
            "FILENAME" => string.Empty,
            "PAGE" or "NUMPAGES" => "1",
            _ => fallback,
        };

    // Resolve a document-property / date field's cached display text (page-independent fields only).
    // Page/NumPages resolve to "1" as a sensible placeholder; the renderer recomputes paginated fields.
    private string ResolveDocumentField(RunFieldKind kind) => kind switch
    {
        RunFieldKind.Date or RunFieldKind.Time =>
            DateTime.Now.ToString("M/d/yyyy", CultureInfo.InvariantCulture),
        RunFieldKind.Author      => _doc.Properties.Author ?? string.Empty,
        RunFieldKind.Title       => _doc.Properties.Title ?? string.Empty,
        RunFieldKind.Subject     => _doc.Properties.Subject ?? string.Empty,
        RunFieldKind.Keywords    => _doc.Properties.Keywords ?? string.Empty,
        RunFieldKind.DocComments => _doc.Properties.Comments ?? string.Empty,
        RunFieldKind.PageNumber or RunFieldKind.NumPages => "1",
        _ => string.Empty,
    };

    /// <summary>
    /// AV-INSERT2: Insert an inline equation at the caret (Word's Insert &gt; Equation). The equation is
    /// carried on a textless object run (<see cref="Run.FromEquation"/>) whose <see cref="Run.Text"/> mirrors
    /// the equation's linear form, so it serialises as an inline <c>m:oMath</c> and renders a readable
    /// stand-in. Appended to the caret's host paragraph, undoable. When <paramref name="equation"/> is null a
    /// default sample (E = mc²) is inserted, matching the WPF host's Equation button.
    /// </summary>
    public void InsertEquation(Equation? equation = null)
    {
        var eq = equation ?? DefaultSampleEquation();
        InsertObjectRun(Run.FromEquation(eq));
        Focus();
    }

    // A sample equation ("E = mc²") whose linear form renders the superscript — the Insert > Equation default.
    private static Equation DefaultSampleEquation()
    {
        var equation = new Equation();
        equation.Runs.Add(MathRun.PlainText("E = m"));
        equation.Runs.Add(MathRun.Superscript("c", "2"));
        return equation;
    }

    /// <summary>
    /// AV-INSERT2: Insert a Quick Part / AutoText snippet's text at the caret as ordinary, editable text
    /// (Word's Insert &gt; Quick Parts). A single-line snippet is inserted in place via the normal
    /// text-edit/undo path (<see cref="InsertText"/>); a multi-line snippet inserts its first line at the
    /// caret and the remaining lines as fresh paragraphs after the caret's block, grouped into one undo. A
    /// null/empty snippet is a no-op. Mirrors the WPF host's Insert Quick Part.
    /// </summary>
    public void InsertQuickPartText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 1)
        {
            InsertText(lines[0]);
            Focus();
            return;
        }

        // Multi-line: first line into the caret paragraph, the rest as new paragraphs after the caret block.
        _bus.BeginUndoGroup();
        InsertText(lines[0]);
        var index = Math.Clamp(_caret.Block + 1, 0, _doc.Blocks.Count);
        for (var i = 1; i < lines.Length; i++)
            _bus.Execute(new InsertParagraphCommand(index++, new Paragraph(lines[i])));
        _bus.CommitUndoGroup("Insert Quick Part");

        var last = Math.Clamp(index - 1, 0, Math.Max(0, _doc.Blocks.Count - 1));
        _caret = new DocPosition(last, BlockLength(last));
        _selectionAnchor = _caret;
        Focus();
    }

    // Resolve the body paragraph that should host a reference marker (footnote/endnote/cross-ref). Prefer
    // the caret's block when it is an editable body paragraph; otherwise the first editable body paragraph;
    // otherwise append a fresh empty paragraph and host it there. Returns -1 only when no paragraph can be
    // created (never, since a fresh one is appended) — defensive.
    private int ResolveReferenceHostBlock()
    {
        var index = _caret.Block;
        if (index >= 0 && index < _doc.Blocks.Count && _doc.Blocks[index] is Paragraph)
            return index;
        index = FirstEditableBlock();
        if (index >= 0)
            return index;
        index = _doc.Blocks.Count;
        _bus.Execute(new InsertBlockCommand(index, new Paragraph()));
        return index;
    }

    private int ReferenceInsertionOffset(int hostIndex)
    {
        if (hostIndex != _caret.Block)
            return BlockLength(hostIndex);

        if (NormalizedSelection() is { } selection
            && selection.Start.Block == hostIndex
            && selection.End.Block == hostIndex)
            return selection.End.Offset;

        return _caret.Offset;
    }

    /// <summary>
    /// Append an object-carrying run to the caret's paragraph (or the nearest editable body paragraph),
    /// routed through the undo/redo bus. Shared by the picture/shape/text-box inserts. Updates the caret
    /// to sit just after the host paragraph's text so subsequent typing lands sensibly.
    /// </summary>
    private void InsertObjectRun(Run run)
    {
        if (IsEditingLocked)
            return;

        // Resolve a body paragraph to host the object. Prefer the caret's block; otherwise the first
        // editable body paragraph; otherwise append a fresh empty paragraph and target that.
        var index = _caret.Block;
        if (index < 0 || index >= _doc.Blocks.Count || _doc.Blocks[index] is not Paragraph p || !IsEditable(p))
        {
            index = FirstEditableBlock();
            if (index < 0)
            {
                index = _doc.Blocks.Count;
                _bus.Execute(new InsertBlockCommand(index, new Paragraph()));
            }
        }

        _bus.Execute(new InsertObjectRunCommand(index, run));
        // Park the caret at the end of the host paragraph's text (object runs carry no text offset).
        _cellCaret = null;
        _caret = new DocPosition(index, BlockLength(index));
        _selectionAnchor = _caret;
    }

    /// <summary>Toggle the current paragraph's list kind (bullet/number); re-applying the same kind clears it.</summary>
    public void ToggleList(ListKind kind)
    {
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return;
        var newKind = paragraph.Formatting.ListKind == kind ? ListKind.None : kind;
        _bus.Execute(new SetParagraphFormattingCommand(_caret.Block, paragraph.Formatting with { ListKind = newKind }));
    }

    // AV-LIST: Tab at the start of a list item (caret offset == 0) demotes (Tab) or promotes
    // (Shift+Tab) the list level. Returns true when the key was consumed; false when the caller
    // should fall through to normal Tab behavior.
    private bool ListTabAtItemStart(bool shift)
    {
        // BS4: Multi-paragraph selection → demote/promote ALL selected list paragraphs (Word behavior).
        // When there is an active selection spanning multiple paragraphs, apply the level change to
        // every list paragraph in the range (skipping non-list paragraphs, matching WPF ChangeListLevel).
        // A single collapsed caret falls through to the existing offset-0 single-item behavior below.
        if (NormalizedSelection() is { } sel)
        {
            // Collect list paragraphs in the selection range.
            var listIndices = new System.Collections.Generic.List<int>();
            for (var i = sel.Start.Block; i <= sel.End.Block && i < _doc.Blocks.Count; i++)
            {
                if (_doc.Blocks[i] is Paragraph lp && IsEditable(lp) && lp.Formatting.ListKind != ListKind.None)
                    listIndices.Add(i);
            }

            if (listIndices.Count == 0)
                return false; // Selection has no list paragraphs → fall through to normal tab/shift-tab.

            // Apply demote (+1) or promote (-1) to every list paragraph in the selection.
            if (listIndices.Count == 1)
            {
                // Single list paragraph in selection: no undo-group overhead.
                var idx = listIndices[0];
                var f = ((Paragraph)_doc.Blocks[idx]).Formatting;
                _bus.Execute(new SetParagraphFormattingCommand(idx, shift
                    ? (f.ListLevel == 0
                        ? f with { ListKind = ListKind.None, ListLevel = 0 }
                        : f with { ListLevel = f.ListLevel - 1 })
                    : f with { ListLevel = Math.Min(f.ListLevel + 1, 8) }));
            }
            else
            {
                // Multiple list paragraphs: wrap in one undo group so a single Ctrl+Z reverts all.
                _bus.BeginUndoGroup();
                foreach (var idx in listIndices)
                {
                    var f = ((Paragraph)_doc.Blocks[idx]).Formatting;
                    _bus.Execute(new SetParagraphFormattingCommand(idx, shift
                        ? (f.ListLevel == 0
                            ? f with { ListKind = ListKind.None, ListLevel = 0 }
                            : f with { ListLevel = f.ListLevel - 1 })
                        : f with { ListLevel = Math.Min(f.ListLevel + 1, 8) }));
                }
                _bus.CommitUndoGroup(shift ? "Promote List Items" : "Demote List Items");
            }
            return true;
        }

        // Single collapsed caret: original behavior — only act when caret is at item start (offset 0).
        if (_caret.Offset != 0)
            return false;
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return false;
        var fmt = paragraph.Formatting;
        if (fmt.ListKind == ListKind.None)
            return false;

        if (shift)
        {
            // Shift+Tab → promote (decrease level).
            if (fmt.ListLevel == 0)
            {
                // Already at level 0: leave the list entirely (Word behavior).
                _bus.Execute(new SetParagraphFormattingCommand(_caret.Block, fmt with { ListKind = ListKind.None, ListLevel = 0 }));
            }
            else
            {
                _bus.Execute(new SetParagraphFormattingCommand(_caret.Block, fmt with { ListLevel = fmt.ListLevel - 1 }));
            }
        }
        else
        {
            // Tab → demote (increase level, cap at 8).
            _bus.Execute(new SetParagraphFormattingCommand(_caret.Block, fmt with { ListLevel = Math.Min(fmt.ListLevel + 1, 8) }));
        }
        return true;
    }

    // AV-LIST: Backspace at the very start of a list item (offset == 0, no selection) →
    // outdent: decrease ListLevel, or remove list formatting entirely when already at level 0.
    // Returns true when the key was consumed; caller should skip normal Backspace.
    private bool BackspaceOutdentListItem()
    {
        if (NormalizedSelection() is not null)
            return false;           // let normal DeleteSelection handle it
        if (_caret.Offset != 0)
            return false;
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return false;
        var fmt = paragraph.Formatting;
        if (fmt.ListKind == ListKind.None)
            return false;

        if (fmt.ListLevel == 0)
        {
            // At top level: remove list formatting entirely.
            _bus.Execute(new SetParagraphFormattingCommand(_caret.Block, fmt with { ListKind = ListKind.None, ListLevel = 0 }));
        }
        else
        {
            _bus.Execute(new SetParagraphFormattingCommand(_caret.Block, fmt with { ListLevel = fmt.ListLevel - 1 }));
        }
        return true;
    }

    /// <summary>Apply a quick paragraph style (font size + weight) to the whole current paragraph.</summary>
    public void ApplyQuickStyle(double fontSizePoints, bool bold)
    {
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return;
        _bus.Execute(new FormatParagraphRunsCommand(
            _caret.Block,
            f => f with { FontSizePt = fontSizePoints, Bold = bold }));
    }

    /// <summary>
    /// AV-STYLES: apply a named built-in style to the current selection / paragraph, model-backed and
    /// undoable. The style is seeded from <see cref="BuiltInStyles"/> if the document's catalog lacks it,
    /// so a freshly-loaded document still resolves the look.
    ///
    /// <para>
    /// <b>Paragraph styles</b> (Normal, Heading 1–4, Title, Subtitle, Quote, Intense Quote, No Spacing,
    /// List Paragraph) set each spanned paragraph's <see cref="Paragraph.StyleId"/> through the reversible
    /// <see cref="SetParagraphStyleCommand"/> (one undo group), so the style's run + paragraph formatting
    /// resolves through <see cref="ResolveRunFmt"/>/<see cref="ResolveParagraphFmt"/> on the next render.
    /// </para>
    /// <para>
    /// <b>Character styles</b> (Strong, Emphasis, Subtle/Intense Emphasis) carry no run-level style id in the
    /// model, so they apply as direct run formatting — the style's set fields are overlaid onto the selected
    /// runs (Word's character-style semantics). With a collapsed caret the format is stored as the pending
    /// run format for the next typed character, mirroring direct character formatting.
    /// </para>
    /// No-op for an unknown style id. Returns the resolved style id (or null when unknown / not applied).
    /// </summary>
    public string? ApplyNamedStyle(string styleId)
    {
        if (string.IsNullOrEmpty(styleId))
            return null;

        // Seed from the built-in catalog when the document does not already define the style, so the
        // StyleId link resolves to real formatting. An existing (possibly customised) definition wins.
        BuiltInStyles.EnsureSeeded(_doc, styleId);
        if (!_doc.Styles.TryGetValue(styleId, out var style))
            return null;

        if (style.Type == StyleType.Character)
        {
            // Character style → overlay the style's run formatting onto the selection's runs.
            // For a CROSS-PARAGRAPH selection (Start.Block != End.Block) ApplyRunFormatting
            // falls into the collapsed-caret branch and only stages a pending format — selected
            // text across blocks is never touched.  Fix: iterate the spanned paragraphs
            // (matching SelectedParagraphIndices) and apply the run transform to each block's
            // SELECTED sub-range, wrapped in one undo group so a single Undo reverts all blocks.
            var sel = NormalizedSelection();
            if (sel is { } s && s.Start.Block != s.End.Block)
            {
                // Multi-paragraph character-style apply.
                var styleRun = style.Run;
                Func<RunFormatting, RunFormatting> transform = f => OverlayCharacterStyle(f, styleRun);

                _bus.BeginUndoGroup();
                for (var blockIdx = s.Start.Block; blockIdx <= s.End.Block && blockIdx < _doc.Blocks.Count; blockIdx++)
                {
                    if (_doc.Blocks[blockIdx] is not Paragraph bp || !IsEditable(bp))
                        continue;

                    // First block: from Start.Offset to the paragraph end.
                    // Middle blocks: entire paragraph (0 to cell count).
                    // Last block: from paragraph start (0) to End.Offset.
                    var a = blockIdx == s.Start.Block ? s.Start.Offset : 0;
                    var b = blockIdx == s.End.Block   ? s.End.Offset   : int.MaxValue;
                    var capturedBlock = blockIdx;
                    var capturedA = a;
                    var capturedB = b;

                    _bus.Execute(new ReplaceParagraphRunsCommand(capturedBlock, p =>
                    {
                        var live = ParaCells(p);
                        var lo = Math.Clamp(capturedA, 0, live.Count);
                        var hi = Math.Clamp(capturedB, 0, live.Count);
                        for (var i = lo; i < hi; i++)
                            live[i] = live[i] with { Fmt = transform(live[i].Fmt) };
                        SetRuns(p, live);
                    }));
                }
                _bus.CommitUndoGroup("Apply Character Style");
            }
            else
            {
                // Single-block selection or collapsed caret: delegate to ApplyRunFormatting which
                // handles both the single-block run-range case and the pending-format caret case.
                ApplyRunFormatting(f => OverlayCharacterStyle(f, style.Run));
            }
            return styleId;
        }

        // Paragraph style → set StyleId on every spanned paragraph (one undoable group).
        var indices = SelectedParagraphIndices();
        if (indices.Count == 0)
            return null;

        if (indices.Count == 1)
        {
            _bus.Execute(new SetParagraphStyleCommand(indices[0], styleId));
        }
        else
        {
            _bus.BeginUndoGroup();
            foreach (var idx in indices)
                _bus.Execute(new SetParagraphStyleCommand(idx, styleId));
            _bus.CommitUndoGroup("Apply Style");
        }
        return styleId;
    }

    // ---- AV-DESIGN: Design-tab document mutations -----------------------------------------------

    /// <summary>
    /// AV-DESIGN: apply a built-in document theme (colour + font scheme) to the style catalog and document
    /// defaults via <see cref="DocumentTheme.Apply"/>, undoable and re-rendered. Mirrors the WPF host's
    /// Design &gt; Themes dropdown.
    /// </summary>
    public void ApplyTheme(DocumentTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _bus.Execute(new DesignCatalogCommand("Apply Theme", doc => DocumentTheme.Apply(doc, theme)));
    }

    /// <summary>
    /// AV-DESIGN: apply only a theme's colour palette (Design &gt; Colors), preserving the current
    /// heading/body fonts. Undoable and re-rendered.
    /// </summary>
    public void ApplyThemeColors(DocumentTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        _bus.Execute(new DesignCatalogCommand("Theme Colors", doc => DocumentTheme.ApplyColors(doc, theme)));
    }

    /// <summary>
    /// AV-DESIGN: apply a Design &gt; Fonts heading/body font pairing to the theme + style catalog,
    /// preserving colours. Undoable and re-rendered.
    /// </summary>
    public void ApplyDocumentFontSet(DocumentFontSet fontSet)
    {
        ArgumentNullException.ThrowIfNull(fontSet);
        _bus.Execute(new DesignCatalogCommand("Theme Fonts", doc => DocumentFontSet.Apply(doc, fontSet)));
    }

    /// <summary>
    /// AV-DESIGN: apply a Design &gt; Paragraph Spacing preset (Compact / Relaxed / Double / …) to the
    /// document default + built-in paragraph styles. Undoable and re-rendered.
    /// </summary>
    public void ApplyParagraphSpacingSet(DocumentParagraphSpacingSet spacingSet)
    {
        ArgumentNullException.ThrowIfNull(spacingSet);
        _bus.Execute(new DesignCatalogCommand("Paragraph Spacing",
            doc => DocumentParagraphSpacingSet.Apply(doc, spacingSet)));
    }

    /// <summary>
    /// AV-DESIGN: apply a Word-style Design &gt; Style Set to the built-in style catalog. Paragraphs retain
    /// their StyleId links and pick up the new look through normal style resolution.
    /// </summary>
    public void ApplyStyleSet(DocumentStyleSet styleSet)
    {
        ArgumentNullException.ThrowIfNull(styleSet);
        _bus.Execute(new DesignCatalogCommand("Style Set", doc => DocumentStyleSet.Apply(doc, styleSet)));
    }

    /// <summary>
    /// Home &gt; Styles &gt; New Style: create a custom paragraph style through the shared
    /// <see cref="StyleManager"/>, then immediately apply it through the normal paragraph-style path.
    /// </summary>
    public DocumentStyle? CreateParagraphStyleAndApply(
        string name,
        string? basedOnId,
        RunFormatting run,
        ParagraphFormatting paragraph,
        string? nextStyleId)
    {
        if (IsEditingLocked)
            return null;

        var targets = SelectedParagraphIndices();
        DocumentStyle? created = null;
        _bus.BeginUndoGroup();
        try
        {
            _bus.Execute(new StyleCatalogCommand("New Style", doc =>
            {
                created = StyleManager.CreateStyle(doc, name, basedOnId, run, paragraph, nextStyleId);
            }));

            if (created is not null)
            {
                foreach (var index in targets)
                    _bus.Execute(new SetParagraphStyleCommand(index, created.Id));
            }

            _bus.CommitUndoGroup("New Style");
        }
        catch
        {
            _bus.AbortUndoGroup();
            throw;
        }

        return created;
    }

    /// <summary>Home &gt; Styles &gt; Manage Styles: modify a style's catalog entry and redraw style-linked text.</summary>
    public DocumentStyle? ModifyParagraphStyle(
        string styleId,
        RunFormatting run,
        ParagraphFormatting paragraph,
        string? basedOnId,
        string? nextStyleId)
    {
        if (IsEditingLocked || string.IsNullOrWhiteSpace(styleId) || !_doc.Styles.ContainsKey(styleId))
            return null;

        DocumentStyle? updated = null;
        _bus.Execute(new StyleCatalogCommand("Modify Style", doc =>
        {
            updated = StyleManager.ModifyStyle(doc, styleId,
                run: run,
                para: paragraph,
                basedOnId: basedOnId,
                clearBasedOn: basedOnId is null,
                nextStyleId: nextStyleId,
                clearNext: nextStyleId is null);
        }));
        return updated;
    }

    /// <summary>Home &gt; Styles &gt; Manage Styles: delete a custom style through the shared catalog rules.</summary>
    public bool DeleteParagraphStyle(string styleId)
    {
        if (IsEditingLocked
            || string.IsNullOrWhiteSpace(styleId)
            || StyleManager.IsBuiltIn(styleId)
            || !_doc.Styles.ContainsKey(styleId))
            return false;

        var deleted = false;
        _bus.Execute(new StyleCatalogCommand("Delete Style", doc => deleted = StyleManager.DeleteStyle(doc, styleId)));
        return deleted;
    }

    /// <summary>
    /// AV-DESIGN: set (or clear) the whole-page background colour (Design &gt; Page Color). A null/empty
    /// value clears it back to the default white sheet; the hex is normalised to "#RRGGBB". Undoable; the
    /// page sheet recolours immediately and round-trips through <c>w:background</c> on save.
    /// </summary>
    public void SetPageColor(string? colorHex) =>
        _bus.Execute(new SetPageColorCommand(NormalizePageColor(colorHex)));

    private static string? NormalizePageColor(string? colorHex)
    {
        if (string.IsNullOrWhiteSpace(colorHex))
            return null;
        var trimmed = colorHex.Trim();
        return trimmed.StartsWith('#') ? trimmed : "#" + trimmed;
    }

    /// <summary>
    /// AV-DESIGN: set (or clear) the page border (Design &gt; Page Borders). Pass null to remove it.
    /// Undoable; the border draws around the page immediately and round-trips through <c>w:pgBorders</c>.
    /// </summary>
    public void SetPageBorder(PageBorder? border) =>
        _bus.Execute(new SetPageBorderCommand(border));

    /// <summary>
    /// AV-DESIGN: toggle the page border on/off with the given colour/width (Design &gt; Page Borders quick
    /// action). When no border is set one is added; otherwise it is cleared. Undoable.
    /// </summary>
    public void TogglePageBorder(string colorHex = "#000000", double widthPt = 1.0) =>
        SetPageBorder(_doc.Page.PageBorder is null ? new PageBorder(colorHex, widthPt) : null);

    /// <summary>
    /// AV-DESIGN: set (or clear) the page watermark with full options (text, font, colour, layout,
    /// opacity). Pass null to remove it. Undoable; the faint diagonal text draws behind the body and
    /// round-trips on save.
    /// </summary>
    public void SetWatermark(WatermarkOptions? options) =>
        _bus.Execute(new SetWatermarkCommand(options));

    /// <summary>
    /// AV-DESIGN: convenience to set a plain-text watermark with sensible defaults (Word's preset
    /// watermarks like CONFIDENTIAL / DRAFT). A null/empty value removes the watermark.
    /// </summary>
    public void SetWatermarkText(string? text) =>
        SetWatermark(string.IsNullOrWhiteSpace(text) ? null : new WatermarkOptions(text.Trim()));

    /// <summary>
    /// AV-STYLES: clear any named paragraph style from the spanned paragraphs (revert to the document
    /// default — Word's "Clear Formatting" / "Normal" reset at the paragraph level), model-backed and
    /// undoable. Equivalent to applying the empty (null) style id via <see cref="SetParagraphStyleCommand"/>.
    /// </summary>
    public void ClearParagraphStyle()
    {
        var indices = SelectedParagraphIndices();
        if (indices.Count == 0)
            return;

        if (indices.Count == 1)
        {
            _bus.Execute(new SetParagraphStyleCommand(indices[0], null));
            return;
        }

        _bus.BeginUndoGroup();
        foreach (var idx in indices)
            _bus.Execute(new SetParagraphStyleCommand(idx, null));
        _bus.CommitUndoGroup("Clear Style");
    }

    // Overlay a character style's run formatting onto a run's existing formatting: only the style's
    // *set* fields win (toggles OR in, optional values override when the style provides one), so a
    // Strong run keeps its font/size/colour and merely turns bold. Mirrors the style-resolution overlay.
    private static RunFormatting OverlayCharacterStyle(RunFormatting baseRun, RunFormatting styleRun) => baseRun with
    {
        Bold          = baseRun.Bold || styleRun.Bold,
        Italic        = baseRun.Italic || styleRun.Italic,
        Underline     = baseRun.Underline || styleRun.Underline,
        Strikethrough = baseRun.Strikethrough || styleRun.Strikethrough,
        SmallCaps     = baseRun.SmallCaps || styleRun.SmallCaps,
        AllCaps       = baseRun.AllCaps || styleRun.AllCaps,
        FontFamily    = styleRun.FontFamily ?? baseRun.FontFamily,
        FontSizePt    = styleRun.FontSizePt ?? baseRun.FontSizePt,
        ColorHex      = styleRun.ColorHex ?? baseRun.ColorHex,
    };

    public void SetSelectionFontFamily(string family) =>
        ApplyRunFormatting(f => f with { FontFamily = string.IsNullOrWhiteSpace(family) ? null : family });

    /// <summary>
    /// Returns the effective run and paragraph formatting at the caret (or at the start of the
    /// selection when there is one). This is the same formatting the ribbon state reflects.
    /// The returned values are already resolved through the style chain so they are suitable for
    /// passing directly to <see cref="RevealFormatting.Describe"/>. Read-only; never mutates the
    /// document.
    /// </summary>
    public (RunFormatting Run, ParagraphFormatting Paragraph) GetCaretFormatting()
    {
        var paragraph = CurrentParagraph();
        if (paragraph is null)
            return (RunFormatting.Default, ParagraphFormatting.Default);

        var cells = ParaCells(paragraph);
        var rawRun = cells.Count == 0
            ? (paragraph.Runs.Count > 0 ? paragraph.Runs[^1].Formatting : RunFormatting.Default)
            : cells[Math.Clamp(_caret.Offset - 1, 0, cells.Count - 1)].Fmt;

        var resolvedRun = ResolveRunFmt(rawRun, paragraph);
        var resolvedParagraph = ResolveParagraphFmt(paragraph);
        return (resolvedRun, resolvedParagraph);
    }

    /// <summary>
    /// Returns the effective run and paragraph formatting for the current selection, scanning ALL
    /// selected cells to detect mixed (indeterminate) properties. Used by the Font dialog to show
    /// indeterminate checkboxes and blank combos when the selection has mixed formatting, matching
    /// Word / WPF parity.
    /// <para>
    /// When there is no selection, behaves identically to <see cref="GetCaretFormatting"/>
    /// (single-cell read, no indeterminate flags).
    /// </para>
    /// </summary>
    public SelectionFormatting GetSelectionFormatting()
    {
        var (run, paragraph) = GetCaretFormatting();

        var sel = NormalizedSelection();
        if (sel is not { } s || s.Start.Block != s.End.Block)
            return new SelectionFormatting(run, paragraph); // no selection or multi-block — no indeterminate

        if (_doc.Blocks[s.Start.Block] is not Paragraph selPara || !IsEditable(selPara))
            return new SelectionFormatting(run, paragraph);

        var allCells = ParaCells(selPara);
        var a = Math.Clamp(s.Start.Offset, 0, allCells.Count);
        var b = Math.Clamp(s.End.Offset, 0, allCells.Count);
        if (b <= a)
            return new SelectionFormatting(run, paragraph);

        var selected = allCells.Skip(a).Take(b - a).ToList();

        // Scan for uniformity.
        var firstFmt = ResolveRunFmt(selected[0].Fmt, selPara);
        var boldMixed       = false;
        var italicMixed     = false;
        var underlineMixed  = false;
        var strikeMixed     = false;
        var familyMixed     = false;
        var sizeMixed       = false;

        foreach (var cell in selected.Skip(1))
        {
            var fmt = ResolveRunFmt(cell.Fmt, selPara);
            if (fmt.Bold        != firstFmt.Bold)        boldMixed      = true;
            if (fmt.Italic      != firstFmt.Italic)      italicMixed    = true;
            if (fmt.Underline   != firstFmt.Underline)   underlineMixed = true;
            if (fmt.Strikethrough != firstFmt.Strikethrough) strikeMixed = true;
            if (fmt.FontFamily  != firstFmt.FontFamily)  familyMixed    = true;
            if (fmt.FontSizePt  != firstFmt.FontSizePt)  sizeMixed      = true;
        }

        return new SelectionFormatting(
            run,
            paragraph,
            BoldIndeterminate:          boldMixed,
            ItalicIndeterminate:        italicMixed,
            UnderlineIndeterminate:     underlineMixed,
            StrikethroughIndeterminate: strikeMixed,
            FamilyIndeterminate:        familyMixed,
            SizeIndeterminate:          sizeMixed);
    }

    // ── Undo-group pass-throughs (used by FontDialog to group all format steps) ─────────────────

    /// <summary>
    /// Begins collecting subsequent command-bus calls into a single undoable group.
    /// Each command still applies immediately. Must be followed by
    /// <see cref="CommitFontUndoGroup"/> or <see cref="AbortFontUndoGroup"/>.
    /// </summary>
    public void BeginFontUndoGroup() => _bus.BeginUndoGroup();

    /// <summary>Commits the current undo group as a single undo step labelled <paramref name="label"/>.</summary>
    public void CommitFontUndoGroup(string label) => _bus.CommitUndoGroup(label);

    /// <summary>Discards the current undo group without pushing onto the undo stack.</summary>
    public void AbortFontUndoGroup() => _bus.AbortUndoGroup();

    /// <summary>Text spanning the current selection (empty when there is no selection).</summary>
    public string SelectedText
    {
        get
        {
            if (NormalizedSelection() is not { } sel)
                return string.Empty;
            if (sel.Start.Block == sel.End.Block && _doc.Blocks[sel.Start.Block] is Paragraph p && IsEditable(p))
            {
                var cells = ParaCells(p);
                var a = Math.Clamp(sel.Start.Offset, 0, cells.Count);
                var b = Math.Clamp(sel.End.Offset, 0, cells.Count);
                return new string(cells.Skip(a).Take(b - a).Select(c => c.Ch).ToArray());
            }

            var sb = new StringBuilder();
            for (var bi = sel.Start.Block; bi <= sel.End.Block && bi < _doc.Blocks.Count; bi++)
            {
                if (bi > sel.Start.Block)
                    sb.Append('\n');
                sb.Append(_doc.Blocks[bi] is Paragraph para ? para.PlainText : string.Empty);
            }

            return sb.ToString();
        }
    }

    public bool TryDeleteSelection()
    {
        if (IsEditingLocked)
            return false;

        if (NormalizedSelection() is null)
            return false;
        DeleteSelection();
        return true;
    }

    public bool PastePlainText(string? clipboardText) =>
        PasteNormalizedText(clipboardText, "Paste Text Only");

    public bool PasteMergeFormatting(string? clipboardText) =>
        PasteNormalizedText(clipboardText, "Merge Formatting");

    private bool PasteNormalizedText(string? clipboardText, string undoLabel)
    {
        if (IsEditingLocked)
            return false;

        var normalized = PasteText.Normalize(clipboardText);
        if (normalized.Length == 0)
            return false;

        var lines = normalized.Split('\n');
        _bus.BeginUndoGroup();
        try
        {
            InsertText(lines[0]);
            for (var i = 1; i < lines.Length; i++)
            {
                InsertParagraphBreak();
                if (lines[i].Length > 0)
                    InsertText(lines[i]);
            }

            _bus.CommitUndoGroup(undoLabel);
        }
        catch
        {
            _bus.AbortUndoGroup();
            throw;
        }

        Focus();
        return true;
    }

    public bool IsFormatPainterArmed => _formatPainter is not null;

    public void ArmFormatPainter(bool locked = false)
    {
        if (IsEditingLocked)
            return;

        var formatting = GetSelectionFormatting();
        _formatPainter = FormatPainterClipboard.Capture(formatting.Run, formatting.Paragraph);
        _formatPainterLocked = locked;
    }

    public void CancelFormatPainter()
    {
        _formatPainter = null;
        _formatPainterLocked = false;
    }

    public bool ApplyFormatPainterToSelection()
    {
        if (_formatPainter is not { } painter || IsEditingLocked || NormalizedSelection() is null)
            return false;

        _bus.BeginUndoGroup();
        try
        {
            ApplyRunFormatting(painter.ApplyTo);
            foreach (var index in SelectedParagraphIndices())
            {
                if (_doc.Blocks[index] is Paragraph paragraph && IsEditable(paragraph))
                    _bus.Execute(new SetParagraphFormattingCommand(index, painter.ApplyTo(paragraph.Formatting)));
            }

            _bus.CommitUndoGroup("Format Painter");
        }
        catch
        {
            _bus.AbortUndoGroup();
            throw;
        }

        if (!_formatPainterLocked)
            CancelFormatPainter();
        Focus();
        return true;
    }

    /// <summary>
    /// Returns the next font size on the standard ladder in the given direction (+1 = grow, -1 = shrink).
    /// Clamps to [1, 1638] (Word's limits). Above the ladder top the step is 8pt.
    /// </summary>
    private static double NextFontSize(double current, int direction)
    {
        if (direction > 0)
        {
            foreach (var s in FontSizeLadder)
                if (s > current + 0.01)
                    return s;
            return Math.Min(current + 8, 1638);
        }
        else
        {
            for (var i = FontSizeLadder.Length - 1; i >= 0; i--)
                if (FontSizeLadder[i] < current - 0.01)
                    return FontSizeLadder[i];
            return Math.Max(current - 8, 1);
        }
    }

    /// <summary>
    /// Applies a character-level text transform to the raw characters in the selection or paragraph.
    /// Used for Change Case operations that need to read each character before writing.
    /// Handles single-block selections, multi-block selections, and the no-selection (whole paragraph) case.
    /// </summary>
    private void ApplyRunFormattingToText(Func<string, string> textTransform)
    {
        var sel = NormalizedSelection();
        if (sel is { } s && s.Start.Block == s.End.Block)
        {
            // Single-block selection: transform only the selected character range.
            var block = s.Start.Block;
            if (_doc.Blocks[block] is not Paragraph p0 || !IsEditable(p0))
                return;
            var a = s.Start.Offset;
            var b = s.End.Offset;
            _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
            {
                var live = ParaCells(p);
                var selectedText = new string(live.Skip(a).Take(b - a).Select(c => c.Ch).ToArray());
                var transformed = textTransform(selectedText);
                for (var i = 0; i < b - a && i < transformed.Length; i++)
                    live[a + i] = live[a + i] with { Ch = transformed[i] };
                SetRuns(p, live);
            }));
        }
        else if (sel is { } ms && ms.Start.Block != ms.End.Block)
        {
            // Multi-block selection: transform the selected portion of each paragraph in range.
            // First block: from Start.Offset to end of paragraph.
            // Interior blocks: entire paragraph.
            // Last block: from start of paragraph to End.Offset.
            for (var bi = ms.Start.Block; bi <= ms.End.Block && bi < _doc.Blocks.Count; bi++)
            {
                if (_doc.Blocks[bi] is not Paragraph para || !IsEditable(para))
                    continue;

                var blockIndex = bi; // capture for lambda
                var isFirst = bi == ms.Start.Block;
                var isLast  = bi == ms.End.Block;
                var from    = isFirst ? ms.Start.Offset : 0;
                // 'to' will be resolved inside the lambda against the live cell list length.
                var toOffset = isLast ? ms.End.Offset : -1; // -1 = end of paragraph

                _bus.Execute(new ReplaceParagraphRunsCommand(blockIndex, p =>
                {
                    var live = ParaCells(p);
                    var effectiveTo = toOffset < 0 ? live.Count : Math.Min(toOffset, live.Count);
                    var effectiveFrom = Math.Min(from, live.Count);
                    if (effectiveTo <= effectiveFrom)
                        return;
                    var segText = new string(live.Skip(effectiveFrom).Take(effectiveTo - effectiveFrom).Select(c => c.Ch).ToArray());
                    var transformed = textTransform(segText);
                    for (var i = 0; i < effectiveTo - effectiveFrom && i < transformed.Length; i++)
                        live[effectiveFrom + i] = live[effectiveFrom + i] with { Ch = transformed[i] };
                    SetRuns(p, live);
                }));
            }
        }
        else if (CurrentParagraph() is { } paragraph && IsEditable(paragraph))
        {
            // No selection: transform the whole current paragraph.
            _bus.Execute(new ReplaceParagraphRunsCommand(_caret.Block, p =>
            {
                var live = ParaCells(p);
                var text = new string(live.Select(c => c.Ch).ToArray());
                var transformed = textTransform(text);
                for (var i = 0; i < live.Count && i < transformed.Length; i++)
                    live[i] = live[i] with { Ch = transformed[i] };
                SetRuns(p, live);
            }));
        }
    }

    /// <summary>Cycles text case: lower → Title Case → UPPER → lower.</summary>
    private string? WordAtCaret()
    {
        if (ProofingWordRangeAtCaret() is not { } range || _doc.Blocks[range.Block] is not Paragraph paragraph)
            return null;

        var cells = ParaCells(paragraph);
        return NormalizeProofingWord(new string(cells.Skip(range.Start).Take(range.End - range.Start).Select(c => c.Ch).ToArray()));
    }

    private (int Block, int Start, int End)? ProofingWordRangeAtCaret()
    {
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return null;

        var cells = ParaCells(paragraph);
        if (cells.Count == 0)
            return null;

        var index = Math.Clamp(_caret.Offset, 0, cells.Count - 1);
        if (!IsProofingWordChar(cells[index].Ch) && index > 0 && IsProofingWordChar(cells[index - 1].Ch))
            index--;
        if (!IsProofingWordChar(cells[index].Ch))
            return null;

        var start = index;
        while (start > 0 && IsProofingWordChar(cells[start - 1].Ch))
            start--;
        var end = index + 1;
        while (end < cells.Count && IsProofingWordChar(cells[end].Ch))
            end++;

        var word = new string(cells.Skip(start).Take(end - start).Select(c => c.Ch).ToArray());
        return NormalizeProofingWord(word) is null ? null : (_caret.Block, start, end);
    }

    private static string? NormalizeProofingWord(string? word)
    {
        return ProofingDiagnosticPlanner.NormalizeWord(word);
    }

    private static bool IsProofingWordChar(char ch) =>
        char.IsLetter(ch) || ch is '\'' or '-' or '\u2019';

    private static string CycleCase(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // Determine current state.
        var isAllLower = text == text.ToLowerInvariant();
        var isAllUpper = text == text.ToUpperInvariant();
        var isTitle = !isAllUpper && text == System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLowerInvariant());

        if (isAllLower)
            return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text);
        if (isTitle)
            return text.ToUpperInvariant();
        return text.ToLowerInvariant();
    }

    private void ApplyRunFormatting(Func<RunFormatting, RunFormatting> transform)
    {
        var sel = NormalizedSelection();
        if (sel is { } s && s.Start.Block == s.End.Block)
        {
            var block = s.Start.Block;
            if (_doc.Blocks[block] is not Paragraph p0 || !IsEditable(p0))
                return;
            var a = s.Start.Offset;
            var b = s.End.Offset;
            _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
            {
                var live = ParaCells(p);
                for (var i = Math.Clamp(a, 0, live.Count); i < Math.Clamp(b, 0, live.Count); i++)
                    live[i] = live[i] with { Fmt = transform(live[i].Fmt) };
                SetRuns(p, live);
            }));
        }
        else if (CurrentParagraph() is { } paragraph && IsEditable(paragraph))
        {
            // BZ5: on a collapsed caret (no selection), store a pending format for the next
            // typed character instead of reformatting every run in the paragraph. Word semantics:
            // a format change at a collapsed caret only affects newly typed text.
            var caretFmt = ActiveFormatting(paragraph, _caret.Offset);
            var newFmt = transform(caretFmt);
            _pendingRunFmt = newFmt;
            // No _bus.Execute here — existing paragraph text is NOT changed.
        }
    }

    private void ToggleRunFlag(Func<RunFormatting, bool> get, Func<RunFormatting, bool, RunFormatting> set)
    {
        var sel = NormalizedSelection();
        if (sel is { } s && s.Start.Block == s.End.Block)
        {
            var block = s.Start.Block;
            if (_doc.Blocks[block] is not Paragraph p0 || !IsEditable(p0))
                return;
            var a = s.Start.Offset;
            var b = s.End.Offset;
            var cells = ParaCells(p0);
            var newValue = !AllSet(cells, a, b, get);
            _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
            {
                var live = ParaCells(p);
                for (var i = Math.Clamp(a, 0, live.Count); i < Math.Clamp(b, 0, live.Count); i++)
                    live[i] = live[i] with { Fmt = set(live[i].Fmt, newValue) };
                SetRuns(p, live);
            }));
        }
        else if (CurrentParagraph() is { } paragraph && IsEditable(paragraph))
        {
            // BZ5: on a collapsed caret, update the pending format for the next typed character
            // instead of reformatting every run in the paragraph.
            var caretFmt = _pendingRunFmt ?? ActiveFormatting(paragraph, _caret.Offset);
            var cells = ParaCells(paragraph);
            // Toggle: if ALL existing cells plus pending have the flag set → clear; else → set.
            var allSetNow = get(caretFmt) && AllSet(cells, 0, cells.Count, get);
            var newValue = !allSetNow;
            _pendingRunFmt = set(caretFmt, newValue);
            // No _bus.Execute here — existing paragraph text is NOT changed.
        }
    }

    private static bool AllSet(IReadOnlyList<Cell> cells, int a, int b, Func<RunFormatting, bool> get)
    {
        var lo = Math.Clamp(a, 0, cells.Count);
        var hi = Math.Clamp(b, 0, cells.Count);
        if (hi <= lo)
            return false;
        for (var i = lo; i < hi; i++)
            if (!get(cells[i].Fmt))
                return false;
        return true;
    }

    // ---- Caret movement -------------------------------------------------------------------------

    private void MoveCaret(int delta, bool extend)
    {
        _pendingRunFmt = null; // BZ5: discard pending format on caret movement
        // AV-TBL: when caret is in a cell, navigate within the cell's paragraph, then cross to
        // adjacent cell paragraphs, then adjacent cells. Cross-row (up/down) navigation is handled
        // by MoveCaretVertical.
        if (_cellCaret is { } cc)
        {
            var newOffset = cc.Offset + delta;
            var para = GetCellParagraph(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx);
            var len = para != null ? ParaCells(para).Count : 0;

            if (newOffset >= 0 && newOffset <= len)
            {
                // Still within the current paragraph.
                _cellCaret = cc with { Offset = newOffset };
            }
            else if (newOffset < 0 && cc.ParaIdx > 0)
            {
                // Move to end of previous paragraph in same cell.
                var prevParaIdx = cc.ParaIdx - 1;
                var prevPara = GetCellParagraph(cc.TableBlock, cc.Row, cc.Col, prevParaIdx);
                var prevLen = prevPara != null ? ParaCells(prevPara).Count : 0;
                _cellCaret = cc with { ParaIdx = prevParaIdx, Offset = prevLen };
            }
            else if (newOffset > len && cc.ParaIdx < (GetCellModel(cc.TableBlock, cc.Row, cc.Col)?.Paragraphs.Count ?? 1) - 1)
            {
                // Move to start of next paragraph in same cell.
                _cellCaret = cc with { ParaIdx = cc.ParaIdx + 1, Offset = 0 };
            }
            else if (newOffset < 0)
            {
                // At start of first paragraph in cell — move to previous cell.
                MoveCaretToAdjacentCell(cc, -1, extend);
                return;
            }
            else
            {
                // At end of last paragraph in cell — move to next cell.
                MoveCaretToAdjacentCell(cc, +1, extend);
                return;
            }

            // Update _caret.Offset to point at the corresponding glyph for TryGetCaretRect.
            var nc = _cellCaret.Value;
            _caret = new DocPosition(nc.TableBlock, FindCellGlyphOffset(nc.TableBlock, nc.Row, nc.Col, nc.ParaIdx, nc.Offset));
            if (!extend) { _selectionAnchor = _caret; _cellAnchor = _cellCaret; }
            InvalidateVisual();
            CaretMoved?.Invoke();
            return;
        }

        // Body paragraph navigation.
        var bodyLen = CurrentLength();
        var bodyNewOffset = _caret.Offset + delta;
        if (bodyNewOffset < 0)
        {
            var prev = PreviousEditableBlock(_caret.Block);
            _caret = prev < 0 ? _caret with { Offset = 0 } : new DocPosition(prev, BlockLength(prev));
        }
        else if (bodyNewOffset > bodyLen)
        {
            var next = NextEditableBlock(_caret.Block);
            _caret = next < 0 ? _caret with { Offset = bodyLen } : new DocPosition(next, 0);
        }
        else
        {
            _caret = _caret with { Offset = bodyNewOffset };
        }

        if (!extend)
            _selectionAnchor = _caret;
        InvalidateVisual();
        CaretMoved?.Invoke();
    }

    // AV-TBL: move caret to the previous (-1) or next (+1) cell in the same row, or across rows.
    private void MoveCaretToAdjacentCell((int TableBlock, int Row, int Col, int ParaIdx, int Offset) cc, int direction, bool extend)
    {
        if (_doc.Blocks.Count <= cc.TableBlock || _doc.Blocks[cc.TableBlock] is not Table table)
            return;

        // Build a flat list of (row, startCol) pairs in reading order.
        var cellOrder = new List<(int Row, int Col)>();
        for (var ri = 0; ri < table.Rows.Count; ri++)
        {
            var col = 0;
            foreach (var cell in table.Rows[ri].Cells)
            {
                cellOrder.Add((ri, col));
                col += Math.Max(1, cell.GridSpan);
            }
        }

        var currentIdx = cellOrder.FindIndex(c => c.Row == cc.Row && c.Col == cc.Col);
        if (currentIdx < 0)
            return;

        var targetIdx = currentIdx + direction;
        if (targetIdx < 0 || targetIdx >= cellOrder.Count)
        {
            // Past first/last cell in table — move to adjacent paragraph block.
            if (direction < 0)
            {
                var prevBlock = PreviousEditableBlock(cc.TableBlock);
                _cellCaret = null;
                _caret = prevBlock < 0 ? new DocPosition(cc.TableBlock, 0) : new DocPosition(prevBlock, BlockLength(prevBlock));
            }
            else
            {
                var nextBlock = NextEditableBlock(cc.TableBlock);
                _cellCaret = null;
                _caret = nextBlock < 0 ? new DocPosition(cc.TableBlock, 0) : new DocPosition(nextBlock, 0);
            }
            if (!extend) { _selectionAnchor = _caret; _cellAnchor = null; }
            InvalidateVisual();
            CaretMoved?.Invoke();
            return;
        }

        var (targetRow, targetCol) = cellOrder[targetIdx];
        var targetCell = GetCellModel(cc.TableBlock, targetRow, targetCol);
        if (targetCell == null)
            return;

        int targetParaIdx, targetOffset;
        if (direction > 0)
        {
            targetParaIdx = 0;
            targetOffset = 0;
        }
        else
        {
            targetParaIdx = Math.Max(0, targetCell.Paragraphs.Count - 1);
            var lastPara = targetCell.Paragraphs.Count > 0 ? targetCell.Paragraphs[targetParaIdx] : null;
            targetOffset = lastPara != null ? ParaCells(lastPara).Count : 0;
        }

        _cellCaret = (cc.TableBlock, targetRow, targetCol, targetParaIdx, targetOffset);
        _caret = new DocPosition(cc.TableBlock, FindCellGlyphOffset(cc.TableBlock, targetRow, targetCol, targetParaIdx, targetOffset));
        if (!extend) { _selectionAnchor = _caret; _cellAnchor = _cellCaret; }
        InvalidateVisual();
        CaretMoved?.Invoke();
    }

    // AV-TBL3: Tab / Shift-Tab cell navigation.
    // Tab  → next cell (left→right, wrap to first cell of next row).
    //         Tab in the last cell of the table appends a new row and moves into its first cell
    //         (Word behaviour).
    // Shift+Tab → previous cell.
    // Never inserts a literal tab character when the caret is in a table.
    private void TabNavigateCell(bool forward)
    {
        if (_cellCaret is not { } cc)
            return;
        if (_doc.Blocks.Count <= cc.TableBlock || _doc.Blocks[cc.TableBlock] is not Table table)
            return;

        // Build a flat reading-order list of (row, gridCol) entries, honouring GridSpan.
        // BH3: Skip VerticalMerge.Continue cells — they are visually part of the merge anchor
        // above them. Tab should only land on Restart/None cells (Word semantics).
        var cellOrder = new List<(int Row, int Col)>();
        for (var ri = 0; ri < table.Rows.Count; ri++)
        {
            var col = 0;
            foreach (var cell in table.Rows[ri].Cells)
            {
                if (cell.VerticalMerge != VerticalMergeState.Continue)
                    cellOrder.Add((ri, col));
                col += Math.Max(1, cell.GridSpan);
            }
        }

        var currentIdx = cellOrder.FindIndex(c => c.Row == cc.Row && c.Col == cc.Col);
        if (currentIdx < 0)
            return;

        var targetIdx = currentIdx + (forward ? 1 : -1);

        if (forward && targetIdx >= cellOrder.Count)
        {
            // Tab in the last cell → append a new row (Word behaviour) and place caret in it.
            _bus.Execute(new InsertTableRowCommand(cc.TableBlock, table.Rows.Count));
            // After insert the table has grown; re-read to find the new last row.
            if (_doc.Blocks[cc.TableBlock] is not Table updatedTable)
                return;
            var newRow = updatedTable.Rows.Count - 1;
            _cellBlockAnchor = null;
            _cellBlockFocus  = null;
            InvalidateLayoutAndVisual();
            PlaceCaretInCell(cc.TableBlock, newRow, 0, 0, 0);
            return;
        }

        if (targetIdx < 0)
        {
            // Shift+Tab at the very first cell — stay put (no wrap before the table start).
            return;
        }

        var (targetRow, targetCol) = cellOrder[targetIdx];
        _cellBlockAnchor = null;
        _cellBlockFocus  = null;
        PlaceCaretInCell(cc.TableBlock, targetRow, targetCol, 0, 0);
    }

    private void MoveToLineEdge(bool toStart, bool extend)
    {
        // AV-TBL: Home/End within a cell moves to start/end of the current cell paragraph.
        if (_cellCaret is { } cc)
        {
            var para = GetCellParagraph(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx);
            var len = para != null ? ParaCells(para).Count : 0;
            var newOffset = toStart ? 0 : len;
            _cellCaret = cc with { Offset = newOffset };
            _cellAnchor = _cellCaret;
            _caret = new DocPosition(cc.TableBlock, FindCellGlyphOffset(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx, newOffset));
            _selectionAnchor = _caret;
            InvalidateVisual();
            CaretMoved?.Invoke();
            return;
        }

        _caret = _caret with { Offset = toStart ? 0 : CurrentLength() };
        if (!extend)
            _selectionAnchor = _caret;
        InvalidateVisual();
        CaretMoved?.Invoke();
    }

    private void MoveCaretVertical(int direction, bool extend)
    {
        _pendingRunFmt = null; // BZ5: discard pending format on caret movement
        if (!TryGetCaretRect(out var rect))
            return;
        var targetY = rect.Y + (direction > 0 ? rect.Height * 1.5 : -rect.Height * 0.5);
        if (TryHitTest(new Point(rect.X, targetY), out var pos))
        {
            _caret = pos;
            if (!extend)
            {
                _selectionAnchor = _caret;
                _cellAnchor = _cellCaret;
            }
            InvalidateVisual();
            CaretMoved?.Invoke();
        }
    }

    // ---- Hit testing / selection ----------------------------------------------------------------

    private bool TryHitTest(Point point, out DocPosition pos)
    {
        pos = _caret;
        if (_placed.Count == 0)
            return false;

        PlacedChar? best = null;
        var bestScore = double.MaxValue;
        foreach (var pc in _placed)
        {
            var dy = point.Y < pc.Y ? pc.Y - point.Y : point.Y > pc.Y + pc.LineHeight ? point.Y - (pc.Y + pc.LineHeight) : 0;
            var dx = Math.Abs(point.X - pc.X);
            var score = dy * 1000 + dx;
            if (score < bestScore)
            {
                bestScore = score;
                best = pc;
            }
        }

        if (best is not { } b)
            return false;

        // AV-TBL: if the best hit is inside a table cell, route into cell editing.
        if (b.IsCell)
        {
            // Snap to the nearer edge of the hit glyph within the cell paragraph.
            var cellOffset = b.CellParaOffset;
            if (!b.Sentinel && point.X > b.X + b.W / 2)
                cellOffset = b.CellParaOffset + 1;

            // Clamp offset to paragraph length.
            var cellPara = GetCellParagraph(b.Block, b.CellRow, b.CellCol, b.CellParaIdx);
            var maxOffset = cellPara != null ? ParaCells(cellPara).Count : 0;
            cellOffset = Math.Clamp(cellOffset, 0, maxOffset);

            // Find the PlacedChar that matches the target cell address+offset so we can use its
            // PlacedChar.Offset as _caret.Offset (needed for TryGetCaretRect lookup).
            var matchingGlyphOffset = FindCellGlyphOffset(b.Block, b.CellRow, b.CellCol, b.CellParaIdx, cellOffset);
            _cellCaret = (b.Block, b.CellRow, b.CellCol, b.CellParaIdx, cellOffset);
            pos = new DocPosition(b.Block, matchingGlyphOffset);
            return true;
        }

        // Body paragraph hit-test (original logic).
        if (_doc.Blocks[b.Block] is not Paragraph paragraph || !IsEditable(paragraph))
        {
            _cellCaret = null;
            return false;
        }

        _cellCaret = null;
        // Snap to the nearer edge of the hit glyph.
        var bodyOffset = b.Offset;
        if (!b.Sentinel && point.X > b.X + b.W / 2)
            bodyOffset = b.Offset + 1;
        pos = new DocPosition(b.Block, Math.Clamp(bodyOffset, 0, BlockLength(b.Block)));
        return true;
    }

    // AV-TBL: find the PlacedChar.Offset value (unique within the table block) for a given cell
    // address + paragraph offset, so _caret.Offset can be set correctly for TryGetCaretRect.
    private int FindCellGlyphOffset(int tableBlock, int row, int col, int paraIdx, int paraOffset)
    {
        // Find the glyph (character or sentinel) at exactly that cell+para+offset.
        // Prefer non-sentinel glyphs; fall back to sentinel if offset == para length.
        PlacedChar? found = null;
        foreach (var pc in _placed)
        {
            if (pc.Block == tableBlock && pc.CellRow == row && pc.CellCol == col && pc.CellParaIdx == paraIdx)
            {
                if (!pc.Sentinel && pc.CellParaOffset == paraOffset)
                {
                    found = pc;
                    break;
                }
                if (pc.Sentinel && pc.CellParaOffset == paraOffset)
                    found = pc; // sentinel match — keep searching for a non-sentinel match
            }
        }
        return found?.Offset ?? (_caret.Block == tableBlock ? _caret.Offset : 0);
    }

    // AV-TBL: retrieve the Paragraph model for a given cell address + paragraph index.
    private Paragraph? GetCellParagraph(int tableBlock, int row, int col, int paraIdx)
    {
        if (tableBlock < 0 || tableBlock >= _doc.Blocks.Count) return null;
        if (_doc.Blocks[tableBlock] is not Table table) return null;
        if (row < 0 || row >= table.Rows.Count) return null;
        var cells = table.Rows[row].Cells;
        // Find the cell whose StartCol matches col (handles merged cells).
        var colIdx = 0;
        foreach (var cell in cells)
        {
            if (colIdx == col)
                return (paraIdx >= 0 && paraIdx < cell.Paragraphs.Count) ? cell.Paragraphs[paraIdx] : null;
            colIdx += Math.Max(1, cell.GridSpan);
        }
        return null;
    }

    // BF5: Expand the raw (TableBlock, MinRow, MinCol, MaxRow, MaxCol) rectangle so that any
    // horizontally-merged cell (GridSpan > 1) whose column span straddles the boundary is fully
    // included. Mirrors Word semantics: a straddling merged cell is included and the effective
    // selection grows to cover the whole merged cell. Iterates until stable (a single pass is
    // usually sufficient; a second pass handles cascaded merges).
    private (int TableBlock, int MinRow, int MinCol, int MaxRow, int MaxCol)
        ExpandForMergedCells((int TableBlock, int MinRow, int MinCol, int MaxRow, int MaxCol) raw)
    {
        if (raw.TableBlock < 0 || raw.TableBlock >= _doc.Blocks.Count)
            return raw;
        if (_doc.Blocks[raw.TableBlock] is not Table table)
            return raw;

        var (block, minRow, minCol, maxRow, maxCol) = raw;
        bool changed;
        do
        {
            changed = false;

            // ── Horizontal expansion ─────────────────────────────────────────────────────────────
            // For every row in the current range, extend minCol/maxCol to fully cover any
            // horizontally merged cell (GridSpan > 1) that straddles the boundary.
            for (var r = minRow; r <= maxRow; r++)
            {
                if (r < 0 || r >= table.Rows.Count) continue;
                var col = 0;
                foreach (var cell in table.Rows[r].Cells)
                {
                    var span = Math.Max(1, cell.GridSpan <= 0 ? 1 : cell.GridSpan);
                    var cellEnd = col + span - 1; // inclusive last column of this cell

                    // Overlap: cell's [col, cellEnd] overlaps selection [minCol, maxCol]?
                    if (col <= maxCol && cellEnd >= minCol)
                    {
                        // Expand the selection to fully include this merged cell.
                        if (col < minCol) { minCol = col; changed = true; }
                        if (cellEnd > maxCol) { maxCol = cellEnd; changed = true; }
                    }
                    col += span;
                }
            }

            // ── Vertical expansion (BG1) ─────────────────────────────────────────────────────────
            // For every grid column in [minCol, maxCol], walk UP from minRow while the cell is a
            // VerticalMerge.Continue (include its Restart head), and walk DOWN from maxRow while
            // the next row's cell is Continue, expanding minRow/maxRow until stable.
            for (var gridCol = minCol; gridCol <= maxCol; gridCol++)
            {
                // Walk UP: if minRow contains a Continue cell, include the Restart head.
                while (minRow > 0)
                {
                    var cell = GetCellModelGridCol(table, minRow, gridCol);
                    if (cell?.VerticalMerge != VerticalMergeState.Continue)
                        break;
                    minRow--;
                    changed = true;
                }

                // Walk DOWN: if the row just below maxRow is Continue, include it.
                while (maxRow + 1 < table.Rows.Count)
                {
                    var cell = GetCellModelGridCol(table, maxRow + 1, gridCol);
                    if (cell?.VerticalMerge != VerticalMergeState.Continue)
                        break;
                    maxRow++;
                    changed = true;
                }
            }
        } while (changed);

        return (block, minRow, minCol, maxRow, maxCol);
    }

    // BH1/BH2: map a GRID column to the Cells list index for a row.
    // TableColumnHelpers.GridColumnToCellIndex is internal to FreeW.Core.Model, so we replicate the
    // same logic here. Returns the index of the first cell whose cumulative span covers gridCol,
    // or -1 if gridCol is beyond the row's total grid width.
    private static int GridColumnToCellIndex(TableRow row, int targetGridCol)
    {
        var gridPos = 0;
        for (var i = 0; i < row.Cells.Count; i++)
        {
            var span = Math.Max(1, row.Cells[i].GridSpan);
            if (targetGridCol < gridPos + span)
                return i;
            gridPos += span;
        }
        return -1;
    }

    // BG1: retrieve a cell by GRID column from a Table instance directly (no block lookup).
    // Returns the first cell whose cumulative grid span covers gridCol, or null if out of range.
    private static TableCell? GetCellModelGridCol(Table table, int row, int gridCol)
    {
        if (row < 0 || row >= table.Rows.Count) return null;
        var colIdx = 0;
        foreach (var cell in table.Rows[row].Cells)
        {
            var span = Math.Max(1, cell.GridSpan);
            if (gridCol >= colIdx && gridCol < colIdx + span)
                return cell;
            colIdx += span;
        }
        return null;
    }

    // AV-TBL: retrieve the TableCell model for a given cell address.
    private TableCell? GetCellModel(int tableBlock, int row, int col)
    {
        if (tableBlock < 0 || tableBlock >= _doc.Blocks.Count) return null;
        if (_doc.Blocks[tableBlock] is not Table table) return null;
        if (row < 0 || row >= table.Rows.Count) return null;
        var colIdx = 0;
        foreach (var cell in table.Rows[row].Cells)
        {
            if (colIdx == col)
                return cell;
            colIdx += Math.Max(1, cell.GridSpan);
        }
        return null;
    }

    private (DocPosition Start, DocPosition End)? NormalizedSelection()
    {
        if (_selectionAnchor is not { } anchor || anchor.Equals(_caret))
            return null;
        return Compare(anchor, _caret) <= 0 ? (anchor, _caret) : (_caret, anchor);
    }

    private static bool IsWithin((DocPosition Start, DocPosition End) sel, int block, int offset)
    {
        var p = new DocPosition(block, offset);
        return Compare(sel.Start, p) <= 0 && Compare(p, sel.End) < 0;
    }

    private static int Compare(DocPosition a, DocPosition b) =>
        a.Block != b.Block ? a.Block.CompareTo(b.Block) : a.Offset.CompareTo(b.Offset);

    // ---- Model helpers --------------------------------------------------------------------------

    /// <summary>
    /// Called by the reviewing pane (and any future consumer) when it mutates the document model
    /// directly outside the command bus — e.g. accept/reject tracked changes. Invalidates the layout
    /// and visual and raises <see cref="DocumentChanged"/> exactly as an in-bus edit does. Note that
    /// direct mutations bypass undo/redo, matching Word's accept/reject semantics.
    /// </summary>
    public void InvalidateAfterExternalMutation()
    {
        ClampCaret();
        InvalidateLayoutAndVisual();
        DocumentChanged?.Invoke();
    }

    /// <summary>
    /// Apply the Document Inspector's selected removal operations to the model and re-render.
    /// Direct mutations bypass undo/redo, matching the existing accept/reject review semantics.
    /// </summary>
    public void ApplyInspectorRemovals(bool comments, bool revisions, bool properties, bool bookmarks)
    {
        if (comments)
            DocumentInspector.RemoveComments(_doc);
        if (revisions)
            DocumentInspector.RemoveRevisions(_doc);
        if (properties)
            DocumentInspector.RemoveProperties(_doc);
        if (bookmarks)
            DocumentInspector.RemoveBookmarks(_doc);

        InvalidateAfterExternalMutation();
    }

    private void OnModelChanged()
    {
        InvalidateLayoutAndVisual();
        DocumentChanged?.Invoke();
    }

    private void InvalidateLayoutAndVisual()
    {
        _laidOutWidth = -1;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void ClampCaret()
    {
        // AV-TBL: clear cell caret on undo/redo to avoid stale cell addresses.
        _cellCaret = null;
        _cellAnchor = null;
        // AV-TBL2: also clear cross-cell block selection (indices may have shifted after mutation).
        _cellBlockAnchor = null;
        _cellBlockFocus  = null;
        if (_caret.Block >= _doc.Blocks.Count)
            _caret = new DocPosition(Math.Max(0, _doc.Blocks.Count - 1), 0);
        _caret = _caret with { Offset = Math.Clamp(_caret.Offset, 0, CurrentLength()) };
        _selectionAnchor = _caret;
        InvalidateVisual();
    }

    private Paragraph? CurrentParagraph() =>
        _caret.Block >= 0 && _caret.Block < _doc.Blocks.Count ? _doc.Blocks[_caret.Block] as Paragraph : null;

    private int CurrentLength() => BlockLength(_caret.Block);

    private int BlockLength(int block) =>
        block >= 0 && block < _doc.Blocks.Count && _doc.Blocks[block] is Paragraph p && IsEditable(p)
            ? ParaCells(p).Count
            : 0;

    private int FirstEditableBlock()
    {
        for (var i = 0; i < _doc.Blocks.Count; i++)
            if (_doc.Blocks[i] is Paragraph p && IsEditable(p))
                return i;
        return 0;
    }

    private int NextEditableBlock(int from)
    {
        for (var i = from + 1; i < _doc.Blocks.Count; i++)
            if (_doc.Blocks[i] is Paragraph p && IsEditable(p))
                return i;
        return -1;
    }

    private int PreviousEditableBlock(int from)
    {
        for (var i = from - 1; i >= 0; i--)
            if (_doc.Blocks[i] is Paragraph p && IsEditable(p))
                return i;
        return -1;
    }

    /// <summary>Char-level editing only on paragraphs whose runs are all plain text (no images/fields/controls).</summary>
    private bool IsEditable(Paragraph paragraph) =>
        !IsEditingLocked && IsPlainTextEditable(paragraph);

    private static bool IsPlainTextEditable(Paragraph paragraph) =>
        // AV-COMMENT: a CommentId is a soft run mark (like a hyperlink) — it must NOT make the paragraph
        // non-editable, or its glyphs would fall back to FallbackCells (which drops the comment id and the
        // anchor render). Word keeps commented text fully editable. The textless comment-reference run has
        // empty text and contributes no cells, so it does not affect editability either.
        paragraph.Runs.All(r => r.Image is null && r.Equation is null && r.FieldKind == RunFieldKind.None
            && r.FootnoteId is null && r.EndnoteId is null && r.Control is null);

    private static List<Cell> ParaCells(Paragraph paragraph)
    {
        var cells = new List<Cell>();
        foreach (var run in paragraph.Runs)
        {
            // AV-LINK: capture the run's hyperlink target so the link span survives the cell round-trip and
            // SetRuns can re-segment runs on a hyperlink boundary. null when the run carries no link.
            var link = run.HyperlinkUrl is { Length: > 0 } || run.HyperlinkAnchor is { Length: > 0 }
                ? new LinkInfo(run.HyperlinkUrl, run.HyperlinkAnchor, run.HyperlinkTooltip)
                : (LinkInfo?)null;
            foreach (var ch in run.Text)
                // AV-COMMENT: carry the run's CommentId so commented ranges survive the cell round-trip
                // (layout + edit). Textless comment-reference runs contribute no cells, as before.
                // AV-TRACKEDIT: also carry the run's tracked-change mark so recorded revisions survive the
                // round-trip (and SetRuns can re-segment runs on a revision boundary).
                cells.Add(new Cell(ch, run.Formatting, run.CommentId, run.Revision, run.RevisionAuthor, run.RevisionDateXml, link, run.FormatRevision));
        }
        return cells;
    }

    private static List<Cell> FallbackCells(string text)
    {
        var cells = new List<Cell>(text.Length);
        foreach (var ch in text)
            cells.Add(new Cell(ch, RunFormatting.Default));
        return cells;
    }

    private static void InsertRunAtOffset(Paragraph paragraph, int offset, Run insertedRun)
    {
        var targetOffset = Math.Clamp(offset, 0, paragraph.PlainText.Length);
        var consumed = 0;
        for (var i = 0; i < paragraph.Runs.Count; i++)
        {
            var run = paragraph.Runs[i];
            var runLength = run.Text.Length;
            if (targetOffset > consumed + runLength)
            {
                consumed += runLength;
                continue;
            }

            var local = targetOffset - consumed;
            if (local <= 0)
            {
                paragraph.Runs.Insert(i, insertedRun);
            }
            else if (local >= runLength)
            {
                paragraph.Runs.Insert(i + 1, insertedRun);
            }
            else
            {
                var before = CloneRunWithText(run, run.Text[..local]);
                var after = CloneRunWithText(run, run.Text[local..]);
                paragraph.Runs.RemoveAt(i);
                paragraph.Runs.Insert(i, before);
                paragraph.Runs.Insert(i + 1, insertedRun);
                paragraph.Runs.Insert(i + 2, after);
            }
            return;
        }

        paragraph.Runs.Add(insertedRun);
    }

    private static Run CloneRunWithText(Run source, string text) => new(text, source.Formatting)
    {
        Image = source.Image,
        Equation = source.Equation,
        Shape = source.Shape,
        WordArt = source.WordArt,
        Chart = source.Chart,
        EmbeddedObject = source.EmbeddedObject,
        SmartArt = source.SmartArt,
        PreservedDrawing = source.PreservedDrawing,
        DrawingGroup = source.DrawingGroup,
        HyperlinkUrl = source.HyperlinkUrl,
        HyperlinkAnchor = source.HyperlinkAnchor,
        HyperlinkTooltip = source.HyperlinkTooltip,
        FieldKind = source.FieldKind,
        TableFormula = source.TableFormula,
        Citation = source.Citation,
        CrossReference = source.CrossReference,
        ComplexField = source.ComplexField,
        FootnoteId = source.FootnoteId,
        EndnoteId = source.EndnoteId,
        CommentId = source.CommentId,
        IsCommentReference = source.IsCommentReference,
        IsPageBreak = source.IsPageBreak,
        Revision = source.Revision,
        Control = source.Control,
        RevisionAuthor = source.RevisionAuthor,
        RevisionDateXml = source.RevisionDateXml,
        FormatRevision = source.FormatRevision
    };

    private static void SetRuns(Paragraph paragraph, IReadOnlyList<Cell> cells)
    {
        var citationMarks = TextlessRunPositions(paragraph)
            .Where(item => item.Run.Citation is not null)
            .ToList();

        // AV-COMMENT: preserve which comment ids had a textless reference run (they carry no cells, so the
        // cell round-trip would otherwise drop them). Re-emitted after the run is last anchored below so the
        // w:commentReference survives an edit inside a commented paragraph.
        var referencedComments = paragraph.Runs
            .Where(r => r.IsCommentReference && r.CommentId is not null)
            .Select(r => r.CommentId!.Value)
            .ToHashSet();

        paragraph.Runs.Clear();
        var lastAnchorIndexFor = new Dictionary<int, int>();
        var i = 0;
        while (i < cells.Count)
        {
            var fmt = cells[i].Fmt;
            // AV-COMMENT: also break runs on a comment-id boundary so the anchoring CommentId is preserved
            // across edits (a run is one contiguous run of equal Fmt AND equal CommentId).
            var commentId = cells[i].CommentId;
            // AV-TRACKEDIT: a run is also broken on a tracked-change boundary so recorded insertions /
            // deletions (and their author/date) survive the cell round-trip (a run is one contiguous run of
            // equal Fmt AND CommentId AND Revision mark).
            var revision = cells[i].Revision;
            var revisionAuthor = cells[i].RevisionAuthor;
            var revisionDateXml = cells[i].RevisionDateXml;
            // AV-LINK: a run is also one contiguous span of equal hyperlink target, so an inserted/edited
            // hyperlink survives the cell round-trip and re-emits as a w:hyperlink-wrapped run on save.
            var link = cells[i].Link;
            var formatRevision = cells[i].FormatRevision;
            var start = i;
            while (i < cells.Count
                   && cells[i].Fmt.Equals(fmt)
                   && cells[i].CommentId == commentId
                   && cells[i].Revision == revision
                   && cells[i].RevisionAuthor == revisionAuthor
                   && cells[i].RevisionDateXml == revisionDateXml
                   && cells[i].Link == link
                   && cells[i].FormatRevision == formatRevision)
                i++;
            var text = new string(cells.Skip(start).Take(i - start).Select(c => c.Ch).ToArray());
            paragraph.Runs.Add(new Run(text, fmt)
            {
                CommentId = commentId,
                Revision = revision,
                RevisionAuthor = revisionAuthor,
                RevisionDateXml = revisionDateXml,
                HyperlinkUrl = link?.Url,
                HyperlinkAnchor = link?.Anchor,
                HyperlinkTooltip = link?.Tooltip,
                FormatRevision = formatRevision,
            });
            if (commentId is { } cid)
                lastAnchorIndexFor[cid] = paragraph.Runs.Count - 1;
        }

        // Re-append a comment-reference run just after each comment's last anchored run, for every comment
        // that still has anchored text and previously carried a reference. Insert from the rightmost anchor
        // first so earlier insert positions stay valid.
        foreach (var cid in referencedComments
                     .Where(lastAnchorIndexFor.ContainsKey)
                     .OrderByDescending(cid => lastAnchorIndexFor[cid]))
        {
            paragraph.Runs.Insert(lastAnchorIndexFor[cid] + 1, Run.CommentReference(cid));
        }

        foreach (var (offset, run) in citationMarks.OrderBy(item => item.Offset))
            InsertRunAtOffset(paragraph, offset, CloneRunWithText(run, string.Empty));
    }

    private static List<(int Offset, Run Run)> TextlessRunPositions(Paragraph paragraph)
    {
        var positions = new List<(int Offset, Run Run)>();
        var offset = 0;
        foreach (var run in paragraph.Runs)
        {
            if (run.Text.Length == 0)
                positions.Add((offset, run));
            else
                offset += run.Text.Length;
        }

        return positions;
    }

    /// <summary>
    /// Resolve a run's effective display formatting by cascading the paragraph's named style under it
    /// (run override wins; then the style chain's Run values; then the document default size). Display-only —
    /// the model runs stay raw so the StyleId link round-trips on save.
    /// </summary>
    private RunFormatting ResolveRunFmt(RunFormatting raw, Paragraph paragraph)
    {
        var styleRun = RunFormatting.Default;
        var hasStyle = false;
        foreach (var style in StyleChain(paragraph.StyleId))
        {
            styleRun = OverlayRun(styleRun, style.Run);
            hasStyle = true;
        }

        return hasStyle ? OverlayRun(styleRun, raw) with
        {
            FontSizePt = raw.FontSizePt ?? styleRun.FontSizePt ?? _doc.DefaultRun.FontSizePt,
        } : raw;
    }

    /// <summary>Cascade the paragraph's named-style paragraph formatting (alignment + spacing)
    /// under the paragraph's own values; the paragraph's explicit values win.</summary>
    private ParagraphFormatting ResolveParagraphFmt(Paragraph paragraph)
    {
        var styleParagraph = ParagraphFormatting.Default;
        var hasStyle = false;
        foreach (var style in StyleChain(paragraph.StyleId))
        {
            styleParagraph = OverlayParagraph(styleParagraph, style.Paragraph);
            hasStyle = true;
        }

        if (!hasStyle)
            return paragraph.Formatting;

        return paragraph.Formatting with
        {
            Alignment = paragraph.Formatting.Alignment == TextAlignment.Left
                ? styleParagraph.Alignment
                : paragraph.Formatting.Alignment,
            SpaceBeforePt = paragraph.Formatting.SpaceBeforeIsSet
                ? paragraph.Formatting.SpaceBeforePt
                : styleParagraph.SpaceBeforePt,
            SpaceBeforeIsSet = paragraph.Formatting.SpaceBeforeIsSet || styleParagraph.SpaceBeforeIsSet,
            SpaceAfterPt = paragraph.Formatting.SpaceAfterIsSet
                ? paragraph.Formatting.SpaceAfterPt
                : styleParagraph.SpaceAfterPt,
            SpaceAfterIsSet = paragraph.Formatting.SpaceAfterIsSet || styleParagraph.SpaceAfterIsSet,
        };
    }

    private IEnumerable<DocumentStyle> StyleChain(string? styleId)
    {
        if (string.IsNullOrEmpty(styleId))
            yield break;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var chain = new List<DocumentStyle>();
        var id = styleId;
        while (!string.IsNullOrEmpty(id) && seen.Add(id) && _doc.Styles.TryGetValue(id, out var style))
        {
            chain.Add(style);
            id = style.BasedOnStyleId;
        }

        for (var i = chain.Count - 1; i >= 0; i--)
            yield return chain[i];
    }

    private static RunFormatting OverlayRun(RunFormatting baseRun, RunFormatting over) => baseRun with
    {
        FontFamily = over.FontFamily ?? baseRun.FontFamily,
        FontSizePt = over.FontSizePt ?? baseRun.FontSizePt,
        ColorHex = over.ColorHex ?? baseRun.ColorHex,
        HighlightColorHex = over.HighlightColorHex ?? baseRun.HighlightColorHex,
        CharacterBorder = over.CharacterBorder ?? baseRun.CharacterBorder,
        CharacterShadingHex = over.CharacterShadingHex ?? baseRun.CharacterShadingHex,
        CharacterShadingPattern = over.CharacterShadingHex is not null
            ? over.CharacterShadingPattern
            : baseRun.CharacterShadingPattern,
        LanguageTag = over.LanguageTag ?? baseRun.LanguageTag,
        VerticalAlign = over.VerticalAlign != VerticalAlign.Baseline ? over.VerticalAlign : baseRun.VerticalAlign,
        Rtl = baseRun.Rtl || over.Rtl,
        CharacterSpacingPt = over.CharacterSpacingPt != 0 ? over.CharacterSpacingPt : baseRun.CharacterSpacingPt,
        KerningMinSizePt = over.KerningMinSizePt ?? baseRun.KerningMinSizePt,
        PositionPt = over.PositionPt != 0 ? over.PositionPt : baseRun.PositionPt,
        Ligatures = over.Ligatures != LigatureMode.None ? over.Ligatures : baseRun.Ligatures,
        NumberForm = over.NumberForm != NumberForm.Default ? over.NumberForm : baseRun.NumberForm,
        NumberSpacing = over.NumberSpacing != NumberSpacing.Default ? over.NumberSpacing : baseRun.NumberSpacing,
        StylisticSet = over.StylisticSet ?? baseRun.StylisticSet,
        Bold = baseRun.Bold || over.Bold,
        Italic = baseRun.Italic || over.Italic,
        Underline = baseRun.Underline || over.Underline,
        Strikethrough = baseRun.Strikethrough || over.Strikethrough,
        SmallCaps = baseRun.SmallCaps || over.SmallCaps,
        AllCaps = baseRun.AllCaps || over.AllCaps,
    };

    private static ParagraphFormatting OverlayParagraph(ParagraphFormatting baseParagraph, ParagraphFormatting over) => baseParagraph with
    {
        Alignment = over.Alignment == TextAlignment.Left ? baseParagraph.Alignment : over.Alignment,
        SpaceBeforePt = over.SpaceBeforeIsSet ? over.SpaceBeforePt : baseParagraph.SpaceBeforePt,
        SpaceBeforeIsSet = baseParagraph.SpaceBeforeIsSet || over.SpaceBeforeIsSet,
        SpaceAfterPt = over.SpaceAfterIsSet ? over.SpaceAfterPt : baseParagraph.SpaceAfterPt,
        SpaceAfterIsSet = baseParagraph.SpaceAfterIsSet || over.SpaceAfterIsSet,
    };

    private static RunFormatting ActiveFormatting(Paragraph paragraph, int offset)
    {
        var cells = ParaCells(paragraph);
        if (cells.Count == 0)
            return paragraph.Runs.Count > 0 ? paragraph.Runs[^1].Formatting : RunFormatting.Default;
        var index = Math.Clamp(offset - 1, 0, cells.Count - 1);
        return cells[index].Fmt;
    }

    // AV-LINK: the hyperlink a character typed at <paramref name="offset"/> should inherit — i.e. the link
    // only when the insertion point is strictly INSIDE a contiguous link span (the chars on both sides share
    // it). This extends a hyperlink when typing within it (matching Word) without extending it when typing at
    // its trailing edge. Returns null at a paragraph edge or outside any link.
    private static LinkInfo? ActiveLink(Paragraph paragraph, int offset)
    {
        var cells = ParaCells(paragraph);
        if (offset <= 0 || offset >= cells.Count)
            return null;
        var left = cells[offset - 1].Link;
        var right = cells[offset].Link;
        return left is { HasTarget: true } && left == right ? left : null;
    }

    // ---- Text shaping helpers -------------------------------------------------------------------

    private FormattedText Build(string text, RunFormatting fmt)
    {
        var typeface = new Typeface(
            fmt.FontFamily is { Length: > 0 } family ? new FontFamily(family) : FontFamily.Default,
            fmt.Italic ? FontStyle.Italic : FontStyle.Normal,
            fmt.Bold ? FontWeight.Bold : FontWeight.Normal);

        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            FontSizePx(fmt),
            BrushFor(fmt.ColorHex));
    }

    private static double FontSizePx(RunFormatting fmt) => (fmt.FontSizePt ?? DefaultFontSizePt) * PxPerPoint;

    private IBrush BrushFor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Brushes.Black;
        if (_brushCache.TryGetValue(hex, out var brush))
            return brush;
        try
        {
            brush = new SolidColorBrush(Color.Parse(hex));
        }
        catch (FormatException)
        {
            brush = Brushes.Black;
        }

        _brushCache[hex] = brush;
        return brush;
    }

    private static IBrush SelectionBrush { get; } = new SolidColorBrush(Color.FromArgb(0x55, 0x33, 0x99, 0xFF));

    // ── AV-COMMENT: comment-anchor render assets ──────────────────────────────────────────────────
    // Light amber tint behind commented glyphs (active threads) and a muted grey tint for resolved ones.
    private static IBrush CommentTintBrush { get; } = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xC1, 0x07));
    private static IBrush ResolvedCommentTintBrush { get; } = new SolidColorBrush(Color.FromArgb(0x1F, 0x9E, 0x9E, 0x9E));
    // Amber underline drawn under commented glyphs — the in-text anchor mark.
    private static readonly Pen CommentUnderlinePen =
        new(new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)), 1.5);
    private static readonly Pen ResolvedCommentUnderlinePen =
        new(new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0x9E)), 1.0);
    // Right-margin comment marker fill (balloon) — amber bracket aligned to the anchor line.
    private static IBrush CommentMarkerBrush { get; } = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
    private static IBrush ResolvedCommentMarkerBrush { get; } = new SolidColorBrush(Color.FromRgb(0xBD, 0xBD, 0xBD));

    // AV-TBL2: overlay brush for rectangular cross-cell block selection (slightly deeper than glyph selection).
    private static IBrush CellBlockSelectionBrush { get; } = new SolidColorBrush(Color.FromArgb(0x66, 0x33, 0x99, 0xFF));

    // ── AV-TRACKEDIT: tracked-change render assets ────────────────────────────────────────────────
    // Word's default single-author revision colour is a deep red/maroon. Tracked insertions draw in this
    // colour and underlined; tracked deletions draw in this colour and struck through.
    private static Color RevisionColor { get; } = Color.FromRgb(0xC0, 0x00, 0x4B);
    private const string RevisionColorHex = "#C0004B";
    private static IBrush RevisionBrush { get; } = new SolidColorBrush(RevisionColor);
    private static readonly Pen RevisionInsertUnderlinePen = new(RevisionBrush, 1.0);
    private static readonly Pen RevisionDeleteStrikePen = new(RevisionBrush, 1.0);
    private static readonly Pen FormatRevisionUnderlinePen = new(RevisionBrush, 1.0, new DashStyle([1, 2], 0));
    private static readonly Pen SimpleMarkupChangeBarPen = new(RevisionBrush, 2.0);
    private static readonly Pen ProofingSquigglePen = new(new SolidColorBrush(Color.FromRgb(0xD1, 0x34, 0x38)), 1.15);

    // ── AV-LINK: hyperlink render colour ──────────────────────────────────────────────────────────
    // Word's default hyperlink character-style colour (a medium blue). A hyperlinked run with no explicit
    // colour of its own renders in this colour + underlined.
    private const string HyperlinkColorHex = "#0563C1";

    // AV-COMMENT: CommentId carries the anchoring review-comment id (null = not commented) so the
    // glyph layout can mark commented ranges. Defaulted so existing Cell(ch, fmt) construction is unchanged.
    // AV-TRACKEDIT: Revision/RevisionAuthor/RevisionDateXml carry a per-character tracked-change mark so
    // recorded insertions/deletions survive the cell round-trip (ParaCells → edit → SetRuns). Defaulted to
    // an un-tracked character so all existing Cell(ch, fmt[, commentId]) construction is unchanged.
    private readonly record struct Cell(
        char Ch,
        RunFormatting Fmt,
        int? CommentId = null,
        RevisionKind Revision = RevisionKind.None,
        string? RevisionAuthor = null,
        string? RevisionDateXml = null,
        // AV-LINK: the run's hyperlink target (external URL / internal bookmark anchor) + ScreenTip, carried
        // per-character so a hyperlink span survives the cell round-trip (ParaCells → edit → SetRuns) and so
        // SetRuns re-segments runs on a hyperlink boundary. null = this glyph is not inside a hyperlink.
        LinkInfo? Link = null,
        FormatRevision? FormatRevision = null);

    /// <summary>
    /// AV-LINK: a hyperlink target carried alongside a glyph/run. Exactly one of <see cref="Url"/> (external)
    /// or <see cref="Anchor"/> (internal bookmark) is meaningful; <see cref="Tooltip"/> is the optional
    /// ScreenTip. Mirrors <see cref="Run.HyperlinkUrl"/>/<see cref="Run.HyperlinkAnchor"/>/<see cref="Run.HyperlinkTooltip"/>.
    /// </summary>
    internal readonly record struct LinkInfo(string? Url, string? Anchor, string? Tooltip)
    {
        public bool IsExternal => !string.IsNullOrEmpty(Url);
        public bool IsInternal => !string.IsNullOrEmpty(Anchor);
        public bool HasTarget => IsExternal || IsInternal;
    }

    private readonly record struct DocPosition(int Block, int Offset);

    private readonly record struct PlacedChar(
        int Block,
        int Offset,
        double X,
        double Y,
        double W,
        double LineHeight,
        RunFormatting Fmt,
        char Ch,
        bool Sentinel,
        // AV-TBL: cell address (-1 = not in a table cell)
        int CellRow = -1,
        int CellCol = -1,
        int CellParaIdx = -1,
        int CellParaOffset = -1,
        // AV-COMMENT: anchoring review-comment id (null = this glyph is not inside a comment range).
        int? CommentId = null,
        // AV-TRACKEDIT: tracked-change mark on this glyph so the render can colour/underline insertions and
        // strike deletions. None for ordinary text.
        RevisionKind Revision = RevisionKind.None,
        // AV-LINK: the hyperlink target this glyph belongs to (null = not a hyperlink), so the render can
        // style it (blue + underline) and the pointer hit-test can follow it on Ctrl+Click.
        LinkInfo? Link = null,
        bool HasFormatRevision = false)
    {
        /// <summary>True when this glyph is inside a table cell (as opposed to a body paragraph).</summary>
        public bool IsCell => CellRow >= 0;

        /// <summary>True when this glyph is part of a hyperlink span.</summary>
        public bool IsHyperlink => Link is { HasTarget: true };

        /// <summary>True when this glyph is covered by a review comment's anchored range.</summary>
        public bool IsCommented => CommentId is not null;

        /// <summary>True when this glyph is part of a tracked insertion.</summary>
        public bool IsInsertedRevision => Revision == RevisionKind.Inserted;

        /// <summary>True when this glyph is part of a tracked deletion (kept and struck).</summary>
        public bool IsDeletedRevision => Revision == RevisionKind.Deleted;
    }

    private sealed class ViewContext(DocumentView view) : IDocumentCommandContext
    {
        public TextDocument Document => view._doc;
    }

    // ── AV-HFEDIT: header/footer slot identity + edit target ──────────────────────────────────────

    /// <summary>
    /// Identifies one of the six header/footer slots of a section's <see cref="SectionHeadersFooters"/>.
    /// </summary>
    internal enum HfSlot
    {
        Header,
        Footer,
        FirstHeader,
        FirstFooter,
        EvenHeader,
        EvenFooter,
    }

    /// <summary>
    /// Fully-qualifies an editable header/footer paragraph: which section's HF store, which slot, and the
    /// paragraph index within that slot. <see cref="SectionIndex"/> is an index into <c>_doc.Sections</c>;
    /// <see cref="UseFinalSectionStore"/> is true when the target is the document-level final-section store
    /// (<see cref="TextDocument.FinalSectionHeadersFooters"/>), which the document-level Header/Footer views
    /// alias. Mirrors the <c>_cellCaret</c> address tuple but for the HF store.
    /// </summary>
    internal readonly record struct HfTarget(int SectionIndex, bool UseFinalSectionStore, HfSlot Slot, int ParaIdx);

    /// <summary>
    /// Resolves an <see cref="HfTarget"/> to the live <see cref="SectionHeadersFooters"/> store it addresses,
    /// or null when the section index is out of range.
    /// </summary>
    private SectionHeadersFooters? ResolveHfStore(HfTarget target)
    {
        if (target.UseFinalSectionStore)
            return _doc.FinalSectionHeadersFooters;
        if (target.SectionIndex < 0 || target.SectionIndex >= _doc.Sections.Count)
            return _doc.FinalSectionHeadersFooters;
        return _doc.Sections[target.SectionIndex].HeadersFooters;
    }

    /// <summary>Returns the <see cref="HeaderFooter"/> slot for a target, or null when the slot is empty/unset.</summary>
    private static HeaderFooter? GetHfSlot(SectionHeadersFooters store, HfSlot slot) => slot switch
    {
        HfSlot.Header      => store.Header,
        HfSlot.Footer      => store.Footer,
        HfSlot.FirstHeader => store.FirstHeader,
        HfSlot.FirstFooter => store.FirstFooter,
        HfSlot.EvenHeader  => store.EvenHeader,
        HfSlot.EvenFooter  => store.EvenFooter,
        _                  => null,
    };

    /// <summary>Resolves a target's <see cref="Paragraph"/> model, or null when unavailable.</summary>
    private Paragraph? GetHfParagraph(HfTarget target)
    {
        var store = ResolveHfStore(target);
        if (store is null)
            return null;
        var hf = GetHfSlot(store, target.Slot);
        if (hf is null || target.ParaIdx < 0 || target.ParaIdx >= hf.Paragraphs.Count)
            return null;
        return hf.Paragraphs[target.ParaIdx];
    }

    // ── HF: header/footer render item ─────────────────────────────────────────────────────────────

    /// <summary>One pre-computed line to draw in a header or footer band.</summary>
    private sealed class HfRenderItem
    {
        /// <summary>Text to draw (already field-resolved).</summary>
        public string Text = string.Empty;
        /// <summary>Run formatting for the text.</summary>
        public RunFormatting Fmt = RunFormatting.Default;
        /// <summary>Top-left X in page-space coordinates.</summary>
        public double X;
        /// <summary>Top Y in page-space coordinates.</summary>
        public double Y;
        /// <summary>Available width for alignment.</summary>
        public double AvailableWidth;
        /// <summary>Paragraph alignment for this line.</summary>
        public TextAlignment Alignment;

        // ── AV-HFEDIT: editing back-reference + offset mapping ────────────────────────────────────
        /// <summary>
        /// The header/footer target this rendered line belongs to (which section slot + paragraph index),
        /// or null when the line is not editable (defensive — every emitted line carries a target).
        /// </summary>
        public HfTarget? Target;
        /// <summary>Line height (DIP) used for the editing region band + caret height.</summary>
        public double LineHeight;
        /// <summary>
        /// Model-text offset (index into the paragraph's literal plain text) at which this segment's
        /// displayed text begins. A click X inside the segment maps to ModelStartOffset + (chars before X).
        /// </summary>
        public int ModelStartOffset;
    }

    // ── AV-NOTERENDER: footnote / endnote render item ─────────────────────────────────────────────────

    /// <summary>One pre-computed footnote/endnote text fragment to draw (absolute page-space position).</summary>
    private sealed class NoteRenderItem
    {
        /// <summary>Text to draw (note number prefix or a wrapped word/segment).</summary>
        public string Text = string.Empty;
        /// <summary>Run formatting (note body ~9pt, or superscript for the number prefix).</summary>
        public RunFormatting Fmt = RunFormatting.Default;
        /// <summary>Top-left X in page-space coordinates.</summary>
        public double X;
        /// <summary>Top Y in page-space coordinates.</summary>
        public double Y;
    }

    // ── Floating shape data captured during layout ────────────────────────────────────────────────
    // Stores everything needed to draw a floating shape in Render() without re-touching the model.

    private sealed class FloatingShapeData
    {
        public Rect Rect;           // page-space bounding rect
        public bool BehindText;     // true → draw before body text; false → draw after
        public int ZOrder;
        // AV-FLSEL: model location so hit-test can issue commands.
        public int BlockIndex;
        public int RunIndex;

        // Geometry
        public ShapeKind Kind;
        public CustomGeometry? CustomGeo;  // non-null for freeform shapes (overrides Kind)

        // Fill
        public IBrush? FillBrush;          // null → no fill

        // Outline
        public Pen? OutlinePen;             // null → no stroke

        // Text (optional)
        public string? Text;               // plain text to centre inside the shape

        // Rotation / flip (in degrees; 0 → no rotation)
        public double RotationAngle;
        public bool FlipH;
        public bool FlipV;

        public DrawingObjectEffectsPlan Effects = DrawingObjectEffectsPlan.None;
    }

    // ── FO3: data classes for floating charts, WordArt, SmartArt, and drawing groups ───────────────

    private sealed class FloatingChartData
    {
        public Rect         Rect;
        public bool         BehindText;
        public int          ZOrder;
        // AV-FLSEL: model location so hit-test can issue commands.
        public int BlockIndex;
        public int RunIndex;
        public ChartKind    Kind;
        public string?      Title;
        public List<string> Categories = [];
        public List<(string? Name, List<double> Values)> Series = [];
        // AV-POLISH: chart annotation fields
        public ChartVisualGeometryKind GeometryKind;
        public bool    ShowLegend;
        public bool    ShowGridlines;
        public bool    PlotAreaFill;
        public bool    ShowDataLabels;
        public string? CategoryAxisTitle;
        public string? ValueAxisTitle;
        public List<Color> Palette = [];
    }

    private sealed class FloatingWordArtData
    {
        public Rect         Rect;
        public bool         BehindText;
        public int          ZOrder;
        // AV-FLSEL: model location so hit-test can issue commands.
        public int BlockIndex;
        public int RunIndex;
        public string       Text        = string.Empty;
        public WordArtStyle Style;
        public double       FontSizePt  = 36;
        public WordArtWarp  Warp;
    }

    private sealed class FloatingSmartArtData
    {
        public Rect             Rect;
        public bool             BehindText;
        public int              ZOrder;
        // AV-FLSEL: model location so hit-test can issue commands.
        public int BlockIndex;
        public int RunIndex;
        public SmartArtKind     Kind;
        public string           LayoutId = "list1";
        public SmartArtStyle    Style = SmartArtStyle.Default;
        // Flattened node texts (first-level nodes + their children depth-first).
        public List<string>     NodeTexts = [];
        public List<SmartArtNodeVisualPlan> NodePlans = [];
        public List<Color>      NodeFills = [];
        public Color            NodeTextColor = Colors.White;
    }

    private sealed class FloatingGroupChildData
    {
        // Resolved page-space sub-rect for this child (group origin + child offset).
        public Rect Rect;
        public int ChildIndex;
        // What kind of child: Image, Shape, Chart, WordArt, SmartArt.
        public enum ChildKind { Image, Shape, Chart, WordArt, SmartArt }
        public ChildKind Kind;
        // Reused data structs (only the relevant one is non-null):
        public Bitmap?           Bitmap;    // Image
        public FloatingShapeData? Shape;    // Shape
        public FloatingChartData? Chart;    // Chart
        public FloatingWordArtData? WordArt; // WordArt
        public FloatingSmartArtData? SmartArt; // SmartArt
    }

    private sealed class FloatingGroupData
    {
        public Rect Rect;
        public bool BehindText;
        public int  ZOrder;
        // AV-FLSEL: model location so hit-test can issue commands.
        public int BlockIndex;
        public int RunIndex;
        public List<FloatingGroupChildData> Children = [];
    }

    // ── FO3 collection helpers ────────────────────────────────────────────────────────────────────

    private static FloatingWordArtData BuildFloatingWordArtData(
        DrawingObjectVisualPlan plan,
        int blockIndex = -1,
        int runIndex = -1) =>
        new()
        {
            Rect = ToAvaloniaRect(plan.Rect),
            BehindText = plan.BehindText,
            ZOrder = plan.ZOrderIndex,
            BlockIndex = blockIndex,
            RunIndex = runIndex,
            Text = plan.WordArt?.Text ?? string.Empty,
            Style = plan.WordArt?.Style ?? WordArtStyle.FillBlue,
            FontSizePt = (plan.WordArt?.FontSizeDip ?? 48) / PxPerPoint,
            Warp = plan.WordArt?.Warp ?? WordArtWarp.None,
        };

    private static FloatingSmartArtData BuildFloatingSmartArtData(
        SmartArt smartArt,
        Rect rect,
        bool behindText,
        int zOrder,
        int blockIndex = -1,
        int runIndex = -1)
    {
        var plan = ChartSmartArtVisualPlanner.BuildSmartArtPlan(smartArt);

        return new FloatingSmartArtData
        {
            Rect = rect,
            BehindText = behindText,
            ZOrder = zOrder,
            BlockIndex = blockIndex,
            RunIndex = runIndex,
            Kind = smartArt.Kind,
            LayoutId = plan.LayoutId,
            Style = plan.Style,
            NodeTexts = plan.Nodes.Select(n => n.Text).ToList(),
            NodePlans = plan.Nodes.ToList(),
            NodeFills = plan.Nodes.Select(n => ToAvaloniaChartColor(n.FillHex)).ToList(),
            NodeTextColor = plan.Nodes.Count > 0
                ? ToAvaloniaChartColor(plan.Nodes[0].TextHex)
                : ToAvaloniaChartColor(plan.ColorScheme.TextHex),
        };
    }

    private FloatingGroupData BuildFloatingGroupData(
        FreeW.Core.Model.DrawingGroup group,
        DocumentFloatingObjectSnapshot snapshot)
    {
        var children = new List<FloatingGroupChildData>();
        var planChildren = DrawingObjectVisualPlanner.BuildVisualPlan(group, snapshot)
            .GroupChildren
            .ToDictionary(child => child.ChildIndex);
        foreach (var childSnapshot in DocumentViewLayoutPlanner.BuildFloatingGroupChildSnapshots(group, snapshot.Rect))
        {
            if (childSnapshot.ChildIndex < 0 || childSnapshot.ChildIndex >= group.Children.Count)
                continue;

            var child = group.Children[childSnapshot.ChildIndex];
            var childRect = planChildren.TryGetValue(childSnapshot.ChildIndex, out var planChild)
                ? ToAvaloniaRect(planChild.Visual.Rect)
                : ToAvaloniaRect(childSnapshot.Rect);
            var childData = new FloatingGroupChildData
            {
                Rect = childRect,
                ChildIndex = childSnapshot.ChildIndex
            };

            switch (childSnapshot.Kind)
            {
                case DocumentFloatingObjectKind.Image when child is InlineImage img:
                    childData.Kind = FloatingGroupChildData.ChildKind.Image;
                    childData.Bitmap = DecodeBitmap(img);
                    break;

                case DocumentFloatingObjectKind.Shape when child is Shape:
                    if (!planChildren.TryGetValue(childSnapshot.ChildIndex, out var shapePlan))
                        continue;
                    childData.Kind = FloatingGroupChildData.ChildKind.Shape;
                    childData.Shape = BuildFloatingShapeData(shapePlan.Visual);
                    break;

                case DocumentFloatingObjectKind.Chart when child is Chart chart:
                    childData.Kind = FloatingGroupChildData.ChildKind.Chart;
                    childData.Chart = BuildChartData(
                        chart,
                        childRect,
                        snapshot.BehindText,
                        snapshot.ZOrderIndex,
                        chart.Series.Select(s => (s.Name, new List<double>(s.Values))).ToList());
                    break;

                case DocumentFloatingObjectKind.WordArt when child is WordArt:
                    if (!planChildren.TryGetValue(childSnapshot.ChildIndex, out var wordArtPlan))
                        continue;
                    childData.Kind = FloatingGroupChildData.ChildKind.WordArt;
                    childData.WordArt = BuildFloatingWordArtData(wordArtPlan.Visual);
                    break;

                case DocumentFloatingObjectKind.SmartArt when child is SmartArt smartArt:
                    childData.Kind = FloatingGroupChildData.ChildKind.SmartArt;
                    childData.SmartArt = BuildFloatingSmartArtData(smartArt, childRect, snapshot.BehindText, snapshot.ZOrderIndex);
                    break;

                default:
                    continue;
            }

            children.Add(childData);
        }

        return new FloatingGroupData
        {
            Rect = ToAvaloniaRect(snapshot.Rect),
            BehindText = snapshot.BehindText,
            ZOrder = snapshot.ZOrderIndex,
            BlockIndex = snapshot.BlockIndex,
            RunIndex = snapshot.RunIndex,
            Children = children,
        };
    }

    // Colour palette for chart series — matches Word's default colorful1 scheme.
    private static readonly IBrush ChartFrameFill    = new SolidColorBrush(Color.FromArgb(0xFF, 0xF9, 0xF9, 0xF9));
    private static readonly Pen    ChartFramePen     = new Pen(new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)), 1.0);
    private static readonly IBrush ChartGridlineBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00));
    private static readonly IBrush ChartLegendBg    = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));

    private static Color ToAvaloniaChartColor(string? hex) =>
        TryParseAvaloniaColor(hex, out var color) ? color : Color.FromRgb(0x44, 0x72, 0xC4);

    private static Color ChartColorAt(FloatingChartData chart, int index) =>
        chart.Palette.Count > 0 ? chart.Palette[index % chart.Palette.Count] : ToAvaloniaChartColor("#4472C4");

    private static Color SmartArtFillAt(FloatingSmartArtData smartArt, int index) =>
        smartArt.NodeFills.Count > 0 ? smartArt.NodeFills[index % smartArt.NodeFills.Count] : ToAvaloniaChartColor("#4E81BD");

    private static SmartArtNodeVisualPlan? SmartArtPlanAt(FloatingSmartArtData smartArt, int index) =>
        index >= 0 && index < smartArt.NodePlans.Count ? smartArt.NodePlans[index] : null;

    /// <summary>
    /// Centralised factory for <see cref="FloatingChartData"/> — shared by the floating chart collector,
    /// the inline chart layout, and the drawing-group child collector. Resolves QuickLayout / StyleId
    /// to determine which annotation flags are active, then copies chart model data into the data struct.
    /// </summary>
    private static FloatingChartData BuildChartData(
        Chart chart, Rect rect, bool behindText, int zOrder,
        List<(string? Name, List<double> Values)> series)
    {
        var plan = ChartSmartArtVisualPlanner.BuildChartPlan(chart);

        return new FloatingChartData
        {
            Rect              = rect,
            BehindText        = behindText,
            ZOrder            = zOrder,
            Kind              = chart.Kind,
            GeometryKind      = plan.GeometryKind,
            Title             = plan.ShowTitle ? chart.Title : null,
            Categories        = new List<string>(chart.Categories),
            Series            = series,
            ShowLegend        = plan.ShowLegend,
            ShowGridlines     = plan.ShowGridlines,
            PlotAreaFill      = plan.PlotAreaFill,
            ShowDataLabels    = plan.ShowDataLabels,
            CategoryAxisTitle = plan.CategoryAxisTitle,
            ValueAxisTitle    = plan.ValueAxisTitle,
            Palette           = plan.PaletteHex.Select(ToAvaloniaChartColor).ToList(),
        };
    }

    /// <summary>
    /// Renders a floating chart at its page-space rect.
    /// Column/Bar/Line/Pie/Doughnut/Area/Scatter: basic geometry rendered from series data.
    /// AV-POLISH: axis titles (value axis rotated left, category axis bottom-centre), data-value labels
    /// (above/on bars, at line points, on pie slices), and a series legend (right side or bottom) are
    /// drawn when the chart model requests them. The plot area shrinks to accommodate the annotations.
    /// </summary>
    private void DrawFloatingChart(DrawingContext context, FloatingChartData cd)
    {
        var rect = cd.Rect;
        // Frame.
        context.FillRectangle(ChartFrameFill, rect);
        context.DrawRectangle(null, ChartFramePen, rect);

        // ── Title bar ──
        const double titleH = 20;
        var titleY = rect.Y + 4;
        if (!string.IsNullOrEmpty(cd.Title))
        {
            var titleFmt = new RunFormatting { FontSizePt = 9, Bold = true };
            var ft = Build(cd.Title, titleFmt);
            var tx = rect.X + (rect.Width - ft.WidthIncludingTrailingWhitespace) / 2;
            context.DrawText(ft, new Point(Math.Max(rect.X + 2, tx), titleY));
        }

        var annotFmt = new RunFormatting { FontSizePt = 7 };

        // BC2: Legend is placed at the BOTTOM (matches WPF). Build legend entries for ALL series
        // with "Series N" fallback; for Pie/Doughnut use categories with "Item N" fallback.
        var isPieFamily = cd.Kind is ChartKind.Pie or ChartKind.Doughnut;
        List<(string label, int colorIdx)> legendEntries = [];
        if (cd.ShowLegend)
        {
            if (isPieFamily)
            {
                // Pie/doughnut: one entry per slice from Categories (or "Item N").
                var sliceCount = cd.Series.Count > 0 ? cd.Series[0].Values.Count : cd.Categories.Count;
                for (var i = 0; i < sliceCount; i++)
                {
                    var lbl = i < cd.Categories.Count && !string.IsNullOrEmpty(cd.Categories[i])
                        ? cd.Categories[i] : $"Item {i + 1}";
                    legendEntries.Add((lbl, i));
                }
            }
            else
            {
                // Non-pie: one entry per series, "Series N" fallback.
                for (var si = 0; si < cd.Series.Count; si++)
                {
                    var name = string.IsNullOrEmpty(cd.Series[si].Name) ? $"Series {si + 1}" : cd.Series[si].Name!;
                    legendEntries.Add((name, si));
                }
            }
        }

        // Legend height: reserve at bottom when entries exist.
        const double legendRowH  = 11;
        const double legendSwSz  = 8;
        const double legendPad   = 2;
        var legendH = legendEntries.Count > 0 ? legendRowH + legendPad * 2 : 0.0;

        // ── Value-axis (Y) title — left strip ──
        const double valAxisTitleW = 12; // width of rotated text strip
        var hasValTitle = !string.IsNullOrEmpty(cd.ValueAxisTitle);
        var valTitleW = hasValTitle ? valAxisTitleW : 0.0;

        // ── Category-axis (X) title — bottom strip ──
        const double catAxisTitleH = 12;
        var hasCatTitle = !string.IsNullOrEmpty(cd.CategoryAxisTitle);
        var catTitleH = hasCatTitle ? catAxisTitleH : 0.0;

        // ── Plot area bounds after reserving annotation strips ──
        var plotTop    = rect.Y + (string.IsNullOrEmpty(cd.Title) ? 8 : titleH + 4);
        var plotBottom = rect.Bottom - 18 - catTitleH - legendH; // x-axis labels + optional cat title + legend
        var plotLeft   = rect.X + 32 + valTitleW;      // y-axis labels + optional val title
        var plotRight  = rect.Right - 8;
        var plotW      = Math.Max(10, plotRight - plotLeft);
        var plotH      = Math.Max(10, plotBottom - plotTop);

        if (cd.Series.Count == 0 || plotW < 5 || plotH < 5)
            return;

        if (cd.PlotAreaFill && !isPieFamily)
            context.FillRectangle(new SolidColorBrush(Color.FromRgb(0xD9, 0xE2, 0xF3)),
                new Rect(plotLeft, plotTop, plotW, plotH));

        // BC3: Compute the axis range for non-pie charts (used for gridline labels and data drawing).
        var (axisMin, axisMax, axisRange) = ComputeAxisRange(cd);
        var zeroFraction = -axisMin / axisRange;

        // ── Gridlines + BC1: Y-axis tick labels ──
        const int gridLines = 4;
        var gridPen = new Pen(ChartGridlineBrush, 0.5);
        for (var g = 0; g <= gridLines; g++)
        {
            var gy = plotBottom - g * plotH / gridLines;
            if (cd.ShowGridlines || g == 0)
                context.DrawLine(gridPen, new Point(plotLeft, gy), new Point(plotRight, gy));

            // BC1: Draw value-axis tick label in the reserved left strip.
            if (!isPieFamily && (cd.ShowGridlines || g == 0))
            {
                var tickVal = axisMin + (g * axisRange / gridLines);
                var tickLabel = tickVal.ToString("G3", System.Globalization.CultureInfo.InvariantCulture);
                var tickFt = Build(tickLabel, annotFmt);
                var tx = plotLeft - tickFt.WidthIncludingTrailingWhitespace - 2;
                var ty = gy - tickFt.Height / 2;
                if (tx >= rect.X)
                    context.DrawText(tickFt, new Point(tx, ty));
            }
        }

        // ── Chart geometry ──
        switch (cd.Kind)
        {
            case ChartKind.Column:
            case ChartKind.Bar:
                DrawChartBars(context, cd, plotLeft, plotTop, plotW, plotH, plotBottom,
                    horizontal: cd.Kind == ChartKind.Bar);
                break;

            case ChartKind.Line:
            case ChartKind.Area:
                DrawChartLines(context, cd, plotLeft, plotTop, plotW, plotH, plotBottom,
                    fillArea: cd.Kind == ChartKind.Area);
                break;

            case ChartKind.Scatter:
                DrawChartScatterMarkers(context, cd, plotLeft, plotTop, plotW, plotH, plotBottom);
                break;

            case ChartKind.Pie:
                DrawChartPie(context, cd, plotLeft, plotTop, plotW, plotH, doughnut: false);
                break;

            case ChartKind.Doughnut:
                DrawChartPie(context, cd, plotLeft, plotTop, plotW, plotH, doughnut: true);
                break;
        }

        // BC1: Draw category-axis (X) labels under each bar/point group (mirrors WPF AddCategoryLabel).
        if (!isPieFamily && cd.Categories.Count > 0)
        {
            var cats = cd.Categories.Count;
            switch (cd.Kind)
            {
                case ChartKind.Column:
                {
                    var groupW = plotW / Math.Max(1, cats);
                    for (var ci = 0; ci < cats; ci++)
                    {
                        var cat = cd.Categories[ci];
                        if (string.IsNullOrEmpty(cat)) continue;
                        var ft  = Build(cat, annotFmt);
                        var cx  = plotLeft + ci * groupW + groupW / 2;
                        var tx  = cx - ft.WidthIncludingTrailingWhitespace / 2;
                        var ty  = plotBottom + 2;
                        context.DrawText(ft, new Point(Math.Clamp(tx, plotLeft, plotRight - ft.WidthIncludingTrailingWhitespace), ty));
                    }
                    break;
                }
                case ChartKind.Bar:
                {
                    var groupH = plotH / Math.Max(1, cats);
                    for (var ci = 0; ci < cats; ci++)
                    {
                        var cat = cd.Categories[ci];
                        if (string.IsNullOrEmpty(cat)) continue;
                        var ft  = Build(cat, annotFmt);
                        var cy  = plotTop + ci * groupH + groupH / 2;
                        var ty  = cy - ft.Height / 2;
                        // Label on the left side of the bar chart.
                        var tx  = rect.X + 2;
                        context.DrawText(ft, new Point(tx, ty));
                    }
                    break;
                }
                case ChartKind.Line:
                case ChartKind.Scatter:
                case ChartKind.Area:
                {
                    for (var ci = 0; ci < cats; ci++)
                    {
                        var cat = cd.Categories[ci];
                        if (string.IsNullOrEmpty(cat)) continue;
                        var ft  = Build(cat, annotFmt);
                        var px  = plotLeft + ci * plotW / Math.Max(1, cats - 1);
                        var tx  = px - ft.WidthIncludingTrailingWhitespace / 2;
                        var ty  = plotBottom + 2;
                        context.DrawText(ft, new Point(Math.Clamp(tx, plotLeft, plotRight - ft.WidthIncludingTrailingWhitespace), ty));
                    }
                    break;
                }
            }
        }

        // ── Data labels ──
        if (cd.ShowDataLabels && cd.Series.Count > 0)
        {
            DrawChartDataLabels(context, cd, plotLeft, plotTop, plotW, plotH, plotBottom, annotFmt);
        }

        // ── Value-axis title (rotated 90° counter-clockwise, centred on left of y-axis) ──
        if (hasValTitle)
        {
            var valTitleFt = Build(cd.ValueAxisTitle!, annotFmt);
            var titleCentreY = (plotTop + plotBottom) / 2;
            var titleX = rect.X + 2; // far-left strip
            // Draw rotated: push transform, draw, pop.
            var rotState = context.PushTransform(
                Matrix.CreateTranslation(titleX + valTitleFt.Height / 2, titleCentreY) *
                Matrix.CreateRotation(-Math.PI / 2));
            context.DrawText(valTitleFt, new Point(-valTitleFt.WidthIncludingTrailingWhitespace / 2, -valTitleFt.Height / 2));
            rotState.Dispose();
        }

        // ── Category-axis title (below x-axis labels, horizontally centred) ──
        if (hasCatTitle)
        {
            var catTitleFt = Build(cd.CategoryAxisTitle!, annotFmt);
            var catTitleX = plotLeft + (plotW - catTitleFt.WidthIncludingTrailingWhitespace) / 2;
            var catTitleY = rect.Bottom - legendH - catTitleFt.Height - 1;
            context.DrawText(catTitleFt, new Point(Math.Max(rect.X + 2, catTitleX), catTitleY));
        }

        // BC2: Legend (BOTTOM, matches WPF) — all series with "Series N" fallback; pie uses categories.
        if (legendEntries.Count > 0)
        {
            const double swatchSz = legendSwSz;
            const double rowH     = legendRowH;
            const double pad      = legendPad;

            // Lay the entries out horizontally centred, wrapping if needed.
            var legendY  = rect.Bottom - legendH + pad;
            var legendX0 = plotLeft;
            var curX     = legendX0;
            // Measure total width to centre.
            var totalLegendW = 0.0;
            foreach (var (lbl, _) in legendEntries)
            {
                var w = Build(lbl, annotFmt).WidthIncludingTrailingWhitespace;
                totalLegendW += swatchSz + 3 + w + 12;
            }
            curX = plotLeft + Math.Max(0, (plotW - totalLegendW) / 2);

            // Semi-transparent background.
            context.FillRectangle(ChartLegendBg,
                new Rect(rect.X, rect.Bottom - legendH, rect.Width, legendH));

            foreach (var (lbl, colorIdx) in legendEntries)
            {
                var color = ChartColorAt(cd, colorIdx);
                var brush = new SolidColorBrush(color);
                var nameFt = Build(lbl, annotFmt);
                var entryW = swatchSz + 3 + nameFt.WidthIncludingTrailingWhitespace + 12;

                if (curX + entryW > plotRight)
                    break; // stop if no room

                // Swatch.
                context.FillRectangle(brush, new Rect(curX, legendY + (rowH - swatchSz) / 2, swatchSz, swatchSz));
                // Name.
                context.DrawText(nameFt, new Point(curX + swatchSz + 3, legendY + (rowH - nameFt.Height) / 2));
                curX += entryW;
            }
        }

        // Kind label (bottom-right corner, tiny).
        var kindFmt = new RunFormatting { FontSizePt = 7, ColorHex = "#999999" };
        var kindFt  = Build(cd.Kind.ToString(), kindFmt);
        context.DrawText(kindFt, new Point(rect.Right - kindFt.WidthIncludingTrailingWhitespace - 2, rect.Bottom - kindFt.Height));
    }

    /// <summary>
    /// Draws data-value labels for bar/column, line/scatter/area, and pie/doughnut charts.
    /// For bars: value text above (column) or at end (bar) of each bar.
    /// For lines: value text above each data point.
    /// For pie/doughnut: percentage text at the slice midpoint angle.
    /// Approximation: text is positioned geometrically; no collision avoidance.
    /// </summary>
    /// <summary>
    /// Shared helper: compute the axis [axisMin, axisMax] range for bar/line data labels and axis labels.
    /// Ensures the range includes 0, guards degenerate all-zero data.
    /// </summary>
    private static (double axisMin, double axisMax, double axisRange) ComputeAxisRange(FloatingChartData cd)
    {
        var minVal = 0.0;
        var maxVal = 0.0;
        foreach (var (_, vals) in cd.Series)
            foreach (var v in vals)
            {
                if (v < minVal) minVal = v;
                if (v > maxVal) maxVal = v;
            }
        var axisMin   = Math.Min(0, minVal);
        var axisMax   = Math.Max(0, maxVal);
        if (axisMax <= axisMin) axisMax = axisMin + 1;
        return (axisMin, axisMax, axisMax - axisMin);
    }

    private void DrawChartDataLabels(DrawingContext context, FloatingChartData cd,
        double plotLeft, double plotTop, double plotW, double plotH, double plotBottom,
        RunFormatting fmt)
    {
        // BC3: Use axis range that includes negative values.
        var (axisMin, axisMax, axisRange) = ComputeAxisRange(cd);
        var zeroFraction = -axisMin / axisRange;
        var zeroY = plotBottom - zeroFraction * plotH;
        var zeroX = plotLeft   + zeroFraction * plotW;

        switch (cd.Kind)
        {
            case ChartKind.Column:
            {
                var cats    = cd.Categories.Count > 0 ? cd.Categories.Count : (cd.Series[0].Values.Count > 0 ? cd.Series[0].Values.Count : 1);
                var nSeries = cd.Series.Count;
                var groupW  = plotW / Math.Max(1, cats);
                var barPad  = Math.Max(1, groupW * 0.1);
                var barGroupW = groupW - 2 * barPad;
                var seriesW = barGroupW / Math.Max(1, nSeries);

                for (var si = 0; si < nSeries; si++)
                {
                    var (_, vals) = cd.Series[si];
                    for (var ci = 0; ci < cats; ci++)
                    {
                        var val     = ci < vals.Count ? vals[ci] : 0;
                        var bw      = Math.Max(1, seriesW - 1);
                        var bx      = plotLeft + barPad + ci * groupW + si * seriesW;
                        var valFrac = val / axisRange;
                        var barH    = Math.Abs(valFrac) * plotH;
                        var barTopY = val >= 0 ? zeroY - barH : zeroY;

                        var label = val.ToString("G3", System.Globalization.CultureInfo.InvariantCulture);
                        var ft = Build(label, fmt);
                        var lx = bx + (bw - ft.WidthIncludingTrailingWhitespace) / 2;
                        double ly;
                        if (val >= 0)
                        {
                            ly = barTopY - ft.Height - 1;
                            if (ly < plotTop) ly = barTopY + 1;
                        }
                        else
                        {
                            ly = barTopY + barH + 1; // below the bar for negative
                            if (ly + ft.Height > plotBottom) ly = barTopY - ft.Height - 1;
                        }
                        context.DrawText(ft, new Point(Math.Max(plotLeft, lx), ly));
                    }
                }
                break;
            }

            case ChartKind.Bar:
            {
                var cats    = cd.Categories.Count > 0 ? cd.Categories.Count : (cd.Series[0].Values.Count > 0 ? cd.Series[0].Values.Count : 1);
                var nSeries = cd.Series.Count;
                var groupW  = plotH / Math.Max(1, cats);  // horizontal: groups along Y
                var barPad  = Math.Max(1, groupW * 0.1);
                var barGroupH = groupW - 2 * barPad;
                var seriesH = barGroupH / Math.Max(1, nSeries);

                for (var si = 0; si < nSeries; si++)
                {
                    var (_, vals) = cd.Series[si];
                    for (var ci = 0; ci < cats; ci++)
                    {
                        var val   = ci < vals.Count ? vals[ci] : 0;
                        var barW  = Math.Abs(val / axisRange) * plotW;
                        var bx    = val >= 0 ? zeroX : zeroX - barW;
                        var by    = plotTop + (ci * (barGroupH + 2 * barPad) + barPad + si * seriesH);

                        var label = val.ToString("G3", System.Globalization.CultureInfo.InvariantCulture);
                        var ft = Build(label, fmt);
                        var lx = val >= 0 ? bx + barW + 1 : bx - ft.WidthIncludingTrailingWhitespace - 1;
                        var ly = by + (Math.Max(1, seriesH - 1) - ft.Height) / 2;
                        context.DrawText(ft, new Point(Math.Clamp(lx, plotLeft, plotLeft + plotW - ft.WidthIncludingTrailingWhitespace), ly));
                    }
                }
                break;
            }

            case ChartKind.Line:
            case ChartKind.Scatter:
            case ChartKind.Area:
            {
                var cats = Math.Max(2, cd.Categories.Count > 0 ? cd.Categories.Count : (cd.Series[0].Values.Count));
                for (var si = 0; si < cd.Series.Count; si++)
                {
                    var (_, vals) = cd.Series[si];
                    for (var ci = 0; ci < vals.Count; ci++)
                    {
                        var val = vals[ci];
                        var px  = plotLeft + ci * plotW / Math.Max(1, cats - 1);
                        var py  = plotBottom - ((val - axisMin) / axisRange * plotH);

                        var label = val.ToString("G3", System.Globalization.CultureInfo.InvariantCulture);
                        var ft = Build(label, fmt);
                        var lx = px - ft.WidthIncludingTrailingWhitespace / 2;
                        var ly = py - ft.Height - 2;
                        if (ly < plotTop) ly = py + 2;
                        context.DrawText(ft, new Point(Math.Clamp(lx, plotLeft, plotLeft + plotW - ft.WidthIncludingTrailingWhitespace), ly));
                    }
                }
                break;
            }

            case ChartKind.Pie:
            case ChartKind.Doughnut:
            {
                if (cd.Series.Count == 0) break;
                var vals  = cd.Series[0].Values;
                var total = vals.Sum();
                if (total <= 0) break;

                var cx = plotLeft + plotW / 2;
                var cy = plotTop  + plotH / 2;
                var r  = Math.Min(plotW, plotH) / 2 - 4;
                var labelR = r * (cd.Kind == ChartKind.Doughnut ? 0.75 : 0.65); // place at midpoint radius

                var startAngle = -Math.PI / 2;
                for (var si = 0; si < vals.Count; si++)
                {
                    var sweep = vals[si] / total * 2 * Math.PI;
                    var midAngle = startAngle + sweep / 2;
                    var pct = (vals[si] / total * 100).ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + "%";
                    var ft = Build(pct, fmt);
                    var lx = cx + labelR * Math.Cos(midAngle) - ft.WidthIncludingTrailingWhitespace / 2;
                    var ly = cy + labelR * Math.Sin(midAngle) - ft.Height / 2;
                    context.DrawText(ft, new Point(lx, ly));
                    startAngle += sweep;
                }
                break;
            }
        }
    }

    private void DrawChartBars(DrawingContext context, FloatingChartData cd,
        double plotLeft, double plotTop, double plotW, double plotH, double plotBottom, bool horizontal)
    {
        var cats    = cd.Categories.Count > 0 ? cd.Categories.Count : (cd.Series[0].Values.Count > 0 ? cd.Series[0].Values.Count : 1);
        var nSeries = cd.Series.Count;
        var nBars   = cats;

        // BC3: Compute axis range that includes negative values and anchors the zero baseline.
        var minVal = 0.0;
        var maxVal = 0.0;
        foreach (var (_, vals) in cd.Series)
            foreach (var v in vals)
            {
                if (v < minVal) minVal = v;
                if (v > maxVal) maxVal = v;
            }
        var axisMin = Math.Min(0, minVal);
        var axisMax = Math.Max(0, maxVal);
        // Guard degenerate all-zero data.
        if (axisMax <= axisMin) axisMax = axisMin + 1;
        var axisRange = axisMax - axisMin;

        // Zero baseline position within the plot area.
        // In vertical bars:  zeroY   = plotBottom - (0 - axisMin)/axisRange * plotH
        // In horizontal bars: zeroX  = plotLeft   + (0 - axisMin)/axisRange * plotW
        var zeroFraction = -axisMin / axisRange; // fraction of plotH/plotW at which y=0 lives
        var zeroY = plotBottom - zeroFraction * plotH;
        var zeroX = plotLeft   + zeroFraction * plotW;

        var groupW    = plotW / Math.Max(1, nBars);
        var barPad    = Math.Max(1, groupW * 0.1);
        var barGroupW = groupW - 2 * barPad;
        var seriesW   = barGroupW / Math.Max(1, nSeries);

        for (var si = 0; si < nSeries; si++)
        {
            var (_, vals) = cd.Series[si];
            var color = ChartColorAt(cd, si);
            var brush = new SolidColorBrush(color);

            for (var ci = 0; ci < nBars; ci++)
            {
                var val = ci < vals.Count ? vals[ci] : 0;

                if (horizontal)
                {
                    var bh       = Math.Max(1, seriesW - 1);
                    var by       = plotTop + (ci * (barGroupW + 2 * barPad) + barPad + si * seriesW);
                    var valFrac  = val / axisRange;
                    var barW     = Math.Abs(valFrac) * plotW;
                    double bx;
                    if (val >= 0)
                        bx = zeroX;
                    else
                    {
                        bx   = zeroX - barW;
                        barW = Math.Abs(barW);
                    }
                    if (barW < 1) barW = 1;
                    context.FillRectangle(brush, new Rect(bx, by, barW, bh));
                }
                else
                {
                    var bw      = Math.Max(1, seriesW - 1);
                    var bx      = plotLeft + barPad + ci * groupW + si * seriesW;
                    var valFrac = val / axisRange;
                    var barH    = Math.Abs(valFrac) * plotH;
                    double barTop;
                    if (val >= 0)
                        barTop = zeroY - barH;
                    else
                        barTop = zeroY;
                    if (barH < 1) barH = 1;
                    context.FillRectangle(brush, new Rect(bx, barTop, bw, barH));
                }
            }
        }

        // Draw zero-baseline axis line.
        var axisLinePen = new Pen(ChartGridlineBrush, 1.0);
        if (!horizontal)
            context.DrawLine(axisLinePen, new Point(plotLeft, zeroY), new Point(plotLeft + plotW, zeroY));
        else
            context.DrawLine(axisLinePen, new Point(zeroX, plotTop), new Point(zeroX, plotBottom));
    }

    private void DrawChartLines(DrawingContext context, FloatingChartData cd,
        double plotLeft, double plotTop, double plotW, double plotH, double plotBottom, bool fillArea)
    {
        var cats = Math.Max(2, cd.Categories.Count > 0 ? cd.Categories.Count : (cd.Series[0].Values.Count));

        // BC3: Compute axis range including negative values.
        var minVal = 0.0;
        var maxVal = 0.0;
        foreach (var (_, vals) in cd.Series)
            foreach (var v in vals)
            {
                if (v < minVal) minVal = v;
                if (v > maxVal) maxVal = v;
            }
        var axisMin   = Math.Min(0, minVal);
        var axisMax   = Math.Max(0, maxVal);
        if (axisMax <= axisMin) axisMax = axisMin + 1;
        var axisRange = axisMax - axisMin;

        // Zero baseline Y position within the plot.
        var zeroFraction = -axisMin / axisRange;
        var zeroY = plotBottom - zeroFraction * plotH;

        // Map a data value to a pixel Y within the plot.
        double ValToY(double v) => plotBottom - ((v - axisMin) / axisRange) * plotH;

        for (var si = 0; si < cd.Series.Count; si++)
        {
            var (_, vals) = cd.Series[si];
            if (vals.Count == 0) continue;

            var color = ChartColorAt(cd, si);
            var pen   = new Pen(new SolidColorBrush(color), 1.5);

            var pts = new List<Point>();
            for (var ci = 0; ci < Math.Max(cats, vals.Count); ci++)
            {
                var val = ci < vals.Count ? vals[ci] : 0;
                var px  = plotLeft + ci * plotW / Math.Max(1, cats - 1);
                var py  = ValToY(val);
                pts.Add(new Point(px, py));
            }

            for (var pi = 0; pi < pts.Count - 1; pi++)
                context.DrawLine(pen, pts[pi], pts[pi + 1]);

            if (fillArea && pts.Count >= 2)
            {
                var geo = new StreamGeometry();
                using var ctx = geo.Open();
                ctx.BeginFigure(new Point(pts[0].X, zeroY), isFilled: true);
                foreach (var p in pts) ctx.LineTo(p);
                ctx.LineTo(new Point(pts[^1].X, zeroY));
                ctx.EndFigure(true);
                context.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x55, color.R, color.G, color.B)), null, geo);
            }
        }

        // Draw zero-baseline axis line.
        context.DrawLine(new Pen(ChartGridlineBrush, 1.0),
            new Point(plotLeft, zeroY), new Point(plotLeft + plotW, zeroY));
    }

    private void DrawChartScatterMarkers(DrawingContext context, FloatingChartData cd,
        double plotLeft, double plotTop, double plotW, double plotH, double plotBottom)
    {
        var xVals = cd.Categories
            .Select(c => double.TryParse(c, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : double.NaN)
            .ToList();
        var xMin = xVals.Where(v => !double.IsNaN(v)).DefaultIfEmpty(1).Min();
        var xMax = xVals.Where(v => !double.IsNaN(v)).DefaultIfEmpty(1).Max();
        if (xMax <= xMin) xMax = xMin + 1;

        var yMax = Math.Max(1.0, cd.Series.SelectMany(s => s.Values).DefaultIfEmpty(1).Max());

        double Px(int c)
        {
            var x = c < xVals.Count && !double.IsNaN(xVals[c]) ? xVals[c] : c + 1;
            return plotLeft + (x - xMin) / (xMax - xMin) * plotW;
        }

        double Py(double value) => plotBottom - plotH * (Math.Max(0, value) / yMax);

        for (var si = 0; si < cd.Series.Count; si++)
        {
            var (_, values) = cd.Series[si];
            var brush = new SolidColorBrush(ChartColorAt(cd, si));
            for (var ci = 0; ci < values.Count; ci++)
            {
                var px = Px(ci);
                var py = Py(values[ci]);
                context.DrawEllipse(brush, null, new Point(px, py), 3.5, 3.5);
            }
        }

        context.DrawLine(new Pen(ChartGridlineBrush, 1.0),
            new Point(plotLeft, plotBottom), new Point(plotLeft + plotW, plotBottom));
    }

    private static void DrawChartPie(DrawingContext context, FloatingChartData cd,
        double plotLeft, double plotTop, double plotW, double plotH, bool doughnut)
    {
        if (cd.Series.Count == 0) return;
        var vals = cd.Series[0].Values;
        if (vals.Count == 0) return;

        var total = vals.Sum();
        if (total <= 0) return;

        var cx = plotLeft + plotW / 2;
        var cy = plotTop  + plotH / 2;
        var r  = Math.Min(plotW, plotH) / 2 - 4;
        var innerR = doughnut ? r * 0.5 : 0;

        var startAngle = -Math.PI / 2; // start at top
        for (var si = 0; si < vals.Count; si++)
        {
            var sweep     = vals[si] / total * 2 * Math.PI;
            var color     = ChartColorAt(cd, si);
            var brush     = new SolidColorBrush(color);

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                if (doughnut)
                {
                    // Outer arc start.
                    var ox1 = cx + r * Math.Cos(startAngle);
                    var oy1 = cy + r * Math.Sin(startAngle);
                    var ox2 = cx + r * Math.Cos(startAngle + sweep);
                    var oy2 = cy + r * Math.Sin(startAngle + sweep);
                    // Inner arc end (reversed).
                    var ix1 = cx + innerR * Math.Cos(startAngle + sweep);
                    var iy1 = cy + innerR * Math.Sin(startAngle + sweep);
                    var ix2 = cx + innerR * Math.Cos(startAngle);
                    var iy2 = cy + innerR * Math.Sin(startAngle);

                    var isLarge = sweep > Math.PI;
                    ctx.BeginFigure(new Point(ox1, oy1), isFilled: true);
                    ctx.ArcTo(new Point(ox2, oy2), new Size(r, r), 0, isLarge, SweepDirection.Clockwise);
                    ctx.LineTo(new Point(ix1, iy1));
                    ctx.ArcTo(new Point(ix2, iy2), new Size(innerR, innerR), 0, isLarge, SweepDirection.CounterClockwise);
                    ctx.EndFigure(true);
                }
                else
                {
                    var ex = cx + r * Math.Cos(startAngle + sweep);
                    var ey = cy + r * Math.Sin(startAngle + sweep);
                    var isLarge = sweep > Math.PI;
                    ctx.BeginFigure(new Point(cx, cy), isFilled: true);
                    ctx.LineTo(new Point(cx + r * Math.Cos(startAngle), cy + r * Math.Sin(startAngle)));
                    ctx.ArcTo(new Point(ex, ey), new Size(r, r), 0, isLarge, SweepDirection.Clockwise);
                    ctx.EndFigure(true);
                }
            }
            context.DrawGeometry(brush, new Pen(Brushes.White, 0.5), geo);
            startAngle += sweep;
        }
    }

    // WordArt style → (fill colour, outline colour, glow/shadow hint) lookup.
    private static (string FillHex, string? OutlineHex, bool Bold) WordArtStyleToColors(WordArtStyle style) =>
        style switch
        {
            WordArtStyle.FillBlue      => ("#4472C4", null, true),
            WordArtStyle.GradientFill  => ("#4472C4", null, true),
            WordArtStyle.GradFillMulti => ("#ED7D31", null, true),
            WordArtStyle.Outline       => ("#FFFFFF", "#4472C4", true),
            WordArtStyle.ChromeOne     => ("#FFFFFF", "#000000", true),
            WordArtStyle.ChromeTwo     => ("#4472C4", "#FFFFFF", true),
            WordArtStyle.Shadow        => ("#4472C4", null, true),
            WordArtStyle.ShadowOrange  => ("#ED7D31", null, true),
            WordArtStyle.FillGold      => ("#FFC000", null, true),
            WordArtStyle.FillWhite     => ("#FFFFFF", "#AAAAAA", true),
            WordArtStyle.GlowBlue      => ("#4472C4", null, true),
            WordArtStyle.GlowGold      => ("#FFC000", null, true),
            WordArtStyle.Reflection    => ("#4472C4", null, true),
            WordArtStyle.Bevel         => ("#4472C4", null, true),
            WordArtStyle.PatternFill   => ("#4472C4", "#4472C4", true),
            _                          => ("#4472C4", null, true),
        };

    /// <summary>
    /// Renders a floating WordArt at its page-space rect.
    /// Fully rendered: styled text with fill colour (and outline when preset uses one) at correct size,
    /// position, and z-order. Text warp (WordArtWarp) approximated with a skew/arc label — no actual
    /// path deformation (Avalonia immediate-mode has no text-on-path API); this is noted as a follow-up.
    /// </summary>
    private void DrawFloatingWordArt(DrawingContext context, FloatingWordArtData wd)
    {
        if (string.IsNullOrEmpty(wd.Text)) return;

        var rect = wd.Rect;
        var (fillHex, outlineHex, bold) = WordArtStyleToColors(wd.Style);

        // Draw a light background frame so the WordArt region is visible even without warp geometry.
        context.DrawRectangle(null,
            new Pen(new SolidColorBrush(Color.FromArgb(0x44, 0x44, 0x72, 0xC4)), 1.0,
                new DashStyle([3, 3], 0)),
            rect);

        // If the style has a fill background (e.g. gradient styles) draw a subtle bg gradient.
        if (wd.Style is WordArtStyle.GradientFill or WordArtStyle.GradFillMulti)
        {
            var gb = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint   = new RelativePoint(1, 1, RelativeUnit.Relative),
            };
            if (TryParseAvaloniaColor(fillHex, out var c1))
                gb.GradientStops.Add(new global::Avalonia.Media.GradientStop(c1, 0));
            gb.GradientStops.Add(new global::Avalonia.Media.GradientStop(Colors.White, 1));
            context.FillRectangle(gb, rect);
        }

        // Render the text centred in the rect at the WordArt font size.
        var textFmt = new RunFormatting
        {
            FontSizePt = Math.Max(8, wd.FontSizePt),
            Bold       = bold,
            ColorHex   = fillHex,
        };

        // Warp hint: for non-None warps add a "(warp)" note since path-deform is not supported in
        // Avalonia immediate-mode. The text is still at the right position and size.
        var displayText = wd.Text;

        var ft = Build(displayText, textFmt);
        var tx = rect.X + Math.Max(0, (rect.Width  - ft.WidthIncludingTrailingWhitespace) / 2);
        var ty = rect.Y + Math.Max(0, (rect.Height - ft.Height) / 2);
        using (context.PushClip(rect))
            context.DrawText(ft, new Point(tx, ty));

        // For styles with an outline, draw the text a second time with a contrasting colour offset by 1px
        // to simulate an outline effect (poor-man's outline — Avalonia has no DrawTextOutline API).
        if (!string.IsNullOrEmpty(outlineHex))
        {
            var outlineFmt = textFmt with { ColorHex = outlineHex };
            var outlineFt  = Build(displayText, outlineFmt);
            using (context.PushClip(rect))
                context.DrawText(outlineFt, new Point(tx + 1, ty + 1));
        }

        // Warp indicator (small label in corner if warp is set).
        if (wd.Warp != WordArtWarp.None)
        {
            var warpFmt = new RunFormatting { FontSizePt = 7, ColorHex = "#888888" };
            var warpFt  = Build($"~{wd.Warp}", warpFmt);
            context.DrawText(warpFt, new Point(rect.X + 2, rect.Bottom - warpFt.Height));
        }
    }

    /// <summary>
    /// Renders a floating SmartArt diagram at its page-space rect.
    /// Rendered: node topology — boxes with text labels + connecting lines — at the correct placement.
    /// Layout algorithm: simple left-to-right (List/Process) or top-down tree (Hierarchy).
    /// Full DrawingML layout engine is not implemented (that would require porting the diagram layout
    /// parts); this is noted as a follow-up. Placement and z-order are correct.
    /// </summary>
    private void DrawFloatingSmartArt(DrawingContext context, FloatingSmartArtData sd)
    {
        var rect = sd.Rect;

        // Frame.
        context.FillRectangle(ChartFrameFill, rect);
        context.DrawRectangle(null, ChartFramePen, rect);

        // Kind label.
        var headerFmt = new RunFormatting { FontSizePt = 8, Bold = true, ColorHex = "#555555" };
        var headerFt  = Build($"SmartArt ({sd.Kind})", headerFmt);
        context.DrawText(headerFt, new Point(rect.X + 4, rect.Y + 2));

        if (sd.NodeTexts.Count == 0) return;

        // Draw node boxes.
        const double nodePad  = 6;
        const double nodeH    = 26;
        const double connGap  = 8;

        var areaTop   = rect.Y + headerFt.Height + 6;
        var areaH     = rect.Height - (areaTop - rect.Y) - nodePad;
        var areaW     = rect.Width - 2 * nodePad;

        if (sd.Kind == SmartArtKind.Hierarchy)
        {
            // Simple top-down: root on row 0, children on row 1, evenly spaced.
            var roots    = sd.NodeTexts.Count > 0 ? new[] { sd.NodeTexts[0] } : [];
            var children = sd.NodeTexts.Skip(1).ToArray();

            // Root box.
            var rootW = Math.Min(areaW, 120);
            var rootX = rect.X + (rect.Width - rootW) / 2;
            var rootY = areaTop + nodePad;
            var rootRect = new Rect(rootX, rootY, rootW, nodeH);
            DrawSmartArtNodeBox(context, sd, 0, rootRect);
            DrawSmartArtNodeText(context, roots.Length > 0 ? roots[0] : string.Empty, rootRect, SmartArtTextColorAt(sd, 0));

            if (children.Length > 0)
            {
                var childW  = Math.Min((areaW - (children.Length - 1) * connGap) / children.Length, 90);
                var childY  = rootY + nodeH + connGap * 2;
                var totalChildW = childW * children.Length + connGap * (children.Length - 1);
                var childStartX = rect.X + (rect.Width - totalChildW) / 2;

                // Vertical line from root to child row.
                var midRootX = rootX + rootW / 2;
                var connectorPen = SmartArtConnectorPenAt(sd, 0);
                context.DrawLine(connectorPen, new Point(midRootX, rootY + nodeH), new Point(midRootX, childY - connGap));
                // Horizontal line across child tops.
                if (children.Length > 1)
                {
                    context.DrawLine(connectorPen,
                        new Point(childStartX + childW / 2, childY - connGap),
                        new Point(childStartX + (children.Length - 1) * (childW + connGap) + childW / 2, childY - connGap));
                }

                for (var ci = 0; ci < children.Length; ci++)
                {
                    var cx = childStartX + ci * (childW + connGap);
                    var childRect = new Rect(cx, childY, childW, nodeH);
                    DrawSmartArtNodeBox(context, sd, ci + 1, childRect);
                    DrawSmartArtNodeText(context, children[ci], childRect, SmartArtTextColorAt(sd, ci + 1));
                    // Vertical drop line from horizontal bus to child.
                    context.DrawLine(connectorPen,
                        new Point(cx + childW / 2, childY - connGap),
                        new Point(cx + childW / 2, childY));
                }
            }
        }
        else
        {
            // List / Process: horizontal row of boxes with right-arrow connectors.
            var count  = sd.NodeTexts.Count;
            var arrows = sd.Kind == SmartArtKind.Process ? count - 1 : 0;
            var arrowW = 12.0;
            var totalArrowW = arrows * (arrowW + 2);
            var boxW = Math.Max(24, (areaW - totalArrowW - 2 * nodePad) / count);
            var boxY = areaTop + (areaH - nodeH) / 2;

            var bx = rect.X + nodePad;
            for (var ni = 0; ni < count; ni++)
            {
                var nodeRect = new Rect(bx, boxY, boxW, nodeH);
                DrawSmartArtNodeBox(context, sd, ni, nodeRect);
                DrawSmartArtNodeText(context, sd.NodeTexts[ni], nodeRect, SmartArtTextColorAt(sd, ni));
                bx += boxW;

                // Arrow connector between process nodes.
                if (sd.Kind == SmartArtKind.Process && ni < count - 1)
                {
                    var arrowMidY = boxY + nodeH / 2;
                    var arrowX1   = bx + 2;
                    var arrowX2   = arrowX1 + arrowW;
                    var arrowPen  = SmartArtConnectorPenAt(sd, ni, 1.5);
                    context.DrawLine(arrowPen, new Point(arrowX1, arrowMidY), new Point(arrowX2, arrowMidY));
                    // Arrow head.
                    context.DrawLine(arrowPen, new Point(arrowX2, arrowMidY), new Point(arrowX2 - 4, arrowMidY - 3));
                    context.DrawLine(arrowPen, new Point(arrowX2, arrowMidY), new Point(arrowX2 - 4, arrowMidY + 3));
                    bx += arrowW + 2;
                }
            }
        }
    }

    private static void DrawSmartArtNodeBox(DrawingContext context, FloatingSmartArtData smartArt, int nodeIndex, Rect nodeRect)
    {
        var plan = SmartArtPlanAt(smartArt, nodeIndex);
        var fill = new SolidColorBrush(plan is null ? SmartArtFillAt(smartArt, nodeIndex) : ToAvaloniaChartColor(plan.FillHex));
        var borderPen = plan is { BorderThickness: > 0 }
            ? new Pen(new SolidColorBrush(ToAvaloniaChartColor(plan.BorderHex)), plan.BorderThickness)
            : null;
        var radius = plan?.CornerRadius ?? 0;

        if (plan is { ShadowOpacity: > 0 })
        {
            var blur = Math.Clamp(plan.ShadowBlur, 0, 12);
            var depth = Math.Clamp(plan.ShadowDepth, 0, 5);
            var softRect = OffsetAndInflate(nodeRect, depth, depth, blur * 0.12);
            context.DrawRectangle(EffectBrush(Colors.Black, plan.ShadowOpacity * 0.24), null, new RoundedRect(softRect, radius + blur * 0.12));
            var hardRect = OffsetAndInflate(nodeRect, depth * 0.6, depth * 0.6, 0);
            context.DrawRectangle(EffectBrush(Colors.Black, plan.ShadowOpacity * 0.18), null, new RoundedRect(hardRect, radius));
        }

        context.DrawRectangle(fill, borderPen, new RoundedRect(nodeRect, radius));
    }

    private static Color SmartArtTextColorAt(FloatingSmartArtData smartArt, int index) =>
        SmartArtPlanAt(smartArt, index) is { } plan
            ? ToAvaloniaChartColor(plan.TextHex)
            : smartArt.NodeTextColor;

    private static Pen SmartArtConnectorPenAt(FloatingSmartArtData smartArt, int index, double minimumThickness = 1.0)
    {
        if (SmartArtPlanAt(smartArt, index) is { } plan)
        {
            return new Pen(
                new SolidColorBrush(ToAvaloniaChartColor(plan.ConnectorHex)),
                Math.Max(minimumThickness, plan.BorderThickness > 0 ? plan.BorderThickness : minimumThickness));
        }

        return new Pen(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), minimumThickness);
    }

    private void DrawSmartArtNodeText(DrawingContext context, string text, Rect nodeRect, Color textColor)
    {
        if (string.IsNullOrEmpty(text)) return;
        var fmt = new RunFormatting { FontSizePt = 7.5, ColorHex = $"#{textColor.R:X2}{textColor.G:X2}{textColor.B:X2}", Bold = true };
        var ft  = Build(text.Length > 12 ? text[..12] + "…" : text, fmt);
        var tx  = nodeRect.X + Math.Max(2, (nodeRect.Width  - ft.WidthIncludingTrailingWhitespace) / 2);
        var ty  = nodeRect.Y + Math.Max(0, (nodeRect.Height - ft.Height) / 2);
        using (context.PushClip(nodeRect))
            context.DrawText(ft, new Point(tx, ty));
    }

    /// <summary>
    /// Renders a floating drawing group by composing the group placement with each child's offset
    /// and dispatching to the child-type-specific draw method. Z-order within the group follows child
    /// list order (no per-child z-order on the DrawingGroup model — children are always painted front-to-back).
    /// </summary>
    private void DrawFloatingGroup(DrawingContext context, FloatingGroupData gd)
    {
        // Optional: thin dashed group bounding frame for debuggability.
        context.DrawRectangle(null,
            new Pen(new SolidColorBrush(Color.FromArgb(0x44, 0x44, 0x44, 0x44)), 0.5,
                new DashStyle([2, 2], 0)),
            gd.Rect);

        foreach (var child in gd.Children)
        {
            switch (child.Kind)
            {
                case FloatingGroupChildData.ChildKind.Image:
                    DrawFloatingImage(context, child.Rect, child.Bitmap);
                    break;

                case FloatingGroupChildData.ChildKind.Shape when child.Shape is { } sd:
                    DrawFloatingShape(context, sd);
                    break;

                case FloatingGroupChildData.ChildKind.Chart when child.Chart is { } cd:
                    DrawFloatingChart(context, cd);
                    break;

                case FloatingGroupChildData.ChildKind.WordArt when child.WordArt is { } wd:
                    DrawFloatingWordArt(context, wd);
                    break;

                case FloatingGroupChildData.ChildKind.SmartArt when child.SmartArt is { } sasd:
                    DrawFloatingSmartArt(context, sasd);
                    break;
            }
        }
    }
}
