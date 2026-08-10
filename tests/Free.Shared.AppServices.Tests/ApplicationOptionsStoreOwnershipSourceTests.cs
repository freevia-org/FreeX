namespace Free.Shared.AppServices.Tests;

public sealed class ApplicationOptionsStoreOwnershipSourceTests
{
    [Fact]
    public void SharedTier_OwnsPersistentAndInMemoryOptionsStores()
    {
        var source = Read("shared", "Free.Shared.AppServices", "ApplicationOptionsSupport.cs");

        source.Should().Contain("interface IApplicationOptionsStore<T>");
        source.Should().Contain("class ApplicationOptionsStore<T> : IApplicationOptionsStore<T>");
        source.Should().Contain("class InMemoryApplicationOptionsStore<T> : IApplicationOptionsStore<T>");
    }

    [Theory]
    [InlineData("freew", "FreeW.App.Host", "FreeWOptions")]
    [InlineData("freew", "FreeW.App.Avalonia", "FreeWOptions")]
    [InlineData("freep", "FreeP.App.Host", "FreePOptions")]
    [InlineData("freep", "FreeP.App.Avalonia", "FreePOptions")]
    public void SisterHosts_UseSharedMemorySeamForIsolatedWindows(
        string appDirectory,
        string projectDirectory,
        string optionsType)
    {
        var source = Read(appDirectory, projectDirectory, "MainWindow.cs");

        source.Should().Contain($"IApplicationOptionsStore<{optionsType}>");
        source.Should().Contain($"InMemoryApplicationOptionsStore<{optionsType}>");
        source.Should().NotContain("settings.transient.json");
    }

    [Fact]
    public void ProductionBootstraps_RetainPersistentOptionsComposition()
    {
        var wpfRunner = Read("shared", "Free.Shared.Shell.Wpf", "WpfApplicationStartupRunner.cs");
        var freeWProgram = Read("freew", "FreeW.App.Host", "Program.cs");
        var freePProgram = Read("freep", "FreeP.App.Host", "Program.cs");
        var freeWApp = Read("freew", "FreeW.App.Avalonia", "App.cs");
        var freePApp = Read("freep", "FreeP.App.Avalonia", "App.cs");

        wpfRunner.Should().Contain("ApplicationOptionsStore<TOptions>.Create(");
        freeWProgram.Should().Contain("WpfApplicationStartupRunner.Run");
        freePProgram.Should().Contain("WpfApplicationStartupRunner.Run");

        freeWApp.Should().Contain("ApplicationOptionsStore<FreeWOptions>.Create(");
        freeWApp.Should().Contain("PlatformApplicationDataPathProvider.LocalInstance");
        freeWApp.Should().Contain("var loadedOptions = optionsStore.Load();");
        freeWApp.Should().Contain("new MainWindow(args, loadedOptions, optionsStore)");

        freePApp.Should().Contain("ApplicationOptionsStore<FreePOptions>.Create();");
        freePApp.Should().Contain("var options = optionsStore.Load();");
        freePApp.Should().Contain("optionsStore: optionsStore");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(TestWorkspaceFileLocator.FindFromWorkspaceRoot(parts));
}
