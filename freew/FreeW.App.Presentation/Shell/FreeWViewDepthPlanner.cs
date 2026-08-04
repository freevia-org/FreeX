using FreeW.App.Presentation.DocumentView;

namespace FreeW.App.Presentation.Shell;

public enum FreeWViewDepthMode
{
    LiveEditor,
    SplitPreview,
    MultiplePagesPreview,
    SideToSidePreview
}

public enum FreeWViewDepthCommand
{
    RestoreLiveEditor,
    ToggleSplit,
    ToggleMultiplePages,
    ToggleSideToSide
}

public enum FreeWViewDepthPagePairNavigationCommand
{
    PreviousPair,
    NextPair
}

public enum FreeWViewDepthSurfaceKind
{
    LiveEditor,
    SplitEditorWithReadOnlyPreview,
    ReadOnlyPagePreview,
    EditablePageView
}

public sealed record FreeWViewDepthState(FreeWViewDepthMode Mode)
{
    public bool IsSplitActive => Mode == FreeWViewDepthMode.SplitPreview;
    public bool IsMultiplePagesActive => Mode == FreeWViewDepthMode.MultiplePagesPreview;
    public bool IsSideToSideActive => Mode == FreeWViewDepthMode.SideToSidePreview;
}

public sealed record FreeWViewDepthPlan(
    FreeWViewDepthMode Mode,
    FreeWViewDepthSurfaceKind SurfaceKind,
    bool IsSplitActive,
    bool IsMultiplePagesActive,
    bool IsSideToSideActive,
    bool UsesReadOnlySnapshot,
    int PagesAcross,
    DocumentViewDepthLayoutPlan Layout,
    string StatusText,
    string? Limitation);

public sealed record FreeWViewDepthPagePairNavigationState(
    FreeWViewDepthMode Mode,
    int FirstVisiblePageNumber,
    int LastVisiblePageNumber,
    int TotalPages,
    int PagesPerPair,
    bool CanGoToPreviousPair,
    bool CanGoToNextPair,
    string StatusText)
{
    public bool IsSideToSideNavigationActive => Mode == FreeWViewDepthMode.SideToSidePreview;
}

public static class FreeWViewDepthPlanner
{
    public static FreeWViewDepthPlan Plan(FreeWViewDepthState current, FreeWViewDepthCommand command)
    {
        var target = command switch
        {
            FreeWViewDepthCommand.RestoreLiveEditor => FreeWViewDepthMode.LiveEditor,
            FreeWViewDepthCommand.ToggleSplit => current.IsSplitActive
                ? FreeWViewDepthMode.LiveEditor
                : FreeWViewDepthMode.SplitPreview,
            FreeWViewDepthCommand.ToggleMultiplePages => current.IsMultiplePagesActive
                ? FreeWViewDepthMode.LiveEditor
                : FreeWViewDepthMode.MultiplePagesPreview,
            FreeWViewDepthCommand.ToggleSideToSide => current.IsSideToSideActive
                ? FreeWViewDepthMode.LiveEditor
                : FreeWViewDepthMode.SideToSidePreview,
            _ => FreeWViewDepthMode.LiveEditor
        };

        return Build(target);
    }

    public static FreeWViewDepthPlan Build(FreeWViewDepthMode mode) => mode switch
    {
        FreeWViewDepthMode.SplitPreview => new FreeWViewDepthPlan(
            mode,
            FreeWViewDepthSurfaceKind.SplitEditorWithReadOnlyPreview,
            IsSplitActive: true,
            IsMultiplePagesActive: false,
            IsSideToSideActive: false,
            UsesReadOnlySnapshot: true,
            PagesAcross: 1,
            Layout: DocumentViewDepthLayoutPlanner.Build(mode),
            StatusText: "Split view active: live editor above, read-only paginated snapshot below.",
            Limitation: "The Avalonia split preview is read-only in the secondary pane; dual live editing remains deferred."),
        FreeWViewDepthMode.MultiplePagesPreview => new FreeWViewDepthPlan(
            mode,
            FreeWViewDepthSurfaceKind.ReadOnlyPagePreview,
            IsSplitActive: false,
            IsMultiplePagesActive: true,
            IsSideToSideActive: false,
            UsesReadOnlySnapshot: true,
            PagesAcross: 2,
            Layout: DocumentViewDepthLayoutPlanner.Build(mode),
            StatusText: "Multiple Pages view active: read-only 2-by-2 page-grid preview.",
            Limitation: "Editing is disabled while the Multiple Pages preview is active; editable page grids remain deferred."),
        FreeWViewDepthMode.SideToSidePreview => new FreeWViewDepthPlan(
            mode,
            FreeWViewDepthSurfaceKind.EditablePageView,
            IsSplitActive: false,
            IsMultiplePagesActive: false,
            IsSideToSideActive: true,
            UsesReadOnlySnapshot: false,
            PagesAcross: 2,
            Layout: DocumentViewDepthLayoutPlanner.Build(mode),
            StatusText: "Side to Side view active: editable two-page horizontal-flow view with pair navigation.",
            Limitation: "Avalonia's horizontal page-grid layout remains deferred; cross-page clipboard and undo use the live document editor."),
        _ => new FreeWViewDepthPlan(
            FreeWViewDepthMode.LiveEditor,
            FreeWViewDepthSurfaceKind.LiveEditor,
            IsSplitActive: false,
            IsMultiplePagesActive: false,
            IsSideToSideActive: false,
            UsesReadOnlySnapshot: false,
            PagesAcross: 1,
            Layout: DocumentViewDepthLayoutPlanner.Build(FreeWViewDepthMode.LiveEditor),
            StatusText: "Live editor active.",
            Limitation: null)
    };

