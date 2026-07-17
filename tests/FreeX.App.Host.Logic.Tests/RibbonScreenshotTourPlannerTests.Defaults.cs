using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class RibbonScreenshotTourPlannerTests
{
    [Fact]
    public void DefaultTabs_CoverMainRibbonTourOrderAndFileNames()
    {
        RibbonScreenshotTourPlanner.DefaultTabs
            .Should()
            .Equal(
            [
                new("Home", "Home", "HomeTab"),
                new("Insert", "Insert", "InsertTab"),
                new("Draw", "Draw", "DrawTab"),
                new("Page Layout", "Page_Layout", "PageLayoutTab"),
                new("Formulas", "Formulas", "FormulasTab"),
                new("Data", "Data", "DataTab"),
                new("Review", "Review", "ReviewTab"),
                new("View", "View", "ViewTab"),
                new("Help", "Help", "HelpTab")
            ]);
    }

    [Fact]
    public void PlannerLivesInRibbonDefinitionsAndHostOnlyConsumesThePlan()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var hostPlannerPath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "RibbonScreenshotTourPlanner.cs");
        var definitionsSource = DialogSourceTestSupport.ReadRibbonDefinitionSource("RibbonScreenshotTourPlanner.cs");

        File.Exists(hostPlannerPath)
            .Should()
            .BeFalse("renderer-neutral screenshot tour tabs, widths, phases, and capture names belong in ribbon definitions");

        definitionsSource.Should().Contain("namespace FreeX.Ribbon.Definitions;");
        definitionsSource.Should().Contain("public static class RibbonScreenshotTourPlanner");
        definitionsSource.Should().Contain("public sealed record RibbonScreenshotTourPlan");
        definitionsSource.Should().NotContain("namespace FreeX.App.Host");
        definitionsSource.Should().NotContain("using System.Windows");
        definitionsSource.Should().NotContain("RenderTargetBitmap");
        definitionsSource.Should().NotContain("Environment.GetEnvironmentVariable");
    }

    [Fact]
    public void DefaultTabs_MatchVisibleRibbonCatalogExceptBackstageAndContextualTabs()
    {
        var expectedTabs = RibbonXamlCatalogSnapshotReader.ReadMainWindow()
            .VisibleTabs
            .Select(tab => tab.Header)
            .Where(header => header != "File")
            .ToArray();

        RibbonScreenshotTourPlanner.DefaultTabs.Select(tab => tab.Header)
            .Should()
            .Equal(expectedTabs);
    }

    [Fact]
    public void TableContextTabs_ExtendDefaultTourWithTableDesignContextualTab()
    {
        RibbonScreenshotTourPlanner.TableContextTabs
            .Should()
            .Equal(
            [
                .. RibbonScreenshotTourPlanner.DefaultTabs,
                new("Table Design", "Table_Design", "TableDesignTab")
            ]);
    }

    [Fact]
    public void DrawingObjectContextTabs_ExtendDefaultTourWithShapeAndPictureFormatContextualTabs()
    {
        RibbonScreenshotTourPlanner.DrawingObjectContextTabs
            .Should()
            .Equal(
            [
                .. RibbonScreenshotTourPlanner.DefaultTabs,
                new("Shape Format", "Shape_Format", "ShapeFormatTab"),
                new("Picture Format", "Picture_Format", "PictureFormatTab")
            ]);
    }

    [Fact]
    public void PivotContextTabs_ExtendDefaultTourWithPivotAnalyzeAndDesignContextualTabs()
    {
        RibbonScreenshotTourPlanner.PivotContextTabs
            .Should()
            .Equal(
            [
                .. RibbonScreenshotTourPlanner.DefaultTabs,
                new("PivotTable Analyze", "PivotTable_Analyze", "PivotTableAnalyzeTab"),
                new("Design", "PivotTable_Design", "PivotTableDesignTab")
            ]);
    }

    [Fact]
    public void ChartContextTabs_ExtendDefaultTourWithChartDesignAndFormatContextualTabs()
    {
        RibbonScreenshotTourPlanner.ChartContextTabs
            .Should()
            .Equal(
            [
                .. RibbonScreenshotTourPlanner.DefaultTabs,
                new("Chart Design", "Chart_Design", "ChartDesignTab"),
                new("Format", "Chart_Format", "ChartFormatTab")
            ]);
    }

    [Fact]
    public void DefaultWidths_CoverRepresentativeRibbonWidths()
    {
        RibbonScreenshotTourPlanner.DefaultWidths
            .Should()
            .Equal(
            [
                new("max", null),
                new("1100", 1100),
                new("900", 900),
                new("750", 750)
            ]);
    }

    [Fact]
    public void DefaultWidths_ExplainEvidencePurposeForResizeBreakpointReview()
    {
        RibbonScreenshotTourPlanner.DefaultWidths
            .Select(width => $"{width.Label}:{width.EvidencePurpose()}")
            .Should()
            .Equal(
            [
                "max:Maximized baseline before resize pressure.",
                "1100:Wide ribbon breakpoint before most command groups collapse.",
                "900:Medium ribbon breakpoint where grouped commands begin to compress.",
                "750:Narrow ribbon breakpoint for overflow and compact command layouts."
            ]);
    }

    [Fact]
    public void DefaultWidths_MatchPowerShellScreenshotEvidenceMatrix()
    {
        var expectedLabels = RibbonScreenshotTourPlanner.DefaultWidths
            .Select(width => width.Label)
            .ToArray();

        var support = WorkspaceFileLocator.ReadAllText("tools", "ScreenshotCaptureSupport.ps1");
        var widthBlock = Regex.Match(
            support,
            @"\$defaultCaptureWidths\s*=\s*@\((?<widths>.*?)\)\s*function Resolve-CaptureWidths",
            RegexOptions.Singleline);
        widthBlock.Success.Should().BeTrue("the shared capture support should declare the default ribbon evidence width matrix");

        var actualLabels = Regex
            .Matches(widthBlock.Groups["widths"].Value, @"Label\s*=\s*""(?<label>[^""]+)""")
            .Select(match => match.Groups["label"].Value)
            .ToArray();
        actualLabels.Should().Equal(expectedLabels);

        foreach (var scriptName in new[] { "screenshot_excel.ps1", "screenshot_ribbon.ps1" })
        {
            var source = WorkspaceFileLocator.ReadAllText("tools", scriptName);
            source.Should().Contain("ScreenshotCaptureSupport.ps1");
            source.Should().Contain("Resolve-CaptureWidths $Widths");
        }
    }

    [Fact]
    public void BurstPhases_CoverImmediateFirstRenderAndSettledLayoutMoments()
    {
        RibbonScreenshotTourPlanner.BurstPhases
            .Select(phase => $"{phase.Label}:{phase.FileNameSuffix}")
            .Should()
            .Equal(
            [
                "immediate:immediate",
                "first-render:first_render",
                "settled:settled"
            ]);
    }

    [Fact]
    public void BurstPhases_AreDistinctFromTheNormalSettledOnlyTour()
    {
        RibbonScreenshotTourPlanner.DefaultPhases
            .Should()
            .Equal([RibbonScreenshotTourPlanner.SettledPhase]);

        RibbonScreenshotTourPlanner.BurstPhases
            .Should()
            .HaveCount(3)
            .And.OnlyHaveUniqueItems(phase => phase.Label)
            .And.OnlyHaveUniqueItems(phase => phase.FileNameSuffix);

        RibbonScreenshotTourPlanner.BurstPhases.Select(phase => phase.Label)
            .Should()
            .Equal(["immediate", "first-render", "settled"]);
    }
}
