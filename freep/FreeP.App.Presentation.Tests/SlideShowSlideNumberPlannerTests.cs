using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowSlideNumberPlannerTests
{
    [Theory]
    [InlineData("D0", '0')]
    [InlineData("NumPad4", '4')]
    [InlineData("digit9", '9')]
    public void TryGetDigitAcceptsDesktopAndKeypadNames(string keyName, char expected)
    {
        SlideShowSlideNumberPlanner.TryGetDigit(keyName, out var digit).Should().BeTrue();
        digit.Should().Be(expected);
    }

    [Fact]
    public void AppendDigitBoundsThePendingEntry()
    {
        var value = string.Empty;
        foreach (var digit in "12345")
            value = SlideShowSlideNumberPlanner.AppendDigit(value, digit);

        value.Should().Be("1234");
    }

    [Theory]
    [InlineData("1", 1, true)]
    [InlineData("004", 4, true)]
    [InlineData("0", 0, false)]
    [InlineData("abc", 0, false)]
    public void TryParseSlideNumberRequiresPositiveOneBasedValue(
        string buffer,
        int expected,
        bool valid)
    {
        SlideShowSlideNumberPlanner.TryParseSlideNumber(buffer, out var number).Should().Be(valid);
        number.Should().Be(expected);
    }

    [Fact]
    public void PlanSlideNumberJumpUsesOneBasedInputAndRejectsOutOfRange()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Add(new Slide { Title = "Two" });
        presentation.Slides.Add(new Slide { Title = "Three" });
        var controller = new SlideShowController(presentation.Slides, 0);

        var jump = SlideShowHostPlanner.PlanSlideNumberJump(controller, presentation.Slides, 3);
        jump.Kind.Should().Be(SlideShowHostCommandKind.NavigateToSlide);
        controller.CurrentSlideIndex.Should().Be(2);

        var invalid = SlideShowHostPlanner.PlanSlideNumberJump(controller, presentation.Slides, 4);
        invalid.Kind.Should().Be(SlideShowHostCommandKind.None);
        controller.CurrentSlideIndex.Should().Be(2);
    }

    [Fact]
    public void PlanSlideNumberJumpUsesDeckNumberWhenHiddenSlidesAreFilteredFromRoute()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Add(new Slide { Title = "Hidden", IsHidden = true });
        presentation.Slides.Add(new Slide { Title = "Visible three" });
        var visibleSlides = presentation.Slides.Where(slide => !slide.IsHidden).ToArray();
        var sourceIndices = presentation.Slides
            .Select((slide, index) => (slide, index))
            .Where(entry => !entry.slide.IsHidden)
            .Select(entry => entry.index)
            .ToArray();
        var controller = new SlideShowController(visibleSlides, 0);

        var jump = SlideShowHostPlanner.PlanSlideNumberJump(
            controller,
            visibleSlides,
            3,
            sourceIndices);

        jump.Kind.Should().Be(SlideShowHostCommandKind.NavigateToSlide);
        controller.CurrentSlideIndex.Should().Be(1);
        controller.CurrentSlide!.Title.Should().Be("Visible three");

        var hidden = SlideShowHostPlanner.PlanSlideNumberJump(
            controller,
            visibleSlides,
            2,
            sourceIndices);
        hidden.Kind.Should().Be(SlideShowHostCommandKind.None);
        controller.CurrentSlideIndex.Should().Be(1);
    }
}
