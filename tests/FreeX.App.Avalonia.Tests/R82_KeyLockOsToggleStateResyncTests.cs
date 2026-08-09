using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Input;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression coverage for R82-meta-3: the Avalonia CAPS LOCK/NUM LOCK status-bar indicators tracked
/// toggle state purely by flipping a bool on each CapsLock/NumLock KeyDown that reached this window --
/// <c>_isCapsLockToggleOnForShell</c>/<c>_isNumLockToggleOnForShell</c> started at their default `false`
/// with no initialization from the real OS toggle state, so a physical Caps Lock already on BEFORE this
/// window ever existed (or toggled while some other window had focus) left the indicator wrongly hidden
/// even though the real key was on. <see cref="MainWindow.ResyncKeyLockToggleStateFromOs"/> now
/// initializes/resyncs the tracked state from an OS query (see <see
/// cref="MainWindow.TryGetOsKeyToggleState"/>) both at construction and on every window Activated. These
/// tests drive that resync via <see cref="MainWindow.KeyLockOsToggleStateOverrideForTest"/> -- a
/// deterministic stand-in for the real OS query -- rather than depending on the actual keyboard LED
/// state of whatever machine runs the test.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R82_KeyLockOsToggleStateResyncTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task Construction_ResyncsCapsLockIndicator_FromOsToggleState_WithoutAnyKeyDown()
    {
        await Session.Dispatch(() =>
        {
            // Simulates "Caps Lock was already toggled on before this window (or the whole process)
            // ever existed" -- there is no KeyDown for the constructor's resync to observe here at all.
            MainWindow.KeyLockOsToggleStateOverrideForTest = key => key == Key.CapsLock ? true : (bool?)null;
            try
            {
                var window = new MainWindow([]);
                try
                {
                    // Failing before the fix: the constructor never queried any OS toggle state, so
                    // _isCapsLockToggleOnForShell stayed at its default false and this was hidden.
                    window.IsCapsLockIndicatorVisibleForTest.Should().BeTrue(
                        "the constructor must resync the CAPS LOCK indicator from the real OS toggle " +
                        "state so a key already toggled on before launch is reflected immediately");
                    window.IsNumLockIndicatorVisibleForTest.Should().BeFalse(
                        "the OS query only reported CapsLock on -- NumLock must stay independently hidden");
                }
                finally
                {
                    window.AllowCloseWithoutDirtyPromptForParityCapture();

                    window.Close();
                }
            }
            finally
            {
                MainWindow.KeyLockOsToggleStateOverrideForTest = null;
            }

            return true;
        }, CancellationToken.None);
    }

    // No-regression sibling: when no OS query is available for a key (the override returns null, the
    // same signal the real non-Windows fallback produces), resync must leave whatever was already
    // tracked untouched rather than clobbering it back to hidden/false.
    [Fact]
    public async Task Resync_LeavesTrackedState_UnchangedWhenOsQueryUnavailable()
    {
        await Session.Dispatch(async () =>
        {
            // Deterministic from construction onward, regardless of the real machine's actual CapsLock
            // state: the constructor's own resync must not interfere with the KeyDown-driven toggle
            // this test exercises below.
            MainWindow.KeyLockOsToggleStateOverrideForTest = _ => null;
            var window = new MainWindow([]);
            try
            {
                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.CapsLock });
                window.IsCapsLockIndicatorVisibleForTest.Should().BeTrue(
                    "a real CapsLock key-down must still toggle the tracked state as before");

                MainWindow.KeyLockOsToggleStateOverrideForTest = _ => null;
                window.ResyncKeyLockToggleStateFromOsForTest();

                window.IsCapsLockIndicatorVisibleForTest.Should().BeTrue(
                    "resync must leave the previously key-down-tracked state untouched when no OS " +
                    "query is available for a key, not silently reset it back to hidden");
            }
            finally
            {
                MainWindow.KeyLockOsToggleStateOverrideForTest = null;

                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }
}
