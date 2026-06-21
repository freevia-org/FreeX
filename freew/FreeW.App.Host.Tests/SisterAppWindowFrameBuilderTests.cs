using System.Windows;
using System.Windows.Controls;
using System.Linq;
using Free.Shared.Ribbon.Wpf;
using Xunit;

namespace FreeW.App.Host.Tests;

public sealed class SisterAppWindowFrameBuilderTests
{
    [StaFact]
    public void Build_ComposesTitleBarAboveBodyAndBackstageOverlay()
    {
        var titleBar = new Border();
        var body = new Grid();
        var backstage = new UserControl();

        var result = SisterAppWindowFrameBuilder.Build(new SisterAppWindowFrameSpec(titleBar, body, backstage));

        result.Root.RowDefinitions.Should().HaveCount(2);
        result.Root.RowDefinitions[0].Height.Should().Be(GridLength.Auto);
        result.Root.RowDefinitions[1].Height.Should().Be(new GridLength(1, GridUnitType.Star));
        result.Root.Children.Cast<UIElement>().Should().Equal(titleBar, result.BelowTitle);
        Grid.GetRow(titleBar).Should().Be(0);
        Grid.GetRow(result.BelowTitle).Should().Be(1);

        result.BelowTitle.Children.Cast<UIElement>().Should().Equal(body, backstage);
    }
}
