namespace FreeP.App.Compositor.Tests;

/// <summary>
/// Pins the shape of the WPF/Avalonia FreeP host dedup: logic that both shells used to carry as
/// identical copies lives once in the portable tier or the linked RendererShared folder.
/// </summary>
public sealed class FreePHostDedupSourceTests
{
    [Theory]
    [InlineData("FreeP.App.Host")]
    [InlineData("FreeP.App.Avalonia")]
    public void Main_window_builds_workflow_sessions_through_the_shared_factory(string project)
    {
        var source = File.ReadAllText(Path.Combine(RepositoryRoot(), "freep", project, "MainWindow.cs"));

        source.Should().Contain("PresentationHostSessionFactory.Create(");
        source.Should().NotContain("new PresentationSmartArtTextPaneSessionCallbacks(");
        source.Should().NotContain("new PresentationZoomAuthoringSessionCallbacks(");
        source.Should().NotContain("_notesPaneSession = new(");
        source.Should().NotContain("_customShowSession = new(");
    }

    [Fact]
    public void Session_factory_wires_the_host_delegates_into_every_session()
    {
        var dirty = 0;
        var refreshed = 0;
        var hostUpdates = 0;
        var chartOptions = 0;
        var presentation = new Presentation();
        var editor = new EditingSession(presentation, new PresentationCommandBus(presentation));
        var sessions = PresentationHostSessionFactory.Create(new PresentationHostSessionPorts(
            GetEditor: () => editor,
            MarkDirty: () => dirty++,
            RefreshCanvas: () => refreshed++,
            UpdateHost: () => hostUpdates++,
            RenderSlidePreview: (_, _, _, _) => [],
            DomainContextMenu: new PresentationDomainContextMenuSessionCallbacks(
                (_, _) => { },
                _ => { },
                _ => { },
                _ => { },
                _ => { },
                () => chartOptions++),
            RenderSmartArtTextPane: _ => { }));

        sessions.SmartArtTextPane.Should().NotBeNull();
        sessions.ZoomAuthoring.Should().NotBeNull();
        sessions.DomainContextMenu.Should().NotBeNull();
        sessions.NotesPane.Should().NotBeNull();
        sessions.HyperlinkWorkflow.Should().NotBeNull();
        sessions.CustomShow.Should().NotBeNull();
        sessions.AnimationPane.Should().NotBeNull();
        (dirty + refreshed + hostUpdates + chartOptions).Should().Be(0);
    }

    [Theory]
    [InlineData("FreeP.App.Host")]
    [InlineData("FreeP.App.Avalonia")]
    public void Review_facade_lives_in_the_linked_renderer_shared_partial(string project)
    {
        var root = RepositoryRoot();
        var facade = File.ReadAllText(Path.Combine(root, "freep", "RendererShared", "MainWindow.ReviewFacade.cs"));
        var host = File.ReadAllText(Path.Combine(root, "freep", project, "MainWindow.cs"));
        var csproj = File.ReadAllText(Path.Combine(root, "freep", project, project + ".csproj"));

        facade.Should().Contain("internal PresentationReadingOrderPlan ShowReadingOrderPane()");
        facade.Should().Contain("internal PresentationProofingPanePlan ShowProofingPane()");
        facade.Should().Contain("internal PresentationCommentMutationPlan AddComment(");
        csproj.Should().Contain("../RendererShared/MainWindow.ReviewFacade.cs");
        host.Should().NotContain("internal PresentationReadingOrderPlan ShowReadingOrderPane()");
        host.Should().NotContain("internal PresentationProofingPanePlan ShowProofingPane()");
        host.Should().NotContain("internal PresentationCommentMutationPlan AddComment(");
        host.Should().NotContain("_altTextPaneHostCoordinator.SetInput(");
    }

    [Fact]
    public void Avalonia_alt_text_pane_shows_the_validation_message_and_disabled_reason_like_wpf()
    {
        var root = RepositoryRoot();
        var wpf = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Host", "MainWindow.cs"));
        var avalonia = File.ReadAllText(Path.Combine(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs"));

        wpf.Should().Contain("_altTextDescriptionBox.ToolTip = plan.Description.ValidationMessage ?? plan.Description.Placeholder;");
        wpf.Should().Contain("_altTextApplyButton.ToolTip = plan.ApplyAction.DisabledReason;");
        avalonia.Should().Contain(
            "ToolTip.SetTip(_altTextDescriptionBox, plan.Description.ValidationMessage ?? plan.Description.Placeholder);");
        avalonia.Should().Contain("ToolTip.SetTip(_altTextApplyButton, plan.ApplyAction.DisabledReason);");
    }
    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
}
