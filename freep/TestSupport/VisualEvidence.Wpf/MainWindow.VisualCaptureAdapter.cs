using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Free.Shared.Ribbon.Wpf;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

public sealed partial class MainWindow
{
    internal WpfVisualCaptureAdapter CreateVisualCaptureAdapter() => new(this);

    internal sealed class WpfVisualCaptureAdapter(MainWindow owner)
    {
    internal MainWindow Window => owner;
    internal EditingSession Editor => owner.Editor;
    internal FrameworkElement ClientRoot => owner.Content as FrameworkElement ?? owner;
    internal FrameworkElement TitleBar => owner._titleBar;
    internal FrameworkElement RibbonRoot =>
        (FrameworkElement?)Ancestors(owner._ribbonTabs).OfType<Border>().FirstOrDefault() ?? owner._ribbonTabs;
    internal FrameworkElement SlidePaneRoot => owner.SlidePaneHost;
    internal FrameworkElement CanvasRoot => owner._canvasHost;
    internal FrameworkElement NotesRoot => owner._notesBox;
    internal FrameworkElement StatusRoot =>
        (FrameworkElement?)Ancestors(owner._slideCountText).OfType<Border>().FirstOrDefault() ?? owner._slideCountText;
    internal TabControl RibbonTabs => owner._ribbonTabs;
    internal IReadOnlyList<uint> SelectedShapeIds => owner.Editor.SelectedShapeIds;
    internal int SlideCount => owner.Editor.Presentation.Slides.Count;
    internal int CurrentSlideIndex => owner.Editor.CurrentSlideIndex;
    internal int CurrentShapeCount => owner.Editor.CurrentSlide?.Shapes.Count ?? 0;
    internal string? CurrentLayoutId => owner.Editor.CurrentSlide?.LayoutId;
    internal string CurrentSlideTitle => owner.Editor.CurrentSlide?.Title ?? string.Empty;
    internal string SelectedShapeKind => owner.Editor.CurrentSlide?.Shapes
        .FirstOrDefault(shape => owner.Editor.SelectedShapeIds.Contains(shape.Id))?.Kind.ToString() ?? string.Empty;
    internal bool IsTablePickerVisible => owner.IsTablePickerVisible;
    internal bool IsLayoutPickerVisible => owner.IsLayoutPickerVisible;
    internal int TableChoiceCount => owner.LastTablePickerPlan?.Choices.Count ?? 0;
    internal int DefaultTableChoiceCount => owner.LastTablePickerPlan?.Choices.Count(choice => choice.IsDefault) ?? 0;
    internal int CurrentLayoutChoiceCount => owner.LastLayoutPickerPlan?.Choices.Count(choice => choice.Chrome.IsCurrent) ?? 0;
    internal int DisabledLayoutChoiceCount => owner.LastLayoutPickerPlan?.Choices.Count(choice => !choice.Chrome.IsEnabled) ?? 0;
    internal string? BackstagePaneLabel => owner._backstage.EvidencePaneLabel;
    internal string StatusText => owner._slideCountText.Text;
    internal bool ShowGridlines => owner._viewShowState.ShowGridlines;
    internal bool ShowGuides => owner._viewShowState.ShowGuides;
    internal string ZoomMode => owner._viewZoomState.Mode.ToString();
    internal int ZoomPercent => owner._viewZoomState.ZoomPercent;
    internal bool IsTitleBarVisible => owner._titleBar.IsVisible && owner._titleBar.ActualHeight > 0;
    internal bool HasIcon => owner.Icon is not null;
    internal string WindowTitle => owner.Title;

    internal DependencyObject DialogMetadataRoot(string routeId) => routeId switch
    {
        "startup.slide-pane" => owner.SlidePaneHost,
        "startup.notes-pane" => owner._notesBox,
        "review.comments-pane" => owner._commentListHost,
        "review.accessibility-pane" => owner._accessibilityCheckerPaneHost,
        "review.alt-text-pane" => owner._altTextPaneHost,
        "review.reading-order-pane" => owner._readingOrderPaneHost,
        "review.proofing-pane" => owner._proofingPaneHost,
        "accessibility.media-caption-pane" => owner._mediaCaptionPaneHost,
        "context.smartart-text-pane" => owner._smartArtTextPaneHost,
        "animations.animation-pane" => owner._animPaneHost,
        "file.print-options" => owner._backstage.CurrentPaneContent ?? owner._backstage,
        "insert.table-picker" => owner._tablePickerHost,
        "design.layout-picker" => owner._layoutPickerHost,
        _ => owner,
    };

