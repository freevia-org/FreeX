using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Free.Shared.Shell.Avalonia;
using FreeW.Core.Model;

namespace FreeW.App.Avalonia.Tests;

public sealed class PageSetupDialogVisualParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Theory]
    [InlineData(PageSetupDialogTab.Margins, 0)]
    [InlineData(PageSetupDialogTab.Paper, 1)]
    [InlineData(PageSetupDialogTab.Layout, 2)]
    public async Task Uses_Wpf_page_setup_metrics_and_preserves_initial_tab(
        PageSetupDialogTab initialTab,
        int expectedTab)
    {
        await Session.Dispatch(() =>
        {
            var dialog = new PageSetupDialog(new PageSettings(), initialTab);
            try
            {
                dialog.Width.Should().Be(420);
                dialog.FontFamily.Should().Be(AvaloniaCompactDialogChrome.WindowsUiFontFamily);
                dialog.FontSize.Should().Be(12);

                var tabs = dialog.GetLogicalDescendants().OfType<TabControl>().Single();
                tabs.SelectedIndex.Should().Be(expectedTab);
                tabs.Items.OfType<TabItem>().Select(item => item.Header?.ToString())
                    .Should().Equal("Margins", "Paper", "Layout");

                var grids = dialog.GetLogicalDescendants().OfType<Grid>().ToArray();
                grids.Should().NotBeEmpty();
                grids.Where(grid => grid.ColumnDefinitions.Count == 2)
                    .Should().OnlyContain(grid =>
                        grid.ColumnDefinitions[0].Width == GridLength.Auto
                        && grid.ColumnDefinitions[1].Width == new GridLength(1, GridUnitType.Star));

                dialog.GetLogicalDescendants().OfType<TextBox>()
                    .Should().OnlyContain(box => box.MinWidth == 120);
                dialog.GetLogicalDescendants().OfType<ComboBox>()
                    .Should().OnlyContain(combo => combo.MinWidth == 180);

                var actionButtons = dialog.GetLogicalDescendants()
                    .OfType<Button>()
                    .Where(button => button is not ToggleButton)
                    .ToArray();
                actionButtons.Where(button => button.Content?.ToString() is "OK" or "Cancel")
                    .Should().OnlyContain(button => button.MinWidth == 72);
                actionButtons.Where(button => button.Content?.ToString() is "Line Numbers…" or "Borders…")
                    .Should().OnlyContain(button => button.MinWidth == 110);
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Layout_tab_matches_Wpf_field_order_and_checkbox_grouping()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new PageSetupDialog(new PageSettings(), PageSetupDialogTab.Layout);
            try
            {
                var tabs = dialog.GetLogicalDescendants().OfType<TabControl>().Single();
                var layout = (StackPanel)tabs.Items.OfType<TabItem>().Single(item => item.Header?.ToString() == "Layout").Content!;

                layout.Children.OfType<Grid>()
                    .Select(grid => grid.Children.OfType<TextBlock>().Single().Text)
                    .Should().Equal(
                        "Section start:",
                        "Vertical alignment:",
                        "Header from edge (pt):",
                        "Footer from edge (pt):");

                var checks = layout.Children.OfType<StackPanel>().Single(panel =>
                    panel.Children.OfType<CheckBox>().Any());
                checks.Margin.Should().Be(new Thickness(0, 8, 0, 0));
                checks.Children.OfType<CheckBox>().Select(check => check.Content?.ToString())
                    .Should().Equal("Different first page", "Different odd and even");
                checks.Children.OfType<CheckBox>().Select(check => check.Margin)
                    .Should().Equal(new Thickness(0), new Thickness(0, 4, 0, 0));
                checks.Children.OfType<CheckBox>()
                    .Should().OnlyContain(check => check.Height == 18 && check.MinHeight == 18);
            }
            finally
            {
                dialog.Close();
            }
        }, CancellationToken.None);
    }
}
