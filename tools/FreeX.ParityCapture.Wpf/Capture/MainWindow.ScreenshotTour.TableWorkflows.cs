using System.IO;
using System.Text.Json;
using System.Windows;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private async Task CaptureTableWorkflowsTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteTableWorkflowsTourEvidence(outputDir);

        var savedWorkbookPath = Path.Combine(outputDir, TableWorkflowsTourSavedWorkbookFileName);
        DeleteIfExists(savedWorkbookPath);

        WindowState = WindowState.Normal;
        Width = 1220;
        Height = 780;
        await Task.Delay(700);

        var context = EnsureTableWorkflowsTourContext();
        var captures = new List<TableWorkflowsTourManifestCapture>();
        Window? openDialog = null;

        try
        {
            SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Insert"));
            UpdateViewport();
            RefreshToolbar();
            UpdateLayout();
            await WaitForRibbonScreenshotRenderPassAsync();

            openDialog = new CreateTableDialog(
                context.Sheet.Id,
                context.SourceRange.ToString(),
                context.CreateTableStyleName)
            {
                Owner = this
            };
            await ShowInsertTablesChartsTourDialogAsync(openDialog);
            captures.Add(await CaptureTableWorkflowsDialogAsync(
                openDialog,
                outputDir,
                "create-table-dialog",
                "freex_table_workflows_create_table_dialog",
                "Create Table dialog is opened against the seeded range before the deterministic submitted create-table command runs."));
            CloseInsertTablesChartsTourDialog(openDialog);
            openDialog = null;

            context = SubmitTableWorkflowsCreateTable(context);
            captures.Add(await CaptureTableWorkflowsWindowStateAsync(
                outputDir,
                context,
                "create-table-submitted-result",
                "freex_table_workflows_create_table_submitted_result",
                "CreateStyledStructuredTableCommand plus RenameStructuredTableCommand produced the structured table; Table Design is visible for the active table selection.",
                "created"));

            context = SubmitTableWorkflowsFilterTotalsAndStyle(context);
            captures.Add(await CaptureTableWorkflowsWindowStateAsync(
                outputDir,
                context,
                "filter-totals-style-result",
                "freex_table_workflows_filter_totals_style_result",
                "Table filter state, totals row, sum/count totals, TableStyleMedium4, first/last-column emphasis, and row/column striping are visible after submitted table commands.",
                "mutated"));

            context = await SaveTableWorkflowsWorkbookAsync(savedWorkbookPath, context);
            captures.Add(await CaptureTableWorkflowsWindowStateAsync(
                outputDir,
                context,
                "saved-native-workbook",
                "freex_table_workflows_saved_native_workbook",
                "XLSX save completed through SaveWorkbookToTargetAsync while table totals/style/filter state remains visible.",
                "saved"));

            await OpenFileAsync(savedWorkbookPath);
            context = ResolveTableWorkflowsCurrentContext(savedWorkbookPath, "reopened");
            ReapplyTableWorkflowsPersistedFilterIfNeeded(context);
            captures.Add(await CaptureTableWorkflowsWindowStateAsync(
                outputDir,
                context,
                "reopened-persisted-table",
                "freex_table_workflows_reopened_persisted_table",
                "OpenFileAsync reopened the saved .xlsx workbook and restored the structured table name, range, totals row, style, and filter metadata; the filter command is reapplied from persisted metadata if hidden rows need materialization.",
                "reopened"));

            ValidateTableWorkflowsTourEvidence(outputDir, captures, savedWorkbookPath, context);
            await WriteTableWorkflowsTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteTableWorkflowsTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (openDialog is { IsVisible: true })
                CloseInsertTablesChartsTourDialog(openDialog);
        }
    }

    private TableWorkflowsTourContext EnsureTableWorkflowsTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Table workflows tour requires an active worksheet.");

        HideStartScreen();
        _currentSheetId = sheet.Id;
        _currentFilePath = null;
        _workbook.Name = "Table workflows";
        sheet.Name = "Table Workflows";
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

        for (uint row = 1; row <= 14; row++)
        {
            for (uint col = 1; col <= 8; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        SeedTableWorkflowsSourceData(sheet);
        var sourceRange = Range(sheet.Id, 1, 1, 6, 4);
        SetSelectionRange(sourceRange, sourceRange.Start);
        EnsureCellVisible(sourceRange.Start);
        RefreshTableContextualTab();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateViewport();
        UpdateLayout();
        MarkWorkbookDirty();
        UpdateTitleBar();

        return new TableWorkflowsTourContext(
            Sheet: sheet,
            SourceRange: sourceRange,
            TableId: 0,
            TableName: ScreenshotTourTableName,
            CreateTableStyleName: "TableStyleMedium2",
            FinalTableStyleName: "TableStyleMedium4",
            SavedWorkbookPath: string.Empty,
            SavedWorkbookOutputFileName: string.Empty,
            SavedWorkbookBytes: 0,
            PersistenceStage: "seeded");
    }

    private static void SeedTableWorkflowsSourceData(Sheet sheet)
    {
        var cells = new (uint Row, uint Col, ScalarValue Value)[]
        {
            (1, 1, new TextValue("Region")),
            (1, 2, new TextValue("Sales")),
            (1, 3, new TextValue("Orders")),
            (1, 4, new TextValue("Status")),
            (2, 1, new TextValue("North")),
            (2, 2, new NumberValue(1280)),
            (2, 3, new NumberValue(4)),
            (2, 4, new TextValue("Open")),
            (3, 1, new TextValue("South")),
            (3, 2, new NumberValue(960)),
            (3, 3, new NumberValue(3)),
            (3, 4, new TextValue("Closed")),
            (4, 1, new TextValue("West")),
            (4, 2, new NumberValue(1140)),
            (4, 3, new NumberValue(2)),
            (4, 4, new TextValue("Hold")),
            (5, 1, new TextValue("East")),
            (5, 2, new NumberValue(1410)),
            (5, 3, new NumberValue(5)),
            (5, 4, new TextValue("Open")),
            (6, 1, new TextValue("North")),
            (6, 2, new NumberValue(1510)),
            (6, 3, new NumberValue(6)),
            (6, 4, new TextValue("Open")),
            (8, 1, new TextValue("Below-table persistence guard")),
            (9, 1, new TextValue("Rows hidden by the table filter should not erase this note."))
        };

        foreach (var (row, col, value) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, row, col), value);
    }

    private TableWorkflowsTourContext SubmitTableWorkflowsCreateTable(TableWorkflowsTourContext context)
    {
        if (!TableStyleGalleryPlanner.TryGetOption(context.CreateTableStyleName, _workbook.Theme, out var option))
            option = TableStyleGalleryPlanner.GetOption(0, _workbook.Theme);

        ExecuteTableWorkflowsCommand(
            new CreateStyledStructuredTableCommand(
                context.Sheet.Id,
                context.SourceRange,
                context.CreateTableStyleName,
                firstRowHasHeaders: true,
                option.Banding),
            "Create Table");

        var createdTable = context.Sheet.StructuredTables.Single(table => table.Range.Equals(context.SourceRange));
        ExecuteTableWorkflowsCommand(
            new RenameStructuredTableCommand(context.Sheet.Id, createdTable.Id, ScreenshotTourTableName),
            "Rename Table");

        var table = FindTableWorkflowsTable(context.Sheet)
            ?? throw new InvalidOperationException("Table workflows tour could not resolve the created table.");

        SetSelectionRange(new GridRange(new CellAddress(context.Sheet.Id, 2, 2), new CellAddress(context.Sheet.Id, 2, 2)), new CellAddress(context.Sheet.Id, 2, 2));
        RefreshTableContextualTab();
        RefreshToolbar();
        UpdateViewport();
        MarkWorkbookDirty();
        UpdateTitleBar();

        return context with { TableId = table.Id };
    }

    private TableWorkflowsTourContext SubmitTableWorkflowsFilterTotalsAndStyle(TableWorkflowsTourContext context)
    {
        var table = FindTableWorkflowsTable(context.Sheet)
            ?? throw new InvalidOperationException("Table workflows tour could not resolve the table before mutation.");

        ConfigureTableWorkflowsTotalsAndFilterMetadata(table);
        ExecuteTableWorkflowsCommand(
            new ApplyStructuredTableFiltersCommand(context.Sheet.Id, table.Id),
            "Apply Table Filter");

        ExecuteTableWorkflowsCommand(
            new SetStructuredTableTotalsRowCommand(context.Sheet.Id, table.Id, showTotalsRow: true),
            "Show Table Totals Row");

        table = FindTableWorkflowsTable(context.Sheet)
            ?? throw new InvalidOperationException("Table workflows tour could not resolve the table after totals row command.");

        if (!TableStyleGalleryPlanner.TryGetOption(context.FinalTableStyleName, _workbook.Theme, out var option))
            option = TableStyleGalleryPlanner.GetOption(1, _workbook.Theme);

        ExecuteTableWorkflowsCommand(
            new ApplyStructuredTableStyleCommand(
                context.Sheet.Id,
                table.Id,
                option.Banding,
                context.FinalTableStyleName,
                updateStyleName: true,
                showFirstColumn: true,
                showLastColumn: true,
                showRowStripes: true,
                showColumnStripes: true,
                hasAutoFilter: true),
            "Apply Table Style");

        SetSelectionRange(new GridRange(new CellAddress(context.Sheet.Id, 2, 2), new CellAddress(context.Sheet.Id, 2, 2)), new CellAddress(context.Sheet.Id, 2, 2));
        RefreshTableContextualTab();
        RefreshToolbar();
        UpdateViewport();
        MarkWorkbookDirty();
        UpdateTitleBar();

        return context with { TableId = table.Id };
    }

    private static void ConfigureTableWorkflowsTotalsAndFilterMetadata(StructuredTableModel table)
    {
        table.Columns.Clear();
        table.Columns.Add(new StructuredTableColumnModel(1, "Region", TotalsRowLabel: "Total"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Sales", TotalsRowFunction: "sum"));
        table.Columns.Add(new StructuredTableColumnModel(3, "Orders", TotalsRowFunction: "count"));
        table.Columns.Add(new StructuredTableColumnModel(4, "Status", TotalsRowFunction: "count"));

        table.FilterColumns.Clear();
        table.FilterColumns.Add(new StructuredTableFilterColumnModel(0, ["North", "East"]));
    }

    private void ExecuteTableWorkflowsCommand(IWorkbookCommand command, string title)
    {
        if (!TryExecuteCommand(command, title, out var outcome))
            throw new InvalidOperationException(outcome.ErrorMessage ?? $"Table workflows tour command '{title}' failed.");
    }

    private async Task<TableWorkflowsTourContext> SaveTableWorkflowsWorkbookAsync(
        string savedWorkbookPath,
        TableWorkflowsTourContext context)
    {
        var adapter = FileFormatResolver.FindSaveAdapter(_fileAdapters, ".xlsx", out _)
            ?? throw new InvalidOperationException("Table workflows tour could not find the XLSX save adapter.");
        if (!await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter)))
            throw new InvalidOperationException("Table workflows tour could not save the XLSX workbook.");

        return context with
        {
            SavedWorkbookPath = savedWorkbookPath,
            SavedWorkbookOutputFileName = Path.GetFileName(savedWorkbookPath),
            SavedWorkbookBytes = new FileInfo(savedWorkbookPath).Length,
            PersistenceStage = "saved"
        };
    }

    private TableWorkflowsTourContext ResolveTableWorkflowsCurrentContext(string savedWorkbookPath, string persistenceStage)
    {
        var sheet = _workbook.Sheets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "Table Workflows", StringComparison.OrdinalIgnoreCase))
            ?? GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Table workflows tour could not resolve the reopened worksheet.");

        _currentSheetId = sheet.Id;
        var table = FindTableWorkflowsTable(sheet)
            ?? sheet.StructuredTables.FirstOrDefault()
            ?? throw new InvalidOperationException("Table workflows tour could not resolve the persisted structured table.");

        return new TableWorkflowsTourContext(
            Sheet: sheet,
            SourceRange: table.Range,
            TableId: table.Id,
            TableName: table.Name,
            CreateTableStyleName: "TableStyleMedium2",
            FinalTableStyleName: table.StyleName ?? "TableStyleMedium4",
            SavedWorkbookPath: savedWorkbookPath,
            SavedWorkbookOutputFileName: Path.GetFileName(savedWorkbookPath),
            SavedWorkbookBytes: File.Exists(savedWorkbookPath) ? new FileInfo(savedWorkbookPath).Length : 0,
            PersistenceStage: persistenceStage);
    }

    private void ReapplyTableWorkflowsPersistedFilterIfNeeded(TableWorkflowsTourContext context)
    {
        var table = FindTableWorkflowsTable(context.Sheet)
            ?? throw new InvalidOperationException("Table workflows tour could not resolve the persisted table before filter materialization.");
        if (table.FilterColumns.Count == 0 || context.Sheet.FilterHiddenRows.Count > 0)
            return;

        ExecuteTableWorkflowsCommand(
            new ApplyStructuredTableFiltersCommand(context.Sheet.Id, table.Id),
            "Reapply Persisted Table Filter");
        RefreshTableContextualTab();
        RefreshToolbar();
        UpdateViewport();
    }

    private async Task<TableWorkflowsTourManifestCapture> CaptureTableWorkflowsDialogAsync(
        Window dialog,
        string outputDir,
        string state,
        string fileName,
        string evidenceSummary)
    {
        await CaptureElementAsync(dialog, outputDir, fileName);
        return new TableWorkflowsTourManifestCapture(
            CaptureKey: $"table-workflows:{state}",
            PairKey: $"interactive:table-workflows:{state}",
            CatalogIds: ["UI-CAT-HOME-003", "UI-CAT-INSERT-001", "UI-CAT-INSERT-001D", "UI-CMD-HOME-STYLE-002", "UI-CMD-INSERT-004"],
            State: state,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "RenderTargetBitmap-dialog",
            CaptureLogicalWidth: dialog.ActualWidth,
            CaptureLogicalHeight: dialog.ActualHeight,
            SheetName: _workbook.GetSheet(_currentSheetId)?.Name ?? string.Empty,
            SelectedRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            TableName: string.Empty,
            TableRange: string.Empty,
            TableStyleName: string.Empty,
            HasAutoFilter: false,
            TotalsRowShown: false,
            FilterColumnCount: 0,
            FilterHiddenRowCount: 0,
            TotalsRegionLabel: string.Empty,
            SalesTotalValue: string.Empty,
            OrdersTotalValue: string.Empty,
            StatusTotalValue: string.Empty,
            PersistenceStage: "dialog",
            SavedWorkbookOutputFileName: string.Empty,
            SavedWorkbookBytes: 0,
            EvidenceSummary: evidenceSummary);
    }

    private async Task<TableWorkflowsTourManifestCapture> CaptureTableWorkflowsWindowStateAsync(
        string outputDir,
        TableWorkflowsTourContext context,
        string state,
        string fileName,
        string evidenceSummary,
        string persistenceStage)
    {
        SelectRibbonTourTab(new RibbonScreenshotTourTab("Table Design", "Table_Design", "TableDesignTab"));
        SetSelectionRange(new GridRange(new CellAddress(context.Sheet.Id, 2, 2), new CellAddress(context.Sheet.Id, 2, 2)), new CellAddress(context.Sheet.Id, 2, 2));
        RefreshTableContextualTab();
        UpdateViewport();
        RefreshToolbar();
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);
        await CaptureCurrentWindowAsync(outputDir, fileName, 780);
        return CreateTableWorkflowsCapture(context, state, fileName, evidenceSummary, persistenceStage);
    }

    private TableWorkflowsTourManifestCapture CreateTableWorkflowsCapture(
        TableWorkflowsTourContext context,
        string state,
        string fileName,
        string evidenceSummary,
        string persistenceStage)
    {
        var table = FindTableWorkflowsTable(context.Sheet) ?? context.Sheet.StructuredTables.FirstOrDefault();
        var totalsRow = table?.Range.End.Row ?? 0;
        return new TableWorkflowsTourManifestCapture(
            CaptureKey: $"table-workflows:{state}",
            PairKey: $"interactive:table-workflows:{state}",
            CatalogIds: ["UI-CAT-HOME-003", "UI-CAT-INSERT-001", "UI-CAT-INSERT-001D", "UI-CMD-HOME-STYLE-002", "UI-CMD-INSERT-004"],
            State: state,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "RenderTargetBitmap-window-full",
            CaptureLogicalWidth: ActualWidth,
            CaptureLogicalHeight: Math.Min(ActualHeight, 780),
            SheetName: context.Sheet.Name,
            SelectedRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            TableName: table?.Name ?? string.Empty,
            TableRange: table?.Range.ToString() ?? string.Empty,
            TableStyleName: table?.StyleName ?? string.Empty,
            HasAutoFilter: table?.HasAutoFilter ?? false,
            TotalsRowShown: table?.TotalsRowShown ?? false,
            FilterColumnCount: table?.FilterColumns.Count ?? 0,
            FilterHiddenRowCount: context.Sheet.FilterHiddenRows.Count,
            TotalsRegionLabel: totalsRow == 0 ? string.Empty : ScalarValueToDisplayText(context.Sheet.GetValue(totalsRow, 1)),
            SalesTotalValue: totalsRow == 0 ? string.Empty : ScalarValueToDisplayText(context.Sheet.GetValue(totalsRow, 2)),
            OrdersTotalValue: totalsRow == 0 ? string.Empty : ScalarValueToDisplayText(context.Sheet.GetValue(totalsRow, 3)),
            StatusTotalValue: totalsRow == 0 ? string.Empty : ScalarValueToDisplayText(context.Sheet.GetValue(totalsRow, 4)),
            PersistenceStage: persistenceStage,
            SavedWorkbookOutputFileName: context.SavedWorkbookOutputFileName,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            EvidenceSummary: evidenceSummary);
    }

    private static string ScalarValueToDisplayText(ScalarValue value) =>
        value switch
        {
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            DateTimeValue dateTime => dateTime.ToDateTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ErrorValue error => error.Code,
            _ => string.Empty
        };

    private static StructuredTableModel? FindTableWorkflowsTable(Sheet sheet) =>
        sheet.StructuredTables.FirstOrDefault(table =>
            string.Equals(table.Name, ScreenshotTourTableName, StringComparison.OrdinalIgnoreCase));

    private static void DeleteTableWorkflowsTourEvidence(string outputDir)
    {
        if (!Directory.Exists(outputDir))
            return;

        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_table_workflows_*.png"))
            File.Delete(file);

        DeleteIfExists(Path.Combine(outputDir, TableWorkflowsTourManifestFileName));
        DeleteIfExists(Path.Combine(outputDir, TableWorkflowsTourSavedWorkbookFileName));
    }

    private static void ValidateTableWorkflowsTourEvidence(
        string outputDir,
        IReadOnlyList<TableWorkflowsTourManifestCapture> captures,
        string savedWorkbookPath,
        TableWorkflowsTourContext reopenedContext)
    {
        if (captures.Count != 5)
            throw new InvalidOperationException($"Table workflows tour expected 5 captures but created {captures.Count}.");

        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Table workflows tour did not create planned capture(s): {string.Join(", ", missing)}.");

        var blank = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !IsNonBlankPng(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (blank.Length > 0)
            throw new InvalidOperationException($"Table workflows tour created blank capture(s): {string.Join(", ", blank)}.");

        if (!File.Exists(savedWorkbookPath) || new FileInfo(savedWorkbookPath).Length <= 0)
            throw new InvalidOperationException("Table workflows tour did not retain a non-empty XLSX workbook.");

        var table = FindTableWorkflowsTable(reopenedContext.Sheet)
            ?? throw new InvalidOperationException("Table workflows tour reopened workbook without the named structured table.");
        if (!table.TotalsRowShown || !string.Equals(table.StyleName, "TableStyleMedium4", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Table workflows tour reopened workbook without the expected totals row and table style.");
        if (table.FilterColumns.Count == 0)
            throw new InvalidOperationException("Table workflows tour reopened workbook without the expected table filter metadata.");
    }

    private static async Task WriteTableWorkflowsTourManifestAsync(
        string outputDir,
        TableWorkflowsTourContext context,
        IReadOnlyList<TableWorkflowsTourManifestCapture> captures)
    {
        var manifest = new TableWorkflowsTourManifest(
            Tool: "FREEX_TABLE_WORKFLOWS_TOUR",
            EvidenceFamily: "table-workflows-totals-persistence",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "table:workflows-totals-persistence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_table_workflows_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds: ["UI-CAT-HOME-003", "UI-CAT-INSERT-001", "UI-CAT-INSERT-001D", "UI-CMD-HOME-STYLE-002", "UI-CMD-INSERT-004"],
            SheetName: context.Sheet.Name,
            SourceRange: context.SourceRange.ToString(),
            TableName: context.TableName,
            CreateTableStyleName: context.CreateTableStyleName,
            FinalTableStyleName: context.FinalTableStyleName,
            SavedWorkbookPath: context.SavedWorkbookPath,
            SavedWorkbookOutputFileName: context.SavedWorkbookOutputFileName,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            PersistencePath: "SaveWorkbookToTargetAsync(.xlsx adapter) then OpenFileAsync(saved .xlsx)",
            CaptureStatus: "complete-with-deterministic-seeded-totals-metadata",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: captures.Count,
            ActualCaptureCount: captures.Count,
            Pairing: new TableWorkflowsTourManifestPairing(
                "interactive:table-workflows:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures; no global mouse, keyboard, keytip, range-picker, native save dialog, UIA, or screen capture input is used."
                    : "Window and dialog captures abort unless the expected FreeX WPF surface owns foreground focus before RenderTargetBitmap capture; native save/open dialogs are not opened by this deterministic tour."),
            Captures: captures,
            SubmittedMutations:
            [
                "CreateStyledStructuredTableCommand creates the table from the seeded source range.",
                "RenameStructuredTableCommand assigns the deterministic table name used for save/reopen resolution.",
                "ApplyStructuredTableFiltersCommand applies a table filter with North/East visible rows.",
                "SetStructuredTableTotalsRowCommand inserts and materializes the totals row.",
                "ApplyStructuredTableStyleCommand applies TableStyleMedium4 plus first/last-column and row/column stripe options.",
                "SaveWorkbookToTargetAsync writes the .xlsx workbook and OpenFileAsync reloads it through the host open path.",
                "After reopen, ApplyStructuredTableFiltersCommand re-materializes hidden rows from persisted table filter metadata when needed."
            ],
            SeededMetadata:
            [
                "TotalsRowLabel/Function metadata is seeded directly on the structured table columns because FreeX does not yet expose an in-app totals-function dropdown submission route.",
                "The filter criteria metadata is seeded directly on the structured table before ApplyStructuredTableFiltersCommand is submitted."
            ],
            CoveredStates:
            [
                "Create Table dialog with seeded range",
                "Submitted Create Table result with Table Design contextual tab",
                "Filtered table visual state with totals row and style options",
                "XLSX saved workbook state",
                "XLSX reopen proof for table name/range/totals/style plus filter metadata re-materialized for visual state"
            ],
            Limitations:
            [
                "This bounded tour opens production FreeX WPF surfaces in process and captures them with RenderTargetBitmap.",
                "It does not synthesize foreground mouse/keytip/Ctrl+T/range-picker/dialog access-key/UIA input.",
                "Totals function dropdown selection and filter dropdown selection remain seeded metadata plus submitted command evidence, not foreground dropdown interaction proof.",
                "No Microsoft Excel counterpart screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, TableWorkflowsTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.TableWorkflowsTourManifest);
    }

    private sealed record TableWorkflowsTourContext(
        Sheet Sheet,
        GridRange SourceRange,
        int TableId,
        string TableName,
        string CreateTableStyleName,
        string FinalTableStyleName,
        string SavedWorkbookPath,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string PersistenceStage);

    private sealed record TableWorkflowsTourManifest(
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
        string SourceRange,
        string TableName,
        string CreateTableStyleName,
        string FinalTableStyleName,
        string SavedWorkbookPath,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string PersistencePath,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        TableWorkflowsTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<TableWorkflowsTourManifestCapture> Captures,
        IReadOnlyList<string> SubmittedMutations,
        IReadOnlyList<string> SeededMetadata,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record TableWorkflowsTourManifestPairing(
        string PairKeyTemplate,
        string CounterpartApp,
        string CounterpartTool,
        string CounterpartStatus);

    private sealed record TableWorkflowsTourManifestCapture(
        string CaptureKey,
        string PairKey,
        IReadOnlyList<string> CatalogIds,
        string State,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SheetName,
        string SelectedRange,
        string TableName,
        string TableRange,
        string TableStyleName,
        bool HasAutoFilter,
        bool TotalsRowShown,
        int FilterColumnCount,
        int FilterHiddenRowCount,
        string TotalsRegionLabel,
        string SalesTotalValue,
        string OrdersTotalValue,
        string StatusTotalValue,
        string PersistenceStage,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string EvidenceSummary);
}
