using System.Windows.Input;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class StandaloneAltKeyTipTrackerTests
{
    [Theory]
    [InlineData(Key.LeftAlt)]
    [InlineData(Key.RightAlt)]
    [InlineData(Key.System)]
    public void ShouldToggleOnKeyUp_RequiresUnmodifiedStandaloneAltPress(Key key)
    {
        var tracker = new StandaloneAltKeyTipTracker();

        tracker.BeginStandaloneAltCandidate();

        tracker.ShouldToggleOnKeyUp(key).Should().BeTrue();
    }

    [Fact]
    public void ShouldToggleOnKeyUp_IgnoresAltChordAfterAnyNonAltKey()
    {
        var tracker = new StandaloneAltKeyTipTracker();

        tracker.BeginStandaloneAltCandidate();
        tracker.CancelStandaloneAltCandidate();

        tracker.ShouldToggleOnKeyUp(Key.System).Should().BeFalse();
    }

    [Fact]
    public void ShouldToggleOnKeyUp_ConsumesPendingCandidateAfterNonAltKeyUp()
    {
        var tracker = new StandaloneAltKeyTipTracker();

        tracker.BeginStandaloneAltCandidate();

        tracker.ShouldToggleOnKeyUp(Key.F10).Should().BeFalse();
        tracker.ShouldToggleOnKeyUp(Key.System).Should().BeFalse();
    }

    [Theory]
    [InlineData(Key.LeftAlt, true)]
    [InlineData(Key.RightAlt, true)]
    [InlineData(Key.System, true)]
    [InlineData(Key.F10, false)]
    public void IsStandaloneAltKey_AcceptsOnlyStandaloneAltKeys(Key key, bool expected)
    {
        StandaloneAltKeyTipTracker.IsStandaloneAltKey(key).Should().Be(expected);
    }

    [Theory]
    [InlineData(0x2C)]
    [InlineData(0x48)]
    [InlineData(0x70)]
    [InlineData(0x73)]
    [InlineData(0x75)]
    [InlineData(0x79)]
    [InlineData(0x7A)]
    [InlineData(0x7B)]
    [InlineData(0x5D)]
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
