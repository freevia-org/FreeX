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
}
