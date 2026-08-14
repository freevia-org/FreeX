using FluentAssertions;
using FreeX.App.Host;
using static FreeX.App.Host.Tests.WorkbookWindowRegistryTestSupport;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Regression coverage for R90-app-window-arrange-freeze-ui-5-3: Excel's "Reset Window Position"
/// lives in the View Side by Side group and restores BOTH windows of the active side-by-side pair
/// to their original tiled top/bottom (or left/right) halves -- undoing any manual resize/drag made
/// to either window while comparing them. It must never touch an unrelated window, and must do
/// nothing when no side-by-side pair is active (WorkbookWindowRegistry.ResetSideBySidePair, driven
/// by MainWindow.MultiWindow.cs's ViewResetWindowPositionBtn_Click).
/// </summary>
public sealed class R90_ResetSideBySidePairTests
{
    [Fact]
    public void ResetSideBySidePair_WhenActive_RetilesBothPairedWindowsBackToTheirTiledHalves()
    {
        var (registry, windows) = RegisterWindows(2);
        registry.EnableSideBySide(windows[0], workAreaWidth: 1920, workAreaHeight: 1080).Should().BeTrue();

        var originalPrimaryBounds = windows[0].TiledBounds.Should().ContainSingle().Subject;
        var originalPartnerBounds = windows[1].TiledBounds.Should().ContainSingle().Subject;

        // Simulate the user manually dragging/resizing one paired window while comparing (bypasses
        // the registry, exactly like a live WPF window drag would).
        windows[0].TileToWorkArea(new System.Windows.Rect(100, 100, 200, 200));
        windows[0].TiledBounds.Should().HaveCount(2);

        registry.ResetSideBySidePair(windows[0], 1920, 1080).Should().BeTrue();

        // BOTH windows of the pair received a fresh tile call restoring the ORIGINAL side-by-side
        // halves -- not the manually-dragged rectangle, and not an unrelated cascade formula.
        windows[0].TiledBounds.Should().HaveCount(3);
        windows[0].TiledBounds[^1].Should().Be(originalPrimaryBounds);
        windows[1].TiledBounds.Should().HaveCount(2);
        windows[1].TiledBounds[^1].Should().Be(originalPartnerBounds);
    }

    [Fact]
    public void ResetSideBySidePair_NeverTouchesAWindowThatIsNotPartOfThePair()
    {
        var (registry, windows) = RegisterWindows(3);
        registry.EnableSideBySide(windows[0], 1920, 1080).Should().BeTrue();
        windows[2].TiledBounds.Should().BeEmpty("window C never joined the side-by-side pair");

        registry.ResetSideBySidePair(windows[1], 1920, 1080).Should().BeTrue();

        windows[2].TiledBounds.Should().BeEmpty("Reset Window Position must not reposition an unrelated window");
    }

    [Fact]
    public void ResetSideBySidePair_UnrelatedRequesterCannotRetileAnotherPair()
    {
        var (registry, windows) = RegisterWindows(3);
        registry.EnableSideBySide(windows[0], 1920, 1080).Should().BeTrue();
        var primaryTileCount = windows[0].TiledBounds.Count;
        var partnerTileCount = windows[1].TiledBounds.Count;

        registry.ResetSideBySidePair(windows[2], 1920, 1080).Should().BeFalse();

        windows[0].TiledBounds.Should().HaveCount(primaryTileCount);
        windows[1].TiledBounds.Should().HaveCount(partnerTileCount);
        windows[2].TiledBounds.Should().BeEmpty();
    }

    /// <summary>No-regression sibling: with no active side-by-side pair, Reset Window Position is a no-op.</summary>
    [Fact]
    public void ResetSideBySidePair_WhenNotActive_ReturnsFalseAndTouchesNoWindow()
    {
        var (registry, windows) = RegisterWindows(2);

        registry.IsSideBySideActive.Should().BeFalse();
        registry.ResetSideBySidePair(windows[0], 1920, 1080).Should().BeFalse();

        windows[0].TiledBounds.Should().BeEmpty();
        windows[1].TiledBounds.Should().BeEmpty();
    }
}
