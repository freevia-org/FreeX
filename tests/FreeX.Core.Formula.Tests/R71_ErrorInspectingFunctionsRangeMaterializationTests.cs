using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R71-formula-logical-info-4-1: when an IS*/N/TYPE/ERROR.TYPE argument is a disjoint explicit
/// intersection (e.g. A1:A2 C1:C2 -> #NULL!), TryResolveReferenceRange wraps the failure as a
/// RangeMaterializationErrorValue, and the pre-func short-circuit in
/// FormulaEvaluator.EvaluateFunction returned that raw error before the function body ever ran --
/// so ISERROR/ISNA/ISBLANK/ISNUMBER/ISTEXT/ISNONTEXT/ISLOGICAL/ISERR/N/TYPE/ERROR.TYPE never got a
/// chance to inspect the erroring argument, contrary to Excel's IS-family contract (which inspects,
/// never propagates). Fixed by unwrapping RangeMaterializationErrorValue to its inner ErrorValue in
/// expandedArgs for that specific error-inspecting function family only, before invoking the
/// function body -- every other function (SUM, VLOOKUP, ...) still short-circuits and propagates.
/// </summary>
public sealed class R71_ErrorInspectingFunctionsRangeMaterializationTests
{
    private readonly FormulaEvaluator _eval = new();

    // Column A (col 1) and column C (col 3) never overlap -> the intersection materializes as
    // #NULL!, matching R65_ExplicitIntersectionOperatorTests.DisjointIntersection_ReturnsNullError.
    private static Sheet MakeSheet()
    {
        var sheet = new Sheet(SheetId.New(), "S");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        return sheet;
    }

    [Fact]
    public void Iserror_OfDisjointIntersection_InspectsAndReturnsTrue()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=ISERROR(A1:A2 C1:C2)", sheet).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Isna_OfDisjointIntersection_ReturnsFalse()
    {
        // #NULL! is not #N/A, so ISNA must report FALSE rather than propagating the #NULL!.
        var sheet = MakeSheet();

        _eval.Evaluate("=ISNA(A1:A2 C1:C2)", sheet).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void Isnumber_OfDisjointIntersection_ReturnsFalse()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=ISNUMBER(A1:A2 C1:C2)", sheet).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void Isblank_OfDisjointIntersection_ReturnsFalse()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=ISBLANK(A1:A2 C1:C2)", sheet).Should().Be(new BoolValue(false));
    }

    [Fact]
    public void ErrorType_OfDisjointIntersection_ReturnsOne()
    {
        // ERROR.TYPE(#NULL!) = 1 in Excel.
        var sheet = MakeSheet();

        _eval.Evaluate("=ERROR.TYPE(A1:A2 C1:C2)", sheet).Should().Be(new NumberValue(1));
    }

    [Fact]
    public void N_OfDisjointIntersection_PropagatesTheErrorValue()
    {
        // N() passes ErrorValue arguments through unchanged (NScalar's ErrorValue case), so it
        // still surfaces #NULL! -- but via the function body actually running, not the pre-func
        // short-circuit.
        var sheet = MakeSheet();

        _eval.Evaluate("=N(A1:A2 C1:C2)", sheet).Should().Be(ErrorValue.Null);
    }

    [Fact]
    public void Iserror_OfUnresolvedNamedRangeEndpoint_InspectsAndReturnsTrue()
    {
        // A named-range endpoint that fails to resolve (no such name defined) wraps as #NAME?
        // via TryResolveReferenceRange -- ISERROR must inspect it, not propagate it.
        var sheet = MakeSheet();

        _eval.Evaluate("=ISERROR(NoSuchName:B10)", sheet).Should().Be(new BoolValue(true));
    }

    // --- No-regression siblings -------------------------------------------------------------

    [Fact]
    public void Sum_OfDisjointIntersection_StillShortCircuitsToNullError()
    {
        // Non-error-inspecting aggregate functions must still propagate the range-materialization
        // error exactly as before (see R65_ExplicitIntersectionOperatorTests's sibling test).
        var sheet = MakeSheet();

        _eval.Evaluate("=SUM(A1:A2 C1:C2)", sheet).Should().Be(ErrorValue.Null);
    }

    [Fact]
    public void Iserror_OfDivisionByZero_StillReturnsTrue()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=ISERROR(1/0)", sheet).Should().Be(new BoolValue(true));
    }

    [Fact]
    public void Isnumber_OfPlainNumber_StillReturnsTrue()
    {
        var sheet = MakeSheet();

        _eval.Evaluate("=ISNUMBER(5)", sheet).Should().Be(new BoolValue(true));
    }
}
