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

    [Fact]
    public void CreateParitySummary_IsTheSharedD6FixtureForBothCaptureHosts()
    {
        var sheetId = SheetId.New();

        var summary = EvaluateFormulaParityFixture.CreateSummary(sheetId);

        summary.SheetId.Should().Be(sheetId);
        summary.SheetName.Should().Be("Sheet1");
        summary.Address.Should().Be(new CellAddress(sheetId, 6, 4));
        summary.FormulaText.Should().Be("=SUM(D2:D5)");
        summary.ValueText.Should().Be("469");
        summary.Steps.Select(step => (step.Expression, step.ValueText))
            .Should().Equal(
                ("SUM(D2:D5)", "469"),
                ("D2:D5", "{120;85;200;64}"),
                ("=SUM(D2:D5)", "469"));

        var session = EvaluateFormulaDialogPlanner.CreateSession(summary);
        session.CurrentStep!.Expression.Should().Be("SUM(D2:D5)");
        session.CurrentHighlight.Highlight.Should().Be("SUM(D2:D5)");
    }

    [Fact]
    public void LayoutConstants_MatchTheWpfEvaluateFormulaDialogContract()
    {
        EvaluateFormulaDialogPlanner.Width.Should().Be(600);
        EvaluateFormulaDialogPlanner.Height.Should().Be(360);
        EvaluateFormulaDialogPlanner.MinWidth.Should().Be(420);
        EvaluateFormulaDialogPlanner.MinHeight.Should().Be(240);
        EvaluateFormulaDialogPlanner.RootMargin.Should().Be(12);
        EvaluateFormulaDialogPlanner.ActionRowTopMargin.Should().Be(10);
        EvaluateFormulaDialogPlanner.ActionSpacing.Should().Be(4);
        EvaluateFormulaDialogPlanner.ButtonHeight.Should().Be(26);
        EvaluateFormulaDialogPlanner.HelpButtonWidth.Should().Be(142);
        EvaluateFormulaDialogPlanner.StepFontSize.Should().Be(16);
        EvaluateFormulaDialogPlanner.ValueFontSize.Should().Be(14);
    }
}
