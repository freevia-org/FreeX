using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Free.Shared.Ribbon.Wpf;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

public sealed partial class MainWindow
{
    private bool _wholeWindowVisualEvidenceViewStateActivated;

    internal IReadOnlyList<DialogPaneVisualEvidenceAssertion> PrepareWholeWindowVisualEvidence(
        WholeWindowVisualEvidenceScenario scenario,
        DialogPaneVisualEvidenceFixture fixture)
    {
        PrepareRichEditorFixture(scenario, fixture);
        var cleanStartupState = scenario.Kind == WholeWindowVisualEvidenceScenarioKind.Startup && scenario.ActivationId == "slide";
        if (!cleanStartupState)
            LoadModel(fixture.Presentation);
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

        _wholeWindowVisualEvidenceViewStateActivated = scenario.Kind != WholeWindowVisualEvidenceScenarioKind.ViewState;
        switch (scenario.Kind)
        {
            case WholeWindowVisualEvidenceScenarioKind.Startup when scenario.ActivationId == "notes":
            case WholeWindowVisualEvidenceScenarioKind.WorkspaceRegion when scenario.ActivationId == "notes-pane":
                _notesBox.Focus();
                break;
            case WholeWindowVisualEvidenceScenarioKind.BackstagePane:
                _backstage.Show(scenario.ActivationId);
                break;
            case WholeWindowVisualEvidenceScenarioKind.AuxiliaryPane:
                ShowAuxiliaryPaneForVisualEvidence(scenario.ActivationId);
                break;
        }

        RefreshCanvas();
        RefreshNotesPane();
        UpdateSlideCount();
        NormalizeWholeWindowVisualEvidenceShellState(scenario);

        var assertions = new List<DialogPaneVisualEvidenceAssertion>
        {
            new(
                "fixture-loaded",
                Editor.Presentation.Slides.Count == (cleanStartupState ? 1 : 3),
                cleanStartupState
                    ? $"Captured the clean startup document with {Editor.Presentation.Slides.Count} slide."
                    : $"Loaded {Editor.Presentation.Slides.Count} seeded slides."),
            new(
                "slide-activated",
                Editor.CurrentSlideIndex == scenario.SlideIndex,
                $"Activated slide index {Editor.CurrentSlideIndex}; expected {scenario.SlideIndex}."),
            new(
                "selection-activated",
                selectionPrepared,
                $"Selected shape ids: {string.Join(",", Editor.SelectedShapeIds)}."),
        };
        assertions.AddRange(PrepareRichEditorOverlayForVisualEvidence(scenario, fixture));
        return assertions;
    }

    private static void PrepareRichEditorFixture(
        WholeWindowVisualEvidenceScenario scenario,
        DialogPaneVisualEvidenceFixture fixture)
    {
        if (scenario.Kind != WholeWindowVisualEvidenceScenarioKind.RichEditorOverlay)
            return;

        var shape = fixture.Presentation.Slides[scenario.SlideIndex].Shapes
            .Single(candidate => candidate.Id == fixture.TextShapeId);
        shape.TextBody = DialogPaneVisualEvidenceFixtureFactory.CreateRichEditorBody();
    }

