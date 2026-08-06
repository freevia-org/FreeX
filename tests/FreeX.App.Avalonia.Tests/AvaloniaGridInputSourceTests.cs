using System.IO;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaGridInputSourceTests
{
    [Fact]
    public void WorksheetHeaders_ExposeResizeHandlesAndCommitThroughSessionSizing()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        // Split panes (K10) fold Window ▸ Split's pinned rows/columns ahead of the main pane's own
        // viewport.ColMetrics into one combined `colMetrics` sequence (CombineSplitColumnMetrics), so
        // headers are built from that combined local rather than viewport.ColMetrics directly.
        source.Should().Contain("CreateColumnHeaderCell(col, colMetrics[colIndex], selected, zoomFactor)");
        source.Should().Contain("CreateRowHeaderCell(row, rowMetric, selectedRow, zoomFactor)");
        source.Should().Contain("AddColumnResizeHandle(header, col, metric, zoomFactor)");
        source.Should().Contain("AddRowResizeHandle(header, row, metric, zoomFactor)");
        source.Should().Contain("BeginHeaderResize(args, handle, HeaderResizeKind.Column");
        source.Should().Contain("BeginHeaderResize(args, handle, HeaderResizeKind.Row");
        source.Should().Contain("IsHeaderResizeHotspot(point.Position, header.Bounds, HeaderResizeKind.Column)");
        source.Should().Contain("IsHeaderResizeHotspot(point.Position, header.Bounds, HeaderResizeKind.Row)");
        source.Should().Contain("private const double HeaderResizeHitThickness = 9;");
        source.Should().Contain("args.Pointer.Capture(_sheetGridHost)");
        source.Should().Contain("_sheetGridHost.PointerMoved += HeaderResizeCapturePointerMoved;");
        source.Should().Contain("_sheetGridHost.PointerReleased += HeaderResizeCapturePointerReleased;");
        source.Should().Contain("GridResizeSizePlanner.ClampColumnSize(requestedSize)");
        source.Should().Contain("GridResizeSizePlanner.ClampRowSize(requestedSize)");
        source.Should().Contain("new SetColumnWidthCommand(");
        source.Should().Contain("new SetRowHeightCommand(");
        var commitResize = source[
            source.IndexOf("private void CommitHeaderResize(", StringComparison.Ordinal)..
            source.IndexOf("private void PreviewHeaderResize(", StringComparison.Ordinal)];
        commitResize.Should().NotContain("SelectEntireColumn(");
        commitResize.Should().NotContain("SelectEntireRow(");
    }

    [Fact]
    public void WorksheetHeaderBoundaryDoubleClick_RoutesToAutoFit()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("handle.DoubleTapped += (_, args) =>");
        source.Should().Contain("AutoFitColumnFromHeader(col);");
        source.Should().Contain("AutoFitRowFromHeader(row);");
        source.Should().Contain("AutoFitSelectedColumnWidth();");
        source.Should().Contain("AutoFitSelectedRowHeight();");

        var columnHandle = source[
            source.IndexOf("private Control AddColumnResizeHandle(", StringComparison.Ordinal)..
            source.IndexOf("private Control AddRowResizeHandle(", StringComparison.Ordinal)];
        columnHandle.Should().Contain("if (args.ClickCount >= 2)");
        columnHandle.IndexOf("if (args.ClickCount >= 2)", StringComparison.Ordinal)
            .Should().BeLessThan(columnHandle.IndexOf("BeginHeaderResize(", StringComparison.Ordinal));

        var rowHandle = source[
            source.IndexOf("private Control AddRowResizeHandle(", StringComparison.Ordinal)..
            source.IndexOf("private static Border CreateHeaderResizeHandle", StringComparison.Ordinal)];
        rowHandle.Should().Contain("if (args.ClickCount >= 2)");
        rowHandle.IndexOf("if (args.ClickCount >= 2)", StringComparison.Ordinal)
            .Should().BeLessThan(rowHandle.IndexOf("BeginHeaderResize(", StringComparison.Ordinal));
    }

    [Fact]
    public void WorksheetCells_UsePointerCaptureForDragRangeSelection()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private CellAddress? _cellDragSelectionAnchor;");
        source.Should().Contain("BeginCellSelectionDrag(args, border, address);");
        // Capture lives on the survivor host (not the per-cell Border, which RefreshShell rebuilds
        // mid-drag), and a PointerCaptureLost handler aborts the drag if the OS revokes capture.
        source.Should().Contain("_sheetGridHost.PointerMoved += CellSelectionCapturePointerMoved;");
        source.Should().Contain("_sheetGridHost.PointerReleased += CellSelectionCapturePointerReleased;");
        source.Should().Contain("_sheetGridHost.PointerCaptureLost += CellSelectionCapturePointerCaptureLost;");
        source.Should().Contain("DetachCellSelectionDragHandlers();");
        source.Should().Contain("args.Pointer.Capture(_sheetGridHost);");
        source.Should().Contain("TryResolveCellPointerAddress(args, out var pointerAddress)");
        source.Should().Contain("TryContinueFormulaRangeSelectionDrag(target)");
        source.Should().Contain("if (_cellDragFormulaPointCursor == address)");
        source.Should().Contain("TrackFormulaPointDragAnchor(address, referenceStart, referenceLength);");
        source.Should().Contain("_formulaReferenceStart = _cellDragFormulaReferenceStart;");
        source.Should().Contain("_formulaReferenceLength = _cellDragFormulaReferenceLength;");
        source.Should().Contain("SelectRangeFromAnchor(anchor, target);");
        source.Should().Contain("_cellDragSelectionPointer?.Capture(null);");
        source.Should().Contain("_session.SelectAnchoredRange(anchor, address);");
        source.Should().Contain("TryInsertFormulaPointReference(address))");
        source.Should().Contain("BeginCellSelectionDrag(args, border, address);");
        source.Should().Contain("RestoreFormulaRangeEditorFocusAfterDrag(formulaRangeEditor);");

        var continuation = source[
            source.IndexOf("private void ContinueCellSelectionDrag(", StringComparison.Ordinal)..
            source.IndexOf("private async Task EndCellSelectionDragAsync(", StringComparison.Ordinal)];
        continuation.IndexOf("TryContinueFormulaRangeSelectionDrag(target)", StringComparison.Ordinal)
            .Should().BeLessThan(continuation.IndexOf("SelectRangeFromAnchor(anchor, target)", StringComparison.Ordinal));
        continuation.Replace("\r\n", "\n", StringComparison.Ordinal).Should().Contain(
            "if (TryContinueFormulaRangeSelectionDrag(target))\n        {\n            args.Handled = true;\n            return;\n        }");
    }

    [Fact]
    public void FormulaEditing_RendersReferenceTextColorsAndGridAdornersWithoutOwningGestures()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private static readonly IReadOnlyList<IBrush> FormulaReferenceBrushes");
        source.Should().Contain("private readonly TextBlock _formulaReferenceTextOverlay = new();");
        source.Should().Contain("_formulaBox.TextChanged += FormulaBox_TextChanged;");
        source.Should().Contain("FormulaReferenceHighlightPlanner.GetHighlights(");
        source.Should().Contain("ResolveStructuredFormulaReference");
        source.Should().Contain("StructuredReferenceResolver.ResolveCurrentRowColumn");
        source.Should().Contain("StructuredReferenceResolver.Resolve(");
        source.Should().Contain("AddFormulaReferenceHighlightOverlay(overlay, viewport, showHeadings, zoomFactor);");
        source.Should().Contain("TryGetDisplayedRangeBounds(");
        source.Should().Contain("IsHitTestVisible = false");
        // R78-render-inplace-editor-5-2: the reference-coloring overlay logic was generalized to an
        // (editor, overlay) pair so the in-cell editor gets the same colored runs as the formula bar
        // -- it no longer hardcodes `_formulaBox` here.
        source.Should().Contain("editor.Foreground = Brushes.Transparent;");
        source.Should().Contain("new Run(text) { Foreground = brush }");
        source.Should().Contain("RefreshShell(\"Ready\");");
    }

    [Fact]
    public void WorksheetCells_WireAutofillHandleAndSelectionMoveDrag()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var sessionSource = File.ReadAllText(RepoFile("src", "FreeX.App.Services", "WorkbookSession.cs"));

        source.Should().Contain("AddSelectionOverlayToGrid(");
        source.Should().Contain("AutomationProperties.SetAutomationId(outline, \"WorksheetSelectionOutline\");");
        source.Should().Contain("AutomationProperties.SetAutomationId(handle, \"WorksheetAutofillHandle\");");
        source.Should().Contain("TryBeginAutofillDrag(args, border, address)");
        source.Should().Contain("GridAutofillPlanner.IsOnHandle(");
        source.Should().NotContain("!_session.SelectedRange.Contains(address)");
        source.Should().Contain("private const double AutofillHandleSize = 10;");
        source.Should().Contain("private const double AutofillHandleHitPadding = 6;");
        source.Should().Contain("if (IsPointerOnAutofillHandle(args))");
        source.Should().Contain("? new Cursor(StandardCursorType.Hand)");
        source.Should().Contain(": Cursor.Default;");
        source.Should().Contain("HasHyperlinkActivationModifier(args.KeyModifiers)");
        source.Should().Contain("await OpenSelectedHyperlinkAsync();");
        source.Should().Contain("GridAutofillPlanner.CalculateCompletedSelectionRange(source, operationRange)");
        source.Should().Contain("_session.FillSelectedRange(direction)");
        source.Should().Contain("TryBeginSelectionMoveDrag(args, border, address)");
        source.Should().Contain("GridSelectionMovePlanner.IsOnMoveBorder(");
        source.Should().Contain("GridSelectionMovePlanner.CalculateTargetRange(");
        source.Should().Contain("_session.MoveSelectedRangeTo(source, target)");
        sessionSource.Should().Contain("public WorkbookCellEditResult MoveSelectedRangeTo(GridRange sourceRange, GridRange targetRange)");
        sessionSource.Should().Contain("new MoveRangeCommand(ActiveSheet.Id, sourceRange, targetRange.Start)");
    }

    [Fact]
    public void CtrlCopySelection_RebuildsAfterRestoringDestinationRange()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var copyStart = source.IndexOf("var copyResult = _session.ExecuteReviewCommand(", StringComparison.Ordinal);
        var copyEnd = source.IndexOf("return;", copyStart, StringComparison.Ordinal);

        copyStart.Should().BeGreaterThanOrEqualTo(0);
        copyEnd.Should().BeGreaterThan(copyStart);
        var copyBranch = source[copyStart..copyEnd];
        var firstRefresh = copyBranch.IndexOf("RefreshShell(copyStatus);", StringComparison.Ordinal);
        var restoreSelection = copyBranch.IndexOf("_session.SelectRange(target);", StringComparison.Ordinal);
        var finalRefresh = copyBranch.LastIndexOf("RefreshShell(copyStatus);", StringComparison.Ordinal);

        firstRefresh.Should().BeGreaterThanOrEqualTo(0);
        restoreSelection.Should().BeGreaterThan(firstRefresh,
            "the complete destination range must be restored after the generic edit refresh collapses selection");
        finalRefresh.Should().BeGreaterThan(restoreSelection,
            "the second grid rebuild must render the restored destination range");
    }

    // ── R83-render-selection-fillhandle-5-2: fill-handle hover must use the crosshair, not Hand ──

    [Fact]
    public void FormulaPointCtrlClick_TakesPrecedenceOverHyperlinkActivation()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var pointerStart = source.IndexOf("border.PointerPressed += (_, args) =>", StringComparison.Ordinal);
        var pointerEnd = source.IndexOf(
            "// PointerMoved and PointerReleased for cell-selection drag",
            pointerStart,
            StringComparison.Ordinal);

        pointerStart.Should().BeGreaterThanOrEqualTo(0);
        pointerEnd.Should().BeGreaterThan(pointerStart);
        var pointerHandler = source[pointerStart..pointerEnd];

        var appendIndex = pointerHandler.IndexOf(
            "TryAppendDisjointFormulaPointReference(address)",
            StringComparison.Ordinal);
        var hyperlinkIndex = pointerHandler.IndexOf(
            "HasHyperlinkActivationModifier(args.KeyModifiers)",
            StringComparison.Ordinal);

        appendIndex.Should().BeGreaterThanOrEqualTo(0);
        hyperlinkIndex.Should().BeGreaterThanOrEqualTo(0);
        appendIndex.Should().BeLessThan(
            hyperlinkIndex,
            "WPF/Excel must append a formula area before Ctrl+click hyperlink navigation is considered");
        pointerHandler.Should().Contain("IsFormulaDisjointReferenceModifier(args.KeyModifiers)");
    }

    [Fact]
    public void WorksheetCapturedDrags_RequestSharedEdgeAutoScrollAndRefreshViewport()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var dragStart = source.IndexOf("private void ContinueCellSelectionDrag(", StringComparison.Ordinal);
        var dragEnd = source.IndexOf("private async Task EndCellSelectionDragAsync(", dragStart, StringComparison.Ordinal);

        dragStart.Should().BeGreaterThanOrEqualTo(0);
        dragEnd.Should().BeGreaterThan(dragStart);

        var drag = source[dragStart..dragEnd];
        drag.Should().Contain("RequestCellDragAutoScroll(args);");
        drag.IndexOf("RequestCellDragAutoScroll(args);", StringComparison.Ordinal)
            .Should()
            .BeLessThan(drag.IndexOf("TryResolveCellPointerAddress(args, out var pointerAddress)", StringComparison.Ordinal));

        var autoScrollStart = source.IndexOf("private void RequestCellDragAutoScroll(", StringComparison.Ordinal);
        var autoScrollEnd = source.IndexOf("private async Task EndCellSelectionDragAsync(", autoScrollStart, StringComparison.Ordinal);
        var autoScroll = source[autoScrollStart..autoScrollEnd];
        autoScroll.Should().Contain("GridAutofillPlanner.CalculateEdgeScrollIntent(");
        autoScroll.Should().Contain("WorkbookViewportScrollPlanner.CalculateDragAutoScroll(");
        autoScroll.Should().Contain("WorkbookViewportScrollPlanner.CalculateViewportOrigin(");
        autoScroll.Should().Contain("RefreshShellForViewportPan(\"Ready\");");
        autoScroll.Should().Contain("BroadcastScrollOffsetToSideBySidePartner();");
        drag.Should().Contain("if (_selectionMoveDragging)");
        drag.Should().Contain("ContinueSelectionMoveDrag(args, target);");
        drag.Should().Contain("if (_autofillDragging)");
        drag.Should().Contain("ContinueAutofillDrag(args, target);");
        drag.Should().Contain("SelectRangeFromAnchor(anchor, target);");
    }

    [Fact]
    public void SheetGridHostPointerMoved_FillHandleHover_UsesCrosshairNotHandCursor()
    {
        // Failure scenario: hovering the fill-handle grip previously set _sheetGridHost.Cursor to
        // the same StandardCursorType.Hand used for hyperlink hover (see the ternary a few lines
        // below in the same handler, and the header/select-all-corner Hand cursors), giving Linux/
        // macOS users the pointing-hand "click to navigate" affordance over a "drag to fill" target
        // instead of Excel/WPF's thin black crosshair (GridView.Input.cs:344 `Cursors.Cross`).
        //
        // The exact bare statement `_sheetGridHost.Cursor = new Cursor(StandardCursorType.Hand);`
        // only ever existed at the fill-handle branch of SheetGridHost_PointerMoved -- every other
        // Hand-cursor assignment in the file is either a ternary expression (hyperlink hover,
        // header hover) or targets a different control (border-draw-mode/header controls), so this
        // single assertion pins the exact regression without being sensitive to CRLF line endings.
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().NotContain(
            "_sheetGridHost.Cursor = new Cursor(StandardCursorType.Hand);",
            "the fill handle must show Excel/WPF's crosshair cursor, not the Hand cursor used for hyperlink hover");
    }

    [Fact]
    public void SheetGridHostPointerMoved_OtherCursorAffordances_AreUnaffected()
    {
        // No-regression sibling: the fix must only touch the fill-handle branch. The border-draw-
        // mode crosshair, the selection-move SizeAll cursor, and the Ctrl+hyperlink-hover ternary
        // (still Hand, by design -- it really is a click-to-navigate affordance) must all still be
        // present and unchanged.
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("_sheetGridHost.Cursor = new Cursor(StandardCursorType.Cross);");
        source.Should().Contain("_sheetGridHost.Cursor = new Cursor(StandardCursorType.SizeAll);");
        source.Should().Contain("if (IsPointerOnAutofillHandle(args))");
        source.Should().Contain("? new Cursor(StandardCursorType.Hand)");
        source.Should().Contain(": Cursor.Default;");
        source.Should().Contain("HasHyperlinkActivationModifier(args.KeyModifiers)");
    }

    [Fact]
    public void DrawingObjects_ExposeWpfParityResizeAndRotationHandles()
    {
        var windowSource = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var interactionSource = File.ReadAllText(RepoFile(
            "src", "FreeX.App.Avalonia", "MainWindow.DrawingObjectInteraction.cs"));

        windowSource.Should().Contain("CreateDrawingObjectSelectionAdorner(width, height, drawingObject.RotationDegrees)");
        windowSource.Should().Contain("TryBeginDrawingObjectDrag(renderPlan, container, surface, adorner, args)");
        windowSource.Should().Contain("WireDrawingObjectDragMoveRelease(renderPlan, container, surface)");

        interactionSource.Should().Contain("private const double DrawingObjectHandleSize = 8;");
        interactionSource.Should().Contain("private const double DrawingObjectRotationGripDiameter = 10;");
        interactionSource.Should().Contain("ObjectDragPlanner.RotationGripOffset");
        interactionSource.Should().Contain("ObjectDragPlanner.HitTestHandle(");
        interactionSource.Should().Contain("ObjectDragPlanner.CalculateDragTransform(");
        interactionSource.Should().Contain("ObjectDragPlanner.CalculateRotationDegrees(");
        interactionSource.Should().Contain("ObjectDragPlanner.ShouldCommitMove(");
        interactionSource.Should().Contain("ObjectDragPlanner.ShouldCommitResize(");
        interactionSource.Should().Contain("DrawingObjectCommandPlanner.BuildResizeWithAnchorCommand(");
        interactionSource.Should().Contain("DrawingObjectCommandPlanner.BuildResizeCommand(");
        interactionSource.Should().Contain("DrawingObjectCommandPlanner.BuildRotateCommand(");
        interactionSource.Should().Contain("args.Pointer.Capture(container);");
        interactionSource.Should().Contain("args.Pointer.Capture(null);");
        interactionSource.Should().Contain("container.PointerCaptureLost +=");
        interactionSource.Should().Contain("RefreshShell(string.Empty);");
    }

    [Fact]
    public void WorksheetHeaders_ResolvePointerDragAcrossVisibleHeaderMetrics()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("TryResolveColumnHeaderPointerIndex(args, out var col)");
        source.Should().Contain("TryResolveRowHeaderPointerIndex(args, out var row)");
        source.Should().Contain("var pos = args.GetPosition(_sheetGridHost);");
        // Split panes (K10): header pointer-drag resolution walks the combined split+main pane
        // metrics (CombineSplitColumnMetrics/CombineSplitRowMetrics) so dragging across a pinned
        // split header resolves correctly too, not just viewport.ColMetrics/RowMetrics (main pane).
        source.Should().Contain("foreach (var metric in CombineSplitColumnMetrics(_session.Viewport))");
        source.Should().Contain("foreach (var metric in CombineSplitRowMetrics(_session.Viewport))");
        source.Should().Contain("SelectEntireColumnFromHeaderDrag(targetCol, _headerSelectionDragAnchorIndex);");
        source.Should().Contain("SelectEntireRowFromHeaderDrag(targetRow, _headerSelectionDragAnchorIndex);");
    }

    [Fact]
    public void WorksheetContextClick_AcceptsRightClickAndMacControlClick()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private static bool IsContextClick(PointerPoint point, PointerEventArgs args)");
        source.Should().Contain("point.Properties.IsRightButtonPressed");
        source.Should().Contain("OperatingSystem.IsMacOS()");
        source.Should().Contain("args.KeyModifiers.HasFlag(KeyModifiers.Control)");
        source.Should().Contain("HasHyperlinkActivationModifier(args.KeyModifiers)");
        source.Should().Contain("if (IsContextClick(point, args))");
        source.Should().Contain("OpenWorksheetCellContextMenu((Control?)_activeCellBorder ?? _sheetGridHost);");
        source.Should().Contain("OpenColumnHeaderContextMenu(_sheetGridHost);");
        source.Should().Contain("OpenRowHeaderContextMenu(_sheetGridHost);");
    }

    [Fact]
    public void SelectAllCorner_SelectsWholeSheetAndExposesStableAutomation()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("CreateSelectAllCornerCell(zoomFactor)");
        source.Should().Contain("AutomationProperties.SetAutomationId(header, \"WorksheetSelectAllCorner\");");
        source.Should().Contain("private void SelectAllCells()");
        source.Should().Contain("new CellAddress(sheetId, 1, 1)");
        source.Should().Contain("new CellAddress(sheetId, CellAddress.MaxRow, CellAddress.MaxCol)");
        source.Should().Contain("_session.SelectRange(range);");
    }

    [Fact]
    public void ShiftArrowNavigation_ExtendsRangeInsteadOfMovingAnchor()
    {
        // NOTE: this test previously pinned NavigateActiveCell's old plain-move-only switch
        // (literal `MoveOrExtendActiveCell(-1, 0, extendSelection);` call sites with no Ctrl
        // boundary-jump, End-mode, or merge-snap support - see J9/J34/J36 in the review corpus).
        // NavigateActiveCell now routes through ExcelWorksheetNavigationPlanner and
        // MoveOrExtendActiveCellTo(CellAddress, bool) so it can also handle Ctrl+Arrow/Home/End
        // and End-mode; this test is updated to pin the new structure while still verifying the
        // same externally-observable behavior (Shift+Arrow extends the range instead of moving
        // the anchor, using a persisted extension anchor/cursor).
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("var extendSelection = e.KeyModifiers.HasFlag(KeyModifiers.Shift);");
        source.Should().Contain("private CellAddress? _selectionExtensionAnchor;");
        source.Should().Contain("private CellAddress? _selectionExtensionCursor;");
        source.Should().Contain("var anchor = _selectionExtensionAnchor ?? _session.ActiveCell;");
        source.Should().Contain("_selectionExtensionAnchor = anchor;");
        source.Should().Contain("_selectionExtensionCursor = target;");
        source.Should().Contain("_session.SelectAnchoredRange(anchor, target);");
        source.Should().Contain("private void MoveOrExtendActiveCellTo(CellAddress target, bool extendSelection)");
    }

    [Fact]
    public void DoubleClickCell_OpensInlineEditorWithHitTestedCaret()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("border.DoubleTapped += (_, args) =>");
        source.Should().Contain("point.Properties.IsLeftButtonPressed && IsCellDoubleClick(address, args.ClickCount)");
        source.Should().Contain("Stopwatch.GetElapsedTime(_lastCellPointerPressTimestamp, now).TotalMilliseconds");
        source.Should().Contain("CalculateInlineCellCaretIndex(");
        source.Should().Contain("BeginInlineCellEdit(address, editText, caretIndex);");
        // R78-render-inplace-editor-5-2: CreateInlineCellEditor now returns the editor wrapped
        // together with its own reference-highlight overlay in a container Control (mirroring the
        // formula bar's overlay host), instead of returning the bare TextBox directly.
        source.Should().Contain("private Control CreateInlineCellEditor(");
        source.Should().Contain("AutomationProperties.SetAutomationId(editor, \"WorksheetInlineCellEditor\");");
        source.Should().Contain("editor.Focus();");
        source.Should().Contain("editor.CaretIndex = caret;");
        source.Should().Contain("new FormattedText(");
        source.Should().Contain("BeginInlineCellEdit(address, editText, editText.Length);");
    }

    [Fact]
    public void DeleteKey_InTextEditor_DoesNotClearSelectedCells()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private bool IsTextEditingEventSource(KeyEventArgs args)");
        source.Should().Contain("_inlineCellEditor?.IsFocused == true");
        source.Should().Contain("args.Source is TextBox");
        source.Should().Contain("args.Source is TextPresenter");
        source.Should().Contain("if (IsTextEditingEventSource(e))");
        source.Should().Contain("ClearSelectedRangeContents();");
    }

    [Fact]
    public void AddArrowheadOverlays_PassesNoFlipToLineEndpoints_BecauseContainerTransformAlreadyFlips()
    {
        // Regression guard for the double-flip arrowhead bug:
        // ApplyDrawingObjectTransform sets a ScaleTransform on the arrowhead overlay container,
        // which already mirrors all child Paths. LineEndpoints must therefore receive
        // flipHorizontal: false / flipVertical: false so the outer transform is the ONLY flip
        // applied. If these are ever changed back to d.FlipHorizontal / d.FlipVertical, arrowheads
        // will land at the wrong corners and point the wrong way on flipped connectors.
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        // The call in AddArrowheadOverlays must pass literal false for both flip arguments.
        source.Should().Contain("flipHorizontal: false, flipVertical: false, kind)",
            because: "AddArrowheadOverlays must not pre-flip endpoints — the container ScaleTransform already handles the flip");

        // The outer transform method that applies the flip to the container must still exist.
        source.Should().Contain("ApplyDrawingObjectTransform(",
            because: "the container-level flip transform that makes passing false correct must remain in place");
    }

    // ── WordArt render parity (WW3 + WW4) ────────────────────────────────────

    [Fact]
    public void WordArtOverlay_RendersTextOutlineLayerWhenShapeTextOutlineColorIsSet()
    {
        // WW3: CreateShapeTextOverlay must return a Panel with offset outline TextBlocks
        // when IsWordArt=true and ShapeTextOutlineColor is set, approximating WPF's per-glyph stroke.
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var overlayMethod = source[
            source.IndexOf("private static Control CreateShapeTextOverlay", StringComparison.Ordinal)..
            source.IndexOf("// Maps DrawingShapeOutlineDash", StringComparison.Ordinal)];

        // Return type must be Control (not TextBlock) to allow returning Panel for outline case.
        overlayMethod.Should().Contain("private static Control CreateShapeTextOverlay(");
        overlayMethod.Should().NotContain("private static TextBlock CreateShapeTextOverlay(");

        // Outline branch: guarded by IsWordArt + ShapeTextOutlineColor.
        overlayMethod.Should().Contain("d.IsWordArt && d.ShapeTextOutlineColor is { } outlineColor");

        // Outline layer uses a Panel to hold offset TextBlocks + the fill text on top.
        overlayMethod.Should().Contain("var panel = new Panel");
        overlayMethod.Should().Contain("panel.Children.Add(");

        // Fill text is added last (on top of outline layers).
        var panelAdd = overlayMethod.IndexOf("var panel = new Panel", StringComparison.Ordinal);
        var fillBlockAdd = overlayMethod.LastIndexOf("panel.Children.Add(fillBlock)", StringComparison.Ordinal);
        fillBlockAdd.Should().BeGreaterThan(panelAdd,
            "fillBlock must be added to the panel after the outline layers so it renders on top");

        // Gradient fill is still applied to fillBlock even when an outline is present.
        overlayMethod.Should().Contain("d.IsWordArt && d.ShapeTextGradientEndColor is { } gradEnd");
        var gradientIdx = overlayMethod.IndexOf("d.IsWordArt && d.ShapeTextGradientEndColor is { } gradEnd", StringComparison.Ordinal);
        gradientIdx.Should().BeLessThan(panelAdd,
            "gradient brush must be set on textBrush before the outline Panel branch so fillBlock inherits it");
    }

    [Fact]
    public void WordArtOverlay_RendersBodyFillWhenShapeHasAuthoredFill()
    {
        // WW4 (Avalonia): The body fill must NOT be suppressed for WordArt shapes that carry
        // an authored fill (FillColor non-null). Only WordArt with FillColor=null uses Transparent.
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var createVisual = source[
            source.IndexOf("private static Control CreateDrawingShapeVisual(", StringComparison.Ordinal)..
            source.IndexOf("private static void AddArrowheadOverlays(", StringComparison.Ordinal)];

        // The body fill should now be driven by FillColor alone (non-null = has fill).
        // The old unconditional IsWordArt → Transparent gate must be gone.
        createVisual.Should().Contain(
            "var metadata = DrawingObjectRenderMetadataPlanner.ResolveBoundsShapeRenderMetadata(drawingObject);",
            "body fill fallback policy should be owned by the shared Presentation planner");
        createVisual.Should().Contain(
            "var fill = metadata.FillColor is { } fc",
            "Avalonia should only translate resolved colors to brushes");
        createVisual.Should().Contain(
            ": Brushes.Transparent;",
            "null resolved fill still renders transparently for WordArt without an authored body fill");

        // The old pattern that gated fill on !IsWordArt unconditionally must not appear.
        createVisual.Should().NotContain(
            "!drawingObject.IsWordArt && drawingObject.FillColor is { } fc",
            "IsWordArt must not unconditionally suppress FillColor-bearing shapes");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");

        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}
