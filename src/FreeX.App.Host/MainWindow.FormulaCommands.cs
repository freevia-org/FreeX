using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FreeX.App.Presentation;
using FreeX.App.Presentation.Calculation;
using FreeX.App.Presentation.DefinedNames;
using FreeX.App.Presentation.Dialogs;
using FreeX.App.Presentation.FormulaAuditing;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private void SelectFormulaAuditCells(bool selectDependents, bool includeTransitive)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var activeCell = _selectionCursor ?? _selectionAnchor ?? range.Start;
        var plan = FormulaAuditSelectionPlanner.Plan(
            _workbook,
            activeCell,
            selectDependents,
            includeTransitive);
        if (plan is null)
        {
            StatusReadyText.Visibility = Visibility.Visible;
            var depth = includeTransitive ? "traceable" : "direct";
            StatusReadyText.Text = selectDependents
                ? $"No {depth} dependents"
                : $"No {depth} precedents";
            return;
        }

        var targetMatches = plan.Matches;
        _currentSheetId = plan.TargetSheetId;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        var compressedRanges = SelectionRangeService.CompressAddresses(targetMatches);
        _selectionAnchor = targetMatches[0];
        _selectionCursor = targetMatches[0];
        SheetGrid.SelectedRange = new GridRange(targetMatches[0], targetMatches[0]);
        SheetGrid.SelectedRanges = compressedRanges;
        CellAddressBox.Text = compressedRanges.Count == 1
            ? FormatRangeReference(compressedRanges[0].Start, compressedRanges[0].End)
            : $"{targetMatches.Count} cells";
        FormulaBar.Text = FormatFormulaBarText(_workbook.GetSheet(_currentSheetId)?.GetCell(targetMatches[0]), targetMatches[0]);
        EnsureCellVisible(targetMatches[0]);
        UpdateViewport();
        RefreshSheetTabs();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void InsertFunctionBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new InsertFunctionDialog();
        if (ShowOwnedDialog(dlg) != true || dlg.SelectedFunction is not { } function) return;
        if (SheetGrid.SelectedRange is null) return;
        InsertFormulaFunction(function);
    }

    private void FormulaRecentlyUsedBtn_Click(object sender, RoutedEventArgs e) => InsertFunctionBtn_Click(sender, e);

    private void DefineNameBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        var definedNames = new DefinedNamesSession(_workbook, _currentSheetId);
        NameDefinitionDialog? dialog = null;
        dialog = new NameDefinitionDialog(
            new NameDefinitionDialogResult("", DefinedNameScope.WorkbookLabel, "", FormatWorkbookRange(range)),
            DefinedNameUiPolicy.BuildScopeOptions(definedNames.ScopeChoices),
            request => ApplyNameDefinitionSelection(dialog, request),
            isValidRange: rangeText => definedNames.TryParseRange(rangeText, out _),
            validateName: _workbook.ValidateNamedRangeName)
        {
            Owner = this
        };

        if (ShowOwnedDialog(dialog) != true)
            return;

        var draft = DefinedNameUiPolicy.CreateDraft(
            dialog.Result.Name,
            definedNames.GetScope(dialog.Result.ScopeSheetId),
            dialog.Result.RefersTo,
            dialog.Result.Comment);
        var plan = definedNames.PlanSave(draft);
        if (!plan.Validation.Name.IsValid)
        {
            ShowOwnedMessage(
                plan.Validation.Name.Error == DefinedNameError.Duplicate
                    ? UiText.Get("NameDefinition_NameConflictsMessage")
                    : DefinedNameValidationMessages.Describe(plan.Validation.Name.Error).Resolve(UiText.Get),
                UiText.Get("NameDefinition_NewNameTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!plan.Validation.RefersTo.IsValid || plan.Command is null)
        {
            ShowOwnedMessage(
                UiText.Get("NameDefinition_InvalidRangeFormatMessage"),
                UiText.Get("NameDefinition_NewNameTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!TryExecuteCommand(
                plan.Command,
                UiText.Get("MainWindow_Content_DefineName")))
            return;

        RefreshStatusBar();
    }

    private void CreateNamesFromSelectionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        var sheet = _session.ActiveSheet;
        var detected = CreateNamesFromSelectionPlanner.DetectOptions(range, sheet.GetValue);
        var dlg = new CreateNamesFromSelectionDialog(detected) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var command = new CreateNamedRangesFromSelectionCommand(
            range,
            dlg.UseTopRow,
            dlg.UseLeftColumn,
            dlg.UseBottomRow,
            dlg.UseRightColumn);
        TryExecuteCommand(command, "Create from Selection");
    }

    private void UseInFormulaBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        var plan = DefinedNameUiPolicy.PlanUseInFormula(
            _workbook,
            FormatWorkbookRange,
            DefinedNameUiProfile.Wpf);
        if (!plan.HasItems)
        {
            _messageService.ShowInfo(UiText.Get("MainWindowMessage_UseInFormulaNoNames"), UiText.Get("MainWindowMessage_UseInFormulaTitle"));
            return;
        }

        var menu = new ContextMenu();
        foreach (var descriptor in plan.Items)
        {
            var item = new MenuItem { Header = descriptor.Name };
            item.Click += (_, _) => InsertDefinedNameIntoFormula(descriptor.Name);
            menu.Items.Add(item);
        }

        MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());
        OpenRibbonContextMenu(btn, menu);
    }

    private void OpenPasteNamesDialog()
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var title = UiText.Get("PasteNames_Title");
        var items = PasteNamesPlanner.BuildItems(_workbook, FormatWorkbookRange);
        if (items.Count == 0)
        {
            _messageService.ShowInfo(UiText.Get("PasteNames_NoNamesMessage"), title);
            return;
        }

        var dialog = new PasteNamesDialog(items)
        {
            Owner = this
        };

        if (ShowOwnedDialog(dialog) != true)
            return;

        if (dialog.Result.Action == PasteNamesDialogAction.InsertName &&
            !string.IsNullOrWhiteSpace(dialog.Result.Name))
        {
            InsertDefinedNameIntoFormula(dialog.Result.Name);
            return;
        }

        if (dialog.Result.Action != PasteNamesDialogAction.PasteList)
            return;

        if (!PasteNamesPlanner.TryBuildPasteListEdits(range.Start, items, out var edits, out var error))
        {
            _messageService.ShowWarning(DescribePasteNamesListError(error), title);
            return;
        }

        if (!TryExecuteEditCells(edits, title))
            return;

        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private static string DescribePasteNamesListError(PasteNamesListError error) =>
        UiText.Get(DefinedNameUiPolicy.GetPasteNamesListErrorResourceKey(error, DefinedNameUiProfile.Wpf));

    private void InsertDefinedNameIntoFormula(string name)
    {
        var formulaText = FormulaBar.Text;
        var caretIndex = FormulaBar.CaretIndex;
        if (ShouldSeedDefinedNameFormula(formulaText))
        {
            formulaText = "";
            caretIndex = 0;
        }

        var result = FormulaInsertionService.InsertDefinedName(formulaText, caretIndex, name);
        BeginFormulaBarFormulaEdit(result.Text, result.CaretIndex);
    }

    private bool ShouldSeedDefinedNameFormula(string formulaText)
    {
        if (_formulaEditCell is not null ||
            _inlineEditor?.IsVisible == true ||
            _formulaRangeEditingSession.IsFormulaText(formulaText) ||
            SheetGrid.SelectedRange?.Start is not { } address)
        {
            return false;
        }

        var cell = _workbook.GetSheet(_currentSheetId)?.GetCell(address);
        return string.Equals(formulaText, FormatFormulaBarText(cell, address), StringComparison.Ordinal);
    }

    private void TracePrecedentsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        TracePrecedentsForCell(range.Start, "Trace Precedents");
    }

    private void TracePrecedentsForCell(CellAddress activeCell, string title)
    {
        var arrows = FormulaTraceArrowPlanner.GetNextPrecedentTraceArrows(_workbook, activeCell, _formulaTraceArrows);
        if (arrows.Count == 0)
        {
            var message = FormulaAuditingService.GetDirectPrecedents(_workbook, activeCell).Count == 0
                ? $"{FormulaAuditFormatter.FormatAddress(_workbook, activeCell)} has no direct precedents."
                : $"{FormulaAuditFormatter.FormatAddress(_workbook, activeCell)} has no more precedent cells to trace.";
            _messageService.ShowInfo(message, title);
            return;
        }

        _formulaTraceArrows.AddRange(arrows);
        UpdateViewport();
    }

    private void TraceDependentsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        var activeCell = range.Start;
        var arrows = FormulaTraceArrowPlanner.GetNextDependentTraceArrows(_workbook, activeCell, _formulaTraceArrows);
        if (arrows.Count == 0)
        {
            var message = FormulaAuditingService.GetDirectDependents(_workbook, activeCell).Count == 0
                ? $"{FormulaAuditFormatter.FormatAddress(_workbook, activeCell)} has no direct dependents."
                : $"{FormulaAuditFormatter.FormatAddress(_workbook, activeCell)} has no more dependent cells to trace.";
            _messageService.ShowInfo(message, "Trace Dependents");
            return;
        }

        _formulaTraceArrows.AddRange(arrows);
        UpdateViewport();
    }

    private void RemoveArrowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        {
            OpenRibbonContextMenu(btn, cm);
            return;
        }

        RemoveTraceArrows(kind: null, "Remove Arrows");
    }

    private void RemoveAllArrowsMenuItem_Click(object sender, RoutedEventArgs e) =>
        RemoveTraceArrows(kind: null, "Remove Arrows");

    private void RemovePrecedentArrowsMenuItem_Click(object sender, RoutedEventArgs e) =>
        RemoveTraceArrows(FormulaTraceArrowKind.Precedent, "Remove Precedent Arrows");

    private void RemoveDependentArrowsMenuItem_Click(object sender, RoutedEventArgs e) =>
        RemoveTraceArrows(FormulaTraceArrowKind.Dependent, "Remove Dependent Arrows");

    private void RemoveTraceArrows(FormulaTraceArrowKind? kind, string title)
    {
        if (_formulaTraceArrows.Count == 0)
        {
            _messageService.ShowInfo(UiText.Get("MainWindowMessage_TraceArrowsNoneToRemove"), title);
            return;
        }

        var removed = kind is null
            ? _formulaTraceArrows.Count
            : _formulaTraceArrows.RemoveAll(arrow => arrow.Kind == kind.Value);

        if (kind is null)
            _formulaTraceArrows.Clear();

        if (removed == 0)
        {
            _messageService.ShowInfo(UiText.Get("MainWindowMessage_TraceArrowsNoMatchingToRemove"), title);
            return;
        }

        UpdateViewport();
    }

    /// <summary>
    /// Discards every Trace Precedents/Dependents arrow without prompting. Row/column insert and
    /// delete rewrite formulas and move cells (RowColumnShiftHelpers.RewriteAllFormulas /
    /// ShiftAddressBearingRows*/Columns*) but never touch <see cref="_formulaTraceArrows"/> itself, so
    /// a stale arrow set silently keeps pointing at pre-edit grid coordinates that, after the shift,
    /// belong to different cells than the formula's actual (now-moved) precedents/dependents — a
    /// wrong, misleading audit arrow. Excel clears trace arrows outright on a structural edit rather
    /// than trying to re-derive them, since the frontier the arrows were expanded from may no longer
    /// even resolve to a formula cell; this mirrors that behavior.
    /// </summary>
    /// <remarks>
    /// Invoked from every row/column insert/delete call site in MainWindow.CellsCommands.cs
    /// (InsertRowsCommand/DeleteRowsCommand/InsertColumnsCommand/DeleteColumnsCommand executions).
    /// The Avalonia shell has the matching invalidation in
    /// src/FreeX.App.Avalonia/MainWindow.RibbonMenuWires.cs (InsertSheetRows/InsertSheetColumns/
    /// DeleteSheetRows/DeleteSheetColumns), which reproduces the identical stale-arrow bug there.
    /// </remarks>
    private void ClearFormulaTraceArrowsAfterStructuralEdit()
    {
        if (_formulaTraceArrows.Count == 0)
            return;

        _formulaTraceArrows.Clear();
        UpdateViewport();
    }

    private void ShowFormulasBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        // Toggle against THIS window's own effective Show Formulas state (R89-show-formulas-
        // per-window-1), not the shared Sheet field directly -- a sibling "New Window" may have
        // already flipped the shared field without this window ever adopting it, and reading the
        // raw sheet here would silently re-toggle from that sibling's value instead of this
        // window's own last-known one (mirrors the Freeze Panes/Split per-window pattern).
        var showFormulas = !GetEffectiveViewState(sheet).ShowFormulas;
        var targetSheetIds = CurrentGroupedEditSheetIds();
        if (!TryExecuteGroupedWorksheetViewState(
                targetSheetIds,
                () => _session.SetShowFormulas(showFormulas),
                "Show Formulas"))
            return;

        UpdateViewport();
    }

    private void ErrorCheckBtn_Click(object sender, RoutedEventArgs e)
    {
        RecalculateWorkbook();

        var issues = FormulaAuditingService.FindFormulaErrorIssues(_workbook, _currentSheetId, _session.CyclicCells);
        if (issues.Count == 0)
        {
            _messageService.ShowInfo(UiText.Get("MainWindowMessage_ErrorCheckingNoIssues"), UiText.Get("MainWindowMessage_ErrorCheckingTitle"));
            return;
        }

        var dialog = new ErrorCheckingDialog(
            issues,
            address =>
            {
                NavigateToCell(address);
                RefreshSheetTabs();
                UpdateViewport();
                RefreshStatusBar();
            },
            issue =>
            {
                if (!TryExecuteCommand(
                        new SetFormulaErrorIgnoredCommand(issue.SheetId, issue.Address, ignored: true),
                        "Ignore Error"))
                    return false;

                UpdateViewport();
                RefreshStatusBar();
                return true;
            },
            issue =>
            {
                NavigateToCell(issue.Address);
                RefreshSheetTabs();
                UpdateViewport();
                RefreshStatusBar();
                TracePrecedentsForCell(issue.Address, "Trace Error");
            },
            showCalculationSteps: issue =>
            {
                NavigateToCell(issue.Address);
                RefreshSheetTabs();
                UpdateViewport();
                RefreshStatusBar();

                var summary = FormulaEvaluationSummaryService.GetSummary(_workbook, issue.Address);
                if (summary is null)
                {
                    _messageService.ShowInfo(UiText.Get("MainWindowMessage_EvaluateFormulaSelectCell"), UiText.Get("MainWindowMessage_EvaluateFormulaTitle"));
                    return;
                }

                var evaluationDialog = new EvaluateFormulaDialog(summary)
                {
                    Owner = this
                };
                evaluationDialog.ShowDialog();
            },
            openOptions: () => ShowOptionsDialog(OptionsDialogInitialSection.FormulaErrorChecking))
        {
            Owner = this
        };
        dialog.Show();
    }

    private void EvaluateFormulaBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        RecalculateWorkbook();
        var summary = FormulaEvaluationSummaryService.GetSummary(_workbook, range.Start);
        if (summary is null)
        {
            _messageService.ShowInfo(UiText.Get("MainWindowMessage_EvaluateFormulaSelectCell"), UiText.Get("MainWindowMessage_EvaluateFormulaTitle"));
            return;
        }

        var dialog = new EvaluateFormulaDialog(summary)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void AddWatchBtn_Click(object sender, RoutedEventArgs e)
    {
        AddWatchFromSelection(showMessage: true);
    }

    private int AddWatchFromSelection(bool showMessage)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return 0;

        var added = WatchWindowService.AddWatches(_workbook, range);
        _watchWindowDialog?.Refresh();
        if (showMessage)
        {
            _messageService.ShowInfo(
                WatchWindowMessageFormatter.FormatAddResult(added, FormatRangeReference(range.Start, range.End)),
                "Watch Window");
        }
        return added;
    }

    private void DeleteWatchBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var removed = WatchWindowService.RemoveWatches(_workbook, range);
        _watchWindowDialog?.Refresh();
        _messageService.ShowInfo(
            WatchWindowMessageFormatter.FormatRemoveResult(removed, FormatRangeReference(range.Start, range.End)),
            "Watch Window");
    }

    private void WatchWindowBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_watchWindowDialog is null)
        {
            _watchWindowDialog = new WatchWindowDialog(
                () =>
                {
                    RecalculateWorkbook();
                    return WatchWindowService.GetEntries(_workbook);
                },
                () => AddWatchFromSelection(showMessage: false),
                () => SheetGrid.SelectedRange is { } range
                    ? FormatRangeReference(range.Start, range.End)
                    : "",
                address =>
                {
                    NavigateToCell(address);
                    RefreshSheetTabs();
                    UpdateViewport();
                    RefreshStatusBar();
                },
                address =>
                {
                    WatchWindowService.RemoveWatch(_workbook, address);
                    UpdateViewport();
                })
            {
                Owner = this
            };
            _watchWindowDialog.Closed += (_, _) => _watchWindowDialog = null;
            _watchWindowDialog.Show();
        }
        else
        {
            _watchWindowDialog.Refresh();
            if (_watchWindowDialog.WindowState == WindowState.Minimized)
                _watchWindowDialog.WindowState = WindowState.Normal;
            _watchWindowDialog.Activate();
        }
    }

    private void CalcNowBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteCalculationAction(CalculationCommandAction.CalculateNow);
    }
    private void CalcFullBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteCalculationAction(CalculationCommandAction.CalculateFull);
    }
    private void CalcSheetBtn_Click(object sender, RoutedEventArgs e)
    {
        ExecuteCalculationAction(CalculationCommandAction.CalculateActiveSheet);
    }
    private void CalcOptionsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
            OpenRibbonContextMenu(btn, cm);
    }

    private void RefreshCalculationModeRibbonStates()
    {
        _ribbonState.SetState(
            FreeXRibbonCommandIds.FormulasCalculationAutomatic,
            CalculationCommandPolicy.ModeCommandState(
                _workbook.CalculationMode,
                WorkbookCalculationMode.Automatic));
        _ribbonState.SetState(
            FreeXRibbonCommandIds.FormulasCalculationAutomaticExceptDataTables,
            CalculationCommandPolicy.ModeCommandState(
                _workbook.CalculationMode,
                WorkbookCalculationMode.AutomaticExceptDataTables));
        _ribbonState.SetState(
            FreeXRibbonCommandIds.FormulasCalculationManual,
            CalculationCommandPolicy.ModeCommandState(
                _workbook.CalculationMode,
                WorkbookCalculationMode.Manual));
    }

    private void CalcAutoMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyCalculationModeChange(WorkbookCalculationMode.Automatic);

    private void CalcAutoExceptDataTablesMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyCalculationModeChange(WorkbookCalculationMode.AutomaticExceptDataTables);

    private void CalcManualMenuItem_Click(object sender, RoutedEventArgs e) =>
        ApplyCalculationModeChange(WorkbookCalculationMode.Manual);

    private void ApplyCalculationModeChange(WorkbookCalculationMode requestedMode)
    {
        ApplyCalculationWorkflowOutcome(CalculationWorkflow.ChangeMode(requestedMode));
    }

    private CalculationWorkflowSession CalculationWorkflow =>
        new(
            _workbook,
            (command, label) =>
            {
                var success = TryExecuteCommand(command, label, out var outcome);
                return new CalculationCommandExecutionResult(
                    success,
                    outcome.ErrorMessage,
                    outcome.IsNoOp);
            },
            new CalculationRecalculationOperations(
                RecalculateDirtyCells,
                RecalculateWorkbook,
                () =>
                {
                    _session.RecalculateActiveSheet();
                    InvalidateNavigationCaches();
                }));

    private void ExecuteCalculationAction(CalculationCommandAction action)
    {
        ApplyCalculationWorkflowOutcome(CalculationWorkflow.Execute(action));
    }

    private void ApplyCalculationWorkflowOutcome(CalculationWorkflowOutcome outcome)
    {
        if (outcome.Success)
            ApplyCalculationRefresh(outcome.RefreshPolicy);
    }

    private void ApplyCalculationRefresh(CalculationStateRefreshPolicy policy)
    {
        if (policy.HasFlag(CalculationStateRefreshPolicy.CommandSurface))
            RefreshToolbar();
        if (policy.HasFlag(CalculationStateRefreshPolicy.FormulaResults))
            UpdateViewport();
    }

    private void FormulaLogicalBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenFormulaFunctionMenu(sender, ["IF", "IFS", "AND", "OR", "NOT", "IFERROR", "IFNA"]);
    }
    private void FormulaFinancialBtn_Click(object sender, RoutedEventArgs e) => OpenFormulaFunctionMenu(sender, ["PMT", "NPV", "IRR", "RATE", "PV", "FV"]);
    private void FormulaTextBtn_Click(object sender, RoutedEventArgs e)    => OpenFormulaFunctionMenu(sender, ["CONCAT", "LEFT", "RIGHT", "MID", "LEN", "TRIM", "TEXT", "UPPER", "LOWER", "PROPER", "SUBSTITUTE", "FIND", "SEARCH", "REPT", "VALUE"]);
    private void FormulaDateBtn_Click(object sender, RoutedEventArgs e)    => OpenFormulaFunctionMenu(sender, ["TODAY", "NOW", "DATE", "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND", "WEEKDAY", "EDATE", "DATEDIF"]);
    private void FormulaLookupBtn_Click(object sender, RoutedEventArgs e)  => OpenFormulaFunctionMenu(sender, ["VLOOKUP", "HLOOKUP", "XLOOKUP", "INDEX", "MATCH"]);
    private void FormulaMathBtn_Click(object sender, RoutedEventArgs e)    => OpenFormulaFunctionMenu(sender, ["SUM", "AVERAGE", "COUNT", "COUNTA", "MIN", "MAX", "ROUND", "ABS", "SQRT", "MOD", "POWER", "INT", "CEILING", "FLOOR", "SIGN", "LOG", "LN", "EXP", "PI", "FACT", "RANDBETWEEN"]);
    private void FormulaMoreBtn_Click(object sender, RoutedEventArgs e)    => InsertFunctionBtn_Click(sender, e);

    private void OpenFormulaFunctionMenu(object sender, IReadOnlyList<string> functionNames)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        var menu = new ContextMenu();
        foreach (var functionName in functionNames)
        {
            var item = new MenuItem { Header = functionName };
            item.Click += (_, _) => InsertFormulaFunction(functionName);
            menu.Items.Add(item);
        }

        MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());
        OpenRibbonContextMenu(btn, menu);
    }

    private void InsertFormulaFunction(string funcName)
    {
        if (SheetGrid.SelectedRange is null) return;
        var normalizedName = funcName.Trim().ToUpperInvariant();
        InsertFunctionCatalogEntry? function = null;
        foreach (var entry in InsertFunctionCatalogPlanner.BuildCatalog())
        {
            if (!string.Equals(entry.Name, normalizedName, StringComparison.OrdinalIgnoreCase))
                continue;

            function = entry;
            break;
        }

        if (function is null)
        {
            InsertRawFormulaFunction(normalizedName);
            return;
        }

        InsertFormulaFunction(function);
    }

    private void InsertFormulaFunction(InsertFunctionCatalogEntry function)
    {
        if (SheetGrid.SelectedRange is null) return;

        FunctionArgumentsDialog? argumentsDialog = null;
        argumentsDialog = new FunctionArgumentsDialog(
            function,
            request => ApplyFunctionArgumentRangeSelection(argumentsDialog, request)) { Owner = this };
        if (ShowOwnedDialog(argumentsDialog) != true || string.IsNullOrWhiteSpace(argumentsDialog.ResultFormula))
            return;

        BeginFormulaBarFormulaEdit("=" + argumentsDialog.ResultFormula);
    }

    private void ApplyFunctionArgumentRangeSelection(
        FunctionArgumentsDialog? dialog,
        FunctionArgumentRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange => dialog.ApplyRangeSelection(request.ArgumentIndex, FormatWorkbookRange(selectedRange)));
    }

    private void InsertRawFormulaFunction(string funcName)
    {
        BeginFormulaBarFormulaEdit($"={funcName}(");
    }

    private void BeginFormulaBarFormulaEdit(string text, int? caretIndex = null)
    {
        CaptureFormulaEditCell();
        _formulaRangeEditingSession.SetPointModeForFormulaText(text);
        ClearFormulaReferenceEntrySpan();
        FormulaBar.Text = text;
        if (caretIndex is { } requestedCaretIndex)
        {
            FocusFormulaBar();
            FormulaBar.CaretIndex = Math.Clamp(requestedCaretIndex, 0, FormulaBar.Text.Length);
            SetFormulaEditStatusBarMode(pointMode: false);
        }
        else
        {
            FocusFormulaBarAtEnd();
        }

        RefreshFormulaReferenceHighlights();
    }

    private void Formula_IF_Click(object sender, RoutedEventArgs e)      => InsertFormulaFunction("IF");
    private void Formula_AND_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("AND");
    private void Formula_OR_Click(object sender, RoutedEventArgs e)      => InsertFormulaFunction("OR");
    private void Formula_NOT_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("NOT");
    private void Formula_IFS_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("IFS");
    private void Formula_CONCAT_Click(object sender, RoutedEventArgs e)  => InsertFormulaFunction("CONCAT");
    private void Formula_LEFT_Click(object sender, RoutedEventArgs e)    => InsertFormulaFunction("LEFT");
    private void Formula_RIGHT_Click(object sender, RoutedEventArgs e)   => InsertFormulaFunction("RIGHT");
    private void Formula_MID_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("MID");
    private void Formula_LEN_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("LEN");
    private void Formula_TRIM_Click(object sender, RoutedEventArgs e)    => InsertFormulaFunction("TRIM");
    private void Formula_TEXT_Click(object sender, RoutedEventArgs e)    => InsertFormulaFunction("TEXT");
    private void Formula_TODAY_Click(object sender, RoutedEventArgs e)   => InsertFormulaFunction("TODAY");
    private void Formula_NOW_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("NOW");
    private void Formula_DATE_Click(object sender, RoutedEventArgs e)    => InsertFormulaFunction("DATE");
    private void Formula_YEAR_Click(object sender, RoutedEventArgs e)    => InsertFormulaFunction("YEAR");
    private void Formula_MONTH_Click(object sender, RoutedEventArgs e)   => InsertFormulaFunction("MONTH");
    private void Formula_DAY_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("DAY");
    private void Formula_VLOOKUP_Click(object sender, RoutedEventArgs e) => InsertFormulaFunction("VLOOKUP");
    private void Formula_HLOOKUP_Click(object sender, RoutedEventArgs e) => InsertFormulaFunction("HLOOKUP");
    private void Formula_INDEX_Click(object sender, RoutedEventArgs e)   => InsertFormulaFunction("INDEX");
    private void Formula_MATCH_Click(object sender, RoutedEventArgs e)   => InsertFormulaFunction("MATCH");
    private void Formula_XLOOKUP_Click(object sender, RoutedEventArgs e) => InsertFormulaFunction("XLOOKUP");
    private void Formula_SUM_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("SUM");
    private void Formula_ROUND_Click(object sender, RoutedEventArgs e)   => InsertFormulaFunction("ROUND");
    private void Formula_ABS_Click(object sender, RoutedEventArgs e)     => InsertFormulaFunction("ABS");
    private void Formula_SQRT_Click(object sender, RoutedEventArgs e)    => InsertFormulaFunction("SQRT");

    private void ApplyNamedRangeSelection(
        NamedRangeDialog? dialog,
        NamedRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange => dialog.ApplyRangeSelection(request.Target, FormatWorkbookRange(selectedRange)));
    }

    private void ApplyNameDefinitionSelection(
        NameDefinitionDialog? dialog,
        NamedRangeSelectionRequest request)
    {
        if (dialog is null)
            return;

        BeginDialogRangeSelection(
            dialog,
            request.CollapseDialog,
            selectedRange => dialog.ApplyRangeSelection(FormatWorkbookRange(selectedRange)));
    }
}
