using System.Collections.Generic;
using System.Windows;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookWindowRegistrySideBySideTests
{
    private sealed class FakeWindow : IWorkbookWindow
    {
        public WorkbookScrollOffset Offset { get; set; }
        public int SetScrollOffsetCount { get; private set; }
        public readonly List<Rect> TiledBounds = [];

        public void ApplyWindowTitleSuffix(string suffix) { }
        public void RefreshFromSharedWorkbook() { }
        public void ActivateWindow() { }
        public void SetWindowVisible(bool visible) { }

        public WorkbookScrollOffset GetScrollOffset() => Offset;

        public void SetScrollOffset(WorkbookScrollOffset offset)
        {
            Offset = offset;
            SetScrollOffsetCount++;
        }

        public void TileToWorkArea(Rect bounds) => TiledBounds.Add(bounds);
    }

    private static (WorkbookWindowRegistry Registry, FakeWindow[] Windows) RegisterWindows(int count)
    {
        var registry = new WorkbookWindowRegistry();
        var windows = new FakeWindow[count];
        for (var i = 0; i < count; i++)
        {
            windows[i] = new FakeWindow();
            registry.Register(windows[i]);
        }

        return (registry, windows);
    }

    [Fact]
    public void SideBySide_IsInactiveByDefault()
    {
        var (registry, _) = RegisterWindows(2);

        registry.IsSideBySideActive.Should().BeFalse();
        registry.IsSynchronousScrollActive.Should().BeFalse();
    }

    [Fact]
    public void EnableSideBySide_TilesTheActiveWindowAndItsPartnerAndBecomesActive()
    {
        var (registry, windows) = RegisterWindows(2);

        registry.EnableSideBySide(windows[0], workAreaWidth: 1920, workAreaHeight: 1080).Should().BeTrue();

        registry.IsSideBySideActive.Should().BeTrue();
        windows[0].TiledBounds.Should().ContainSingle();
        windows[1].TiledBounds.Should().ContainSingle();
        windows[0].TiledBounds[0].Left.Should().Be(0);
        windows[1].TiledBounds[0].Left.Should().BeApproximately(960, 0.001);
    }

    [Fact]
    public void EnableSideBySide_WithNoOtherVisibleWindow_IsRefused()
    {
        var (registry, windows) = RegisterWindows(1);

        registry.EnableSideBySide(windows[0], 1920, 1080).Should().BeFalse();
        registry.IsSideBySideActive.Should().BeFalse();
        windows[0].TiledBounds.Should().BeEmpty();
    }

    [Fact]
    public void DisableSideBySide_LeavesWindowsWhereTheyAreAndAlsoStopsSynchronousScrolling()
    {
        var (registry, windows) = RegisterWindows(2);
        registry.EnableSideBySide(windows[0], 1920, 1080);
        registry.SetSynchronousScroll(true);

        registry.DisableSideBySide();

        registry.IsSideBySideActive.Should().BeFalse();
        registry.IsSynchronousScrollActive.Should().BeFalse();
        // Disabling is a no-op layout-wise: no extra tiling calls beyond the original enable.
        windows[0].TiledBounds.Should().ContainSingle();
        windows[1].TiledBounds.Should().ContainSingle();
    }

    [Fact]
    public void SynchronousScroll_CannotBeEnabledWhenSideBySideIsInactive()
    {
        var (registry, _) = RegisterWindows(2);

        registry.SetSynchronousScroll(true).Should().BeFalse();
        registry.IsSynchronousScrollActive.Should().BeFalse();
    }

    [Fact]
    public void SynchronousScroll_CanBeEnabledOnceSideBySideIsActive()
    {
        var (registry, windows) = RegisterWindows(2);
        registry.EnableSideBySide(windows[0], 1920, 1080);

        registry.SetSynchronousScroll(true).Should().BeTrue();
        registry.IsSynchronousScrollActive.Should().BeTrue();
    }

    [Fact]
    public void BroadcastScrollOffset_AppliesTheOffsetToThePairedWindowOnly()
    {
        var (registry, windows) = RegisterWindows(2);
        registry.EnableSideBySide(windows[0], 1920, 1080);
        registry.SetSynchronousScroll(true);

        var offset = new WorkbookScrollOffset(12, 5);
        registry.BroadcastScrollOffset(windows[0], offset);

        windows[1].Offset.Should().Be(offset);
        windows[1].SetScrollOffsetCount.Should().Be(1);
        windows[0].SetScrollOffsetCount.Should().Be(0, "the origin window already has the offset");
    }

    [Fact]
    public void BroadcastScrollOffset_FromThePairedWindow_AppliesBackToThePrimary()
    {
        var (registry, windows) = RegisterWindows(2);
        registry.EnableSideBySide(windows[0], 1920, 1080);
        registry.SetSynchronousScroll(true);

        var offset = new WorkbookScrollOffset(3, 7);
        registry.BroadcastScrollOffset(windows[1], offset);

        windows[0].Offset.Should().Be(offset);
        windows[0].SetScrollOffsetCount.Should().Be(1);
    }

    [Fact]
    public void BroadcastScrollOffset_DoesNotFeedBackIntoTheOriginWindow()
    {
        var (registry, windows) = RegisterWindows(2);
        registry.EnableSideBySide(windows[0], 1920, 1080);
        registry.SetSynchronousScroll(true);

        // The partner re-broadcasts while applying; the guard must keep this from looping back.
        registry.BroadcastScrollOffset(windows[0], new WorkbookScrollOffset(1, 1));
        registry.BroadcastScrollOffset(windows[1], windows[1].Offset);

        windows[0].SetScrollOffsetCount.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void BroadcastScrollOffset_IsIgnoredWhenSynchronousScrollIsOff()
    {
        var (registry, windows) = RegisterWindows(2);
        registry.EnableSideBySide(windows[0], 1920, 1080);

        registry.BroadcastScrollOffset(windows[0], new WorkbookScrollOffset(9, 9));

        windows[1].SetScrollOffsetCount.Should().Be(0);
    }

    [Fact]
    public void Unregister_TheSideBySidePartner_DeactivatesSideBySide()
    {
        var (registry, windows) = RegisterWindows(2);
        registry.EnableSideBySide(windows[0], 1920, 1080);

        registry.Unregister(windows[1]);

        registry.IsSideBySideActive.Should().BeFalse();
        registry.IsSynchronousScrollActive.Should().BeFalse();
    }
}
