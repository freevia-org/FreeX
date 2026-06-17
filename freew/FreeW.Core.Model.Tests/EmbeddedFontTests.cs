namespace FreeW.Core.Model.Tests;

/// <summary>
/// Unit coverage for the <see cref="EmbeddedFont"/> model and the <see cref="TextDocument.EmbeddedFonts"/>
/// opt-in (roadmap item F3). Each style is optional; the default document embeds no fonts.
/// </summary>
public class EmbeddedFontTests
{
    [Fact]
    public void NewDocument_HasNoEmbeddedFonts()
    {
        var doc = new TextDocument();

        doc.EmbeddedFonts.Should().BeEmpty();
    }

    [Fact]
    public void EmbeddedFont_DefaultsAllStylesToNull()
    {
        var font = new EmbeddedFont("Demo Sans");

        font.Family.Should().Be("Demo Sans");
        font.Regular.Should().BeNull();
        font.Bold.Should().BeNull();
        font.Italic.Should().BeNull();
        font.BoldItalic.Should().BeNull();
        font.HasAnyStyle.Should().BeFalse();
    }

    [Fact]
    public void HasAnyStyle_IsTrue_WhenAnyStyleCarriesBytes()
    {
        new EmbeddedFont("A", Regular: [1, 2, 3]).HasAnyStyle.Should().BeTrue();
        new EmbeddedFont("A", Bold: [1]).HasAnyStyle.Should().BeTrue();
        new EmbeddedFont("A", Italic: [1]).HasAnyStyle.Should().BeTrue();
        new EmbeddedFont("A", BoldItalic: [1]).HasAnyStyle.Should().BeTrue();
    }

    [Fact]
    public void HasAnyStyle_IsFalse_WhenAllStylesAreNullOrEmpty()
    {
        new EmbeddedFont("A").HasAnyStyle.Should().BeFalse();
        new EmbeddedFont("A", Regular: []).HasAnyStyle.Should().BeFalse();
    }

    [Fact]
    public void EmbeddedFonts_CanBePopulated()
    {
        var doc = new TextDocument();
        var font = new EmbeddedFont("Mono", Regular: [10, 20, 30]);

        doc.EmbeddedFonts.Add(font);

        doc.EmbeddedFonts.Should().ContainSingle().Which.Should().Be(font);
    }

    [Fact]
    public void EmbeddedFont_IsValueEqualByFamilyAndBytesReference()
    {
        byte[] bytes = [1, 2, 3];
        var a = new EmbeddedFont("X", Regular: bytes);
        var b = new EmbeddedFont("X", Regular: bytes);

        // Records compare by member; the byte[] is reference-compared, so sharing the array is equal.
        a.Should().Be(b);
    }
}
