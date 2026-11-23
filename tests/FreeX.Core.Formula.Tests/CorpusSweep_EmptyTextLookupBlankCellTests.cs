using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Whole-corpus recalc-parity sweep finding: an exact-match lookup for the TEXT value "" matched truly
/// blank cells in FreeX (VLOOKUP returned the blank cell's 0, MATCH returned a position) where real Excel
/// returns #N/A (XLOOKUP: the if_not_found value). A cell that actually holds "" text still matches.
/// Verified against Excel via COM: XMATCH/MATCH/HLOOKUP "" over blanks = #N/A, XLOOKUP = if_not_found,
/// MATCH/VLOOKUP/XMATCH "" over a ="" cell = found.
/// </summary>
public sealed class CorpusSweep_EmptyTextLookupBlankCellTests
{
    private readonly FormulaEvaluator _eval = new();

    // A1:A3 = blank, blank, 5 ; B1:B3 = 1,2,3 ; D5 = "" (text), D6 = 7 ; E5/E6 = 10/11.
    private static (Workbook workbook, Sheet sheet) Make()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(5));
        for (uint r = 1; r <= 3; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(r));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 4), new TextValue(""));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 4), new NumberValue(7));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 5), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 5), new NumberValue(11));
        return (workbook, sheet);
    }

    [Theory]
    [InlineData("=VLOOKUP(\"\",A1:B3,2,0)")]
    [InlineData("=VLOOKUP(\"\",A:A,1,0)")]
    [InlineData("=MATCH(\"\",A1:A3,0)")]
    [InlineData("=XMATCH(\"\",A1:A3)")]
    [InlineData("=HLOOKUP(\"\",TRANSPOSE(A1:A3),1,0)")]
    public void EmptyTextLookup_AgainstBlankCells_IsNotFound(string formula)
    {
        var (workbook, sheet) = Make();

        var result = _eval.Evaluate(formula, sheet, workbook, new CellAddress(sheet.Id, 20, 8));

        result.Should().BeOfType<ErrorValue>().Which.Code.Should().Be("#N/A");
    }

    [Fact]
    public void EmptyTextLookup_XlookupAgainstBlankCells_UsesIfNotFound()
    {
        var (workbook, sheet) = Make();

        var result = _eval.Evaluate("=XLOOKUP(\"\",A1:A3,B1:B3,\"nf\")", sheet, workbook, new CellAddress(sheet.Id, 20, 8));

        result.Should().Be(new TextValue("nf"));
    }

    [Theory]
    [InlineData("=VLOOKUP(\"\",D5:E6,2,0)", 10)]
    [InlineData("=MATCH(\"\",D5:D6,0)", 1)]
    [InlineData("=XMATCH(\"\",D5:D6)", 1)]
    public void EmptyTextLookup_AgainstCellHoldingEmptyText_StillMatches(string formula, double expected)
    {
        var (workbook, sheet) = Make();

        var result = _eval.Evaluate(formula, sheet, workbook, new CellAddress(sheet.Id, 20, 8));

        result.Should().Be(new NumberValue(expected));
    }

    [Fact]
    public void BlankCellReferenceLookup_Xlookup_StillMatchesBlank()
    {
        var (workbook, sheet) = Make();

        // E1 is a truly empty cell reference: Excel's XLOOKUP matches it to the first blank (-> B1 = 1).
        var result = _eval.Evaluate("=XLOOKUP(E1,A1:A3,B1:B3,\"nf\")", sheet, workbook, new CellAddress(sheet.Id, 20, 8));

        result.Should().Be(new NumberValue(1));
    }
}
