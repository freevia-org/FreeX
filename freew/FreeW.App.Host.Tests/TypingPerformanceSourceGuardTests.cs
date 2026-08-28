using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class TypingPerformanceSourceGuardTests
{
    [Fact]
    public void OrdinaryTyping_UsesTheNativeEditorBeforeTheModelRenderingPath()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "Editing", "DocumentView.cs"));

        var nativePath = source.IndexOf("CanUseNativeUntrackedTextInput()", StringComparison.Ordinal);
        var modelPath = source.IndexOf("TryApplyBodyTextInput(e.Text)", StringComparison.Ordinal);

        nativePath.Should().BeGreaterThan(0);
        modelPath.Should().BeGreaterThan(nativePath);
    }

    [Fact]
    public void EditorChrome_RefreshesAfterTypingPauses()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var source = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Host", "MainWindow.cs"));

        source.Should().Contain("ScheduleEditorChromeRefresh(source)");
        source.Should().Contain("Interval = TimeSpan.FromMilliseconds(150)");
    }
}
