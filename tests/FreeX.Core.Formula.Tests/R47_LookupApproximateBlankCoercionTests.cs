using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R47-formula-vlookup-hlookup-approx-3-1: approximate-match VLOOKUP/HLOOKUP/MATCH/LOOKUP must
/// NOT skip a genuinely blank cell in the lookup column/row/vector -- Excel coerces a blank
/// participating in a numeric approximate-match scan to 0 (matching the coercion CompareScalar
/// already applies), so it stays eligible as a candidate between real sorted values instead of
/// being excluded outright the way a genuinely foreign type (text/logical amid numeric data) is.
///
/// The table_array/lookup_vector argument in these tests is wrapped in INDEX(range,0,0) rather
/// than passed as a bare range literal, so evaluation is forced through the generic
/// BuiltInFunctions.Lookup.Legacy.cs path (where R47-formula-vlookup-hlookup-approx-3-1 lived)
/// instead of FormulaEvaluator's separate "direct literal range" fast path, which has its own
/// independent (and, as of this fix, still separately-bugged) copy of the same scan.
/// </summary>
public sealed class R47_LookupApproximateBlankCoercionTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    [Fact]
    public void Vlookup_Approximate_BlankBetweenNumericKeys_CoercesToZero_NotSkipped()
    {
        // Ascending column [-5, blank, 10] (blank coerces to 0, giving effective [-5, 0, 10]).
        // VLOOKUP(0.5, ...) should land on the blank row ("mid"), not skip past it to "low".
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-5)), (1, 2, new TextValue("low")),
            (2, 1, BlankValue.Instance),  (2, 2, new TextValue("mid")),
            (3, 1, new NumberValue(10)),  (3, 2, new TextValue("high")));

        _eval.Evaluate("=VLOOKUP(0.5,INDEX(A1:B3,0,0),2,TRUE)", sheet)
            .Should().Be(new TextValue("mid"));
    }

    [Fact]
    public void Vlookup_Approximate_NoBlankInColumn_StillFindsCorrectRow_NotRegressedByBlankCoercionFix()
    {
        // Sibling regression guard: an ordinary all-numeric sorted column (no blanks at all)
        // must still resolve to the correct row through the same INDEX(...,0,0)-forced slow path.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-5)), (1, 2, new TextValue("low")),
            (2, 1, new NumberValue(1)),  (2, 2, new TextValue("mid")),
            (3, 1, new NumberValue(10)), (3, 2, new TextValue("high")));

        _eval.Evaluate("=VLOOKUP(0.5,INDEX(A1:B3,0,0),2,TRUE)", sheet)
            .Should().Be(new TextValue("low"));
    }

    [Fact]
    public void Hlookup_Approximate_BlankBetweenNumericKeys_CoercesToZero_NotSkipped()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-5)), (1, 2, BlankValue.Instance), (1, 3, new NumberValue(10)),
            (2, 1, new TextValue("low")), (2, 2, new TextValue("mid")), (2, 3, new TextValue("high")));

        _eval.Evaluate("=HLOOKUP(0.5,INDEX(A1:C2,0,0),2,TRUE)", sheet)
            .Should().Be(new TextValue("mid"));
    }

    [Fact]
    public void Match_Approximate_Ascending_BlankBetweenNumericKeys_CoercesToZero_NotSkipped()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-5)),
            (2, 1, BlankValue.Instance),
            (3, 1, new NumberValue(10)));

        // Effective sequence [-5, 0, 10]: MATCH(0.5, ..., 1) should land on row 2 (the blank),
        // not row 1.
        _eval.Evaluate("=MATCH(0.5,INDEX(A1:A3,0,0),1)", sheet)
            .Should().Be(new NumberValue(2));
    }

    [Fact]
    public void Lookup_VectorForm_BlankBetweenNumericKeys_CoercesToZero_NotSkipped()
    {
        var sheet = MakeSheet(
            (1, 1, new NumberValue(-5)), (1, 2, new TextValue("low")),
            (2, 1, BlankValue.Instance),  (2, 2, new TextValue("mid")),
            (3, 1, new NumberValue(10)),  (3, 2, new TextValue("high")));

        _eval.Evaluate("=LOOKUP(0.5,INDEX(A1:A3,0,0),INDEX(B1:B3,0,0))", sheet)
            .Should().Be(new TextValue("mid"));
    }

    [Fact]
    public void Vlookup_Approximate_BlankNotChosenAsBestMatch_StillHoldsViaDirectRangeFastPath()
    {
        // No-regression guard mirroring the pre-existing pinned test
        // (FunctionLibraryTests.Lookup.Vlookup_Approximate_BlankNotChosenAsBestMatch): with this
        // particular dataset, coercing the blank to 0 and skipping it entirely produce the exact
        // same answer (the blank's coerced value 0 never overtakes the very next real candidate
        // during the last-match-wins scan), so the direct-range fast path's pre-existing behavior
        // and this fix's slow-path behavior agree here.
        var sheet = MakeSheet(
            (1, 1, BlankValue.Instance), (1, 2, new TextValue("blank-row")),
            (2, 1, new NumberValue(1)),  (2, 2, new TextValue("one")),
            (3, 1, new NumberValue(3)),  (3, 2, new TextValue("three")));

        _eval.Evaluate("=VLOOKUP(2,A1:B3,2,TRUE)", sheet).Should().Be(new TextValue("one"));
        _eval.Evaluate("=VLOOKUP(2,INDEX(A1:B3,0,0),2,TRUE)", sheet).Should().Be(new TextValue("one"));
    }
}
