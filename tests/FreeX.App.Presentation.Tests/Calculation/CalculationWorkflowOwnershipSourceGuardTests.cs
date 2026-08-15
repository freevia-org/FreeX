using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Calculation;

public sealed class CalculationWorkflowOwnershipSourceGuardTests
{
    [Fact]
    public void Hosts_DelegateCalculationExecutionAndRecalculationPolicyToPresentation()
    {
        var host = ReadSource("FreeX.App.Host", "MainWindow.FormulaCommands.cs");
        var avalonia = ReadSource("FreeX.App.Avalonia", "MainWindow.Calculation.cs");
        var hostOptions = ReadSource("FreeX.App.Host", "MainWindow.Backstage.cs");
        var avaloniaOptions = ReadSource("FreeX.App.Avalonia", "MainWindow.Options.cs");

        foreach (var source in new[] { host, avalonia })
        {
            source.Should().Contain("CalculationWorkflow.ChangeMode(");
            source.Should().Contain("CalculationWorkflow.Execute(");
            source.Should().Contain("new CalculationRecalculationOperations(");
            source.Should().NotContain("CalculationCommandPolicy.PlanModeChange(");
            source.Should().NotContain("CalculationCommandPolicy.PlanAction(");
            source.Should().NotContain("case CalculationRecalculationScope.");
        }

        foreach (var source in new[] { hostOptions, avaloniaOptions })
        {
            source.Should().Contain("CalculationOptionsSubmissionCoordinator.Apply(CalculationWorkflow, submission)");
            source.Should().Contain("CalculationWorkflow.ChangeFormulaErrorRules(");
            source.Should().NotContain("CalculationWorkflow.ChangeIterativeCalculation(");
            source.Should().NotContain("CalculationCommandPolicy.PlanIterativeCalculationChange(");
            source.Should().NotContain("CalculationCommandPolicy.PlanFormulaErrorRuleChanges(");
            source.Should().NotContain("ApplyCalculationRecalculation(");
        }
    }

    [Fact]
    public void Workflow_RemainsRendererNeutral()
    {
        var source = ReadSource(
            "FreeX.App.Presentation",
            "Calculation",
            "CalculationWorkflowSession.cs");

        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia.");
        source.Should().NotContain("FreeX.App.Host");
        source.Should().NotContain("FreeX.App.Avalonia");
    }

    [Fact]
    public void CalculationModeMenuState_IsSharedAndBothRenderersRefreshItWhenOpened()
    {
        var host = ReadSource("FreeX.App.Host", "MainWindow.FormulaCommands.cs");
        var avalonia = ReadSource("FreeX.App.Avalonia", "MainWindow.cs");
        var definition = ReadSource("FreeX.Ribbon.Definitions", "FreeXRibbonDefinition.cs");
        var renderer = File.ReadAllText(Path.Combine(
            RepositoryFileLocator.FindDirectory("shared", "Free.Shared.Ribbon.Avalonia"),
            "AvaloniaRibbonRenderer.cs"));
        var wpfRenderer = File.ReadAllText(Path.Combine(
            RepositoryFileLocator.FindDirectory("shared", "Free.Shared.Ribbon.Wpf"),
            "RibbonWpfRenderer.cs"));
        var wpfRibbon = ReadSource("FreeX.App.Host", "MainWindow.RibbonDeclarative.cs");

        host.Should().Contain("CalculationCommandPolicy.ModeCommandState(");
        host.Should().Contain("private void RefreshCalculationModeRibbonStates()");
        host.Should().NotContain("CalculationOptionsContextMenu_Opened");
        wpfRibbon.Should().Contain("RefreshCalculationModeRibbonStates();");
        avalonia.Should().ContainAll(
            "[FreeXRibbonCommandIds.FormulasCalculationAutomatic]",
            "[FreeXRibbonCommandIds.FormulasCalculationAutomaticExceptDataTables]",
            "[FreeXRibbonCommandIds.FormulasCalculationManual]",
            "CalculationCommandPolicy.ModeCommandState(");
        definition.Should().Contain("isChecked: false");
        renderer.Should().Contain("flyout.Opened += (_, _) => RefreshMenuCommandStates(flyout, registry);");
        renderer.Should().Contain("RibbonMenuCommandStatePlanner.Plan(");
        renderer.Should().Contain("item.IsChecked = isChecked;");
        wpfRenderer.Should().Contain("contextMenu.Opened += (_, _) => RefreshMenuCommandStates(");
        wpfRenderer.Should().Contain("RibbonMenuCommandStatePlanner.Plan(");
        wpfRenderer.Should().Contain("item.IsChecked = isChecked;");
    }

    [Fact]
    public void OptionsSubmissionPlanningAndExecution_AreOwnedByPresentation()
    {
        var hostDialog = ReadSource("FreeX.App.Host", "OptionsDialog.xaml.cs");
        var host = ReadSource("FreeX.App.Host", "MainWindow.Backstage.cs");
        var avalonia = ReadSource("FreeX.App.Avalonia", "MainWindow.Options.cs");

        hostDialog.Should().Contain("CalculationOptionsSubmissionPlanner.Plan(");
        host.Should().Contain("CalculationOptionsDialogState.FromWorkbook(_workbook)");
        avalonia.Should().Contain("CalculationOptionsDialogState.FromWorkbook(workbook)");
        avalonia.Should().Contain("CalculationOptionsSubmissionPlanner.Plan(");
        hostDialog.Should().NotContain("OptionsDialogCalculationSettings");
        host.Should().NotContain("ApplyOptionsCalculationSettings");
    }

    private static string ReadSource(string projectName, params string[] path)
    {
        var projectRoot = RepositoryFileLocator.FindDirectory("src", projectName);
        return File.ReadAllText(Path.Combine(projectRoot, Path.Combine(path)));
    }
}
