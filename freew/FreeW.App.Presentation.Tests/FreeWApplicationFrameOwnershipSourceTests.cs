namespace FreeW.App.Presentation.Tests;

public sealed class FreeWApplicationFrameOwnershipSourceTests
{
    [Fact]
    public void MainWindowRenderersDelegateApplicationCommandMeaningToSharedRouter()
    {
        foreach (var source in MainWindowSources())
        {
            source.Should().Contain("_applicationCommands = new FreeWApplicationCommandRouter(");
            source.Should().Contain("_applicationCommands.Execute(FreeWKeyboardCommand.SaveDocument)");
            source.Should().NotContain("case FreeWKeyboardCommand.");
        }
    }

    [Fact]
    public void BackstageRenderersDelegatePaneSemanticsToSharedSession()
    {
        foreach (var source in BackstageSources())
        {
            source.Should().Contain("_session = new FreeWBackstageSession(");
            source.Should().Contain("_session.BuildInfoPane()");
            source.Should().Contain("_session.BuildPrintPane()");
            source.Should().Contain("_session.SaveInline(");
            source.Should().NotContain("BackstagePaneSurfacePlanner.Build");
            source.Should().NotContain("SisterBackstageInfoPanePlanner.Build");
            source.Should().NotContain("BackstageInfoSafetyPanePlanner.Build");
        }
    }

    [Fact]
    public void RenderersRetainNativeInputWindowAndControlProjection()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var wpfMain = Read(root, "freew", "FreeW.App.Host", "MainWindow.cs");
        var avaloniaMain = Read(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs");
        var wpfBackstage = Read(root, "freew", "FreeW.App.Host", "Backstage", "BackstageView.cs");
        var avaloniaBackstage = Read(root, "freew", "FreeW.App.Avalonia", "Backstage", "BackstageView.cs");

        wpfMain.Should().Contain("ToWpfKey(").And.Contain("ExecuteEditingCommand(ApplicationCommands.Cut)");
        avaloniaMain.Should().Contain("TryMapKeyboardKey(").And.Contain("CutAsync()");
        wpfBackstage.Should().Contain("new TextBox").And.Contain("SisterBackstageHostController");
        avaloniaBackstage.Should().Contain("new TextBox").And.Contain("new AvaloniaBackstageFrame(");
    }

    [Fact]
    public void AvaloniaLocalBackstageModelsWereRemovedAfterContractMigration()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");

        File.Exists(Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Backstage",
            "BackstageModels.cs")).Should().BeFalse();
    }

    private static IEnumerable<string> MainWindowSources()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        yield return Read(root, "freew", "FreeW.App.Host", "MainWindow.cs");
        yield return Read(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs");
    }

    private static IEnumerable<string> BackstageSources()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        yield return Read(root, "freew", "FreeW.App.Host", "Backstage", "BackstageView.cs");
        yield return Read(root, "freew", "FreeW.App.Avalonia", "Backstage", "BackstageView.cs");
    }

    private static string Read(string root, params string[] relativeParts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(relativeParts).ToArray()));
}
