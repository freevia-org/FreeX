using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class OpenProgressTests
{
    [Fact]
    public void CalculateOpenStageProgress_AdvancesLinearlyWithinStage()
    {
        OpenWorkbookProgressPlanner.CalculateStageProgress(
                stageStartPercent: 16,
                stageEndPercent: 90,
                elapsed: TimeSpan.FromSeconds(5),
                expectedDuration: TimeSpan.FromSeconds(10))
            .Should().Be(53);
    }

    [Fact]
    public void CalculateOpenStageProgress_StaysBelowStageEndUntilWorkCompletes()
    {
        OpenWorkbookProgressPlanner.CalculateStageProgress(
                stageStartPercent: 16,
                stageEndPercent: 90,
                elapsed: TimeSpan.FromSeconds(30),
                expectedDuration: TimeSpan.FromSeconds(10))
            .Should().Be(89.5);
    }

    [Fact]
    public void FormatLoadingFileDetail_ChangesEveryThreeSeconds()
    {
        OpenWorkbookProgressPlanner.FormatLoadingFileDetail("parsing", TimeSpan.FromSeconds(0))
            .Should().Be("Loading file (parsing)");
        OpenWorkbookProgressPlanner.FormatLoadingFileDetail("parsing", TimeSpan.FromSeconds(3))
            .Should().Be("Loading file (reading worksheets)");
        OpenWorkbookProgressPlanner.FormatLoadingFileDetail("parsing", TimeSpan.FromSeconds(6))
            .Should().Be("Loading file (building workbook)");
    }
}
