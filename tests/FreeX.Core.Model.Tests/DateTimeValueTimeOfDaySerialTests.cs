using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class DateTimeValueTimeOfDaySerialTests
{
    [Fact]
    public void FromDateTime_TimeOfDaySerialBelowOne_KeepsFractionWithoutExtraDay()
    {
        // A date-formatted cell holding 0.6 reaches the mapper as 1899-12-31 14:24; it must stay serial 0.6
        // (it previously loaded as 1.6, so SUMIF over such cells gained a spurious +1 per value).
        DateTimeValue.FromDateTime(new DateTime(1899, 12, 31, 14, 24, 0)).Value.Should().BeApproximately(0.6, 1e-9);
        DateTimeValue.FromDateTime(new DateTime(1899, 12, 31)).Value.Should().Be(0d);
        DateTimeValue.FromDateTime(new DateTime(1900, 1, 1)).Value.Should().Be(1d);
    }
}
