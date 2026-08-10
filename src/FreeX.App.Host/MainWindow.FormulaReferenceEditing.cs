using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.Presentation;
using FreeX.App.Presentation.FormulaBar;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void RestoreFormulaEditCellSelection(CellAddress address)
    {
        if (_currentSheetId != address.Sheet)
        {
            _currentSheetId = address.Sheet;
            SelectSingleSheetTab(address.Sheet);
            UpdateViewport();
            RefreshSheetTabs();
        }

        SetSelectionRange(new GridRange(address, address), address);
    }

    private void CaptureFormulaEditCell()
    {
        if (_formulaEditCell is null && SheetGrid.SelectedRange?.Start is { } activeCell)
            _formulaEditCell = activeCell;
    }

    private void ClearFormulaRangeEntryState()
    {
        _formulaEditCell = null;
        HideFormulaFunctionAutocomplete();
        _formulaRangeEditingSession.Reset();
        ClearFormulaReferenceHighlights();
    }

    private void ClearFormulaReferenceEntrySpan() =>
        _formulaRangeEditingSession.ClearReferenceSpan();

    private void UpdateFormulaRangeEntryStateAfterTextChanged(System.Windows.Controls.TextBox editor)
    {
        var textChangePlan = _formulaRangeEditingSession.ApplyTextChanged(editor.Text);
        if (textChangePlan.StartsPointMode)
        {
            ApplyFormulaEditStatusBarPlan(textChangePlan.StatusBarPlan);
        }

        ClearFormulaReferenceEntrySpanIfCaretLeftReference(editor);
    }

    private void ClearFormulaReferenceEntrySpanIfCaretLeftReference(System.Windows.Controls.TextBox editor)
    {
        _formulaRangeEditingSession.ClearReferenceSpanIfCaretLeft(
            editor.Text.Length,
            editor.SelectionStart,
            editor.SelectionLength,
            editor.CaretIndex,
            preserveWhileSelectionActive: false);
    }

    private bool TryToggleFormulaRangeEntrySelectionMode(Key key, ModifierKeys modifiers)
    {
        var editor = GetFormulaRangeEntryEditor();
        if (!IsFormulaRangeEntryActive(editor) ||
            !_formulaRangeEditingSession.TryToggleSelectionMode(
                FormulaBarWpfInputAdapter.ToFormulaEditorKey(key),
                FormulaBarWpfInputAdapter.ToFormulaEditorModifiers(modifiers),
                out var plan))
        {
            return false;
        }

        if (plan.EditStatusBarPlan is { } statusBarPlan)
            ApplyFormulaEditStatusBarPlan(statusBarPlan);
        else if (plan.StatusBarModeResourceKey is { } resourceKey)
            SetStatusBarModeText(UiText.Get(resourceKey));
        return true;
    }

    private bool TryApplyFormulaRangeEntryKeyboardSelection(
        CellAddress current,
        CellAddress target,
        bool extendSelection)
    {
        var range = _formulaRangeEditingSession.PlanKeyboardSelectionRange(
            current,
            target,
            extendSelection);
        if (!_formulaRangeEditingSession.ShouldAppendKeyboardSelection)
            return TryApplyFormulaRangeSelection(target, extendSelection);

        var editor = GetFormulaRangeEntryEditor();
        var formulaCell = _formulaEditCell ?? SheetGrid.SelectedRange?.Start;
        if (editor is null || formulaCell is null)
            return false;

        var snapshot = new FormulaRangeEditorSnapshot(
            editor.Text,
            editor.CaretIndex,
            editor.SelectionLength,
            formulaCell.Value,
            _options.UseR1C1ReferenceStyle,
            _workbook.GetSheet(range.Start.Sheet)?.Name);
        if (!_formulaRangeEditingSession.TryPlanKeyboardDisjointRangeSelectionEdit(
                snapshot,
                current,
                target,
                extendSelection,
                out var plan))
        {
            return TryApplyFormulaRangeSelection(range, range.Start, target);
        }

        ApplyFormulaEditorTextEdit(editor, plan.Edit.TextEdit);

        _formulaRangeEditingSession.ApplySelectionEdit(plan);
        _selectionAnchor = range.Start;
        _selectionCursor = range.End;
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = range;
        CellAddressBox.Text = range.Start == range.End
            ? FormatCellReference(range.Start)
            : FormatRangeReference(range.Start, range.End);
        RefreshStatusBar();
        RefreshFormulaReferenceHighlights();
        SetFormulaEditStatusBarMode(pointMode: true);
        editor.Focus();
        return true;
    }

    private bool IsFormulaRangeEntryActive(System.Windows.Controls.TextBox? editor)
    {
        if (editor is null || _formulaEditCell is null)
            return false;

        return _formulaRangeEditingSession.IsRangeEntryActive(editor.Text);
    }

    private bool IsFormulaReferenceHighlightActive(System.Windows.Controls.TextBox? editor)
    {
        if (editor is null || _formulaEditCell is null)
            return false;

        return _formulaRangeEditingSession.IsFormulaText(editor.Text);
    }

    private System.Windows.Controls.TextBox? GetFormulaRangeEntryEditor()
    {
        if (_inlineEditor?.IsVisible == true && IsFormulaRangeEntryActive(_inlineEditor))
            return _inlineEditor;

        return IsFormulaRangeEntryActive(FormulaBar) ? FormulaBar : null;
    }

    private System.Windows.Controls.TextBox? GetFormulaReferenceHighlightEditor()
    {
        if (FormulaBar.IsFocused && IsFormulaReferenceHighlightActive(FormulaBar))
            return FormulaBar;

        if (_inlineEditor?.IsVisible == true && IsFormulaReferenceHighlightActive(_inlineEditor))
            return _inlineEditor;

        return IsFormulaReferenceHighlightActive(FormulaBar) ? FormulaBar : null;
    }

    private bool TryApplyFormulaRangeSelection(CellAddress target, bool extendSelection)
    {
        var selection = _formulaRangeEditingSession.PlanSelection(target, extendSelection);
        return TryApplyFormulaRangeSelection(
            selection.Range,
            selection.Anchor,
            selection.Cursor);
    }

    private bool TryApplyFormulaRangeSelection(
        GridRange range,
        CellAddress selectionAnchor,
        CellAddress selectionCursor,
        string? selectedSheetNameOverride = null,
        string? selectedWorkbookName = null)
    {
        var editor = GetFormulaRangeEntryEditor();
        if (editor is null)
            return false;

        var formulaCell = _formulaEditCell ?? SheetGrid.SelectedRange?.Start;
        if (formulaCell is null)
            return false;

        var getPivotDataFunctionCall = GetPivotDataFormulaPlanner.CreatePointModeFunctionCall(
            _workbook,
            formulaCell.Value,
            _currentSheetId,
            range,
            _options.GenerateGetPivotData,
            selectedWorkbookName);

        var referenceInsertionIndex = editor.SelectionLength > 0
            ? editor.SelectionStart
            : editor.CaretIndex;
        var snapshot = new FormulaRangeEditorSnapshot(
            editor.Text,
            referenceInsertionIndex,
            editor.SelectionLength,
            formulaCell.Value,
            _options.UseR1C1ReferenceStyle,
            selectedSheetNameOverride ?? _workbook.GetSheet(range.Start.Sheet)?.Name,
            selectedWorkbookName);
        if (!_formulaRangeEditingSession.TryPlanRangeSelectionEdit(
                snapshot,
                range,
                selectionAnchor,
                selectionCursor,
                getPivotDataFunctionCall,
                out var plan))
        {
            return false;
        }

        HideValidationDropdown();
        ClearCommentPreview();

        if (plan.UpdateLocalSelection)
        {
            _selectionAnchor = selectionAnchor;
            _selectionCursor = selectionCursor;
            SheetGrid.SelectedRanges = null;
            SheetGrid.SelectedRange = range;
            CellAddressBox.Text = FormatRangeReference(range.Start, range.End);
        }
        RefreshStatusBar();

        ApplyFormulaEditorTextEdit(editor, plan.Edit.TextEdit);

        _formulaRangeEditingSession.ApplySelectionEdit(plan);
        RefreshFormulaReferenceHighlights();
        SetFormulaEditStatusBarMode(pointMode: true);
        editor.Focus();
        editor.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                editor.Focus();
                System.Windows.Input.Keyboard.Focus(editor);
            }),
            System.Windows.Threading.DispatcherPriority.Input);
        return true;
    }

    private IReadOnlyList<FormulaReferenceHighlight> GetFormulaReferenceHighlights(string text) =>
        FormulaReferenceHighlightPlanner.GetHighlights(
            text,
            _currentSheetId,
            sheetName => _workbook.GetSheet(sheetName)?.Id,
            ResolveStructuredFormulaReference,
            sheetId =>
            {
                for (var index = 0; index < _workbook.Sheets.Count; index++)
                {
                    if (_workbook.Sheets[index].Id == sheetId)
                        return index;
                }

                return null;
            });

    internal bool RaiseFormulaReferenceGripDragForTest(int highlightIndex, CellAddress target)
    {
        var editor = GetFormulaReferenceHighlightEditor();
        var highlights = editor is null
            ? []
            : GetFormulaReferenceHighlights(editor.Text);
        if (editor is null || highlightIndex < 0 || highlightIndex >= highlights.Count ||
            highlights[highlightIndex].Range is not { } originalRange ||
            originalRange.Start.Sheet != target.Sheet)
        {
            return false;
        }

        var newRange = _formulaRangeEditingSession.PlanReferenceDrag(highlights[highlightIndex], target);
        if (newRange is null)
            return false;

        ApplyFormulaReferenceResize(editor, highlights[highlightIndex], newRange.Value);
        RefreshFormulaReferenceHighlights();
        return true;
    }

    private GridRange? ResolveStructuredFormulaReference(string tableName, string selector) =>
        StructuredReferenceResolver.ResolveEditorReference(
            _workbook,
            _workbook.GetSheet(_currentSheetId),
            _formulaEditCell ?? SheetGrid.SelectedRange?.Start,
            tableName,
            selector);

    private void RefreshFormulaReferenceHighlights()
    {
        var editor = GetFormulaReferenceHighlightEditor();
        if (editor is null)
        {
            ClearFormulaReferenceHighlights();
            return;
        }

        var highlights = GetFormulaReferenceHighlights(editor.Text);
        var normalBrush = System.Windows.Media.Brushes.Black;
        if (ReferenceEquals(editor, FormulaBar))
        {
            FormulaBar.Foreground = highlights.Count > 0
                ? System.Windows.Media.Brushes.Transparent
                : normalBrush;
            FormulaReferenceTextOverlay.Apply(
                FormulaBarReferenceOverlay,
                editor.Text,
                highlights,
                _formulaReferenceBrushes,
                normalBrush);
            FormulaReferenceTextOverlay.Clear(_inlineFormulaReferenceOverlay);
        }
        else
        {
            _inlineEditor!.Foreground = editor.Text.StartsWith("=", StringComparison.Ordinal)
                ? System.Windows.Media.Brushes.Transparent
                : normalBrush;
            FormulaReferenceTextOverlay.Apply(
                _inlineFormulaReferenceOverlay!,
                editor.Text,
                highlights,
                _formulaReferenceBrushes,
                normalBrush,
                keepFormulaVisibleWithoutHighlights: true);
            FormulaBar.Foreground = highlights.Count > 0
                ? System.Windows.Media.Brushes.Transparent
                : normalBrush;
            FormulaReferenceTextOverlay.Apply(
                FormulaBarReferenceOverlay,
                editor.Text,
                highlights,
                _formulaReferenceBrushes,
                normalBrush);
        }

        RefreshFormulaReferenceGridOverlays(highlights);
    }

    private void ClearFormulaReferenceHighlights()
    {
        ClearFormulaReferenceGridOverlays();
        FormulaReferenceTextOverlay.Clear(FormulaBarReferenceOverlay);
        FormulaReferenceTextOverlay.Clear(_inlineFormulaReferenceOverlay);
        FormulaBar.Foreground = System.Windows.Media.Brushes.Black;
        if (_inlineEditor is not null)
            _inlineEditor.Foreground = System.Windows.Media.Brushes.Black;
    }

    private void RefreshFormulaReferenceGridOverlays(IReadOnlyList<FormulaReferenceHighlight> highlights)
    {
        // Hide all currently active pool entries before re-showing the ones that are still needed.
        HideFormulaReferenceGridOverlayPool();
        if (SheetGrid.Viewport is null)
            return;

        // First pass: count how many visible overlays we need so we can grow the pool all at once.
        // New pool entries are prepended (Insert(0)) to stay behind all other EditOverlay children,
        // matching the original Insert(0, border) z-ordering. Growing in batch avoids index-shift issues.
        var neededCount = 0;
        foreach (var highlight in highlights)
        {
            if (highlight.Range is not { } range || range.Start.Sheet != _currentSheetId)
                continue;
            if (FreeX.App.UI.GridView.CalculateVisibleSelectionRect(
                    SheetGrid.Viewport, range,
                    SheetGrid.ActualRowHeaderWidth, FreeX.App.UI.GridView.ColHeaderHeight) is not null)
                neededCount++;
        }

        while (_formulaReferenceGridOverlayPool.Count < neededCount)
        {
            var newBorder = new Border
            {
                BorderThickness = new Thickness(2),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            EditOverlay.Children.Insert(0, newBorder);
            _formulaReferenceGridOverlayPool.Insert(0, newBorder);
            _formulaReferenceGridOverlayHighlights.Insert(0, null);

            // R91-formula-editing-assist-5-3: a small hit-testable resize grip at the highlight's
            // bottom-right corner, so the reference it represents can be dragged to resize --
            // unlike the border itself (IsHitTestVisible = false, so mouse clicks fall through to
            // the grid underneath for "pick an entirely new range" point-mode selection). Inserted
            // right after its border (index 0) so it stays on top of that border but still behind
            // every non-pooled overlay child added earlier.
            var newGrip = new System.Windows.Shapes.Rectangle
            {
                Width = FormulaReferenceGripSize,
                Height = FormulaReferenceGripSize,
                Cursor = System.Windows.Input.Cursors.SizeNWSE,
                Visibility = Visibility.Collapsed
            };
            newGrip.MouseLeftButtonDown += FormulaReferenceGrip_MouseLeftButtonDown;
            newGrip.MouseMove += FormulaReferenceGrip_MouseMove;
            newGrip.MouseLeftButtonUp += FormulaReferenceGrip_MouseLeftButtonUp;
            EditOverlay.Children.Insert(1, newGrip);
            _formulaReferenceGridOverlayGripPool.Insert(0, newGrip);
        }

        // Second pass: assign geometry and show the required pool slots.
        var poolIndex = 0;
        foreach (var highlight in highlights)
        {
            if (highlight.Range is not { } range || range.Start.Sheet != _currentSheetId)
                continue;

            var rect = FreeX.App.UI.GridView.CalculateVisibleSelectionRect(
                SheetGrid.Viewport,
                range,
                SheetGrid.ActualRowHeaderWidth,
                FreeX.App.UI.GridView.ColHeaderHeight);
            if (rect is null)
                continue;

            var brush = _formulaReferenceBrushes[highlight.PaletteIndex % _formulaReferenceBrushes.Count];
            var border = _formulaReferenceGridOverlayPool[poolIndex];
            border.Width = rect.Value.Width;
            border.Height = rect.Value.Height;
            border.BorderBrush = brush;
            border.Background = CreateFormulaReferenceFill(brush);
            System.Windows.Controls.Canvas.SetLeft(border, rect.Value.Left);
            System.Windows.Controls.Canvas.SetTop(border, rect.Value.Top);
            border.Visibility = Visibility.Visible;

            var grip = _formulaReferenceGridOverlayGripPool[poolIndex];
            grip.Fill = brush;
            System.Windows.Controls.Canvas.SetLeft(grip, rect.Value.Right - FormulaReferenceGripSize / 2);
            System.Windows.Controls.Canvas.SetTop(grip, rect.Value.Bottom - FormulaReferenceGripSize / 2);
            grip.Visibility = Visibility.Visible;
            _formulaReferenceGridOverlayHighlights[poolIndex] = highlight;

            poolIndex++;
        }

        _formulaReferenceGridOverlayActiveCount = poolIndex;
    }

    private const double FormulaReferenceGripSize = 6.0;

    private void ClearFormulaReferenceGridOverlays()
    {
        HideFormulaReferenceGridOverlayPool();
    }

    private void HideFormulaReferenceGridOverlayPool()
    {
        for (var i = 0; i < _formulaReferenceGridOverlayActiveCount; i++)
        {
            _formulaReferenceGridOverlayPool[i].Visibility = Visibility.Collapsed;
            _formulaReferenceGridOverlayGripPool[i].Visibility = Visibility.Collapsed;
            _formulaReferenceGridOverlayHighlights[i] = null;
        }

        _formulaReferenceGridOverlayActiveCount = 0;
    }

    // ── R91-formula-editing-assist-5-3: drag a reference highlight's corner grip to resize it ──

    private void FormulaReferenceGrip_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Shapes.Rectangle grip)
            return;

        var poolIndex = _formulaReferenceGridOverlayGripPool.IndexOf(grip);
        if (poolIndex < 0 || poolIndex >= _formulaReferenceGridOverlayHighlights.Count ||
            _formulaReferenceGridOverlayHighlights[poolIndex] is not { Range: { } } highlight)
            return;

        var editor = GetFormulaReferenceHighlightEditor();
        if (editor is null)
            return;

        if (!_formulaRangeEditingSession.TryBeginReferenceDrag(highlight))
            return;

        _formulaReferenceDragEditor = editor;
        grip.CaptureMouse();
        e.Handled = true;
    }

    private void FormulaReferenceGrip_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_formulaRangeEditingSession.IsReferenceDragActive ||
            sender is not System.Windows.Shapes.Rectangle grip ||
            SheetGrid.Viewport is null)
            return;

        if (_formulaRangeEditingSession.ReferenceDragHighlight?.Range is { } originalRange &&
            TryResolveDragTargetCell(e.GetPosition(EditOverlay), originalRange.Start.Sheet, out var targetCell) &&
            _formulaRangeEditingSession.PlanActiveReferenceDrag(targetCell) is { } previewRange)
        {
            var rect = FreeX.App.UI.GridView.CalculateVisibleSelectionRect(
                SheetGrid.Viewport, previewRange, SheetGrid.ActualRowHeaderWidth, FreeX.App.UI.GridView.ColHeaderHeight);
            if (rect is { } previewRect)
            {
                var poolIndex = _formulaReferenceGridOverlayGripPool.IndexOf(grip);
                if (poolIndex >= 0 && poolIndex < _formulaReferenceGridOverlayPool.Count)
                {
                    var border = _formulaReferenceGridOverlayPool[poolIndex];
                    border.Width = previewRect.Width;
                    border.Height = previewRect.Height;
                    System.Windows.Controls.Canvas.SetLeft(border, previewRect.Left);
                    System.Windows.Controls.Canvas.SetTop(border, previewRect.Top);
                }
                System.Windows.Controls.Canvas.SetLeft(grip, previewRect.Right - FormulaReferenceGripSize / 2);
                System.Windows.Controls.Canvas.SetTop(grip, previewRect.Bottom - FormulaReferenceGripSize / 2);
            }
        }

        e.Handled = true;
    }

    private void FormulaReferenceGrip_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_formulaRangeEditingSession.IsReferenceDragActive ||
            sender is not System.Windows.Shapes.Rectangle grip)
            return;

        grip.ReleaseMouseCapture();

        var highlight = _formulaRangeEditingSession.EndReferenceDrag();
        var editor = _formulaReferenceDragEditor;
        _formulaReferenceDragEditor = null;

        if (highlight?.Range is not { } originalRange || editor is null)
            return;

        if (TryResolveDragTargetCell(e.GetPosition(EditOverlay), originalRange.Start.Sheet, out var targetCell))
        {
            if (_formulaRangeEditingSession.PlanReferenceDrag(highlight, targetCell) is { } newRange)
                ApplyFormulaReferenceResize(editor, highlight, newRange);
        }

        // Whether or not the drag actually changed anything, rebuild the overlays from the (possibly
        // now-changed) text -- a no-op drag left the pool's live-preview geometry pointing at the
        // preview rect rather than the committed one.
        RefreshFormulaReferenceHighlights();
        e.Handled = true;
    }

    private void ApplyFormulaReferenceResize(
        System.Windows.Controls.TextBox editor,
        FormulaReferenceHighlight highlight,
        GridRange newRange)
    {
        var edit = _formulaRangeEditingSession.PlanReferenceResizeEdit(
            editor.Text,
            highlight,
            newRange,
            _options.UseR1C1ReferenceStyle);

        ApplyTextEdit(editor, edit);
        _formulaRangeEditingSession.ApplyReferenceResizeEdit(highlight, edit);
        if (ReferenceEquals(editor, _inlineEditor))
            FormulaBar.Text = edit.Text;
        else if (_inlineEditor?.IsVisible == true)
            _inlineEditor.Text = edit.Text;
    }

    /// <summary>
    /// Converts an EditOverlay-relative drag point into the worksheet cell under it. EditOverlay is
    /// unscaled (see ShowInlineEditor's own cx/cy zoom multiplication), so the point is divided by
    /// the current zoom level before hit-testing against the (unzoomed) viewport metrics.
    /// </summary>
    private bool TryResolveDragTargetCell(System.Windows.Point overlayPoint, SheetId sheet, out CellAddress targetCell)
    {
        targetCell = default;
        if (SheetGrid.Viewport is null || _zoomLevel <= 0)
            return false;

        var unzoomedPoint = new System.Windows.Point(overlayPoint.X / _zoomLevel, overlayPoint.Y / _zoomLevel);
        if (FreeX.App.UI.GridView.HitTestViewportCell(SheetGrid.Viewport, sheet, unzoomedPoint) is not { } hit)
            return false;

        targetCell = hit;
        return true;
    }

    private static Brush CreateFormulaReferenceFill(Brush brush)
    {
        if (brush is SolidColorBrush solid)
            return new SolidColorBrush(Color.FromArgb(36, solid.Color.R, solid.Color.G, solid.Color.B));

        return System.Windows.Media.Brushes.Transparent;
    }
}
