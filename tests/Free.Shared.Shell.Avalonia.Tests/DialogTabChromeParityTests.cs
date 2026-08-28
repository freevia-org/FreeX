using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;

namespace Free.Shared.Shell.Avalonia.Tests;

public sealed class DialogTabChromeParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ShellHeadlessApp).Assembly);

    [Fact]
    public async Task Avalonia_tab_chrome_uses_the_shared_zero_gap_zero_separator_contract()
    {
        await Session.Dispatch(() =>
        {
            var tabs = new TabControl();
            tabs.Items.Add(new TabItem { Header = "One", Content = new TextBlock { Text = "Body" } });
            tabs.SelectedIndex = 0;

            AvaloniaCompactDialogChrome.ApplyClassicTabChrome(tabs);
            var window = new Window { Content = tabs };
            try
            {
                window.Show();
                tabs.ApplyTemplate();
                tabs.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                var selectedTab = tabs.Items.OfType<TabItem>().Single();
                selectedTab.Margin.Bottom.Should().Be(-DialogTabChromeMetrics.SelectedTabContentOverlap);
                selectedTab.BorderThickness.Bottom.Should().Be(0);

                var selectedContentHost = tabs.GetVisualDescendants()
                    .OfType<ContentPresenter>()
                    .Single(presenter => presenter.Name == "PART_SelectedContentHost");
                selectedContentHost.Margin.Should().Be(new Thickness(0));
                selectedContentHost.BorderThickness.Should().Be(
                    new Thickness(DialogTabChromeMetrics.PaneBorderThickness));
            }
            finally
            {
                window.Close();
            }
        }, CancellationToken.None);
    }

    [Fact]
    public async Task Shared_legal_notices_applies_the_WPF_selected_pane_trailing_edge_contract()
    {
        await Session.Dispatch(() =>
        {
            var presentation = new LegalNoticesDialogPresentation(
                "Legal Notices",
                [new LegalNoticeDocument("Legal Notices", "test.txt", "legal text")],
                "Summary",
                "Close",
                "Help",
                "Summary",
                "Sections",
                "Choose a section",
                "Read-only text");
            var dialog = new AvaloniaLegalNoticesDialog(presentation);
            try
            {
                dialog.Show();
                dialog.UpdateLayout();
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);

                var selectedPane = dialog.GetVisualDescendants()
                    .OfType<ContentPresenter>()
                    .Single(presenter => presenter.Name == "PART_SelectedContentHost");

                selectedPane.Margin.Should().Be(new Thickness(0, -5, 1, 0));
            }
            finally
            {
                if (dialog.IsVisible)
                    dialog.Close();
            }
        }, CancellationToken.None);
    }
}
