using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Free.Shared.Drawing;
using Free.Shared.Pdf;
using Free.Shared.Ribbon;
using FreeW.App.Presentation.Dialogs;
using FreeW.App.Presentation.ContextMenus;
using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Editing;
using FreeW.App.Presentation.Links;
using FreeW.App.Presentation.Proofing;
using FreeW.App.Presentation.Ribbon;
using FreeW.App.Presentation.Shell;
using FreeW.Core.IO;
using FreeW.Core.Model;
using SkiaSharp;

namespace FreeW.App.Avalonia.Editing;

public sealed partial class DocumentView
{

    /// <summary>
    /// Opens the date-picker calendar for a body field and hands back the flyout, so a test can inspect
    /// what the click gesture actually puts on screen (a headless run cannot click inside a popup).
    /// </summary>
    internal global::Avalonia.Controls.Flyout? OpenContentControlCalendarForTest(int blockIndex, int runIndex)
    {
        if (_doc.Blocks.ElementAtOrDefault(blockIndex) is not Paragraph paragraph
            || paragraph.Runs.ElementAtOrDefault(runIndex) is not { } run
            || !OpenContentControlCalendar(new ContentControlTarget(blockIndex, runIndex), run))
        {
            return null;
        }

        return _contentControlCalendarFlyout;
    }
    internal ((int BlockIndex, int RunIndex, int TextParagraphIndex, int TextRunIndex, int Offset) Start,
        (int BlockIndex, int RunIndex, int TextParagraphIndex, int TextRunIndex, int Offset) End)? ShapeTextSelectionInfo =>
        CurrentShapeTextSelection;

    internal IReadOnlyList<(double X1, double Y1, double X2, double Y2)> ComputeGridlines() =>
        BuildGridlines();

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

    public IReadOnlyDictionary<FloatHandle, Rect> HandleRectsForSelection()
    {
        var dict = new Dictionary<FloatHandle, Rect>();
        if (_selectedFloatingGroupChild is { } child
            && TryGetFloatingGroupChildGeometry(
                child.BlockIndex,
                child.RunIndex,
                child.ChildPath,
                out var geometry))
        {
            foreach (var handle in DocumentViewLayoutPlanner.BuildFloatingGroupChildHandleRectsThroughGroupChain(
                         ToPlannerRect(geometry.Child.Rect),
                         FloatHandleSize,
                         geometry.Child.RotationAngle,
                         geometry.Child.FlipH,
                         geometry.Child.FlipV,
                         geometry.ParentTransforms))
                dict[FromPlannerHandle(handle.Handle)] = ToAvaloniaRect(handle.Rect);
            return dict;
        }

        if (_selectedFloating is { } selection)
        {
            var (angle, flipH, flipV) = GetFloatRotation(selection.BlockIndex, selection.RunIndex, selection.Kind);
            foreach (var (handle, rect) in HandleRects(selection.Rect, angle, flipH, flipV))
                dict[handle] = rect;
        }
        return dict;
    }

    public FloatHandle BeginFloatDrag(Point start)
    {
        if (_selectedFloating is not { } selection)
            return FloatHandle.None;
        var handle = HitTestHandle(start);
        if (handle == FloatHandle.None)
            return FloatHandle.None;
        BeginFloatingDrag(start, SelectedFloatingDragRect(selection), handle);
        return handle;
    }

    public void SimulateDragTo(Point to, bool shift = false) => UpdateFloatDrag(to, shift);

    public void EndFloatDrag(Point to, bool shift = false) => CommitFloatDrag(to, shift);

    public bool CancelFloatDrag() => TryCancelFloatDrag();

    internal static double PageBorderInsetDip => PageBorderInsetPt * PxPerPoint;

    internal IReadOnlyList<double> BodyPageVerticalOffsetsForTest => _bodyPageVerticalOffsets;
    internal IReadOnlyList<double> BodyPageVerticalJustifiedGapsForTest => _bodyPageVerticalJustifiedGaps;
    internal AutomationPeer CreateAutomationPeerForTests() => OnCreateAutomationPeer();
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

    internal void SimulateHyperlinkActivatedForTest(string url) => HyperlinkActivated?.Invoke(url);

    internal bool HitTestHeaderFooterForTest(Point point)
    {
        if (_laidOutWidth < 0)
            Relayout(FallbackWidth);
        return TryHitTestHeaderFooter(point);
    }

