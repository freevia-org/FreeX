using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class SlideShowScreenModeTests
{
    [StaFact]
    public void WpfHost_BlankScreenModeCanBeChangedAndRestored()
    {
        var window = new SlideShowWindow(Presentation.CreateEmpty());

        window.ScreenMode.Should().Be(SlideShowScreenMode.Normal);
        window.SetScreenMode(SlideShowScreenMode.Black);
        window.ScreenMode.Should().Be(SlideShowScreenMode.Black);
        window.SetScreenMode(SlideShowScreenMode.White);
        window.ScreenMode.Should().Be(SlideShowScreenMode.White);
        window.SetScreenMode(SlideShowScreenMode.Normal);
        window.ScreenMode.Should().Be(SlideShowScreenMode.Normal);
    }
}
