using Free.Shared.Ribbon.KeyTips;

namespace Free.Shared.Ribbon.Tests.KeyTips;

public sealed class RibbonKeyTipTextTests
{
    [Theory]
    [InlineData(" h ", "H")]
    [InlineData("fx", "FX")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void Normalize_TrimsUppercasesAndNullsBlankText(string input, string? expected) =>
        RibbonKeyTipText.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData("HG", "H", "G")]
    [InlineData("L", "H", "L")]
    [InlineData(" hG ", " h ", "G")]
    [InlineData("H", "H", "H")]
    public void ApplyScopePrefix_StripsOnlyLongerMatchingPrefixes(
        string keyTip,
        string scopePrefix,
        string expected) =>
        RibbonKeyTipText.ApplyScopePrefix(keyTip, scopePrefix).Should().Be(expected);

    [Fact]
    public void CreateUniqueKeyTip_PrefersAccessKeyMarkerBeforeHeaderCharacters()
    {
        var keyTip = RibbonKeyTipText.CreateUniqueKeyTip("Save _As", ["S"]);

        keyTip.Should().Be("A");
    }

    [Fact]
    public void CreateUniqueKeyTip_AvoidsPrefixCollisions()
    {
        var keyTip = RibbonKeyTipText.CreateUniqueKeyTip("Clear", ["C", "CL"]);

        keyTip.Should().Be("L");
    }

    [Fact]
    public void CreateUniqueKeyTip_FallsBackAfterSingleCharactersAndNumbers()
    {
        var used = "ABCDEFGHIJKLMNOPQRSTUVWXYZ"
            .Select(character => character.ToString())
            .Concat(Enumerable.Range(1, 9).Select(number => number.ToString()))
            .ToArray();

        RibbonKeyTipText.CreateUniqueKeyTip("!!!", used).Should().Be("0A");
    }

    [Theory]
    [InlineData("A", true)]
    [InlineData("1C", true)]
    [InlineData("É", false)]
    [InlineData("", false)]
    public void IsTypeableKeyTip_AcceptsKeyboardTokensOnly(string keyTip, bool expected) =>
        RibbonKeyTipText.IsTypeableKeyTip(keyTip).Should().Be(expected);
}