    internal Rect? HfCaretRectForTest => TryGetHfCaretRect(out var rect) ? rect : null;
    internal void BackspaceForTest() => Backspace();
    internal void DeleteForwardForTest() => DeleteForward();
    internal void InsertParagraphBreakForTest() => InsertParagraphBreak();

    internal bool SimulateHfKeyForTest(Key key, bool shift = false)
    {
        if (_hfCaret is null)
            return false;
        switch (key)
        {
            case Key.Tab:
                if (!shift)
                    HfInsertText("\t");
                return true;
            case Key.Up:
            case Key.Down:
                return true;
            default:
                return false;
        }
    }

    internal void MoveCaretToBlockForTest(int blockIndex, int offset)
    {
        _hfCaret = null;
        MoveCaretToBlock(blockIndex, offset);
    }

    internal void HandleBodyClickForTest(Point point)
    {
        if (_laidOutWidth < 0)
            Relayout(FallbackWidth);
        if (_viewMode == DocumentViewMode.PrintLayout && TryHitTestHeaderFooter(point))
            return;
        _hfCaret = null;
        if (TryHitTest(point, out var position))
        {
            _caret = position;
            _selectionAnchor = position;
        }
        InvalidateVisual();
    }

    internal void SetCellSelectionAnchorForTest(
        int tableBlockIndex,
        int row,
        int col,
        int paraIdx,
        int anchorOffset)
    {
        if (_laidOutWidth < 0)
            Relayout(FallbackWidth);
        _cellAnchor = (tableBlockIndex, row, col, paraIdx, anchorOffset);
        _selectionAnchor = new DocPosition(tableBlockIndex, anchorOffset);
    }

    internal void SetCellTextSelectionForTest(
        int tableBlockIndex,
        int anchorRow,
        int anchorCol,
        int anchorParaIdx,
        int anchorOffset,
        int caretRow,
        int caretCol,
        int caretParaIdx,
        int caretOffset)
    {
        _cellBlockAnchor = null;
        _cellBlockFocus = null;
        _cellAnchor = (tableBlockIndex, anchorRow, anchorCol, anchorParaIdx, anchorOffset);
        _cellCaret = (tableBlockIndex, caretRow, caretCol, caretParaIdx, caretOffset);
        _caret = new DocPosition(tableBlockIndex, caretOffset);
        _selectionAnchor = new DocPosition(tableBlockIndex, anchorOffset);
        _hfCaret = null;
    }

    internal Rect? CaretRectForTest => TryGetCaretRect(out var rect) ? rect : null;

    internal double HorizontalPageExtentForTest =>
        _surfacePlan.UsesProjectedPageFlow
            ? _surfacePlan.ScrollableWidthForPages(_pageCount)
            : 0;

    internal Point RenderedPageOriginForTest(int pageIndex) =>
        new(_surfacePlan.RenderedPageLeftDip(pageIndex), _surfacePlan.RenderedPageTopDip(pageIndex));

    internal IReadOnlyList<(char Character, int ParagraphIndex, int RunIndex, int Offset,
        double X, double Y, double Width, double Height, RunFormatting Formatting)>
        FloatingShapeTextGlyphsForTest
    {
        get
        {
            if (_laidOutWidth < 0)
                Relayout(FallbackWidth);
            return _floatingShapes
                .SelectMany(shape => shape.TextLayout?.Glyphs ?? [])
                .Select(glyph => (glyph.Character, glyph.ParagraphIndex, glyph.RunIndex, glyph.Offset,
                    glyph.X, glyph.Y, glyph.Width, glyph.Height, glyph.Formatting))
                .ToList();
        }
    }

    internal IReadOnlyList<(int BlockIndex, int RunIndex, Rect Rect)> FloatingSnapshotRectsForTest
    {
        get
        {
            if (_laidOutWidth < 0)
                Relayout(FallbackWidth);
            return _floatingSnapshots
                .Select(snapshot => (snapshot.BlockIndex, snapshot.RunIndex, ToAvaloniaRect(snapshot.Rect)))
                .ToList();
        }
    }

    internal IReadOnlyList<LineNumberRenderItem> GetLineNumberRenderItemsForTest() =>
        BuildLineNumberRenderItems();

    internal bool BeginShapeEditPointDragForTest(int segmentIndex, IPointer? pointer = null)
    {
        if (_shapeEditPointsTarget is null || !ShapeEditPointRectsForSelection().ContainsKey(segmentIndex))
            return false;
        BeginShapeEditPointDrag(segmentIndex, pointer);
        return true;
    }

