using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class BackstageGreetingFormatterTests
{
    [Theory]
    [InlineData(0, "Backstage_GreetingMorning")]
    [InlineData(11, "Backstage_GreetingMorning")]
    [InlineData(12, "Backstage_GreetingAfternoon")]
    [InlineData(16, "Backstage_GreetingAfternoon")]
    [InlineData(17, "Backstage_GreetingEvening")]
    [InlineData(23, "Backstage_GreetingEvening")]
    public void FormatGreeting_UsesExcelBackstageDaypartBoundaries(int hour, string expectedKey)
    {
        BackstageGreetingFormatter.FormatGreeting(hour).Should().Be(UiText.Get(expectedKey));
    }
}
