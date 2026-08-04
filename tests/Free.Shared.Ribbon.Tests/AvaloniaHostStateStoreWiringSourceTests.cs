namespace Free.Shared.Ribbon.Tests;

public sealed class AvaloniaHostStateStoreWiringSourceTests
{
    [Fact]
    public void EveryProductionAvaloniaHost_OwnsAndPassesTheSharedRibbonStateStore()
    {
        var freeX = NormalizeLineEndings(File.ReadAllText(RepoFile("src/FreeX.App.Avalonia/MainWindow.cs")));
        var freeW = NormalizeLineEndings(File.ReadAllText(RepoFile("freew/FreeW.App.Avalonia/MainWindow.cs")));
        var freeP = NormalizeLineEndings(File.ReadAllText(RepoFile("freep/FreeP.App.Avalonia/MainWindow.cs")));

        Assert.Contains("private readonly RibbonStateStore _ribbonStateStore = new();", freeX);
        Assert.Contains("_ribbonContextSource,\n            _ribbonStateStore", freeX);

        Assert.Contains("private readonly RibbonStateStore _ribbonStateStore = new();", freeW);
        Assert.Contains("stateStore: _ribbonStateStore", freeW);
        Assert.Contains("_ribbonStateStore);", freeW);

        Assert.Contains("private readonly RibbonStateStore _ribbonStateStore = new();", freeP);
        Assert.Contains("stateStore: _ribbonStateStore", freeP);
        Assert.Contains("_ribbonStateStore);", freeP);
    }

    private static string RepoFile(string relativePath) =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../", relativePath));

    private static string NormalizeLineEndings(string source) =>
        source.Replace("\r\n", "\n", StringComparison.Ordinal);
}
