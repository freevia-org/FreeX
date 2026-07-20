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
                Editor.SelectedShapeIds.SequenceEqual([fixture.SelectionForRoute(scenario.RouteId)]),
                $"Selected shape ids: {string.Join(",", Editor.SelectedShapeIds)}."),
            new DialogPaneVisualEvidenceAssertion(
                "no-preselection-mutation",
                scenario.SurfaceKind != DialogPaneVisualEvidenceSurfaceKind.ChoiceOverlay ||
                    (Editor.CurrentSlide.Shapes.Count == beforeShapeCount &&
                     StringComparer.Ordinal.Equals(Editor.CurrentSlide.LayoutId, beforeLayout)),
                "Opening the choice overlay did not mutate shape count or layout."),
        ];
    }

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
