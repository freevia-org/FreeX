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

        foreach (var scriptName in new[] { "screenshot_excel.ps1", "screenshot_ribbon.ps1" })
        {
            var source = File.ReadAllText(WorkspaceFileLocator.Find("tools", scriptName));
            var widthBlock = Regex.Match(
                source,
                @"\$defaultCaptureWidths\s*=\s*@\((?<widths>.*?)\)\s*function Resolve-CaptureWidths",
                RegexOptions.Singleline);

            widthBlock.Success.Should().BeTrue($"{scriptName} should declare the default ribbon evidence width matrix");

            var actualLabels = Regex
                .Matches(widthBlock.Groups["widths"].Value, @"Label\s*=\s*""(?<label>[^""]+)""")
                .Select(match => match.Groups["label"].Value)
                .ToArray();

            actualLabels.Should().Equal(expectedLabels);
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
