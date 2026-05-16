using System.Windows.Input;
using FluentAssertions;
using Freexcel.App.Host;

namespace Freexcel.App.Host.Tests;

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
}
