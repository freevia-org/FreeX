using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using FreeP.App.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.VisualEvidence.Avalonia;

internal sealed class AvaloniaWholeWindowVisualEvidenceCoordinator : IWholeWindowVisualEvidenceProbe
{
    private readonly MainWindow.AvaloniaVisualCaptureAdapter _access;
    private readonly WholeWindowVisualEvidenceHostCoordinator _coordinator;

    internal AvaloniaWholeWindowVisualEvidenceCoordinator(MainWindow.AvaloniaVisualCaptureAdapter access)
    {
        _access = access;
        _coordinator = new(this);
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

    void IWholeWindowVisualEvidenceProbe.LoadPresentation(Presentation presentation) =>
        _access.LoadPresentation(presentation);

    void IWholeWindowVisualEvidenceProbe.SelectSlide(int slideIndex) => _access.SelectSlide(slideIndex);

    void IWholeWindowVisualEvidenceProbe.SelectShape(uint shapeId) => _access.SelectShape(shapeId);

    void IWholeWindowVisualEvidenceProbe.ClearSelection() => _access.ClearSelection();

    void IWholeWindowVisualEvidenceProbe.HideCommentsPane() => _access.HideCommentsPane();

    void IWholeWindowVisualEvidenceProbe.SelectRibbonTab(string tabId) => _access.SelectRibbonTab(tabId);

    void IWholeWindowVisualEvidenceProbe.FocusNotes() => _access.FocusNotes();

    void IWholeWindowVisualEvidenceProbe.ShowBackstagePane(string paneId) => _access.ShowBackstagePane(paneId);

    void IWholeWindowVisualEvidenceProbe.ShowCommentsPane() => _access.ShowCommentsPane();

    void IWholeWindowVisualEvidenceProbe.SelectFirstComment() => _access.SelectFirstComment();

    void IWholeWindowVisualEvidenceProbe.ShowAccessibilityPane() => _access.ShowAccessibilityPane();

    void IWholeWindowVisualEvidenceProbe.SelectFirstAccessibilityIssue() => _access.SelectFirstAccessibilityIssue();

    void IWholeWindowVisualEvidenceProbe.ShowAltTextPane() => _access.ShowAltTextPane();

    void IWholeWindowVisualEvidenceProbe.ShowReadingOrderPane() => _access.ShowReadingOrderPane();

    void IWholeWindowVisualEvidenceProbe.ShowProofingPane() => _access.ShowProofingPane();

    void IWholeWindowVisualEvidenceProbe.SelectFirstProofingIssue() => _access.SelectFirstProofingIssue();

    void IWholeWindowVisualEvidenceProbe.ShowMediaCaptionPane() => _access.ShowMediaCaptionPane();

    void IWholeWindowVisualEvidenceProbe.ShowSmartArtTextPane() => _access.ShowSmartArtTextPane();

    void IWholeWindowVisualEvidenceProbe.EnsureAnimationPaneVisible() => _access.EnsureAnimationPaneVisible();

    bool IWholeWindowVisualEvidenceProbe.SetViewShowState(bool showGridlines, bool showGuides) =>
        _access.SetViewShowState(showGridlines, showGuides);

    void IWholeWindowVisualEvidenceProbe.SetZoom(PresentationViewZoomState state) => _access.SetZoom(state);

    void IWholeWindowVisualEvidenceProbe.RefreshWholeWindow() => _access.RefreshWholeWindow();

    void IWholeWindowVisualEvidenceProbe.NormalizeShell() => _access.NormalizeShell();

    WholeWindowVisualEvidenceBaselineState IWholeWindowVisualEvidenceProbe.CaptureBaselineState() => new(
        _access.SlideCount,
        _access.CurrentSlideIndex,
        _access.SelectedShapeIds);

    WholeWindowVisualEvidenceRichEditorPreparationState IWholeWindowVisualEvidenceProbe.PrepareRichEditor(
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

    WholeWindowVisualEvidenceProbeState IWholeWindowVisualEvidenceProbe.CaptureSemanticState(
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

    WholeWindowVisualEvidenceRichEditorProbeState IWholeWindowVisualEvidenceProbe.CaptureRichEditorState()
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
