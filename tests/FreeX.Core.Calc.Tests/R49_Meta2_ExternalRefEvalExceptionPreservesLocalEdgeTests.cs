using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

// R49-meta-2: the r48 Lexer fix made "=A1+[1]Sheet1!C1" parse successfully, so
// RegisterFormulaDependencies now runs and registers the LOCAL A1 dependency edge before
// evaluation. But evaluating the uncached external "[1]Sheet1!C1" reference throws
// FormulaParseException from SheetEvalContext.GetCellValue (by design — it preserves the cell's
// last-known cached value instead of recomputing to blank; see FormulaEvaluator.Contexts.cs). The
// pre-existing "catch (FormulaParseException)" handler unconditionally cleared CachedAst +
// dependencies BEFORE checking whether this was a genuine parse failure, wiping out the
// just-registered local A1 edge even though the formula parsed fine. The fix only clears the AST
// and dependency edges when cell.CachedAst is still null (a genuine, unparseable formula) —
// otherwise (an eval-time throw against an already-parsed AST) it preserves them.
public class R49_Meta2_ExternalRefEvalExceptionPreservesLocalEdgeTests
{
    private static ExternalLinkModel MakeUncachedExternalLink()
    {
        var link = new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "Data File.xlsx",
            TargetMode = "External",
        };
        link.SheetNames.Add("Sheet1");
        // Deliberately no CachedSheetData entries: any read of a cell on this external sheet
        // throws FormulaParseException from SheetEvalContext.GetCellValue at evaluation time.
        return link;
    }

    [Fact]
    public void MixedLocalAndExternalReference_PreservesLocalDependencyEdge_SoLaterEditDirtiesIt()
    {
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("Sheet1");
        wb.ExternalLinks.Add(MakeUncachedExternalLink());

        var graph = new DependencyGraph();
        var engine = new RecalcEngine(graph, new FormulaEvaluator());

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetFormula(b1, "A1+[1]Sheet1!C1");

        engine.RecalculateAllFormulas(wb);

        // The formula parsed fine (r48's Lexer fix) and RegisterFormulaDependencies ran before the
        // eval-time external-reference exception; the local A1 edge must survive that exception.
        graph.GetDirectPrecedents(b1).Should().Contain(a1,
            "the eval-time external-reference exception must not wipe the local A1 dependency " +
            "edge that was just registered for a formula that parsed successfully");

        // A later edit to A1 must still mark B1 dirty via the surviving dependency edge.
        sheet.SetCell(a1, new NumberValue(99));
        engine.Recalculate(wb, [a1]);

        graph.GetDirectDependents(a1).Should().Contain(b1);
    }

    [Fact]
    public void GenuinelyUnparseableFormula_StillClearsAstAndDependencies_AndReportsValueError()
    {
        // Sibling/no-regression: a formula that never parses at all (a genuine syntax error, not an
        // external-reference eval-time throw) must still hit the original behavior — CachedAst
        // stays null, dependencies are cleared, and the cell gets #VALUE!.
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("Sheet1");

        var graph = new DependencyGraph();
        var engine = new RecalcEngine(graph, new FormulaEvaluator());

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetFormula(b1, "A1+"); // dangling operator: genuinely unparseable syntax

        var report = engine.RecalculateAllFormulas(wb);

        sheet.GetCell(b1)!.CachedAst.Should().BeNull();
        graph.GetDirectPrecedents(b1).Should().BeEmpty();
        sheet.GetValue(b1).Should().Be(ErrorValue.Value);
        report.Errors.Should().Contain(e => e.Cell == b1 && e.Error == "#VALUE!");
    }
}
