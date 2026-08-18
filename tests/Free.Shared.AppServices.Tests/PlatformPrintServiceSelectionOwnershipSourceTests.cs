namespace Free.Shared.AppServices.Tests;

public sealed class PlatformPrintServiceSelectionOwnershipSourceTests
{
    [Fact]
    public void AvaloniaHosts_DelegatePlatformSelectionToPortableOwner()
    {
        var selector = Read(
            "shared", "Free.Shared.AppServices", "Printing", "PlatformPrintServiceSelector.cs");
        var windowsService = Read(
            "shared", "Free.Shared.AppServices.Windows", "WindowsPrintService.cs");
        var freeW = Read("freew", "FreeW.App.Avalonia", "MainWindow.cs");
        var freeP = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var freeX = Read("src", "FreeX.App.Avalonia", "MainWindow.cs");

        selector.Should().Contain("public static class PlatformPrintServiceSelector");
        selector.Should().Contain("OperatingSystem.IsWindows()");
        selector.Should().Contain("Func<IPlatformPrintService>? windowsFactory");
        selector.Should().Contain("Func<IPlatformPrintService> cupsFactory");
        selector.Should().NotContain("WindowsPrintService")
            .And.NotContain("CupsPrintService");
        windowsService.Should().NotContain("PlatformPrintServiceFactory");

        foreach (var appSource in new[] { freeW, freeP, freeX })
        {
            appSource.Should().Contain("PlatformPrintServiceSelector.Select(");
            appSource.Should().NotContain("PlatformPrintServiceFactory");
        }

        freeW.Should().Contain("windowsFactory: static () => new WindowsPrintService()")
            .And.Contain("cupsFactory: static () => new CupsPrintService()");

        var freePSelection = Slice(
            freeP,
            "private static IPlatformPrintService CreatePlatformPrintService()",
            "private static Task<PrintSelection?> ShowPlatformPrintSelectionDialogAsync(");
        freePSelection.Should().NotContain("OperatingSystem.IsWindows()");
        // r140: this used to REQUIRE the two opt-outs, pinning the defect that made a failed print
        // report success. FreeP now takes WindowsPrintService's defaults like FreeW, so the contract
        // guards the safe wiring and forbids the opt-outs coming back.
        freePSelection.Should().Contain("windowsFactory: static () => new WindowsPrintService()")
            .And.Contain("cupsFactory: static () => new CupsPrintService()")
            .And.NotContain("RequirePrinterDiscoveryBeforeSubmission")
            .And.NotContain("RejectNonZeroHandlerExitCode");

        var freeXSelection = Slice(
            freeX,
            "private static IPlatformPrintService CreatePlatformPrintService()",
            "private Control BuildContent()");
        freeXSelection.Should().NotContain("OperatingSystem.IsWindows()");
        freeXSelection.Should().Contain("windowsFactory: null")
            .And.Contain("CupsPrinterDiscoveryMode.DestinationNames");
    }

    [Fact]
    public void PrintRenderingAndPackageGeneration_RemainProductOwned()
    {
        var selector = Read(
            "shared", "Free.Shared.AppServices", "Printing", "PlatformPrintServiceSelector.cs");
        var freeW = Read("freew", "FreeW.App.Avalonia", "MainWindow.cs");
        var freeP = Read("freep", "FreeP.App.Avalonia", "MainWindow.cs");
        var freeXPrint = Read("src", "FreeX.App.Avalonia", "MainWindow.Print.cs");

        selector.Should().NotContain("PrintOutputPackage")
            .And.NotContain("RenderPrintReadyPdfAsync")
            .And.NotContain("saveSelectedPrintPdf");
        freeW.Should().Contain("saveSelectedPrintPdf")
            .And.Contain("_savePrintPdf");
        freeP.Should().Contain("RefreshPrintOutputPackage")
            .And.Contain("BuildPrintOutputPackage");
        freeXPrint.Should().Contain("RenderPrintReadyPdfAsync");
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        end.Should().BeGreaterThan(start);
        return source[start..end];
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(TestWorkspaceFileLocator.FindFromWorkspaceRoot(parts));
}
