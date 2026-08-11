using FluentAssertions;
using FreeX.App.Presentation.Consolidate;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Consolidate;

public sealed class ConsolidateApplicationWorkflowTests
{
    [Fact]
    public void Plan_ClassifiesOverwriteConfirmationAndConfirmedRequest()
    {
        var workbook = new Workbook("Consolidate");
        var sheet = workbook.AddSheet("Data");
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(source, new NumberValue(2));
        sheet.SetCell(destination, new TextValue("old"));
        var request = Request(source, destination);

        var confirmation = ConsolidateApplicationWorkflow.Plan(workbook, request, overwriteConfirmed: false);
        var ready = ConsolidateApplicationWorkflow.Plan(workbook, request, overwriteConfirmed: true);

        confirmation.Disposition.Should().Be(ConsolidateApplicationDisposition.ConfirmOverwrite);
        confirmation.ApplyPlan!.OverwriteTargets.Should().Equal(destination);
        confirmation.CanExecute.Should().BeFalse();
        ready.Disposition.Should().Be(ConsolidateApplicationDisposition.Ready);
        ready.CanExecute.Should().BeTrue();
    }

    [Fact]
    public void Plan_ReportsRendererNeutralValidationIssue()
    {
        var workbook = new Workbook("Consolidate");
        var sheet = workbook.AddSheet("Data");
        var missingSheet = SheetId.New();
        var request = Request(
            new CellAddress(missingSheet, 1, 1),
            new CellAddress(sheet.Id, 1, 2));

        var plan = ConsolidateApplicationWorkflow.Plan(workbook, request, overwriteConfirmed: false);

        plan.Disposition.Should().Be(ConsolidateApplicationDisposition.Invalid);
        plan.Issue.Kind.Should().Be(ConsolidateDialogIssueKind.InvalidSourceRange);
        plan.CanExecute.Should().BeFalse();
    }

    [Fact]
    public void Execute_OwnsCommandCreationAndNormalizesAdapterOutcome()
    {
        var workbook = new Workbook("Consolidate");
        var sheet = workbook.AddSheet("Data");
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(source, new NumberValue(7));
        var plan = ConsolidateApplicationWorkflow.Plan(
            workbook,
            Request(source, destination),
            overwriteConfirmed: true);

        var outcome = ConsolidateApplicationWorkflow.Execute(
            plan,
            commandFactory =>
            {
                var result = commandFactory().Apply(new TestCommandContext(workbook));
                return new ConsolidateCommandAdapterResult(result.Success, result.ErrorMessage);
            });

        outcome.Status.Should().Be(ConsolidateExecutionStatus.Applied);
        outcome.DestinationCell.Should().Be(destination);
        sheet.GetValue(destination.Row, destination.Col).Should().Be(new NumberValue(7));
    }

    [Fact]
    public void Execute_CapturesAdapterExceptionAsFailure()
    {
        var workbook = new Workbook("Consolidate");
        var sheet = workbook.AddSheet("Data");
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(source, new NumberValue(7));
        var plan = ConsolidateApplicationWorkflow.Plan(
            workbook,
            Request(source, destination),
            overwriteConfirmed: true);

        var outcome = ConsolidateApplicationWorkflow.Execute(
            plan,
            _ => throw new InvalidOperationException("protected"));

        outcome.Status.Should().Be(ConsolidateExecutionStatus.Failed);
        outcome.ErrorMessage.Should().Be("protected");
    }

    private static ConsolidateDialogResult Request(CellAddress source, CellAddress destination) =>
        new(
            [new GridRange(source, source)],
            destination,
            ConsolidateFunction.Sum);

    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }
}
