using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class ResidualRendererBoundaryTests
{
    [Fact]
    public void FormulaPointModeAndQuickAnalysis_DelegateRemainingApplicationTransitions()
    {
        var hostFormula = ReadHost("MainWindow.FormulaPointMode.cs");
        var avaloniaFormula = ReadAvalonia("MainWindow.FormulaPointMode.cs");
        var hostQuickAnalysis = ReadHost("MainWindow.QuickAnalysis.cs");
        var avaloniaQuickAnalysis = ReadAvalonia("MainWindow.QuickAnalysis.cs");
        var quickAnalysisSources = hostQuickAnalysis + Environment.NewLine + avaloniaQuickAnalysis;

        hostFormula.Should().Contain("_session.SelectFormulaPointModeSourceRange(range)");
        avaloniaFormula.Should().Contain("_session.SelectFormulaPointModeSourceRange(range)");
        hostFormula.Should().NotContain("_currentSheetId = range.Start.Sheet");
        avaloniaFormula.Should().NotContain("_session.SelectSheet(range.Start.Sheet)");

        hostQuickAnalysis.Should().Contain("_quickAnalysisSession.ExecuteSelectionAsync(");
        avaloniaQuickAnalysis.Should().Contain("_quickAnalysisSession.ExecuteSelectionAsync(");
        hostQuickAnalysis.Should().Contain("_session.ExecuteQuickAnalysisTotal(operation)");
        avaloniaQuickAnalysis.Should().Contain("_session.ExecuteQuickAnalysisTotal(operation)");
        avaloniaQuickAnalysis.Should().Contain("_session.ExecuteQuickAnalysisSparklines(operation)");
        quickAnalysisSources.Should().NotContain("switch (operation.Kind)");
        quickAnalysisSources.Should().NotContain("QuickAnalysisHostOperationKind.");
        quickAnalysisSources.Should().NotContain("QuickAnalysisHostOperationPlanner.TryBuildTotalFormulaEdits(");
        quickAnalysisSources.Should().NotContain("QuickAnalysisHostOperationPlanner.TryBuildSparklineCommands(");
        quickAnalysisSources.Should().NotContain("new EditCellsCommand(");
    }

    [Fact]
    public void ExhaustedInteractionSurfaces_KeepOnlyNativeControlsGeometryFocusAndRendering()
    {
        var hostDialogRange = ReadHost("MainWindow.DialogRangeSelection.cs");
        var avaloniaDialogRange = ReadAvalonia("MainWindow.DialogRangeSelection.cs");
        var hostTextBox = ReadHost("MainWindow.TextBoxInlineEditing.cs");
        var avaloniaTextBox = ReadAvalonia("MainWindow.TextBoxInlineEditing.cs");
        var hostFormControls = ReadHost("MainWindow.FormControls.cs");
        var avaloniaFormControls = ReadAvalonia("MainWindow.FormControls.cs");

        foreach (var source in new[] { hostDialogRange, avaloniaDialogRange })
        {
            source.Should().Contain("DialogRangeSelectionController<DialogRangePickerContext>");
            source.Should().Contain("_dialogRangeSelectionController.DecideKey(");
            source.Should().NotContain("format switch");
        }

        foreach (var source in new[] { hostTextBox, avaloniaTextBox })
        {
            source.Should().Contain("TextBoxInlineEditSession _textBoxInlineEditSession = new();");
            source.Should().NotContain("TextBoxInlineEditPlanner.CreateCommitPlan(");
            source.Should().NotContain("TextBoxInlineEditPlanner.PlanKeyDown(");
            source.Should().NotContain("new SetTextBoxTextCommand(");
        }

        foreach (var source in new[] { hostFormControls, avaloniaFormControls })
        {
            source.Should().Contain("FormControlInteractionService.CreateCommand(");
            source.Should().Contain("new FormControlInteractionRequest(");
            source.Should().NotContain("new SetCellValueCommand(");
        }
    }

    [Fact]
    public void ExhaustedPageLayoutAndBackstageSurfaces_ConsumePortablePolicyOwners()
    {
        var hostPageLayout = ReadHost("MainWindow.PageLayout.cs");
        var avaloniaPageLayout = ReadAvalonia("MainWindow.PageLayout.cs");
        var pageLayoutSources = hostPageLayout + Environment.NewLine + avaloniaPageLayout;
        var hostBackstage = ReadHost("MainWindow.Backstage.cs");
        var avaloniaBackstage = ReadAvalonia("MainWindow.Backstage.cs");

        pageLayoutSources.Should().Contain("PageLayoutCommandSession");
        pageLayoutSources.Should().Contain("PageSetupSubmissionPlanner.TryBuild(");
        pageLayoutSources.Should().NotContain("new SetPageMarginsCommand(");
        pageLayoutSources.Should().NotContain("new SetPageOrientationCommand(");
        pageLayoutSources.Should().NotContain("new SetPaperSizeCommand(");
        pageLayoutSources.Should().NotContain("new SetScaleToFitCommand(");
        pageLayoutSources.Should().NotContain("new SetPageBreaksCommand(");

        hostBackstage.Should().Contain("FreeXBackstageInfoPanePlanner.Build(");
        avaloniaBackstage.Should().Contain("FreeXBackstageInfoPanePlanner.Build(");
        avaloniaBackstage.Should().Contain("FreeXBackstagePaneProjectionPlanner.BuildInfoDialog(");
        avaloniaBackstage.Should().NotContain("ExecuteReviewCommand(");
        avaloniaBackstage.Should().NotContain("_recalcEngine");
    }

    [Fact]
    public void AcceptedForeignImportAndBackstageOpenRecalcExceptions_RemainExact()
    {
        var hostDirectory = Path.Combine(
            WorkspaceFileLocator.FindWorkspaceRoot(),
            "src",
            "FreeX.App.Host");
        var sources = Directory.GetFiles(hostDirectory, "MainWindow*.cs")
            .ToDictionary(path => Path.GetFileName(path)!, File.ReadAllText);

        var directBusExecutionOwners = sources
            .SelectMany(pair => Regex.Matches(pair.Value, @"_commandBus\.(?:Execute|ExecuteRepeatable)\(")
                .Select(_ => pair.Key))
            .ToList();
        directBusExecutionOwners.Should().Equal("MainWindow.DataCommands.cs");
        sources["MainWindow.DataCommands.cs"].Should().Contain("_commandBus.Execute(targetWorkbook.Id");

        foreach (var (fileName, source) in sources.Where(pair => pair.Key != "MainWindow.Backstage.cs"))
        {
            source.Should().NotContain(
                "_recalcEngine.Recalculate",
                $"{fileName} must use WorkbookSession for owned-workbook recalculation");
        }

        var backstage = sources["MainWindow.Backstage.cs"];
        backstage.Should().Contain("_fileWorkflow.OpenAsync(");
        backstage.Should().NotContain("new OpenWorkbookLoader(");
        backstage.Should().Contain("_recalcEngine.RebuildFormulaDependencies(_workbook)");
        backstage.Should().Contain("WorkbookSessionFactory.ApplyOnOpenVolatileRecalc(_recalcEngine, _workbook, _fileAdapters)");
    }

    private static string ReadHost(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", fileName);

    private static string ReadAvalonia(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Avalonia", fileName);
}
