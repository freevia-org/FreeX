using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWReadModePlannerTests
{
    [Theory]
    [InlineData("narrow", 560)]
    [InlineData("default", 760)]
    [InlineData("wide", 1024)]
    [InlineData("unknown", 760)]
    public void ColumnWidth_UsesSharedReadModeAuthority(string token, double expected)
    {
        FreeWReadModePlanner.ColumnWidth(token).Should().Be(expected);
    }

    [Theory]
    [InlineData("none", "#FFFFFF")]
    [InlineData("sepia", "#F0E0C0")]
    [InlineData("inverse", "#1E1E1E")]
    [InlineData("unknown", "#FFFFFF")]
    public void PageColorHex_UsesSharedReadModeAuthority(string token, string expected)
    {
        FreeWReadModePlanner.PageColorHex(token).Should().Be(expected);
    }

    [Fact]
    public void NormalizeTokens_RejectsUnknownValues()
    {
        FreeWReadModePlanner.NormalizeColumnWidth("wide").Should().Be(FreeWReadModePlanner.WideColumn);
        FreeWReadModePlanner.NormalizeColumnWidth("bogus").Should().Be(FreeWReadModePlanner.DefaultColumn);
        FreeWReadModePlanner.NormalizePageColor("inverse").Should().Be(FreeWReadModePlanner.InverseColor);
        FreeWReadModePlanner.NormalizePageColor("bogus").Should().Be(FreeWReadModePlanner.NoColor);
    }
}
