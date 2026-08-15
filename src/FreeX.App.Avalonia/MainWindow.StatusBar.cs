using System.Collections.Generic;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Free.Shared.AppServices;
using FreeX.App.Services;

namespace FreeX.App.Avalonia;

public sealed partial class MainWindow
{
    private static readonly IStatusBarTextProvider StatusBarTextProvider =
        new ResourceKeyStatusBarTextProvider(UiText.Get);

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
    private readonly Dictionary<string, bool> _statusBarOptionVisibility;

    // Tracks the runtime-built customize toggle items by OptionTag so the menu's live checked state can
    // be refreshed on open, mirroring the WPF host's _statusBarCustomizeMenuItems registry.
    private readonly Dictionary<string, MenuItem> _statusBarCustomizeMenuItems = new(StringComparer.Ordinal);

    // Selection statistic controls receive their polite live-region metadata at construction.
    // Only the status text still needs lazy initialization because it is renderer-global state.
    private bool _statusTextLiveSettingApplied;
    private StatusBarAutomationSnapshot? _lastStatusBarAutomationSnapshot;

    private bool GetStatusBarOption(string optionTag) =>
        StatusBarVisibilityPlanner.IsOptionVisible(_statusBarOptionVisibility, optionTag);

    /// <summary>
    /// Builds the neutral <see cref="StatusBarViewModel"/> for the current selection / sheet / zoom using
    /// the shared builder and the session's <see cref="FreeX.App.Services.WorkbookSession.SelectionStats"/>
    /// (the same stats path the WPF host consumes — no re-implementation of the stats math).
    /// </summary>
    private StatusBarViewModel BuildStatusBarViewModel(string readyText) =>
        FreeXStatusBarRendererPlanner.BuildModel(
            _session.SelectionStats,
            StatusBarZoomSliderPlanner.ClampZoomPercent(_session.ZoomPercent),
            // R128-status-bar-calculate-indicator: CalculationModeIsManual (MainWindow.Calculation.cs)
            // + Workbook.HasPendingManualRecalculation drive Excel's "Calculate" cell-mode indicator in
            // place of "Ready" -- see NormalizeReadyText's calc-mode overload.
            FreeXStatusBarRendererPlanner.NormalizeReadyText(
                readyText,
                StatusBarTextProvider,
                CalculationModeIsManual,
                _session.Workbook.HasPendingManualRecalculation),
            _session.ViewMode,
            StatusBarTextProvider);

    /// <summary>
    /// Renders the six selection-statistic fields and ready text
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
        var rendererPlan = FreeXStatusBarRendererPlanner.BuildRendererPlan(model, _statusBarOptionVisibility);
        _statusText.Text = rendererPlan.ReadyText;
        _statusText.Foreground = StatusBarForeground;
        _statusText.IsVisible = rendererPlan.ReadyTextVisible;

        ApplySelectionStatisticReadouts(rendererPlan);

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
        var result = StatusBarOptionUpdateWorkflow.ApplyToRuntimeSession(
            _optionsRuntimeSession,
            optionTag,
            isChecked);
        _statusBarOptionVisibility.Clear();
        foreach (var (tag, isVisible) in result.Visibility.ToDictionary())
            _statusBarOptionVisibility[tag] = isVisible;
        if (result.IsRecognized && !result.IsPersisted)
        {
            ShowEditIssue(result.PersistenceError ?? UiText.Get("Options_SaveFailed"));
        }

