using FluentAssertions;

using FreeX.App.Presentation.Calculation;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Calculation;

public sealed class CalculationWorkflowSessionTests
{
    [Fact]
    public void ChangeMode_NoOpSkipsExecutionAndRecalculation()
    {
        var workbook = CreateWorkbook(WorkbookCalculationMode.Manual);
        var executionCount = 0;
        var recalculations = new List<string>();
        var session = CreateSession(
            workbook,
            (_, _) =>
            {
                executionCount++;
                return new CalculationCommandExecutionResult(true);
            },
            recalculations);

        var outcome = session.ChangeMode(WorkbookCalculationMode.Manual);

        outcome.Success.Should().BeTrue();
        outcome.ExecutedCommand.Should().BeFalse();
        outcome.IsNoOp.Should().BeTrue();
        outcome.RefreshPolicy.Should().Be(CalculationStateRefreshPolicy.CommandSurface);
        outcome.Status.Should().Be(new CalculationStatusPlan(
            "ShellLoc_CalculationAlreadySet",
            "ShellLoc_CalcModeManual"));
        executionCount.Should().Be(0);
        recalculations.Should().BeEmpty();
    }

    [Fact]
    public void ChangeMode_ExecutesBeforeApplyingRequestedRecalculation()
    {
        var workbook = CreateWorkbook(WorkbookCalculationMode.Manual);
        var operations = new List<string>();
        var session = CreateSession(
            workbook,
            (command, label) =>
            {
                operations.Add($"execute:{label}");
                var result = command.Apply(new TestCommandContext(workbook));
                return new CalculationCommandExecutionResult(result.Success, result.ErrorMessage);
            },
            operations);

        var outcome = session.ChangeMode(WorkbookCalculationMode.Automatic);

        outcome.Success.Should().BeTrue();
        outcome.ExecutedCommand.Should().BeTrue();
        outcome.IsNoOp.Should().BeFalse();
        outcome.RecalculationScope.Should().Be(CalculationRecalculationScope.FullWorkbook);
        outcome.RefreshPolicy.Should().Be(
            CalculationStateRefreshPolicy.CommandSurface |
            CalculationStateRefreshPolicy.FormulaResults);
        workbook.CalculationMode.Should().Be(WorkbookCalculationMode.Automatic);
        operations.Should().Equal(
            $"execute:{CalculationCommandPolicy.CommandLabel}",
            "full");
    }

