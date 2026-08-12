using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeP.App.Compositor;
using FreeP.App.Host;

namespace FreeP.VisualEvidence.Wpf;

internal sealed class WpfWholeWindowVisualEvidenceCoordinator(MainWindow.WpfVisualCaptureAdapter access)
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
        var definition = FreeP.Ribbon.Definitions.FreePRibbon.Build(FreeP.Ribbon.Definitions.FreePRibbonCapabilities.Wpf);
        var activeTabId = ActiveRibbonTabId(definition);
        var visibleTabs = VisibleRibbonTabIds(definition);
        var visibleContextualTabs = definition.Tabs
            .Where(tab => tab.IsContextual && visibleTabs.Contains(tab.Id, StringComparer.Ordinal))
            .Select(tab => tab.Id)
            .ToArray();
        var focus = DescribeFocus(Keyboard.FocusedElement);
        var assertions = preparationAssertions.ToList();
        var preparation = _preparation ??
            throw new InvalidOperationException("Whole-window evidence must be prepared before semantic capture.");
        assertions.AddRange(preparation.CreateActivationAssertions(new(
            _viewStateActivated,
            activeTabId,
            visibleContextualTabs,
            StringComparer.OrdinalIgnoreCase.Equals(access.BackstagePaneLabel, preparation.Activation.Id),
            access.BackstagePaneLabel)));

        var root = access.ClientRoot;
        var semantic = new WholeWindowVisualEvidenceSemanticState(
            scenario.Id,
            "wpf",
            scenario.ActivationId,
            access.CurrentSlideIndex,
            access.CurrentSlideTitle,
            access.SelectedShapeIds.ToArray(),
            access.SelectedShapeKind,
            activeTabId,
            visibleTabs,
            visibleContextualTabs,
            scenario.Kind == WholeWindowVisualEvidenceScenarioKind.BackstagePane,
            access.BackstagePaneLabel ?? string.Empty,
            focus.Role,
            focus.Label,
            access.StatusText,
            access.ShowGridlines,
            access.ShowGuides,
            access.ZoomMode,
            access.ZoomPercent,
            access.IsTitleBarVisible,
            CountQuickAccessButtons(),
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
                richEditor.ActiveVisual is null
                    ? new WholeWindowVisualEvidenceBounds(0, 0, 0, 0)
                    : BoundsRelativeTo(root, richEditor.ActiveVisual)),
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
            "The production WPF RichTextBox owns keyboard focus."));
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

    private string ActiveRibbonTabId(Free.Shared.Ribbon.RibbonDefinition definition)
    {
        var index = access.RibbonTabs.SelectedIndex;
        if (index == 0)
            return "file";
        return index > 0 && index - 1 < definition.Tabs.Count ? definition.Tabs[index - 1].Id : string.Empty;
    }

    private IReadOnlyList<string> VisibleRibbonTabIds(Free.Shared.Ribbon.RibbonDefinition definition)
    {
        var result = new List<string> { "file" };
        for (var index = 1; index < access.RibbonTabs.Items.Count && index - 1 < definition.Tabs.Count; index++)
        {
            if (access.RibbonTabs.Items[index] is TabItem { Visibility: Visibility.Visible })
                result.Add(definition.Tabs[index - 1].Id);
        }
        return result;
    }

    private int CountQuickAccessButtons()
    {
        var ids = new HashSet<string>(["Save", "Undo", "Redo"], StringComparer.Ordinal);
        return Descendants(access.TitleBar).OfType<Button>()
            .Count(button => ids.Contains(AutomationProperties.GetAutomationId(button)));
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

    private static (string Role, string Label) DescribeFocus(IInputElement? focused) => focused switch
    {
        TextBox box => ("textbox", AutomationProperties.GetName(box)),
        Button button => ("button", AutomationProperties.GetName(button)),
        TabItem tab => ("tab", tab.Header?.ToString() ?? string.Empty),
        _ => (string.Empty, string.Empty),
    };

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
