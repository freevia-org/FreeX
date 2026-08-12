using FluentAssertions;
using FreeX.App.Presentation.Consolidate;
using FreeX.App.Presentation.TextToColumns;

namespace FreeX.App.Services.Tests;

public sealed class ParityFixtureOwnershipTests
{
    private static readonly string[] ServicesFixtureTypes =
    [
        "FreeX.App.Services.HyperlinkDialogParityFixture",
        "FreeX.App.Services.OptionsDialogParityFixture",
        "FreeX.App.Services.ParityDemoWorkbookFactory",
        "FreeX.App.Services.ScenarioManagerParityFixture",
        "FreeX.App.Services.ShapeGradientParityFixture",
        "FreeX.App.Services.SubtotalParityFixture",
        "FreeX.App.Services.SubtotalParityFixtureState",
    ];

    private static readonly string[] PresentationFixtureTypes =
    [
        "FreeX.App.Presentation.Accessibility.AccessibilityCheckerParityFixture",
        "FreeX.App.Presentation.ConditionalFormatting.ConditionalFormatManageParityFixture",
        "FreeX.App.Presentation.Consolidate.ConsolidateParityFixture",
        "FreeX.App.Presentation.DrawingUI.SelectionPaneParityFixture",
        "FreeX.App.Presentation.Filtering.AutoFilterParityFixturePlan",
        "FreeX.App.Presentation.Filtering.AutoFilterParityFixturePlanner",
        "FreeX.App.Presentation.PageLayout.PrintPreviewParityFixture",
        "FreeX.App.Presentation.PageLayout.PrintPreviewParityPage",
        "FreeX.App.Presentation.PageLayout.PrintPreviewParityTextRun",
        "FreeX.App.Presentation.SheetUI.SheetTabsOverflowParityFixture",
        "FreeX.App.Presentation.TextToColumns.TextToColumnsParityFixture",
    ];

    [Fact]
    public void ShippingPortableAssemblies_DoNotOwnParityFixtures()
    {
        var servicesAssembly = typeof(WorkbookSessionFactory).Assembly;
        var presentationAssembly = typeof(ConsolidateDialogInitialState).Assembly;

        ServicesFixtureTypes.Should().AllSatisfy(typeName =>
            servicesAssembly.GetType(typeName).Should().BeNull(typeName));
        PresentationFixtureTypes.Should().AllSatisfy(typeName =>
            presentationAssembly.GetType(typeName).Should().BeNull(typeName));

        typeof(ParityDemoWorkbookFactory).Assembly.GetName().Name
            .Should().Be("FreeX.ParityCapture.Support");
    }

    [Fact]
    public void ShippingPlanners_DoNotExposeParityOnlyFactories()
    {
        typeof(WorkbookSessionFactory).GetMethod("CreateParityDemo").Should().BeNull();
        typeof(EvaluateFormulaDialogPlanner).GetMethod("CreateParitySummary").Should().BeNull();
        typeof(ErrorCheckingDialogPlanner).GetMethod("CreateParityIssues").Should().BeNull();
    }

    [Fact]
    public void TextToColumnsShippingMetrics_RemainInPresentation()
    {
        typeof(TextToColumnsDialogMetrics).Assembly.GetName().Name
            .Should().Be(typeof(ConsolidateDialogInitialState).Assembly.GetName().Name);
        TextToColumnsDialogMetrics.WindowWidth.Should().Be(560);
        TextToColumnsDialogMetrics.WindowHeight.Should().Be(560);
        TextToColumnsDialogMetrics.MinimumWindowWidth.Should().Be(520);
        TextToColumnsDialogMetrics.MinimumWindowHeight.Should().Be(500);
        TextToColumnsDialogMetrics.PreviewRowLimit.Should().Be(3);
    }
}
