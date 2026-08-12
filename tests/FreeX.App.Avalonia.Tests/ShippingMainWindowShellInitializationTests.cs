extern alias ProductionAvalonia;

using System.Threading;
using Avalonia.Headless;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class ShippingMainWindowShellInitializationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task ShippingMainWindow_ConstructsItsProductionShellWithoutCaptureConstants()
    {
        await Session.Dispatch(() =>
        {
            var window = new ProductionAvalonia::FreeX.App.Avalonia.MainWindow([]);

            window.Content.Should().NotBeNull();
            window.Width.Should().Be(1120);
            window.Height.Should().Be(720);
            window.MinWidth.Should().Be(820);
            window.MinHeight.Should().Be(520);

            window.AllowCloseWithoutDirtyPromptForParityCapture();
            window.Close();
        }, CancellationToken.None);
    }
}
