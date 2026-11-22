using System.Threading;

using Avalonia.Headless;
using Avalonia.Input;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// WPF StandaloneAltKeyTipTracker parity for the Avalonia shell. On Windows the key tips toggle when a
/// bare Alt is RELEASED with no other key in between; on macOS the same gesture is a bare Option tap,
/// so Option+Arrow word navigation, Option+click and Option+letter never flash or strand key tips.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class StandaloneAltKeyTipReleaseParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task AltPress_DoesNotShowKeyTipsUntilRelease_AndSecondTapHidesThem() => Run(async window =>
    {
        await Down(window, Key.LeftAlt, KeyModifiers.Alt);
        window.RibbonKeyTipsVisibleForTest.Should().BeFalse();

        window.RaiseKeyUpForTest(new KeyEventArgs { Key = Key.LeftAlt });
        window.RibbonKeyTipsVisibleForTest.Should().BeTrue();

        await Down(window, Key.RightAlt, KeyModifiers.Alt);
        window.RibbonKeyTipsVisibleForTest.Should().BeTrue("mode is still on until the release");
        window.RaiseKeyUpForTest(new KeyEventArgs { Key = Key.RightAlt });
        window.RibbonKeyTipsVisibleForTest.Should().BeFalse();
    });

    [Theory]
    [InlineData(Key.Down)]
    [InlineData(Key.Left)]
    [InlineData(Key.Right)]
    [InlineData(Key.Back)]
    public Task OptionPlusNonKeyTipKey_NeverShowsKeyTips(Key key) => Run(async window =>
    {
        await Down(window, Key.LeftAlt, KeyModifiers.Alt);
        await Down(window, key, KeyModifiers.Alt);
        window.RaiseKeyUpForTest(new KeyEventArgs { Key = key, KeyModifiers = KeyModifiers.Alt });
        window.RaiseKeyUpForTest(new KeyEventArgs { Key = Key.LeftAlt });

        window.RibbonKeyTipsVisibleForTest.Should().BeFalse();
        window.RibbonKeyTipInputForTest.Should().BeEmpty();
    });

    [Fact]
    public Task AltWithOtherModifier_IsNotAStandaloneCandidate() => Run(async window =>
    {
        await Down(window, Key.LeftAlt, KeyModifiers.Alt | KeyModifiers.Shift);
        window.RaiseKeyUpForTest(new KeyEventArgs { Key = Key.LeftAlt });
        window.RibbonKeyTipsVisibleForTest.Should().BeFalse();

        await Down(window, Key.LeftAlt, KeyModifiers.Alt | KeyModifiers.Meta);
        window.RaiseKeyUpForTest(new KeyEventArgs { Key = Key.LeftAlt });
        window.RibbonKeyTipsVisibleForTest.Should().BeFalse();
    });

    [Fact]
    public Task AltChordLetter_RoutesToTabWithoutLeavingTheStandaloneCandidateArmed() => Run(async window =>
    {
        await Down(window, Key.LeftAlt, KeyModifiers.Alt);
        var h = await Down(window, Key.H, KeyModifiers.Alt);
        h.Handled.Should().BeTrue();
        window.RibbonKeyTipInputForTest.Should().Be("H");

        // The H press cancelled the standalone candidate, so releasing Alt must not toggle anything.
        window.RaiseKeyUpForTest(new KeyEventArgs { Key = Key.LeftAlt });
        window.RibbonKeyTipInputForTest.Should().Be("H");
    });

    [Fact]
    public Task EscapeLeavesKeyTipMode_AndDeactivationExitsIt() => Run(async window =>
    {
        await Tap(window);
        window.RibbonKeyTipsVisibleForTest.Should().BeTrue();
        var esc = await Down(window, Key.Escape, KeyModifiers.None);
        esc.Handled.Should().BeTrue();
        window.RibbonKeyTipsVisibleForTest.Should().BeFalse();

        await Tap(window);
        window.RibbonKeyTipsVisibleForTest.Should().BeTrue();
        window.RaiseDeactivatedForTest();
        window.RibbonKeyTipsVisibleForTest.Should().BeFalse();

        // A pending Alt press must not survive losing focus (Cmd+Tab while holding Option).
        await Down(window, Key.LeftAlt, KeyModifiers.Alt);
        window.RaiseDeactivatedForTest();
        window.RaiseKeyUpForTest(new KeyEventArgs { Key = Key.LeftAlt });
        window.RibbonKeyTipsVisibleForTest.Should().BeFalse();
    });

    [Fact]
    public Task TapAltThenTabKey_ThenCommandKeyTip_FollowsWpfScopes() => Run(async window =>
    {
        await Tap(window);
        (await Down(window, Key.H, KeyModifiers.None)).Handled.Should().BeTrue();
        window.RibbonKeyTipsVisibleForTest.Should().BeFalse("WPF shows the command-level tips after a tab key");

        // Alt tap while the session is active exits instead of re-entering (WPF ExitRibbonKeyTipMode).
        await Tap(window);
        window.RibbonKeyTipInputForTest.Should().BeEmpty();
        window.RibbonKeyTipsVisibleForTest.Should().BeFalse();
    });

    private static async Task Tap(MainWindow window)
    {
        await Down(window, Key.LeftAlt, KeyModifiers.Alt);
        window.RaiseKeyUpForTest(new KeyEventArgs { Key = Key.LeftAlt });
    }

    private static async Task<KeyEventArgs> Down(MainWindow window, Key key, KeyModifiers modifiers)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = modifiers };
        await window.RaiseKeyDownForTest(args);
        return args;
    }

    private static Task Run(Func<MainWindow, Task> test) =>
        Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            try
            {
                await test(window);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
}
