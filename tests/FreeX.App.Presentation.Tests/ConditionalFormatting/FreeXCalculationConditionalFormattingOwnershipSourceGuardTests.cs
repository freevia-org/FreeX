using FluentAssertions;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

public sealed class FreeXCalculationConditionalFormattingOwnershipSourceGuardTests
{
    [Fact]
    public void WpfAndAvaloniaCalculationSurfacesDelegatePortableDecisionsToPolicy()
    {
        var wpf = ReadSource("src", "FreeX.App.Host", "MainWindow.FormulaCommands.cs");
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.Calculation.cs");
        var avaloniaKeyboard = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.KeyboardParity.cs");
        var paired = wpf + Environment.NewLine + avalonia;

        wpf.Should().Contain("CalculationCommandPolicy.PlanModeChange(");
        avalonia.Should().Contain("CalculationCommandPolicy.PlanModeChange(");
        wpf.Should().Contain("CalculationCommandPolicy.PlanAction(");
        avalonia.Should().Contain("CalculationCommandPolicy.PlanAction(");
        paired.Should().Contain("CalculationStateRefreshPolicy.FormulaResults");
        paired.Should().NotContain("new SetCalculationModeCommand(");
        avalonia.Should().Contain("_session.RecalculateDirtyCells();");
        avaloniaKeyboard.Replace("\r\n", "\n", StringComparison.Ordinal).Should().Contain(
            "case AvaloniaHostShortcut.RebuildDependenciesAndCalculate:\n                CalculateFull();");

        var policy = ReadSource(
            "src",
            "FreeX.App.Presentation",
            "Calculation",
            "CalculationCommandPolicy.cs");
        AssertPortable(policy);
        policy.Should().Contain("CalculationCommandAction.CalculateNow => CalculationRecalculationScope.DirtyWorkbook");
        policy.Should().Contain("new SetCalculationModeCommand(requestedMode)");
        policy.Should().Contain("ShellLoc_CalculationAlreadySet");
        policy.Should().Contain("ShellLoc_CouldNotChangeCalcMode");
    }

    [Fact]
    public void WpfAndAvaloniaConditionalFormatSurfacesDelegateMutationAndFeedbackPlanning()
    {
        var wpf = ReadSource("src", "FreeX.App.Host", "MainWindow.HomeFormatting.cs");
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.ConditionalFormat.cs");
        var paired = wpf + Environment.NewLine + avalonia;

        wpf.Should().Contain("ConditionalFormatCommandPlanner.PlanClear(");
        wpf.Should().Contain("ConditionalFormatCommandPlanner.PlanApplyRule(");
        wpf.Should().Contain("ConditionalFormatCommandPlanner.PlanReplaceAll(");
        avalonia.Should().Contain("ConditionalFormatCommandPlanner.PlanApplyPreset(");
        avalonia.Should().Contain("ConditionalFormatCommandPlanner.PlanApplyIconSet(");
        avalonia.Should().Contain("ConditionalFormatCommandPlanner.PlanApplyHighlightGreaterThan(");
        avalonia.Should().Contain("manageSession.CreateApplyPlan(");
        paired.Should().Contain("ConditionalFormatStateRefreshPolicy.WorksheetVisualState");
        paired.Should().NotContain("new ClearConditionalFormatsCommand(");
        paired.Should().NotContain("new ApplyConditionalFormatCommand(");
        paired.Should().NotContain("new ReplaceAllConditionalFormatsCommand(");
        avalonia.Should().NotContain("presetInput = presetInput with { IsTop = false }");
        avalonia.Should().Contain("ConditionalFormatInputParser.FormatRgb(color)");
        avalonia.Should().Contain("ManageConditionalFormatsPlanner.NormalizeAppliesToText(");

        var planner = ReadSource(
            "src",
            "FreeX.App.Presentation",
            "ConditionalFormatting",
            "ConditionalFormatCommandPlanner.cs");
        AssertPortable(planner);
        planner.Should().Contain("InsertLoc_CfFailed");
        planner.Should().Contain("InsertLoc_CfManageRulesApplied");
    }

    private static void AssertPortable(string source)
    {
        source.Should().NotContain("System.Windows");
        source.Should().NotContain("Avalonia");
        source.Should().NotContain("FreeX.App.Host");
        source.Should().NotContain("FreeX.App.Avalonia");
    }

    private static string ReadSource(params string[] parts)
    {
        var presentationRoot = RepositoryFileLocator.FindDirectory("src", "FreeX.App.Presentation");
        var repoRoot = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");
        return File.ReadAllText(Path.Combine([repoRoot, .. parts]));
    }
}
