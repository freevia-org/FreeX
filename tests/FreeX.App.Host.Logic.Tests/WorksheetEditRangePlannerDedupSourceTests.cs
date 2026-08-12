using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class WorksheetEditRangePlannerDedupSourceTests
{
    [Fact]
    public void HostPlannerFilesAreRemovedAndCallSitesUsePresentationPlanners()
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
        dataSource.Should().Contain("using FreeX.App.Presentation.DataTools;");
        dataSource.Should().Contain("ForecastSheetSourceRangePlanner.Create(sheet, range)");

        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
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
