using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class WorkbookWindowRegistryVisibilityTests
{
    private sealed class FakeWindow : IWorkbookWindow
    {
        public bool IsWindowVisible { get; private set; } = true;
        public int SetVisibleTrueCount { get; private set; }
        public int SetVisibleFalseCount { get; private set; }
        public int ActivateCount { get; private set; }

        public void ApplyWindowTitleSuffix(string suffix) { }
        public void RefreshFromSharedWorkbook() { }
        public void ActivateWindow() => ActivateCount++;

        public void SetWindowVisible(bool visible)
        {
            IsWindowVisible = visible;
            if (visible)
                SetVisibleTrueCount++;
            else
                SetVisibleFalseCount++;
        }

        public WorkbookScrollOffset GetScrollOffset() => default;
        public void SetScrollOffset(WorkbookScrollOffset offset) { }
        public void TileToWorkArea(System.Windows.Rect bounds) { }
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
    public void NewlyRegisteredWindows_AreAllVisible()
    {
        var (registry, windows) = RegisterWindows(2);

        registry.VisibleCount.Should().Be(2);
        registry.HiddenWindows.Should().BeEmpty();
        registry.IsVisible(windows[0]).Should().BeTrue();
        registry.IsVisible(windows[1]).Should().BeTrue();
    }

    [Fact]
    public void CanHide_IsFalse_WhenItIsTheOnlyVisibleWindow()
    {
        var (registry, windows) = RegisterWindows(1);

        registry.CanHide(windows[0]).Should().BeFalse();
    }

    [Fact]
    public void CanHide_IsTrue_WhenAnotherWindowRemainsVisible()
    {
        var (registry, windows) = RegisterWindows(2);

        registry.CanHide(windows[0]).Should().BeTrue();
        registry.CanHide(windows[1]).Should().BeTrue();
    }

    [Fact]
    public void Hide_HidesTheWindow_AndUpdatesVisibilityBookkeeping()
    {
        var (registry, windows) = RegisterWindows(2);

        registry.Hide(windows[0]).Should().BeTrue();

        windows[0].IsWindowVisible.Should().BeFalse();
        windows[0].SetVisibleFalseCount.Should().Be(1);
        registry.VisibleCount.Should().Be(1);
        registry.IsVisible(windows[0]).Should().BeFalse();
        registry.HiddenWindows.Should().Equal(windows[0]);
    }

    [Fact]
    public void Hide_TheLastVisibleWindow_IsRefused()
    {
        var (registry, windows) = RegisterWindows(2);
        registry.Hide(windows[0]);

        registry.CanHide(windows[1]).Should().BeFalse();
        registry.Hide(windows[1]).Should().BeFalse();

        windows[1].IsWindowVisible.Should().BeTrue();
        windows[1].SetVisibleFalseCount.Should().Be(0);
        registry.VisibleCount.Should().Be(1);
    }

    [Fact]
    public void Hide_AnAlreadyHiddenWindow_IsANoOp()
    {
        var (registry, windows) = RegisterWindows(3);
        registry.Hide(windows[0]);

        registry.Hide(windows[0]).Should().BeFalse();

        registry.HiddenWindows.Should().Equal(windows[0]);
        windows[0].SetVisibleFalseCount.Should().Be(1);
    }

    [Fact]
    public void Unhide_RestoresTheWindow_AndActivatesIt()
    {
        var (registry, windows) = RegisterWindows(2);
        registry.Hide(windows[0]);

        registry.Unhide(windows[0]).Should().BeTrue();

        windows[0].IsWindowVisible.Should().BeTrue();
        windows[0].SetVisibleTrueCount.Should().Be(1);
        windows[0].ActivateCount.Should().Be(1);
        registry.VisibleCount.Should().Be(2);
        registry.HiddenWindows.Should().BeEmpty();
    }

    [Fact]
    public void Unhide_AVisibleWindow_IsANoOp()
    {
        var (registry, windows) = RegisterWindows(2);

        registry.Unhide(windows[0]).Should().BeFalse();

        windows[0].SetVisibleTrueCount.Should().Be(0);
        windows[0].ActivateCount.Should().Be(0);
    }

    [Fact]
    public void HiddenWindows_ReportsHiddenWindowsInRegistrationOrder()
    {
        var (registry, windows) = RegisterWindows(3);

        registry.Hide(windows[2]);
        registry.Hide(windows[0]);

        registry.HiddenWindows.Should().Equal(windows[0], windows[2]);
        registry.VisibleCount.Should().Be(1);
    }

    [Fact]
    public void Unregister_AHiddenWindow_DropsItFromTheHiddenList()
    {
        var (registry, windows) = RegisterWindows(2);
        registry.Hide(windows[0]);

        registry.Unregister(windows[0]);

        registry.HiddenWindows.Should().BeEmpty();
        registry.VisibleCount.Should().Be(1);
        registry.Count.Should().Be(1);
    }

    [Fact]
    public void CanHide_IsFalse_WhenWindowIsNotRegistered()
    {
        var registry = new WorkbookWindowRegistry();
        var stranger = new FakeWindow();

        registry.CanHide(stranger).Should().BeFalse();
    }
}
