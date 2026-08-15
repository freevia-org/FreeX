using FluentAssertions;

using FreeX.App.Presentation.Calculation;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FreeX.Ribbon.Definitions;
using Free.Shared.Ribbon;

namespace FreeX.App.Presentation.Tests.Calculation;

public sealed class CalculationCommandPolicyTests
{
    [Fact]
    public void ToggleTarget_UsesAutomaticForManualAndManualForBothAutomaticModes()
    {
        CalculationCommandPolicy.ToggleTarget(WorkbookCalculationMode.Manual)
            .Should().Be(WorkbookCalculationMode.Automatic);
        CalculationCommandPolicy.ToggleTarget(WorkbookCalculationMode.Automatic)
            .Should().Be(WorkbookCalculationMode.Manual);
        CalculationCommandPolicy.ToggleTarget(WorkbookCalculationMode.AutomaticExceptDataTables)
            .Should().Be(WorkbookCalculationMode.Manual);
    }

    [Theory]
    [InlineData(WorkbookCalculationMode.Automatic)]
    [InlineData(WorkbookCalculationMode.AutomaticExceptDataTables)]
    [InlineData(WorkbookCalculationMode.Manual)]
    public void ModeCommandState_ChecksExactlyTheCurrentMode(WorkbookCalculationMode currentMode)
    {
        var candidates = new[]
        {
            WorkbookCalculationMode.Automatic,
            WorkbookCalculationMode.AutomaticExceptDataTables,
            WorkbookCalculationMode.Manual,
        };

        var states = candidates.Select(candidate =>
            CalculationCommandPolicy.ModeCommandState(currentMode, candidate)).ToArray();

        states.Should().ContainSingle(state => state.IsChecked);
        states[Array.IndexOf(candidates, currentMode)].IsChecked.Should().BeTrue();
        states.Should().OnlyContain(state => state.IsEnabled);
    }

    [Fact]
    public void CanonicalCalculationMenu_DeclaresEveryModeChoiceCheckable()
    {
        var menu = FreeXRibbon.Build().Tabs
            .SelectMany(tab => tab.Groups)
            .SelectMany(group => group.Controls)
            .OfType<RibbonDropdown>()
            .Single(control => control.CommandId.Value == "Calculation Options")
            .Menu;

        menu.Items.Select(item => item.CommandId?.Value).Should().Equal(
            FreeXRibbonCommandIds.FormulasCalculationAutomatic,
            FreeXRibbonCommandIds.FormulasCalculationAutomaticExceptDataTables,
            FreeXRibbonCommandIds.FormulasCalculationManual);
        menu.Items.Should().OnlyContain(item => item.IsChecked == false);
    }

    [Fact]
    public void PlanModeChange_SuppressesNoOpAndOwnsAlreadySetFeedback()
    {
        var plan = CalculationCommandPolicy.PlanModeChange(
            WorkbookCalculationMode.Manual,
            WorkbookCalculationMode.Manual);

        plan.IsNoOp.Should().BeTrue();
        plan.Command.Should().BeNull();
        plan.RecalculationScope.Should().Be(CalculationRecalculationScope.None);
        plan.RefreshPolicy.Should().Be(CalculationStateRefreshPolicy.CommandSurface);
        plan.Status.Should().Be(new CalculationStatusPlan(
            "ShellLoc_CalculationAlreadySet",
            "ShellLoc_CalcModeManual"));
        plan.FailureResourceKey.Should().Be("ShellLoc_CouldNotChangeCalcMode");
    }

    [Theory]
    [InlineData(WorkbookCalculationMode.Automatic)]
    [InlineData(WorkbookCalculationMode.AutomaticExceptDataTables)]
    public void PlanModeChange_AutomaticVariantsCreateCommandAndRequestFullRefresh(
        WorkbookCalculationMode requestedMode)
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.CalculationMode = WorkbookCalculationMode.Manual;
        var plan = CalculationCommandPolicy.PlanModeChange(
            workbook.CalculationMode,
            requestedMode);

