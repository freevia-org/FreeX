namespace FreeP.App.Compositor;

public enum WholeWindowVisualEvidenceScenarioKind
{
    Startup,
    StaticRibbonTab,
    ContextualSelection,
    BackstagePane,
    StatusBar,
    ViewState,
    WorkspaceRegion,
    AuxiliaryPane,
}

public sealed record WholeWindowVisualEvidenceScenario(
    string Id,
    WholeWindowVisualEvidenceScenarioKind Kind,
    string ActivationId,
    string ExpectedActiveRibbonTabId = "",
    string ExpectedContextualTabId = "",
    string SelectionRouteId = "",
    int SlideIndex = 0);

public static class WholeWindowVisualEvidenceCatalog
{
    public const int LogicalClientWidth = 1280;
    public const int LogicalClientHeight = 760;
    public const double TargetDpi = 96d;

    public static IReadOnlyList<WholeWindowVisualEvidenceScenario> All { get; } =
    [
        Scenario("startup.slide", WholeWindowVisualEvidenceScenarioKind.Startup, "slide", selectionRouteId: "shape"),
        Scenario("startup.notes", WholeWindowVisualEvidenceScenarioKind.Startup, "notes", selectionRouteId: "none", slideIndex: 1),

        Ribbon("home"),
        Ribbon("insert"),
        Ribbon("design"),
        Ribbon("transitions"),
        Ribbon("animations"),
        Ribbon("view"),

        Context("shape", "ShapeFormatTab", "shape"),
        Context("chart", "ChartDesignTab", "chart"),
        Context("media", "MediaFormatTab", "media"),
        Context("smartart", "SmartArtDesignTab", "smartart"),

        Backstage("Info"),
        Backstage("Recent"),
        Backstage("New from template"),
        Backstage("Print"),
        Backstage("Export"),
        Backstage("Options"),
        Backstage("Account"),

        Scenario("status.slide-1", WholeWindowVisualEvidenceScenarioKind.StatusBar, "slide-1", selectionRouteId: "none"),
        Scenario("status.slide-2", WholeWindowVisualEvidenceScenarioKind.StatusBar, "slide-2", selectionRouteId: "none", slideIndex: 1),
        Scenario("view.gridlines-guides", WholeWindowVisualEvidenceScenarioKind.ViewState, "gridlines-guides", "view", selectionRouteId: "shape"),
        Scenario("view.clean-canvas", WholeWindowVisualEvidenceScenarioKind.ViewState, "clean-canvas", "view", selectionRouteId: "shape"),
        Scenario("view.zoom-fit", WholeWindowVisualEvidenceScenarioKind.ViewState, "zoom-fit", "view", selectionRouteId: "chart"),
        Scenario("view.zoom-200", WholeWindowVisualEvidenceScenarioKind.ViewState, "zoom-200", "view", selectionRouteId: "chart"),

        Scenario("workspace.slide-pane", WholeWindowVisualEvidenceScenarioKind.WorkspaceRegion, "slide-pane", "insert", selectionRouteId: "none", slideIndex: 1),
        Scenario("workspace.notes-pane", WholeWindowVisualEvidenceScenarioKind.WorkspaceRegion, "notes-pane", "view", selectionRouteId: "none", slideIndex: 1),
        Scenario("workspace.canvas", WholeWindowVisualEvidenceScenarioKind.WorkspaceRegion, "canvas", "design", selectionRouteId: "chart"),

        Auxiliary("review.comments-pane", "comments", "shape"),
        Auxiliary("review.accessibility-pane", "accessibility", "shape"),
        Auxiliary("review.alt-text-pane", "alt-text", "shape"),
        Auxiliary("review.reading-order-pane", "reading-order", "shape"),
        Auxiliary("review.proofing-pane", "proofing", "shape"),
        Auxiliary("accessibility.media-caption-pane", "media-caption", "media"),
        Auxiliary("context.smartart-text-pane", "smartart-text", "smartart"),
        Auxiliary("animations.animation-pane", "animation", "shape", "animations"),
    ];

    public static WholeWindowVisualEvidenceScenario Get(string id) =>
        All.Single(scenario => StringComparer.Ordinal.Equals(scenario.Id, id));

    public static uint SelectionFor(
        WholeWindowVisualEvidenceScenario scenario,
        DialogPaneVisualEvidenceFixture fixture) => scenario.SelectionRouteId switch
        {
            "shape" => fixture.TextShapeId,
            "chart" => fixture.ChartShapeId,
            "media" => fixture.MediaShapeId,
            "smartart" => fixture.SmartArtShapeId,
            "none" or "" => 0,
            _ => throw new InvalidOperationException(
                $"Unknown whole-window selection route '{scenario.SelectionRouteId}' for {scenario.Id}."),
        };

