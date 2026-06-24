namespace Free.Shared.Theme.Tests;

/// <summary>Unit tests for <see cref="ThemeColor"/> parsing and round-tripping.</summary>
public sealed class ThemeColorTests
{
    [Fact]
    public void FromHex_RRGGBB_SetsAlpha255()
    {
        var c = ThemeColor.FromHex("#0F6D8C");
        c.A.Should().Be(255);
        c.R.Should().Be(0x0F);
        c.G.Should().Be(0x6D);
        c.B.Should().Be(0x8C);
    }

    [Fact]
    public void FromHex_AARRGGBB_ParsesAlpha()
    {
        var c = ThemeColor.FromHex("#55FFFFFF");
        c.A.Should().Be(0x55);
        c.R.Should().Be(0xFF);
        c.G.Should().Be(0xFF);
        c.B.Should().Be(0xFF);
    }

    [Fact]
    public void ToHex_OpaqueColor_ReturnsRRGGBB()
    {
        var c = ThemeColor.FromHex("#0F6D8C");
        c.ToHex().Should().Be("#0F6D8C");
    }

    [Fact]
    public void ToHex_TranslucentColor_ReturnsAARRGGBB()
    {
        var c = ThemeColor.FromHex("#55FFFFFF");
        c.ToHex().Should().Be("#55FFFFFF");
    }

    [Fact]
    public void RoundTrip_Alpha_55FFFFFF()
    {
        const string hex = "#55FFFFFF";
        ThemeColor.FromHex(hex).ToHex().Should().Be(hex);
    }

    [Fact]
    public void RoundTrip_Opaque_0F6D8C()
    {
        const string hex = "#0F6D8C";
        ThemeColor.FromHex(hex).ToHex().Should().Be(hex);
    }

    [Theory]
    [InlineData("BADFORMAT")]
    [InlineData("#12345")]
    [InlineData("#123456789")]
    public void FromHex_InvalidFormat_Throws(string bad)
    {
        var act = () => ThemeColor.FromHex(bad);
        act.Should().Throw<FormatException>();
    }
}
