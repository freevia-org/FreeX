namespace FreeP.App.Compositor.Tests;

public sealed class PresentationWorkareaOwnershipSourceTests
{
    [Fact]
    public void MainWindowRenderersDelegatePresentationEditorAndLifecycleOwnershipToSharedSession()
    {
        foreach (var source in MainWindowSources())
        {
            source.Should().Contain("private readonly PresentationWorkareaSession _workareaSession;");
            source.Should().Contain(
                "_workareaSession = new PresentationWorkareaSession(CreateWorkareaEndpoint());");
            source.Should().Contain("_workareaSession.ReplacePresentation(presentation);");
            source.Should().Contain("_workareaSession.ExecuteCommand");
            source.Should().Contain("_workareaSession.CanOpenDomainDialog(");
            source.Should().NotContain("private void ExecuteKeyboardCommand(");
            source.Should().NotContain("case FreePKeyboardCommand.");
            source.Should().NotContain("new PresentationCommandBus(");
            source.Should().NotContain("new EditingSession(");
            source.Should().NotContain("_presentation = presentation;");
            source.Should().NotContain("Editor.Changed +=");
            source.Should().NotContain("Editor.CurrentSlideChanged +=");
            source.Should().NotContain("Editor.SelectionChanged +=");
            source.Should().NotContain("Editor.ActiveTableCellChanged +=");
            source.Should().NotContain("Editor.CanEditSelectedChartData");
            source.Should().NotContain("Editor.CanEditSelectedChartFormatting");
            source.Should().NotContain("ChartExSeriesLayoutPlanner.CanEdit(Editor.SelectedChart)");
            source.Should().NotContain("Editor.SelectedChart is not { ChartType:");
        }
    }

    [Fact]
    public void RendererEndpointsRetainOnlyNativeControlAndServiceRealization()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpf = Read(root, "freep", "FreeP.App.Host", "MainWindow.cs");
        var avalonia = Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var wpfEndpoint = Read(root, "freep", "FreeP.App.Host", "MainWindow.WorkareaEndpoint.cs");
        var avaloniaEndpoint = Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.WorkareaEndpoint.cs");
        var chartEndpoints = Read(root, "freep", "RendererShared", "MainWindow.ChartDialogEndpoints.cs");

