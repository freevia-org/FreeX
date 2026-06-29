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
    private readonly Dictionary<string, bool> _statusBarOptionVisibility =
        AvaloniaStatusBarSource.CreateDefaultOptionVisibility();

    // Tracks the runtime-built customize toggle items by OptionTag so the menu's live checked state can
    // be refreshed on open, mirroring the WPF host's _statusBarCustomizeMenuItems registry.
    private readonly Dictionary<string, MenuItem> _statusBarCustomizeMenuItems = new(StringComparer.Ordinal);

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
            AvaloniaStatusBarSource.NormalizeReadyText(readyText),
            _session.ActiveSheet.ViewMode);

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
        var plan = AvaloniaStatusBarSource.BuildPresentation(model, _statusBarOptionVisibility);
        var visibility = plan.Visibility;
        _statusText.Text = plan.ReadyText;
        _statusText.Foreground = StatusBarForeground;
        _statusText.IsVisible = visibility.ReadyTextVisible && plan.VisibleReadoutText.Length == 0;

        _selectionStatsText.Text = plan.VisibleReadoutText;
        _selectionStatsText.Foreground = StatusBarForeground;
        _selectionStatsText.IsVisible = visibility.StatsPanelVisible && plan.VisibleReadoutText.Length > 0;
        // Keep the accessible NAME a stable label ("Selection statistics"); the dynamic readouts are the
        // element's Text (value/content). Overwriting Name with the readouts broke the launch-smoke /
        // accessibility contract (GetName must equal "Selection statistics") whenever a selection had stats.
        AutomationProperties.SetName(_selectionStatsText, "Selection statistics");

        _zoomText.IsVisible = visibility.ZoomVisible;
        _zoomText.Foreground = StatusBarForeground;
        _zoomText.Text = StatusBarZoomSliderPlanner.FormatZoomPercent(plan.ZoomPercent);

        _statusNormalViewButton.IsVisible = visibility.ViewShortcutsVisible;
        _statusPageLayoutViewButton.IsVisible = visibility.ViewShortcutsVisible;
        _statusPageBreakPreviewButton.IsVisible = visibility.ViewShortcutsVisible;
        UpdateStatusBarViewButtons();

        _statusZoomSliderHost.IsVisible = visibility.ZoomSliderVisible;
        _statusZoomSlider.IsVisible = visibility.ZoomSliderVisible;
        _isUpdatingStatusZoomSlider = true;
        try
        {
            var sliderPlan = StatusBarZoomSliderPlanner.Build(plan.ZoomPercent);
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
        ApplyStatusBarModel(_statusText.Text ?? AvaloniaStatusBarSource.ReadyText());
    }
}