        plan.Command.Should().BeOfType<SetCalculationModeCommand>();
        plan.RecalculationScope.Should().Be(CalculationRecalculationScope.FullWorkbook);
        plan.RefreshPolicy.Should().Be(
            CalculationStateRefreshPolicy.CommandSurface |
            CalculationStateRefreshPolicy.FormulaResults);
        plan.Command!.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        workbook.CalculationMode.Should().Be(requestedMode);
    }

    [Fact]
    public void PlanModeChange_ManualDoesNotRequestFormulaRecalculation()
    {
        var plan = CalculationCommandPolicy.PlanModeChange(
            WorkbookCalculationMode.AutomaticExceptDataTables,
            WorkbookCalculationMode.Manual);

        plan.IsNoOp.Should().BeFalse();
        plan.RecalculationScope.Should().Be(CalculationRecalculationScope.None);
        plan.RefreshPolicy.Should().Be(CalculationStateRefreshPolicy.CommandSurface);
    }

    [Theory]
    [InlineData(CalculationCommandAction.CalculateNow, CalculationRecalculationScope.DirtyWorkbook)]
    [InlineData(CalculationCommandAction.CalculateFull, CalculationRecalculationScope.FullWorkbook)]
    [InlineData(CalculationCommandAction.CalculateActiveSheet, CalculationRecalculationScope.ActiveSheet)]
    public void PlanAction_DistinguishesForcedCalculationScopes(
        CalculationCommandAction action,
        CalculationRecalculationScope expectedScope)
    {
        var plan = CalculationCommandPolicy.PlanAction(action);

        plan.RecalculationScope.Should().Be(expectedScope);
        plan.RefreshPolicy.Should().HaveFlag(CalculationStateRefreshPolicy.FormulaResults);
        plan.Status.ResourceKey.Should().Be("ShellLoc_RecalculatedAllFormulas");
    }

    [Fact]
    public void PlanFormulaErrorRuleChanges_OnlyBuildsSupportedStateChanges()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        workbook.DisabledFormulaErrorCodes.Add(ErrorValue.DivByZero.Code);
        workbook.DisabledFormulaErrorCodes.Add(ErrorValue.Ref.Code);
        var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ErrorValue.Ref.Code,
            ErrorValue.Value.Code,
            "#NOT-SUPPORTED!"
        };

        var commands = CalculationCommandPolicy.PlanFormulaErrorRuleChanges(
            workbook.DisabledFormulaErrorCodes,
            requested);

        commands.Should().HaveCount(2);
        foreach (var command in commands)
            command.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        workbook.DisabledFormulaErrorCodes.Should().BeEquivalentTo(
            [ErrorValue.Ref.Code, ErrorValue.Value.Code]);
    }

    [Fact]
    public void PlanIterativeCalculationChange_TreatsImplicitAndExplicitDefaultsAsEquivalent()
    {
        var plan = CalculationCommandPolicy.PlanIterativeCalculationChange(
            currentEnabled: false,
            currentMaxIterations: null,
            currentMaxChange: null,
            requestedEnabled: false,
            requestedMaxIterations: CalculationCommandPolicy.DefaultMaxCalculationIterations,
            requestedMaxChange: CalculationCommandPolicy.DefaultMaxCalculationChange);

        plan.IsNoOp.Should().BeTrue();
        plan.RecalculationScope.Should().Be(CalculationRecalculationScope.None);
    }

    [Fact]
    public void PlanIterativeCalculationChange_BuildsCommandAndFullRecalculationIntent()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        var plan = CalculationCommandPolicy.PlanIterativeCalculationChange(
            workbook.IterativeCalculation,
            workbook.MaxCalculationIterations,
            workbook.MaxCalculationChange,
            requestedEnabled: true,
            requestedMaxIterations: 25,
            requestedMaxChange: 0.05);

        plan.Command.Should().BeOfType<SetIterativeCalculationOptionsCommand>();
        plan.RecalculationScope.Should().Be(CalculationRecalculationScope.FullWorkbook);
        plan.FailureResourceKey.Should().Be(CalculationCommandPolicy.FailureResourceKey);
        plan.Command!.Apply(new TestCommandContext(workbook)).Success.Should().BeTrue();
        workbook.IterativeCalculation.Should().BeTrue();
        workbook.MaxCalculationIterations.Should().Be(25);
        workbook.MaxCalculationChange.Should().Be(0.05);
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}
