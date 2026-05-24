using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
        var editor = GetFormulaRangeEntryEditor();
        if (editor is null)
            return false;

        var formulaCell = _formulaEditCell ?? SheetGrid.SelectedRange?.Start;
        if (formulaCell is null)
            return false;

        if (!extendSelection || _formulaRangeSelectionAnchor is null)
            _formulaRangeSelectionAnchor = target;

        var anchor = _formulaRangeSelectionAnchor.Value;
        var range = new GridRange(
            new CellAddress(_currentSheetId, Math.Min(anchor.Row, target.Row), Math.Min(anchor.Col, target.Col)),
            new CellAddress(_currentSheetId, Math.Max(anchor.Row, target.Row), Math.Max(anchor.Col, target.Col)));

        if (!FormulaRangeEntryPlanner.TryApplyRangeSelection(
                editor.Text,
                editor.CaretIndex,
                editor.SelectionLength,
                _formulaReferenceStart,
                _formulaReferenceLength,
                range,
                formulaCell.Value,
                _options.UseR1C1ReferenceStyle,
                out var edit))
        {
            return false;
        }

        _selectionAnchor = anchor;
        _selectionCursor = target;
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
        editor.Focus();
        return true;
    }

    private IReadOnlyList<FormulaReferenceHighlight> GetFormulaReferenceHighlights(string text) =>
        FormulaReferenceHighlightPlanner.GetHighlights(
            text,
            _currentSheetId,
            sheetName => _workbook.GetSheet(sheetName)?.Id);

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
        ClearFormulaReferenceGridOverlays();
        if (SheetGrid.Viewport is null)
            return;

        foreach (var highlight in highlights)
        {
            if (highlight.Range is not { } range || range.Start.Sheet != _currentSheetId)
                continue;

            var rect = Freexcel.App.UI.GridView.CalculateVisibleSelectionRect(
                SheetGrid.Viewport,
                range,
                SheetGrid.ActualRowHeaderWidth,
                Freexcel.App.UI.GridView.ColHeaderHeight);
            if (rect is null)
                continue;

            var brush = _formulaReferenceBrushes[highlight.PaletteIndex % _formulaReferenceBrushes.Count];
            var border = new Border
            {
                Width = rect.Value.Width,
                Height = rect.Value.Height,
                BorderThickness = new Thickness(2),
                BorderBrush = brush,
                Background = CreateFormulaReferenceFill(brush),
                IsHitTestVisible = false
            };
            System.Windows.Controls.Canvas.SetLeft(border, rect.Value.Left);
            System.Windows.Controls.Canvas.SetTop(border, rect.Value.Top);
            EditOverlay.Children.Insert(0, border);
            _formulaReferenceGridOverlays.Add(border);
        }
    }

    private void ClearFormulaReferenceGridOverlays()
    {
        foreach (var element in _formulaReferenceGridOverlays)
            EditOverlay.Children.Remove(element);

        _formulaReferenceGridOverlays.Clear();
    }

    private static Brush CreateFormulaReferenceFill(Brush brush)
    {
        if (brush is SolidColorBrush solid)
            return new SolidColorBrush(Color.FromArgb(36, solid.Color.R, solid.Color.G, solid.Color.B));

        return System.Windows.Media.Brushes.Transparent;
    }
}
