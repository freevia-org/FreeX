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
    public void MainWindowRenderersDelegateLiveOptionsMutationToSharedRuntimeSession()
    {
        foreach (var source in MainWindowSources())
        {
            source.Should().Contain("new FreeWOptionsRuntimeSession(_options)");
            source.Should().Contain("_optionsRuntime.EditorTypingOptions");
            source.Should().Contain("_optionsRuntime.Apply(edited)");
            source.Should().NotContain("_options.RecentFilesCap = edited.RecentFilesCap");
            source.Should().NotContain("_options.AutoCorrectEnabled = edited.AutoCorrectEnabled");
        }
    }

    [Fact]
    public void MainWindowRenderersUsePortableApplicationFrameText()
    {
        foreach (var source in MainWindowSources())
        {
            source.Should().Contain("FreeWApplicationFrameTextCatalog")
                .And.NotContain("\"Help Online\"")
                .And.NotContain("\"Feedback\"")
                .And.NotContain("\"Check for Updates\"")
                .And.NotContain("\"Read Mode\"")
                .And.NotContain("\"Print Layout\"")
                .And.NotContain("\"Web Layout\"")
                .And.NotContain("\"Draft\"")
                .And.NotContain("\"Page Edit\"")
                .And.NotContain("\"Previous pair\"")
                .And.NotContain("\"Next pair\"");
        }

        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var catalog = Read(
            root,
            "freew",
            "FreeW.App.Presentation",
            "Shell",
            "FreeWApplicationFrameTextCatalog.cs");
        catalog.Should().Contain("public static class FreeWApplicationFrameTextCatalog")
            .And.NotContain("System.Windows")
            .And.NotContain("Avalonia");
    }

    [Fact]
    public void MainWindowRenderersUsePortableApplicationFrameTitle()
    {
        foreach (var source in MainWindowSources())
        {
            source.Should().Contain("Title = FreeWApplicationFrameDescriptor.Title.ApplicationName;")
                .And.NotContain("Title = \"FreeW\";");
        }

        var avaloniaSource = MainWindowSources().Last();
        avaloniaSource.Should().Contain("Separator: FreeWApplicationFrameDescriptor.Title.Separator")
            .And.Contain("DirtyMarker: FreeWApplicationFrameDescriptor.Title.DirtyMarker")
            .And.Contain("UntitledDisplayName: FreeWApplicationFrameDescriptor.Title.DefaultDocumentDisplayName")
            .And.NotContain("private const string DefaultTitle");
    }

    [Fact]
    public void MainWindowRenderersDelegateDataFolderAndDesktopUriPolicies()
    {
        foreach (var source in MainWindowSources())
        {
            source.Should().Contain("FreeWApplicationFrameDescriptor.ResolveDataFolderLabel");
            source.Should().Contain("DesktopExternalUriLauncher.Open(");
            source.Should().NotContain("private static string ResolveDataFolderLabel");
            source.Should().NotContain("private string ResolveDataFolderLabel");
            source.Should().NotContain("uri => Process.Start(");
            source.Should().NotContain("uri => System.Diagnostics.Process.Start(");
        }

        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var descriptor = Read(
            root,
            "freew",
            "FreeW.App.Presentation",
            "Shell",
            "FreeWApplicationFrameDescriptor.cs");
        descriptor.Should().Contain("AppStoragePathPlanner.GetApplicationDataDirectoryLabelOrFallback(pathProvider)")
            .And.Contain("AppStoragePathPlanner.GetApplicationDataDirectoryLabelOrFallback(")
            .And.NotContain("Path.GetDirectoryName(optionsStorePath)")
            .And.NotContain("System.Windows")
            .And.NotContain("Avalonia");
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
            source.Should().Contain("BackstageActionBinder.DismissBefore(");
            source.Should().NotContain("BackstagePaneSurfacePlanner.Build");
            source.Should().NotContain("SisterBackstageInfoPanePlanner.Build");
            source.Should().NotContain("BackstageInfoSafetyPanePlanner.Build");
            source.Should().NotContain("private Action DismissThen");
            source.Should().NotContain("private Action HideThen");
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
