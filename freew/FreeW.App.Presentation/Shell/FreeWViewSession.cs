using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Shell;

public sealed record FreeWViewDepthTransition(
    FreeWViewDepthPlan Previous,
    FreeWViewDepthPlan Current,
    bool ExitSplitSurface,
    bool ExitPageSurface);

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

public sealed record FreeWOutlineViewTransition(
    bool IsOutlineMode,
    bool IsPagedEditMode,
    bool ExitPageSurface,
    bool ExitPagedEditSurface,
    bool EnterPagedEditSurface);

/// <summary>
/// Owns the portable view-mode and view-depth state for a FreeW work area. Renderers apply the
/// returned plans to native controls and continue to own measurement, focus, and surface creation.
/// </summary>
public sealed class FreeWViewSession
{
    private readonly FreeWViewDepthCapabilities _capabilities;
    private bool _restorePagedEditAfterOutline;

    public FreeWViewSession(FreeWViewDepthCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        _capabilities = capabilities;
        CurrentDepth = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.LiveEditor, capabilities);
        PagePairNavigation = FreeWViewDepthPlanner.BuildPagePairNavigation(
            CurrentDepth,
            requestedFirstVisiblePageNumber: 1,
            totalPages: 1);
    }

    public FreeWViewDepthPlan CurrentDepth { get; private set; }

    public FreeWViewDepthPagePairNavigationState PagePairNavigation { get; private set; }

    public bool IsPageSurfaceActive =>
        CurrentDepth.SurfaceKind is FreeWViewDepthSurfaceKind.ReadOnlyPagePreview or
            FreeWViewDepthSurfaceKind.EditablePageView;

    public FreeWViewDepthTransition Execute(FreeWViewDepthCommand command) =>
        TransitionTo(FreeWViewDepthPlanner.Plan(
            new FreeWViewDepthState(CurrentDepth.Mode),
            command,
            _capabilities));

    public FreeWViewDepthTransition RestoreLiveEditor() =>
        TransitionTo(FreeWViewDepthPlanner.Build(FreeWViewDepthMode.LiveEditor, _capabilities));

    public FreeWViewDepthPagePairNavigationState StartPagePairNavigation(
        int totalPages,
        int requestedFirstVisiblePageNumber = 1)
    {
        PagePairNavigation = FreeWViewDepthPlanner.BuildPagePairNavigation(
            CurrentDepth,
            requestedFirstVisiblePageNumber,
            totalPages);
        return PagePairNavigation;
    }

    public FreeWViewDepthPagePairNavigationState NavigatePagePair(
        FreeWViewDepthPagePairNavigationCommand command)
    {
        if (!_capabilities.SupportsPagePairNavigation || !CurrentDepth.IsSideToSideActive)
            return PagePairNavigation;

        PagePairNavigation = FreeWViewDepthPlanner.NavigatePagePair(
            CurrentDepth,
            PagePairNavigation,
            command);
        return PagePairNavigation;
    }

    public void ResetPagePairNavigation()
    {
        var live = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.LiveEditor, _capabilities);
        PagePairNavigation = FreeWViewDepthPlanner.BuildPagePairNavigation(
            live,
            requestedFirstVisiblePageNumber: 1,
            totalPages: 1);
    }

    public FreeWDocumentViewChangePlan PlanDocumentViewChange(
        DocumentViewMode currentMode,
        bool isOutlineMode,
        bool isPagedEditMode,
        DocumentViewMode targetMode)
    {
        if (targetMode == DocumentViewMode.PagedEdit)
            throw new ArgumentOutOfRangeException(nameof(targetMode), targetMode, "Paged Edit is an overlay workflow.");

        return new FreeWDocumentViewChangePlan(
            targetMode,
            ExitOutlineMode: isOutlineMode,
            ExitPagedEditMode: isPagedEditMode,
            ExitPaginatedView: IsPageSurfaceActive);
    }

    public FreeWDocumentViewCheckPlan BuildDocumentViewChecks(
        DocumentViewMode currentMode,
        bool isOutlineMode,
        bool isPagedEditMode) => new(
            PrintLayout: !isOutlineMode && !isPagedEditMode && currentMode == DocumentViewMode.PrintLayout,
            WebLayout: !isOutlineMode && !isPagedEditMode && currentMode == DocumentViewMode.WebLayout,
            Draft: !isOutlineMode && !isPagedEditMode && currentMode == DocumentViewMode.Draft,
            PagedEdit: !isOutlineMode && isPagedEditMode);

    public FreeWOutlineViewTransition EnterOutline(bool isPagedEditMode)
    {
        _restorePagedEditAfterOutline = isPagedEditMode;
        return new FreeWOutlineViewTransition(
            IsOutlineMode: true,
            IsPagedEditMode: false,
            ExitPageSurface: IsPageSurfaceActive,
            ExitPagedEditSurface: isPagedEditMode,
            EnterPagedEditSurface: false);
    }

    public FreeWOutlineViewTransition LeaveOutline(bool restorePriorView = true)
    {
        var restorePagedEdit = restorePriorView && _restorePagedEditAfterOutline;
        _restorePagedEditAfterOutline = false;
        return new FreeWOutlineViewTransition(
            IsOutlineMode: false,
            IsPagedEditMode: restorePagedEdit,
            ExitPageSurface: false,
            ExitPagedEditSurface: false,
            EnterPagedEditSurface: restorePagedEdit);
    }

    private FreeWViewDepthTransition TransitionTo(FreeWViewDepthPlan next)
    {
        var previous = CurrentDepth;
        var nextIsPageSurface = next.SurfaceKind is FreeWViewDepthSurfaceKind.ReadOnlyPagePreview or
            FreeWViewDepthSurfaceKind.EditablePageView;
        var exitPageSurface = IsPageSurfaceActive &&
            (!nextIsPageSurface || previous.Mode != next.Mode);
        var exitSplitSurface = previous.IsSplitActive && !next.IsSplitActive;

        CurrentDepth = next;
        if (!next.IsSideToSideActive)
            ResetPagePairNavigation();

        return new FreeWViewDepthTransition(previous, next, exitSplitSurface, exitPageSurface);
    }
}
