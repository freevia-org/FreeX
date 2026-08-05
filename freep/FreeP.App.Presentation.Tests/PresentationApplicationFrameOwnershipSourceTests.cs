namespace FreeP.App.Compositor.Tests;

public sealed class PresentationApplicationFrameOwnershipSourceTests
{
    [Fact]
    public void MainWindowRenderersDelegateCommandMeaningAndEditorLifecycleToSharedSession()
    {
        foreach (var source in MainWindowSources())
        {
            source.Should().Contain("_applicationFrameSession = new PresentationApplicationFrameSession(");
            source.Should().Contain("_applicationFrameSession?.Attach(Editor);");
            source.Should().Contain("_applicationFrameSession!.ExecuteCommand");
            source.Should().NotContain("private void ExecuteKeyboardCommand(");
            source.Should().NotContain("case FreePKeyboardCommand.");
            source.Should().NotContain("Editor.Changed +=");
            source.Should().NotContain("Editor.CurrentSlideChanged +=");
            source.Should().NotContain("Editor.SelectionChanged +=");
            source.Should().NotContain("Editor.ActiveTableCellChanged +=");
        }
    }

    [Fact]
    public void MainWindowRenderersRetainOnlyNativeShortcutAndClipboardAdaptation()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
        var wpf = Read(root, "freep", "FreeP.App.Host", "MainWindow.cs");
        var avalonia = Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs");

        wpf.Should().Contain("ToWpfKey(")
            .And.Contain("WpfClipboardCommands.Copy(Editor, _osClipboard)");
        avalonia.Should().Contain("TryMapKeyboardKey(")
            .And.Contain("QueueClipboardCopy");
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
