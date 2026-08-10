namespace Free.Shared.AppServices.Tests;

public sealed class PlatformClipboardOwnershipSourceTests
{
    [Fact]
    public void NeutralContract_ContainsNoToolkitTypes()
    {
        var source = Read("shared", "Free.Shared.AppServices", "IPlatformClipboard.cs");

        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia.");
        source.Should().NotContain("IDataObject");
        source.Should().NotContain("BitmapSource");
        source.Should().Contain("PlatformClipboardReadStatus");
        source.Should().Contain("Unavailable");
        source.Should().Contain("Unsupported");
    }

    [Fact]
    public void RequestedHosts_DoNotAccessNativeClipboardsDirectly()
    {
        var boundedFiles = new[]
        {
            Read("src", "FreeX.App.Host", "MainWindow.ClipboardCommands.cs"),
            Read("src", "FreeX.App.Host", "MainWindow.ReviewCommands.cs"),
            Read("src", "FreeX.App.Host", "WorkbookStatisticsDialog.cs"),
            Read("src", "FreeX.App.Avalonia", "MainWindow.cs"),
            Read("src", "FreeX.App.Avalonia", "MainWindow.HelpCommands.cs"),
            Read("freew", "FreeW.App.Host", "Editing", "PaginatedEditorPanel.cs"),
            Read("freew", "FreeW.App.Host", "Editing", "DocumentView.cs"),
            Read("freew", "FreeW.App.Host", "PasteSpecialDialog.cs"),
            Read("freew", "FreeW.App.Host", "MainWindow.cs"),
            Read("freew", "FreeW.App.Host", "ThesaurusPane.cs"),
            Read("freew", "FreeW.App.Avalonia", "MainWindow.cs"),
            Read("freew", "FreeW.App.Avalonia", "MainWindow.HelpCommands.cs"),
            Read("freew", "FreeW.App.Avalonia", "ThesaurusPane.cs"),
            Read("freep", "FreeP.App.Host", "OsClipboardService.cs"),
            Read("freep", "FreeP.App.Avalonia", "PresentationClipboardService.cs"),
        };

        foreach (var source in boundedFiles)
        {
            SourceWithoutComments(source).Should().NotContain("System.Windows.Clipboard");
            SourceWithoutComments(source).Should().NotContain(".Clipboard.SetTextAsync(");
            SourceWithoutComments(source).Should().NotContain(".Clipboard.TryGetTextAsync(");
            SourceWithoutComments(source).Should().NotContain("clipboard.TryGetDataAsync(");
            SourceWithoutComments(source).Should().NotContain("clipboard.TryGetBitmapAsync(");
        }
    }

    [Fact]
    public void ToolkitAdapters_OwnNativeClipboardCalls()
    {
        var wpf = Read("shared", "Free.Shared.Shell.Wpf", "WpfPlatformClipboard.cs");
        var avalonia = Read("shared", "Free.Shared.Shell.Avalonia", "AvaloniaPlatformClipboard.cs");
        var freePHost = Read("freep", "FreeP.App.Host", "OsClipboardService.cs");
        var freePAvalonia = Read(
            "freep",
            "FreeP.App.Avalonia",
            "PresentationClipboardService.cs");

        wpf.Should().Contain("class WpfPlatformClipboard : IPlatformClipboard");
        avalonia.Should().Contain("class AvaloniaPlatformClipboard : IPlatformClipboard");
        wpf.Should().Contain("catch (OperationCanceledException)");
        avalonia.Should().Contain("catch (OperationCanceledException)");
        freePHost.Should().Contain("private readonly IPlatformClipboard _clipboard;");
        freePAvalonia.Should().Contain("IPlatformClipboard systemClipboard");
    }

    private static string SourceWithoutComments(string source) =>
        string.Join('\n', source.Split('\n').Where(line => !line.TrimStart().StartsWith("//")));

    private static string Read(params string[] parts) =>
        File.ReadAllText(TestWorkspaceFileLocator.FindFromWorkspaceRoot(parts));
}
