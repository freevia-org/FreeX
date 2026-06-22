using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookProgressPresentationPlannerTests
{
    [Fact]
    public void CalculateServiceStagePercent_AdvancesWithinStage()
    {
        WorkbookProgressPresentationPlanner.CalculateServiceStagePercent(
                startPercent: 16,
                endPercent: 90,
                elapsed: TimeSpan.FromSeconds(5),
                expectedDuration: TimeSpan.FromSeconds(10))
            .Should().Be(53);
    }

    [Fact]
    public void CalculateServiceStagePercent_ReturnsIndeterminateAfterEstimate()
    {
        WorkbookProgressPresentationPlanner.CalculateServiceStagePercent(
                startPercent: 16,
                endPercent: 90,
                elapsed: TimeSpan.FromSeconds(30),
                expectedDuration: TimeSpan.FromSeconds(10))
            .Should().BeNull();
    }

    [Fact]
    public void CalculateRunningStagePercent_HoldsBelowStageEnd()
    {
        WorkbookProgressPresentationPlanner.CalculateRunningStagePercent(
                startPercent: 16,
                endPercent: 90,
                elapsed: TimeSpan.FromSeconds(30),
                expectedDuration: TimeSpan.FromSeconds(10),
                holdbackPercent: 0.5)
            .Should().Be(89.5);
    }

    [Fact]
    public void EstimateStageDuration_UsesFloorUntilSizeEstimateExceedsIt()
    {
        WorkbookProgressStageRunner.EstimateStageDuration(
                sizeBytes: 64 * 1024,
                secondsPerMegabyte: 1.4,
                floorSeconds: 0.5)
            .Should().Be(TimeSpan.FromSeconds(0.5));

        WorkbookProgressStageRunner.EstimateStageDuration(
                sizeBytes: 2 * 1024 * 1024,
                secondsPerMegabyte: 1.4,
                floorSeconds: 0.5)
            .Should().Be(TimeSpan.FromSeconds(2.8));
    }

    [Fact]
    public void BuildOpenTextPlan_RotatesParsingDetailResourceKeysEveryThreeSeconds()
    {
        WorkbookProgressPresentationPlanner.BuildOpenTextPlan(
                WorkbookOpenProgressStep.Parsing,
                TimeSpan.FromSeconds(0))
            .DetailResourceKey.Should().Be("Progress_LoadingFileParsing");
        WorkbookProgressPresentationPlanner.BuildOpenTextPlan(
                WorkbookOpenProgressStep.Parsing,
                TimeSpan.FromSeconds(3))
            .DetailResourceKey.Should().Be("Progress_LoadingFileReadingWorksheets");
        WorkbookProgressPresentationPlanner.BuildOpenTextPlan(
                WorkbookOpenProgressStep.Parsing,
                TimeSpan.FromSeconds(6))
            .DetailResourceKey.Should().Be("Progress_LoadingFileBuildingWorkbook");
    }

    [Fact]
    public void BuildSaveTextPlan_RotatesWritingDetailResourceKeysEveryThreeSeconds()
    {
        WorkbookProgressPresentationPlanner.BuildSaveTextPlan(
                WorkbookSaveProgressStep.Writing,
                TimeSpan.FromSeconds(0))
            .DetailResourceKey.Should().Be("Progress_SavingFileWriting");
        WorkbookProgressPresentationPlanner.BuildSaveTextPlan(
                WorkbookSaveProgressStep.Writing,
                TimeSpan.FromSeconds(3))
            .DetailResourceKey.Should().Be("Progress_SavingFileWritingBytes");
        WorkbookProgressPresentationPlanner.BuildSaveTextPlan(
                WorkbookSaveProgressStep.Writing,
                TimeSpan.FromSeconds(6))
            .DetailResourceKey.Should().Be("Progress_SavingFileFlushingPackage");
    }

    [Fact]
    public void PhaseMappings_PreserveHostPresentationSemantics()
    {
        WorkbookProgressPresentationPlanner.ToOpenProgressStep(WorkbookOpenPhase.Parsing)
            .Should().Be(WorkbookOpenProgressStep.Parsing);
        WorkbookProgressPresentationPlanner.ToSaveProgressStep(WorkbookSavePhase.Preparing)
            .Should().Be(WorkbookSaveProgressStep.Serializing);
        WorkbookProgressPresentationPlanner.ParseOpenProgressStep(" Preparing View ")
            .Should().Be(WorkbookOpenProgressStep.PreparingView);
        WorkbookProgressPresentationPlanner.ParseSaveProgressStep(" writing ")
            .Should().Be(WorkbookSaveProgressStep.Writing);
    }

    [Fact]
    public void WorkbookOpenAndSaveServices_UseSharedProgressRunner()
    {
        var openSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Services", "WorkbookOpenService.cs"));
        var saveSource = File.ReadAllText(RepositoryFileLocator.Find(
            "src", "FreeX.App.Services", "WorkbookSaveService.cs"));

        openSource.Should().Contain("WorkbookProgressStageRunner.RunStageAsync");
        saveSource.Should().Contain("WorkbookProgressStageRunner.RunStageAsync");
        openSource.Should().Contain("WorkbookProgressStageRunner.EstimateStageDuration");
        saveSource.Should().Contain("WorkbookProgressStageRunner.EstimateStageDuration");

        foreach (var source in new[] { openSource, saveSource })
        {
            source.Should().NotContain("private static async Task<T> RunStageAsync");
            source.Should().NotContain("private static async Task ReportStageProgressAsync");
            source.Should().NotContain("private static double? CalculateStageProgress");
            source.Should().NotContain("private static TimeSpan EstimateStageDuration");
        }
    }
}
