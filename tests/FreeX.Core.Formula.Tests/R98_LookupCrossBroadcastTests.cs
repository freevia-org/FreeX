using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R98-formula-lookup-cross-broadcast: MATCH/VLOOKUP/HLOOKUP/INDEX/XLOOKUP/XMATCH build their
/// array-broadcast result via MapScalarArgs/MapTernaryTextArgs (BuiltInFunctions.TextCore.Helpers.cs),
/// which previously chose the shape of the first non-1x1 range argument and then required every
/// OTHER range argument to either match that exact shape or be a 1x1 scalar (CanBroadcastToShape).
/// When two array arguments have perpendicular orientations -- e.g. a column-vector lookup_value
/// crossed with a row-vector match_type -- the exact-shape check failed for the second range and
/// the whole call wrongly returned #VALUE!. Real Excel (365 dynamic arrays) instead performs a 2-D
/// "grow" broadcast (bounding max(rows) x max(cols)), spilling a full matrix -- the same rule the
/// codebase already applied to binary math/bit/engineering functions in round 62
/// (MapBinaryMathArgs / TryGrowBroadcastShape). Fixed by routing MapScalarArgs/MapTernaryTextArgs/
/// MapQuaternaryTextArgs through TryGrowBroadcastShape, the same choke point MapBinaryMathArgs uses.
/// </summary>
public class R98_LookupCrossBroadcastTests
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

    // F1:F5 = 10,20,30,40,50 (ascending, sorted for approximate match).
    // A1:A2 = 20,25 (a 2x1 COLUMN vector of lookup values).
    // D1:E1 = 0,1  (a 1x2 ROW vector of match_type: 0=exact, 1=ascending-approximate).
    private static Sheet MakeMatchCrossBroadcastSheet() => MakeSheet(
        (1, 6, new NumberValue(10)), (2, 6, new NumberValue(20)), (3, 6, new NumberValue(30)),
        (4, 6, new NumberValue(40)), (5, 6, new NumberValue(50)),
        (1, 1, new NumberValue(20)), (2, 1, new NumberValue(25)),
        (1, 4, new NumberValue(0)), (1, 5, new NumberValue(1)));

    [Fact]
    public void Match_ColumnLookupValueCrossedWithRowMatchType_SpillsCrossBroadcastMatrix()
    {
        var sheet = MakeMatchCrossBroadcastSheet();

        // =MATCH(A1:A2, F1:F5, D1:E1) must 2-D cross-broadcast a 2x1 column vector against a 1x2
        // row vector into a 2x2 spilled result (row i = lookup value i, col j = match_type j),
        // matching Excel dynamic arrays -- NOT #VALUE! from the old exact-shape-only rule.
        var result = _eval.Evaluate("=MATCH(A1:A2,F1:F5,D1:E1)", sheet);

        AssertGrid(result, new ScalarValue[,]
        {
            // match_type=0 (exact)      match_type=1 (<=  approx)
            { new NumberValue(2),        new NumberValue(2) },  // lookup 20: exact hit row2; approx largest<=20 is row2
            { ErrorValue.NA,             new NumberValue(2) },  // lookup 25: no exact hit;  approx largest<=25 is row2 (20)
        });
    }

    [Fact]
    public void Vlookup_ColumnLookupValueCrossedWithRowColumnIndex_SpillsCrossBroadcastMatrix()
    {
        // Table B1:D3: col1 keys 10,20,30 (ascending); col2 = 100,200,300; col3 = 1000,2000,3000.
        var sheet = MakeSheet(
            (1, 2, new NumberValue(10)), (1, 3, new NumberValue(100)), (1, 4, new NumberValue(1000)),
            (2, 2, new NumberValue(20)), (2, 3, new NumberValue(200)), (2, 4, new NumberValue(2000)),
            (3, 2, new NumberValue(30)), (3, 3, new NumberValue(300)), (3, 4, new NumberValue(3000)),
            // A1:A2 = 20,30 (2x1 column vector of lookup values).
            (1, 1, new NumberValue(20)), (2, 1, new NumberValue(30)),
            // F1:G1 = 2,3 (1x2 row vector of col_index_num).
            (1, 6, new NumberValue(2)), (1, 7, new NumberValue(3)));

        var result = _eval.Evaluate("=VLOOKUP(A1:A2,B1:D3,F1:G1,FALSE)", sheet);

        AssertGrid(result, new ScalarValue[,]
        {
            { new NumberValue(200),  new NumberValue(2000) },  // lookup 20: col2=200, col3=2000
            { new NumberValue(300),  new NumberValue(3000) },  // lookup 30: col2=300, col3=3000
        });
    }

    [Fact]
    public void Match_SameShapeRangeArguments_StillMatchExactlyAsBefore()
    {
        // Sibling no-regression: both range arguments sharing the SAME shape (2x1 each) must
        // keep working exactly as before the fix (this was already the accepted "exact shape
        // match" case, not a perpendicular cross-broadcast).
        var sheet = MakeSheet(
            (1, 6, new NumberValue(10)), (2, 6, new NumberValue(20)), (3, 6, new NumberValue(30)),
            (1, 1, new NumberValue(20)), (2, 1, new NumberValue(30)),
            (1, 4, new NumberValue(0)), (2, 4, new NumberValue(0)));

        var result = _eval.Evaluate("=MATCH(A1:A2,F1:F3,D1:D2)", sheet);

        AssertGrid(result, new ScalarValue[,]
        {
            { new NumberValue(2) },
            { new NumberValue(3) },
        });
    }

    [Fact]
    public void Match_TrulyIncompatibleRangeShapes_StillReturnsValueError()
    {
        // Sibling no-regression: two ranges that conflict on the SAME axis (neither equal nor
        // size-1 on that axis) must still be a genuine #VALUE! shape mismatch under the new
        // cross-broadcast rule, just like the R62 math-function guard.
        var sheet = MakeSheet(
            (1, 6, new NumberValue(10)), (2, 6, new NumberValue(20)), (3, 6, new NumberValue(30)),
            // A1:A3 is a 3x1 column vector...
            (1, 1, new NumberValue(20)), (2, 1, new NumberValue(30)), (3, 1, new NumberValue(10)),
            // ...D1:D2 is a 2x1 column vector: same axis (rows), incompatible extents (3 vs 2).
            (1, 4, new NumberValue(0)), (2, 4, new NumberValue(0)));

        var result = _eval.Evaluate("=MATCH(A1:A3,F1:F3,D1:D2)", sheet);

        result.Should().Be(ErrorValue.Value);
    }
}
