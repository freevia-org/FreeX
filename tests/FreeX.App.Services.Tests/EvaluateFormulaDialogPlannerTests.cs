using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class EvaluateFormulaDialogPlannerTests
{
    [Fact]
    public void CreateSummary_ReturnsFormulaSummaryForFormulaCell()
    {
        var workbook = new Workbook("Evaluate Formula");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));
        var formulaAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(formulaAddress, new Cell
        {
            FormulaText = "B1*2",
            Value = new NumberValue(10)
        });

        var summary = EvaluateFormulaDialogPlanner.CreateSummary(workbook, formulaAddress);

        summary.Should().NotBeNull();
        summary!.SheetName.Should().Be("Sheet1");
        summary.Address.Should().Be(formulaAddress);
        summary.FormulaText.Should().Be("=B1*2");
        summary.ValueText.Should().Be("10");
        summary.Steps.Select(step => (step.Expression, step.ValueText))
            .Should().Equal(("B1", "5"), ("2", "2"), ("B1*2", "10"));
    }

    [Fact]
    public void CreateSummary_ReturnsNullForNonFormulaCell()
    {
        var workbook = new Workbook("Evaluate Formula");
        var sheet = workbook.AddSheet("Sheet1");
        var valueAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(valueAddress, new NumberValue(10));

        EvaluateFormulaDialogPlanner.CreateSummary(workbook, valueAddress).Should().BeNull();
    }

    [Fact]
    public void CreateSession_StartsAtFirstEvaluationStep()
    {
        var workbook = new Workbook("Evaluate Formula");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(5));
        var formulaAddress = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(formulaAddress, new Cell
        {
            FormulaText = "B1*2",
            Value = new NumberValue(10)
        });
        var summary = EvaluateFormulaDialogPlanner.CreateSummary(workbook, formulaAddress)!;

        var session = EvaluateFormulaDialogPlanner.CreateSession(summary);

        session.Summary.Should().Be(summary);
        session.CurrentStep.Should().Be(summary.Steps[0]);
        session.CurrentHighlight.Prefix.Should().Be("=");
        session.CurrentHighlight.Highlight.Should().Be("B1");
        session.CanMoveNext.Should().BeTrue();
    }
}
