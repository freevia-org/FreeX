using FreeP.VisualEvidence;

namespace FreeP.App.Compositor.Tests;

public sealed class WholeWindowVisualEvidenceHostCoordinatorTests
{
    [Fact]
    public void Prepare_routes_auxiliary_activation_in_portable_order()
    {
        var fixture = DialogPaneVisualEvidenceFixtureFactory.Create();
        var probe = new FakeProbe();
        var coordinator = new WholeWindowVisualEvidenceHostCoordinator(probe);

        var assertions = coordinator.Prepare(
            WholeWindowVisualEvidenceCatalog.Get("review.comments-pane"),
            fixture);

        probe.Calls.Should().Equal(
            "LoadPresentation:3",
            "SelectSlide:0",
            $"SelectShape:{fixture.TextShapeId}",
            "HideCommentsPane",
            "SelectRibbonTab:home",
            "ShowCommentsPane",
            "SelectFirstComment",
            "RefreshWholeWindow",
            "NormalizeShell",
            "CaptureBaselineState");
        assertions.Should().OnlyContain(assertion => assertion.Passed);
    }

    [Fact]
    public void Prepare_routes_view_state_after_native_refresh()
    {
        var fixture = DialogPaneVisualEvidenceFixtureFactory.Create();
        var probe = new FakeProbe();
        var coordinator = new WholeWindowVisualEvidenceHostCoordinator(probe);

        coordinator.Prepare(WholeWindowVisualEvidenceCatalog.Get("view.zoom-fit"), fixture);

        probe.Calls.Should().Equal(
            "LoadPresentation:3",
            "SelectSlide:0",
            $"SelectShape:{fixture.ChartShapeId}",
            "HideCommentsPane",
            "SelectRibbonTab:view",
            "RefreshWholeWindow",
            "SetZoom:FitToWindow:100",
            "NormalizeShell",
            "CaptureBaselineState");
    }

    [Fact]
    public void CaptureSemantic_assembles_native_observations_and_portable_assertions()
    {
        var fixture = DialogPaneVisualEvidenceFixtureFactory.Create();
        var scenario = WholeWindowVisualEvidenceCatalog.Get("backstage.print");
        var probe = new FakeProbe
        {
            SemanticState = FakeProbe.CreateSemanticState() with
            {
                BackstageOpen = true,
                BackstagePaneLabel = "Print",
                FocusedRole = "button",
                FocusedLabel = "Print",
            },
        };
        var coordinator = new WholeWindowVisualEvidenceHostCoordinator(probe);
        var preparationAssertions = coordinator.Prepare(scenario, fixture);

        var semantic = coordinator.CaptureSemantic(scenario, preparationAssertions);

        semantic.Host.Should().Be("fake");
        semantic.BackstageOpen.Should().BeTrue();
        semantic.BackstagePane.Should().Be("Print");
        semantic.FocusedRole.Should().Be("button");
        semantic.AppIconIdentity.Should().Be("shared-shell:FreeP");
        semantic.TitleBarBounds.Should().Be(new WholeWindowVisualEvidenceBounds(1, 2, 3, 4));
        semantic.Assertions.Should().OnlyContain(assertion => assertion.Passed);
        semantic.Assertions.Should().ContainSingle(assertion => assertion.Id == "backstage-pane-activated");
        probe.Calls.Should().Contain("CaptureSemanticState:backstage.print");
    }

    private sealed class FakeProbe : IWholeWindowVisualEvidenceProbe
    {
        private int _slideCount = 1;
        private int _currentSlideIndex;
        private readonly List<uint> _selectedShapeIds = [];

        internal List<string> Calls { get; } = [];
        internal WholeWindowVisualEvidenceProbeState SemanticState { get; init; } = CreateSemanticState();

        public void LoadPresentation(Presentation presentation)
        {
            _slideCount = presentation.Slides.Count;
            Calls.Add($"LoadPresentation:{_slideCount}");
        }

        public void SelectSlide(int slideIndex)
        {
            _currentSlideIndex = slideIndex;
            Calls.Add($"SelectSlide:{slideIndex}");
        }

