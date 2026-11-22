using FluentAssertions;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Floating-point residue behaviour verified against real Excel (COM): an unparenthesized outermost +/- snaps a
/// tiny residue to 0, a parenthesized whole formula or a +/- nested in another operator keeps it, and SUM returns
/// the raw double accumulation (no 15-digit rounding) so residues such as F38-SUM(...)+F31 match Excel's cache.
/// </summary>
public sealed class ExcelFloatingPointResidueTests
{
    private readonly FormulaEvaluator _eval = new();

    private (Workbook wb, Sheet sheet) Make(params double[] column)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        for (var i = 0; i < column.Length; i++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)(i + 1), 1), new NumberValue(column[i]));
        return (wb, sheet);
    }

    [Theory]
    [InlineData("=A1+A2-A3", 0d)]
    [InlineData("=(A1+A2)-A3", 0d)]
    [InlineData("=A1+(A2-A3)", 0d)]
    [InlineData("=(A1+A2-A3)", 5.551115123125783E-17)]
    [InlineData("=((A1+A2)-A3)", 5.551115123125783E-17)]
    [InlineData("=+(A1+A2-A3)", 5.551115123125783E-17)]
    [InlineData("=(A1+A2-A3)+0", 5.551115123125783E-17)]
    [InlineData("=0+(A1+A2-A3)", 5.551115123125783E-17)]
    [InlineData("=A1+A2-A3+0", 5.551115123125783E-17)]
    public void RootSnap_MatchesExcelParenthesisBehaviour(string formula, double expected)
    {
        var (wb, sheet) = Make(0.1, 0.2, 0.3);
        var result = _eval.Evaluate(formula, sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(expected, 1e-30);
    }

    [Fact]
    public void SubtractingRawSum_KeepsOneUlpResidueLikeExcel()
    {
        // F38-SUM(F30:F30,F32:F32,F34:F37)+F31 from a real workbook: Excel cached 4.656612873077393E-10
        // (one ulp of 3.66e6); a 15-digit-rounded SUM total produced 1.86e-9.
        var (wb, sheet) = Make(80298.03000000001, 0, 3224408.0900000012, 0, 5000, 11193.35, 314697.45, 25015.88, 3660612.8000000017);
        var result = _eval.Evaluate("=A9-SUM(A1:A1,A3:A3,A5:A8)+A2", sheet, wb);
        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(4.656612873077393E-10, 1e-20);
    }
}
