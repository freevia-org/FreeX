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
    private WholeWindowVisualEvidencePreparationPlan? _wholeWindowVisualEvidencePreparation;

    internal IReadOnlyList<DialogPaneVisualEvidenceAssertion> PrepareWholeWindowVisualEvidence(
        WholeWindowVisualEvidenceScenario scenario,
        DialogPaneVisualEvidenceFixture fixture)
    {
        var plan = WholeWindowVisualEvidencePreparationSession.Prepare(scenario, fixture);
        _wholeWindowVisualEvidencePreparation = plan;
        if (plan.LoadFixturePresentation)
            LoadModel(fixture.Presentation);
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
                _backstage.Show(plan.Activation.Id);
                break;
            default:
                if (plan.Activation.IsAuxiliaryPane)
                    ShowAuxiliaryPaneForVisualEvidence(plan.Activation.Kind);
                break;
        }

        RefreshCanvas();
        RefreshNotesPane();
        UpdateSlideCount();
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

        var editor = SlideCanvas.TextEditor;
        editor?.Activate(richEditor.ShapeId);
        var selectionSet = editor?.TrySelectTextRange(richEditor.SelectionStart, richEditor.SelectionEnd) == true;
        var body = Editor.CurrentSlide?.Shapes.Single(shape => shape.Id == richEditor.ShapeId).TextBody;
        int runCount = body?.Paragraphs.SelectMany(paragraph => paragraph.Runs).Count() ?? 0;

        return plan.CreateRichEditorAssertions(new(
            editor?.IsActive == true,
            editor?.ActiveShapeId ?? 0,
            selectionSet,
            editor?.SelectedText ?? string.Empty,
            editor?.IsEditorFocused == true,
            runCount,
            "The production WPF RichTextBox owns keyboard focus."));
    }

    internal void NormalizeWholeWindowVisualEvidenceShellState(WholeWindowVisualEvidenceScenario scenario)
    {
        var activation = WholeWindowVisualEvidencePreparationSession.ResolveActivation(scenario);
        if (activation.IsViewState)
            _wholeWindowVisualEvidenceViewStateActivated = PrepareViewStateForVisualEvidence(activation.Kind);
        Title = "Untitled — FreeP";
        _slideCountText.Text = $"Slide {Editor.CurrentSlideIndex + 1} / {Editor.Presentation.Slides.Count}";
    }

    internal WholeWindowVisualEvidenceSemanticState CaptureWholeWindowVisualEvidenceSemanticState(
        WholeWindowVisualEvidenceScenario scenario,
        IReadOnlyList<DialogPaneVisualEvidenceAssertion> preparationAssertions)
    {
        var definition = FreeP.Ribbon.Definitions.FreePRibbon.Build(FreeP.Ribbon.Definitions.FreePRibbonCapabilities.Wpf);
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
        var preparation = _wholeWindowVisualEvidencePreparation ??
            throw new InvalidOperationException("Whole-window evidence must be prepared before semantic capture.");
        assertions.AddRange(preparation.CreateActivationAssertions(new(
            _wholeWindowVisualEvidenceViewStateActivated,
            activeTabId,
            visibleContextualTabs,
            StringComparer.OrdinalIgnoreCase.Equals(_backstage.EvidencePaneLabel, preparation.Activation.Id),
            _backstage.EvidencePaneLabel)));

        var semantic = new WholeWindowVisualEvidenceSemanticState(
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
        if (scenario.Kind != WholeWindowVisualEvidenceScenarioKind.RichEditorOverlay ||
            !StringComparer.Ordinal.Equals(scenario.ActivationId, "selection"))
            return semantic;

        var editor = SlideCanvas.TextEditor;
        return semantic with
        {
            RichEditor = new WholeWindowVisualEvidenceRichEditorState(
                editor?.IsActive == true,
                DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectionStart,
                DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectionEnd,
                editor?.SelectedText ?? string.Empty,
                editor?.ActiveRichTextVisual is FrameworkElement richVisual
                    ? BoundsRelativeTo(root, richVisual)
                    : new WholeWindowVisualEvidenceBounds(0, 0, 0, 0)),
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

        var definition = FreeP.Ribbon.Definitions.FreePRibbon.Build(FreeP.Ribbon.Definitions.FreePRibbonCapabilities.Wpf);
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
