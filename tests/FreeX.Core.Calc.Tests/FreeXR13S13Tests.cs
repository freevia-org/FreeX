using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Round-13 bucket S13 regression test.
///
/// R13-external-links-3: IsLikelyExternalWorkbookReferenceFormula was a bare
/// `formulaText.Contains('[')`, which misclassifies ANY bracket-containing parse failure — not
/// just a genuine `[Book]Sheet!Ref` external-workbook reference — as a preservable external link,
/// so the catch(FormulaParseException) block in RecalcEngine `continue`s and leaves the cell's
/// prior/stale value in place instead of reporting #VALUE!. Excel rejects such malformed input;
/// FreeX must too.
/// </summary>
public sealed class FreeXR13S13Tests
{
    private static RecalcEngine Engine() =>
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

    [Fact]
    public void RecalculateAllFormulas_MalformedBracketFormula_ReportsValueError_NotPreservedAsExternalLink()
    {
        // A1 currently shows 5. The user edits it to "=SUM([" — a malformed formula that merely
        // happens to contain a '[' (not a genuine external-workbook reference). Before the fix,
        // the bare Contains('[') heuristic swallowed the FormulaParseException and left A1 at its
        // stale value of 5 with no error reported.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new Cell { FormulaText = "SUM([", Value = new NumberValue(5) });

        var report = Engine().RecalculateAllFormulas(workbook);

        sheet.GetValue(1, 1).Should().Be(ErrorValue.Value,
            "a malformed formula that merely contains a bracket is not a genuine external workbook " +
            "reference and must resolve to #VALUE!, matching Excel, instead of keeping its stale prior value");
        report.Errors.Should().Contain(e => e.Cell == addr && e.Error == "#VALUE!");
    }

    [Fact]
    public void RecalculateAllFormulas_StillPreservesCachedValue_ForGenuineExternalWorkbookReference()
    {
        // Guard against over-narrowing the fix: a genuine external-workbook reference — bracketed
        // workbook token immediately followed by a sheet-qualified ref — must still preserve its
        // last-known cached value exactly like before.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new Cell { FormulaText = "[Book1.xlsx]Sheet1!A1", Value = new NumberValue(42) });

        var report = Engine().RecalculateAllFormulas(workbook);

        sheet.GetValue(1, 1).Should().Be(new NumberValue(42),
            "a genuine external-workbook reference must still preserve its cached value");
        report.Errors.Should().NotContain(e => e.Cell == addr);
    }
}
