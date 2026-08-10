using System.IO;

namespace FreeP.App.Presentation.Tests;

public sealed class PrintPlatformDedupSourceTests
{
    [Fact]
    public void WindowsPrinterAndShellPolicies_HaveOneSharedOwner()
    {
        var root = FindWorkspaceRoot();
        var freeP = Read(root, "freep", "FreeP.App.Recording.Windows", "WindowsNativePrintHandoff.cs");
        var freeW = Read(root, "freew", "FreeW.App.Avalonia", "Printing", "WindowsPrintService.cs");
        var catalog = Read(root, "shared", "Free.Shared.AppServices.Windows", "WindowsPrinterCatalog.cs");
        var handoff = Read(root, "shared", "Free.Shared.AppServices.Windows", "WindowsShellPdfPrintHandoff.cs");

        foreach (var client in new[] { freeP, freeW })
        {
            client.Should().NotContain("EnumPrinters(");
            client.Should().NotContain("GetDefaultPrinter(");
            client.Should().NotContain("Verb = \"printto\"");
            client.Should().NotContain("TimeSpan.FromSeconds(8)");
            client.Should().NotContain("ProcessStartInfo");
        }

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
        var wpfCommands = Read(root, "freep", "FreeP.App.Host", "FileCommands.cs");
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

    private static string FindWorkspaceRoot() =>
        TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeP.slnx");

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
}
