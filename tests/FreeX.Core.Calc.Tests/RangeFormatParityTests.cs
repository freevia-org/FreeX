using FreeX.Core.Model;
using FreeX.Core.Formula;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace FreeX.Core.Calc.Tests;

// Extensive A1 + full-row/full-column ("RC") range-format parity check vs Excel.
// Reproduces the reported "G:G gives #REF" issue and audits the whole reference matrix.
public sealed class RangeFormatParityTests
{
    private readonly ITestOutputHelper _out;
    public RangeFormatParityTests(ITestOutputHelper output) => _out = output;

    // Builds a workbook with known data and evaluates `formula` in a result cell well away from
    // every tested range (row 50, col 10 = J50) so the formula never sits inside its own range
    // (which would correctly be #CIRCULAR!). Data lives in rows 1-10, cols A-G.
    private static ScalarValue Eval(string formula, uint resultRow = 50, uint resultCol = 10)
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var wb = new Workbook();
        wb.AddSheet("Sheet1");
        var sheet = wb.Sheets.First();

        // Column G: G1..G3 = 10,20,30  (sum 60, count 3)
        sheet.SetCell(new CellAddress(sheet.Id, 1, 7), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 7), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 7), new NumberValue(30));
        // Row 10: A10,B10,C10 = 5,6,7 (sum 18)
        sheet.SetCell(new CellAddress(sheet.Id, 10, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 2), new NumberValue(6));
        sheet.SetCell(new CellAddress(sheet.Id, 10, 3), new NumberValue(7));
        // 2x2 block A1:B2 = 1,2 / 3,4 (sum 10)
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(4));

        var anchor = new CellAddress(sheet.Id, resultRow, resultCol);
        sheet.SetFormula(anchor, formula);
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [anchor]);
        return sheet.GetValue(resultRow, resultCol);
    }

    [Theory]
    // baseline cell/range (must already work)
    [InlineData("SUM(G1:G3)", 60)]
    [InlineData("SUM(A1:B2)", 10)]
    [InlineData("G1+G2", 30)]
    // single full-COLUMN ranges
    [InlineData("SUM(G:G)", 60)]
    [InlineData("SUM($G:$G)", 60)]
    [InlineData("COUNT(G:G)", 3)]
    [InlineData("AVERAGE(G:G)", 20)]
    [InlineData("MIN(G:G)", 10)]
    [InlineData("MAX(G:G)", 30)]
    [InlineData("SUM(Sheet1!G:G)", 60)]
    // MULTI-column full ranges (the reported #REF! bug: F:G, A:C, ...)
    [InlineData("SUM(F:G)", 60)]      // F empty + G(60)
    [InlineData("SUM(A:C)", 28)]      // colA(1+3+5)=9 + colB(2+4+6)=12 + colC(7) = 28
    [InlineData("SUM(A:G)", 88)]      // all data: A:C (28) + G col (60) = 88
    [InlineData("SUM($A:$C)", 28)]
    [InlineData("COUNT(A:C)", 7)]     // 4 block + 3 row10
    [InlineData("MAX(A:G)", 30)]
    [InlineData("SUM(Sheet1!F:G)", 60)]
    // full-ROW ranges (single + multi)
    [InlineData("SUM(10:10)", 18)]
    [InlineData("SUM($10:$10)", 18)]
    [InlineData("COUNT(10:10)", 3)]
    [InlineData("SUM(9:10)", 18)]     // row 9 empty + row 10
    [InlineData("SUM(1:2)", 40)]      // rows 1-2 across all cols: A1,B1,G1,A2,B2,G2 = 1+2+10+3+4+20
    [InlineData("SUM(1:10)", 88)]     // all data
    [InlineData("SUM(Sheet1!10:10)", 18)]
    // ranges that overlap NO populated cells -> 0 (not #REF!)
    [InlineData("SUM(X:Z)", 0)]
    [InlineData("SUM(100:100)", 0)]
    public void Range_Evaluates_ToExpected(string formula, double expected)
    {
        var result = Eval(formula);
        _out.WriteLine($"{formula} => {result}");
        result.Should().Be(new NumberValue(expected), $"'{formula}' should equal {expected}, not error/wrong");
    }

    [Theory]
    // Functions that MATERIALIZE a full-column/full-row range used to hit the 1,000,000-cell cap and
    // return #REF! even for a single column. These are the real-world patterns (COUNTIFS/SUMIFS over
    // '<sheet>'!$L:$L, COLUMN($O:$O), SUMPRODUCT(col)) that the materialized clamp restores.
    [InlineData("COUNTIFS(A:A,\">0\")", 3)]          // colA has 1,3,5 -> all >0
    [InlineData("COUNTIFS(G:G,\">15\")", 2)]         // G has 10,20,30 -> 20,30
    [InlineData("SUMIFS(G:G,A:A,\">0\")", 30)]       // rows where A>0: G1=10,G2=20,G10=blank
    [InlineData("SUMPRODUCT(G:G)", 60)]
    [InlineData("SUMPRODUCT(G1:G3,G1:G3)", 1400)]    // 100+400+900
    [InlineData("COLUMN(C:C)", 3)]
    [InlineData("COLUMN($G:$G)", 7)]
    [InlineData("ROW(10:10)", 10)]
    public void MaterializedFullRange_Functions(string formula, double expected)
    {
        var result = Eval(formula);
        _out.WriteLine($"{formula} => {result}");
        result.Should().Be(new NumberValue(expected), $"'{formula}' should equal {expected}, not #REF!/wrong");
    }

    [Theory]
    // ROWS/COLUMNS read the *reference* dimensions, not the populated extent: a full column is still
    // 1,048,576 rows. Guards that the used-range clamp never leaks into reference-dimension functions.
    [InlineData("ROWS(A:A)", 1048576)]
    [InlineData("COLUMNS(A:C)", 3)]
    [InlineData("ROWS(1:1)", 1)]
    [InlineData("COLUMNS(1:1)", 16384)]
    public void ReferenceDimension_Functions_UseNominalExtent(string formula, double expected)
    {
        var result = Eval(formula);
        _out.WriteLine($"{formula} => {result}");
        result.Should().Be(new NumberValue(expected));
    }
}
