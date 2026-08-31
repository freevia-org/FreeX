using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using System.Windows.Media;
using Free.Shared.Shell.Wpf;

namespace Free.Shared.Shell.Wpf.Tests;

public sealed class BackstageRailForegroundTests
{
    [Fact]
    public void SetAccent_RepaintsBackArrowForALightRail()
    {
        RunOnSta(() =>
        {
            var foreground = Color.FromRgb(0x24, 0x2A, 0x31);
            var frame = new BackstageFrame();

            frame.SetAccent(new BackstageAccent(
                Sidebar: Color.FromRgb(0xF3, 0xF4, 0xF6),
                Hover: Color.FromRgb(0xE5, 0xE9, 0xED),
                Selected: Color.FromRgb(0xD8, 0xE5, 0xE9),
                Separator: Color.FromRgb(0xD7, 0xDC, 0xE2),
                Foreground: foreground));

            var layout = (Grid)frame.Content;
            var rail = (Border)layout.Children[0];
            var railDock = (DockPanel)rail.Child;
            var backArrow = (TextBlock)((Button)railDock.Children[0]).Content;

            ((SolidColorBrush)backArrow.Foreground).Color.Should().Be(foreground);
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
