using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Free.Shared.Shell.Avalonia;

namespace FreeW.App.Avalonia.Tests;

public sealed class LegalNoticesDialogVisualParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(FreeWHeadlessApp).Assembly);

    [Fact]
    public async Task Legal_notices_matches_WPF_metrics_for_all_tabs_and_focus_targets()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new LegalNoticesDialog(
            [
                ("Project License", "license text"),
                ("Legal Notices", "legal text"),
                ("Privacy Notice", "privacy text"),
                ("Third-Party Notices", "third-party notices"),
                ("Third-Party License Texts", "third-party license texts"),
            ]);

            dialog.Width.Should().Be(840);
            dialog.Height.Should().Be(620);
            dialog.MinWidth.Should().Be(620);
            dialog.MinHeight.Should().Be(420);
            dialog.FontFamily.Should().Be(AvaloniaCompactDialogChrome.WindowsUiFontFamily);
            dialog.FontSize.Should().Be(12);

            var tabs = dialog.GetLogicalDescendants().OfType<TabControl>().Single();
            tabs.Items.Count.Should().Be(5);
            tabs.SelectedIndex.Should().Be(0);
            tabs.Padding.Should().Be(new Thickness(0));
            tabs.HorizontalContentAlignment.Should().Be(global::Avalonia.Layout.HorizontalAlignment.Stretch);
            tabs.VerticalContentAlignment.Should().Be(global::Avalonia.Layout.VerticalAlignment.Stretch);

            var tabItems = tabs.Items.OfType<TabItem>().ToArray();
            tabItems.Select(tab => tab.Header?.ToString()).Should().Equal(
                "Project License",
                "Legal Notices",
                "Privacy Notice",
                "Third-Party Notices",
                "Third-Party License Texts");

            foreach (var tab in tabItems)
            {
                var text = tab.Content.Should().BeOfType<TextBox>().Subject;
                text.IsReadOnly.Should().BeTrue();
                text.Focusable.Should().BeTrue();
                text.AcceptsReturn.Should().BeTrue();
                text.AcceptsTab.Should().BeTrue();
                text.Padding.Should().Be(new Thickness(8));
                text.FontSize.Should().Be(12);
                text.FontFamily.Should().Be(new FontFamily("Consolas"));
                ((ISolidColorBrush)text.Foreground!).Color.Should().Be(Colors.Black);
                AutomationProperties.GetAutomationId(text).Should().StartWith("LegalNotices");
            }

            tabs.SelectedIndex = 4;
            tabs.SelectedIndex.Should().Be(4);
            dialog.Close();
        }, CancellationToken.None);
    }
}
