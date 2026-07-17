using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R48b: TRIMMEAN was previously unimplemented (returned #NAME?) despite being a standard
/// Excel statistical function. A prior fixer's implementation had to be reverted because it
/// registered TRIMMEAN without a matching docs/parity/functions.md row, tripping the
/// FormulaParityCatalogTests contract; this round adds both the implementation and the doc
/// row together.
///
/// Pins TRIMMEAN's Excel-matching semantics: trim floor(n * percent / 2) values from each end
/// of the sorted numeric values (ignoring text/logical/blank cells, matching MEDIAN/PERCENTILE
/// range coercion), then average what remains.
/// </summary>
public class R48b_TrimmeanTests
{
    private readonly FormulaEvaluator _eval = new();

    private static (Workbook wb, Sheet sheet) MakeWb(params (int row, int col, ScalarValue val)[] cells)
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return (wb, sheet);
    }

    [Fact]
    public void Trimmean_TenValuesPointTwo_TrimsOneFromEachEnd()
    {
        // {4,5,6,7,2,3,4,5,1,2}: n=10, trim floor(10*0.2/2)=1 from each end.
        // sorted: 1,2,2,3,4,4,5,5,6,7 -> drop 1 and 7 -> remaining 2,2,3,4,4,5,5,6 -> avg 31/8.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(4)), (2, 1, new NumberValue(5)), (3, 1, new NumberValue(6)),
            (4, 1, new NumberValue(7)), (5, 1, new NumberValue(2)), (6, 1, new NumberValue(3)),
            (7, 1, new NumberValue(4)), (8, 1, new NumberValue(5)), (9, 1, new NumberValue(1)),
            (10, 1, new NumberValue(2)));

        var result = _eval.Evaluate("=TRIMMEAN(A1:A10,0.2)", sheet, wb);

        result.Should().BeOfType<NumberValue>();
        ((NumberValue)result).Value.Should().BeApproximately(31.0 / 8.0, 1e-9);
    }

    [Fact]
    public void Trimmean_PercentZero_EqualsPlainAverage()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)),
            (4, 1, new NumberValue(4)), (5, 1, new NumberValue(5)));

        var trimmean = _eval.Evaluate("=TRIMMEAN(A1:A5,0)", sheet, wb);
        var average = _eval.Evaluate("=AVERAGE(A1:A5)", sheet, wb);

        trimmean.Should().Be(average);
    }

    [Fact]
    public void Trimmean_PercentGreaterThanOrEqualToOne_ReturnsNumError()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)));

        _eval.Evaluate("=TRIMMEAN(A1:A3,1)", sheet, wb).Should().Be(ErrorValue.Num);
        _eval.Evaluate("=TRIMMEAN(A1:A3,1.5)", sheet, wb).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Trimmean_PercentNegative_ReturnsNumError()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)));

        _eval.Evaluate("=TRIMMEAN(A1:A3,-0.1)", sheet, wb).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Trimmean_SmallArray_FloorRoundsTrimCountToZero()
    {
        // n=3, percent=0.2: floor(3*0.2/2) = floor(0.3) = 0, so nothing is trimmed even
        // though percent is nonzero -- the average of all 3 values is returned.
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)), (2, 1, new NumberValue(20)), (3, 1, new NumberValue(30)));

        var result = _eval.Evaluate("=TRIMMEAN(A1:A3,0.2)", sheet, wb);

        result.Should().Be(new NumberValue(20));
    }

    [Fact]
    public void Trimmean_NonNumericPercent_ReturnsValueError()
    {
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)));

        _eval.Evaluate("=TRIMMEAN(A1:A3,\"abc\")", sheet, wb).Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Trimmean_EmptyArray_ReturnsNumError()
    {
        // No numeric cells at all in the referenced range.
        var (wb, sheet) = MakeWb();

        _eval.Evaluate("=TRIMMEAN(A1:A3,0)", sheet, wb).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Trimmean_IgnoresTextLogicalAndBlankCells()
    {
        // Matches MEDIAN/PERCENTILE range coercion: text, booleans, and blanks in the range
        // are ignored rather than coerced to 0/1, so only the 3 numeric cells count (n=3,
        // trim floor(3*0/2)=0).
        var (wb, sheet) = MakeWb(
            (1, 1, new NumberValue(10)), (2, 1, new TextValue("ignored")),
            (3, 1, new NumberValue(20)), (4, 1, new BoolValue(true)),
            (5, 1, new NumberValue(30)));

        var result = _eval.Evaluate("=TRIMMEAN(A1:A5,0)", sheet, wb);

        result.Should().Be(new NumberValue(20));
    }
}
