using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class PivotPlannerDedupSourceTests
{
    [Fact]
    public void PivotSourceRangePlanner_HostFacadeIsRemovedAndInsertUsesSharedCreatePlannerDirectly()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var hostFacadePath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "PivotTableSourceRangePlanner.cs");
        var hostTestsPath = Path.Combine(repoRoot, "tests", "FreeX.App.Host.Logic.Tests", "PivotTableSourceRangePlannerTests.cs");
        var pivotCommandsSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.PivotCommands.cs");
        var presentationSource = DialogSourceTestSupport.ReadPresentationSources("PivotUI", "PivotCreatePlanner.cs");

        File.Exists(hostFacadePath)
            .Should()
            .BeFalse("the WPF Host should call the shared PivotCreatePlanner directly instead of carrying enum/record mirrors");
        File.Exists(hostTestsPath)
            .Should()
            .BeFalse("source-range behavior is covered by the shared PivotCreatePlanner tests");

        pivotCommandsSource.Should().Contain("PivotApplication.PrepareCreate(_currentSheetId, SheetGrid.SelectedRange)");
        pivotCommandsSource.Should().Contain("PivotApplication.PlanCreate(");
        pivotCommandsSource.Should().NotContain("PivotCreatePlanner.BuildCommand(");
        pivotCommandsSource.Should().NotContain("PivotTableSourceRangePlanner");

        presentationSource.Should().Contain("public sealed record PivotCreateSourceRangePlan");
        presentationSource.Should().Contain("public enum PivotCreateSourceRangeError");
    }

    [Fact]
    public void GetPivotDataFormulaPlanner_LivesInPresentationPivotUi()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var hostPlannerPath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "GetPivotDataFormulaPlanner.cs");
        var hostResolverPath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "PivotSourceHeaderResolver.cs");
        var formulaEditingSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.FormulaReferenceEditing.cs");
        var pivotCommandsSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.PivotCommands.cs");
        var presentationSource = DialogSourceTestSupport.ReadPresentationSources("PivotUI", "GetPivotDataFormulaPlanner.cs");
        var formulaSessionSource = DialogSourceTestSupport.ReadPresentationSources(
            "FormulaBar",
            "FormulaRangeEditingSession.cs");
        var resolverSource = DialogSourceTestSupport.ReadPresentationSources("PivotUI", "PivotSourceHeaderResolver.cs");

        File.Exists(hostPlannerPath)
            .Should()
            .BeFalse("GETPIVOTDATA composition is model-driven pivot UI planning, not WPF Host rendering");
        File.Exists(hostResolverPath)
            .Should()
            .BeFalse("pivot cache header fallback is shared PivotUI metadata resolution, not WPF Host rendering");

        formulaEditingSource.Should().Contain("_formulaRangeEditingSession.TryApplyPointRangeSelectionEdit(");
        formulaEditingSource.Should().NotContain("GetPivotDataFormulaPlanner.CreatePointModeFunctionCall(");
        formulaEditingSource.Should().NotContain("GetPivotDataFormulaPlanner.Create(");
        formulaSessionSource.Should().Contain("GetPivotDataFormulaPlanner.CreatePointModeFunctionCall(");
        pivotCommandsSource.Should().Contain("PivotApplication.ReadSourceHeaders(");
        pivotCommandsSource.Should().NotContain("PivotSourceHeaderResolver.Resolve(");
        presentationSource.Should().Contain("public sealed record GetPivotDataFormulaPlan");
        presentationSource.Should().Contain("public static class GetPivotDataFormulaPlanner");
        presentationSource.Should().Contain("PivotSourceHeaderResolver.Resolve");
        resolverSource.Should().Contain("public static class PivotSourceHeaderResolver");
        resolverSource.Should().Contain("cache.CacheId == pivotTable.CacheId");
    }

    [Fact]
    public void PivotHostPlannerFacadeAndHostOnlyModels_AreRemoved()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var hostPivotUiPath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "PivotUiPlanner.cs");
        var hostModelsPath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "PivotUiHostModels.cs");
        var pivotCommandsSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.PivotCommands.cs");
        var deferredLayoutSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.PivotFieldListDeferredLayout.cs");
        var hostHeaderPlannerPath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "PivotHeaderDropdownPlanner.cs");
        var hostAdornmentPlannerPath = Path.Combine(repoRoot, "src", "FreeX.App.Host", "PivotRowLabelAdornmentPlanner.cs");
        var uiHeaderRecordPath = Path.Combine(repoRoot, "src", "FreeX.App.UI", "PivotHeaderDropdownButton.cs");
        var uiAdornmentRecordPath = Path.Combine(repoRoot, "src", "FreeX.App.UI", "PivotRowLabelAdornment.cs");
        var imageCompareProjectSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "tools",
            "FreeX.SheetGridImageCompare",
            "FreeX.SheetGridImageCompare.csproj"));
        var imageCompareSource = File.ReadAllText(Path.Combine(
            repoRoot,
            "tools",
            "FreeX.SheetGridImageCompare",
            "Program.cs"));
        var viewportSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.Viewport.cs");
        var headerRoutingSource = DialogSourceTestSupport.ReadHostSourceFile("MainWindow.PivotHeaderDropdowns.cs");
        var valueFieldPlannerPath = Path.Combine(
            repoRoot,
            "src",
            "FreeX.App.Host",
            "PivotValueFieldSettingsDialogPlanner.cs");
        var valueFieldDialogSource = DialogSourceTestSupport.ReadHostSourceFile("PivotValueFieldSettingsDialog.xaml.cs");
        var sharedUiSource = DialogSourceTestSupport.ReadPresentationSources("PivotUI", "PivotUiPlanner.cs");
        var sharedFieldListSource = DialogSourceTestSupport.ReadPresentationSources("PivotUI", "PivotFieldListPaneBuilder.cs");
        var sharedLayoutSource = DialogSourceTestSupport.ReadPresentationSources("PivotUI", "PivotFieldLayoutPlanner.cs");
        var sharedAdornmentSource = DialogSourceTestSupport.ReadPresentationSources("PivotUI", "PivotGridAdornmentPlanner.cs");
        var sharedValueFieldSource = DialogSourceTestSupport.ReadPresentationSources("PivotUI", "PivotValueFieldPlanner.cs");

        File.Exists(hostPivotUiPath).Should().BeFalse("the shared PivotUiPlanner should be consumed directly by WPF Host");
        File.Exists(hostModelsPath).Should().BeFalse("field-list items and deferred layouts are shared presentation models");
        pivotCommandsSource.Should().Contain("PivotUiPlanner.FindPivotTableContainingSelection");
        pivotCommandsSource.Should().Contain("PivotFieldLayoutPlanner.PlanDrop(");
        pivotCommandsSource.Should().Contain("PivotApplication.PlanLayout(");
        deferredLayoutSource.Should().Contain("PivotFieldListPaneBuilder.FilterAvailableFields(");
        deferredLayoutSource.Should().Contain("PivotFieldLayoutDraft");
        sharedFieldListSource.Should().Contain("public static IReadOnlyList<PivotAvailableFieldItemModel> BuildAvailableFields(");
        sharedLayoutSource.Should().Contain("public static PivotFieldLayoutDropPlan PlanDrop(");

        File.Exists(hostHeaderPlannerPath).Should().BeFalse("WPF should consume the canonical header targets directly");
        File.Exists(hostAdornmentPlannerPath).Should().BeFalse("WPF should consume the canonical row adornments directly");
        File.Exists(uiHeaderRecordPath).Should().BeFalse("the WPF renderer accepts the canonical header target record");
        File.Exists(uiAdornmentRecordPath).Should().BeFalse("the WPF renderer accepts the canonical row adornment record");
        viewportSource.Should().Contain("PivotGridAdornmentPlanner.BuildHeaderTargets(_workbook, sheet)");
        viewportSource.Should().Contain("SheetGrid.PivotHeaderDropdowns = pivotHeaderDropdownTargets;");
        viewportSource.Should().Contain("PivotGridAdornmentPlanner.BuildRowLabelAdornments(_workbook, sheet)");
        viewportSource.Should().NotContain("new FreeX.App.UI.PivotHeaderDropdownButton");
        headerRoutingSource.Should().Contain("target.MenuTarget.PivotTableName");
        headerRoutingSource.Should().Contain("target.MenuTarget.Area switch");
        imageCompareProjectSource.Should().NotContain("FreeX.App.Host");
        imageCompareSource.Should().Contain("PivotGridAdornmentPlanner.BuildHeaderTargets(workbook, sheet)");
        imageCompareSource.Should().Contain("PivotGridAdornmentPlanner.BuildRowLabelAdornments(workbook, sheet)");
        imageCompareSource.Should().NotContain("FreeX.App.Host.PivotHeaderDropdownPlanner");

        File.Exists(valueFieldPlannerPath).Should().BeFalse();
        valueFieldDialogSource.Should().Contain("PivotValueFieldPlanner.GetSummaryFunctions(WpfResourceKeyTextResolver.Instance)");
        valueFieldDialogSource.Should().Contain("PivotValueFieldPlanner.GetShowValuesAsOptions(WpfResourceKeyTextResolver.Instance)");
        valueFieldDialogSource.Should().Contain("PivotValueFieldPlanner.TryValidateShowValuesAs(");
        valueFieldDialogSource.Should().Contain("PivotValueFieldPlanner.CreateResult(");

        sharedUiSource.Should().Contain("public sealed record PivotFieldListPanePlan");
        sharedUiSource.Should().Contain("public sealed record PivotShowDetailsTarget");
        sharedAdornmentSource.Should().Contain("public static IReadOnlyList<PivotHeaderDropdownTarget> BuildHeaderTargets");
        sharedAdornmentSource.Should().Contain("public static IReadOnlyList<PivotRowLabelAdornment> BuildRowLabelAdornments");
        sharedValueFieldSource.Should().Contain("public enum PivotShowValuesAsValidationError");
        sharedValueFieldSource.Should().Contain("ResourceKeyTextResolver text");
    }
}
