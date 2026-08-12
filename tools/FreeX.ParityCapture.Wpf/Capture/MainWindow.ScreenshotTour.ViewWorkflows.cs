using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private async Task CaptureViewWorkflowsTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteViewWorkflowsTourEvidence(outputDir);

        var savedWorkbookPath = Path.Combine(outputDir, ViewWorkflowsTourSavedWorkbookFileName);
        DeleteIfExists(savedWorkbookPath);

        WindowState = WindowState.Normal;
        Width = 1220;
        Height = 780;
        await Task.Delay(700);

        var originalFormulaBarVisible = _options.ShowFormulaBar;
        var captures = new List<ViewWorkflowsTourManifestCapture>();
        var workflows = new List<ViewWorkflowsTourManifestWorkflow>();
        var plannedCaptures = CreateViewWorkflowsPlannedCaptures();

        try
        {
            var context = EnsureViewWorkflowsTourContext();
            captures.Add(await CaptureViewWorkflowsWindowStateAsync(
                outputDir,
                context,
                "seeded-normal-baseline",
                "freex_view_workflows_seeded_normal_baseline",
                "Seeded workbook starts in Normal view with Show toggles enabled and no panes split or frozen.",
                "initial-seeded-context"));

            ApplyViewWorkflowsSavedCustomViewState(context);
            ExecuteViewWorkflowsCommand(
                new SaveCustomViewCommand(ViewWorkflowsTourCustomViewName, includePrintSettings: true, includeHiddenRowsColumnsAndFilterSettings: true),
                "Save Custom View");
            context = ResolveViewWorkflowsCurrentContext(savedWorkbookPath, "custom-view-saved");
            captures.Add(await CaptureViewWorkflowsWindowStateAsync(
                outputDir,
                context,
                "custom-view-save-result",
                "freex_view_workflows_custom_view_save_result",
                "Save Custom View result captures a named custom view that stores Page Layout mode, hidden Show toggles, frozen panes at C4, and 125% zoom.",
                "SaveCustomViewCommand(\"View Workflow Submitted\", includePrintSettings: true, includeHiddenRowsColumnsAndFilterSettings: true)"));
            workflows.Add(CreateCapturedViewWorkflow(
                "Custom View save result",
                ["UI-CAT-VIEW-001", "UI-CMD-VIEW-002"],
                "SaveCustomViewCommand",
                "custom-view-save-result"));

            ApplyViewWorkflowsSplitAndArrangeState(context);
            captures.Add(await CaptureViewWorkflowsWindowStateAsync(
                outputDir,
                context,
                "split-arrange-result",
                "freex_view_workflows_split_arrange_result",
                "Split command result captures E6 split panes and Horizontal Arrange All model state after clearing the frozen pane state.",
                "SetSplitPanesCommand(sheet.Id, 6, 5) plus SetWorkbookWindowArrangementCommand(Horizontal)"));
            workflows.Add(CreateCapturedViewWorkflow(
                "Split and Arrange All result",
                ["UI-CAT-VIEW-002", "UI-CMD-VIEW-003", "UI-CMD-VIEW-004"],
                "SetSplitPanesCommand plus SetWorkbookWindowArrangementCommand",
                "split-arrange-result"));

            ExecuteViewWorkflowsCommand(new ApplyCustomViewCommand(ViewWorkflowsTourCustomViewName), "Apply Custom View");
            SyncViewWorkflowsUiFromSheet(context.Sheet);
            context = ResolveViewWorkflowsCurrentContext(savedWorkbookPath, "custom-view-applied");
            captures.Add(await CaptureViewWorkflowsWindowStateAsync(
                outputDir,
                context,
                "custom-view-show-applied-result",
                "freex_view_workflows_custom_view_show_applied_result",
                "Show Custom View result restores the saved Page Layout, hidden toggles, frozen panes, and 125% zoom after the workbook was changed to split/Page Break state.",
                "ApplyCustomViewCommand(\"View Workflow Submitted\")"));
            workflows.Add(CreateCapturedViewWorkflow(
                "Custom View show/apply result",
                ["UI-CAT-VIEW-001", "UI-CMD-VIEW-002"],
                "ApplyCustomViewCommand",
                "custom-view-show-applied-result"));

            ApplyViewWorkflowsPageBreakPersistenceState(context);
            context = ResolveViewWorkflowsCurrentContext(savedWorkbookPath, "save-ready");
            captures.Add(await CaptureViewWorkflowsWindowStateAsync(
                outputDir,
                context,
                "view-toggle-save-ready",
                "freex_view_workflows_view_toggle_save_ready",
                "View toggle result before persistence captures Page Break Preview, hidden gridlines, visible headings/ruler, split panes, and 150% zoom while the custom view still exists.",
                "SetWorksheetViewModeCommand(PageBreakPreview), SetWorksheetViewOptionsCommand, SetSplitPanesCommand, SetWorksheetZoomCommand"));
            workflows.Add(CreateCapturedViewWorkflow(
                "Workbook view toggle persistence setup",
                ["UI-CAT-VIEW-001", "UI-CAT-VIEW-002", "UI-CAT-STATUS-003A-E", "UI-CMD-VIEW-001", "UI-CMD-VIEW-003", "UI-CMD-VIEW-004"],
                "SetWorksheetViewModeCommand plus SetWorksheetViewOptionsCommand plus SetSplitPanesCommand plus SetWorksheetZoomCommand",
                "view-toggle-save-ready"));

            context = await SaveViewWorkflowsWorkbookAsync(savedWorkbookPath, context);
            captures.Add(await CaptureViewWorkflowsWindowStateAsync(
                outputDir,
                context,
                "saved-native-workbook",
                "freex_view_workflows_saved_native_workbook",
                "Native FreeX save result captures the saved workbook path/status while the View workflow state and saved custom view remain in the model.",
                "SaveWorkbookToTargetAsync(FileSaveTarget(savedWorkbookPath, native FreeX adapter))"));

            await OpenFileAsync(savedWorkbookPath);
            context = ResolveViewWorkflowsCurrentContext(savedWorkbookPath, "reopened");
            SyncViewWorkflowsUiFromSheet(context.Sheet);
            captures.Add(await CaptureViewWorkflowsWindowStateAsync(
                outputDir,
                context,
                "reopened-view-toggle-persistence",
                "freex_view_workflows_reopened_view_toggle_persistence",
                "Reopened workbook proof captures persisted Page Break Preview, hidden gridlines, split panes, 150% zoom, and the saved custom view list after OpenFileAsync.",
                "OpenFileAsync(savedWorkbookPath) -> native FreeX adapter"));
            workflows.Add(CreateCapturedViewWorkflow(
                "View toggle save/reopen persistence",
                ["UI-CAT-VIEW-001", "UI-CAT-VIEW-002", "UI-CAT-STATUS-003A-E", "UI-CMD-VIEW-001", "UI-CMD-VIEW-003", "UI-CMD-VIEW-004"],
                "SaveWorkbookToTargetAsync plus OpenFileAsync",
                "view-toggle-save-ready",
                "saved-native-workbook",
                "reopened-view-toggle-persistence"));

            ExecuteViewWorkflowsCommand(new ApplyCustomViewCommand(ViewWorkflowsTourCustomViewName), "Apply Reopened Custom View");
            context = ResolveViewWorkflowsCurrentContext(savedWorkbookPath, "reopened-custom-view-applied");
            SyncViewWorkflowsUiFromSheet(context.Sheet);
            captures.Add(await CaptureViewWorkflowsWindowStateAsync(
                outputDir,
                context,
                "reopened-custom-view-show-result",
                "freex_view_workflows_reopened_custom_view_show_result",
                "After reopen, Show Custom View result applies the persisted custom view and restores Page Layout, hidden toggles, frozen panes, and 125% zoom.",
                "ApplyCustomViewCommand(\"View Workflow Submitted\") after OpenFileAsync"));
            workflows.Add(CreateCapturedViewWorkflow(
                "Custom View persisted show result",
                ["UI-CAT-VIEW-001", "UI-CMD-VIEW-002"],
                "OpenFileAsync plus ApplyCustomViewCommand",
                "reopened-view-toggle-persistence",
                "reopened-custom-view-show-result"));

            ExecuteViewWorkflowsCommand(new DeleteCustomViewCommand(ViewWorkflowsTourCustomViewName), "Delete Custom View");
            context = ResolveViewWorkflowsCurrentContext(savedWorkbookPath, "custom-view-deleted");
            captures.Add(await CaptureViewWorkflowsCustomViewsDialogAsync(
                outputDir,
                context,
                "custom-view-delete-result-dialog",
                "freex_view_workflows_custom_view_delete_result_dialog",
                "Delete Custom View result captures the production Custom Views dialog after the saved view has been removed from the workbook list.",
                "DeleteCustomViewCommand(\"View Workflow Submitted\") then CustomViewsDialog"));
            workflows.Add(CreateCapturedViewWorkflow(
                "Custom View delete result",
                ["UI-CAT-VIEW-001", "UI-CMD-VIEW-002"],
                "DeleteCustomViewCommand plus CustomViewsDialog",
                "custom-view-delete-result-dialog"));

            ValidateViewWorkflowsTourEvidence(outputDir, captures, savedWorkbookPath);
            await WriteViewWorkflowsTourManifestAsync(outputDir, context, plannedCaptures, captures, workflows);
        }
        catch
        {
            DeleteViewWorkflowsTourEvidence(outputDir);
            throw;
        }
        finally
        {
            SetViewPanesZoomTourFormulaBarVisible(originalFormulaBarVisible);
        }
    }

    private ViewWorkflowsTourContext EnsureViewWorkflowsTourContext()
    {
        CreateNewWorkbook();
        HideStartScreen();

        var sheet = _workbook.Sheets[0];
        _currentSheetId = sheet.Id;
        _currentFilePath = null;
        _workbook.Name = "View workflow evidence";
        sheet.Name = "View Workflows";
        _workbook.CustomViews.Clear();
        sheet.RowOutlineLevels.Clear();
        sheet.FilterHiddenRows.Clear();
        sheet.HiddenRows.Clear();
        sheet.GroupHiddenRows.Clear();

        for (uint row = 1; row <= 28; row++)
        {
            for (uint col = 1; col <= 10; col++)
            {
                var address = new CellAddress(sheet.Id, row, col);
                if (row == 1)
                    sheet.SetCell(address, new TextValue($"View Field {col}"));
                else if (col == 1)
                    sheet.SetCell(address, new TextValue($"Workflow row {row - 1}"));
                else
                    sheet.SetCell(address, new NumberValue((row - 1) * 10 + col));
            }
        }

        sheet.SetCell(new CellAddress(sheet.Id, 31, 1), new TextValue("Custom view marker"));
        sheet.SetCell(new CellAddress(sheet.Id, 31, 2), new TextValue(ViewWorkflowsTourCustomViewName));

        SelectViewPanesZoomTourRange(sheet, new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 8, 5)));
        ExecuteViewWorkflowsCommand(new SetWorksheetViewModeCommand(sheet.Id, WorksheetViewMode.Normal), "Normal View");
        ExecuteViewWorkflowsCommand(new SetWorksheetViewOptionsCommand(sheet.Id, showGridlines: true, showHeadings: true, showRulers: true), "View Show");
        ExecuteViewWorkflowsCommand(new SetFreezePanesCommand(sheet.Id, 0, 0), "Unfreeze Panes");
        ExecuteViewWorkflowsCommand(new SetSplitPanesCommand(sheet.Id, null, null), "Clear Split");
        ExecuteViewWorkflowsCommand(new SetWorksheetZoomCommand(sheet.Id, 100), "Zoom 100");
        SyncViewWorkflowsUiFromSheet(sheet);
        SelectViewRibbonTabForTour();
        UpdateTitleBar();
        MarkWorkbookDirty();

        return new ViewWorkflowsTourContext(
            Sheet: sheet,
            SavedWorkbookPath: string.Empty,
            SavedWorkbookOutputFileName: string.Empty,
            SavedWorkbookBytes: 0,
            PersistenceStage: "seeded");
    }

    private void ApplyViewWorkflowsSavedCustomViewState(ViewWorkflowsTourContext context)
    {
        var sheet = context.Sheet;
        ExecuteViewWorkflowsCommand(new SetWorksheetViewModeCommand(sheet.Id, WorksheetViewMode.PageLayout), "Page Layout View");
        ExecuteViewWorkflowsCommand(new SetWorksheetViewOptionsCommand(sheet.Id, showGridlines: false, showHeadings: false, showRulers: false), "View Show");
        ExecuteViewWorkflowsCommand(new SetWorksheetZoomCommand(sheet.Id, 125), "Zoom 125");
        SelectViewPanesZoomTourRange(sheet, new GridRange(new CellAddress(sheet.Id, 4, 3), new CellAddress(sheet.Id, 4, 3)));
        ExecuteViewWorkflowsCommand(new SetFreezePanesCommand(sheet.Id, 3, 2), "Freeze Panes");
        SyncViewWorkflowsUiFromSheet(sheet);
    }

    private void ApplyViewWorkflowsSplitAndArrangeState(ViewWorkflowsTourContext context)
    {
        var sheet = context.Sheet;
        ExecuteViewWorkflowsCommand(new SetWorksheetViewModeCommand(sheet.Id, WorksheetViewMode.PageBreakPreview), "Page Break Preview");
        ExecuteViewWorkflowsCommand(new SetWorksheetViewOptionsCommand(sheet.Id, showGridlines: true, showHeadings: true, showRulers: true), "View Show");
        ExecuteViewWorkflowsCommand(new SetWorksheetZoomCommand(sheet.Id, 175), "Zoom 175");
        SelectViewPanesZoomTourRange(sheet, new GridRange(new CellAddress(sheet.Id, 6, 5), new CellAddress(sheet.Id, 6, 5)));
        ExecuteViewWorkflowsCommand(new SetSplitPanesCommand(sheet.Id, 6, 5), "Split");
        ExecuteViewWorkflowsCommand(new SetWorkbookWindowArrangementCommand(WorkbookWindowArrangement.Horizontal), "Arrange Windows");
        SyncViewWorkflowsUiFromSheet(sheet);
    }

    private void ApplyViewWorkflowsPageBreakPersistenceState(ViewWorkflowsTourContext context)
    {
        var sheet = context.Sheet;
        ExecuteViewWorkflowsCommand(new SetWorksheetViewModeCommand(sheet.Id, WorksheetViewMode.PageBreakPreview), "Page Break Preview");
        ExecuteViewWorkflowsCommand(new SetWorksheetViewOptionsCommand(sheet.Id, showGridlines: false, showHeadings: true, showRulers: true), "View Show");
        ExecuteViewWorkflowsCommand(new SetWorksheetZoomCommand(sheet.Id, 150), "Zoom 150");
        SelectViewPanesZoomTourRange(sheet, new GridRange(new CellAddress(sheet.Id, 8, 4), new CellAddress(sheet.Id, 8, 4)));
        ExecuteViewWorkflowsCommand(new SetSplitPanesCommand(sheet.Id, 8, 4), "Split");
        SyncViewWorkflowsUiFromSheet(sheet);
    }

    private void ExecuteViewWorkflowsCommand(IWorkbookCommand command, string title)
    {
        if (!TryExecuteCommand(command, title, out var outcome))
            throw new InvalidOperationException(outcome.ErrorMessage ?? $"View workflows tour command '{title}' failed.");

        UpdateViewport();
        RefreshToolbar();
        RefreshStatusBar();
    }

    private void SyncViewWorkflowsUiFromSheet(Sheet sheet)
    {
        _currentSheetId = sheet.Id;
        SyncZoomFromSheet(sheet.ZoomPercent);
        SyncViewPanesZoomTourWorkbookViewButtons();
        SyncStatusViewShortcutState(WorksheetViewModeUiStatePlanner.Build(sheet.ViewMode));
        _suppressViewOptionSync = true;
        try
        {
            _ribbonState.SetChecked("Gridlines", sheet.ShowGridlines);
            _ribbonState.SetChecked("Headings", sheet.ShowHeadings);
            _ribbonState.SetChecked("Ruler", sheet.ShowRulers);
        }
        finally
        {
            _suppressViewOptionSync = false;
        }

        _ribbonState.SetChecked("Split", sheet.SplitRow is not null || sheet.SplitColumn is not null);
        SetViewPanesZoomTourFormulaBarVisible(true);
        SelectViewRibbonTabForTour();
        UpdateViewport();
        RefreshViewWindowCommandState();
        UpdateLayout();
    }

    private async Task<ViewWorkflowsTourContext> SaveViewWorkflowsWorkbookAsync(
        string savedWorkbookPath,
        ViewWorkflowsTourContext context)
    {
        var adapter = FileFormatResolver.FindSaveAdapter(_fileAdapters, ".fxl", out _)
            ?? throw new InvalidOperationException("View workflows tour could not find the native FreeX save adapter.");
        if (!await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter)))
            throw new InvalidOperationException("View workflows tour could not save the native FreeX workbook.");

        return context with
        {
            SavedWorkbookPath = savedWorkbookPath,
            SavedWorkbookOutputFileName = Path.GetFileName(savedWorkbookPath),
            SavedWorkbookBytes = new FileInfo(savedWorkbookPath).Length,
            PersistenceStage = "saved"
        };
    }

    private ViewWorkflowsTourContext ResolveViewWorkflowsCurrentContext(
        string savedWorkbookPath,
        string persistenceStage)
    {
        var sheet = _workbook.Sheets.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, "View Workflows", StringComparison.OrdinalIgnoreCase))
            ?? GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("View workflows tour could not resolve the active worksheet.");

        _currentSheetId = sheet.Id;
        return new ViewWorkflowsTourContext(
            Sheet: sheet,
            SavedWorkbookPath: savedWorkbookPath,
            SavedWorkbookOutputFileName: string.IsNullOrWhiteSpace(savedWorkbookPath) ? string.Empty : Path.GetFileName(savedWorkbookPath),
            SavedWorkbookBytes: File.Exists(savedWorkbookPath) ? new FileInfo(savedWorkbookPath).Length : 0,
            PersistenceStage: persistenceStage);
    }

    private async Task<ViewWorkflowsTourManifestCapture> CaptureViewWorkflowsWindowStateAsync(
        string outputDir,
        ViewWorkflowsTourContext context,
        string state,
        string fileName,
        string evidenceSummary,
        string commandRoute)
    {
        SyncViewWorkflowsUiFromSheet(context.Sheet);
        EnsureCellVisible(SheetGrid?.SelectedRange?.Start ?? new CellAddress(context.Sheet.Id, 1, 1));
        await WaitForRibbonScreenshotRenderPassAsync();
        await CaptureCurrentWindowAsync(outputDir, fileName, 780);
        return CreateViewWorkflowsCapture(
            context,
            state,
            "main-window-view-tab-grid-status",
            fileName,
            "RenderTargetBitmap-window-full",
            ActualWidth,
            Math.Min(ActualHeight, 780),
            evidenceSummary,
            commandRoute);
    }

    private async Task<ViewWorkflowsTourManifestCapture> CaptureViewWorkflowsCustomViewsDialogAsync(
        string outputDir,
        ViewWorkflowsTourContext context,
        string state,
        string fileName,
        string evidenceSummary,
        string commandRoute)
    {
        SyncViewWorkflowsUiFromSheet(context.Sheet);
        var dialog = new CustomViewsDialog(_workbook, ExecuteCustomViewDialogCommand) { Owner = this };
        try
        {
            dialog.Show();
            await Task.Delay(350);
            dialog.UpdateLayout();
            await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, fileName);
            return CreateViewWorkflowsCapture(
                context,
                state,
                "custom-views-dialog",
                fileName,
                "RenderTargetBitmap-custom-views-dialog-window",
                dialog.ActualWidth,
                dialog.ActualHeight,
                evidenceSummary,
                commandRoute);
        }
        finally
        {
            dialog.Close();
        }
    }

    private ViewWorkflowsTourManifestCapture CreateViewWorkflowsCapture(
        ViewWorkflowsTourContext context,
        string state,
        string surface,
        string fileName,
        string captureMethod,
        double captureLogicalWidth,
        double captureLogicalHeight,
        string evidenceSummary,
        string commandRoute)
    {
        var sheet = context.Sheet;
        var customView = _workbook.CustomViews.FirstOrDefault(view =>
            string.Equals(view.Name, ViewWorkflowsTourCustomViewName, StringComparison.OrdinalIgnoreCase));
        return new ViewWorkflowsTourManifestCapture(
            CaptureKey: $"view-workflows:{state}",
            PairKey: $"interactive:view-workflows:{state}",
            CatalogIds: ["UI-CAT-VIEW-001", "UI-CAT-VIEW-002", "UI-CAT-STATUS-003A-E", "UI-CMD-VIEW-001", "UI-CMD-VIEW-002", "UI-CMD-VIEW-003", "UI-CMD-VIEW-004"],
            State: state,
            Surface: surface,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: captureLogicalWidth,
            CaptureLogicalHeight: captureLogicalHeight,
            SheetName: sheet.Name,
            SelectedRange: SheetGrid?.SelectedRange?.ToString() ?? string.Empty,
            ViewMode: sheet.ViewMode.ToString(),
            ShowGridlines: sheet.ShowGridlines,
            ShowHeadings: sheet.ShowHeadings,
            ShowRulers: sheet.ShowRulers,
            FormulaBarVisible: FormulaBarBorder.Visibility == Visibility.Visible,
            FrozenRows: sheet.FrozenRows,
            FrozenCols: sheet.FrozenCols,
            SplitRow: sheet.SplitRow,
            SplitColumn: sheet.SplitColumn,
            ZoomText: StatusZoomText.Text,
            ZoomPercent: sheet.ZoomPercent,
            ZoomSliderValue: ZoomSlider.Value,
            StatusNormalViewChecked: StatusNormalViewButton.IsChecked == true,
            StatusPageLayoutViewChecked: StatusPageLayoutViewButton.IsChecked == true,
            StatusPageBreakPreviewChecked: StatusPageBreakPreviewButton.IsChecked == true,
            ViewNormalChecked: IsRibbonCommandChecked("Normal"),
            ViewPageLayoutChecked: IsRibbonCommandChecked("Page Layout"),
            ViewPageBreakPreviewChecked: IsRibbonCommandChecked("Page Break Preview"),
            ViewGridlinesChecked: IsRibbonCommandChecked("Gridlines"),
            ViewHeadingsChecked: IsRibbonCommandChecked("Headings"),
            ViewRulerChecked: IsRibbonCommandChecked("Ruler"),
            SplitButtonChecked: IsRibbonCommandChecked("Split"),
            WindowArrangement: _workbook.WindowArrangement.ToString(),
            CustomViewCount: _workbook.CustomViews.Count,
            CustomViewNames: _workbook.CustomViews.Select(view => view.Name).ToArray(),
            SavedCustomViewPresent: customView is not null,
            SavedCustomViewSheetStateSummary: customView is null
                ? string.Empty
                : string.Join("; ", customView.Sheets.Select(DescribeViewWorkflowsCustomViewState)),
            PersistenceStage: context.PersistenceStage,
            SavedWorkbookOutputFileName: context.SavedWorkbookOutputFileName,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            CommandRoute: commandRoute,
            EvidenceSummary: evidenceSummary);
    }

    private static string DescribeViewWorkflowsCustomViewState(WorksheetCustomViewState state) =>
        $"{state.SheetName}:view={state.ViewMode},frozen={state.FrozenRows}/{state.FrozenCols},split={state.SplitRow?.ToString() ?? ""}/{state.SplitColumn?.ToString() ?? ""},show={state.ShowGridlines}/{state.ShowHeadings}/{state.ShowRulers},zoom={state.ZoomPercent}";

    private static IReadOnlyList<ViewWorkflowsTourPlannedCapture> CreateViewWorkflowsPlannedCaptures() =>
    [
        new("seeded-normal-baseline", "freex_view_workflows_seeded_normal_baseline.png", "captured", "Initial deterministic seeded workbook state."),
        new("custom-view-save-result", "freex_view_workflows_custom_view_save_result.png", "captured", "SaveCustomViewCommand result state."),
        new("split-arrange-result", "freex_view_workflows_split_arrange_result.png", "captured", "Split panes and Arrange All model result."),
        new("custom-view-show-applied-result", "freex_view_workflows_custom_view_show_applied_result.png", "captured", "ApplyCustomViewCommand result state before persistence."),
        new("view-toggle-save-ready", "freex_view_workflows_view_toggle_save_ready.png", "captured", "Page Break Preview/show-toggle/split/zoom state ready for save."),
        new("saved-native-workbook", "freex_view_workflows_saved_native_workbook.png", "captured", "SaveWorkbookToTargetAsync result with retained .fxl artifact."),
        new("reopened-view-toggle-persistence", "freex_view_workflows_reopened_view_toggle_persistence.png", "captured", "OpenFileAsync reload proof for view toggles, split, zoom, and custom view list."),
        new("reopened-custom-view-show-result", "freex_view_workflows_reopened_custom_view_show_result.png", "captured", "ApplyCustomViewCommand after reopen proves custom view persistence."),
        new("custom-view-delete-result-dialog", "freex_view_workflows_custom_view_delete_result_dialog.png", "captured", "DeleteCustomViewCommand result shown in the Custom Views dialog."),
        new("physical-split-divider-drag", "", "planned-but-blocked", "Unsafe to synthesize foreground split-divider drag inside the in-process screenshot tour."),
        new("new-window-side-by-side-os-layout", "", "planned-but-blocked", "Requires coordinated foreground ownership of multiple live WPF top-level windows and OS focus."),
        new("synchronous-scrolling-foreground-proof", "", "planned-but-blocked", "Requires foreground wheel/scroll input across paired windows.")
    ];

    private static ViewWorkflowsTourManifestWorkflow CreateCapturedViewWorkflow(
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
            LimitationNote: "Captured through deterministic in-process command/session paths and RenderTargetBitmap; no global mouse, keytip, native dialog, UI Automation Invoke, foreground drag, or OS multi-window input is synthesized.",
            CaptureKeys: captureStates.Select(state => $"view-workflows:{state}").ToArray());

    private static void DeleteViewWorkflowsTourEvidence(string outputDir)
    {
        if (!Directory.Exists(outputDir))
            return;

        foreach (var file in Directory.EnumerateFiles(outputDir, "freex_view_workflows_*.png"))
            File.Delete(file);

        DeleteIfExists(Path.Combine(outputDir, ViewWorkflowsTourManifestFileName));
        DeleteIfExists(Path.Combine(outputDir, ViewWorkflowsTourSavedWorkbookFileName));
    }

    private static void ValidateViewWorkflowsTourEvidence(
        string outputDir,
        IReadOnlyList<ViewWorkflowsTourManifestCapture> captures,
        string savedWorkbookPath)
    {
        if (captures.Count != 9)
            throw new InvalidOperationException($"View workflows tour expected 9 actual captures but created {captures.Count}.");

        var missing = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"View workflows tour did not create {missing.Length} planned capture(s): {string.Join(", ", missing)}.");

        var blank = captures
            .Select(capture => capture.OutputFileName)
            .Where(fileName => !IsNonBlankPng(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (blank.Length > 0)
            throw new InvalidOperationException(
                $"View workflows tour created blank capture(s): {string.Join(", ", blank)}.");

        if (!File.Exists(savedWorkbookPath) || new FileInfo(savedWorkbookPath).Length <= 0)
            throw new InvalidOperationException("View workflows tour did not retain a non-empty native FreeX workbook.");
    }

    private static async Task WriteViewWorkflowsTourManifestAsync(
        string outputDir,
        ViewWorkflowsTourContext context,
        IReadOnlyList<ViewWorkflowsTourPlannedCapture> plannedCaptures,
        IReadOnlyList<ViewWorkflowsTourManifestCapture> captures,
        IReadOnlyList<ViewWorkflowsTourManifestWorkflow> workflows)
    {
        var blockedCaptureCount = plannedCaptures.Count(planned => string.Equals(planned.Status, "planned-but-blocked", StringComparison.Ordinal));
        var manifest = new ViewWorkflowsTourManifest(
            Tool: "FREEX_VIEW_WORKFLOWS_TOUR",
            EvidenceFamily: "view-custom-multi-window-submitted-workflows",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "view-workflows:submitted-result-and-persistence-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_view_workflows_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds: ["UI-CAT-VIEW-001", "UI-CAT-VIEW-002", "UI-CAT-STATUS-003A-E", "UI-CMD-VIEW-001", "UI-CMD-VIEW-002", "UI-CMD-VIEW-003", "UI-CMD-VIEW-004"],
            SheetName: context.Sheet.Name,
            SavedWorkbookPath: context.SavedWorkbookPath,
            SavedWorkbookOutputFileName: context.SavedWorkbookOutputFileName,
            SavedWorkbookBytes: context.SavedWorkbookBytes,
            PersistencePath: "SaveWorkbookToTargetAsync(.fxl native FreeX adapter) then OpenFileAsync(saved .fxl)",
            CaptureStatus: blockedCaptureCount == 0 ? "complete" : "captured-with-planned-foreground-limitations",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: plannedCaptures.Count,
            ActualCaptureCount: captures.Count,
            BlockedCaptureCount: blockedCaptureCount,
            Pairing: new ViewWorkflowsTourManifestPairing(
                "interactive:view-workflows:<State>",
                "excel-or-foreground-runner",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process RenderTargetBitmap captures after real host command/save/open execution; no global mouse, keyboard, keytip, native dialog, UI Automation Invoke, split-divider drag, wheel, or OS multi-window input is used."
                    : "Window and dialog captures abort unless the expected FreeX WPF surface owns foreground focus immediately before render and file write."),
            PlannedCaptures: plannedCaptures,
            Captures: captures,
            Workflows: workflows,
            SubmittedMutations:
            [
                "SetWorksheetViewModeCommand switches Normal, Page Layout, and Page Break Preview states.",
                "SetWorksheetViewOptionsCommand mutates gridline, heading, and ruler visibility.",
                "SetWorksheetZoomCommand mutates worksheet zoom and the tour syncs the production zoom/status controls from sheet state.",
                "SetFreezePanesCommand captures frozen rows/columns at C4 and clears split state.",
                "SetSplitPanesCommand captures E6 and D8 split states and clears frozen pane state.",
                "SetWorkbookWindowArrangementCommand captures Horizontal Arrange All model state.",
                "SaveCustomViewCommand stores the custom view; ApplyCustomViewCommand restores it before and after save/reopen; DeleteCustomViewCommand removes it.",
                "SaveWorkbookToTargetAsync writes the native .fxl workbook and OpenFileAsync reloads it through the host open path."
            ],
            CoveredStates:
            [
                "Custom View save, show/apply, persisted show/apply after reopen, and delete result states.",
                "Workbook view toggles and status view shortcut state for Normal, Page Layout, and Page Break Preview.",
                "Gridline/headings/ruler show-toggle result states in the View ribbon.",
                "Freeze Panes and Split result states with pane-model summaries in the manifest.",
                "Zoom 125/150/175 result states with status zoom text and slider values.",
                "Arrange All Horizontal model state.",
                "Native FreeX save/reopen persistence for current view state and saved custom view metadata."
            ],
            Limitations:
            [
                "This tour drives deterministic in-process command/session paths and captures WPF surfaces with RenderTargetBitmap.",
                "It does not synthesize foreground mouse/keytip/access-key/UI Automation interactions, physical split-divider drag, status-slider drag, Ctrl+wheel, or foreground status-button clicks already covered by the status/footer interaction slice.",
                "New Window, View Side by Side, Synchronous Scrolling, and Reset Window Position have live registry-backed code/tests, but OS-level multi-window foreground screenshots are recorded as planned-but-blocked here.",
                "The formula bar is an application option and is not stored in workbook custom views; it is kept visible for these captures and not claimed as custom-view persistence.",
                "Persistence is proven for the native FreeX .fxl adapter through host save/open services; XLSX custom-view interoperability remains a separate compatibility lane.",
                "No paired Microsoft Excel screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, ViewWorkflowsTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.ViewWorkflowsTourManifest);
    }

    private sealed record ViewWorkflowsTourContext(
        Sheet Sheet,
        string SavedWorkbookPath,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string PersistenceStage);

    private sealed record ViewWorkflowsTourManifest(
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
        string SavedWorkbookPath,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string PersistencePath,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        int BlockedCaptureCount,
        ViewWorkflowsTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<ViewWorkflowsTourPlannedCapture> PlannedCaptures,
        IReadOnlyList<ViewWorkflowsTourManifestCapture> Captures,
        IReadOnlyList<ViewWorkflowsTourManifestWorkflow> Workflows,
        IReadOnlyList<string> SubmittedMutations,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record ViewWorkflowsTourManifestPairing(
        string PairKeyTemplate,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartStatus);

    private sealed record ViewWorkflowsTourPlannedCapture(
        string State,
        string OutputFileName,
        string Status,
        string Notes);

    private sealed record ViewWorkflowsTourManifestWorkflow(
        string Name,
        IReadOnlyList<string> CatalogRows,
        string PlannedStatus,
        string ActualStatus,
        string CommandRoute,
        string LimitationNote,
        IReadOnlyList<string> CaptureKeys);

    private sealed record ViewWorkflowsTourManifestCapture(
        string CaptureKey,
        string PairKey,
        IReadOnlyList<string> CatalogIds,
        string State,
        string Surface,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SheetName,
        string SelectedRange,
        string ViewMode,
        bool ShowGridlines,
        bool ShowHeadings,
        bool ShowRulers,
        bool FormulaBarVisible,
        uint FrozenRows,
        uint FrozenCols,
        uint? SplitRow,
        uint? SplitColumn,
        string ZoomText,
        int ZoomPercent,
        double ZoomSliderValue,
        bool StatusNormalViewChecked,
        bool StatusPageLayoutViewChecked,
        bool StatusPageBreakPreviewChecked,
        bool ViewNormalChecked,
        bool ViewPageLayoutChecked,
        bool ViewPageBreakPreviewChecked,
        bool ViewGridlinesChecked,
        bool ViewHeadingsChecked,
        bool ViewRulerChecked,
        bool SplitButtonChecked,
        string WindowArrangement,
        int CustomViewCount,
        IReadOnlyList<string> CustomViewNames,
        bool SavedCustomViewPresent,
        string SavedCustomViewSheetStateSummary,
        string PersistenceStage,
        string SavedWorkbookOutputFileName,
        long SavedWorkbookBytes,
        string CommandRoute,
        string EvidenceSummary);
}
