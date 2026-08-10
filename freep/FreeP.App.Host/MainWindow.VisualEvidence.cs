using System.Windows;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

public sealed partial class MainWindow
{
    internal IDialogPaneVisualEvidenceRouteHost CreateDialogPaneVisualEvidenceRouteHost() =>
        new WpfDialogPaneVisualEvidenceRouteHost(this);

    internal DependencyObject DialogPaneVisualEvidenceMetadataRoot(DialogPaneVisualEvidenceScenario scenario) =>
        scenario.RouteId switch
        {
            "startup.slide-pane" => SlidePaneHost,
            "startup.notes-pane" => _notesBox,
            "review.comments-pane" => _commentListHost,
            "review.accessibility-pane" => _accessibilityCheckerPaneHost,
            "review.alt-text-pane" => _altTextPaneHost,
            "review.reading-order-pane" => _readingOrderPaneHost,
            "review.proofing-pane" => _proofingPaneHost,
            "accessibility.media-caption-pane" => _mediaCaptionPaneHost,
            "context.smartart-text-pane" => _smartArtTextPaneHost,
            "animations.animation-pane" => _animPaneHost,
            "file.print-options" => _backstage.CurrentPaneContent ?? _backstage,
            "insert.table-picker" => _tablePickerHost,
            "design.layout-picker" => _layoutPickerHost,
            _ => this,
        };

    private sealed class WpfDialogPaneVisualEvidenceRouteHost(MainWindow owner)
        : IDialogPaneVisualEvidenceRouteHost
    {
        public IReadOnlyList<uint> SelectedShapeIds => owner.Editor.SelectedShapeIds;
        public int SlideCount => owner.Editor.Presentation.Slides.Count;
        public int CurrentShapeCount => owner.Editor.CurrentSlide?.Shapes.Count ?? 0;
        public string? CurrentLayoutId => owner.Editor.CurrentSlide?.LayoutId;
        public bool IsTablePickerVisible => owner.IsTablePickerVisible;
        public bool IsLayoutPickerVisible => owner.IsLayoutPickerVisible;

        public DialogPaneVisualEvidenceChoiceState ChoiceState => new(
            owner.LastTablePickerPlan?.Choices.Count ?? 0,
            owner.LastTablePickerPlan?.Choices.Count(choice => choice.IsDefault) ?? 0,
            owner.LastLayoutPickerPlan?.Choices.Count(choice => choice.Chrome.IsCurrent) ?? 0,
            owner.LastLayoutPickerPlan?.Choices.Count(choice => !choice.Chrome.IsEnabled) ?? 0);

        public void LoadPresentation(FreeP.Core.Model.Presentation presentation) =>
            owner.LoadModel(presentation);

        public void SelectShape(uint shapeId) => owner.Editor.Select(shapeId);
        public void RefreshCanvas() => owner.RefreshCanvas();
        public void ShowReviewCommentsPane() => owner.ShowReviewCommentsPane();
        public void SelectFirstReviewComment() => owner.SetSelectedReviewCommentIndexForTests(0);
        public void ShowAccessibilityCheckerPane() => owner.ShowAccessibilityCheckerPane();

        public void SelectFirstAccessibilityIssue()
        {
            if (owner.AccessibilityCheckerPaneRowCount > 0)
                owner.SelectAccessibilityCheckerRow(0);
        }

        public void ShowAltTextPane() => owner.ShowAltTextPane();
        public void ShowReadingOrderPane() => owner.ShowReadingOrderPane();
        public void ShowProofingPane() => owner.ShowProofingPane();

        public void SelectFirstProofingIssue()
        {
            if (owner.ProofingPaneIssueRowCount > 0)
                owner.SelectProofingIssueRow(0);
        }

        public void ShowMediaCaptionPane() => owner.ShowMediaCaptionPane();
        public void ShowSmartArtTextPane() => owner.ShowSmartArtTextPane();

        public void EnsureAnimationPaneVisible()
        {
            if (owner._animPaneHost?.Visibility != Visibility.Visible)
                owner.ToggleAnimationPane();
        }

        public void ShowPrintOptionsPane() => owner._backstage.Show("Print");
        public void OpenTablePicker() => owner.OpenTablePicker();
        public void OpenLayoutPicker() => owner.OpenLayoutPicker();
        public void HideTablePicker() => owner.HideTablePicker();
        public void HideLayoutPicker() => owner.HideLayoutPicker();
    }
}
