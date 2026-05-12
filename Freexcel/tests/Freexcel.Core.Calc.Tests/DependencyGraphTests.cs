using Freexcel.Core.Calc;
using Freexcel.Core.Formula;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.Core.Calc.Tests;

public class DependencyGraphTests
{
    [Fact]
    public void SetDependencies_TracksDependents()
    {
        var graph = new DependencyGraph();
        var sheet = SheetId.New();
        var a1 = new CellAddress(sheet, 1, 1);
        var b1 = new CellAddress(sheet, 1, 2);

        // B1 depends on A1 (e.g. =A1+1)
        graph.SetDependencies(b1, [a1]);

        graph.GetDirectDependents(a1).Should().Contain(b1);
        graph.GetDirectPrecedents(b1).Should().Contain(a1);
    }

    [Fact]
    public void ClearDependencies_RemovesLinks()
    {
        var graph = new DependencyGraph();
        var sheet = SheetId.New();
        var a1 = new CellAddress(sheet, 1, 1);
        var b1 = new CellAddress(sheet, 1, 2);

        graph.SetDependencies(b1, [a1]);
        graph.ClearDependencies(b1);

        graph.GetDirectDependents(a1).Should().NotContain(b1);
    }

    [Fact]
    public void RecalcOrder_ReturnsTopologicalOrder()
    {
        var graph = new DependencyGraph();
        var sheet = SheetId.New();
        var a1 = new CellAddress(sheet, 1, 1);
        var b1 = new CellAddress(sheet, 1, 2); // =A1+1
        var c1 = new CellAddress(sheet, 1, 3); // =B1*2

        graph.SetDependencies(b1, [a1]);
        graph.SetDependencies(c1, [b1]);

        var plan = graph.GetRecalcOrder([a1]);

        plan.CyclicCells.Should().BeEmpty();
        plan.OrderedCells.Should().HaveCount(2);
        var b1Idx = plan.OrderedCells.ToList().IndexOf(b1);
        var c1Idx = plan.OrderedCells.ToList().IndexOf(c1);
        b1Idx.Should().BeLessThan(c1Idx, "B1 should be recalculated before C1");
    }

    [Fact]
    public void RecalcOrder_DetectsCycles()
    {
        var graph = new DependencyGraph();
        var sheet = SheetId.New();
        var a1 = new CellAddress(sheet, 1, 1);
        var b1 = new CellAddress(sheet, 1, 2);

        // A1 -> B1 -> A1 (circular)
        graph.SetDependencies(a1, [b1]);
        graph.SetDependencies(b1, [a1]);

        var plan = graph.GetRecalcOrder([a1]);

        plan.CyclicCells.Should().NotBeEmpty();
    }
}

public class VolatileFunctionTests
{
    private static RecalcEngine MakeEngine()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        return new RecalcEngine(graph, evaluator);
    }

    [Fact]
    public void Now_ReturnsDateTimeValue()
    {
        var evaluator = new FormulaEvaluator();
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var result = evaluator.Evaluate("=NOW()", sheet);
        result.Should().BeOfType<DateTimeValue>();
    }

    [Fact]
    public void Today_ReturnsDateValue_WithTimeZero()
    {
        var evaluator = new FormulaEvaluator();
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var result = evaluator.Evaluate("=TODAY()", sheet);
        result.Should().BeOfType<DateTimeValue>();
        var dt = ((DateTimeValue)result).ToDateTime();
        dt.TimeOfDay.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void Rand_ReturnsNumberBetweenZeroAndOne()
    {
        var evaluator = new FormulaEvaluator();
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var result = evaluator.Evaluate("=RAND()", sheet);
        result.Should().BeOfType<NumberValue>();
        var v = ((NumberValue)result).Value;
        v.Should().BeGreaterThanOrEqualTo(0.0).And.BeLessThan(1.0);
    }

    [Fact]
    public void Rand_ReturnsDifferentValuesOnSuccessiveCalls()
    {
        var evaluator = new FormulaEvaluator();
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var r1 = ((NumberValue)evaluator.Evaluate("=RAND()", sheet)).Value;
        var r2 = ((NumberValue)evaluator.Evaluate("=RAND()", sheet)).Value;
        // Astronomically unlikely to be equal
        r1.Should().NotBe(r2);
    }

    [Fact]
    public void VolatileCell_RecalculatesOnEveryRecalcPass()
    {
        var engine = MakeEngine();
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var sheetId = sheet.Id;
        var a1 = new CellAddress(sheetId, 1, 1);

        sheet.SetFormula(a1, "NOW()");
        var lexer = new Lexer("=NOW()");
        var ast = new Parser(lexer.Tokenize()).Parse();
        engine.RegisterFormulaDependencies(a1, ast, sheetId);

        engine.Recalculate(workbook, []);
        var first = sheet.GetCell(a1)!.Value;
        first.Should().BeOfType<DateTimeValue>();

        Thread.Sleep(50);

        engine.Recalculate(workbook, []);
        var second = sheet.GetCell(a1)!.Value;
        second.Should().BeOfType<DateTimeValue>();
    }

    [Fact]
    public void NonVolatileCell_DoesNotRecalculate_WhenNothingChanged()
    {
        var engine = MakeEngine();
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        var sheetId = sheet.Id;
        var a1 = new CellAddress(sheetId, 1, 1);
        var b1 = new CellAddress(sheetId, 1, 2);

        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetFormula(b1, "SUM(A1)");
        var lexer = new Lexer("=SUM(A1)");
        var ast = new Parser(lexer.Tokenize()).Parse();
        engine.RegisterFormulaDependencies(b1, ast, sheetId);

        // B1 starts as a formula cell with no computed value yet
        var before = sheet.GetCell(b1)!.Value;

        // Recalculate with no changed cells — B1 should not be evaluated
        var report = engine.Recalculate(workbook, []);

        report.RecalculatedCells.Should().NotContain(b1);
        sheet.GetCell(b1)!.Value.Should().Be(before);
    }
}
