namespace FreeP.App.Compositor;

public enum WholeWindowVisualEvidenceScenarioKind
{
    Startup,
    StaticRibbonTab,
    BackstagePane,
    StatusBar,
    ViewState,
    WorkspaceRegion,
    AuxiliaryPane,
    RichEditorOverlay,
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
    public static IReadOnlyList<int> ResponsiveChromeWidths { get; } = [1280, 1100, 900, 750];

    public static IReadOnlyList<WholeWindowVisualEvidenceScenario> All { get; } =
    [
        Scenario("startup.slide", WholeWindowVisualEvidenceScenarioKind.Startup, "slide", selectionRouteId: "none"),
        Scenario("startup.notes", WholeWindowVisualEvidenceScenarioKind.Startup, "notes", selectionRouteId: "none", slideIndex: 1),

        Ribbon("home"),
        Ribbon("insert"),
        Ribbon("design"),
        Ribbon("transitions"),
        Ribbon("animations"),
        Ribbon("view"),

        Backstage("Info"),
        Backstage("Recent"),
        Backstage("New from template"),
        Backstage("Print"),
        Backstage("Export"),
        Backstage("Options"),
        Backstage("Account"),

        Scenario("status.slide-2", WholeWindowVisualEvidenceScenarioKind.StatusBar, "slide-2", selectionRouteId: "none", slideIndex: 1),
        Scenario("view.gridlines-guides", WholeWindowVisualEvidenceScenarioKind.ViewState, "gridlines-guides", "view", selectionRouteId: "shape"),
        Scenario("view.clean-canvas", WholeWindowVisualEvidenceScenarioKind.ViewState, "clean-canvas", "view", selectionRouteId: "shape"),
        Scenario("view.zoom-fit", WholeWindowVisualEvidenceScenarioKind.ViewState, "zoom-fit", "view", selectionRouteId: "chart"),
        Scenario("view.zoom-200", WholeWindowVisualEvidenceScenarioKind.ViewState, "zoom-200", "view", selectionRouteId: "chart"),

        Scenario("workspace.slide-pane", WholeWindowVisualEvidenceScenarioKind.WorkspaceRegion, "slide-pane", "insert", selectionRouteId: "none", slideIndex: 1),
        Scenario("workspace.notes-pane", WholeWindowVisualEvidenceScenarioKind.WorkspaceRegion, "notes-pane", "view", selectionRouteId: "none", slideIndex: 1),
        Scenario("workspace.canvas", WholeWindowVisualEvidenceScenarioKind.WorkspaceRegion, "canvas", "design", selectionRouteId: "chart"),

        Scenario("editor.rich-text-selection", WholeWindowVisualEvidenceScenarioKind.RichEditorOverlay, "selection", selectionRouteId: "shape"),
        Scenario("editor.rich-text-caret", WholeWindowVisualEvidenceScenarioKind.RichEditorOverlay, "caret", selectionRouteId: "shape"),

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

public enum WholeWindowVisualEvidenceActivationKind
{
    None,
    FocusNotesPane,
    BackstagePane,
    ReviewCommentsPane,
    AccessibilityCheckerPane,
    AltTextPane,
    ReadingOrderPane,
    ProofingPane,
    MediaCaptionPane,
    SmartArtTextPane,
    AnimationPane,
    ViewGridlinesAndGuides,
    ViewCleanCanvas,
    ViewZoomFit,
    ViewZoom200,
}

public sealed record WholeWindowVisualEvidenceActivation(
    WholeWindowVisualEvidenceActivationKind Kind,
    string Id = "")
{
    public bool IsViewState => Kind is
        WholeWindowVisualEvidenceActivationKind.ViewGridlinesAndGuides or
        WholeWindowVisualEvidenceActivationKind.ViewCleanCanvas or
        WholeWindowVisualEvidenceActivationKind.ViewZoomFit or
        WholeWindowVisualEvidenceActivationKind.ViewZoom200;

    public bool IsAuxiliaryPane => Kind is
        WholeWindowVisualEvidenceActivationKind.ReviewCommentsPane or
        WholeWindowVisualEvidenceActivationKind.AccessibilityCheckerPane or
        WholeWindowVisualEvidenceActivationKind.AltTextPane or
        WholeWindowVisualEvidenceActivationKind.ReadingOrderPane or
        WholeWindowVisualEvidenceActivationKind.ProofingPane or
        WholeWindowVisualEvidenceActivationKind.MediaCaptionPane or
        WholeWindowVisualEvidenceActivationKind.SmartArtTextPane or
        WholeWindowVisualEvidenceActivationKind.AnimationPane;
}

public sealed record WholeWindowVisualEvidenceRichEditorPlan(
    uint ShapeId,
    int SelectionStart,
    int SelectionEnd,
    string ExpectedSelectedText,
    int ExpectedRunCount);

public sealed record WholeWindowVisualEvidenceBaselineState(
    int SlideCount,
    int CurrentSlideIndex,
    IReadOnlyList<uint> SelectedShapeIds);

public sealed record WholeWindowVisualEvidenceRichEditorPreparationState(
    bool IsActive,
    uint ActiveShapeId,
    bool SelectionSet,
    string SelectedText,
    bool IsFocused,
    int RunCount,
    string FocusDetail);

public sealed record WholeWindowVisualEvidenceActivationState(
    bool ViewStateActivated,
    string ActiveRibbonTabId,
    IReadOnlyList<string> VisibleContextualTabIds,
    bool BackstageActivated,
    string? BackstagePaneLabel);

public sealed record WholeWindowVisualEvidencePreparationPlan(
    WholeWindowVisualEvidenceScenario Scenario,
    bool LoadFixturePresentation,
    int ExpectedSlideCount,
    int SlideIndex,
    uint SelectionShapeId,
    string ActiveRibbonTabId,
    WholeWindowVisualEvidenceActivation Activation,
    WholeWindowVisualEvidenceRichEditorPlan? RichEditor)
{
    public IReadOnlyList<DialogPaneVisualEvidenceAssertion> CreateBaselineAssertions(
        WholeWindowVisualEvidenceBaselineState state)
    {
        var selectionPrepared = SelectionShapeId == 0
            ? state.SelectedShapeIds.Count == 0
            : state.SelectedShapeIds.SequenceEqual([SelectionShapeId]);

        return
        [
            new(
                "fixture-loaded",
                state.SlideCount == ExpectedSlideCount,
                LoadFixturePresentation
                    ? $"Loaded {state.SlideCount} seeded slides."
                    : $"Captured the clean startup document with {state.SlideCount} slide."),
            new(
                "slide-activated",
                state.CurrentSlideIndex == SlideIndex,
                $"Activated slide index {state.CurrentSlideIndex}; expected {SlideIndex}."),
            new(
                "selection-activated",
                selectionPrepared,
                $"Selected shape ids: {string.Join(",", state.SelectedShapeIds)}."),
        ];
    }

    public IReadOnlyList<DialogPaneVisualEvidenceAssertion> CreateRichEditorAssertions(
        WholeWindowVisualEvidenceRichEditorPreparationState state)
    {
        if (RichEditor is not { } richEditor)
            return [];

        return
        [
            new(
                "rich-editor-activated",
                state.IsActive && state.ActiveShapeId == richEditor.ShapeId,
                $"Active rich-editor shape id is {state.ActiveShapeId}; expected {richEditor.ShapeId}."),
            new(
                "rich-editor-selection",
                state.SelectionSet && StringComparer.Ordinal.Equals(state.SelectedText, richEditor.ExpectedSelectedText),
                $"Selected '{state.SelectedText}' at logical range {richEditor.SelectionStart}..{richEditor.SelectionEnd}."),
            new(
                "rich-editor-focus",
                state.IsFocused,
                state.FocusDetail),
            new(
                "rich-editor-mixed-runs",
                state.RunCount == richEditor.ExpectedRunCount,
                $"The production overlay contains {state.RunCount} model runs; expected {richEditor.ExpectedRunCount} mixed-format runs."),
        ];
    }

    public IReadOnlyList<DialogPaneVisualEvidenceAssertion> CreateActivationAssertions(
        WholeWindowVisualEvidenceActivationState state)
    {
        var assertions = new List<DialogPaneVisualEvidenceAssertion>();
        if (Activation.IsViewState)
        {
            assertions.Add(new(
                "view-state-activated-via-command",
                state.ViewStateActivated,
                $"Activated view state '{Activation.Id}' through the runtime ribbon command path."));
        }

        if (!string.IsNullOrWhiteSpace(Scenario.ExpectedActiveRibbonTabId) &&
            Activation.Kind != WholeWindowVisualEvidenceActivationKind.BackstagePane)
        {
            assertions.Add(new(
                "active-ribbon-tab",
                StringComparer.Ordinal.Equals(state.ActiveRibbonTabId, Scenario.ExpectedActiveRibbonTabId),
                $"Active ribbon tab is '{state.ActiveRibbonTabId}'; expected '{Scenario.ExpectedActiveRibbonTabId}'."));
        }

        if (!string.IsNullOrWhiteSpace(Scenario.ExpectedContextualTabId))
        {
            assertions.Add(new(
                "contextual-tab-visible",
                state.VisibleContextualTabIds.Contains(Scenario.ExpectedContextualTabId, StringComparer.Ordinal),
                state.VisibleContextualTabIds.Count == 0
                    ? $"Expected contextual tab '{Scenario.ExpectedContextualTabId}', but FreeP declares no contextual ribbon tabs."
                    : $"Visible contextual tabs: {string.Join(", ", state.VisibleContextualTabIds)}."));
        }

        if (Activation.Kind == WholeWindowVisualEvidenceActivationKind.BackstagePane)
        {
            assertions.Add(new(
                "backstage-pane-activated",
                state.BackstageActivated,
                $"Backstage pane is '{state.BackstagePaneLabel ?? "unavailable"}'; expected '{Activation.Id}'."));
        }

        return assertions;
    }
}

public static class WholeWindowVisualEvidencePreparationSession
{
    public const string DefaultRibbonTabId = "home";

    public static WholeWindowVisualEvidencePreparationPlan Prepare(
        WholeWindowVisualEvidenceScenario scenario,
        DialogPaneVisualEvidenceFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(fixture);

        var cleanStartupState = scenario.Kind == WholeWindowVisualEvidenceScenarioKind.Startup &&
            StringComparer.Ordinal.Equals(scenario.ActivationId, "slide");
        var richEditor = PrepareRichEditorFixture(scenario, fixture);
        return new(
            scenario,
            LoadFixturePresentation: !cleanStartupState,
            ExpectedSlideCount: cleanStartupState ? 1 : fixture.Presentation.Slides.Count,
            scenario.SlideIndex,
            WholeWindowVisualEvidenceCatalog.SelectionFor(scenario, fixture),
            string.IsNullOrWhiteSpace(scenario.ExpectedActiveRibbonTabId)
                ? DefaultRibbonTabId
                : scenario.ExpectedActiveRibbonTabId,
            ResolveActivation(scenario),
            richEditor);
    }

    public static WholeWindowVisualEvidenceActivation ResolveActivation(
        WholeWindowVisualEvidenceScenario scenario) => scenario.Kind switch
        {
            WholeWindowVisualEvidenceScenarioKind.Startup
                when StringComparer.Ordinal.Equals(scenario.ActivationId, "notes") =>
                new(WholeWindowVisualEvidenceActivationKind.FocusNotesPane, scenario.ActivationId),
            WholeWindowVisualEvidenceScenarioKind.WorkspaceRegion
                when StringComparer.Ordinal.Equals(scenario.ActivationId, "notes-pane") =>
                new(WholeWindowVisualEvidenceActivationKind.FocusNotesPane, scenario.ActivationId),
            WholeWindowVisualEvidenceScenarioKind.BackstagePane =>
                new(WholeWindowVisualEvidenceActivationKind.BackstagePane, scenario.ActivationId),
            WholeWindowVisualEvidenceScenarioKind.AuxiliaryPane => scenario.ActivationId switch
            {
                "comments" => new(WholeWindowVisualEvidenceActivationKind.ReviewCommentsPane, scenario.ActivationId),
                "accessibility" => new(WholeWindowVisualEvidenceActivationKind.AccessibilityCheckerPane, scenario.ActivationId),
                "alt-text" => new(WholeWindowVisualEvidenceActivationKind.AltTextPane, scenario.ActivationId),
                "reading-order" => new(WholeWindowVisualEvidenceActivationKind.ReadingOrderPane, scenario.ActivationId),
                "proofing" => new(WholeWindowVisualEvidenceActivationKind.ProofingPane, scenario.ActivationId),
                "media-caption" => new(WholeWindowVisualEvidenceActivationKind.MediaCaptionPane, scenario.ActivationId),
                "smartart-text" => new(WholeWindowVisualEvidenceActivationKind.SmartArtTextPane, scenario.ActivationId),
                "animation" => new(WholeWindowVisualEvidenceActivationKind.AnimationPane, scenario.ActivationId),
                _ => throw UnknownActivation(scenario),
            },
            WholeWindowVisualEvidenceScenarioKind.ViewState => scenario.ActivationId switch
            {
                "gridlines-guides" => new(WholeWindowVisualEvidenceActivationKind.ViewGridlinesAndGuides, scenario.ActivationId),
                "clean-canvas" => new(WholeWindowVisualEvidenceActivationKind.ViewCleanCanvas, scenario.ActivationId),
                "zoom-fit" => new(WholeWindowVisualEvidenceActivationKind.ViewZoomFit, scenario.ActivationId),
                "zoom-200" => new(WholeWindowVisualEvidenceActivationKind.ViewZoom200, scenario.ActivationId),
                _ => throw UnknownActivation(scenario),
            },
            _ => new(WholeWindowVisualEvidenceActivationKind.None),
        };

    private static InvalidOperationException UnknownActivation(WholeWindowVisualEvidenceScenario scenario) =>
        new($"Unknown whole-window activation '{scenario.ActivationId}' for {scenario.Id}.");

    private static WholeWindowVisualEvidenceRichEditorPlan? PrepareRichEditorFixture(
        WholeWindowVisualEvidenceScenario scenario,
        DialogPaneVisualEvidenceFixture fixture)
    {
        if (scenario.Kind != WholeWindowVisualEvidenceScenarioKind.RichEditorOverlay)
            return null;

        var body = DialogPaneVisualEvidenceFixtureFactory.CreateRichEditorBody();
        var shape = fixture.Presentation.Slides[scenario.SlideIndex].Shapes
            .Single(candidate => candidate.Id == fixture.TextShapeId);
        shape.TextBody = body;

        var selectsText = StringComparer.Ordinal.Equals(scenario.ActivationId, "selection");
        var start = selectsText
            ? DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectionStart
            : DialogPaneVisualEvidenceFixtureFactory.RichEditorCaretPosition;
        return new(
            fixture.TextShapeId,
            start,
            selectsText ? DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectionEnd : start,
            selectsText ? DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectedText : string.Empty,
            body.Paragraphs.SelectMany(paragraph => paragraph.Runs).Count());
    }
}

public sealed record WholeWindowVisualEvidenceBounds(double X, double Y, double Width, double Height)
{
    public bool IsVisible => Width > 0 && Height > 0;
}

public sealed record WholeWindowVisualEvidenceRichEditorState(
    bool Active,
    int SelectionStart,
    int SelectionEnd,
    string SelectedText,
    WholeWindowVisualEvidenceBounds Bounds)
{
    public static WholeWindowVisualEvidenceRichEditorState Empty { get; } = new(
        false,
        0,
        0,
        string.Empty,
        new WholeWindowVisualEvidenceBounds(0, 0, 0, 0));
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
    IReadOnlyList<DialogPaneVisualEvidenceAssertion> Assertions)
{
    public WholeWindowVisualEvidenceRichEditorState RichEditor { get; init; } =
        WholeWindowVisualEvidenceRichEditorState.Empty;
}

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