    private IReadOnlyList<DialogPaneVisualEvidenceAssertion> PrepareRichEditorOverlayForVisualEvidence(
        WholeWindowVisualEvidenceScenario scenario,
        DialogPaneVisualEvidenceFixture fixture)
    {
        if (scenario.Kind != WholeWindowVisualEvidenceScenarioKind.RichEditorOverlay)
            return [];

        var editor = SlideCanvas.TextEditor;
        editor?.Activate(fixture.TextShapeId);
        int start = scenario.ActivationId == "selection"
            ? DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectionStart
            : DialogPaneVisualEvidenceFixtureFactory.RichEditorCaretPosition;
        int end = scenario.ActivationId == "selection"
            ? DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectionEnd
            : start;
        bool selectionSet = editor?.TrySelectTextRange(start, end) == true;
        string expectedText = scenario.ActivationId == "selection"
            ? DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectedText
            : string.Empty;
        var body = Editor.CurrentSlide?.Shapes.Single(shape => shape.Id == fixture.TextShapeId).TextBody;
        int runCount = body?.Paragraphs.SelectMany(paragraph => paragraph.Runs).Count() ?? 0;

        return
        [
            new(
                "rich-editor-activated",
                editor is { IsActive: true, ActiveShapeId: var activeId } && activeId == fixture.TextShapeId,
                $"Active rich-editor shape id is {editor?.ActiveShapeId ?? 0}; expected {fixture.TextShapeId}."),
            new(
                "rich-editor-selection",
                selectionSet && StringComparer.Ordinal.Equals(editor?.SelectedText, expectedText),
                $"Selected '{editor?.SelectedText ?? string.Empty}' at logical range {start}..{end}."),
            new(
                "rich-editor-focus",
                editor?.IsEditorFocused == true,
                "The production WPF RichTextBox owns keyboard focus."),
            new(
                "rich-editor-mixed-runs",
                runCount == 3,
                $"The production overlay contains {runCount} model runs; expected 3 mixed-format runs."),
        ];
    }

    internal void NormalizeWholeWindowVisualEvidenceShellState(WholeWindowVisualEvidenceScenario scenario)
    {
        if (scenario.Kind == WholeWindowVisualEvidenceScenarioKind.ViewState)
            _wholeWindowVisualEvidenceViewStateActivated = PrepareViewStateForVisualEvidence(scenario.ActivationId);
        Title = "Untitled — FreeP";
        _slideCountText.Text = $"Slide {Editor.CurrentSlideIndex + 1} / {Editor.Presentation.Slides.Count}";
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

        if (scenario.Kind == WholeWindowVisualEvidenceScenarioKind.ViewState)
        {
            assertions.Add(new(
                "view-state-activated-via-command",
                _wholeWindowVisualEvidenceViewStateActivated,
                $"Activated view state '{scenario.ActivationId}' through the runtime ribbon command path."));
        }

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
            Icon is null ? "missing" : "shared-shell:FreeP",
            Title,
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

    private bool PrepareViewStateForVisualEvidence(string activationId)
    {
        switch (activationId)
        {
            case "gridlines-guides":
                return EnsureViewShowStateForVisualEvidence(showGridlines: true, showGuides: true);
            case "clean-canvas":
                return EnsureViewShowStateForVisualEvidence(showGridlines: false, showGuides: false);
            case "zoom-fit":
                ApplyPresentationViewZoomState(PresentationViewZoomState.FitToWindow);
                return true;
            case "zoom-200":
                ApplyPresentationViewZoomState(new PresentationViewZoomState(PresentationViewZoomMode.Percent, 200));
                return true;
            default:
                return false;
        }
    }

    private bool EnsureViewShowStateForVisualEvidence(bool showGridlines, bool showGuides)
    {
        var activated = true;
        if (_viewShowState.ShowGridlines != showGridlines)
            activated &= ExecuteRibbonCommandForVisualEvidence(PresentationViewShowPlanner.GridlinesCommandId);
        if (_viewShowState.ShowGuides != showGuides)
            activated &= ExecuteRibbonCommandForVisualEvidence(PresentationViewShowPlanner.GuidesCommandId);
        return activated && _viewShowState == new PresentationViewShowState(showGridlines, showGuides);
    }

    private bool ExecuteRibbonCommandForVisualEvidence(string commandId)
    {
        var button = VisualDescendants(_ribbonTabs)
            .OfType<ButtonBase>()
            .FirstOrDefault(candidate => StringComparer.Ordinal.Equals(RibbonMetadata.GetCommandName(candidate), commandId));
        if (button is null)
            return false;
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
        return true;
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
