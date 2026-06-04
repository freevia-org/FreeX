using System.Windows;
using FluentAssertions;
using FreeX.App.Host;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookWindowRegistryArrangeAllTests
{
    private static (WorkbookWindowRegistry Registry, TestWorkbookWindow[] Windows) RegisterWindows(int count)
    {
        var registry = new WorkbookWindowRegistry();
        var windows = new TestWorkbookWindow[count];
        for (var i = 0; i < count; i++)
        {
            windows[i] = new TestWorkbookWindow();
            registry.Register(windows[i]);
        }

        return (registry, windows);
    }

    [Fact]
    public void ArrangeVisibleWindows_AppliesTheChosenLayoutToVisibleWindowsInRegistrationOrder()
    {
        var (registry, windows) = RegisterWindows(3);

        registry.ArrangeVisibleWindows(WorkbookWindowArrangement.Vertical, 900, 600).Should().BeTrue();

        windows[0].ArrangedBounds.Should().Equal(new Rect(0, 0, 300, 600));
        windows[1].ArrangedBounds.Should().Equal(new Rect(300, 0, 300, 600));
        windows[2].ArrangedBounds.Should().Equal(new Rect(600, 0, 300, 600));
    }

    [Fact]
    public void ArrangeVisibleWindows_LeavesHiddenWindowsUntouched()
    {
        var (registry, windows) = RegisterWindows(3);
        registry.Hide(windows[1]).Should().BeTrue();

        registry.ArrangeVisibleWindows(WorkbookWindowArrangement.Horizontal, 900, 600).Should().BeTrue();

        windows[0].ArrangedBounds.Should().Equal(new Rect(0, 0, 900, 300));
        windows[1].ArrangedBounds.Should().BeEmpty();
        windows[2].ArrangedBounds.Should().Equal(new Rect(0, 300, 900, 300));
        windows[1].IsWindowVisible.Should().BeFalse();
    }

    [Fact]
    public void ArrangeVisibleWindows_DisablesSideBySideAndSynchronousScrolling()
    {
        var (registry, windows) = RegisterWindows(2);
        registry.EnableSideBySide(windows[0], 900, 600).Should().BeTrue();
        registry.SetSynchronousScroll(true).Should().BeTrue();

        registry.ArrangeVisibleWindows(WorkbookWindowArrangement.Tiled, 900, 600).Should().BeTrue();

        registry.IsSideBySideActive.Should().BeFalse();
        registry.IsSynchronousScrollActive.Should().BeFalse();
        registry.BroadcastScrollOffset(windows[0], new WorkbookScrollOffset(5, 5));
        windows[1].SetScrollOffsetCount.Should().Be(0);
    }

    [Fact]
    public void ArrangeVisibleWindows_WithNoWindowsOrInvalidArrangement_IsRefused()
    {
        var registry = new WorkbookWindowRegistry();

        registry.ArrangeVisibleWindows(WorkbookWindowArrangement.Tiled, 900, 600).Should().BeFalse();

        var (registeredRegistry, windows) = RegisterWindows(1);
        registeredRegistry.ArrangeVisibleWindows((WorkbookWindowArrangement)99, 900, 600).Should().BeFalse();
        windows[0].ArrangedBounds.Should().BeEmpty();
    }
}
