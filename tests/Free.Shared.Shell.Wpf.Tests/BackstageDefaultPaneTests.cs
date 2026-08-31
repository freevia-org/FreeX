using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Shell;
using Free.Shared.Shell.Wpf;

namespace Free.Shared.Shell.Wpf.Tests;

public sealed class BackstageDefaultPaneTests
{
    [Fact]
    public void Show_without_a_target_selects_the_first_declared_pane()
    {
        RunOnSta(() =>
        {
            var host = new UserControl();
            var shell = new BackstageViewShell(
                host,
                new BackstageAccent(
                    System.Windows.Media.Colors.Navy,
                    System.Windows.Media.Colors.Teal,
                    System.Windows.Media.Colors.DarkCyan,
                    System.Windows.Media.Colors.Gray),
                [
                    BackstageEntry.Pane("Home", BackstageIconKind.Grid, static () => new Border()),
                    BackstageEntry.Pane("Info", BackstageIconKind.Info, static () => new Border()),
                ],
                static () => { });

            shell.Show();

            shell.Frame.CurrentPaneLabel.Should().Be("Home");
            host.Visibility.Should().Be(Visibility.Visible);
        });
    }

    [Fact]
    public void Primary_rail_commands_remain_scrollable_when_the_available_height_is_constrained()
    {
        RunOnSta(() =>
        {
            var frame = new BackstageFrame();
            frame.SetEntries(
            [
                BackstageEntry.Pane("Home", BackstageIconKind.Grid, static () => new Border()),
                BackstageEntry.Command("New", BackstageIconKind.Insert, static () => { }),
                BackstageEntry.Command("Open", BackstageIconKind.GetData, static () => { }),
                BackstageEntry.Command("Share", BackstageIconKind.Share, static () => { }),
                BackstageEntry.Pane("Info", BackstageIconKind.Info, static () => new Border()),
                BackstageEntry.Command("Save", BackstageIconKind.Save, static () => { }),
                BackstageEntry.Command("Save As", BackstageIconKind.Save, static () => { }),
                BackstageEntry.Pane("Print", BackstageIconKind.Print, static () => new Border()),
                BackstageEntry.Command("Export", BackstageIconKind.Share, static () => { }),
                BackstageEntry.Command("Close", BackstageIconKind.WindowClose, static () => { }),
                BackstageEntry.Command("Options", BackstageIconKind.Info, static () => { }, dockBottom: true),
            ]);

            frame.Show();
            frame.Measure(new Size(220, 220));
            frame.Arrange(new Rect(0, 0, 220, 220));
            frame.UpdateLayout();

            var primaryRail = FindVisualDescendants<ScrollViewer>(frame).Single();
            primaryRail.HorizontalScrollBarVisibility.Should().Be(ScrollBarVisibility.Disabled);
            primaryRail.VerticalScrollBarVisibility.Should().Be(ScrollBarVisibility.Auto);
            primaryRail.ExtentHeight.Should().BeGreaterThan(
                primaryRail.ViewportHeight,
                "the File command rail must not clip commands in a short or high-DPI window");
        });
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
                yield return typed;

            foreach (var descendant in FindVisualDescendants<T>(child))
                yield return descendant;
        }
    }

    private static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
