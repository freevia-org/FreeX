using System.Reflection;

using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

[Collection("AvaloniaHeadless")]
public sealed class CustomViewRestoreInteractionTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    [Fact]
    public Task ShowAndHistoryRestoreTheVisibleGridAtTheSavedZoomAndScrollOrigin() =>
        Session.Dispatch(async () =>
        {
            var options = new FreeXOptionsRuntimeSession(load: static () => new AppOptions(), save: static _ => true);
            var window = new MainWindow([], null!, options);
            try
            {
                window.Show();
                await DrainLayoutAsync();
                var session = window.Session;
                var activeCell = new CellAddress(session.ActiveSheet.Id, 1, 1);
                session.SelectCell(activeCell);
                session.SetZoomPercent(140).Success.Should().BeTrue();
                session.SetViewportOrigin(30, 9);
                session.ExecuteCustomViewCommand(new SaveCustomViewCommand("Panned view")).Success.Should().BeTrue();
                session.SetZoomPercent(80).Success.Should().BeTrue();
                session.SetViewportOrigin(55, 13);

                var open = typeof(MainWindow).GetMethod(
                    "OpenCustomViewsDialogAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var finished = (Task)open.Invoke(window, null)!;
                await DrainLayoutAsync();
                var dialog = window.OwnedWindows.Should().ContainSingle().Which;
                var show = dialog.GetVisualDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetAutomationId(button) == "CustomViewsShowButton");
                show.IsEnabled.Should().BeTrue();
                show.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await finished;
                await DrainLayoutAsync();

                AssertView(window, activeCell, zoom: 140, topRow: 30, leftCol: 9, visibleCellId: "Cell_I30");

                await PressHistoryShortcutAsync(window, Key.Z);
                AssertView(window, activeCell, zoom: 80, topRow: 55, leftCol: 13, visibleCellId: "Cell_M55");

                await PressHistoryShortcutAsync(window, Key.Y);
                AssertView(window, activeCell, zoom: 140, topRow: 30, leftCol: 9, visibleCellId: "Cell_I30");
            }
            finally
            {
                foreach (var dialog in window.OwnedWindows.ToArray())
                    dialog.Close();
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
            return true;
        }, CancellationToken.None);

    private static void AssertView(
        MainWindow window, CellAddress activeCell, int zoom, uint topRow, uint leftCol, string visibleCellId)
    {
        window.Session.ActiveCell.Should().Be(activeCell);
        window.Session.ZoomPercent.Should().Be(zoom);
        window.Session.ViewportOrigin.Should().Be((topRow, leftCol));
        window.GetVisualDescendants().OfType<Control>().Should()
            .Contain(control => AutomationProperties.GetAutomationId(control) == visibleCellId && control.IsVisible);
    }

    private static async Task PressHistoryShortcutAsync(MainWindow window, Key key)
    {
        var args = new KeyEventArgs { Key = key, KeyModifiers = KeyModifiers.Control };
        await window.RaiseKeyDownForTest(args);
        args.Handled.Should().BeTrue();
        await DrainLayoutAsync();
    }

    private static async Task DrainLayoutAsync()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Input);
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
    }
}
