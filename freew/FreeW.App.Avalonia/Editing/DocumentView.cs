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
    private readonly Dictionary<InlineImage, Bitmap?> _bitmapCache = new();
    private readonly List<(Rect Rect, int Block, int Row, int Col)> _cellHits = new();

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

    /// <summary>Raised when a table cell is double-clicked, so the shell can open a cell editor.</summary>
    public event Action<CellEditRequest>? CellEditRequested;

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
        _cellHits.Clear();

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

        var textWidth = _contentWidth;
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
                if (paragraph.Runs.Any(r => r.Image is not null))
                {
                    listNumber = 0;
                    prevList = ListKind.None;
                    LayoutImageParagraphPaged(blockIndex, paragraph, textWidth);
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
            // The number of pages = pageIndex of the last content Y + 1.
            var lastPageIndex = (int)(_layoutContentY / _layoutTextAreaHeight);
            _pageCount = Math.Max(1, lastPageIndex + 1);
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

        var pageIndex = (int)(contentY / _layoutTextAreaHeight);
        var offsetWithinPage = contentY - pageIndex * _layoutTextAreaHeight;
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
        var availableWidth = Math.Max(60, textWidth - leftInset);
        _layoutContentY += pf.SpaceBeforePt * PxPerPoint;

        if (marker is not null)
        {
            var markerFmt = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Formatting : RunFormatting.Default;
            var markerWidth = Build(marker, markerFmt).WidthIncludingTrailingWhitespace;
            // Place the marker at the current content-space Y converted to page-space.
            var markerY = ContentYToPageSpaceY(_layoutContentY);
            _markers.Add((_contentLeft + leftInset - markerWidth - 6, markerY, marker, markerFmt));
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

        while (i < cells.Count)
        {
            if (cells[i].Ch == ' ')
                lastBreak = i;

            if (lineWidth + measured[i] > availableWidth && i > lineStart)
            {
                var breakAt = lastBreak >= lineStart ? lastBreak + 1 : i;
                EmitLinePaged(blockIndex, cells, measured, heights, lineStart, breakAt, alignment, availableWidth, leftInset);
                lineStart = breakAt;
                lineWidth = 0;
                lastBreak = -1;
                for (var k = lineStart; k < i; k++)
                    lineWidth += measured[k];
            }

            lineWidth += measured[i];
            i++;
        }

        EmitLinePaged(blockIndex, cells, measured, heights, lineStart, cells.Count, alignment, availableWidth, leftInset, isLast: true);
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
        double leftInset = 0,
        bool isLast = false)
    {
        double lineWidth = 0;
        double lineHeight = DefaultFontSizePt * PxPerPoint * 1.3;
        for (var c = from; c < to; c++)
        {
            lineWidth += measured[c];
            if (heights[c] > lineHeight)
                lineHeight = heights[c];
        }

        // Ensure the whole line fits on one page (push to next page if it overflows).
        var contentY = ReserveContentY(lineHeight);
        var pageSpaceY = ContentYToPageSpaceY(contentY);

        var x = _contentLeft + leftInset + AlignmentOffset(alignment, availableWidth, lineWidth);
        for (var c = from; c < to; c++)
        {
            _placed.Add(new PlacedChar(blockIndex, c, x, pageSpaceY, measured[c], lineHeight, cells[c].Fmt, cells[c].Ch, Sentinel: false));
            x += measured[c];
        }

        // End-of-line / end-of-paragraph sentinel carries the caret slot after the last char.
        if (isLast)
            _placed.Add(new PlacedChar(blockIndex, to, x, pageSpaceY, 0, lineHeight, RunFormatting.Default, '\0', Sentinel: true));

        _layoutContentY = contentY + lineHeight;
    }

    private static double AlignmentOffset(TextAlignment alignment, double textWidth, double lineWidth) => alignment switch
    {
        TextAlignment.Center => Math.Max(0, (textWidth - lineWidth) / 2),
        TextAlignment.Right => Math.Max(0, textWidth - lineWidth),
        _ => 0,
    };

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

        EmitLinePaged(blockIndex, cells, measured, heights, 0, cells.Count, TextAlignment.Left, textWidth, isLast: true);
    }

    private static string TablePlainText(Table table) =>
        string.Join("  |  ", table.Rows.SelectMany(r => r.Cells).Select(c => c.PlainText));

    // ---- Table rendering (grid + modal cell text editing) ----------------------------------------

    private void LayoutTablePaged(int blockIndex, Table table, double textWidth)
    {
        var cols = Math.Max(1, table.ColumnCount);
        var colWidths = ComputeColumnWidths(table, cols, textWidth);
        var columnLeft = new double[cols + 1];
        var running = _contentLeft;
        for (var c = 0; c < cols; c++)
        {
            columnLeft[c] = running;
            running += colWidths[c];
        }
        columnLeft[cols] = running;

        const double pad = 5;
        var borders = table.Formatting.Borders;
        var headerOffset = table.Formatting.HeaderRow ? 1 : 0;
        var glyphOffset = 0;

        for (var r = 0; r < table.Rows.Count; r++)
        {
            var row = table.Rows[r];
            var isHeader = table.Formatting.HeaderRow && r == 0;
            var isBand = table.Formatting.BandedRows && !isHeader && (r - headerOffset) % 2 == 1;

            var measured = new List<(int StartCol, int Span, List<(double Height, List<(char Ch, double W)> Chars)> Lines, RunFormatting Fmt)>();
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

                measured.Add((col, span, lines, fmt));
                col += span;
            }

            // Treat the row as a unit: reserve space on the current page (or push to next).
            var rowContentY = ReserveContentY(rowHeight);
            var rowPageSpaceY = ContentYToPageSpaceY(rowContentY);

            foreach (var (startCol, span, lines, fmt) in measured)
            {
                double cellWidth = 0;
                for (var s = 0; s < span; s++)
                    cellWidth += colWidths[startCol + s];
                var rect = new Rect(columnLeft[startCol], rowPageSpaceY, cellWidth, rowHeight);
                IBrush? fill = isHeader ? HeaderFill : isBand ? BandFill : null;
                _rects.Add((rect, fill, borders));
                _cellHits.Add((rect, blockIndex, r, startCol));

                var ty = rowPageSpaceY + pad;
                foreach (var (lineHeight, chars) in lines)
                {
                    var tx = columnLeft[startCol] + pad;
                    foreach (var (ch, w) in chars)
                    {
                        _placed.Add(new PlacedChar(blockIndex, glyphOffset++, tx, ty, w, lineHeight, fmt, ch, Sentinel: false));
                        tx += w;
                    }

                    ty += lineHeight;
                }
            }

            _layoutContentY = rowContentY + rowHeight;
        }

        _layoutContentY += 8;
    }

    private void LayoutImageParagraphPaged(int blockIndex, Paragraph paragraph, double textWidth)
    {
        const double gap = 6;
        var alignment = paragraph.Formatting.Alignment;
        foreach (var run in paragraph.Runs)
        {
            if (run.Image is not { } image)
                continue;

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
            var x = _contentLeft + AlignmentOffset(alignment, textWidth, width);
            _images.Add((new Rect(x, imgPageSpaceY, width, height), DecodeBitmap(image)));
            _layoutContentY = imgContentY + height + gap;
        }
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

        var selection = NormalizedSelection();
        foreach (var pc in _placed)
        {
            if (pc.Sentinel)
                continue;

            if (selection is { } sel && IsWithin(sel, pc.Block, pc.Offset))
                context.FillRectangle(SelectionBrush, new Rect(pc.X, pc.Y, Math.Max(2, pc.W), pc.LineHeight));

            var ft = Build(pc.Ch.ToString(), pc.Fmt);
            context.DrawText(ft, new Point(pc.X, pc.Y));

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

        if (IsFocused && NormalizedSelection() is null && TryGetCaretRect(out var caretRect))
            context.FillRectangle(Brushes.Black, caretRect);
    }

    private void DrawDecoration(DrawingContext context, PlacedChar pc, double yLine)
    {
        var pen = new Pen(BrushFor(pc.Fmt.ColorHex), Math.Max(1, FontSizePx(pc.Fmt) / 14));
        context.DrawLine(pen, new Point(pc.X, yLine), new Point(pc.X + pc.W, yLine));
    }

    private bool TryGetCaretRect(out Rect rect)
    {
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

        // Double-click a table cell opens the modal cell editor; in-cell caret editing is not modelled yet.
        if (e.ClickCount == 2)
        {
            var hit = e.GetPosition(this);
            foreach (var cell in _cellHits)
            {
                if (cell.Rect.Contains(hit))
                {
                    CellEditRequested?.Invoke(new CellEditRequest(cell.Block, cell.Row, cell.Col, GetCellText(cell.Block, cell.Row, cell.Col)));
                    return;
                }
            }
        }

        Focus();
        var point = e.GetPosition(this);
        if (TryHitTest(point, out var pos))
        {
            _selectionAnchor = (e.KeyModifiers & KeyModifiers.Shift) != 0 ? (_selectionAnchor ?? _caret) : null;
            _caret = pos;
            if ((e.KeyModifiers & KeyModifiers.Shift) == 0)
                _selectionAnchor = pos;
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
        if (NormalizedSelection() is not null)
            DeleteSelection();
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return;

        var block = _caret.Block;
        var offset = _caret.Offset;
        var fmt = ActiveFormatting(paragraph, offset);
        _bus.Execute(new ReplaceParagraphRunsCommand(block, p =>
        {
            var cells = ParaCells(p);
            foreach (var ch in text)
                cells.Insert(Math.Clamp(offset, 0, cells.Count), new Cell(ch, fmt));
            SetRuns(p, cells);
        }));
        _caret = new DocPosition(block, offset + text.Length);
        _selectionAnchor = _caret;
    }

    private void Backspace()
    {
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
        if (NormalizedSelection() is not null) { DeleteSelection(); return; }
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return;
        var len = ParaCells(paragraph).Count;
        if (_caret.Offset < len)
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
        if (NormalizedSelection() is not null)
            DeleteSelection();
        if (CurrentParagraph() is not { } paragraph || !IsEditable(paragraph))
            return;

        var block = _caret.Block;
        var offset = _caret.Offset;
        var cells = ParaCells(paragraph);
        var first = new Paragraph { Formatting = paragraph.Formatting, StyleId = paragraph.StyleId };
        SetRuns(first, cells.Take(offset).ToList());
        var second = new Paragraph { Formatting = paragraph.Formatting };
        SetRuns(second, cells.Skip(offset).ToList());
        _bus.Execute(new ReplaceBlocksCommand(block, 1, new Block[] { first, second }));
        _caret = new DocPosition(block + 1, 0);
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
        var len = CurrentLength();
        var newOffset = _caret.Offset + delta;
        if (newOffset < 0)
        {
            var prev = PreviousEditableBlock(_caret.Block);
            _caret = prev < 0 ? _caret with { Offset = 0 } : new DocPosition(prev, BlockLength(prev));
        }
        else if (newOffset > len)
        {
            var next = NextEditableBlock(_caret.Block);
            _caret = next < 0 ? _caret with { Offset = len } : new DocPosition(next, 0);
        }
        else
        {
            _caret = _caret with { Offset = newOffset };
        }

        if (!extend)
            _selectionAnchor = _caret;
        InvalidateVisual();
        CaretMoved?.Invoke();
    }

    private void MoveToLineEdge(bool toStart, bool extend)
    {
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
                _selectionAnchor = _caret;
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

        if (best is not { } b || _doc.Blocks[b.Block] is not Paragraph paragraph || !IsEditable(paragraph))
            return false;

        // Snap to the nearer edge of the hit glyph.
        var offset = b.Offset;
        if (!b.Sentinel && point.X > b.X + b.W / 2)
            offset = b.Offset + 1;
        pos = new DocPosition(b.Block, Math.Clamp(offset, 0, BlockLength(b.Block)));
        return true;
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
        bool Sentinel);

    private sealed class ViewContext(DocumentView view) : IDocumentCommandContext
    {
        public TextDocument Document => view._doc;
    }
}
