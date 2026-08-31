using System.Windows;
using FluentAssertions;
using FreeX.App.Host;

namespace FreeX.App.Host.Tests;

public sealed class RibbonTopLevelTabKeyTipVisibilityTests
{
    [Theory]
    [InlineData(Visibility.Visible, 80, 24, true)]
    [InlineData(Visibility.Collapsed, 80, 24, false)]
    [InlineData(Visibility.Hidden, 80, 24, false)]
    [InlineData(Visibility.Visible, 0, 24, false)]
    [InlineData(Visibility.Visible, 80, 0, false)]
    public void IsVisibleTopLevelTabKeyTip_UsesTabHeaderVisibilityAndLayoutBounds(
        Visibility visibility,
        double actualWidth,
        double actualHeight,
        bool expected)
    {
        MainWindow.IsVisibleTopLevelTabKeyTip(visibility, actualWidth, actualHeight)
            .Should()
            .Be(expected);
    }
}
