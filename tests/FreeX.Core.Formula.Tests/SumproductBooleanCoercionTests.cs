using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression tests for R41-formula-array-constants-3-1.
///
/// Root-cause: SUMPRODUCT coerced each term via TryCellNumber, which returns
/// false (and thus 0) for a BoolValue. Real Excel multiplies TRUE/FALSE as
/// 1/0 inside SUMPRODUCT's array multiplication (the ubiquitous
/// SUMPRODUCT((range=criteria)) counting/summing idiom relies on this), so
/// =SUMPRODUCT({1,2,3},{TRUE,FALSE,TRUE}) must be 4, not 0.
///
/// This is deliberately local to SUMPRODUCT: SUM and friends intentionally
/// IGNORE booleans encountered inside a range/array argument, and that
/// behavior must remain unchanged (see TryCellNumber and its other callers).
/// </summary>
public class SumproductBooleanCoercionTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    // (1) The exact failure scenario from the finding: a boolean array term
    // must multiply as 1/0, not 0.
    [Fact]
    public void Sumproduct_BooleanArrayTerm_MultipliesAsOneOrZero()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new BoolValue(true)),
            (2, 1, new NumberValue(2)), (2, 2, new BoolValue(false)),
            (3, 1, new NumberValue(3)), (3, 2, new BoolValue(true)));

        // 1*TRUE + 2*FALSE + 3*TRUE = 1 + 0 + 3 = 4
        _eval.Evaluate("=SUMPRODUCT(A1:A3,B1:B3)", sheet).Should().Be(new NumberValue(4));
    }

    // (2) A lone boolean array (the SUMPRODUCT((range=criteria)) counting idiom)
    // must sum TRUE as 1.
    [Fact]
    public void Sumproduct_SingleBooleanArray_SumsAsCount()
    {
        var sheet = MakeSheet(
            (1, 1, new BoolValue(true)),
            (2, 1, new BoolValue(false)),
            (3, 1, new BoolValue(true)));

        _eval.Evaluate("=SUMPRODUCT(A1:A3)", sheet).Should().Be(new NumberValue(2));
    }

    // (3) The classic COUNTIF-via-SUMPRODUCT idiom: (A1:A3=1)*1 must still count
    // correctly (comparison array is multiplied by literal 1, not booleans
    // directly — this is the sibling no-regression case that already worked).
    [Fact]
    public void Sumproduct_ComparisonTimesOneIdiom_StillCounts()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)),
            (2, 1, new NumberValue(2)),
            (3, 1, new NumberValue(1)));

        _eval.Evaluate("=SUMPRODUCT((A1:A3=1)*1)", sheet).Should().Be(new NumberValue(2));
    }

    // (4) Non-numeric text within an array term still coerces to 0 (Excel's
    // SUMPRODUCT treats text as 0, unlike booleans which become 1/0).
    [Fact]
    public void Sumproduct_TextArrayTerm_CoercesToZero()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (1, 2, new TextValue("x")),
            (2, 1, new NumberValue(2)), (2, 2, new NumberValue(5)),
            (3, 1, new NumberValue(3)), (3, 2, new TextValue("y")));

        // 1*0 + 2*5 + 3*0 = 10
        _eval.Evaluate("=SUMPRODUCT(A1:A3,B1:B3)", sheet).Should().Be(new NumberValue(10));
    }
}
