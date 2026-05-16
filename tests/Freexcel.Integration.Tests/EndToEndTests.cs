using Freexcel.Core.Model;
using Freexcel.Core.Formula;
using Freexcel.Core.Calc;
using FluentAssertions;

namespace Freexcel.Integration.Tests;

public class EndToEndTests
{
    [Fact]
    public void FullWorkflow_EditAndRecalculate()
    {
        var wb = new Workbook("Test");
        var sheet = wb.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var a2 = new CellAddress(sheet.Id, 2, 1);
        var a3 = new CellAddress(sheet.Id, 3, 1);

        sheet.SetCell(a1, new NumberValue(10));
        sheet.SetCell(a2, new NumberValue(20));

        sheet.SetFormula(a3, "SUM(A1:A2)");
        var lexer = new Lexer("=SUM(A1:A2)");
        var tokens = lexer.Tokenize();
        var parser = new Parser(tokens);
        var ast = parser.Parse();
        engine.RegisterFormulaDependencies(a3, ast, sheet.Id, wb);

        var report = engine.Recalculate(wb, [a1, a2]);

        sheet.GetValue(a3).Should().Be(new NumberValue(30));
    }

    [Fact]
    public void CrossSheet_FormulaReference_RecalculatesWhenSourceChanges()
    {
        var wb     = new Workbook("T");
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");
        var graph  = new DependencyGraph();
        var engine = new RecalcEngine(graph, new FormulaEvaluator());

        var src  = new CellAddress(sheet1.Id, 1, 1);
        var dest = new CellAddress(sheet2.Id, 1, 1);

        sheet1.SetCell(src, new NumberValue(42));
        sheet2.SetFormula(dest, "Sheet1!A1*2");

        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [src]);

        sheet2.GetValue(dest).Should().Be(new NumberValue(84));

        sheet1.SetCell(src, new NumberValue(10));
        engine.Recalculate(wb, [src]);
        sheet2.GetValue(dest).Should().Be(new NumberValue(20));
    }

    [Fact]
    public void IF_ShortCircuit_DoesNotEvaluateUntakenBranch()
    {
        var wb    = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var a1    = new CellAddress(sheet.Id, 1, 1);
        var eval  = new FormulaEvaluator();

        // =IF(TRUE,"safe",1/0) — false branch must NOT be evaluated
        eval.Evaluate("=IF(TRUE,\"safe\",1/0)", sheet, wb)
            .Should().Be(new TextValue("safe"),
                "the false branch containing a division-by-zero must not be evaluated when condition is TRUE");

        // =IF(FALSE,1/0,"safe") — true branch must NOT be evaluated
        eval.Evaluate("=IF(FALSE,1/0,\"safe\")", sheet, wb)
            .Should().Be(new TextValue("safe"),
                "the true branch containing a division-by-zero must not be evaluated when condition is FALSE");

        // =IFERROR(1/0, "caught") — error must be caught
        eval.Evaluate("=IFERROR(1/0,\"caught\")", sheet, wb)
            .Should().Be(new TextValue("caught"),
                "IFERROR must catch division-by-zero and return the fallback value");

        // Normal IF: picks correct branch based on cell value
        sheet.SetCell(a1, new NumberValue(10));
        eval.Evaluate("=IF(A1>5,\"big\",\"small\")", sheet, wb)
            .Should().Be(new TextValue("big"));
        sheet.SetCell(a1, new NumberValue(3));
        eval.Evaluate("=IF(A1>5,\"big\",\"small\")", sheet, wb)
            .Should().Be(new TextValue("small"));
    }
}
