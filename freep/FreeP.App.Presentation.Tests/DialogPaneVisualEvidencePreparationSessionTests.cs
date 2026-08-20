using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class DialogPaneVisualEvidencePreparationSessionTests
{
    [Fact]
    public void Session_owns_fixture_route_assertions_and_completion_order()
    {
        var session = DialogPaneVisualEvidencePreparationSession.Create(
            DialogPaneVisualEvidenceCatalog.Get("review.comments-pane.seeded"));
        var host = new RecordingRouteHost();
        var adapter = new RecordingDialogAdapter();

        var assertions = session.PrepareRoute(host).ToList();

        session.Stage.Should().Be(DialogPaneVisualEvidencePreparationStage.RoutePrepared);
        host.CommentsShown.Should().BeTrue();
        host.FirstCommentSelected.Should().BeTrue();
        assertions.Should().OnlyContain(assertion => assertion.Passed);
        assertions.Select(assertion => assertion.Id).Should().Equal(
            "seeded-presentation",
            "seeded-selection",
            "no-preselection-mutation",
            "choice-state");

        session.PrepareLoadedDialogState<object>(null, adapter, assertions);
        session.CompleteRoute(host).Should().BeEmpty();
        session.Stage.Should().Be(DialogPaneVisualEvidencePreparationStage.Completed);
    }

    [Fact]
    public void Session_owns_choice_overlay_state_and_dismissal()
    {
        var session = DialogPaneVisualEvidencePreparationSession.Create(
            DialogPaneVisualEvidenceCatalog.Get("design.layout-picker.open"));
        var host = new RecordingRouteHost();
        var assertions = session.PrepareRoute(host).ToList();

        host.LayoutPickerVisible.Should().BeTrue();
        assertions.Single(assertion => assertion.Id == "choice-state").Passed.Should().BeTrue();
        assertions.Single(assertion => assertion.Id == "no-preselection-mutation").Passed.Should().BeTrue();

        session.PrepareLoadedDialogState<object>(null, new RecordingDialogAdapter(), assertions);
        var completion = session.CompleteRoute(host);

        host.LayoutPickerVisible.Should().BeFalse();
        completion.Should().ContainSingle().Which.Should().Be(
            new DialogPaneVisualEvidenceAssertion(
                "dismissal",
                true,
                "Choice overlay is hidden after dismissal."));
    }

    [Fact]
    public void Session_owns_fixture_intent_and_before_show_dialog_validation()
    {
        var session = DialogPaneVisualEvidencePreparationSession.Create(
            DialogPaneVisualEvidenceCatalog.Get("slideshow.custom-shows.validation"));
        var host = new RecordingRouteHost();
        var adapter = new RecordingDialogAdapter();
        var assertions = session.PrepareRoute(host).ToList();

        session.Fixture.Presentation.CustomShows.Should().BeEmpty();
        var dialog = session.CreateDialog(adapter, assertions);

        adapter.CustomShowsCreated.Should().BeTrue();
        adapter.CustomShowsValidationPrepared.Should().BeTrue();
        session.PrepareLoadedDialogState(dialog, adapter, assertions);
        session.Stage.Should().Be(DialogPaneVisualEvidencePreparationStage.LoadedStatePrepared);
    }

    [Fact]
    public void Session_owns_after_load_validation_assertion_projection()
    {
        var session = DialogPaneVisualEvidencePreparationSession.Create(
            DialogPaneVisualEvidenceCatalog.Get("design.slide-size.invalid"));
        var host = new RecordingRouteHost();
        var adapter = new RecordingDialogAdapter
        {
            SlideSizeResult = new(false, "Width must be greater than zero."),
        };
        var assertions = session.PrepareRoute(host).ToList();
        var dialog = session.CreateDialog(adapter, assertions);

        session.PrepareLoadedDialogState(dialog, adapter, assertions);

        assertions.Should().Contain(new DialogPaneVisualEvidenceAssertion(
            "validation-visible",
            true,
            "Width must be greater than zero."));
    }

    private sealed class RecordingRouteHost : IVisualEvidenceAppHost
    {
        private Presentation _presentation = Presentation.CreateEmpty();
        private readonly List<uint> _selection = [];

        public IReadOnlyList<uint> SelectedShapeIds => _selection;
        public int SlideCount => _presentation.Slides.Count;
        public int CurrentSlideIndex { get; private set; }
        public int CurrentShapeCount => _presentation.Slides[0].Shapes.Count;
        public string? CurrentLayoutId => _presentation.Slides[0].LayoutId;
        public bool IsTablePickerVisible => TablePickerVisible;
        public bool IsLayoutPickerVisible => LayoutPickerVisible;
        public bool TablePickerVisible { get; private set; }
        public bool LayoutPickerVisible { get; private set; }
        public bool CommentsShown { get; private set; }
        public bool FirstCommentSelected { get; private set; }

        public DialogPaneVisualEvidenceChoiceState ChoiceState => new(
            TablePickerVisible ? 25 : 0,
            TablePickerVisible ? 1 : 0,
            LayoutPickerVisible ? 1 : 0,
            LayoutPickerVisible ? 1 : 0);

        public void LoadPresentation(Presentation presentation) => _presentation = presentation;
        public void SelectSlide(int slideIndex) => CurrentSlideIndex = slideIndex;

        public void SelectShape(uint shapeId)
        {
            _selection.Clear();
            _selection.Add(shapeId);
        }

        public void ClearSelection() => _selection.Clear();
        public void RefreshCanvas() { }
        public void RefreshWholeWindow() { }
        public void NormalizeShell() { }
        public void HideCommentsPane() => CommentsShown = false;
        public void ResetAuxiliaryPanes() { }
        public void HideBackstage() { }
        public bool SelectRibbonTab(string tabId) => true;
        public void FocusNotes() { }
        public void ShowBackstagePane(string paneId) { }
        public void ShowCommentsPane() => CommentsShown = true;
        public void SelectFirstComment() => FirstCommentSelected = true;
        public void ShowAccessibilityPane() { }
        public void SelectFirstAccessibilityIssue() { }
        public void ShowAltTextPane() { }
        public void ShowReadingOrderPane() { }
        public void ShowProofingPane() { }
        public void SelectFirstProofingIssue() { }
        public void ShowMediaCaptionPane() { }
        public void ShowSmartArtTextPane() { }
        public void EnsureAnimationPaneVisible() { }
        public void ShowPrintOptionsPane() { }
        public void OpenTablePicker() => TablePickerVisible = true;
        public void OpenLayoutPicker() => LayoutPickerVisible = true;
        public void HideTablePicker() => TablePickerVisible = false;
        public void HideLayoutPicker() => LayoutPickerVisible = false;
        public bool SetViewShowState(bool showGridlines, bool showGuides) => true;
        public void SetZoom(PresentationViewZoomState state) { }
    }

    private sealed class RecordingDialogAdapter : IDialogPaneVisualEvidenceDialogAdapter<object>
    {
        public bool CustomShowsCreated { get; private set; }
        public bool CustomShowsValidationPrepared { get; private set; }
        public DialogPaneVisualEvidenceValidationResult SlideSizeResult { get; init; } = new(true);

        public object CreateSlideSize(DialogPaneVisualEvidenceSlideSizePreparation preparation) => new();
        public object CreateHeaderFooter(DialogPaneVisualEvidenceHeaderFooterPreparation preparation) => new();
        public object CreateFindReplace(DialogPaneVisualEvidenceFindReplacePreparation preparation) => new();

        public object CreateHyperlink(
            DialogPaneVisualEvidenceHyperlinkPreparation preparation,
            DialogPaneVisualEvidenceFixture fixture) => new();

        public object CreateChartData(DialogPaneVisualEvidenceChartDataPreparation preparation) => new();

        public object CreateCustomShows(DialogPaneVisualEvidenceCustomShowsPreparation preparation)
        {
            CustomShowsCreated = true;
            return new object();
        }

        public bool ApplyHyperlinkValidation(
            object dialog,
            DialogPaneVisualEvidenceHyperlinkInput input) => false;

        public void PrepareCustomShowsValidation(object dialog) =>
            CustomShowsValidationPrepared = true;

        public DialogPaneVisualEvidenceValidationResult PrepareSlideSizeLoadedState(object dialog) =>
            SlideSizeResult;

        public DialogPaneVisualEvidenceValidationResult PrepareChartDataLoadedState(object dialog) =>
            new(true, "Enter a number.");
    }
}
