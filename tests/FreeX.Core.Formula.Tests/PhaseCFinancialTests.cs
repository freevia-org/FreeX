using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Tests for Phase C financial functions:
/// ISPMT, IPMT, PPMT, CUMIPMT, CUMPRINC, EFFECT, NOMINAL, MIRR, XIRR, XNPV,
/// RRI, PDURATION, FVSCHEDULE, DB, DDB, VDB, SYD, AMORDEGRC, AMORLINC,
/// DOLLARDE, DOLLARFR, DISC, INTRATE, RECEIVED, ACCRINT,
/// TBILLEQ, TBILLPRICE, TBILLYIELD, COUPDAYBS, COUPDAYS, COUPDAYSNC,
/// COUPNCD, COUPNUM, COUPPCD, PRICE, YIELD, PRICEDISC, PRICEMAT,
/// YIELDDISC, YIELDMAT, DURATION, MDURATION.
/// </summary>
public partial class PhaseCFinancialTests
{
    private readonly FormulaEvaluator _eval = new();

    private double Calc(string formula)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        var result = _eval.Evaluate("=" + formula, sheet, wb);
        result.Should().BeOfType<NumberValue>($"formula {formula} should return a number");
        return ((NumberValue)result).Value;
    }

    private string CalcError(string formula)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        var result = _eval.Evaluate("=" + formula, sheet, wb);
        result.Should().BeOfType<ErrorValue>($"formula {formula} should return an error");
        return ((ErrorValue)result).Code;
    }

    private double CalcWithData(string formula, params (int row, int col, double val)[] cells)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), new NumberValue(v));
        var result = _eval.Evaluate("=" + formula, sheet, wb);
        result.Should().BeOfType<NumberValue>($"formula {formula} should return a number");
        return ((NumberValue)result).Value;
    }

    private ScalarValue EvalWithData(string formula, params (int row, int col, double val)[] cells)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), new NumberValue(v));
        return _eval.Evaluate("=" + formula, sheet, wb);
    }

    private static void AssertApproxColumn(ScalarValue value, params double[] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.Length);
        range.ColCount.Should().Be(1);
        for (int row = 0; row < expected.Length; row++)
            ((NumberValue)range.At(row + 1, 1)).Value.Should().BeApproximately(expected[row], 1e-10);
    }

    private static void AssertApproxGrid(ScalarValue value, double[,] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        int rows = expected.GetLength(0);
        int cols = expected.GetLength(1);
        range.RowCount.Should().Be(rows);
        range.ColCount.Should().Be(cols);
        for (int r = 1; r <= rows; r++)
            for (int c = 1; c <= cols; c++)
                ((NumberValue)range.At(r, c)).Value.Should().BeApproximately(expected[r - 1, c - 1], 1e-9);
    }
}
