using System.Diagnostics;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R54-formula-financial-depreciation-4-1/4-3: DDB, AMORDEGRC, and AMORLINC each truncate a
/// caller-supplied `period` into a loop bound with no upper bound and (for DDB) no protection
/// against the truncation overflowing once `period` exceeds Int32.MaxValue.
///
/// DDB (4-1): `(int)Math.Floor(period)` silently overflows to a garbage loop bound for a
/// period beyond Int32.MaxValue, and the subsequent `fraction = period - fullPeriods`
/// computation inherits that garbage, producing an astronomically wrong (not #NUM!) result
/// instead of the mathematically-correct near-zero value once book value has already reached
/// the salvage floor.
///
/// AMORDEGRC/AMORLINC (4-3): once book value has converged (AMORDEGRC: the rounded per-period
/// depreciation underflows to exactly 0; AMORLINC: the constant per-period depreciation is
/// fully clamped to 0 because book value has reached salvage), every remaining period is
/// provably identical, so a huge-but-legal `period` should return near-instantly rather than
/// iterating the full, unbounded period count.
/// </summary>
public sealed class R54_DepreciationUnboundedLoopGuardTests
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

    // ── R54-formula-financial-depreciation-4-1 (DDB) ────────────────────────

    [Fact]
    public void Ddb_HugePeriodBeyondInt32Range_DoesNotOverflowToWrongValue()
    {
        // period = 3,000,000,000 exceeds Int32.MaxValue (~2.147e9). With cost=1000, salvage=100,
        // life=5, factor=2, book value has already been driven down to the exact salvage floor
        // by period 6, so the mathematically-correct depreciation for period 3,000,000,000 (an
        // exact whole number, so there is no fractional-period slice) is exactly 0. A truncating
        // `(int)Math.Floor(period)` cast that overflows would instead compute a garbage loop
        // bound and multiply a nonzero per-period depreciation by a huge bogus "fraction",
        // yielding an astronomically wrong nonzero result.
        Calc("DDB(1000,100,5,3000000000,2)").Should().Be(0.0);
    }

    [Fact]
    public void Ddb_NormalPeriodWithinLife_StillComputesCorrectDepreciation()
    {
        // Sibling no-regression guard: an ordinary first-period call must still compute the
        // standard double-declining-balance amount (cost * factor / life = 1000*2/5 = 400)
        // unaffected by the convergence-based early exit or the double-based loop counter.
        Calc("DDB(1000,100,5,1,2)").Should().Be(400.0);
    }

    // ── R54-formula-financial-depreciation-4-3 (AMORDEGRC / AMORLINC) ───────

    [Fact]
    public void Amordegrc_NearZeroRateHugePeriod_ReturnsQuicklyWithoutUnboundedLoop()
    {
        // rate is small enough that the very first period's rounded depreciation underflows to
        // exactly 0 currency units; book value then never changes again, so every one of the
        // 200,000,000 requested periods is provably 0. Without an early exit, the loop must
        // still spin through all 200,000,000 iterations (each doing a Math.Round call) to reach
        // that same answer -- taking seconds rather than returning near-instantly.
        var stopwatch = Stopwatch.StartNew();
        var value = Calc("AMORDEGRC(1000,DATE(2020,1,1),DATE(2020,6,1),100,200000000,0.0000001,0)");
        stopwatch.Stop();

        value.Should().Be(0.0);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "a rounded-to-zero per-period depreciation must short-circuit instead of iterating all 200,000,000 periods");
    }

    [Fact]
    public void Amordegrc_DocumentedExample_StillMatchesExcel_NotRegressed()
    {
        // Sibling no-regression guard: Excel's own documented AMORDEGRC example, well within a
        // small period count, must still resolve exactly as before.
        Calc("AMORDEGRC(2400,DATE(1998,8,19),DATE(1998,12,31),300,1,0.15,1)").Should().Be(776.0);
    }

    [Fact]
    public void Amorlinc_NormalRateHugePeriod_ReturnsQuicklyWithoutUnboundedLoop()
    {
        // cost=2400, salvage=300, rate=0.15 fully depreciates book value to the salvage floor
        // within a handful of periods, so the mathematically-correct depreciation for period
        // 2,000,000,000 (a huge-but-legal, well-within-Int32-range value) is exactly 0. Without
        // an early exit, the loop must still spin through all 2,000,000,000 iterations to reach
        // that same answer.
        var stopwatch = Stopwatch.StartNew();
        var value = Calc("AMORLINC(2400,DATE(1998,8,19),DATE(1998,12,31),300,2000000000,0.15,1)");
        stopwatch.Stop();

        value.Should().Be(0.0);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2),
            "book value pinned at the salvage floor must short-circuit instead of iterating all 2,000,000,000 periods");
    }

    [Fact]
    public void Amorlinc_DocumentedExample_StillMatchesExcel_NotRegressed()
    {
        // Sibling no-regression guard: a normal, well within-life schedule must still behave as
        // before (period 1's full annual straight-line slice, clamped to remaining basis).
        Calc("AMORLINC(2400,DATE(1998,8,19),DATE(1998,12,31),300,1,0.15,1)")
            .Should().BeApproximately(360.0, 0.5);
    }
}
