using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Shell;

public enum FreeWChromeVisibility
{
    Visible,
    Hidden,
    Collapsed,
}

public sealed record FreeWEditorChromeVisibility(
    FreeWChromeVisibility TitleBar,
    FreeWChromeVisibility Ribbon,
    FreeWChromeVisibility DataFolder,
    FreeWChromeVisibility ViewSwitch,
    FreeWChromeVisibility Zoom,
    FreeWChromeVisibility NavigationPane,
    FreeWChromeVisibility RevealPane,
    FreeWChromeVisibility ReviewingPane)
{
    public static FreeWEditorChromeVisibility ReadMode { get; } = new(
        TitleBar: FreeWChromeVisibility.Collapsed,
        Ribbon: FreeWChromeVisibility.Collapsed,
        DataFolder: FreeWChromeVisibility.Collapsed,
        ViewSwitch: FreeWChromeVisibility.Collapsed,
        Zoom: FreeWChromeVisibility.Collapsed,
        NavigationPane: FreeWChromeVisibility.Collapsed,
        RevealPane: FreeWChromeVisibility.Collapsed,
        ReviewingPane: FreeWChromeVisibility.Collapsed);
}

public sealed record FreeWReadModeTransition(
    bool IsActive,
    FreeWEditorChromeVisibility Chrome,
    double ColumnWidth,
    string PageColorHex);

public sealed record FreeWReadModeColumnPlan(
    string Token,
    double ColumnWidth,
    bool ApplyImmediately);

public sealed record FreeWReadModePageColorPlan(
    string Token,
    string PageColorHex,
    bool ApplyImmediately);

/// <summary>
/// Owns host-neutral interaction state and decisions for the FreeW editor work area.
/// Native controls, rendering, focus, and editor-layout snapshots stay in the platform adapters.
/// </summary>
public sealed class FreeWEditorInteractionSession
{
    private FreeWEditorChromeVisibility? _chromeBeforeReadMode;

    public bool IsReadModeActive { get; private set; }

    public string ReadModeColumnWidth { get; private set; } = FreeWReadModePlanner.DefaultColumn;

    public string ReadModePageColor { get; private set; } = FreeWReadModePlanner.NoColor;

    public FreeWReadModeTransition ToggleReadMode(FreeWEditorChromeVisibility currentChrome)
    {
        ArgumentNullException.ThrowIfNull(currentChrome);

        IsReadModeActive = !IsReadModeActive;
        if (IsReadModeActive)
        {
            _chromeBeforeReadMode = currentChrome;
            return BuildReadModeTransition(FreeWEditorChromeVisibility.ReadMode);
        }

        var restoredChrome = _chromeBeforeReadMode ?? currentChrome;
        _chromeBeforeReadMode = null;
        return BuildReadModeTransition(restoredChrome);
    }

    public FreeWReadModeColumnPlan UpdateReadModeColumnWidth(string? token)
    {
        ReadModeColumnWidth = FreeWReadModePlanner.NormalizeColumnWidth(token);
        return new FreeWReadModeColumnPlan(
            ReadModeColumnWidth,
            FreeWReadModePlanner.ColumnWidth(ReadModeColumnWidth),
            IsReadModeActive);
    }

    public FreeWReadModePageColorPlan UpdateReadModePageColor(string? token)
    {
        ReadModePageColor = FreeWReadModePlanner.NormalizePageColor(token);
        return new FreeWReadModePageColorPlan(
            ReadModePageColor,
            FreeWReadModePlanner.PageColorHex(ReadModePageColor),
            IsReadModeActive);
    }

    public FreeWEditorStatusPlan BuildStatus(FreeWEditorStatusSnapshot snapshot) =>
        FreeWEditorStatusPlanner.Build(snapshot);

    public FreeWEditorStatusPlan BuildStatus(FreeWEditorStatusContext context) =>
        FreeWEditorStatusPlanner.Build(context);

    private FreeWReadModeTransition BuildReadModeTransition(FreeWEditorChromeVisibility chrome) => new(
        IsReadModeActive,
        chrome,
        FreeWReadModePlanner.ColumnWidth(ReadModeColumnWidth),
        FreeWReadModePlanner.PageColorHex(ReadModePageColor));
}
