using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using FreeX.App.Services;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private async Task CaptureDataWhatIfWorkflowsTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteDataWhatIfWorkflowsTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1220;
        Height = 780;
        await Task.Delay(700);

        var context = EnsureDataWhatIfWorkflowsTourContext();
        var captures = new List<DataWhatIfWorkflowsTourManifestCapture>();
        var workflows = new List<DataWhatIfWorkflowsTourManifestWorkflow>();

        try
        {
            captures.Add(await CaptureDataWhatIfWorkflowsWindowStateAsync(
                outputDir,
                context,
                "UI-CMD-DATA-006",
                "seeded-formula-grid",
                "Worksheet grid",
                "freex_data_what_if_workflows_seeded_formula_grid",
                context.SeededOverviewRange,
                "Seeded What-If worksheet grid with Goal Seek, Scenario Manager, and one/two-variable Data Table formula targets before submissions.",
                "Seeded workbook state before command execution."));

            captures.Add(await CaptureGoalSeekDialogForDataWhatIfTourAsync(outputDir, context));

            var goalSeekResult = _session.FindGoalSeekSolution(new GoalSeekRequest(
                context.GoalSeekSetCell,
                TargetValue: 180.0,
                context.GoalSeekChangingCell));
            if (!goalSeekResult.Converged)
                throw new InvalidOperationException("Data What-If workflows tour expected Goal Seek to converge for the seeded linear formula.");
            captures.Add(await CaptureGoalSeekStatusDialogForDataWhatIfTourAsync(outputDir, goalSeekResult));
            ExecuteDataWhatIfWorkflowsTourCommand(
                new GoalSeekCommand(context.GoalSeekChangingCell, goalSeekResult.FoundValue),
                "Goal Seek",
                out var goalSeekOutcome);
            captures.Add(await CaptureDataWhatIfWorkflowsWindowStateAsync(
                outputDir,
                context,
                "UI-CMD-DATA-006",
                "goal-seek-applied-result",
                "Worksheet grid",
                "freex_data_what_if_workflows_goal_seek_result",
                context.GoalSeekRange,
                $"Worksheet grid after GoalSeekService converged and GoalSeekCommand applied changing-cell value {goalSeekResult.FoundValue.ToString("0.########", CultureInfo.InvariantCulture)}.",
                "GoalSeekService.Seek(...); TryExecuteCommand(new GoalSeekCommand(changingCell, result.FoundValue), \"Goal Seek\")"));
            workflows.Add(CreateCapturedDataWhatIfWorkflow(
                "Goal Seek submitted result",
                ["UI-CAT-DATA-002", "UI-CAT-DIALOG-001A", "UI-CMD-DATA-006"],
                "GoalSeekDialog default capture, GoalSeekService.Seek, GoalSeekStatusDialog, GoalSeekCommand",
                "goal-seek-dialog-defaults",
                "goal-seek-status-success",
                "goal-seek-applied-result"));

            ExecuteDataWhatIfWorkflowsTourCommand(
                new SaveScenarioCommand(
                    "Base Plan",
                    [
                        new ScenarioCellValue(context.ScenarioUnitsCell, new NumberValue(10)),
                        new ScenarioCellValue(context.ScenarioPriceCell, new NumberValue(20))
                    ],
                    "Seeded baseline scenario",
                    hidden: false,
                    locked: true),
                "Scenario Manager");
            ExecuteDataWhatIfWorkflowsTourCommand(
                new SaveScenarioCommand(
                    "Upside Plan",
                    [
                        new ScenarioCellValue(context.ScenarioUnitsCell, new NumberValue(14)),
                        new ScenarioCellValue(context.ScenarioPriceCell, new NumberValue(23))
                    ],
                    "Higher units and price",
                    hidden: false,
                    locked: true),
                "Scenario Manager");
            ExecuteDataWhatIfWorkflowsTourCommand(
                new SaveScenarioCommand(
                    "Lean Plan",
                    [
                        new ScenarioCellValue(context.ScenarioUnitsCell, new NumberValue(8)),
                        new ScenarioCellValue(context.ScenarioPriceCell, new NumberValue(18))
                    ],
                    "Conservative volume",
                    hidden: false,
                    locked: true),
                "Scenario Manager");
            captures.Add(await CaptureScenarioManagerDialogForDataWhatIfTourAsync(outputDir, context));

            ExecuteDataWhatIfWorkflowsTourCommand(
                new ApplyScenarioCommand("Upside Plan"),
                "Scenario Manager",
                out var scenarioOutcome);
            captures.Add(await CaptureDataWhatIfWorkflowsWindowStateAsync(
                outputDir,
                context,
                "UI-CMD-DATA-006",
                "scenario-manager-show-upside-result",
                "Worksheet grid",
                "freex_data_what_if_workflows_scenario_show_result",
                context.ScenarioRange,
                "Worksheet grid after ApplyScenarioCommand showed the saved Upside Plan scenario and recalculated the scenario result formula.",
                "TryExecuteCommand(new ApplyScenarioCommand(\"Upside Plan\"), \"Scenario Manager\"); RecalculateIfAutomatic(outcome.AffectedCells)"));

            ExecuteDataWhatIfWorkflowsTourCommand(
                new ScenarioSummaryReportCommand(
                    [context.ScenarioResultCell],
                    (workbook, changedCells) =>
                    {
                        if (workbook.CalculationMode == WorkbookCalculationMode.Automatic)
                            _session.RecalculateChangedCellsAlways(changedCells);
                    }),
                "Scenario Manager");
            var reportSheet = _workbook.Sheets.LastOrDefault(sheet => string.Equals(sheet.Name, "Scenario Summary", StringComparison.Ordinal));
            if (reportSheet is null)
                throw new InvalidOperationException("Data What-If workflows tour expected Scenario Summary report sheet.");
            _currentSheetId = reportSheet.Id;
            _groupedSheetIds.Clear();
            _groupedSheetIds.Add(_currentSheetId);
            RefreshSheetTabs();
            captures.Add(await CaptureDataWhatIfWorkflowsWindowStateAsync(
                outputDir,
                context with { Sheet = reportSheet },
                "UI-CMD-DATA-006",
                "scenario-summary-report-result",
                "Worksheet grid",
                "freex_data_what_if_workflows_scenario_summary_report",
                new GridRange(new CellAddress(reportSheet.Id, 1, 1), new CellAddress(reportSheet.Id, 12, 4)),
                "Scenario Summary worksheet created by ScenarioSummaryReportCommand, including changing cells and result-cell values for the seeded scenarios.",
                "TryExecuteCommand(new ScenarioSummaryReportCommand([scenarioResultCell], recalcCallback), \"Scenario Manager\")"));
            workflows.Add(CreateCapturedDataWhatIfWorkflow(
                "Scenario Manager save, show, and summary",
                ["UI-CAT-DATA-002", "UI-CAT-DIALOG-001A", "UI-CMD-DATA-006"],
                "SaveScenarioCommand, ScenarioManagerDialog default capture, ApplyScenarioCommand, ScenarioSummaryReportCommand",
                "scenario-manager-dialog-with-scenarios",
                "scenario-manager-show-upside-result",
                "scenario-summary-report-result"));

            _currentSheetId = context.Sheet.Id;
            _groupedSheetIds.Clear();
            _groupedSheetIds.Add(_currentSheetId);
            RefreshSheetTabs();
            captures.Add(await CaptureDataTableDialogForDataWhatIfTourAsync(outputDir, context));

            var oneVariablePlanResult = DataTablePlanner.CreatePlan(
                context.Sheet,
                context.OneVariableDataTableRange,
                rowInputCellText: null,
                columnInputCellText: context.OneVariableInputCell.ToA1());
            if (oneVariablePlanResult.Plan is not { } oneVariablePlan)
                throw new InvalidOperationException($"Data What-If workflows tour one-variable Data Table plan failed: {oneVariablePlanResult.StatusText}");
            ExecuteDataWhatIfWorkflowsTourCommand(oneVariablePlan.CreateCommand(), "Data Table", out var oneVariableOutcome);
            captures.Add(await CaptureDataWhatIfWorkflowsWindowStateAsync(
                outputDir,
                context,
                "UI-CMD-DATA-006",
                "data-table-one-variable-result",
                "Worksheet grid",
                "freex_data_what_if_workflows_data_table_one_variable_result",
                context.OneVariableDataTableRange,
                "Worksheet grid after DataTablePlanner produced a one-variable plan and OneVariableDataTableCommand filled recalculated output formulas.",
                "DataTablePlanner.CreatePlan(... columnInputCellText); TryExecuteCommand(plan.CreateCommand(), \"Data Table\"); RecalculateIfAutomatic(outcome.AffectedCells)"));

            var twoVariablePlanResult = DataTablePlanner.CreatePlan(
                context.Sheet,
                context.TwoVariableDataTableRange,
                rowInputCellText: context.TwoVariableRowInputCell.ToA1(),
                columnInputCellText: context.TwoVariableColumnInputCell.ToA1());
            if (twoVariablePlanResult.Plan is not { } twoVariablePlan)
                throw new InvalidOperationException($"Data What-If workflows tour two-variable Data Table plan failed: {twoVariablePlanResult.StatusText}");
            ExecuteDataWhatIfWorkflowsTourCommand(twoVariablePlan.CreateCommand(), "Data Table", out var twoVariableOutcome);
            captures.Add(await CaptureDataWhatIfWorkflowsWindowStateAsync(
                outputDir,
                context,
                "UI-CMD-DATA-006",
                "data-table-two-variable-result",
                "Worksheet grid",
                "freex_data_what_if_workflows_data_table_two_variable_result",
                context.TwoVariableDataTableRange,
                "Worksheet grid after DataTablePlanner produced a two-variable plan and TwoVariableDataTableCommand filled recalculated output formulas.",
                "DataTablePlanner.CreatePlan(... rowInputCellText, columnInputCellText); TryExecuteCommand(plan.CreateCommand(), \"Data Table\"); RecalculateIfAutomatic(outcome.AffectedCells)"));
            workflows.Add(CreateCapturedDataWhatIfWorkflow(
                "Data Table one/two-variable submitted results",
                ["UI-CAT-DATA-002", "UI-CAT-DIALOG-001A", "UI-CMD-DATA-006"],
                "DataTableDialog default capture, DataTablePlanner.CreatePlan, OneVariableDataTableCommand, TwoVariableDataTableCommand",
                "data-table-dialog-defaults",
                "data-table-one-variable-result",
                "data-table-two-variable-result"));

            workflows.Add(new DataWhatIfWorkflowsTourManifestWorkflow(
                Name: "Foreground What-If range picker, access keys, and modal button commit",
                CatalogRows: ["UI-CAT-DATA-002", "UI-CAT-DIALOG-001A", "UI-CMD-DATA-006"],
                PlannedStatus: "planned",
                ActualStatus: "planned-but-blocked",
                CommandRoute: "Goal Seek/Data Table range-picker collapse, Scenario Manager side-button UI, Data > What-If keytips Alt,A,W,G/S/D",
                LimitationNote: "This deterministic slice does not synthesize foreground mouse, keytip, access-key, Enter/Escape, or collapsed range-picker input; those paths remain foreground-only evidence work.",
                CaptureKeys: []));

            ValidateDataWhatIfWorkflowsTourEvidence(outputDir, captures);
            await WriteDataWhatIfWorkflowsTourManifestAsync(outputDir, context, captures, workflows);
        }
        catch
        {
            DeleteDataWhatIfWorkflowsTourEvidence(outputDir);
            throw;
        }
    }

    private DataWhatIfWorkflowsTourContext EnsureDataWhatIfWorkflowsTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Data What-If workflows tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        ClearDataWhatIfWorkflowsSheetArea(sheet);
        _workbook.Scenarios.Clear();

        var goalSeekUnits = new CellAddress(sheet.Id, 2, 2);
        var goalSeekRevenue = new CellAddress(sheet.Id, 4, 2);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Goal Seek"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Units"));
        sheet.SetCell(goalSeekUnits, new NumberValue(6));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Price"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Revenue"));
        sheet.SetFormula(goalSeekRevenue, "B2*B3");

        var scenarioUnits = new CellAddress(sheet.Id, 2, 5);
        var scenarioPrice = new CellAddress(sheet.Id, 3, 5);
        var scenarioRevenue = new CellAddress(sheet.Id, 4, 5);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 4), new TextValue("Scenario Manager"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 4), new TextValue("Units"));
        sheet.SetCell(scenarioUnits, new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 4), new TextValue("Price"));
        sheet.SetCell(scenarioPrice, new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("Revenue"));
        sheet.SetFormula(scenarioRevenue, "E2*E3");

        var oneVariableInput = new CellAddress(sheet.Id, 8, 2);
        var oneVariableFormula = new CellAddress(sheet.Id, 10, 2);
        sheet.SetCell(new CellAddress(sheet.Id, 7, 1), new TextValue("One-variable Data Table"));
        sheet.SetCell(new CellAddress(sheet.Id, 8, 1), new TextValue("Input cell"));
        sheet.SetCell(oneVariableInput, new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 9, 1), new TextValue("Trial"));
        sheet.SetCell(new CellAddress(sheet.Id, 9, 2), new TextValue("Markup"));
        sheet.SetFormula(oneVariableFormula, "B8*1.25");
        sheet.SetCell(new CellAddress(sheet.Id, 11, 1), new NumberValue(80));
        sheet.SetCell(new CellAddress(sheet.Id, 12, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 13, 1), new NumberValue(120));

        var twoVariableRowInput = new CellAddress(sheet.Id, 16, 2);
        var twoVariableColumnInput = new CellAddress(sheet.Id, 17, 2);
        var twoVariableFormula = new CellAddress(sheet.Id, 10, 4);
        sheet.SetCell(new CellAddress(sheet.Id, 15, 1), new TextValue("Two-variable Data Table"));
        sheet.SetCell(new CellAddress(sheet.Id, 16, 1), new TextValue("Row input"));
        sheet.SetCell(twoVariableRowInput, new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 17, 1), new TextValue("Col input"));
        sheet.SetCell(twoVariableColumnInput, new NumberValue(5));
        sheet.SetFormula(twoVariableFormula, "B16*B17");
        sheet.SetCell(new CellAddress(sheet.Id, 10, 5), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 6), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 7), new NumberValue(12));
        sheet.SetCell(new CellAddress(sheet.Id, 11, 4), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 12, 4), new NumberValue(8));
        sheet.SetCell(new CellAddress(sheet.Id, 13, 4), new NumberValue(11));

        RebuildDependenciesAndCalculate();

        var overviewRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 17, 7));
        SetSelectionRange(overviewRange, goalSeekRevenue);
        EnsureCellVisible(overviewRange.Start);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();

        return new DataWhatIfWorkflowsTourContext(
            sheet,
            overviewRange,
            new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
            goalSeekRevenue,
            goalSeekUnits,
            new GridRange(new CellAddress(sheet.Id, 1, 4), new CellAddress(sheet.Id, 4, 5)),
            scenarioUnits,
            scenarioPrice,
            scenarioRevenue,
            new GridRange(new CellAddress(sheet.Id, 10, 1), new CellAddress(sheet.Id, 13, 2)),
            oneVariableInput,
            new GridRange(new CellAddress(sheet.Id, 10, 4), new CellAddress(sheet.Id, 13, 7)),
            twoVariableRowInput,
            twoVariableColumnInput);
    }

    private static void ClearDataWhatIfWorkflowsSheetArea(Sheet sheet)
    {
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 40, 10));
        foreach (var address in range.AllCells())
            sheet.ClearCell(address);

        sheet.AutoFilter = null;
        sheet.FilterHiddenRows.Clear();
        sheet.HiddenRows.Clear();
        sheet.GroupHiddenRows.Clear();
        sheet.RowOutlineLevels.Clear();
        sheet.DataValidations.Clear();
    }

    private async Task<DataWhatIfWorkflowsTourManifestCapture> CaptureGoalSeekDialogForDataWhatIfTourAsync(
        string outputDir,
        DataWhatIfWorkflowsTourContext context)
    {
        SetSelectionRange(context.GoalSeekRange, context.GoalSeekSetCell);
        await WaitForDataWhatIfWorkflowsWindowAsync(context.GoalSeekRange.Start);

        var dialog = new GoalSeekDialog(context.Sheet.Id, context.GoalSeekSetCell) { Owner = this };
        try
        {
            dialog.Show();
            SetTextBoxValue(dialog, "GoalSeekToValueBox", "180");
            SetTextBoxValue(dialog, "GoalSeekChangingCellBox", context.GoalSeekChangingCell.ToA1());
            dialog.UpdateLayout();
            await Task.Delay(350);
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_data_what_if_workflows_goal_seek_dialog");
        }
        finally
        {
            dialog.Close();
        }

        return CreateDataWhatIfDialogCapture(
            "goal-seek-dialog-defaults",
            "goal-seek-dialog",
            "freex_data_what_if_workflows_goal_seek_dialog",
            "Goal Seek dialog is open with the selected formula cell prefilled, target value entered, changing cell entered, and OK as the default action.",
            "new GoalSeekDialog(sheet.Id, selectedSetCell); dialog fields populated without submitting foreground input.");
    }

    private async Task<DataWhatIfWorkflowsTourManifestCapture> CaptureGoalSeekStatusDialogForDataWhatIfTourAsync(
        string outputDir,
        GoalSeekResult result)
    {
        var dialog = new GoalSeekStatusDialog(result, targetValue: 180.0) { Owner = this };
        try
        {
            dialog.Show();
            dialog.UpdateLayout();
            await Task.Delay(350);
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_data_what_if_workflows_goal_seek_status_success");
        }
        finally
        {
            dialog.Close();
        }

        return CreateDataWhatIfDialogCapture(
            "goal-seek-status-success",
            "goal-seek-status-dialog",
            "freex_data_what_if_workflows_goal_seek_status_success",
            $"Goal Seek Status dialog reports convergence with Keep Result as the default action; found value is {result.FoundValue.ToString("0.########", CultureInfo.InvariantCulture)}.",
            "new GoalSeekStatusDialog(goalSeekResult, targetValue: 180.0)");
    }

    private async Task<DataWhatIfWorkflowsTourManifestCapture> CaptureScenarioManagerDialogForDataWhatIfTourAsync(
        string outputDir,
        DataWhatIfWorkflowsTourContext context)
    {
        SetSelectionRange(context.ScenarioRange, context.ScenarioUnitsCell);
        await WaitForDataWhatIfWorkflowsWindowAsync(context.ScenarioRange.Start);

        var dialog = new ScenarioManagerDialog(_workbook, _currentSheetId, ResolveSheetIdByName) { Owner = this };
        try
        {
            dialog.Show();
            dialog.UpdateLayout();
            await Task.Delay(350);
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_data_what_if_workflows_scenario_manager_dialog");
        }
        finally
        {
            dialog.Close();
        }

        return CreateDataWhatIfDialogCapture(
            "scenario-manager-dialog-with-scenarios",
            "scenario-manager-dialog",
            "freex_data_what_if_workflows_scenario_manager_dialog",
            "Scenario Manager dialog lists the seeded Base, Upside, and Lean scenarios with Show enabled/default for the selected scenario and Add/Edit/Delete/Summary side actions visible.",
            "new ScenarioManagerDialog(workbook, currentSheetId, ResolveSheetIdByName) after SaveScenarioCommand seeds.");
    }

    private async Task<DataWhatIfWorkflowsTourManifestCapture> CaptureDataTableDialogForDataWhatIfTourAsync(
        string outputDir,
        DataWhatIfWorkflowsTourContext context)
    {
        SetSelectionRange(context.OneVariableDataTableRange, context.OneVariableDataTableRange.Start);
        await WaitForDataWhatIfWorkflowsWindowAsync(context.OneVariableDataTableRange.Start);

        var dialog = new DataTableDialog(_currentSheetId, context.OneVariableDataTableRange) { Owner = this };
        try
        {
            dialog.Show();
            SetTextBoxValue(dialog, "DataTableColumnInputCellBox", context.OneVariableInputCell.ToA1());
            dialog.UpdateLayout();
            await Task.Delay(350);
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_data_what_if_workflows_data_table_dialog");
        }
        finally
        {
            dialog.Close();
        }

        return CreateDataWhatIfDialogCapture(
            "data-table-dialog-defaults",
            "data-table-dialog",
            "freex_data_what_if_workflows_data_table_dialog",
            "Data Table dialog is open for the selected one-variable table range with row and column input editors plus range-picker buttons; column input is populated for the deterministic submission.",
            "new DataTableDialog(currentSheetId, selectedTableRange); column input populated without submitting foreground input.");
    }

    private static void SetTextBoxValue(Window window, string automationId, string value)
    {
        var textBox = FindDescendantByAutomationId<TextBox>(window, automationId)
            ?? throw new InvalidOperationException($"Data What-If workflows tour could not find text box '{automationId}'.");
        textBox.Text = value;
        textBox.SelectAll();
    }

    private void ExecuteDataWhatIfWorkflowsTourCommand(IWorkbookCommand command, string title)
    {
        if (!TryExecuteCommand(command, title, out var outcome))
            throw new InvalidOperationException($"Data What-If workflows tour command '{title}' failed: {outcome.ErrorMessage}");
    }

    private void ExecuteDataWhatIfWorkflowsTourCommand(IWorkbookCommand command, string title, out CommandOutcome outcome)
    {
        if (!TryExecuteCommand(command, title, out outcome))
            throw new InvalidOperationException($"Data What-If workflows tour command '{title}' failed: {outcome.ErrorMessage}");
    }

    private async Task WaitForDataWhatIfWorkflowsWindowAsync(CellAddress visibleCell)
    {
        EnsureCellVisible(visibleCell);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();
        await Task.Delay(300);
        await WaitForRibbonScreenshotRenderPassAsync();
    }

    private async Task<DataWhatIfWorkflowsTourManifestCapture> CaptureDataWhatIfWorkflowsWindowStateAsync(
        string outputDir,
        DataWhatIfWorkflowsTourContext context,
        string catalogRow,
        string state,
        string surface,
        string fileName,
        GridRange focusRange,
        string evidenceSummary,
        string commandRoute)
    {
        SetSelectionRange(focusRange, focusRange.Start);
        await WaitForDataWhatIfWorkflowsWindowAsync(focusRange.Start);
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);

        return new DataWhatIfWorkflowsTourManifestCapture(
            CaptureKey: $"data-what-if-workflows:{state}",
            PairKey: $"interactive:data-what-if-workflows:{state}",
            CatalogRow: catalogRow,
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "RenderTargetBitmap-window-full",
            CommandRoute: commandRoute,
            EvidenceSummary: evidenceSummary,
            SelectedRange: focusRange.ToString(),
            VisibleRows: DescribeDataWhatIfVisibleRows(context.Sheet, focusRange),
            ScenarioCount: _workbook.Scenarios.Count,
            CaptureLogicalWidth: ActualWidth,
            CaptureLogicalHeight: Math.Min(ActualHeight, 760));
    }

    private static DataWhatIfWorkflowsTourManifestCapture CreateDataWhatIfDialogCapture(
        string state,
        string surface,
        string fileName,
        string evidenceSummary,
        string commandRoute) =>
        new(
            CaptureKey: $"data-what-if-workflows:{state}",
            PairKey: $"interactive:data-what-if-workflows:{state}",
            CatalogRow: "UI-CMD-DATA-006",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "RenderTargetBitmap-dialog-window",
            CommandRoute: commandRoute,
            EvidenceSummary: evidenceSummary,
            SelectedRange: "",
            VisibleRows: [],
            ScenarioCount: 0,
            CaptureLogicalWidth: 0,
            CaptureLogicalHeight: 0);

    private static IReadOnlyList<string> DescribeDataWhatIfVisibleRows(Sheet sheet, GridRange range)
    {
        var rows = new List<string>();
        var endRow = Math.Min(range.End.Row, range.Start.Row + 13);
        for (var row = range.Start.Row; row <= endRow; row++)
        {
            var values = new List<string>();
            var endCol = Math.Min(range.End.Col, range.Start.Col + 6);
            for (var col = range.Start.Col; col <= endCol; col++)
            {
                var cell = sheet.GetCell(row, col);
                values.Add(FormatDataWhatIfCellValue(cell));
            }

            rows.Add($"{row}:{string.Join("|", values)}");
        }

        return rows;
    }

    private static string FormatDataWhatIfCellValue(Cell? cell)
    {
        if (!string.IsNullOrWhiteSpace(cell?.FormulaText))
            return $"={cell.FormulaText}:{FormatDataWhatIfScalar(cell.Value)}";

        return FormatDataWhatIfScalar(cell?.Value);
    }

    private static string FormatDataWhatIfScalar(ScalarValue? value) => value switch
    {
        null or BlankValue => "",
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString("0.########", CultureInfo.InvariantCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        DateTimeValue date => date.Value.ToString("0.########", CultureInfo.InvariantCulture),
        ErrorValue error => error.Code,
        _ => value.ToString() ?? ""
    };

    private static DataWhatIfWorkflowsTourManifestWorkflow CreateCapturedDataWhatIfWorkflow(
        string name,
        IReadOnlyList<string> catalogRows,
        string commandRoute,
        params string[] captureKeys) =>
        new(
            Name: name,
            CatalogRows: catalogRows,
            PlannedStatus: "planned",
            ActualStatus: "captured",
            CommandRoute: commandRoute,
            LimitationNote: "Captured through deterministic in-process dialog construction, supported command/service execution, and RenderTargetBitmap; no global mouse, keytip, access-key, native dialog, or UI Automation Invoke input is synthesized.",
            CaptureKeys: captureKeys.Select(key => $"data-what-if-workflows:{key}").ToArray());

    private static void DeleteDataWhatIfWorkflowsTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_data_what_if_workflows_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, DataWhatIfWorkflowsTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateDataWhatIfWorkflowsTourEvidence(
        string outputDir,
        IReadOnlyList<DataWhatIfWorkflowsTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Data What-If workflows tour did not create planned capture '{capture.OutputFileName}'.");
        }
    }

    private static async Task WriteDataWhatIfWorkflowsTourManifestAsync(
        string outputDir,
        DataWhatIfWorkflowsTourContext context,
        IReadOnlyList<DataWhatIfWorkflowsTourManifestCapture> captures,
        IReadOnlyList<DataWhatIfWorkflowsTourManifestWorkflow> workflows)
    {
        var actualWorkflowCount = workflows.Count(workflow => string.Equals(workflow.ActualStatus, "captured", StringComparison.Ordinal));
        var manifest = new DataWhatIfWorkflowsTourManifest(
            Tool: "FREEX_DATA_WHAT_IF_WORKFLOWS_TOUR",
            EvidenceFamily: "data-what-if-workflows",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "data-what-if-workflows:submitted-command-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_data_what_if_workflows_<Workflow>_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md#UI-CAT-DATA-002",
            CatalogRows: ["UI-CAT-DATA-002", "UI-CAT-DIALOG-001A", "UI-CMD-DATA-006"],
            SheetName: context.Sheet.Name,
            GoalSeekRange: context.GoalSeekRange.ToString(),
            ScenarioRange: context.ScenarioRange.ToString(),
            OneVariableDataTableRange: context.OneVariableDataTableRange.ToString(),
            TwoVariableDataTableRange: context.TwoVariableDataTableRange.ToString(),
            CaptureStatus: "partial-with-blocked-planned-items",
            CaptureMethod: "RenderTargetBitmap-window-and-dialogs-with-real-what-if-commands",
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures after supported command/service execution; no global mouse, keyboard, keytip, native dialog, access-key, or UI Automation Invoke input is used."
                    : "Window captures abort unless the expected FreeX WPF surface owns foreground focus immediately before render and file write."),
            PlannedWorkflowCount: workflows.Count,
            ActualWorkflowCount: actualWorkflowCount,
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Captures: captures,
            Workflows: workflows,
            CoveredStates:
            [
                "Seeded What-If formula grid",
                "Goal Seek dialog defaults, success status, and applied result",
                "Scenario Manager seeded dialog, Show result, and Scenario Summary report",
                "Data Table dialog defaults and one/two-variable submitted results"
            ],
            Limitations:
            [
                "This slice submits currently supported FreeX command/service paths where they are deterministic in process.",
                "Goal Seek status is captured before applying the Keep Result path; the application of that result is proven separately through GoalSeekCommand and the worksheet grid result.",
                "Scenario Manager add/edit side-button foreground submission is represented by SaveScenarioCommand seeding plus the production ScenarioManagerDialog state; physical button activation remains open.",
                "Data Table one/two-variable results are captured through DataTablePlanner and DataTable commands; collapsed range-picker and Enter/Escape dialog submission remain open.",
                "The tour does not synthesize foreground mouse, keytip, access-key, dropdown keyboard, collapsed range-picker, or UI Automation Invoke workflows.",
                "Forecast Sheet is not included in this bounded slice, which focuses on product-supported Goal Seek, Scenario Manager, and Data Table evidence.",
                "No Microsoft Excel counterpart screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, DataWhatIfWorkflowsTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.DataWhatIfWorkflowsTourManifest);
    }

    private sealed record DataWhatIfWorkflowsTourContext(
        Sheet Sheet,
        GridRange SeededOverviewRange,
        GridRange GoalSeekRange,
        CellAddress GoalSeekSetCell,
        CellAddress GoalSeekChangingCell,
        GridRange ScenarioRange,
        CellAddress ScenarioUnitsCell,
        CellAddress ScenarioPriceCell,
        CellAddress ScenarioResultCell,
        GridRange OneVariableDataTableRange,
        CellAddress OneVariableInputCell,
        GridRange TwoVariableDataTableRange,
        CellAddress TwoVariableRowInputCell,
        CellAddress TwoVariableColumnInputCell);

    private sealed record DataWhatIfWorkflowsTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogRows,
        string SheetName,
        string GoalSeekRange,
        string ScenarioRange,
        string OneVariableDataTableRange,
        string TwoVariableDataTableRange,
        string CaptureStatus,
        string CaptureMethod,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        int PlannedWorkflowCount,
        int ActualWorkflowCount,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        IReadOnlyList<DataWhatIfWorkflowsTourManifestCapture> Captures,
        IReadOnlyList<DataWhatIfWorkflowsTourManifestWorkflow> Workflows,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record DataWhatIfWorkflowsTourManifestWorkflow(
        string Name,
        IReadOnlyList<string> CatalogRows,
        string PlannedStatus,
        string ActualStatus,
        string CommandRoute,
        string LimitationNote,
        IReadOnlyList<string> CaptureKeys);

    private sealed record DataWhatIfWorkflowsTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string CatalogRow,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        string CommandRoute,
        string EvidenceSummary,
        string SelectedRange,
        IReadOnlyList<string> VisibleRows,
        int ScenarioCount,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight);
}
