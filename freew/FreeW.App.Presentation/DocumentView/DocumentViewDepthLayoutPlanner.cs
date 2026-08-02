using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.DocumentView;

public enum DocumentViewDepthPageFlow
{
    LiveDocument,
    SplitVerticalSnapshot,
    MultiplePagesGrid,
    SideToSideHorizontal
}

public enum DocumentViewDepthZoomIntent
{
    PreserveCurrentZoom,
    FitSinglePage,
    FitPagesAcross,
    FitSplitPane
}

public sealed record DocumentViewDepthLayoutPlan(
    DocumentViewDepthPageFlow PageFlow,
    bool UsesLiveEditor,
    bool AllowsPrimaryEditing,
    bool UsesReadOnlySnapshot,
    bool RequiresPrintLayoutSnapshot,
    int PagesAcross,
    int PageRows,
    int PreferredVisiblePageCount,
    double InterPageGapDip,
    DocumentViewDepthZoomIntent ZoomIntent,
    bool UsesHorizontalPageFlow)
{
    public bool IsMultiPageArrangement => PagesAcross > 1 || PreferredVisiblePageCount > 1;
}

public sealed record DocumentViewDepthViewportPlan(
    DocumentViewDepthLayoutPlan Layout,
    double Scale,
    double ViewportWidthDip,
    double ViewportHeightDip,
    double PageWidthDip,
    double PageHeightDip,
    double RequiredPageSpanWidthDip,
    double RequiredPageSpanHeightDip);

public static class DocumentViewDepthLayoutPlanner
{
    public const double DefaultInterPageGapDip = 24.0;

    public static DocumentViewDepthLayoutPlan Build(FreeWViewDepthMode mode) => mode switch
    {
        FreeWViewDepthMode.SplitPreview => new DocumentViewDepthLayoutPlan(
            DocumentViewDepthPageFlow.SplitVerticalSnapshot,
            UsesLiveEditor: true,
            AllowsPrimaryEditing: true,
            UsesReadOnlySnapshot: true,
            RequiresPrintLayoutSnapshot: true,
            PagesAcross: 1,
            PageRows: 1,
            PreferredVisiblePageCount: 1,
            InterPageGapDip: DefaultInterPageGapDip,
            ZoomIntent: DocumentViewDepthZoomIntent.FitSplitPane,
            UsesHorizontalPageFlow: false),
        FreeWViewDepthMode.MultiplePagesPreview => new DocumentViewDepthLayoutPlan(
            DocumentViewDepthPageFlow.MultiplePagesGrid,
            UsesLiveEditor: false,
            AllowsPrimaryEditing: false,
            UsesReadOnlySnapshot: true,
            RequiresPrintLayoutSnapshot: true,
            PagesAcross: 2,
            PageRows: 2,
            PreferredVisiblePageCount: 4,
            InterPageGapDip: DefaultInterPageGapDip,
            ZoomIntent: DocumentViewDepthZoomIntent.FitPagesAcross,
            UsesHorizontalPageFlow: false),
        FreeWViewDepthMode.SideToSidePreview => new DocumentViewDepthLayoutPlan(
            DocumentViewDepthPageFlow.SideToSideHorizontal,
            UsesLiveEditor: true,
            AllowsPrimaryEditing: true,
            UsesReadOnlySnapshot: false,
            RequiresPrintLayoutSnapshot: true,
            PagesAcross: 2,
            PageRows: 1,
            PreferredVisiblePageCount: 2,
            InterPageGapDip: DefaultInterPageGapDip,
            ZoomIntent: DocumentViewDepthZoomIntent.FitPagesAcross,
            UsesHorizontalPageFlow: true),
        _ => new DocumentViewDepthLayoutPlan(
            DocumentViewDepthPageFlow.LiveDocument,
            UsesLiveEditor: true,
            AllowsPrimaryEditing: true,
            UsesReadOnlySnapshot: false,
            RequiresPrintLayoutSnapshot: false,
            PagesAcross: 1,
            PageRows: 1,
            PreferredVisiblePageCount: 1,
            InterPageGapDip: DefaultInterPageGapDip,
            ZoomIntent: DocumentViewDepthZoomIntent.PreserveCurrentZoom,
            UsesHorizontalPageFlow: false)
    };

    public static DocumentViewDepthViewportPlan BuildViewportPlan(
        DocumentViewDepthLayoutPlan layout,
        double viewportWidthDip,
        double viewportHeightDip,
        double pageWidthDip,
        double pageHeightDip)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var safeViewportWidth = SafePositive(viewportWidthDip);
        var safeViewportHeight = SafePositive(viewportHeightDip);
        var safePageWidth = SafePositive(pageWidthDip);
        var safePageHeight = SafePositive(pageHeightDip);
        var pagesAcross = Math.Max(1, layout.PagesAcross);
        var pageRows = Math.Max(1, layout.PageRows);
        var horizontalGaps = layout.InterPageGapDip * Math.Max(0, pagesAcross - 1);
        var verticalGaps = layout.InterPageGapDip * Math.Max(0, pageRows - 1);
        var requiredWidth = safePageWidth * pagesAcross + horizontalGaps;
        var requiredHeight = safePageHeight * pageRows + verticalGaps;
        var (horizontalChrome, verticalChrome) = ChromeFor(layout);

        var fitWidth = (safeViewportWidth - horizontalChrome) / requiredWidth;
        var fitHeight = (safeViewportHeight - verticalChrome) / requiredHeight;
        var fit = Math.Min(fitWidth, fitHeight);
        var scale = ClampScale(fit);

        return new DocumentViewDepthViewportPlan(
            layout,
            scale,
            safeViewportWidth,
            safeViewportHeight,
            safePageWidth,
            safePageHeight,
            requiredWidth,
            requiredHeight);
    }

    public static double BuildPreviewScale(
        DocumentViewDepthLayoutPlan layout,
        double viewportWidthDip,
        double viewportHeightDip,
        double pageWidthDip,
        double pageHeightDip) =>
        BuildViewportPlan(layout, viewportWidthDip, viewportHeightDip, pageWidthDip, pageHeightDip).Scale;

    public static double BuildDocumentViewerZoomPercent(
        DocumentViewDepthLayoutPlan layout,
        double pageWidthZoomFactor)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var factor = double.IsFinite(pageWidthZoomFactor) && pageWidthZoomFactor > 0
            ? pageWidthZoomFactor
            : 1.0;
        var pagesAcross = Math.Max(1, layout.PagesAcross);
        var zoomPercent = factor * 100.0 / pagesAcross;
        return Math.Clamp(Math.Round(zoomPercent, 2), 10.0, 500.0);
    }

    private static (double HorizontalDip, double VerticalDip) ChromeFor(DocumentViewDepthLayoutPlan layout) =>
        layout.PageFlow == DocumentViewDepthPageFlow.SplitVerticalSnapshot
            ? (48.0, 32.0)
            : (96.0, 72.0);

    private static double SafePositive(double value) =>
        double.IsFinite(value) && value > 0 ? value : 1.0;

    private static double ClampScale(double value)
    {
        if (!double.IsFinite(value) || value <= 0)
            return 1.0;

        return Math.Clamp(Math.Round(value, 2), 0.25, 1.25);
    }
}
