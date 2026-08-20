using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.VisualEvidence;

public interface IWholeWindowVisualEvidenceNativeInspector
{
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

public sealed class WholeWindowVisualEvidenceHostCoordinator(
    IVisualEvidenceAppHost host,
    IWholeWindowVisualEvidenceNativeInspector inspector)
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
            host.LoadPresentation(fixture.Presentation);
        host.SelectSlide(plan.SlideIndex);
        if (plan.SelectionShapeId == 0)
            host.ClearSelection();
        else
            host.SelectShape(plan.SelectionShapeId);

        host.HideCommentsPane();
        host.ResetAuxiliaryPanes();
        if (plan.Activation.Kind != WholeWindowVisualEvidenceActivationKind.BackstagePane)
            host.HideBackstage();
        RestoreBaselineViewState();
        host.SelectRibbonTab(plan.ActiveRibbonTabId);
        var assertions = plan.CreateBaselineAssertions(inspector.CaptureBaselineState()).ToList();
        _viewStateActivated = !plan.Activation.IsViewState;
        Activate(plan.Activation);
        host.RefreshWholeWindow();
        Normalize(scenario);

        if (plan.RichEditor is { } richEditor)
            assertions.AddRange(plan.CreateRichEditorAssertions(inspector.PrepareRichEditor(richEditor)));
        return assertions;
    }

    public void Normalize(WholeWindowVisualEvidenceScenario scenario)
    {
        if (_preparation is { } preparation && host.CurrentSlideIndex != preparation.SlideIndex)
        {
            host.SelectSlide(preparation.SlideIndex);
            if (preparation.SelectionShapeId != 0)
                host.SelectShape(preparation.SelectionShapeId);
            host.RefreshWholeWindow();
        }

        var activation = WholeWindowVisualEvidencePreparationSession.ResolveActivation(scenario);
        if (activation.IsViewState)
            _viewStateActivated = PrepareViewState(activation.Kind);
        host.NormalizeShell();
    }

    public WholeWindowVisualEvidenceSemanticState CaptureSemantic(
        WholeWindowVisualEvidenceScenario scenario,
        IReadOnlyList<DialogPaneVisualEvidenceAssertion> preparationAssertions)
    {
        var preparation = _preparation ??
            throw new InvalidOperationException("Whole-window evidence must be prepared before semantic capture.");
        var state = inspector.CaptureSemanticState(scenario);
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

        var richEditor = inspector.CaptureRichEditorState();
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
                host.FocusNotes();
                break;
            case WholeWindowVisualEvidenceActivationKind.BackstagePane:
                host.ShowBackstagePane(activation.Id);
                break;
            case WholeWindowVisualEvidenceActivationKind.ReviewCommentsPane:
                host.ShowCommentsPane();
                host.SelectFirstComment();
                break;
            case WholeWindowVisualEvidenceActivationKind.AccessibilityCheckerPane:
                host.ShowAccessibilityPane();
                host.SelectFirstAccessibilityIssue();
                break;
            case WholeWindowVisualEvidenceActivationKind.AltTextPane:
                host.ShowAltTextPane();
                break;
            case WholeWindowVisualEvidenceActivationKind.ReadingOrderPane:
                host.ShowReadingOrderPane();
                break;
            case WholeWindowVisualEvidenceActivationKind.ProofingPane:
                host.ShowProofingPane();
                host.SelectFirstProofingIssue();
                break;
            case WholeWindowVisualEvidenceActivationKind.MediaCaptionPane:
                host.ShowMediaCaptionPane();
                break;
            case WholeWindowVisualEvidenceActivationKind.SmartArtTextPane:
                host.ShowSmartArtTextPane();
                break;
            case WholeWindowVisualEvidenceActivationKind.AnimationPane:
                host.EnsureAnimationPaneVisible();
                break;
        }
    }

    private bool PrepareViewState(WholeWindowVisualEvidenceActivationKind activation) => activation switch
    {
        WholeWindowVisualEvidenceActivationKind.ViewGridlinesAndGuides => host.SetViewShowState(true, true),
        WholeWindowVisualEvidenceActivationKind.ViewCleanCanvas => host.SetViewShowState(false, false),
        WholeWindowVisualEvidenceActivationKind.ViewZoomFit => SetZoom(PresentationViewZoomState.FitToWindow),
        WholeWindowVisualEvidenceActivationKind.ViewZoom200 =>
            SetZoom(new PresentationViewZoomState(PresentationViewZoomMode.Percent, 200)),
        _ => false,
    };

    private void RestoreBaselineViewState()
    {
        host.SetViewShowState(showGridlines: true, showGuides: true);
        host.SetZoom(PresentationViewZoomState.FitToWindow);
    }

    private bool SetZoom(PresentationViewZoomState state)
    {
        host.SetZoom(state);
        return true;
    }
}