    internal bool MoveActiveShapeEditPointFromPageForTest(int segmentIndex, Point pagePoint) =>
        TryPointToCustomCoordinate(pagePoint, out var x, out var y)
        && MoveActiveShapeEditPoint(segmentIndex, x, y);

    internal bool IsShapeEditPointUndoGroupOpenForTest => _bus.IsUndoGroupOpen;

    internal Rect? FloatDragBaseRectForTest => _floatingDrag.BaseRect is { } rect
        ? ToAvaloniaRect(rect)
        : null;

    internal (int BlockIndex, int RunIndex, int ChildIndex, string Kind, Rect Rect)?
        HitTestFloatingGroupChildForTest(Point point) =>
        TryHitTestFloatingGroupChild(point, out var hit)
            ? (hit.BlockIndex, hit.RunIndex, hit.ChildIndex, hit.Kind, hit.Rect)
            : null;

    internal IReadOnlyList<int>? SelectedFloatingGroupChildPathForTest =>
        _selectedFloatingGroupChild?.ChildPath;

    internal IReadOnlyList<(int ChildIndex, Rect Rect)> FloatingGroupChildRectsForTest(
        int blockIndex, int runIndex) =>
        TryGetFloatingGroupData(blockIndex, runIndex, out var groupData)
            ? groupData.Children.Select(child => (child.ChildIndex, child.Rect)).ToArray()
            : [];

    internal Rect? FloatingGroupChildRectForPathForTest(
        int blockIndex,
        int runIndex,
        IReadOnlyList<int> childPath) =>
        TryGetFloatingGroupChildGeometry(blockIndex, runIndex, childPath, out var geometry)
            ? geometry.Child.Rect
            : null;

    internal bool PlaceShapeTextCaretForTest(Point point) =>
        _shapeCaret is { } active && TryPlaceShapeTextCaret(point, active);

    internal bool BeginShapeTextSelectionForTest(Point point) =>
        BeginShapeTextSelectionDrag(point, extend: false, pointer: null, out _);

    internal void UpdateShapeTextSelectionForTest(Point point) => UpdateShapeTextSelectionDrag(point);

    internal void EndShapeTextSelectionForTest(Point point)
    {
        UpdateShapeTextSelectionDrag(point);
        FinishShapeTextSelectionDrag(releasePointerCapture: false);
    }

    internal bool SelectShapeTextRangeForTest(int paragraphIndex, int startOffset, int endOffset)
    {
        if (_shapeCaret is not { } caret
            || !TryGetShapeTextTarget(
                caret.BlockIndex, caret.RunIndex, _activeShapeTextChildPath,
                out _, out var shape)
            || paragraphIndex < 0
            || paragraphIndex >= shape.TextParagraphs.Count)
            return false;

        _shapeSelectionAnchor = ShapeTextPositionAtOffset(
            shape, caret.BlockIndex, caret.RunIndex, paragraphIndex, startOffset);
        _shapeCaret = ShapeTextPositionAtOffset(
            shape, caret.BlockIndex, caret.RunIndex, paragraphIndex, endOffset);
        InvalidateVisual();
        CaretMoved?.Invoke();
        return ShapeTextSelectionInfo is not null;
    }

    internal bool SelectedFloatingGroupChildMatchesPointForTest(Point point) =>
        _selectedFloatingGroupChild is { } selected
        && TryHitTestFloatingGroupChild(point, out var hit)
        && IsSameFloatingGroupChild(selected, hit);

    internal bool SelectFloatingGroupChildForTest(Point point)
    {
        if (!TryHitTestFloatingGroupChild(point, out var hit))
            return false;

        SelectFloatingGroupChildCore(hit);
        return true;
    }


    /// <summary>
    /// Operates the content control at the given point via the body/table-cell click gesture's own path
    /// (<see cref="TryActivateContentControl"/>) — checkbox toggle, calendar/menu open, or (F3) selecting
    /// a placeholder-showing plain-text/rich-text field's whole run so a headless test can exercise the
    /// exact code path a real mouse click runs, not just the caret placement <see cref="HandleBodyClickForTest"/>
    /// alone provides.
    /// </summary>
    internal bool ActivateContentControlAtForTest(Point point) => TryActivateContentControl(point);

    /// <summary>
    /// Operates the content control the header/footer caret sits on — the click gesture's own path, which
    /// a headless run reaches this way because a header field has no model index to address it by.
    /// </summary>
    internal bool ActivateHfContentControlForTest() => TryActivateHfContentControl();

