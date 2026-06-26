using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    // Gap between consecutive page rectangles (grey desk visible between them).
    private const double PageGap = 20;
    // Minimum horizontal gap between the control edge and the page left/right edge.
    private const double MinHorzGutter = 24;
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

    // Web/Draft layout constants.
    // Web: content column capped at this width (responsive up to this limit).
    private const double WebMaxContentWidth = 1000;
    // Web: small fixed left/right inset (no page chrome).
    private const double WebInset = 24;
    // Draft: even smaller left margin, no right constraint.
    private const double DraftInset = 16;

    private DocumentViewMode _viewMode = DocumentViewMode.PrintLayout;
    private bool _showParagraphMarks;

    // Standard Word font-size ladder (pt).
    private static readonly double[] FontSizeLadder = [8, 9, 10, 11, 12, 14, 16, 18, 20, 24, 28, 36, 48, 72];

    private readonly Dictionary<string, IBrush> _brushCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<PlacedChar> _placed = new();
    private readonly List<(double X, double Y, string Text, RunFormatting Fmt)> _markers = new();
    private readonly List<(Rect Rect, IBrush? Fill, bool Border)> _rects = new();
    private readonly List<(Rect Rect, Bitmap? Image)> _images = new();
    // Floating images collected during layout; rendered separately from inline images with z-order.
    // BehindText=true → drawn before body text (behind); BehindText=false → drawn after (in front).
    private readonly List<(Rect Rect, Bitmap? Image, bool BehindText, int ZOrder)> _floatingImages = new();
    // Floating shapes collected during layout; rendered in the same z-ordered passes as floating images.
    // ShapeData captures everything needed to draw the shape in Render() without re-touching the model.
    private readonly List<FloatingShapeData> _floatingShapes = new();
    // FO3: floating charts, WordArt, SmartArt, drawing groups — same z-ordered behind/in-front passes.
    private readonly List<FloatingChartData>    _floatingCharts    = new();
    private readonly List<FloatingWordArtData>  _floatingWordArts  = new();
    private readonly List<FloatingSmartArtData> _floatingSmartArts = new();
    private readonly List<FloatingGroupData>    _floatingGroups    = new();
    // AV-WRAP: unified list of wrap-exclusion zones (Square/Tight/TopAndBottom only).
    // Populated during Collect* calls; consulted by EmitLinePaged and LayoutParagraphPaged.
    // Each entry is a page-space rect + the wrapping mode (Behind/InFront entries are never added).
    private readonly List<(Rect Rect, ImageWrapping Wrapping)> _wrapExclusions = new();
    // HF: pre-computed header/footer render items (rebuilt in Relayout when PrintLayout).
    private readonly List<HfRenderItem> _headerFooterItems = new();
    // FO4: inline (non-floating) charts, WordArt, SmartArt — rendered in the text flow like inline images.
    private readonly List<FloatingChartData>    _inlineCharts    = new();
    private readonly List<FloatingWordArtData>  _inlineWordArts  = new();
    private readonly List<FloatingSmartArtData> _inlineSmartArts = new();
    private readonly Dictionary<InlineImage, Bitmap?> _bitmapCache = new();
    private readonly List<(Rect Rect, int Block, int Row, int Col)> _cellHits = new();

    // ── AV-TBL: in-place table cell caret ─────────────────────────────────────────────────────────
    // Non-null when the caret is inside a table cell. Stores the fully-qualified cell address and the
    // character offset within that cell's paragraph. When _cellCaret is set, _caret.Block = the table
    // block index and _caret.Offset = the PlacedChar.Offset emitted for that glyph (for hit-test lookup).
    private (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? _cellCaret;
    // Non-null when there is a selection anchor inside a cell (same encoding as _cellCaret).
    private (int TableBlock, int Row, int Col, int ParaIdx, int Offset)? _cellAnchor;

    private TextDocument _doc = TextDocument.CreateEmpty();
    private DocumentCommandBus _bus;
    private DocPosition _caret;
    private DocPosition? _selectionAnchor;
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
    {
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
            InvalidateLayoutAndVisual();
            ViewModeChanged?.Invoke();
        }
    }

    public sealed record CellEditRequest(int Block, int Row, int Col, string Text);

    public string GetCellText(int block, int row, int col)
    {
        if (block >= 0 && block < _doc.Blocks.Count && _doc.Blocks[block] is Table table
            && row >= 0 && row < table.Rows.Count && col >= 0 && col < table.Rows[row].Cells.Count)
            return table.Rows[row].Cells[col].PlainText;
        return string.Empty;
    }

    public void SetCellText(int block, int row, int col, string text) =>
        _bus.Execute(new CellTextCommand(block, row, col, text));

    public TextDocument Document => _doc;
    public bool CanUndo => _bus.CanUndo;
    public bool CanRedo => _bus.CanRedo;

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
        InvalidateLayoutAndVisual();
        DocumentChanged?.Invoke();
    }

    public void Undo()
    {
        if (_bus.Undo())
            ClampCaret();
    }

    public void Redo()
    {
        if (_bus.Redo())
            ClampCaret();
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
        InvalidateVisual();
        CaretMoved?.Invoke();
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
    public IReadOnlyList<(Rect Rect, bool BehindText, int ZOrder, SmartArtKind Kind, int NodeCount)> FloatingSmartArtRects
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _floatingSmartArts.Select(s => (s.Rect, s.BehindText, s.ZOrder, s.Kind, s.NodeTexts.Count)).ToList();
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
    public IReadOnlyList<(Rect Rect, SmartArtKind Kind, int NodeCount)> InlineSmartArtRects
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _inlineSmartArts.Select(s => (s.Rect, s.Kind, s.NodeTexts.Count)).ToList();
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
            return _wrapExclusions.ToList();
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
            var list = new List<(int, string)>();
            foreach (var fi in _floatingImages.Where(fi => fi.BehindText)) list.Add((fi.ZOrder, "Image"));
            foreach (var sd in _floatingShapes.Where(sd => sd.BehindText)) list.Add((sd.ZOrder, "Shape"));
            foreach (var cd in _floatingCharts.Where(c => c.BehindText)) list.Add((cd.ZOrder, "Chart"));
            foreach (var wd in _floatingWordArts.Where(w => w.BehindText)) list.Add((wd.ZOrder, "WordArt"));
            foreach (var sa in _floatingSmartArts.Where(s => s.BehindText)) list.Add((sa.ZOrder, "SmartArt"));
            foreach (var gd in _floatingGroups.Where(g => g.BehindText)) list.Add((gd.ZOrder, "Group"));
            return list.OrderBy(d => d.Item1).ToList();
        }
    }

    /// <summary>Merged in-front floating-object draw order (ZOrder, type).</summary>
    public IReadOnlyList<(int ZOrder, string TypeTag)> MergedFrontDrawOrder
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            var list = new List<(int, string)>();
            foreach (var fi in _floatingImages.Where(fi => !fi.BehindText)) list.Add((fi.ZOrder, "Image"));
            foreach (var sd in _floatingShapes.Where(sd => !sd.BehindText)) list.Add((sd.ZOrder, "Shape"));
            foreach (var cd in _floatingCharts.Where(c => !c.BehindText)) list.Add((cd.ZOrder, "Chart"));
            foreach (var wd in _floatingWordArts.Where(w => !w.BehindText)) list.Add((wd.ZOrder, "WordArt"));
            foreach (var sa in _floatingSmartArts.Where(s => !s.BehindText)) list.Add((sa.ZOrder, "SmartArt"));
            foreach (var gd in _floatingGroups.Where(g => !g.BehindText)) list.Add((gd.ZOrder, "Group"));
            return list.OrderBy(d => d.Item1).ToList();
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
                .Select(i => (i.Text, i.X, i.Y, i.Alignment, i.AvailableWidth))
                .ToList();
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
            var yWithinPagePx = runY - DeskPadding - runPageIndex * (_pageHeightPx + PageGap);
            var baselineFromTopPt = yWithinPagePx / PxPerPoint + fontSizePt;
            var yPt = pageHeightPt - baselineFromTopPt;

            pagesOps[runPageIndex].Add(new Free.Shared.Pdf.PdfText(
                Math.Max(0, xPt), yPt, fontSizePt, face, color, runText.ToString()));

            runText.Clear();
            runFmt = null;
        }

        // Glyphs are now in page-space Y (discrete multi-page layout).
        // Derive page index and within-page Y directly from the page-space Y.
        var pageStride = _pageHeightPx + PageGap; // distance between page tops in page space
        foreach (var g in glyphs)
        {
            // Invert ContentYToPageSpaceY:
            //   pageSpaceY = DeskPadding + pageIndex*(pageHeightPx+PageGap) + marginTopDip + offsetWithinPage
            var rel = g.Y - DeskPadding;
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
        _images.Clear();
        _floatingImages.Clear();
        _floatingShapes.Clear();
        _floatingCharts.Clear();
        _floatingWordArts.Clear();
        _floatingSmartArts.Clear();
        _floatingGroups.Clear();
        _wrapExclusions.Clear();
        _inlineCharts.Clear();
        _inlineWordArts.Clear();
        _inlineSmartArts.Clear();
        _cellHits.Clear();
        _headerFooterItems.Clear();

        if (_viewMode == DocumentViewMode.PrintLayout)
        {
            // ---- Print Layout: paginated white pages on a grey desk ----
            // Page geometry from the document's PageSettings: a centred page with its own margins.
            _pageWidth = Math.Max(320, _doc.Page.WidthPt * PxPerPoint);
            _pageHeightPx = Math.Max(400, _doc.Page.HeightPt * PxPerPoint);
            var marginLeft  = Math.Max(0, _doc.Page.MarginLeftPt)   * PxPerPoint;
            var marginRight = Math.Max(0, _doc.Page.MarginRightPt)  * PxPerPoint;
            _marginTopDip    = Math.Max(0, _doc.Page.MarginTopPt)    * PxPerPoint;
            _marginBottomDip = Math.Max(0, _doc.Page.MarginBottomPt) * PxPerPoint;
            // Centre the page in the available width, never closer than MinHorzGutter to the edge.
            _pageLeft = Math.Max(MinHorzGutter, (width - _pageWidth) / 2);
            _contentLeft = _pageLeft + marginLeft;
            _contentWidth = Math.Max(120, _pageWidth - marginLeft - marginRight);
        }
        else if (_viewMode == DocumentViewMode.WebLayout)
        {
            // ---- Web Layout: continuous column, capped width, no page chrome ----
            // Responsive up to WebMaxContentWidth; small fixed inset on each side.
            var colWidth = Math.Min(width - 2 * WebInset, WebMaxContentWidth);
            _pageWidth = Math.Max(320, colWidth);
            _pageHeightPx = double.MaxValue / 2; // effectively infinite — no pagination
            _marginTopDip    = WebInset;
            _marginBottomDip = WebInset;
            _pageLeft = WebInset;
            _contentLeft = WebInset;
            _contentWidth = Math.Max(120, colWidth);
        }
        else // Draft
        {
            // ---- Draft: plain left-margin continuous flow ----
            _pageWidth = Math.Max(320, width - DraftInset);
            _pageHeightPx = double.MaxValue / 2;
            _marginTopDip    = DraftInset;
            _marginBottomDip = DraftInset;
            _pageLeft = DraftInset;
            _contentLeft = DraftInset;
            _contentWidth = Math.Max(120, width - DraftInset * 2);
        }

        // AV-COL: compute multi-column geometry from PageSettings.
        // Only active in PrintLayout mode — Web/Draft always use a single column.
        {
            var pageColCount = _viewMode == DocumentViewMode.PrintLayout
                ? Math.Max(1, _doc.Page.ColumnCount)
                : 1;
            if (pageColCount > 1)
            {
                var gapDip = Math.Max(0, _doc.Page.ColumnSpacingPt * PxPerPoint);
                double colWidthDip;
                if (_doc.Page.ColumnWidthsPt is { Count: > 1 } explicitWidths
                    && explicitWidths.Count == pageColCount)
                {
                    // Unequal layout: use the narrowest column to guarantee all N columns fit.
                    colWidthDip = explicitWidths.Min() * PxPerPoint;
                }
                else
                {
                    colWidthDip = (_contentWidth - (pageColCount - 1) * gapDip) / pageColCount;
                }
                colWidthDip = Math.Max(1, colWidthDip);
                _colCount       = pageColCount;
                _colWidth       = colWidthDip;
                _colGap         = gapDip;
                _colLineBetween = _doc.Page.ColumnsLineBetween;
            }
            else
            {
                _colCount       = 1;
                _colWidth       = _contentWidth;
                _colGap         = 0;
                _colLineBetween = false;
            }
        }

        // Body text layout uses _colWidth as the per-column wrap width.
        var textWidth = _colWidth;
        // Available text-area height per page (between top and bottom margin).
        // For Web/Draft this is effectively infinite so ReserveContentY never paginates.
        var textAreaHeight = _viewMode == DocumentViewMode.PrintLayout
            ? Math.Max(40, _pageHeightPx - _marginTopDip - _marginBottomDip)
            : double.MaxValue / 2;

        // _layoutContentY tracks the "content Y" — the offset within the flowing text area
        // (0 = start of the first text area). This gets converted to page-space Y via
        // ContentYToPageSpaceY() when placing glyphs.
        _layoutContentY = 0;
        _layoutTextAreaHeight = textAreaHeight;

        var listNumber = 0;
        var prevList = ListKind.None;
        for (var blockIndex = 0; blockIndex < _doc.Blocks.Count; blockIndex++)
        {
            var block = _doc.Blocks[blockIndex];
            if (block is Paragraph paragraph)
            {
                // Route to the image-paragraph path only when the paragraph contains inline images.
                // Paragraphs whose images are ALL floating (anchored) are laid out as normal text
                // paragraphs so that the anchor content-Y is tracked; their images are collected
                // into _floatingImages by CollectFloatingImages() called from within each layout method.
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
                        // also calls CollectFloatingImages internally.
                        listNumber = 0;
                        prevList = ListKind.None;
                        LayoutImageParagraphPaged(blockIndex, paragraph, textWidth);
                        continue;
                    }
                    // Floating-only: fall through to normal paragraph layout below,
                    // which calls CollectFloatingImages at the start of EmitLinePaged.
                }

                // FO4: route paragraphs with inline charts / SmartArt / WordArt to the dedicated path.
                if (hasInlineChart || hasInlineWordArt || hasInlineSmArt)
                {
                    listNumber = 0;
                    prevList = ListKind.None;
                    LayoutInlineObjectParagraphPaged(blockIndex, paragraph, textWidth);
                    continue;
                }

                var kind = paragraph.Formatting.ListKind;
                double inset = 0;
                string? marker = null;
                if (kind != ListKind.None)
                {
                    var level = Math.Max(0, paragraph.Formatting.ListLevel);
                    inset = ListIndentStep * (level + 1);
                    if (kind is ListKind.Number or ListKind.MultiLevel)
                    {
                        listNumber = prevList is ListKind.Number or ListKind.MultiLevel ? listNumber + 1 : 1;
                        marker = $"{listNumber}.";
                    }
                    else
                    {
                        marker = "•"; // bullet
                        listNumber = 0;
                    }
                }
                else
                {
                    listNumber = 0;
                }

                prevList = kind;
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

        if (_viewMode == DocumentViewMode.PrintLayout)
        {
            // The number of column-slots used = floor(lastContentY / textAreaHeight) + 1.
            // Number of pages = ceil(slots / colCount).
            var lastSlot = _layoutContentY > 0 ? (int)(_layoutContentY / _layoutTextAreaHeight) : 0;
            var totalSlots = lastSlot + 1;
            _pageCount = Math.Max(1, (int)Math.Ceiling((double)totalSlots / _colCount));
            // Total scroll height: N pages * (pageHeight + gap) + initial DeskPadding + trailing bottom margin.
            _contentHeight = _pageCount * (_pageHeightPx + PageGap) + DeskPadding + _marginBottomDip;
        }
        else
        {
            // Web/Draft: single continuous column — total height is just the content plus margins.
            _pageCount = 1;
            _contentHeight = _layoutContentY + _marginBottomDip;
        }

        _laidOutWidth = width;

        if (_viewMode == DocumentViewMode.PrintLayout)
            BuildHeaderFooterItems();
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
    private (SectionHeadersFooters Hf, int SectionRelPageNumber, PageSettings Page)[]
        ComputePageSectionMap(IReadOnlyList<Section> sections, int pageCount)
    {
        var result = new (SectionHeadersFooters Hf, int SectionRelPageNumber, PageSettings Page)[pageCount];

        // Map each model-block index to a section index (0-based).
        // A block belongs to section[k] if it comes before the k-th SectionBreak paragraph.
        var blocks = _doc.Blocks;
        int[] blockSection = new int[blocks.Count];
        {
            int secIdx = 0;
            for (int b = 0; b < blocks.Count; b++)
            {
                blockSection[b] = secIdx;
                // A paragraph carrying a SectionBreak ends secIdx and starts secIdx+1.
                if (blocks[b] is Paragraph { SectionBreak: { } } && secIdx < sections.Count - 1)
                    secIdx++;
            }
        }

        // Determine the owning section index for each page.
        // We use _placed: for each block, find the first PlacedChar on it and read its page.
        // The first block placed on a page sets that page's section.
        int[] pageSectionIdx = new int[pageCount];
        // Track whether a page already has an assignment so first-placed-block wins.
        bool[] pageAssigned = new bool[pageCount];

        // Walk _placed in order (they are added in layout order, so earlier blocks come first).
        foreach (var pc in _placed)
        {
            if (pc.Sentinel) continue;
            var b = pc.Block;
            if (b < 0 || b >= blocks.Count) continue;
            var pg = PageIndexFromPageSpaceY(pc.Y);
            pg = Math.Clamp(pg, 0, pageCount - 1);
            if (!pageAssigned[pg])
            {
                pageSectionIdx[pg] = blockSection[b];
                pageAssigned[pg] = true;
            }
        }

        // Fill any pages that got no placed content (e.g. blank trailing pages) by
        // propagating the previous page's section forward.
        for (int pg = 1; pg < pageCount; pg++)
        {
            if (!pageAssigned[pg])
            {
                pageSectionIdx[pg] = pageSectionIdx[pg - 1];
                pageAssigned[pg] = true;
            }
        }

        // Compute sectionFirstPage[k] = first 0-based page belonging to section k.
        var sectionFirstPage = new int[Math.Max(1, sections.Count)];
        for (int pg = 0; pg < pageCount; pg++)
        {
            int sec = Math.Clamp(pageSectionIdx[pg], 0, sections.Count - 1);
            if (pg == 0 || pageSectionIdx[pg - 1] != sec)
                sectionFirstPage[sec] = pg;
        }

        // Build the result.
        for (int pg = 0; pg < pageCount; pg++)
        {
            int sec = Math.Clamp(pageSectionIdx[pg], 0, sections.Count - 1);
            var sectionHf = sections[sec].HeadersFooters;

            // AE3 fix: fall back to document-level headers when the section has no own slots.
            if (sectionHf.IsEmpty)
                sectionHf = _doc.FinalSectionHeadersFooters;

            var sectionPage = sections[sec].Page;
            int sectionRelPage = pg - sectionFirstPage[sec] + 1;
            result[pg] = (sectionHf, sectionRelPage, sectionPage);
        }

        return result;
    }

    /// <summary>
    /// Selects the correct header/footer slot pair for the given page within a section.
    /// Mirrors WPF's PaginatedEditorPanel.ResolveHfSlots: gates first-page variant on
    /// <paramref name="sectionRelativePageNumber"/> == 1 (AE2 fix), not document-page 0.
    /// </summary>
    private static (HeaderFooter? Header, HeaderFooter? Footer) ResolveHfSlotsAvalonia(
        SectionHeadersFooters sectionHf,
        int sectionRelativePageNumber,
        PageSettings pageSettings,
        bool diffOddEvenDoc)
    {
        var diffFirst = pageSettings.DifferentFirstPage;

        if (diffFirst && sectionRelativePageNumber == 1)
        {
            return (sectionHf.FirstHeader, sectionHf.FirstFooter);
        }

        if (diffOddEvenDoc && sectionRelativePageNumber % 2 == 0)
        {
            return (sectionHf.EvenHeader ?? sectionHf.Header,
                    sectionHf.EvenFooter ?? sectionHf.Footer);
        }

        return (sectionHf.Header, sectionHf.Footer);
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

        // Gather sections for per-page section mapping.
        var sections = _doc.Sections;

        // Document-level odd/even flag lives on _doc.Page.
        var diffOddEven = _doc.Page.DifferentOddEvenPages;

        // AE1 fix: build a true page→section map via section-break markers on model blocks,
        // mirroring WPF's PaginatedEditorPanel.ComputePageSectionMap.
        var pageToSection = ComputePageSectionMap(sections, _pageCount);

        for (var pi = 0; pi < _pageCount; pi++)
        {
            // Page-space top of this page.
            var pageTop = DeskPadding + pi * (_pageHeightPx + PageGap);

            // AE1: use the true owning section for this page (not an even distribution).
            var (sectionHf, sectionRelPage, sectionPage) = pageToSection[pi];

            // 1-based page number for this page (pi is 0-based).
            var pageNumber = pi + 1;

            // AE2 fix: resolve HF slots using section-relative page number, mirroring
            // WPF's PaginatedEditorPanel.ResolveHfSlots.
            HeaderFooter? header;
            HeaderFooter? footer;
            (header, footer) = ResolveHfSlotsAvalonia(sectionHf, sectionRelPage, sectionPage, diffOddEven);

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

            // Emit header.
            if (header is not null && !header.IsEmpty)
            {
                var hfY = pageTop + headerDistDip;
                EmitHfParagraphs(header, hfY, hfWidth, pageNumber, _pageCount);
            }

            // Emit footer.
            if (footer is not null && !footer.IsEmpty)
            {
                // Footer distance is from the BOTTOM of the page upward; the footer text
                // starts at: pageBottom - footerDistDip (+ a line-height offset per line).
                var pageBottom = pageTop + _pageHeightPx;
                var hfY = pageBottom - footerDistDip;
                EmitHfParagraphs(footer, hfY, hfWidth, pageNumber, _pageCount);
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
    private void EmitHfParagraphs(HeaderFooter hf, double startY, double availWidth, int pageNumber, int pageCount)
    {
        var y = startY;
        foreach (var para in hf.Paragraphs)
        {
            var pf = ResolveParagraphFmt(para);

            // Build segments split on TAB characters.
            // Each entry carries (tabStopIndex, Text, Fmt):
            //   tabStopIndex 0 → segment before the first tab (left-aligned at X=_contentLeft)
            //   tabStopIndex 1 → segment after the first tab (centre stop)
            //   tabStopIndex 2 → segment after the second tab (right stop)
            // Multiple consecutive tabs advance the index so the ordinal is always correct even
            // if some slots carry empty text (e.g. "Left\t\tRight" puts "Right" at stop 2).
            var segments = new List<(int StopIndex, string Text, RunFormatting Fmt)>();
            var sb = new System.Text.StringBuilder();
            RunFormatting segFmt = para.Runs.Count > 0 ? para.Runs[0].Formatting : RunFormatting.Default;
            var stopIndex = 0;

            foreach (var run in para.Runs)
            {
                var fieldText = ResolveHfField(run, pageNumber, pageCount);
                var text = fieldText ?? run.Text;
                if (run.Formatting.FontSizePt.HasValue)
                    segFmt = run.Formatting;

                // Split the resolved text on tab characters.
                var parts = text.Split('\t');
                for (var pi = 0; pi < parts.Length; pi++)
                {
                    sb.Append(parts[pi]);
                    if (pi < parts.Length - 1)
                    {
                        // A TAB was consumed — flush the current buffer as a segment and advance the stop index.
                        segments.Add((stopIndex, sb.ToString(), segFmt));
                        sb.Clear();
                        stopIndex++;
                    }
                }
            }
            // Flush the final (or only) segment.
            segments.Add((stopIndex, sb.ToString(), segFmt));

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
                // No tabs — use paragraph alignment as before.
                var seg = segments[0];
                if (!string.IsNullOrEmpty(seg.Text))
                {
                    _headerFooterItems.Add(new HfRenderItem
                    {
                        Text           = seg.Text,
                        Fmt            = seg.Fmt,
                        X              = _contentLeft,
                        Y              = y,
                        AvailableWidth = availWidth,
                        Alignment      = pf.Alignment,
                    });
                }
            }
            else
            {
                // Tab-split: each segment is positioned by its tab-stop ordinal.
                // StopIndex 0 → left (at _contentLeft, no stop lookup needed).
                // StopIndex 1 → first tab stop  (default: centre).
                // StopIndex 2 → second tab stop (default: right).
                foreach (var (si, text, fmt) in segments)
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
                        Text           = text,
                        Fmt            = fmt,
                        X              = itemX,
                        Y              = y,
                        AvailableWidth = 0,                  // absolute X — skip alignment offset at draw time
                        Alignment      = TextAlignment.Left, // draw loop uses X as-is (offset=0 for Left)
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
        if (_viewMode != DocumentViewMode.PrintLayout)
            return _marginTopDip + contentY;

        // AV-COL: with multi-column layout each "slot" is one column's worth of content.
        // slot = the index of the column being filled (0 = page0/col0, 1 = page0/col1, ...).
        // pageIndex = slot / _colCount  (multiple columns fill the same page).
        // offsetWithinPage = contentY mod textAreaHeight  (within the column's vertical span).
        var slot      = (int)(contentY / _layoutTextAreaHeight);
        var pageIndex = slot / _colCount;
        var offsetWithinPage = contentY - slot * _layoutTextAreaHeight;
        return DeskPadding + pageIndex * (_pageHeightPx + PageGap) + _marginTopDip + offsetWithinPage;
    }

    /// <summary>
    /// Derives the zero-based page index from a page-space Y coordinate.
    /// Inverse of <see cref="ContentYToPageSpaceY"/>.
    /// In Web/Draft modes always returns 0 (single continuous page).
    /// </summary>
    private int PageIndexFromPageSpaceY(double pageSpaceY)
    {
        if (_viewMode != DocumentViewMode.PrintLayout)
            return 0;

        // Each page occupies (pageHeightPx + PageGap) in page space, starting at DeskPadding.
        var rel = pageSpaceY - DeskPadding;
        if (rel < 0) return 0;
        return Math.Max(0, (int)(rel / (_pageHeightPx + PageGap)));
    }

    /// <summary>
    /// Advances _layoutContentY to the next page boundary if the line of <paramref name="lineHeight"/>
    /// would overflow the current page's text area. Returns the content Y at which the line should start.
    /// </summary>
    private double ReserveContentY(double lineHeight)
    {
        var posInPage = _layoutContentY % _layoutTextAreaHeight;
        if (posInPage > 0 && posInPage + lineHeight > _layoutTextAreaHeight)
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
        var posInPage = _layoutContentY % _layoutTextAreaHeight;
        if (posInPage > 0 && posInPage + lineHeight > _layoutTextAreaHeight)
            return _layoutContentY + (_layoutTextAreaHeight - posInPage);
        return _layoutContentY;
    }

    // ── AV-WRAP: wrap-exclusion helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Small horizontal gap (DIP) added between text and the edge of a Square/Tight-wrapped float.
    /// Matches Word's default wrap distance (approximately 9pt = 12 DIP).
    /// </summary>
    private const double WrapGap = 9.0;

    /// <summary>
    /// Registers a page-space rect as a text-wrap exclusion zone for the given wrapping mode.
    /// Behind and InFront objects are silently ignored (they never push text aside).
    /// </summary>
    private void AddWrapExclusion(Rect pageSpaceRect, ImageWrapping wrapping)
    {
        if (wrapping is ImageWrapping.Square or ImageWrapping.Tight or ImageWrapping.TopAndBottom)
            _wrapExclusions.Add((pageSpaceRect, wrapping));
    }

    /// <summary>
    /// For a line whose top is at <paramref name="lineTopY"/> (page-space) and whose height is
    /// <paramref name="lineHeight"/>, computes the adjusted left offset and available width
    /// after applying all registered exclusion zones for the column band
    /// [<paramref name="colLeft"/>, <paramref name="colLeft"/> + <paramref name="colW"/>).
    /// </summary>
    /// <remarks>
    /// Square/Tight floats narrowing the line: the float is classified as left or right of the
    /// line's centre point.  A float on the left pushes the left start inward; a float on the
    /// right narrows the right edge.  If two floats are present (left + right simultaneously)
    /// both adjustments are applied.  If a float spans the full column width it is treated as
    /// TopAndBottom (handled separately in <see cref="LayoutParagraphPaged"/>).
    /// </remarks>
    /// <returns>
    /// <c>leftDelta</c>: amount to ADD to the line's left start (≥ 0).<br/>
    /// <c>rightShrink</c>: amount to SUBTRACT from the line's available width (≥ 0).<br/>
    /// Neither value will make the line degenerate (minimum 20 DIP usable width is guaranteed).
    /// </returns>
    private (double LeftDelta, double RightShrink) ComputeSquareTightExclusion(
        double lineTopY, double lineHeight, double colLeft, double colW)
    {
        var lineBottomY = lineTopY + lineHeight;
        var colRight    = colLeft + colW;

        var maxLeftDelta  = 0.0;
        var maxRightShrink = 0.0;

        foreach (var (rect, wrapping) in _wrapExclusions)
        {
            // Only Square/Tight in this helper; TopAndBottom is handled in the paragraph loop.
            if (wrapping == ImageWrapping.TopAndBottom)
                continue;

            // Vertical overlap check.
            if (rect.Bottom <= lineTopY || rect.Top >= lineBottomY)
                continue;

            // Horizontal overlap with the column band?
            if (rect.Right <= colLeft || rect.Left >= colRight)
                continue;

            // Is the float on the left or right half of the column?
            var floatCentreX = rect.Left + rect.Width / 2;
            var colCentreX   = colLeft + colW / 2;

            if (floatCentreX <= colCentreX)
            {
                // Float is on the LEFT — push the line start rightward.
                var pushTo = Math.Min(rect.Right + WrapGap, colRight - 20) - colLeft;
                if (pushTo > maxLeftDelta) maxLeftDelta = pushTo;
            }
            else
            {
                // Float is on the RIGHT — reduce the available width.
                var shrinkTo = colRight - Math.Max(rect.Left - WrapGap, colLeft + 20);
                if (shrinkTo > maxRightShrink) maxRightShrink = shrinkTo;
            }
        }

        // Ensure the combined adjustment doesn't make width < 20 DIP.
        var totalShrink = maxLeftDelta + maxRightShrink;
        if (totalShrink > colW - 20)
        {
            // Scale back proportionally so at least 20 DIP remain.
            var scale = (colW - 20) / totalShrink;
            maxLeftDelta   *= scale;
            maxRightShrink *= scale;
        }

        return (maxLeftDelta, maxRightShrink);
    }

    /// <summary>
    /// Returns the page-space Y of the bottom edge of the lowest TopAndBottom exclusion zone that
    /// intersects the vertical band [<paramref name="lineTopY"/>, <paramref name="lineTopY"/> +
    /// <paramref name="lineHeight"/>), or −1 if none does.  The caller should advance
    /// <c>_layoutContentY</c> past this bottom so the line lands below the float.
    /// </summary>
    private double TopAndBottomExclusionBottom(double lineTopY, double lineHeight)
    {
        var lineBottomY = lineTopY + lineHeight;
        var maxBottom   = -1.0;
        foreach (var (rect, wrapping) in _wrapExclusions)
        {
            if (wrapping != ImageWrapping.TopAndBottom) continue;
            if (rect.Bottom <= lineTopY || rect.Top >= lineBottomY) continue;
            if (rect.Bottom > maxBottom) maxBottom = rect.Bottom;
        }
        return maxBottom;
    }

    /// <summary>
    /// Advances <c>_layoutContentY</c> past any TopAndBottom exclusion zones that would overlap
    /// a line of <paramref name="estimatedLineHeight"/> at the current position.
    /// Loops until no TopAndBottom zone overlaps, capping at 200 iterations to prevent infinite loops
    /// for pathological documents.
    /// Only active when there are TopAndBottom exclusions registered.
    /// </summary>
    private void AdvancePastTopAndBottomExclusions(double estimatedLineHeight)
    {
        if (_wrapExclusions.Count == 0) return;
        for (var guard = 0; guard < 200; guard++)
        {
            var peekContentY  = PeekFirstLineContentY(estimatedLineHeight);
            var peekPageSpaceY = ContentYToPageSpaceY(peekContentY);
            var exclusionBottom = TopAndBottomExclusionBottom(peekPageSpaceY, estimatedLineHeight);
            if (exclusionBottom < 0) break; // no overlap → done

            // Convert the exclusion bottom (page-space) back to content-space and advance past it.
            // Content Y = slot * textAreaHeight + offsetWithinPage.
            // For the simple case: advance _layoutContentY so that PeekFirstLineContentY would land
            // below exclusionBottom. We find the content Y that would produce a page-space Y of
            // exclusionBottom by inverting ContentYToPageSpaceY.
            // PageSpaceY = DeskPadding + pageIndex*(pageHeight+gap) + marginTopDip + offsetWithinPage
            // We read the page index from the current slot, then:
            // offsetWithinPage = exclusionBottom - DeskPadding - pageIndex*(pageHeight+gap) - marginTopDip
            var slot          = (int)(peekContentY / _layoutTextAreaHeight);
            var pageIndex     = _colCount > 1 ? slot / _colCount : slot;
            var pageTop       = _viewMode == DocumentViewMode.PrintLayout
                                    ? DeskPadding + pageIndex * (_pageHeightPx + PageGap)
                                    : 0;
            var offsetInPage  = exclusionBottom - pageTop - _marginTopDip;
            var targetContentY = slot * _layoutTextAreaHeight + Math.Max(0, offsetInPage);
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
        CollectFloatingImages(paragraph, anchorContentY);
        CollectFloatingShapes(paragraph, anchorContentY);
        // FO3: collect charts, WordArt, SmartArt, and drawing groups at the same accurate anchor Y.
        CollectFloatingCharts(paragraph, anchorContentY);
        CollectFloatingWordArts(paragraph, anchorContentY);
        CollectFloatingSmartArts(paragraph, anchorContentY);
        CollectFloatingGroups(paragraph, anchorContentY);

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
        for (var c = 0; c < cells.Count; c++)
        {
            var ft = Build(cells[c].Ch.ToString(), cells[c].Fmt);
            measured[c] = ft.WidthIncludingTrailingWhitespace;
            heights[c] = ft.Height;
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
            var (ld, rs)   = ComputeSquareTightExclusion(peekPageSpaceY, estimatedH, cLeft, wrapColW);
            return Math.Max(20, baseAlignWidth - ld - rs);
        }

        while (i < cells.Count)
        {
            if (cells[i].Ch == ' ')
                lastBreak = i;

            // OO2/OO3 fix: for each line compute how much of availableWidth is consumed by
            // the per-line left indent BEYOND paraLeftInset.  The wrapping budget (lineAvail)
            // is already reduced for the first-line positive indent; hanging-indent continuation
            // lines also need the same reduction so they do not overshoot the right margin.
            // lineExtraInset is the extra indent relative to paraLeftInset for this line:
            //   • line 0 + positive first-line indent  → indentFirst  (normal first-line indent)
            //   • line > 0 + hanging indent (negative) → -indentFirst (continuation shifted right)
            //   • everything else                      → 0
            var lineExtraInset = (lineIndex == 0 && indentFirst > 0) ? indentFirst :
                                 (lineIndex  > 0 && indentFirst < 0) ? -indentFirst : 0.0;
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
                for (var k = lineStart; k < i; k++)
                    lineWidth += measured[k];
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
        var (wrapLeftDelta, wrapRightShrink) = _wrapExclusions.Count > 0
            ? ComputeSquareTightExclusion(pageSpaceY, lineHeight, colLeft, colW)
            : (0.0, 0.0);
        var effectiveLeftInset = leftInset + wrapLeftDelta;
        var effectiveWidth     = availableWidth - wrapLeftDelta - wrapRightShrink;
        if (effectiveWidth < 20) effectiveWidth = 20; // safety floor

        var x = colLeft + effectiveLeftInset + AlignmentOffset(alignment, effectiveWidth, lineWidth, isLast);
        for (var c = from; c < to; c++)
        {
            _placed.Add(new PlacedChar(blockIndex, c, x, pageSpaceY, measured[c], lineHeight, cells[c].Fmt, cells[c].Ch, Sentinel: false));
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
        var borders = table.Formatting.Borders;
        var headerOffset = table.Formatting.HeaderRow ? 1 : 0;
        // AV-TBL: glyphOffset is unique within this table block and is used as PlacedChar.Offset so
        // TryGetCaretRect can match (Block == tableBlockIndex && Offset == glyphOffset).
        var glyphOffset = 0;

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            var isHeader = table.Formatting.HeaderRow && r == 0;
            var isBand = table.Formatting.BandedRows && !isHeader && (r - headerOffset) % 2 == 1;

            // AV-TBL: carry the TableCell model reference and actual column index so we can emit
            // per-paragraph, per-character cell-aware PlacedChars for caret routing.
            var measured = new List<(TableCell Cell, int StartCol, int Span, List<(double Height, List<(char Ch, double W)> Chars)> Lines, RunFormatting Fmt)>();
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

                var lines = WrapCellLines(cell.PlainText, fmt, Math.Max(10, cellWidth - 2 * pad));
                var cellHeight = lines.Sum(l => l.Height) + 2 * pad;
                if (cellHeight > rowHeight)
                    rowHeight = cellHeight;

                measured.Add((cell, col, span, lines, fmt));
                col += span;
            }

            // Treat the row as a unit: reserve space on the current page (or push to next).
            var rowContentY = ReserveContentY(rowHeight);
            var rowPageSpaceY = ContentYToPageSpaceY(rowContentY);

            // AV-COL-NONTXT AG1: use the column band that this row's content-Y falls in.
            var rowColLeft = ColumnLeftFor(rowContentY);

            foreach (var (cellModel, startCol, span, lines, fmt) in measured)
            {
                double cellWidth = 0;
                for (var s = 0; s < span; s++)
                    cellWidth += colWidths[startCol + s];
                var cellX = rowColLeft + colOffsets[startCol];
                var rect = new Rect(cellX, rowPageSpaceY, cellWidth, rowHeight);
                IBrush? fill = isHeader ? HeaderFill : isBand ? BandFill : null;
                _rects.Add((rect, fill, borders));
                _cellHits.Add((rect, blockIndex, r, startCol));

                var ty = rowPageSpaceY + pad;
                // AV-TBL: walk paragraphs so each PlacedChar carries its paragraph index and
                // character offset within that paragraph. WrapCellLines flattens all paragraphs
                // via cell.PlainText which joins them with '\n'. We skip '\n' separator chars
                // (they belong to neither paragraph) and advance the paragraph index after each.
                var paraIdx = 0;
                var paraCharOffset = 0; // char position within current paragraph
                var parasLengths = cellModel.Paragraphs.Count > 0
                    ? cellModel.Paragraphs.Select(p => p.PlainText.Length).ToList()
                    : new List<int> { 0 };

                foreach (var (lineHeight, chars) in lines)
                {
                    var tx = cellX + pad;
                    foreach (var (ch, w) in chars)
                    {
                        // Skip '\n' separator: it joins paragraphs in cell.PlainText but is not
                        // part of any paragraph's own text content.
                        if (ch == '\n')
                        {
                            if (paraIdx < parasLengths.Count - 1)
                            {
                                paraIdx++;
                                paraCharOffset = 0;
                            }
                            // Do not emit a PlacedChar for the separator; skip it visually too.
                            continue;
                        }

                        // Advance paragraph index if we've consumed this paragraph's text (safety).
                        while (paraIdx < parasLengths.Count - 1 && paraCharOffset >= parasLengths[paraIdx])
                        {
                            paraIdx++;
                            paraCharOffset = 0;
                        }
                        _placed.Add(new PlacedChar(blockIndex, glyphOffset, tx, ty, w, lineHeight, fmt, ch,
                            Sentinel: false, CellRow: r, CellCol: startCol, CellParaIdx: paraIdx, CellParaOffset: paraCharOffset));
                        glyphOffset++;
                        paraCharOffset++;
                        tx += w;
                    }

                    ty += lineHeight;
                }

                // Emit a sentinel glyph for each paragraph in the cell (at the end of last para, or after all text).
                // This allows the caret to sit after the last character in the cell.
                // We emit exactly one sentinel per cell paragraph so caret can land "after" each para.
                var sentinelParaIdx = parasLengths.Count - 1;
                var sentinelParaOffset = parasLengths.Count > 0 ? parasLengths[sentinelParaIdx] : 0;
                // Position the sentinel at the right edge of the last text line, or at pad if no text.
                var sentinelX = ty > rowPageSpaceY + pad
                    ? columnLeft[startCol] + pad  // start of next line
                    : columnLeft[startCol] + pad;
                var sentinelY = lines.Count > 0 ? rowPageSpaceY + pad + lines.Sum(l => l.Height) - lines[^1].Height : rowPageSpaceY + pad;
                var sentinelH = lines.Count > 0 ? lines[^1].Height : Build("A", fmt).Height;
                // Advance sentinel X past last character on last line
                if (lines.Count > 0)
                {
                    var lastLineChars = lines[^1].Chars;
                    var lastX = columnLeft[startCol] + pad + lastLineChars.Sum(c => c.W);
                    sentinelX = lastX;
                }
                _placed.Add(new PlacedChar(blockIndex, glyphOffset, sentinelX, sentinelY, 0, sentinelH, fmt, '\0',
                    Sentinel: true, CellRow: r, CellCol: startCol, CellParaIdx: sentinelParaIdx, CellParaOffset: sentinelParaOffset));
                glyphOffset++;
            }

            _layoutContentY = rowContentY + rowHeight;
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
        CollectFloatingImages(paragraph, anchorContentY);
        CollectFloatingShapes(paragraph, anchorContentY);
        // FO3: collect charts, WordArt, SmartArt, and drawing groups at the same accurate anchor Y.
        CollectFloatingCharts(paragraph, anchorContentY);
        CollectFloatingWordArts(paragraph, anchorContentY);
        CollectFloatingSmartArts(paragraph, anchorContentY);
        CollectFloatingGroups(paragraph, anchorContentY);

        foreach (var run in paragraph.Runs)
        {
            if (run.Image is not { IsFloating: false } image)
                continue; // Skip floating images — they are handled by CollectFloatingImages.

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
        CollectFloatingImages(paragraph, anchorContentY);
        CollectFloatingShapes(paragraph, anchorContentY);
        CollectFloatingCharts(paragraph, anchorContentY);
        CollectFloatingWordArts(paragraph, anchorContentY);
        CollectFloatingSmartArts(paragraph, anchorContentY);
        CollectFloatingGroups(paragraph, anchorContentY);

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

                // Flatten nodes depth-first (mirrors CollectFloatingSmartArts).
                var texts = new List<string>();
                static void FlattenNodes(IEnumerable<SmartArtNode> nodes, List<string> into)
                {
                    foreach (var n in nodes) { into.Add(n.Text); FlattenNodes(n.Children, into); }
                }
                FlattenNodes(sa.Nodes, texts);

                _inlineSmartArts.Add(new FloatingSmartArtData
                {
                    Rect      = rect,
                    BehindText = false,
                    ZOrder     = 0,
                    Kind       = sa.Kind,
                    NodeTexts  = texts,
                });

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

    /// <summary>
    /// Scans <paramref name="paragraph"/> for floating images and appends each one to
    /// <c>_floatingImages</c> with its page-space rect computed from <see cref="FloatingPlacement"/>.
    /// <paramref name="anchorContentY"/> is the content-space Y at which the paragraph starts —
    /// used as the vertical reference when <see cref="VerticalAnchor.Paragraph"/> is set.
    /// </summary>
    private void CollectFloatingImages(Paragraph paragraph, double anchorContentY)
    {
        // Page index for the anchor paragraph.
        var anchorPageIndex = _viewMode == DocumentViewMode.PrintLayout
            ? (int)(anchorContentY / _layoutTextAreaHeight)
            : 0;

        // Page top in page-space (DeskPadding + pageIndex*(pageHeight+gap)).
        double PageTop(int pi) =>
            _viewMode == DocumentViewMode.PrintLayout
                ? DeskPadding + pi * (_pageHeightPx + PageGap)
                : 0;

        foreach (var run in paragraph.Runs)
        {
            if (run.Image is not { IsFloating: true } img)
                continue;

            var imgW = img.WidthPt  > 0 ? img.WidthPt  * PxPerPoint : 120;
            var imgH = img.HeightPt > 0 ? img.HeightPt * PxPerPoint :  80;

            // ── Horizontal position ──────────────────────────────────────────────────
            double x = img.HorizontalAnchor switch
            {
                HorizontalAnchor.Page   => _pageLeft  + img.HorizontalOffsetPt * PxPerPoint,
                HorizontalAnchor.Margin => _contentLeft + img.HorizontalOffsetPt * PxPerPoint,
                _                       => _contentLeft + img.HorizontalOffsetPt * PxPerPoint, // Column (default)
            };

            // ── Vertical position ────────────────────────────────────────────────────
            double y = img.VerticalAnchor switch
            {
                // Paragraph anchor: offset from the page-space Y of the anchor paragraph.
                VerticalAnchor.Paragraph =>
                    ContentYToPageSpaceY(anchorContentY) + img.VerticalOffsetPt * PxPerPoint,

                // Margin anchor: offset from the top-margin edge on the anchor's page.
                VerticalAnchor.Margin =>
                    PageTop(anchorPageIndex) + _marginTopDip + img.VerticalOffsetPt * PxPerPoint,

                // Page anchor: offset from the physical page top on the anchor's page.
                VerticalAnchor.Page =>
                    PageTop(anchorPageIndex) + img.VerticalOffsetPt * PxPerPoint,

                _ => ContentYToPageSpaceY(anchorContentY) + img.VerticalOffsetPt * PxPerPoint,
            };

            var rect = new Rect(x, y, imgW, imgH);
            var behindText = img.Wrapping == ImageWrapping.Behind;
            _floatingImages.Add((rect, DecodeBitmap(img), behindText, img.ZOrderIndex));
            // AV-WRAP: register exclusion zone for text-flow wrapping (Square/Tight/TopAndBottom only).
            AddWrapExclusion(rect, img.Wrapping);
        }
    }

    /// <summary>
    /// Scans <paramref name="paragraph"/> for floating shapes and appends each to <c>_floatingShapes</c>
    /// with its page-space rect and pre-built brushes/pens. Mirrors <see cref="CollectFloatingImages"/>.
    /// <paramref name="anchorContentY"/> is the content-space Y of the paragraph start.
    /// </summary>
    private void CollectFloatingShapes(Paragraph paragraph, double anchorContentY)
    {
        var anchorPageIndex = _viewMode == DocumentViewMode.PrintLayout
            ? (int)(anchorContentY / _layoutTextAreaHeight)
            : 0;

        double PageTop(int pi) =>
            _viewMode == DocumentViewMode.PrintLayout
                ? DeskPadding + pi * (_pageHeightPx + PageGap)
                : 0;

        foreach (var run in paragraph.Runs)
        {
            if (run.Shape is not { IsFloating: true } shape)
                continue;

            var pl = shape.Placement!; // guaranteed non-null when IsFloating

            var shapeW = shape.WidthPt  > 0 ? shape.WidthPt  * PxPerPoint : 120;
            var shapeH = shape.HeightPt > 0 ? shape.HeightPt * PxPerPoint :  80;

            // ── Horizontal position ──────────────────────────────────────────────────
            double x = pl.HorizontalAnchor switch
            {
                HorizontalAnchor.Page   => _pageLeft    + pl.HorizontalOffsetPt * PxPerPoint,
                HorizontalAnchor.Margin => _contentLeft + pl.HorizontalOffsetPt * PxPerPoint,
                _                       => _contentLeft + pl.HorizontalOffsetPt * PxPerPoint, // Column
            };

            // ── Vertical position ────────────────────────────────────────────────────
            double y = pl.VerticalAnchor switch
            {
                VerticalAnchor.Paragraph =>
                    ContentYToPageSpaceY(anchorContentY) + pl.VerticalOffsetPt * PxPerPoint,

                VerticalAnchor.Margin =>
                    PageTop(anchorPageIndex) + _marginTopDip + pl.VerticalOffsetPt * PxPerPoint,

                VerticalAnchor.Page =>
                    PageTop(anchorPageIndex) + pl.VerticalOffsetPt * PxPerPoint,

                _ => ContentYToPageSpaceY(anchorContentY) + pl.VerticalOffsetPt * PxPerPoint,
            };

            var rect = new Rect(x, y, shapeW, shapeH);
            var behindText = pl.Wrapping == ImageWrapping.Behind;

            // ── Fill brush ───────────────────────────────────────────────────────────
            IBrush? fillBrush = null;
            if (shape.ExtendedFill is { } extFill)
            {
                fillBrush = extFill.Kind switch
                {
                    ShapeFillKind.NoFill   => null,
                    ShapeFillKind.Gradient => BuildAvaloniaGradientBrush(extFill, rect),
                    ShapeFillKind.Pattern  => BuildAvaloniaPatternBrush(extFill),
                    _                      => ParseSolidBrush(shape.FillColorHex),
                };
            }
            else if (!string.IsNullOrEmpty(shape.FillColorHex))
            {
                fillBrush = ParseSolidBrush(shape.FillColorHex);
            }

            // ── Outline pen ──────────────────────────────────────────────────────────
            Pen? outlinePen = null;
            if (!string.IsNullOrEmpty(shape.OutlineColorHex))
            {
                var strokeBrush = ParseSolidBrush(shape.OutlineColorHex);
                var strokeW = shape.OutlineWidthPt > 0 ? shape.OutlineWidthPt * PxPerPoint : 1.0;
                DashStyle? dashStyle = shape.OutlineDash?.ToLowerInvariant() switch
                {
                    "dash"        => new DashStyle([4, 3], 0),
                    "sysdot"      => new DashStyle([1, 2], 0),
                    "dashDot"
                    or "dashdot"  => new DashStyle([4, 2, 1, 2], 0),
                    _             => null,
                };
                outlinePen = dashStyle is not null
                    ? new Pen(strokeBrush, strokeW, dashStyle)
                    : new Pen(strokeBrush, strokeW);
            }

            // ── Text ─────────────────────────────────────────────────────────────────
            var text = shape.HasText ? shape.PlainText : null;

            _floatingShapes.Add(new FloatingShapeData
            {
                Rect          = rect,
                BehindText    = behindText,
                ZOrder        = pl.ZOrderIndex,
                Kind          = shape.Kind,
                CustomGeo     = shape.HasCustomGeometry ? shape.CustomGeometry : null,
                FillBrush     = fillBrush,
                OutlinePen    = outlinePen,
                Text          = text,
                RotationAngle = shape.RotationAngle,
                FlipH         = shape.FlipH,
                FlipV         = shape.FlipV,
            });
            // AV-WRAP: register exclusion zone.
            AddWrapExclusion(rect, pl.Wrapping);
        }
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

    private static IBrush HeaderFill { get; } = new SolidColorBrush(Color.FromRgb(0xDE, 0xE9, 0xF7));
    private static IBrush BandFill { get; } = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));
    private static Pen TableBorderPen { get; } = new Pen(new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A)), 0.75);
    private static IBrush PageDeskBrush   { get; } = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));
    private static IBrush PageShadowBrush { get; } = new SolidColorBrush(Color.FromArgb(0x55, 0x00, 0x00, 0x00));
    private static Pen    PageBorderPen   { get; } = new Pen(new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)), 0.5);
    // AV-COL: thin gray rule drawn in each inter-column gap when ColumnsLineBetween is set.
    private static Pen    ColumnRulePen   { get; } = new Pen(new SolidColorBrush(Colors.Gray), 1.0);

    // ---- Render ---------------------------------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        if (_laidOutWidth < 0 || Math.Abs(_laidOutWidth - Bounds.Width) > 0.5)
            Relayout(Bounds.Width > 0 ? Bounds.Width : FallbackWidth);

        if (_viewMode == DocumentViewMode.PrintLayout)
        {
            // Grey desk fills the full control area.
            context.FillRectangle(PageDeskBrush, new Rect(Bounds.Size));

            // Draw each discrete page rectangle: white page with drop-shadow + border.
            for (var pi = 0; pi < _pageCount; pi++)
            {
                var pageTop = DeskPadding + pi * (_pageHeightPx + PageGap);
                var pageRect   = new Rect(_pageLeft, pageTop, _pageWidth, _pageHeightPx);
                var shadowRect = new Rect(_pageLeft + 3, pageTop + 3, _pageWidth, _pageHeightPx);
                context.FillRectangle(PageShadowBrush, shadowRect);
                context.FillRectangle(Brushes.White, pageRect);
                context.DrawRectangle(null, PageBorderPen, pageRect);
            }
        }
        else
        {
            // Web Layout / Draft: plain white background — no desk, no page chrome.
            context.FillRectangle(Brushes.White, new Rect(Bounds.Size));
        }

        // Table fills + borders sit beneath the text.
        foreach (var (rect, fill, border) in _rects)
        {
            if (fill is not null)
                context.FillRectangle(fill, rect);
            if (border)
                context.DrawRectangle(null, TableBorderPen, rect);
        }

        // AV-COL: draw column rules (vertical divider lines in each inter-column gap) when enabled.
        // One rule per gap, centred horizontally, running the full text-area height on each page.
        if (_viewMode == DocumentViewMode.PrintLayout && _colCount > 1 && _colLineBetween)
        {
            for (var pi = 0; pi < _pageCount; pi++)
            {
                var pageTop = DeskPadding + pi * (_pageHeightPx + PageGap);
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

        // Behind-text pass: merge ALL six floating types into ONE list sorted by ZOrderIndex.
        // UU1 fix: previously images and shapes were drawn in two separate OrderBy loops so any
        // shape always painted over any image regardless of ZOrder.
        // XX1 fix: the FO3 wave (charts/WordArt/SmartArt/groups) was added as FOUR SEPARATE
        // post-pass loops, re-introducing the same UU1 bug.  The WPF reference
        // (SyncFloatingObjectsCanvas) adds ALL types to ONE list and sorts by ZOrder — we do the
        // same here so all six types interleave correctly within each band.
        {
            var behindDraws = new List<(int ZOrder, Action Draw)>();
            foreach (var fi in _floatingImages.Where(fi => fi.BehindText))
            {
                var (rect, bitmap, _, _) = fi;
                behindDraws.Add((fi.ZOrder, () => DrawFloatingImage(context, rect, bitmap)));
            }
            foreach (var sd in _floatingShapes.Where(sd => sd.BehindText))
            {
                var captured = sd;
                behindDraws.Add((sd.ZOrder, () => DrawFloatingShape(context, captured)));
            }
            foreach (var cd in _floatingCharts.Where(c => c.BehindText))
            {
                var captured = cd;
                behindDraws.Add((cd.ZOrder, () => DrawFloatingChart(context, captured)));
            }
            foreach (var wd in _floatingWordArts.Where(w => w.BehindText))
            {
                var captured = wd;
                behindDraws.Add((wd.ZOrder, () => DrawFloatingWordArt(context, captured)));
            }
            foreach (var sd in _floatingSmartArts.Where(s => s.BehindText))
            {
                var captured = sd;
                behindDraws.Add((sd.ZOrder, () => DrawFloatingSmartArt(context, captured)));
            }
            foreach (var gd in _floatingGroups.Where(g => g.BehindText))
            {
                var captured = gd;
                behindDraws.Add((gd.ZOrder, () => DrawFloatingGroup(context, captured)));
            }
            foreach (var (_, draw) in behindDraws.OrderBy(d => d.ZOrder))
                draw();
        }

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

        var selection = NormalizedSelection();
        foreach (var pc in _placed)
        {
            if (pc.Sentinel)
                continue;

            if (selection is { } sel && IsWithin(sel, pc.Block, pc.Offset))
                context.FillRectangle(SelectionBrush, new Rect(pc.X, pc.Y, Math.Max(2, pc.W), pc.LineHeight));

            // Highlight: fill a background rect behind the glyph before drawing text.
            if (!string.IsNullOrEmpty(pc.Fmt.HighlightColorHex))
            {
                var hlBrush = BrushFor(pc.Fmt.HighlightColorHex);
                context.FillRectangle(hlBrush, new Rect(pc.X, pc.Y, Math.Max(1, pc.W), pc.LineHeight));
            }

            // Superscript/subscript: draw at a smaller size + vertical offset.
            // Word approximation: ~58% of the font size, raised/lowered by ~33% of line height.
            var drawFmt = pc.Fmt;
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

            var ft = Build(pc.Ch.ToString(), drawFmt);
            context.DrawText(ft, new Point(pc.X, drawY));

            if (pc.Fmt.Underline)
                DrawDecoration(context, pc, pc.Y + pc.LineHeight * 0.82);
            if (pc.Fmt.Strikethrough)
                DrawDecoration(context, pc, pc.Y + pc.LineHeight * 0.5);
        }

        foreach (var (mx, my, text, fmt) in _markers)
            context.DrawText(Build(text, fmt), new Point(mx, my));

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
        {
            var frontDraws = new List<(int ZOrder, Action Draw)>();
            foreach (var fi in _floatingImages.Where(fi => !fi.BehindText))
            {
                var (rect, bitmap, _, _) = fi;
                frontDraws.Add((fi.ZOrder, () => DrawFloatingImage(context, rect, bitmap)));
            }
            foreach (var sd in _floatingShapes.Where(sd => !sd.BehindText))
            {
                var captured = sd;
                frontDraws.Add((sd.ZOrder, () => DrawFloatingShape(context, captured)));
            }
            foreach (var cd in _floatingCharts.Where(c => !c.BehindText))
            {
                var captured = cd;
                frontDraws.Add((cd.ZOrder, () => DrawFloatingChart(context, captured)));
            }
            foreach (var wd in _floatingWordArts.Where(w => !w.BehindText))
            {
                var captured = wd;
                frontDraws.Add((wd.ZOrder, () => DrawFloatingWordArt(context, captured)));
            }
            foreach (var sd in _floatingSmartArts.Where(s => !s.BehindText))
            {
                var captured = sd;
                frontDraws.Add((sd.ZOrder, () => DrawFloatingSmartArt(context, captured)));
            }
            foreach (var gd in _floatingGroups.Where(g => !g.BehindText))
            {
                var captured = gd;
                frontDraws.Add((gd.ZOrder, () => DrawFloatingGroup(context, captured)));
            }
            foreach (var (_, draw) in frontDraws.OrderBy(d => d.ZOrder))
                draw();
        }

        // HF: draw header/footer items (pre-computed in BuildHeaderFooterItems). The in-front floating
        // objects are already drawn by the unified z-ordered frontDraws pass above.
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
        }

        if (IsFocused && NormalizedSelection() is null && TryGetCaretRect(out var caretRect))
            context.FillRectangle(Brushes.Black, caretRect);
    }

    /// <summary>
    /// Renders a single floating image (or a placeholder rect if the bitmap could not be decoded).
    /// Shared by the behind-text and in-front passes in <see cref="Render"/>.
    /// </summary>
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

    private void DrawDecoration(DrawingContext context, PlacedChar pc, double yLine)
    {
        var pen = new Pen(BrushFor(pc.Fmt.ColorHex), Math.Max(1, FontSizePx(pc.Fmt) / 14));
        context.DrawLine(pen, new Point(pc.X, yLine), new Point(pc.X + pc.W, yLine));
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

        if (TryHitTest(point, out var pos))
        {
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
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && TryHitTest(e.GetPosition(this), out var pos))
        {
            _caret = pos;
            InvalidateVisual();
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
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
        }
    }

    // ---- Editing operations (all via the command bus) -------------------------------------------

    public void InsertText(string text)
    {
        // AV-TBL: route into table cell when the caret is inside a cell.
        if (_cellCaret is { } cc)
        {
            var para = GetCellParagraph(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx);
            if (para == null || !IsEditable(para))
                return;
            var offset = cc.Offset;
            var fmt = ActiveFormatting(para, offset);
            _bus.Execute(new ReplaceCellParagraphRunsCommand(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx, p =>
            {
                var chars = ParaCells(p);
                foreach (var ch in text)
                    chars.Insert(Math.Clamp(offset, 0, chars.Count), new Cell(ch, fmt));
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
        var bodyFmt = ActiveFormatting(paragraph, bodyOffset);
        _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
        {
            var cells = ParaCells(p);
            foreach (var ch in text)
                cells.Insert(Math.Clamp(bodyOffset, 0, cells.Count), new Cell(ch, bodyFmt));
            SetRuns(p, cells);
        }));
        _caret = new DocPosition(block, bodyOffset + text.Length);
        _selectionAnchor = _caret;
    }

    private void Backspace()
    {
        // AV-TBL: route into table cell.
        if (_cellCaret is { } cc)
        {
            if (cc.Offset > 0)
            {
                var offset = cc.Offset;
                _bus.Execute(new ReplaceCellParagraphRunsCommand(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx, p =>
                {
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
        if (_caret.Offset > 0)
        {
            var block = _caret.Block;
            var offset = _caret.Offset;
            _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
            {
                var cells = ParaCells(p);
                if (offset - 1 < cells.Count)
                    cells.RemoveAt(offset - 1);
                SetRuns(p, cells);
            }));
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
        // AV-TBL: route into table cell.
        if (_cellCaret is { } cc)
        {
            var para = GetCellParagraph(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx);
            if (para == null || !IsEditable(para))
                return;
            var len = ParaCells(para).Count;
            if (cc.Offset < len)
            {
                var offset = cc.Offset;
                _bus.Execute(new ReplaceCellParagraphRunsCommand(cc.TableBlock, cc.Row, cc.Col, cc.ParaIdx, p =>
                {
                    var chars = ParaCells(p);
                    if (offset < chars.Count)
                        chars.RemoveAt(offset);
                    SetRuns(p, chars);
                }));
                // Caret stays at same offset (now pointing at the next char).
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
            _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
            {
                var cells = ParaCells(p);
                if (offset < cells.Count)
                    cells.RemoveAt(offset);
                SetRuns(p, cells);
            }));
        }
    }

    private void InsertParagraphBreak()
    {
        // AV-TBL: route into table cell.
        if (_cellCaret is { } cc)
        {
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
        var firstPara = new Paragraph { Formatting = paragraph.Formatting, StyleId = paragraph.StyleId };
        SetRuns(firstPara, bodyCells.Take(bodyOffset).ToList());
        var secondPara = new Paragraph { Formatting = paragraph.Formatting };
        SetRuns(secondPara, bodyCells.Skip(bodyOffset).ToList());
        _bus.Execute(new ReplaceBlocksCommand(block, 1, new Block[] { firstPara, secondPara }));
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

    private void DeleteSelection()
    {
        if (NormalizedSelection() is not { } sel)
            return;

        if (sel.Start.Block == sel.End.Block)
        {
            var block = sel.Start.Block;
            var a = sel.Start.Offset;
            var b = sel.End.Offset;
            _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
            {
                var cells = ParaCells(p);
                var lo = Math.Clamp(a, 0, cells.Count);
                var hi = Math.Clamp(b, 0, cells.Count);
                cells.RemoveRange(lo, Math.Max(0, hi - lo));
                SetRuns(p, cells);
            }));
            _caret = new DocPosition(block, a);
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

    public void SetAlignment(TextAlignment alignment)
    {
        if (CurrentParagraph() is not { } paragraph)
            return;
        _bus.Execute(new SetParagraphFormattingCommand(_caret.Block, paragraph.Formatting with { Alignment = alignment }));
    }

    public void SetSelectionFontSize(double points) => ApplyRunFormatting(f => f with { FontSizePt = points });

    /// <summary>Insert a bordered table (with a header row) after the current block. Cells edit on double-click.</summary>
    public void InsertTable(int rows, int columns)
    {
        var table = Table.Create(Math.Max(1, rows), Math.Max(1, columns));
        table.Formatting = TableFormatting.Default with { Borders = true, HeaderRow = true };
        var insertAt = Math.Clamp(_caret.Block + 1, 0, _doc.Blocks.Count);
        _bus.Execute(new InsertBlockCommand(insertAt, table));
    }

    /// <summary>Toggle the current paragraph's list kind (bullet/number); re-applying the same kind clears it.</summary>
    public void ToggleList(ListKind kind)
    {
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return;
        var newKind = paragraph.Formatting.ListKind == kind ? ListKind.None : kind;
        _bus.Execute(new SetParagraphFormattingCommand(_caret.Block, paragraph.Formatting with { ListKind = newKind }));
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
        if (NormalizedSelection() is null)
            return false;
        DeleteSelection();
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
            _bus.Execute(new FormatParagraphRunsCommand(_caret.Block, transform));
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
            var cells = ParaCells(paragraph);
            var newValue = !AllSet(cells, 0, cells.Count, get);
            _bus.Execute(new FormatParagraphRunsCommand(_caret.Block, f => set(f, newValue)));
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
    private static bool IsEditable(Paragraph paragraph) =>
        paragraph.Runs.All(r => r.Image is null && r.Equation is null && r.FieldKind == RunFieldKind.None
            && r.FootnoteId is null && r.EndnoteId is null && r.CommentId is null && r.Control is null);

    private static List<Cell> ParaCells(Paragraph paragraph)
    {
        var cells = new List<Cell>();
        foreach (var run in paragraph.Runs)
            foreach (var ch in run.Text)
                cells.Add(new Cell(ch, run.Formatting));
        return cells;
    }

    private static List<Cell> FallbackCells(string text)
    {
        var cells = new List<Cell>(text.Length);
        foreach (var ch in text)
            cells.Add(new Cell(ch, RunFormatting.Default));
        return cells;
    }

    private static void SetRuns(Paragraph paragraph, IReadOnlyList<Cell> cells)
    {
        paragraph.Runs.Clear();
        var i = 0;
        while (i < cells.Count)
        {
            var fmt = cells[i].Fmt;
            var start = i;
            while (i < cells.Count && cells[i].Fmt.Equals(fmt))
                i++;
            var text = new string(cells.Skip(start).Take(i - start).Select(c => c.Ch).ToArray());
            paragraph.Runs.Add(new Run(text, fmt));
        }
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

    private readonly record struct Cell(char Ch, RunFormatting Fmt);

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
        int CellParaOffset = -1)
    {
        /// <summary>True when this glyph is inside a table cell (as opposed to a body paragraph).</summary>
        public bool IsCell => CellRow >= 0;
    }

    private sealed class ViewContext(DocumentView view) : IDocumentCommandContext
    {
        public TextDocument Document => view._doc;
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
    }

    // ── Floating shape data captured during layout ────────────────────────────────────────────────
    // Stores everything needed to draw a floating shape in Render() without re-touching the model.

    private sealed class FloatingShapeData
    {
        public Rect Rect;           // page-space bounding rect
        public bool BehindText;     // true → draw before body text; false → draw after
        public int ZOrder;

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
    }

    // ── FO3: data classes for floating charts, WordArt, SmartArt, and drawing groups ───────────────

    private sealed class FloatingChartData
    {
        public Rect         Rect;
        public bool         BehindText;
        public int          ZOrder;
        public ChartKind    Kind;
        public string?      Title;
        public List<string> Categories = [];
        public List<(string? Name, List<double> Values)> Series = [];
        // AV-POLISH: chart annotation fields
        public bool    ShowLegend;
        public bool    ShowDataLabels;
        public string? CategoryAxisTitle;
        public string? ValueAxisTitle;
    }

    private sealed class FloatingWordArtData
    {
        public Rect         Rect;
        public bool         BehindText;
        public int          ZOrder;
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
        public SmartArtKind     Kind;
        // Flattened node texts (first-level nodes + their children depth-first).
        public List<string>     NodeTexts = [];
    }

    private sealed class FloatingGroupChildData
    {
        // Resolved page-space sub-rect for this child (group origin + child offset).
        public Rect Rect;
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
        public List<FloatingGroupChildData> Children = [];
    }

    // ── FO3 collection helpers ────────────────────────────────────────────────────────────────────

    /// <summary>Resolves floating-placement page-space position. Mirrors CollectFloatingShapes anchor logic.</summary>
    private (double X, double Y) ResolveFloatingPos(FloatingPlacement pl, double anchorContentY)
    {
        var anchorPageIndex = _viewMode == DocumentViewMode.PrintLayout
            ? (int)(anchorContentY / _layoutTextAreaHeight)
            : 0;

        double PageTop(int pi) =>
            _viewMode == DocumentViewMode.PrintLayout
                ? DeskPadding + pi * (_pageHeightPx + PageGap)
                : 0;

        double x = pl.HorizontalAnchor switch
        {
            HorizontalAnchor.Page   => _pageLeft    + pl.HorizontalOffsetPt * PxPerPoint,
            HorizontalAnchor.Margin => _contentLeft + pl.HorizontalOffsetPt * PxPerPoint,
            _                       => _contentLeft + pl.HorizontalOffsetPt * PxPerPoint,
        };

        double y = pl.VerticalAnchor switch
        {
            VerticalAnchor.Paragraph =>
                ContentYToPageSpaceY(anchorContentY) + pl.VerticalOffsetPt * PxPerPoint,
            VerticalAnchor.Margin =>
                PageTop(anchorPageIndex) + _marginTopDip + pl.VerticalOffsetPt * PxPerPoint,
            VerticalAnchor.Page =>
                PageTop(anchorPageIndex) + pl.VerticalOffsetPt * PxPerPoint,
            _ => ContentYToPageSpaceY(anchorContentY) + pl.VerticalOffsetPt * PxPerPoint,
        };

        return (x, y);
    }

    /// <summary>Collects floating charts anchored to <paramref name="paragraph"/>.</summary>
    private void CollectFloatingCharts(Paragraph paragraph, double anchorContentY)
    {
        foreach (var run in paragraph.Runs)
        {
            if (run.Chart is not { IsFloating: true } chart)
                continue;

            var pl = chart.Placement!;
            var w  = chart.WidthPt  > 0 ? chart.WidthPt  * PxPerPoint : 360 * PxPerPoint;
            var h  = chart.HeightPt > 0 ? chart.HeightPt * PxPerPoint : 216 * PxPerPoint;
            var (x, y) = ResolveFloatingPos(pl, anchorContentY);

            var series = chart.Series.Select(s => (s.Name, new List<double>(s.Values))).ToList();
            var chartRect = new Rect(x, y, w, h);
            _floatingCharts.Add(BuildChartData(chart, chartRect, pl.Wrapping == ImageWrapping.Behind, pl.ZOrderIndex, series));
            // AV-WRAP: register exclusion zone.
            AddWrapExclusion(chartRect, pl.Wrapping);
        }
    }

    /// <summary>Collects floating WordArt anchored to <paramref name="paragraph"/>.</summary>
    private void CollectFloatingWordArts(Paragraph paragraph, double anchorContentY)
    {
        foreach (var run in paragraph.Runs)
        {
            if (run.WordArt is not { IsFloating: true } wa)
                continue;

            var pl = wa.Placement!;
            // WordArt width: estimate from text length × font size × scaling factor (no explicit WidthPt on model).
            var w = Math.Max(72, wa.FontSizePt * Math.Max(1, wa.Text.Length) * 0.62) * PxPerPoint;
            var h = Math.Max(40, wa.FontSizePt * 1.6) * PxPerPoint;
            var (x, y) = ResolveFloatingPos(pl, anchorContentY);

            var waRect = new Rect(x, y, w, h);
            _floatingWordArts.Add(new FloatingWordArtData
            {
                Rect       = waRect,
                BehindText = pl.Wrapping == ImageWrapping.Behind,
                ZOrder     = pl.ZOrderIndex,
                Text       = wa.Text,
                Style      = wa.Style,
                FontSizePt = wa.FontSizePt,
                Warp       = wa.Warp,
            });
            // AV-WRAP: register exclusion zone.
            AddWrapExclusion(waRect, pl.Wrapping);
        }
    }

    /// <summary>Collects floating SmartArt diagrams anchored to <paramref name="paragraph"/>.</summary>
    private void CollectFloatingSmartArts(Paragraph paragraph, double anchorContentY)
    {
        foreach (var run in paragraph.Runs)
        {
            if (run.SmartArt is not { IsFloating: true } sa)
                continue;

            var pl = sa.Placement!;
            var w  = sa.WidthPt  > 0 ? sa.WidthPt  * PxPerPoint : 468 * PxPerPoint;
            var h  = sa.HeightPt > 0 ? sa.HeightPt * PxPerPoint : 216 * PxPerPoint;
            var (x, y) = ResolveFloatingPos(pl, anchorContentY);

            // Flatten node texts depth-first for render.
            var texts = new List<string>();
            static void FlattenNodes(IEnumerable<SmartArtNode> nodes, List<string> into)
            {
                foreach (var n in nodes)
                {
                    into.Add(n.Text);
                    FlattenNodes(n.Children, into);
                }
            }
            FlattenNodes(sa.Nodes, texts);

            var saRect = new Rect(x, y, w, h);
            _floatingSmartArts.Add(new FloatingSmartArtData
            {
                Rect       = saRect,
                BehindText = pl.Wrapping == ImageWrapping.Behind,
                ZOrder     = pl.ZOrderIndex,
                Kind       = sa.Kind,
                NodeTexts  = texts,
            });
            // AV-WRAP: register exclusion zone.
            AddWrapExclusion(saRect, pl.Wrapping);
        }
    }

    /// <summary>Collects floating drawing groups anchored to <paramref name="paragraph"/>.</summary>
    private void CollectFloatingGroups(Paragraph paragraph, double anchorContentY)
    {
        foreach (var run in paragraph.Runs)
        {
            if (run.DrawingGroup is not { } grp)
                continue;

            var pl = grp.Placement;
            var gw = grp.WidthPt  > 0 ? grp.WidthPt  * PxPerPoint : 144 * PxPerPoint;
            var gh = grp.HeightPt > 0 ? grp.HeightPt * PxPerPoint :  72 * PxPerPoint;
            var (gx, gy) = ResolveFloatingPos(pl, anchorContentY);
            var groupRect = new Rect(gx, gy, gw, gh);

            var behindText = pl.Wrapping == ImageWrapping.Behind;
            var children   = new List<FloatingGroupChildData>();

            for (var i = 0; i < grp.Children.Count; i++)
            {
                var child = grp.Children[i];
                var offX  = i < grp.ChildOffsets.Count ? grp.ChildOffsets[i].X * PxPerPoint : 0;
                var offY  = i < grp.ChildOffsets.Count ? grp.ChildOffsets[i].Y * PxPerPoint : 0;
                var cw    = grp.ChildWidthPt(i)  * PxPerPoint;
                var ch    = grp.ChildHeightPt(i) * PxPerPoint;
                var childRect = new Rect(gx + offX, gy + offY, cw, ch);

                var cd = new FloatingGroupChildData { Rect = childRect };
                switch (child)
                {
                    case InlineImage img:
                        cd.Kind   = FloatingGroupChildData.ChildKind.Image;
                        cd.Bitmap = DecodeBitmap(img);
                        break;

                    case Shape s:
                    {
                        cd.Kind = FloatingGroupChildData.ChildKind.Shape;
                        IBrush? fb = null;
                        if (s.ExtendedFill is { } ef)
                        {
                            fb = ef.Kind switch
                            {
                                ShapeFillKind.NoFill   => null,
                                ShapeFillKind.Gradient => BuildAvaloniaGradientBrush(ef, childRect),
                                ShapeFillKind.Pattern  => BuildAvaloniaPatternBrush(ef),
                                _                      => ParseSolidBrush(s.FillColorHex),
                            };
                        }
                        else if (!string.IsNullOrEmpty(s.FillColorHex))
                        {
                            fb = ParseSolidBrush(s.FillColorHex);
                        }

                        Pen? op = null;
                        if (!string.IsNullOrEmpty(s.OutlineColorHex))
                        {
                            var sw = s.OutlineWidthPt > 0 ? s.OutlineWidthPt * PxPerPoint : 1.0;
                            op = new Pen(ParseSolidBrush(s.OutlineColorHex) ?? Brushes.Black, sw);
                        }

                        cd.Shape = new FloatingShapeData
                        {
                            Rect          = childRect,
                            BehindText    = behindText,
                            ZOrder        = pl.ZOrderIndex,
                            Kind          = s.Kind,
                            CustomGeo     = s.HasCustomGeometry ? s.CustomGeometry : null,
                            FillBrush     = fb,
                            OutlinePen    = op,
                            Text          = s.HasText ? s.PlainText : null,
                            RotationAngle = s.RotationAngle,
                            FlipH         = s.FlipH,
                            FlipV         = s.FlipV,
                        };
                        break;
                    }

                    case Chart c:
                        cd.Kind = FloatingGroupChildData.ChildKind.Chart;
                        cd.Chart = BuildChartData(c, childRect, behindText, pl.ZOrderIndex,
                            c.Series.Select(s => (s.Name, new List<double>(s.Values))).ToList());
                        break;

                    case WordArt wa:
                        cd.Kind = FloatingGroupChildData.ChildKind.WordArt;
                        cd.WordArt = new FloatingWordArtData
                        {
                            Rect       = childRect,
                            BehindText = behindText,
                            ZOrder     = pl.ZOrderIndex,
                            Text       = wa.Text,
                            Style      = wa.Style,
                            FontSizePt = wa.FontSizePt,
                            Warp       = wa.Warp,
                        };
                        break;

                    case SmartArt sa:
                    {
                        var texts = new List<string>();
                        static void FlattenNodes(IEnumerable<SmartArtNode> nodes, List<string> into)
                        {
                            foreach (var n in nodes) { into.Add(n.Text); FlattenNodes(n.Children, into); }
                        }
                        FlattenNodes(sa.Nodes, texts);
                        cd.Kind = FloatingGroupChildData.ChildKind.SmartArt;
                        cd.SmartArt = new FloatingSmartArtData
                        {
                            Rect       = childRect,
                            BehindText = behindText,
                            ZOrder     = pl.ZOrderIndex,
                            Kind       = sa.Kind,
                            NodeTexts  = texts,
                        };
                        break;
                    }
                }

                children.Add(cd);
            }

            _floatingGroups.Add(new FloatingGroupData
            {
                Rect       = groupRect,
                BehindText = behindText,
                ZOrder     = pl.ZOrderIndex,
                Children   = children,
            });
            // AV-WRAP: register exclusion zone using the group's overall bounding rect.
            AddWrapExclusion(groupRect, pl.Wrapping);
        }
    }

    // ── FO3 draw helpers ──────────────────────────────────────────────────────────────────────────

    // Colour palette for chart series — matches Word's default colorful1 scheme.
    private static readonly Color[] ChartSeriesColors =
    [
        Color.FromRgb(0x43, 0x72, 0xC4), // blue
        Color.FromRgb(0xED, 0x7D, 0x31), // orange
        Color.FromRgb(0xA9, 0xD1, 0x8E), // green
        Color.FromRgb(0xFF, 0xC0, 0x00), // gold
        Color.FromRgb(0x5A, 0x96, 0xC5), // steel blue
        Color.FromRgb(0x70, 0xAD, 0x47), // lime green
    ];

    private static readonly IBrush ChartFrameFill    = new SolidColorBrush(Color.FromArgb(0xFF, 0xF9, 0xF9, 0xF9));
    private static readonly Pen    ChartFramePen     = new Pen(new SolidColorBrush(Color.FromRgb(0xBB, 0xBB, 0xBB)), 1.0);
    private static readonly IBrush ChartGridlineBrush = new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x00, 0x00));
    private static readonly IBrush ChartLegendBg    = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF));

    /// <summary>
    /// Centralised factory for <see cref="FloatingChartData"/> — shared by the floating chart collector,
    /// the inline chart layout, and the drawing-group child collector. Resolves QuickLayout / StyleId
    /// to determine which annotation flags are active, then copies chart model data into the data struct.
    /// </summary>
    private static FloatingChartData BuildChartData(
        Chart chart, Rect rect, bool behindText, int zOrder,
        List<(string? Name, List<double> Values)> series)
    {
        // Resolve effective annotation flags: QuickLayout overrides individual properties.
        bool showLegend, showDataLabels, showAxisTitles;
        if (chart.QuickLayoutId > 0 && ChartQuickLayout.FindById(chart.QuickLayoutId) is { } ql)
        {
            showLegend     = ql.ShowLegend;
            showDataLabels = ql.ShowDataLabels;
            showAxisTitles = ql.ShowAxisTitles;
        }
        else if (chart.StyleId > 0 && ChartStyle.FindById(chart.StyleId) is { } st)
        {
            showLegend     = chart.ShowLegend; // style does not override ShowLegend
            showDataLabels = st.ShowDataLabels;
            showAxisTitles = !string.IsNullOrEmpty(chart.CategoryAxisTitle) ||
                             !string.IsNullOrEmpty(chart.ValueAxisTitle);
        }
        else
        {
            showLegend     = chart.ShowLegend;
            showDataLabels = false;
            showAxisTitles = !string.IsNullOrEmpty(chart.CategoryAxisTitle) ||
                             !string.IsNullOrEmpty(chart.ValueAxisTitle);
        }

        var isPieFamily = chart.Kind is ChartKind.Pie or ChartKind.Doughnut;

        return new FloatingChartData
        {
            Rect              = rect,
            BehindText        = behindText,
            ZOrder            = zOrder,
            Kind              = chart.Kind,
            Title             = chart.Title,
            Categories        = new List<string>(chart.Categories),
            Series            = series,
            ShowLegend        = showLegend,
            ShowDataLabels    = showDataLabels,
            CategoryAxisTitle = isPieFamily ? null : (showAxisTitles ? chart.CategoryAxisTitle : null),
            ValueAxisTitle    = isPieFamily ? null : (showAxisTitles ? chart.ValueAxisTitle    : null),
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

        // ── Legend geometry (right side, each series gets a swatch + name row) ──
        // Reserve right strip for legend when ShowLegend and at least one named series exists.
        var legendW = 0.0;
        var namedSeries = cd.Series.Where(s => !string.IsNullOrEmpty(s.Name)).ToList();
        if (cd.ShowLegend && namedSeries.Count > 0)
        {
            var maxNameW = namedSeries.Max(s => Build(s.Name!, annotFmt).WidthIncludingTrailingWhitespace);
            legendW = Math.Min(rect.Width * 0.30, maxNameW + 18); // swatch(10)+gap(4)+text+pad(4)
        }

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
        var plotBottom = rect.Bottom - 18 - catTitleH; // x-axis labels + optional cat title
        var plotLeft   = rect.X + 32 + valTitleW;      // y-axis labels + optional val title
        var plotRight  = rect.Right - 8 - legendW;
        var plotW      = Math.Max(10, plotRight - plotLeft);
        var plotH      = Math.Max(10, plotBottom - plotTop);

        if (cd.Series.Count == 0 || plotW < 5 || plotH < 5)
            return;

        // ── Gridlines ──
        const int gridLines = 4;
        var gridPen = new Pen(ChartGridlineBrush, 0.5);
        for (var g = 0; g <= gridLines; g++)
        {
            var gy = plotBottom - g * plotH / gridLines;
            context.DrawLine(gridPen, new Point(plotLeft, gy), new Point(plotRight, gy));
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
            case ChartKind.Scatter:
            case ChartKind.Area:
                DrawChartLines(context, cd, plotLeft, plotTop, plotW, plotH, plotBottom,
                    fillArea: cd.Kind == ChartKind.Area);
                break;

            case ChartKind.Pie:
                DrawChartPie(context, cd, plotLeft, plotTop, plotW, plotH, doughnut: false);
                break;

            case ChartKind.Doughnut:
                DrawChartPie(context, cd, plotLeft, plotTop, plotW, plotH, doughnut: true);
                break;
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
            var catTitleY = rect.Bottom - catTitleFt.Height - 1;
            context.DrawText(catTitleFt, new Point(Math.Max(rect.X + 2, catTitleX), catTitleY));
        }

        // ── Legend (right strip) ──
        if (cd.ShowLegend && namedSeries.Count > 0)
        {
            const double swatchSz = 8;
            const double rowH     = 11;
            const double pad      = 4;
            var legendX     = plotRight + pad;
            var legendTotalH = namedSeries.Count * rowH;
            var legendY     = (plotTop + plotBottom) / 2 - legendTotalH / 2;

            // Semi-transparent background.
            context.FillRectangle(ChartLegendBg,
                new Rect(legendX - 2, legendY - 2, legendW, legendTotalH + 4));

            for (var si = 0; si < namedSeries.Count; si++)
            {
                var (name, _) = namedSeries[si];
                var originalIdx = cd.Series.IndexOf(namedSeries[si]);
                var color = ChartSeriesColors[(originalIdx >= 0 ? originalIdx : si) % ChartSeriesColors.Length];
                var brush = new SolidColorBrush(color);
                var rowY = legendY + si * rowH;

                // Swatch.
                context.FillRectangle(brush, new Rect(legendX, rowY + (rowH - swatchSz) / 2, swatchSz, swatchSz));

                // Name.
                var nameFt = Build(name!, annotFmt);
                context.DrawText(nameFt, new Point(legendX + swatchSz + 2, rowY + (rowH - nameFt.Height) / 2));
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
    private void DrawChartDataLabels(DrawingContext context, FloatingChartData cd,
        double plotLeft, double plotTop, double plotW, double plotH, double plotBottom,
        RunFormatting fmt)
    {
        var maxVal = 1.0;
        foreach (var (_, vals) in cd.Series)
            foreach (var v in vals)
                if (v > maxVal) maxVal = v;

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
                        var val   = ci < vals.Count ? vals[ci] : 0;
                        var ratio = maxVal > 0 ? val / maxVal : 0;
                        var bw    = Math.Max(1, seriesW - 1);
                        var barH  = Math.Max(1, ratio * plotH);
                        var bx    = plotLeft + barPad + ci * groupW + si * seriesW;
                        var barTopY = plotBottom - barH;

                        var label = val.ToString("G3", System.Globalization.CultureInfo.InvariantCulture);
                        var ft = Build(label, fmt);
                        var lx = bx + (bw - ft.WidthIncludingTrailingWhitespace) / 2;
                        var ly = barTopY - ft.Height - 1;
                        if (ly < plotTop) ly = barTopY + 1; // flip inside bar if clipped
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
                        var ratio = maxVal > 0 ? val / maxVal : 0;
                        var barW  = Math.Max(1, ratio * plotW);
                        var by    = plotTop + (ci * (barGroupH + 2 * barPad) + barPad + si * seriesH);

                        var label = val.ToString("G3", System.Globalization.CultureInfo.InvariantCulture);
                        var ft = Build(label, fmt);
                        var lx = plotLeft + barW + 1;
                        var ly = by + (Math.Max(1, seriesH - 1) - ft.Height) / 2;
                        context.DrawText(ft, new Point(Math.Min(lx, plotLeft + plotW - ft.WidthIncludingTrailingWhitespace), ly));
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
                        var py  = plotBottom - (maxVal > 0 ? val / maxVal * plotH : 0);

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

        // Find max value across all series.
        var maxVal = 1.0;
        foreach (var (_, vals) in cd.Series)
            foreach (var v in vals)
                if (v > maxVal) maxVal = v;

        var groupW = plotW / Math.Max(1, nBars);
        var barPad = Math.Max(1, groupW * 0.1);
        var barGroupW = groupW - 2 * barPad;
        var seriesW = barGroupW / Math.Max(1, nSeries);

        for (var si = 0; si < nSeries; si++)
        {
            var (_, vals) = cd.Series[si];
            var color = ChartSeriesColors[si % ChartSeriesColors.Length];
            var brush = new SolidColorBrush(color);

            for (var ci = 0; ci < nBars; ci++)
            {
                var val   = ci < vals.Count ? vals[ci] : 0;
                var ratio = maxVal > 0 ? val / maxVal : 0;

                if (horizontal)
                {
                    var bh     = Math.Max(1, seriesW - 1);
                    var barH   = Math.Max(1, ratio * plotW);
                    var by     = plotTop + (ci * (barGroupW + 2 * barPad) + barPad + si * seriesW);
                    var barRect = new Rect(plotLeft, by, barH, bh);
                    context.FillRectangle(brush, barRect);
                }
                else
                {
                    var bw     = Math.Max(1, seriesW - 1);
                    var barH   = Math.Max(1, ratio * plotH);
                    var bx     = plotLeft + barPad + ci * groupW + si * seriesW;
                    var barRect = new Rect(bx, plotBottom - barH, bw, barH);
                    context.FillRectangle(brush, barRect);
                }
            }
        }
    }

    private void DrawChartLines(DrawingContext context, FloatingChartData cd,
        double plotLeft, double plotTop, double plotW, double plotH, double plotBottom, bool fillArea)
    {
        var cats   = Math.Max(2, cd.Categories.Count > 0 ? cd.Categories.Count : (cd.Series[0].Values.Count));
        var maxVal = 1.0;
        foreach (var (_, vals) in cd.Series)
            foreach (var v in vals)
                if (v > maxVal) maxVal = v;

        for (var si = 0; si < cd.Series.Count; si++)
        {
            var (_, vals) = cd.Series[si];
            if (vals.Count == 0) continue;

            var color = ChartSeriesColors[si % ChartSeriesColors.Length];
            var pen   = new Pen(new SolidColorBrush(color), 1.5);

            var pts = new List<Point>();
            for (var ci = 0; ci < Math.Max(cats, vals.Count); ci++)
            {
                var val = ci < vals.Count ? vals[ci] : 0;
                var px  = plotLeft + ci * plotW / Math.Max(1, cats - 1);
                var py  = plotBottom - (maxVal > 0 ? val / maxVal * plotH : 0);
                pts.Add(new Point(px, py));
            }

            for (var pi = 0; pi < pts.Count - 1; pi++)
                context.DrawLine(pen, pts[pi], pts[pi + 1]);

            if (fillArea && pts.Count >= 2)
            {
                var geo = new StreamGeometry();
                using var ctx = geo.Open();
                ctx.BeginFigure(new Point(pts[0].X, plotBottom), isFilled: true);
                foreach (var p in pts) ctx.LineTo(p);
                ctx.LineTo(new Point(pts[^1].X, plotBottom));
                ctx.EndFigure(true);
                context.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x55, color.R, color.G, color.B)), null, geo);
            }
        }
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
            var color     = ChartSeriesColors[si % ChartSeriesColors.Length];
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

    // SmartArt node colours per slot (reuses the chart palette for consistency).
    private static readonly IBrush[] SmartArtNodeFills =
    [
        new SolidColorBrush(Color.FromRgb(0x43, 0x72, 0xC4)),
        new SolidColorBrush(Color.FromRgb(0xED, 0x7D, 0x31)),
        new SolidColorBrush(Color.FromRgb(0xA9, 0xD1, 0x8E)),
        new SolidColorBrush(Color.FromRgb(0xFF, 0xC0, 0x00)),
        new SolidColorBrush(Color.FromRgb(0x5A, 0x96, 0xC5)),
        new SolidColorBrush(Color.FromRgb(0x70, 0xAD, 0x47)),
    ];

    private static readonly Pen SmartArtNodePen    = new Pen(Brushes.White, 1.0);
    private static readonly Pen SmartArtConnectPen = new Pen(new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)), 1.0);

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
            context.FillRectangle(SmartArtNodeFills[0], rootRect);
            DrawSmartArtNodeText(context, roots.Length > 0 ? roots[0] : string.Empty, rootRect);

            if (children.Length > 0)
            {
                var childW  = Math.Min((areaW - (children.Length - 1) * connGap) / children.Length, 90);
                var childY  = rootY + nodeH + connGap * 2;
                var totalChildW = childW * children.Length + connGap * (children.Length - 1);
                var childStartX = rect.X + (rect.Width - totalChildW) / 2;

                // Vertical line from root to child row.
                var midRootX = rootX + rootW / 2;
                context.DrawLine(SmartArtConnectPen, new Point(midRootX, rootY + nodeH), new Point(midRootX, childY - connGap));
                // Horizontal line across child tops.
                if (children.Length > 1)
                {
                    context.DrawLine(SmartArtConnectPen,
                        new Point(childStartX + childW / 2, childY - connGap),
                        new Point(childStartX + (children.Length - 1) * (childW + connGap) + childW / 2, childY - connGap));
                }

                for (var ci = 0; ci < children.Length; ci++)
                {
                    var cx = childStartX + ci * (childW + connGap);
                    var childRect = new Rect(cx, childY, childW, nodeH);
                    var fill = SmartArtNodeFills[(ci + 1) % SmartArtNodeFills.Length];
                    context.FillRectangle(fill, childRect);
                    DrawSmartArtNodeText(context, children[ci], childRect);
                    // Vertical drop line from horizontal bus to child.
                    context.DrawLine(SmartArtConnectPen,
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
                var fill = SmartArtNodeFills[ni % SmartArtNodeFills.Length];
                context.FillRectangle(fill, nodeRect);
                DrawSmartArtNodeText(context, sd.NodeTexts[ni], nodeRect);
                bx += boxW;

                // Arrow connector between process nodes.
                if (sd.Kind == SmartArtKind.Process && ni < count - 1)
                {
                    var arrowMidY = boxY + nodeH / 2;
                    var arrowX1   = bx + 2;
                    var arrowX2   = arrowX1 + arrowW;
                    var arrowPen  = new Pen(SmartArtNodeFills[ni % SmartArtNodeFills.Length], 1.5);
                    context.DrawLine(arrowPen, new Point(arrowX1, arrowMidY), new Point(arrowX2, arrowMidY));
                    // Arrow head.
                    context.DrawLine(arrowPen, new Point(arrowX2, arrowMidY), new Point(arrowX2 - 4, arrowMidY - 3));
                    context.DrawLine(arrowPen, new Point(arrowX2, arrowMidY), new Point(arrowX2 - 4, arrowMidY + 3));
                    bx += arrowW + 2;
                }
            }
        }
    }

    private void DrawSmartArtNodeText(DrawingContext context, string text, Rect nodeRect)
    {
        if (string.IsNullOrEmpty(text)) return;
        var fmt = new RunFormatting { FontSizePt = 7.5, ColorHex = "#FFFFFF", Bold = true };
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
