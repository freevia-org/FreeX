using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.Core.Model;

namespace FreeP.VisualEvidence.Wpf;

internal sealed class WpfWholeWindowVisualEvidenceCoordinator : IWholeWindowVisualEvidenceNativeInspector
{
    private readonly MainWindow.WpfVisualCaptureAdapter _access;
    private readonly WholeWindowVisualEvidenceHostCoordinator _coordinator;

    internal WpfWholeWindowVisualEvidenceCoordinator(MainWindow.WpfVisualCaptureAdapter access)
    {
        _access = access;
        _coordinator = new(new WpfVisualEvidenceAppHost(access), this);
    }

    internal IReadOnlyList<DialogPaneVisualEvidenceAssertion> Prepare(
        WholeWindowVisualEvidenceScenario scenario,
        DialogPaneVisualEvidenceFixture fixture) =>
        _coordinator.Prepare(scenario, fixture);

    internal void Normalize(WholeWindowVisualEvidenceScenario scenario) =>
        _coordinator.Normalize(scenario);

    internal WholeWindowVisualEvidenceSemanticState CaptureSemantic(
        WholeWindowVisualEvidenceScenario scenario,
        IReadOnlyList<DialogPaneVisualEvidenceAssertion> preparationAssertions) =>
        _coordinator.CaptureSemantic(scenario, preparationAssertions);

    WholeWindowVisualEvidenceBaselineState IWholeWindowVisualEvidenceNativeInspector.CaptureBaselineState() => new(
        _access.SlideCount,
        _access.CurrentSlideIndex,
        _access.SelectedShapeIds);

    WholeWindowVisualEvidenceRichEditorPreparationState IWholeWindowVisualEvidenceNativeInspector.PrepareRichEditor(
        WholeWindowVisualEvidenceRichEditorPlan plan)
    {
        var state = _access.PrepareRichEditor(plan.ShapeId, plan.SelectionStart, plan.SelectionEnd);
        return new(
            state.IsActive,
            state.ActiveShapeId,
            state.SelectionSet,
            state.SelectedText,
            state.IsFocused,
            state.RunCount,
            "The production WPF RichTextBox owns keyboard focus.");
    }

    WholeWindowVisualEvidenceProbeState IWholeWindowVisualEvidenceNativeInspector.CaptureSemanticState(
        WholeWindowVisualEvidenceScenario scenario)
    {
        var definition = FreeP.Ribbon.Definitions.FreePRibbon.Build(FreeP.Ribbon.Definitions.FreePRibbonCapabilities.Wpf);
        var activeTabId = ActiveRibbonTabId(definition);
        var visibleTabs = VisibleRibbonTabIds(definition);
        var visibleContextualTabs = definition.Tabs
            .Where(tab => tab.IsContextual && visibleTabs.Contains(tab.Id, StringComparer.Ordinal))
            .Select(tab => tab.Id)
            .ToArray();
        var focus = DescribeFocus(Keyboard.FocusedElement);
        var root = _access.ClientRoot;
        return new(
            "wpf",
            _access.CurrentSlideIndex,
            _access.CurrentSlideTitle,
            _access.SelectedShapeIds,
            _access.SelectedShapeKind,
            activeTabId,
            visibleTabs,
            visibleContextualTabs,
            scenario.Kind == WholeWindowVisualEvidenceScenarioKind.BackstagePane,
            _access.BackstagePaneLabel,
            focus.Role,
            focus.Label,
            _access.StatusText,
            _access.ShowGridlines,
            _access.ShowGuides,
            _access.ZoomMode,
            _access.ZoomPercent,
            _access.IsTitleBarVisible,
            CountQuickAccessButtons(),
            _access.HasIcon,
            _access.WindowTitle,
            BoundsRelativeTo(root, _access.TitleBar),
            BoundsRelativeTo(root, _access.RibbonRoot),
            BoundsRelativeTo(root, _access.SlidePaneRoot),
            BoundsRelativeTo(root, _access.CanvasRoot),
            BoundsRelativeTo(root, _access.NotesRoot),
            BoundsRelativeTo(root, _access.StatusRoot),
            _access.VisibleAuxiliaryPanes());
    }

    WholeWindowVisualEvidenceRichEditorProbeState IWholeWindowVisualEvidenceNativeInspector.CaptureRichEditorState()
    {
        var state = _access.CaptureRichEditor();
        return new(
            state.IsActive,
            state.SelectedText,
            state.ActiveVisual is null
                ? new WholeWindowVisualEvidenceBounds(0, 0, 0, 0)
                : BoundsRelativeTo(_access.ClientRoot, state.ActiveVisual));
    }

