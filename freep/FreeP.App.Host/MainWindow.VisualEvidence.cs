using System.Windows;
using FreeP.App.Compositor;

namespace FreeP.App.Host;

public sealed partial class MainWindow
{
    internal IReadOnlyList<DialogPaneVisualEvidenceAssertion> PrepareDialogPaneVisualEvidence(
        DialogPaneVisualEvidenceScenario scenario,
        DialogPaneVisualEvidenceFixture fixture)
    {
        LoadModel(fixture.Presentation);
        Editor.Select(fixture.SelectionForRoute(scenario.RouteId));
        RefreshCanvas();

        var seededSelection = Editor.SelectedShapeIds.ToArray();
        var beforeShapeCount = Editor.CurrentSlide!.Shapes.Count;
        var beforeLayout = Editor.CurrentSlide.LayoutId;
        switch (scenario.RouteId)
        {
            case "review.comments-pane":
                ShowReviewCommentsPane();
                SetSelectedReviewCommentIndexForTests(0);
                break;
            case "review.accessibility-pane":
                ShowAccessibilityCheckerPane();
                if (AccessibilityCheckerPaneRowCount > 0)
                    SelectAccessibilityCheckerRow(0);
                break;
            case "review.alt-text-pane":
                ShowAltTextPane();
                break;
            case "review.reading-order-pane":
                ShowReadingOrderPane();
                break;
            case "review.proofing-pane":
                ShowProofingPane();
                if (ProofingPaneIssueRowCount > 0)
                    SelectProofingIssueRow(0);
                break;
            case "accessibility.media-caption-pane":
                ShowMediaCaptionPane();
                break;
            case "context.smartart-text-pane":
                ShowSmartArtTextPane();
                break;
            case "animations.animation-pane":
                if (_animPaneHost?.Visibility != Visibility.Visible)
                    ToggleAnimationPane();
                break;
            case "file.print-options":
                _backstage.Show("Print");
                break;
            case "insert.table-picker":
                OpenTablePicker();
                break;
            case "design.layout-picker":
                OpenLayoutPicker();
                break;
        }

        return
        [
            new DialogPaneVisualEvidenceAssertion(
                "seeded-presentation",
                Editor.Presentation.Slides.Count == 3,
                $"Loaded {Editor.Presentation.Slides.Count} seeded slides."),
            new DialogPaneVisualEvidenceAssertion(
                "seeded-selection",
                seededSelection.SequenceEqual([fixture.SelectionForRoute(scenario.RouteId)]),
                $"Initially selected shape ids: {string.Join(",", seededSelection)}."),
            new DialogPaneVisualEvidenceAssertion(
                "no-preselection-mutation",
                scenario.SurfaceKind != DialogPaneVisualEvidenceSurfaceKind.ChoiceOverlay ||
                    (Editor.CurrentSlide.Shapes.Count == beforeShapeCount &&
                     StringComparer.Ordinal.Equals(Editor.CurrentSlide.LayoutId, beforeLayout)),
                "Opening the choice overlay did not mutate shape count or layout."),
            new DialogPaneVisualEvidenceAssertion(
                "choice-state",
                scenario.RouteId switch
                {
                    "insert.table-picker" => LastTablePickerPlan is { Choices.Count: 25 } &&
                        LastTablePickerPlan.Choices.Count(choice => choice.IsDefault) == 1,
                    "design.layout-picker" => LastLayoutPickerPlan is not null &&
                        LastLayoutPickerPlan.Choices.Count(choice => choice.Chrome.IsCurrent) == 1 &&
                        LastLayoutPickerPlan.Choices.Count(choice => !choice.Chrome.IsEnabled) == 1,
                    _ => true,
                },
                "The picker exposes its expected default/current/disabled choice state."),
        ];
    }

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

    internal IReadOnlyList<DialogPaneVisualEvidenceAssertion> CompleteDialogPaneVisualEvidence(
        DialogPaneVisualEvidenceScenario scenario)
    {
        if (scenario.RouteId == "insert.table-picker")
            HideTablePicker();
        else if (scenario.RouteId == "design.layout-picker")
            HideLayoutPicker();

        return scenario.SurfaceKind == DialogPaneVisualEvidenceSurfaceKind.ChoiceOverlay
            ?
            [
                new DialogPaneVisualEvidenceAssertion(
                    "dismissal",
                    !IsTablePickerVisible && !IsLayoutPickerVisible,
                    "Choice overlay is hidden after dismissal."),
            ]
            : [];
    }
}