    /// <summary>Operates the content control the shape-text caret sits on — the click gesture's own path.</summary>
    internal bool ActivateShapeTextContentControlForTest() => TryActivateShapeTextContentControl();

    /// <summary>The date-picker calendar currently on screen, if any (see <see cref="OpenContentControlCalendarForTest"/>).</summary>
    internal global::Avalonia.Controls.Flyout? ActiveContentControlCalendarForTest => _contentControlCalendarFlyout;
    internal ContextMenu? ActiveContextMenuForTests => _activeContextMenu;
    internal void OpenEditorContextMenuForTests() => OpenEditorContextMenu();
    internal void RaiseKeyDownForContextMenuTests(KeyEventArgs args) => OnKeyDown(args);

    /// <summary>Simulates a key press through the editor's own key handling.</summary>
    internal void SimulateKeyForTest(Key key, bool shift = false, bool control = false)
    {
        var modifiers = KeyModifiers.None;
        if (shift)
            modifiers |= KeyModifiers.Shift;
        if (control)
            modifiers |= KeyModifiers.Control;
        OnKeyDown(new KeyEventArgs
        {
            Key = key,
            KeyModifiers = modifiers,
            RoutedEvent = KeyDownEvent,
        });
    }

    internal void SimulateTextInputForTest(string text)
    {
        foreach (var character in text)
            OnTextInput(new TextInputEventArgs { Text = character.ToString() });
    }

    internal int CaretBlockForTest => _caret.Block;
    internal int CaretOffsetForTest => _caret.Offset;

    internal IReadOnlyList<(int CommentId, Rect Rect)> CommentAnchorGlyphs()
    {
        Relayout(_laidOutWidth > 0 ? _laidOutWidth : FallbackWidth);
        return CommentAnchorGlyphSnapshot(highlightedOnly: false);
    }

