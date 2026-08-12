using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FreeP.App.Avalonia;
using FreeP.App.Compositor;

namespace FreeP.VisualEvidence.Avalonia;

internal sealed class AvaloniaWholeWindowVisualEvidenceCoordinator(MainWindow.AvaloniaVisualCaptureAdapter access)
{
    private bool _viewStateActivated;
    private WholeWindowVisualEvidencePreparationPlan? _preparation;

    internal IReadOnlyList<DialogPaneVisualEvidenceAssertion> Prepare(
        WholeWindowVisualEvidenceScenario scenario,
        DialogPaneVisualEvidenceFixture fixture)
    {
        var plan = WholeWindowVisualEvidencePreparationSession.Prepare(scenario, fixture);
        _preparation = plan;
        if (plan.LoadFixturePresentation)
            access.LoadPresentation(fixture.Presentation);
        access.SelectSlide(plan.SlideIndex);
        if (plan.SelectionShapeId == 0)
            access.ClearSelection();
        else
            access.SelectShape(plan.SelectionShapeId);

        access.HideCommentsPane();
        access.SelectRibbonTab(plan.ActiveRibbonTabId);
        _viewStateActivated = !plan.Activation.IsViewState;
        switch (plan.Activation.Kind)
        {
            case WholeWindowVisualEvidenceActivationKind.FocusNotesPane:
                access.FocusNotes();
                break;
            case WholeWindowVisualEvidenceActivationKind.BackstagePane:
                access.ShowBackstagePane(plan.Activation.Id);
                break;
            default:
                if (plan.Activation.IsAuxiliaryPane)
                    ShowAuxiliaryPane(plan.Activation.Kind);
                break;
        }

        access.RefreshWholeWindow();
        Normalize(scenario);
        var assertions = plan.CreateBaselineAssertions(new(
            access.SlideCount,
            access.CurrentSlideIndex,
            access.SelectedShapeIds)).ToList();
        assertions.AddRange(PrepareRichEditor(plan));
        return assertions;
    }

    internal void Normalize(WholeWindowVisualEvidenceScenario scenario)
    {
        var activation = WholeWindowVisualEvidencePreparationSession.ResolveActivation(scenario);
        if (activation.IsViewState)
            _viewStateActivated = PrepareViewState(activation.Kind);
        access.NormalizeShell();
    }

