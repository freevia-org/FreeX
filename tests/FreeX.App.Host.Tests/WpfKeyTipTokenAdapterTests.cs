using System.Windows.Input;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class WpfKeyTipTokenAdapterTests
{
    [Theory]
    [InlineData(Key.D1, "1")]
    [InlineData(Key.NumPad3, "3")]
    [InlineData(Key.Y, "Y")]
    public void NativeAdapter_NormalizesLetterAndDigitKeyTips(Key key, string expected)
    {
        MainWindow.ToWpfKeyTipToken(key).Should().Be(expected);
    }

    [Theory]
    [InlineData(Key.Space)]
    [InlineData(Key.Tab)]
    [InlineData(Key.OemPlus)]
    [InlineData(Key.Escape)]
    public void NativeAdapter_RejectsNonLetterDigitKeys(Key key)
    {
        MainWindow.ToWpfKeyTipToken(key).Should().BeNull();
    }
}
