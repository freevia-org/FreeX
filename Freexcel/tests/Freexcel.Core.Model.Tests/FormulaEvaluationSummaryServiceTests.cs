using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class FormulaEvaluationSummaryServiceTests
{
    [Fact]
    public void GetSummary_ReturnsFormulaAndCurrentValueForFormulaCell()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new Cell
        {
            FormulaText = "B1*2",
            Value = new NumberValue(10)
        });

        var summary = FormulaEvaluationSummaryService.GetSummary(workbook, address);

        summary.Should().NotBeNull();
        summary!.FormulaText.Should().Be("=B1*2");
        summary.ValueText.Should().Be("10");
    }

    [Fact]
    public void GetSummary_ReturnsEvaluationStepsForReferencesAndExpressions()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new Cell
        {
            FormulaText = "B1*2",
            Value = new NumberValue(10)
        });

        var summary = FormulaEvaluationSummaryService.GetSummary(workbook, address);

        summary.Should().NotBeNull();
        summary!.Steps.Select(step => (step.Expression, step.ValueText))
            .Should().Equal(("B1", "5"), ("2", "2"), ("B1*2", "10"));
    }

    [Fact]
    public void FormulaEvaluationSession_StepsForwardAndBackwardThroughSummary()
    {
        var summary = new FormulaEvaluationSummary(
            new SheetId(Guid.NewGuid()),
            "Sheet1",
            new CellAddress(new SheetId(Guid.NewGuid()), 1, 1),
            "=B1*2",
            "10",
            [
                new FormulaEvaluationStep("B1", "5"),
                new FormulaEvaluationStep("2", "2"),
                new FormulaEvaluationStep("B1*2", "10")
            ]);

        var session = FormulaEvaluationSession.Start(summary);

        session.CurrentStep.Should().Be(summary.Steps[0]);
        session.CurrentStepNumber.Should().Be(1);
        session.CanMovePrevious.Should().BeFalse();
        session.CanMoveNext.Should().BeTrue();

        session.MoveNext().Should().BeTrue();
        session.CurrentStep.Should().Be(summary.Steps[1]);
        session.CurrentStepNumber.Should().Be(2);
        session.CanMovePrevious.Should().BeTrue();

        session.MovePrevious().Should().BeTrue();
        session.CurrentStep.Should().Be(summary.Steps[0]);
    }

    [Fact]
    public void FormulaEvaluationSession_ExposesCurrentFormulaHighlight()
    {
        var summary = new FormulaEvaluationSummary(
            new SheetId(Guid.NewGuid()),
            "Sheet1",
            new CellAddress(new SheetId(Guid.NewGuid()), 1, 1),
            "=B1*2",
            "10",
            [
                new FormulaEvaluationStep("B1", "5"),
                new FormulaEvaluationStep("2", "2"),
                new FormulaEvaluationStep("B1*2", "10")
            ]);

        var session = FormulaEvaluationSession.Start(summary);

        session.CurrentHighlight.Should().Be(new FormulaEvaluationHighlight("=", "B1", "*2"));

        session.MoveNext();

        session.CurrentHighlight.Should().Be(new FormulaEvaluationHighlight("=B1*", "2", ""));
    }

    [Fact]
    public void FormulaEvaluationSession_StepOutMovesToContainingExpression()
    {
        var summary = new FormulaEvaluationSummary(
            new SheetId(Guid.NewGuid()),
            "Sheet1",
            new CellAddress(new SheetId(Guid.NewGuid()), 1, 1),
            "=SUM(B1*2,C1)",
            "13",
            [
                new FormulaEvaluationStep("B1", "5"),
                new FormulaEvaluationStep("2", "2"),
                new FormulaEvaluationStep("B1*2", "10"),
                new FormulaEvaluationStep("C1", "3"),
                new FormulaEvaluationStep("SUM(B1*2,C1)", "13")
            ]);

        var session = FormulaEvaluationSession.Start(summary);

        session.StepOut().Should().BeTrue();

        session.CurrentStep.Should().Be(summary.Steps[2]);
        session.CurrentStepNumber.Should().Be(3);

        session.StepOut().Should().BeTrue();
        session.CurrentStep.Should().Be(summary.Steps[4]);
    }

    [Fact]
    public void GetSummary_ReturnsNullForNonFormulaCell()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(address, new NumberValue(10));

        FormulaEvaluationSummaryService.GetSummary(workbook, address).Should().BeNull();
    }
}
