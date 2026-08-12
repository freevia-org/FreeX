using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Theme;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

public sealed partial class MainWindow
{
    private bool _wholeWindowVisualEvidenceViewStateActivated;
    private WholeWindowVisualEvidencePreparationPlan? _wholeWindowVisualEvidencePreparation;

    internal IReadOnlyList<DialogPaneVisualEvidenceAssertion> PrepareWholeWindowVisualEvidence(
        WholeWindowVisualEvidenceScenario scenario,
        DialogPaneVisualEvidenceFixture fixture)
    {
        var plan = WholeWindowVisualEvidencePreparationSession.Prepare(scenario, fixture);
        _wholeWindowVisualEvidencePreparation = plan;
        if (plan.LoadFixturePresentation)
            LoadPresentationContent(fixture.Presentation);
        Editor.SelectSlide(plan.SlideIndex);
        if (plan.SelectionShapeId == 0)
            Editor.ClearSelection();
        else
            Editor.Select(plan.SelectionShapeId);

        HideReviewCommentsPane();
        SelectRibbonTabForVisualEvidence(plan.ActiveRibbonTabId);

        _wholeWindowVisualEvidenceViewStateActivated = !plan.Activation.IsViewState;
        switch (plan.Activation.Kind)
        {
            case WholeWindowVisualEvidenceActivationKind.FocusNotesPane:
                _notesBox.Focus();
                break;
            case WholeWindowVisualEvidenceActivationKind.BackstagePane:
                _backstage.Show();
                _backstage.TryActivateEntry(plan.Activation.Id);
                break;
            default:
                if (plan.Activation.IsAuxiliaryPane)
                    ShowAuxiliaryPaneForVisualEvidence(plan.Activation.Kind);
                break;
        }

        RefreshCanvas();
        RefreshNotesPane();
        UpdateStatus();
        NormalizeWholeWindowVisualEvidenceShellState(scenario);

        var assertions = plan.CreateBaselineAssertions(new(
            Editor.Presentation.Slides.Count,
            Editor.CurrentSlideIndex,
            Editor.SelectedShapeIds)).ToList();
        assertions.AddRange(PrepareRichEditorOverlayForVisualEvidence(plan));
        return assertions;
    }

    private IReadOnlyList<DialogPaneVisualEvidenceAssertion> PrepareRichEditorOverlayForVisualEvidence(
        WholeWindowVisualEvidencePreparationPlan plan)
    {
        if (plan.RichEditor is not { } richEditor)
            return [];

        _textEditor?.Activate(richEditor.ShapeId);
        var selectionSet = _textEditor?.TrySelectTextRange(richEditor.SelectionStart, richEditor.SelectionEnd) == true;
        var body = Editor.CurrentSlide?.Shapes.Single(shape => shape.Id == richEditor.ShapeId).TextBody;
        int runCount = body?.Paragraphs.SelectMany(paragraph => paragraph.Runs).Count() ?? 0;

        return plan.CreateRichEditorAssertions(new(
            _textEditor?.IsActive == true,
            _textEditor?.ActiveShapeId ?? 0,
            selectionSet,
            _textEditor?.SelectedText ?? string.Empty,
            _textEditor?.IsEditorFocused == true,
            runCount,
            "The production Avalonia rich-text input owns keyboard focus."));
    }

    internal void NormalizeWholeWindowVisualEvidenceShellState(WholeWindowVisualEvidenceScenario scenario)
    {
        var activation = WholeWindowVisualEvidencePreparationSession.ResolveActivation(scenario);
        if (activation.IsViewState)
            _wholeWindowVisualEvidenceViewStateActivated = PrepareViewStateForVisualEvidence(activation.Kind);
        Title = "Untitled — FreeP";
        _statusText.Text = $"Slide {Editor.CurrentSlideIndex + 1} / {Editor.Presentation.Slides.Count}";
    }

