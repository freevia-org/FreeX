using System.Threading;
using Avalonia.Headless;
using Avalonia.Input;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class MacOsKeyLockStateTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public void NativeCapsLockQuery_IsAvailableOnMacOs()
    {
        // Exercise the real framework export and ABI without changing the user's keyboard
        // state or requiring Accessibility/Input Monitoring permission.
        MainWindow.TryGetMacOsCapsLockToggleState().HasValue.Should().Be(OperatingSystem.IsMacOS());
    }

    [Fact]
    public Task StatusRefresh_TracksNativeChangesWithoutCapsLockKeyDown() =>
        Run(async window =>
        {
            var capsLockOn = false;
            MainWindow.KeyLockOsToggleStateOverrideForTest = key => key == Key.CapsLock ? capsLockOn : null;

            // macOS Caps Lock changes can arrive as flagsChanged rather than KeyDown. Refresh
            // must query the current state rather than depend on having seen a toggle key.
            capsLockOn = true;
            window.RefreshShellForTest();
            window.IsCapsLockIndicatorVisibleForTest.Should().BeTrue();
            window.IsNumLockIndicatorVisibleForTest.Should().BeFalse();

            // A native Caps Lock query must not erase independently tracked Num Lock state
            // on platforms where no Num Lock query exists.
            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.NumLock });
            window.IsNumLockIndicatorVisibleForTest.Should().BeTrue();

            capsLockOn = false;
            window.RefreshShellForTest();
            window.IsCapsLockIndicatorVisibleForTest.Should().BeFalse();
            window.IsNumLockIndicatorVisibleForTest.Should().BeTrue();
        });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task CapsLockKeyDown_DoesNotInvertAuthoritativeNativeState(bool capsLockOn) =>
        Run(async window =>
        {
            MainWindow.KeyLockOsToggleStateOverrideForTest = key => key == Key.CapsLock ? capsLockOn : null;
            window.ResyncKeyLockToggleStateFromOsForTest();

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.CapsLock });
            window.IsCapsLockIndicatorVisibleForTest.Should().Be(capsLockOn);
            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.CapsLock });
            window.IsCapsLockIndicatorVisibleForTest.Should().Be(capsLockOn);
        });

    private static Task Run(Func<MainWindow, Task> action) =>
        Session.Dispatch(async () =>
        {
            MainWindow.KeyLockOsToggleStateOverrideForTest = _ => null;
            MainWindow? window = null;
            try
            {
                window = new MainWindow([]);
                await action(window);
            }
            finally
            {
                if (window is not null)
                {
                    window.AllowCloseWithoutDirtyPromptForParityCapture();
                    window.Close();
                }

                MainWindow.KeyLockOsToggleStateOverrideForTest = null;
            }
        }, CancellationToken.None);
}