        wpf.Should().Contain("ToWpfKey(")
            .And.Contain("private void ShowDomainDialog(Window dialog)")
            .And.NotContain("new ChartDataDialog(Editor)")
            .And.NotContain("WpfClipboardCommands.Copy(Editor, _osClipboard)");
        avalonia.Should().Contain("TryMapKeyboardKey(")
            .And.Contain("private void ShowDomainDialog(Window dialog)")
            .And.NotContain("new ChartDataDialog(Editor)")
            .And.Contain("LastCustomSlideSizeInitialState = dialog.InitialState;")
            .And.Contain("LastHeaderFooterState = dialog.InitialState;")
            .And.NotContain("SlideSizeDialogPlanner.BuildInitialState(")
            .And.NotContain("LastHeaderFooterState = HeaderFooterCommandPlanner.BuildState(Editor)")
            .And.NotContain("QueueClipboardCopy();");
        chartEndpoints.Should().Contain(
                "OpenChartDialog(PresentationDomainDialogKind.ChartData, () => new ChartDataDialog(Editor))")
            .And.Contain("ShowDomainDialog(createDialog())")
            .And.Contain("_workareaSession.CanOpenDomainDialog(kind)");
        wpfEndpoint.Should().Contain("Copy = () => _osClipboard.Copy(Editor)")
            .And.Contain("RefreshSlidePane = RefreshSlidePane")
            .And.Contain("RefreshCurrentSlideStatus = UpdateSlideCount")
            .And.NotContain("SlidePaneHost.Child = new SlidePane(context.Snapshot.Editor)");
        avaloniaEndpoint.Should().Contain(
                "Copy = QueueClipboardCopy")
            .And.Contain("RewireInteractionToEditor();")
            .And.Contain("RefreshCurrentSlideStatus = UpdateStatus")
            .And.NotContain("SlidePanePlanner.SetSelectedSlide(");
    }

    [Fact]
    public void RendererEndpointProfilesContainDelegatesButNoDispatchPolicy()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        foreach (var endpoint in new[]
                 {
                     Read(root, "freep", "FreeP.App.Host", "MainWindow.WorkareaEndpoint.cs"),
                     Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.WorkareaEndpoint.cs"),
                 })
        {
            endpoint.Should().Contain("new PresentationWorkareaEndpoint(new PresentationWorkareaEndpointProfile")
                .And.Contain("new PresentationWorkareaOperationEndpoints")
                .And.Contain("new PresentationWorkareaNativeCommandEndpoints")
                .And.NotContain("PresentationWorkareaPaneEndpoints")
                .And.NotContain("switch")
                .And.NotContain("IPresentationWorkareaEndpoint.")
                .And.NotContain("PresentationWorkareaOperation.")
                .And.NotContain("PresentationWorkareaNativeCommand.");
        }
    }

    [Fact]
    public void BothRenderersProjectAnimationPaneLifecycleFromTheSharedWorkareaSession()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpfMain = Read(root, "freep", "FreeP.App.Host", "MainWindow.cs");
        var wpfPane = Read(root, "freep", "FreeP.App.Host", "AnimationPane.cs");

        foreach (var endpoint in new[]
                 {
                     Read(root, "freep", "FreeP.App.Host", "MainWindow.WorkareaEndpoint.cs"),
                     Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.WorkareaEndpoint.cs"),
                 })
        {
            endpoint.Should().Contain("ResetAnimationSession =")
                .And.Contain("ResetAnimationSelection =")
                .And.Contain("RefreshAnimationPaneAfterEditorChanged =")
                .And.Contain("RefreshAnimationPaneAfterNavigation =")
                .And.Contain("RefreshAnimationPaneAfterSelection =")
                .And.Contain("RefreshAnimationPaneAfterPresentationChanged =");
        }

        wpfMain.Should().Contain("private readonly AnimationPaneSession _animationPaneSession;")
            .And.Contain("_animationPaneSession = new(() => Editor);")
            .And.Contain("new AnimationPane(")
            .And.Contain("_animationPaneSession,");
        wpfPane.Should().Contain("AnimationPaneSession session")
            .And.NotContain("new AnimationPaneSession(")
            .And.NotContain("CurrentSlideChanged +=")
            .And.NotContain("Changed             +=");
    }

    [Fact]
    public void PortableWorkareaHasNoRendererDependenciesAndOwnsOperationPlans()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = Read(root, "freep", "FreeP.App.Presentation", "PresentationWorkareaSession.cs");
        var dispatcher = Read(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationWorkareaEndpointDispatcher.cs");

        source.Should().Contain("public static class PresentationWorkareaOperationPlanner")
            .And.Contain("public sealed class PresentationWorkareaSession : IDisposable")
            .And.Contain("public PresentationSlidePaneSession SlidePaneSession { get; }")
            .And.Contain("public PresentationWorkareaPaneSession Panes { get; } = new();")
            .And.Contain("Panes.IsVisible(PresentationWorkareaPane.SmartArtText)")
            .And.Contain("PresentationDomainDialogLaunchPlanner.CanOpen(Editor, dialogKind)")
            .And.Contain("editor.CurrentSlideChanged += HandleCurrentSlideChanged;")
            .And.Contain("editor.SelectionChanged += HandleSelectionChanged;")
            .And.NotContain("System.Windows")
            .And.NotContain("Avalonia");

        dispatcher.Should().Contain("public static class PresentationWorkareaEndpointDispatcher")
            .And.Contain("PresentationWorkareaOperation.BindEditor =>")
            .And.Contain("PresentationWorkareaNativeCommand.Copy =>")
            .And.NotContain("PresentationWorkareaPaneEndpoints")
            .And.NotContain("System.Windows")
            .And.NotContain("Avalonia");

        var paneSession = Read(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationWorkareaPaneSession.cs");
        paneSession.Should().Contain("public sealed class PresentationWorkareaPaneSession")
            .And.Contain("PresentationWorkareaPaneVisibilityPolicy.RequestedOrContent")
            .And.Contain("public PresentationWorkareaPaneTransitionPlan Show(")
            .And.Contain("public PresentationWorkareaPaneTransitionPlan Hide(")
            .And.NotContain("System.Windows")
            .And.NotContain("Avalonia");

        var dialogLaunch = Read(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationDomainDialogLaunchPlanner.cs");
        dialogLaunch.Should().Contain("public static class PresentationDomainDialogLaunchPlanner")
            .And.Contain("ChartExSeriesLayoutPlanner.CanEdit(editor.SelectedChart)")
            .And.NotContain("System.Windows")
            .And.NotContain("Avalonia");

        var slidePane = Read(
            root,
            "freep",
            "FreeP.App.Presentation",
            "PresentationSlidePaneSession.cs");
        slidePane.Should().Contain("public sealed class PresentationSlidePaneSession")
            .And.Contain("public SlidePaneSessionChangePlan ApplyNativeSelection(")
            .And.Contain("editor.Bus.Execute(new BatchCommand(\"Duplicate Slides\"")
            .And.Contain("editor.Bus.Execute(new BatchCommand(\"Delete Slides\"")
            .And.Contain("editor.Bus.Execute(new BatchCommand(\"Move Slides\"")
            .And.NotContain("System.Windows")
            .And.NotContain("Avalonia");
    }

    private static IEnumerable<string> MainWindowSources()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        yield return Read(root, "freep", "FreeP.App.Host", "MainWindow.cs");
        yield return Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs");
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
}