    internal IReadOnlyList<double> ComputeRulerTicks()
    {
        if (!_showRuler || _viewMode != DocumentViewMode.PrintLayout)
            return [];
        const double inchDip = 72.0;
        return DocumentViewLayoutPlanner.BuildRulerTicks(_surfacePlan, inchDip);
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
    /// AV-CCEDIT: the placed glyphs of a block that render inside a content control — the shaded region
    /// the chrome draws — with the character each one actually renders as.
    /// </summary>
    internal IReadOnlyList<(char Ch, ContentControlKind Kind, int Offset, Rect Rect)> ContentControlGlyphsForTest(
        int blockIndex)
    {
        if (_laidOutWidth < 0)
            Relayout(FallbackWidth);

        return _placed
            .Where(p => p.Block == blockIndex && !p.Sentinel && p.Control is not null)
            .Select(p => (p.Ch, p.Control!.Kind, p.Offset, new Rect(p.X, p.Y, Math.Max(1, p.W), p.LineHeight)))
            .ToList();
    }

    /// <summary>
    /// AV-CCEDIT: the rectangle a content control occupies in the header/footer band, which renders from
    /// its own item list rather than the placed body glyphs.
    /// </summary>
    internal Rect? ContentControlHeaderFooterRegionForTest()
    {
        if (_laidOutWidth < 0)
            Relayout(FallbackWidth);

        foreach (var item in _headerFooterItems)
        {
            if (item.Control is null || string.IsNullOrEmpty(item.Text))
                continue;
            var text = Build(item.Text, item.Fmt);
            var left = item.X + AlignmentOffset(
                item.Alignment,
                item.AvailableWidth,
                text.WidthIncludingTrailingWhitespace,
                isLast: true);
            return new Rect(
                left,
                item.Y,
                Math.Max(1, text.WidthIncludingTrailingWhitespace),
                item.LineHeight > 0 ? item.LineHeight : text.Height);
        }

        return null;
    }

    /// <summary>Drives the pointer-hover affordances (tooltip + cursor) for a content control.</summary>
    internal string? ContentControlHoverTipForTest(Point point)
    {
        if (_laidOutWidth < 0)
            Relayout(FallbackWidth);

        UpdateContentControlHover(point);
        return ToolTip.GetTip(this) as string;
    }

    /// <summary>
    /// Returns the effective display formatting for a block's placed glyphs.
    /// </summary>
    internal IReadOnlyList<RunFormatting> GetPlacedFormattingForBlock(int blockIndex) =>
        _placed
            .Where(p => p.Block == blockIndex && !p.Sentinel)
            .Select(p => p.Fmt)
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

    internal IReadOnlyList<(int Block, int BreakOffset, double X, double Y, double W, double LineHeight)>
        AutomaticHyphenGlyphs
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _automaticHyphenGlyphs
                .Select(item => (item.Block, item.BreakOffset, item.X, item.Y, item.W, item.LineHeight))
                .ToList();
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
    /// Runs the shared AutoCorrect/AutoFormat-as-you-type evaluation for a just-typed character, exactly as
    /// <see cref="OnTextInput"/> does. Exposed for R143 (shared-undo-boundaries) unit tests covering the
    /// auto-recognized-hyperlink undo-step granularity.
    /// </summary>
    internal bool TryAutoCorrectPublic(char justTyped) => TryAutoCorrect(justTyped);

    /// <summary>
    /// Invoke the list Tab/Shift-Tab handler and return whether it consumed the key.
    /// Exposed for AV-LIST unit tests.
    /// </summary>
    internal bool ListTabAtItemStartPublic(bool shift) => ListTabAtItemStart(shift);

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

        var listMarkerSequence = new DocumentListMarkerSequencePlanner(
            _doc.MultiLevelList.NumberFormats, _doc.MultiLevelList.LevelTexts);
        var preservedNumberingMarkers = PreservedNumberingMarkerPlanner.Build(_doc);
        for (int i = 0; i < _doc.Blocks.Count; i++)
        {
            if (_doc.Blocks[i] is not Paragraph p)
            {
                // BT1 fix: Table and other non-Paragraph blocks (read-only, etc.) do NOT reset
                // the numbered-list counters — the render loop leaves the shared sequence untouched.
                // Word numbering continues across an intervening table; the helper must match.
                continue;
            }

            // BW1: mirror the render loop's inline-object detection (~1767-1789).
            // A paragraph that routes through LayoutImageParagraphPaged (has an inline image)
            // or LayoutInlineObjectParagraphPaged (has an inline chart/WordArt/SmartArt) resets
            // list sequencing and is treated as non-list — exactly what we must replicate here so
            // the helper and render agree for ALL paragraph kinds.
            var hasInlineImage   = p.Runs.Any(r => r.Image    is { IsFloating: false });
            var hasInlineChart   = p.Runs.Any(r => r.Chart    is { IsFloating: false });
            var hasInlineWordArt = p.Runs.Any(r => r.WordArt  is { IsFloating: false });
            var hasInlineSmArt   = p.Runs.Any(r => r.SmartArt is { IsFloating: false });
            var hasEmbeddedObject = p.Runs.Any(r => r.EmbeddedObject is not null);
            if (hasInlineImage || hasInlineChart || hasInlineWordArt || hasInlineSmArt || hasEmbeddedObject)
            {
                // Render loop resets all counters and skips list numbering for this paragraph.
                listMarkerSequence.Reset();
                if (i == blockIdx) return null;
                continue;
            }

            var kind = p.Formatting.ListKind;
            if (kind != ListKind.None)
            {
                var markerPlan = listMarkerSequence.Advance(p);
                if (i == blockIdx)
                    return kind == ListKind.Bullet ? null : markerPlan.MarkerText;
            }
            else
            {
                // R132 MED fix: a non-list paragraph does NOT end the numbered run -- Word continues
                // numbering across intervening body text (and preserved-numbering chrome).
                if (i == blockIdx)
                    return preservedNumberingMarkers.TryGetValue(i, out var preservedMarker)
                        ? preservedMarker.Text
                        : null;
            }
        }
        return null;
    }

    /// <summary>
    /// Returns the text of every rendered list/preserved-numbering marker, in document render order
    /// (body paragraphs interleaved with table-cell paragraphs in row/cell order) -- unlike
    /// <see cref="GetListMarkerForBlockPublic"/>, which only walks top-level body blocks and cannot see
    /// a marker rendered inside a table cell. Exposed for R158 table-cell list-marker tests, which need
    /// to assert numbering continuity across a body -> table -> body run.
    /// </summary>
    internal IReadOnlyList<string> AllRenderedMarkerTextsForTest
    {
        get
        {
            if (_laidOutWidth < 0)
                Relayout(FallbackWidth);
            return _markers.Select(m => m.Text).ToList();
        }
    }

    /// <summary>
    /// Simulates pressing Down (+1) or Up (-1) arrow from the current caret position.
    /// Exposed internally so regression tests can assert that vertical navigation reaches
    /// a tall inline object (ZZ1).
    /// </summary>
    internal void TestMoveCaretVertical(int direction) => MoveCaretVertical(direction, extend: false);

