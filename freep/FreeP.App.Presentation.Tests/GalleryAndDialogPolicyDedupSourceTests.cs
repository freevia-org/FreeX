namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Fails if a host re-declares the gallery plans, drag state machine, settings catalog or visual constants that
/// now live in <c>FreeP.App.Presentation</c>.
/// </summary>
public sealed class GalleryAndDialogPolicyDedupSourceTests
{
    [Fact]
    public void TransitionGalleries_DelegateTablesAndGeometryToThePortablePlanner()
    {
        foreach (var source in HostSources("Ribbon/PresentationTransitionGallery.cs", "PresentationTransitionGallery.cs"))
        {
            source.Should().Contain("TransitionGalleryPlanner.VisibleEntries(");
            source.Should().Contain("TransitionGalleryPlanner.PreviewFor(");
            source.Should().Contain("FreePRibbonGalleryCommands.Execute(");
            source.Should().Contain("FreePRibbonGalleryMetrics.HoverBackground");
            source.Should().NotContain("\"freep.transition.");
            source.Should().NotContain("Ribbon_Command_Transition");
            source.Should().NotContain("GetVisibleEntryCount");
            source.Should().NotContain("<= 800");
            source.Should().NotContain("enum TransitionPreview");
            source.Should().NotContain("0xEA, 0xF1");
            source.Should().NotContain("0x9C, 0x9C");
            source.Should().NotContain("0xB7, 0xB7");
            source.Should().NotContain("registry.TryGet(");
            source.Should().NotContain("index % 2 == 0");
        }
    }

    [Fact]
    public void AnimationGalleries_DelegateFavouritesAndCategoriesToThePortablePlanner()
    {
        foreach (var source in HostSources("Ribbon/PresentationAnimationGallery.cs", "PresentationAnimationGallery.cs"))
        {
            source.Should().Contain("AnimationGalleryPlanner.Plan(tab, state)");
            source.Should().Contain("plan.Favorites");
            source.Should().Contain("plan.Categories");
            source.Should().Contain("FreePRibbonGalleryCommands.Execute(");
            source.Should().NotContain("FavoriteCommandIds");
            source.Should().NotContain("\"freep.anim.");
            source.Should().NotContain("\"animation-effects\"");
            source.Should().NotContain("\".entrance.\"");
            source.Should().NotContain("Ribbon_Category_Animation");
            source.Should().NotContain("0xEA, 0xF1");
            source.Should().NotContain("0xA6, 0xA6");
            source.Should().NotContain("0x8C, 0x8C");
            source.Should().NotContain("registry.TryGet(");
        }
    }

    [Fact]
    public void ThemeGalleries_DelegateSelectionAndCommandIdsToThePortablePlanner()
    {
        foreach (var source in HostSources("Ribbon/PresentationThemeGallery.cs", "PresentationThemeGallery.cs"))
        {
            source.Should().Contain("ThemeGalleryPlanner.Plan(");
            source.Should().Contain("ThemeGalleryPlanner.CommandIdFor(");
            source.Should().Contain("ThemeGalleryPlanner.AccentSlots");
            source.Should().NotContain("\"freep.theme.\"");
            source.Should().NotContain("OrdinalIgnoreCase");
            source.Should().NotContain("entries[0]");
            source.Should().NotContain("ThemeColorSlot.Accent1");
            source.Should().NotContain("0xEA, 0xF1");
            source.Should().NotContain("0x9C, 0x9C");
            source.Should().NotContain("registry.TryGet(");
        }
    }

    [Fact]
    public void CustomShowDialogs_UseTheSharedDragTrackerInsteadOfRedeclaringTheStateMachine()
    {
        foreach (var source in HostSources("CustomShowDialog.cs"))
        {
            source.Should().Contain("SlideShowCustomShowDragTracker");
            source.Should().Contain("_customShowSlideDrag.Press(");
            source.Should().Contain("_customShowSlideDrag.ShouldBegin(");
            source.Should().Contain("SlideShowCustomShowDragTracker.ResolveDropIndex(");
            source.Should().NotContain("_customShowSlideDragStartPoint");
            source.Should().NotContain("_customShowSlideDragSourceIndex");
            source.Should().NotContain("_customShowSlideDragActive");
            source.Should().NotContain("Height / 2");
            source.Should().NotContain("ActualHeight / 2");
        }
    }

    [Fact]
    public void SlideShowSettingsDialogs_IterateTheCatalogAndResolveLoopStateThroughTheSession()
    {
        foreach (var source in HostSources("SlideShowSettingsDialog.cs"))
        {
            source.Should().Contain("SlideShowSettingsDialogSession.ToggleFields");
            source.Should().Contain("SlideShowSettingsDialogSession.ResolveLoopState(");
            source.Should().Contain("SlideShowSettingsDialogVisualMetrics.");
            source.Should().NotContain("_useTimingsCheck = new CheckBox");
            source.Should().NotContain("_loopCheck = new CheckBox");
            source.Should().NotContain("initial.UseSlideTimings");
            source.Should().NotContain("initial.ShowMasterShapes");
            source.Should().NotContain("BrowsedAtKiosk");
            source.Should().NotContain("Width = 360");
            source.Should().NotContain("Width = 345");
        }
    }

    [Fact]
    public void DriftedDialogMetrics_ComeFromTheSharedMetricsClasses()
    {
        var zoomHost = Read("Host", "ZoomDialogChrome.cs");
        var zoomAvalonia = Read("Avalonia", "ZoomDialogChrome.cs");
        zoomHost.Should().Contain("ZoomDialogVisualMetrics.ButtonMinWidth").And.NotContain("MinWidth = 75");
        zoomAvalonia.Should().Contain("ZoomDialogVisualMetrics.ButtonMinWidth")
            .And.NotContain("MinWidth = 80").And.NotContain("minWidth: 80");

        foreach (var source in HostSources("MotionPathEditorDialog.cs"))
        {
            source.Should().Contain("MotionPathEditorDialogVisualMetrics.KindWidth");
            source.Should().Contain("MotionPathEditorDialogVisualMetrics.RowBorderColor");
            source.Should().NotContain("_kind.Width = 7");
            source.Should().NotContain("LightGray");
            source.Should().NotContain("0xDD, 0xDD");
        }

        Read("Host", "HeaderFooterDialog.cs").Should().Contain("HeaderFooterDialogVisualMetrics.WpfFooterCheckBottomMargin");
        Read("Avalonia", "HeaderFooterDialog.cs").Should().Contain("HeaderFooterDialogVisualMetrics.AvaloniaFooterCheckBottomMargin");
    }

    [Fact]
    public void MediaControllers_ExposeOnlyTheRequestBasedEnterSlide()
    {
        foreach (var source in HostSources("SlideShowMediaController.cs", "AvaloniaSlideShowMediaController.cs"))
        {
            source.Should().Contain("EnterSlide(");
            source.Should().Contain("SlideShowMediaNativeEntryRequest request");
            source.Should().NotContain("bool showMediaControls = true");
            source.Should().NotContain("int? preferredCaptionSlideIndex = null");
        }
    }

    private static string Read(string host, params string[] relativeParts) =>
        TestWorkspaceFileLocator.ReadAllText(
            ["freep", host == "Host" ? "FreeP.App.Host" : "FreeP.App.Avalonia", .. relativeParts]);

    private static IEnumerable<string> HostSources(string hostFile, string avaloniaFile)
    {
        yield return Read("Host", hostFile.Split('/'));
        yield return Read("Avalonia", avaloniaFile.Split('/'));
    }

    private static IEnumerable<string> HostSources(string fileName) => HostSources(fileName, fileName);
}
