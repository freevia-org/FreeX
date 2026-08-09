using System.Collections.Generic;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Free.Shared.AppServices;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    // ── Shared status-bar model wiring ────────────────────────────────────────
    // The Avalonia footer renders from the platform-neutral StatusBarViewModel produced by the shared
    // StatusBarDisplayModelBuilder (the same builder + WorkbookSelectionStats path the WPF host uses).
    // The customize toggles drive the per-readout visibility map below — the Avalonia analog of the WPF
    // host's persisted StatusBarShow* options — which filters the rendered readouts/zoom.

    // Per-option-tag visibility, keyed by the StatusBarCustomizeContextMenuPlanner OptionTag values.
    // R88-app-status-bar-aggregates-5-1: seeded from the PERSISTED AppOptions.StatusBarShow* toggles
    // (via the shared StatusBarOptionVisibilityStore, the same store the WPF host's FreeXOptions
    // implements) rather than always the hardcoded Excel defaults, so a customization made in a
    // previous session survives a relaunch instead of silently resetting every time.
    private readonly Dictionary<string, bool> _statusBarOptionVisibility =
        StatusBarOptionVisibilityStore.ToVisibility(AppOptionsStore.Load()).ToDictionary();

    // Tracks the runtime-built customize toggle items by OptionTag so the menu's live checked state can
    // be refreshed on open, mirroring the WPF host's _statusBarCustomizeMenuItems registry.
    private readonly Dictionary<string, MenuItem> _statusBarCustomizeMenuItems = new(StringComparer.Ordinal);

    // Whether AutomationProperties.LiveSetting has already been applied to _selectionStatsText.
    // Applied lazily on the first render rather than at construction time (which lives in
    // MainWindow.cs) so this file alone can wire the live-region behavior.
    private bool _selectionStatsLiveSettingApplied;

    // Whether AutomationProperties.LiveSetting has already been applied to _statusText. Same lazy
    // pattern as _selectionStatsLiveSettingApplied — see EnsureStatusTextLiveRegion.
    private bool _statusTextLiveSettingApplied;

    private bool GetStatusBarOption(string optionTag) =>
        AvaloniaStatusBarSource.IsOptionVisible(_statusBarOptionVisibility, optionTag);

    /// <summary>
    /// Builds the neutral <see cref="StatusBarViewModel"/> for the current selection / sheet / zoom using
    /// the shared builder and the session's <see cref="FreeX.App.Services.WorkbookSession.SelectionStats"/>
    /// (the same stats path the WPF host consumes — no re-implementation of the stats math).
    /// </summary>
    private StatusBarViewModel BuildStatusBarViewModel(string readyText) =>
        AvaloniaStatusBarSource.BuildModel(
            _session.SelectionStats,
            StatusBarZoomSliderPlanner.ClampZoomPercent(_session.ZoomPercent),
            // R128-status-bar-calculate-indicator: CalculationModeIsManual (MainWindow.Calculation.cs)
            // + Workbook.HasPendingManualRecalculation drive Excel's "Calculate" cell-mode indicator in
            // place of "Ready" -- see NormalizeReadyText's calc-mode overload.
            AvaloniaStatusBarSource.NormalizeReadyText(
                readyText,
                CalculationModeIsManual,
                _session.Workbook.HasPendingManualRecalculation),
            _session.ViewMode);

    /// <summary>
    /// Renders the footer readout (<see cref="_selectionStatsText"/>) and ready text
    /// (<see cref="_statusText"/>) from the neutral model, and reflects zoom on the zoom readout. The
    /// per-readout visibility map (driven by the customize menu) filters which aggregate readouts appear,
    /// mirroring the WPF host's per-option StatusBarShow* gating.
    /// </summary>
    private void ApplyStatusBarModel(string status)
    {
        var model = BuildStatusBarViewModel(status);

        // Render the neutral StatusBarViewModel: the readout is the model's visible aggregate readouts
        // (filtered by the customize toggles); zoom comes from the model; CellMode/Zoom toggles gate the
        // status / zoom controls — mirroring the WPF host's per-option StatusBarShow* gating.
        var rendererPlan = AvaloniaStatusBarSource.BuildRendererPlan(model, _statusBarOptionVisibility);
        _statusText.Text = rendererPlan.ReadyText;
        _statusText.Foreground = StatusBarForeground;
        _statusText.IsVisible = rendererPlan.ReadyTextVisible;

        _selectionStatsText.Text = rendererPlan.VisibleReadoutText;
        _selectionStatsText.Foreground = StatusBarForeground;
        _selectionStatsText.IsVisible = rendererPlan.VisibleReadoutTextVisible;
        // Keep the accessible NAME a stable label ("Selection statistics"); the dynamic readouts are the
        // element's Text (value/content). Overwriting Name with the readouts broke the launch-smoke /
        // accessibility contract (GetName must equal "Selection statistics") whenever a selection had stats.
        AutomationProperties.SetName(_selectionStatsText, "Selection statistics");
        EnsureSelectionStatsLiveRegion();

        _zoomText.IsVisible = rendererPlan.IsElementVisible(StatusBarPresentationElement.ZoomText);
        _zoomText.Foreground = StatusBarForeground;
        _zoomText.Text = StatusBarZoomSliderPlanner.FormatZoomPercent(rendererPlan.ZoomPercent);

        var viewShortcutsVisible = rendererPlan.IsElementVisible(StatusBarPresentationElement.ViewShortcuts);
        _statusNormalViewButton.IsVisible = viewShortcutsVisible;
        _statusPageLayoutViewButton.IsVisible = viewShortcutsVisible;
        _statusPageBreakPreviewButton.IsVisible = viewShortcutsVisible;
        UpdateStatusBarViewButtons();

        var zoomSliderVisible = rendererPlan.IsElementVisible(StatusBarPresentationElement.ZoomSlider);
        _statusZoomSliderHost.IsVisible = zoomSliderVisible;
        _statusZoomSlider.IsVisible = zoomSliderVisible;
        _isUpdatingStatusZoomSlider = true;
        try
        {
            var sliderPlan = StatusBarZoomSliderPlanner.Build(rendererPlan.ZoomPercent);
            _statusZoomSlider.Value = sliderPlan.SliderValue;
            UpdateStatusZoomSliderThumb(sliderPlan.SliderValue);
        }
        finally
        {
            _isUpdatingStatusZoomSlider = false;
        }
    }

    // ── "Customize Status Bar" right-click menu ───────────────────────────────
    // Built from the neutral StatusBarCustomizeContextMenuPlanner (the same plan the WPF host renders),
    // with each checkable toggle wired to flip its OptionTag in _statusBarOptionVisibility and re-render.

    private ContextMenu BuildStatusBarCustomizeContextMenu()
    {
        var menu = AvaloniaStatusBarCustomizeMenu.Build(
            GetStatusBarOption,
            OnStatusBarCustomizeToggled,
            _statusBarCustomizeMenuItems);
        menu.Opened += StatusBarCustomizeMenu_Opened;
        return menu;
    }

    private void StatusBarCustomizeMenu_Opened(object? sender, RoutedEventArgs e)
    {
        foreach (var (optionTag, menuItem) in _statusBarCustomizeMenuItems)
            menuItem.IsChecked = GetStatusBarOption(optionTag);
    }

    private void OnStatusBarCustomizeToggled(string optionTag, bool isChecked)
    {
        _statusBarOptionVisibility[optionTag] = isChecked;

        // R88-app-status-bar-aggregates-5-1: persist the toggle to the on-disk options file (mirrors
        // the WPF host's StatusBarCustomizeMenuItem_Click, which calls
        // StatusBarOptionVisibilityStore.TrySetOption(_options, ...) followed by _options.Save()) so
        // the customization survives a relaunch instead of only living in the in-memory dictionary
        // above.
        var options = AppOptionsStore.Load();
        if (StatusBarOptionVisibilityStore.TrySetOption(options, optionTag, isChecked) &&
            !AppOptionsStore.Save(options))
        {
            ShowEditIssue(options.LastPersistenceError ?? UiText.Get("Options_SaveFailed"));
        }

        ApplyStatusBarModel(_statusText.Text ?? AvaloniaStatusBarSource.ReadyText());
    }

    // ── Accessibility: live-region announcement for selection statistics ─────
    // WPF's StatusAvgText/StatusCountText/StatusNumericalCountText/StatusSumText/StatusMinText/
    // StatusMaxText are each individually AutomationProperties.LiveSetting="Polite", so a screen
    // reader announces the new values whenever a selection's Sum/Average/Count/etc. change
    // (MainWindow.xaml:1183-1212 + MainWindow.GridStatus.cs's SetStatusStatisticTextIfChanged /
    // NotifyStatusStatisticAutomationChanged, which re-raises AutomationElementIdentifiers.NameProperty
    // with the new value text). Avalonia renders all readouts into a single _selectionStatsText
    // TextBlock whose accessible NAME and HelpText must both stay their fixed, static values —
    // "Selection statistics" / "Shows statistics for the current selection." — because the launch-smoke
    // source-contract check (MainWindow.cs's BuildLaunchSmokeSnapshot / HasSelectionStatsAutomationName
    // and HasSelectionStatsAutomationHelp, pinned by tests/FreeX.App.Host.Tests/
    // MacOsAppReadinessPreflightTests.cs and tests/FreeX.App.Services.Tests/AvaloniaShellSourceTests.cs)
    // asserts both hold their static values at any point after construction, not just at startup —
    // so, unlike WPF, neither Name nor HelpText is a safe carrier for the live value here.
    //
    // Mark the control as an AT-SPI/UIA live region (LiveSetting="Polite") so any backend that
    // announces on the element's Text/content change (rather than only Name/HelpText) picks up the
    // new Sum/Average/Count readout as _selectionStatsText.Text is updated above. This is applied
    // once, lazily, since the field is constructed in MainWindow.cs (out of scope for this file).
    //
    // Full parity with WPF's per-field Name-carried announcement would additionally require either
    // restructuring this single TextBlock into six separately-named controls (mirroring WPF's
    // StatusAvgText/StatusCountText/.../StatusMaxText) or a MainWindow.cs-level accessible-name
    // strategy change — both out of scope for this pass, since MainWindow.cs (the field's owner) is
    // assigned to another change in this pass. Tracked as a residual gap rather than worked around
    // by violating the pinned Name/HelpText contract above.
    private void EnsureSelectionStatsLiveRegion()
    {
        if (_selectionStatsLiveSettingApplied)
            return;

        AutomationProperties.SetLiveSetting(_selectionStatsText, AutomationLiveSetting.Polite);
        _selectionStatsLiveSettingApplied = true;
    }

    // ── Accessibility: live-region announcement for status/edit-issue text ───
    // _statusText carries both routine "Ready"/save-completed status AND edit/validation-commit
    // failure messages (ShowEditIssue, ShowSaveIssue, ShowOpenIssue, ShowExportIssue) — the Avalonia
    // shell has no owned modal MessageBox for validation violations the way WPF's
    // MainWindow.Editing.cs ShowOwnedMessage does, so this text update is the ONLY signal a failed
    // commit occurred. Without a live region a screen-reader user gets no announcement at all and can
    // believe a rejected edit succeeded. Mirrors EnsureSelectionStatsLiveRegion's lazy-apply pattern
    // (Name/HelpText stay their fixed "Status"/help-text values; LiveSetting is what makes a
    // content/Text change get announced).
    private void EnsureStatusTextLiveRegion()
    {
        if (_statusTextLiveSettingApplied)
            return;

        AutomationProperties.SetLiveSetting(_statusText, AutomationLiveSetting.Polite);
        _statusTextLiveSettingApplied = true;
    }
}
