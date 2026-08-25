using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWViewDepthPlannerTests
{
    [Fact]
    public void Toggle_commands_are_mutually_exclusive()
    {
        var live = new FreeWViewDepthState(FreeWViewDepthMode.LiveEditor);

        var split = FreeWViewDepthPlanner.Plan(live, FreeWViewDepthCommand.ToggleSplit);
        var multiple = FreeWViewDepthPlanner.Plan(new FreeWViewDepthState(split.Mode), FreeWViewDepthCommand.ToggleMultiplePages);
        var sideToSide = FreeWViewDepthPlanner.Plan(new FreeWViewDepthState(multiple.Mode), FreeWViewDepthCommand.ToggleSideToSide);

        split.IsSplitActive.Should().BeTrue();
        split.IsMultiplePagesActive.Should().BeFalse();
        split.IsSideToSideActive.Should().BeFalse();

        multiple.IsSplitActive.Should().BeFalse();
        multiple.IsMultiplePagesActive.Should().BeTrue();
        multiple.IsSideToSideActive.Should().BeFalse();
        multiple.PagesAcross.Should().Be(2);
        multiple.Layout.PageFlow.Should().Be(DocumentViewDepthPageFlow.MultiplePagesGrid);
        multiple.Layout.PageRows.Should().Be(2);
        multiple.Layout.PreferredVisiblePageCount.Should().Be(4);

        sideToSide.IsSplitActive.Should().BeFalse();
        sideToSide.IsMultiplePagesActive.Should().BeFalse();
        sideToSide.IsSideToSideActive.Should().BeTrue();
        sideToSide.PagesAcross.Should().Be(2);
        sideToSide.Layout.PageFlow.Should().Be(DocumentViewDepthPageFlow.SideToSideHorizontal);
        sideToSide.Layout.PageRows.Should().Be(1);
        sideToSide.Layout.UsesHorizontalPageFlow.Should().BeTrue();
    }

    [Fact]
    public void Repeating_active_toggle_restores_live_editor()
    {
        var active = new FreeWViewDepthState(FreeWViewDepthMode.MultiplePagesPreview);

        var plan = FreeWViewDepthPlanner.Plan(active, FreeWViewDepthCommand.ToggleMultiplePages);

        plan.Mode.Should().Be(FreeWViewDepthMode.LiveEditor);
        plan.SurfaceKind.Should().Be(FreeWViewDepthSurfaceKind.LiveEditor);
        plan.UsesReadOnlySnapshot.Should().BeFalse();
    }

    [Fact]
    public void Restore_live_editor_clears_all_view_depth_state_from_every_mode()
    {
        foreach (var mode in new[]
                 {
                     FreeWViewDepthMode.SplitPreview,
                     FreeWViewDepthMode.MultiplePagesPreview,
                     FreeWViewDepthMode.SideToSidePreview,
                 })
        {
            var plan = FreeWViewDepthPlanner.Plan(
                new FreeWViewDepthState(mode),
                FreeWViewDepthCommand.RestoreLiveEditor);

            plan.Mode.Should().Be(FreeWViewDepthMode.LiveEditor);
            plan.SurfaceKind.Should().Be(FreeWViewDepthSurfaceKind.LiveEditor);
            plan.IsSplitActive.Should().BeFalse();
            plan.IsMultiplePagesActive.Should().BeFalse();
            plan.IsSideToSideActive.Should().BeFalse();
            plan.UsesReadOnlySnapshot.Should().BeFalse();
        }
    }

    [Fact]
    public void Preview_plans_keep_split_editors_live()
    {
        var split = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.SplitPreview);
        var multiple = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.MultiplePagesPreview);
        var sideToSide = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.SideToSidePreview);

        split.UsesReadOnlySnapshot.Should().BeFalse();
        multiple.UsesReadOnlySnapshot.Should().BeFalse();
        sideToSide.UsesReadOnlySnapshot.Should().BeFalse();
        split.SurfaceKind.Should().Be(FreeWViewDepthSurfaceKind.SplitEditors);
        split.Limitation.Should().BeNull();
        multiple.Limitation.Should().BeNull();
        sideToSide.Limitation.Should().BeNull();
    }

    [Theory]
    [InlineData(1, 1, 1, 1, false, false, "Side to Side page 1 of 1.")]
    [InlineData(2, 5, 1, 2, false, true, "Side to Side pages 1-2 of 5.")]
    [InlineData(3, 5, 3, 4, true, true, "Side to Side pages 3-4 of 5.")]
    [InlineData(5, 5, 5, 5, true, false, "Side to Side page 5 of 5.")]
    [InlineData(99, 6, 5, 6, true, false, "Side to Side pages 5-6 of 6.")]
    public void Side_to_side_page_pair_navigation_normalizes_and_clamps(
        int requestedFirstPage,
        int totalPages,
        int expectedFirstPage,
        int expectedLastPage,
        bool expectedPrevious,
        bool expectedNext,
        string expectedStatus)
    {
        var plan = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.SideToSidePreview);

        var state = FreeWViewDepthPlanner.BuildPagePairNavigation(
            plan,
            requestedFirstPage,
            totalPages);

        state.IsSideToSideNavigationActive.Should().BeTrue();
        state.FirstVisiblePageNumber.Should().Be(expectedFirstPage);
        state.LastVisiblePageNumber.Should().Be(expectedLastPage);
        state.TotalPages.Should().Be(Math.Max(1, totalPages));
        state.PagesPerPair.Should().Be(2);
        state.CanGoToPreviousPair.Should().Be(expectedPrevious);
        state.CanGoToNextPair.Should().Be(expectedNext);
        state.StatusText.Should().Be(expectedStatus);
    }

    [Fact]
    public void Side_to_side_navigation_steps_by_pair_and_clamps_at_document_edges()
    {
        var plan = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.SideToSidePreview);
        var first = FreeWViewDepthPlanner.BuildPagePairNavigation(plan, 1, totalPages: 5);

        var second = FreeWViewDepthPlanner.NavigatePagePair(
            plan,
            first,
            FreeWViewDepthPagePairNavigationCommand.NextPair);
        var third = FreeWViewDepthPlanner.NavigatePagePair(
            plan,
            second,
            FreeWViewDepthPagePairNavigationCommand.NextPair);
        var stillThird = FreeWViewDepthPlanner.NavigatePagePair(
            plan,
            third,
            FreeWViewDepthPagePairNavigationCommand.NextPair);
        var backToSecond = FreeWViewDepthPlanner.NavigatePagePair(
            plan,
            third,
            FreeWViewDepthPagePairNavigationCommand.PreviousPair);

        second.FirstVisiblePageNumber.Should().Be(3);
        second.LastVisiblePageNumber.Should().Be(4);
        third.FirstVisiblePageNumber.Should().Be(5);
        third.LastVisiblePageNumber.Should().Be(5);
        stillThird.FirstVisiblePageNumber.Should().Be(5);
        stillThird.CanGoToNextPair.Should().BeFalse();
        backToSecond.FirstVisiblePageNumber.Should().Be(3);
    }

    [Fact]
    public void Page_pair_navigation_is_disabled_for_non_side_to_side_modes()
    {
        var multiplePages = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.MultiplePagesPreview);

        var state = FreeWViewDepthPlanner.BuildPagePairNavigation(
            multiplePages,
            requestedFirstVisiblePageNumber: 3,
            totalPages: 8);

        state.IsSideToSideNavigationActive.Should().BeFalse();
        state.FirstVisiblePageNumber.Should().Be(1);
        state.LastVisiblePageNumber.Should().Be(1);
        state.TotalPages.Should().Be(8);
        state.CanGoToPreviousPair.Should().BeFalse();
        state.CanGoToNextPair.Should().BeFalse();
        state.StatusText.Should().Be(multiplePages.StatusText);
    }

    [Fact]
    public void Preview_scale_accounts_for_page_grid_and_side_to_side_fit()
    {
        var live = FreeWViewDepthPlanner.BuildPreviewScale(
            FreeWViewDepthMode.LiveEditor,
            viewportWidthDip: 1200,
            viewportHeightDip: 800,
            pageWidthDip: 600,
            pageHeightDip: 800);
        var multiplePages = FreeWViewDepthPlanner.BuildPreviewScale(
            FreeWViewDepthMode.MultiplePagesPreview,
            viewportWidthDip: 1200,
            viewportHeightDip: 800,
            pageWidthDip: 600,
            pageHeightDip: 800);
        var sideToSide = FreeWViewDepthPlanner.BuildPreviewScale(
            FreeWViewDepthMode.SideToSidePreview,
            viewportWidthDip: 1200,
            viewportHeightDip: 800,
            pageWidthDip: 600,
            pageHeightDip: 800);

        multiplePages.Should().BeLessThan(live);
        sideToSide.Should().BeGreaterThan(multiplePages);
        sideToSide.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Shared_layout_policy_distinguishes_multiple_pages_from_side_to_side()
    {
        var multiple = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.MultiplePagesPreview).Layout;
        var sideToSide = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.SideToSidePreview).Layout;

        multiple.PageFlow.Should().Be(DocumentViewDepthPageFlow.MultiplePagesGrid);
        multiple.PagesAcross.Should().Be(2);
        multiple.PageRows.Should().Be(2);
        multiple.ZoomIntent.Should().Be(DocumentViewDepthZoomIntent.FitPagesAcross);
        multiple.UsesHorizontalPageFlow.Should().BeFalse();
        multiple.UsesLiveEditor.Should().BeTrue();
        multiple.AllowsPrimaryEditing.Should().BeTrue();
        multiple.UsesReadOnlySnapshot.Should().BeFalse();

        sideToSide.PageFlow.Should().Be(DocumentViewDepthPageFlow.SideToSideHorizontal);
        sideToSide.PagesAcross.Should().Be(2);
        sideToSide.PageRows.Should().Be(1);
        sideToSide.PreferredVisiblePageCount.Should().Be(2);
        sideToSide.ZoomIntent.Should().Be(DocumentViewDepthZoomIntent.FitPagesAcross);
        sideToSide.UsesHorizontalPageFlow.Should().BeTrue();
    }

    [Fact]
    public void Shared_viewport_plan_exposes_required_page_span_for_renderers()
    {
        var multiple = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.MultiplePagesPreview).Layout;
        var sideToSide = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.SideToSidePreview).Layout;

        var multipleViewport = DocumentViewDepthLayoutPlanner.BuildViewportPlan(
            multiple,
            viewportWidthDip: 1400,
            viewportHeightDip: 1000,
            pageWidthDip: 600,
            pageHeightDip: 800);
        var sideViewport = DocumentViewDepthLayoutPlanner.BuildViewportPlan(
            sideToSide,
            viewportWidthDip: 1400,
            viewportHeightDip: 1000,
            pageWidthDip: 600,
            pageHeightDip: 800);

        multipleViewport.RequiredPageSpanWidthDip.Should().Be(1224);
        multipleViewport.RequiredPageSpanHeightDip.Should().Be(1624);
        sideViewport.RequiredPageSpanWidthDip.Should().Be(1224);
        sideViewport.RequiredPageSpanHeightDip.Should().Be(800);
        sideViewport.Scale.Should().BeGreaterThan(multipleViewport.Scale);
    }

    [Fact]
    public void Document_viewer_zoom_uses_pages_across_from_shared_layout()
    {
        var sideToSide = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.SideToSidePreview).Layout;
        var live = FreeWViewDepthPlanner.Build(FreeWViewDepthMode.LiveEditor).Layout;

        DocumentViewDepthLayoutPlanner.BuildDocumentViewerZoomPercent(sideToSide, pageWidthZoomFactor: 1.2)
            .Should().Be(60);
        DocumentViewDepthLayoutPlanner.BuildDocumentViewerZoomPercent(live, pageWidthZoomFactor: 1.2)
            .Should().Be(120);
    }

    [Fact]
    public void Side_to_side_navigation_semantics_are_renderer_neutral()
    {
        FreeWApplicationFrameTextCatalog.PreviousPagePairSemantic.Should().Be(
            new FreeWSemanticIdentity(
                "FreeW.SideToSide.Previouspair",
                "Previous Side-to-Side page pair"));
        FreeWApplicationFrameTextCatalog.NextPagePairSemantic.Should().Be(
            new FreeWSemanticIdentity(
                "FreeW.SideToSide.Nextpair",
                "Next Side-to-Side page pair"));
        FreeWApplicationFrameTextCatalog.PagePairStatusAutomationId.Should()
            .Be("FreeW.SideToSidePagePairStatus");
    }
}