    /// <summary>Simulates pressing Left (-1) or Right (+1) arrow from the current caret position.</summary>
    internal void MoveCaretHorizontalForTest(int delta, bool extend = false) => MoveCaret(delta, extend);

    /// <summary>Selects a body text range without going through a pointer drag.</summary>
    internal void SetBodySelectionForTest(int anchorBlock, int anchorOffset, int caretBlock, int caretOffset)
    {
        _hfCaret = null;
        _cellCaret = null;
        _cellAnchor = null;
        _caret = new DocPosition(caretBlock, caretOffset);
        _selectionAnchor = new DocPosition(anchorBlock, anchorOffset);
    }

    // ── AV-DRAGMOVE test hooks ──────────────────────────────────────────────────────────────────
    // Drive the exact private press/move/release helpers OnPointerPressed/OnPointerMoved/
    // OnPointerReleased call for a body-text drag, the same way BeginFloatDrag/SimulateDragTo/
    // EndFloatDrag drive the floating-object drag helpers — no logic is duplicated for the test.

    /// <summary>
    /// Simulates a plain (non-shift, non-Ctrl) left-button press at <paramref name="point"/> in the
    /// document body, mirroring OnPointerPressed's own decision exactly: hit-tests the point, arms a
    /// pending drag when it falls inside the current selection (returns true), otherwise collapses the
    /// selection to the press point (returns false) the same way an ordinary click always has.
    /// </summary>
    internal bool TryArmBodyTextDragForTest(Point point)
    {
        if (_laidOutWidth < 0)
            Relayout(FallbackWidth);
        if (!TryHitTest(point, out var pos))
            return false;
        if (TryArmBodyTextDrag(point, pos))
            return true;
        _selectionAnchor = pos;
        _cellAnchor = _cellCaret;
        _caret = pos;
        return false;
    }

    /// <summary>Simulates a pointer move while a body-text drag is pending/active.</summary>
    internal void UpdateBodyTextDragForTest(Point point, bool ctrlHeld = false) =>
        UpdateBodyTextDrag(point, ctrlHeld);

    /// <summary>Simulates the release that completes (or abandons) a body-text drag.</summary>
    internal void CommitBodyTextDragForTest(Point point, bool ctrlHeld = false) =>
        CommitBodyTextDrag(point, ctrlHeld);

    internal bool BodyTextDragPendingForTest => _bodyDragPending;
    internal bool BodyTextDragActiveForTest => _bodyDragActive;

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

