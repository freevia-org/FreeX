namespace FreeW.Core.Model.Tests;

public class PasteTextTests
{
    [Fact]
    public void Normalize_ConvertsCrlfToLf()
    {
        PasteText.Normalize("a\r\nb\r\nc").Should().Be("a\nb\nc");
    }

    [Fact]
    public void Normalize_ConvertsLoneCrToLf()
    {
        PasteText.Normalize("a\rb\rc").Should().Be("a\nb\nc");
    }

    [Fact]
    public void Normalize_LeavesLfUnchanged()
    {
        PasteText.Normalize("a\nb\nc").Should().Be("a\nb\nc");
    }

    [Fact]
    public void Normalize_MixedLineEndingsAllCollapseToLf()
    {
        PasteText.Normalize("a\r\nb\rc\nd").Should().Be("a\nb\nc\nd");
    }

    [Fact]
    public void Normalize_StripsControlCharacters()
    {
        // NUL, bell, vertical tab, form feed, escape, DEL should all be removed.
        PasteText.Normalize("a\0b\ac\vd\fefg").Should().Be("abcdefg");
    }

    [Fact]
    public void Normalize_PreservesTabsAndNewlines()
    {
        PasteText.Normalize("col1\tcol2\nrow2").Should().Be("col1\tcol2\nrow2");
    }

    [Fact]
    public void Normalize_PreservesTrailingWhitespacePerLine()
    {
        PasteText.Normalize("a  \r\nb\t").Should().Be("a  \nb\t");
    }

    [Fact]
    public void Normalize_EmptyInputReturnsEmpty()
    {
        PasteText.Normalize(string.Empty).Should().Be(string.Empty);
    }

    [Fact]
    public void Normalize_NullInputReturnsEmpty()
    {
        PasteText.Normalize(null).Should().Be(string.Empty);
    }

    [Fact]
    public void Normalize_PlainTextPassesThroughUnchanged()
    {
        PasteText.Normalize("Hello, world!").Should().Be("Hello, world!");
    }
}
