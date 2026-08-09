using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Calculation;

public sealed class CommandPolicyTailOwnershipSourceGuardTests
{
    [Fact]
    public void OptionsRenderersDelegateFormulaErrorAndIterativeCommandPlanning()
    {
        var wpf = ReadSource("src", "FreeX.App.Host", "MainWindow.Backstage.cs");
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.Options.cs");
        var paired = wpf + Environment.NewLine + avalonia;

        wpf.Should().Contain("CalculationCommandPolicy.PlanFormulaErrorRuleChanges(");
        avalonia.Should().Contain("CalculationCommandPolicy.PlanFormulaErrorRuleChanges(");
        wpf.Should().Contain("CalculationCommandPolicy.PlanIterativeCalculationChange(");
        avalonia.Should().Contain("CalculationCommandPolicy.PlanIterativeCalculationChange(");
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

    private static string ReadSource(params string[] parts)
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = Directory.GetParent(presentationRoot)?.Parent?.FullName
            ?? throw new DirectoryNotFoundException("Could not resolve repository root.");
        return File.ReadAllText(Path.Combine([repoRoot, .. parts]));
    }
}
