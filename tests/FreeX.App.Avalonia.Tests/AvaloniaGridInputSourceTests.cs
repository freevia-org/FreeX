using System.IO;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaGridInputSourceTests
{
    [Fact]
    public void WorksheetHeaders_ExposeResizeHandlesAndCommitThroughSessionSizing()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("CreateColumnHeaderCell(col, viewport.ColMetrics[colIndex], selected, zoomFactor)");
        source.Should().Contain("CreateRowHeaderCell(row, rowMetric, selectedRow, zoomFactor)");
        source.Should().Contain("AddColumnResizeHandle(header, col, metric, zoomFactor)");
        source.Should().Contain("AddRowResizeHandle(header, row, metric, zoomFactor)");
        source.Should().Contain("BeginHeaderResize(args, handle, HeaderResizeKind.Column");
        source.Should().Contain("BeginHeaderResize(args, handle, HeaderResizeKind.Row");
        source.Should().Contain("args.Pointer.Capture(_sheetGridHost)");
        source.Should().Contain("_sheetGridHost.PointerMoved += HeaderResizeCapturePointerMoved;");
        source.Should().Contain("_sheetGridHost.PointerReleased += HeaderResizeCapturePointerReleased;");
        source.Should().Contain("GridResizeSizePlanner.ClampColumnSize(requestedSize)");
        source.Should().Contain("GridResizeSizePlanner.ClampRowSize(requestedSize)");
        source.Should().Contain("_session.SetSelectedColumnsWidth(ColumnWidthPixelMapper.PixelsToColumnWidth(clampedSize))");
        source.Should().Contain("_session.SetSelectedRowsHeight(clampedSize)");
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
    }

    [Fact]
    public void WorksheetCells_UsePointerCaptureForDragRangeSelection()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private CellAddress? _cellDragSelectionAnchor;");
        source.Should().Contain("BeginCellSelectionDrag(args, border, address);");
        source.Should().Contain("border.PointerMoved += (_, args) => ContinueCellSelectionDrag(args, address);");
        source.Should().Contain("border.PointerReleased += (_, args) => EndCellSelectionDrag(args);");
        source.Should().Contain("args.Pointer.Capture(capture);");
        source.Should().Contain("TryResolveCellPointerAddress(args, out var pointerAddress)");
        source.Should().Contain("SelectRangeFromAnchor(anchor, target);");
        source.Should().Contain("_cellDragSelectionPointer?.Capture(null);");
        source.Should().Contain("_session.SelectRange(new GridRange(anchor, address));");
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
        source.Should().Contain("_formulaBox.Foreground = Brushes.Transparent;");
        source.Should().Contain("new Run(text) { Foreground = brush }");
        source.Should().Contain("RefreshShell(\"Ready\");");
    }

    [Fact]
    public void WorksheetCells_WireAutofillHandleAndSelectionMoveDrag()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var sessionSource = File.ReadAllText(RepoFile("src", "FreeX.App.Services", "WorkbookSession.cs"));

        source.Should().Contain("AddAutofillHandleAdorner(border, zoomFactor);");
        source.Should().Contain("TryBeginAutofillDrag(args, border, address)");
        source.Should().Contain("GridAutofillPlanner.IsOnHandle(");
        source.Should().Contain("GridAutofillPlanner.CalculateCompletedSelectionRange(source, fillRange)");
        source.Should().Contain("_session.FillSelectedRange(direction)");
        source.Should().Contain("TryBeginSelectionMoveDrag(args, border, address)");
        source.Should().Contain("GridSelectionMovePlanner.IsOnMoveBorder(");
        source.Should().Contain("GridSelectionMovePlanner.CalculateTargetRange(");
        source.Should().Contain("_session.MoveSelectedRangeTo(source, target)");
        sessionSource.Should().Contain("public WorkbookCellEditResult MoveSelectedRangeTo(GridRange sourceRange, GridRange targetRange)");
        sessionSource.Should().Contain("new MoveRangeCommand(ActiveSheet.Id, sourceRange, targetRange.Start)");
    }

    [Fact]
    public void WorksheetHeaders_ResolvePointerDragAcrossVisibleHeaderMetrics()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("TryResolveColumnHeaderPointerIndex(args, out var col)");
        source.Should().Contain("TryResolveRowHeaderPointerIndex(args, out var row)");
        source.Should().Contain("var pos = args.GetPosition(_sheetGridHost);");
        source.Should().Contain("foreach (var metric in _session.Viewport.ColMetrics)");
        source.Should().Contain("foreach (var metric in _session.Viewport.RowMetrics)");
        source.Should().Contain("SelectEntireColumn(targetCol, extend: true);");
        source.Should().Contain("SelectEntireRow(targetRow, extend: true);");
    }

    [Fact]
    public void WorksheetContextClick_AcceptsRightClickAndControlClick()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("private static bool IsContextClick(PointerPoint point, PointerEventArgs args)");
        source.Should().Contain("point.Properties.IsRightButtonPressed");
        source.Should().Contain("point.Properties.IsLeftButtonPressed && args.KeyModifiers.HasFlag(KeyModifiers.Control)");
        source.Should().Contain("if (IsContextClick(point, args))");
        source.Should().Contain("OpenWorksheetCellContextMenu(border);");
        source.Should().Contain("OpenColumnHeaderContextMenu(header);");
        source.Should().Contain("OpenRowHeaderContextMenu(header);");
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
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("var extendSelection = e.KeyModifiers.HasFlag(KeyModifiers.Shift);");
        source.Should().Contain("MoveOrExtendActiveCell(-1, 0, extendSelection);");
        source.Should().Contain("MoveOrExtendActiveCell(1, 0, extendSelection);");
        source.Should().Contain("MoveOrExtendActiveCell(0, -1, extendSelection);");
        source.Should().Contain("MoveOrExtendActiveCell(0, 1, extendSelection);");
        source.Should().Contain("private CellAddress? _selectionExtensionAnchor;");
        source.Should().Contain("private CellAddress? _selectionExtensionCursor;");
        source.Should().Contain("var anchor = _selectionExtensionAnchor ?? _session.ActiveCell;");
        source.Should().Contain("var cursor = _selectionExtensionCursor ?? _session.ActiveCell;");
        source.Should().Contain("_selectionExtensionCursor = target;");
        source.Should().Contain("_session.SelectRange(new GridRange(anchor, target));");
    }

    [Fact]
    public void DoubleClickCell_OpensInlineEditorWithHitTestedCaret()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        source.Should().Contain("border.DoubleTapped += (_, args) =>");
        source.Should().Contain("CalculateInlineCellCaretIndex(");
        source.Should().Contain("BeginInlineCellEdit(address, editText, caretIndex);");
        source.Should().Contain("private TextBox CreateInlineCellEditor(");
        source.Should().Contain("AutomationProperties.SetAutomationId(editor, \"WorksheetInlineCellEditor\");");
        source.Should().Contain("editor.Focus();");
        source.Should().Contain("editor.CaretIndex = caret;");
        source.Should().Contain("new FormattedText(");
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
