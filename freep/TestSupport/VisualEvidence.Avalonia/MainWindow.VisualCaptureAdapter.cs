using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Theme;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

public sealed partial class MainWindow
{
    internal AvaloniaVisualCaptureAdapter CreateVisualCaptureAdapter() => new(this);

    internal sealed class AvaloniaVisualCaptureAdapter(MainWindow owner)
    {
    internal MainWindow Window => owner;
    internal EditingSession Editor => owner.Editor;
    internal Visual ClientRoot => owner.Content as Visual ?? owner;
    internal Visual TitleBar => owner._titleBar;
    internal Visual? RibbonRoot => owner._ribbonControl?.GetVisualAncestors().OfType<Border>().FirstOrDefault() ?? owner._ribbonControl;
    internal Visual SlidePaneRoot => owner._slidePaneList.Parent?.Parent as Visual ?? owner._slidePaneList;
    internal Visual CanvasRoot => owner._canvasHost;
    internal Visual NotesRoot => owner._notesBox;
    internal Visual StatusRoot =>
        (Visual?)owner._statusText.GetVisualAncestors().OfType<Border>().FirstOrDefault() ?? owner._statusText;
    internal TabControl? RibbonTabs => owner._ribbonControl is null
        ? null
        : owner._ribbonControl.GetVisualDescendants().Prepend(owner._ribbonControl).OfType<TabControl>().FirstOrDefault();
    internal IReadOnlyList<uint> SelectedShapeIds => owner.Editor.SelectedShapeIds;
    internal int SlideCount => owner.Editor.Presentation.Slides.Count;
    internal int CurrentSlideIndex => owner.Editor.CurrentSlideIndex;
    internal int CurrentShapeCount => owner.Editor.CurrentSlide?.Shapes.Count ?? 0;
    internal string? CurrentLayoutId => owner.Editor.CurrentSlide?.LayoutId;
    internal string CurrentSlideTitle => owner.Editor.CurrentSlide?.Title ?? string.Empty;
    internal string SelectedShapeKind => owner.Editor.CurrentSlide is { } slide
        ? SlideShapeTraversal.EnumerateDepthFirst(slide)
            .FirstOrDefault(shape => owner.Editor.SelectedShapeIds.Contains(shape.Id))?.Kind.ToString() ?? string.Empty
        : string.Empty;
    internal bool IsTablePickerVisible => owner.IsTablePickerVisible;
    internal bool IsLayoutPickerVisible => owner.IsLayoutPickerVisible;
    internal int TableChoiceCount => owner.LastTablePickerPlan?.Choices.Count ?? 0;
    internal int DefaultTableChoiceCount => owner.LastTablePickerPlan?.Choices.Count(choice => choice.IsDefault) ?? 0;
    internal int CurrentLayoutChoiceCount => owner.LastLayoutPickerPlan?.Choices.Count(choice => choice.Chrome.IsCurrent) ?? 0;
    internal int DisabledLayoutChoiceCount => owner.LastLayoutPickerPlan?.Choices.Count(choice => !choice.Chrome.IsEnabled) ?? 0;
    internal bool IsBackstageOpen => owner._backstage.IsOpen;
    internal string? BackstagePaneLabel => owner._backstage.CurrentPaneLabel;
    internal string StatusText => owner._statusText.Text ?? string.Empty;
    internal bool ShowGridlines => owner._viewShowState.ShowGridlines;
    internal bool ShowGuides => owner._viewShowState.ShowGuides;
    internal string ZoomMode => owner._viewZoomState.Mode.ToString();
    internal int ZoomPercent => owner._viewZoomState.ZoomPercent;
    internal bool IsTitleBarVisible => owner._titleBar.IsVisible && owner._titleBar.Bounds.Height > 0;
    internal int QuickAccessButtonCount => owner._quickAccessButtons.Count;
    internal bool HasIcon => owner.Icon is not null;
    internal string WindowTitle => owner.Title ?? string.Empty;

