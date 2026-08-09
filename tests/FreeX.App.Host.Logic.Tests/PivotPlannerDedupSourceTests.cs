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
        var resolverSource = DialogSourceTestSupport.ReadPresentationSources("PivotUI", "PivotSourceHeaderResolver.cs");

        File.Exists(hostPlannerPath)
            .Should()
            .BeFalse("GETPIVOTDATA composition is model-driven pivot UI planning, not WPF Host rendering");
        File.Exists(hostResolverPath)
            .Should()
            .BeFalse("pivot cache header fallback is shared PivotUI metadata resolution, not WPF Host rendering");

        formulaEditingSource.Should().Contain("GetPivotDataFormulaPlanner.CreatePointModeFunctionCall(");
        formulaEditingSource.Should().NotContain("GetPivotDataFormulaPlanner.Create(");
        pivotCommandsSource.Should().Contain("PivotSourceContext.ReadHeaders(");
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
        var headerSource = DialogSourceTestSupport.ReadHostSourceFile("PivotHeaderDropdownPlanner.cs");
        var adornmentSource = DialogSourceTestSupport.ReadHostSourceFile("PivotRowLabelAdornmentPlanner.cs");
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

        headerSource.Should().Contain("using SharedPivotGridAdornmentPlanner = FreeX.App.Presentation.PivotUI.PivotGridAdornmentPlanner;");
        headerSource.Should().Contain("SharedPivotGridAdornmentPlanner.BuildHeaderTargets(workbook, sheet)");
        headerSource.Should().NotContain("private static void AddTargets");
        headerSource.Should().NotContain("private static IReadOnlyList<string> ReadHeaders");

        adornmentSource.Should().Contain("SharedPivotGridAdornmentPlanner.BuildRowLabelAdornments(workbook, sheet)");
        adornmentSource.Should().NotContain("private static void AddAdornments");
        adornmentSource.Should().NotContain("private static bool HasChildRowsBeforeNextPeer");

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
