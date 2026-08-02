using FreeP.App.Compositor;
using FreeP.App.Host;
using FreeP.Core.Model;

namespace FreeP.App.Host.Tests;

public sealed class SlideShowScreenModeTests
{
    private static Presentation MakePresentation(int slideCount)
    {
        var presentation = Presentation.CreateEmpty();
        for (var i = 1; i < slideCount; i++)
            presentation.Slides.Add(new Slide { Title = $"Slide {i + 1}" });
        return presentation;
    }

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

    [StaFact]
    public void WpfHost_CanJumpToOneBasedSlideNumber()
    {
        var window = new SlideShowWindow(MakePresentation(3), startIndex: 0);

        window.ExecuteSlideNumberJump(3);

        window.Controller.CurrentSlideIndex.Should().Be(2);
    }

    [StaFact]
    public void WpfHost_NumericJumpUsesDeckNumberWhenHiddenSlideIsSkipped()
    {
        var presentation = MakePresentation(3);
        presentation.Slides[1].IsHidden = true;
        var window = new SlideShowWindow(presentation, startIndex: 0);

        window.ExecuteSlideNumberJump(3);

        window.Controller.CurrentSlideIndex.Should().Be(1);
        window.Controller.CurrentSlide!.Title.Should().Be("Slide 3");
    }

    [StaFact]
    public void WpfHost_CanRevealNextHiddenSlideWithoutChangingPlaybackIndex()
    {
        var presentation = MakePresentation(3);
        presentation.Slides[1].IsHidden = true;
        var window = new SlideShowWindow(presentation, startIndex: 0);

        window.ExecuteHiddenSlideReveal()!.Title.Should().Be("Slide 2");
        window.Controller.CurrentSlideIndex.Should().Be(0);
    }
}
