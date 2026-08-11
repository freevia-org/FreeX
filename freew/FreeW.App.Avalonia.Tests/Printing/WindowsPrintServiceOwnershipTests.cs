namespace FreeW.App.Avalonia.Tests.Printing;

public sealed class WindowsPrintServiceOwnershipTests
{
    [Fact]
    public void AvaloniaHost_ConsumesSharedWindowsPrintServiceFactory()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var appSource = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs"));
        var formerAppOwner = Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Printing",
            "WindowsPrintService.cs");
        var sharedSource = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.AppServices.Windows",
            "WindowsPrintService.cs"));

        File.Exists(formerAppOwner).Should().BeFalse();
        appSource.Should().Contain("PlatformPrintServiceFactory.Create()");
        appSource.Should().Contain("using Free.Shared.AppServices.Windows;");
        sharedSource.Should().Contain("public sealed class WindowsPrintService");
        sharedSource.Should().Contain("public static class PlatformPrintServiceFactory");
    }
}