    private static WholeWindowVisualEvidenceScenario Scenario(
        string id,
        WholeWindowVisualEvidenceScenarioKind kind,
        string activationId,
        string expectedActiveRibbonTabId = "home",
        string expectedContextualTabId = "",
        string selectionRouteId = "",
        int slideIndex = 0) =>
        new(id, kind, activationId, expectedActiveRibbonTabId, expectedContextualTabId, selectionRouteId, slideIndex);

    private static WholeWindowVisualEvidenceScenario Ribbon(string tabId) =>
        Scenario($"ribbon.{tabId}", WholeWindowVisualEvidenceScenarioKind.StaticRibbonTab, tabId, tabId);

    private static WholeWindowVisualEvidenceScenario Context(string name, string expectedTabId, string selectionRouteId) =>
        Scenario(
            $"contextual.{name}",
            WholeWindowVisualEvidenceScenarioKind.ContextualSelection,
            name,
            expectedContextualTabId: expectedTabId,
            selectionRouteId: selectionRouteId);

    private static WholeWindowVisualEvidenceScenario Backstage(string paneLabel) =>
        Scenario(
            $"backstage.{paneLabel.ToLowerInvariant().Replace(' ', '-')}",
            WholeWindowVisualEvidenceScenarioKind.BackstagePane,
            paneLabel,
            expectedActiveRibbonTabId: string.Empty,
            selectionRouteId: "shape");

    private static WholeWindowVisualEvidenceScenario Auxiliary(
        string id,
        string activationId,
        string selectionRouteId,
        string activeTabId = "home") =>
        Scenario(id, WholeWindowVisualEvidenceScenarioKind.AuxiliaryPane, activationId, activeTabId, selectionRouteId: selectionRouteId);
}

public sealed record WholeWindowVisualEvidenceBounds(double X, double Y, double Width, double Height)
{
    public bool IsVisible => Width > 0 && Height > 0;
}

public sealed record WholeWindowVisualEvidenceSemanticState(
    string ScenarioId,
    string Host,
    string ActivationId,
    int CurrentSlideIndex,
    string CurrentSlideTitle,
    IReadOnlyList<uint> SelectedShapeIds,
    string SelectedShapeKind,
    string ActiveRibbonTabId,
    IReadOnlyList<string> VisibleRibbonTabIds,
    IReadOnlyList<string> VisibleContextualTabIds,
    bool BackstageOpen,
    string BackstagePane,
    string FocusedRole,
    string FocusedLabel,
    string StatusText,
    bool ShowGridlines,
    bool ShowGuides,
    string ZoomMode,
    int ZoomPercent,
    bool AppOwnedTitleBarVisible,
    int QuickAccessButtonCount,
    string AppIconIdentity,
    string WindowTitle,
    int StatusViewModeControlCount,
    bool StatusZoomControlVisible,
    WholeWindowVisualEvidenceBounds TitleBarBounds,
    WholeWindowVisualEvidenceBounds RibbonBounds,
    WholeWindowVisualEvidenceBounds SlidePaneBounds,
    WholeWindowVisualEvidenceBounds CanvasBounds,
    WholeWindowVisualEvidenceBounds NotesPaneBounds,
    WholeWindowVisualEvidenceBounds StatusBarBounds,
    IReadOnlyList<string> VisibleAuxiliaryPanes,
    IReadOnlyList<DialogPaneVisualEvidenceAssertion> Assertions);

public sealed record WholeWindowVisualEvidenceCapture(
    string ScenarioId,
    string Host,
    string CaptureStatus,
    string FullImagePath,
    string ClientImagePath,
    double LogicalWidth,
    double LogicalHeight,
    int PixelWidth,
    int PixelHeight,
    double DpiX,
    double DpiY,
    double SourceDpiX,
    double SourceDpiY,
    long NonBackgroundPixelCount,
    string FullImageSha256,
    string ClientImageSha256,
    WholeWindowVisualEvidenceSemanticState SemanticState,
    IReadOnlyList<string> Limitations);

public sealed record WholeWindowVisualEvidenceHostManifest(
    int SchemaVersion,
    string Host,
    string CaptureMode,
    double TargetDpi,
    int LogicalClientWidth,
    int LogicalClientHeight,
    string GeneratedAtUtc,
    IReadOnlyList<WholeWindowVisualEvidenceCapture> Captures,
    IReadOnlyList<string> Limitations);
