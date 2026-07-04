using FluentAssertions;
using FreeX.App.Host;
using static FreeX.App.Host.Tests.WorkbookWindowRegistryTestSupport;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for the process-global side-by-side pairing state: an unrelated third
/// window must not be able to silently un-pair (and desync the synchronous scrolling of) a pair
/// it was never part of. See DisableSideBySideFor on WorkbookWindowRegistry.
/// </summary>
public sealed class WorkbookWindowRegistrySideBySideOwnershipTests
{
    [Fact]
    public void DisableSideBySideFor_FromAnUnrelatedThirdWindow_LeavesTheActivePairIntact()
    {
        var (registry, windows) = RegisterWindows(3);
        registry.EnableSideBySide(windows[0], 1920, 1080).Should().BeTrue();
        registry.SetSynchronousScroll(true).Should().BeTrue();

        // Window C (windows[2]) was never part of the A/B pair; toggling "View Side by Side" on it
        // must not tear down A/B's pairing or synchronous scrolling.
        registry.DisableSideBySideFor(windows[2]).Should().BeFalse();

        registry.IsSideBySideActive.Should().BeTrue();
        registry.IsSynchronousScrollActive.Should().BeTrue();

        // Scrolling still mirrors between the real pair.
        registry.BroadcastScrollOffset(windows[0], new WorkbookScrollOffset(4, 1));
        windows[1].SetScrollOffsetCount.Should().Be(1);
    }

    [Fact]
    public void DisableSideBySideFor_FromThePrimary_DeactivatesThePair()
    {
        var (registry, windows) = RegisterWindows(2);
        registry.EnableSideBySide(windows[0], 1920, 1080);
        registry.SetSynchronousScroll(true);

        registry.DisableSideBySideFor(windows[0]).Should().BeTrue();

        registry.IsSideBySideActive.Should().BeFalse();
        registry.IsSynchronousScrollActive.Should().BeFalse();
    }

    [Fact]
    public void DisableSideBySideFor_FromThePartner_DeactivatesThePair()
    {
        var (registry, windows) = RegisterWindows(2);
        registry.EnableSideBySide(windows[0], 1920, 1080);
        registry.SetSynchronousScroll(true);

        registry.DisableSideBySideFor(windows[1]).Should().BeTrue();

        registry.IsSideBySideActive.Should().BeFalse();
        registry.IsSynchronousScrollActive.Should().BeFalse();
    }

    [Fact]
    public void DisableSideBySideFor_WhenNoPairIsActive_IsANoOpAndReturnsFalse()
    {
        var (registry, windows) = RegisterWindows(2);

        registry.DisableSideBySideFor(windows[0]).Should().BeFalse();

        registry.IsSideBySideActive.Should().BeFalse();
    }
}
