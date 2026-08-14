namespace FreeP.App.Compositor.Tests;

public sealed class PresentationSelectionPaneHostParityTests
{
    [Theory]
    [InlineData("FreeP.App.Host")]
    [InlineData("FreeP.App.Avalonia")]
    public void Native_selection_panes_refresh_shared_accessibility_after_every_render(string project)
    {
        var root = RepositoryRoot();
        var projectDirectory = Path.Combine(root, "freep", project);
        var paneSource = File.ReadAllText(Path.Combine(projectDirectory, "SelectionPane.cs"));
        var mainWindowSource = File.ReadAllText(Path.Combine(projectDirectory, "MainWindow.cs"));

        paneSource.Should().Contain("PresentationSelectionPaneFormSession<");
        paneSource.Should().Contain("Action? onAccessibilityChanged = null");
        paneSource.Should().Contain("onAccessibilityChanged);");
        paneSource.Should().NotContain("private PresentationSelectionPanePlan Render(");
        mainWindowSource.Should().Contain(
            "new SelectionPane(Editor, RefreshPaneAccessibilityMetadata)");
        mainWindowSource.Should().Contain("_proofingPaneHost is null");
        mainWindowSource.Should().Contain("_selectionPane is null");
    }

    private static string RepositoryRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");
}
