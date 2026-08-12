using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    // Activated by FREEX_SHEET_TAB_WORKFLOWS_TOUR=1 env var. Output lands in <repo-root>/screenshots/sheet-tab-workflows-tour/.
    private void TryStartSheetTabWorkflowsTour()
    {
        if (Environment.GetEnvironmentVariable("FREEX_SHEET_TAB_WORKFLOWS_TOUR") != "1")
            return;

        var outputDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "screenshots", SheetTabWorkflowsTourOutputDirectoryName));
        Directory.CreateDirectory(outputDir);
        _ = RunSheetTabWorkflowsTourAsync(outputDir);
    }

    private async Task RunSheetTabWorkflowsTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteSheetTabWorkflowsTourEvidence(outputDir);
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        WindowState = WindowState.Normal;
        Width = 1220;
        Height = 760;
        await Task.Delay(700);

        var captures = new List<SheetTabWorkflowsTourManifestCapture>();
        var workflows = new List<SheetTabWorkflowsTourManifestWorkflow>();
        var savedWorkbookPath = Path.Combine(outputDir, SheetTabWorkflowsTourSavedWorkbookFileName);

        try
        {
            var context = EnsureSheetTabWorkflowsTourContext();
            captures.Add(await CaptureSheetTabWorkflowsWindowAsync(
                outputDir,
                "seeded-before-workflows",
                "freex_sheet_tab_workflows_seeded_before",
                "Seeded workbook before submitted sheet-tab workflows, with Summary, Inputs, Archive, and Review sheets visible.",
                "Setup seeded workbook; no submitted command yet."));

            InsertNewSheet();
            await WaitForSheetTabWorkflowsRenderAsync();
            var insertedSheet = _workbook.Sheets[^1];
            captures.Add(await CaptureSheetTabWorkflowsWindowAsync(
                outputDir,
                "insert-sheet-result",
                "freex_sheet_tab_workflows_insert_sheet_result",
                "Insert Sheet result shows the newly added sheet selected through the host InsertNewSheet/AddSheetCommand path.",
                "InsertNewSheet() -> TryExecuteRepeatableCommand(AddSheetCommand)"));
            workflows.Add(CreateCapturedSheetTabWorkflow(
                "Insert sheet submitted result",
                ["UI-CAT-SHEETTAB-001C", "UI-CAT-SHEETTAB-002A-J"],
                "InsertNewSheet() -> AddSheetCommand",
                "insert-sheet-result"));

            ExecuteSheetTabWorkflowsCommand(
                new RenameSheetCommand(insertedSheet.Id, "Submitted Plan"),
                "Rename Sheet");
            _currentSheetId = insertedSheet.Id;
            RefreshSheetTabs();
            captures.Add(await CaptureSheetTabWorkflowsWindowAsync(
                outputDir,
                "rename-submitted-result",
                "freex_sheet_tab_workflows_rename_submitted_result",
                "Rename submitted result shows the inserted sheet renamed to Submitted Plan through RenameSheetCommand.",
                "RenameSheetCommand(insertedSheet.Id, \"Submitted Plan\")"));
            workflows.Add(CreateCapturedSheetTabWorkflow(
                "Rename submitted result",
                ["UI-CAT-SHEETTAB-001C", "UI-CAT-SHEETTAB-002A-J"],
                "RenameSheetCommand",
                "rename-submitted-result"));

            var moveCopySource = context.SummarySheet;
            var sourceIndex = FindWorkbookSheetIndex(moveCopySource.Id);
            var postCopySheetCount = _workbook.Sheets.Count + 1;
            var copyIndex = Math.Min(sourceIndex + 1, postCopySheetCount - 1);
            var targetIndex = postCopySheetCount - 1;
            ExecuteSheetTabWorkflowsCommand(
                new CompositeWorkbookCommand(
                    "Move or Copy Sheet",
                    [
                        new DuplicateSheetCommand(moveCopySource.Id),
                        new MoveSheetCommand(copyIndex, targetIndex)
                    ]),
                "Move or Copy Sheet");
            var copySheet = _workbook.Sheets[targetIndex];
            ExecuteSheetTabWorkflowsCommand(new RenameSheetCommand(copySheet.Id, "Summary Copy"), "Rename Sheet");
            _currentSheetId = copySheet.Id;
            _groupedSheetIds.Clear();
            _groupedSheetIds.Add(_currentSheetId);
            _sheetGroupAnchor = _currentSheetId;
            RefreshSheetTabs();
            captures.Add(await CaptureSheetTabWorkflowsWindowAsync(
                outputDir,
                "move-or-copy-result",
                "freex_sheet_tab_workflows_move_or_copy_result",
                "Move or Copy result shows a copied Summary sheet moved to the end of the workbook order through the same single CompositeWorkbookCommand route used after dialog submission.",
                "CompositeWorkbookCommand(DuplicateSheetCommand(sourceSheet.Id), MoveSheetCommand(copyIndex, lastIndex)) -> RenameSheetCommand(copySheet.Id, \"Summary Copy\")"));
            workflows.Add(CreateCapturedSheetTabWorkflow(
                "Move or Copy create-copy result",
                ["UI-CAT-SHEETTAB-001B", "UI-CAT-SHEETTAB-002A-J"],
                "Single CompositeWorkbookCommand for Move or Copy create-copy",
                "move-or-copy-result"));

            ExecuteSheetTabWorkflowsCommand(
                new SetSheetTabColorCommand(insertedSheet.Id, new CellColor(255, 192, 0)),
                "Tab Color");
            _currentSheetId = insertedSheet.Id;
            RefreshSheetTabs();
            captures.Add(await CaptureSheetTabWorkflowsWindowAsync(
                outputDir,
                "tab-color-applied-result",
                "freex_sheet_tab_workflows_tab_color_applied",
                "Tab Color result shows Submitted Plan with an applied amber tab color through SetSheetTabColorCommand.",
                "SetSheetTabColorCommand(insertedSheet.Id, CellColor(255, 192, 0))"));
            workflows.Add(CreateCapturedSheetTabWorkflow(
                "Tab color applied result",
                ["UI-CAT-SHEETTAB-001D", "UI-CAT-SHEETTAB-002A-J"],
                "SetSheetTabColorCommand",
                "tab-color-applied-result"));

            ExecuteSheetTabWorkflowsCommand(new SetSheetHiddenCommand(context.ArchiveSheet.Id, hidden: true), "Hide Sheet");
            _currentSheetId = insertedSheet.Id;
            _groupedSheetIds.Clear();
            _groupedSheetIds.Add(_currentSheetId);
            _sheetGroupAnchor = _currentSheetId;
            RefreshSheetTabs();
            captures.Add(await CaptureSheetTabWorkflowsWindowAsync(
                outputDir,
                "hide-sheet-result",
                "freex_sheet_tab_workflows_hide_sheet_result",
                "Hide Sheet result excludes Archive from the visible tab strip while the workbook still contains the hidden sheet.",
                "SetSheetHiddenCommand(archiveSheet.Id, hidden: true)"));
            workflows.Add(CreateCapturedSheetTabWorkflow(
                "Hide sheet result",
                ["UI-CAT-SHEETTAB-001D", "UI-CAT-SHEETTAB-002A-J"],
                "SetSheetHiddenCommand(hidden: true)",
                "hide-sheet-result"));

            ExecuteSheetTabWorkflowsCommand(new SetSheetHiddenCommand(context.ArchiveSheet.Id, hidden: false), "Unhide Sheet");
            _currentSheetId = context.ArchiveSheet.Id;
            _groupedSheetIds.Clear();
            _groupedSheetIds.Add(_currentSheetId);
            _sheetGroupAnchor = _currentSheetId;
            RefreshSheetTabs();
            captures.Add(await CaptureSheetTabWorkflowsWindowAsync(
                outputDir,
                "unhide-sheet-result",
                "freex_sheet_tab_workflows_unhide_sheet_result",
                "Unhide Sheet result restores Archive to the visible tab strip through SetSheetHiddenCommand.",
                "SetSheetHiddenCommand(archiveSheet.Id, hidden: false)"));
            workflows.Add(CreateCapturedSheetTabWorkflow(
                "Unhide sheet result",
                ["UI-CAT-SHEETTAB-001D", "UI-CAT-SHEETTAB-002A-J"],
                "SetSheetHiddenCommand(hidden: false)",
                "unhide-sheet-result"));

            _currentSheetId = insertedSheet.Id;
            SheetCtxSelectAllSheets_Click(this, new RoutedEventArgs());
            captures.Add(await CaptureSheetTabWorkflowsWindowAsync(
                outputDir,
                "select-all-sheets-result",
                "freex_sheet_tab_workflows_select_all_sheets_result",
                "Select All Sheets result shows all visible sheets grouped through the production sheet-tab context handler.",
                "SheetCtxSelectAllSheets_Click -> SheetGroupSelectionService.SelectAll"));
            workflows.Add(CreateCapturedSheetTabWorkflow(
                "Select All Sheets grouping result",
                ["UI-CAT-SHEETTAB-001A", "UI-CAT-SHEETTAB-001D", "UI-CAT-SHEETTAB-002A-J"],
                "SheetCtxSelectAllSheets_Click",
                "select-all-sheets-result"));

            SheetCtxUngroupSheets_Click(this, new RoutedEventArgs());
            captures.Add(await CaptureSheetTabWorkflowsWindowAsync(
                outputDir,
                "ungroup-sheets-result",
                "freex_sheet_tab_workflows_ungroup_sheets_result",
                "Ungroup Sheets result restores single-sheet targeting through the production sheet-tab context handler.",
                "SheetCtxUngroupSheets_Click"));
            workflows.Add(CreateCapturedSheetTabWorkflow(
                "Ungroup Sheets result",
                ["UI-CAT-SHEETTAB-001A", "UI-CAT-SHEETTAB-001D", "UI-CAT-SHEETTAB-002A-J"],
                "SheetCtxUngroupSheets_Click",
                "ungroup-sheets-result"));

            if (File.Exists(savedWorkbookPath))
                File.Delete(savedWorkbookPath);
            var adapter = FileFormatResolver.FindSaveAdapter(_fileAdapters, ".xlsx", out _)
                ?? throw new InvalidOperationException("Sheet-tab workflows tour could not find an XLSX save adapter.");
            if (!await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter)))
                throw new InvalidOperationException("Sheet-tab workflows tour could not save the workflow workbook.");
            captures.Add(await CaptureSheetTabWorkflowsWindowAsync(
                outputDir,
                "save-persistence-path-result",
                "freex_sheet_tab_workflows_save_persistence_path",
                "Save result uses SaveWorkbookToTargetAsync to persist the renamed, moved, colored, and ungrouped workbook to an XLSX path.",
                "SaveWorkbookToTargetAsync(FileSaveTarget(savedWorkbookPath, xlsxAdapter))"));
            workflows.Add(CreateCapturedSheetTabWorkflow(
                "Save persistence path",
                ["UI-CAT-SHEETTAB-001C", "UI-CAT-SHEETTAB-001D", "UI-CAT-SHEETTAB-002A-J"],
                "SaveWorkbookToTargetAsync",
                "save-persistence-path-result"));

            await OpenFileAsync(savedWorkbookPath);
            var reopenedSubmittedSheet = _workbook.Sheets.FirstOrDefault(sheet => sheet.Name == "Submitted Plan")
                ?? throw new InvalidOperationException("Sheet-tab workflows tour could not find Submitted Plan after reopen.");
            _currentSheetId = reopenedSubmittedSheet.Id;
            _groupedSheetIds.Clear();
            _groupedSheetIds.Add(_currentSheetId);
            _sheetGroupAnchor = _currentSheetId;
            RefreshSheetTabs();
            captures.Add(await CaptureSheetTabWorkflowsWindowAsync(
                outputDir,
                "reopened-persistence-result",
                "freex_sheet_tab_workflows_reopened_persistence_result",
                "Reopened persistence result shows Submitted Plan, Summary Copy, restored Archive visibility, and Submitted Plan tab color after OpenFileAsync loaded the saved workbook.",
                "OpenFileAsync(savedWorkbookPath) -> WorkbookOpenService/XlsxFileAdapter"));
            workflows.Add(CreateCapturedSheetTabWorkflow(
                "Reopen persistence proof",
                ["UI-CAT-SHEETTAB-001C", "UI-CAT-SHEETTAB-001D", "UI-CAT-SHEETTAB-002A-J"],
                "OpenFileAsync -> WorkbookOpenService/XlsxFileAdapter",
                "reopened-persistence-result"));

            ValidateSheetTabWorkflowsTourEvidence(outputDir, captures);
            await WriteSheetTabWorkflowsTourManifestAsync(outputDir, savedWorkbookPath, captures, workflows);
        }
        catch
        {
            DeleteSheetTabWorkflowsTourEvidence(outputDir);
            throw;
        }

        _suppressClosePrompt = true;
        Application.Current.Shutdown();
    }

    private SheetTabWorkflowsTourContext EnsureSheetTabWorkflowsTourContext()
    {
        CreateNewWorkbook();
        HideStartScreen();

        var summary = _workbook.Sheets[0];
        summary.Name = "Summary";
        var inputs = _workbook.AddSheet("Inputs");
        var archive = _workbook.AddSheet("Archive");
        var review = _workbook.AddSheet("Review");

        SeedSheetTabWorkflowsSheet(summary, "Summary", "Ready");
        SeedSheetTabWorkflowsSheet(inputs, "Inputs", "Open");
        SeedSheetTabWorkflowsSheet(archive, "Archive", "Hidden later");
        SeedSheetTabWorkflowsSheet(review, "Review", "Waiting");

        summary.TabColor = new CellColor(91, 155, 213);
        inputs.TabColor = new CellColor(112, 173, 71);

        _currentSheetId = summary.Id;
        _groupedSheetIds.Clear();
        _groupedSheetIds.Add(_currentSheetId);
        _sheetGroupAnchor = _currentSheetId;
        SetSelectionRange(Range(summary.Id, 1, 1, 6, 4), new CellAddress(summary.Id, 1, 1));
        EnsureCellVisible(new CellAddress(summary.Id, 1, 1));
        RefreshSheetTabs();
        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();

        return new SheetTabWorkflowsTourContext(summary, inputs, archive, review);
    }

    private static void SeedSheetTabWorkflowsSheet(Sheet sheet, string title, string status)
    {
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Sheet"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Workflow"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue(title));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Sheet-tab workflow proof"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), new TextValue(status));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("Persisted marker"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue($"{title} tab-state evidence"));
    }

    private void ExecuteSheetTabWorkflowsCommand(IWorkbookCommand command, string title)
    {
        if (!TryExecuteCommand(command, title, out var outcome))
            throw new InvalidOperationException(outcome.ErrorMessage ?? $"Sheet-tab workflows tour command '{title}' failed.");

        UpdateViewport();
        RefreshSheetTabs();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private async Task WaitForSheetTabWorkflowsRenderAsync()
    {
        UpdateViewport();
        RefreshSheetTabs();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateLayout();
        await Task.Delay(300);
        await WaitForRibbonScreenshotRenderPassAsync();
    }

    private async Task<SheetTabWorkflowsTourManifestCapture> CaptureSheetTabWorkflowsWindowAsync(
        string outputDir,
        string state,
        string fileName,
        string evidenceSummary,
        string commandRoute)
    {
        HideStartScreen();
        await WaitForSheetTabWorkflowsRenderAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);

        return new SheetTabWorkflowsTourManifestCapture(
            CaptureKey: $"sheet-tab-workflows:{state}",
            PairKey: $"interactive:sheet-tab-workflows:{state}",
            State: state,
            Surface: "main-window-grid-and-sheet-tab-strip",
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: "RenderTargetBitmap-main-window",
            CommandRoute: commandRoute,
            EvidenceSummary: evidenceSummary,
            ActiveSheetName: _workbook.GetSheet(_currentSheetId)?.Name ?? "",
            SheetOrder: _workbook.Sheets.Select(sheet => sheet.Name).ToArray(),
            VisibleSheetNames: _workbook.Sheets.Where(sheet => !sheet.IsHidden && !sheet.IsVeryHidden).Select(sheet => sheet.Name).ToArray(),
            HiddenSheetNames: _workbook.Sheets.Where(sheet => sheet.IsHidden || sheet.IsVeryHidden).Select(sheet => sheet.Name).ToArray(),
            GroupedSheetNames: _workbook.Sheets.Where(sheet => _groupedSheetIds.Contains(sheet.Id)).Select(sheet => sheet.Name).ToArray(),
            ActiveTabColor: FormatSheetTabWorkflowColor(_workbook.GetSheet(_currentSheetId)?.TabColor),
            CurrentFilePath: _currentFilePath ?? "",
            WorkbookDirty: _workbookDirty,
            CanUndo: _session.CanUndo,
            CanRedo: _session.CanRedo,
            CaptureLogicalWidth: ActualWidth,
            CaptureLogicalHeight: Math.Min(ActualHeight, 760));
    }

    private static string FormatSheetTabWorkflowColor(CellColor? color) =>
        color is null ? "" : $"{color.Value.R},{color.Value.G},{color.Value.B}";

    private static SheetTabWorkflowsTourManifestWorkflow CreateCapturedSheetTabWorkflow(
        string name,
        IReadOnlyList<string> catalogRows,
        string commandRoute,
        params string[] captureStates) =>
        new(
            Name: name,
            CatalogRows: catalogRows,
            PlannedStatus: "planned",
            ActualStatus: "captured",
            CommandRoute: commandRoute,
            LimitationNote: "Captured through deterministic in-process host command/session paths and RenderTargetBitmap; no global mouse, double-click, drag, right-click, keytip, access-key, or UI Automation input is synthesized.",
            CaptureKeys: captureStates.Select(state => $"sheet-tab-workflows:{state}").ToArray());

    private static void DeleteSheetTabWorkflowsTourEvidence(string outputDir)
    {
        if (!Directory.Exists(outputDir))
            return;

        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_sheet_tab_workflows_*.png"))
            File.Delete(file);

        var manifestPath = Path.Combine(outputDir, SheetTabWorkflowsTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);

        var savedWorkbookPath = Path.Combine(outputDir, SheetTabWorkflowsTourSavedWorkbookFileName);
        if (File.Exists(savedWorkbookPath))
            File.Delete(savedWorkbookPath);
    }

    private static void ValidateSheetTabWorkflowsTourEvidence(
        string outputDir,
        IReadOnlyList<SheetTabWorkflowsTourManifestCapture> captures)
    {
        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();

        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"Sheet-tab workflows tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");
    }

    private static async Task WriteSheetTabWorkflowsTourManifestAsync(
        string outputDir,
        string savedWorkbookPath,
        IReadOnlyList<SheetTabWorkflowsTourManifestCapture> captures,
        IReadOnlyList<SheetTabWorkflowsTourManifestWorkflow> workflows)
    {
        var plannedCaptureKeys = new[]
        {
            "seeded-before-workflows",
            "insert-sheet-result",
            "rename-submitted-result",
            "move-or-copy-result",
            "tab-color-applied-result",
            "hide-sheet-result",
            "unhide-sheet-result",
            "select-all-sheets-result",
            "ungroup-sheets-result",
            "save-persistence-path-result",
            "reopened-persistence-result"
        };

        var manifest = new SheetTabWorkflowsTourManifest(
            Tool: "FREEX_SHEET_TAB_WORKFLOWS_TOUR",
            EvidenceFamily: "sheet-tab-workflows",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "sheet-tab-workflows:submitted-result-and-persistence-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_sheet_tab_workflows_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md#UI-CAT-CONTEXT-002",
            CatalogRows:
            [
                "UI-CAT-SHEETTAB-001A",
                "UI-CAT-SHEETTAB-001B",
                "UI-CAT-SHEETTAB-001C",
                "UI-CAT-SHEETTAB-001D",
                "UI-CAT-SHEETTAB-002A-J"
            ],
            SavedWorkbookPath: savedWorkbookPath,
            SavedWorkbookExists: File.Exists(savedWorkbookPath),
            CaptureStatus: "complete-with-foreground-input-limitations",
            CaptureMethod: "RenderTargetBitmap-main-window-with-real-host-command-and-save-open-paths",
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures after real host command/save/open execution; no global mouse, keyboard, keytip, native dialog, or UI Automation Invoke input is used."
                    : "Window captures abort unless the expected FreeX WPF surface owns foreground focus immediately before render and file write."),
            PlannedWorkflowCount: workflows.Count,
            ActualWorkflowCount: workflows.Count(workflow => string.Equals(workflow.ActualStatus, "captured", StringComparison.Ordinal)),
            PlannedCaptureCount: plannedCaptureKeys.Length,
            ActualCaptureCount: captures.Count,
            PlannedCaptureKeys: plannedCaptureKeys,
            ActualCaptureKeys: captures.Select(capture => capture.State).ToArray(),
            CommandRoutesUsed: captures.Select(capture => capture.CommandRoute).Distinct().ToArray(),
            Captures: captures,
            Workflows: workflows,
            CoveredStates:
            [
                "Seeded workbook before submitted sheet-tab workflows",
                "Insert Sheet selected the newly created sheet",
                "Submitted rename result through RenameSheetCommand",
                "Move or Copy create-copy result through CompositeWorkbookCommand",
                "Tab Color applied through SetSheetTabColorCommand",
                "Hide Sheet and Unhide Sheet result states through SetSheetHiddenCommand",
                "Select All Sheets and Ungroup Sheets grouping states through production context handlers",
                "SaveWorkbookToTargetAsync persistence path",
                "OpenFileAsync reload proof for sheet name, order, visibility, and tab color"
            ],
            Limitations:
            [
                "This slice drives deterministic in-process command/session paths and captures the resulting WPF main window with RenderTargetBitmap.",
                "It does not synthesize physical double-click rename, foreground right-click context-menu opening, tab drag reorder, Ctrl/Shift tab-click grouping, keytip/access-key traversal, native dialogs, UI Automation Invoke, or screen-wide capture input.",
                "Move or Copy is represented by the submitted composite command result, matching the host path after the Move or Copy dialog returns rather than physically submitting the dialog.",
                "Rename, tab color, hide, and unhide are captured after their backing commands execute; dialog foreground entry and color-picker foreground submission remain separate evidence gaps.",
                "The persistence proof saves and reopens an XLSX through FreeX host services; no Microsoft Excel counterpart screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, SheetTabWorkflowsTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.SheetTabWorkflowsTourManifest);
    }

    private sealed record SheetTabWorkflowsTourContext(
        Sheet SummarySheet,
        Sheet InputsSheet,
        Sheet ArchiveSheet,
        Sheet ReviewSheet);

    private sealed record SheetTabWorkflowsTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogRows,
        string SavedWorkbookPath,
        bool SavedWorkbookExists,
        string CaptureStatus,
        string CaptureMethod,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        int PlannedWorkflowCount,
        int ActualWorkflowCount,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        IReadOnlyList<string> PlannedCaptureKeys,
        IReadOnlyList<string> ActualCaptureKeys,
        IReadOnlyList<string> CommandRoutesUsed,
        IReadOnlyList<SheetTabWorkflowsTourManifestCapture> Captures,
        IReadOnlyList<SheetTabWorkflowsTourManifestWorkflow> Workflows,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record SheetTabWorkflowsTourManifestWorkflow(
        string Name,
        IReadOnlyList<string> CatalogRows,
        string PlannedStatus,
        string ActualStatus,
        string CommandRoute,
        string LimitationNote,
        IReadOnlyList<string> CaptureKeys);

    private sealed record SheetTabWorkflowsTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        string CommandRoute,
        string EvidenceSummary,
        string ActiveSheetName,
        IReadOnlyList<string> SheetOrder,
        IReadOnlyList<string> VisibleSheetNames,
        IReadOnlyList<string> HiddenSheetNames,
        IReadOnlyList<string> GroupedSheetNames,
        string ActiveTabColor,
        string CurrentFilePath,
        bool WorkbookDirty,
        bool CanUndo,
        bool CanRedo,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight);
}
