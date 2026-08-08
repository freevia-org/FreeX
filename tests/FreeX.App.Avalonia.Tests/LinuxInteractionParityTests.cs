using System.Threading;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class LinuxInteractionParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task WindowUsesCanonicalIconAndNameBoxTextPadding()
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);

            window.HasWindowIconForTest.Should().BeTrue();
            window.CellAddressPaddingForTest.Should().Be(new Thickness(4, 0, 0, 0));

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task BackstageOverlayOpensInPlaceAndEscapeClosesIt()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);

            window.ShowBackstageOverlayForTest();
            window.IsBackstageOverlayVisibleForTest.Should().BeTrue();

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.Escape });
            window.IsBackstageOverlayVisibleForTest.Should().BeFalse();

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task StandaloneAltTogglesTopLevelRibbonKeyTips()
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.LeftAlt });
            window.RibbonKeyTipsVisibleForTest.Should().BeTrue();

            await window.RaiseKeyDownForTest(new KeyEventArgs { Key = Key.LeftAlt });
            window.RibbonKeyTipsVisibleForTest.Should().BeFalse();

            window.AllowCloseWithoutDirtyPromptForParityCapture();

            window.Close();
        }, CancellationToken.None);
    }
}
