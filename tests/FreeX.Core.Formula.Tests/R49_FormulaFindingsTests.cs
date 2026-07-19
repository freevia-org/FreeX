using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-49 formula-bucket fixes:
///  - ODDFPRICE/ODDFYIELD/ODDLPRICE/ODDLYIELD under basis 2 must use the fixed 360/frequency
///    quasi-coupon-period length, matching the sibling PRICE/YIELD convention.
///  - LET must reject a duplicate binding name within the same call.
///  - NUMBERVALUE must accept a trailing-minus negative number, like VALUE() already does.
///
/// (R49-docs-parity-vs-reality-sweep-1, the BuiltInFunctions.Names/Bessel visibility finding, is
/// SKIPPED here -- see the structured result for the blocker: fixing it correctly surfaces a
/// pre-existing gap in docs/parity/functions.md, a file outside this task's edit scope, and
/// breaks FormulaParityCatalogTests.Registry_DoesNotContainUndocumentedFunctions.)
/// </summary>
public sealed class R49_FormulaFindingsTests
{
    private readonly FormulaEvaluator _eval = new();
    private readonly Sheet _sheet = new(SheetId.New(), "Sheet1");

    private ScalarValue Eval(string formula) => _eval.Evaluate(formula, _sheet);
    private static double Num(ScalarValue v) => ((NumberValue)v).Value;

    // ── formula-financial-bond-3-2: ODDLPRICE basis 2 quasi-coupon-period length ──

    [Fact]
    public void Oddlprice_Basis2_UsesFixed360OverFrequencyPeriodLength_NotActualElapsedDays()
    {
        // last_interest 2007-10-15 -> next nominal coupon 2008-04-15 spans a leap-year February,
        // so the ACTUAL elapsed days is 183, not the fixed E = 360/2 = 180 Excel uses for basis 2.
        // Pre-fix, FinancialDays' "actual elapsed days" branch was used for basis 2, giving a
        // materially different price than Excel's documented-convention fixed-E computation.
        double basis2 = Num(Eval("=ODDLPRICE(DATE(2008,2,7),DATE(2008,6,15),DATE(2007,10,15),3.75%,4.05%,100,2,2)"));

        // Pre-fix (actual-elapsed-days E=183) this evaluates to ~99.87917; with the fixed
        // E=360/2=180 quasi-coupon-period length Excel documents, it is ~99.87690.
        basis2.Should().BeApproximately(99.87690169847588, 1e-9);
        basis2.Should().NotBeApproximately(99.87916768152911, 1e-6);
    }

    [Fact]
    public void Oddlprice_Basis0_DocumentedExcelExample_StillMatches_NoRegression()
    {
        // Sibling no-regression case: the pre-existing Microsoft-documented basis-0 example must
        // still match (basis 0 already used the fixed 360/frequency period length before this fix
        // via Days360Us, which for this input already equals 180 exactly).
        Num(Eval("=ODDLPRICE(DATE(2008,2,7),DATE(2008,6,15),DATE(2007,10,15),3.75%,4.05%,100,2,0)"))
            .Should().BeApproximately(99.88, 0.005);
    }

    // ── formula-lambda-helpers-3-1: LET must reject a duplicate binding name ──

    [Fact]
    public void Let_DuplicateBindingNameInSameCall_ReturnsValueError()
    {
        // Excel rejects "=LET(x, 1, x, x+1, x)" outright ("You can't define the same name twice
        // in a LET function"). Pre-fix, FreeX silently rebound x to x+1=2 and returned 2 instead
        // of erroring.
        Eval("=LET(x, 1, x, x+1, x)").Should().Be(ErrorValue.Value);
    }

    [Fact]
    public void Let_NestedLetShadowingSameName_StillAllowed_NoRegression()
    {
        // A nested LET redefining the same name is a SEPARATE call/binding dictionary and must
        // still be allowed (Excel allows shadowing across nested LET scopes).
        Num(Eval("=LET(x, 10, LET(x, 3, x*2))")).Should().Be(6.0);
    }

    // ── formula-text-format-parse-3-1: NUMBERVALUE trailing-minus negative ──

    [Fact]
    public void Numbervalue_TrailingMinus_ParsesAsNegative()
    {
        // Pre-fix, NUMBERVALUE's double.TryParse used NumberStyles.Float only (no
        // AllowTrailingSign), so a trailing '-' failed to parse and returned #VALUE!, unlike
        // VALUE() which already accepts this accounting convention.
        Num(Eval("=NUMBERVALUE(\"1234-\")")).Should().Be(-1234.0);
    }

    [Fact]
    public void Numbervalue_OrdinaryNegativeAndPositive_StillParse_NoRegression()
    {
        Num(Eval("=NUMBERVALUE(\"-1234\")")).Should().Be(-1234.0);
        Num(Eval("=NUMBERVALUE(\"1234\")")).Should().Be(1234.0);
    }
}
