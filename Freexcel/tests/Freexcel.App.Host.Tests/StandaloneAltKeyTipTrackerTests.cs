using System.Windows.Input;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class StandaloneAltKeyTipTrackerTests
{
    [Fact]
    public void ShouldToggleOnKeyUp_RequiresUnmodifiedStandaloneAltPress()
    {
        var tracker = new StandaloneAltKeyTipTracker();

        tracker.BeginStandaloneAltCandidate();

        tracker.ShouldToggleOnKeyUp(Key.System).Should().BeTrue();
    }

    [Fact]
    public void ShouldToggleOnKeyUp_IgnoresAltChordAfterAnyNonAltKey()
    {
        var tracker = new StandaloneAltKeyTipTracker();

        tracker.BeginStandaloneAltCandidate();
        tracker.CancelStandaloneAltCandidate();

        tracker.ShouldToggleOnKeyUp(Key.System).Should().BeFalse();
    }

    [Theory]
    [InlineData(0x2C)]
    [InlineData(0x48)]
    [InlineData(0x70)]
    public void IsAltVirtualKey_RejectsNonAltSystemChordKeys(int virtualKey)
    {
        StandaloneAltKeyTipTracker.IsAltVirtualKey(virtualKey).Should().BeFalse();
    }

    [Theory]
    [InlineData(0x12)]
    [InlineData(0xA4)]
    [InlineData(0xA5)]
    public void IsAltVirtualKey_AcceptsAltVirtualKeys(int virtualKey)
    {
        StandaloneAltKeyTipTracker.IsAltVirtualKey(virtualKey).Should().BeTrue();
    }
}
