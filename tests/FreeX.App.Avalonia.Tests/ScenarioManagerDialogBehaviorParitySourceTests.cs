using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class ScenarioManagerDialogBehaviorParitySourceTests
{
    [Fact]
    public void SaveAndEdit_UseDialogReferencesAndPreserveWpfRequestSemantics()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var dialogSource = source[SourceStart(source)..SourceEnd(source)];

        dialogSource.Should().NotContain("IsReadOnly = true");
        dialogSource.Should().Contain("ScenarioManagerDialogPlanner.ValidateAcceptRequest(");
        dialogSource.Should().Contain("ScenarioManagerDialogPlanner.ProjectAcceptResult(");
        dialogSource.Should().Contain("WorkbookRangeTextCodec.TryParseMany(");
        dialogSource.Should().Contain("var acceptedName = accepted.NewScenarioName.Trim();");
        dialogSource.Should().Contain("acceptedName,");
        dialogSource.Should().Contain("ReplaceScenarioName: accepted.Action == ScenarioManagerDialogAction.Edit");
        dialogSource.Should().Contain("Hidden: accepted.Hidden");
        dialogSource.Should().Contain("Locked: accepted.Locked");
        dialogSource.Should().Contain("saveButton.Click += (_, _) => SaveCurrentValues(ScenarioManagerDialogAction.Add);");
        dialogSource.Should().Contain("editButton.Click += (_, _) => SaveCurrentValues(ScenarioManagerDialogAction.Edit);");
        dialogSource.Should().Contain("ranges = [_session.SelectedRange];");
        dialogSource.Should().Contain("new HashSet<CellAddress>()");
        dialogSource.Should().Contain("RefreshDialogPlan(acceptedName);");
    }

    [Fact]
    public void SummaryReport_ValidatesAndPassesResultCellReferencesToTheSharedPlanner()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));
        var dialogSource = source[SourceStart(source)..SourceEnd(source)];

        dialogSource.Should().Contain("ValidateScenarioManagerFields(ScenarioManagerDialogAction.Report)");
        dialogSource.Should().Contain("ScenarioManager_EnterValidResultCellsReference");
        dialogSource.Should().Contain("CreateSummaryReportPlan(_session.Workbook, resultCells)");
        dialogSource.Should().Contain("resultRanges.SelectMany(range => range.AllCells()).Distinct().ToArray()");
        dialogSource.Should().Contain("resultCellsBox.Focus();");
    }

    private static int SourceStart(string source) =>
        source.IndexOf("private async Task ShowScenarioManagerCompactDialogAsync", StringComparison.Ordinal);

    private static int SourceEnd(string source) =>
        source.IndexOf("private static string FormatScenarioManagerSelectionSummary", StringComparison.Ordinal);

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
