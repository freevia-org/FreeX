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

public enum FreeWViewDepthSurfaceKind
{
    LiveEditor,
    SplitEditorWithReadOnlyPreview,
    ReadOnlyPagePreview
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
            FreeWViewDepthSurfaceKind.ReadOnlyPagePreview,
            IsSplitActive: false,
            IsMultiplePagesActive: false,
            IsSideToSideActive: true,
            UsesReadOnlySnapshot: true,
            PagesAcross: 2,
            Layout: DocumentViewDepthLayoutPlanner.Build(mode),
            StatusText: "Side to Side view active: read-only two-page horizontal-flow preview.",
            Limitation: "Animated horizontal page turning remains deferred; the shared state now carries Side-to-Side page flow."),
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
}
