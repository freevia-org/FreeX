using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FreeX.App.Presentation;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void CaptureFormulaEditCell()
    {
        if (_formulaEditCell is null && SheetGrid.SelectedRange?.Start is { } activeCell)
            _formulaEditCell = activeCell;
    }

    private void ClearFormulaRangeEntryState()
    {
        _formulaEditCell = null;
        _formulaRangeSelectionAnchor = null;
        _formulaRangeEntryMode = false;
        ClearFormulaReferenceEntrySpan();
        ClearFormulaReferenceHighlights();
    }

    private void ClearFormulaReferenceEntrySpan()
    {
        _formulaReferenceStart = null;
        _formulaReferenceLength = null;
    }

    private void UpdateFormulaRangeEntryStateAfterTextChanged(System.Windows.Controls.TextBox editor)
    {
        var textChangePlan = FormulaEditInteractionPlanner.BuildTextChangePlan(editor.Text);
        if (textChangePlan.StartsPointMode)
        {
            _formulaRangeEntryMode = true;
            ApplyFormulaEditStatusBarPlan(textChangePlan.StatusBarPlan);
        }

        ClearFormulaReferenceEntrySpanIfCaretLeftReference(editor);
    }

    private void ClearFormulaReferenceEntrySpanIfCaretLeftReference(System.Windows.Controls.TextBox editor)
    {
        if (_formulaReferenceStart is not { } start || _formulaReferenceLength is not { } length)
            return;

        var end = start + length;
        if (start < 0 || length < 0 || start > editor.Text.Length || end > editor.Text.Length)
        {
            ClearFormulaReferenceEntrySpan();
            return;
        }

        var selectionStart = Math.Clamp(editor.SelectionStart, 0, editor.Text.Length);
        if (editor.SelectionLength > 0)
        {
            var selectionEnd = Math.Clamp(selectionStart + editor.SelectionLength, selectionStart, editor.Text.Length);
            if (selectionStart < start || selectionEnd > end)
                ClearFormulaReferenceEntrySpan();
            return;
        }

        var caret = Math.Clamp(editor.CaretIndex, 0, editor.Text.Length);
        if (caret < start || caret > end)
            ClearFormulaReferenceEntrySpan();
    }

    private bool IsFormulaRangeEntryActive(System.Windows.Controls.TextBox? editor)
    {
        if (editor is null || _formulaEditCell is null)
            return false;

        return FormulaEditInteractionPlanner.IsRangeEntryActive(editor.Text, _formulaRangeEntryMode);
    }

    private bool IsFormulaReferenceHighlightActive(System.Windows.Controls.TextBox? editor)
    {
        if (editor is null || _formulaEditCell is null)
            return false;

        return FormulaEditInteractionPlanner.IsFormulaText(editor.Text);
    }

    private System.Windows.Controls.TextBox? GetFormulaRangeEntryEditor()
    {
        if (_inlineEditor?.IsVisible == true && IsFormulaRangeEntryActive(_inlineEditor))
            return _inlineEditor;

        return IsFormulaRangeEntryActive(FormulaBar) ? FormulaBar : null;
    }

    private System.Windows.Controls.TextBox? GetFormulaReferenceHighlightEditor()
    {
        if (_inlineEditor?.IsVisible == true && IsFormulaReferenceHighlightActive(_inlineEditor))
            return _inlineEditor;

        return IsFormulaReferenceHighlightActive(FormulaBar) ? FormulaBar : null;
    }

    private bool TryApplyFormulaRangeSelection(CellAddress target, bool extendSelection)
    {
        if (!extendSelection || _formulaRangeSelectionAnchor is null)
            _formulaRangeSelectionAnchor = target;

        var anchor = _formulaRangeSelectionAnchor.Value;
        var range = new GridRange(
            new CellAddress(_currentSheetId, Math.Min(anchor.Row, target.Row), Math.Min(anchor.Col, target.Col)),
            new CellAddress(_currentSheetId, Math.Max(anchor.Row, target.Row), Math.Max(anchor.Col, target.Col)));

        return TryApplyFormulaRangeSelection(range, anchor, target);
    }

    private bool TryApplyFormulaRangeSelection(
        GridRange range,
        CellAddress selectionAnchor,
        CellAddress selectionCursor)
    {
        var editor = GetFormulaRangeEntryEditor();
        if (editor is null)
            return false;

        var formulaCell = _formulaEditCell ?? SheetGrid.SelectedRange?.Start;
        if (formulaCell is null)
            return false;

        var getPivotDataPlan = range.Start == range.End
            ? GetPivotDataFormulaPlanner.Create(
                _workbook,
                _workbook.GetSheet(formulaCell.Value.Sheet)!,
                _workbook.GetSheet(_currentSheetId)!,
                range.Start)
            : null;

        var referenceInsertionIndex = editor.SelectionLength > 0
            ? editor.SelectionStart
            : editor.CaretIndex;
        var applied = getPivotDataPlan is not null
            ? FormulaRangeEntryPlanner.TryApplySelectionText(
                editor.Text,
                referenceInsertionIndex,
                editor.SelectionLength,
                _formulaReferenceStart,
                _formulaReferenceLength,
                getPivotDataPlan.FunctionCall,
                out var edit)
            : FormulaRangeEntryPlanner.TryApplyRangeSelection(
                editor.Text,
                referenceInsertionIndex,
                editor.SelectionLength,
                _formulaReferenceStart,
                _formulaReferenceLength,
                range,
                formulaCell.Value,
                _options.UseR1C1ReferenceStyle,
                out edit,
                _workbook.GetSheet(range.Start.Sheet)?.Name);

        if (!applied)
        {
            return false;
        }

        HideValidationDropdown();
        ClearCommentPreview();

        _selectionAnchor = selectionAnchor;
        _selectionCursor = selectionCursor;
        SheetGrid.SelectedRanges = null;
        SheetGrid.SelectedRange = range;
        CellAddressBox.Text = FormatRangeReference(range.Start, range.End);
        RefreshStatusBar();

        ApplyTextEdit(editor, edit.TextEdit);
        if (!ReferenceEquals(editor, FormulaBar))
            FormulaBar.Text = editor.Text;
        else if (_inlineEditor?.IsVisible == true)
            _inlineEditor.Text = editor.Text;

        _formulaReferenceStart = edit.ReferenceStart;
        _formulaReferenceLength = edit.ReferenceLength;
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
            ResolveStructuredFormulaReference);

    private GridRange? ResolveStructuredFormulaReference(string tableName, string selector)
    {
        var currentSheet = _workbook.GetSheet(_currentSheetId);
        var currentAddress = _formulaEditCell ?? SheetGrid.SelectedRange?.Start;
        var trimmedSelector = selector.Trim();

        if (trimmedSelector.StartsWith('@') && trimmedSelector.Length > 1)
        {
            var address = StructuredReferenceResolver.ResolveCurrentRowColumn(
                _workbook,
                currentSheet,
                currentAddress,
                string.IsNullOrWhiteSpace(tableName) ? null : tableName,
                trimmedSelector[1..].Trim());

            return address is null ? null : new GridRange(address.Value, address.Value);
        }

        return StructuredReferenceResolver.Resolve(
            _workbook,
            currentSheet,
            tableName,
            trimmedSelector,
            currentAddress);
    }

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
            poolIndex++;
        }

        _formulaReferenceGridOverlayActiveCount = poolIndex;
    }

    private void ClearFormulaReferenceGridOverlays()
    {
        HideFormulaReferenceGridOverlayPool();
    }

    private void HideFormulaReferenceGridOverlayPool()
    {
        for (var i = 0; i < _formulaReferenceGridOverlayActiveCount; i++)
            _formulaReferenceGridOverlayPool[i].Visibility = Visibility.Collapsed;

        _formulaReferenceGridOverlayActiveCount = 0;
    }

    private static Brush CreateFormulaReferenceFill(Brush brush)
    {
        if (brush is SolidColorBrush solid)
            return new SolidColorBrush(Color.FromArgb(36, solid.Color.R, solid.Color.G, solid.Color.B));

        return System.Windows.Media.Brushes.Transparent;
    }
}
