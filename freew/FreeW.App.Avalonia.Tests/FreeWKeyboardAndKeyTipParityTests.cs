using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using FreeW.App.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class FreeWKeyboardAndKeyTipParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task StandaloneAltTogglesKeyTipsAndEscapeDismissesThem()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                Press(window, Key.LeftAlt).Handled.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTest.Should().BeTrue();

                Press(window, Key.LeftAlt).Handled.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTest.Should().BeFalse();

                Press(window, Key.RightAlt).Handled.Should().BeTrue();
                Press(window, Key.Escape).Handled.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTest.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task StandaloneF10UsesTheSameKeyTipModeAsAlt()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                Press(window, Key.F10).Handled.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTest.Should().BeTrue();

                Press(window, Key.Escape).Handled.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTest.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ShiftF10RemainsAvailableToTheContextMenuRoute()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var shortcut = Press(window, Key.F10, KeyModifiers.Shift);

                shortcut.Handled.Should().BeFalse();
                window.RibbonKeyTipsVisibleForTest.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task AvaloniaShellRendersOneAuthoritativeFileKeyTip()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var tabs = window.RibbonControlForTest.Should().BeOfType<TabControl>().Subject;
                tabs.Items.OfType<TabItem>().Count(item => item.Tag?.ToString() == "FileTab")
                    .Should().Be(1);
                tabs.Items.OfType<TabItem>().Count(item => item.Tag?.ToString() == "file")
                    .Should().Be(0);

                Press(window, Key.LeftAlt).Handled.Should().BeTrue();
                Press(window, Key.F).Handled.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTest.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task VisibleKeyTipActivatesMatchingTopLevelTab()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                Press(window, Key.LeftAlt);
                var activation = Press(window, Key.N);

                activation.Handled.Should().BeTrue();
                window.RibbonKeyTipsVisibleForTest.Should().BeFalse();
                var tabs = window.RibbonControlForTest.Should().BeOfType<TabControl>().Subject;
                ((TabItem)tabs.SelectedItem!).Tag.Should().Be("insert");
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task UnknownTopLevelKeyLeavesKeyTipsVisible()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                Press(window, Key.LeftAlt);
                var unknown = Press(window, Key.Q);

                unknown.Handled.Should().BeFalse();
                window.RibbonKeyTipsVisibleForTest.Should().BeTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task ShiftF1ReachesRevealFormattingWithoutControl()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                window.RevealPane.IsVisible.Should().BeFalse();

                var shortcut = Press(window, Key.F1, KeyModifiers.Shift);

                shortcut.Handled.Should().BeTrue();
                window.RevealPane.IsVisible.Should().BeTrue();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Theory]
    [InlineData(KeyModifiers.Control)]
    [InlineData(KeyModifiers.Meta)]
    public async Task SelectAllUsesTheSharedControlGestureOnDesktopPlatforms(KeyModifiers modifier)
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                window.Editor.SelectedText.Should().BeEmpty();

                var shortcut = Press(window, Key.A, modifier);

                shortcut.Handled.Should().BeTrue();
                window.Editor.SelectedText.Should().NotBeEmpty();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CtrlShiftZIsNotClaimedWhenWpfDoesNotMapIt()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                Press(window, Key.Z, KeyModifiers.Control | KeyModifiers.Shift)
                    .Handled.Should().BeFalse();
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    private static KeyEventArgs Press(
        MainWindow window,
        Key key,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = modifiers };
        window.RaiseKeyDownForTest(args);
        return args;
    }
}
