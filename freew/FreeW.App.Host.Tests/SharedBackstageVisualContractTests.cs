using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;

namespace FreeW.App.Host.Tests;

public sealed class SharedBackstageVisualContractTests
{
    [StaFact]
    public void Wpf_backstage_kit_uses_the_neutral_pane_metrics_and_theme()
    {
        var kit = new BackstageVisualKit(Color.FromRgb(0x0F, 0x6D, 0x8C), 150, 190);
        var heading = kit.HeadingText("Heading");
        var section = kit.SubHeading("Section");
        var field = Assert.IsType<Grid>(kit.Field("Label", "Value"));

        heading.FontSize.Should().Be(BackstageVisualContract.Pane.HeadingFontSize);
        heading.Margin.Should().Be(ToThickness(BackstageVisualContract.Pane.HeadingMargin));
        ((SolidColorBrush)heading.Foreground).Color.Should().Be(ToColor(BackstageVisualContract.Theme.PrimaryText));
        section.FontSize.Should().Be(BackstageVisualContract.Pane.SectionHeaderFontSize);
        section.Margin.Should().Be(ToThickness(BackstageVisualContract.Pane.SectionHeaderMargin));
        field.Margin.Should().Be(ToThickness(BackstageVisualContract.Pane.DetailGridMargin));
        field.ColumnDefinitions[0].Width.Value.Should().Be(BackstageVisualContract.Pane.DetailLabelColumnWidth);
        field.Children.OfType<TextBlock>().Should().AllSatisfy(text =>
        {
            text.FontSize.Should().Be(BackstageVisualContract.Pane.DetailFontSize);
        });
    }

    [StaFact]
    public void Wpf_frame_uses_the_neutral_navigation_geometry()
    {
        var frame = new BackstageFrame();
        var layout = Assert.IsType<Grid>(frame.Content);
        var rail = Assert.IsType<Border>(layout.Children[0]);
        var railDock = Assert.IsType<DockPanel>(rail.Child);
        var bottomNav = Assert.IsType<StackPanel>(railDock.Children[1]);
        var content = Assert.IsType<System.Windows.Controls.ContentControl>(layout.Children[1]);

        layout.ColumnDefinitions[0].Width.Value.Should().Be(BackstageVisualContract.Frame.RailWidth);
        bottomNav.Margin.Should().Be(ToThickness(BackstageVisualContract.Frame.BottomNavigationMargin));
        content.Margin.Should().Be(ToThickness(BackstageVisualContract.Frame.ContentPadding));
    }

    private static Thickness ToThickness(BackstageVisualThickness thickness) =>
        new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);

    private static Color ToColor(BackstageVisualColor color) => Color.FromRgb(color.Red, color.Green, color.Blue);
}
