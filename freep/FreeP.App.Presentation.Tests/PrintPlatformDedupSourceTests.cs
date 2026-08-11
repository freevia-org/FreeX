using System.IO;

namespace FreeP.App.Compositor.Tests;

public sealed class PrintPlatformDedupSourceTests
{
    [Fact]
    public void WindowsPrinterAndShellPolicies_HaveOneSharedOwner()
    {
        var root = FindWorkspaceRoot();
        var freeP = Read(root, "freep", "FreeP.App.Recording.Windows", "WindowsNativePrintHandoff.cs");
        var sharedService = Read(root, "shared", "Free.Shared.AppServices.Windows", "WindowsPrintService.cs");
        var catalog = Read(root, "shared", "Free.Shared.AppServices.Windows", "WindowsPrinterCatalog.cs");
        var handoff = Read(root, "shared", "Free.Shared.AppServices.Windows", "WindowsShellPdfPrintHandoff.cs");
        var formerFreeWOwner = Path.Combine(
            root,
            "freew",
            "FreeW.App.Avalonia",
            "Printing",
            "WindowsPrintService.cs");

        File.Exists(formerFreeWOwner).Should().BeFalse();
        freeP.Should().Contain("IPlatformPrintService");
        freeP.Should().Contain("new WindowsPrintService(");
        freeP.Should().NotContain("IWindowsPdfPrintHandoff");
        freeP.Should().NotContain("EnumPrinters(");
        freeP.Should().NotContain("GetDefaultPrinter(");
        freeP.Should().NotContain("Verb = \"printto\"");
        freeP.Should().NotContain("ProcessStartInfo");

        sharedService.Should().Contain("class WindowsPrintService");
        sharedService.Should().Contain("class PlatformPrintServiceFactory");
        catalog.Should().Contain("class WindowsPrinterCatalog");
        catalog.Should().Contain("EnumPrinters(");
        catalog.Should().Contain("GetDefaultPrinter(");
        handoff.Should().Contain("class WindowsShellPdfPrintHandoff");
        handoff.Should().Contain("Verb = \"printto\"");
        handoff.Should().Contain("TimeSpan.FromSeconds(8)");
        handoff.Should().Contain("WindowStyle = ProcessWindowStyle.Hidden");
    }

    [Fact]
    public void FreePPrintJobAndFailureText_HavePortableOwners()
    {
        var root = FindWorkspaceRoot();
        var wpfPrint = Read(root, "freep", "FreeP.App.Host", "WpfPresentationPrintService.cs");
        var wpfCommands = Read(
            root, "freep", "FreeP.App.Host", "WpfPresentationFileCommandPorts.cs");
        var wpfWindow = Read(root, "freep", "FreeP.App.Host", "MainWindow.cs");
        var avaloniaWindow = Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var avaloniaPorts = Read(root, "freep", "FreeP.App.Avalonia", "MainWindow.FileCommandPorts.cs");
        var linuxOutput = Read(root, "freep", "FreeP.App.Recording", "Recording", "LinuxNativeOutput.cs");

        wpfPrint.Should().NotContain("BuildDocumentName");
        wpfPrint.Should().NotContain("\"FreeP slides\"");
        wpfPrint.Should().NotContain("\"FreeP notes pages\"");
        wpfPrint.Should().NotContain("\"FreeP handouts\"");
        wpfPrint.Should().Contain("PresentationFileTextResources.NormalizePrintJobName(suggestedPrintJobName)");
        wpfCommands.Should().Contain("handoffPlan.SuggestedPrintJobName");

        wpfWindow.Should().NotContain("Text = \"Accessibility\"");
        avaloniaWindow.Should().NotContain("Text = \"Accessibility\"");
        wpfCommands.Should().NotContain("?? \"Video export failed.\"");
        avaloniaPorts.Should().NotContain("?? \"Video export failed.\"");
        avaloniaWindow.Should().NotContain("?? \"Video export failed.\"");
        avaloniaWindow.Should().NotContain("?? \"FreeP presentation\"");
        linuxOutput.Should().NotContain("? \"FreeP presentation\"");
    }

    [Fact]
    public void WpfQueueDiscovery_HasOneSharedOwner()
    {
        var root = FindWorkspaceRoot();
        var freeP = Read(root, "freep", "FreeP.App.Host", "WpfPresentationPrintService.cs");
        var freeXPreview = Read(root, "src", "FreeX.App.Host", "WpfPrintPreviewToolbarPlanner.cs");
        var freeXNative = Read(root, "src", "FreeX.App.Host", "NativePrintDialogService.cs");
        var freeXBackstage = Read(root, "src", "FreeX.App.Host", "MainWindow.Backstage.cs");
        var shared = Read(root, "shared", "Free.Shared.Shell.Wpf", "WpfPrintQueueCatalog.cs");

        foreach (var client in new[] { freeP, freeXPreview, freeXNative, freeXBackstage })
        {
            client.Should().Contain("WpfPrintQueueCatalog");
            client.Should().NotContain("new LocalPrintServer");
            client.Should().NotContain("new System.Printing.LocalPrintServer");
            client.Should().NotContain("GetPrintQueues()");
        }

        shared.Should().Contain("new LocalPrintServer()");
        shared.Should().Contain("GetPrintQueues()");
        shared.Should().Contain("WpfPrintQueueResolutionFallback");
    }

    private static string FindWorkspaceRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
}
