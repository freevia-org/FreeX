using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.VisualEvidence;

public interface IWholeWindowVisualEvidenceProbe
{
    void LoadPresentation(Presentation presentation);
    void SelectSlide(int slideIndex);
    void SelectShape(uint shapeId);
    void ClearSelection();
    void HideCommentsPane();
    void SelectRibbonTab(string tabId);
    void FocusNotes();
    void ShowBackstagePane(string paneId);
    void ShowCommentsPane();
    void SelectFirstComment();
    void ShowAccessibilityPane();
    void SelectFirstAccessibilityIssue();
    void ShowAltTextPane();
    void ShowReadingOrderPane();
    void ShowProofingPane();
    void SelectFirstProofingIssue();
    void ShowMediaCaptionPane();
    void ShowSmartArtTextPane();
    void EnsureAnimationPaneVisible();
    bool SetViewShowState(bool showGridlines, bool showGuides);
    void SetZoom(PresentationViewZoomState state);
    void RefreshWholeWindow();
    void NormalizeShell();
    WholeWindowVisualEvidenceBaselineState CaptureBaselineState();
    WholeWindowVisualEvidenceRichEditorPreparationState PrepareRichEditor(
        WholeWindowVisualEvidenceRichEditorPlan plan);
    WholeWindowVisualEvidenceProbeState CaptureSemanticState(
        WholeWindowVisualEvidenceScenario scenario);
    WholeWindowVisualEvidenceRichEditorProbeState CaptureRichEditorState();
}

public sealed record WholeWindowVisualEvidenceProbeState(
    string Host,
    int CurrentSlideIndex,
    string CurrentSlideTitle,
    IReadOnlyList<uint> SelectedShapeIds,
    string SelectedShapeKind,
    string ActiveRibbonTabId,
    IReadOnlyList<string> VisibleRibbonTabIds,
    IReadOnlyList<string> VisibleContextualTabIds,
    bool BackstageOpen,
    string? BackstagePaneLabel,
    string FocusedRole,
    string FocusedLabel,
    string StatusText,
    bool ShowGridlines,
    bool ShowGuides,
    string ZoomMode,
    int ZoomPercent,
    bool AppOwnedTitleBarVisible,
    int QuickAccessButtonCount,
    bool HasAppIcon,
    string WindowTitle,
    WholeWindowVisualEvidenceBounds TitleBarBounds,
    WholeWindowVisualEvidenceBounds RibbonBounds,
    WholeWindowVisualEvidenceBounds SlidePaneBounds,
    WholeWindowVisualEvidenceBounds CanvasBounds,
    WholeWindowVisualEvidenceBounds NotesPaneBounds,
    WholeWindowVisualEvidenceBounds StatusBarBounds,
    IReadOnlyList<string> VisibleAuxiliaryPanes);

public sealed record WholeWindowVisualEvidenceRichEditorProbeState(
    bool IsActive,
    string SelectedText,
    WholeWindowVisualEvidenceBounds Bounds);

public sealed class WholeWindowVisualEvidenceHostCoordinator(IWholeWindowVisualEvidenceProbe probe)
{
    private bool _viewStateActivated;
    private WholeWindowVisualEvidencePreparationPlan? _preparation;

    public IReadOnlyList<DialogPaneVisualEvidenceAssertion> Prepare(
        WholeWindowVisualEvidenceScenario scenario,
        DialogPaneVisualEvidenceFixture fixture)
    {
        var plan = WholeWindowVisualEvidencePreparationSession.Prepare(scenario, fixture);
        _preparation = plan;
        if (plan.LoadFixturePresentation)
            probe.LoadPresentation(fixture.Presentation);
        probe.SelectSlide(plan.SlideIndex);
        if (plan.SelectionShapeId == 0)
            probe.ClearSelection();
        else
            probe.SelectShape(plan.SelectionShapeId);

        probe.HideCommentsPane();
        probe.SelectRibbonTab(plan.ActiveRibbonTabId);
        _viewStateActivated = !plan.Activation.IsViewState;
        Activate(plan.Activation);
        probe.RefreshWholeWindow();
        Normalize(scenario);

        var assertions = plan.CreateBaselineAssertions(probe.CaptureBaselineState()).ToList();
        if (plan.RichEditor is { } richEditor)
            assertions.AddRange(plan.CreateRichEditorAssertions(probe.PrepareRichEditor(richEditor)));
        return assertions;
    }

    public void Normalize(WholeWindowVisualEvidenceScenario scenario)
    {
        var activation = WholeWindowVisualEvidencePreparationSession.ResolveActivation(scenario);
        if (activation.IsViewState)
            _viewStateActivated = PrepareViewState(activation.Kind);
        probe.NormalizeShell();
    }