    internal Visual DialogMetadataRoot(string routeId) => routeId switch
    {
        "startup.slide-pane" => owner._slidePaneList.Parent?.Parent as Visual ?? owner._slidePaneList,
        "startup.notes-pane" => owner._notesBox,
        "review.comments-pane" => owner._reviewCommentsPaneHost,
        "review.accessibility-pane" => owner._accessibilityCheckerPaneHost,
        "review.alt-text-pane" => owner._altTextPaneHost,
        "review.reading-order-pane" => owner._readingOrderPaneHost,
        "review.proofing-pane" => owner._proofingPaneHost,
        "accessibility.media-caption-pane" => owner._mediaCaptionPaneHost,
        "context.smartart-text-pane" => owner._smartArtTextPaneHost,
        "animations.animation-pane" => owner._animationPaneHost,
        "file.print-options" => owner._printOptionsPaneHost,
        "insert.table-picker" => owner._tablePickerHost,
        "design.layout-picker" => owner._layoutPickerHost,
        _ => owner,
    };

    internal void LoadPresentation(Presentation presentation) => owner.LoadPresentationContent(presentation);
    internal void SelectSlide(int slideIndex) => owner.Editor.SelectSlide(slideIndex);
    internal void SelectShape(uint shapeId) => owner.Editor.Select(shapeId);
    internal void ClearSelection() => owner.Editor.ClearSelection();
    internal void RefreshCanvas() => owner.RefreshCanvas();
    internal void HideCommentsPane() => owner.HideReviewCommentsPane();
    internal void ShowCommentsPane() => owner.ShowReviewCommentsPane();
    internal void SelectFirstComment() => owner.SetSelectedReviewCommentIndexForTests(0);
    internal void ShowAccessibilityPane() => owner.ShowAccessibilityCheckerPane();
    internal void SelectFirstAccessibilityIssue()
    {
        if (owner.AccessibilityCheckerPaneRowCount > 0)
            owner.SelectAccessibilityCheckerRow(0);
    }
    internal void ShowAltTextPane() => owner.ShowAltTextPane();
    internal void ShowReadingOrderPane() => owner.ShowReadingOrderPane();
    internal void ShowProofingPane() => owner.ShowProofingPane();
    internal void SelectFirstProofingIssue()
    {
        if (owner.ProofingPaneIssueRowCount > 0)
            owner.SelectProofingIssueRow(0);
    }
    internal void ShowMediaCaptionPane() => owner.ShowMediaCaptionPane();
    internal void ShowSmartArtTextPane() => owner.ShowSmartArtTextPane();
    internal void EnsureAnimationPaneVisible()
    {
        if (!owner.IsAnimationPaneVisible)
            owner.ShowAnimationPane();
    }
    internal void ShowPrintOptionsPane() => owner.ShowPrintOptionsPane();
    internal void ShowBackstagePane(string paneId)
    {
        owner._backstage.Show();
        owner._backstage.TryActivateEntry(paneId);
    }
    internal void OpenTablePicker() => owner.OpenTablePicker();
    internal void OpenLayoutPicker()
    {
        owner.LastLayoutPickerPlan = PresentationDesignCommandPlanner.BuildLayoutPickerPlan(
            owner._presentation,
            owner.Editor.CurrentSlideIndex);
        owner.ShowLayoutPicker(owner.LastLayoutPickerPlan);
    }
    internal void HideTablePicker() => owner.HideTablePicker();
    internal void HideLayoutPicker() => owner.HideLayoutPicker();
    internal void FocusNotes() => owner._notesBox.Focus();

    internal void RefreshWholeWindow()
    {
        owner.RefreshCanvas();
        owner.RefreshNotesPane();
        owner.UpdateStatus();
    }

    internal void NormalizeShell()
    {
        owner.Title = "Untitled \u2014 FreeP";
        owner._statusText.Text = $"Slide {CurrentSlideIndex + 1} / {SlideCount}";
    }

