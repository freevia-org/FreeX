using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Free.Shared.Ribbon.Avalonia;

namespace Free.Shared.Ribbon.Tests;

public sealed class RibbonTabChromeParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task Avalonia_tab_chrome_uses_the_shared_header_geometry()
    {
        await Session.Dispatch(() =>
        {
            var definition = new RibbonDefinitionBuilder()
                .Tab("home", "Home", "H", tab => tab.Group("group", "Group", "G", 1, group =>
                    group.Button("command", "Command")))
                .Build();
            var ribbon = AvaloniaRibbonRenderer.BuildRibbon(definition);
            var window = new Window { Width = 640, Height = 220, Content = ribbon };
            try
            {
                window.Show();
                window.Measure(new Size(640, 220));
                window.Arrange(new Rect(0, 0, 640, 220));
                window.UpdateLayout();

                var tabs = Assert.IsType<TabControl>(ribbon);
                tabs.Items.OfType<TabItem>().Should().NotBeEmpty();
                foreach (var tab in tabs.Items.OfType<TabItem>())
                {
                    tab.Height.Should().Be(RibbonTabChromeMetrics.HeaderHeight);
                    var header = Assert.IsType<Grid>(tab.Header);
                    header.Height.Should().Be(RibbonTabChromeMetrics.HeaderHeight);
                    header.Margin.Should().Be(new Thickness(0, 0, RibbonTabChromeMetrics.InterTabGap, 0));
                    var label = header.Children.OfType<TextBlock>().Single();
                    label.FontSize.Should().Be(RibbonTabChromeMetrics.FontSize);
                    label.Margin.Should().Be(new Thickness(
                        RibbonTabChromeMetrics.HeaderHorizontalPadding,
                        RibbonTabChromeMetrics.HeaderVerticalPadding,
                        RibbonTabChromeMetrics.HeaderHorizontalPadding,
                        RibbonTabChromeMetrics.HeaderVerticalPadding));

                    var underline = header.Children.OfType<Border>()
                        .Single(border => Equals(border.Tag, "FreeX.SelectedTabUnderline"));
                    underline.Height.Should().Be(RibbonTabChromeMetrics.SelectedUnderlineThickness);
                    underline.MinHeight.Should().Be(RibbonTabChromeMetrics.SelectedUnderlineThickness);
                }
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }
}