    private string ActiveRibbonTabId(Free.Shared.Ribbon.RibbonDefinition definition)
    {
        var index = _access.RibbonTabs.SelectedIndex;
        if (index == 0)
            return "file";
        return index > 0 && index - 1 < definition.Tabs.Count ? definition.Tabs[index - 1].Id : string.Empty;
    }

    private IReadOnlyList<string> VisibleRibbonTabIds(Free.Shared.Ribbon.RibbonDefinition definition)
    {
        var result = new List<string> { "file" };
        for (var index = 1; index < _access.RibbonTabs.Items.Count && index - 1 < definition.Tabs.Count; index++)
        {
            if (_access.RibbonTabs.Items[index] is TabItem { Visibility: Visibility.Visible })
                result.Add(definition.Tabs[index - 1].Id);
        }
        return result;
    }

    private int CountQuickAccessButtons()
    {
        var ids = new HashSet<string>(["Save", "Undo", "Redo"], StringComparer.Ordinal);
        return Descendants(_access.TitleBar).OfType<Button>()
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

internal sealed class WpfVisualEvidenceAppHost(MainWindow.WpfVisualCaptureAdapter access)
    : IVisualEvidenceAppHost
{
    public IReadOnlyList<uint> SelectedShapeIds => access.SelectedShapeIds;
    public int SlideCount => access.SlideCount;
    public int CurrentSlideIndex => access.CurrentSlideIndex;
    public int CurrentShapeCount => access.CurrentShapeCount;
    public string? CurrentLayoutId => access.CurrentLayoutId;
    public bool IsTablePickerVisible => access.IsTablePickerVisible;
    public bool IsLayoutPickerVisible => access.IsLayoutPickerVisible;
    public DialogPaneVisualEvidenceChoiceState ChoiceState => new(
        access.TableChoiceCount,
        access.DefaultTableChoiceCount,
        access.CurrentLayoutChoiceCount,
        access.DisabledLayoutChoiceCount);

    public void LoadPresentation(Presentation presentation) => access.LoadPresentation(presentation);
    public void SelectSlide(int slideIndex) => access.SelectSlide(slideIndex);
    public void SelectShape(uint shapeId) => access.SelectShape(shapeId);
    public void ClearSelection() => access.ClearSelection();
    public void RefreshCanvas() => access.RefreshCanvas();
    public void RefreshWholeWindow() => access.RefreshWholeWindow();
    public void NormalizeShell() => access.NormalizeShell();
    public void HideCommentsPane() => access.HideCommentsPane();
    public bool SelectRibbonTab(string tabId) => access.SelectRibbonTab(tabId);
    public void FocusNotes() => access.FocusNotes();
    public void ShowBackstagePane(string paneId) => access.ShowBackstagePane(paneId);
    public void ShowCommentsPane() => access.ShowCommentsPane();
    public void SelectFirstComment() => access.SelectFirstComment();
    public void ShowAccessibilityPane() => access.ShowAccessibilityPane();
    public void SelectFirstAccessibilityIssue() => access.SelectFirstAccessibilityIssue();
    public void ShowAltTextPane() => access.ShowAltTextPane();
    public void ShowReadingOrderPane() => access.ShowReadingOrderPane();
    public void ShowProofingPane() => access.ShowProofingPane();
    public void SelectFirstProofingIssue() => access.SelectFirstProofingIssue();
    public void ShowMediaCaptionPane() => access.ShowMediaCaptionPane();
    public void ShowSmartArtTextPane() => access.ShowSmartArtTextPane();
    public void EnsureAnimationPaneVisible() => access.EnsureAnimationPaneVisible();
    public void ShowPrintOptionsPane() => access.ShowPrintOptionsPane();
    public void OpenTablePicker() => access.OpenTablePicker();
    public void OpenLayoutPicker() => access.OpenLayoutPicker();
    public void HideTablePicker() => access.HideTablePicker();
    public void HideLayoutPicker() => access.HideLayoutPicker();
    public bool SetViewShowState(bool showGridlines, bool showGuides) =>
        access.SetViewShowState(showGridlines, showGuides);
    public void SetZoom(PresentationViewZoomState state) => access.SetZoom(state);
}
