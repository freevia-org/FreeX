using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public partial class MainWindow
{
    private async Task CaptureStatusFooterInteractionsTourAsync(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
        DeleteStatusFooterInteractionsTourEvidence(outputDir);

        WindowState = WindowState.Normal;
        Width = 1180;
        Height = 760;
        await Task.Delay(700);

        var sheet = EnsureStatusFooterTourContext();
        var captures = new List<StatusFooterInteractionsTourManifestCapture>();

        try
        {
            SelectStatusFooterTourRange(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)));
            captures.Add(await CaptureStatusFooterInteractionsStateAsync(
                outputDir,
                "selection-stats-single-number",
                "freex_status_footer_interactions_stats_single_number",
                "selection-model-single-cell",
                "Single numeric cell selection shows Count/Numerical Count/Sum/Min/Max for A1."));

            SelectStatusFooterTourRange(new GridRange(
                new CellAddress(sheet.Id, 1, 3),
                new CellAddress(sheet.Id, 4, 3)));
            captures.Add(await CaptureStatusFooterInteractionsStateAsync(
                outputDir,
                "selection-stats-text-only",
                "freex_status_footer_interactions_stats_text_only",
                "selection-model-text-range",
                "Text-only C1:C4 selection changes the footer to Count without numerical aggregates."));

            SelectStatusFooterTourRange(new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 4, 3)));
            captures.Add(await CaptureStatusFooterInteractionsStateAsync(
                outputDir,
                "selection-stats-mixed-range",
                "freex_status_footer_interactions_stats_mixed_range",
                "selection-model-mixed-range",
                "Mixed numeric/text A1:C4 selection restores Average, Count, Numerical Count, Sum, Min, and Max."));

            await RaiseStatusFooterButtonClickAsync(StatusPageLayoutViewButton);
            captures.Add(await CaptureStatusFooterInteractionsStateAsync(
                outputDir,
                "view-shortcut-page-layout-clicked",
                "freex_status_footer_interactions_view_page_layout_clicked",
                "StatusPageLayoutViewButton.Click -> PageLayoutViewBtn_Click -> SetWorksheetViewMode",
                "Status footer Page Layout shortcut click updates the active worksheet view and checked shortcut state."));

            await RaiseStatusFooterButtonClickAsync(StatusPageBreakPreviewButton);
            captures.Add(await CaptureStatusFooterInteractionsStateAsync(
                outputDir,
                "view-shortcut-page-break-clicked",
                "freex_status_footer_interactions_view_page_break_clicked",
                "StatusPageBreakPreviewButton.Click -> PageBreakPreviewBtn_Click -> SetWorksheetViewMode",
                "Status footer Page Break Preview shortcut click updates the active worksheet view and checked shortcut state."));

            await RaiseStatusFooterButtonClickAsync(StatusNormalViewButton);
            captures.Add(await CaptureStatusFooterInteractionsStateAsync(
                outputDir,
                "view-shortcut-normal-clicked",
                "freex_status_footer_interactions_view_normal_clicked",
                "StatusNormalViewButton.Click -> NormalViewBtn_Click -> SetWorksheetViewMode",
                "Status footer Normal shortcut click returns the active worksheet to Normal view."));

            await SetStatusFooterTourZoomAsync(100);
            await RaiseStatusFooterButtonClickAsync(StatusZoomOutButton);
            captures.Add(await CaptureStatusFooterInteractionsStateAsync(
                outputDir,
                "zoom-button-out-result",
                "freex_status_footer_interactions_zoom_button_out",
                "StatusZoomOutButton.Click -> ZoomOutBtn_Click -> ZoomSlider_ValueChanged -> SetWorksheetZoomCommand",
                "Status footer Zoom Out button lowers the zoom text and slider value through the production slider route."));

            await RaiseStatusFooterButtonClickAsync(StatusZoomInButton);
            captures.Add(await CaptureStatusFooterInteractionsStateAsync(
                outputDir,
                "zoom-button-in-result",
                "freex_status_footer_interactions_zoom_button_in",
                "StatusZoomInButton.Click -> ZoomInBtn_Click -> ZoomSlider_ValueChanged -> SetWorksheetZoomCommand",
                "Status footer Zoom In button raises the zoom text and slider value through the production slider route."));

            await SetStatusFooterTourZoomAsync(175);
            Zoom100Btn_Click(this, new RoutedEventArgs());
            await Task.Delay(250);
            captures.Add(await CaptureStatusFooterInteractionsStateAsync(
                outputDir,
                "zoom-100-command-result",
                "freex_status_footer_interactions_zoom_100_command",
                "View Zoom100Btn_Click -> ZoomSlider_ValueChanged -> SetWorksheetZoomCommand",
                "100% zoom command resets the status footer zoom text to 100% and the slider midpoint."));

            ApplyStatusFooterInteractionsCustomZoom(125);
            captures.Add(await CaptureStatusFooterInteractionsStateAsync(
                outputDir,
                "zoom-custom-125-result",
                "freex_status_footer_interactions_zoom_custom_125",
                "ZoomDialog.TryCreateResult -> ZoomSelectionPlanner.CalculateZoomPercent -> ZoomSlider_ValueChanged",
                "Custom 125% zoom result updates the status footer zoom text and slider without foreground wheel input."));

            await CaptureStatusFooterInteractionsModalCloseAsync();
            captures.Add(await CaptureStatusFooterInteractionsStateAsync(
                outputDir,
                "zoom-dialog-modal-close-focus-return",
                "freex_status_footer_interactions_zoom_dialog_close_focus_return",
                "ZoomDialog.ShowDialog timed cancel -> FocusSheetGridIfNeeded",
                "After an owned Zoom dialog closes, focus returns to the worksheet while the status footer remains stable.",
                captureFullWindow: true));

            ValidateStatusFooterInteractionsTourEvidence(outputDir, captures);
            await WriteStatusFooterInteractionsTourManifestAsync(outputDir, captures);
        }
        catch
        {
            DeleteStatusFooterInteractionsTourEvidence(outputDir);
            throw;
        }
    }

    private async Task RaiseStatusFooterButtonClickAsync(ButtonBase button)
    {
        if (!button.IsEnabled)
            throw new InvalidOperationException($"Status/footer interactions tour expected '{button.Name}' to be enabled.");

        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
        RefreshStatusBar();
        UpdateViewport();
        await Task.Delay(250);
    }

    private void ApplyStatusFooterInteractionsCustomZoom(int zoomPercent)
    {
        if (!ZoomDialog.TryCreateResult(zoomPercent.ToString(System.Globalization.CultureInfo.InvariantCulture), out var result, out var error))
            throw new InvalidOperationException(error ?? "Status/footer interactions tour could not create a custom Zoom dialog result.");

        var plannedZoom = ZoomSelectionPlanner.CalculateZoomPercent(
            result.ZoomPercent,
            result.FitSelection,
            SheetGrid.ActualWidth,
            SheetGrid.ActualHeight,
            SheetGrid.SelectedRange?.ColCount ?? 1,
            SheetGrid.SelectedRange?.RowCount ?? 1);
        ZoomSlider.Value = StatusZoomSliderValueForPercent(plannedZoom);
        RefreshStatusBar();
        UpdateViewport();
    }

    private async Task CaptureStatusFooterInteractionsModalCloseAsync()
    {
        var dialog = new ZoomDialog((int)Math.Round(_zoomLevel * 100)) { Owner = this };
        dialog.Loaded += (_, _) =>
        {
            dialog.Dispatcher.BeginInvoke(
                (Action)(() => dialog.DialogResult = false),
                DispatcherPriority.Background);
        };

        dialog.ShowDialog();
        FocusSheetGridIfNeeded();
        RefreshStatusBar();
        UpdateViewport();
        await Task.Delay(250);
    }

    private async Task<StatusFooterInteractionsTourManifestCapture> CaptureStatusFooterInteractionsStateAsync(
        string outputDir,
        string state,
        string fileName,
        string entryPath,
        string evidencePurpose,
        bool captureFullWindow = false)
    {
        UpdateLayout();
        await WaitForRibbonScreenshotRenderPassAsync();
        if (captureFullWindow)
            await CaptureCurrentWindowAsync(outputDir, fileName, 760);
        else
            await CaptureElementAsync(StatusBarRoot, outputDir, fileName);

        return CreateStatusFooterInteractionsTourCapture(state, fileName, entryPath, evidencePurpose, captureFullWindow);
    }

    private StatusFooterInteractionsTourManifestCapture CreateStatusFooterInteractionsTourCapture(
        string state,
        string fileName,
        string entryPath,
        string evidencePurpose,
        bool captureFullWindow)
    {
        var activeRange = SheetGrid.SelectedRange;
        IReadOnlyList<string> selectedRanges = SheetGrid.SelectedRanges is { Count: > 0 } ranges
            ? ranges.Select(range => range.ToString()).ToArray()
            : activeRange is null
                ? Array.Empty<string>()
                : new[] { activeRange.ToString() ?? string.Empty };
        var viewMode = _workbook.GetSheet(_currentSheetId)?.ViewMode ?? WorksheetViewMode.Normal;
        var focus = DescribeStatusFooterInteractionsFocus();

        return new StatusFooterInteractionsTourManifestCapture(
            CaptureKey: $"interactive:status-footer-interactions:{state}",
            PairKey: $"interactive:status-footer-interactions:{state}",
            ScenarioId: "status-footer:interaction-evidence",
            State: state,
            CaptureStatus: "complete",
            FileName: fileName,
            OutputFileName: $"{fileName}.png",
            CaptureMethod: captureFullWindow
                ? "RenderTargetBitmap-window-full"
                : "RenderTargetBitmap-status-footer-element",
            EntryPath: entryPath,
            EvidencePurpose: evidencePurpose,
            CaptureLogicalWidth: captureFullWindow ? ActualWidth : StatusBarRoot.ActualWidth,
            CaptureLogicalHeight: captureFullWindow ? Math.Min(ActualHeight, 760) : StatusBarRoot.ActualHeight,
            ActiveRange: activeRange?.ToString() ?? string.Empty,
            SelectedRanges: selectedRanges,
            StatusModeText: StatusReadyText.Text,
            StatsVisible: StatusStatsPanel.Visibility == Visibility.Visible,
            AverageText: StatusAvgText.Text,
            CountText: StatusCountText.Text,
            NumericalCountText: StatusNumericalCountText.Text,
            SumText: StatusSumText.Text,
            MinText: StatusMinText.Text,
            MaxText: StatusMaxText.Text,
            ViewMode: viewMode.ToString(),
            NormalViewChecked: StatusNormalViewButton.IsChecked == true,
            PageLayoutViewChecked: StatusPageLayoutViewButton.IsChecked == true,
            PageBreakPreviewChecked: StatusPageBreakPreviewButton.IsChecked == true,
            ViewShortcutSummary: CreateStatusFooterInteractionsViewShortcutSummary(viewMode),
            ZoomText: StatusZoomText.Text,
            ZoomPercent: (int)Math.Round(_zoomLevel * 100),
            ZoomSliderValue: ZoomSlider.Value,
            ZoomOutButtonEnabled: StatusZoomOutButton.IsEnabled,
            ZoomInButtonEnabled: StatusZoomInButton.IsEnabled,
            ZoomControlSummary: CreateStatusFooterInteractionsZoomSummary(),
            FormulaBarText: FormulaBar.Text,
            FocusedElementType: focus.ElementType,
            FocusedAutomationId: focus.AutomationId,
            FocusedName: focus.Name);
    }

    private string CreateStatusFooterInteractionsViewShortcutSummary(WorksheetViewMode viewMode) =>
        $"mode={viewMode}; normal={StatusNormalViewButton.IsChecked == true}; pageLayout={StatusPageLayoutViewButton.IsChecked == true}; pageBreakPreview={StatusPageBreakPreviewButton.IsChecked == true}";

    private string CreateStatusFooterInteractionsZoomSummary() =>
        $"text={StatusZoomText.Text}; slider={ZoomSlider.Value:0.###}; zoomOutEnabled={StatusZoomOutButton.IsEnabled}; zoomInEnabled={StatusZoomInButton.IsEnabled}";

    private static (string ElementType, string AutomationId, string Name) DescribeStatusFooterInteractionsFocus()
    {
        var focused = Keyboard.FocusedElement;
        if (focused is not DependencyObject dependencyObject)
            return (focused?.GetType().Name ?? string.Empty, string.Empty, string.Empty);

        return (
            dependencyObject.GetType().Name,
            AutomationProperties.GetAutomationId(dependencyObject) ?? string.Empty,
            AutomationProperties.GetName(dependencyObject) ?? string.Empty);
    }

    private static IReadOnlyList<string> StatusFooterInteractionsTourExpectedFileNames() =>
    [
        "freex_status_footer_interactions_stats_single_number.png",
        "freex_status_footer_interactions_stats_text_only.png",
        "freex_status_footer_interactions_stats_mixed_range.png",
        "freex_status_footer_interactions_view_page_layout_clicked.png",
        "freex_status_footer_interactions_view_page_break_clicked.png",
        "freex_status_footer_interactions_view_normal_clicked.png",
        "freex_status_footer_interactions_zoom_button_out.png",
        "freex_status_footer_interactions_zoom_button_in.png",
        "freex_status_footer_interactions_zoom_100_command.png",
        "freex_status_footer_interactions_zoom_custom_125.png",
        "freex_status_footer_interactions_zoom_dialog_close_focus_return.png"
    ];

    private static void DeleteStatusFooterInteractionsTourEvidence(string outputDir)
    {
        foreach (var fileName in StatusFooterInteractionsTourExpectedFileNames())
        {
            var path = Path.Combine(outputDir, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }

        var manifestPath = Path.Combine(outputDir, StatusFooterInteractionsTourManifestFileName);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);
    }

    private static void ValidateStatusFooterInteractionsTourEvidence(
        string outputDir,
        IReadOnlyList<StatusFooterInteractionsTourManifestCapture> captures)
    {
        foreach (var capture in captures)
        {
            var path = Path.Combine(outputDir, capture.OutputFileName);
            if (!File.Exists(path))
                throw new InvalidOperationException($"Status/footer interactions tour did not create planned capture '{capture.OutputFileName}'.");
        }
    }

    private static async Task WriteStatusFooterInteractionsTourManifestAsync(
        string outputDir,
        IReadOnlyList<StatusFooterInteractionsTourManifestCapture> captures)
    {
        var plannedCaptures = captures
            .Select(capture => new StatusFooterInteractionsTourManifestPlannedCapture(
                capture.State,
                capture.OutputFileName,
                capture.EntryPath,
                "complete",
                "captured with the real FreeX WPF control/session path"))
            .Concat(
            [
                new StatusFooterInteractionsTourManifestPlannedCapture(
                    "zoom-slider-foreground-drag",
                    "not-captured",
                    "foreground mouse drag of StatusZoomSlider",
                    "planned-but-blocked",
                    "Not synthesized because unguarded foreground drag input is unsafe for this in-process tour."),
                new StatusFooterInteractionsTourManifestPlannedCapture(
                    "ctrl-wheel-foreground-zoom",
                    "not-captured",
                    "foreground Ctrl+mouse-wheel over worksheet grid",
                    "planned-but-blocked",
                    "Not synthesized because the tour does not own foreground wheel input."),
                new StatusFooterInteractionsTourManifestPlannedCapture(
                    "native-uia-rangevalue-slider",
                    "not-captured",
                    "native UIA RangeValue set on StatusZoomSlider",
                "planned-but-blocked",
                "Deferred to a foreground UIA runner so the capture can verify operating-system focus and accessibility ownership.")
            ])
            .ToList();

        var manifest = new StatusFooterInteractionsTourManifest(
            Tool: "FREEX_STATUS_FOOTER_INTERACTIONS_TOUR",
            AlsoRunsWithTool: "FREEX_STATUS_FOOTER_TOUR",
            EvidenceFamily: "status-footer-interactions",
            EvidenceSubject: "freex",
            EvidenceApp: "FreeX",
            ScenarioId: "status-footer:interaction-evidence",
            OutputDirectory: outputDir,
            OutputNaming: "freex_status_footer_interactions_<State>.png",
            CatalogEvidenceTarget: "docs/testing/ui-test-catalog.md",
            CatalogIds:
            [
                "UI-CAT-STATUS-001A",
                "UI-CAT-STATUS-001B",
                "UI-CAT-STATUS-001C",
                "UI-CAT-STATUS-001D",
                "UI-CAT-STATUS-003A-E",
                "UI-CMD-STATUS-001",
                "UI-CMD-STATUS-002",
                "UI-CMD-STATUS-003"
            ],
            CaptureStatus: "complete-with-planned-limitations",
            CaptureMode: IsScreenshotTourBackgroundRenderAllowed()
                ? "background-render-opt-in"
                : "foreground-guarded-render",
            PlannedCaptureCount: plannedCaptures.Count,
            ActualCaptureCount: captures.Count,
            Pairing: new StatusFooterInteractionsTourManifestPairing(
                "interactive:status-footer-interactions:<State>",
                "manual-or-excel",
                "not-yet-wired",
                "not-yet-captured"),
            FocusGuard: new RibbonScreenshotTourManifestFocusGuard(
                Required: !IsScreenshotTourBackgroundRenderAllowed(),
                Policy: IsScreenshotTourBackgroundRenderAllowed()
                    ? "FREEX_SS_TOUR_ALLOW_BACKGROUND_RENDER=1 was set; no global mouse, keyboard, wheel, drag, UIA, or screen capture input is used."
                    : "FreeX main window must own foreground focus before each RenderTargetBitmap window capture."),
            PlannedCaptures: plannedCaptures,
            Captures: captures,
            CoveredStates:
            [
                "selection statistics changing from single numeric cell to text-only range to mixed numeric/text range",
                "status footer view shortcut button click results for Normal, Page Layout, and Page Break Preview",
                "status footer Zoom Out and Zoom In button click results routed through the production slider/value-changed command path",
                "View 100% zoom command result reflected in status zoom text and slider value",
                "custom 125% zoom result created through the production ZoomDialog parser/planner path",
                "worksheet focus/status stability after an owned Zoom dialog closes"
            ],
            Limitations:
            [
                "RenderTargetBitmap evidence only; it is not foreground CopyFromScreen proof.",
                "The tour uses WPF ButtonBase.Click events and command/session methods instead of physical mouse clicks.",
                "The custom zoom result uses the production ZoomDialog result parser/planner and slider route; it does not type into the modal dialog.",
                "Foreground wheel zoom, live status-slider drag, and native UIA RangeValue interaction are recorded as planned-but-blocked captures rather than synthesized unsafely.",
                "No paired Microsoft Excel screenshots are produced by this tool."
            ]);

        var path = Path.Combine(outputDir, StatusFooterInteractionsTourManifestFileName);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, manifest, RibbonScreenshotTourManifestJsonContext.Default.StatusFooterInteractionsTourManifest);
    }

    private sealed record StatusFooterInteractionsTourManifest(
        string Tool,
        string AlsoRunsWithTool,
        string EvidenceFamily,
        string EvidenceSubject,
        string EvidenceApp,
        string ScenarioId,
        string OutputDirectory,
        string OutputNaming,
        string CatalogEvidenceTarget,
        IReadOnlyList<string> CatalogIds,
        string CaptureStatus,
        string CaptureMode,
        int PlannedCaptureCount,
        int ActualCaptureCount,
        StatusFooterInteractionsTourManifestPairing Pairing,
        RibbonScreenshotTourManifestFocusGuard FocusGuard,
        IReadOnlyList<StatusFooterInteractionsTourManifestPlannedCapture> PlannedCaptures,
        IReadOnlyList<StatusFooterInteractionsTourManifestCapture> Captures,
        IReadOnlyList<string> CoveredStates,
        IReadOnlyList<string> Limitations);

    private sealed record StatusFooterInteractionsTourManifestPairing(
        string PairKeyPattern,
        string CounterpartSubject,
        string CounterpartTool,
        string CounterpartOutputNaming);

    private sealed record StatusFooterInteractionsTourManifestPlannedCapture(
        string State,
        string OutputFileName,
        string EntryPath,
        string Status,
        string Notes);

    private sealed record StatusFooterInteractionsTourManifestCapture(
        string CaptureKey,
        string PairKey,
        string ScenarioId,
        string State,
        string CaptureStatus,
        string FileName,
        string OutputFileName,
        string CaptureMethod,
        string EntryPath,
        string EvidencePurpose,
        double CaptureLogicalWidth,
        double CaptureLogicalHeight,
        string ActiveRange,
        IReadOnlyList<string> SelectedRanges,
        string StatusModeText,
        bool StatsVisible,
        string AverageText,
        string CountText,
        string NumericalCountText,
        string SumText,
        string MinText,
        string MaxText,
        string ViewMode,
        bool NormalViewChecked,
        bool PageLayoutViewChecked,
        bool PageBreakPreviewChecked,
        string ViewShortcutSummary,
        string ZoomText,
        int ZoomPercent,
        double ZoomSliderValue,
        bool ZoomOutButtonEnabled,
        bool ZoomInButtonEnabled,
        string ZoomControlSummary,
        string FormulaBarText,
        string FocusedElementType,
        string FocusedAutomationId,
        string FocusedName);
}
