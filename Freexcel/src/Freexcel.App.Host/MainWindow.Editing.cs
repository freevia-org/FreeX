using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Freexcel.Core.Calc;
using Freexcel.Core.Commands;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

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
        if (SheetGrid.SelectedRange?.Start is { } address)
        {
            var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(address);
            FormulaBar.Text = FormatFormulaBarText(cell, address);
        }

        FocusFormulaBarAtEnd();
    }

    private void FocusFormulaBarAtEnd()
    {
        FormulaBar.Focus();
        FormulaBar.CaretIndex = FormulaBar.Text.Length;
    }

    private void ShowInlineEditor(CellAddress addr)
    {
        HideValidationDropdown();
        var vp = SheetGrid.Viewport;
        if (vp == null) { FormulaBar.Focus(); return; }

        var rowMetric = vp.RowMetrics.FirstOrDefault(r => r.Row == addr.Row);
        var colMetric = vp.ColMetrics.FirstOrDefault(c => c.Col == addr.Col);
        if (rowMetric == null || colMetric == null) { FormulaBar.Focus(); return; }

        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(addr);
        var text = FormatFormulaBarText(cell, addr);
        _formulaEditCell = addr;
        ClearFormulaReferenceEntrySpan();

        if (_inlineEditor == null)
        {
            _inlineEditor = new System.Windows.Controls.TextBox
            {
                BorderThickness = new System.Windows.Thickness(2),
                BorderBrush     = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(33, 115, 70)),
                Padding         = new System.Windows.Thickness(1),
                FontFamily      = new System.Windows.Media.FontFamily("Calibri"),
                FontSize        = 15.0,
                Background      = System.Windows.Media.Brushes.White,
                AcceptsReturn   = false,
            };
            TextOptions.SetTextFormattingMode(_inlineEditor, TextFormattingMode.Display);
            TextOptions.SetTextRenderingMode(_inlineEditor, TextRenderingMode.ClearType);
            TextOptions.SetTextHintingMode(_inlineEditor, TextHintingMode.Fixed);
            _inlineEditor.PreviewKeyDown += InlineEditor_KeyDown;
            _inlineEditor.LostFocus  += InlineEditor_LostFocus;
            _inlineEditor.TextChanged += (_, _) => FormulaBar.Text = _inlineEditor.Text;
            EditOverlay.Children.Add(_inlineEditor);
        }

        // Cell metrics are in unzoomed coordinates; the EditOverlay is not transformed, so scale.
        double zoom = _zoomLevel;
        double cx = (colMetric.LeftOffset + SheetGrid.ActualRowHeaderWidth) * zoom;
        double cy = (rowMetric.TopOffset  + Freexcel.App.UI.GridView.ColHeaderHeight) * zoom;
        double cellW = colMetric.Width  * zoom;
        double cellH = rowMetric.Height * zoom;

        _inlineEditor.Text = text;
        System.Windows.Controls.Canvas.SetLeft(_inlineEditor, cx - 2);
        System.Windows.Controls.Canvas.SetTop(_inlineEditor,  cy - 2);
        _inlineEditor.Width  = cellW + 4;
        _inlineEditor.Height = Math.Max(cellH + 4, 20);
        _inlineEditor.Visibility  = Visibility.Visible;
        EditOverlay.IsHitTestVisible = true;
        _inlineEditor.Focus();
        _inlineEditor.SelectAll();
    }

    private void HideInlineEditor(bool commit)
    {
        if (_inlineEditor == null) return;
        _inlineEditor.Visibility = Visibility.Collapsed;
        EditOverlay.IsHitTestVisible = false;
        if (commit)
            FormulaBar.Text = _inlineEditor.Text;
    }

    private void RefreshValidationDropdown()
    {
        if (_inlineEditor?.IsVisible == true)
            return;

        if (SheetGrid.SelectedRange is not { } range ||
            _workbook.GetSheet(_currentSheetId) is not { } sheet ||
            TryGetCellOverlayRect(range.Start) is not { } rect)
        {
            HideValidationDropdown();
            return;
        }

        var rule = DataValidationService.GetApplicable(sheet, range.Start)
            .FirstOrDefault(dv => dv.Type == DvType.List && dv.ShowDropdown);
        if (rule is null)
        {
            HideValidationDropdown();
            return;
        }

        var items = DataValidationService.GetListItems(rule, sheet, _workbook);
        if (items.Count == 0)
        {
            HideValidationDropdown();
            return;
        }

        EnsureValidationDropdown();

        _suppressValidationDropdownCommit = true;
        _validationDropdown!.ItemsSource = items;
        var currentText = SpreadsheetDisplayFormatter.FormatCellValue(sheet.GetCell(range.Start)?.Value);
        _validationDropdown.SelectedItem = items.FirstOrDefault(item =>
            string.Equals(item, currentText, StringComparison.OrdinalIgnoreCase));
        _suppressValidationDropdownCommit = false;

        var width = Math.Max(18, Math.Min(rect.Width, 160));
        System.Windows.Controls.Canvas.SetLeft(_validationDropdown, rect.Right - width);
        System.Windows.Controls.Canvas.SetTop(_validationDropdown, rect.Top);
        _validationDropdown.Width = width;
        _validationDropdown.Height = Math.Max(18, rect.Height);
        _validationDropdown.Visibility = Visibility.Visible;
        EditOverlay.IsHitTestVisible = true;
    }

    private void EnsureValidationDropdown()
    {
        if (_validationDropdown is not null)
            return;

        _validationDropdown = new System.Windows.Controls.ComboBox
        {
            FontSize = 12,
            Padding = new System.Windows.Thickness(0),
            Background = System.Windows.Media.Brushes.White,
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(33, 115, 70)),
            BorderThickness = new System.Windows.Thickness(1),
            MaxDropDownHeight = 220,
            ToolTip = "Pick from list"
        };
        _validationDropdown.SelectionChanged += ValidationDropdown_SelectionChanged;
        EditOverlay.Children.Add(_validationDropdown);
    }

    private void HideValidationDropdown()
    {
        if (_validationDropdown is not null)
            _validationDropdown.Visibility = Visibility.Collapsed;

        if (_inlineEditor?.IsVisible != true)
            EditOverlay.IsHitTestVisible = false;
    }

    private void OpenActiveDropdown()
    {
        RefreshValidationDropdown();
        if (_validationDropdown?.Visibility == Visibility.Visible)
        {
            _validationDropdown.Focus();
            _validationDropdown.IsDropDownOpen = true;
            return;
        }

        OpenAutoFilterDropdownForActiveCell();
    }

    private void OpenAutoFilterDropdownForActiveCell()
    {
        if (SheetGrid.SelectedRange?.Start is not { } activeCell ||
            _workbook.GetSheet(_currentSheetId) is not { } sheet ||
            SelectionRangeService.GetCurrentRegion(sheet, activeCell) is not { } currentRegion ||
            !AutoFilterDropdownPlanner.TryPlan(currentRegion, activeCell, out var plan))
        {
            return;
        }

        var menuPlan = AutoFilterDropdownPlanner.CreateMenuPlan(sheet, plan);
        if (menuPlan.Entries.All(entry => entry.Kind != AutoFilterMenuEntryKind.ChecklistItem))
            return;

        var dialog = new AutoFilterDialog(menuPlan)
        {
            Owner = this
        };
        PositionAutoFilterDialogAtActiveCell(dialog, activeCell);

        if (dialog.ShowDialog() != true)
            return;

        if (!ApplyAutoFilterDialogResult(plan.Range, plan.FilterColumnOffset, dialog.Result, "AutoFilter"))
            return;
        UpdateViewport();
    }

    private void PositionAutoFilterDialogAtActiveCell(Window dialog, CellAddress activeCell)
    {
        if (TryGetCellOverlayRect(activeCell) is not { } rect)
            return;

        var screenPoint = SheetGrid.PointToScreen(new System.Windows.Point(rect.Left, rect.Bottom));
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
            screenPoint = target.TransformFromDevice.Transform(screenPoint);

        dialog.WindowStartupLocation = WindowStartupLocation.Manual;
        dialog.Left = screenPoint.X;
        dialog.Top = screenPoint.Y;
    }

    private Rect? TryGetCellOverlayRect(CellAddress addr)
    {
        var vp = SheetGrid.Viewport;
        if (vp is null)
            return null;

        var rowMetric = vp.RowMetrics.FirstOrDefault(r => r.Row == addr.Row);
        var colMetric = vp.ColMetrics.FirstOrDefault(c => c.Col == addr.Col);
        if (rowMetric is null || colMetric is null)
            return null;

        var left = colMetric.LeftOffset + SheetGrid.ActualRowHeaderWidth;
        var top = rowMetric.TopOffset + Freexcel.App.UI.GridView.ColHeaderHeight;
        return new Rect(left, top, colMetric.Width, rowMetric.Height);
    }

    private void ValidationDropdown_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressValidationDropdownCommit ||
            _validationDropdown?.SelectedItem is not string selected ||
            SheetGrid.SelectedRange is not { } range)
        {
            return;
        }

        FormulaBar.Text = selected;
        CommitEdit();
        SetActiveCell(range.Start);
    }

    private void InlineEditor_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F4 && _inlineEditor is not null)
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
            CancelCopyAndTransientModes();
            FocusSheetGridIfNeeded();
            e.Handled = true;
            return;
        }
        var selectedRange = SheetGrid.SelectedRange;
        if (selectedRange is null)
            return;
        var formulaRangeEntryActive = IsFormulaRangeEntryActive(_inlineEditor);
        var current = formulaRangeEntryActive
            ? FormulaRangeEntryPlanner.GetKeyboardCursor(selectedRange.Value, _selectionCursor)
            : selectedRange.Value.Start;

        var intent = ExcelEditKeyPlanner.GetIntent(
            e.Key,
            Keyboard.Modifiers,
            current,
            pageSize: Math.Max(1, (SheetGrid.Viewport?.RowMetrics.Count ?? 25) - 1),
            allowFormulaBarNavigationKeys: false,
            formulaRangeEntryActive: formulaRangeEntryActive);

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
            if (CommitEditAcrossSelection())
            {
                HideInlineEditor(commit: false);
                ClearFormulaRangeEntryState();
            }
            e.Handled = true;
            return;
        }

        if (intent.Action == ExcelEditKeyAction.SelectFormulaReference && intent.Target is { } referenceTarget)
        {
            if (TryApplyFormulaRangeSelection(referenceTarget, extendSelection: Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
            {
                EnsureCellVisible(referenceTarget);
                e.Handled = true;
            }
            return;
        }

        if (intent.Action == ExcelEditKeyAction.CommitAndMove && intent.Target is { } next)
        {
            var text = _inlineEditor!.Text;
            FormulaBar.Text = text;
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
        if (_inlineEditor?.IsVisible == true)
        {
            if (IsFormulaRangeEntryActive(_inlineEditor))
                return;

            FormulaBar.Text = _inlineEditor.Text;
            HideInlineEditor(commit: true);
            CommitEdit();
        }
    }

    private void FocusSheetGridIfNeeded()
    {
        if (!ReferenceEquals(Keyboard.FocusedElement, SheetGrid))
            SheetGrid.Focus();
    }

    private void CaptureFormulaEditCell()
    {
        if (_formulaEditCell is null && SheetGrid.SelectedRange?.Start is { } activeCell)
            _formulaEditCell = activeCell;
    }

    private void ClearFormulaRangeEntryState()
    {
        _formulaEditCell = null;
        _formulaRangeSelectionAnchor = null;
        ClearFormulaReferenceEntrySpan();
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

        return editor.Text.StartsWith("=", StringComparison.Ordinal);
    }

    private System.Windows.Controls.TextBox? GetFormulaRangeEntryEditor()
    {
        if (_inlineEditor?.IsVisible == true && IsFormulaRangeEntryActive(_inlineEditor))
            return _inlineEditor;

        return IsFormulaRangeEntryActive(FormulaBar) ? FormulaBar : null;
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
        editor.Focus();
        return true;
    }

    private void SetSelectionMode(ExcelSelectionMode mode)
    {
        _selectionMode = mode;
        if (mode != ExcelSelectionMode.Normal)
            _endMode = false;
        if (StatusStatsPanel is not null)
            StatusStatsPanel.Visibility = Visibility.Collapsed;
        if (StatusReadyText is null)
            return;

        StatusReadyText.Visibility = Visibility.Visible;
        StatusReadyText.Text = mode switch
        {
            ExcelSelectionMode.Extend => "Extend Selection",
            ExcelSelectionMode.Add => "Add to Selection",
            _ => "Ready"
        };
    }

    private void SetEndMode(bool enabled)
    {
        _endMode = enabled;
        if (enabled)
            _selectionMode = ExcelSelectionMode.Normal;
        if (StatusStatsPanel is not null)
            StatusStatsPanel.Visibility = Visibility.Collapsed;
        if (StatusReadyText is null)
            return;

        StatusReadyText.Visibility = Visibility.Visible;
        StatusReadyText.Text = enabled ? "End Mode" : "Ready";
    }

    private void FormulaBar_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F4)
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
            ClearFormulaRangeEntryState();
            ClearClipboardVisualState();
            SheetGrid.Focus();
            e.Handled = true;
        }
        else if (SheetGrid.SelectedRange is { } selectedRange)
        {
            var formulaRangeEntryActive = IsFormulaRangeEntryActive(FormulaBar);
            var current = formulaRangeEntryActive
                ? FormulaRangeEntryPlanner.GetKeyboardCursor(selectedRange, _selectionCursor)
                : selectedRange.Start;
            int pageSize = Math.Max(1, (SheetGrid.Viewport?.RowMetrics.Count ?? 25) - 1);
            var intent = ExcelEditKeyPlanner.GetIntent(
                e.Key,
                e.KeyboardDevice.Modifiers,
                current,
                pageSize,
                allowFormulaBarNavigationKeys: true,
                formulaRangeEntryActive: formulaRangeEntryActive);

            if (intent.Action == ExcelEditKeyAction.InsertLineBreak)
            {
                InsertLineBreak(FormulaBar);
                e.Handled = true;
            }
            else if (intent.Action == ExcelEditKeyAction.CommitSelection)
            {
                if (CommitEditAcrossSelection())
                    ClearFormulaRangeEntryState();
                e.Handled = true;
            }
            else if (intent.Action == ExcelEditKeyAction.SelectFormulaReference && intent.Target is { } referenceTarget)
            {
                if (TryApplyFormulaRangeSelection(referenceTarget, extendSelection: e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift)))
                {
                    EnsureCellVisible(referenceTarget);
                    e.Handled = true;
                }
            }
            else if (intent.Action == ExcelEditKeyAction.CommitAndMove && intent.Target is { } target)
            {
                if (CommitEdit())
                {
                    ClearFormulaRangeEntryState();
                    SetActiveCell(target);
                    EnsureCellVisible(target);
                }

                e.Handled = true;
            }
        }
    }

    private static bool TryCycleFormulaReference(System.Windows.Controls.TextBox editor)
    {
        var caretIndex = editor.SelectionLength > 0 ? editor.SelectionStart : editor.CaretIndex;
        if (!ExcelTextEditorPlanner.TryCycleFormulaReference(editor.Text, caretIndex, out var edit))
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

    private bool CommitEditAcrossSelection()
    {
        if (SheetGrid.SelectedRange is not { } range) return false;
        if (_formulaEditCell is { } formulaCell)
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

        return CommitPreparedEdits(
            edits,
            text,
            edits.Select(edit => edit.Address).ToList(),
            "Edit Selection");
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
                        MessageBox.Show(violationMsg, dvRule.ErrorTitle ?? "Validation Error",
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
                        var buttons = dvRule.AlertStyle == DvAlertStyle.Information
                            ? MessageBoxButton.OKCancel
                            : MessageBoxButton.YesNo;
                        var result = MessageBox.Show(violationMsg, dvRule.ErrorTitle ?? "Validation Error",
                            buttons, icon);
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
        return true;
    }

    private void UpdateTitleBar()
    {
        var groupSuffix = IsWorkbookGrouped() ? " [Group]" : "";
        var displayName = $"{_workbook.Name}{groupSuffix} - Freexcel";
        WorkbookNameText.Text = displayName;
        this.Title = displayName;
    }

    private bool IsWorkbookGrouped()
        => SheetTabListPlanner.IsWorkbookGrouped(_workbook, _currentSheetId, _groupedSheetIds);

    // ── Start screen ─────────────────────────────────────────────────────────

    private bool? ShowOwnedDialog(Window dialog)
    {
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
        return MessageBox.Show(this, messageBoxText, caption, button, icon);
    }

    private bool TryHandleTopLevelRibbonKeyTip(string keyTip)
    {
        return RibbonTopLevelKeyTipRouter.Resolve(keyTip) switch
        {
            { Kind: RibbonTopLevelKeyTipActionKind.BackstageFile } => OpenFileBackstageFromKeyTip(),
            { Kind: RibbonTopLevelKeyTipActionKind.RibbonTab, RibbonTabHeader: { } header } => SelectRibbonTabByHeader(header),
            _ => false
        };
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
                RibbonTabs.SelectedItem = item;
                RibbonTabs.UpdateLayout();
                NormalizeRibbonSurface(forceCompact: true);
                return true;
            }
        }

        return false;
    }

}