    internal WholeWindowVisualEvidenceSemanticState CaptureWholeWindowVisualEvidenceSemanticState(
        WholeWindowVisualEvidenceScenario scenario,
        IReadOnlyList<DialogPaneVisualEvidenceAssertion> preparationAssertions)
    {
        var definition = FreeP.Ribbon.Definitions.FreePRibbon.Build(FreeP.Ribbon.Definitions.FreePRibbonCapabilities.Avalonia);
        var tabs = RibbonTabControlForVisualEvidence();
        var activeTabId = (tabs?.SelectedItem as TabItem)?.Tag as string ?? string.Empty;
        var visibleTabs = tabs?.Items.OfType<TabItem>()
            .Where(item => item.IsVisible)
            .Select(item => item.Tag as string ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray() ?? [];
        var contextualIds = definition.ContextualTabs.Select(tab => tab.Id).ToHashSet(StringComparer.Ordinal);
        var visibleContextualTabs = visibleTabs.Where(contextualIds.Contains).ToArray();
        var selectedShape = Editor.CurrentSlide is { } currentSlide
            ? SlideShapeTraversal.EnumerateDepthFirst(currentSlide)
                .FirstOrDefault(shape => Editor.SelectedShapeIds.Contains(shape.Id))
            : null;
        var root = Content as Visual ?? this;
        var statusRoot = _statusText.GetVisualAncestors().OfType<Border>().FirstOrDefault() as Visual ?? _statusText;
        var ribbonRoot = _ribbonControl?.GetVisualAncestors().OfType<Border>().FirstOrDefault() as Visual ?? _ribbonControl;
        var slidePaneRoot = _slidePaneList.Parent?.Parent as Visual ?? _slidePaneList;
        var focus = DescribeWholeWindowFocus(FocusManager?.GetFocusedElement());
        var assertions = preparationAssertions.ToList();
        var preparation = _wholeWindowVisualEvidencePreparation ??
            throw new InvalidOperationException("Whole-window evidence must be prepared before semantic capture.");
        assertions.AddRange(preparation.CreateActivationAssertions(new(
            _wholeWindowVisualEvidenceViewStateActivated,
            activeTabId,
            visibleContextualTabs,
            _backstage.IsOpen && StringComparer.OrdinalIgnoreCase.Equals(_backstage.CurrentPaneLabel, preparation.Activation.Id),
            _backstage.CurrentPaneLabel)));

        var semantic = new WholeWindowVisualEvidenceSemanticState(
            scenario.Id,
            "avalonia",
            scenario.ActivationId,
            Editor.CurrentSlideIndex,
            Editor.CurrentSlide?.Title ?? string.Empty,
            Editor.SelectedShapeIds.ToArray(),
            selectedShape?.Kind.ToString() ?? string.Empty,
            activeTabId,
            visibleTabs,
            visibleContextualTabs,
            _backstage.IsOpen,
            _backstage.CurrentPaneLabel ?? string.Empty,
            focus.Role,
            focus.Label,
            _statusText.Text ?? string.Empty,
            _viewShowState.ShowGridlines,
            _viewShowState.ShowGuides,
            _viewZoomState.Mode.ToString(),
            _viewZoomState.ZoomPercent,
            _titleBar.IsVisible && _titleBar.Bounds.Height > 0,
            _quickAccessButtons.Count,
            Icon is null ? "missing" : "shared-shell:FreeP",
            Title ?? string.Empty,
            0,
            false,
            BoundsRelativeTo(root, _titleBar),
            BoundsRelativeTo(root, ribbonRoot),
            BoundsRelativeTo(root, slidePaneRoot),
            BoundsRelativeTo(root, _canvasHost),
            BoundsRelativeTo(root, _notesBox),
            BoundsRelativeTo(root, statusRoot),
            VisibleAuxiliaryPanesForEvidence(),
            assertions);
        if (scenario.Kind != WholeWindowVisualEvidenceScenarioKind.RichEditorOverlay ||
            !StringComparer.Ordinal.Equals(scenario.ActivationId, "selection"))
            return semantic;

        return semantic with
        {
            RichEditor = new WholeWindowVisualEvidenceRichEditorState(
                _textEditor?.IsActive == true,
                DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectionStart,
                DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectionEnd,
                _textEditor?.SelectedText ?? string.Empty,
                BoundsRelativeTo(root, _textEditor?.ActiveRichTextVisual)),
        };
    }

    private bool PrepareViewStateForVisualEvidence(WholeWindowVisualEvidenceActivationKind activation)
    {
        switch (activation)
        {
            case WholeWindowVisualEvidenceActivationKind.ViewGridlinesAndGuides:
                return EnsureViewShowStateForVisualEvidence(showGridlines: true, showGuides: true);
            case WholeWindowVisualEvidenceActivationKind.ViewCleanCanvas:
                return EnsureViewShowStateForVisualEvidence(showGridlines: false, showGuides: false);
            case WholeWindowVisualEvidenceActivationKind.ViewZoomFit:
                ApplyPresentationViewZoomState(PresentationViewZoomState.FitToWindow);
                return true;
            case WholeWindowVisualEvidenceActivationKind.ViewZoom200:
                ApplyPresentationViewZoomState(new PresentationViewZoomState(PresentationViewZoomMode.Percent, 200));
                return true;
            default:
                return false;
        }
    }

    private bool EnsureViewShowStateForVisualEvidence(bool showGridlines, bool showGuides)
    {
        var registry = BuildCommandRegistry();
        var activated = true;
        if (_viewShowState.ShowGridlines != showGridlines)
            activated &= ExecuteRibbonCommandForVisualEvidence(registry, PresentationViewShowPlanner.GridlinesCommandId);
        if (_viewShowState.ShowGuides != showGuides)
            activated &= ExecuteRibbonCommandForVisualEvidence(registry, PresentationViewShowPlanner.GuidesCommandId);
        if (_ribbonControl is not null)
            AvaloniaRibbonRenderer.SyncToggleStates(_ribbonControl, registry, RibbonVisualPalette.FromTheme(App.ActiveTheme));
        return activated && _viewShowState == new PresentationViewShowState(showGridlines, showGuides);
    }

    private static bool ExecuteRibbonCommandForVisualEvidence(RibbonCommandRegistry registry, string commandId)
    {
        if (!registry.TryGet(new RibbonCommandId(commandId), out var command) || command is null)
            return false;
        command.Execute(RibbonCommandContext.Empty);
        return true;
    }

    private void ShowAuxiliaryPaneForVisualEvidence(WholeWindowVisualEvidenceActivationKind activation)
    {
        switch (activation)
        {
            case WholeWindowVisualEvidenceActivationKind.ReviewCommentsPane:
                ShowReviewCommentsPane();
                SetSelectedReviewCommentIndexForTests(0);
                break;
            case WholeWindowVisualEvidenceActivationKind.AccessibilityCheckerPane:
                ShowAccessibilityCheckerPane();
                if (AccessibilityCheckerPaneRowCount > 0)
                    SelectAccessibilityCheckerRow(0);
                break;
            case WholeWindowVisualEvidenceActivationKind.AltTextPane:
                ShowAltTextPane();
                break;
            case WholeWindowVisualEvidenceActivationKind.ReadingOrderPane:
                ShowReadingOrderPane();
                break;
            case WholeWindowVisualEvidenceActivationKind.ProofingPane:
                ShowProofingPane();
                if (ProofingPaneIssueRowCount > 0)
                    SelectProofingIssueRow(0);
                break;
            case WholeWindowVisualEvidenceActivationKind.MediaCaptionPane:
                ShowMediaCaptionPane();
                break;
            case WholeWindowVisualEvidenceActivationKind.SmartArtTextPane:
                ShowSmartArtTextPane();
                break;
            case WholeWindowVisualEvidenceActivationKind.AnimationPane:
                if (!IsAnimationPaneVisible)
                    ShowAnimationPane();
                break;
        }
    }

    private bool SelectRibbonTabForVisualEvidence(string tabId)
    {
        var tabs = RibbonTabControlForVisualEvidence();
        var item = tabs?.Items.OfType<TabItem>()
            .FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Tag as string, tabId));
        if (tabs is null || item is null)
            return false;
        tabs.SelectedItem = item;
        return true;
    }

    private TabControl? RibbonTabControlForVisualEvidence() =>
        _ribbonControl is null
            ? null
            : _ribbonControl.GetVisualDescendants().Prepend(_ribbonControl).OfType<TabControl>().FirstOrDefault();

    private IReadOnlyList<string> VisibleAuxiliaryPanesForEvidence()
    {
        var result = new List<string>();
        if (IsReviewCommentsPaneVisible) result.Add("review.comments-pane");
        if (IsAccessibilityCheckerPaneVisible) result.Add("review.accessibility-pane");
        if (IsAltTextPaneVisible) result.Add("review.alt-text-pane");
        if (IsReadingOrderPaneVisible) result.Add("review.reading-order-pane");
        if (IsProofingPaneVisible) result.Add("review.proofing-pane");
        if (IsMediaCaptionPaneVisible) result.Add("accessibility.media-caption-pane");
        if (IsSmartArtTextPaneVisible) result.Add("context.smartart-text-pane");
        if (IsAnimationPaneVisible) result.Add("animations.animation-pane");
        return result;
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

    private static (string Role, string Label) DescribeWholeWindowFocus(IInputElement? focused) => focused switch
    {
        TextBox box => ("textbox", AutomationProperties.GetName(box) ?? string.Empty),
        Button button => ("button", AutomationProperties.GetName(button) ?? string.Empty),
        TabItem tab => ("tab", tab.Header?.ToString() ?? string.Empty),
        _ => (string.Empty, string.Empty),
    };
}