    public static double BuildPreviewScale(
        FreeWViewDepthMode mode,
        double viewportWidthDip,
        double viewportHeightDip,
        double pageWidthDip,
        double pageHeightDip)
    {
        if (!double.IsFinite(viewportWidthDip) || viewportWidthDip <= 0 ||
            !double.IsFinite(viewportHeightDip) || viewportHeightDip <= 0 ||
            !double.IsFinite(pageWidthDip) || pageWidthDip <= 0 ||
            !double.IsFinite(pageHeightDip) || pageHeightDip <= 0)
        {
            return 1.0;
        }

        return DocumentViewDepthLayoutPlanner.BuildPreviewScale(
            Build(mode).Layout,
            viewportWidthDip,
            viewportHeightDip,
            pageWidthDip,
            pageHeightDip);
    }

    public static FreeWViewDepthPagePairNavigationState BuildPagePairNavigation(
        FreeWViewDepthPlan plan,
        int requestedFirstVisiblePageNumber,
        int totalPages)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var safeTotalPages = Math.Max(1, totalPages);
        var pagesPerPair = Math.Max(1, plan.Layout.PagesAcross);
        if (!plan.IsSideToSideActive)
        {
            return new FreeWViewDepthPagePairNavigationState(
                plan.Mode,
                FirstVisiblePageNumber: 1,
                LastVisiblePageNumber: 1,
                TotalPages: safeTotalPages,
                PagesPerPair: pagesPerPair,
                CanGoToPreviousPair: false,
                CanGoToNextPair: false,
                StatusText: plan.StatusText);
        }

        var first = NormalizePairStart(requestedFirstVisiblePageNumber, safeTotalPages, pagesPerPair);
        var last = Math.Min(safeTotalPages, first + pagesPerPair - 1);
        var maxStart = NormalizePairStart(safeTotalPages, safeTotalPages, pagesPerPair);

        return new FreeWViewDepthPagePairNavigationState(
            plan.Mode,
            first,
            last,
            safeTotalPages,
            pagesPerPair,
            CanGoToPreviousPair: first > 1,
            CanGoToNextPair: first < maxStart,
            FormatSideToSidePagePairStatus(first, last, safeTotalPages));
    }

    public static FreeWViewDepthPagePairNavigationState NavigatePagePair(
        FreeWViewDepthPlan plan,
        FreeWViewDepthPagePairNavigationState current,
        FreeWViewDepthPagePairNavigationCommand command)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(current);

        var step = Math.Max(1, current.PagesPerPair);
        var requested = command switch
        {
            FreeWViewDepthPagePairNavigationCommand.PreviousPair => current.FirstVisiblePageNumber - step,
            FreeWViewDepthPagePairNavigationCommand.NextPair => current.FirstVisiblePageNumber + step,
            _ => current.FirstVisiblePageNumber
        };

        return BuildPagePairNavigation(plan, requested, current.TotalPages);
    }

    private static int NormalizePairStart(int requestedFirstVisiblePageNumber, int totalPages, int pagesPerPair)
    {
        var safeTotalPages = Math.Max(1, totalPages);
        var safePagesPerPair = Math.Max(1, pagesPerPair);
        var clamped = Math.Clamp(requestedFirstVisiblePageNumber, 1, safeTotalPages);
        return ((clamped - 1) / safePagesPerPair) * safePagesPerPair + 1;
    }

    private static string FormatSideToSidePagePairStatus(int first, int last, int totalPages) =>
        first == last
            ? $"Side to Side page {first} of {totalPages}."
            : $"Side to Side pages {first}-{last} of {totalPages}.";
}
