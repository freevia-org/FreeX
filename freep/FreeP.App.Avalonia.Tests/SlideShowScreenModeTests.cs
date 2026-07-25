using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless;
using FreeP.App.Avalonia;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia.Tests;

public sealed class SlideShowScreenModeTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreePHeadlessApp).Assembly);

    [Fact]
    public async Task AvaloniaHost_BlankScreenModeCanBeChangedAndRestored()
    {
        SlideShowScreenMode black = SlideShowScreenMode.Normal;
        SlideShowScreenMode white = SlideShowScreenMode.Normal;
        SlideShowScreenMode normal = SlideShowScreenMode.Black;
        await Session.Dispatch(() =>
        {
            var window = new SlideShowWindow(Presentation.CreateEmpty());
            window.SetScreenMode(SlideShowScreenMode.Black);
            black = window.ScreenMode;
            window.SetScreenMode(SlideShowScreenMode.White);
            white = window.ScreenMode;
            window.SetScreenMode(SlideShowScreenMode.Normal);
            normal = window.ScreenMode;
        }, CancellationToken.None);

        black.Should().Be(SlideShowScreenMode.Black);
        white.Should().Be(SlideShowScreenMode.White);
        normal.Should().Be(SlideShowScreenMode.Normal);
    }
}
