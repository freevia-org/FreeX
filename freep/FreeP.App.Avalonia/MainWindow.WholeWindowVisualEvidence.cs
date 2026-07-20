using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;
using FreeP.App.Compositor;

namespace FreeP.App.Avalonia;

public sealed partial class MainWindow
{
    internal IReadOnlyList<DialogPaneVisualEvidenceAssertion> PrepareWholeWindowVisualEvidence(
        WholeWindowVisualEvidenceScenario scenario,
        DialogPaneVisualEvidenceFixture fixture)
    {
        LoadPresentationContent(fixture.Presentation);
        Editor.SelectSlide(scenario.SlideIndex);
        var selection = WholeWindowVisualEvidenceCatalog.SelectionFor(scenario, fixture);
        if (selection == 0)
            Editor.ClearSelection();
        else
            Editor.Select(selection);
        var selectionPrepared = selection == 0
            ? Editor.SelectedShapeIds.Count == 0
            : Editor.SelectedShapeIds.SequenceEqual([selection]);

        HideReviewCommentsPane();
        SelectRibbonTabForVisualEvidence(
            string.IsNullOrWhiteSpace(scenario.ExpectedActiveRibbonTabId)
                ? "home"
                : scenario.ExpectedActiveRibbonTabId);

        switch (scenario.Kind)
        {
            case WholeWindowVisualEvidenceScenarioKind.Startup when scenario.ActivationId == "notes":
            case WholeWindowVisualEvidenceScenarioKind.WorkspaceRegion when scenario.ActivationId == "notes-pane":
                _notesBox.Focus();
                break;
            case WholeWindowVisualEvidenceScenarioKind.BackstagePane:
                _backstage.Show();
                _backstage.TryActivateEntry(scenario.ActivationId);
                break;
            case WholeWindowVisualEvidenceScenarioKind.ViewState:
                PrepareViewStateForVisualEvidence(scenario.ActivationId);
                break;
            case WholeWindowVisualEvidenceScenarioKind.AuxiliaryPane:
                ShowAuxiliaryPaneForVisualEvidence(scenario.ActivationId);
                break;
        }

        RefreshCanvas();
        RefreshNotesPane();
        UpdateStatus();

        return
        [
            new(
                "fixture-loaded",
                Editor.Presentation.Slides.Count == 3,
                $"Loaded {Editor.Presentation.Slides.Count} seeded slides."),
            new(
                "slide-activated",
                Editor.CurrentSlideIndex == scenario.SlideIndex,
                $"Activated slide index {Editor.CurrentSlideIndex}; expected {scenario.SlideIndex}."),
            new(
                "selection-activated",
                selectionPrepared,
                $"Selected shape ids: {string.Join(",", Editor.SelectedShapeIds)}."),
        ];
    }

