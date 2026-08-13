using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class WpfPrintQueueOwnershipTests
{
    [Fact]
    public void FreeXWpfPrinting_UsesSharedQueueCatalogAndResolver()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var preview = Read(root, "src", "FreeX.App.Host", "WpfPrintPreviewToolbarPlanner.cs");
        var native = Read(root, "src", "FreeX.App.Host", "NativePrintDialogService.cs");
        var backstage = Read(root, "src", "FreeX.App.Host", "MainWindow.Backstage.cs");
        var shared = Read(root, "shared", "Free.Shared.Shell.Wpf", "WpfPrintQueueCatalog.cs");

        foreach (var source in new[] { preview, native, backstage })
        {
            source.Should().Contain("WpfPrintQueueCatalog");
            source.Should().NotContain("new LocalPrintServer");
            source.Should().NotContain("new System.Printing.LocalPrintServer");
            source.Should().NotContain("GetPrintQueues()");
        }

        native.Should().Contain("WpfPrintQueueResolutionFallback.CreateNamedQueue");
        backstage.Should().Contain("WpfPrintQueueCatalog.Resolve(settings.PrinterName)");
        shared.Should().Contain("public static class WpfPrintQueueCatalog");
        shared.Should().Contain("using var server = new LocalPrintServer()");
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
}
