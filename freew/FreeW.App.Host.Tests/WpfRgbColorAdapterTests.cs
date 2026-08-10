using System.Windows.Media;
using FreeW.App.Host.Editing;

namespace FreeW.App.Host.Tests;

public sealed class WpfRgbColorAdapterTests
{
    [Theory]
    [InlineData("0A141E")]
    [InlineData("#0A141E")]
    [InlineData("  #0a141e  ")]
    public void DrawingMlProfileUsesSharedSixDigitRgbGrammar(string token)
    {
        WpfRgbColorAdapter.TryParseDrawingMl(token, out var color).Should().BeTrue();

        color.Should().Be(Color.FromRgb(0x0A, 0x14, 0x1E));
    }

    [Fact]
    public void NativeFallbackPreservesNamedAndArgbWpfTokens()
    {
        WpfRgbColorAdapter.TryParseColorToken("CornflowerBlue", out var named).Should().BeTrue();
        WpfRgbColorAdapter.TryParseColorToken("#800A141E", out var argb).Should().BeTrue();

        named.Should().Be(Colors.CornflowerBlue);
        argb.Should().Be(Color.FromArgb(0x80, 0x0A, 0x14, 0x1E));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#GGHHII")]
    [InlineData("#12345")]
    public void MalformedRgbTokensAreRejected(string? token)
    {
        WpfRgbColorAdapter.TryParseColorToken(token, out _).Should().BeFalse();
    }

    [Fact]
    public void StrictDrawingMlParserDoesNotAcceptWpfSpecificTokens()
    {
        WpfRgbColorAdapter.TryParseDrawingMl("Red", out _).Should().BeFalse();
        WpfRgbColorAdapter.TryParseDrawingMl("#800A141E", out _).Should().BeFalse();
    }

    [Fact]
    public void WpfColorFormattingDelegatesToTheSharedRgbValue()
    {
        WpfRgbColorAdapter.ToHexRgb(Color.FromArgb(0x80, 0x0A, 0x14, 0x1E))
            .Should().Be("#0A141E");
    }
}
