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

    [Fact]
    public void CrossSheet_DependencyPropagates_OnRecalc()
    {
        var wb = new Workbook("Test");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

        var s2a1 = new CellAddress(sheet2.Id, 1, 1);
        var s1b1 = new CellAddress(sheet1.Id, 1, 2);

        sheet2.SetCell(s2a1, new NumberValue(10));
        sheet1.SetFormula(s1b1, "Sheet2!A1");
        var ast = new Parser(new Lexer("=Sheet2!A1").Tokenize()).Parse();
        engine.RegisterFormulaDependencies(s1b1, ast, sheet1.Id, wb);

        engine.Recalculate(wb, [s2a1]);
        sheet1.GetValue(s1b1).Should().Be(new NumberValue(10));

        sheet2.SetCell(s2a1, new NumberValue(99));
        engine.Recalculate(wb, [s2a1]);
        sheet1.GetValue(s1b1).Should().Be(new NumberValue(99));
    }

    [Fact]
    public void VolatileCell_EvaluatesBeforeItsDependents()
    {
        // A1 = =NOW() (volatile)
        // B1 = =A1 (depends on A1)
        // After recalc, B1 should have the same value as A1 (i.e. A1 was evaluated first)
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);

        sheet.SetFormula(a1, "NOW()");
        var lexerA = new Lexer("=NOW()");
        engine.RegisterFormulaDependencies(a1, new Parser(lexerA.Tokenize()).Parse(), sheet.Id);

        sheet.SetFormula(b1, "A1");
        var lexerB = new Lexer("=A1");
        engine.RegisterFormulaDependencies(b1, new Parser(lexerB.Tokenize()).Parse(), sheet.Id);

        engine.Recalculate(wb, []);

        sheet.GetValue(a1).Should().BeOfType<DateTimeValue>();
        sheet.GetValue(b1).Should().BeOfType<DateTimeValue>();
        // B1 should equal A1 (meaning A1 was evaluated before B1 read it)
        sheet.GetValue(b1).Should().Be(sheet.GetValue(a1));
    }
}