        public void SelectShape(uint shapeId)
        {
            _selectedShapeIds.Clear();
            _selectedShapeIds.Add(shapeId);
            Calls.Add($"SelectShape:{shapeId}");
        }

        public void ClearSelection()
        {
            _selectedShapeIds.Clear();
            Calls.Add("ClearSelection");
        }

        public void HideCommentsPane() => Calls.Add("HideCommentsPane");
        public void SelectRibbonTab(string tabId) => Calls.Add($"SelectRibbonTab:{tabId}");
        public void FocusNotes() => Calls.Add("FocusNotes");
        public void ShowBackstagePane(string paneId) => Calls.Add($"ShowBackstagePane:{paneId}");
        public void ShowCommentsPane() => Calls.Add("ShowCommentsPane");
        public void SelectFirstComment() => Calls.Add("SelectFirstComment");
        public void ShowAccessibilityPane() => Calls.Add("ShowAccessibilityPane");
        public void SelectFirstAccessibilityIssue() => Calls.Add("SelectFirstAccessibilityIssue");
        public void ShowAltTextPane() => Calls.Add("ShowAltTextPane");
        public void ShowReadingOrderPane() => Calls.Add("ShowReadingOrderPane");
        public void ShowProofingPane() => Calls.Add("ShowProofingPane");
        public void SelectFirstProofingIssue() => Calls.Add("SelectFirstProofingIssue");
        public void ShowMediaCaptionPane() => Calls.Add("ShowMediaCaptionPane");
        public void ShowSmartArtTextPane() => Calls.Add("ShowSmartArtTextPane");
        public void EnsureAnimationPaneVisible() => Calls.Add("EnsureAnimationPaneVisible");

        public bool SetViewShowState(bool showGridlines, bool showGuides)
        {
            Calls.Add($"SetViewShowState:{showGridlines}:{showGuides}");
            return true;
        }

        public void SetZoom(PresentationViewZoomState state) =>
            Calls.Add($"SetZoom:{state.Mode}:{state.ZoomPercent}");

        public void RefreshWholeWindow() => Calls.Add("RefreshWholeWindow");
        public void NormalizeShell() => Calls.Add("NormalizeShell");

        public WholeWindowVisualEvidenceBaselineState CaptureBaselineState()
        {
            Calls.Add("CaptureBaselineState");
            return new(_slideCount, _currentSlideIndex, _selectedShapeIds.ToArray());
        }

        public WholeWindowVisualEvidenceRichEditorPreparationState PrepareRichEditor(
            WholeWindowVisualEvidenceRichEditorPlan plan)
        {
            Calls.Add("PrepareRichEditor");
            return new(true, plan.ShapeId, true, plan.ExpectedSelectedText, true, plan.ExpectedRunCount, "Fake focus.");
        }

        public WholeWindowVisualEvidenceProbeState CaptureSemanticState(
            WholeWindowVisualEvidenceScenario scenario)
        {
            Calls.Add($"CaptureSemanticState:{scenario.Id}");
            return SemanticState with
            {
                CurrentSlideIndex = _currentSlideIndex,
                SelectedShapeIds = _selectedShapeIds.ToArray(),
            };
        }

        public WholeWindowVisualEvidenceRichEditorProbeState CaptureRichEditorState() =>
            new(true, DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectedText, new(10, 20, 30, 40));

        internal static WholeWindowVisualEvidenceProbeState CreateSemanticState() => new(
            "fake",
            0,
            "Quarterly update",
            [],
            "Text",
            "home",
            ["file", "home"],
            [],
            false,
            null,
            "",
            "",
            "Slide 1 / 3",
            false,
            false,
            "Percent",
            100,
            true,
            3,
            true,
            "Untitled - FreeP",
            new(1, 2, 3, 4),
            new(5, 6, 7, 8),
            new(9, 10, 11, 12),
            new(13, 14, 15, 16),
            new(17, 18, 19, 20),
            new(21, 22, 23, 24),
            []);
    }
}
