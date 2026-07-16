using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Formula.Tests;

/// <summary>
/// R47-render-number-format-display-3-1: SelectDateTimeSection must honor Excel's positional
/// pos/neg/zero section rule for a DateTimeValue (mirroring SelectPositionalSection, already used
/// for NumberValue) instead of always applying section 0 whenever the format has no explicit
/// [condition] sections. A multi-section custom format applied to a date/time-typed value (e.g. a
/// cell holding exactly midnight, serial 0, or a date value driven negative by arithmetic) must
/// pick its section by the value's sign just like a plain number does.
/// </summary>
public sealed class R47_NumberFormatterDateTimeSectionTests
{
    [Fact]
    public void DateTimeValue_ZeroSerial_MultiSectionFormat_UsesZeroSection_NotSectionZero()
    {
        // "h:mm:ss;;\"midnight\"": positive uses section 0 (time), negative uses section 1
        // (empty -- blank), zero uses section 2 ("midnight"). A DateTimeValue of exactly 0 must
        // pick the zero section, not always section 0 (which would render "0:00:00").
        var result = NumberFormatter.Format(new DateTimeValue(0.0), "h:mm:ss;;\"midnight\"");

        result.Should().Be("midnight");
    }

    [Fact]
    public void DateTimeValue_NegativeSerial_MultiSectionFormat_UsesNegativeSection_NotSectionZero()
    {
        // "m/d/yyyy;\"neg date\"": positive uses section 0 (calendar date), negative uses
        // section 1 ("neg date"). A DateTimeValue of -5 must pick the negative section instead of
        // always section 0 (which would render a bogus calendar date like "12/26/1899").
        var result = NumberFormatter.Format(new DateTimeValue(-5.0), "m/d/yyyy;\"neg date\"");

        result.Should().Be("neg date");
    }

    [Fact]
    public void DateTimeValue_PositiveSerial_MultiSectionFormat_StillUsesFirstSection_NotRegressed()
    {
        // Sibling regression guard: a positive DateTimeValue must still resolve to section 0 (the
        // ordinary calendar-date rendering), exactly as it did before the fix -- the pos/neg/zero
        // dispatch must not misroute the common (positive) case.
        var result = NumberFormatter.Format(new DateTimeValue(1.0), "m/d/yyyy;\"neg date\"");

        result.Should().Be("1/1/1900");
    }

    [Fact]
    public void DateTimeValue_PositiveSerial_SingleSectionFormat_Unaffected_NotRegressed()
    {
        // Sibling regression guard: the overwhelmingly common single-section date format (no
        // semicolons at all) must be completely unaffected by the positional-section fix.
        var result = NumberFormatter.Format(new DateTimeValue(1.0), "m/d/yyyy");

        result.Should().Be("1/1/1900");
    }
}
