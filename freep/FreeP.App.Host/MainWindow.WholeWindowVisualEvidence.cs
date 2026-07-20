using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

public sealed partial class MainWindow
{
    internal IReadOnlyList<DialogPaneVisualEvidenceAssertion> PrepareWholeWindowVisualEvidence(
        WholeWindowVisualEvidenceScenario scenario,
        DialogPaneVisualEvidenceFixture fixture)
    {
        LoadModel(fixture.Presentation);
        Editor.SelectSlide(scenario.SlideIndex);
        var selection = WholeWindowVisualEvidenceCatalog.SelectionFor(scenario, fixture);
        if (selection == 0)
            Editor.ClearSelection();
        else
            Editor.Select(selection);

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
                _backstage.Show(scenario.ActivationId);
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
        UpdateSlideCount();

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
                selection == 0
                    ? Editor.SelectedShapeIds.Count == 0
                    : Editor.SelectedShapeIds.SequenceEqual([selection]),
                $"Selected shape ids: {string.Join(",", Editor.SelectedShapeIds)}."),
        ];
    }

    internal WholeWindowVisualEvidenceSemanticState CaptureWholeWindowVisualEvidenceSemanticState(
        WholeWindowVisualEvidenceScenario scenario,
        IReadOnlyList<DialogPaneVisualEvidenceAssertion> preparationAssertions)
    {
        var definition = FreePRibbon.Build();
        var activeTabId = ActiveRibbonTabId(definition);
        var visibleTabs = VisibleRibbonTabIds(definition);
        var visibleContextualTabs = definition.Tabs
            .Where(tab => tab.IsContextual && visibleTabs.Contains(tab.Id, StringComparer.Ordinal))
            .Select(tab => tab.Id)
            .ToArray();
        var selectedShape = Editor.CurrentSlide?.Shapes.FirstOrDefault(shape => Editor.SelectedShapeIds.Contains(shape.Id));
        var root = Content as FrameworkElement ?? this;
        FrameworkElement statusRoot = (FrameworkElement?)Ancestors(_slideCountText).OfType<Border>().FirstOrDefault() ?? _slideCountText;
        FrameworkElement ribbonRoot = (FrameworkElement?)Ancestors(_ribbonTabs).OfType<Border>().FirstOrDefault() ?? _ribbonTabs;
        var focus = DescribeWholeWindowFocus(Keyboard.FocusedElement);
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
                StringComparer.OrdinalIgnoreCase.Equals(_backstage.EvidencePaneLabel, scenario.ActivationId),
                $"Backstage pane is '{_backstage.EvidencePaneLabel ?? "unavailable"}'; expected '{scenario.ActivationId}'."));
        }

        return new WholeWindowVisualEvidenceSemanticState(
            scenario.Id,
            "wpf",
            scenario.ActivationId,
            Editor.CurrentSlideIndex,
            Editor.CurrentSlide?.Title ?? string.Empty,
            Editor.SelectedShapeIds.ToArray(),
            selectedShape?.Kind.ToString() ?? string.Empty,
            activeTabId,
            visibleTabs,
            visibleContextualTabs,
            scenario.Kind == WholeWindowVisualEvidenceScenarioKind.BackstagePane,
            _backstage.EvidencePaneLabel ?? string.Empty,
            focus.Role,
            focus.Label,
            _slideCountText.Text,
            _viewShowState.ShowGridlines,
            _viewShowState.ShowGuides,
            _viewZoomState.Mode.ToString(),
            _viewZoomState.ZoomPercent,
            _titleBar.IsVisible && _titleBar.ActualHeight > 0,
            CountQuickAccessButtons(),
            "generated-badge:P",
            0,
            false,
            BoundsRelativeTo(root, _titleBar),
            BoundsRelativeTo(root, ribbonRoot),
            BoundsRelativeTo(root, SlidePaneHost),
            BoundsRelativeTo(root, _canvasHost),
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
                if (_animPaneHost.Visibility != Visibility.Visible)
                    ToggleAnimationPane();
                break;
        }
    }

    private bool SelectRibbonTabForVisualEvidence(string tabId)
    {
        if (StringComparer.Ordinal.Equals(tabId, "file"))
        {
            _ribbonTabs.SelectedIndex = 0;
            return true;
        }

        var definition = FreePRibbon.Build();
        var index = definition.Tabs.ToList().FindIndex(tab => StringComparer.Ordinal.Equals(tab.Id, tabId));
        if (index < 0)
            return false;
        _ribbonTabs.SelectedIndex = index + 1;
        return true;
    }

    private string ActiveRibbonTabId(Free.Shared.Ribbon.RibbonDefinition definition)
    {
        var index = _ribbonTabs.SelectedIndex;
        if (index == 0)
            return "file";
        return index > 0 && index - 1 < definition.Tabs.Count
            ? definition.Tabs[index - 1].Id
            : string.Empty;
    }

    private IReadOnlyList<string> VisibleRibbonTabIds(Free.Shared.Ribbon.RibbonDefinition definition)
    {
        var result = new List<string> { "file" };
        for (var index = 1; index < _ribbonTabs.Items.Count && index - 1 < definition.Tabs.Count; index++)
        {
            if (_ribbonTabs.Items[index] is TabItem { Visibility: Visibility.Visible })
                result.Add(definition.Tabs[index - 1].Id);
        }
        return result;
    }

    private int CountQuickAccessButtons()
    {
        var ids = new HashSet<string>(["Save", "Undo", "Redo"], StringComparer.Ordinal);
        return VisualDescendants(_titleBar).OfType<Button>()
            .Count(button => ids.Contains(AutomationProperties.GetAutomationId(button)));
    }

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
        if (_animPaneHost.Visibility == Visibility.Visible) result.Add("animations.animation-pane");
        return result;
    }

    private static WholeWindowVisualEvidenceBounds BoundsRelativeTo(FrameworkElement root, FrameworkElement element)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return new(0, 0, 0, 0);
        try
        {
            var point = element.TransformToAncestor(root).Transform(new Point(0, 0));
            return new(point.X, point.Y, element.ActualWidth, element.ActualHeight);
        }
        catch (InvalidOperationException)
        {
            return new(0, 0, 0, 0);
        }
    }

    private static (string Role, string Label) DescribeWholeWindowFocus(IInputElement? focused) => focused switch
    {
        TextBox box => ("textbox", AutomationProperties.GetName(box)),
        Button button => ("button", AutomationProperties.GetName(button)),
        TabItem tab => ("tab", tab.Header?.ToString() ?? string.Empty),
        _ => (string.Empty, string.Empty),
    };

    private static IEnumerable<DependencyObject> VisualDescendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in VisualDescendants(child))
                yield return descendant;
        }
    }

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject start)
    {
        for (var current = VisualTreeHelper.GetParent(start); current is not null; current = VisualTreeHelper.GetParent(current))
            yield return current;
    }
}
