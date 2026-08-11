using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FreeX.App.Presentation.PivotUI;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private async Task CapturePivotAdvancedWorkflowsTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeletePivotAdvancedWorkflowsTourEvidence(outputDir);

        var savedWorkbookPath = Path.Combine(outputDir, PivotAdvancedWorkflowsTourSavedWorkbookFileName);
        DeleteIfExists(savedWorkbookPath);

        WindowState = WindowState.Normal;
        Width = 1360;
        Height = 860;
        await Task.Delay(700);

        var context = EnsurePivotAdvancedWorkflowsTourContext(savedWorkbookPath, "seeded");
        var captures = new List<PivotAdvancedWorkflowsTourManifestCapture>();

        try
        {
            SelectRibbonTourTab(RibbonScreenshotTourPlanner.PivotContextTabs.Single(tab => tab.Header == "PivotTable Analyze"));
            captures.Add(await CapturePivotAdvancedWorkflowsWindowStateAsync(
                outputDir,
                context,
                "seeded-analyze-layout",
                "freex_pivot_advanced_seeded_analyze_layout",
                "PivotTable Analyze is visible for the seeded pivot with row/column/filter/value areas populated before advanced mutations.",
                "seeded"));

            context = SubmitPivotAdvancedFieldLayoutMutation(context);
            captures.Add(await CapturePivotAdvancedWorkflowsWindowStateAsync(
                outputDir,
                context,
                "field-layout-mutated",
                "freex_pivot_advanced_field_layout_mutated",
                "The production field-list move path adds Channel to the Rows area as a model-equivalent field layout mutation.",
                "layout-mutated"));

            captures.Add(await CapturePivotAdvancedLabelFilterDialogAsync(outputDir, context));
            captures.Add(await CapturePivotAdvancedValueFilterDialogAsync(outputDir, context));
            context = SubmitPivotAdvancedFilterMutations(context);
            captures.Add(await CapturePivotAdvancedWorkflowsWindowStateAsync(
                outputDir,
                context,
                "label-value-filters-submitted",
                "freex_pivot_advanced_label_value_filters_submitted",
                "Submitted label and value filter models are applied through ConfigurePivotTableViewCommand and the pivot grid is refreshed.",
                "filters-submitted"));

            captures.Add(await CapturePivotAdvancedValueFieldSettingsDialogAsync(outputDir, context));
            context = SubmitPivotAdvancedValueFieldSettings(context);
            captures.Add(await CapturePivotAdvancedWorkflowsWindowStateAsync(
                outputDir,
                context,
                "value-field-settings-result",
                "freex_pivot_advanced_value_field_settings_result",
                "Submitted Value Field Settings replace Sum of Sales with Avg Sales % Grand Total and a percent number format.",
                "value-settings"));

            context = SubmitPivotAdvancedClearSelectRefreshAndSource(context);
            captures.Add(await CapturePivotAdvancedWorkflowsWindowStateAsync(
                outputDir,
                context,
                "clear-select-refresh-source-result",
                "freex_pivot_advanced_clear_select_refresh_source_result",
                "Clear PivotTable removes filters, Select PivotTable selects the rendered target range, Change Data Source expands the range, and Refresh materializes the new East row.",
                "clear-select-refresh-source"));

            context = SubmitPivotAdvancedPivotChart(context);
            captures.Add(await CapturePivotAdvancedPivotChartFieldButtonMenuAsync(outputDir, context));

            context = await SavePivotAdvancedWorkflowsWorkbookAsync(savedWorkbookPath, context);
            captures.Add(await CapturePivotAdvancedWorkflowsWindowStateAsync(
                outputDir,
                context,
                "saved-xlsx-workbook",
                "freex_pivot_advanced_saved_xlsx_workbook",
                "SaveWorkbookToTargetAsync writes the authored PivotTable/PivotChart workbook through the XLSX adapter while the advanced pivot state remains selected.",
                "saved"));

            await OpenFileAsync(savedWorkbookPath);
            context = ResolvePivotAdvancedWorkflowsCurrentContext(savedWorkbookPath, "reopened");
            captures.Add(await CapturePivotAdvancedWorkflowsWindowStateAsync(
                outputDir,
                context,
                "reopened-persisted-pivot",
                "freex_pivot_advanced_reopened_persisted_pivot",
                "OpenFileAsync reopens the saved XLSX workbook and restores the PivotTable layout, value-field settings, expanded source range, and PivotChart binding where supported.",
                "reopened"));

            ValidatePivotAdvancedWorkflowsTourEvidence(outputDir, captures, savedWorkbookPath, context);
            await WritePivotAdvancedWorkflowsTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeletePivotAdvancedWorkflowsTourEvidence(outputDir);
            throw;
        }
    }

    private PivotAdvancedWorkflowsTourContext EnsurePivotAdvancedWorkflowsTourContext(
        string savedWorkbookPath,
        string persistenceStage)
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Pivot advanced workflows tour requires an active worksheet.");

        HideStartScreen();
        _currentSheetId = sheet.Id;
        _currentFilePath = null;
        _workbook.Name = "Pivot advanced workflows";
        _workbook.Slicers.Clear();
        _workbook.Timelines.Clear();
        sheet.Name = "Pivot Advanced";
        sheet.StructuredTables.Clear();
        sheet.PivotTables.Clear();
        sheet.Charts.Clear();
        sheet.Sparklines.Clear();
        sheet.FilterHiddenRows.Clear();
        sheet.HiddenRows.Clear();
        sheet.HiddenCols.Clear();
        sheet.Comments.Clear();
        sheet.ThreadedComments.Clear();
        sheet.Hyperlinks.Clear();
        sheet.ReplaceMergedRegions([]);

        var clearRange = Range(sheet.Id, 1, 1, 26, 16);
        foreach (var address in clearRange.AllCells())
            sheet.ClearCell(address);

        SeedPivotAdvancedWorkflowsSourceData(sheet);
        var sourceRange = Range(sheet.Id, 1, 1, 7, 6);
        var targetRange = Range(sheet.Id, 2, 8, 14, 14);
        ExecutePivotAdvancedWorkflowsCommand(
            new AddPivotTableCommand(
                sheet.Id,
                sourceRange,
                targetRange,
                ScreenshotTourPivotTableName,
                rowFieldIndexes: [0],
                dataFieldIndexes: [3]),
            "Insert PivotTable");

        var pivotTable = FindScreenshotTourPivotTable(sheet)
            ?? throw new InvalidOperationException("Pivot advanced workflows tour could not find the seeded PivotTable.");
        ExecutePivotAdvancedWorkflowsCommand(
            new ConfigurePivotTableLayoutCommand(
                sheet.Id,
                pivotTable.Name,
                rowFields: [new PivotFieldModel(0)],
                columnFields: [new PivotFieldModel(1)],
                pageFields: [new PivotFieldModel(2)],
                dataFields: [new PivotDataFieldModel(3, "Sum of Sales", "sum", NumberFormatId: 4)]),
            "PivotTable Fields");

        pivotTable = FindScreenshotTourPivotTable(sheet)
            ?? throw new InvalidOperationException("Pivot advanced workflows tour lost the seeded PivotTable after layout setup.");
        SetSelectionRange(new GridRange(pivotTable.TargetRange.Start, pivotTable.TargetRange.Start), pivotTable.TargetRange.Start);
        EnsureCellVisible(pivotTable.TargetRange.Start);
        RefreshPivotFieldListPane();
        UpdateViewport();
        RefreshToolbar();
        MarkWorkbookDirty();
        UpdateTitleBar();

        return CreatePivotAdvancedWorkflowsContext(sheet, pivotTable, sourceRange, savedWorkbookPath, 0, persistenceStage);
    }

    private static void SeedPivotAdvancedWorkflowsSourceData(Sheet sheet)
    {
        var headers = new[] { "Region", "Product", "Quarter", "Sales", "Channel", "Margin" };
        object[][] rows =
        [
            ["North", "Coffee", "Q1", 1280d, "Retail", 0.31d],
            ["North", "Tea", "Q1", 760d, "Online", 0.27d],
            ["South", "Coffee", "Q2", 960d, "Retail", 0.29d],
            ["South", "Tea", "Q2", 690d, "Wholesale", 0.24d],
            ["West", "Cocoa", "Q3", 1140d, "Online", 0.34d],
            ["West", "Coffee", "Q4", 1510d, "Retail", 0.36d]
        ];

        for (var col = 0; col < headers.Length; col++)
            sheet.SetCell(new CellAddress(sheet.Id, 1, (uint)(col + 1)), new TextValue(headers[col]));

        for (var row = 0; row < rows.Length; row++)
        {
            for (var col = 0; col < headers.Length; col++)
            {
                var address = new CellAddress(sheet.Id, (uint)(row + 2), (uint)(col + 1));
                if (rows[row][col] is double number)
                    sheet.SetCell(address, new NumberValue(number));
                else
                    sheet.SetCell(address, new TextValue(rows[row][col].ToString() ?? ""));
            }
        }
    }

    private PivotAdvancedWorkflowsTourContext SubmitPivotAdvancedFieldLayoutMutation(PivotAdvancedWorkflowsTourContext context)
    {
        PivotAvailableFieldsList.SelectedItem = _pivotFieldListAvailableItems.First(item => item.Caption == "Channel");
        MoveSelectedPivotField(PivotFieldBucket.Rows);
        var pivotTable = ResolvePivotAdvancedWorkflowsPivotTable(context.Sheet);
        RefreshPivotFieldListPane();
        return CreatePivotAdvancedWorkflowsContext(context.Sheet, pivotTable, context.SourceRange, context.SavedWorkbookPath, context.SavedWorkbookBytes, "layout-mutated");
    }

    private PivotAdvancedWorkflowsTourContext SubmitPivotAdvancedFilterMutations(PivotAdvancedWorkflowsTourContext context)
    {
        var pivotTable = ResolvePivotAdvancedWorkflowsPivotTable(context.Sheet);
        ExecutePivotAdvancedWorkflowsCommand(
            new ConfigurePivotTableViewCommand(
                context.Sheet.Id,
                pivotTable.Name,
                labelFilters: [new PivotLabelFilterModel(0, PivotLabelFilterKind.Contains, "o")],
                valueFilters: [new PivotValueFilterModel(0, PivotValueFilterKind.GreaterThan, ComparisonValue: 800, SourceFieldIndex: 0)],
                sorts: [new PivotSortModel(PivotSortTarget.Label, PivotSortDirection.Ascending, FieldIndex: 0)]),
            "PivotTable Field");

        pivotTable = ResolvePivotAdvancedWorkflowsPivotTable(context.Sheet);
        RefreshPivotFieldListPane();
        return CreatePivotAdvancedWorkflowsContext(context.Sheet, pivotTable, context.SourceRange, context.SavedWorkbookPath, context.SavedWorkbookBytes, "filters-submitted");
    }

    private PivotAdvancedWorkflowsTourContext SubmitPivotAdvancedValueFieldSettings(PivotAdvancedWorkflowsTourContext context)
    {
        var pivotTable = ResolvePivotAdvancedWorkflowsPivotTable(context.Sheet);
        var dataField = new PivotDataFieldModel(
            3,
            "Avg Sales % Grand Total",
            "average",
            NumberFormatId: 10,
            ShowValuesAs: PivotShowValuesAs.PercentOfGrandTotal,
            NumberFormatCode: "0.0%");
        ExecutePivotAdvancedWorkflowsCommand(
            new ConfigurePivotTableLayoutCommand(
                context.Sheet.Id,
                pivotTable.Name,
                pivotTable.RowFields.ToList(),
                pivotTable.ColumnFields.ToList(),
                pivotTable.PageFields.ToList(),
                [dataField]),
            "Value Field Settings");

        pivotTable = ResolvePivotAdvancedWorkflowsPivotTable(context.Sheet);
        RefreshPivotFieldListPane();
        return CreatePivotAdvancedWorkflowsContext(context.Sheet, pivotTable, context.SourceRange, context.SavedWorkbookPath, context.SavedWorkbookBytes, "value-settings");
    }

    private PivotAdvancedWorkflowsTourContext SubmitPivotAdvancedClearSelectRefreshAndSource(PivotAdvancedWorkflowsTourContext context)
    {
        var sheet = context.Sheet;
        sheet.SetCell(new CellAddress(sheet.Id, 8, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 8, 2), new TextValue("Coffee"));
        sheet.SetCell(new CellAddress(sheet.Id, 8, 3), new TextValue("Q4"));
        sheet.SetCell(new CellAddress(sheet.Id, 8, 4), new NumberValue(1750));
        sheet.SetCell(new CellAddress(sheet.Id, 8, 5), new TextValue("Retail"));
        sheet.SetCell(new CellAddress(sheet.Id, 8, 6), new NumberValue(0.4));

        var pivotTable = ResolvePivotAdvancedWorkflowsPivotTable(sheet);
        ExecutePivotAdvancedWorkflowsCommand(new ClearPivotTableViewCommand(sheet.Id, pivotTable.Name), "Clear PivotTable");
        pivotTable = ResolvePivotAdvancedWorkflowsPivotTable(sheet);
        var selectedRange = PivotUiPlanner.ResolvePivotTableSelectionRange(pivotTable);
        SetSelectionRange(selectedRange, selectedRange.Start);
        EnsureCellVisible(selectedRange.Start);

        var expandedSourceRange = Range(sheet.Id, 1, 1, 8, 6);
        ExecutePivotAdvancedWorkflowsCommand(new ChangePivotTableSourceCommand(sheet.Id, pivotTable.Name, expandedSourceRange), "Change PivotTable Data Source");
        ExecutePivotAdvancedWorkflowsCommand(new RefreshPivotTableCommand(sheet.Id, pivotTable.Name), "Refresh PivotTable");

        pivotTable = ResolvePivotAdvancedWorkflowsPivotTable(sheet);
        RefreshPivotFieldListPane();
        return CreatePivotAdvancedWorkflowsContext(sheet, pivotTable, expandedSourceRange, context.SavedWorkbookPath, context.SavedWorkbookBytes, "clear-select-refresh-source");
    }

    private PivotAdvancedWorkflowsTourContext SubmitPivotAdvancedPivotChart(PivotAdvancedWorkflowsTourContext context)
    {
        var pivotTable = ResolvePivotAdvancedWorkflowsPivotTable(context.Sheet);
        ExecutePivotAdvancedWorkflowsCommand(
            new AddPivotChartCommand(context.Sheet.Id, pivotTable.Name, ChartType.Column, $"{pivotTable.Name} Chart", left: 560, top: 170, width: 430, height: 300),
            "Insert PivotChart");

        pivotTable = ResolvePivotAdvancedWorkflowsPivotTable(context.Sheet);
        UpdateViewport();
        RefreshToolbar();
        return CreatePivotAdvancedWorkflowsContext(context.Sheet, pivotTable, context.SourceRange, context.SavedWorkbookPath, context.SavedWorkbookBytes, "pivotchart");
    }

    private async Task<PivotAdvancedWorkflowsTourContext> SavePivotAdvancedWorkflowsWorkbookAsync(
        string savedWorkbookPath,
        PivotAdvancedWorkflowsTourContext context)
    {
        var adapter = FileDialogFilterBuilder.FindSaveAdapter(_fileAdapters, ".xlsx", out _)
            ?? throw new InvalidOperationException("Pivot advanced workflows tour could not find the XLSX save adapter.");
        if (!await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter)))
            throw new InvalidOperationException("Pivot advanced workflows tour could not save the XLSX workbook.");

        var pivotTable = ResolvePivotAdvancedWorkflowsPivotTable(context.Sheet);
        return CreatePivotAdvancedWorkflowsContext(
            context.Sheet,
            pivotTable,
            context.SourceRange,
            savedWorkbookPath,
            new FileInfo(savedWorkbookPath).Length,
            "saved");
    }

    private PivotAdvancedWorkflowsTourContext ResolvePivotAdvancedWorkflowsCurrentContext(
        string savedWorkbookPath,
        string persistenceStage)
    {
        var sheet = _workbook.Sheets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "Pivot Advanced", StringComparison.OrdinalIgnoreCase))
            ?? GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Pivot advanced workflows tour could not resolve the reopened worksheet.");

        _currentSheetId = sheet.Id;
        var pivotTable = sheet.PivotTables.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, ScreenshotTourPivotTableName, StringComparison.OrdinalIgnoreCase))
            ?? sheet.PivotTables.FirstOrDefault()
            ?? throw new InvalidOperationException("Pivot advanced workflows tour could not resolve the persisted PivotTable.");
        var savedWorkbookBytes = File.Exists(savedWorkbookPath) ? new FileInfo(savedWorkbookPath).Length : 0;

        SetSelectionRange(new GridRange(pivotTable.TargetRange.Start, pivotTable.TargetRange.Start), pivotTable.TargetRange.Start);
        EnsureCellVisible(pivotTable.TargetRange.Start);
        RefreshPivotFieldListPane();
        UpdateViewport();
        RefreshToolbar();

        return CreatePivotAdvancedWorkflowsContext(sheet, pivotTable, pivotTable.SourceRange, savedWorkbookPath, savedWorkbookBytes, persistenceStage);
    }

    private PivotAdvancedWorkflowsTourContext CreatePivotAdvancedWorkflowsContext(
        Sheet sheet,
        PivotTableModel pivotTable,
        GridRange sourceRange,
        string savedWorkbookPath,
        long savedWorkbookBytes,
        string persistenceStage)
    {
        return new PivotAdvancedWorkflowsTourContext(
            Sheet: sheet,
            PivotTable: pivotTable,
            SourceRange: sourceRange,
            SavedWorkbookPath: savedWorkbookPath,
            SavedWorkbookOutputFileName: string.IsNullOrWhiteSpace(savedWorkbookPath) ? string.Empty : Path.GetFileName(savedWorkbookPath),
            SavedWorkbookBytes: savedWorkbookBytes,
            PersistenceStage: persistenceStage);
    }

    private PivotTableModel ResolvePivotAdvancedWorkflowsPivotTable(Sheet sheet) =>
        FindScreenshotTourPivotTable(sheet)
        ?? sheet.PivotTables.FirstOrDefault()
        ?? throw new InvalidOperationException("Pivot advanced workflows tour could not resolve the PivotTable.");

    private async Task<PivotAdvancedWorkflowsTourManifestCapture> CapturePivotAdvancedLabelFilterDialogAsync(
        string outputDir,
        PivotAdvancedWorkflowsTourContext context)
    {
        var dialog = new PivotLabelFilterDialog(0) { Owner = this };
        try
        {
            dialog.Show();
            dialog.Activate();
            await Task.Delay(300);
            if (FindDescendant<TextBox>(dialog) is { } valueBox)
                valueBox.Text = "o";
            dialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_pivot_advanced_label_filter_dialog");
            return CreatePivotAdvancedWorkflowsCapture(
                context,
                "label-filter-dialog",
                "Pivot Label Filter dialog",
                "freex_pivot_advanced_label_filter_dialog",
                "RenderTargetBitmap-pivot-label-filter-dialog-window",
                dialog.ActualWidth,
                dialog.ActualHeight,
                "Label Filter dialog is prepared for the Region field before the deterministic Contains 'o' filter submission.");
        }
        finally
        {
            dialog.Close();
        }
    }

    private async Task<PivotAdvancedWorkflowsTourManifestCapture> CapturePivotAdvancedValueFilterDialogAsync(
        string outputDir,
        PivotAdvancedWorkflowsTourContext context)
    {
        var dialog = new PivotValueFilterDialog(0) { Owner = this };
        try
        {
            dialog.Show();
            dialog.Activate();
            await Task.Delay(300);
            if (FindDescendant<TextBox>(dialog) is { } valueBox)
                valueBox.Text = "800";
            dialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_pivot_advanced_value_filter_dialog");
            return CreatePivotAdvancedWorkflowsCapture(
                context,
                "value-filter-dialog",
                "Pivot Value Filter dialog",
                "freex_pivot_advanced_value_filter_dialog",
                "RenderTargetBitmap-pivot-value-filter-dialog-window",
                dialog.ActualWidth,
                dialog.ActualHeight,
                "Value Filter dialog is prepared for the Region field before the deterministic Greater Than 800 filter submission.");
        }
        finally
        {
            dialog.Close();
        }
    }

    private async Task<PivotAdvancedWorkflowsTourManifestCapture> CapturePivotAdvancedValueFieldSettingsDialogAsync(
        string outputDir,
        PivotAdvancedWorkflowsTourContext context)
    {
        var headers = PivotSourceContext.ReadHeaders(_workbook, context.PivotTable, context.Sheet);
        var dialog = new PivotValueFieldSettingsDialog(context.PivotTable.DataFields.First(), headers) { Owner = this };
        try
        {
            dialog.Show();
            dialog.Activate();
            await Task.Delay(300);
            if (FindDescendant<TabControl>(dialog) is { } tabs)
                tabs.SelectedIndex = 1;
            dialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, "freex_pivot_advanced_value_field_settings_dialog");
            return CreatePivotAdvancedWorkflowsCapture(
                context,
                "value-field-settings-dialog",
                "Value Field Settings dialog",
                "freex_pivot_advanced_value_field_settings_dialog",
                "RenderTargetBitmap-value-field-settings-dialog-window",
                dialog.ActualWidth,
                dialog.ActualHeight,
                "Value Field Settings dialog shows Show Values As controls before the submitted Avg Sales % Grand Total data-field mutation.");
        }
        finally
        {
            dialog.Close();
        }
    }

    private async Task<PivotAdvancedWorkflowsTourManifestCapture> CapturePivotAdvancedPivotChartFieldButtonMenuAsync(
        string outputDir,
        PivotAdvancedWorkflowsTourContext context)
    {
        var headers = PivotSourceContext.ReadHeaders(_workbook, context.PivotTable, context.Sheet);
        _pivotFieldMenuContextCaption = PivotUiPlanner.ResolvePivotChartFieldButtonCaption(context.PivotTable, headers, "Axis Fields");
        var menu = CreatePivotFieldContextMenu();
        try
        {
            menu.PlacementTarget = SheetGrid;
            menu.Placement = PlacementMode.RelativePoint;
            menu.HorizontalOffset = 720;
            menu.VerticalOffset = 260;
            menu.IsOpen = true;
            await Task.Delay(350);
            menu.UpdateLayout();
            await CaptureElementAsync(menu, outputDir, "freex_pivot_advanced_pivotchart_field_button_menu");

            var menuHeaders = new List<string>();
            AddMenuHeaders(menu, menuHeaders);
            return CreatePivotAdvancedWorkflowsCapture(
                context,
                "pivotchart-field-button-menu",
                "PivotChart field-button menu",
                "freex_pivot_advanced_pivotchart_field_button_menu",
                "RenderTargetBitmap-pivotchart-field-button-context-menu",
                menu.ActualWidth,
                menu.ActualHeight,
                "PivotChart field-button menu uses the production Pivot field context menu after the advanced PivotChart is created.",
                menuHeaders);
        }
        finally
        {
            menu.IsOpen = false;
            _pivotFieldMenuContextCaption = null;
        }
    }

    private async Task<PivotAdvancedWorkflowsTourManifestCapture> CapturePivotAdvancedWorkflowsWindowStateAsync(
        string outputDir,
        PivotAdvancedWorkflowsTourContext context,
        string state,
        string fileName,
        string evidenceSummary,
        string persistenceStage)
    {
        SetSelectionRange(new GridRange(context.PivotTable.TargetRange.Start, context.PivotTable.TargetRange.Start), context.PivotTable.TargetRange.Start);
        EnsureCellVisible(context.PivotTable.TargetRange.Start);
        RefreshPivotFieldListPane();
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.PivotContextTabs.Single(tab => tab.Header == "PivotTable Analyze"));
        UpdateViewport();
        RefreshToolbar();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);
        await CaptureCurrentWindowAsync(outputDir, fileName, 820);
        return CreatePivotAdvancedWorkflowsCapture(context with { PersistenceStage = persistenceStage }, state, "PivotTable worksheet/ribbon state", fileName, "RenderTargetBitmap-window-full", ActualWidth, Math.Min(ActualHeight, 820), evidenceSummary);
    }

    private PivotAdvancedWorkflowsTourManifestCapture CreatePivotAdvancedWorkflowsCapture(
        PivotAdvancedWorkflowsTourContext context,
        string state,
        string surface,
        string fileName,
        string captureMethod,
        double logicalWidth,
        double logicalHeight,
        string evidenceSummary,
        IReadOnlyList<string>? menuHeaders = null)
    {
        return new PivotAdvancedWorkflowsTourManifestCapture(
            CaptureKey: $"interactive:pivot-advanced-workflows:{state}",
            PairKey: $"interactive:pivot-advanced-workflows:{state}",
            ScenarioId: "pivot:advanced-workflows-persistence",
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: logicalWidth,
            CaptureLogicalHeight: logicalHeight,
            SheetName: context.Sheet.Name,
            SelectedRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            PivotTableName: context.PivotTable.Name,
            SourceRange: context.PivotTable.SourceRange.ToString(),
            TargetRange: context.PivotTable.TargetRange.ToString(),
            RowAreaFields: FormatPivotFieldCaptions(context.Sheet, context.PivotTable, context.PivotTable.RowFields),
            ColumnAreaFields: FormatPivotFieldCaptions(context.Sheet, context.PivotTable, context.PivotTable.ColumnFields),
            FilterAreaFields: FormatPivotFieldCaptions(context.Sheet, context.PivotTable, context.PivotTable.PageFields),
            ValueAreaFields: context.PivotTable.DataFields.Select(field => field.Name).ToArray(),
            LabelFilterCount: context.PivotTable.LabelFilters.Count,
            ValueFilterCount: context.PivotTable.ValueFilters.Count,
            SortCount: context.PivotTable.Sorts.Count,
            PivotChartCount: context.Sheet.Charts.Count(chart => chart.IsPivotChart),
            SavedWorkbookOutputFileName: context.SavedWorkbookOutputFileName,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            PersistenceStage: context.PersistenceStage,
            MenuHeaders: menuHeaders ?? [],
            EvidenceSummary: evidenceSummary);
    }

    private string[] FormatPivotFieldCaptions(
        Sheet sheet,
        PivotTableModel pivotTable,
        IReadOnlyList<PivotFieldModel> fields)
    {
        var headers = PivotSourceContext.ReadHeaders(_workbook, pivotTable, sheet);
        return fields.Select(field => PivotUiPlanner.FieldCaption(headers, field.SourceFieldIndex)).ToArray();
    }

    private void ExecutePivotAdvancedWorkflowsCommand(IWorkbookCommand command, string title)
    {
        if (!TryExecuteCommand(command, title, out var outcome))
            throw new InvalidOperationException(outcome.ErrorMessage ?? $"Pivot advanced workflows tour command '{title}' failed.");
    }

    private static void DeletePivotAdvancedWorkflowsTourEvidence(string outputDir)
    {
        foreach (var fileName in PivotAdvancedWorkflowsTourExpectedFileNames()
                     .Append(PivotAdvancedWorkflowsTourSavedWorkbookFileName)
                     .Append(PivotAdvancedWorkflowsTourManifestFileName))
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static IReadOnlyList<string> PivotAdvancedWorkflowsTourExpectedFileNames() =>
    [
        "freex_pivot_advanced_seeded_analyze_layout.png",
        "freex_pivot_advanced_field_layout_mutated.png",
        "freex_pivot_advanced_label_filter_dialog.png",
        "freex_pivot_advanced_value_filter_dialog.png",
        "freex_pivot_advanced_label_value_filters_submitted.png",
        "freex_pivot_advanced_value_field_settings_dialog.png",
        "freex_pivot_advanced_value_field_settings_result.png",
        "freex_pivot_advanced_clear_select_refresh_source_result.png",
        "freex_pivot_advanced_pivotchart_field_button_menu.png",
        "freex_pivot_advanced_saved_xlsx_workbook.png",
        "freex_pivot_advanced_reopened_persisted_pivot.png"
    ];

    private static void ValidatePivotAdvancedWorkflowsTourEvidence(
        string outputDir,
        IReadOnlyList<PivotAdvancedWorkflowsTourManifestCapture> captures,
        string savedWorkbookPath,
        PivotAdvancedWorkflowsTourContext context)
    {
        var missingOrEmpty = captures
            .Select(capture => Path.Combine(outputDir, capture.OutputFileName))
            .Where(path => !File.Exists(path) || new FileInfo(path).Length == 0)
            .Select(Path.GetFileName)
            .ToArray();
        if (missingOrEmpty.Length > 0)
            throw new InvalidOperationException($"Pivot advanced workflows tour did not create non-empty evidence: {string.Join(", ", missingOrEmpty)}.");

        if (!File.Exists(savedWorkbookPath) || new FileInfo(savedWorkbookPath).Length == 0)
            throw new InvalidOperationException("Pivot advanced workflows tour did not retain a non-empty saved XLSX workbook.");

        if (context.PivotTable.DataFields.All(field => !field.Name.Contains("Avg Sales", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Pivot advanced workflows tour reopened workbook without the submitted value field settings.");
    }

    private static async Task WritePivotAdvancedWorkflowsTourManifestAsync(
        string outputDir,
        PivotAdvancedWorkflowsTourContext context,
        IReadOnlyList<PivotAdvancedWorkflowsTourManifestCapture> captures)
    {
        var manifest = new PivotAdvancedWorkflowsTourManifest(
            Tool: "FREEX_PIVOT_ADVANCED_WORKFLOWS_TOUR",
            EvidenceFamily: "pivot-advanced-workflows-persistence",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "pivot:advanced-workflows-persistence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_pivot_advanced_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds:
            [
                "UI-CAT-INSERT-001",
                "UI-CAT-CONTEXT-003",
                "UI-CMD-INSERT-002",
                "UI-CMD-INSERT-003",
                "UI-CMD-CTXOBJ-001"
            ],
            SheetName: context.Sheet.Name,
            PivotTableName: context.PivotTable.Name,
            SourceRange: context.PivotTable.SourceRange.ToString(),
            TargetRange: context.PivotTable.TargetRange.ToString(),
            SavedWorkbookPath: context.SavedWorkbookPath,
            SavedWorkbookOutputFileName: context.SavedWorkbookOutputFileName,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            PersistencePath: "SaveWorkbookToTargetAsync(.xlsx adapter) then OpenFileAsync(saved .xlsx)",
            CaptureStatus: "complete-with-deterministic-submitted-pivot-workflows",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: PivotAdvancedWorkflowsTourExpectedFileNames().Count,
            ActualCaptureCount: captures.Count,
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, physical drag/drop, keytip, dialog access-key, UIA, or screen capture input is used."
                    : "Window/dialog/menu captures abort unless the expected FreeX WPF surface owns foreground focus before RenderTargetBitmap capture."),
            Captures: captures,
            SubmittedMutations:
            [
                "MoveSelectedPivotField(PivotFieldBucket.Rows) exercises the production model-equivalent field layout mutation path for Channel.",
                "ConfigurePivotTableViewCommand applies submitted label and value filters plus label sort.",
                "ConfigurePivotTableLayoutCommand applies the submitted Value Field Settings result for Avg Sales % Grand Total.",
                "ClearPivotTableViewCommand, ChangePivotTableSourceCommand, RefreshPivotTableCommand, and PivotTable selection cover clear/select/refresh/change-source states.",
                "AddPivotChartCommand creates a PivotChart before the field-button context menu state is captured.",
                "SaveWorkbookToTargetAsync writes the authored .xlsx and OpenFileAsync reopens it through the host open path."
            ],
            CoveredStates: captures.Select(capture => capture.State).ToArray(),
            Limitations:
            [
                "This bounded tour opens production FreeX WPF surfaces in process and captures them with RenderTargetBitmap.",
                "It does not synthesize foreground mouse/keytip/dialog access-key/UIA input or physical field-list drag/drop.",
                "Field placement is covered by the same model-equivalent mutation path used by the production field-list controls; physical pointer drag/drop remains a separate foreground-only gap.",
                "Label/value filter and value-field setting submissions are deterministic command submissions after dialog surface capture, not foreground OK-button input proof.",
                "PivotChart field-button menu routing is captured through the production field context menu; rendered chart field-button annotations and hit-test pointer opening remain outside this background-render slice.",
                "No Microsoft Excel counterpart screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, PivotAdvancedWorkflowsTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.PivotAdvancedWorkflowsTourManifest);
    }

    private sealed record PivotAdvancedWorkflowsTourContext(
        Sheet Sheet,
        PivotTableModel PivotTable,
        GridRange SourceRange,
        string SavedWorkbookPath,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string PersistenceStage);

    private sealed record PivotAdvancedWorkflowsTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        string SheetName,
        string PivotTableName,
        string SourceRange,
        string TargetRange,
        string SavedWorkbookPath,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string PersistencePath,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<PivotAdvancedWorkflowsTourManifestCapture> Captures,
        IReadOnlyList<string> SubmittedMutations,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record PivotAdvancedWorkflowsTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SheetName,
        string SelectedRange,
        string PivotTableName,
        string SourceRange,
        string TargetRange,
        IReadOnlyList<string> RowAreaFields,
        IReadOnlyList<string> ColumnAreaFields,
        IReadOnlyList<string> FilterAreaFields,
        IReadOnlyList<string> ValueAreaFields,
        int LabelFilterCount,
        int ValueFilterCount,
        int SortCount,
        int PivotChartCount,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string PersistenceStage,
        IReadOnlyList<string> MenuHeaders,
        string EvidenceSummary);
}
