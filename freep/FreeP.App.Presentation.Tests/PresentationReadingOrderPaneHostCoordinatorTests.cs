using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class PresentationReadingOrderPaneHostCoordinatorTests
{
    [Fact]
    public void Present_OwnsVisibilityProjectionActionsAndAccessibilityRefresh()
    {
        var panes = new PresentationWorkareaPaneSession();
        var view = new RecordingReadingOrderPaneHostView();
        var coordinator = new PresentationReadingOrderPaneHostCoordinator(panes, view);
        var plan = CreatePlan();

        var render = coordinator.Present(plan);

        coordinator.IsPaneVisible.Should().BeTrue();
        panes.IsRequested(PresentationWorkareaPane.ReadingOrder).Should().BeTrue();
        view.IsPaneVisible.Should().BeTrue();
        view.AccessibilityRefreshCount.Should().Be(1);
        view.LastRender.Should().BeSameAs(render);
        render.Heading.Should().Be(plan.Heading);
        render.Message.Should().Be(plan.DisplayMessage);
        render.ShouldShowEmptyState.Should().BeTrue();
        render.EmptyStateMessage.Should().Be(PresentationReviewWorkflowPlanner.EmptyReadingOrderMessage);
        render.MoveEarlierAction.CommandId.Should().Be(
            PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId);
        render.MoveEarlierAction.IsEnabled.Should().BeFalse();
        render.MoveLaterAction.CommandId.Should().Be(
            PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId);
    }

    [Fact]
    public void RenderIfVisible_SuppressesHiddenPaneAndRefreshesVisiblePane()
    {
        var panes = new PresentationWorkareaPaneSession();
        var view = new RecordingReadingOrderPaneHostView();
        var coordinator = new PresentationReadingOrderPaneHostCoordinator(panes, view);
        var plan = CreatePlan();

        coordinator.RenderIfVisible(plan).Should().BeNull();
        view.RenderCount.Should().Be(0);

        coordinator.Present(plan);
        coordinator.RenderIfVisible(plan).Should().NotBeNull();

        view.RenderCount.Should().Be(2);
        view.AccessibilityRefreshCount.Should().Be(1);
    }

    [Fact]
    public void MainWindowSourceGuards_KeepReadingOrderLifecycleInPortableCoordinator()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var sources = new[]
        {
            File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.cs")),
            File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs")),
        };

        foreach (var source in sources)
        {
            source.Should().Contain("IPresentationReadingOrderPaneHostView");
            source.Should().Contain(
                "private readonly PresentationReadingOrderPaneHostCoordinator _readingOrderPaneHostCoordinator;");
            source.Should().Contain("_readingOrderPaneHostCoordinator.RenderIfVisible(plan)");
            source.Should().Contain("_readingOrderPaneHostCoordinator.Present(plan)");
            source.Should().Contain("IPresentationReadingOrderPaneHostView.Render(");
            source.Should().NotContain("private void RenderReadingOrderPaneIfVisible(");
            source.Should().NotContain("private void PresentReadingOrderPane(");
            source.Should().NotContain("GetReadingOrderAction(");
            source.Should().NotContain(
                "_workareaSession.Panes.Show(PresentationWorkareaPane.ReadingOrder)");
        }
    }

    private static PresentationReadingOrderPlan CreatePlan() => new(
        SlideIndex: 0,
        HasSlide: true,
        HasSingleSelectedItem: false,
        SelectedShapeId: null,
        SelectedItemIndex: -1,
        Items: [],
        Actions:
        [
            new(
                PresentationReviewWorkflowPlanner.ReadingOrderMoveEarlierCommandId,
                "Move Earlier",
                PresentationReviewWorkflowIntentKind.MoveReadingOrderEarlier,
                IsEnabled: false,
                PresentationWorkflowCapabilityStatus.Deferred,
                "Select an item."),
            new(
                PresentationReviewWorkflowPlanner.ReadingOrderMoveLaterCommandId,
                "Move Later",
                PresentationReviewWorkflowIntentKind.MoveReadingOrderLater,
                IsEnabled: false,
                PresentationWorkflowCapabilityStatus.Deferred,
                "Select an item."),
        ]);

    private sealed class RecordingReadingOrderPaneHostView : IPresentationReadingOrderPaneHostView
    {
        public bool IsPaneVisible { get; private set; }

        public PresentationReadingOrderPaneHostRenderPlan? LastRender { get; private set; }

        public int RenderCount { get; private set; }

        public int AccessibilityRefreshCount { get; private set; }

        public void SetPaneVisible(bool visible) => IsPaneVisible = visible;

        public void Render(PresentationReadingOrderPaneHostRenderPlan plan)
        {
            LastRender = plan;
            RenderCount++;
        }

        public void RefreshAccessibilityMetadata() => AccessibilityRefreshCount++;
    }
}
