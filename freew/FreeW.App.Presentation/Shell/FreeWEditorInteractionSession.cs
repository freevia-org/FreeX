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

public sealed record FreeWDocumentViewSnapshot(
    DocumentViewMode ViewMode,
    bool IsOutlineMode,
    bool IsPagedEditMode,
    bool IsPaginatedViewActive);

public sealed record FreeWDocumentViewChangePlan(
    DocumentViewMode TargetMode,
    bool ExitOutlineMode,
    bool ExitPagedEditMode,
    bool ExitPaginatedView);

public sealed record FreeWDocumentViewCheckPlan(
    bool PrintLayout,
    bool WebLayout,
    bool Draft,
    bool PagedEdit);

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

    public FreeWDocumentViewChangePlan PlanDocumentViewChange(
        FreeWDocumentViewSnapshot snapshot,
        DocumentViewMode targetMode)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (targetMode == DocumentViewMode.PagedEdit)
            throw new ArgumentOutOfRangeException(nameof(targetMode), targetMode, "Paged Edit is an overlay workflow.");

        return new FreeWDocumentViewChangePlan(
            targetMode,
            ExitOutlineMode: snapshot.IsOutlineMode,
            ExitPagedEditMode: snapshot.IsPagedEditMode,
            ExitPaginatedView: snapshot.IsPaginatedViewActive);
    }

    public FreeWDocumentViewCheckPlan BuildDocumentViewChecks(FreeWDocumentViewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new FreeWDocumentViewCheckPlan(
            PrintLayout: !snapshot.IsOutlineMode && !snapshot.IsPagedEditMode &&
                         snapshot.ViewMode == DocumentViewMode.PrintLayout,
            WebLayout: !snapshot.IsOutlineMode && !snapshot.IsPagedEditMode &&
                       snapshot.ViewMode == DocumentViewMode.WebLayout,
            Draft: !snapshot.IsOutlineMode && !snapshot.IsPagedEditMode &&
                   snapshot.ViewMode == DocumentViewMode.Draft,
            PagedEdit: snapshot.IsPagedEditMode);
    }

    public FreeWEditorStatusPlan BuildStatus(FreeWEditorStatusSnapshot snapshot) =>
        FreeWEditorStatusPlanner.Build(snapshot);

    private FreeWReadModeTransition BuildReadModeTransition(FreeWEditorChromeVisibility chrome) => new(
        IsReadModeActive,
        chrome,
        FreeWReadModePlanner.ColumnWidth(ReadModeColumnWidth),
        FreeWReadModePlanner.PageColorHex(ReadModePageColor));
}
