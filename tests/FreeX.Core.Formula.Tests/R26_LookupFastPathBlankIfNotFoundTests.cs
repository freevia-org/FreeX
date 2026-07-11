using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Regression test for round-26 finding R26-meta-2 in FormulaEvaluator.LookupFastPaths.cs:
///
/// TryEvaluateXlookupDirectRanges (the bare-range XLOOKUP fast path) treated an
/// explicitly-supplied but blank-valued if_not_found argument the same as an omitted one,
/// always substituting #N/A instead of returning the supplied blank value verbatim. This was
/// an un-mirrored twin of round-25's R25-lookup-functions-deep-3 fix to the slow path
/// (BuiltInFunctions.Lookup.Modern.cs's Xlookup, see R25_LookupModernTests.cs), which already
/// keys the default off argument arity (args.Count > 3) alone. Fixed by making the fast path
/// do the same: once the if_not_found argument is confirmed supplied (node.Arguments.Count > 3)
/// and not itself an error/range, its evaluated value -- blank or not -- is used verbatim.
/// </summary>
public class R26_LookupFastPathBlankIfNotFoundTests
{
    private readonly FormulaEvaluator _eval = new();

    [Fact]
    public void Xlookup_DirectRange_ExplicitlySuppliedBlankIfNotFound_ReturnsBlankNotNA()
    {
        // A1:A3 = {1,2,3}; C1 is a genuinely empty cell, referenced explicitly (bare, no
        // wrapper) as if_not_found -- this takes the direct-range fast path this bucket owns.
        // FormulaEvaluator.NormalizeTopLevelResult converts a formula whose FINAL top-level
        // result is blank into 0 (matching real Excel), which is pre-existing, intentional
        // behavior unrelated to this fix -- so the fix is visible at the top level as "0"
        // instead of the old bug's "#N/A", and directly (pre-normalization) via ISBLANK.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)));

        _eval.Evaluate("=ISBLANK(XLOOKUP(99,A1:A3,A1:A3,C1))", sheet).Should().Be(new BoolValue(true));
        _eval.Evaluate("=XLOOKUP(99,A1:A3,A1:A3,C1)", sheet).Should().Be(new NumberValue(0));
    }

    [Fact]
    public void Xlookup_DirectRange_OmittedIfNotFound_StillDefaultsToNA()
    {
        // Sibling already-working case: a genuinely omitted (3-arg call) if_not_found must keep
        // defaulting to #N/A on the direct-range fast path -- the arity-based fix must not
        // affect this case.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)));

        _eval.Evaluate("=XLOOKUP(99,A1:A3,A1:A3)", sheet).Should().Be(ErrorValue.NA);
    }

    [Fact]
    public void Xlookup_DirectRange_ExplicitlySuppliedNonBlankIfNotFound_StillReturnsSuppliedValue()
    {
        // Sibling already-working case: a non-blank explicit if_not_found value on the
        // direct-range fast path must keep working exactly as before.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)));

        _eval.Evaluate("=XLOOKUP(99,A1:A3,A1:A3,\"NF\")", sheet).Should().Be(new TextValue("NF"));
    }

    [Fact]
    public void Xlookup_DirectRange_MatchFound_StillReturnsMatchedValue()
    {
        // Sibling already-working case: a successful match must still short-circuit before
        // if_not_found is even consulted, blank or not.
        var sheet = MakeSheet(
            (1, 1, new NumberValue(1)), (2, 1, new NumberValue(2)), (3, 1, new NumberValue(3)));

        _eval.Evaluate("=XLOOKUP(2,A1:A3,A1:A3,C1)", sheet).Should().Be(new NumberValue(2));
    }

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (row, col, val) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)row, (uint)col), val);
        return sheet;
    }
}
