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
    SplitEditors,
    ReadOnlyPagePreview,
    EditablePageView
}

public sealed record FreeWViewDepthCapabilities(
    bool SupportsSplitPreview,
    bool SupportsMultiplePagesPreview,
    bool SupportsSideToSidePreview,
    bool SupportsEditableSideToSide,
    bool SupportsPagePairNavigation)
{
    public static FreeWViewDepthCapabilities FullDesktop { get; } = new(
        SupportsSplitPreview: true,
        SupportsMultiplePagesPreview: true,
        SupportsSideToSidePreview: true,
        SupportsEditableSideToSide: true,
        SupportsPagePairNavigation: true);

    public bool Supports(FreeWViewDepthMode mode) => mode switch
    {
        FreeWViewDepthMode.LiveEditor => true,
        FreeWViewDepthMode.SplitPreview => SupportsSplitPreview,
        FreeWViewDepthMode.MultiplePagesPreview => SupportsMultiplePagesPreview,
        FreeWViewDepthMode.SideToSidePreview => SupportsSideToSidePreview,
        _ => false,
    };
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
    public static FreeWViewDepthPlan Plan(
        FreeWViewDepthState current,
        FreeWViewDepthCommand command,
        FreeWViewDepthCapabilities? capabilities = null)
    {
        capabilities ??= FreeWViewDepthCapabilities.FullDesktop;
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

        return Build(capabilities.Supports(target) ? target : current.Mode, capabilities);
    }

    public static FreeWViewDepthPlan Build(
        FreeWViewDepthMode mode,
        FreeWViewDepthCapabilities? capabilities = null)
    {
        capabilities ??= FreeWViewDepthCapabilities.FullDesktop;
        if (!capabilities.Supports(mode))
            mode = FreeWViewDepthMode.LiveEditor;

        return mode switch
        {
        FreeWViewDepthMode.SplitPreview => new FreeWViewDepthPlan(
            mode,
            FreeWViewDepthSurfaceKind.SplitEditors,
            IsSplitActive: true,
            IsMultiplePagesActive: false,
            IsSideToSideActive: false,
            UsesReadOnlySnapshot: false,
            PagesAcross: 1,
            Layout: DocumentViewDepthLayoutPlanner.Build(mode),
            StatusText: "Split view active: synchronized live editors above and below.",
            Limitation: null),
        FreeWViewDepthMode.MultiplePagesPreview => new FreeWViewDepthPlan(
            mode,
            FreeWViewDepthSurfaceKind.EditablePageView,
            IsSplitActive: false,
            IsMultiplePagesActive: true,
            IsSideToSideActive: false,
            UsesReadOnlySnapshot: false,
            PagesAcross: 2,
            Layout: DocumentViewDepthLayoutPlanner.Build(mode),
            StatusText: "Multiple Pages view active: editable 2-by-2 page grid.",
            Limitation: null),
        FreeWViewDepthMode.SideToSidePreview => new FreeWViewDepthPlan(
            mode,
            capabilities.SupportsEditableSideToSide
                ? FreeWViewDepthSurfaceKind.EditablePageView
                : FreeWViewDepthSurfaceKind.ReadOnlyPagePreview,
            IsSplitActive: false,
            IsMultiplePagesActive: false,
            IsSideToSideActive: true,
            UsesReadOnlySnapshot: !capabilities.SupportsEditableSideToSide,
            PagesAcross: 2,
            Layout: DocumentViewDepthLayoutPlanner.Build(mode),
            StatusText: capabilities.SupportsEditableSideToSide
                ? "Side to Side view active: editable two-page horizontal-flow view with pair navigation."
                : "Side to Side view active: read-only two-page horizontal-flow preview with pair navigation.",
            Limitation: capabilities.SupportsEditableSideToSide
                ? null
                : "Editing is disabled because this host does not provide an editable side-to-side surface."),
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
    }

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