        ApplyStatusBarModel(_statusText.Text ?? StatusBarTextProvider.GetReadyText());
    }

    // ── Accessibility: live-region announcement for selection statistics ─────
    // Match WPF's six independently named polite live controls. Each field carries its formatted
    // value in Name and HelpText while visible so assistive clients announce the statistic that
    // changed. The shared renderer plan remains the sole authority for text and visibility.
    private void ConfigureSelectionStatisticText(
        TextBlock textBlock,
        StatusBarReadoutKind kind,
        double maxWidth,
        double trailingMargin)
    {
        var fallbackName = UiText.Get(StatusBarTextResourceKeys.ReadoutLabel(kind));
        textBlock.FontSize = 12;
        textBlock.Foreground = StatusBarForeground;
        textBlock.MaxWidth = maxWidth;
        textBlock.Margin = new global::Avalonia.Thickness(0, 0, trailingMargin, 0);
        textBlock.TextTrimming = global::Avalonia.Media.TextTrimming.CharacterEllipsis;
        textBlock.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center;
        textBlock.IsVisible = false;
        AutomationProperties.SetAutomationId(
            textBlock,
            StatusBarPresentationPlanner.ReadoutAutomationId(kind));
        AutomationProperties.SetName(textBlock, fallbackName);
        AutomationProperties.SetHelpText(textBlock, fallbackName);
        AutomationProperties.SetLiveSetting(textBlock, AutomationLiveSetting.Polite);
    }

    private IEnumerable<TextBlock> SelectionStatisticTexts()
    {
        yield return _statusAverageText;
        yield return _statusCountText;
        yield return _statusNumericalCountText;
        yield return _statusSumText;
        yield return _statusMinimumText;
        yield return _statusMaximumText;
    }

    private TextBlock SelectionStatisticText(StatusBarReadoutKind kind) => kind switch
    {
        StatusBarReadoutKind.Average => _statusAverageText,
        StatusBarReadoutKind.Count => _statusCountText,
        StatusBarReadoutKind.NumericalCount => _statusNumericalCountText,
        StatusBarReadoutKind.Sum => _statusSumText,
        StatusBarReadoutKind.Minimum => _statusMinimumText,
        StatusBarReadoutKind.Maximum => _statusMaximumText,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    private void ApplySelectionStatisticReadouts(StatusBarRendererPlan rendererPlan)
    {
        _selectionStatsPanel.IsVisible = rendererPlan.VisibleReadoutTextVisible;

        foreach (var readout in rendererPlan.ReadoutElements)
        {
            var textBlock = SelectionStatisticText(readout.Kind);
            var isVisible = rendererPlan.IsElementVisible(readout.Element) &&
                !string.IsNullOrWhiteSpace(readout.Text);
            if (!string.Equals(textBlock.Text, readout.Text, StringComparison.Ordinal))
                textBlock.Text = readout.Text;
            textBlock.Foreground = StatusBarForeground;
            textBlock.IsVisible = isVisible;
            if (!string.Equals(
                    AutomationProperties.GetAutomationId(textBlock),
                    readout.AutomationId,
                    StringComparison.Ordinal))
            {
                AutomationProperties.SetAutomationId(textBlock, readout.AutomationId);
            }

        }

        var automationSnapshot = StatusBarAutomationChangePlanner.BuildSnapshot(
            rendererPlan,
            UiText.Get,
            UiText.Get("Toolbar_SelectionStatisticsAutomationName"));
        foreach (var change in StatusBarAutomationChangePlanner.PlanChanges(
                     _lastStatusBarAutomationSnapshot,
                     automationSnapshot))
        {
            var control = change.Current.Element == StatusBarPresentationElement.StatsPanel
                ? (Control)_selectionStatsPanel
                : SelectionStatisticText(ReadoutKind(change.Current.Element));
            AutomationProperties.SetAutomationId(control, change.Current.AutomationId);
            AutomationProperties.SetName(control, change.Current.Name);
            AutomationProperties.SetHelpText(control, change.Current.HelpText);
            if (change.ShouldNotify)
                NotifyStatusBarAutomationChanged(control, change.PreviousName, change.Current.Name);
        }

        _lastStatusBarAutomationSnapshot = automationSnapshot;
    }

    private static StatusBarReadoutKind ReadoutKind(StatusBarPresentationElement element) => element switch
    {
        StatusBarPresentationElement.Average => StatusBarReadoutKind.Average,
        StatusBarPresentationElement.Count => StatusBarReadoutKind.Count,
        StatusBarPresentationElement.NumericalCount => StatusBarReadoutKind.NumericalCount,
        StatusBarPresentationElement.Sum => StatusBarReadoutKind.Sum,
        StatusBarPresentationElement.Minimum => StatusBarReadoutKind.Minimum,
        StatusBarPresentationElement.Maximum => StatusBarReadoutKind.Maximum,
        _ => throw new ArgumentOutOfRangeException(nameof(element), element, null),
    };

    private static void NotifyStatusBarAutomationChanged(
        Control control,
        string previousName,
        string currentName)
    {
        if (TopLevel.GetTopLevel(control) is null)
            return;

        try
        {
            var peer = ControlAutomationPeer.FromElement(control)
                ?? ControlAutomationPeer.CreatePeerForElement(control);
            peer?.RaisePropertyChangedEvent(
                AutomationElementIdentifiers.NameProperty,
                previousName,
                currentName);
        }
        catch (InvalidOperationException)
        {
        }
    }

    // ── Accessibility: live-region announcement for status/edit-issue text ───
    // _statusText carries both routine "Ready"/save-completed status AND edit/validation-commit
    // failure messages (ShowEditIssue, ShowSaveIssue, ShowOpenIssue, ShowExportIssue) — the Avalonia
    // shell has no owned modal MessageBox for validation violations the way WPF's
    // MainWindow.Editing.cs ShowOwnedMessage does, so this text update is the ONLY signal a failed
    // commit occurred. Without a live region a screen-reader user gets no announcement at all and can
    // believe a rejected edit succeeded. Name/HelpText stay their fixed "Status"/help-text values;
    // LiveSetting is what makes a content/Text change get announced.
    private void EnsureStatusTextLiveRegion()
    {
        if (_statusTextLiveSettingApplied)
            return;

        AutomationProperties.SetLiveSetting(_statusText, AutomationLiveSetting.Polite);
        _statusTextLiveSettingApplied = true;
    }
}
