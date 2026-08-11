using FluentAssertions;

using FreeX.App.Presentation.Calculation;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Calculation;

public sealed class CalculationOptionsSubmissionTests
{
    [Fact]
    public void Plan_UnchangedAutomaticChoicePreservesAutomaticExceptDataTables()
    {
        var initial = new CalculationOptionsDialogState(
            AutoCalculate: true,
            IterativeCalculation: false,
            MaxCalculationIterations: null,
            MaxCalculationChange: null,
            WorkbookCalculationMode.AutomaticExceptDataTables);

        var submission = CalculationOptionsSubmissionPlanner.Plan(
            initial,
            autoCalculate: true,
            iterativeCalculation: false,
            CalculationCommandPolicy.DefaultMaxCalculationIterations,
            CalculationCommandPolicy.DefaultMaxCalculationChange);

        submission.Should().BeNull();
    }

    [Fact]
    public void Plan_IterativeOnlyEditDoesNotCollapseAutomaticExceptDataTables()
    {
        var initial = new CalculationOptionsDialogState(
            true,
            false,
            null,
            null,
            WorkbookCalculationMode.AutomaticExceptDataTables);

        var submission = CalculationOptionsSubmissionPlanner.Plan(
            initial,
            autoCalculate: true,
            iterativeCalculation: true,
            maxCalculationIterations: 50,
            maxCalculationChange: 0.01);

        submission.Should().NotBeNull();
        submission!.RequestedMode.Should().BeNull();
        submission.IterativeCalculation.Should().Be(new IterativeCalculationSubmission(true, 50, 0.01));
    }

    [Theory]
    [InlineData(true, false, WorkbookCalculationMode.Manual)]
    [InlineData(false, true, WorkbookCalculationMode.Automatic)]
    public void Plan_ChangedTwoStateChoiceRequestsExpectedMode(
        bool initialAutoCalculate,
        bool requestedAutoCalculate,
        WorkbookCalculationMode expectedMode)
    {
        var initial = new CalculationOptionsDialogState(
            initialAutoCalculate,
            false,
            null,
            null);

        var submission = CalculationOptionsSubmissionPlanner.Plan(
            initial,
            requestedAutoCalculate,
            iterativeCalculation: false,
            CalculationCommandPolicy.DefaultMaxCalculationIterations,
            CalculationCommandPolicy.DefaultMaxCalculationChange);

        submission!.RequestedMode.Should().Be(expectedMode);
        submission.IterativeCalculation.Should().BeNull();
    }

    [Fact]
    public void Coordinator_AppliesModeAndIterativeChangesThroughWorkflow()
    {
        var workbook = new Workbook("Book")
        {
            CalculationMode = WorkbookCalculationMode.AutomaticExceptDataTables,
        };
        workbook.AddSheet("Sheet1");
        var recalculationCount = 0;
        var workflow = new CalculationWorkflowSession(
            workbook,
            (command, _) =>
            {
                var result = command.Apply(new TestCommandContext(workbook));
                return new CalculationCommandExecutionResult(result.Success, result.ErrorMessage);
            },
            new CalculationRecalculationOperations(
                () => recalculationCount++,
                () => recalculationCount++,
                () => recalculationCount++));
        var submission = new CalculationOptionsSubmission(
            WorkbookCalculationMode.Manual,
            new IterativeCalculationSubmission(true, 25, 0.05));

        var outcome = CalculationOptionsSubmissionCoordinator.Apply(workflow, submission);

        outcome.Success.Should().BeTrue();
        workbook.CalculationMode.Should().Be(WorkbookCalculationMode.Manual);
        workbook.IterativeCalculation.Should().BeTrue();
        workbook.MaxCalculationIterations.Should().Be(25);
        workbook.MaxCalculationChange.Should().Be(0.05);
        recalculationCount.Should().Be(1);
    }

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) => Workbook.GetSheet(sheetId)!;
    }
}
