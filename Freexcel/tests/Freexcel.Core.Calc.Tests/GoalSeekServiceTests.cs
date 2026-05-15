using Freexcel.Core.Model;
using Freexcel.Core.Formula;
using Freexcel.Core.Calc;
using FluentAssertions;

namespace Freexcel.Core.Calc.Tests;

public class GoalSeekServiceTests
{
    private static (RecalcEngine engine, Workbook wb) MakeEngine()
    {
        var graph     = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine    = new RecalcEngine(graph, evaluator);
        var wb        = new Workbook();
        wb.AddSheet("Sheet1");
        return (engine, wb);
    }

    [Fact]
    public void GoalSeek_LinearFormula_FindsExactSolution()
    {
        // A1=1 (changing), B1=A1*3 (formula), target=12 → A1 should become 4
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "A1*3");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1]);

        var result = GoalSeekService.Seek(wb, engine, b1, 12.0, a1);

        result.Converged.Should().BeTrue();
        result.FoundValue.Should().BeApproximately(4.0, 1e-4);
        result.ActualResult.Should().BeApproximately(12.0, 1e-4);
    }

    [Fact]
    public void GoalSeek_QuadraticFormula_Converges()
    {
        // A1=1 (changing), B1=A1*A1 (formula), target=25 → A1 should become 5
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "A1*A1");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1]);

        var result = GoalSeekService.Seek(wb, engine, b1, 25.0, a1);

        result.Converged.Should().BeTrue();
        result.FoundValue.Should().BeApproximately(5.0, 1e-4);
        result.ActualResult.Should().BeApproximately(25.0, 1e-4);
    }

    [Fact]
    public void GoalSeek_AlreadyAtTarget_ConvergesImmediately()
    {
        // A1=5, B1=A1*2, target=10 → already at solution
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetFormula(b1, "A1*2");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1]);

        var result = GoalSeekService.Seek(wb, engine, b1, 10.0, a1);

        result.Converged.Should().BeTrue();
        result.FoundValue.Should().BeApproximately(5.0, 1e-4);
    }

    [Fact]
    public void GoalSeek_RestoresOriginalValueAfterSeek()
    {
        // Verify changingCell is restored to its original value after GoalSeekService.Seek returns
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetCell(a1, new NumberValue(3.0));
        sheet.SetFormula(b1, "A1*4");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1]);

        _ = GoalSeekService.Seek(wb, engine, b1, 100.0, a1);

        // A1 must be back to 3.0 regardless of seek outcome
        var restoredValue = sheet.GetValue(a1);
        restoredValue.Should().Be(new NumberValue(3.0));
    }

    [Fact]
    public void GoalSeek_FlatFunction_ReturnsNotConverged()
    {
        // A1=1, B1=5 (constant literal, not dependent on A1), target=10 → can't converge
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetCell(a1, new NumberValue(1));
        // B1 is a constant — changing A1 has no effect on it
        sheet.SetCell(b1, new NumberValue(5));
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1]);

        var result = GoalSeekService.Seek(wb, engine, b1, 10.0, a1);

        result.Converged.Should().BeFalse();
    }
}
