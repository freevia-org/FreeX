using Free.Shared.AppServices.Printing;

namespace Free.Shared.AppServices.Tests;

public sealed class PrinterSubmissionSelectionPolicyTests
{
    private static readonly PrinterDiscoveryResult Discovery = new(
        PrinterDiscoveryStatus.Available,
        [new PrinterInfo("Office"), new PrinterInfo("PDF")],
        "PDF");

    [Theory]
    [InlineData("office", "Office")]
    [InlineData("PDF", "PDF")]
    [InlineData("Missing", null)]
    [InlineData(" Office ", null)]
    public void Resolve_RequestedPrinterUsesCanonicalDiscoveredName(string requested, string? expected)
    {
        PrinterSubmissionSelectionPolicy.Resolve(requested, Discovery).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Resolve_NoRequestedPrinterUsesDiscoveryDefault(string? requested)
    {
        PrinterSubmissionSelectionPolicy.Resolve(requested, Discovery).Should().Be("PDF");
    }

    [Fact]
    public void Resolve_NoRequestedPrinterFallsBackToFirstDiscoveredPrinter()
    {
        var discovery = Discovery with { DefaultPrinter = null };

        PrinterSubmissionSelectionPolicy.Resolve(null, discovery).Should().Be("Office");
    }

    [Fact]
    public void Resolve_PreservesDiscoveryDefaultWithoutRevalidatingIt()
    {
        var discovery = Discovery with { DefaultPrinter = "Spooler Alias" };

        PrinterSubmissionSelectionPolicy.Resolve(null, discovery).Should().Be("Spooler Alias");
    }

    [Fact]
    public void PlatformPrintServicesDelegateSubmissionSelectionToSharedPolicy()
    {
        var windowsSource = File.ReadAllText(TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "shared", "Free.Shared.AppServices.Windows", "WindowsPrintService.cs"));
        var cupsSource = File.ReadAllText(TestWorkspaceFileLocator.FindFromWorkspaceRoot(
            "shared", "Free.Shared.AppServices", "Printing", "CupsPrintService.cs"));

        windowsSource.Should().Contain("PrinterSubmissionSelectionPolicy.Resolve")
            .And.NotContain("private static string? ResolvePrinter");
        cupsSource.Should().Contain("PrinterSubmissionSelectionPolicy.Resolve")
            .And.NotContain("private static string? ResolvePrinter");
    }
}
