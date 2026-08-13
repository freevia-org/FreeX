namespace FreeW.App.Avalonia.Tests.Printing;

public sealed class WindowsPrintServiceOwnershipTests
{
    [Fact]
    public void AvaloniaHost_SuppliesSharedBackendsToPortableSelector()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var appSource = File.ReadAllText(Path.Combine(root, "freew", "FreeW.App.Avalonia", "MainWindow.cs"));
        var formerAppOwner = Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Printing",
            "WindowsPrintService.cs");
        var windowsSource = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.AppServices.Windows",
            "WindowsPrintService.cs"));
        var selectorSource = File.ReadAllText(Path.Combine(
            root,
            "shared",
            "Free.Shared.AppServices",
            "Printing",
            "PlatformPrintServiceSelector.cs"));

        File.Exists(formerAppOwner).Should().BeFalse();
        appSource.Should().Contain("PlatformPrintServiceSelector.Select(");
        appSource.Should().Contain("windowsFactory: static () => new WindowsPrintService()");
        appSource.Should().Contain("cupsFactory: static () => new CupsPrintService()");
        appSource.Should().Contain("using Free.Shared.AppServices.Windows;");
        windowsSource.Should().Contain("public sealed class WindowsPrintService");
        windowsSource.Should().NotContain("PlatformPrintServiceFactory");
        selectorSource.Should().Contain("public static class PlatformPrintServiceSelector");
        selectorSource.Should().Contain("OperatingSystem.IsWindows()");
    }
}
