namespace FreeW.Core.IO.Tests;

public sealed class WordHighlightColorCodecTests
{
    public static TheoryData<string, string> CanonicalColors => new()
    {
        { "yellow", "#FFFF00" },
        { "green", "#00FF00" },
        { "cyan", "#00FFFF" },
        { "magenta", "#FF00FF" },
        { "blue", "#0000FF" },
        { "red", "#FF0000" },
        { "darkBlue", "#000080" },
        { "darkCyan", "#008080" },
        { "darkGreen", "#008000" },
        { "darkMagenta", "#800080" },
        { "darkRed", "#800000" },
        { "darkYellow", "#808000" },
        { "darkGray", "#808080" },
        { "lightGray", "#C0C0C0" },
        { "black", "#000000" },
        { "white", "#FFFFFF" },
    };

    [Theory]
    [MemberData(nameof(CanonicalColors))]
    public void ToHex_MapsEveryWordTokenToCanonicalUppercaseHex(string token, string hex)
    {
        WordHighlightColorCodec.ToHex(token).Should().Be(hex);
    }

    [Theory]
    [MemberData(nameof(CanonicalColors))]
    public void ToToken_MapsHashBareLowercaseAndRepeatedHashAliasesToCanonicalToken(string token, string hex)
    {
        WordHighlightColorCodec.ToToken(hex).Should().Be(token);
        WordHighlightColorCodec.ToToken(hex.TrimStart('#')).Should().Be(token);
        WordHighlightColorCodec.ToToken(hex.ToLowerInvariant()).Should().Be(token);
        WordHighlightColorCodec.ToToken("##" + hex.TrimStart('#')).Should().Be(token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("none")]
    [InlineData("auto")]
    [InlineData("Yellow")]
    [InlineData("yellow ")]
    [InlineData("unknown")]
    public void ToHex_PreservesCaseSensitiveUnknownNoneAndAutoBehavior(string? token)
    {
        WordHighlightColorCodec.ToHex(token).Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("auto")]
    [InlineData("none")]
    [InlineData("#123456")]
    [InlineData(" #FFFF00")]
    [InlineData("#FFFF00 ")]
    public void ToToken_PreservesUnknownAndWhitespaceBehavior(string? hex)
    {
        WordHighlightColorCodec.ToToken(hex).Should().BeNull();
    }
}
