using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationAltTextPaneHostCoordinatorTests
{
    [Fact]
    public void Coordinator_OwnsVisibilityInputRefreshApplyAndRenderProjection()
    {
        var (editor, shape) = CreateSelectedShapeEditor();
        var panes = new PresentationWorkareaPaneSession();
        var view = new RecordingAltTextPaneHostView();
        var coordinator = new PresentationAltTextPaneHostCoordinator(
            CreateSession(editor),
            panes,
            view);

        var opened = coordinator.Show();

        opened.Description.Value.Should().BeEmpty();
        coordinator.IsPaneVisible.Should().BeTrue();
        panes.IsVisible(PresentationWorkareaPane.AltText).Should().BeTrue();
        view.IsPaneVisible.Should().BeTrue();
        view.LastRender!.ApplyAction.IsEnabled.Should().BeFalse();
        view.LastRender.Description.ValidationMessage.Should().NotBeNullOrEmpty();

        coordinator.SetInput(new(
            "  Hero packaging photo  ",
            "  Product packaging on a white background.  ",
            IsDecorative: false));

        view.Input.Title.Should().Be("Hero packaging photo");
        view.Input.Description.Should().Be("Product packaging on a white background.");
        view.LastRender!.ApplyAction.IsEnabled.Should().BeTrue();

        var mutation = coordinator.Apply();

        mutation.ShouldApply.Should().BeTrue();
        shape.AlternativeTextTitle.Should().Be("Hero packaging photo");
        shape.AlternativeText.Should().Be("Product packaging on a white background.");
        shape.IsDecorative.Should().BeFalse();
        view.LastRender.Title.Value.Should().Be("Hero packaging photo");

        coordinator.Hide();

        coordinator.IsPaneVisible.Should().BeFalse();
        panes.IsRequested(PresentationWorkareaPane.AltText).Should().BeFalse();
        view.IsPaneVisible.Should().BeFalse();
        view.AccessibilityRefreshCount.Should().Be(2);
    }

    [Fact]
    public void Coordinator_SuppressesNestedRendererEventsAndSupportsDecorativeInput()
    {
        var (editor, shape) = CreateSelectedShapeEditor();
        var panes = new PresentationWorkareaPaneSession();
        var view = new RecordingAltTextPaneHostView();
        var coordinator = new PresentationAltTextPaneHostCoordinator(
            CreateSession(editor),
            panes,
            view);
        var nestedRefreshCount = 0;
        view.DuringUpdate = () =>
        {
            nestedRefreshCount++;
            coordinator.Refresh().Should().BeNull();
        };

        coordinator.SetInput(new("Ignored title", string.Empty, IsDecorative: true));
        var mutation = coordinator.Apply();

        mutation.ShouldApply.Should().BeTrue();
        shape.IsDecorative.Should().BeTrue();
        shape.AlternativeTextTitle.Should().BeEmpty();
        shape.AlternativeText.Should().BeEmpty();
        view.LastRender!.Title.IsEnabled.Should().BeFalse();
        view.LastRender.Description.IsEnabled.Should().BeFalse();
        view.LastRender.ApplyAction.IsEnabled.Should().BeTrue();
        coordinator.IsUpdating.Should().BeFalse();
        nestedRefreshCount.Should().BeGreaterThan(1);
    }

    [Fact]
    public void MainWindowSourceGuards_KeepAltTextTransitionsInPortableCoordinator()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpf = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        foreach (var source in new[] { wpf, avalonia })
        {
            source.Should().Contain("IPresentationAltTextPaneHostView");
            source.Should().Contain("private readonly PresentationAltTextPaneHostCoordinator _altTextPaneHostCoordinator;");
            source.Should().Contain("_altTextPaneHostCoordinator.Show()");
            source.Should().Contain("_altTextPaneHostCoordinator.Hide()");
            source.Should().Contain("_altTextPaneHostCoordinator.SetInput(");
            source.Should().Contain("_altTextPaneHostCoordinator.Apply()");
            source.Should().Contain("_altTextPaneHostCoordinator.Refresh()");
            source.Should().Contain("_altTextPaneHostCoordinator.RenderIfVisible(plan)");
            source.Should().Contain("IPresentationAltTextPaneHostView.CaptureInput()");
            source.Should().Contain("IPresentationAltTextPaneHostView.Render(");
            source.Should().NotContain("_altTextPaneRefreshing");
            source.Should().NotContain("GetAltTextPaneAction(");
            source.Should().NotContain("private void RenderAltTextPane(");
            source.Should().NotContain("_reviewWorkflowSession.RefreshAltTextPlans(");
            source.Should().NotContain("PresentationReviewWorkflowPlanner.AltTextPaneApplyCommandId");
            source.Should().NotContain("_workareaSession.Panes.Show(PresentationWorkareaPane.AltText)");
            source.Should().NotContain("_workareaSession.Panes.Hide(PresentationWorkareaPane.AltText)");
        }
    }

    private static PresentationReviewWorkflowSession CreateSession(EditingSession editor)
    {
        static void Ignore() { }

        return new(
            () => editor,
            new PresentationReviewWorkflowSessionCallbacks(
                MarkDirty: Ignore,
                RefreshCanvas: Ignore,
                RefreshNotesPane: Ignore,
                RenderAccessibilityCheckerPaneIfVisible: _ => { },
                PresentAccessibilityCheckerPane: _ => { },
                OpenAltTextPane: Ignore,
                OpenHyperlinkDialog: Ignore,
                OpenMediaCaptionPane: Ignore,
                RenderCommentPane: _ => { },
                RenderAltTextPaneIfVisible: _ => { },
                RenderReadingOrderPaneIfVisible: _ => { },
                PresentReadingOrderPane: _ => { },
                RenderProofingPaneIfVisible: _ => { },
                PresentProofingPane: _ => { },
                UpdateAfterCommentMutation: Ignore,
                UpdateAfterCommentNavigation: Ignore,
                UpdateAfterProofingCorrection: Ignore));
    }

    private static (EditingSession Editor, SlideShape Shape) CreateSelectedShapeEditor()
    {
        var presentation = Presentation.CreateEmpty();
        var shape = new SlideShape { Id = 7, Name = "Photo" };
        presentation.Slides[0].Shapes.Add(shape);
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        editor.Select(shape.Id);
        return (editor, shape);
    }

    private sealed class RecordingAltTextPaneHostView : IPresentationAltTextPaneHostView
    {
        public bool IsPaneVisible { get; private set; }

        public PresentationAltTextPaneHostSnapshot Input { get; private set; } = new(null, null, false);

        public PresentationAltTextPaneHostRenderPlan? LastRender { get; private set; }

        public int AccessibilityRefreshCount { get; private set; }

        public Action? DuringUpdate { get; set; }

        public PresentationAltTextPaneHostSnapshot CaptureInput() => Input;

        public void SetPaneVisible(bool visible)
        {
            IsPaneVisible = visible;
            DuringUpdate?.Invoke();
        }

        public void SetInput(PresentationAltTextPaneHostSnapshot input)
        {
            Input = input;
            DuringUpdate?.Invoke();
        }

        public void Render(PresentationAltTextPaneHostRenderPlan plan)
        {
            LastRender = plan;
            Input = new(plan.Title.Value, plan.Description.Value, plan.IsDecorative);
            DuringUpdate?.Invoke();
        }

        public void RefreshAccessibilityMetadata() => AccessibilityRefreshCount++;
    }
}
