using FluentAssertions;
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.Model;

namespace FreeP.App.Rendering.Avalonia.Tests;

/// <summary>
/// r199: <see cref="SlideCanvas"/> carried a settable <c>SlideIndex</c> alongside <c>Slide</c>, and
/// composition trusted it. SlideShowWindow assigns <c>Slide</c> at twenty-odd navigation sites and
/// <c>SlideIndex</c> at none, so a running presentation on the Avalonia shell resolved every
/// <c>slidenum</c> field as "1". The editing canvas and the PDF/print exporter both set the index,
/// and the WPF twin derives it from the deck and so could never go stale -- this shell's live show
/// was the one surface that lied.
/// </summary>
public sealed class R199_SlideNumberFieldIndexTests
{
    private static (SlideCanvas Canvas, Presentation Deck) DeckOfThree()
    {
        var deck = new Presentation();
        for (var i = 0; i < 3; i++)
            deck.Slides.Add(new Slide());

        return (new SlideCanvas { Presentation = deck }, deck);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SettingOnlyTheSlide_StillResolvesThatSlidesOwnIndex(int index)
    {
        var (canvas, deck) = DeckOfThree();

        // Exactly what SlideShowWindow does on every navigation: assign Slide, nothing else.
        canvas.Slide = deck.Slides[index];

        canvas.ResolveSlideIndex().Should().Be(index);
    }

    [Fact]
    public void AStaleSlideIndexDoesNotOverrideTheSlideActuallyShown()
    {
        var (canvas, deck) = DeckOfThree();
        canvas.Slide = deck.Slides[0];
        canvas.SlideIndex = 0;

        canvas.Slide = deck.Slides[2];

        canvas.ResolveSlideIndex().Should().Be(2, "the deck, not the last index anyone remembered to set");
    }

    [Fact]
    public void ASlideOutsideTheDeckFallsBackToTheSuppliedIndex()
    {
        // The control: thumbnails and previews of a detached slide are the one case the deck cannot
        // answer, and they are why the property still exists.
        var (canvas, _) = DeckOfThree();
        canvas.Slide = new Slide();
        canvas.SlideIndex = 7;

        canvas.ResolveSlideIndex().Should().Be(7);
    }
}
