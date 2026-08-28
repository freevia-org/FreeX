using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
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
