using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public partial class MainWindow
{
    private void SelectFormulaAuditCells(bool selectDependents, bool includeTransitive)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var activeCell = _selectionCursor ?? _selectionAnchor ?? range.Start;
        var matches = GetFormulaAuditMatches(activeCell, selectDependents, includeTransitive);
        var plan = FormulaAuditSelectionPlanner.Plan(_currentSheetId, matches);
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

    private IReadOnlyList<CellAddress> GetFormulaAuditMatches(
        CellAddress activeCell,
        bool selectDependents,
        bool includeTransitive)
    {
        if (!includeTransitive)
        {
            return selectDependents
                ? FormulaAuditingService.GetDirectDependents(_workbook, activeCell)
                : FormulaAuditingService.GetDirectPrecedents(_workbook, activeCell);
        }

        var arrows = selectDependents
            ? FormulaAuditingService.GetDependentTraceArrows(_workbook, activeCell)
            : FormulaAuditingService.GetPrecedentTraceArrows(_workbook, activeCell);
        return arrows
            .Select(arrow => selectDependents ? arrow.To : arrow.From)
            .ToList();
    }

    private void InsertFunctionBtn_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new InsertFunctionDialog();
        if (ShowOwnedDialog(dlg) != true || string.IsNullOrEmpty(dlg.SelectedFormula)) return;
        if (SheetGrid.SelectedRange is null) return;
        FormulaBar.Text = "=" + dlg.SelectedFormula;
        EnterEditMode();
    }

    private void DefineNameBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;
        var dialog = new NamedRangeDialog(_workbook, _commandBus, range)
        {
            Owner = this
        };
        dialog.ShowDialog();
        RefreshStatusBar();
    }

    private void CreateNamesFromSelectionBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        var dlg = new CreateNamesFromSelectionDialog { Owner = this };
        if (dlg.ShowDialog() != true) return;

        var command = new CreateNamedRangesFromSelectionCommand(
            range,
            dlg.UseTopRow,
            dlg.UseLeftColumn,
            dlg.UseBottomRow,
            dlg.UseRightColumn);
        var outcome = _commandBus.Execute(_workbook.Id, command);
        if (!outcome.Success)
            ShowCommandError(outcome, "Create from Selection");
    }

    private void UseInFormulaBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (_workbook.NamedRanges.Count == 0)
        {
            MessageBox.Show("No names are defined in this workbook.", "Use in Formula", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var menu = new ContextMenu();
        foreach (var name in _workbook.NamedRanges.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => InsertDefinedNameIntoFormula(name);
            menu.Items.Add(item);
        }

        MenuKeyTipAssigner.AssignUniqueKeyTips(menu.Items.OfType<MenuItem>());
        OpenRibbonContextMenu(btn, menu);
    }

    private void InsertDefinedNameIntoFormula(string name)
    {
        var result = FormulaInsertionService.InsertDefinedName(FormulaBar.Text, FormulaBar.CaretIndex, name);
        FormulaBar.Text = result.Text;
        FormulaBar.CaretIndex = result.CaretIndex;
        FormulaBar.Focus();
        EnterEditMode();
    }

    private void TracePrecedentsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        TracePrecedentsForCell(range.Start, "Trace Precedents");
    }

    private void TracePrecedentsForCell(CellAddress activeCell, string title)
    {
        var precedents = FormulaAuditingService.GetDirectPrecedents(_workbook, activeCell);
        if (precedents.Count == 0)
        {
            MessageBox.Show($"{FormulaAuditFormatter.FormatAddress(_workbook, activeCell)} has no direct precedents.",
                title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _formulaTraceArrows.Clear();
        _formulaTraceArrows.AddRange(FormulaAuditingService.GetPrecedentTraceArrows(_workbook, activeCell));
        UpdateViewport();
        MessageBox.Show(
            $"{FormulaAuditFormatter.FormatAddress(_workbook, activeCell)} directly references {precedents.Count} cell(s):\n{FormulaAuditFormatter.FormatAddresses(_workbook, precedents)}",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void TraceDependentsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range) return;

        var activeCell = range.Start;
        var dependents = FormulaAuditingService.GetDirectDependents(_workbook, activeCell);
        if (dependents.Count == 0)
        {
            MessageBox.Show($"{FormulaAuditFormatter.FormatAddress(_workbook, activeCell)} has no direct dependents.",
                "Trace Dependents", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _formulaTraceArrows.Clear();
        _formulaTraceArrows.AddRange(FormulaAuditingService.GetDependentTraceArrows(_workbook, activeCell));
        UpdateViewport();
        MessageBox.Show(
            $"{FormulaAuditFormatter.FormatAddress(_workbook, activeCell)} is directly referenced by {dependents.Count} cell(s):\n{FormulaAuditFormatter.FormatAddresses(_workbook, dependents)}",
            "Trace Dependents",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void RemoveArrowsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_formulaTraceArrows.Count == 0)
        {
            MessageBox.Show("No auditing arrows to remove.", "Remove Arrows", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _formulaTraceArrows.Clear();
        UpdateViewport();
    }

    private void ShowFormulasBtn_Click(object sender, RoutedEventArgs e)
    {
        var sheet = _workbook.GetSheet(_currentSheetId);
        if (sheet is null) return;

        var showFormulas = !sheet.ShowFormulas;
        if (!TryExecuteGroupedSheetCommand(
                "Show Formulas",
                sheetId => new SetWorksheetShowFormulasCommand(sheetId, showFormulas)))
            return;

        UpdateViewport();
    }

    private void ErrorCheckBtn_Click(object sender, RoutedEventArgs e)
    {
        RecalculateWorkbook();

        var issues = FormulaAuditingService.FindFormulaErrorIssues(_workbook, _currentSheetId);
        if (issues.Count == 0)
        {
            MessageBox.Show("No issues found.", "Error Checking", MessageBoxButton.OK, MessageBoxImage.Information);
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
            ShowOptionsDialog)
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
            MessageBox.Show("Select a cell that contains a formula.", "Evaluate Formula", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show(
                WatchWindowMessageFormatter.FormatAddResult(added, FormatRangeReference(range.Start, range.End)),
                "Watch Window",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        return added;
    }

    private void DeleteWatchBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SheetGrid.SelectedRange is not { } range)
            return;

        var removed = WatchWindowService.RemoveWatches(_workbook, range);
        _watchWindowDialog?.Refresh();
        MessageBox.Show(
            WatchWindowMessageFormatter.FormatRemoveResult(removed, FormatRangeReference(range.Start, range.End)),
            "Watch Window",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
        RecalculateWorkbook();
        UpdateViewport();
    }
    private void CalcSheetBtn_Click(object sender, RoutedEventArgs e)
    {
        _recalcEngine.RecalculateSheetFormulas(_workbook, _currentSheetId);
        UpdateViewport();
    }
    private void CalcOptionsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu is { } cm)
        { cm.PlacementTarget = btn; cm.IsOpen = true; }
    }
    private void CalcAutoMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (!TryExecuteCommand(new SetCalculationModeCommand(WorkbookCalculationMode.Automatic), "Calculation Options"))
            return;
        RecalculateWorkbook();
        UpdateViewport();
    }

    private void CalcManualMenuItem_Click(object sender, RoutedEventArgs e)
    {
        TryExecuteCommand(new SetCalculationModeCommand(WorkbookCalculationMode.Manual), "Calculation Options");
    }

    private void FormulaLogicalBtn_Click(object sender, RoutedEventArgs e)
    {
        OpenFormulaFunctionMenu(sender, ["IF", "IFS", "AND", "OR", "NOT", "IFERROR", "IFNA"]);
    }
    private void FormulaFinancialBtn_Click(object sender, RoutedEventArgs e) => OpenFormulaFunctionMenu(sender, ["PMT", "NPV", "IRR", "RATE", "PV", "FV"]);
    private void FormulaTextBtn_Click(object sender, RoutedEventArgs e)    => OpenFormulaFunctionMenu(sender, ["CONCAT", "LEFT", "RIGHT", "MID", "LEN", "TRIM", "TEXT", "UPPER", "LOWER", "PROPER", "SUBSTITUTE", "FIND", "SEARCH", "REPT", "VALUE"]);
    private void FormulaDateBtn_Click(object sender, RoutedEventArgs e)    => OpenFormulaFunctionMenu(sender, ["TODAY", "NOW", "DATE", "YEAR", "MONTH", "DAY", "HOUR", "MINUTE", "SECOND", "WEEKDAY", "EDATE", "DATEDIF"]);
    private void FormulaLookupBtn_Click(object sender, RoutedEventArgs e)  => OpenFormulaFunctionMenu(sender, ["VLOOKUP", "HLOOKUP", "XLOOKUP", "INDEX", "MATCH"]);
    private void FormulaMathBtn_Click(object sender, RoutedEventArgs e)    => OpenFormulaFunctionMenu(sender, ["SUM", "AVERAGE", "COUNT", "MIN", "MAX", "ROUND", "ABS", "SQRT", "MOD", "POWER", "INT", "CEILING", "FLOOR", "SIGN", "LOG", "LN", "EXP", "PI", "FACT", "RANDBETWEEN"]);
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
        FormulaBar.Text = $"={funcName}(";
        EnterEditMode();
        FormulaBar.CaretIndex = FormulaBar.Text.Length;
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
}
