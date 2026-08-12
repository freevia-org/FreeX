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
            source.Should().Contain("_dialogRangeSelectionController.HandleKey(");
            source.Should().Contain("DialogRangeSelectionGeometryPlanner.ResolveDimension(");
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
    public void SessionOwnedForeignImportAndBackstageOpenRecalcException_RemainExact()
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
        directBusExecutionOwners.Should().BeEmpty();
        sources["MainWindow.DataCommands.cs"].Should().Contain(
            "targetSession.ExecuteCommandPreservingSelection(command)");

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

    [Fact]
    public void ResidualRendererNeutralState_LivesInPresentationOrServices()
    {
        var root = WorkspaceFileLocator.FindWorkspaceRoot();
        var host = Path.Combine(root, "src", "FreeX.App.Host");
        var avalonia = Path.Combine(root, "src", "FreeX.App.Avalonia");
        var presentation = Path.Combine(root, "src", "FreeX.App.Presentation");
        var services = Path.Combine(root, "src", "FreeX.App.Services");
        var hostMain = ReadHost("MainWindow.xaml.cs");
        var hostPivot = ReadHost("MainWindow.PivotChartCommands.cs");
        var hostShapeEffects = ReadHost("ShapeEffectsDialog.cs");
        var avaloniaKeyboard = ReadAvalonia("MainWindow.KeyboardParity.cs");
        var avaloniaPivot = ReadAvalonia("MainWindow.PivotChartContextMenus.cs");

        foreach (var fileName in new[]
                 {
                     "AppLanguageCatalog.cs",
                     "FailedWorkbookCommand.cs",
                     "FormulaAuditFormatter.cs",
                     "NewWorkbookFactory.cs",
                     "PivotFieldFilterSummary.cs",
                     "SelectionCornerNavigator.cs",
                     "SparklineValueCache.cs",
                     "StatusBarStatsCache.cs",
                     "ToolbarVisualState.cs",
                     "ToolbarVisualStateCache.cs"
                 })
        {
            File.Exists(Path.Combine(host, fileName)).Should().BeFalse();
        }

        File.Exists(Path.Combine(avalonia, "ConditionalFormatCellRenderPlanner.cs")).Should().BeFalse();
        File.Exists(Path.Combine(avalonia, "ConditionalFormatStatsCache.cs")).Should().BeFalse();
        File.Exists(Path.Combine(avalonia, "Charts", "InsertChartCommandFactory.cs")).Should().BeFalse();
        File.Exists(Path.Combine(presentation, "ConditionalFormatting", "ConditionalFormatCellRenderPlanner.cs")).Should().BeTrue();
        File.Exists(Path.Combine(presentation, "ConditionalFormatting", "ConditionalFormatStatsCache.cs"))
            .Should()
            .BeFalse("the live evaluators own their aggregate state without an unused public cache");
        File.Exists(Path.Combine(presentation, "Charts", "Editing", "ChartCommandWorkflowPlanner.cs")).Should().BeTrue();
        File.Exists(Path.Combine(presentation, "GridInteraction", "SelectionCornerNavigator.cs")).Should().BeTrue();
        File.Exists(Path.Combine(services, "FailedWorkbookCommand.cs")).Should().BeTrue();
        hostMain.Should().Contain("WorkbookSelectionStatsCache _statusBarStatsCache");
        hostPivot.Should().Contain("PivotFieldFilterSummary.CreateState(");
        ReadHost("MainWindow.PivotCommands.cs").Should().Contain("PivotApplication.PlanFieldItemSelection(");
        hostShapeEffects.Should().NotContain("ShapeEffectsDialogPlanner");
        avaloniaKeyboard.Should().Contain("SelectionCornerNavigator.GetNextCorner(");
        avaloniaKeyboard.Should().NotContain("var corners = new[]");
        avaloniaPivot.Should().Contain("PivotFieldFilterSummary.CreateState(");
        ReadAvalonia("MainWindow.PivotFilters.cs").Should().Contain("PivotApplication.PlanFieldItemSelection(");
    }

    private static string ReadHost(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Host", fileName);

    private static string ReadAvalonia(string fileName) =>
        WorkspaceFileLocator.ReadAllText("src", "FreeX.App.Avalonia", fileName);
}
