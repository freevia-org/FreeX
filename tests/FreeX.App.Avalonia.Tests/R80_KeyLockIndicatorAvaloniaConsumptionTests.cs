using System.Threading;
using System.Threading.Tasks;

using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// R80-meta-3: the r79 Caps Lock / Num Lock status-bar indicators (KeyLockIndicatorPlanner +
/// StatusBar_CapsLock/StatusBar_NumLock) were wired into the WPF host's XAML
/// (StatusCapsLockText/StatusNumLockText) but had no Avalonia equivalent at all -- the shared
/// planner was consumed by exactly one shell. These tests drive the real Avalonia consumption path
/// (MainWindow.KeyLock.cs, wired from BuildStatusBar/MainWindow_KeyDownAsync/RefreshShell) via the
/// production KeyDown handler seam, asserting on the indicator TextBlocks' live visibility rather
/// than a source-string proxy.
///
/// R82-meta-3 added an OS-toggle-state resync (see MainWindow.KeyLock.cs) that runs at construction,
/// so these tests -- which want to isolate the pure key-down-tracked behavior these were written to
/// cover -- force <see cref="MainWindow.KeyLockOsToggleStateOverrideForTest"/> to null-for-every-key
/// (the "no OS query available" signal) before constructing each window. Without this, a test run on
/// a machine whose real Num Lock happens to be physically on would see the resync pick that up and
/// the NUM LOCK assertions below would flake against real hardware state instead of testing the
/// key-tracked toggle logic these tests target.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class R80_KeyLockIndicatorAvaloniaConsumptionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task NewWindow_KeyLockIndicatorsAreHiddenByDefault()
    {
        await Session.Dispatch(() =>
        {
            MainWindow.KeyLockOsToggleStateOverrideForTest = _ => null;
            try
            {
                var window = new MainWindow([]);

                window.IsCapsLockIndicatorVisibleForTest.Should().BeFalse(
                    "no key has been toggled yet, so the CAPS LOCK indicator must start hidden");
                window.IsNumLockIndicatorVisibleForTest.Should().BeFalse(
                    "no key has been toggled yet, so the NUM LOCK indicator must start hidden");

                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
            finally
            {
                MainWindow.KeyLockOsToggleStateOverrideForTest = null;
            }

            return true;
        }, CancellationToken.None);
    }

    [Fact]
    public async Task CapsLockKeyDown_TogglesCapsLockIndicatorVisibility_WithoutAffectingNumLock()
    {
        await Session.Dispatch(async () =>
        {
            MainWindow.KeyLockOsToggleStateOverrideForTest = _ => null;
            try
            {
                var window = new MainWindow([]);

                // Failing before the fix: the Avalonia shell had no StatusCapsLockText-equivalent control
                // and no KeyLockIndicatorPlanner consumption at all, so this property did not exist.
                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.CapsLock });
                window.IsCapsLockIndicatorVisibleForTest.Should().BeTrue(
                    "toggling Caps Lock on must reveal the status-bar indicator, matching the WPF host");
                window.IsNumLockIndicatorVisibleForTest.Should().BeFalse(
                    "toggling Caps Lock must not affect the independent NUM LOCK indicator");

                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.CapsLock });
                window.IsCapsLockIndicatorVisibleForTest.Should().BeFalse(
                    "a second Caps Lock key-down toggles the physical key back off, so the indicator must hide again");

                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
            finally
            {
                MainWindow.KeyLockOsToggleStateOverrideForTest = null;
            }

            return true;
        }, CancellationToken.None);
    }

    // No-regression sibling: an unrelated key must never flip either indicator.
    [Fact]
    public async Task UnrelatedKeyDown_DoesNotAffectKeyLockIndicators()
    {
        await Session.Dispatch(async () =>
        {
            MainWindow.KeyLockOsToggleStateOverrideForTest = _ => null;
            try
            {
                var window = new MainWindow([]);
                var sheet = window.Session.Workbook.AddSheet("CleanFixture");
                window.Session.SelectSheet(sheet.Id);

                await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.A });

                window.IsCapsLockIndicatorVisibleForTest.Should().BeFalse(
                    "an ordinary letter key-down must not toggle the CAPS LOCK indicator");
                window.IsNumLockIndicatorVisibleForTest.Should().BeFalse(
                    "an ordinary letter key-down must not toggle the NUM LOCK indicator");

                window.AllowCloseWithoutDirtyPromptForParityCapture();

                window.Close();
            }
            finally
            {
                MainWindow.KeyLockOsToggleStateOverrideForTest = null;
            }

            return true;
        }, CancellationToken.None);
    }
}
