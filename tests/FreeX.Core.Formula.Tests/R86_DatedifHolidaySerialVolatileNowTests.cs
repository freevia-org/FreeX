using System.Diagnostics;
using System.Threading;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// Round-86 formula-datetime-volatile bucket regression tests (Core.Formula:
/// BuiltInFunctions.DateTime.cs / BuiltInFunctions.WorkdayIntl.cs):
///
/// R86-formula-datetime-parts-5-1: DATEDIF's start&gt;end guard compared the *collapsed*
/// DateTime values (start.Date/end.Date), which map both serial 59 ("1900-02-28") and serial 60
/// (the 1900 phantom leap day, "1900-02-29") onto the identical real date. A reversed range whose
/// endpoints straddle that exact pair (e.g. DATEDIF(60, 59, unit) - start &gt; end) therefore read
/// end.Date == start.Date and slipped past the guard instead of returning #NUM!. Fixed by
/// comparing the raw floored serials instead.
///
/// R86-formula-datetime-parts-5-2: WORKDAY/NETWORKDAYS(.INTL)'s holiday set stored/looked up
/// holidays by collapsed DateTime.Date, so a holiday specified at serial 59 also silently excluded
/// serial 60 (and vice versa) since both collapse to the same DateTime. Fixed by keying the
/// holiday set on the raw floored Excel serial instead of the resolved DateTime.
///
/// R86-calc-volatile-circular-5-1: NOW()/TODAY() read DateTime.Now/DateTime.Today fresh on every
/// single call, so calls separated by measurable wall-clock time (e.g. a large dependency chain
/// evaluated within one recalculation pass) could observe visibly different instants instead of
/// Excel's guaranteed single frozen calculation-time snapshot per pass. Fixed with a bounded,
/// self-contained mitigation: cache the captured instant and keep reusing it as long as
/// NOW()/TODAY() keep being invoked within a short idle window.
/// </summary>
public sealed class R86_DatedifHolidaySerialVolatileNowTests
{
    private readonly FormulaEvaluator _eval = new();

