using FluentAssertions;
using Freexcel.Core.Commands;
using Freexcel.Core.Model;

namespace Freexcel.Core.Model.Tests;

public sealed class DateTimeEntryServiceTests
{
    [Fact]
    public void CurrentDate_ReturnsDateWithMidnightTime()
    {
        var now = new DateTime(2026, 5, 14, 16, 30, 45);

        var value = DateTimeEntryService.CurrentDate(now);

        value.ToDateTime().Should().Be(new DateTime(2026, 5, 14));
    }

    [Fact]
    public void CurrentTime_ReturnsFractionalDayOnly()
    {
        var now = new DateTime(2026, 5, 14, 16, 30, 45);

        var value = DateTimeEntryService.CurrentTime(now);

        value.Value.Should().BeApproximately(now.TimeOfDay.TotalDays, 0.0000000001);
    }
}
