using FluentAssertions;

namespace FreeX.App.Presentation.Tests;

public sealed class ReachabilityOwnershipTests
{
    [Fact]
    public void ShippingAssemblies_DoNotContainRetiredTestOnlyOrSupersededTypes()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var retiredPaths = new[]
        {
            Path.Combine("src", "FreeX.App.Presentation", "ConditionalFormatting", "ConditionalFormatRangeSelector.cs"),
            Path.Combine("src", "FreeX.App.Presentation", "ConditionalFormatting", "ConditionalFormatRulePlanner.cs"),
            Path.Combine("src", "FreeX.App.Presentation", "ConditionalFormatting", "ConditionalFormatStatsCache.cs"),
            Path.Combine("src", "FreeX.App.Presentation", "DrawingInteraction", "DrawingObjectHitTestPlanner.cs"),
            Path.Combine("src", "FreeX.App.Presentation", "PageLayout", "PrintExportDrawingEvidencePlanner.cs"),
            Path.Combine("src", "FreeX.App.Presentation", "Ribbon", "RibbonRuntimeCatalogPlanner.cs"),
            Path.Combine("src", "FreeX.App.Presentation", "DataTools", "DataListCommandRangePlanner.cs"),
            Path.Combine("src", "FreeX.App.Presentation", "PageLayout", "PageSetupRangeParser.cs"),
            Path.Combine("src", "FreeX.App.Presentation", "Protection", "ProtectionInputParser.cs"),
            Path.Combine("src", "FreeX.App.Presentation", "PresentationLayer.cs"),
            Path.Combine("src", "FreeX.App.Services", "AccessibilityIssueFormatter.cs"),
            Path.Combine("src", "FreeX.App.Services", "ExportPublishOptionEvidencePlanner.cs"),
            Path.Combine("src", "FreeX.Core.Commands", "SortInputParser.cs"),
            Path.Combine("src", "FreeX.Core.Commands", "SubtotalInputParser.cs"),
            Path.Combine("src", "FreeX.Core.IO", "XlsxCellGradientFillWriter.cs"),
            Path.Combine("shared", "Free.Shared.Opc", "NativePasswordHelper.cs")
        };

        foreach (var relativePath in retiredPaths)
        {
            File.Exists(Path.Combine(root, relativePath))
                .Should()
                .BeFalse($"{relativePath} is retired production or test-evidence code");
        }
    }

    [Fact]
    public void EvidenceOnlyPlanners_LiveUnderTestSupport()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var testSupportPaths = new[]
        {
            Path.Combine(
                "tests",
                "FreeX.App.Presentation.Tests",
                "TestSupport",
                "PageLayout",
                "PrintExportDrawingEvidencePlanner.cs"),
            Path.Combine(
                "tests",
                "FreeX.App.Services.Tests",
                "TestSupport",
                "ExportPublishOptionEvidencePlanner.cs"),
            Path.Combine(
                "tests",
                "SharedTestInfrastructure",
                "FreeX",
                "RibbonRuntimeCatalogPlanner.cs")
        };

        foreach (var relativePath in testSupportPaths)
        {
            File.Exists(Path.Combine(root, relativePath))
                .Should()
                .BeTrue($"{relativePath} preserves useful evidence without shipping in production");
        }
    }

    [Fact]
    public void PlaceholderDtosAndResults_AreAbsentFromProductionSources()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        var sources = string.Join(
            Environment.NewLine,
            File.ReadAllText(Path.Combine(root, "src", "FreeX.Core.Model", "Dtos.cs")),
            File.ReadAllText(Path.Combine(
                root,
                "src",
                "FreeX.App.Presentation",
                "ConditionalFormatting",
                "ConditionalFormattingResults.cs")),
            File.ReadAllText(Path.Combine(
                root,
                "src",
                "FreeX.App.Services",
                "DataValidationAffordancePlanner.cs")));

        sources.Should().NotContain("NewWorkbookOptions");
        sources.Should().NotContain("record WorkbookMeta");
        sources.Should().NotContain("record SheetMeta");
        sources.Should().NotContain("HighlightResult");
        sources.Should().NotContain("DvInputMessagePlacement");
    }
}
