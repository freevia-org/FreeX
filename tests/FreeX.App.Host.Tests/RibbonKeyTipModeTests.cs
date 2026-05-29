using System.Windows.Input;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class RibbonKeyTipModeTests
{
    [Fact]
    public void HandleTopLevelKey_AfterAltMode_ProducesKeyTipTokenAndExitsMode()
    {
        var mode = new RibbonKeyTipMode();
        mode.Enter();

        var result = mode.HandleTopLevelKey(Key.H);

        result.Handled.Should().BeTrue();
        result.KeyTip.Should().Be("H");
        result.Canceled.Should().BeFalse();
        mode.IsActive.Should().BeFalse();
    }

    [Fact]
    public void HandleTopLevelKey_Escape_CancelsMode()
    {
        var mode = new RibbonKeyTipMode();
        mode.Enter();

        var result = mode.HandleTopLevelKey(Key.Escape);

        result.Handled.Should().BeTrue();
        result.Canceled.Should().BeTrue();
        result.KeyTip.Should().BeNull();
        mode.IsActive.Should().BeFalse();
    }

    [Fact]
    public void HandleTopLevelKey_IgnoresUnsupportedKeyAndKeepsModeActive()
    {
        var mode = new RibbonKeyTipMode();
        mode.Enter();

        var result = mode.HandleTopLevelKey(Key.Tab);

        result.Should().Be(RibbonKeyTipModeResult.Ignored);
        mode.IsActive.Should().BeTrue("unsupported keys should not consume a pending standalone Alt keytip mode");
    }

    [Theory]
    [InlineData(Key.D1, "1")]
    [InlineData(Key.NumPad3, "3")]
    [InlineData(Key.Y, "Y")]
    public void HandleTopLevelKey_NormalizesLetterAndDigitKeyTips(Key key, string expected)
    {
        var mode = new RibbonKeyTipMode();
        mode.Enter();

        mode.HandleTopLevelKey(key).KeyTip.Should().Be(expected);
    }

    [Theory]
    [InlineData(Key.Space)]
    [InlineData(Key.Tab)]
    [InlineData(Key.OemPlus)]
    [InlineData(Key.Escape)]
    public void ToKeyTipToken_RejectsNonLetterDigitKeys(Key key)
    {
        RibbonKeyTipMode.ToKeyTipToken(key).Should().BeNull();
    }
}
