using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class TestReliabilityPreflightTests
{
    [Fact]
    public void UiE2eTests_UseExplicitOptInSkipPreconditions()
    {
        var preconditions = File.ReadAllText(WorkspaceFileLocator.Find("tests", "FreeX.App.Host.Tests", "UiE2ePreconditions.cs"));
        var formulaE2e = File.ReadAllText(WorkspaceFileLocator.Find("tests", "FreeX.App.Host.Tests", "FormulaEditingUiE2eTests.cs"));
        var uiaSnapshot = File.ReadAllText(WorkspaceFileLocator.Find("tests", "FreeX.App.Host.Tests", "UiAutomationCatalogSnapshotTests.cs"));

        preconditions.Should().Contain("FREEX_UIE2E");
        preconditions.Should().Contain("SkipException.ForSkip");
        formulaE2e.Should().Contain("UiE2ePreconditions.SkipUnlessEnabled();");
        formulaE2e.Should().Contain("UiAutomationCatalogSnapshotHarness.Run(run)");
        uiaSnapshot.Should().NotContain("UiE2ePreconditions.SkipUnlessEnabled();");
        formulaE2e.Should().NotMatchRegex(@"if \(!OperatingSystem\.IsWindows\(\)\)\s*return;");
        uiaSnapshot.Should().NotMatchRegex(@"!Environment\.UserInteractive\)\s*return;");
    }

    [Fact]
    public void HeavyWorkbookRetest_ReportsMissingWorkbookAsSkippedPrecondition()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("tests", "FreeX.App.Host.Tests", "HeavyWorkbookRetestTests.cs"));

        source.Should().Contain("FREEX_HEAVY_WORKBOOK_PATH");
        source.Should().Contain("SkipException.ForSkip");
        source.Should().Contain("FREEX_HEAVY_WORKBOOK_PATH does not point to an existing workbook.");
        source.Should().NotMatchRegex(@"if \(sourcePath is null\)\s*return;");
    }
}