    [Fact]
    public void ChangeMode_FailureSkipsRecalculationAndReturnsRendererFeedback()
    {
        var workbook = CreateWorkbook(WorkbookCalculationMode.Manual);
        var recalculations = new List<string>();
        var session = CreateSession(
            workbook,
            (_, _) => new CalculationCommandExecutionResult(
                Success: false,
                ErrorMessage: "protected"),
            recalculations);

        var outcome = session.ChangeMode(WorkbookCalculationMode.Automatic);

        outcome.Success.Should().BeFalse();
        outcome.ExecutedCommand.Should().BeTrue();
        outcome.RecalculationScope.Should().Be(CalculationRecalculationScope.None);
        outcome.RefreshPolicy.Should().Be(CalculationStateRefreshPolicy.None);
        outcome.FailureResourceKey.Should().Be(CalculationCommandPolicy.FailureResourceKey);
        outcome.ErrorMessage.Should().Be("protected");
        recalculations.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CalculationCommandAction.CalculateNow, "dirty")]
    [InlineData(CalculationCommandAction.CalculateFull, "full")]
    [InlineData(CalculationCommandAction.CalculateActiveSheet, "sheet")]
    public void Execute_DispatchesEachActionThroughTheMatchingPortableOperation(
        CalculationCommandAction action,
        string expectedOperation)
    {
        var workbook = CreateWorkbook(WorkbookCalculationMode.Automatic);
        var recalculations = new List<string>();
        var session = CreateSession(
            workbook,
            (_, _) => throw new InvalidOperationException("Actions do not execute mode commands."),
            recalculations);

        var outcome = session.Execute(action);

        outcome.Success.Should().BeTrue();
        outcome.ExecutedCommand.Should().BeFalse();
        outcome.RefreshPolicy.Should().HaveFlag(CalculationStateRefreshPolicy.CommandSurface);
        outcome.RefreshPolicy.Should().HaveFlag(CalculationStateRefreshPolicy.FormulaResults);
        outcome.Status.ResourceKey.Should().Be("ShellLoc_RecalculatedAllFormulas");
        recalculations.Should().Equal(expectedOperation);
    }

    [Fact]
    public void ChangeIterativeCalculation_EquivalentDefaultsAreANoOp()
    {
        var workbook = CreateWorkbook(WorkbookCalculationMode.Automatic);
        var executionCount = 0;
        var recalculations = new List<string>();
        var session = CreateSession(
            workbook,
            (_, _) =>
            {
                executionCount++;
                return new CalculationCommandExecutionResult(true);
            },
            recalculations);

        var outcome = session.ChangeIterativeCalculation(
            enabled: false,
            maxIterations: CalculationCommandPolicy.DefaultMaxCalculationIterations,
            maxChange: CalculationCommandPolicy.DefaultMaxCalculationChange);

        outcome.Success.Should().BeTrue();
        outcome.ExecutedCommand.Should().BeFalse();
        outcome.IsNoOp.Should().BeTrue();
        executionCount.Should().Be(0);
        recalculations.Should().BeEmpty();
    }

    [Fact]
    public void ChangeIterativeCalculation_ExecutesAndRecalculatesTheWorkbook()
    {
        var workbook = CreateWorkbook(WorkbookCalculationMode.Automatic);
        var operations = new List<string>();
        var session = CreateSession(
            workbook,
            (command, label) =>
            {
                operations.Add($"execute:{label}");
                var result = command.Apply(new TestCommandContext(workbook));
                return new CalculationCommandExecutionResult(result.Success, result.ErrorMessage);
            },
            operations);

        var outcome = session.ChangeIterativeCalculation(
            enabled: true,
            maxIterations: 25,
            maxChange: 0.05);

        outcome.Success.Should().BeTrue();
        outcome.ExecutedCommand.Should().BeTrue();
        outcome.RecalculationScope.Should().Be(CalculationRecalculationScope.FullWorkbook);
        workbook.IterativeCalculation.Should().BeTrue();
        workbook.MaxCalculationIterations.Should().Be(25);
        workbook.MaxCalculationChange.Should().Be(0.05);
        operations.Should().Equal(
            $"execute:{CalculationCommandPolicy.CommandLabel}",
            "full");
    }

    [Fact]
    public void ChangeIterativeCalculation_FailureSkipsRecalculation()
    {
        var workbook = CreateWorkbook(WorkbookCalculationMode.Automatic);
        var recalculations = new List<string>();
        var session = CreateSession(
            workbook,
            (_, _) => new CalculationCommandExecutionResult(false, "blocked"),
            recalculations);

        var outcome = session.ChangeIterativeCalculation(
            enabled: true,
            maxIterations: 25,
            maxChange: 0.05);

        outcome.Success.Should().BeFalse();
        outcome.FailureResourceKey.Should().Be(CalculationCommandPolicy.FailureResourceKey);
        outcome.ErrorMessage.Should().Be("blocked");
        recalculations.Should().BeEmpty();
    }

    [Fact]
    public void ChangeFormulaErrorRules_ExecutesThePortableChangeSetInOrder()
    {
        var workbook = CreateWorkbook(WorkbookCalculationMode.Automatic);
        workbook.DisabledFormulaErrorCodes.Add(ErrorValue.DivByZero.Code);
        var labels = new List<string>();
        var session = CreateSession(
            workbook,
            (command, label) =>
            {
                labels.Add(label);
                var result = command.Apply(new TestCommandContext(workbook));
                return new CalculationCommandExecutionResult(result.Success, result.ErrorMessage);
            },
            new List<string>());

        var outcome = session.ChangeFormulaErrorRules([ErrorValue.Ref.Code]);

        outcome.Success.Should().BeTrue();
        outcome.PlannedCommandCount.Should().Be(2);
        outcome.ExecutedCommandCount.Should().Be(2);
        workbook.DisabledFormulaErrorCodes.Should().BeEquivalentTo([ErrorValue.Ref.Code]);
        labels.Should().OnlyContain(label =>
            label == CalculationCommandPolicy.FormulaErrorRulesCommandLabel);
    }

    [Fact]
    public void ChangeFormulaErrorRules_StopsAtTheFirstExecutionFailure()
    {
        var workbook = CreateWorkbook(WorkbookCalculationMode.Automatic);
        workbook.DisabledFormulaErrorCodes.Add(ErrorValue.DivByZero.Code);
        var executionCount = 0;
        var session = CreateSession(
            workbook,
            (_, _) =>
            {
                executionCount++;
                return new CalculationCommandExecutionResult(false, "blocked");
            },
            new List<string>());

        var outcome = session.ChangeFormulaErrorRules([ErrorValue.Ref.Code]);

        outcome.Success.Should().BeFalse();
        outcome.PlannedCommandCount.Should().Be(2);
        outcome.ExecutedCommandCount.Should().Be(1);
        outcome.ErrorMessage.Should().Be("blocked");
        executionCount.Should().Be(1);
        workbook.DisabledFormulaErrorCodes.Should().Equal(ErrorValue.DivByZero.Code);
    }

    private static Workbook CreateWorkbook(WorkbookCalculationMode calculationMode)
    {
        var workbook = new Workbook("Book")
        {
            CalculationMode = calculationMode,
        };
        workbook.AddSheet("Sheet1");
        return workbook;
    }

    private static CalculationWorkflowSession CreateSession(
        Workbook workbook,
        CalculationCommandExecutor execute,
        ICollection<string> recalculations) =>
        new(
            workbook,
            execute,
            new CalculationRecalculationOperations(
                () => recalculations.Add("dirty"),
                () => recalculations.Add("full"),
                () => recalculations.Add("sheet")));

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}