    internal void LoadPresentation(Presentation presentation) => owner.LoadModel(presentation);
    internal void SelectSlide(int slideIndex) => owner.Editor.SelectSlide(slideIndex);
    internal void SelectShape(uint shapeId) => owner.Editor.Select(shapeId);
    internal void ClearSelection() => owner.Editor.ClearSelection();
    internal void RefreshCanvas() => owner.RefreshCanvas();
    internal void HideCommentsPane() => owner.HideReviewCommentsPane();
    internal void ShowCommentsPane() => owner.ShowReviewCommentsPane();
    internal void SelectFirstComment() => owner._reviewWorkflowSession.SetSelectedReviewCommentIndex(0);
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
        if (owner._animPaneHost.Visibility != Visibility.Visible)
            owner.ToggleAnimationPane();
    }
    internal void ShowPrintOptionsPane() => owner._backstage.Show("Print");
    internal void ShowBackstagePane(string paneId) => owner._backstage.Show(paneId);
    internal void OpenTablePicker() => owner.OpenTablePicker();
    internal void OpenLayoutPicker() => owner.OpenLayoutPicker();
    internal void HideTablePicker() => owner.HideTablePicker();
    internal void HideLayoutPicker() => owner.HideLayoutPicker();
    internal void FocusNotes() => owner._notesBox.Focus();

    internal void RefreshWholeWindow()
    {
        owner.RefreshCanvas();
        owner.RefreshNotesPane();
        owner.UpdateSlideCount();
    }

    internal void NormalizeShell()
    {
        owner.Title = "Untitled \u2014 FreeP";
        owner._slideCountText.Text = $"Slide {CurrentSlideIndex + 1} / {SlideCount}";
    }

    internal bool SelectRibbonTab(string tabId)
    {
        if (StringComparer.Ordinal.Equals(tabId, "file"))
        {
            owner._ribbonTabs.SelectedIndex = 0;
            return true;
        }

        var definition = FreeP.Ribbon.Definitions.FreePRibbon.Build(FreeP.Ribbon.Definitions.FreePRibbonCapabilities.Wpf);
        var index = definition.Tabs.ToList().FindIndex(tab => StringComparer.Ordinal.Equals(tab.Id, tabId));
        if (index < 0)
            return false;
        owner._ribbonTabs.SelectedIndex = index + 1;
        return true;
    }

    internal bool SetViewShowState(bool showGridlines, bool showGuides)
    {
        var activated = true;
        if (owner._viewShowState.ShowGridlines != showGridlines)
            activated &= ExecuteRibbonCommand(PresentationViewShowPlanner.GridlinesCommandId);
        if (owner._viewShowState.ShowGuides != showGuides)
            activated &= ExecuteRibbonCommand(PresentationViewShowPlanner.GuidesCommandId);
        return activated && owner._viewShowState == new PresentationViewShowState(showGridlines, showGuides);
    }

    internal void SetZoom(PresentationViewZoomState state) => owner.ApplyPresentationViewZoomState(state);

    internal WpfVisualCaptureRichEditorState PrepareRichEditor(uint shapeId, int selectionStart, int selectionEnd)
    {
        var editor = owner.SlideCanvas.TextEditor;
        editor?.Activate(shapeId);
        var selectionSet = editor?.TrySelectTextRange(selectionStart, selectionEnd) == true;
        var body = owner.Editor.CurrentSlide?.Shapes.Single(shape => shape.Id == shapeId).TextBody;
        return new(
            editor?.IsActive == true,
            editor?.ActiveShapeId ?? 0,
            selectionSet,
            editor?.SelectedText ?? string.Empty,
            editor?.IsEditorFocused == true,
            body?.Paragraphs.SelectMany(paragraph => paragraph.Runs).Count() ?? 0,
            editor?.ActiveRichTextVisual as FrameworkElement);
    }

    internal WpfVisualCaptureRichEditorState CaptureRichEditor() => new(
        owner.SlideCanvas.TextEditor?.IsActive == true,
        owner.SlideCanvas.TextEditor?.ActiveShapeId ?? 0,
        false,
        owner.SlideCanvas.TextEditor?.SelectedText ?? string.Empty,
        owner.SlideCanvas.TextEditor?.IsEditorFocused == true,
        0,
        owner.SlideCanvas.TextEditor?.ActiveRichTextVisual as FrameworkElement);

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
        if (owner._animPaneHost.Visibility == Visibility.Visible) result.Add("animations.animation-pane");
        return result;
    }

    private bool ExecuteRibbonCommand(string commandId)
    {
        var button = VisualDescendants(owner._ribbonTabs)
            .OfType<ButtonBase>()
            .FirstOrDefault(candidate => StringComparer.Ordinal.Equals(RibbonMetadata.GetCommandName(candidate), commandId));
        if (button is null)
            return false;
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
        return true;
    }

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
}

internal sealed record WpfVisualCaptureRichEditorState(
    bool IsActive,
    uint ActiveShapeId,
    bool SelectionSet,
    string SelectedText,
    bool IsFocused,
    int RunCount,
    FrameworkElement? ActiveVisual);
