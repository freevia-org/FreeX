using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Regression: a second MainWindow (View > New Window) re-pointed the application-wide Dock menu at its own
/// NativeMenu, which AppKit's native menu proxy rejects with "The menu being updated does not match".
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class NativeDockMenuSingleInstallTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task SecondWindowMenu_DoesNotReplaceTheApplicationDockMenu()
    {
        await Session.Dispatch(() =>
        {
            var app = Application.Current!;
            var previous = NativeDock.GetMenu(app);
            NativeDock.SetMenu(app, null);
            try
            {
                var first = new NativeMenu();
                var second = new NativeMenu();

                MainWindow.TryInstallNativeDockMenu(app, first).Should().BeTrue();
                MainWindow.TryInstallNativeDockMenu(app, second).Should().BeFalse();

                NativeDock.GetMenu(app).Should().BeSameAs(first);
            }
            finally
            {
                NativeDock.SetMenu(app, previous);
            }
        }, CancellationToken.None);
    }
}