    internal bool SelectRibbonTab(string tabId)
    {
        var tabs = RibbonTabs;
        var item = tabs?.Items.OfType<TabItem>()
            .FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Tag as string, tabId));
        if (tabs is null || item is null)
            return false;
        tabs.SelectedItem = item;
        return true;
    }

    internal bool SetViewShowState(bool showGridlines, bool showGuides)
    {
        var registry = owner.BuildCommandRegistry();
        var activated = true;
        if (owner._viewShowState.ShowGridlines != showGridlines)
            activated &= ExecuteRibbonCommand(registry, PresentationViewShowPlanner.GridlinesCommandId);
        if (owner._viewShowState.ShowGuides != showGuides)
            activated &= ExecuteRibbonCommand(registry, PresentationViewShowPlanner.GuidesCommandId);
        if (owner._ribbonControl is not null)
            AvaloniaRibbonRenderer.SyncToggleStates(owner._ribbonControl, registry, RibbonVisualPalette.FromTheme(App.ActiveTheme));
        return activated && owner._viewShowState == new PresentationViewShowState(showGridlines, showGuides);
    }

    internal void SetZoom(PresentationViewZoomState state) => owner.ApplyPresentationViewZoomState(state);

    internal AvaloniaVisualCaptureRichEditorState PrepareRichEditor(uint shapeId, int selectionStart, int selectionEnd)
    {
        owner._textEditor?.Activate(shapeId);
        var selectionSet = owner._textEditor?.TrySelectTextRange(selectionStart, selectionEnd) == true;
        var body = owner.Editor.CurrentSlide?.Shapes.Single(shape => shape.Id == shapeId).TextBody;
        return new(
            owner._textEditor?.IsActive == true,
            owner._textEditor?.ActiveShapeId ?? 0,
            selectionSet,
            owner._textEditor?.SelectedText ?? string.Empty,
            owner._textEditor?.IsEditorFocused == true,
            body?.Paragraphs.SelectMany(paragraph => paragraph.Runs).Count() ?? 0,
            owner._textEditor?.ActiveRichTextVisual);
    }

    internal AvaloniaVisualCaptureRichEditorState CaptureRichEditor() => new(
        owner._textEditor?.IsActive == true,
        owner._textEditor?.ActiveShapeId ?? 0,
        false,
        owner._textEditor?.SelectedText ?? string.Empty,
        owner._textEditor?.IsEditorFocused == true,
        0,
        owner._textEditor?.ActiveRichTextVisual);

    internal IReadOnlyList<string> VisibleAuxiliaryPanes()
    {
        var result = new List<string>();
        if (owner.IsReviewCommentsPaneVisible) result.Add("review.comments-pane");
        if (owner.IsAccessibilityCheckerPaneVisible) result.Add("review.accessibility-pane");
        if (owner.IsAltTextPaneVisible) result.Add("review.alt-text-pane");
        if (owner.IsReadingOrderPaneVisible) result.Add("review.reading-order-pane");
        if (owner.IsProofingPaneVisible) result.Add("review.proofing-pane");
        if (owner.IsMediaCaptionPaneVisible) result.Add("accessibility.media-caption-pane");
        if (owner.IsSmartArtTextPaneVisible) result.Add("context.smartart-text-pane");
        if (owner.IsAnimationPaneVisible) result.Add("animations.animation-pane");
        return result;
    }

    private static bool ExecuteRibbonCommand(RibbonCommandRegistry registry, string commandId)
    {
        if (!registry.TryGet(new RibbonCommandId(commandId), out var command) || command is null)
            return false;
        command.Execute(RibbonCommandContext.Empty);
        return true;
    }
    }
}

internal sealed record AvaloniaVisualCaptureRichEditorState(
    bool IsActive,
    uint ActiveShapeId,
    bool SelectionSet,
    string SelectedText,
    bool IsFocused,
    int RunCount,
    Visual? ActiveVisual);
