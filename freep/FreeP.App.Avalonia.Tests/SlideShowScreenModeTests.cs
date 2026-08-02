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

    private static Presentation MakePresentation(int slideCount)
    {
        var presentation = Presentation.CreateEmpty();
        for (var i = 1; i < slideCount; i++)
            presentation.Slides.Add(new Slide { Title = $"Slide {i + 1}" });
        return presentation;
    }

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

    [Fact]
    public async Task AvaloniaHost_CanJumpToOneBasedSlideNumber()
    {
        var index = -1;
        await Session.Dispatch(() =>
        {
            var window = new SlideShowWindow(MakePresentation(3), startIndex: 0);
            window.ExecuteSlideNumberJump(3);
            index = window.Controller.CurrentSlideIndex;
        }, CancellationToken.None);

        index.Should().Be(2);
    }

    [Fact]
    public async Task AvaloniaHost_NumericJumpUsesDeckNumberWhenHiddenSlideIsSkipped()
    {
        var index = -1;
        var title = string.Empty;
        await Session.Dispatch(() =>
        {
            var presentation = MakePresentation(3);
            presentation.Slides[1].IsHidden = true;
            var window = new SlideShowWindow(presentation, startIndex: 0);
            window.ExecuteSlideNumberJump(3);
            index = window.Controller.CurrentSlideIndex;
            title = window.Controller.CurrentSlide?.Title ?? string.Empty;
        }, CancellationToken.None);

        index.Should().Be(1);
        title.Should().Be("Slide 3");
    }
}
