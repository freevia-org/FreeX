using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class WholeWindowVisualEvidenceContractTests
{
    [Fact]
    public void RichTextSelectionVisualContract_ExposesNativeAndRealizedSharedPalette()
    {
        InCanvasRichTextSelectionVisualContract.SelectionOpacity.Should().Be(0.4);
        (
            InCanvasRichTextSelectionVisualContract.BackgroundRed,
            InCanvasRichTextSelectionVisualContract.BackgroundGreen,
            InCanvasRichTextSelectionVisualContract.BackgroundBlue,
            InCanvasRichTextSelectionVisualContract.ForegroundRed,
            InCanvasRichTextSelectionVisualContract.ForegroundGreen,
            InCanvasRichTextSelectionVisualContract.ForegroundBlue)
            .Should().Be((0x00, 0x78, 0xD7, 0xFF, 0xFF, 0xFF));
        (
            InCanvasRichTextSelectionVisualContract.RealizedBackgroundRed,
            InCanvasRichTextSelectionVisualContract.RealizedBackgroundGreen,
            InCanvasRichTextSelectionVisualContract.RealizedBackgroundBlue,
            InCanvasRichTextSelectionVisualContract.RealizedForegroundRed,
            InCanvasRichTextSelectionVisualContract.RealizedForegroundGreen,
            InCanvasRichTextSelectionVisualContract.RealizedForegroundBlue)
            .Should().Be((0x99, 0xC9, 0xEF, 0x1C, 0x63, 0xB1));
    }

    [Fact]
    public void Catalog_defines_unique_complete_96_dpi_whole_window_matrix()
    {
        WholeWindowVisualEvidenceCatalog.All.Should().HaveCount(33);
        WholeWindowVisualEvidenceCatalog.All.Select(scenario => scenario.Id).Should().OnlyHaveUniqueItems();
        WholeWindowVisualEvidenceCatalog.LogicalClientWidth.Should().Be(1280);
        WholeWindowVisualEvidenceCatalog.LogicalClientHeight.Should().Be(760);
        WholeWindowVisualEvidenceCatalog.TargetDpi.Should().Be(96);

        WholeWindowVisualEvidenceCatalog.All.Count(scenario => scenario.Kind == WholeWindowVisualEvidenceScenarioKind.Startup).Should().Be(2);
        WholeWindowVisualEvidenceCatalog.All.Count(scenario => scenario.Kind == WholeWindowVisualEvidenceScenarioKind.StaticRibbonTab).Should().Be(6);
        WholeWindowVisualEvidenceCatalog.All.Count(scenario => scenario.Kind == WholeWindowVisualEvidenceScenarioKind.BackstagePane).Should().Be(7);
        WholeWindowVisualEvidenceCatalog.All.Count(scenario => scenario.Kind is WholeWindowVisualEvidenceScenarioKind.StatusBar or WholeWindowVisualEvidenceScenarioKind.ViewState).Should().Be(5);
        WholeWindowVisualEvidenceCatalog.All.Count(scenario => scenario.Kind == WholeWindowVisualEvidenceScenarioKind.WorkspaceRegion).Should().Be(3);
        WholeWindowVisualEvidenceCatalog.All.Count(scenario => scenario.Kind == WholeWindowVisualEvidenceScenarioKind.AuxiliaryPane).Should().Be(8);
        WholeWindowVisualEvidenceCatalog.All.Count(scenario => scenario.Kind == WholeWindowVisualEvidenceScenarioKind.RichEditorOverlay).Should().Be(2);
        WholeWindowVisualEvidenceCatalog.Get("editor.rich-text-selection").ActivationId.Should().Be("selection");
        WholeWindowVisualEvidenceCatalog.Get("editor.rich-text-caret").ActivationId.Should().Be("caret");
        DialogPaneVisualEvidenceFixtureFactory.CreateRichEditorBody().Wrap.Should().BeFalse(
            "the deterministic mixed-font pair uses the shared no-wrap editor path");
    }

    [Fact]
    public void Rich_editor_fixture_has_mixed_runs_and_stable_selection_offsets()
    {
        var body = DialogPaneVisualEvidenceFixtureFactory.CreateRichEditorBody();
        var text = InCanvasTextEditPlanner.ExtractPlainText(body);

        body.Paragraphs.SelectMany(paragraph => paragraph.Runs).Should().HaveCount(3);
        text[DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectionStart..
            DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectionEnd]
            .Should().Be(DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectedText);
        DialogPaneVisualEvidenceFixtureFactory.RichEditorCaretPosition.Should().BeInRange(0, text.Length);
    }

    [Fact]
    public void Catalog_does_not_invent_contextual_tabs_absent_from_the_product_ribbon()
    {
        WholeWindowVisualEvidenceCatalog.All
            .Should().NotContain(scenario => !string.IsNullOrWhiteSpace(scenario.ExpectedContextualTabId));
        WholeWindowVisualEvidenceCatalog.All.Select(scenario => scenario.Id)
            .Should().NotContain("status.slide-1");
    }

    [Fact]
    public void Preparation_plan_preserves_clean_startup_without_loading_seeded_fixture()
    {
        var fixture = DialogPaneVisualEvidenceFixtureFactory.Create();

        var plan = WholeWindowVisualEvidencePreparationSession.Prepare(
            WholeWindowVisualEvidenceCatalog.Get("startup.slide"),
            fixture);

        plan.LoadFixturePresentation.Should().BeFalse();
        plan.ExpectedSlideCount.Should().Be(1);
        plan.SlideIndex.Should().Be(0);
        plan.SelectionShapeId.Should().Be(0);
        plan.ActiveRibbonTabId.Should().Be(WholeWindowVisualEvidencePreparationSession.DefaultRibbonTabId);
        plan.Activation.Should().Be(new WholeWindowVisualEvidenceActivation(
            WholeWindowVisualEvidenceActivationKind.None));
        plan.RichEditor.Should().BeNull();
    }

    [Theory]
    [InlineData("startup.notes", WholeWindowVisualEvidenceActivationKind.FocusNotesPane, "notes")]
    [InlineData("workspace.notes-pane", WholeWindowVisualEvidenceActivationKind.FocusNotesPane, "notes-pane")]
    [InlineData("backstage.info", WholeWindowVisualEvidenceActivationKind.BackstagePane, "Info")]
    [InlineData("review.comments-pane", WholeWindowVisualEvidenceActivationKind.ReviewCommentsPane, "comments")]
    [InlineData("review.accessibility-pane", WholeWindowVisualEvidenceActivationKind.AccessibilityCheckerPane, "accessibility")]
    [InlineData("review.alt-text-pane", WholeWindowVisualEvidenceActivationKind.AltTextPane, "alt-text")]
    [InlineData("review.reading-order-pane", WholeWindowVisualEvidenceActivationKind.ReadingOrderPane, "reading-order")]
    [InlineData("review.proofing-pane", WholeWindowVisualEvidenceActivationKind.ProofingPane, "proofing")]
    [InlineData("accessibility.media-caption-pane", WholeWindowVisualEvidenceActivationKind.MediaCaptionPane, "media-caption")]
    [InlineData("context.smartart-text-pane", WholeWindowVisualEvidenceActivationKind.SmartArtTextPane, "smartart-text")]
    [InlineData("animations.animation-pane", WholeWindowVisualEvidenceActivationKind.AnimationPane, "animation")]
    [InlineData("view.gridlines-guides", WholeWindowVisualEvidenceActivationKind.ViewGridlinesAndGuides, "gridlines-guides")]
    [InlineData("view.clean-canvas", WholeWindowVisualEvidenceActivationKind.ViewCleanCanvas, "clean-canvas")]
    [InlineData("view.zoom-fit", WholeWindowVisualEvidenceActivationKind.ViewZoomFit, "zoom-fit")]
    [InlineData("view.zoom-200", WholeWindowVisualEvidenceActivationKind.ViewZoom200, "zoom-200")]
    [InlineData("ribbon.home", WholeWindowVisualEvidenceActivationKind.None, "")]
    public void Preparation_plan_routes_native_activation_without_framework_types(
        string scenarioId,
        WholeWindowVisualEvidenceActivationKind expectedKind,
        string expectedId)
    {
        var plan = WholeWindowVisualEvidencePreparationSession.Prepare(
            WholeWindowVisualEvidenceCatalog.Get(scenarioId),
            DialogPaneVisualEvidenceFixtureFactory.Create());

        plan.LoadFixturePresentation.Should().BeTrue();
        plan.ExpectedSlideCount.Should().Be(3);
        plan.Activation.Should().Be(new WholeWindowVisualEvidenceActivation(expectedKind, expectedId));
        plan.ActiveRibbonTabId.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("review.comments-pane", "text")]
    [InlineData("view.zoom-fit", "chart")]
    [InlineData("accessibility.media-caption-pane", "media")]
    [InlineData("context.smartart-text-pane", "smartart")]
    [InlineData("status.slide-2", "none")]
    public void Preparation_plan_resolves_selection_routes(string scenarioId, string expectedSelection)
    {
        var fixture = DialogPaneVisualEvidenceFixtureFactory.Create();
        uint expectedShapeId = expectedSelection switch
        {
            "text" => fixture.TextShapeId,
            "chart" => fixture.ChartShapeId,
            "media" => fixture.MediaShapeId,
            "smartart" => fixture.SmartArtShapeId,
            _ => 0u,
        };

        var plan = WholeWindowVisualEvidencePreparationSession.Prepare(
            WholeWindowVisualEvidenceCatalog.Get(scenarioId),
            fixture);

        plan.SelectionShapeId.Should().Be(expectedShapeId);
    }

    [Theory]
    [InlineData("editor.rich-text-selection", 10, 35, "revenue review highlights")]
    [InlineData("editor.rich-text-caret", 67, 67, "")]
    public void Preparation_plan_mutates_rich_editor_fixture_and_exposes_deterministic_range(
        string scenarioId,
        int expectedStart,
        int expectedEnd,
        string expectedText)
    {
        var fixture = DialogPaneVisualEvidenceFixtureFactory.Create();
        var shape = fixture.Presentation.Slides[0].Shapes.Single(candidate => candidate.Id == fixture.TextShapeId);
        var originalBody = shape.TextBody;
        originalBody.Should().NotBeNull();

        var plan = WholeWindowVisualEvidencePreparationSession.Prepare(
            WholeWindowVisualEvidenceCatalog.Get(scenarioId),
            fixture);

        plan.RichEditor.Should().Be(new WholeWindowVisualEvidenceRichEditorPlan(
            fixture.TextShapeId,
            expectedStart,
            expectedEnd,
            expectedText,
            3));
        shape.TextBody.Should().NotBeSameAs(originalBody);
        shape.TextBody!.Paragraphs.SelectMany(paragraph => paragraph.Runs).Should().HaveCount(3);
        InCanvasTextEditPlanner.ExtractPlainText(shape.TextBody)[expectedStart..expectedEnd]
            .Should().Be(expectedText);
    }

    [Fact]
    public void Preparation_plan_builds_baseline_and_rich_editor_assertions_from_host_observations()
    {
        var fixture = DialogPaneVisualEvidenceFixtureFactory.Create();
        var plan = WholeWindowVisualEvidencePreparationSession.Prepare(
            WholeWindowVisualEvidenceCatalog.Get("editor.rich-text-selection"),
            fixture);

        var baseline = plan.CreateBaselineAssertions(new(
            3,
            0,
            [fixture.TextShapeId]));
        var richEditor = plan.CreateRichEditorAssertions(new(
            true,
            fixture.TextShapeId,
            true,
            DialogPaneVisualEvidenceFixtureFactory.RichEditorSelectedText,
            true,
            3,
            "Native editor owns focus."));

        baseline.Select(assertion => assertion.Id).Should().Equal(
            "fixture-loaded",
            "slide-activated",
            "selection-activated");
        baseline.Should().OnlyContain(assertion => assertion.Passed);
        richEditor.Select(assertion => assertion.Id).Should().Equal(
            "rich-editor-activated",
            "rich-editor-selection",
            "rich-editor-focus",
            "rich-editor-mixed-runs");
        richEditor.Should().OnlyContain(assertion => assertion.Passed);
        richEditor.Single(assertion => assertion.Id == "rich-editor-focus").Detail
            .Should().Be("Native editor owns focus.");
    }

    [Fact]
    public void Preparation_plan_builds_activation_assertions_for_view_and_backstage_routes()
    {
        var view = WholeWindowVisualEvidencePreparationSession.Prepare(
            WholeWindowVisualEvidenceCatalog.Get("view.zoom-fit"),
            DialogPaneVisualEvidenceFixtureFactory.Create());
        var backstage = WholeWindowVisualEvidencePreparationSession.Prepare(
            WholeWindowVisualEvidenceCatalog.Get("backstage.print"),
            DialogPaneVisualEvidenceFixtureFactory.Create());

        var viewAssertions = view.CreateActivationAssertions(new(
            true,
            "view",
            [],
            false,
            null));
        var backstageAssertions = backstage.CreateActivationAssertions(new(
            true,
            "home",
            [],
            true,
            "Print"));

        viewAssertions.Select(assertion => assertion.Id).Should().Equal(
            "view-state-activated-via-command",
            "active-ribbon-tab");
        viewAssertions.Should().OnlyContain(assertion => assertion.Passed);
        backstageAssertions.Should().ContainSingle()
            .Which.Should().Match<DialogPaneVisualEvidenceAssertion>(assertion =>
                assertion.Id == "backstage-pane-activated" && assertion.Passed);
    }

    [Fact]
    public void Native_whole_window_hosts_delegate_portable_preparation_and_assertions()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var owner = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "TestSupport",
            "VisualEvidence",
            "WholeWindowVisualEvidenceHostCoordinator.cs"));
        var hosts = new[]
        {
            File.ReadAllText(Path.Combine(root, "freep", "TestSupport", "VisualEvidence.Wpf", "WpfWholeWindowVisualEvidenceCoordinator.cs")),
            File.ReadAllText(Path.Combine(root, "freep", "TestSupport", "VisualEvidence.Avalonia", "AvaloniaWholeWindowVisualEvidenceCoordinator.cs")),
        };

        owner.Should().Contain("public interface IWholeWindowVisualEvidenceProbe")
            .And.Contain("public sealed class WholeWindowVisualEvidenceHostCoordinator")
            .And.Contain("WholeWindowVisualEvidencePreparationSession.Prepare(scenario, fixture)")
            .And.Contain("plan.CreateBaselineAssertions(probe.CaptureBaselineState())")
            .And.Contain("plan.CreateRichEditorAssertions(probe.PrepareRichEditor(richEditor))")
            .And.Contain("preparation.CreateActivationAssertions(new(")
            .And.NotContain("using System.Windows")
            .And.NotContain("using Avalonia");
        foreach (var host in hosts)
        {
            host.Should().Contain(": IWholeWindowVisualEvidenceProbe")
                .And.Contain("_coordinator = new(this);")
                .And.Contain("_coordinator.Prepare(scenario, fixture)")
                .And.Contain("_coordinator.CaptureSemantic(scenario, preparationAssertions)")
                .And.NotContain("WholeWindowVisualEvidencePreparationSession.Prepare(")
                .And.NotContain("CreateBaselineAssertions(")
                .And.NotContain("CreateRichEditorAssertions(")
                .And.NotContain("CreateActivationAssertions(")
                .And.NotContain("private void ShowAuxiliaryPane(")
                .And.NotContain("private bool PrepareViewState(");
        }
    }

    [Fact]
    public void Media_caption_capture_uses_the_canonical_host_lifecycle_in_both_renderers()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var coordinator = File.ReadAllText(Path.Combine(
            root,
            "freep",
            "TestSupport",
            "VisualEvidence",
            "WholeWindowVisualEvidenceHostCoordinator.cs"));
        var hosts = new[]
        {
            (
                MainWindow: File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.cs")),
                Capture: File.ReadAllText(Path.Combine(root, "freep", "TestSupport", "VisualEvidence.Wpf", "WpfWholeWindowVisualEvidenceCoordinator.cs")),
                Adapter: File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.VisualCaptureAdapter.cs"))),
            (
                MainWindow: File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs")),
                Capture: File.ReadAllText(Path.Combine(root, "freep", "TestSupport", "VisualEvidence.Avalonia", "AvaloniaWholeWindowVisualEvidenceCoordinator.cs")),
                Adapter: File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.VisualCaptureAdapter.cs"))),
        };

        coordinator.Should().Contain("case WholeWindowVisualEvidenceActivationKind.MediaCaptionPane:")
            .And.Contain("probe.ShowMediaCaptionPane();");
        foreach (var host in hosts)
        {
            host.Capture.Should().Contain("IWholeWindowVisualEvidenceProbe.ShowMediaCaptionPane()")
                .And.Contain("_access.ShowMediaCaptionPane();");
            host.Adapter.Should().Contain("if (owner.IsMediaCaptionPaneVisible) result.Add(\"accessibility.media-caption-pane\")");
            host.MainWindow.Should().Contain("ShowMediaCaptionPane() =>")
                .And.Contain("_mediaPaneHostCoordinator.Show();")
                .And.Contain("HideMediaCaptionPane() => _mediaPaneHostCoordinator.Hide();");
        }
    }
}
