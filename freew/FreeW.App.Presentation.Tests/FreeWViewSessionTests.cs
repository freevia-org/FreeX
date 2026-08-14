using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWViewSessionTests
{
    [Fact]
    public void DepthTransitionsOwnMutualExclusionAndNativeCleanupIntent()
    {
        var session = new FreeWViewSession(FreeWViewDepthCapabilities.FullDesktop);

        var split = session.Execute(FreeWViewDepthCommand.ToggleSplit);
        var multiple = session.Execute(FreeWViewDepthCommand.ToggleMultiplePages);
        var sideToSide = session.Execute(FreeWViewDepthCommand.ToggleSideToSide);
        var live = session.Execute(FreeWViewDepthCommand.ToggleSideToSide);

        split.Current.IsSplitActive.Should().BeTrue();
        split.ExitSplitSurface.Should().BeFalse();
        multiple.ExitSplitSurface.Should().BeTrue();
        multiple.Current.IsMultiplePagesActive.Should().BeTrue();
        sideToSide.ExitPageSurface.Should().BeTrue();
        sideToSide.Current.SurfaceKind.Should().Be(FreeWViewDepthSurfaceKind.EditablePageView);
        live.ExitPageSurface.Should().BeTrue();
        live.Current.Mode.Should().Be(FreeWViewDepthMode.LiveEditor);
    }

    [Fact]
    public void CapabilitiesKeepUnsupportedCommandsStableAndSelectReadOnlyFallbacks()
    {
        var capabilities = new FreeWViewDepthCapabilities(
            SupportsSplitPreview: false,
            SupportsMultiplePagesPreview: true,
            SupportsSideToSidePreview: true,
            SupportsEditableSideToSide: false,
            SupportsPagePairNavigation: true);
        var session = new FreeWViewSession(capabilities);

        session.Execute(FreeWViewDepthCommand.ToggleSplit).Current.Mode
            .Should().Be(FreeWViewDepthMode.LiveEditor);

        var sideToSide = session.Execute(FreeWViewDepthCommand.ToggleSideToSide).Current;
        sideToSide.Mode.Should().Be(FreeWViewDepthMode.SideToSidePreview);
        sideToSide.SurfaceKind.Should().Be(FreeWViewDepthSurfaceKind.ReadOnlyPagePreview);
        sideToSide.UsesReadOnlySnapshot.Should().BeTrue();
        sideToSide.Limitation.Should().Contain("this host");
    }

    [Fact]
    public void SessionOwnsPagePairNavigationState()
    {
        var session = new FreeWViewSession(FreeWViewDepthCapabilities.FullDesktop);
        session.Execute(FreeWViewDepthCommand.ToggleSideToSide);

        session.StartPagePairNavigation(totalPages: 5);
        session.NavigatePagePair(FreeWViewDepthPagePairNavigationCommand.NextPair);

        session.PagePairNavigation.FirstVisiblePageNumber.Should().Be(3);
        session.PagePairNavigation.LastVisiblePageNumber.Should().Be(4);

        session.RestoreLiveEditor();
        session.PagePairNavigation.IsSideToSideNavigationActive.Should().BeFalse();
    }

    [Fact]
    public void DocumentViewChangeUsesSessionDepthInsteadOfRendererSnapshots()
    {
        var session = new FreeWViewSession(FreeWViewDepthCapabilities.FullDesktop);
        session.Execute(FreeWViewDepthCommand.ToggleMultiplePages);

        var plan = session.PlanDocumentViewChange(
            DocumentViewMode.Draft,
            isOutlineMode: true,
            isPagedEditMode: true,
            DocumentViewMode.WebLayout);

        plan.Should().Be(new FreeWDocumentViewChangePlan(
            DocumentViewMode.WebLayout,
            ExitOutlineMode: true,
            ExitPagedEditMode: true,
            ExitPaginatedView: true));
    }

    [Fact]
    public void DocumentViewChangeRejectsPagedEditAsALiveEditorMode()
    {
        var session = new FreeWViewSession(FreeWViewDepthCapabilities.FullDesktop);

        var act = () => session.PlanDocumentViewChange(
            DocumentViewMode.PrintLayout,
            isOutlineMode: false,
            isPagedEditMode: false,
            DocumentViewMode.PagedEdit);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(DocumentViewMode.PrintLayout, true, false, false)]
    [InlineData(DocumentViewMode.WebLayout, false, true, false)]
    [InlineData(DocumentViewMode.Draft, false, false, true)]
    public void DocumentViewChecksProjectExactlyOneLiveMode(
        DocumentViewMode mode,
        bool printLayout,
        bool webLayout,
        bool draft)
    {
        var session = new FreeWViewSession(FreeWViewDepthCapabilities.FullDesktop);

        session.BuildDocumentViewChecks(mode, isOutlineMode: false, isPagedEditMode: false)
            .Should().Be(new FreeWDocumentViewCheckPlan(printLayout, webLayout, draft, PagedEdit: false));
        session.BuildDocumentViewChecks(mode, isOutlineMode: true, isPagedEditMode: false)
            .Should().Be(new FreeWDocumentViewCheckPlan(false, false, false, false));
        session.BuildDocumentViewChecks(mode, isOutlineMode: false, isPagedEditMode: true)
            .Should().Be(new FreeWDocumentViewCheckPlan(false, false, false, true));
        session.BuildDocumentViewChecks(mode, isOutlineMode: true, isPagedEditMode: true)
            .Should().Be(new FreeWDocumentViewCheckPlan(false, false, false, false));
    }

    [Fact]
    public void OutlineTransitionsExitPageDepthAndSuspendThenRestorePagedEdit()
    {
        var session = new FreeWViewSession(FreeWViewDepthCapabilities.FullDesktop);
        session.Execute(FreeWViewDepthCommand.ToggleMultiplePages);

        var enter = session.EnterOutline(isPagedEditMode: true);
        enter.Should().Be(new FreeWOutlineViewTransition(
            IsOutlineMode: true,
            IsPagedEditMode: false,
            ExitPageSurface: true,
            ExitPagedEditSurface: true,
            EnterPagedEditSurface: false));

        var leave = session.LeaveOutline();
        leave.Should().Be(new FreeWOutlineViewTransition(
            IsOutlineMode: false,
            IsPagedEditMode: true,
            ExitPageSurface: false,
            ExitPagedEditSurface: false,
            EnterPagedEditSurface: true));
    }

    [Fact]
    public void OutlineTransitionCanDiscardSuspendedPagedEditForAnExplicitViewChange()
    {
        var session = new FreeWViewSession(FreeWViewDepthCapabilities.FullDesktop);

        session.EnterOutline(isPagedEditMode: true);

        session.LeaveOutline(restorePriorView: false)
            .Should().Be(new FreeWOutlineViewTransition(
                IsOutlineMode: false,
                IsPagedEditMode: false,
                ExitPageSurface: false,
                ExitPagedEditSurface: false,
                EnterPagedEditSurface: false));
    }
}
