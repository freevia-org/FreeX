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
            StatusText: "Split view active: live editor above, read-only paginated snapshot below.",
            Limitation: "The Avalonia split preview is read-only in the secondary pane; dual live editing remains deferred."),
        FreeWViewDepthMode.MultiplePagesPreview => new FreeWViewDepthPlan(
            mode,
            FreeWViewDepthSurfaceKind.ReadOnlyPagePreview,
            IsSplitActive: false,
            IsMultiplePagesActive: true,
            IsSideToSideActive: false,
            UsesReadOnlySnapshot: true,
            PagesAcross: 1,
            StatusText: "Multiple Pages view active: read-only paginated preview.",
            Limitation: "Editing is disabled while the Avalonia Multiple Pages preview is active."),
        FreeWViewDepthMode.SideToSidePreview => new FreeWViewDepthPlan(
            mode,
            FreeWViewDepthSurfaceKind.ReadOnlyPagePreview,
            IsSplitActive: false,
            IsMultiplePagesActive: false,
            IsSideToSideActive: true,
            UsesReadOnlySnapshot: true,
            PagesAcross: 2,
            StatusText: "Side to Side view active: read-only two-page-fit preview.",
            Limitation: "The Avalonia surface uses the existing vertical paginated renderer at two-page fit; horizontal page turning remains deferred."),
        _ => new FreeWViewDepthPlan(
            FreeWViewDepthMode.LiveEditor,
            FreeWViewDepthSurfaceKind.LiveEditor,
            IsSplitActive: false,
            IsMultiplePagesActive: false,
            IsSideToSideActive: false,
            UsesReadOnlySnapshot: false,
            PagesAcross: 1,
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

        var pagesAcross = mode == FreeWViewDepthMode.SideToSidePreview ? 2 : 1;
        var interPageGap = pagesAcross > 1 ? 24 * (pagesAcross - 1) : 0;
        var horizontalChrome = mode == FreeWViewDepthMode.SplitPreview ? 48 : 96;
        var verticalChrome = mode == FreeWViewDepthMode.SplitPreview ? 32 : 72;

        var fitWidth = (viewportWidthDip - horizontalChrome) / (pageWidthDip * pagesAcross + interPageGap);
        var fitHeight = (viewportHeightDip - verticalChrome) / pageHeightDip;
        var fit = Math.Min(fitWidth, fitHeight);

        if (!double.IsFinite(fit) || fit <= 0)
            return 1.0;

        return Math.Clamp(Math.Round(fit, 2), 0.25, 1.25);
    }
}
