using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R118-formula-arity3plus-cross-broadcast: MapTernaryTextArgs/MapQuaternaryTextArgs/MapScalarArgs
/// (BuiltInFunctions.TextCore.Helpers.cs) -- the shared choke point behind ~30 financial/statistical/
/// text functions (PMT/PV/FV/NPER/RATE/IPMT/PPMT, bonds/depreciation, BASE, CEILING.MATH/FLOOR.MATH,
/// DATEDIF/DAYS360/YEARFRAC, CONVERT, the *.DIST family, etc.) -- previously chose the shape of the
/// FIRST non-1x1 range argument (ChooseBroadcastShape) and then required every OTHER range argument
/// to either match that exact shape or be a 1x1 scalar (CanBroadcastToShape). When two array
/// arguments have perpendicular orientations -- e.g. PMT's row-vector rate crossed with a
/// column-vector nper -- the exact-shape check failed for the second range and the whole call
/// wrongly returned #VALUE!. Real Excel (365 dynamic arrays) instead performs a 2-D "grow" broadcast
/// (bounding max(rows) x max(cols)), spilling a full matrix -- the same rule the codebase already
/// applied to binary math functions (round 62) and to the lookup family's Grow variants (round 98).
/// Fixed by routing MapScalarArgs/MapTernaryTextArgs/MapQuaternaryTextArgs through the same
/// TryGrowBroadcastShape/ValueAtBroadcastCell logic.
/// </summary>
public class R118_ArityThreePlusCrossBroadcastTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    private static void AssertGrid(ScalarValue value, ScalarValue[,] expected)
    {
        var range = value.Should().BeOfType<RangeValue>().Subject;
        range.RowCount.Should().Be(expected.GetLength(0));
        range.ColCount.Should().Be(expected.GetLength(1));
        for (int r = 0; r < expected.GetLength(0); r++)
            for (int c = 0; c < expected.GetLength(1); c++)
                range.At(r + 1, c + 1).Should().Be(expected[r, c], $"cell ({r + 1},{c + 1})");
    }

    [Fact]
    public void Pmt_RowRateCrossedWithColumnNper_SpillsCrossBroadcastMatrix()
    {
        // A1:C1 = 0.01,0.02,0.03 (1x3 ROW vector of monthly rates).
        // D1:D3 = 12,24,36      (3x1 COLUMN vector of nper).
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0.01)), (1, 2, new NumberValue(0.02)), (1, 3, new NumberValue(0.03)),
            (1, 4, new NumberValue(12)), (2, 4, new NumberValue(24)), (3, 4, new NumberValue(36)));

        // =PMT(A1:C1, D1:D3, -1000) must 2-D cross-broadcast the 1x3 row vector against the 3x1
        // column vector into a 3x3 spilled result (row i = nper i, col j = rate j) -- NOT #VALUE!
        // from the old exact-shape-only rule (which picked rate's 1x3 shape and then rejected
        // nper's 3x1 shape outright).
        var result = _eval.Evaluate("=PMT(A1:C1,D1:D3,-1000)", sheet);

        double[] rates = [0.01, 0.02, 0.03];
        double[] npers = [12, 24, 36];
        var expected = new ScalarValue[3, 3];
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
            {
                var scalar = _eval.Evaluate($"=PMT({rates[c]},{npers[r]},-1000)", sheet);
                expected[r, c] = scalar;
            }

        AssertGrid(result, expected);

        // Sanity: the matrix is not degenerate/uniform -- prove real per-cell computation happened
        // (each row differs, each column differs), not e.g. every cell collapsing to one scalar.
        var range = result.Should().BeOfType<RangeValue>().Subject;
        range.At(1, 1).Should().NotBe(range.At(1, 2));
        range.At(1, 1).Should().NotBe(range.At(2, 1));
    }

    [Fact]
    public void Base_ColumnNumberCrossedWithRowRadix_SpillsCrossBroadcastMatrix()
    {
        // A1:A2 = 8,15 (2x1 COLUMN vector of numbers).
        // B1:C1 = 2,16 (1x2 ROW vector of radices).
        var sheet = MakeSheet(
            (1, 1, new NumberValue(8)), (2, 1, new NumberValue(15)),
            (1, 2, new NumberValue(2)), (1, 3, new NumberValue(16)));

        // =BASE(A1:A2, B1:C1) must 2-D cross-broadcast the 2x1 column vector against the 1x2 row
        // vector into a 2x2 spilled result (row i = number i, col j = radix j).
        var result = _eval.Evaluate("=BASE(A1:A2,B1:C1)", sheet);

        AssertGrid(result, new ScalarValue[,]
        {
            { new TextValue("1000"), new TextValue("8") },   // 8 in base 2, base 16
            { new TextValue("1111"), new TextValue("F") },   // 15 in base 2, base 16
        });
    }

    [Fact]
    public void Pmt_SameShapeRangeArguments_StillMatchExactlyAsBefore()
    {
        // Sibling no-regression: both range arguments sharing the SAME shape (1x2 each) must keep
        // working exactly as before the fix (this was already the accepted "exact shape match"
        // case, not a perpendicular cross-broadcast).
        var sheet = MakeSheet(
            (1, 1, new NumberValue(0.01)), (1, 2, new NumberValue(0.02)),
            (1, 4, new NumberValue(12)), (1, 5, new NumberValue(24)));

        var result = _eval.Evaluate("=PMT(A1:B1,D1:E1,-1000)", sheet);

        var expected1 = _eval.Evaluate("=PMT(0.01,12,-1000)", sheet);
        var expected2 = _eval.Evaluate("=PMT(0.02,24,-1000)", sheet);

        AssertGrid(result, new ScalarValue[,] { { expected1, expected2 } });
    }

    [Fact]
    public void Pmt_TrulyIncompatibleRangeShapes_StillReturnsValueError()
    {
        // Sibling no-regression: two ranges that conflict on the SAME axis (neither equal nor
        // size-1 on that axis) must still be a genuine #VALUE! shape mismatch under the new
        // cross-broadcast rule, just like the R62/R98 guards.
        var sheet = MakeSheet(
            // A1:A3 is a 3x1 column vector of rates...
            (1, 1, new NumberValue(0.01)), (2, 1, new NumberValue(0.02)), (3, 1, new NumberValue(0.03)),
            // ...D1:D2 is a 2x1 column vector of nper: same axis (rows), incompatible extents (3 vs 2).
            (1, 4, new NumberValue(12)), (2, 4, new NumberValue(24)));

        var result = _eval.Evaluate("=PMT(A1:A3,D1:D2,-1000)", sheet);

        result.Should().Be(ErrorValue.Value);
    }
}
