using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Nothing clamps a date serial at creation — date autofill extrapolates a series freely, Paste
/// Special can do arithmetic on a date, and a loaded file may carry any double — so a
/// <see cref="DateTimeValue"/> can hold a serial outside <c>DateTime</c>'s representable range.
/// <para>
/// <see cref="DateTimeValue.ToDateTime"/> must keep throwing for those, because the IO writers rely
/// on that to persist the original serial as a raw numeric/text cell (R68) instead of silently
/// rewriting the saved value. Display-side callers use <see cref="DateTimeValue.TryToDateTime"/>
/// instead, which is what stops an out-of-range serial from crashing the app on an ordinary action
/// such as opening the AutoFilter dropdown.
/// </para>
/// </summary>
public sealed class DateTimeValueOutOfRangeSerialTests
{
    [Theory]
    [InlineData(1e9)]          // far past year 9999, e.g. a runaway autofill series
    [InlineData(-1e9)]         // far before year 100
    [InlineData(3_000_000d)]   // just past the OADate maximum
    [InlineData(-700_000d)]    // just past the OADate minimum
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.NaN)]
    public void TryToDateTime_OutOfRangeSerial_ReturnsFalseInsteadOfThrowing(double serial)
    {
        var value = new DateTimeValue(serial);

        var act = () => value.TryToDateTime(out _);

        act.Should().NotThrow("display-side callers must degrade, not crash the app");
        value.TryToDateTime(out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(45306, 2024, 1, 15)]
    [InlineData(15, 1900, 1, 15)]     // inside the 1900 phantom-leap-day correction window
    [InlineData(61, 1900, 3, 1)]
    public void TryToDateTime_InRangeSerial_ConvertsExactlyLikeToDateTime(
        double serial,
        int year,
        int month,
        int day)
    {
        var value = new DateTimeValue(serial);

        value.TryToDateTime(out var converted).Should().BeTrue();
        converted.Should().Be(new DateTime(year, month, day));
        converted.Should().Be(value.ToDateTime(), "the safe path must agree with the throwing one");
    }

    [Fact]
    public void ToDateTime_KeepsThrowingForOutOfRangeSerials_SoSaveFidelityIsPreserved()
    {
        var act = () => new DateTimeValue(1e9).ToDateTime();

        // FromOADate reports the failure as ArgumentException (ArgumentOutOfRangeException derives
        // from it), so assert the base type to cover both shapes.
        act.Should().Throw<ArgumentException>(
            "the IO writers detect an unrepresentable serial this way and persist it verbatim (R68)");
    }

    [Fact]
    public void TryToDateTime_DoesNotMutateTheStoredSerial()
    {
        var value = new DateTimeValue(1e9);

        value.TryToDateTime(out _);

        value.Value.Should().Be(1e9, "saving must still round-trip the original serial");
    }
}
