using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class WorksheetEditRangePlannerDedupSourceTests
{
    [Fact]
    public void HostPlannerFilesAreRemovedAndCallSitesUseSharedPlanners()
    {
        var hostSourceDirectory = DialogSourceTestSupport.FindHostSourceDirectory("MainWindow.CellsCommands.cs");
        foreach (var fileName in new[]
        {
            "CopyFromAbovePlanner.cs",
            "SelectionMoveOverwritePlanner.cs",
            "DataListCommandRangePlanner.cs",
            "ForecastSheetSourceRangePlanner.cs"
        })
        {
            File.Exists(Path.Combine(hostSourceDirectory, fileName)).Should().BeFalse();
        }

        var cellsSource = DialogSourceTestSupport.ReadHostSources("MainWindow.CellsCommands.cs");
        cellsSource.Should().Contain("using FreeX.App.Presentation.Editing;");
        cellsSource.Should().Contain("using FreeX.App.Presentation.GridInteraction;");
        cellsSource.Should().Contain("CopyFromAbovePlanner.CreateEdit(sheet, target, mode)");
        cellsSource.Should().Contain("SelectionMoveOverwritePlanner.HasOverwriteTargets(sheet, sourceRange, targetRange)");

        var selectionMoveMethod = SourceMethodExtractor.ExtractMethodSource(
            cellsSource,
            "private void OnSelectionMoveRequested(");
        selectionMoveMethod.Should().Contain("UiText.Get(\"MainWindowMessage_TextToColumnsReplaceDataPrompt\")");
        selectionMoveMethod.Should().Contain("_messageService.AskYesNo");
        selectionMoveMethod.Should().Contain("new MoveRangeCommand(_currentSheetId, sourceRange, targetRange.Start)");

        var dataSource = DialogSourceTestSupport.ReadHostSources("MainWindow.DataCommands.cs");
        dataSource.Should().Contain("using FreeX.App.Services;");
        dataSource.Should().Contain("ForecastSheetPlanner.CreatePlan(_workbook, range, dialog.Result.Periods)");
        dataSource.Should().NotContain("ForecastSheetSourceRangePlanner.Create(");

        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        File.Exists(Path.Combine(
                repoRoot,
                "src",
                "FreeX.App.Services",
                "ForecastSheetSourceRangePlanner.cs"))
            .Should()
            .BeTrue("Forecast Sheet current-region policy must be shared by both renderers");
        File.Exists(Path.Combine(
                repoRoot,
                "src",
                "FreeX.App.Presentation",
                "DataTools",
                "ForecastSheetSourceRangePlanner.cs"))
            .Should()
            .BeFalse("the source-range policy moved into the Services workflow used by both renderers");
        File.Exists(Path.Combine(
                repoRoot,
                "src",
                "FreeX.App.Presentation",
                "DataTools",
                "DataListCommandRangePlanner.cs"))
            .Should()
            .BeFalse("the unused current-region replica must not ship beside SelectionRangeService");
    }
}
