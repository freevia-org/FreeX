using System.Reflection;
using System.Threading;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class DialogTabCyclePreHandledTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public async Task PreHandledTab_MovesExactlyOneStopForwardAndReverse()
    {
        await Session.Dispatch(() =>
        {
            var first = new Button { Content = "First" };
            var second = new Button { Content = "Second" };
            var third = new Button { Content = "Third" };
            var root = new StackPanel
            {
                Orientation = Orientation.Vertical,
                Children = { first, second, third },
            };
            var dialog = new Window { Content = root };

            try
            {
                ConfigureDialogTabCycle(dialog, root);
                dialog.Show();
                dialog.UpdateLayout();

                first.Focus().Should().BeTrue();
                RaisePreHandledTab(first, KeyModifiers.None);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(second);

                third.Focus().Should().BeTrue();
                RaisePreHandledTab(third, KeyModifiers.Shift);
                Dispatcher.UIThread.RunJobs(DispatcherPriority.Input);
                dialog.FocusManager?.GetFocusedElement().Should().BeSameAs(second);
            }
            finally
            {
                dialog.Close();
            }

            return Task.CompletedTask;
        }, CancellationToken.None);
    }

    private static void ConfigureDialogTabCycle(Window dialog, Control root)
    {
        var method = typeof(MainWindow).GetMethod(
            "ConfigureDialogTabCycle",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing ConfigureDialogTabCycle helper.");
        method.Invoke(null, [dialog, root]);
    }

    private static void RaisePreHandledTab(Control source, KeyModifiers modifiers)
    {
        source.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Tab,
            PhysicalKey = PhysicalKey.Tab,
            KeyModifiers = modifiers,
            KeyDeviceType = KeyDeviceType.Keyboard,
            Source = source,
            Handled = true,
        });
    }
}
