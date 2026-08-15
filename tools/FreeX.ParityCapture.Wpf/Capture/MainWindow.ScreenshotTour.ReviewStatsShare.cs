using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Free.Shared.AppServices;
using FreeX.Core.Commands;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private const string ReviewStatsShareTourManifestFileName = "review_stats_share_tour_manifest.json";
    private const string ReviewStatsShareTourOutputDirectoryName = "review-stats-share-tour";
    private const string ReviewStatsShareTourSavedWorkbookFileName = "freex_review_share_ready_saved.xlsx";

    private async Task CaptureReviewStatsShareTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteReviewStatsShareTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1220;
        Height = 760;
        await Task.Delay(700);

        var context = EnsureReviewStatsShareTourContext();
        var captures = new List<ReviewStatsShareTourManifestCapture>();
        Window? openDialog = null;

        try
        {
            _currentFilePath = null;
            SelectReviewStatsShareRibbonTabForTour();
            FocusReviewStatsShareCommand("ReviewShareButton");
            captures.Add(await CaptureReviewStatsShareWindowStateAsync(
                outputDir,
                "review-tab-share-unsaved-context",
                "Review tab",
                "Review > Share",
                "freex_review_stats_share_review_tab_unsaved",
                "Review tab context shows Workbook Statistics and Share controls while the workbook is unsaved."));

            openDialog = new WorkbookStatisticsDialog(WorkbookStatisticsService.GetStatistics(_workbook)) { Owner = this };
            await ShowDataToolsTourDialogAsync(openDialog);
            captures.Add(await CaptureReviewStatsShareDialogAsync(
                openDialog,
                outputDir,
                "workbook-statistics-dialog",
                "Workbook Statistics",
                "Review > Workbook Statistics",
                "freex_review_workbook_statistics_dialog",
                "Workbook Statistics dialog shows deterministic sheet/cell/formula/comment/object counts with OK as the default close path."));
            CloseDataToolsTourDialog(openDialog);
            openDialog = null;

            ShowStartScreen();
            ShowInfoView();
            _backstageFrame?.FocusEntry("BackstageShareButton");
            captures.Add(await CaptureReviewStatsShareWindowStateAsync(
                outputDir,
                "review-share-unsaved-guard-status",
                "Backstage Info share status",
                "Review > Share / shared Save As guard",
                "freex_review_share_unsaved_guard_status",
                "Shared share-readiness status records the Review Share unsaved guard that requires Save As before Windows Share can send the workbook."));

            context = await SaveReviewStatsShareTourWorkbookAsync(outputDir, context);
            ShowStartScreen();
            ShowInfoView();
            _backstageFrame?.FocusEntry("BackstageShareButton");
            captures.Add(await CaptureReviewStatsShareWindowStateAsync(
                outputDir,
                "review-share-saved-ready-status",
                "Backstage Info share status",
                "Review > Share / shared saved-ready status",
                "freex_review_share_saved_ready_status",
                "Shared share-readiness status records the saved local workbook state before the Review Share workflow invokes Windows Share."));

            SelectReviewStatsShareRibbonTabForTour();
            FocusReviewStatsShareCommand("ReviewShareButton");
            captures.Add(await CaptureReviewStatsShareWindowStateAsync(
                outputDir,
                "review-tab-share-saved-context",
                "Review tab",
                "Review > Share",
                "freex_review_stats_share_review_tab_saved",
                "Review tab returns after the saved-ready status proof with the same Share command visible."));

            DeleteReviewStatsShareTourSavedWorkbook(outputDir);
            context = context with { SavedWorkbookRetained = false };
            ValidateReviewStatsShareTourEvidence(outputDir);
            await WriteReviewStatsShareTourManifestAsync(outputDir, context, captures);
        }
        catch
        {
            DeleteReviewStatsShareTourEvidence(outputDir);
            throw;
        }
        finally
        {
            if (openDialog is { IsVisible: true })
                CloseDataToolsTourDialog(openDialog);
        }
    }

    private ReviewStatsShareTourContext EnsureReviewStatsShareTourContext()
    {
        var sheet = GetCurrentOrFirstScreenshotTourSheet()
            ?? throw new InvalidOperationException("Review statistics/share tour requires an active worksheet.");

        _currentSheetId = sheet.Id;
        _currentFilePath = null;
        sheet.Comments.Clear();
        sheet.ThreadedComments.Clear();
        sheet.Pictures.Clear();
        sheet.Charts.Clear();
        sheet.DrawingShapes.Clear();
        sheet.TextBoxes.Clear();
        sheet.ReplaceMergedRegions([]);

        for (uint row = 1; row <= 8; row++)
        {
            for (uint col = 1; col <= 5; col++)
                sheet.ClearCell(new CellAddress(sheet.Id, row, col));
        }

        sheet.ColumnWidths[1] = 18;
        sheet.ColumnWidths[2] = 18;
        sheet.ColumnWidths[3] = 20;
        sheet.ColumnWidths[4] = 18;

        SetTourCell(sheet, 1, 1, new TextValue("Review stats/share"));
        SetTourCell(sheet, 2, 1, new TextValue("Workbook statistics"));
        SetTourCell(sheet, 2, 2, new NumberValue(42));
        SetTourCell(sheet, 3, 1, new TextValue("Formula"));
        sheet.SetFormula(new CellAddress(sheet.Id, 3, 2), "SUM(B2:B2)");
        SetTourCell(sheet, 4, 1, new TextValue("Threaded comment"));
        SetTourCell(sheet, 5, 1, new TextValue("Simple note"));
        SetTourCell(sheet, 6, 1, new TextValue("Share status"));
        SetTourCell(sheet, 6, 2, new TextValue("Unsaved guard"));

        var threadedCell = new CellAddress(sheet.Id, 4, 1);
        var noteCell = new CellAddress(sheet.Id, 5, 1);
        sheet.ThreadedComments[threadedCell] = new ThreadedComment("Statistics/share tour threaded comment", "FreeX");
        sheet.Comments[noteCell] = "Statistics/share tour note.";

        var selection = Range(sheet.Id, 1, 1, 6, 3);
        SetSelectionRange(selection, selection.Start);
        EnsureCellVisible(selection.Start);
        RefreshReviewCommentNoteCommandStates();
        RefreshToolbar();
        RefreshStatusBar();
        UpdateViewport();
        UpdateLayout();

        var statistics = WorkbookStatisticsService.GetStatistics(_workbook);
        var unsavedSharePlan = DocumentShareReadinessPlanner.CreatePlan(null, DocumentShareSurface.WindowsShare);
        return new ReviewStatsShareTourContext(
            SheetName: sheet.Name,
            ActiveRange: selection.ToString(),
            StatisticsSummary: WorkbookStatisticsDialog.CreateMessage(statistics),
            UnsavedShareStatus: DocumentShareReadinessPlanner.FormatStatus(
                unsavedSharePlan,
                DocumentShareReadinessTextSpec.WorkbookEnglish),
            SavedShareStatus: string.Empty,
            SavedWorkbookOutputFileName: ReviewStatsShareTourSavedWorkbookFileName,
            SavedWorkbookRetained: false);
    }

    private async Task<ReviewStatsShareTourContext> SaveReviewStatsShareTourWorkbookAsync(
        string outputDir,
        ReviewStatsShareTourContext context)
    {
        var savedWorkbookPath = Path.Combine(outputDir, ReviewStatsShareTourSavedWorkbookFileName);
        if (File.Exists(savedWorkbookPath))
            File.Delete(savedWorkbookPath);

        var adapter = FileFormatResolver.FindSaveAdapter(_fileAdapters, ".xlsx", out _)
            ?? throw new InvalidOperationException("Review statistics/share tour could not find an XLSX save adapter.");
        var saved = await SaveWorkbookToTargetAsync(new FileSaveTarget(savedWorkbookPath, adapter));
        if (!saved)
            throw new InvalidOperationException("Review statistics/share tour could not save the share-ready workbook.");

        var savedSharePlan = DocumentShareReadinessPlanner.CreatePlan(_currentFilePath, DocumentShareSurface.WindowsShare);
        return context with
        {
            SavedShareStatus = DocumentShareReadinessPlanner.FormatStatus(
                savedSharePlan,
                DocumentShareReadinessTextSpec.WorkbookEnglish),
            SavedWorkbookRetained = File.Exists(savedWorkbookPath)
        };
    }

    private static void DeleteReviewStatsShareTourSavedWorkbook(string outputDir)
    {
        var savedWorkbookPath = Path.Combine(outputDir, ReviewStatsShareTourSavedWorkbookFileName);
        if (File.Exists(savedWorkbookPath))
            File.Delete(savedWorkbookPath);
    }

    private void SelectReviewStatsShareRibbonTabForTour()
    {
        HideStartScreen();
        SelectRibbonTourTab(RibbonScreenshotTourPlanner.DefaultTabs.Single(tab => tab.Header == "Review"));
        RefreshReviewCommentNoteCommandStates();
        RefreshToolbar();
        UpdateLayout();
    }

    private void FocusReviewStatsShareCommand(string automationId)
    {
        var button = FindDescendantByAutomationId<Button>(RibbonTabs, automationId)
            ?? throw new InvalidOperationException($"Review statistics/share tour could not find '{automationId}'.");
        button.Focus();
        Keyboard.Focus(button);
    }

    private async Task<ReviewStatsShareTourManifestCapture> CaptureReviewStatsShareWindowStateAsync(
        string outputDir,
        string state,
        string surface,
        string entryPath,
        string fileName,
        string evidenceSummary)
    {
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        await Task.Delay(250);
        await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        return CreateReviewStatsShareCapture(
            state,
            surface,
            entryPath,
            fileName,
            "RenderTargetBitmap-main-window",
            ActualWidth,
            Math.Min(ActualHeight, 760),
            evidenceSummary);
    }

    private async Task<ReviewStatsShareTourManifestCapture> CaptureReviewStatsShareDialogAsync(
        Window dialog,
        string outputDir,
        string state,
        string surface,
        string entryPath,
        string fileName,
        string evidenceSummary)
    {
        await WaitForDataToolsDialogRenderAsync(dialog);
        await CaptureWindowElementForScreenshotTourAsync(dialog, outputDir, fileName);
        return CreateReviewStatsShareCapture(
            state,
            surface,
            entryPath,
            fileName,
            "RenderTargetBitmap-review-dialog-window",
            dialog.ActualWidth,
            dialog.ActualHeight,
            evidenceSummary);
    }

    private ReviewStatsShareTourManifestCapture CreateReviewStatsShareCapture(
        string state,
        string surface,
        string entryPath,
        string fileName,
        string captureMethod,
        double captureLogicalWidth,
        double captureLogicalHeight,
        string evidenceSummary)
    {
        var sharePlan = DocumentShareReadinessPlanner.CreatePlan(_currentFilePath, DocumentShareSurface.WindowsShare);
        var focusedAutomationId = Keyboard.FocusedElement is DependencyObject focusedElement
            ? AutomationProperties.GetAutomationId(focusedElement)
            : null;
        return new ReviewStatsShareTourManifestCapture(
            CaptureKey: $"review-stats-share:{state}",
            PairKey: $"interactive:review-stats-share:{state}",
            ScenarioId: "review-stats-share:visual-evidence",
            State: state,
            Surface: surface,
            EntryPath: entryPath,
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureMethod,
            CaptureLogicalWidth: captureLogicalWidth,
            CaptureLogicalHeight: captureLogicalHeight,
            SelectedRange: SheetGrid.SelectedRange?.ToString() ?? string.Empty,
            CurrentFilePath: _currentFilePath,
            FocusedElementAutomationId: focusedAutomationId,
            SharePlanKind: sharePlan.Kind.ToString(),
            ShareStatus: DocumentShareReadinessPlanner.FormatStatus(
                sharePlan,
                DocumentShareReadinessTextSpec.WorkbookEnglish),
            StatisticsSummary: WorkbookStatisticsDialog.CreateMessage(WorkbookStatisticsService.GetStatistics(_workbook)),
            EvidenceSummary: evidenceSummary);
    }

    private static void DeleteReviewStatsShareTourEvidence(string outputDir)
    {
        foreach (var fileName in ReviewStatsShareTourExpectedFileNames().Append(ReviewStatsShareTourManifestFileName))
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }

        var savedWorkbookPath = Path.Combine(outputDir, ReviewStatsShareTourSavedWorkbookFileName);
        if (File.Exists(savedWorkbookPath))
            File.Delete(savedWorkbookPath);
    }

    private static void ValidateReviewStatsShareTourEvidence(string outputDir)
    {
        var missing = ReviewStatsShareTourExpectedFileNames()
            .Where(fileName => !File.Exists(Path.Combine(outputDir, fileName)))
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Review statistics/share tour did not capture expected evidence: {string.Join(", ", missing)}.");
    }

    private static IReadOnlyList<string> ReviewStatsShareTourExpectedFileNames() =>
    [
        "freex_review_stats_share_review_tab_unsaved.png",
        "freex_review_workbook_statistics_dialog.png",
        "freex_review_share_unsaved_guard_status.png",
        "freex_review_share_saved_ready_status.png",
        "freex_review_stats_share_review_tab_saved.png"
    ];

    private static async Task WriteReviewStatsShareTourManifestAsync(
        string outputDir,
        ReviewStatsShareTourContext context,
        IReadOnlyList<ReviewStatsShareTourManifestCapture> captures)
    {
        var manifest = new ReviewStatsShareTourManifest(
            Tool: "FREEX_REVIEW_STATS_SHARE_TOUR",
            EvidenceFamily: "review-stats-share",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "review-stats-share:visual-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_review_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds:
            [
                "UI-CAT-REVIEW-001",
                "UI-CMD-REVIEW-002",
                "UI-CMD-REVIEW-005"
            ],
            EntryPaths:
            [
                "Review > Workbook Statistics",
                "Review > Share"
            ],
            SheetName: context.SheetName,
            ActiveRange: context.ActiveRange,
            StatisticsSummary: context.StatisticsSummary,
            UnsavedShareStatus: context.UnsavedShareStatus,
            SavedShareStatus: context.SavedShareStatus,
            SavedWorkbookOutputFileName: context.SavedWorkbookOutputFileName,
            SavedWorkbookRetained: context.SavedWorkbookRetained,
            CaptureStatus: "complete",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: ReviewStatsShareTourExpectedFileNames().Count,
            ActualCaptureCount: captures.Count,
            Pairing: new ReviewStatsShareTourManifestPairing(
                "interactive:review-stats-share:<State>",
                "excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? $"{ScreenshotTourAllowBackgroundRenderEnvVar}=1 allowed deterministic in-process WPF RenderTargetBitmap capture; no global mouse, keyboard, native Save As, or Windows Share UI input was used."
                    : "Abort before file write unless the expected FreeX main window or Review dialog owns foreground focus for each capture."),
            Captures: captures,
            CoveredStates:
            [
                "Review tab context with Workbook Statistics and Share controls visible.",
                "Workbook Statistics dialog with deterministic workbook counts.",
                "Review Share unsaved guard status requiring Save As before Windows Share.",
                "Review Share saved local workbook status before native Windows Share.",
                "Review tab return state with Share visible."
            ],
            Limitations:
            [
                "This tour drives FreeX in process and captures WPF windows with RenderTargetBitmap; it is not foreground CopyFromScreen proof.",
                "Review Share status evidence stops at the shared planner/status surface and does not launch native Save As or Windows Share.",
                "The saved-ready proof uses a deterministic local XLSX written by the tour, then removes it after the status capture; no external cloud/share provider is opened.",
                "Workbook Statistics is captured visually but this slice does not perform foreground access-key traversal or paired Microsoft Excel screenshot comparison."
            ]);

        var path = Path.Combine(outputDir, ReviewStatsShareTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.ReviewStatsShareTourManifest);
    }

    private sealed record ReviewStatsShareTourContext(
        string SheetName,
        string ActiveRange,
        string StatisticsSummary,
        string UnsavedShareStatus,
        string SavedShareStatus,
        string SavedWorkbookOutputFileName,
        bool SavedWorkbookRetained);

    private sealed record ReviewStatsShareTourManifest(
        string Tool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        IReadOnlyList<string> EntryPaths,
        string SheetName,
        string ActiveRange,
        string StatisticsSummary,
        string UnsavedShareStatus,
        string SavedShareStatus,
        string SavedWorkbookOutputFileName,
        bool SavedWorkbookRetained,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        ReviewStatsShareTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<ReviewStatsShareTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record ReviewStatsShareTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record ReviewStatsShareTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string Surface,
        string EntryPath,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string SelectedRange,
        string? CurrentFilePath,
        string? FocusedElementAutomationId,
        string SharePlanKind,
        string ShareStatus,
        string StatisticsSummary,
        string EvidenceSummary);

}
