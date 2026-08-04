using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class BackstageVisualContractTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ShellHeadlessApp).Assembly);

    [Fact]
    public async Task Avalonia_backstage_chrome_uses_the_neutral_pane_metrics_and_theme()
    {
        await Session.Dispatch(() =>
        {
            var style = AvaloniaBackstageChromeStyle.FromContract();
            var heading = AvaloniaBackstageChrome.CreateHeading("Heading", style);
            var section = AvaloniaBackstageChrome.CreateSectionHeader("Section", style);
            var detail = AvaloniaBackstageChrome.CreateDetailGrid();
            AvaloniaBackstageChrome.AddDetailRow(detail, "Label", "Value", "ValueId", style);

            heading.FontSize.Should().Be(BackstageVisualContract.Pane.HeadingFontSize);
            heading.Margin.Should().Be(ToThickness(BackstageVisualContract.Pane.HeadingMargin));
            ((SolidColorBrush)heading.Foreground!).Color.Should().Be(ToColor(BackstageVisualContract.Theme.PrimaryText));
            section.FontSize.Should().Be(BackstageVisualContract.Pane.SectionHeaderFontSize);
            section.Margin.Should().Be(ToThickness(BackstageVisualContract.Pane.SectionHeaderMargin));
            detail.Margin.Should().Be(ToThickness(BackstageVisualContract.Pane.DetailGridMargin));
            detail.ColumnDefinitions[0].Width.Value.Should().Be(BackstageVisualContract.Pane.DetailLabelColumnWidth);
            detail.Children.OfType<TextBlock>().Should().AllSatisfy(text =>
            {
                text.FontSize.Should().Be(BackstageVisualContract.Pane.DetailFontSize);
            });
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Avalonia_backstage_frame_uses_the_neutral_navigation_geometry()
    {
        await Session.Dispatch(() =>
        {
            var frame = new AvaloniaBackstageFrame(
                new AvaloniaBackstageAccent(
                    Colors.Black,
                    Colors.Gray,
                    Colors.Blue,
                    Colors.White),
                Array.Empty<SisterBackstageEntryPlan<Control>>());

            var layout = Assert.IsType<Grid>(frame.Content);
            var rail = Assert.IsType<DockPanel>(layout.Children[0]);
            var bottomNav = Assert.IsType<StackPanel>(rail.Children[1]);
            var contentArea = Assert.IsType<Border>(layout.Children[1]);
            var scroll = Assert.IsType<ScrollViewer>(contentArea.Child);

            layout.ColumnDefinitions[0].Width.Value.Should().Be(BackstageVisualContract.Frame.RailWidth);
            bottomNav.Margin.Should().Be(ToThickness(BackstageVisualContract.Frame.BottomNavigationMargin));
            scroll.Padding.Should().Be(ToThickness(BackstageVisualContract.Frame.ContentPadding));
        }, CancellationToken.None);
    }

    private static Thickness ToThickness(BackstageVisualThickness thickness) =>
        new(thickness.Left, thickness.Top, thickness.Right, thickness.Bottom);

    private static Color ToColor(BackstageVisualColor color) => Color.FromRgb(color.Red, color.Green, color.Blue);
}