    /// <summary>Test-facing view of the shared rich floating-shape glyph layout.</summary>
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
                .Select(BuildFloatingGroupChildEffectSummary)
                .Where(summary => summary is not null)
                .Select(summary => summary!)
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
            return _floatingCharts.Select(c => (c.Rect, c.BehindText, c.ZOrder, c.Scene.Kind,
                c.Scene.Texts.FirstOrDefault(text => text.Kind == ChartSceneTextKind.Title)?.Text)).ToList();
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
                (c.Rect, c.BehindText, c.ZOrder, c.Scene.Kind,
                 c.Scene.Texts.FirstOrDefault(text => text.Kind == ChartSceneTextKind.Title)?.Text,
                 c.Scene.Categories,
                 c.Scene.SeriesCount)).ToList();
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

    /// <summary>Shared visual-plan summaries consumed by floating WordArt rendering.</summary>
    public IReadOnlyList<string> FloatingWordArtVisualSummaries
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _floatingWordArts.Select(w => w.StyleSummary + ";effects:" + w.Effects.Summary).ToList();
        }
    }

    /// <summary>Number of floating SmartArt diagrams collected during the last layout pass.</summary>
    public int FloatingSmartArtCount
    {
        get { if (_laidOutWidth < 0) Relayout(FallbackWidth); return _floatingSmartArts.Count; }
    }

    /// <summary>Snapshot of floating SmartArt rects for tests (rect, behind-text, z-order, kind, node count).</summary>
    public IReadOnlyList<(Rect Rect, bool BehindText, int ZOrder, SmartArtKind Kind, int NodeCount,
        int MaxHierarchyDepth, int HierarchyConnectorCount,
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
                    s.HierarchyGeometry?.MaxDepth ?? 0,
                    s.HierarchyGeometry?.Connectors.Count ?? 0,
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

    /// <summary>Snapshot of shared layout geometry plans used by floating SmartArt diagrams.</summary>
    public IReadOnlyList<(string LayoutId, string? GeometryKind, int GeometryNodeCount, int GeometryConnectorCount,
        int PolygonNodeCount, int FirstPolygonPointCount)> FloatingSmartArtLayoutGeometries
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _floatingSmartArts
                .Select(s => (
                    s.LayoutId,
                    s.LayoutGeometry?.Kind.ToString(),
                    s.LayoutGeometry?.Nodes.Count ?? 0,
                    s.LayoutGeometry?.Connectors.Count ?? 0,
                    s.LayoutGeometry?.Nodes.Count(n => n.HasPolygon) ?? 0,
                    s.LayoutGeometry?.Nodes.FirstOrDefault(n => n.HasPolygon)?.PolygonPoints.Count ?? 0))
                .ToList();
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

    /// <summary>Snapshot rectangles for floating-object owner-column alignment tests.</summary>
    // ── FO4 introspection properties (inline objects) ────────────────────────────────────────────────

    /// <summary>Number of inline (non-floating) shapes laid out in the last layout pass.</summary>
    public int InlineShapeCount
    {
        get { if (_laidOutWidth < 0) Relayout(FallbackWidth); return _inlineShapes.Count; }
    }

    /// <summary>Snapshot of inline shape rects for tests.</summary>
    public IReadOnlyList<(Rect Rect, ShapeKind Kind, string? Text)> InlineShapeRects
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _inlineShapes.Select(shape => (shape.Rect, shape.Kind, shape.Text)).ToList();
        }
    }

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
            return _inlineCharts.Select(c => (c.Rect, c.Scene.Kind,
                c.Scene.Texts.FirstOrDefault(text => text.Kind == ChartSceneTextKind.Title)?.Text)).ToList();
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

    /// <summary>Effect summaries for inline WordArt, preserving the shared WordArt visual planner output.</summary>
    public IReadOnlyList<string> InlineWordArtEffectSummaries
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _inlineWordArts
                .Where(w => w.Effects.HasAny)
                .Select(w => w.Effects.Summary)
                .ToList();
        }
    }

    /// <summary>Shared visual-plan summaries consumed by inline WordArt rendering.</summary>
    public IReadOnlyList<string> InlineWordArtVisualSummaries
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _inlineWordArts.Select(w => w.StyleSummary + ";effects:" + w.Effects.Summary).ToList();
        }
    }

    /// <summary>Number of inline (non-floating) SmartArt diagrams laid out in the last layout pass.</summary>
    public int InlineSmartArtCount
    {
        get { if (_laidOutWidth < 0) Relayout(FallbackWidth); return _inlineSmartArts.Count; }
    }

    /// <summary>Snapshot of inline SmartArt rects for tests (rect, kind, node count).</summary>
    public IReadOnlyList<(Rect Rect, SmartArtKind Kind, int NodeCount,
        int MaxHierarchyDepth, int HierarchyConnectorCount,
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
                    s.HierarchyGeometry?.MaxDepth ?? 0,
                    s.HierarchyGeometry?.Connectors.Count ?? 0,
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

    /// <summary>Snapshot of shared layout geometry plans used by inline SmartArt diagrams.</summary>
    public IReadOnlyList<(string LayoutId, string? GeometryKind, int GeometryNodeCount, int GeometryConnectorCount,
        int PolygonNodeCount, int FirstPolygonPointCount)> InlineSmartArtLayoutGeometries
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _inlineSmartArts
                .Select(s => (
                    s.LayoutId,
                    s.LayoutGeometry?.Kind.ToString(),
                    s.LayoutGeometry?.Nodes.Count ?? 0,
                    s.LayoutGeometry?.Connectors.Count ?? 0,
                    s.LayoutGeometry?.Nodes.Count(n => n.HasPolygon) ?? 0,
                    s.LayoutGeometry?.Nodes.FirstOrDefault(n => n.HasPolygon)?.PolygonPoints.Count ?? 0))
                .ToList();
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

    internal IReadOnlyList<(Rect SourceRect, Rect VisualRect)> InlineImageVisualRects
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _images
                .Select(i => (i.Rect, i.Image?.VisualRect(i.Rect) ?? i.Rect))
                .ToList();
        }
    }

    internal IReadOnlyList<(Rect SourceRect, Rect VisualRect)> FloatingImageVisualRects
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _floatingImages
                .Select(i => (i.Rect, i.Image?.VisualRect(i.Rect) ?? i.Rect))
                .ToList();
        }
    }

    internal IReadOnlyList<(Rect Rect, EmbeddedObjectVisualPlan Plan, bool HasDecodedIcon,
        int BlockIndex, int RunIndex, int CellRow, int CellColumn, int CellParagraphIndex)>
        EmbeddedObjectRenderItems
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _inlineEmbeddedObjects
                .Select(item => (item.Rect, item.Plan, item.Icon is not null,
                    item.BlockIndex, item.RunIndex, item.CellRow, item.CellColumn, item.CellParagraphIndex))
                .ToList();
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
                .Where(i => i.Image is null && !string.IsNullOrEmpty(i.Text)) // AV-HFEDIT: skip empty editable-region placeholders
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
                .Where(i => i.Image is null && !string.IsNullOrEmpty(i.Text)) // AV-HFEDIT: skip empty editable-region placeholders
                .Select(i => (i.Text, i.X, i.Y, i.Alignment, i.AvailableWidth))
                .ToList();
        }
    }

    internal IReadOnlyList<(string Signature, Rect Rect, TextAlignment Alignment, string SlotName)> HeaderFooterImageItems
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _headerFooterItems
                .Where(i => i.Image is not null)
                .Select(i => (
                    i.ImageSignature ?? string.Empty,
                    new Rect(i.X, i.Y, Math.Max(1, i.Width), Math.Max(1, i.Height)),
                    i.Alignment,
                    HeaderFooterDialogPlanner.SlotNameFor(i.Slot)))
                .ToList();
        }
    }

    internal IReadOnlyList<(Rect Rect, EmbeddedObjectVisualPlan Plan, bool HasDecodedIcon,
        HeaderFooterSlotKind Slot, int RunIndex)> HeaderFooterEmbeddedObjectItems
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _headerFooterItems
                .Where(item => item.EmbeddedObject is not null)
                .Select(item => (
                    new Rect(item.X, item.Y, Math.Max(1, item.Width), Math.Max(1, item.Height)),
                    item.EmbeddedObject!,
                    item.EmbeddedObjectIcon is not null,
                    item.Slot,
                    item.RunIndex))
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
                (c.Scene.Legend.Count > 0,
                 c.Scene.Texts.Any(text => text.Kind == ChartSceneTextKind.DataLabel),
                 c.Scene.Texts.FirstOrDefault(text => text.Kind == ChartSceneTextKind.AxisTitle && text.RotationDegrees == 0)?.Text,
                 c.Scene.Texts.FirstOrDefault(text => text.Kind == ChartSceneTextKind.AxisTitle && text.RotationDegrees != 0)?.Text)).ToList();
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
                (c.Scene.Legend.Count > 0,
                 c.Scene.Texts.Any(text => text.Kind == ChartSceneTextKind.DataLabel),
                 c.Scene.Texts.FirstOrDefault(text => text.Kind == ChartSceneTextKind.AxisTitle && text.RotationDegrees == 0)?.Text,
                 c.Scene.Texts.FirstOrDefault(text => text.Kind == ChartSceneTextKind.AxisTitle && text.RotationDegrees != 0)?.Text)).ToList();
        }
    }

    internal IReadOnlyList<(ChartVisualGeometryKind GeometryKind, IReadOnlyList<string> PaletteHex)> InlineChartVisualPlans
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _inlineCharts.Select(c =>
                (c.Scene.GeometryKind, c.Scene.PaletteHex)).ToList();
        }
    }

    public IReadOnlyList<(string Text, EquationVisualSegmentRole Role, EquationVisualBaselineRole BaselineRole,
        double FontSizeScale, string FontFamily, bool Italic)> EquationVisualSegments
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _equationVisualSegments.ToList();
        }
    }

    public IReadOnlyList<(EquationVisualElementKind Kind, string LinearText, string Numerator, string Denominator,
        string Radicand, string Degree, string Operator, string LowerLimit, string UpperLimit, string Operand,
        IReadOnlyList<EquationVisualMatrixRow> MatrixRows, string BaseText, string Accent, bool BarTop,
        string OpenDelimiter, string CloseDelimiter, string GroupCharacter, string GroupCharacterPosition,
        string FunctionName, string FunctionArgument)>
        EquationVisualElements
    {
        get
        {
            if (_laidOutWidth < 0) Relayout(FallbackWidth);
            return _equationVisualElements.ToList();
        }
    }
}
