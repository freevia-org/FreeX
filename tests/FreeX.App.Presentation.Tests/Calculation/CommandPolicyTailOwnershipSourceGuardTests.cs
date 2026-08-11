using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Calculation;

public sealed class CommandPolicyTailOwnershipSourceGuardTests
{
    [Fact]
    public void OptionsRenderersDelegateFormulaErrorAndCalculationSubmissionWorkflows()
    {
        var wpf = ReadSource("src", "FreeX.App.Host", "MainWindow.Backstage.cs");
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.Options.cs");
        var paired = wpf + Environment.NewLine + avalonia;

        wpf.Should().Contain("CalculationWorkflow.ChangeFormulaErrorRules(");
        avalonia.Should().Contain("CalculationWorkflow.ChangeFormulaErrorRules(");
        wpf.Should().Contain("CalculationOptionsSubmissionCoordinator.Apply(CalculationWorkflow, submission)");
        avalonia.Should().Contain("CalculationOptionsSubmissionCoordinator.Apply(CalculationWorkflow, submission)");
        paired.Should().NotContain("CalculationCommandPolicy.PlanFormulaErrorRuleChanges(");
        paired.Should().NotContain("CalculationCommandPolicy.PlanIterativeCalculationChange(");
        paired.Should().NotContain("ApplyCalculationRecalculation(");
        paired.Should().NotContain("new SetFormulaErrorCheckingRuleCommand(");
        paired.Should().NotContain("new SetIterativeCalculationOptionsCommand(");
    }

    [Fact]
    public void MergeRenderersDelegateCommandWrapping()
    {
        var wpf = ReadSource("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs");
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.MergePaste.cs");
        var paired = wpf + Environment.NewLine + avalonia;

        wpf.Should().Contain("CellMergePlanner.CreateMergeCellsCommand(");
        wpf.Should().Contain("CellMergePlanner.CreateMergeAcrossCommand(");
        avalonia.Should().Contain("CellMergePlanner.CreateMergeCellsCommand(");
        avalonia.Should().Contain("CellMergePlanner.CreateMergeAcrossCommand(");
        avalonia.Should().Contain("CellMergePlanner.WrapCommands(\"Merge Cells\"");
        avalonia.Should().Contain("CellMergePlanner.WrapCommands(\"Merge Across\"");
        paired.Should().NotContain("new CompositeWorkbookCommand(\"Merge Cells\"");
        paired.Should().NotContain("new CompositeWorkbookCommand(\"Merge Across\"");

        var planner = ReadSource("src", "FreeX.App.Services", "CellMergePlanner.cs");
        planner.Should().NotContain("using System.Windows");
        planner.Should().NotContain("using Avalonia");
    }

    private static string ReadSource(params string[] parts) =>
        TestWorkspaceFileLocator.ReadAllText(parts);
}
