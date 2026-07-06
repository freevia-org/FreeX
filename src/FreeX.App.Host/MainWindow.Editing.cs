using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private void EnterEditMode()
    {
        if (_selectionAnchor.HasValue)
            ShowInlineEditor(_selectionAnchor.Value);
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
            var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(address);
            FormulaBar.Text = FormatFormulaBarText(cell, address);
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

    private void ShowInlineEditor(CellAddress addr)
    {
        if (!HideTextBoxInlineEditor(commit: true))
            return;

        HideValidationDropdown();
        var vp = SheetGrid.Viewport;
        if (vp == null) { FormulaBar.Focus(); return; }

        var rowMetric = FindRowMetric(vp.RowMetrics, addr.Row);
        var colMetric = FindColMetric(vp.ColMetrics, addr.Col);
        if (rowMetric == null || colMetric == null) { FormulaBar.Focus(); return; }

        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(addr);
        var text = FormatFormulaBarText(cell, addr);
        _formulaEditCell = addr;
        _formulaRangeEntryMode = false;
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
            _inlineEditor.TextChanged += (_, _) =>
            {
                SyncFormulaBarTextFromInlineEditor();
                UpdateFormulaRangeEntryStateAfterTextChanged(_inlineEditor);
                RefreshInlineEditorTextSurface();
                RefreshInlineEditorChromeBorder();
                RefreshFormulaReferenceHighlights();
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
        var layout = FormulaInlineEditorLayoutPlanner.Create(cx, cy, cellW, cellH);

        _inlineEditor.Text = text;
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
        _inlineEditor.CaretIndex = _inlineEditor.Text.Length;
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
        var layout = FormulaInlineEditorLayoutPlanner.Create(
            chromeBaseRect.Left,
            chromeBaseRect.Top,
            chromeBaseRect.Width,
            chromeBaseRect.Height,
            desiredTextWidth,
            EditOverlay.ActualWidth);

        System.Windows.Controls.Canvas.SetLeft(_inlineEditor, layout.TextOverlayRect.Left - 4);
        _inlineEditor.Width = layout.TextOverlayRect.Width + 8;

        if (_inlineFormulaReferenceOverlay is not null)
        {
            System.Windows.Controls.Canvas.SetLeft(_inlineFormulaReferenceOverlay, layout.TextOverlayRect.Left);
            _inlineFormulaReferenceOverlay.Width = layout.TextOverlayRect.Width;
        }
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
        if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.None && _inlineEditor is not null)
        {
            var togglePlan = FormulaEditInteractionPlanner.BuildPointModeTogglePlan(_inlineEditor.Text, _formulaRangeEntryMode);
            _formulaRangeEntryMode = togglePlan.PointMode;
            if (togglePlan.ClearReferenceSpan)
                ClearFormulaReferenceEntrySpan();
            ApplyFormulaEditStatusBarPlan(togglePlan.StatusBarPlan);
            e.Handled = togglePlan.Handled;
            return;
        }

        if (ExcelEditKeyPlanner.ShouldCycleFormulaReference(
                FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.Key),
                FormulaBarWpfInputAdapter.ToFormulaEditorModifiers(Keyboard.Modifiers),
                FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.SystemKey)) &&
            _inlineEditor is not null)
        {
            if (TryCycleFormulaReference(_inlineEditor))
            {
                FormulaBar.Text = _inlineEditor.Text;
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
                var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(addr.Value);
                FormulaBar.Text = FormatFormulaBarText(cell, addr.Value);
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
        var inlineEditorCommitsOnArrow = FormulaEditInteractionPlanner.ShouldCommitInlineArrows(
            _inlineEditor?.Text,
            _formulaRangeEntryMode);
        var formulaReferenceCurrent = formulaRangeEntryActive
            ? FormulaRangeEntryPlanner.GetKeyboardCursor(selectedRange.Value, _selectionCursor)
            : selectedRange.Value.Start;
        var editNavigationCurrent = _formulaEditCell ?? selectedRange.Value.Start;
        var wpfModifiers = Keyboard.Modifiers;
        var modifiers = FormulaBarWpfInputAdapter.ToFormulaEditorModifiers(wpfModifiers);
        var pageSize = Math.Max(1, (SheetGrid.Viewport?.RowMetrics.Count ?? 25) - 1);
        var colPageSize = Math.Max(1, (SheetGrid.Viewport?.ColMetrics.Count ?? 12) - 1);

        if (formulaRangeEntryActive &&
            FormulaRangeEntryPlanner.GetKeyboardSelectionTarget(
                FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.Key),
                FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.SystemKey),
                modifiers,
                formulaReferenceCurrent,
                _workbook.GetSheet(_currentSheetId),
                pageSize,
                colPageSize) is { } formulaReferenceShortcutTarget)
        {
            if (TryApplyFormulaRangeSelection(
                    formulaReferenceShortcutTarget,
                    extendSelection: wpfModifiers.HasFlag(ModifierKeys.Shift)))
            {
                EnsureCellVisible(formulaReferenceShortcutTarget);
                e.Handled = true;
            }
            return;
        }

        var intent = ExcelEditKeyPlanner.GetIntent(
            FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.Key),
            modifiers,
            editNavigationCurrent,
            pageSize: pageSize,
            allowFormulaBarNavigationKeys: false,
            formulaRangeEntryActive: formulaRangeEntryActive,
            inlineEditorCommitsOnArrow: inlineEditorCommitsOnArrow,
            moveSelectionAfterEnter: _options.MoveSelectionAfterEnter,
            enterDirection: FormulaBarWpfInputAdapter.ToFormulaEditorEnterDirection(_options.AfterEnterDirection),
            systemKey: FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.SystemKey));

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
            if (TryApplyFormulaRangeSelection(referenceTarget, extendSelection: wpfModifiers.HasFlag(ModifierKeys.Shift)))
            {
                EnsureCellVisible(referenceTarget);
                e.Handled = true;
            }
            return;
        }

        if (intent.Action == ExcelEditKeyAction.CommitAndMove && intent.Target is { } rawNext)
        {
            var next = AdjustTargetPastMerge(_workbook.GetSheet(_currentSheetId), editNavigationCurrent, rawNext);
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

    /// <summary>
    /// When <paramref name="from"/> (the cell that was just being edited) belongs to a merged
    /// region and the plain +1/-1 step in <paramref name="next"/> still lands inside that same
    /// merge, advances past the merge's far edge in the direction of travel instead. Without this,
    /// Enter/Tab from inside a merge spanning more than one row/column recomputes "next" from the
    /// merge's own top-left anchor (SetActiveCell always collapses the selection to the merge's
    /// bounds), so a plain current+1 still falls inside the same merge and the cursor never
    /// advances -- unlike Excel, which always steps past the whole merged block.
    /// </summary>
    private static CellAddress AdjustTargetPastMerge(Sheet? sheet, CellAddress from, CellAddress next)
    {
        if (sheet is not { MergedRegions.Count: > 0 } || sheet.GetMergeRegion(from) is not { } merge)
            return next;

        if (!merge.Contains(next))
            return next;

        var row = next.Row;
        var col = next.Col;
        if (next.Row != from.Row)
        {
            row = next.Row > from.Row
                ? Math.Min(merge.End.Row + 1, CellAddress.MaxRow)
                : (merge.Start.Row > 1 ? merge.Start.Row - 1 : 1u);
        }
        else if (next.Col != from.Col)
        {
            col = next.Col > from.Col
                ? Math.Min(merge.End.Col + 1, CellAddress.MaxCol)
                : (merge.Start.Col > 1 ? merge.Start.Col - 1 : 1u);
        }

        return new CellAddress(next.Sheet, row, col);
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
        ApplyFormulaEditStatusBarPlan(FormulaEditInteractionPlanner.BuildEditStatusBarPlan(pointMode));
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
        if (e.Key == Key.F2 && e.KeyboardDevice.Modifiers == ModifierKeys.None)
        {
            var togglePlan = FormulaEditInteractionPlanner.BuildPointModeTogglePlan(FormulaBar.Text, _formulaRangeEntryMode);
            _formulaRangeEntryMode = togglePlan.PointMode;
            if (togglePlan.ClearReferenceSpan)
                ClearFormulaReferenceEntrySpan();
            ApplyFormulaEditStatusBarPlan(togglePlan.StatusBarPlan);
            e.Handled = togglePlan.Handled;
        }
        else if (ExcelEditKeyPlanner.ShouldCycleFormulaReference(
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
                var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(addr.Value);
                FormulaBar.Text = FormatFormulaBarText(cell, addr.Value);
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
            var formulaTextActive = FormulaEditInteractionPlanner.IsFormulaText(FormulaBar.Text);
            var formulaReferenceCurrent = formulaRangeEntryActive
                ? FormulaRangeEntryPlanner.GetKeyboardCursor(selectedRange, _selectionCursor)
                : selectedRange.Start;
            var editNavigationCurrent = _formulaEditCell ?? selectedRange.Start;
            int pageSize = Math.Max(1, (SheetGrid.Viewport?.RowMetrics.Count ?? 25) - 1);
            int colPageSize = Math.Max(1, (SheetGrid.Viewport?.ColMetrics.Count ?? 12) - 1);
            var wpfModifiers = e.KeyboardDevice.Modifiers;
            var modifiers = FormulaBarWpfInputAdapter.ToFormulaEditorModifiers(wpfModifiers);
            if (formulaRangeEntryActive &&
                FormulaRangeEntryPlanner.GetKeyboardSelectionTarget(
                    FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.Key),
                    FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.SystemKey),
                    modifiers,
                    formulaReferenceCurrent,
                    _workbook.GetSheet(_currentSheetId),
                    pageSize,
                    colPageSize) is { } formulaReferenceShortcutTarget)
            {
                if (TryApplyFormulaRangeSelection(
                        formulaReferenceShortcutTarget,
                        extendSelection: wpfModifiers.HasFlag(ModifierKeys.Shift)))
                {
                    EnsureCellVisible(formulaReferenceShortcutTarget);
                    e.Handled = true;
                }
                return;
            }

            var intent = ExcelEditKeyPlanner.GetIntent(
                FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.Key),
                modifiers,
                editNavigationCurrent,
                pageSize,
                allowFormulaBarNavigationKeys: !formulaTextActive,
                formulaRangeEntryActive: formulaRangeEntryActive,
                moveSelectionAfterEnter: _options.MoveSelectionAfterEnter,
                enterDirection: FormulaBarWpfInputAdapter.ToFormulaEditorEnterDirection(_options.AfterEnterDirection),
                systemKey: FormulaBarWpfInputAdapter.ToFormulaEditorKey(e.SystemKey));

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
                if (TryApplyFormulaRangeSelection(referenceTarget, extendSelection: wpfModifiers.HasFlag(ModifierKeys.Shift)))
                {
                    EnsureCellVisible(referenceTarget);
                    e.Handled = true;
                }
            }
            else if (intent.Action == ExcelEditKeyAction.CommitAndMove && intent.Target is { } rawTarget)
            {
                var target = AdjustTargetPastMerge(_workbook.GetSheet(_currentSheetId), editNavigationCurrent, rawTarget);
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
            var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(addr.Value);
            FormulaBar.Text = FormatFormulaBarText(cell, addr.Value);
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
        var names = _workbook.NamedRanges.Keys
            .Concat(_workbook.ScopedNamedRanges.Keys
                .Where(key => key.Sheet.Equals(_currentSheetId))
                .Select(key => key.Name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        CellAddressBox.ItemsSource = names;
    }

    private void CellAddressBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { IsDropDownOpen: true, SelectedItem: string name })
            return;

        CellAddressBox.Text = name;
        if (!TryParseNameBoxReferenceRange(name, out var selectedRange))
            return;

        NavigateNameBoxTo(selectedRange);
        FocusSheetGridIfNeeded();
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
        FocusSheetGridIfNeeded();
        e.Handled = true;
    }

    // Sheet-scope-aware Name Box reference resolution, matching formula evaluation's precedence
    // (Workbook.TryGetNamedRange(name, contextSheetId, ...): sheet-scoped names on the active sheet
    // take precedence over a same-named workbook-global name). Also resolves cross-sheet references
    // typed as SheetName!A1 (matching the Avalonia shell's TryParseCellAddressBoxReferenceRange).
    private bool TryParseNameBoxReferenceRange(string text, out GridRange range) =>
        WorkbookReferenceNavigator.TryParseReferenceRange(
            text,
            _currentSheetId,
            name => _workbook.Sheets.FirstOrDefault(sheet =>
                string.Equals(sheet.Name, name, StringComparison.OrdinalIgnoreCase))?.Id,
            _workbook.NamedRanges,
            name => _workbook.TryGetNamedRange(name, _currentSheetId, out var scoped) ? scoped : null,
            out range);

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
        var name = CellAddressBox.Text.Trim();
        if (_workbook.ValidateNamedRangeName(name) is not null)
            return false;
        if (SheetGrid.SelectedRange is not { } range)
            return false;

        var command = new DefineNamedRangeCommand(name, range);
        if (!TryExecuteCommand(command, UiText.Get("MainWindow_Content_DefineName")))
            return false;

        CellAddressBox.Text = name;
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
        if (!ExcelTextEditorPlanner.TryCycleFormulaReference(
                editor.Text, caretIndex, anchor, _options.UseR1C1ReferenceStyle, out var edit))
            return false;

        ApplyTextEdit(editor, edit);
        return true;
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

        if (!TryCreateCellFromEntryText(addr, text, out var newCell))
            return false;

        var committed = CommitPreparedEdits([(addr, newCell)], text, [addr], "Edit Cell");
        if (committed)
            ClearFormulaRangeEntryState();
        return committed;
    }

    private bool CommitEditAcrossSelection(bool fillFormulaEditCellOnly = false)
    {
        if (SheetGrid.SelectedRange is not { } range) return false;
        if (fillFormulaEditCellOnly && _formulaEditCell is { } formulaCell)
        {
            var formulaText = FormulaBar.Text;
            if (!TryCreateCellFromEntryText(formulaCell, formulaText, out var newCell))
                return false;

            var committed = CommitPreparedEdits([(formulaCell, newCell)], formulaText, [formulaCell], "Edit Cell");
            if (committed)
                ClearFormulaRangeEntryState();
            return committed;
        }

        var text = FormulaBar.Text;
        var edits = new List<(CellAddress Address, Cell NewCell)>();
        foreach (var address in range.AllCells())
        {
            if (!TryCreateCellFromEntryText(address, text, out var newCell))
                return false;

            edits.Add((address, newCell));
        }

        if (edits.Count == 0)
            return false;

        var selectionCommitted = CommitPreparedEdits(
            edits,
            text,
            edits.Select(edit => edit.Address).ToList(),
            "Edit Selection");
        if (selectionCommitted)
            ClearFormulaRangeEntryState();
        return selectionCommitted;
    }

    private bool TryCreateCellFromEntryText(CellAddress addr, string text, out Cell newCell)
    {
        newCell = CellEntryParser.CreateCell(text, addr, _options.UseR1C1ReferenceStyle);

        if (newCell.Value is { } value)
        {
            var sheet = _workbook.GetSheet(_currentSheetId);
            if (sheet != null)
            {
                var applicableRules = DataValidationService.GetApplicable(sheet, addr);
                DataValidation? violatingRule = null;
                string? violationMsg = null;
                foreach (var dv in applicableRules)
                {
                    var msg = DataValidationService.Validate(dv, value, sheet, addr, _workbook);
                    if (msg != null) { violatingRule = dv; violationMsg = msg; break; }
                }

                if (violationMsg != null && violatingRule != null)
                {
                    var dvRule = violatingRule;
                    var action = DataValidationService.GetInvalidEntryAction(dvRule);
                    if (action == DataValidationInvalidEntryAction.Block)
                    {
                        var icon = dvRule.AlertStyle switch
                        {
                            DvAlertStyle.Information => MessageBoxImage.Information,
                            DvAlertStyle.Warning => MessageBoxImage.Warning,
                            _ => MessageBoxImage.Error
                        };
                        ShowOwnedMessage(violationMsg, dvRule.ErrorTitle ?? "Validation Error",
                            MessageBoxButton.OK, icon);
                        RefreshValidationDropdown();
                        return false;
                    }

                    if (action == DataValidationInvalidEntryAction.AskToContinue)
                    {
                        var icon = dvRule.AlertStyle switch
                        {
                            DvAlertStyle.Information => MessageBoxImage.Information,
                            DvAlertStyle.Warning => MessageBoxImage.Warning,
                            _ => MessageBoxImage.Error
                        };
                        // Excel's three AskToContinue alert styles offer different button sets:
                        // Information is OK/Cancel (OK = accept, Cancel = stay in the cell to
                        // re-edit); Warning is Yes/No/Cancel (Yes = accept, No = stay in the cell
                        // to re-edit, Cancel = discard the entry and restore the prior value).
                        var buttons = dvRule.AlertStyle == DvAlertStyle.Information
                            ? MessageBoxButton.OKCancel
                            : MessageBoxButton.YesNoCancel;
                        var result = ShowOwnedMessage(violationMsg, dvRule.ErrorTitle ?? "Validation Error",
                            buttons, icon);
                        if (result == MessageBoxResult.Cancel && dvRule.AlertStyle == DvAlertStyle.Warning)
                        {
                            RefreshValidationDropdown();
                            RestoreFormulaBarToCommittedValue(addr);
                            return false;
                        }

                        if (result is MessageBoxResult.No or MessageBoxResult.Cancel)
                        {
                            RefreshValidationDropdown();
                            return false;
                        }
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Discards the in-progress edit and restores the formula bar to the cell's currently
    /// committed value/formula, mirroring what Escape does while editing. Used when a Warning-style
    /// data validation alert is dismissed with Cancel: Excel discards the invalid entry entirely
    /// rather than leaving it for the user to fix (that's what No does instead).
    /// </summary>
    private void RestoreFormulaBarToCommittedValue(CellAddress addr)
    {
        HideInlineEditor(commit: false);
        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(addr);
        FormulaBar.Text = FormatFormulaBarText(cell, addr);
        ClearFormulaRangeEntryState();
    }

    private bool CommitPreparedEdits(
        IReadOnlyList<(CellAddress Address, Cell NewCell)> edits,
        string text,
        IReadOnlyList<CellAddress> fallbackAffectedCells,
        string title)
    {
        if (!TryExecuteEditCells(edits, title, out var outcome))
            return false;

        var affectedCells = outcome.AffectedCells ?? fallbackAffectedCells;
        if (text.StartsWith("="))
        {
            // For now, we manually register dependencies because we haven't automated this in the command yet.
            try
            {
                foreach (var affected in affectedCells)
                {
                    var formulaA1 = _options.UseR1C1ReferenceStyle
                        ? FormulaReferenceStyleService.ToA1(text.Substring(1), affected)
                        : text.Substring(1);
                    var lexer = new Lexer("=" + formulaA1);
                    var parser = new Parser(lexer.Tokenize());
                    var ast = parser.Parse();
                    _recalcEngine.RegisterFormulaDependencies(affected, ast, affected.Sheet, _workbook);
                }
            }
            catch
            {
                // Formula syntax is invalid; clear stale dependencies so this cell
                // does not incorrectly depend on previously-referenced cells.
                foreach (var affected in affectedCells)
                    _recalcEngine.ClearFormulaDependencies(affected);
            }
        }
        else
        {
            foreach (var affected in affectedCells)
                _recalcEngine.ClearFormulaDependencies(affected);
        }

        RecalculateIfAutomatic(affectedCells);
        UpdateViewport();
        RefreshStatusBar();
        RefreshValidationDropdown();
        RefreshDvInputMessage();
        return true;
    }

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
                    NormalizeRibbonSurface(forceCompact: true);
                }

                return true;
            }
        }

        return false;
    }

}
