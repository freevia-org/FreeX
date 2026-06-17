namespace FreeW.Core.Model.Tests;

public class WordArtTests
{
    [Fact]
    public void Create_CarriesTextStyleAndFontSize()
    {
        var wordArt = WordArt.Create("Decorative", WordArtStyle.GradientFill, fontSizePt: 48);

        wordArt.Text.Should().Be("Decorative");
        wordArt.Style.Should().Be(WordArtStyle.GradientFill);
        wordArt.FontSizePt.Should().Be(48);
    }

    [Fact]
    public void Create_DefaultsToFillBlueAndHeadingSize()
    {
        var wordArt = WordArt.Create("Title");

        wordArt.Style.Should().Be(WordArtStyle.FillBlue);
        wordArt.FontSizePt.Should().Be(36);
    }

    [Fact]
    public void FromWordArt_MirrorsTextAsRunFallback()
    {
        var run = Run.FromWordArt(WordArt.Create("Banner", WordArtStyle.Shadow));

        run.WordArt.Should().NotBeNull();
        run.WordArt!.Style.Should().Be(WordArtStyle.Shadow);
        run.Text.Should().Be("Banner");
    }
}