    private static Sheet MakeSheet(params (int row, int col, ScalarValue val)[] cells)
    {
        var sheet = new Sheet(SheetId.New(), "S");
        foreach (var (r, c, v) in cells)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)r, (uint)c), v);
        return sheet;
    }

    // --- R86-formula-datetime-parts-5-1: DATEDIF reversed-range guard vs. serial 59/60 ---

    [Fact]
    public void Datedif_PhantomLeapDayReversedRange_UnitD_ReturnsNumError()
    {
        // start_date = serial 60 (1900-02-29, the phantom leap day), end_date = serial 59
        // (1900-02-28): 60 > 59 is a reversed range, so DATEDIF must return #NUM!. Pre-fix, both
        // serials collapsed to the same DateTime, so end.Date < start.Date read false and the
        // "D" arm went on to compute 59-60 = -1 instead of erroring.
        _eval.Evaluate("=DATEDIF(60,59,\"D\")", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Datedif_PhantomLeapDayReversedRange_UnitY_ReturnsNumError()
    {
        // Every unit shares the same guard, not just "D".
        _eval.Evaluate("=DATEDIF(60,59,\"Y\")", MakeSheet()).Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Datedif_OrdinaryReversedRange_StillReturnsNumError()
    {
        // Sibling no-regression case: an ordinary (non-phantom-leap-day) reversed range must
        // still correctly error, exactly as before the fix.
        _eval.Evaluate("=DATEDIF(DATE(2024,5,1),DATE(2024,1,1),\"D\")", MakeSheet())
            .Should().Be(ErrorValue.Num);
    }

    [Fact]
    public void Datedif_PhantomLeapDayForwardRange_StillComputesNormally()
    {
        // Sibling no-regression case: a valid forward range starting exactly at the phantom leap
        // day (serial 60) must still compute correctly, unaffected by the tightened guard.
        _eval.Evaluate("=DATEDIF(60,DATE(1900,4,28),\"YM\")", MakeSheet())
            .Should().Be(new NumberValue(1));
    }

    // --- R86-formula-datetime-parts-5-2: holiday matching keyed by DateTime vs. serial ---

    [Fact]
    public void NetworkdaysIntl_HolidayAtSerial59_DoesNotAlsoExcludeSerial60()
    {
        // Serials 58..62 = Mon..Fri in Excel's 1900 system. A single holiday specified at serial
        // 59 (1900-02-28) must exclude only that day, leaving 4 working days (58,60,61,62). The
        // pre-fix HashSet<DateTime> collapsed serial 59 and serial 60 (the phantom leap day) onto
        // the same DateTime, so specifying the serial-59 holiday also silently excluded serial 60.
        _eval.Evaluate("=NETWORKDAYS.INTL(58,62,1,59)", MakeSheet())
            .Should().Be(new NumberValue(4));
    }

    [Fact]
    public void Networkdays_HolidayAtSerial60_ExcludesExactlyThatDayNotSerial59()
    {
        // Sibling coverage for the reverse direction described in the finding: a holiday
        // specified AT serial 60 must exclude serial 60, not silently reconstruct/exclude serial
        // 59 instead. The span 60..62 (Wed/Thu/Fri, no weekend) deliberately excludes serial 59
        // itself, so a pre-fix DateToSerial(holiday.Date) reconstruction (which can only ever
        // yield 59, never 60) would fall outside this range and subtract nothing, returning 3
        // instead of the correct 2.
        _eval.Evaluate("=NETWORKDAYS(60,62,60)", MakeSheet())
            .Should().Be(new NumberValue(2));
    }

    [Fact]
    public void WorkdayIntl_HolidayOnOrdinaryModernDate_StillExcludedNormally()
    {
        // Sibling no-regression case: an ordinary (non-1900-phantom) holiday must still be
        // correctly skipped by WORKDAY.INTL, unaffected by switching the holiday set's key type.
        // No-weekend mask + a single 2024-01-09 holiday: 5 working days forward from 2024-01-08
        // (skipping the 1/9 holiday) lands on 2024-01-14.
        double expected = new DateTime(2024, 1, 14).ToOADate();
        ((NumberValue)_eval.Evaluate("=WORKDAY.INTL(DATE(2024,1,8),5,\"0000000\",DATE(2024,1,9))", MakeSheet())).Value
            .Should().BeApproximately(expected, 1);
    }

    // --- R86-calc-volatile-circular-5-1: NOW()/TODAY() pass-scoped consistency ---

    [Fact]
    public void Now_RepeatedCallsWithinShortIdleWindow_ReturnIdenticalSerial()
    {
        // The production idle window is 200 ms. A fixed Sleep(50) is not evidence that the
        // observed interval stayed inside it: under a parallel suite the test thread can be
        // descheduled for several hundred milliseconds. Retry until we observe a bounded
        // interval, while retaining enough work between calls for an uncached NOW() to change.
        const int maximumObservedIntervalMs = 150;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var first = (DateTimeValue)_eval.Evaluate("=NOW()", MakeSheet());
            var elapsed = Stopwatch.StartNew();
            Thread.SpinWait(50_000);
            var second = (DateTimeValue)_eval.Evaluate("=NOW()", MakeSheet());
            elapsed.Stop();

            if (elapsed.ElapsedMilliseconds > maximumObservedIntervalMs)
            {
                continue;
            }

            second.Value.Should().Be(first.Value);
            return;
        }

        Assert.Fail("Could not observe two NOW() evaluations within the 200 ms idle window.");
    }

    [Fact]
    public void Now_ReturnsCurrentDate_UnaffectedByCaching()
    {
        // Sibling no-regression case: NOW() must still resolve to today's actual calendar date
        // (not a stale/frozen date from a previous test run), just cached within a short burst.
        var result = (DateTimeValue)_eval.Evaluate("=NOW()", MakeSheet());
        var expectedToday = DateTime.Today.ToOADate();
        result.Value.Should().BeInRange(expectedToday, expectedToday + 1);
    }
}
