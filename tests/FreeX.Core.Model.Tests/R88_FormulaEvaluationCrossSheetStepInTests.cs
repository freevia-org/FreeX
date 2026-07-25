using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R88-app-formula-auditing-5-4: Evaluate Formula "Step In" was unavailable for a precedent
/// that lives on another sheet, even though the precedent itself holds its own formula.
/// TryBuildNestedSummary used to match only `CellRefNode { SheetName: null }`, so any reference
/// carrying an explicit SheetName (every cross-sheet precedent) returned null and never got a
/// NestedSummary, leaving CanStepIn permanently false for that step.
/// </summary>
public sealed class R88_FormulaEvaluationCrossSheetStepInTests
{
    [Fact]
    public void GetSummary_AttachesNestedSummaryForCrossSheetFormulaReference()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new Cell
        {
            FormulaText = "10*2",
            Value = new NumberValue(20)
        });
        var address = new CellAddress(sheet1.Id, 1, 2);
        sheet1.SetCell(address, new Cell
        {
            FormulaText = "Sheet2!A1+1",
            Value = new NumberValue(21)
        });

        var summary = FormulaEvaluationSummaryService.GetSummary(workbook, address);

        summary.Should().NotBeNull();
        var referenceStep = summary!.Steps[0];
        referenceStep.ValueText.Should().Be("20");
        referenceStep.NestedSummary.Should().NotBeNull(
            "a cross-sheet reference to a cell that itself holds a formula must support Step In, matching Excel");
        referenceStep.NestedSummary!.SheetId.Should().Be(sheet2.Id);
        referenceStep.NestedSummary.Address.Should().Be(new CellAddress(sheet2.Id, 1, 1));
        referenceStep.NestedSummary.FormulaText.Should().Be("=10*2");
        referenceStep.NestedSummary.Steps.Select(step => (step.Expression, step.ValueText))
            .Should().Equal(("10", "10"), ("2", "2"), ("10*2", "20"));
    }

    [Fact]
    public void FormulaEvaluationSession_StepInAndStepOutNavigateCrossSheetFormulaReference()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new Cell
        {
            FormulaText = "10*2",
            Value = new NumberValue(20)
        });
        var address = new CellAddress(sheet1.Id, 1, 2);
        sheet1.SetCell(address, new Cell
        {
            FormulaText = "Sheet2!A1+1",
            Value = new NumberValue(21)
        });
        var summary = FormulaEvaluationSummaryService.GetSummary(workbook, address)!;
        var session = FormulaEvaluationSession.Start(summary);

        session.CanStepIn.Should().BeTrue();
        session.StepIn().Should().BeTrue();

        session.Summary.SheetId.Should().Be(sheet2.Id);
        session.Summary.Address.Should().Be(new CellAddress(sheet2.Id, 1, 1));
        session.Summary.FormulaText.Should().Be("=10*2");
        session.CanStepOut.Should().BeTrue();

        session.StepOut().Should().BeTrue();

        session.Summary.Should().Be(summary);
        session.CurrentStep.Should().Be(summary.Steps[0]);
    }

    // No-regression sibling: a cross-sheet reference to a plain VALUE cell (no formula) must
    // still get no NestedSummary/Step In, exactly as it did before this fix -- only the
    // "carries its own formula" case should have gained Step In support.
    [Fact]
    public void GetSummary_DoesNotAttachNestedSummaryForCrossSheetValueReference()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(5));
        var address = new CellAddress(sheet1.Id, 1, 2);
        sheet1.SetCell(address, new Cell
        {
            FormulaText = "Sheet2!A1+1",
            Value = new NumberValue(6)
        });

        var summary = FormulaEvaluationSummaryService.GetSummary(workbook, address)!;
        var session = FormulaEvaluationSession.Start(summary);

        summary.Steps[0].NestedSummary.Should().BeNull();
        session.CanStepIn.Should().BeFalse();
        session.StepIn().Should().BeFalse();
    }
}
