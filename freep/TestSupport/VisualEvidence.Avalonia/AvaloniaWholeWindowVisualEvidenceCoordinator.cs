using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FreeP.App.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.VisualEvidence.Avalonia;

internal sealed class AvaloniaWholeWindowVisualEvidenceCoordinator : IWholeWindowVisualEvidenceNativeInspector
{
    private readonly MainWindow.AvaloniaVisualCaptureAdapter _access;
    private readonly WholeWindowVisualEvidenceHostCoordinator _coordinator;

    internal AvaloniaWholeWindowVisualEvidenceCoordinator(MainWindow.AvaloniaVisualCaptureAdapter access)
    {
        _access = access;
        _coordinator = new(new AvaloniaVisualEvidenceAppHost(access), this);
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
            "The production Avalonia rich-text input owns keyboard focus.");
    }

    WholeWindowVisualEvidenceProbeState IWholeWindowVisualEvidenceNativeInspector.CaptureSemanticState(
        WholeWindowVisualEvidenceScenario scenario)
    {
        var definition = FreeP.Ribbon.Definitions.FreePRibbon.Build(FreeP.Ribbon.Definitions.FreePRibbonCapabilities.Avalonia);
        var tabs = _access.RibbonTabs;
        var activeTabId = (tabs?.SelectedItem as TabItem)?.Tag as string ?? string.Empty;
        var visibleTabs = tabs?.Items.OfType<TabItem>()
            .Where(item => item.IsVisible)
            .Select(item => item.Tag as string ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToArray() ?? [];
        var contextualIds = definition.ContextualTabs.Select(tab => tab.Id).ToHashSet(StringComparer.Ordinal);
        var visibleContextualTabs = visibleTabs.Where(contextualIds.Contains).ToArray();
        var focus = DescribeFocus(_access.Window.FocusManager?.GetFocusedElement());
        var root = _access.ClientRoot;
        return new(
            "avalonia",
            _access.CurrentSlideIndex,
            _access.CurrentSlideTitle,
            _access.SelectedShapeIds,
            _access.SelectedShapeKind,
            activeTabId,
            visibleTabs,
            visibleContextualTabs,
            _access.IsBackstageOpen,
            _access.BackstagePaneLabel,
            focus.Role,
            focus.Label,
            _access.StatusText,
            _access.ShowGridlines,
            _access.ShowGuides,
            _access.ZoomMode,
            _access.ZoomPercent,
            _access.IsTitleBarVisible,
            _access.QuickAccessButtonCount,
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
            BoundsRelativeTo(_access.ClientRoot, state.ActiveVisual));
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

internal sealed class AvaloniaVisualEvidenceAppHost(MainWindow.AvaloniaVisualCaptureAdapter access)
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
