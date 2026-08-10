using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeX.App.Presentation.DefinedNames;
using FreeX.App.Presentation.FormulaBar;
using FreeX.App.Presentation.GridInteraction;
using FreeX.App.Presentation.SheetUI;
using FreeX.App.Presentation.Shell;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    // R83-app-flashfill-autocomplete-5-2: reentrancy guard for ApplyCellValueAutoCompleteSuggestion
    // (setting _inlineEditor.Text to add the suggested tail fires TextChanged again).
    private bool _applyingCellValueAutoCompleteSuggestion;

    // Set for one TextChanged pass when Backspace/Delete just removed a live suggestion, so that
    // pass doesn't instantly re-offer the very completion the user just rejected -- mirrors Excel,
    // where Delete/Backspace reject the suggestion instead of re-triggering it.

    // R91-formula-editing-assist-5-1/5-2: the function-name AutoComplete popup and the live
    // argument-signature tooltip, lazily created the first time either is needed (mirroring
    // _inlineEditor's own lazy construction). Both are driven by the portable planners in
    // FreeX.App.Presentation.FormulaBar (FormulaFunctionAutocompletePlanner /
    // FormulaSignatureHelpPlanner); this file only adapts WPF text/caret state into and out of them.
    private System.Windows.Controls.Primitives.Popup? _functionAutocompletePopup;
    private System.Windows.Controls.ListBox? _functionAutocompleteListBox;
    private System.Windows.Controls.Primitives.Popup? _signatureHelpPopup;
    private System.Windows.Controls.TextBlock? _signatureHelpTextBlock;

    private bool FunctionAutocompleteIsOpen => _functionAutocompletePopup?.IsOpen == true;

    private void EnterEditMode(double? clickX = null)
    {
        if (_selectionAnchor.HasValue)
            ShowInlineEditor(_selectionAnchor.Value, clickX);
        else
        {
            FocusFormulaBarAtEnd();
        }
    }

    private void EditActiveCellInFormulaBar()
    {
        CaptureFormulaEditCell();
        if (_inlineEditor?.IsVisible == true)
        {
            SyncFormulaBarTextFromInlineEditor();
            FocusFormulaBarAtEnd();
            return;
        }

        if (SheetGrid.SelectedRange?.Start is { } address)
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            var cell = sheet?.GetCell(address);
            FormulaBar.Text = FormatFormulaBarText(cell, address);
            // R88-render-rtl-bidi-5-3: this is the "click straight into the Formula Bar" edit-start
            // path (the inline editor is never shown here), so the Formula Bar itself must get the
            // RTL/LTR base paragraph direction -- ShowInlineEditor only sets it on the in-cell editor.
            FormulaBar.FlowDirection = ResolveInlineEditorFlowDirection(sheet, cell);
        }

        FocusFormulaBarAtEnd();
    }

    private void FocusFormulaBarAtEnd()
    {
        FocusFormulaBar();
        FormulaBar.CaretIndex = FormulaBar.Text.Length;
        SetFormulaEditStatusBarMode(pointMode: false);
    }

    private void FocusFormulaBar()
    {
        FocusManager.SetFocusedElement(this, FormulaBar);
        FormulaBar.Focus();
        Keyboard.Focus(FormulaBar);
    }

    private void ShowInlineEditor(CellAddress addr, double? clickX = null)
    {
        if (!HideTextBoxInlineEditor(commit: true))
            return;

        HideValidationDropdown();
        var vp = SheetGrid.Viewport;
        if (vp == null) { FormulaBar.Focus(); return; }

        var rowMetric = FindRowMetric(vp.RowMetrics, addr.Row);
        var colMetric = FindColMetric(vp.ColMetrics, addr.Col);
        if (rowMetric == null || colMetric == null) { FormulaBar.Focus(); return; }

        var sheet = _workbook.GetSheet(_currentSheetId);
        var cell = sheet?.GetCell(addr);
        var text = FormatFormulaBarText(cell, addr);
        _formulaEditCell = addr;
        _formulaRangeEditingSession.SetPointMode(false);
        _formulaEditEnteredViaEditKey = true;
        ClearFormulaReferenceEntrySpan();

        if (_inlineEditor == null)
        {
            _inlineEditorChrome = new System.Windows.Controls.Border
            {
                Background = System.Windows.Media.Brushes.White,
                BorderThickness = new System.Windows.Thickness(2),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(15, 109, 140)),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed
            };
            _inlineEditor = new System.Windows.Controls.TextBox
            {
                BorderThickness = new System.Windows.Thickness(0),
                Padding         = new System.Windows.Thickness(4, 0, 4, 0),
                FontFamily      = new System.Windows.Media.FontFamily("Calibri"),
                FontSize        = 15.0,
                Background      = System.Windows.Media.Brushes.White,
                AcceptsReturn   = false,
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
            };
            TextOptions.SetTextFormattingMode(_inlineEditor, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(_inlineEditor, TextRenderingMode.ClearType);
            TextOptions.SetTextHintingMode(_inlineEditor, TextHintingMode.Fixed);
            _inlineEditor.PreviewKeyDown += InlineEditor_KeyDown;
            _inlineEditor.LostFocus  += InlineEditor_LostFocus;
            _inlineEditor.SelectionChanged += (_, _) =>
            {
                if (!_isApplyingFormulaEditorText && _inlineEditor is { } inlineEditor)
                    ClearFormulaReferenceEntrySpanIfCaretLeftReference(inlineEditor);
            };
            _inlineEditor.TextChanged += (_, _) =>
            {
                if (_isApplyingFormulaEditorText)
                    return;

                SyncFormulaBarTextFromInlineEditor();
                UpdateFormulaRangeEntryStateAfterTextChanged(_inlineEditor);
                RefreshInlineEditorTextSurface();
                RefreshInlineEditorChromeBorder();
                RefreshFormulaReferenceHighlights();

                if (!_formulaRangeEditingSession.ConsumeCellValueAutocompleteSuppression())
                    ApplyCellValueAutoCompleteSuggestion();

                RefreshFormulaFunctionAutocomplete(_inlineEditor);
                RefreshFormulaSignatureHelp(_inlineEditor);
            };
            _inlineFormulaReferenceOverlay = new System.Windows.Controls.TextBlock
            {
                FontFamily = new System.Windows.Media.FontFamily("Calibri"),
                FontSize = 15.0,
                IsHitTestVisible = false,
                Margin = new Thickness(0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Visibility = Visibility.Collapsed
            };
            TextOptions.SetTextFormattingMode(_inlineFormulaReferenceOverlay, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(_inlineFormulaReferenceOverlay, TextRenderingMode.ClearType);
            TextOptions.SetTextHintingMode(_inlineFormulaReferenceOverlay, TextHintingMode.Fixed);
            EditOverlay.Children.Add(_inlineEditorChrome);
            EditOverlay.Children.Add(_inlineEditor);
            EditOverlay.Children.Add(_inlineFormulaReferenceOverlay);
        }

        // Cell metrics are in unzoomed coordinates; the EditOverlay is not transformed, so scale.
        double zoom = _zoomLevel;
        double cx = (colMetric.LeftOffset + SheetGrid.ActualRowHeaderWidth) * zoom;
        double cy = (rowMetric.TopOffset  + FreeX.App.UI.GridView.ColHeaderHeight) * zoom;
        double cellW = colMetric.Width  * zoom;
        double cellH = rowMetric.Height * zoom;

        // R75-render-merged-cells-4-2: when addr is a merge anchor, widen the editor box to span
        // the full merged rectangle instead of just the anchor's own single-cell box (mirrors
        // GridView.Rendering.cs's RenderCells text pass, which sums the same extra column/row
        // metrics for merged content).
        if (sheet is { MergedRegions.Count: > 0 } && sheet.GetMergeRegion(addr) is { } merge && merge.Start == addr)
        {
            for (uint c2 = merge.Start.Col + 1; c2 <= merge.End.Col; c2++)
                if (FindColMetric(vp.ColMetrics, c2) is { } extraCol)
                    cellW += extraCol.Width * zoom;
            for (uint r2 = merge.Start.Row + 1; r2 <= merge.End.Row; r2++)
                if (FindRowMetric(vp.RowMetrics, r2) is { } extraRow)
                    cellH += extraRow.Height * zoom;
        }

        _inlineEditorSingleLineHeight = cellH;
        var layout = FormulaInlineEditorLayoutPlanner.Create(cx, cy, cellW, cellH, lineCount: CountInlineEditorLines(text));

        _inlineEditor.Text = text;
        // R78-render-inplace-editor-5-4: match the cell's own effective horizontal alignment
        // (mirrors the Avalonia shell's CreateInlineCellEditor, which threads the same
        // style/value-derived alignment into its TextBox) instead of always defaulting to left.
        _inlineEditor.TextAlignment = ResolveInlineEditorTextAlignment(sheet, cell);
        // R88-render-rtl-bidi-5-3: also switch the base paragraph embedding direction (not just the
        // text-block anchor) so an RTL-reading-order cell edits with true right-to-left bidi
        // reordering/caret behavior; the Formula Bar edits the same cell so it must match.
        var inlineEditorFlowDirection = ResolveInlineEditorFlowDirection(sheet, cell);
        _inlineEditor.FlowDirection = inlineEditorFlowDirection;
        FormulaBar.FlowDirection = inlineEditorFlowDirection;
        AutomationProperties.SetAutomationId(_inlineEditor, "WorksheetInlineCellEditor");
        AutomationProperties.SetName(_inlineEditor, UiText.Format("MainWindow_AutomationName_InlineCellEditorFormat", FormatCellReference(addr)));
        _inlineEditorChromeBaseRect = layout.EditorRect;
        ApplyInlineEditorChromeFrame(FormulaInlineEditorOverflow.None);

        System.Windows.Controls.Canvas.SetLeft(_inlineEditor, layout.TextOverlayRect.Left - 4);
        System.Windows.Controls.Canvas.SetTop(_inlineEditor, layout.EditorRect.Top);
        _inlineEditor.Width  = layout.TextOverlayRect.Width + 8;
        _inlineEditor.Height = layout.EditorRect.Height;
        if (_inlineFormulaReferenceOverlay is not null)
        {
            System.Windows.Controls.Canvas.SetLeft(_inlineFormulaReferenceOverlay, layout.TextOverlayRect.Left);
            System.Windows.Controls.Canvas.SetTop(_inlineFormulaReferenceOverlay, layout.TextOverlayRect.Top);
            _inlineFormulaReferenceOverlay.Width = layout.TextOverlayRect.Width;
            _inlineFormulaReferenceOverlay.Height = layout.TextOverlayRect.Height;
        }
        RefreshInlineEditorTextSurface();
        RefreshInlineEditorChromeBorder();

        if (_inlineEditorChrome is not null)
            _inlineEditorChrome.Visibility = Visibility.Visible;
        _inlineEditor.Visibility  = Visibility.Visible;
        SheetGrid.EditingCell = addr;
        EditOverlay.IsHitTestVisible = true;
        RefreshFormulaReferenceHighlights();
        _inlineEditor.Focus();
        _inlineEditor.CaretIndex = ResolveInlineEditorCaretIndex(clickX, layout.TextOverlayRect.Left - 4);
        _inlineEditor.SelectionLength = 0;
        SetFormulaEditStatusBarMode(pointMode: false);

        static RowMetric? FindRowMetric(IReadOnlyList<RowMetric> metrics, uint row)
        {
            foreach (var metric in metrics)
            {
                if (metric.Row == row)
                    return metric;
            }

            return null;
        }

        static ColMetric? FindColMetric(IReadOnlyList<ColMetric> metrics, uint col)
        {
            foreach (var metric in metrics)
            {
                if (metric.Col == col)
                    return metric;
            }

            return null;
        }
    }

    /// <summary>
    /// R83-app-flashfill-autocomplete-5-2: Excel's "AutoComplete for cell values". Runs on every
    /// inline-editor keystroke; only actually offers a completion when all of these hold, so it
    /// never fires while the user is mid-formula, mid-navigation, or already accepted/rejected a
    /// suggestion:
    /// <list type="bullet">
    /// <item>the option is enabled (<see cref="AppOptions.EnableAutoCompleteForCellValues"/>);</item>
    /// <item>the cell is a plain text entry -- not a formula (leading '=') and not mid formula
    /// range-reference entry;</item>
    /// <item>the caret sits at the very end of the text with nothing selected -- i.e. the user is
    /// typing forward, not editing mid-string or already sitting on a live suggestion.</item>
    /// </list>
    /// On a match it appends the remainder of the matched column entry and selects it (WPF
    /// ComboBox-style): Tab/Enter commits the completed text, continuing to type overwrites the
    /// selected remainder with the new keystroke (which re-runs this same check for the new
    /// prefix), and Backspace/Delete rejects it via the session's one-shot suppression state.
    /// </summary>
    private void ApplyCellValueAutoCompleteSuggestion() => ApplyCellValueAutoCompleteSuggestion(_inlineEditor);

    /// <summary>
    /// R88-app-autocomplete-picklist-5-3: same AutoComplete logic as the inline in-cell editor's own
    /// suggestion pass, generalized to whichever <see cref="TextBox"/> is the live editing surface --
    /// the inline editor when it is visible, or the Formula Bar itself when the user began the edit
    /// by clicking straight into the Formula Bar (in which case <see cref="ShowInlineEditor"/> is
    /// never invoked and the inline editor's own TextChanged handler never runs).
    /// </summary>
    private void ApplyCellValueAutoCompleteSuggestion(System.Windows.Controls.TextBox? editor)
    {
        if (_applyingCellValueAutoCompleteSuggestion || editor is null)
            return;
        if (_formulaEditCell is not { } addr)
            return;

        var text = editor.Text;
        var sheet = _workbook.GetSheet(_currentSheetId);
        var plan = _formulaRangeEditingSession.PlanCellValueAutocomplete(
            _options.EnableAutoCompleteForCellValues,
            text,
            editor.CaretIndex,
            editor.SelectionLength,
            sheet,
            addr);
        if (plan is null)
            return;

        _applyingCellValueAutoCompleteSuggestion = true;
        try
        {
            editor.Text = plan.Value.Text;
            editor.Select(plan.Value.SelectionStart, plan.Value.SelectionLength);
        }
        finally
        {
            _applyingCellValueAutoCompleteSuggestion = false;
        }
    }

    // ── R91-formula-editing-assist-5-1: function-name AutoComplete popup ───────────────────────

    /// <summary>
    /// Recomputes and shows/hides the function-name AutoComplete popup for the given formula editor
    /// (the inline in-cell editor or the Formula Bar), driven entirely by
    /// <see cref="FormulaFunctionAutocompletePlanner"/>. Called on every formula-editor TextChanged
    /// pass alongside the existing reference-highlight refresh.
    /// </summary>
    private void RefreshFormulaFunctionAutocomplete(System.Windows.Controls.TextBox editor)
    {
        var candidates = _formulaRangeEditingSession.RefreshFunctionAutocomplete(
            editor.Text,
            editor.CaretIndex,
            BuiltInFunctions.Names,
            _workbook.NamedRanges.Keys,
            _workbook.Sheets.SelectMany(s => s.StructuredTables).Select(t => t.Name));

        if (candidates.Count == 0)
        {
            HideFormulaFunctionAutocomplete();
            return;
        }

        ShowFormulaFunctionAutocomplete(editor, candidates);
    }

    private void ShowFormulaFunctionAutocomplete(System.Windows.Controls.TextBox editor, IReadOnlyList<string> candidates)
    {
        EnsureFunctionAutocompletePopup();
        _functionAutocompleteListBox!.ItemsSource = candidates;
        _functionAutocompleteListBox.SelectedIndex = 0;

        var caretRect = editor.GetRectFromCharacterIndex(editor.CaretIndex);
        _functionAutocompletePopup!.PlacementTarget = editor;
        _functionAutocompletePopup.Placement = System.Windows.Controls.Primitives.PlacementMode.RelativePoint;
        _functionAutocompletePopup.HorizontalOffset = caretRect.Left;
        _functionAutocompletePopup.VerticalOffset = caretRect.Bottom;
        _functionAutocompletePopup.IsOpen = true;
    }

    private void HideFormulaFunctionAutocomplete()
    {
        if (_functionAutocompletePopup is not null)
            _functionAutocompletePopup.IsOpen = false;
        _formulaRangeEditingSession.ClearFunctionAutocomplete();
    }

    private void EnsureFunctionAutocompletePopup()
    {
        if (_functionAutocompletePopup is not null)
            return;

        _functionAutocompleteListBox = new System.Windows.Controls.ListBox
        {
            MaxHeight = 220,
            Focusable = false,
        };
        _functionAutocompletePopup = new System.Windows.Controls.Primitives.Popup
        {
            Child = new System.Windows.Controls.Border
            {
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = System.Windows.Media.Brushes.Gray,
                BorderThickness = new Thickness(1),
                Child = _functionAutocompleteListBox
            },
            StaysOpen = true,
            AllowsTransparency = true,
        };
    }

    /// <summary>
    /// Handles Up/Down/Tab/Enter/Escape while the function AutoComplete popup is open. Returns true
    /// when the key was consumed by the popup, so the caller (InlineEditor_KeyDown) skips its normal
    /// formula-editing handling for that key.
    /// </summary>
    private bool HandleFunctionAutocompleteKeyDown(System.Windows.Controls.TextBox editor, Key key)
    {
        if (!FunctionAutocompleteIsOpen)
            return false;

        var plan = _formulaRangeEditingSession.PlanFunctionAutocompleteKey(
            FormulaBarWpfInputAdapter.ToFormulaEditorKey(key),
            _functionAutocompleteListBox!.SelectedIndex);
        switch (plan.Action)
        {
            case FormulaFunctionAutocompleteKeyAction.MoveSelection:
                _functionAutocompleteListBox.SelectedIndex = plan.SelectionIndex;
                break;

            case FormulaFunctionAutocompleteKeyAction.CommitSelection:
                if (_functionAutocompleteListBox.SelectedItem is string chosen)
                    CommitFunctionAutocomplete(editor, chosen);
                break;

            case FormulaFunctionAutocompleteKeyAction.Dismiss:
                HideFormulaFunctionAutocomplete();
                break;
        }

        return plan.Handled;
    }

    private void CommitFunctionAutocomplete(System.Windows.Controls.TextBox editor, string chosenName)
    {
        var edit = _formulaRangeEditingSession.CommitFunctionAutocomplete(
            editor.Text,
            chosenName,
            BuiltInFunctions.Names);
        HideFormulaFunctionAutocomplete();
        ApplyTextEdit(editor, edit);
        if (ReferenceEquals(editor, _inlineEditor))
            FormulaBar.Text = edit.Text;
        else if (_inlineEditor?.IsVisible == true)
            _inlineEditor.Text = edit.Text;
    }

    // ── R91-formula-editing-assist-5-2: live argument-signature tooltip ────────────────────────

    /// <summary>
    /// Recomputes and shows/hides the live argument-signature tooltip for the given formula editor,
    /// driven entirely by <see cref="FormulaSignatureHelpPlanner"/>. The current argument (the one
    /// the caret sits inside) is rendered bold, matching Excel's own function ScreenTip.
    /// </summary>
    private void RefreshFormulaSignatureHelp(System.Windows.Controls.TextBox editor)
    {
        var info = FormulaSignatureHelpPlanner.Resolve(editor.Text, editor.CaretIndex);
        if (info is null)
        {
            if (_signatureHelpPopup is not null)
                _signatureHelpPopup.IsOpen = false;
            return;
        }

        EnsureSignatureHelpPopup();
        _signatureHelpTextBlock!.Inlines.Clear();
        _signatureHelpTextBlock.Inlines.Add(new System.Windows.Documents.Run(info.FunctionName + "("));
        for (var i = 0; i < info.Arguments.Count; i++)
        {
            if (i > 0)
                _signatureHelpTextBlock.Inlines.Add(new System.Windows.Documents.Run(", "));

            var argument = info.Arguments[i];
            var displayName = argument.Optional ? $"[{argument.Name}]" : argument.Name;
            var run = new System.Windows.Documents.Run(displayName)
            {
                FontWeight = argument.IsCurrent ? FontWeights.Bold : FontWeights.Normal
            };
            _signatureHelpTextBlock.Inlines.Add(run);
        }
        _signatureHelpTextBlock.Inlines.Add(new System.Windows.Documents.Run(")"));

        var caretRect = editor.GetRectFromCharacterIndex(editor.CaretIndex);
        _signatureHelpPopup!.PlacementTarget = editor;
        _signatureHelpPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.RelativePoint;
        _signatureHelpPopup.HorizontalOffset = caretRect.Left;
        _signatureHelpPopup.VerticalOffset = caretRect.Top - 24;
        _signatureHelpPopup.IsOpen = true;
    }

    private void EnsureSignatureHelpPopup()
    {
        if (_signatureHelpPopup is not null)
            return;

        _signatureHelpTextBlock = new System.Windows.Controls.TextBlock
        {
            Padding = new Thickness(4, 2, 4, 2),
        };
        _signatureHelpPopup = new System.Windows.Controls.Primitives.Popup
        {
            Child = new System.Windows.Controls.Border
            {
                Background = System.Windows.Media.Brushes.LightYellow,
                BorderBrush = System.Windows.Media.Brushes.Gray,
                BorderThickness = new Thickness(1),
                Child = _signatureHelpTextBlock
            },
            StaysOpen = true,
            AllowsTransparency = true,
            IsHitTestVisible = false,
        };
    }

    /// <summary>
    /// Real Excel (and FreeX's own Avalonia shell via CalculateInlineCellCaretIndex) places the
    /// caret at the pixel position that was double-clicked, not always at the end of the text
    /// (R61-render-formula-bar-6-2). When a double-click x-coordinate (in SheetGrid/EditOverlay
    /// coordinate space, i.e. e.GetPosition(SheetGrid).X) is supplied, hit-test it against the
    /// already-laid-out inline editor via WPF's own GetCharacterIndexFromPoint; a plain keyboard
    /// entry (F2, typing to start an entry, Enter/Tab navigation, etc.) passes null and keeps the
    /// existing "caret at end" behavior.
    /// </summary>
    private int ResolveInlineEditorCaretIndex(double? clickX, double editorCanvasLeft)
    {
        if (_inlineEditor is null)
            return 0;

        var textLength = _inlineEditor.Text.Length;
        if (clickX is not { } x)
            return textLength;

        // The editor was just given its final Text/Width/Height/position this pass; force a
        // synchronous layout pass so GetCharacterIndexFromPoint hit-tests the current content
        // rather than stale (or absent) layout.
        _inlineEditor.UpdateLayout();
        double localX = x - editorCanvasLeft;
        int hitIndex = _inlineEditor.GetCharacterIndexFromPoint(
            new System.Windows.Point(localX, _inlineEditor.ActualHeight / 2), snapToText: true);
        return hitIndex >= 0 ? Math.Clamp(hitIndex, 0, textLength) : textLength;
    }

    private void SyncFormulaBarTextFromInlineEditor()
    {
        if (_inlineEditor is null || _syncingFormulaEditorText || FormulaBar.Text == _inlineEditor.Text)
            return;

        try
        {
            _syncingFormulaEditorText = true;
            FormulaBar.Text = _inlineEditor.Text;
        }
        finally
        {
            _syncingFormulaEditorText = false;
        }
    }

    private void SyncInlineEditorTextFromFormulaBar()
    {
        if (_inlineEditor?.IsVisible != true || _syncingFormulaEditorText || _inlineEditor.Text == FormulaBar.Text)
            return;

        try
        {
            _syncingFormulaEditorText = true;
            _inlineEditor.Text = FormulaBar.Text;
        }
        finally
        {
            _syncingFormulaEditorText = false;
        }

        RefreshInlineEditorTextSurface();
        RefreshInlineEditorChromeBorder();
    }

    private void RefreshInlineEditorTextSurface()
    {
        if (_inlineEditor is null || _inlineEditorChromeBaseRect is not { } chromeBaseRect)
            return;

        var desiredTextWidth = MeasureEditorTextWidth(_inlineEditor);
        // R78-render-inplace-editor-5-3: always multiply from the fixed single-line height
        // baseline (not chromeBaseRect.Height, which this method itself grows below) so a
        // multi-line recompute never compounds off the previous pass's already-grown height.
        var lineCount = CountInlineEditorLines(_inlineEditor.Text);
        var layout = FormulaInlineEditorLayoutPlanner.Create(
            chromeBaseRect.Left,
            chromeBaseRect.Top,
            chromeBaseRect.Width,
            _inlineEditorSingleLineHeight,
            desiredTextWidth,
            EditOverlay.ActualWidth,
            lineCount);

        System.Windows.Controls.Canvas.SetLeft(_inlineEditor, layout.TextOverlayRect.Left - 4);
        _inlineEditor.Width = layout.TextOverlayRect.Width + 8;
        _inlineEditor.Height = layout.EditorRect.Height;
        _inlineEditorChromeBaseRect = chromeBaseRect with { Height = layout.EditorRect.Height };

        if (_inlineFormulaReferenceOverlay is not null)
        {
            System.Windows.Controls.Canvas.SetLeft(_inlineFormulaReferenceOverlay, layout.TextOverlayRect.Left);
            _inlineFormulaReferenceOverlay.Width = layout.TextOverlayRect.Width;
            _inlineFormulaReferenceOverlay.Height = layout.TextOverlayRect.Height;
        }
    }

    /// <summary>
    /// R88-render-rtl-bidi-5-3: resolves the WPF <see cref="FlowDirection"/> the inline editor (and
    /// the Formula Bar, which edits the same cell) should use so an RTL-reading-order cell edits
    /// with a true right-to-left paragraph embedding direction -- matching real Excel's behavior of
    /// starting the caret at the right, sending Home to the visual-right start, and live bidi
    /// reordering while typing. <see cref="ResolveInlineEditorTextAlignment"/> only anchors the text
    /// block to one edge; it never switches the base paragraph direction WPF's TextBox uses for
    /// caret/insertion-point behavior, so both must be set together.
    /// </summary>
    private FlowDirection ResolveInlineEditorFlowDirection(Sheet? sheet, Cell? cell)
    {
        var style = cell is null ? null : _workbook.GetStyle(cell.StyleId);
        var isEffectivelyRightToLeft = CellTextOrientationLayoutPlanner.ResolveIsEffectivelyRightToLeft(
            style?.ReadingOrder ?? CellReadingOrder.Context, sheet?.IsRightToLeft ?? false);
        return isEffectivelyRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    /// <summary>
    /// R78-render-inplace-editor-5-4: resolves the WPF <see cref="TextAlignment"/> the inline
    /// editor should use so it matches the cell's own effective horizontal alignment (e.g. a
    /// right-aligned/"General" numeric cell edits with right-aligned text) instead of always
    /// defaulting to left. Mirrors the Avalonia shell's MapCellTextAlignment / this file's own
    /// GridView.Rendering.cs ResolveWrapTextAlignment: explicit Left/Right/Center/Justify/
    /// Distributed are direction-agnostic, while General resolves to the "end" of the cell's
    /// effective reading order for numeric/date content and the "start" for everything else.
    /// </summary>
    private TextAlignment ResolveInlineEditorTextAlignment(Sheet? sheet, Cell? cell)
    {
        var style = cell is null ? null : _workbook.GetStyle(cell.StyleId);
        var hAlign = style?.HorizontalAlignment ?? FreeX.Core.Model.HorizontalAlignment.General;
        var isNumeric = cell?.Value is NumberValue or DateTimeValue;
        var isEffectivelyRightToLeft = CellTextOrientationLayoutPlanner.ResolveIsEffectivelyRightToLeft(
            style?.ReadingOrder ?? CellReadingOrder.Context, sheet?.IsRightToLeft ?? false);

        return hAlign switch
        {
            FreeX.Core.Model.HorizontalAlignment.Left => TextAlignment.Left,
            FreeX.Core.Model.HorizontalAlignment.Center
                or FreeX.Core.Model.HorizontalAlignment.Justify
                or FreeX.Core.Model.HorizontalAlignment.Distributed => TextAlignment.Center,
            FreeX.Core.Model.HorizontalAlignment.Right => TextAlignment.Right,
            FreeX.Core.Model.HorizontalAlignment.General when isNumeric =>
                isEffectivelyRightToLeft ? TextAlignment.Left : TextAlignment.Right,
            FreeX.Core.Model.HorizontalAlignment.General =>
                isEffectivelyRightToLeft ? TextAlignment.Right : TextAlignment.Left,
            _ => TextAlignment.Left
        };
    }

    /// <summary>
    /// Counts the number of display lines in inline-editor text, i.e. one more than the number of
    /// line breaks (Alt+Enter inserts <see cref="Environment.NewLine"/>, but a cell value loaded
    /// from a file may carry a bare "\n"; both are counted the same way as a line separator).
    /// </summary>
    private static int CountInlineEditorLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return 1;

        var lineCount = 1;
        foreach (var ch in text)
        {
            if (ch == '\n')
                lineCount++;
        }

        return lineCount;
    }

    private void RefreshInlineEditorChromeBorder()
    {
        if (_inlineEditorChrome is null || _inlineEditor is null || _inlineEditorChromeBaseRect is not { } chromeBaseRect)
            return;

        var overflow = GetInlineEditorTextOverflow(_inlineEditor, chromeBaseRect.Width);
        ApplyInlineEditorChromeFrame(overflow);
    }

    private void ApplyInlineEditorChromeFrame(FormulaInlineEditorOverflow overflow)
    {
        if (_inlineEditorChrome is null || _inlineEditorChromeBaseRect is not { } chromeBaseRect)
            return;

        var chromeRect = FormulaInlineEditorLayoutPlanner.GetChromeRect(chromeBaseRect, overflow);
        System.Windows.Controls.Canvas.SetLeft(_inlineEditorChrome, chromeRect.Left);
        System.Windows.Controls.Canvas.SetTop(_inlineEditorChrome, chromeRect.Top);
        _inlineEditorChrome.Width = chromeRect.Width;
        _inlineEditorChrome.Height = chromeRect.Height;
        _inlineEditorChrome.BorderThickness = FormulaBarWpfInputAdapter.ToWpfThickness(
            FormulaInlineEditorLayoutPlanner.GetChromeBorderThickness(overflow));
    }

    private static FormulaInlineEditorOverflow GetInlineEditorTextOverflow(System.Windows.Controls.TextBox editor, double chromeWidth)
    {
        if (chromeWidth <= 0 || string.IsNullOrEmpty(editor.Text))
            return FormulaInlineEditorOverflow.None;

        var formattedText = CreateEditorFormattedText(editor);

        var innerWidth = Math.Max(0, chromeWidth - editor.Padding.Left - editor.Padding.Right);
        var scrollOffset = Math.Max(0, editor.HorizontalOffset);
        var spillsLeft = scrollOffset > 0;
        var spillsRight = formattedText.WidthIncludingTrailingWhitespace - scrollOffset > innerWidth;
        return new FormulaInlineEditorOverflow(spillsLeft, spillsRight);
    }

    private static double MeasureEditorTextWidth(System.Windows.Controls.TextBox editor) =>
        string.IsNullOrEmpty(editor.Text)
            ? 0
            : CreateEditorFormattedText(editor).WidthIncludingTrailingWhitespace;

    private static FormattedText CreateEditorFormattedText(System.Windows.Controls.TextBox editor)
    {
        var typeface = new Typeface(editor.FontFamily, editor.FontStyle, editor.FontWeight, editor.FontStretch);
        var pixelsPerDip = VisualTreeHelper.GetDpi(editor).PixelsPerDip;
        return new FormattedText(
            editor.Text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            typeface,
            editor.FontSize,
            Brushes.Black,
            pixelsPerDip);
    }

    private void HideInlineEditor(bool commit)
    {
        if (_inlineEditor == null) return;
        _inlineEditor.Visibility = Visibility.Collapsed;
        if (_inlineEditorChrome is not null)
            _inlineEditorChrome.Visibility = Visibility.Collapsed;
        _inlineEditorChromeBaseRect = null;
        SheetGrid.EditingCell = null;
        HideFormulaFunctionAutocomplete();
        if (_signatureHelpPopup is not null)
            _signatureHelpPopup.IsOpen = false;
        FormulaReferenceTextOverlay.Clear(_inlineFormulaReferenceOverlay);
        ClearFormulaReferenceGridOverlays();
        if (_textBoxInlineEditor?.IsVisible != true &&
            _validationDropdown?.Visibility != Visibility.Visible)
            EditOverlay.IsHitTestVisible = false;
        if (commit)
            FormulaBar.Text = _inlineEditor.Text;
    }

    private void InlineEditor_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        // R91-formula-editing-assist-5-1: while the function-name AutoComplete popup is open, Up/
        // Down/Tab/Enter/Escape drive the popup instead of their normal formula-editing meaning
        // (moving the caret, cycling a reference, committing the edit). Checked first, before any
        // other key handling below, exactly as Excel's own popup takes priority over those keys.
        if (_inlineEditor is not null && HandleFunctionAutocompleteKeyDown(_inlineEditor, e.Key))
        {
            e.Handled = true;
            return;
        }

        // R83-app-flashfill-autocomplete-5-2: Backspace/Delete reject a live AutoComplete
        // suggestion (Excel behavior) rather than instantly re-offering the same completion the
        // deletion just removed. The key itself is left unhandled so it still performs its normal
        // TextBox deletion.
        if (e.Key == Key.Back || e.Key == Key.Delete)
            _formulaRangeEditingSession.SuppressNextCellValueAutocomplete();

        if (TryToggleFormulaRangeEntrySelectionMode(e.Key, Keyboard.Modifiers))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.None && _inlineEditor is not null)
        {
            var togglePlan = _formulaRangeEditingSession.TogglePointMode(_inlineEditor.Text);
            ApplyFormulaEditStatusBarPlan(togglePlan.StatusBarPlan);
            e.Handled = togglePlan.Handled;
            return;
        }

        if (_formulaRangeEditingSession.ShouldCycleReference(
                FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.Key),
                FormulaBarWpfInputAdapter.ToFormulaEditorModifiers(Keyboard.Modifiers),
                FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.SystemKey)) &&
            _inlineEditor is not null)
        {
            if (TryCycleFormulaReference(_inlineEditor))
            {
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.Escape)
        {
            HideInlineEditor(commit: false);
            // Restore original text in formula bar
            var addr = _formulaEditCell ?? SheetGrid.SelectedRange?.Start;
            if (addr.HasValue)
            {
                var cell = _workbook.GetSheet(addr.Value.Sheet)?.GetCell(addr.Value);
                FormulaBar.Text = FormatFormulaBarText(cell, addr.Value);
                RestoreFormulaEditCellSelection(addr.Value);
            }
            ClearFormulaRangeEntryState();
            RefreshStatusBar();
            CancelCopyAndTransientModes();
            FocusSheetGridIfNeeded();
            e.Handled = true;
            return;
        }
        var selectedRange = SheetGrid.SelectedRange;
        if (selectedRange is null)
            return;
        var formulaRangeEntryActive = IsFormulaRangeEntryActive(_inlineEditor);
        var formulaReferenceCurrent = formulaRangeEntryActive
            ? _formulaRangeEditingSession.ResolveKeyboardCursor(
                selectedRange.Value,
                _selectionCursor)
            : selectedRange.Value.Start;
        var editNavigationCurrent = _formulaEditCell ?? selectedRange.Value.Start;
        var wpfModifiers = Keyboard.Modifiers;
        var modifiers = FormulaBarWpfInputAdapter.ToFormulaEditorModifiers(wpfModifiers);
        var pageSize = Math.Max(1, (SheetGrid.Viewport?.RowMetrics.Count ?? 25) - 1);
        var colPageSize = Math.Max(1, (SheetGrid.Viewport?.ColMetrics.Count ?? 12) - 1);

        var formulaReferenceNavigation = formulaRangeEntryActive
            ? _formulaRangeEditingSession.PlanKeyboardNavigation(
                selectedRange.Value,
                _selectionCursor,
                FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.Key),
                FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.SystemKey),
                modifiers,
                _workbook.GetSheet(_currentSheetId),
                pageSize,
                colPageSize)
            : null;
        if (formulaReferenceNavigation is { } navigation)
        {
            if (TryApplyFormulaRangeEntryKeyboardSelection(
                    navigation.Current,
                    navigation.Target,
                    navigation.ExtendSelection))
            {
                EnsureCellVisible(navigation.Target);
                e.Handled = true;
            }
            return;
        }

        var intent = _formulaRangeEditingSession.PlanEditKey(
            FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.Key),
            FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.SystemKey),
            modifiers,
            editNavigationCurrent,
            pageSize,
            _inlineEditor?.Text,
            _formulaEditCell is not null,
            FormulaEditorSurfaceKind.Inline,
            _formulaEditEnteredViaEditKey,
            _options.MoveSelectionAfterEnter,
            FormulaBarWpfInputAdapter.ToFormulaEditorEnterDirection(_options.AfterEnterDirection));

        if (intent.Action == ExcelEditKeyAction.InsertLineBreak)
        {
            InsertLineBreak(_inlineEditor!);
            FormulaBar.Text = _inlineEditor!.Text;
            e.Handled = true;
            return;
        }

        if (intent.Action == ExcelEditKeyAction.CommitSelection)
        {
            FormulaBar.Text = _inlineEditor!.Text;
            if (CommitEditAcrossSelection(fillFormulaEditCellOnly: formulaRangeEntryActive))
            {
                HideInlineEditor(commit: false);
                ClearFormulaRangeEntryState();
            }
            e.Handled = true;
            return;
        }

        if (intent.Action == ExcelEditKeyAction.SelectFormulaReference && intent.Target is { } referenceTarget)
        {
            if (TryApplyFormulaRangeEntryKeyboardSelection(
                    formulaReferenceCurrent,
                    referenceTarget,
                    _formulaRangeEditingSession.ShouldExtendKeyboardSelection(modifiers)))
            {
                EnsureCellVisible(referenceTarget);
                e.Handled = true;
            }
            return;
        }

        if (intent.Action == ExcelEditKeyAction.CommitAndMove && intent.Target is { } rawNext)
        {
            var next = ExcelWorksheetNavigationPlanner.AdjustTargetPastMerge(
                _workbook.GetSheet(_currentSheetId),
                editNavigationCurrent,
                rawNext);
            var text = _inlineEditor!.Text;
            FormulaBar.Text = text;
            if (string.IsNullOrEmpty(text))
            {
                HideInlineEditor(commit: false);
                ClearFormulaRangeEntryState();
                SetActiveCell(next);
                EnsureCellVisible(next);
                e.Handled = true;
                return;
            }

            if (CommitEdit())
            {
                HideInlineEditor(commit: false);
                ClearFormulaRangeEntryState();
                SetActiveCell(next);
                EnsureCellVisible(next);
            }
            e.Handled = true;
        }
    }

    private static void InsertLineBreak(System.Windows.Controls.TextBox editor)
    {
        var edit = ExcelTextEditorPlanner.InsertLineBreak(
            editor.Text,
            editor.SelectionStart,
            editor.SelectionLength,
            Environment.NewLine);
        ApplyTextEdit(editor, edit);
    }

    private void InlineEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_inlineEditor?.IsVisible != true)
            return;

        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(CommitInlineEditorLostFocusIfNeeded));
    }

    private void CommitInlineEditorLostFocusIfNeeded()
    {
        if (_inlineEditor?.IsVisible != true)
            return;

        if (ReferenceEquals(Keyboard.FocusedElement, FormulaBar) ||
            ReferenceEquals(FocusManager.GetFocusedElement(this), FormulaBar))
            return;

        if (IsFormulaRangeEntryActive(_inlineEditor))
            return;

        FormulaBar.Text = _inlineEditor.Text;
        HideInlineEditor(commit: true);
        CommitEdit();
    }

    private bool TryCommitPendingSpellCheckEdit()
    {
        if (_inlineEditor?.IsVisible == true)
        {
            if (IsFormulaRangeEntryActive(_inlineEditor))
                return false;

            FormulaBar.Text = _inlineEditor.Text;
            if (!CommitEdit())
                return false;

            HideInlineEditor(commit: false);
            ClearFormulaRangeEntryState();
            return true;
        }

        if (_formulaEditCell is not null)
        {
            if (IsFormulaRangeEntryActive(FormulaBar))
                return false;

            return CommitEdit();
        }

        if (SheetGrid.SelectedRange?.Start is not { } activeCell)
            return true;

        var sheet = _workbook.GetSheet(_currentSheetId);
        var currentText = FormatFormulaBarText(sheet?.GetCell(activeCell), activeCell);
        if (string.Equals(FormulaBar.Text, currentText, StringComparison.Ordinal))
            return true;

        return CommitEdit();
    }

    private void FocusSheetGridIfNeeded()
    {
        if (!ReferenceEquals(Keyboard.FocusedElement, SheetGrid))
            SheetGrid.Focus();
    }


    private void SetSelectionMode(ExcelSelectionMode mode)
    {
        _selectionMode = mode;
        if (mode != ExcelSelectionMode.Normal)
            _endMode = false;
        SetStatusBarModeResourceKey(ExcelSelectionModePlanner.StatusBarModeResourceKey(mode));
    }

    private void SetEndMode(bool enabled)
    {
        _endMode = enabled;
        if (enabled)
            _selectionMode = ExcelSelectionMode.Normal;
        SetStatusBarModeResourceKey(ExcelSelectionModePlanner.EndModeStatusBarResourceKey(enabled));
    }

    private void SetStatusBarModeResourceKey(string resourceKey)
    {
        SetStatusBarModeText(UiText.Get(resourceKey));
    }

    private void SetStatusBarModeText(string text)
    {
        if (StatusStatsPanel is null || StatusReadyText is null)
            return;

        ApplyStatusBarDisplayState(_statusBarDisplayStateCache.GetReady(
            GetCurrentStatusBarViewMode(),
            zoomPercent: 0,
            text));
    }

    private void SetFormulaEditStatusBarMode(bool pointMode)
    {
        ApplyFormulaEditStatusBarPlan(_formulaRangeEditingSession.BuildEditStatusBarPlan(pointMode));
    }

    private void ApplyFormulaEditStatusBarPlan(FormulaEditStatusBarPlan plan)
    {
        SetStatusBarModeResourceKey(plan.ResourceKey);
    }

    private void ApplyFormulaEditStatusBarPlan(FormulaEditStatusBarPlan? plan)
    {
        if (plan is { } statusBarPlan)
            ApplyFormulaEditStatusBarPlan(statusBarPlan);
    }

    private void FormulaBar_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.KeyboardDevice.Modifiers == ModifierKeys.None &&
            (e.Key == Key.Enter || e.Key == Key.Escape || e.Key == Key.F4) &&
            TryRouteFormulaPointModeKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        // R88-app-autocomplete-picklist-5-3: Backspace/Delete reject a live AutoComplete suggestion
        // (Excel behavior) rather than instantly re-offering the same completion the deletion just
        // removed -- mirrors InlineEditor_KeyDown's identical guard for the in-cell editor.
        if (e.Key == Key.Back || e.Key == Key.Delete)
            _formulaRangeEditingSession.SuppressNextCellValueAutocomplete();

        if (TryToggleFormulaRangeEntrySelectionMode(e.Key, e.KeyboardDevice.Modifiers))
        {
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2 && e.KeyboardDevice.Modifiers == ModifierKeys.None)
        {
            var togglePlan = _formulaRangeEditingSession.TogglePointMode(FormulaBar.Text);
            ApplyFormulaEditStatusBarPlan(togglePlan.StatusBarPlan);
            e.Handled = togglePlan.Handled;
        }
        else if (_formulaRangeEditingSession.ShouldCycleReference(
                     FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.Key),
                     FormulaBarWpfInputAdapter.ToFormulaEditorModifiers(e.KeyboardDevice.Modifiers),
                     FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.SystemKey)))
        {
            if (TryCycleFormulaReference(FormulaBar))
                e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Escape)
        {
            // Restore the original cell value and return focus to grid
            var addr = _formulaEditCell ?? SheetGrid.SelectedRange?.Start;
            if (addr.HasValue)
            {
                var cell = _workbook.GetSheet(addr.Value.Sheet)?.GetCell(addr.Value);
                FormulaBar.Text = FormatFormulaBarText(cell, addr.Value);
                RestoreFormulaEditCellSelection(addr.Value);
            }
            HideInlineEditor(commit: false);
            ClearFormulaRangeEntryState();
            RefreshStatusBar();
            ClearClipboardVisualState();
            SheetGrid.Focus();
            e.Handled = true;
        }
        else if (SheetGrid.SelectedRange is { } selectedRange)
        {
            var formulaRangeEntryActive = IsFormulaRangeEntryActive(FormulaBar);
            var formulaReferenceCurrent = formulaRangeEntryActive
                ? _formulaRangeEditingSession.ResolveKeyboardCursor(
                    selectedRange,
                    _selectionCursor)
                : selectedRange.Start;
            var editNavigationCurrent = _formulaEditCell ?? selectedRange.Start;
            int pageSize = Math.Max(1, (SheetGrid.Viewport?.RowMetrics.Count ?? 25) - 1);
            int colPageSize = Math.Max(1, (SheetGrid.Viewport?.ColMetrics.Count ?? 12) - 1);
            var wpfModifiers = e.KeyboardDevice.Modifiers;
            var modifiers = FormulaBarWpfInputAdapter.ToFormulaEditorModifiers(wpfModifiers);
            var formulaReferenceNavigation = formulaRangeEntryActive
                ? _formulaRangeEditingSession.PlanKeyboardNavigation(
                    selectedRange,
                    _selectionCursor,
                    FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.Key),
                    FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.SystemKey),
                    modifiers,
                    _workbook.GetSheet(_currentSheetId),
                    pageSize,
                    colPageSize)
                : null;
            if (formulaReferenceNavigation is { } navigation)
            {
                if (TryApplyFormulaRangeEntryKeyboardSelection(
                        navigation.Current,
                        navigation.Target,
                        navigation.ExtendSelection))
                {
                    EnsureCellVisible(navigation.Target);
                    e.Handled = true;
                }
                return;
            }

            var intent = _formulaRangeEditingSession.PlanEditKey(
                FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.Key),
                FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.SystemKey),
                modifiers,
                editNavigationCurrent,
                pageSize,
                FormulaBar.Text,
                _formulaEditCell is not null,
                FormulaEditorSurfaceKind.FormulaBar,
                false,
                _options.MoveSelectionAfterEnter,
                FormulaBarWpfInputAdapter.ToFormulaEditorEnterDirection(_options.AfterEnterDirection));

            if (intent.Action == ExcelEditKeyAction.InsertLineBreak)
            {
                InsertLineBreak(FormulaBar);
                e.Handled = true;
            }
            else if (intent.Action == ExcelEditKeyAction.CommitSelection)
            {
                if (CommitEditAcrossSelection(fillFormulaEditCellOnly: formulaRangeEntryActive))
                {
                    HideInlineEditor(commit: false);
                    ClearFormulaRangeEntryState();
                }
                e.Handled = true;
            }
            else if (intent.Action == ExcelEditKeyAction.SelectFormulaReference && intent.Target is { } referenceTarget)
            {
                if (TryApplyFormulaRangeEntryKeyboardSelection(
                        formulaReferenceCurrent,
                        referenceTarget,
                        _formulaRangeEditingSession.ShouldExtendKeyboardSelection(modifiers)))
                {
                    EnsureCellVisible(referenceTarget);
                    e.Handled = true;
                }
            }
            else if (intent.Action == ExcelEditKeyAction.CommitAndMove && intent.Target is { } rawTarget)
            {
                var target = ExcelWorksheetNavigationPlanner.AdjustTargetPastMerge(
                    _workbook.GetSheet(_currentSheetId),
                    editNavigationCurrent,
                    rawTarget);
                if (CommitEdit())
                {
                    HideInlineEditor(commit: false);
                    ClearFormulaRangeEntryState();
                    SetActiveCell(target);
                    EnsureCellVisible(target);
                }

                e.Handled = true;
            }
        }
    }

    private void FormulaBarCancelButton_Click(object sender, RoutedEventArgs e)
    {
        var addr = _formulaEditCell ?? SheetGrid.SelectedRange?.Start;
        if (addr.HasValue)
        {
            var cell = _workbook.GetSheet(addr.Value.Sheet)?.GetCell(addr.Value);
            FormulaBar.Text = FormatFormulaBarText(cell, addr.Value);
            RestoreFormulaEditCellSelection(addr.Value);
        }

        HideInlineEditor(commit: false);
        ClearFormulaRangeEntryState();
        RefreshStatusBar();
        ClearClipboardVisualState();
        FocusSheetGridIfNeeded();
    }

    private void FormulaBarEnterButton_Click(object sender, RoutedEventArgs e)
    {
        if (CommitEdit())
        {
            HideInlineEditor(commit: false);
            ClearFormulaRangeEntryState();
        }

        FocusSheetGridIfNeeded();
    }

    private void CellAddressBox_DropDownOpened(object sender, EventArgs e)
    {
        CellAddressBox.ItemsSource = NameBoxDropdownPlanner.Build(_workbook, _currentSheetId);
    }

    private void CellAddressBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { IsDropDownOpen: true, SelectedItem: NameBoxNavigationItem item })
            return;

        CellAddressBox.Text = item.Name;
        if (item.Range is { } selectedRange)
            NavigateNameBoxTo(selectedRange);
        else if (!TrySelectNameBoxObject(item))
            return;

        FocusSheetGridIfNeeded();
    }

    private bool TrySelectNameBoxObject(NameBoxNavigationItem item)
    {
        if (item.Kind != NameBoxNavigationItemKind.Object ||
            item.ObjectKind is not { } objectKind ||
            item.ObjectId is not { } objectId ||
            item.Anchor is not { } anchor)
        {
            return false;
        }

        if (!_currentSheetId.Equals(item.SheetId))
        {
            _currentSheetId = item.SheetId;
            RefreshSheetTabs();
        }

        SelectInsertedDrawingObject(
            objectId,
            objectKind switch
            {
                SelectionPaneObjectKind.Chart => FreeX.App.UI.ObjectKind.Chart,
                SelectionPaneObjectKind.Picture => FreeX.App.UI.ObjectKind.Picture,
                SelectionPaneObjectKind.TextBox => FreeX.App.UI.ObjectKind.TextBox,
                SelectionPaneObjectKind.Shape => FreeX.App.UI.ObjectKind.Shape,
                _ => FreeX.App.UI.ObjectKind.None,
            },
            anchor);
        return true;
    }

    private void CellAddressBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape && e.KeyboardDevice.Modifiers == ModifierKeys.None)
        {
            RestoreCellAddressBoxText();
            FocusSheetGridIfNeeded();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter || e.KeyboardDevice.Modifiers != ModifierKeys.None)
            return;

        var navigationText = DefinedNameUiPolicy.ResolveNameBoxNavigationDisplayText(
            _workbook,
            _currentSheetId,
            CellAddressBox.Text);
        if (!TryParseNameBoxReferenceRange(CellAddressBox.Text, out var selectedRange))
        {
            if (TryDefineNameFromNameBox())
            {
                e.Handled = true;
                return;
            }

            FocusManager.SetFocusedElement(this, CellAddressBox);
            CellAddressBox.Focus();
            Keyboard.Focus(CellAddressBox);
            CellAddressBox.SelectAll();
            e.Handled = true;
            return;
        }

        NavigateNameBoxTo(selectedRange);
        CellAddressBox.Text = navigationText;
        CellAddressBox.SelectAll();
        FocusSheetGridIfNeeded();
        e.Handled = true;
    }

    // Sheet-scope-aware Name Box reference resolution, matching formula evaluation's precedence
    // (Workbook.TryGetNamedRange(name, contextSheetId, ...): sheet-scoped names on the active sheet
    // take precedence over a same-named workbook-global name). Also resolves cross-sheet references
    // typed as SheetName!A1 (matching the Avalonia shell's TryParseCellAddressBoxReferenceRange).
    private bool TryParseNameBoxReferenceRange(string text, out GridRange range)
    {
        if (WorkbookReferenceNavigator.TryParseReferenceRange(
                text,
                _currentSheetId,
                name => _workbook.Sheets.FirstOrDefault(sheet =>
                    string.Equals(sheet.Name, name, StringComparison.OrdinalIgnoreCase))?.Id,
                _workbook.NamedRanges,
                (name, sheetId) => _workbook.TryGetNamedRange(name, sheetId, out var scoped) ? scoped : null,
                out range))
        {
            return true;
        }

        // Excel also lets the Name Box resolve a structured table's name, selecting the table's
        // data-body range (the same rows a structured reference like Table1[#Data] would select),
        // rather than only cell/named-range references. Without this, an existing table's name
        // falls through to TryDefineNameFromNameBox and silently creates a colliding defined name.
        return StructuredTableSelectionPlanner.TryResolveDataBodyRange(
            _workbook,
            text,
            out range);
    }

    // Cross-sheet Name Box navigation must refresh the sheet-tab strip (active-tab highlight)
    // and the Protect-Sheet/Protect-Workbook ribbon state for the newly-active sheet, matching
    // every other sheet-activation path (e.g. SheetTab_MouseLeftButtonDown).
    private void NavigateNameBoxTo(GridRange selectedRange)
    {
        var previousSheetId = _currentSheetId;
        _currentSheetId = selectedRange.Start.Sheet;
        SetSelectionRange(selectedRange, selectedRange.Start);
        EnsureCellVisible(selectedRange.Start);
        UpdateViewport();
        RefreshValidationDropdown();
        RefreshDvInputMessage();

        if (!_currentSheetId.Equals(previousSheetId))
            RefreshSheetTabs();
    }

    private bool TryDefineNameFromNameBox()
    {
        var plan = DefinedNameUiPolicy.PlanNameBoxDefinition(
            _workbook,
            _currentSheetId,
            SheetGrid.SelectedRange,
            CellAddressBox.Text,
            DefinedNameUiProfile.Wpf);
        if (!plan.CanDefine)
            return false;

        if (!TryExecuteCommand(plan.Command!, UiText.Get("MainWindow_Content_DefineName")))
            return false;

        CellAddressBox.Text = plan.Name;
        CellAddressBox.SelectAll();
        RefreshToolbar();
        RefreshStatusBar();
        FocusSheetGridIfNeeded();
        return true;
    }

    private void RestoreCellAddressBoxText()
    {
        CellAddressBox.Text = SheetGrid.SelectedRange is { } range
            ? FormatNameBoxSelectionText(range)
            : "A1";
        CellAddressBox.SelectAll();
    }

    private bool TryCycleFormulaReference(System.Windows.Controls.TextBox editor)
    {
        var caretIndex = editor.SelectionLength > 0 ? editor.SelectionStart : editor.CaretIndex;
        var anchor = _formulaEditCell ?? SheetGrid.SelectedRange?.Start;
        if (!_formulaRangeEditingSession.TryPlanReferenceCycle(
                editor.Text, caretIndex, anchor, _options.UseR1C1ReferenceStyle, out var edit))
            return false;

        ApplyFormulaEditorTextEdit(editor, edit);
        _formulaRangeEditingSession.TrackReferenceSpan(edit.SelectionStart, edit.SelectionLength);
        return true;
    }

    private void ApplyFormulaEditorTextEdit(System.Windows.Controls.TextBox editor, ExcelTextEdit edit)
    {
        _isApplyingFormulaEditorText = true;
        try
        {
            ApplyTextEdit(editor, edit);
            if (!ReferenceEquals(editor, FormulaBar))
                FormulaBar.Text = editor.Text;
            else if (_inlineEditor?.IsVisible == true)
                _inlineEditor.Text = editor.Text;
        }
        finally
        {
            _isApplyingFormulaEditorText = false;
        }
    }

    private static void ApplyTextEdit(System.Windows.Controls.TextBox editor, ExcelTextEdit edit)
    {
        editor.Text = edit.Text;
        editor.SelectionStart = edit.SelectionStart;
        editor.SelectionLength = edit.SelectionLength;
    }

    private bool CommitEdit()
    {
        if (SheetGrid.SelectedRange == null && _formulaEditCell is null) return false;
        var addr = _formulaEditCell ?? SheetGrid.SelectedRange!.Value.Start;
        var text = FormulaBar.Text;

        SynchronizeWorkbookSessionSelection();
        var committed = CompleteWorkbookSessionCellCommit(
            _session.CommitCellText(text, _options.UseR1C1ReferenceStyle),
            addr,
            "Edit Cell");
        if (committed)
            ClearFormulaRangeEntryState();
        return committed;
    }

    private bool CommitEditAcrossSelection(bool fillFormulaEditCellOnly = false)
    {
        if (SheetGrid.SelectedRange is not { } range) return false;
        SynchronizeWorkbookSessionSelection();
        if (fillFormulaEditCellOnly && _formulaEditCell is { } formulaCell)
        {
            var formulaText = FormulaBar.Text;
            var committed = CompleteWorkbookSessionCellCommit(
                _session.CommitCellText(formulaText, _options.UseR1C1ReferenceStyle),
                formulaCell,
                "Edit Cell");
            if (committed)
                ClearFormulaRangeEntryState();
            return committed;
        }

        var text = FormulaBar.Text;
        var selectionCommitted = CompleteWorkbookSessionCellCommit(
            _session.CommitCellTextAcrossSelection(text, _options.UseR1C1ReferenceStyle),
            range.Start,
            "Edit Selection");
        if (selectionCommitted)
            ClearFormulaRangeEntryState();
        return selectionCommitted;
    }

    private void ConfigureWorkbookSessionRendererAdapters() =>
        _session.DataValidationPromptResolver = ResolveDataValidationPrompt;

    private UserMessageResult ResolveDataValidationPrompt(DataValidationPromptRequest request)
    {
        return ShowOwnedSynchronousPrompt(FreeXSynchronousPromptCatalog.ForDataValidation(
            request.Title,
            request.Message,
            request.AlertStyle));
    }

    private UserMessageResult ShowOwnedSynchronousPrompt(FreeXSynchronousPromptDescriptor descriptor)
    {
        Activate();
        var request = descriptor.Resolve(UiText.Get, UiText.Format);
        return _messageService.ShowMessage(
            request.Message,
            request.Title,
            request.Buttons,
            request.Kind);
    }

    /// <summary>
    /// Decides whether a Cancel response to an AskToContinue data-validation alert should discard
    /// the invalid entry and restore the cell's previously committed value. Excel's Information
    /// style offers only OK/Cancel (Cancel discards), and its Warning style offers Yes/No/Cancel
    /// (Cancel also discards, while No instead leaves the invalid entry for the user to fix). Stop
    /// style never reaches AskToContinue (it's always Block), so it never restores here.
    /// </summary>
    internal static bool ShouldRestoreOnCancel(DvAlertStyle alertStyle, MessageBoxResult result) =>
        result == MessageBoxResult.Cancel && alertStyle != DvAlertStyle.Stop;

    /// <summary>
    /// Discards the in-progress edit and restores the formula bar to the cell's currently
    /// committed value/formula, mirroring what Escape does while editing. Used when an
    /// AskToContinue-style data validation alert (Information or Warning) is dismissed with
    /// Cancel: Excel discards the invalid entry entirely rather than leaving it for the user to
    /// fix (that's what No does instead).
    /// </summary>
    private void RestoreFormulaBarToCommittedValue(CellAddress addr)
    {
        HideInlineEditor(commit: false);
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(addr);
        FormulaBar.Text = FormatFormulaBarText(cell, addr);
        ClearFormulaRangeEntryState();
    }

    private bool CompleteWorkbookSessionCellCommit(
        WorkbookCellEditResult result,
        CellAddress editedAddress,
        string title)
    {
        if (result.Success || result.Failure is null)
        {
            RecordDiagnosticEvent("command_invoked", new Dictionary<string, string?>
            {
                ["command"] = title,
                ["status"] = result.Success ? "succeeded" : "failed"
            });
        }

        if (!result.Success)
        {
            ShowWorkbookSessionCellEditFailure(result, editedAddress, title);
            return false;
        }

        if (_workbookClipboardSession.HasContent || SheetGrid.ClipboardRange is not null)
        {
            _workbookClipboardSession.Clear();
            ClearClipboardVisualState();
        }

        ApplyWorkbookSessionSelectionToRenderer();
        InvalidateNavigationCaches();
        UpdateTitleBar();
        _windowRegistry?.NotifyDocumentStateChanged(this);
        UpdateViewport();
        RefreshStatusBar();
        RefreshValidationDropdown();
        RefreshDvInputMessage();
        NotifyOtherWindowsOfWorkbookChange();
        return true;
    }

    private void ShowWorkbookSessionCellEditFailure(
        WorkbookCellEditResult result,
        CellAddress editedAddress,
        string title)
    {
        switch (result.Failure)
        {
            case { Kind: WorkbookCellEditFailureKind.InvalidEntrySyntax }:
                ShowOwnedMessage(
                    "Microsoft Excel found an error in this formula. Please check the formula and try again.",
                    "Microsoft Excel",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;

            case
            {
                Kind: WorkbookCellEditFailureKind.DataValidationBlocked,
                AlertStyle: { } alertStyle
            } blocked:
                ShowOwnedMessage(
                    result.ErrorMessage ?? "The value is not valid.",
                    blocked.Title ?? "Validation Error",
                    MessageBoxButton.OK,
                    ToDataValidationMessageBoxImage(alertStyle));
                RefreshValidationDropdown();
                return;

            case
            {
                Kind: WorkbookCellEditFailureKind.DataValidationDeclined,
                AlertStyle: { } alertStyle,
                PromptDecision: { } decision
            }:
                RefreshValidationDropdown();
                if (ShouldRestoreOnCancel(alertStyle, ToMessageBoxResult(decision)))
                    RestoreFormulaBarToCommittedValue(editedAddress);
                return;

            default:
                ShowCommandError(new CommandOutcome(false, result.ErrorMessage), title);
                return;
        }
    }

    private static MessageBoxImage ToDataValidationMessageBoxImage(DvAlertStyle alertStyle) =>
        alertStyle switch
        {
            DvAlertStyle.Information => MessageBoxImage.Information,
            DvAlertStyle.Warning => MessageBoxImage.Warning,
            _ => MessageBoxImage.Error
        };

    private void UpdateTitleBar()
    {
        var displayName = WorkbookTitleFormatter.Format(
            _workbook.Name, _workbookDirty, IsWorkbookGrouped(), _windowTitleSuffix);
        WorkbookNameText.Text = displayName;
        this.Title = displayName;
    }

    private bool IsWorkbookGrouped()
        => SheetTabListPlanner.IsWorkbookGrouped(_workbook, _currentSheetId, _groupedSheetIds);

    // ── Start screen ─────────────────────────────────────────────────────────

    private bool? ShowOwnedDialog(Window dialog)
    {
        RecordDiagnosticEvent("dialog_opened", new Dictionary<string, string?>
        {
            ["dialog"] = dialog.GetType().Name
        });
        dialog.Owner = this;
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.ShowActivated = true;
        Activate();
        return dialog.ShowDialog();
    }

    private MessageBoxResult ShowOwnedMessage(
        string messageBoxText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon)
    {
        Activate();
        return ToMessageBoxResult(_messageService.ShowMessage(
            messageBoxText,
            caption,
            ToUserMessageButtons(button),
            ToUserMessageIcon(icon)));
    }

    private static UserMessageButtons ToUserMessageButtons(MessageBoxButton button) =>
        button switch
        {
            MessageBoxButton.OK => UserMessageButtons.Ok,
            MessageBoxButton.OKCancel => UserMessageButtons.OkCancel,
            MessageBoxButton.YesNo => UserMessageButtons.YesNo,
            MessageBoxButton.YesNoCancel => UserMessageButtons.YesNoCancel,
            _ => UserMessageButtons.Ok
        };

    private static UserMessageIcon ToUserMessageIcon(MessageBoxImage icon) =>
        icon switch
        {
            MessageBoxImage.None => UserMessageIcon.None,
            MessageBoxImage.Question => UserMessageIcon.Question,
            MessageBoxImage.Warning => UserMessageIcon.Warning,
            MessageBoxImage.Error => UserMessageIcon.Error,
            MessageBoxImage.Information => UserMessageIcon.Information,
            _ => UserMessageIcon.None
        };

    private static MessageBoxResult ToMessageBoxResult(UserMessageResult result) =>
        result switch
        {
            UserMessageResult.Ok => MessageBoxResult.OK,
            UserMessageResult.Cancel => MessageBoxResult.Cancel,
            UserMessageResult.Yes => MessageBoxResult.Yes,
            UserMessageResult.No => MessageBoxResult.No,
            _ => MessageBoxResult.None
        };

    private bool TryHandleTopLevelRibbonKeyTip(string keyTip)
    {
        return RibbonTopLevelKeyTipRouter.Resolve(keyTip, EnumerateVisibleTopLevelRibbonKeyTipEntries()) switch
        {
            { Kind: RibbonTopLevelKeyTipActionKind.BackstageFile } => OpenFileBackstageFromKeyTip(),
            { Kind: RibbonTopLevelKeyTipActionKind.RibbonTab, RibbonTabHeader: { } header } => SelectRibbonTabByHeader(header),
            _ => false
        };
    }

    private IEnumerable<RibbonTopLevelKeyTipEntry> EnumerateVisibleTopLevelRibbonKeyTipEntries()
    {
        foreach (var tabItem in GetVisibleKeyTipElements(RibbonKeyTipScope.TopLevel).OfType<TabItem>())
        {
            if (tabItem.Header is string header)
                yield return new RibbonTopLevelKeyTipEntry(header, RibbonTooltip.GetKeyTip(tabItem));
        }
    }

    private bool SelectRibbonTabByHeader(string header)
    {
        if (RibbonTabs == null)
            return false;

        foreach (var item in RibbonTabs.Items)
        {
            if (item is TabItem { Header: string tabHeader } &&
                string.Equals(tabHeader, header, StringComparison.OrdinalIgnoreCase))
            {
                var selectionChanged = !ReferenceEquals(RibbonTabs.SelectedItem, item);
                if (selectionChanged)
                {
                    ChangeRibbonSelectionWithoutTabNormalization(() => RibbonTabs.SelectedItem = item);
                    UpdateRibbonLayoutIfNeeded(RibbonTabs, force: true);
                    NormalizeRibbonSurfaceAfterTabSelection();
                }
                else
                {
                    UpdateRibbonLayoutIfNeeded(RibbonTabs);
                    NormalizeRibbonSurface(forceLayout: true);
                }

                return true;
            }
        }

        return false;
    }

}
