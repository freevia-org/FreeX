using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private async Task CaptureDataSubmittedWorkflowsTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteDataSubmittedWorkflowsTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1220;
        Height = 780;
        await Task.Delay(700);

        var context = EnsureDataSubmittedWorkflowsTourContext();
        var captures = new List<DataSubmittedWorkflowsTourManifestCapture>();
        var workflows = new List<DataSubmittedWorkflowsTourManifestWorkflow>();

        try
        {
            captures.Add(await CaptureDataSubmittedWorkflowsWindowStateAsync(
                outputDir,
                context,
                "UI-CMD-DATA-002",
                "sort-before-unsorted-data",
                "Worksheet grid",
                "freex_data_submitted_workflows_sort_before",
                context.SortHeaderRange,
                "Seeded table before submitting SortCommand; Amount values are intentionally unsorted.",
                "Seeded workbook state before command execution."));

            var sortCommand = new SortCommand(
                context.Sheet.Id,
                context.SortDataRowsRange,
                [new SortKey(2, Ascending: false)]);
            ExecuteDataSubmittedWorkflowsTourCommand(sortCommand, "Sort");
            await WaitForDataSubmittedWorkflowsWindowAsync(context.SortHeaderRange.Start);
            captures.Add(await CaptureDataSubmittedWorkflowsWindowStateAsync(
                outputDir,
                context,
                "UI-CMD-DATA-002",
                "sort-after-amount-descending",
                "Worksheet grid",
                "freex_data_submitted_workflows_sort_after_amount_desc",
                context.SortHeaderRange,
                "Worksheet grid after the real SortCommand sorted data rows by Amount descending while preserving the header row.",
                "TryExecuteCommand(new SortCommand(sheet.Id, dataRowsRange, [new SortKey(2, ascending: false)]), \"Sort\")"));
            workflows.Add(CreateActualDataSubmittedWorkflow(
                "Sort mutation",
                ["UI-CAT-DATA-001", "UI-CMD-DATA-002"],
                "TryExecuteCommand(new SortCommand(...))",
                "sort-before-unsorted-data",
                "sort-after-amount-descending"));

            context.Sheet.AutoFilter = new WorksheetAutoFilterModel(context.FilterRange.ToString(), null);
            var filterPlan = _filterWorkflowSession.PlanAllowedValues(
                context.Sheet.Id,
                context.FilterRange,
                columnOffset: 3,
                allowedValues: ["Open"]);
            if (!TryExecuteAutoFilterMutation(filterPlan))
                throw new InvalidOperationException("Data submitted workflows tour could not apply the Status=Open AutoFilter.");

            await WaitForDataSubmittedWorkflowsWindowAsync(context.FilterRange.Start);
            captures.Add(await CaptureDataSubmittedWorkflowsWindowStateAsync(
                outputDir,
                context,
                "UI-CMD-DATA-002",
                "autofilter-applied-open",
                "Worksheet grid",
                "freex_data_submitted_workflows_autofilter_applied_open",
                context.FilterRange,
                "Worksheet grid after the real FilterCommand hid rows whose Status is not Open.",
                "WorksheetFilterWorkflowSession.PlanAllowedValues followed by TryExecuteAutoFilterMutation"));

            ExecuteDataSubmittedWorkflowsTourCommand(
                new FilterCommand(context.Sheet.Id, context.FilterRange, filterColOffset: 3, allowedValues: []),
                "Clear Filter");
            await WaitForDataSubmittedWorkflowsWindowAsync(context.FilterRange.Start);
            captures.Add(await CaptureDataSubmittedWorkflowsWindowStateAsync(
                outputDir,
                context,
                "UI-CMD-DATA-002",
                "autofilter-cleared-all-rows-visible",
                "Worksheet grid",
                "freex_data_submitted_workflows_autofilter_cleared",
                context.FilterRange,
                "Worksheet grid after the real clear-filter command restored all table rows.",
                "TryExecuteCommand(new FilterCommand(sheet.Id, tableRange, 3, []), \"Clear Filter\")"));

            ReapplyAutoFilter();
            await WaitForDataSubmittedWorkflowsWindowAsync(context.FilterRange.Start);
            captures.Add(await CaptureDataSubmittedWorkflowsWindowStateAsync(
                outputDir,
                context,
                "UI-CMD-DATA-002",
                "autofilter-reapplied-open",
                "Worksheet grid",
                "freex_data_submitted_workflows_autofilter_reapplied_open",
                context.FilterRange,
                "Worksheet grid after the host ReapplyAutoFilter path replayed the shared Status=Open intent.",
                "ReapplyAutoFilter() using WorksheetFilterWorkflowSession"));
            workflows.Add(CreateActualDataSubmittedWorkflow(
                "AutoFilter apply, clear, and reapply",
                ["UI-CAT-DATA-001", "UI-CMD-DATA-002"],
                "WorksheetFilterWorkflowSession apply, FilterCommand clear, ReapplyAutoFilter",
                "autofilter-applied-open",
                "autofilter-cleared-all-rows-visible",
                "autofilter-reapplied-open"));

            ExecuteDataSubmittedWorkflowsTourCommand(
                new AdvancedFilterCommand(
                    context.AdvancedFilterListRange,
                    context.AdvancedFilterCriteriaRange,
                    context.AdvancedFilterCopyToCell,
                    UniqueRecordsOnly: false),
                "Advanced Filter");
            await WaitForDataSubmittedWorkflowsWindowAsync(context.AdvancedFilterCopyToCell);
            var advancedFilterOutputRange = new GridRange(
                context.AdvancedFilterCriteriaRange.Start,
                new CellAddress(
                    context.Sheet.Id,
                    context.AdvancedFilterCopyToCell.Row + 2,
                    context.AdvancedFilterCopyToCell.Col + context.AdvancedFilterListRange.ColCount - 1));
            captures.Add(await CaptureDataSubmittedWorkflowsWindowStateAsync(
                outputDir,
                context,
                "UI-CMD-DATA-003",
                "advanced-filter-copy-to-result",
                "Worksheet grid",
                "freex_data_submitted_workflows_advanced_filter_copy_to_result",
                advancedFilterOutputRange,
                "Worksheet grid after AdvancedFilterCommand copied North records to the requested output range.",
                "TryExecuteCommand(new AdvancedFilterCommand(listRange, criteriaRange, copyToCell, uniqueRecordsOnly: false), \"Advanced Filter\")"));
            workflows.Add(CreateActualDataSubmittedWorkflow(
                "Advanced Filter copy-to submitted result",
                ["UI-CAT-DATA-002", "UI-CMD-DATA-003"],
                "AdvancedFilterCommand through TryExecuteCommand",
                "advanced-filter-copy-to-result"));

            var textToColumnsResult = TextToColumnsDialog.CreateResult(
                TextToColumnsDelimiterKind.Comma,
                destination: context.TextToColumnsDestination,
                columnFormats:
                [
                    TextToColumnsColumnFormat.Text,
                    TextToColumnsColumnFormat.General,
                    TextToColumnsColumnFormat.Text
                ]);
            var textToColumnsCommand = CreateTextToColumnsCommand(
                [context.Sheet.Id],
                context.TextToColumnsSourceRange,
                textToColumnsResult);
            ExecuteDataSubmittedWorkflowsTourCommand(textToColumnsCommand, "Text to Columns");
            await WaitForDataSubmittedWorkflowsWindowAsync(context.TextToColumnsSourceRange.Start);
            captures.Add(await CaptureDataSubmittedWorkflowsWindowStateAsync(
                outputDir,
                context,
                "UI-CMD-DATA-004",
                "text-to-columns-result",
                "Worksheet grid",
                "freex_data_submitted_workflows_text_to_columns_result",
                new GridRange(context.TextToColumnsSourceRange.Start, new CellAddress(context.Sheet.Id, context.TextToColumnsSourceRange.End.Row, context.TextToColumnsDestination.Col + 2)),
                "Worksheet grid after TextToColumnsCommandPlanner produced EditCellsCommand output at the requested destination.",
                "CreateTextToColumnsCommand([sheet.Id], sourceRange, TextToColumnsDialog.CreateResult(...))"));
            workflows.Add(CreateActualDataSubmittedWorkflow(
                "Text to Columns submitted result",
                ["UI-CAT-DATA-002", "UI-CMD-DATA-004"],
                "CreateTextToColumnsCommand -> EditCellsCommand",
                "text-to-columns-result"));

            var validationRule = new DataValidation
            {
                AppliesTo = context.ValidationRange,
                Type = DvType.List,
                Formula1 = "\"North,South,West\"",
                AllowBlank = true,
                ShowDropdown = true,
                ShowInputMessage = true,
                PromptTitle = "Region list",
                PromptMessage = "Choose North, South, or West.",
                ShowErrorMessage = true,
                AlertStyle = DvAlertStyle.Stop,
                ErrorTitle = "Invalid region",
                ErrorMessage = "Use one of the approved region names."
            };
            ExecuteDataSubmittedWorkflowsTourCommand(
                new SetDataValidationCommand(context.Sheet.Id, validationRule),
                "Data Validation");
            CircleInvalidDataMenuItem_Click(this, new RoutedEventArgs());
            await WaitForDataSubmittedWorkflowsWindowAsync(context.ValidationRange.Start);
            captures.Add(await CaptureDataSubmittedWorkflowsWindowStateAsync(
                outputDir,
                context,
                "UI-CMD-DATA-005",
                "data-validation-invalid-cell-selected",
                "Worksheet grid",
                "freex_data_submitted_workflows_data_validation_invalid_selected",
                context.ValidationRange,
                "Worksheet grid after SetDataValidationCommand and the production Circle Invalid Data path selected the invalid Mars entry.",
                "TryExecuteCommand(new SetDataValidationCommand(...)); CircleInvalidDataMenuItem_Click(...)"));
            workflows.Add(CreateActualDataSubmittedWorkflow(
                "Data Validation invalid-data proof",
                ["UI-CAT-DATA-002", "UI-CMD-DATA-005"],
                "SetDataValidationCommand plus CircleInvalidDataMenuItem_Click/DataValidationCirclePlanner",
                "data-validation-invalid-cell-selected"));

            ExecuteDataSubmittedWorkflowsTourCommand(
                new SubtotalCommand(
                    context.Sheet.Id,
                    context.SubtotalRange,
                    groupByColumnOffset: 0,
                    subtotalColumnOffsets: [2],
                    functionNumber: 9,
                    pageBreakBetweenGroups: false,
                    summaryBelowData: true),
                "Subtotal",
                out var subtotalOutcome);
            await WaitForDataSubmittedWorkflowsWindowAsync(context.SubtotalRange.Start);
            captures.Add(await CaptureDataSubmittedWorkflowsWindowStateAsync(
                outputDir,
                context,
                "UI-CMD-DATA-007",
                "subtotal-command-result",
                "Worksheet grid",
                "freex_data_submitted_workflows_subtotal_result",
                new GridRange(context.SubtotalRange.Start, new CellAddress(context.Sheet.Id, context.SubtotalRange.End.Row + 4, context.SubtotalRange.End.Col)),
                "Worksheet grid after SubtotalCommand inserted group subtotal rows and a grand-total row.",
                "TryExecuteCommand(new SubtotalCommand(...)); RecalculateIfAutomatic(outcome.AffectedCells)"));
            workflows.Add(CreateActualDataSubmittedWorkflow(
                "Subtotal submitted mutation",
                ["UI-CAT-DATA-003", "UI-CMD-DATA-007"],
                "SubtotalCommand through TryExecuteCommand",
                "subtotal-command-result"));

            var duplicateRange = SeedDataSubmittedWorkflowsRemoveDuplicatesSection(context.Sheet);
            await WaitForDataSubmittedWorkflowsWindowAsync(duplicateRange.Start);
            var removeDuplicatesCommand = new RemoveDuplicateRowsCommand(
                context.Sheet.Id,
                new GridRange(
                    new CellAddress(context.Sheet.Id, duplicateRange.Start.Row + 1, duplicateRange.Start.Col),
                    duplicateRange.End),
                [0, 1, 2]);
            ExecuteDataSubmittedWorkflowsTourCommand(removeDuplicatesCommand, "Remove Duplicates");
            await WaitForDataSubmittedWorkflowsWindowAsync(duplicateRange.Start);
            captures.Add(await CaptureDataSubmittedWorkflowsWindowStateAsync(
                outputDir,
                context,
                "UI-CMD-DATA-005",
                "remove-duplicates-result",
                "Worksheet grid",
                "freex_data_submitted_workflows_remove_duplicates_result",
                duplicateRange,
                $"Worksheet grid after RemoveDuplicateRowsCommand removed {removeDuplicatesCommand.RemovedRowCount} duplicate row.",
                "TryExecuteCommand(new RemoveDuplicateRowsCommand(sheet.Id, dataRowsRange, [0, 1, 2]), \"Remove Duplicates\")"));
            workflows.Add(CreateActualDataSubmittedWorkflow(
                "Remove Duplicates submitted result",
                ["UI-CAT-DATA-002", "UI-CMD-DATA-005"],
                "RemoveDuplicateRowsCommand through TryExecuteCommand",
                "remove-duplicates-result"));

            workflows.Add(new DataSubmittedWorkflowsTourManifestWorkflow(
                Name: "Data Validation dropdown popup commit",
                CatalogRows: ["UI-CAT-DATA-002", "UI-CMD-DATA-005"],
                PlannedStatus: "planned",
                ActualStatus: "planned-but-blocked",
                CommandRoute: "OpenActiveDropdown/ComboBox popup plus ValidationDropdown_SelectionChanged",
                LimitationNote: "The list dropdown is a WPF popup/ComboBox interaction path and requires foreground keyboard or mouse input to prove safely; this slice instead captures deterministic invalid-data proof through SetDataValidationCommand and Circle Invalid Data.",
                CaptureKeys: []));

            ValidateDataSubmittedWorkflowsTourEvidence(outputDir, captures);
            await WriteDataSubmittedWorkflowsTourManifestAsync(outputDir, context, captures, workflows);
        }
        catch
        {
            DeleteDataSubmittedWorkflowsTourEvidence(outputDir);
            throw;
        }
    }

    private DataSubmittedWorkflowsTourContext EnsureDataSubmittedWorkflowsTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Data submitted workflows tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        ClearDataSubmittedWorkflowsSheetArea(sheet);

        var headers = new[] { "Region", "Rep", "Amount", "Status", "Month" };
        for (var index = 0; index < headers.Length; index++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(index + 1)), new TextValue(headers[index]));

        var rows = new (string Region, string Rep, double Amount, string Status, string Month)[]
        {
            ("South", "Beth", 3150, "Closed", "Feb"),
            ("North", "Ada", 4200, "Open", "Jan"),
            ("East", "Drew", 2800, "Pending", "Apr"),
            ("West", "Eli", 6300, "Open", "May"),
            ("North", "Cora", 5100, "Open", "Mar"),
            ("East", "Fay", 2400, "Closed", "Jun"),
            ("South", "Gus", 4700, "Open", "Jul"),
            ("West", "Hana", 3900, "Pending", "Aug")
        };

        for (var index = 0; index < rows.Length; index++)
        {
            var row = (uint)(index + 2);
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(rows[index].Region));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(rows[index].Rep));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(rows[index].Amount));
            sheet.SetCell(new CellAddress(sheet.Id, row, 4), new TextValue(rows[index].Status));
            sheet.SetCell(new CellAddress(sheet.Id, row, 5), new TextValue(rows[index].Month));
        }

        sheet.SetCell(new CellAddress(sheet.Id, 11, 1), new TextValue("Text to Columns source"));
        sheet.SetCell(new CellAddress(sheet.Id, 11, 2), new TextValue("Destination columns"));
        sheet.SetCell(new CellAddress(sheet.Id, 12, 1), new TextValue("North,125,Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 13, 1), new TextValue("West,98,Closed"));
        sheet.SetCell(new CellAddress(sheet.Id, 14, 1), new TextValue("East,143,Open"));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 7), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 7), new TextValue("North"));

        sheet.SetCell(new CellAddress(sheet.Id, 17, 1), new TextValue("Data Validation"));
        sheet.SetCell(new CellAddress(sheet.Id, 18, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 19, 1), new TextValue("Mars"));
        sheet.SetCell(new CellAddress(sheet.Id, 20, 1), new TextValue("West"));

        sheet.SetCell(new CellAddress(sheet.Id, 23, 1), new TextValue("Subtotal Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 23, 2), new TextValue("Rep"));
        sheet.SetCell(new CellAddress(sheet.Id, 23, 3), new TextValue("Amount"));
        var subtotalRows = new (string Region, string Rep, double Amount)[]
        {
            ("East", "Drew", 2800),
            ("East", "Fay", 2400),
            ("North", "Ada", 4200),
            ("North", "Cora", 5100),
            ("West", "Eli", 6300),
            ("West", "Hana", 3900)
        };
        for (var index = 0; index < subtotalRows.Length; index++)
        {
            var row = (uint)(index + 24);
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(subtotalRows[index].Region));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(subtotalRows[index].Rep));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(subtotalRows[index].Amount));
        }

        sheet.AutoFilter = null;
        sheet.FilterHiddenRows.Clear();
        sheet.HiddenRows.Clear();
        sheet.GroupHiddenRows.Clear();
        sheet.RowOutlineLevels.Clear();
        sheet.DataValidations.Clear();
        _filterWorkflowSession.ResetAutoFilterState();

        var filterRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 9, 5));
        var advancedFilterCriteriaRange = new GridRange(new CellAddress(sheet.Id, 1, 7), new CellAddress(sheet.Id, 2, 7));
        var advancedFilterCopyToCell = new CellAddress(sheet.Id, 4, 7);
        var textToColumnsRange = new GridRange(new CellAddress(sheet.Id, 12, 1), new CellAddress(sheet.Id, 14, 1));
        var validationRange = new GridRange(new CellAddress(sheet.Id, 18, 1), new CellAddress(sheet.Id, 20, 1));
        var subtotalRange = new GridRange(new CellAddress(sheet.Id, 23, 1), new CellAddress(sheet.Id, 29, 3));

        SetSelectionRange(filterRange, filterRange.Start);
        EnsureCellVisible(filterRange.Start);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();

        return new DataSubmittedWorkflowsTourContext(
            sheet,
            filterRange,
            new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 9, 5)),
            filterRange,
            new CellAddress(sheet.Id, 1, 4),
            filterRange,
            advancedFilterCriteriaRange,
            advancedFilterCopyToCell,
            textToColumnsRange,
            new CellAddress(sheet.Id, 12, 2),
            validationRange,
            subtotalRange);
    }

    private static void ClearDataSubmittedWorkflowsSheetArea(Sheet sheet)
    {
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 60, 12));
        foreach (var address in range.AllCells())
            sheet.ClearCell(address);
    }

    private GridRange SeedDataSubmittedWorkflowsRemoveDuplicatesSection(Sheet sheet)
    {
        const uint headerRow = 42;
        sheet.SetCell(new CellAddress(sheet.Id, headerRow, 1), new TextValue("Duplicate Region"));
        sheet.SetCell(new CellAddress(sheet.Id, headerRow, 2), new TextValue("Rep"));
        sheet.SetCell(new CellAddress(sheet.Id, headerRow, 3), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, headerRow, 4), new TextValue("Status"));
        var rows = new (string Region, string Rep, double Amount, string Status)[]
        {
            ("North", "Ada", 4200, "Open"),
            ("South", "Beth", 3150, "Closed"),
            ("North", "Ada", 4200, "Open"),
            ("West", "Eli", 6300, "Open"),
            ("South", "Beth", 3150, "Closed")
        };

        for (var index = 0; index < rows.Length; index++)
        {
            var row = (uint)(headerRow + 1 + index);
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue(rows[index].Region));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(rows[index].Rep));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(rows[index].Amount));
            sheet.SetCell(new CellAddress(sheet.Id, row, 4), new TextValue(rows[index].Status));
        }

        var range = new GridRange(
            new CellAddress(sheet.Id, headerRow, 1),
            new CellAddress(sheet.Id, headerRow + (uint)rows.Length, 4));
        SetSelectionRange(range, range.Start);
        UpdateViewport();
        RefreshStatusBar();
        return range;
    }

    private void ExecuteDataSubmittedWorkflowsTourCommand(IWorkbookCommand command, string title)
    {
        if (!TryExecuteCommand(command, title, out var outcome))
            throw new InvalidOperationException($"Data submitted workflows tour command '{title}' failed: {outcome.ErrorMessage}");
    }

    private void ExecuteDataSubmittedWorkflowsTourCommand(IWorkbookCommand command, string title, out CommandOutcome outcome)
    {
        if (!TryExecuteCommand(command, title, out outcome))
            throw new InvalidOperationException($"Data submitted workflows tour command '{title}' failed: {outcome.ErrorMessage}");
    }

    private async Task WaitForDataSubmittedWorkflowsWindowAsync(CellAddress visibleCell)
    {
        EnsureCellVisible(visibleCell);
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();
        await Task.Delay(300);
        await WaitForRibbonScreenshotRenderPassAsync();
    }

    private async Task<DataSubmittedWorkflowsTourManifestCapture> CaptureDataSubmittedWorkflowsWindowStateAsync(
        string outputDir,
        DataSubmittedWorkflowsTourContext context,
        string catalogRow,
        string state,
        string surface,
        string fileName,
        GridRange focusRange,
        string evidenceSummary,
        string commandRoute)
    {
        SetSelectionRange(focusRange, focusRange.Start);
        await WaitForDataSubmittedWorkflowsWindowAsync(focusRange.Start);
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);

        return new DataSubmittedWorkflowsTourManifestCapture(
            CaptureKey: $"data-submitted-workflows:{state}",
            PairKey: $"interactive:data-submitted-workflows:{state}",
            CatalogRow: catalogRow,
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "RenderTargetBitmap-window-full",
            CommandRoute: commandRoute,
            EvidenceSummary: evidenceSummary,
            SelectedRange: focusRange.ToString(),
            VisibleRows: DescribeDataSubmittedVisibleRows(context.Sheet, focusRange),
            FilterHiddenRows: context.Sheet.FilterHiddenRows.OrderBy(row => row).Select(row => row.ToString()).ToArray(),
            GroupHiddenRows: context.Sheet.GroupHiddenRows.OrderBy(row => row).Select(row => row.ToString()).ToArray(),
            DataValidationRuleCount: context.Sheet.DataValidations.Count,
            CaptureLogicalWidth: ActualWidth,
            CaptureLogicalHeight: Math.Min(ActualHeight, 760));
    }

    private static IReadOnlyList<string> DescribeDataSubmittedVisibleRows(Sheet sheet, GridRange range)
    {
        var rows = new List<string>();
        var endRow = Math.Min(range.End.Row, range.Start.Row + 11);
        for (var row = range.Start.Row; row <= endRow; row++)
        {
            if (sheet.HiddenRows.Contains(row) ||
                sheet.FilterHiddenRows.Contains(row) ||
                sheet.GroupHiddenRows.Contains(row))
            {
                continue;
            }

            var values = new List<string>();
            var endCol = Math.Min(range.End.Col, range.Start.Col + 4);
            for (var col = range.Start.Col; col <= endCol; col++)
                values.Add(FormatDataSubmittedCellValue(sheet.GetValue(row, col)));
            rows.Add($"{row}:{string.Join("|", values)}");
        }

        return rows;
    }

    private static string FormatDataSubmittedCellValue(ScalarValue? value) => value switch
    {
        null or BlankValue => "",
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        DateTimeValue date => date.Value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture),
        ErrorValue error => error.Code,
        _ => value.ToString() ?? ""
    };

    private static DataSubmittedWorkflowsTourManifestWorkflow CreateActualDataSubmittedWorkflow(
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
            LimitationNote: "Captured through deterministic in-process command execution and RenderTargetBitmap; no global mouse, keytip, native dialog, or UI Automation Invoke input is synthesized.",
            CaptureKeys: captureKeys.Select(key => $"data-submitted-workflows:{key}").ToArray());

    private static void DeleteDataSubmittedWorkflowsTourEvidence(string outputDir)
    {
        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_data_submitted_workflows_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, DataSubmittedWorkflowsTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateDataSubmittedWorkflowsTourEvidence(
        string outputDir,
        IReadOnlyList<DataSubmittedWorkflowsTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Data submitted workflows tour did not create planned capture '{capture.OutputFileName}'.");
        }
    }

    private static async Task WriteDataSubmittedWorkflowsTourManifestAsync(
        string outputDir,
        DataSubmittedWorkflowsTourContext context,
        IReadOnlyList<DataSubmittedWorkflowsTourManifestCapture> captures,
        IReadOnlyList<DataSubmittedWorkflowsTourManifestWorkflow> workflows)
    {
        var actualWorkflowCount = workflows.Count(workflow => string.Equals(workflow.ActualStatus, "captured", StringComparison.Ordinal));
        var manifest = new DataSubmittedWorkflowsTourManifest(
            Tool: "FREEX_DATA_SUBMITTED_WORKFLOWS_TOUR",
            EvidenceFamily: "data-submitted-workflows",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "data-submitted-workflows:submitted-command-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_data_submitted_workflows_<Workflow>_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md#UI-CAT-DATA-001",
            CatalogRows: ["UI-CAT-DATA-001", "UI-CAT-DATA-002", "UI-CAT-DATA-003", "UI-CMD-DATA-002", "UI-CMD-DATA-003", "UI-CMD-DATA-004", "UI-CMD-DATA-005", "UI-CMD-DATA-007"],
            SheetName: context.Sheet.Name,
            SortRange: context.SortHeaderRange.ToString(),
            FilterRange: context.FilterRange.ToString(),
            AdvancedFilterCriteriaRange: context.AdvancedFilterCriteriaRange.ToString(),
            AdvancedFilterCopyToCell: context.AdvancedFilterCopyToCell.ToA1(),
            TextToColumnsRange: context.TextToColumnsSourceRange.ToString(),
            ValidationRange: context.ValidationRange.ToString(),
            SubtotalRange: context.SubtotalRange.ToString(),
            CaptureStatus: "partial-with-blocked-planned-items",
            CaptureMethod: "RenderTargetBitmap-window-full-with-real-workbook-commands",
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures after real command execution; no global mouse, keyboard, keytip, native dialog, or UI Automation Invoke input is used."
                    : "Window captures abort unless the expected FreeX WPF surface owns foreground focus immediately before render and file write."),
            PlannedWorkflowCount: workflows.Count,
            ActualWorkflowCount: actualWorkflowCount,
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Captures: captures,
            Workflows: workflows,
            CoveredStates:
            [
                "Sort before and after submitted SortCommand",
                "AutoFilter applied, cleared, and reapplied visible-row states",
                "Advanced Filter copy-to submitted result",
                "Text to Columns submitted result grid",
                "Data Validation invalid-cell selection through Circle Invalid Data",
                "Subtotal submitted mutation result",
                "Remove Duplicates submitted result"
            ],
            Limitations:
            [
                "This slice submits real FreeX command/service paths where they are deterministic in process.",
                "The tour does not synthesize foreground mouse, keytip, dropdown keyboard, access-key, range-picker, or UI Automation Invoke workflows.",
                "The AutoFilter clear capture uses the same FilterCommand clear path while preserving the shared live filter intent so ReapplyAutoFilter can be captured in the same deterministic run.",
                "The Data Validation proof captures invalid-data detection/selection rather than the foreground ComboBox dropdown popup or modal invalid-entry alert.",
                "Remove Duplicates is seeded near the end of the sheet because the command deletes worksheet rows; the tour keeps that mutation isolated from earlier captures.",
                "No Microsoft Excel counterpart screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, DataSubmittedWorkflowsTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.DataSubmittedWorkflowsTourManifest);
    }

    private sealed record DataSubmittedWorkflowsTourContext(
        Sheet Sheet,
        GridRange SortHeaderRange,
        GridRange SortDataRowsRange,
        GridRange FilterRange,
        CellAddress FilterHeaderCell,
        GridRange AdvancedFilterListRange,
        GridRange AdvancedFilterCriteriaRange,
        CellAddress AdvancedFilterCopyToCell,
        GridRange TextToColumnsSourceRange,
        CellAddress TextToColumnsDestination,
        GridRange ValidationRange,
        GridRange SubtotalRange);

    private sealed record DataSubmittedWorkflowsTourManifest(
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
        string SortRange,
        string FilterRange,
        string AdvancedFilterCriteriaRange,
        string AdvancedFilterCopyToCell,
        string TextToColumnsRange,
        string ValidationRange,
        string SubtotalRange,
        string CaptureStatus,
        string CaptureMethod,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        int PlannedWorkflowCount,
        int ActualWorkflowCount,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        IReadOnlyList<DataSubmittedWorkflowsTourManifestCapture> Captures,
        IReadOnlyList<DataSubmittedWorkflowsTourManifestWorkflow> Workflows,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record DataSubmittedWorkflowsTourManifestWorkflow(
        string Name,
        IReadOnlyList<string> CatalogRows,
        string PlannedStatus,
        string ActualStatus,
        string CommandRoute,
        string LimitationNote,
        IReadOnlyList<string> CaptureKeys);

    private sealed record DataSubmittedWorkflowsTourManifestCapture(
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
        IReadOnlyList<string> FilterHiddenRows,
        IReadOnlyList<string> GroupHiddenRows,
        int DataValidationRuleCount,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight);
}