    internal WholeWindowVisualEvidenceSemanticState CaptureSemantic(
        WholeWindowVisualEvidenceScenario scenario,
        IReadOnlyList<DialogPaneVisualEvidenceAssertion> preparationAssertions)
    {
        var definition = FreeP.Ribbon.Definitions.FreePRibbon.Build(FreeP.Ribbon.Definitions.FreePRibbonCapabilities.Avalonia);
        var tabs = access.RibbonTabs;
        var activeTabId = (tabs?.SelectedItem as TabItem)?.Tag as string ?? string.Empty;
        var visibleTabs = tabs?.Items.OfType<TabItem>()
            .Where(item => item.IsVisible)
            .Select(item => item.Tag as string ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray() ?? [];
        var contextualIds = definition.ContextualTabs.Select(tab => tab.Id).ToHashSet(StringComparer.Ordinal);
        var visibleContextualTabs = visibleTabs.Where(contextualIds.Contains).ToArray();
        var focus = DescribeFocus(access.Window.FocusManager?.GetFocusedElement());
        var assertions = preparationAssertions.ToList();
        var preparation = _preparation ??
            throw new InvalidOperationException("Whole-window evidence must be prepared before semantic capture.");
        assertions.AddRange(preparation.CreateActivationAssertions(new(
            _viewStateActivated,
            activeTabId,
            visibleContextualTabs,
            access.IsBackstageOpen && StringComparer.OrdinalIgnoreCase.Equals(access.BackstagePaneLabel, preparation.Activation.Id),
            access.BackstagePaneLabel)));

        var root = access.ClientRoot;
        var semantic = new WholeWindowVisualEvidenceSemanticState(
            scenario.Id,
            "avalonia",
            scenario.ActivationId,
            access.CurrentSlideIndex,
            access.CurrentSlideTitle,
            access.SelectedShapeIds.ToArray(),
            access.SelectedShapeKind,
            activeTabId,
            visibleTabs,
            visibleContextualTabs,
            access.IsBackstageOpen,
            access.BackstagePaneLabel ?? string.Empty,
            focus.Role,
            focus.Label,
            access.StatusText,
            access.ShowGridlines,
            access.ShowGuides,
            access.ZoomMode,
            access.ZoomPercent,
            access.IsTitleBarVisible,
            access.QuickAccessButtonCount,
            access.HasIcon ? "shared-shell:FreeP" : "missing",
            access.WindowTitle,
            0,
            false,
            BoundsRelativeTo(root, access.TitleBar),
            BoundsRelativeTo(root, access.RibbonRoot),
            BoundsRelativeTo(root, access.SlidePaneRoot),
            BoundsRelativeTo(root, access.CanvasRoot),
            BoundsRelativeTo(root, access.NotesRoot),
            BoundsRelativeTo(root, access.StatusRoot),
            access.VisibleAuxiliaryPanes(),
            assertions);
        if (scenario.Kind != WholeWindowVisualEvidenceScenarioKind.RichEditorOverlay ||
            !StringComparer.Ordinal.Equals(scenario.ActivationId, "selection"))
            return semantic;

        var richEditor = access.CaptureRichEditor();
        return semantic with
        {
            RichEditor = new WholeWindowVisualEvidenceRichEditorState(
                richEditor.IsActive,
                DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectionStart,
                DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectionEnd,
                richEditor.SelectedText,
                BoundsRelativeTo(root, richEditor.ActiveVisual)),
        };
    }

    private IReadOnlyList<DialogPaneVisualEvidenceAssertion> PrepareRichEditor(
        WholeWindowVisualEvidencePreparationPlan plan)
    {
        if (plan.RichEditor is not { } richEditor)
            return [];
        var state = access.PrepareRichEditor(richEditor.ShapeId, richEditor.SelectionStart, richEditor.SelectionEnd);
        return plan.CreateRichEditorAssertions(new(
            state.IsActive,
            state.ActiveShapeId,
            state.SelectionSet,
            state.SelectedText,
            state.IsFocused,
            state.RunCount,
            "The production Avalonia rich-text input owns keyboard focus."));
    }

    private bool PrepareViewState(WholeWindowVisualEvidenceActivationKind activation) => activation switch
    {
        WholeWindowVisualEvidenceActivationKind.ViewGridlinesAndGuides => access.SetViewShowState(true, true),
        WholeWindowVisualEvidenceActivationKind.ViewCleanCanvas => access.SetViewShowState(false, false),
        WholeWindowVisualEvidenceActivationKind.ViewZoomFit => SetZoom(PresentationViewZoomState.FitToWindow),
        WholeWindowVisualEvidenceActivationKind.ViewZoom200 =>
            SetZoom(new PresentationViewZoomState(PresentationViewZoomMode.Percent, 200)),
        _ => false,
    };

    private bool SetZoom(PresentationViewZoomState state)
    {
        access.SetZoom(state);
        return true;
    }

    private void ShowAuxiliaryPane(WholeWindowVisualEvidenceActivationKind activation)
    {
        switch (activation)
        {
            case WholeWindowVisualEvidenceActivationKind.ReviewCommentsPane:
                access.ShowCommentsPane();
                access.SelectFirstComment();
                break;
            case WholeWindowVisualEvidenceActivationKind.AccessibilityCheckerPane:
                access.ShowAccessibilityPane();
                access.SelectFirstAccessibilityIssue();
                break;
            case WholeWindowVisualEvidenceActivationKind.AltTextPane: access.ShowAltTextPane(); break;
            case WholeWindowVisualEvidenceActivationKind.ReadingOrderPane: access.ShowReadingOrderPane(); break;
            case WholeWindowVisualEvidenceActivationKind.ProofingPane:
                access.ShowProofingPane();
                access.SelectFirstProofingIssue();
                break;
            case WholeWindowVisualEvidenceActivationKind.MediaCaptionPane: access.ShowMediaCaptionPane(); break;
            case WholeWindowVisualEvidenceActivationKind.SmartArtTextPane: access.ShowSmartArtTextPane(); break;
            case WholeWindowVisualEvidenceActivationKind.AnimationPane: access.EnsureAnimationPaneVisible(); break;
        }
    }

    private static WholeWindowVisualEvidenceBounds BoundsRelativeTo(Visual root, Visual? element)
    {
        if (element is null || !element.IsVisible || element.Bounds.Width <= 0 || element.Bounds.Height <= 0)
            return new(0, 0, 0, 0);
        var point = element.TranslatePoint(default, root);
        return point is null
            ? new(0, 0, 0, 0)
            : new(point.Value.X, point.Value.Y, element.Bounds.Width, element.Bounds.Height);
    }

    private static (string Role, string Label) DescribeFocus(IInputElement? focused) => focused switch
    {
        TextBox box => ("textbox", AutomationProperties.GetName(box) ?? string.Empty),
        Button button => ("button", AutomationProperties.GetName(button) ?? string.Empty),
        TabItem tab => ("tab", tab.Header?.ToString() ?? string.Empty),
        _ => (string.Empty, string.Empty),
    };
}