    internal WholeWindowVisualEvidenceSemanticState CaptureWholeWindowVisualEvidenceSemanticState(
        WholeWindowVisualEvidenceScenario scenario,
        IReadOnlyList<DialogPaneVisualEvidenceAssertion> preparationAssertions)
    {
        var definition = FreePRibbonAvalonia.Build();
        var tabs = RibbonTabControlForVisualEvidence();
        var activeTabId = (tabs?.SelectedItem as TabItem)?.Tag as string ?? string.Empty;
        var visibleTabs = tabs?.Items.OfType<TabItem>()
            .Where(item => item.IsVisible)
            .Select(item => item.Tag as string ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray() ?? [];
        var contextualIds = definition.ContextualTabs.Select(tab => tab.Id).ToHashSet(StringComparer.Ordinal);
        var visibleContextualTabs = visibleTabs.Where(contextualIds.Contains).ToArray();
        var selectedShape = Editor.CurrentSlide?.Shapes.FirstOrDefault(shape => Editor.SelectedShapeIds.Contains(shape.Id));
        var root = Content as Visual ?? this;
        var statusRoot = _statusText.GetVisualAncestors().OfType<Border>().FirstOrDefault() as Visual ?? _statusText;
        var ribbonRoot = _ribbonControl?.GetVisualAncestors().OfType<Border>().FirstOrDefault() as Visual ?? _ribbonControl;
        var slidePaneRoot = _slidePaneList.Parent?.Parent as Visual ?? _slidePaneList;
        var focus = DescribeWholeWindowFocus(FocusManager?.GetFocusedElement());
        var assertions = preparationAssertions.ToList();

        if (!string.IsNullOrWhiteSpace(scenario.ExpectedActiveRibbonTabId) &&
            scenario.Kind != WholeWindowVisualEvidenceScenarioKind.BackstagePane)
        {
            assertions.Add(new(
                "active-ribbon-tab",
                StringComparer.Ordinal.Equals(activeTabId, scenario.ExpectedActiveRibbonTabId),
                $"Active ribbon tab is '{activeTabId}'; expected '{scenario.ExpectedActiveRibbonTabId}'."));
        }

        if (!string.IsNullOrWhiteSpace(scenario.ExpectedContextualTabId))
        {
            assertions.Add(new(
                "contextual-tab-visible",
                visibleContextualTabs.Contains(scenario.ExpectedContextualTabId, StringComparer.Ordinal),
                visibleContextualTabs.Length == 0
                    ? $"Expected contextual tab '{scenario.ExpectedContextualTabId}', but FreeP declares no contextual ribbon tabs."
                    : $"Visible contextual tabs: {string.Join(", ", visibleContextualTabs)}."));
        }

        if (scenario.Kind == WholeWindowVisualEvidenceScenarioKind.BackstagePane)
        {
            assertions.Add(new(
                "backstage-pane-activated",
                _backstage.IsOpen && StringComparer.OrdinalIgnoreCase.Equals(_backstage.CurrentPaneLabel, scenario.ActivationId),
                $"Backstage pane is '{_backstage.CurrentPaneLabel ?? "unavailable"}'; expected '{scenario.ActivationId}'."));
        }

        return new WholeWindowVisualEvidenceSemanticState(
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
            BoundsRelativeTo(root, _slideCanvas),
            BoundsRelativeTo(root, _notesBox),
            BoundsRelativeTo(root, statusRoot),
            VisibleAuxiliaryPanesForEvidence(),
            assertions);
    }

    private void PrepareViewStateForVisualEvidence(string activationId)
    {
        switch (activationId)
        {
            case "gridlines-guides":
                ApplyPresentationViewShowState(new PresentationViewShowState(true, true));
                break;
            case "clean-canvas":
                ApplyPresentationViewShowState(new PresentationViewShowState(false, false));
                break;
            case "zoom-fit":
                ApplyPresentationViewZoomState(PresentationViewZoomState.FitToWindow);
                break;
            case "zoom-200":
                ApplyPresentationViewZoomState(new PresentationViewZoomState(PresentationViewZoomMode.Percent, 200));
                break;
        }
    }

    private void ShowAuxiliaryPaneForVisualEvidence(string activationId)
    {
        switch (activationId)
        {
            case "comments":
                ShowReviewCommentsPane();
                SetSelectedReviewCommentIndexForTests(0);
                break;
            case "accessibility":
                ShowAccessibilityCheckerPane();
                if (AccessibilityCheckerPaneRowCount > 0)
                    SelectAccessibilityCheckerRow(0);
                break;
            case "alt-text":
                ShowAltTextPane();
                break;
            case "reading-order":
                ShowReadingOrderPane();
                break;
            case "proofing":
                ShowProofingPane();
                if (ProofingPaneIssueRowCount > 0)
                    SelectProofingIssueRow(0);
                break;
            case "media-caption":
                ShowMediaCaptionPane();
                break;
            case "smartart-text":
                ShowSmartArtTextPane();
                break;
            case "animation":
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
