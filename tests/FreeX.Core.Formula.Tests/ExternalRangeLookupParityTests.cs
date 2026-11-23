using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Parity with real Excel (verified over COM with a small generated source/dependent workbook pair,
/// source closed) for full-column / large-range references into an external workbook whose link cache
/// is sparse (Excel omits blank cells from <c>externalBook/sheetDataSet</c>) and holds error cells, and
/// for the exact-match lookup rule these workbooks exposed: error cells in the lookup vector are
/// SKIPPED by MATCH/VLOOKUP/HLOOKUP/XMATCH/XLOOKUP exact modes, they never abort the scan.
///
/// Source sheet (the cached data): A1 "h", A2 #N/A, A3 0, A4 5, A5 "x", A7 0, A8 7 (A6 blank);
/// B1:B8 = 10..80; C1 "c", C20 99.
/// </summary>
public sealed class ExternalRangeLookupParityTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook Workbook, Sheet Sheet) Build()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Local");
        var link = new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "S.xlsx",
            TargetMode = "External",
        };
        link.SheetNames.Add("Sheet1");
        var cache = new ExternalCachedSheetModel { SheetId = 0 };
        cache.Values[(1, 1)] = new TextValue("h");
        cache.Values[(2, 1)] = new ErrorValue("#N/A");
        cache.Values[(3, 1)] = new NumberValue(0);
        cache.Values[(4, 1)] = new NumberValue(5);
        cache.Values[(5, 1)] = new TextValue("x");
        cache.Values[(7, 1)] = new NumberValue(0);
        cache.Values[(8, 1)] = new NumberValue(7);
        for (uint r = 1; r <= 8; r++)
            cache.Values[(r, 2)] = new NumberValue(r * 10);
        cache.Values[(1, 3)] = new TextValue("c");
        cache.Values[(20, 3)] = new NumberValue(99);
        link.CachedSheetData.Add(cache);
        workbook.ExternalLinks.Add(link);
        return (workbook, sheet);
    }

    private ScalarValue Eval(string formula)
    {
        var (workbook, sheet) = Build();
        return _eval.Evaluate(formula, sheet, workbook, new CellAddress(sheet.Id, 1, 1));
    }

    [Theory]
    [InlineData("=COUNT([1]Sheet1!$A:$A)", 4)]
    [InlineData("=COUNTA([1]Sheet1!$A:$A)", 7)]
    [InlineData("=COUNTBLANK([1]Sheet1!$A$1:$A$12)", 5)]
    [InlineData("=COUNT([1]Sheet1!$B$1:$B$100)", 8)]
    [InlineData("=COUNT([1]Sheet1!$C:$C)", 1)]
    [InlineData("=SUM([1]Sheet1!$B:$B)", 360)]
    [InlineData("=MAX([1]Sheet1!$B:$B)", 80)]
    [InlineData("=SUMPRODUCT([1]Sheet1!$B$1:$B$8)", 360)]
    [InlineData("=MATCH(0,[1]Sheet1!$A:$A,0)", 3)]
    [InlineData("=MATCH(0,[1]Sheet1!$A$1:$A$13000,0)", 3)]
    [InlineData("=MATCH(5,[1]Sheet1!$A:$A,0)", 4)]
    [InlineData("=MATCH(\"x\",[1]Sheet1!$A:$A,0)", 5)]
    [InlineData("=MATCH(0,[1]Sheet1!$A$3:$A$8,0)", 1)]
    [InlineData("=VLOOKUP(0,[1]Sheet1!$A:$B,2,0)", 30)]
    [InlineData("=VLOOKUP(5,[1]Sheet1!$A:$B,2,0)", 40)]
    [InlineData("=VLOOKUP(7,[1]Sheet1!$A:$B,2,0)", 80)]
    [InlineData("=INDEX([1]Sheet1!$B:$B,3)", 30)]
    [InlineData("=INDEX([1]Sheet1!$A:$A,6)", 0)]
    [InlineData("=INDEX([1]Sheet1!$A:$A,100)", 0)]
    [InlineData("=ROWS([1]Sheet1!$A:$A)", 1048576)]
    [InlineData("=SUMIF([1]Sheet1!$A:$A,0,[1]Sheet1!$B:$B)", 100)]
    [InlineData("=COUNTIF([1]Sheet1!$A:$A,0)", 2)]
    public void ExternalFullColumnAndLargeRanges_MatchExcel(string formula, double expected)
    {
        Eval(formula).Should().Be(new NumberValue(expected), formula);
    }

    [Theory]
    [InlineData("=MATCH(1,[1]Sheet1!$C:$C,0)")]
    [InlineData("=SUM([1]Sheet1!$A:$A)")]
    public void ExternalRanges_StillSurfaceGenuineErrors(string formula)
    {
        Eval(formula).Should().Be(ErrorValue.NA, formula);
    }

    [Fact]
    public void ExternalRange_InColumnTheCacheNeverCovered_StillPreservesLoadedValue()
    {
        // Column D was never referenced by Excel, so a miss there is "never refreshed", not blank.
        // The evaluator reports the cache-miss FormulaParseException as #VALUE!; RecalcEngine turns it into
        // "keep the loaded value".
        Eval("=COUNT([1]Sheet1!$D$1:$D$5)").Should().Be(ErrorValue.Value);
    }

    private static void Put(Sheet sheet, uint row, uint col, ScalarValue value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), value);

    private static Sheet LocalLookupSheet()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("S");
        ScalarValue[] keys = [new TextValue("h"), ErrorValue.NA, new NumberValue(0), new NumberValue(5), new TextValue("x"), ErrorValue.DivByZero];
        for (uint r = 1; r <= 6; r++)
        {
            Put(sheet, r, 1, keys[r - 1]);
            Put(sheet, r, 2, new NumberValue(r * 10));
            // transposed copy for HLOOKUP in D1:I2
            Put(sheet, 1, 3 + r, keys[r - 1]);
            Put(sheet, 2, 3 + r, new NumberValue(r * 10));
        }

        return sheet;
    }

    [Theory]
    [InlineData("=MATCH(0,A1:A6,0)", 3)]
    [InlineData("=MATCH(5,A1:A6,0)", 4)]
    [InlineData("=MATCH(\"x\",A1:A6,0)", 5)]
    [InlineData("=VLOOKUP(5,A1:B6,2,0)", 40)]
    [InlineData("=VLOOKUP(\"x\",A1:B6,2,FALSE)", 50)]
    [InlineData("=HLOOKUP(5,D1:I2,2,0)", 40)]
    [InlineData("=XMATCH(5,A1:A6,0,1)", 4)]
    [InlineData("=XMATCH(5,A1:A6,0,-1)", 4)]
    [InlineData("=XMATCH(\"x*\",A1:A6,2)", 5)]
    [InlineData("=XLOOKUP(5,A1:A6,B1:B6)", 40)]
    [InlineData("=XLOOKUP(\"x*\",A1:A6,B1:B6,\"nf\",2)", 50)]
    public void ExactLookups_SkipErrorCellsInTheLookupVector(string formula, double expected)
    {
        var sheet = LocalLookupSheet();
        _eval.Evaluate(formula, sheet, null, new CellAddress(sheet.Id, 10, 1)).Should().Be(new NumberValue(expected), formula);
    }

    [Fact]
    public void ExactMatch_ErrorLookupValue_StillPropagates()
    {
        var sheet = LocalLookupSheet();
        _eval.Evaluate("=MATCH(#N/A,A1:A6,0)", sheet, null, new CellAddress(sheet.Id, 10, 1)).Should().Be(ErrorValue.NA);
    }
}