    public WholeWindowVisualEvidenceSemanticState CaptureSemantic(
        WholeWindowVisualEvidenceScenario scenario,
        IReadOnlyList<DialogPaneVisualEvidenceAssertion> preparationAssertions)
    {
        var preparation = _preparation ??
            throw new InvalidOperationException("Whole-window evidence must be prepared before semantic capture.");
        var state = probe.CaptureSemanticState(scenario);
        var assertions = preparationAssertions.ToList();
        assertions.AddRange(preparation.CreateActivationAssertions(new(
            _viewStateActivated,
            state.ActiveRibbonTabId,
            state.VisibleContextualTabIds,
            state.BackstageOpen && StringComparer.OrdinalIgnoreCase.Equals(
                state.BackstagePaneLabel,
                preparation.Activation.Id),
            state.BackstagePaneLabel)));

        var semantic = new WholeWindowVisualEvidenceSemanticState(
            scenario.Id,
            state.Host,
            scenario.ActivationId,
            state.CurrentSlideIndex,
            state.CurrentSlideTitle,
            state.SelectedShapeIds.ToArray(),
            state.SelectedShapeKind,
            state.ActiveRibbonTabId,
            state.VisibleRibbonTabIds,
            state.VisibleContextualTabIds,
            state.BackstageOpen,
            state.BackstagePaneLabel ?? string.Empty,
            state.FocusedRole,
            state.FocusedLabel,
            state.StatusText,
            state.ShowGridlines,
            state.ShowGuides,
            state.ZoomMode,
            state.ZoomPercent,
            state.AppOwnedTitleBarVisible,
            state.QuickAccessButtonCount,
            state.HasAppIcon ? "shared-shell:FreeP" : "missing",
            state.WindowTitle,
            0,
            false,
            state.TitleBarBounds,
            state.RibbonBounds,
            state.SlidePaneBounds,
            state.CanvasBounds,
            state.NotesPaneBounds,
            state.StatusBarBounds,
            state.VisibleAuxiliaryPanes,
            assertions);
        if (scenario.Kind != WholeWindowVisualEvidenceScenarioKind.RichEditorOverlay ||
            !StringComparer.Ordinal.Equals(scenario.ActivationId, "selection"))
            return semantic;

        var richEditor = probe.CaptureRichEditorState();
        return semantic with
        {
            RichEditor = new WholeWindowVisualEvidenceRichEditorState(
                richEditor.IsActive,
                DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectionStart,
                DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectionEnd,
                richEditor.SelectedText,
                richEditor.Bounds),
        };
    }

    private void Activate(WholeWindowVisualEvidenceActivation activation)
    {
        switch (activation.Kind)
        {
            case WholeWindowVisualEvidenceActivationKind.None:
                break;
            case WholeWindowVisualEvidenceActivationKind.FocusNotesPane:
                probe.FocusNotes();
                break;
            case WholeWindowVisualEvidenceActivationKind.BackstagePane:
                probe.ShowBackstagePane(activation.Id);
                break;
            case WholeWindowVisualEvidenceActivationKind.ReviewCommentsPane:
                probe.ShowCommentsPane();
                probe.SelectFirstComment();
                break;
            case WholeWindowVisualEvidenceActivationKind.AccessibilityCheckerPane:
                probe.ShowAccessibilityPane();
                probe.SelectFirstAccessibilityIssue();
                break;
            case WholeWindowVisualEvidenceActivationKind.AltTextPane:
                probe.ShowAltTextPane();
                break;
            case WholeWindowVisualEvidenceActivationKind.ReadingOrderPane:
                probe.ShowReadingOrderPane();
                break;
            case WholeWindowVisualEvidenceActivationKind.ProofingPane:
                probe.ShowProofingPane();
                probe.SelectFirstProofingIssue();
                break;
            case WholeWindowVisualEvidenceActivationKind.MediaCaptionPane:
                probe.ShowMediaCaptionPane();
                break;
            case WholeWindowVisualEvidenceActivationKind.SmartArtTextPane:
                probe.ShowSmartArtTextPane();
                break;
            case WholeWindowVisualEvidenceActivationKind.AnimationPane:
                probe.EnsureAnimationPaneVisible();
                break;
        }
    }

    private bool PrepareViewState(WholeWindowVisualEvidenceActivationKind activation) => activation switch
    {
        WholeWindowVisualEvidenceActivationKind.ViewGridlinesAndGuides => probe.SetViewShowState(true, true),
        WholeWindowVisualEvidenceActivationKind.ViewCleanCanvas => probe.SetViewShowState(false, false),
        WholeWindowVisualEvidenceActivationKind.ViewZoomFit => SetZoom(PresentationViewZoomState.FitToWindow),
        WholeWindowVisualEvidenceActivationKind.ViewZoom200 =>
            SetZoom(new PresentationViewZoomState(PresentationViewZoomMode.Percent, 200)),
        _ => false,
    };

    private bool SetZoom(PresentationViewZoomState state)
    {
        probe.SetZoom(state);
        return true;
    }
}
