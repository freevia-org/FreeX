namespace FreeP.App.Compositor.Tests;

public sealed class PresentationWorkareaOwnershipSourceTests
{
    [Fact]
    public void MainWindowRenderersDelegatePresentationEditorAndLifecycleOwnershipToSharedSession()
    {
        foreach (var source in MainWindowSources())
        {
            source.Should().Contain("private readonly PresentationWorkareaSession _workareaSession;");
            source.Should().Contain("_workareaSession = new PresentationWorkareaSession(this);");
            source.Should().Contain("_workareaSession.ReplacePresentation(presentation);");
            source.Should().Contain("_workareaSession.ExecuteCommand");
            source.Should().NotContain("private void ExecuteKeyboardCommand(");
            source.Should().NotContain("case FreePKeyboardCommand.");
            source.Should().NotContain("new PresentationCommandBus(");
            source.Should().NotContain("new EditingSession(");
            source.Should().NotContain("_presentation = presentation;");
            source.Should().NotContain("Editor.Changed +=");
            source.Should().NotContain("Editor.CurrentSlideChanged +=");
            source.Should().NotContain("Editor.SelectionChanged +=");
            source.Should().NotContain("Editor.ActiveTableCellChanged +=");
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

        wpf.Should().Contain("ToWpfKey(")
            .And.NotContain("WpfClipboardCommands.Copy(Editor, _osClipboard)");
        avalonia.Should().Contain("TryMapKeyboardKey(")
            .And.NotContain("QueueClipboardCopy();");
        wpfEndpoint.Should().Contain("WpfClipboardCommands.Copy(Editor, _osClipboard)")
            .And.Contain("SlidePaneHost.Child = new SlidePane(context.Snapshot.Editor)");
        avaloniaEndpoint.Should().Contain(
                "PresentationWorkareaNativeCommand.Copy => QueueClipboardCopy")
            .And.Contain("RewireInteractionToEditor();");
    }

    [Fact]
    public void PortableWorkareaHasNoRendererDependenciesAndOwnsOperationPlans()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var source = Read(root, "freep", "FreeP.App.Presentation", "PresentationWorkareaSession.cs");

        source.Should().Contain("public static class PresentationWorkareaOperationPlanner")
            .And.Contain("public sealed class PresentationWorkareaSession : IDisposable")
            .And.Contain("editor.CurrentSlideChanged += HandleCurrentSlideChanged;")
            .And.Contain("editor.SelectionChanged += HandleSelectionChanged;")
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
